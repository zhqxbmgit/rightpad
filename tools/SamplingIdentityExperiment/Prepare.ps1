param([string]$Task='20261102-sampling-identity-r1')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\ArtifactPaths.ps1')
$round=New-RightpadTask $Task
$recovery=Join-Path $round 'recovery';$evidence=Join-Path $round 'evidence';$backup=Join-Path $recovery 'android'
if (Test-Path (Join-Path $recovery 'plan.json')) { throw 'Fresh experiment plan required' }
$null=New-Item -ItemType Directory -Path $backup
$common=Get-Content (Join-Path $PSScriptRoot '..\ModeRegionFeedbackDeployment\Common.ps1') -Raw
$common.Replace('20261030-mode-region-feedback-r1',$Task) | Set-Content (Join-Path $recovery 'Common.ps1') -Encoding UTF8
Copy-Item (Join-Path $PSScriptRoot 'Guard.ps1') $recovery
Copy-Item (Join-Path $PSScriptRoot '..\ModeRegionFeedbackDeployment\StartProtection.ps1') $recovery
$adbRoot='C:\Users\zhq\AppData\Local\Android\Sdk\platform-tools';$adbCopy=Join-Path $recovery 'adb'
$null=New-Item -ItemType Directory -Path $adbCopy
foreach($name in @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll')){Copy-Item (Join-Path $adbRoot $name) $adbCopy}
$live=@(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'")
if($live.Count -ne 1){throw 'Expected one live Receiver'}
$p=Get-Process -Id $live[0].ProcessId;$owner=Invoke-CimMethod -InputObject $live[0] -MethodName GetOwner
$release=Split-Path $p.Path -Parent
$plan=[ordered]@{task=$Task;evidence=$evidence;adb=(Join-Path $adbCopy 'adb.exe');device='192.168.110.127:5555';hardwareSerial='c2e6a5e3';model='23127PN0CC';
 candidate=(Join-Path $backup 'base.apk');candidateHash='';layout=(Join-Path $backup 'layout.raw');layoutHash='';oldPackages=@();files=@();
 experiments=@('com.rightpad.sampling.control','com.tencent.tmgp.pubgmhd','com.tencent.tmgp.dfm');
 receiverFiles=@(Get-ChildItem -LiteralPath $release -File -Recurse|ForEach-Object {@{path=$_.FullName;sha256=(Get-FileHash $_.FullName).Hash}});
 receiver=@{pid=$p.Id;createdUtc=$p.StartTime.ToUniversalTime().ToString('o');path=$p.Path};
 settingsPath=(Join-Path $env:LOCALAPPDATA 'rightpad\settings.json');settingsHash=(Get-FileHash (Join-Path $env:LOCALAPPDATA 'rightpad\settings.json')).Hash;
 flightLog=(Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.log');launcher='C:\rightpad\windows\tools\RightpadReceiverTask.ps1';
 user=($owner.Domain+'\'+$owner.User);session=$p.SessionId;mutex='Local\RightpadSamplingIdentity20261102R1';
 guardTask='Rightpad-Sampling-Identity-20261102-R1';baseline=$null;preparedUtc=[DateTime]::UtcNow.ToString('o')}
$planPath=Join-Path $recovery 'plan.json'
$plan|ConvertTo-Json -Depth 12|Set-Content $planPath -Encoding UTF8
. (Join-Path $recovery 'Common.ps1')
Assert-Device
$plan.baseline=Snapshot
if(!$plan.baseline -or $plan.baseline.connectionState -ne 'Connected'){throw 'Original not Connected'}
foreach($e in (PhonePackages)){
 $target=Join-Path $backup $e.name
 $null=Adb ('pull "'+$e.devicePath+'" "'+$target+'"')
 if((HashOf $target) -ne $e.sha256){throw 'APK backup mismatch'}
 $plan.oldPackages+=@(@{name=$e.name;devicePath=$e.devicePath;path=$target;bytes=(Get-Item $target).Length;sha256=$e.sha256})
}
$plan.candidateHash=HashOf $plan.candidate
Adb 'exec-out run-as com.rightpad.capture cat files/screen-controls.properties' 15000 $plan.layout
$plan.layoutHash=LayoutHash
if((HashOf $plan.layout) -ne $plan.layoutHash){throw 'Original layout backup mismatch'}
$dependencies=@(Get-ChildItem -LiteralPath $recovery -File -Recurse|Where-Object Name -ne 'plan.json')
$dependencies+=@(Get-ChildItem -LiteralPath (Join-Path $round 'candidate') -File -Recurse)
$dependencies+=Get-Item $plan.launcher
$plan.files=@($dependencies|ForEach-Object {@{path=$_.FullName;bytes=$_.Length;sha256=(HashOf $_.FullName)}})
$plan|ConvertTo-Json -Depth 12|Set-Content $planPath -Encoding UTF8
Assert-Backups
$health=Health $true
Save 'preparation.json' @{at=[DateTime]::UtcNow.ToString('o');snapshot=$health;receiver=$plan.receiver;packages=$plan.oldPackages;layoutHash=$plan.layoutHash;experiments=$plan.experiments}
'PASS original package/layout backup and full real health'
