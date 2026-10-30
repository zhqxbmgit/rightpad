$ErrorActionPreference='Stop'
$task='20261030-mode-region-feedback-r1'
$round=Join-Path ([IO.File]::ReadAllText('C:\rightpad\artifact-root.txt').Trim()) ('test-results\'+$task)
$recovery=Join-Path $round 'recovery';$evidence=Join-Path $round 'evidence';$backup=Join-Path $recovery 'android'
if(Test-Path -LiteralPath (Join-Path $recovery 'plan.json')){throw 'Plan already exists; do not overwrite this round'}
$null=New-Item -ItemType Directory -Path $backup -Force
foreach($name in @('Common.ps1','Guard.ps1','StartProtection.ps1','Deploy.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $recovery}
$adbRoot='C:\Users\zhq\AppData\Local\Android\Sdk\platform-tools';$adbCopy=Join-Path $recovery 'adb';$null=New-Item -ItemType Directory -Path $adbCopy
foreach($name in @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll')){Copy-Item -LiteralPath (Join-Path $adbRoot $name) -Destination $adbCopy}
$adbExe=Join-Path $adbCopy 'adb.exe'
$online=@((& $adbExe devices) -split '\r?\n' | Where-Object {$_ -match '^\S+\s+device$'} | ForEach-Object {($_ -split '\s+')[0]})
if($online.Count -ne 1){throw 'Expected exactly one actual online device'}
$device=$online[0];$hardware=(& $adbExe -s $device shell getprop ro.serialno).Trim();$model=(& $adbExe -s $device shell getprop ro.product.model).Trim()
$candidate=Join-Path $round 'candidate\mode-region-feedback.apk'
Copy-Item -LiteralPath ('C:\rightpad-artifacts\builds\android\'+$task+'\app\build\outputs\apk\debug\app-debug.apk') -Destination $candidate
$live=@(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'")
if($live.Count -ne 1){throw 'Expected one current Receiver'}
$p=Get-Process -Id $live[0].ProcessId;$owner=Invoke-CimMethod -InputObject $live[0] -MethodName GetOwner
$release=Split-Path $p.Path -Parent
$plan=[ordered]@{task=$task;evidence=$evidence;adb=(Join-Path $adbCopy 'adb.exe');device=$device;hardwareSerial=$hardware;model=$model;
 candidate=$candidate;candidateHash=(Get-FileHash $candidate).Hash;layout=(Join-Path $backup 'layout.raw');layoutHash='';
 oldPackages=@();files=@();receiverFiles=@(Get-ChildItem -LiteralPath $release -File -Recurse|ForEach-Object {[ordered]@{path=$_.FullName;sha256=(Get-FileHash $_.FullName).Hash}});
 receiver=[ordered]@{pid=$p.Id;createdUtc=$p.StartTime.ToUniversalTime().ToString('o');path=$p.Path};
 settingsPath=(Join-Path $env:LOCALAPPDATA 'rightpad\settings.json');settingsHash=(Get-FileHash (Join-Path $env:LOCALAPPDATA 'rightpad\settings.json')).Hash;
 flightLog=(Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.log');launcher='C:\rightpad\windows\tools\RightpadReceiverTask.ps1';
 user=($owner.Domain+'\'+$owner.User);session=$p.SessionId;mutex='Local\RightpadAndroidRegionFeedback20261030R1';
 guardTask='Rightpad-Android-Region-Feedback-20261030-R1';baseline=$null;certificate='';preparedUtc=[DateTime]::UtcNow.ToString('o')}
$planPath=Join-Path $recovery 'plan.json';$plan|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $planPath -Encoding UTF8
. (Join-Path $recovery 'Common.ps1')
Assert-Device
$plan.baseline=Snapshot
if(!$plan.baseline -or $plan.baseline.connectionState -ne 'Connected'){throw 'Baseline not Connected'}
$installed=PhonePackages
foreach($e in $installed){
 $target=Join-Path $backup $e.name
 $null=Adb ('pull "'+$e.devicePath+'" "'+$target+'"')
 if((HashOf $target) -ne $e.sha256){throw 'Pulled APK mismatch'}
 $plan.oldPackages+=@([ordered]@{name=$e.name;devicePath=$e.devicePath;path=$target;bytes=(Get-Item $target).Length;sha256=$e.sha256})
}
Adb 'exec-out run-as com.rightpad.capture cat files/screen-controls.properties' 15000 $plan.layout
$plan.layoutHash=LayoutHash
if((HashOf $plan.layout) -ne $plan.layoutHash){throw 'Raw layout backup mismatch'}
$signer='C:\Users\zhq\AppData\Local\Android\Sdk\build-tools\36.0.0\apksigner.bat'
$env:PATH=$env:JAVA_HOME+'\bin;'+$env:PATH
function Certificate([string]$path,[string]$log){
 $text=(& $signer verify --verbose --print-certs $path 2>&1|Out-String)
 $text|Set-Content -LiteralPath (Join-Path $evidence $log) -Encoding UTF8
 if($LASTEXITCODE -ne 0){throw 'APK signature verification failed'}
 $cert=[regex]::Match($text,'(?:Signer #1 certificate|certificate) SHA-256 digest: ([0-9a-fA-F]+)').Groups[1].Value.ToUpperInvariant()
 if(!$cert){throw 'Signer certificate missing'};return $cert
}
$candidateCert=Certificate $plan.candidate 'candidate-signature.txt'
foreach($e in $plan.oldPackages){if((Certificate $e.path ('old-'+$e.name+'-signature.txt')) -ne $candidateCert){throw 'Signature mismatch; old App retained'}}
$plan.certificate=$candidateCert
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inventories=@(foreach($apk in @($plan.candidate)+@($plan.oldPackages|ForEach-Object path)){
 $zip=[IO.Compression.ZipFile]::OpenRead($apk)
 try{[ordered]@{path=$apk;sha256=(HashOf $apk);bytes=(Get-Item $apk).Length;entries=@($zip.Entries|ForEach-Object {[ordered]@{name=$_.FullName;bytes=$_.Length}})}}finally{$zip.Dispose()}
})
Save 'apk-inventories.json' $inventories
Adb 'shell dumpsys package com.rightpad.capture' | Set-Content -LiteralPath (Join-Path $evidence 'android-package-before.txt') -Encoding UTF8
Adb 'shell dumpsys activity activities' | Set-Content -LiteralPath (Join-Path $evidence 'android-activity-before.txt') -Encoding UTF8
Adb 'shell pidof com.rightpad.capture' | Set-Content -LiteralPath (Join-Path $evidence 'android-pid-before.txt') -Encoding UTF8
# Files required for recovery, context verification and candidate installation are frozen before ready.
$dependencies=@(Get-ChildItem -LiteralPath $recovery -File -Recurse|Where-Object Name -ne 'plan.json')
$dependencies+=Get-Item -LiteralPath $candidate,$plan.launcher
$plan.files=@($dependencies|ForEach-Object {[ordered]@{path=$_.FullName;bytes=$_.Length;sha256=(HashOf $_.FullName)}})
$plan|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $planPath -Encoding UTF8
Assert-Backups;Assert-Receiver|Out-Null
Save 'preparation.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');device=$plan.device;hardwareSerial=$plan.hardwareSerial;model=$plan.model;api=(Adb 'shell getprop ro.build.version.sdk');
 packages=$plan.oldPackages;candidateHash=$plan.candidateHash;certificate=$plan.certificate;layoutHash=$plan.layoutHash;layoutBytes=(Get-Item $plan.layout).Length;receiver=$plan.receiver;baseline=$plan.baseline;backupVerified=$true})
'PASS current full APK set, raw layout, compatible certificate and unchanged Receiver verified'
