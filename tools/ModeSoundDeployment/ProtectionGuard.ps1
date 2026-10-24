param(
    [ValidateSet('Preflight','Decide','Guard','Recover')][string]$Mode = 'Preflight',
    [int]$DeadlineSeconds = 0
)
$ErrorActionPreference = 'Stop'
# Independent recovery protection for task 20261024-mode-sound-deploy.
# Runs only under its own Task Scheduler job; it never starts a Receiver outside
# the approved launcher and never touches an instance it did not verify.
$recoveryRoot = $PSScriptRoot
$round = Split-Path $recoveryRoot -Parent
$evidence = Join-Path $round 'evidence'
$plan = Get-Content -LiteralPath (Join-Path $recoveryRoot 'guard-plan.json') -Raw | ConvertFrom-Json
if ($plan.task -ne '20261024-mode-sound-deploy') { throw 'Guard plan task identity mismatch' }
if ($DeadlineSeconds -le 0) { $DeadlineSeconds = [int]$plan.guardDeadlineSeconds }
$backupManifest = Get-Content -LiteralPath $plan.backupManifest -Raw | ConvertFrom-Json

function Save([string]$name, $value) {
    $value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence $name) -Encoding UTF8
}
function Log([string]$message) {
    Add-Content -LiteralPath (Join-Path $evidence 'guard.log') -Value ((Get-Date).ToString('o') + ' ' + $message)
}
function HashOf([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function ReadJson([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }

# The recovery/deployment transition mutex must be creatable from this exact
# restricted interactive token. A creation failure would make every recovery
# attempt throw at its first line, so it is a hard preflight requirement.
function Test-Mutex([string]$name) {
    $result = [ordered]@{ name = $name; created = $false; acquired = $false; released = $false; heldByOther = $false; error = $null }
    $mutex = $null
    try {
        $mutex = New-Object System.Threading.Mutex($false, $name)
        $result.created = $true
        if ($mutex.WaitOne(0)) {
            $result.acquired = $true
            $mutex.ReleaseMutex()
            $result.released = $true
        } else {
            $result.heldByOther = $true
        }
    } catch {
        $result.error = $_.Exception.Message
    } finally {
        if ($mutex) { try { $mutex.Dispose() } catch { } }
    }
    $result.ok = [bool]($result.created -and (!$result.acquired -or $result.released))
    [pscustomobject]$result
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
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr j, int c, IntPtr b, int n, out int needed);
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
    public static int CurrentJobFlags() {
        int n; int size = IntPtr.Size == 8 ? 144 : 112;
        IntPtr b = Marshal.AllocHGlobal(size);
        try {
            if (!QueryInformationJobObject(IntPtr.Zero, 9, b, size, out n)) throw new Win32Exception();
            return Marshal.ReadInt32(b, 16);
        } finally { Marshal.FreeHGlobal(b); }
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
        ParentPid = $cim.ParentProcessId
    }
}

function Get-OwnContext {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $self = Get-Process -Id $PID
    $context = Get-DesktopIdentity $self
    $cim = Get-CimInstance Win32_Process -Filter "ProcessId=$PID"
    $scheduler = Get-CimInstance Win32_Service -Filter "Name='Schedule'"
    $result = [ordered]@{
        pid = $PID
        createdUtc = $self.StartTime.ToUniversalTime().ToString('o')
        parent = [int]$cim.ParentProcessId
        scheduler = [int]$scheduler.ProcessId
        user = $context.User
        session = $context.SessionId
        explorerSessions = @($context.ExplorerSessionIds)
        integrity = $context.IntegritySid
        desktop = $context.Desktop
        jobFlags = [RightpadDevToken]::CurrentJobFlags()
        at = (Get-Date).ToUniversalTime().ToString('o')
    }
    if ($result.parent -ne $result.scheduler) {
        # Task Scheduler may interleave an intermediate host; accept only a direct
        # scheduler parent so this proof stays independent of any agent terminal.
        throw "Guard parent is not the Task Scheduler service (parent=$($result.parent) scheduler=$($result.scheduler))."
    }
    if ($result.user -ne $plan.user -or $result.session -ne $plan.session -or
        $result.integrity -ne $plan.integritySid -or $result.desktop -ne $plan.desktop) {
        throw "Guard context mismatch: user=$($result.user) session=$($result.session) integrity=$($result.integrity) desktop=$($result.desktop)."
    }
    if ($result.jobFlags -band 0x2000) { throw 'Guard inherited KILL_ON_JOB_CLOSE.' }
    $result
}

function FindReceiver {
    $all = @(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'" |
        Where-Object { $_.ExecutablePath -eq $plan.releaseTarget.TrimEnd('\') + '\Rightpad.Receiver.exe' })
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

function Get-PortOwners {
    @(Get-NetUDPEndpoint -LocalPort 50000 -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.OwningProcess })
}

function Assert-OldIdentity([string]$why) {
    $live = FindReceiver
    if (!$live) { throw "Live Receiver absent during $why." }
    if ($live.Pid -ne [int]$plan.oldIdentity.pid -or
        $live.CreatedUtc -ne [DateTime]::Parse($plan.oldIdentity.startUtc).ToUniversalTime().ToString('o')) {
        throw "Receiver identity changed before $why (pid=$($live.Pid) created=$($live.CreatedUtc))."
    }
    $owners = Get-PortOwners
    if ($owners.Count -ne 1 -or $owners[0] -ne $live.Pid) { throw "UDP 50000 is not owned by the verified Receiver during $why." }
    $live
}

function Assert-Backup {
    foreach ($entry in $backupManifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $plan.backup $entry.relative))
        if (!$path.StartsWith($plan.backup.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup path escapes the package.' }
        if (!(Test-Path -LiteralPath $path)) { throw "Missing backup file: $path" }
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$entry.bytes -or (HashOf $path) -ne $entry.sha256) {
            throw "Backup hash mismatch: $path"
        }
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
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$entry.bytes -or (HashOf $path) -ne $entry.sha256) {
            throw "Candidate hash mismatch: $name"
        }
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
        if ((Get-Item -LiteralPath $path).Length -ne [int64]$entry.bytes -or (HashOf $path) -ne $entry.sha256) {
            throw "Formal release changed since backup: $name"
        }
    }
    if ((HashOf $plan.settingsTarget) -ne $plan.oldSettingsSha256) { throw 'Formal settings.json changed since backup.' }
    if ((HashOf $plan.launcher) -ne $plan.launcherSha256) { throw 'Launcher changed since the reviewed plan.' }
}

function Assert-FormalHashes([string]$releaseDll,[string]$nativeDll,[string]$exe) {
    if ((HashOf (Join-Path $plan.releaseTarget 'Rightpad.Receiver.dll')) -ne $releaseDll) { throw 'Deployed Receiver DLL hash mismatch.' }
    if ((HashOf (Join-Path $plan.releaseTarget 'Rightpad.VirtualHid.dll')) -ne $nativeDll) { throw 'Deployed native bridge hash mismatch.' }
    if ((HashOf (Join-Path $plan.releaseTarget 'Rightpad.Receiver.exe')) -ne $exe) { throw 'Deployed Receiver EXE hash mismatch.' }
}

function Start-Receiver([string]$why) {
    if (FindReceiver) { throw "A Receiver is already running before start ($why)." }
    if ((Get-PortOwners).Count) { throw "UDP 50000 is occupied before start ($why)." }
    Log "launcher start requested: $why"
    & $plan.launcher -Mode Start | Out-File -LiteralPath (Join-Path $evidence ('launcher-start-' + $why + '.txt')) -Encoding UTF8
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $live = FindReceiver
        if ($live) {
            $owners = Get-PortOwners
            if ($owners.Count -eq 1 -and $owners[0] -eq $live.Pid) { return $live }
        }
        Start-Sleep -Milliseconds 400
    }
    throw "Receiver did not become ready after start ($why)."
}

function Assert-ReceiverContext($live) {
    $context = Get-DesktopIdentity (Get-Process -Id $live.Pid)
    if ($context.User -ne $plan.user -or $context.SessionId -ne $plan.session -or
        $context.SessionId -notin $context.ExplorerSessionIds -or
        $context.IntegritySid -ne $plan.integritySid -or $context.Desktop -ne $plan.desktop) {
        throw "Receiver context failed validation: $($context | ConvertTo-Json -Compress)"
    }
    if ($context.SessionId -eq 0) { throw 'Receiver is not in an interactive session.' }
    $context
}

function LogDirectoryOf($live) {
    $match = [regex]::Match([string]$live.CommandLine, '--dev-log-dir\s+"([^"]+)"')
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

function Assert-ReceiverLog([string]$directory, [bool]$requireMouseButtons) {
    if (!$directory) { return $false }
    $log = Join-Path $directory 'receiver.log'
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
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
    $paths = @(
        (Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.log'),
        (Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.previous.log')
    )
    $found = $null
    foreach ($path in $paths) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        $lines = @(Get-Content -LiteralPath $path -Tail 300 -ErrorAction SilentlyContinue)
        foreach ($line in $lines) {
            try {
                $record = $line | ConvertFrom-Json
                if ($record.type -eq 'snapshot' -and [int]$record.receiverPid -eq $receiverPid) { $found = $record }
            } catch { }
        }
    }
    $found
}

function Test-SnapshotStructure($snapshot) {
    # Structural health only. Android presence is recorded but never vetoes:
    # a temporarily offline phone must not turn a good deployment into a rollback.
    if (!$snapshot) { return $false }
    if ($snapshot.runtimeState -ne 'Running') { return $false }
    if ($snapshot.lastError) { return $false }
    if ([int]$snapshot.mouseOutputFailures -ne 0) { return $false }
    try { $age = ([DateTimeOffset]::Now - [DateTimeOffset]::Parse([string]$snapshot.wallTime)).TotalSeconds } catch { return $false }
    return ($age -ge -5 -and $age -lt 20)
}

# Full structural health of a published deployment: unique instance identity,
# formal hashes, port ownership, receiver context, startup log and snapshot.
function Test-PublishedHealth($release) {
    try {
        Assert-CandidateFilesAt $plan.releaseTarget
        Assert-FormalHashes $plan.candidateReceiverDll $plan.candidateNativeDll $plan.candidateExe
        $live = FindReceiver
        if (!$live) { return $null }
        if ($live.Pid -ne [int]$release.pid -or
            $live.CreatedUtc -ne [DateTime]::Parse([string]$release.createdUtc).ToUniversalTime().ToString('o')) { return $null }
        $owners = Get-PortOwners
        if ($owners.Count -ne 1 -or $owners[0] -ne $live.Pid) { return $null }
        Assert-ReceiverContext $live | Out-Null
        if (!(Assert-ReceiverLog (LogDirectoryOf $live) $true)) { return $null }
        $snapshot = Get-FlightSnapshot $live.Pid
        if (!(Test-SnapshotStructure $snapshot)) { return $null }
        [pscustomobject]@{
            healthy = $true; pid = $live.Pid; createdUtc = $live.CreatedUtc
            connectionState = [string]$snapshot.connectionState
            snapshotWallTime = [string]$snapshot.wallTime
        }
    } catch {
        Log ('health pending: ' + $_)
        $null
    }
}

function Test-RestoredHealth {
    try { Assert-FormalMatchesBackup } catch { return $null }
    $live = FindReceiver
    if (!$live) { return $null }
    $owners = Get-PortOwners
    if ($owners.Count -ne 1 -or $owners[0] -ne $live.Pid) { return $null }
    try { Assert-ReceiverContext $live | Out-Null } catch { return $null }
    $directory=LogDirectoryOf $live
    if ($directory -and !(Assert-ReceiverLog $directory $false)) { return $null }
    $snapshot = Get-FlightSnapshot $live.Pid
    if (!(Test-SnapshotStructure $snapshot)) { return $null }
    [pscustomobject]@{
        healthy = $true; pid = $live.Pid; createdUtc = $live.CreatedUtc
        connectionState = [string]$snapshot.connectionState
        snapshotWallTime = [string]$snapshot.wallTime
    }
}

function Decision([bool]$armed, [bool]$published, [bool]$healthy, [bool]$expired) {
    if (!$armed) { return 'idle' }
    if ($published -and $healthy) { return 'release' }
    if ($expired) { return 'recover' }
    return 'wait'
}

function Test-DecisionTable {
    $cases = @(
        @($false, $false, $false, $false, 'idle'),
        @($true, $false, $false, $false, 'wait'),
        @($true, $true, $true, $false, 'release'),
        @($true, $true, $false, $false, 'wait'),
        @($true, $true, $false, $true, 'recover'),
        @($true, $false, $false, $true, 'recover')
    )
    foreach ($case in $cases) {
        $actual = Decision $case[0] $case[1] $case[2] $case[3]
        if ($actual -ne $case[4]) { throw "Decision rehearsal failed: $($case -join ',') => $actual" }
    }
    $cases.Count
}

function Invoke-Preflight {
    $context = Get-OwnContext
    $mutex = Test-Mutex ([string]$plan.mutex)
    if (!$mutex.ok) {
        throw ("Recovery mutex unavailable in this restricted task context: name=" + $mutex.name + " created=" + $mutex.created +
            " acquired=" + $mutex.acquired + " released=" + $mutex.released + " error=" + $mutex.error)
    }
    Assert-Backup
    Assert-Candidate
    Assert-FormalMatchesBackup
    $live = Assert-OldIdentity 'preflight'
    Assert-ReceiverContext $live | Out-Null
    $logDirectory = LogDirectoryOf $live
    & (Join-Path $plan.backup 'Recovery.ps1') -Mode Preflight | Out-Null
    $decisionCases = Test-DecisionTable
    $check = [ordered]@{
        context = $context
        mutex = $mutex
        mutexOk = $true
        mutexNamespace = if ($mutex.name.StartsWith('Global\')) { 'Global (machine session namespace)' } else { 'Local (current logon session namespace)' }
        passed = $true
        backup = $plan.backup
        backupFiles = @($backupManifest.files).Count
        candidate = $plan.candidate
        candidateFiles = @($plan.candidateFiles.PSObject.Properties).Count
        recoveryPreflight = 'PASS offline recovery paths and hashes'
        live = $live
        liveLog = $logDirectory
        settingsSha256 = (HashOf $plan.settingsTarget)
        formalMatchesBackup = $true
        decisionCases = $decisionCases
        mutations = $false
        at = (Get-Date).ToUniversalTime().ToString('o')
    }
    Save 'independent-preflight.json' $check
    $check
}

function Invoke-Recover([string]$reason) {
    Log "recovery started: $reason"
    $issues = New-Object 'System.Collections.Generic.List[string]'
    $mutex = New-Object System.Threading.Mutex($false, [string]$plan.mutex)
    $held = $false
    try {
        Assert-Backup;Assert-Dependencies
        $held = Acquire-RecoveryMutex $mutex
        if (!$held) { throw 'Recovery lock not acquired; another transition is still running.' }
        else {
            $current = FindReceiver
            if ($current) {Stop-VerifiedReceiver $current}
            $deadline = (Get-Date).AddSeconds(20)
            while ((Get-Date) -lt $deadline) {
                if (!(FindReceiver) -and (Get-PortOwners).Count -eq 0) { break }
                Start-Sleep -Milliseconds 300
            }
            if (FindReceiver) { throw 'Receiver still present after recovery stop.' }
            if ((Get-PortOwners).Count) { throw 'UDP 50000 still occupied after recovery stop.' }
            $stamp = Get-Date -Format yyyyMMddHHmmssfff
            $staging = Join-Path $plan.releaseParent ('.restore-staging-' + $stamp)
            $hold = Join-Path $plan.releaseParent ('.restore-hold-' + $stamp)
            foreach ($path in @($staging, $hold)) {
                if (![IO.Path]::GetFullPath($path).StartsWith($plan.releaseParent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Unsafe recovery staging path.'
                }
            }
            Copy-Item -LiteralPath (Join-Path $plan.backup 'release') -Destination $staging -Recurse
            foreach ($entry in $backupManifest.files) {
                if (!$entry.relative.StartsWith('release\')) { continue }
                $name = $entry.relative.Substring(8)
                $staged = Join-Path $staging $name
                if (!(Test-Path -LiteralPath $staged) -or (HashOf $staged) -ne $entry.sha256) { throw "Staged recovery file mismatch: $name" }
            }
            if (Test-Path -LiteralPath $plan.releaseTarget) { Move-Item -LiteralPath $plan.releaseTarget -Destination $hold }
            Move-Item -LiteralPath $staging -Destination $plan.releaseTarget
            Copy-Item -LiteralPath (Join-Path $plan.backup 'settings.json') -Destination $plan.settingsTarget -Force
            foreach ($entry in $backupManifest.files) {
                if (!$entry.relative.StartsWith('release\')) { continue }
                $name = $entry.relative.Substring(8)
                if ((HashOf (Join-Path $plan.releaseTarget $name)) -ne $entry.sha256) { throw "Restored file mismatch: $name" }
            }
            if ((HashOf $plan.settingsTarget) -ne $plan.oldSettingsSha256) { throw 'Restored settings hash mismatch.' }
            $started = $null
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    $started = Start-Receiver ('recovery-' + $attempt)
                    break
                } catch {
                    $issues.Add("Recovery start attempt $($attempt): $($_.Exception.Message)")
                    Log ("recovery start attempt " + $attempt + " failed: " + $_)
                    $leftover = FindReceiver
                    if ($leftover) {
                        try { & $plan.launcher -Mode Stop | Out-File -LiteralPath (Join-Path $evidence ('recovery-stop-' + $attempt + '.txt')) -Encoding UTF8 } catch { }
                    }
                    Start-Sleep -Seconds 2
                }
            }
            if (!$started) { throw 'Receiver did not start after recovery.' }
            $context = Assert-ReceiverContext $started
            $logDirectory = LogDirectoryOf $started
            # Recovery always restores the backed-up (pre-round) package; it does
            # not carry the new mouse_buttons startup line.
            $logOk = Assert-ReceiverLog $logDirectory $false
            if (!$logOk) { $issues.Add('Restored Receiver log validation incomplete.') }
            $snapshot = Get-FlightSnapshot $started.Pid
            Save 'recovery-applied.json' ([ordered]@{
                at = (Get-Date).ToUniversalTime().ToString('o')
                reason = $reason
                pid = $started.Pid
                createdUtc = $started.CreatedUtc
                exe = $started.Path
                context = $context
                log = $logDirectory
                logValidated = $logOk
                snapshotRunning = [bool]($snapshot -and $snapshot.runtimeState -eq 'Running')
                snapshotConnected = [bool]($snapshot -and $snapshot.connectionState -eq 'Connected')
                hold = $hold
                issues = @($issues.ToArray())
                note = 'Backup package restored and started through the approved launcher. Android presence is recorded, not a health veto.'
            })
            Log 'recovery applied'
        }
    } catch {
        Log ('RECOVERY ERROR ' + $_)
        Save 'recovery-error.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); reason = $reason; error = $_.Exception.Message; issues = @($issues.ToArray()) })
        throw
    } finally {
        if ($held) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Invoke-Guard {
    $preflight = Invoke-Preflight
    $request=ReadJson (Join-Path $evidence 'guard-start-request.json')
    if($request.task -ne $plan.task -or [DateTime]::Parse($preflight.context.createdUtc).ToUniversalTime() -lt [DateTime]::Parse($request.at).ToUniversalTime()) {throw 'Guard launch request identity mismatch'}
    $armedPath = Join-Path $evidence 'deployment-armed.json'
    $releasePath = Join-Path $evidence 'deployment-release.json'
    $failedPath = Join-Path $evidence 'deployment-failed.json'
    $ready = [ordered]@{
        nonce = $request.nonce
        context = $preflight.context
        mutex = $preflight.mutex
        mutexOk = $preflight.mutexOk
        mutexNamespace = $preflight.mutexNamespace
        passed = $true
        at = (Get-Date).ToUniversalTime().ToString('o')
        deadlineSeconds = $DeadlineSeconds
        backup = $plan.backup
        candidate = $plan.candidate
        oldPid = [int]$plan.oldIdentity.pid
        oldCreatedUtc = $plan.oldIdentity.startUtc
        mode = 'guard'
    }
    Save 'guard-ready.json' $ready
    Log 'guard ready; independent preflight passed'
    $until = (Get-Date).AddSeconds($DeadlineSeconds)
    while ((Get-Date) -lt $until) {
        Save 'guard-heartbeat.json' ([ordered]@{nonce=$request.nonce;pid=$PID;createdUtc=$preflight.context.createdUtc;at=[DateTime]::UtcNow.ToString('o')})
        $armed = $null
        if (Test-Path -LiteralPath $armedPath) {
            try { $armed = ReadJson $armedPath } catch { $armed = $null }
        }
        if ($armed) {
            if($armed.guardNonce -ne $ready.nonce -or [int]$armed.guardPid -ne $PID -or $armed.guardCreatedUtc -ne $preflight.context.createdUtc -or
                [DateTime]::Parse($armed.at).ToUniversalTime() -lt [DateTime]::Parse($ready.at).ToUniversalTime()) {throw 'Armed does not reference this live ready guard'}
            if(!(Test-Path -LiteralPath (Join-Path $evidence 'guard-armed.json'))) {
                Save 'guard-armed.json' ([ordered]@{nonce=$ready.nonce;pid=$PID;createdUtc=$preflight.context.createdUtc;readyAt=$ready.at;armedAt=$armed.at;at=[DateTime]::UtcNow.ToString('o')})
            }
            if ([int]$armed.oldPid -ne [int]$plan.oldIdentity.pid -or
                [DateTime]::Parse([string]$armed.oldCreatedUtc).ToUniversalTime().ToString('o') -ne [DateTime]::Parse($plan.oldIdentity.startUtc).ToUniversalTime().ToString('o')) {
                Save 'guard-refused.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); armed = $armed; reason = 'Armed identity is not this round''s verified instance.' })
                return
            }
            $published = Test-Path -LiteralPath $releasePath
            # A deployment that failed and already restored and verified the old
            # package is terminal; never stop a healthy rollback result again.
            if (Test-Path -LiteralPath $failedPath) {
                $failure = $null
                try { $failure = ReadJson $failedPath } catch { }
                $restored = $null
                if ($failure -and $failure.rolledBack -eq $true) { $restored = Test-RestoredHealth }
                if ($restored) {
                    Save 'guard-released.json' ([ordered]@{
                        at = (Get-Date).ToUniversalTime().ToString('o'); healthy = $true; mode = 'deployment-failed-but-restored'
                        connectionState = $restored.connectionState; pid = $restored.pid; createdUtc = $restored.createdUtc
                        context = $preflight.context; actualRollback = $false
                    })
                    Log 'deployment failed but the verified old package is healthy; guard releases'
                    return
                }
                Invoke-Recover 'deployment failed and the restored instance is not healthy'
                return
            }
            $healthy = $null
            if ($published) {
                try {
                    $release = ReadJson $releasePath
                    $healthy = Test-PublishedHealth $release
                } catch { $healthy = $null; Log ('health pending: ' + $_) }
            }
            $expired = $false
            if ($armed.recoverAfterUtc) { $expired = (Get-Date).ToUniversalTime() -ge [DateTime]::Parse([string]$armed.recoverAfterUtc).ToUniversalTime() }
            $decision = Decision $true $published ($null -ne $healthy) $expired
            if ($decision -eq 'release') {
                Save 'guard-released.json' ([ordered]@{
                    at = (Get-Date).ToUniversalTime().ToString('o'); healthy = $true
                    connectionState = $healthy.connectionState; pid = $healthy.pid; createdUtc = $healthy.createdUtc
                    context = $preflight.context; actualRollback = $false
                })
                Log ('independent health release; connectionState=' + $healthy.connectionState)
                return
            }
            if ($decision -eq 'recover') {
                Invoke-Recover ($(if ($published) { 'published instance not healthy before the deadline' } else { 'deployment did not publish before the deadline' }))
                return
            }
        } elseif (!(Test-Path -LiteralPath (Join-Path $evidence 'guard-waiting.json'))) {
            Save 'guard-waiting.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); armed = $false })
        }
        Start-Sleep -Seconds 2
    }
    # Never exit "idle" once a deployment was armed: that window has no other
    # protection, so an unresolved armed state must restore the old package.
    $armedAtEnd = $null
    if (Test-Path -LiteralPath $armedPath) {
        try { $armedAtEnd = ReadJson $armedPath } catch { }
    }
    if ($armedAtEnd) {
        Log 'guard deadline reached while armed; recovering instead of idling'
        Invoke-Recover 'guard deadline reached while armed with no health release'
        return
    }
    Save 'guard-idle.json' ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); armed = $false; reason = 'Guard deadline reached without an armed deployment record.'; mutations = $false })
    Log 'guard idle timeout; no deployment was armed'
}

. (Join-Path $recoveryRoot 'RoundSafety.ps1')
try {
    switch ($Mode) {
        'Preflight' { $result = Invoke-Preflight; $result | ConvertTo-Json -Depth 8; exit 0 }
        'Decide' {
            $context = Get-OwnContext
            Assert-Backup
            Assert-Candidate
            $cases = Test-DecisionTable
            Save 'independent-decision.json' ([ordered]@{
                context = $context
                cases = $cases
                rules = 'idle when not armed; release only when published and healthy; recover only after the armed deadline; otherwise wait'
                mutations = $false
                passed = $true
                at = (Get-Date).ToUniversalTime().ToString('o')
            })
            'PASS decision rehearsal'
            exit 0
        }
        'Guard' { Invoke-Guard; exit 0 }
        'Recover' { Invoke-Recover 'explicit recovery request'; exit 0 }
    }
} catch {
    Log ('ERROR ' + $_)
    Save ($Mode.ToLowerInvariant() + '-error.json') ([ordered]@{ at = (Get-Date).ToUniversalTime().ToString('o'); error = $_.Exception.Message; mode = $Mode; pid = $PID })
    exit 1
}
