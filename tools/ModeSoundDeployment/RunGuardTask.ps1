param(
    [ValidateSet('Preflight','Decide','Guard')][string]$Mode = 'Preflight',
    [int]$DeadlineSeconds = 0,
    [switch]$RegisterOnly
)
$ErrorActionPreference = 'Stop'
# Registers/updates and runs the independent recovery guard task for this round.
# The task runs as the current interactive user (Limited) with no triggers; it is
# independent of any agent terminal or process tree.
$recoveryRoot = $PSScriptRoot
$round = Split-Path $recoveryRoot -Parent
$evidence = Join-Path $round 'evidence'
$guard = Join-Path $recoveryRoot 'ProtectionGuard.ps1'
$plan = Get-Content -LiteralPath (Join-Path $recoveryRoot 'guard-plan.json') -Raw | ConvertFrom-Json
$taskName = [string]$plan.guardTask
$description = 'rightpad Mode sound independent recovery guard 20261024 (task 20261024-mode-sound-deploy).'
$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -Mode {1}' -f $guard, $Mode
if ($DeadlineSeconds -gt 0) { $arguments += ' -DeadlineSeconds ' + $DeadlineSeconds }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$userSid = $identity.User.Value
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$folder = $service.GetFolder('\')

function Get-GuardTask {
    try { $folder.GetTask($taskName) }
    catch { if ($_.Exception.HResult -ne -2147024894) { throw } }
}
function Assert-OwnTask($task) {
    $definition = $task.Definition
    $principalSid = ([Security.Principal.NTAccount]$definition.Principal.UserId).Translate([Security.Principal.SecurityIdentifier]).Value
    if ($definition.RegistrationInfo.Description -ne $description -or $principalSid -ne $userSid -or
        $definition.Actions.Count -ne 1 -or $definition.Actions.Item(1).Path -ne $powershellPath) {
        throw "Task '$taskName' is not this round's guard; refusing to replace or run it."
    }
}
function Register-GuardTask {
    $existing = Get-GuardTask
    if ($existing) {
        Assert-OwnTask $existing
        if ($existing.State -eq 4) { throw 'Guard task is running; stop it before re-registering.' }
    }
    $definition = $service.NewTask(0)
    $definition.RegistrationInfo.Description = $description
    $definition.Principal.UserId = $identity.Name
    $definition.Principal.LogonType = 3
    $definition.Principal.RunLevel = 0
    $definition.Settings.ExecutionTimeLimit = 'PT1H'
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.RunOnlyIfIdle = $false
    $definition.Settings.RunOnlyIfNetworkAvailable = $false
    $definition.Settings.Enabled = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.RestartCount = 0
    $definition.Settings.MultipleInstances = 2
    $definition.Settings.Hidden = $true
    $action = $definition.Actions.Create(0)
    $action.Path = $powershellPath
    $action.Arguments = $arguments
    $action.WorkingDirectory = $recoveryRoot
    $registration = if ($existing) { 4 } else { 2 }
    $folder.RegisterTaskDefinition($taskName, $definition, $registration, $identity.Name, $null, 3)
    Log-TaskRun ([ordered]@{ action = 'register'; mode = $Mode; deadlineSeconds = $DeadlineSeconds; arguments = $arguments; at = (Get-Date).ToUniversalTime().ToString('o') })
}
function Log-TaskRun($record) {
    $path = Join-Path $evidence 'guard-task-runs.json'
    $records = @()
    if (Test-Path -LiteralPath $path) { $records = @(Get-Content -LiteralPath $path -Raw | ConvertFrom-Json) }
    $records += $record
    $records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
}
function Invoke-GuardTask {
    $task = Get-GuardTask
    if (!$task) { throw 'Guard task is not registered.' }
    Assert-OwnTask $task
    if ($task.State -eq 4) { throw 'Guard task is already running.' }
    $startedAt = Get-Date
    $null = $task.Run($null)
    $deadline = (Get-Date).AddSeconds(240)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $current = (Get-GuardTask).State
        if ($current -ne 4) { break }
    }
    $after = Get-GuardTask
    $result = [ordered]@{
        mode = $Mode; deadlineSeconds = $DeadlineSeconds; startedAt = $startedAt.ToUniversalTime().ToString('o');
        state = $after.State; lastTaskResult = $after.LastTaskResult;
        elapsedSeconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
    }
    Log-TaskRun $result
    $result | ConvertTo-Json
    if ($after.LastTaskResult -ne 0) { exit 1 }
}

Register-GuardTask
if (-not $RegisterOnly) { Invoke-GuardTask }
