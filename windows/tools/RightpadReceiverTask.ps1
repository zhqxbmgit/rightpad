param(
    [ValidateSet('Ensure', 'Start', 'Stop', 'Status', 'Run')]
    [string]$Mode = 'Status'
)

$ErrorActionPreference = 'Stop'
$taskName = 'Rightpad Receiver Dev'
$taskDescription = 'rightpad independent interactive development Receiver (project launcher).'
$windowsRoot = Split-Path $PSScriptRoot -Parent
$receiverPath = Join-Path $windowsRoot 'Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe'
$logRoot = Join-Path $windowsRoot 'test-results\receiver-runtime'
$currentPath = Join-Path $logRoot 'current.json'
$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$taskArguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -Mode Run' -f $PSCommandPath
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$userSid = $identity.User.Value
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')

function Get-DevTask {
    try { $folder.GetTask($taskName) }
    catch {
        if ($_.Exception.HResult -ne -2147024894) { throw }
    }
}

function Assert-OurTask($task) {
    $d = $task.Definition
    $principalSid = ([Security.Principal.NTAccount]$d.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value
    if ($d.RegistrationInfo.Description -ne $taskDescription -or
        $principalSid -ne $userSid -or $d.Actions.Count -ne 1 -or
        $d.Actions.Item(1).Path -ne $powershellPath -or
        $d.Actions.Item(1).Arguments -ne $taskArguments) {
        throw "Task '$taskName' is not this user's project launcher; refusing to replace or stop it."
    }
}

function Ensure-Task {
    $task = Get-DevTask
    if ($task) {
        Assert-OurTask $task
        $d = $task.Definition
        if ($d.Principal.LogonType -eq 3 -and $d.Principal.RunLevel -eq 0 -and
            $d.Triggers.Count -eq 0 -and $d.Settings.ExecutionTimeLimit -eq 'PT0S' -and
            !$d.Settings.DisallowStartIfOnBatteries -and !$d.Settings.StopIfGoingOnBatteries -and
            !$d.Settings.RunOnlyIfIdle -and !$d.Settings.RunOnlyIfNetworkAvailable -and
            $d.Settings.Enabled -and $d.Settings.AllowDemandStart -and $d.Settings.RestartCount -eq 0 -and
            $d.Settings.MultipleInstances -eq 2) { return $task }
        if ($task.State -eq 4) { throw 'Stop the dev task before correcting its definition.' }
    }
    $d = $service.NewTask(0)
    $d.RegistrationInfo.Description = $taskDescription
    $d.Principal.UserId = $identity.Name
    $d.Principal.LogonType = 3 # InteractiveToken: no stored credentials.
    $d.Principal.RunLevel = 0 # LUA / Limited, never elevated.
    $d.Settings.ExecutionTimeLimit = 'PT0S'
    $d.Settings.DisallowStartIfOnBatteries = $false
    $d.Settings.StopIfGoingOnBatteries = $false
    $d.Settings.MultipleInstances = 2 # Ignore a duplicate Start request.
    $d.Settings.RestartCount = 0
    $d.Settings.Hidden = $true
    $action = $d.Actions.Create(0)
    $action.Path = $powershellPath
    $action.Arguments = $taskArguments
    $action.WorkingDirectory = $windowsRoot
    $registration = if ($task) { 4 } else { 2 } # Update only a verified task.
    $folder.RegisterTaskDefinition($taskName, $d, $registration, $identity.Name, $null, 3)
}

function Read-Run {
    if (Test-Path -LiteralPath $currentPath) { Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json }
}

function Get-OwnedReceiver($run) {
    if (!$run) { return }
    $wrapper = Get-Process -Id $run.WrapperPid -ErrorAction SilentlyContinue
    if ($wrapper -and $wrapper.StartTime.ToUniversalTime() -eq ([DateTime]$run.WrapperStartUtc).ToUniversalTime()) {
        $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($run.WrapperPid)" |
            Where-Object { $_.ExecutablePath -eq $receiverPath })
        if ($children.Count -gt 1) { throw 'More than one Receiver belongs to this run.' }
        if ($children.Count -eq 1) { return Get-Process -Id $children[0].ProcessId }
    }
    # A saved creation time prevents a recycled PID from becoming a stop target.
    $pidPath = Join-Path $run.Directory 'receiver.json'
    if (Test-Path -LiteralPath $pidPath) {
        $saved = Get-Content -LiteralPath $pidPath -Raw | ConvertFrom-Json
        $p = Get-Process -Id $saved.Pid -ErrorAction SilentlyContinue
        if ($p -and $p.Path -eq $receiverPath -and
            $p.StartTime.ToUniversalTime() -eq ([DateTime]$saved.StartUtc).ToUniversalTime()) { return $p }
    }
}

function Get-Port { @(Get-NetUDPEndpoint -ErrorAction Stop | Where-Object LocalPort -eq 50000) }

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
    if ($owner.ReturnValue -ne 0) { throw 'Cannot verify Receiver user.' }
    $explorers = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object {
        $o = Invoke-CimMethod -InputObject $_ -MethodName GetOwner
        $o.ReturnValue -eq 0 -and "$($o.Domain)\$($o.User)" -eq $identity.Name
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

function Show-Status {
    $task = Get-DevTask
    if ($task) { Assert-OurTask $task }
    $run = Read-Run
    $p = Get-OwnedReceiver $run
    [pscustomobject]@{
        TaskName = $taskName; TaskExists = [bool]$task
        TaskState = if ($task) { $task.State } else { $null } # 3 Ready, 4 Running
        LastTaskResult = if ($task) { $task.LastTaskResult } else { $null }
        ReceiverPid = if ($p) { $p.Id } else { $null }
        ExecutablePath = if ($p) { $p.Path } else { $receiverPath }
        Identity = if ($p) { Get-DesktopIdentity $p } else { $null }
        Udp50000 = @(Get-Port | Select-Object LocalAddress, LocalPort, OwningProcess)
        Stdout = if ($run) { Join-Path $run.Directory 'stdout.log' } else { $null }
        Stderr = if ($run) { Join-Path $run.Directory 'stderr.log' } else { $null }
    }
}

function Stop-Runtime {
    $task = Get-DevTask
    if (!$task) { throw 'Dev task does not exist; no processes were stopped.' }
    Assert-OurTask $task
    $run = Read-Run
    $p = Get-OwnedReceiver $run
    if ($p) {
        $null = $p.Handle # Retain the exit code even after the process disappears.
        @{ Pid = $p.Id; StartUtc = $p.StartTime.ToUniversalTime().ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.Directory 'receiver.json') -Encoding UTF8
    }
    $task.Stop(0)
    if ($p) {
        if (!$p.WaitForExit(5000)) {
            $remaining = Get-OwnedReceiver $run
            if (!$remaining -or $remaining.Id -ne $p.Id) { throw 'Runtime identity changed during Stop.' }
            $p.Kill() # Only the verified residual of this specific task run.
            if (!$p.WaitForExit(5000)) { throw 'Receiver did not exit.' }
        }
        @{ ExitUtc = [DateTime]::UtcNow.ToString('o'); ExitCode = $p.ExitCode;
           Pid = $p.Id; Reason = 'Explicit task Stop (forced; normal shutdown logs are not guaranteed)' } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.Directory 'stop.json') -Encoding UTF8
    }
    if (Get-OwnedReceiver $run) { throw 'Receiver remains after Stop.' }
    if (@(Get-Port).Count) { throw 'UDP 50000 is still occupied; unrelated processes were not stopped.' }
    if ((Get-DevTask).State -eq 4) { throw 'Task is still running after Stop.' }
}

switch ($Mode) {
    'Ensure' { $task = Ensure-Task; Show-Status }
    'Status' { Show-Status }
    'Stop' { Stop-Runtime; Show-Status }
    'Run' {
        # Internal scheduler entry only: never create a persistent Codex child.
        $parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
        $scheduler = Get-CimInstance Win32_Service -Filter "Name='Schedule'"
        if ($parent -ne $scheduler.ProcessId) { throw 'Run is internal to Task Scheduler; use Start.' }
        $task = Get-DevTask
        Assert-OurTask $task
        $context = Get-DesktopIdentity (Get-Process -Id $PID)
        if ($context.User -ne $identity.Name -or $context.SessionId -eq 0 -or
            $context.SessionId -notin $context.ExplorerSessionIds -or
            $context.IntegritySid -ne 'S-1-16-8192' -or $context.Desktop -ne 'Default') {
            throw 'Scheduled launcher is not in the required interactive desktop context.'
        }
        $jobFlags = [RightpadDevToken]::CurrentJobFlags()
        if ($jobFlags -band 0x2000) { throw 'Scheduled launcher inherited KILL_ON_JOB_CLOSE; refusing persistent launch.' }
        if (!(Test-Path -LiteralPath $receiverPath)) { throw "Missing Release binary: $receiverPath" }
        $runDirectory = Join-Path $logRoot ('{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), $PID)
        $null = New-Item -ItemType Directory -Path $runDirectory
        $run = @{ Directory = $runDirectory; StartUtc = [DateTime]::UtcNow.ToString('o');
            WrapperPid = $PID; WrapperStartUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');
            SchedulerPid = $parent; Context = $context; ImmediateJobLimitFlags = $jobFlags }
        $run | ConvertTo-Json | Set-Content -LiteralPath $currentPath -Encoding UTF8
        $run | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'start.json') -Encoding UTF8
        $exitCode = 1
        try {
            # Do not interpret native stderr as a PowerShell terminating error.
            $ErrorActionPreference = 'Continue'
            $global:LASTEXITCODE = 1 # Preserve failure if native process creation itself fails.
            & $receiverPath --raw-mouse 1> (Join-Path $runDirectory 'stdout.log') 2> (Join-Path $runDirectory 'stderr.log')
            $exitCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = 'Stop'
            @{ ExitUtc = [DateTime]::UtcNow.ToString('o'); ExitCode = $exitCode } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'exit.json') -Encoding UTF8
        }
        exit $exitCode
    }
    'Start' {
        if (!(Test-Path -LiteralPath $receiverPath)) { throw "Build Release first: $receiverPath" }
        $task = Ensure-Task
        if ($task.State -eq 4 -or (Get-OwnedReceiver (Read-Run))) { Stop-Runtime }
        if (@(Get-Port).Count) { throw 'UDP 50000 is occupied; no unrelated process will be stopped.' }
        $null = $task.Run($null)
        $deadline = (Get-Date).AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 500
            $run = Read-Run
            $p = Get-OwnedReceiver $run
            if ($p -and @(Get-Port | Where-Object OwningProcess -eq $p.Id).Count) { break }
        } while ((Get-Date) -lt $deadline)
        if (!$p -or !@(Get-Port | Where-Object OwningProcess -eq $p.Id).Count) {
            throw "Receiver did not become ready. Inspect $logRoot and task result; no fallback launch was attempted."
        }
        @{ Pid = $p.Id; StartUtc = $p.StartTime.ToUniversalTime().ToString('o') } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.Directory 'receiver.json') -Encoding UTF8
        $info = Get-DesktopIdentity $p
        if ($p.Path -ne $receiverPath -or $info.User -ne $identity.Name -or $info.SessionId -eq 0 -or
            $info.SessionId -notin $info.ExplorerSessionIds -or $info.IntegritySid -ne 'S-1-16-8192' -or
            $info.Desktop -ne 'Default') { throw "Receiver context failed validation: $($info | ConvertTo-Json -Compress)" }
        $stdout = Get-Content -LiteralPath (Join-Path $run.Directory 'stdout.log') -Raw
        if ($stdout -notmatch 'mode=raw_mouse' -or $stdout -notmatch 'listening: udp=0.0.0.0:50000' -or
            (Get-Item -LiteralPath (Join-Path $run.Directory 'stderr.log')).Length -gt 0) {
            throw 'Receiver startup logs failed validation; inspect runtime logs.'
        }
        Show-Status
    }
}
