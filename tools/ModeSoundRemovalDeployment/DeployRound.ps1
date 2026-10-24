param(
    [ValidateSet('DryRun','Deploy')][string]$Mode = 'DryRun',
    [switch]$StartGuard
)
$ErrorActionPreference = 'Stop'
# Automatic, non-interactive deployment for task 20261024-mode-sound-removal.
# DryRun performs every check and a staging copy but never stops, replaces or
# starts the Receiver. Deploy is the separately authorized transition; it requires
# a freshly ready independent guard and rolls back by itself on any failure.
# -StartGuard registers and asynchronously starts that guard from this same
# script, then waits for its fresh ready record before anything is armed or
# stopped, so the protection window can never expire before the deployment.
# There is deliberately no prompt, dialog or Read-Host anywhere in this script.
$recoveryRoot = $PSScriptRoot
$round = Split-Path $recoveryRoot -Parent
$evidence = Join-Path $round 'evidence'
$plan = Get-Content -LiteralPath (Join-Path $recoveryRoot 'guard-plan.json') -Raw | ConvertFrom-Json
if ($plan.task -ne '20261024-mode-sound-removal') { throw 'Deployment plan task identity mismatch' }
$backupManifest = Get-Content -LiteralPath $plan.backupManifest -Raw | ConvertFrom-Json
$stopDeadline = 40
$startDeadline = 40
$soakSeconds = 60
$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$budget = [DateTime]::UtcNow.AddSeconds([int]$plan.deployTotalBudgetSeconds)

function Save([string]$name, $value) { $value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence $name) -Encoding UTF8 }
function Log([string]$message) { Add-Content -LiteralPath (Join-Path $evidence 'deploy.log') -Value ((Get-Date).ToString('o') + ' ' + $message) }
function HashOf([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function ReadJson([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }

# Same probe as the guard: the transition mutex must be creatable from this
# restricted interactive context before anything is armed or stopped.
function Test-Mutex([string]$name) {
    $result = [ordered]@{ name = $name; created = $false; acquired = $false; released = $false; heldByOther = $false; error = $null }
    $mutex = $null
    try {
        $mutex = New-Object System.Threading.Mutex($false, $name)
        $result.created = $true
        if ($mutex.WaitOne(0)) { $result.acquired = $true; $mutex.ReleaseMutex(); $result.released = $true }
        else { $result.heldByOther = $true }
    } catch { $result.error = $_.Exception.Message }
    finally { if ($mutex) { try { $mutex.Dispose() } catch { } } }
    $result.ok = [bool]($result.created -and (!$result.acquired -or $result.released))
    [pscustomobject]$result
}

# Register and start the guard task asynchronously, then wait for its fresh ready
# record. Any failure happens before arming/stopping, so a failed protection
# start leaves the running Receiver untouched.
function Start-GuardIfRequested {
    if (!$StartGuard) { return $null }
    $runner = Join-Path $recoveryRoot 'RunGuardTask.ps1'
    # A ready record left by a previous guard instance must never satisfy this
    # deployment: remove it and require the newly started instance to write one.
    $readyPath = Join-Path $evidence 'guard-ready.json'
    if (Test-Path -LiteralPath $readyPath) { Copy-Item -LiteralPath $readyPath -Destination (Join-Path $evidence ('ready-before-'+(Get-Date -Format yyyyMMddHHmmssfff)+'.json')) }
    Log 'registering the independent guard from the deployment script'
    & $powershellPath -NoProfile -ExecutionPolicy Bypass -File $runner -Mode Guard -DeadlineSeconds ([int]$plan.guardDeadlineSeconds) -RegisterOnly *> (Join-Path $evidence 'deploy-guard-register.txt')
    if ($LASTEXITCODE -ne 0) { throw "Guard registration failed ($LASTEXITCODE); no Receiver was stopped." }
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $task = $service.GetFolder('\').GetTask([string]$plan.guardTask)
    if ($task.State -eq 4) { throw 'Guard task is unexpectedly still running after registration.' }
    $startedAt = [DateTime]::UtcNow
    Save 'guard-start-request.json' ([ordered]@{task=$plan.task;at=$startedAt.ToString('o');nonce=[Guid]::NewGuid().ToString('N')})
    $null = $task.Run($null)
    Log 'guard task started asynchronously; waiting for a fresh ready record from this instance'
    $until = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 500
        if (!(Test-Path -LiteralPath $readyPath)) { continue }
        try { $ready = ReadJson $readyPath } catch { continue }
        $readyAt = [DateTime]::Parse([string]$ready.at).ToUniversalTime()
        if ($readyAt -lt $startedAt) { continue }
        $age = ([DateTimeOffset]::UtcNow - [DateTimeOffset]$readyAt).TotalSeconds
        $task = $service.GetFolder('\').GetTask([string]$plan.guardTask)
        if ($ready.passed -eq $true -and $age -ge -30 -and $age -le 120 -and $task.State -eq 4) {
            try {Assert-GuardProcess $ready | Out-Null} catch {continue}
            Log ('guard ready accepted from this instance; age=' + [math]::Round($age,1) + 's')
            return $ready
        }
    }
    throw 'Independent guard did not become ready in time; nothing was armed or stopped.'
}

function Assert-GuardReady {
    $path = Join-Path $evidence 'guard-ready.json'
    if (!(Test-Path -LiteralPath $path)) { throw 'Independent guard has no ready record.' }
    $ready = ReadJson $path
    if ($ready.passed -ne $true) { throw 'Independent guard ready record did not pass.' }
    $age = ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string]$ready.at)).TotalSeconds
    if ($age -gt 120 -or $age -lt -30) { throw "Independent guard ready record is not fresh ($([math]::Round($age,1)) s)." }
    if ($ready.mutexOk -ne $true) { throw 'Independent guard did not prove the transition mutex in its ready record.' }
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $task = $service.GetFolder('\').GetTask([string]$plan.guardTask)
    if ($task.State -ne 4) { throw 'Independent guard task is not running.' }
    if ([int]$ready.oldPid -ne [int]$plan.oldIdentity.pid) { throw 'Independent guard prepared a different instance.' }
    $ready
}

function Get-DesktopIdentity($process) {
    if (!('RightpadDevToken' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
public static class RightpadDevToken {
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr t, int c, IntPtr b, int n, out int needed);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int needed);
    public static string Integrity(IntPtr process) {
        IntPtr token;
        if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception();
        try {
            int n; GetTokenInformation(token, 25, IntPtr.Zero, 0, out n);
            IntPtr b = Marshal.AllocHGlobal(n);
            try {
                if (!GetTokenInformation(token, 25, b, n, out n)) throw new Win32Exception();
                return new SecurityIdentifier(Marshal.ReadIntPtr(b)).Value;
            } finally { Marshal.FreeHGlobal(b); }
        } finally { CloseHandle(token); }
    }
    public static string Desktop(uint thread) {
        int n; var b = new StringBuilder(512);
        if (!GetUserObjectInformation(GetThreadDesktop(thread), 2, b, 1024, out n)) throw new Win32Exception();
        return b.ToString();
    }
}
'@
    }
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
    $owner = Invoke-CimMethod -InputObject $cim -MethodName GetOwner
    if ($owner.ReturnValue -ne 0) { throw 'Cannot verify process owner.' }
    $explorers = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object {
        $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
        $o.ReturnValue -eq 0 -and "$($o.Domain)\$($o.User)" -eq $plan.user
    })
    [pscustomobject]@{
        User = "$($owner.Domain)\$($owner.User)"
        SessionId = $process.SessionId
        ExplorerSessionIds = @($explorers | ForEach-Object SessionId | Select-Object -Unique)
        IntegritySid = [RightpadDevToken]::Integrity($process.Handle)
        Desktop = [RightpadDevToken]::Desktop($process.Threads[0].Id)
    }
}

function FindReceiver {
    $all = @(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'" |
        Where-Object { $_.ExecutablePath -eq ($plan.releaseTarget.TrimEnd('\') + '\Rightpad.Receiver.exe') })
    if ($all.Count -gt 1) { throw 'Multiple formal Receiver processes.' }
    if ($all.Count -eq 0) { return $null }
    $owner = Invoke-CimMethod -InputObject $all[0] -MethodName GetOwner
    if ($owner.ReturnValue -ne 0) { throw 'Cannot verify Receiver owner.' }
    if (("$($owner.Domain)\$($owner.User)") -ne $plan.user -or $all[0].SessionId -ne $plan.session) {
        throw 'Receiver owner/session mismatch.'
    }
    $process = Get-Process -Id $all[0].ProcessId
    [pscustomobject]@{
        Pid = $process.Id
        CreatedUtc = $process.StartTime.ToUniversalTime().ToString('o')
        Path = $all[0].ExecutablePath
        SessionId = $process.SessionId
        CommandLine = $all[0].CommandLine
        MainWindowHandle = $process.MainWindowHandle
    }
}
function Get-PortOwners { @(Get-NetUDPEndpoint -LocalPort 50000 -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.OwningProcess }) }

function Assert-Backup {
    foreach ($entry in $backupManifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $plan.backup $entry.relative))
        if (!$path.StartsWith($plan.backup.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup path escapes the package.' }
        if (!(Test-Path -LiteralPath $path)) { throw "Missing backup file: $path" }
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$entry.bytes -or (HashOf $path) -ne $entry.sha256) { throw "Backup hash mismatch: $path" }
    }
    foreach ($name in @('Rightpad.Receiver.exe','Rightpad.Receiver.dll','Rightpad.Receiver.runtimeconfig.json',
        'Rightpad.Receiver.deps.json','Rightpad.VirtualHid.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $plan.backup ('release\' + $name)))) { throw "Incomplete release backup: $name" }
    }
}
function Assert-Candidate {
    foreach ($name in $plan.candidateFiles.PSObject.Properties.Name) {
        $entry = $plan.candidateFiles.$name
        $path = Join-Path $plan.candidate $name
        if (!(Test-Path -LiteralPath $path)) { throw "Missing candidate file: $name" }
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$entry.bytes -or (HashOf $path) -ne $entry.sha256) { throw "Candidate hash mismatch: $name" }
    }
    foreach ($name in @('Rightpad.Receiver.exe','Rightpad.Receiver.dll','Rightpad.Receiver.runtimeconfig.json',
        'Rightpad.Receiver.deps.json','Rightpad.VirtualHid.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $plan.candidate $name))) { throw "Incomplete candidate package: $name" }
    }
}
function Assert-FormalMatchesBackup {
    foreach ($entry in $backupManifest.files) {
        if (!$entry.relative.StartsWith('release\')) { continue }
        $name = $entry.relative.Substring(8)
        $path = Join-Path $plan.releaseTarget $name
        if (!(Test-Path -LiteralPath $path)) { throw "Formal release file missing: $name" }
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$entry.bytes -or (HashOf $path) -ne $entry.sha256) { throw "Formal release changed since backup: $name" }
    }
    if ((HashOf $plan.settingsTarget) -ne $plan.oldSettingsSha256) { throw 'Formal settings.json changed since backup.' }
    if ((HashOf $plan.launcher) -ne $plan.launcherSha256) { throw 'Launcher changed since the reviewed plan.' }
}
function Assert-FormalHashes([string]$dll,[string]$native,[string]$exe) {
    if ((HashOf (Join-Path $plan.releaseTarget 'Rightpad.Receiver.dll')) -ne $dll) { throw 'Deployed Receiver DLL hash mismatch.' }
    if ((HashOf (Join-Path $plan.releaseTarget 'Rightpad.VirtualHid.dll')) -ne $native) { throw 'Deployed native bridge hash mismatch.' }
    if ((HashOf (Join-Path $plan.releaseTarget 'Rightpad.Receiver.exe')) -ne $exe) { throw 'Deployed Receiver EXE hash mismatch.' }
}
function Assert-OldInstance([string]$why) {
    $live = FindReceiver
    if (!$live) { throw "Live Receiver absent during $why." }
    if ($live.Pid -ne [int]$plan.oldIdentity.pid -or
        $live.CreatedUtc -ne [DateTime]::Parse($plan.oldIdentity.startUtc).ToUniversalTime().ToString('o')) {
        throw "Receiver identity changed before $why (pid=$($live.Pid) created=$($live.CreatedUtc))."
    }
    $owners = Get-PortOwners
    if ($owners.Count -ne 1 -or $owners[0] -ne $live.Pid) { throw "UDP 50000 is not owned by the verified Receiver during $why." }
    Assert-ReceiverContext $live | Out-Null
    $live
}
function Assert-ReceiverContext($live) {
    $context = Get-DesktopIdentity (Get-Process -Id $live.Pid)
    if ($context.User -ne $plan.user -or $context.SessionId -ne $plan.session -or
        $context.SessionId -notin $context.ExplorerSessionIds -or
        $context.IntegritySid -ne $plan.integritySid -or $context.Desktop -ne $plan.desktop) {
        throw "Receiver context failed validation: $($context | ConvertTo-Json -Compress)"
    }
    $context
}
function LogDirectoryOf($live) {
    $match = [regex]::Match([string]$live.CommandLine, '--dev-log-dir\s+"([^"]+)"')
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}
function Assert-ReceiverLog([string]$directory,[bool]$requireMouseButtons) {
    if (!$directory) { return $false }
    $log = Join-Path $directory 'receiver.log'
    $until = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $until) {
        if (Test-Path -LiteralPath $log) {
            $text = Get-Content -LiteralPath $log -Raw
            $ok = $text -match 'application: mode=gui defaultPage=Motion' -and
                $text -match 'mouse_backend: name=libvirtualhid' -and
                $text -match 'mode=raw_mouse' -and
                $text -match 'listening: udp=0\.0\.0\.0:50000' -and
                $text -notmatch 'receiver_error:'
            if ($ok -and $requireMouseButtons) { $ok = $text -match 'mouse_buttons: .*support=left,right,middle' }
            if ($ok) { return $true }
        }
        Start-Sleep -Milliseconds 400
    }
    return $false
}
function Get-FlightSnapshot([int]$receiverPid) {
    $found = $null
    foreach ($path in @((Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.log'),
        (Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.previous.log'))) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        foreach ($line in @(Get-Content -LiteralPath $path -Tail 300 -ErrorAction SilentlyContinue)) {
            try { $record = $line | ConvertFrom-Json; if ($record.type -eq 'snapshot' -and [int]$record.receiverPid -eq $receiverPid) { $found = $record } } catch { }
        }
    }
    $found
}
function Stop-OldInstance($live) {
    Log ("stop requested pid=" + $live.Pid + " created=" + $live.CreatedUtc)
    try { & $plan.launcher -Mode Stop | Out-File -LiteralPath (Join-Path $evidence 'deploy-launcher-stop.txt') -Encoding UTF8 }
    catch { Log ('launcher Stop reported: ' + $_); Save 'deploy-stop-launcher-error.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); error = $_.Exception.Message }) }
    $until = (Get-Date).AddSeconds($stopDeadline)
    while ((Get-Date) -lt $until) {
        if (!(FindReceiver) -and (Get-PortOwners).Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    }
    if (FindReceiver) {
        $residual = FindReceiver
        $process = Get-Process -Id $residual.Pid
        if ($residual.Pid -ne $live.Pid -or $process.StartTime.ToUniversalTime().ToString('o') -ne $live.CreatedUtc) {
            throw 'Stop identity changed; no unrelated process was terminated.'
        }
        if ($process.MainWindowHandle -ne 0) { $process.CloseMainWindow() | Out-Null; $process.WaitForExit(3000) | Out-Null }
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit(6000) | Out-Null }
        Log 'verified residual terminated after the approved launcher stop; graceful shutdown not claimed'
    }
    $until = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $until) {
        if (!(FindReceiver) -and (Get-PortOwners).Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    }
    if (FindReceiver) { throw 'Receiver still present after deployment stop.' }
    if ((Get-PortOwners).Count) { throw 'UDP 50000 still occupied after deployment stop.' }
}
function Start-NewInstance([string]$why) {
    if (FindReceiver) { throw "Receiver already present before start ($why)." }
    if ((Get-PortOwners).Count) { throw "UDP 50000 occupied before start ($why)." }
    & $plan.launcher -Mode Start | Out-File -LiteralPath (Join-Path $evidence ('deploy-launcher-start-' + $why + '.txt')) -Encoding UTF8
    $until = (Get-Date).AddSeconds($startDeadline)
    while ((Get-Date) -lt $until) {
        $live = FindReceiver
        if ($live) {
            $owners = Get-PortOwners
            if ($owners.Count -eq 1 -and $owners[0] -eq $live.Pid) { return $live }
        }
        Start-Sleep -Milliseconds 400
    }
    throw "Receiver did not become ready after start ($why)."
}

function New-Staging([string]$prefix) {
    $stamp = Get-Date -Format yyyyMMddHHmmssfff
    $staging = Join-Path $plan.releaseParent ($prefix + $stamp)
    if (![IO.Path]::GetFullPath($staging).StartsWith($plan.releaseParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
    Copy-Item -LiteralPath $plan.candidate -Destination $staging -Recurse
    foreach ($name in $plan.candidateFiles.PSObject.Properties.Name) {
        $entry = $plan.candidateFiles.$name
        $path = Join-Path $staging $name
        if (!(Test-Path -LiteralPath $path) -or (HashOf $path) -ne $entry.sha256) { throw "Staged file mismatch: $name" }
    }
    $staging
}

function Invoke-Rollback([string]$reason) {
    Log "rollback started: $reason"
    $mutex = New-Object System.Threading.Mutex($false, [string]$plan.mutex)
    $held = $false
    try {
        $held = Acquire-TransitionMutex $mutex 45
        if (!$held) { throw 'Rollback could not acquire the transition lock.' }
        $current = FindReceiver
        Assert-Backup;Assert-Dependencies
        if ($current) {Stop-VerifiedReceiver $current}
        if ((Get-PortOwners).Count) { throw 'UDP 50000 occupied before rollback restore.' }
        $stamp = Get-Date -Format yyyyMMddHHmmssfff
        $staging = Join-Path $plan.releaseParent ('.rollback-staging-' + $stamp)
        $hold = Join-Path $plan.releaseParent ('.rollback-hold-' + $stamp)
        Copy-Item -LiteralPath (Join-Path $plan.backup 'release') -Destination $staging -Recurse
        if (Test-Path -LiteralPath $plan.releaseTarget) { Move-Item -LiteralPath $plan.releaseTarget -Destination $hold }
        Move-Item -LiteralPath $staging -Destination $plan.releaseTarget
        Copy-Item -LiteralPath (Join-Path $plan.backup 'settings.json') -Destination $plan.settingsTarget -Force
        foreach ($entry in $backupManifest.files) {
            if (!$entry.relative.StartsWith('release\')) { continue }
            $name = $entry.relative.Substring(8)
            if ((HashOf (Join-Path $plan.releaseTarget $name)) -ne $entry.sha256) { throw "Rollback file mismatch: $name" }
        }
        if ((HashOf $plan.settingsTarget) -ne $plan.oldSettingsSha256) { throw 'Rollback settings hash mismatch.' }
        $started = Start-NewInstance 'rollback'
        $context = Assert-ReceiverContext $started
        $logOk = Assert-ReceiverLog (LogDirectoryOf $started) $false
        $snapshot = Get-FlightSnapshot $started.Pid
        Save 'deployment-rollback.json' ([ordered]@{
            at = (Get-Date).ToUniversalTime().ToString('o'); reason = $reason
            pid = $started.Pid; createdUtc = $started.CreatedUtc; context = $context
            logValidated = $logOk; snapshotConnected = [bool]($snapshot -and $snapshot.connectionState -eq 'Connected')
            hold = $hold; note = 'Previous package restored and started through the approved launcher.'
        })
        Log 'rollback applied'
        return $true
    } catch {
        Log ('ROLLBACK ERROR ' + $_)
        Save 'deployment-rollback-error.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); reason = $reason; error = $_.Exception.Message })
        return $false
    } finally {
        if ($held) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Test-SnapshotStructure($snapshot) {
    # Structural health only; Android presence is recorded but never vetoes.
    if (!$snapshot) { return $false }
    if ($snapshot.runtimeState -ne 'Running') { return $false }
    if ($snapshot.lastError) { return $false }
    if ([int]$snapshot.mouseOutputFailures -ne 0) { return $false }
    try { $age = ([DateTimeOffset]::Now - [DateTimeOffset]::Parse([string]$snapshot.wallTime)).TotalSeconds } catch { return $false }
    return ($age -ge -5 -and $age -lt 20)
}

# 60 s soak after a successful start: identity, port, snapshot and log must stay
# structurally healthy for the whole window before the release is published.
function Invoke-Soak($started, [string]$logDirectory) {
    $until = (Get-Date).AddSeconds($soakSeconds)
    $checks = 0
    $log = Join-Path $logDirectory 'receiver.log'
    while ((Get-Date) -lt $until) {
        Start-Sleep -Seconds 3
        $live = FindReceiver
        if (!$live -or $live.Pid -ne $started.Pid -or $live.CreatedUtc -ne $started.CreatedUtc) { throw 'Soak failed: Receiver identity changed.' }
        $owners = Get-PortOwners
        if ($owners.Count -ne 1 -or $owners[0] -ne $live.Pid) { throw 'Soak failed: UDP 50000 ownership changed.' }
        if (!(Test-SnapshotStructure (Get-FlightSnapshot $live.Pid))) { throw 'Soak failed: snapshot is not structurally healthy.' }
        if (!(Test-Path -LiteralPath $log)) { throw 'Soak failed: receiver.log disappeared.' }
        if ((Get-Content -LiteralPath $log -Raw) -match 'receiver_error:') { throw 'Soak failed: receiver_error appeared in the new log.' }
        $checks++
    }
    $checks
}

function Invoke-Cleanup {
    $releasedPath = Join-Path $evidence 'guard-released.json'
    $until = (Get-Date).AddSeconds(120)
    $released = $false
    while ((Get-Date) -lt $until) {
        if (Test-Path -LiteralPath $releasedPath) { $released = $true; break }
        Start-Sleep -Seconds 2
    }
    if(!$released) {throw 'Guard release is not confirmed; protection retained'}
    $completion=ReadJson $releasedPath
    if(!$completion.healthy) {throw 'Guard did not confirm health; protection retained'}
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    $folder = $service.GetFolder('\')
    $guardTask = $null
    try { $guardTask = $folder.GetTask([string]$plan.guardTask) } catch { }
    $stateBefore = if ($guardTask) { $guardTask.State } else { $null }
    if ($guardTask) {
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            try { $guardTask = $folder.GetTask([string]$plan.guardTask) } catch { $guardTask = $null; break }
            if ($guardTask.State -ne 4) { break }
            Start-Sleep -Milliseconds 500
        }
        if ($guardTask -and $guardTask.State -eq 4) {throw 'Released guard has not exited; task retained'}
        try { $folder.DeleteTask([string]$plan.guardTask, 0) } catch { Log ('guard task removal reported: ' + $_) }
    }
    $removed = $true
    try { $null = $folder.GetTask([string]$plan.guardTask); $removed = $false } catch { }
    $names = @($folder.GetTasks(1) | ForEach-Object { $_.Name } | Where-Object { $_ -like '*Rightpad*' })
    $devTask = $null
    try { $devTask = $folder.GetTask('Rightpad Receiver Dev') } catch { }
    $receiver = FindReceiver
    $release = $null
    if (Test-Path -LiteralPath (Join-Path $evidence 'deployment-release.json')) {
        try { $release = ReadJson (Join-Path $evidence 'deployment-release.json') } catch { }
    }
    Save 'cleanup.json' ([ordered]@{
        at = (Get-Date).ToUniversalTime().ToString('o')
        guardReleased = $released
        guardTaskName = [string]$plan.guardTask
        guardTaskStateBeforeRemoval = $stateBefore
        guardTaskRemoved = $removed
        devTaskName = 'Rightpad Receiver Dev'
        devTaskExists = [bool]$devTask
        devTaskState = if ($devTask) { $devTask.State } else { $null }
        receiverRunning = [bool]$receiver
        receiver = $receiver
        remainingRightpadTasksIncludingHidden = $names
        backupRetained = $plan.backup
        holdRetained = if ($release) { $release.hold } else { $null }
        logDirectory = if ($release) { $release.log } else { $null }
        evidenceRetained = $evidence
        note = 'Guard task removed after its health release. Backup, hold, logs and evidence are retained. The new Receiver was not stopped.'
    })
    Log ('cleanup: released=' + $released + ' guardRemoved=' + $removed + ' receiverRunning=' + [bool]$receiver)
}

function Invoke-Deploy {
    $startedGuard = Start-GuardIfRequested
    # Prove the transition mutex before anything is armed or stopped, and before
    # this thread acquires it (re-entrant probe would be less informative).
    $mutexProbe = Test-Mutex ([string]$plan.mutex)
    Save 'deploy-mutex.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); probe = $mutexProbe; source = 'DeployRound before arming' })
    if (!$mutexProbe.ok) { throw "Transition mutex unavailable before arming: created=$($mutexProbe.created) error=$($mutexProbe.error)" }
    Log ('transition mutex verified before arming: ' + $mutexProbe.name + ' created=' + $mutexProbe.created)
    $mutex = New-Object System.Threading.Mutex($false, [string]$plan.mutex)
    $held = $false
    try {
        $held = Acquire-TransitionMutex $mutex 45
        if (!$held) { throw 'Deployment could not acquire the transition lock.' }
        $guard = Assert-GuardReady
        Assert-Backup
        Assert-Candidate
        Assert-CandidateFilesAt $plan.candidate
        Assert-Dependencies
        Assert-PreparedSettings
        Assert-FormalMatchesBackup
        $live = Assert-OldInstance 'deployment checks'
        $preSnapshot = Get-FlightSnapshot $live.Pid
        $preDeployConnected = [bool]($preSnapshot -and $preSnapshot.connectionState -eq 'Connected')
        $armedAt = [DateTime]::UtcNow
        Save 'deployment-armed.json' ([ordered]@{
            at = $armedAt.ToString('o')
            oldPid = $live.Pid
            oldCreatedUtc = $live.CreatedUtc
            preDeployConnected = $preDeployConnected
            preDeployConnectionState = if ($preSnapshot) { [string]$preSnapshot.connectionState } else { 'unknown' }
            order = 'guard ready, stop old, deploy complete package, start through the approved launcher, reconnect, soak, publish'
            guardPid = [int]$guard.context.pid
            deployPid = $PID
            deployCreatedUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')
            guardCreatedUtc = $guard.context.createdUtc
            guardNonce = $guard.nonce
            guardReadyAt = $guard.at
            guardMutexOk = $guard.mutexOk
            recoverAfterUtc = $armedAt.AddSeconds([int]$plan.deployRecoverAfterSeconds).ToString('o')
        })
        $transition = $false
        try {
            Assert-ArmedAcknowledgement $guard
            $transition = $true
            Stop-OldInstance $live
            Save 'deploy-old-stopped.json' ([ordered]@{
                at = (Get-Date).ToUniversalTime().ToString('o'); oldPid = $live.Pid; oldCreatedUtc = $live.CreatedUtc
                exited = $true; udp50000Released = $true
            })
            $staging = New-Staging '.deploy-staging-'
            $hold = Join-Path $plan.releaseParent ('.hold-mode-sound-removal-' + (Get-Date -Format yyyyMMddHHmmssfff))
            Move-Item -LiteralPath $plan.releaseTarget -Destination $hold
            Move-Item -LiteralPath $staging -Destination $plan.releaseTarget
            Assert-FormalHashes $plan.candidateReceiverDll $plan.candidateNativeDll $plan.candidateExe
            Assert-CandidateFilesAt $plan.releaseTarget
            Apply-PreparedSettings
            $started = Start-NewInstance 'deploy'
            $context = Assert-ReceiverContext $started
            $logDirectory = LogDirectoryOf $started
            $logOk = Assert-ReceiverLog $logDirectory $true
            if (!$logOk) { throw 'New Receiver startup log validation failed.' }
            # Natural reconnect is mandatory only when the phone was connected
            # before the stop; otherwise it is recorded as an observation.
            $reconnect = $false
            $until = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $until) {
                $snapshot = Get-FlightSnapshot $started.Pid
                if ($snapshot -and (Test-SnapshotStructure $snapshot) -and $snapshot.connectionState -eq 'Connected') { $reconnect = $true; break }
                Start-Sleep -Milliseconds 500
            }
            if ($preDeployConnected -and !$reconnect) { throw 'Android did not naturally reconnect to the new Receiver within 30 s.' }
            $soakChecks = Invoke-Soak $started $logDirectory
            $snapshot = Get-FlightSnapshot $started.Pid
            Save 'deployment-release.json' ([ordered]@{
                at = (Get-Date).ToUniversalTime().ToString('o')
                pid = $started.Pid
                createdUtc = $started.CreatedUtc
                exe = $started.Path
                context = $context
                receiverHash = HashOf (Join-Path $plan.releaseTarget 'Rightpad.Receiver.dll')
                nativeHash = HashOf (Join-Path $plan.releaseTarget 'Rightpad.VirtualHid.dll')
                exeHash = HashOf (Join-Path $plan.releaseTarget 'Rightpad.Receiver.exe')
                settingsHash = HashOf $plan.settingsTarget
                log = $logDirectory
                logValidated = $logOk
                preDeployConnected = $preDeployConnected
                androidNaturalReconnect = $reconnect
                reconnectRequired = $preDeployConnected
                soakSeconds = $soakSeconds
                soakChecks = $soakChecks
                snapshot = $snapshot
                hold = $hold
            })
            Log ('deployment release pid=' + $started.Pid + ' reconnect=' + $reconnect + ' preConnected=' + $preDeployConnected + ' soakChecks=' + $soakChecks)
            'Deployment applied. Independent guard must observe health separately.'
        } catch {
            $failure = $_.Exception.Message
            Log ('DEPLOY ERROR ' + $_)
            # Only a started transition needs a rollback; a pre-stop failure left
            # the verified old instance untouched and healthy.
            $rolledBack = $true
            if ($transition) { $rolledBack = Invoke-Rollback $failure }
            Save 'deployment-failed.json' ([ordered]@{
                at = (Get-Date).ToUniversalTime().ToString('o'); error = $failure; rolledBack = $rolledBack
                transitionStarted = $transition
            })
            throw
        }
    } finally {
        if ($held) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Invoke-DryRun {
    $checks = [ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); mode = 'DryRun'; startGuard = [bool]$StartGuard }
    $checks.plan = (Join-Path $recoveryRoot 'guard-plan.json')
    $checks.guardTask = [string]$plan.guardTask
    $startedGuard = Start-GuardIfRequested
    if ($startedGuard) { $checks.guardStartedByScript = $true }
    $guard = Assert-GuardReady
    $checks.guardReadyAt = $guard.at
    $checks.guardContext = $guard.context
    $checks.guardMutexOk = $guard.mutexOk
    $checks.guardMutex = $guard.mutex
    $localMutex = Test-Mutex ([string]$plan.mutex)
    $checks.localMutex = $localMutex
    if (!$localMutex.ok) { throw "Transition mutex unavailable in the deployment context: created=$($localMutex.created) error=$($localMutex.error)" }
    Assert-Backup
    $checks.backupFiles = @($backupManifest.files).Count
    Assert-Candidate
    Assert-CandidateFilesAt $plan.candidate
    Assert-PreparedSettings
    $checks.candidateFiles = @($plan.candidateFiles.PSObject.Properties).Count
    Assert-FormalMatchesBackup
    $checks.formalMatchesBackup = $true
    $live = Assert-OldInstance 'dry run'
    $checks.live = $live
    $preSnapshot = Get-FlightSnapshot $live.Pid
    $checks.preDeployConnectionState = if ($preSnapshot) { [string]$preSnapshot.connectionState } else { 'unknown' }
    $checks.preDeployConnected = [bool]($preSnapshot -and $preSnapshot.connectionState -eq 'Connected')
    $status = & $plan.launcher -Mode Status
    $launcherView = @($status | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['ReceiverPid'] })
    if ($launcherView.Count -ne 1) {throw 'Launcher status unavailable'}
    if([int]$launcherView[0].ReceiverPid -ne $live.Pid -and $plan.oldLaunchKind -ne 'desktop') {throw 'Launcher status identity mismatch'}
    $checks.oldLaunchKind=$plan.oldLaunchKind
    $portView = @($launcherView[0].Udp50000 | Where-Object { [int]$_.OwningProcess -ne $live.Pid })
    if ($portView.Count) { throw 'Launcher Status reports an unexpected UDP 50000 owner.' }
    $checks.launcherStatusPid = [int]$launcherView[0].ReceiverPid
    $checks.launcherStatusBackend = [string]$launcherView[0].MouseBackend
    # Prove the candidate can be staged and moved next to the target on this volume.
    $staging = New-Staging '.dryrun-staging-'
    $checks.staging = $staging
    $checks.stagingVerified = $true
    $writable = Join-Path $plan.releaseParent ('.dryrun-writable-' + (Get-Date -Format yyyyMMddHHmmssfff))
    Set-Content -LiteralPath $writable -Value 'rightpad deployment writability probe' -Encoding ASCII
    $checks.releaseParentWritable = Test-Path -LiteralPath $writable
    Remove-Item -LiteralPath $writable -Force
    Remove-Item -LiteralPath $staging -Recurse -Force
    $checks.stagingRemoved = !(Test-Path -LiteralPath $staging)
    $checks.stopped = $false
    $checks.replaced = $false
    $checks.started = $false
    $checks.runningReceiver = (FindReceiver)
    $checks.portOwners = Get-PortOwners
    if ($StartGuard) {
        # The dry run is not armed; stop the demonstration guard so the deployment
        # phase can register a fresh one. Nothing outside the guard task is touched.
        $service = New-Object -ComObject Schedule.Service
        $service.Connect()
        $folder = $service.GetFolder('\')
        try {
            $task = $folder.GetTask([string]$plan.guardTask)
            if ($task.State -eq 4) { $task.Stop(0); Start-Sleep -Seconds 2 }
            $checks.guardStoppedAfterDryRun = $true
        } catch { $checks.guardStoppedAfterDryRun = $false }
        Log 'dry run complete; demonstration guard stopped (it was never armed)'
    }
    $checks.passed = $true
    Save 'deploy-dry-run.json' $checks
    $checks | ConvertTo-Json -Depth 8
}

. (Join-Path $recoveryRoot 'RoundSafety.ps1')
try {
    if ($Mode -eq 'DryRun') { Invoke-DryRun | Out-Null; 'PASS deployment dry run (no stop, no replace, no start)'; exit 0 }
    Invoke-Deploy | Out-Null
    Invoke-Cleanup
    'PASS deployment applied and guard released; cleanup recorded'
    exit 0
} catch {
    Save ($Mode.ToLowerInvariant() + '-error.json') ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); mode = $Mode; error = $_.Exception.Message })
    "FAIL $($Mode): $($_.Exception.Message)"
    exit 1
}
