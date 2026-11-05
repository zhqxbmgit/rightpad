param([string]$Task='20261103-io-abort-diagnostics-r1')
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
. (Join-Path $repo 'tools\ArtifactPaths.ps1')
$root=Get-RightpadArtifactRoot
$round=Join-Path $root ('test-results\'+$Task)
$recovery=Join-Path $round 'recovery';$evidence=Join-Path $round 'evidence'
$legacy=Join-Path $repo 'tools\ModeSoundRemovalDeployment'
$formal=Join-Path $repo 'windows\Rightpad.Receiver\bin\Release\net8.0-windows'
$settings=Join-Path $env:LOCALAPPDATA 'rightpad\settings.json'
$candidate=Join-Path $round 'candidate\Release'
$all=@(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'")
if($all.Count -ne 1 -or $all[0].ExecutablePath -ne (Join-Path $formal 'Rightpad.Receiver.exe')){throw 'Expected one formal Receiver'}
$process=Get-Process -Id $all[0].ProcessId
$owner=Invoke-CimMethod -InputObject $all[0] -MethodName GetOwner
if($owner.ReturnValue -ne 0){throw 'Owner unavailable'}
$backup=Get-ChildItem -LiteralPath $recovery -Directory -Filter 'backup-*'|Sort-Object Name -Descending|Select-Object -First 1
if(!$backup){throw 'Create a fresh backup first'}
$manifest=Get-Content -LiteralPath (Join-Path $backup.FullName 'manifest.json') -Raw|ConvertFrom-Json
$files=[ordered]@{}
foreach($file in Get-ChildItem -LiteralPath $candidate -File -Recurse){$files[$file.FullName.Substring($candidate.Length+1)]=[ordered]@{bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}}
$settingsHash=(Get-FileHash -LiteralPath $settings).Hash
$prepared=Join-Path $recovery 'settings-byte-identical.json'
Copy-Item -LiteralPath $settings -Destination $prepared
foreach($name in @('ProtectionGuard.ps1','RunGuardTask.ps1','DeployRound.ps1','RoundSafety.ps1','StartProtection.ps1','SettingsTransition.ps1')){
 $text=Get-Content -LiteralPath (Join-Path $legacy $name) -Raw
 $text=$text.Replace('20261024-mode-sound-removal',$Task).Replace('Rightpad Mode Sound Removal Guard 20261024','Rightpad IO Diagnostics Guard 20261103')
 if($name -eq 'ProtectionGuard.ps1'){$text=$text.Replace('function Invoke-Preflight {','function Invoke-BasePreflight {').Replace('function Test-PublishedHealth($release) {','function Test-BasePublishedHealth($release) {')}
 [IO.File]::WriteAllText((Join-Path $recovery $name),$text,(New-Object Text.UTF8Encoding($false)))
}
Add-Content -LiteralPath (Join-Path $recovery 'RoundSafety.ps1') -Value (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'HealthOverrides.ps1') -Raw)
$plan=[ordered]@{
 task=$Task;createdUtc=[DateTime]::UtcNow.ToString('o');user="$($owner.Domain)\$($owner.User)";session=$process.SessionId;integritySid='S-1-16-8192';desktop='Default'
 releaseTarget=$formal;releaseParent=(Split-Path $formal -Parent);settingsTarget=$settings;launcher=(Join-Path $repo 'windows\tools\RightpadReceiverTask.ps1')
 launcherSha256=(Get-FileHash -LiteralPath (Join-Path $repo 'windows\tools\RightpadReceiverTask.ps1')).Hash
 backup=$backup.FullName;backupManifest=(Join-Path $backup.FullName 'manifest.json');candidate=$candidate;candidateFiles=$files
 candidateReceiverDll=$files['Rightpad.Receiver.dll'].sha256;candidateNativeDll=$files['Rightpad.VirtualHid.dll'].sha256;candidateExe=$files['Rightpad.Receiver.exe'].sha256
 oldIdentity=[ordered]@{pid=$process.Id;startUtc=$process.StartTime.ToUniversalTime().ToString('o');exe=$all[0].ExecutablePath}
 oldLaunchKind=$(if($all[0].CommandLine -match '--dev-log-dir'){'launcher'}else{'desktop'})
 oldSettingsSha256=$settingsHash;preparedSettings=$prepared;expectedSettingsSha256=$settingsHash;settingsNeedsCleanup=$false
 guardTask='Rightpad IO Diagnostics Guard 20261103';mutex='Local\RightpadIoDiagnostics20261103'
 guardDeadlineSeconds=900;deployRecoverAfterSeconds=300;deployTotalBudgetSeconds=300;evidence=$evidence;runtimeRoot=(Join-Path $root 'receiver-runtime')
}
$plan|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $recovery 'guard-plan.json') -Encoding UTF8
foreach($file in Get-ChildItem -LiteralPath $recovery -Filter '*.ps1'){
 $tokens=$null;$errors=$null;$null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ($errors|Out-String)}
}
[pscustomobject]@{plan=(Join-Path $recovery 'guard-plan.json');oldPid=$process.Id;settingsHash=$settingsHash;mutations=$false}
