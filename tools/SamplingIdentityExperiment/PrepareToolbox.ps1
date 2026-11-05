param([string]$Task='20261102-sampling-toolbox-r1',[switch]$OriginalOnly)
$ErrorActionPreference='Stop'
$old='C:\rightpad-artifacts\test-results\20261102-sampling-identity-r1'
$round=& (Join-Path $PSScriptRoot '..\Invoke-Development.ps1') -Action NewTask -Task $task
$recovery=Join-Path $round 'recovery';$evidence=Join-Path $round 'evidence'
if(Test-Path (Join-Path $recovery 'plan.json')){throw 'Fresh toolbox round required'}
$plan=Get-Content (Join-Path $old 'recovery\plan.json') -Raw|ConvertFrom-Json
$plan.task=$task;$plan.evidence=$evidence;$plan.guardTask='Rightpad-'+$task;$plan.mutex='Local\Rightpad-'+$task
Copy-Item (Join-Path $old 'recovery\adb') $recovery -Recurse
$plan.adb=Join-Path $recovery 'adb\adb.exe'
(Get-Content (Join-Path $old 'recovery\Common.ps1') -Raw).Replace('20261102-sampling-identity-r1',$task)|Set-Content (Join-Path $recovery 'Common.ps1') -Encoding UTF8
Copy-Item (Join-Path $PSScriptRoot 'Guard.ps1') $recovery
Copy-Item (Join-Path $old 'recovery\StartProtection.ps1') $recovery
Copy-Item (Join-Path $old 'evidence\Analyze.ps1') $evidence
$inventory=if($OriginalOnly){@()}else{Get-Content (Join-Path $old 'evidence\apk-inventory.json') -Raw|ConvertFrom-Json}
$ids=@{control='com.rightpad.sampling.control';pubg='com.tencent.tmgp.pubgmhd';dfm='com.tencent.tmgp.dfm'}
$apks=@(foreach($item in $inventory){@{package=$ids[$item.variant];sha256=$item.sha256;path=$item.apk}})
$plan|Add-Member -NotePropertyName experimentApks -NotePropertyValue $apks
if($OriginalOnly){$plan.experiments=@()}
$planPath=Join-Path $recovery 'plan.json'
$plan|ConvertTo-Json -Depth 12|Set-Content $planPath -Encoding UTF8
. (Join-Path $recovery 'Common.ps1')
Assert-Device
$live=@(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'")
if($live.Count -ne 1){throw 'Expected one current Receiver'}
$receiver=Get-Process -Id $live[0].ProcessId
$owner=Invoke-CimMethod -InputObject $live[0] -MethodName GetOwner
$plan.receiver=[pscustomobject]@{pid=$receiver.Id;createdUtc=$receiver.StartTime.ToUniversalTime().ToString('o');path=$receiver.Path}
$plan.receiverFiles=@(Get-ChildItem -LiteralPath (Split-Path $receiver.Path -Parent) -File -Recurse|ForEach-Object {@{path=$_.FullName;sha256=(HashOf $_.FullName)}})
$plan.settingsHash=HashOf $plan.settingsPath
$plan.user=$owner.Domain+'\'+$owner.User;$plan.session=$receiver.SessionId
$backup=Join-Path $recovery 'android';$null=New-Item -ItemType Directory $backup
$plan.oldPackages=@(foreach($item in (PhonePackages)){
 $target=Join-Path $backup $item.name
 $null=Adb ('pull "'+$item.devicePath+'" "'+$target+'"')
 if((HashOf $target) -ne $item.sha256){throw 'Fresh original APK backup mismatch'}
 @{name=$item.name;devicePath=$item.devicePath;path=$target;bytes=(Get-Item $target).Length;sha256=$item.sha256}
})
$plan.candidate=Join-Path $backup 'base.apk';$plan.candidateHash=HashOf $plan.candidate
$plan.layout=Join-Path $backup 'layout.raw'
Adb 'exec-out run-as com.rightpad.capture cat files/screen-controls.properties' 15000 $plan.layout
$plan.layoutHash=LayoutHash
if((HashOf $plan.layout) -ne $plan.layoutHash){throw 'Fresh original layout backup mismatch'}
$plan.baseline=Snapshot
$dependencies=@(Get-ChildItem -LiteralPath $recovery -File -Recurse|Where-Object Name -ne 'plan.json')
$dependencies+=@(Get-Item $plan.layout,$plan.candidate,$plan.launcher)
$dependencies+=@($apks|ForEach-Object {Get-Item $_.path})
$plan.files=@($dependencies|ForEach-Object {@{path=$_.FullName;bytes=$_.Length;sha256=(HashOf $_.FullName)}})
$plan|ConvertTo-Json -Depth 12|Set-Content $planPath -Encoding UTF8
Assert-Backups
$s=Health $true
Save 'preparation.json' @{at=[DateTime]::UtcNow.ToString('o');snapshot=$s;retainedApks=$apks;scope='toolbox toggle only; no install or cleanup'}
Adb 'shell dumpsys activity service com.miui.securitycenter/com.miui.gamebooster.service.GameBoosterService'|Set-Content (Join-Path $evidence 'gamebooster-original.txt') -Encoding UTF8
& (Join-Path $recovery 'StartProtection.ps1')
