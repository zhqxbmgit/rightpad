# Round-specific additions to the reviewed existing protection mechanism.
# Loaded after the legacy function definitions so the stronger checks apply.
. (Join-Path $recoveryRoot 'SettingsTransition.ps1')
if([IO.Path]::GetFullPath($plan.releaseTarget) -ne 'C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows' -or
    [IO.Path]::GetFullPath($plan.releaseParent) -ne 'C:\rightpad\windows\Rightpad.Receiver\bin\Release') {throw 'Unexpected formal deployment directory'}
function Acquire-TransitionMutex($mutex,[int]$seconds) {
    try {$mutex.WaitOne([TimeSpan]::FromSeconds($seconds))}
    catch [Threading.AbandonedMutexException] {return $true} # ownership is granted
}
function Acquire-RecoveryMutex($mutex) {
    if(Acquire-TransitionMutex $mutex 45) {return $true}
    $armed=ReadJson (Join-Path $evidence 'deployment-armed.json')
    if([DateTime]::UtcNow -lt [DateTime]::Parse($armed.recoverAfterUtc).ToUniversalTime()) {return $false}
    $deployer=Get-Process -Id ([int]$armed.deployPid) -ErrorAction SilentlyContinue
    if($deployer) {
        $cim=Get-CimInstance Win32_Process -Filter "ProcessId=$($deployer.Id)"
        if($deployer.StartTime.ToUniversalTime().ToString('o') -ne $armed.deployCreatedUtc -or
            !$cim.CommandLine.Contains('"'+(Join-Path $recoveryRoot 'DeployRound.ps1')+'"')) {throw 'Expired deployer identity mismatch; no unrelated process stopped'}
        $deployer.Kill();$null=$deployer.WaitForExit(10000)
        Save 'guard-expired-deployer-stopped.json' ([ordered]@{pid=$deployer.Id;createdUtc=$armed.deployCreatedUtc;at=[DateTime]::UtcNow.ToString('o');reason='Expired transition held mutex beyond 45 s recovery grace'})
    }
    Acquire-TransitionMutex $mutex 10
}
function Save([string]$name,$value) {
    $path=Join-Path $evidence $name
    $temporary=$path+'.'+$PID+'.tmp'
    $value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $path -Force
}
function Test-Mutex([string]$name) {
    $m=$null;$acquired=$false
    try {
        $m=New-Object System.Threading.Mutex($false,$name)
        $acquired=Acquire-TransitionMutex $m 0
        if(!$acquired) {throw 'Transition mutex is held by another operation'}
        $m.ReleaseMutex();$acquired=$false
        [pscustomobject]@{name=$name;created=$true;acquired=$true;released=$true;ok=$true}
    } finally {if($acquired) {$m.ReleaseMutex()};if($m) {$m.Dispose()}}
}
function Assert-Dependencies {
    foreach($entry in $backupManifest.externalFiles) {
        if((HashOf $entry.path) -ne $entry.sha256) {throw "Recovery dependency changed: $($entry.path)"}
    }
}
function Assert-CandidateFilesAt([string]$directory) {
    $expected=@($plan.candidateFiles.PSObject.Properties.Name | Sort-Object)
    $actual=@(Get-ChildItem -LiteralPath $directory -Recurse -File | ForEach-Object {$_.FullName.Substring($directory.TrimEnd('\').Length+1)} | Sort-Object)
    if(($expected -join '|') -ne ($actual -join '|')) {throw 'Candidate package file set differs'}
    foreach($name in $expected) {
        $entry=$plan.candidateFiles.$name;$path=Join-Path $directory $name
        if((Get-Item -LiteralPath $path).Length -ne $entry.bytes -or (HashOf $path) -ne $entry.sha256) {throw "Package mismatch: $name"}
    }
}
function Assert-GuardProcess($ready) {
    $request=ReadJson (Join-Path $evidence 'guard-start-request.json')
    if($ready.nonce -ne $request.nonce -or $request.task -ne $plan.task) {throw 'Guard nonce is not the current launch request'}
    $readyAt=[DateTimeOffset]::Parse([string]$ready.at)
    $requested=[DateTimeOffset]::Parse([string]$request.at)
    if($readyAt -lt $requested -or ([DateTimeOffset]::UtcNow-$readyAt).TotalSeconds -gt 120) {throw 'Guard ready is stale or predates this launch'}
    $guardPid=[int]$ready.context.pid
    $process=Get-Process -Id $guardPid -ErrorAction Stop
    if($process.StartTime.ToUniversalTime().ToString('o') -ne $ready.context.createdUtc -or
        [DateTimeOffset]$process.StartTime -lt $requested) {throw 'Guard live process creation identity mismatch'}
    $cim=Get-CimInstance Win32_Process -Filter "ProcessId=$guardPid"
    $expected=Join-Path $recoveryRoot 'ProtectionGuard.ps1'
    if(!$cim.CommandLine.Contains('"'+$expected+'"') -or $cim.CommandLine -notmatch '-Mode Guard') {throw 'Live guard command is not this round'}
    $context=Get-DesktopIdentity $process
    if($context.User -ne $plan.user -or $context.SessionId -ne $plan.session -or $context.IntegritySid -ne $plan.integritySid -or $context.Desktop -ne $plan.desktop) {throw 'Live guard context mismatch'}
    $service=New-Object -ComObject Schedule.Service;$service.Connect();$task=$service.GetFolder('\').GetTask([string]$plan.guardTask)
    if($task.State -ne 4 -or !$ready.passed -or !$ready.mutexOk) {throw 'Guard is not currently running and ready'}
    $ready
}
function Assert-GuardReady {
    $ready=ReadJson (Join-Path $evidence 'guard-ready.json')
    if([int]$ready.oldPid -ne $plan.oldIdentity.pid -or $ready.oldCreatedUtc -ne $plan.oldIdentity.startUtc) {throw 'Guard old Receiver identity mismatch'}
    Assert-GuardProcess $ready
}
function Stop-VerifiedReceiver($live) {
    $current=FindReceiver
    if(!$current) {return}
    if($current.Pid -ne $live.Pid -or $current.CreatedUtc -ne $live.CreatedUtc -or $current.Path -ne $live.Path) {throw 'Receiver changed before stop'}
    Assert-ReceiverContext $current | Out-Null
    if($current.Pid -ne [int]$plan.oldIdentity.pid -or $current.CreatedUtc -ne $plan.oldIdentity.startUtc) {
        # New instances must be the independent launcher's exact current process.
        $record=ReadJson (Join-Path $plan.runtimeRoot 'current.json')
        $identity=ReadJson (Join-Path $record.Directory 'receiver.json')
        if([int]$identity.Pid -ne $current.Pid -or [DateTime]::Parse([string]$identity.StartUtc).ToUniversalTime().ToString('o') -ne $current.CreatedUtc) {throw 'Refusing to stop an unowned replacement'}
    }
    $process=Get-Process -Id $current.Pid
    $null=$process.Handle
    $graceful=$false
    if($process.MainWindowHandle -ne 0) {
        $null=$process.CloseMainWindow();$graceful=$process.WaitForExit(3000)
    }
    # WPF's normal window close hides to tray. Try it first, then only this exact
    # verified process may be terminated; no stale task record is used to stop it.
    if(!$graceful) {
        $again=FindReceiver
        if($again -and ($again.Pid -ne $current.Pid -or $again.CreatedUtc -ne $current.CreatedUtc)) {throw 'Stop identity changed'}
        if(!$process.HasExited) {$process.Kill();if(!$process.WaitForExit(6000)){throw 'Verified Receiver did not exit'}}
    }
    Log "verified stop pid=$($current.Pid) created=$($current.CreatedUtc) graceful=$graceful"
}
function Stop-OldInstance($live) {
    Assert-Dependencies
    Stop-VerifiedReceiver $live
    $until=[DateTime]::UtcNow.AddSeconds(10)
    do {if(!(FindReceiver) -and (Get-PortOwners).Count -eq 0) {return};Start-Sleep -Milliseconds 200} while([DateTime]::UtcNow -lt $until)
    throw 'Process or UDP 50000 remains after exact stop'
}
function Assert-ArmedAcknowledgement($guard) {
    $path=Join-Path $evidence 'guard-armed.json';$until=[DateTime]::UtcNow.AddSeconds(15)
    while([DateTime]::UtcNow -lt $until) {
        if(Test-Path -LiteralPath $path) {
            $ack=ReadJson $path
            if($ack.nonce -eq $guard.nonce -and [int]$ack.pid -eq $guard.context.pid -and $ack.createdUtc -eq $guard.context.createdUtc) {
                Assert-GuardProcess $guard | Out-Null;return
            }
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Current independent guard did not acknowledge armed; Receiver remains running'
}
