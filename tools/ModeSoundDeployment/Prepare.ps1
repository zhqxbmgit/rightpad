param([string]$Task='20261024-mode-sound-deploy')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\ArtifactPaths.ps1')
$root=Get-RightpadArtifactRoot
$round=Join-Path $root "test-results\$Task"
$evidence=Join-Path $round 'evidence';$recovery=Join-Path $round 'recovery'
$formal=Join-Path $repo 'windows\Rightpad.Receiver\bin\Release\net8.0-windows'
$settings=Join-Path $env:LOCALAPPDATA 'rightpad\settings.json'
$candidate=Join-Path $round 'candidate\Release'
$launcher=Join-Path $repo 'windows\tools\RightpadReceiverTask.ps1'
$all=@(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'")
if($all.Count -ne 1 -or $all[0].ExecutablePath -ne (Join-Path $formal 'Rightpad.Receiver.exe')) {throw 'Expected one current formal Receiver'}
$process=Get-Process -Id $all[0].ProcessId
$owner=Invoke-CimMethod -InputObject $all[0] -MethodName GetOwner
if($owner.ReturnValue -ne 0){throw 'Process owner unavailable'}
$backup=Get-ChildItem -LiteralPath $recovery -Directory -Filter 'backup-*' | Sort-Object Name -Descending | Select-Object -First 1
if(!$backup){throw 'Create current backup through Invoke-Development first'}
$files=[ordered]@{}
foreach($file in Get-ChildItem -LiteralPath $candidate -File -Recurse) {
    $files[$file.FullName.Substring($candidate.Length+1)]=[ordered]@{bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
$plan=[ordered]@{
    task=$Task;createdUtc=[DateTime]::UtcNow.ToString('o');user="$($owner.Domain)\$($owner.User)";session=$process.SessionId
    integritySid='S-1-16-8192';desktop='Default';releaseTarget=$formal;releaseParent=(Split-Path $formal -Parent)
    settingsTarget=$settings;launcher=$launcher;launcherSha256=(Get-FileHash -LiteralPath $launcher).Hash
    backup=$backup.FullName;backupManifest=(Join-Path $backup.FullName 'manifest.json');candidate=$candidate;candidateFiles=$files
    candidateReceiverDll=$files['Rightpad.Receiver.dll'].sha256;candidateNativeDll=$files['Rightpad.VirtualHid.dll'].sha256;candidateExe=$files['Rightpad.Receiver.exe'].sha256
    oldIdentity=[ordered]@{pid=$process.Id;startUtc=$process.StartTime.ToUniversalTime().ToString('o');exe=$all[0].ExecutablePath}
    oldSettingsSha256=(Get-FileHash -LiteralPath $settings).Hash
    guardTask='Rightpad Mode Sound Guard 20261024 Deploy';mutex='Global\RightpadModeSound20261024'
    guardDeadlineSeconds=900;deployRecoverAfterSeconds=300;deployTotalBudgetSeconds=300
    evidence=$evidence;runtimeRoot=(Join-Path $root 'receiver-runtime')
    oldLaunchKind=$(if($all[0].CommandLine -match '--dev-log-dir'){'launcher'}else{'desktop'})
}
foreach($name in @('ProtectionGuard.ps1','RunGuardTask.ps1','DeployRound.ps1','RoundSafety.ps1','StartProtection.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $recovery
}
$plan | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $recovery 'guard-plan.json') -Encoding UTF8
$backupManifest=Get-Content -LiteralPath $plan.backupManifest -Raw | ConvertFrom-Json
$releaseHashes=@(Get-ChildItem -LiteralPath $formal -Recurse -File | ForEach-Object {[ordered]@{path=$_.FullName;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$snapshots=@(Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.log') -Tail 300 | ForEach-Object {try {$r=$_|ConvertFrom-Json;if($r.type -eq 'snapshot' -and [int]$r.receiverPid -eq $process.Id){$r}}catch{}})
$service=New-Object -ComObject Schedule.Service;$service.Connect();$task=$service.GetFolder('\').GetTask('Rightpad Receiver Dev')
$survey=[ordered]@{
    at=[DateTime]::UtcNow.ToString('o');identity=$plan.oldIdentity;owner=$plan.user;session=$process.SessionId
    parent=$all[0].ParentProcessId;commandLine=$all[0].CommandLine;oldLaunchKind=$plan.oldLaunchKind
    formalFiles=$releaseHashes;settingsPath=$settings;settingsHash=$plan.oldSettingsSha256;settings=(Get-Content -LiteralPath $settings -Raw|ConvertFrom-Json)
    udp50000=@(Get-NetUDPEndpoint -LocalPort 50000|Select-Object LocalAddress,LocalPort,OwningProcess)
    launcherTaskXml=$task.Xml;launcherState=$task.State
    launcherCurrent=(Get-Content -LiteralPath (Join-Path $root 'receiver-runtime\current.json') -Raw|ConvertFrom-Json)
    hkcuStartup=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name rightpad -ErrorAction SilentlyContinue).rightpad
    flightSnapshot=($snapshots|Select-Object -Last 1)
    loadedModules=@($process.Modules|Where-Object {$_.FileName.StartsWith($formal,[StringComparison]::OrdinalIgnoreCase)}|ForEach-Object {[ordered]@{path=$_.FileName;sha256=(Get-FileHash -LiteralPath $_.FileName).Hash}})
    backup=$backup.FullName
}
$survey|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $evidence 'field-survey.json') -Encoding UTF8
# Preserve current launcher/runtime-path material as files as well as hash refs.
$support=Join-Path $recovery 'support';New-Item -ItemType Directory -Path $support -Force|Out-Null
foreach($entry in $backupManifest.externalFiles) {Copy-Item -LiteralPath $entry.path -Destination $support}
$task.Xml | Set-Content -LiteralPath (Join-Path $support 'receiver-task.xml') -Encoding UTF8
Get-ChildItem -LiteralPath $support -File|ForEach-Object {[ordered]@{path=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $recovery 'support-manifest.json') -Encoding UTF8
foreach($file in Get-ChildItem -LiteralPath $recovery -Filter '*.ps1') {
    $tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ($errors|Out-String)}
}
'PASS new plan and fresh field survey; no stop or deploy'
$plan.oldIdentity|ConvertTo-Json
