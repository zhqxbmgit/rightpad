param([string]$Task='20261029-mode-fixed-regions-r2',[string]$Device='192.168.110.127:5555',[string]$CandidateName='Release-final',[string]$ProtectionAttempt='')
$ErrorActionPreference='Stop'
if($Task -ne '20261029-mode-fixed-regions-r2'){throw 'This reviewed implementation is scoped to this round'}
$source=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$artifact=(Get-Content -LiteralPath (Join-Path $source 'artifact-root.txt') -Raw).Trim()
$round=Join-Path $artifact ('test-results\'+$Task);$recovery=Join-Path $round 'recovery';$evidence=Join-Path $round 'evidence'
if($ProtectionAttempt){
 if($ProtectionAttempt -notmatch '^[a-zA-Z0-9-]+$'){throw 'Invalid protection attempt'}
 $recovery=Join-Path $recovery $ProtectionAttempt;$evidence=Join-Path $evidence $ProtectionAttempt
 $null=New-Item -ItemType Directory -Path $recovery,$evidence
}
if(Test-Path -LiteralPath (Join-Path $recovery 'plan.json')){throw 'Recovery plan already exists; do not overwrite'}
. (Join-Path $PSScriptRoot 'Common.ps1')
$evidence=if($ProtectionAttempt){Join-Path (Join-Path $round 'evidence') $ProtectionAttempt}else{Join-Path $round 'evidence'}
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$sdk=Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$signer=Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory|Sort-Object Name -Descending|Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'lib\apksigner.jar')}|Select-Object -First 1
if(!$signer){throw 'apksigner unavailable'}
$plan=[pscustomobject]@{task=$Task;evidence=$evidence;adb=(Join-Path $sdk 'platform-tools\adb.exe');device=$Device;
 java=$(if($env:JAVA_HOME){Join-Path $env:JAVA_HOME 'bin\java.exe'}else{(Get-Command java.exe -ErrorAction Stop).Source});apksignerJar=(Join-Path $signer.FullName 'lib\apksigner.jar');
 launcher=(Join-Path $source 'windows\tools\RightpadReceiverTask.ps1');formal=(Join-Path $source 'windows\Rightpad.Receiver\bin\Release\net8.0-windows');
 settings=(Join-Path $env:LOCALAPPDATA 'rightpad\settings.json');flightLog=(Join-Path $env:LOCALAPPDATA 'rightpad\diagnostics\flight-recorder.log')}
$installed=PhonePackages
$serial=Adb 'shell getprop ro.serialno';$model=Adb 'shell getprop ro.product.model'
if($serial -ne 'c2e6a5e3' -or $model -ne '23127PN0CC'){throw 'Device differs from this round verified baseline'}
$oldReceiver=Find-Receiver;if(!$oldReceiver){throw 'Formal Receiver absent'}
Import-DesktopHelper;$ctx=Get-DesktopIdentity (Get-Process -Id $oldReceiver.pid)
if($ctx.IntegritySid -ne 'S-1-16-8192' -or $ctx.Desktop -ne 'Default' -or $ctx.SessionId -eq 0){throw 'Old Receiver context invalid'}
$baseline=Snapshot $oldReceiver.pid
if(!$baseline -or $baseline.runtimeState -ne 'Running' -or $baseline.connectionState -ne 'Connected' -or $baseline.lastError){throw 'Old pair not healthy'}
& (Join-Path $source 'tools\New-RecoveryBackup.ps1') -TaskDirectory $round|Out-File -LiteralPath (Join-Path $evidence ('backup-preflight-'+$stamp+'.txt')) -Encoding UTF8
$backup=Get-ChildItem -LiteralPath (Join-Path $round 'recovery') -Directory -Filter 'backup-*'|Sort-Object Name -Descending|Select-Object -First 1
if(!$backup){throw 'Windows backup missing'}
$android=Join-Path $recovery ('android-'+$stamp);$null=New-Item -ItemType Directory -Path $android
$oldPackages=@(foreach($entry in $installed){
 $path=Join-Path $android $entry.name
 $out=Adb ('pull "'+$entry.devicePath+'" "'+$path+'"') 30000
 if((HashOf $path) -ne $entry.sha256){throw 'Pulled APK does not match installed file'}
 [ordered]@{name=$entry.name;path=$path;devicePath=$entry.devicePath;sha256=$entry.sha256;bytes=(Get-Item -LiteralPath $path).Length}
})
$layoutBackup=Join-Path $recovery ('screen-controls-'+$stamp+'.properties')
$null=Adb 'exec-out run-as com.rightpad.capture cat files/screen-controls.properties' 15000 $layoutBackup
$layoutHash=HashOf $layoutBackup
if((LayoutHash) -ne $layoutHash){throw 'Raw layout copy mismatch'}
$apk=Join-Path $round ('candidate\android-'+$stamp+'\rightpad-mode-fixed-regions.apk');$null=New-Item -ItemType Directory -Path (Split-Path $apk -Parent)
$apkSource=Join-Path $artifact ('builds\android\'+$Task+'\app\build\outputs\apk\debug\app-debug.apk')
Copy-Item -LiteralPath $apkSource -Destination $apk
$newPackages=@([ordered]@{name='base.apk';path=$apk;sha256=(HashOf $apk);bytes=(Get-Item -LiteralPath $apk).Length})
$cert=(Certificate $oldPackages[0].path).digest
foreach($entry in @($oldPackages)+@($newPackages)){
 $result=Certificate $entry.path
 if($result.digest -ne $cert){throw 'Candidate and actual installed signing certificates differ'}
 $result.report|Set-Content -LiteralPath (Join-Path $evidence ('signature-'+$(if($entry.path -eq $apk){'candidate'}else{$entry.name})+'.txt')) -Encoding UTF8
}
$candidate=Join-Path (Join-Path $round 'candidate') $CandidateName
$newWindows=@(Get-ChildItem -LiteralPath $candidate -File -Recurse|ForEach-Object {
 [ordered]@{name=$_.FullName.Substring($candidate.Length+1);path=$_.FullName;bytes=$_.Length;sha256=(HashOf $_.FullName)}
})
$oldWindows=@(Get-ChildItem -LiteralPath (Join-Path $backup.FullName 'release') -File -Recurse|ForEach-Object {
 [ordered]@{name=$_.FullName.Substring((Join-Path $backup.FullName 'release').Length+1);backup=$_.FullName;bytes=$_.Length;sha256=(HashOf $_.FullName)}
})
if((Compare-Object @($newWindows|ForEach-Object name) @($oldWindows|ForEach-Object name))){throw 'Package file sets differ; exact recovery needs a reviewed extra-file rule'}
foreach($name in @('Rightpad.VirtualHid.dll','libvirtualhid-LICENSE.txt','lizardbyte-common-LICENSE.txt')){
 if(($newWindows|Where-Object name -eq $name).sha256 -ne ($oldWindows|Where-Object name -eq $name).sha256){throw 'Unchanged native bridge/licenses mismatch'}
}
foreach($name in @('Common.ps1','Guard.ps1','Deploy.ps1','StartGuard.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $recovery $name)}
$dependencies=@(foreach($path in @($plan.launcher,(Join-Path $source 'tools\ArtifactPaths.ps1'),(Join-Path $source 'artifact-root.txt'))+@(Get-ChildItem -LiteralPath $recovery -Filter '*.ps1' -File|ForEach-Object FullName)){
 [ordered]@{path=$path;bytes=(Get-Item -LiteralPath $path).Length;sha256=(HashOf $path)}
})
$backupFiles=@(Get-ChildItem -LiteralPath $backup.FullName -File -Recurse|ForEach-Object {[ordered]@{path=$_.FullName;bytes=$_.Length;sha256=(HashOf $_.FullName)}})+
 @($oldPackages|ForEach-Object {[ordered]@{path=$_.path;bytes=$_.bytes;sha256=$_.sha256}})+@([ordered]@{path=$layoutBackup;bytes=(Get-Item $layoutBackup).Length;sha256=$layoutHash})
$data=[ordered]@{}
foreach($property in $plan.PSObject.Properties){$data[$property.Name]=$property.Value}
$data.hardwareSerial=$serial;$data.model=$model;$data.user=$ctx.User;$data.session=$ctx.SessionId;$data.oldReceiver=$oldReceiver;$data.baseline=$baseline
$data.certificate=$cert;$data.oldPackages=$oldPackages;$data.newPackages=$newPackages;$data.oldWindows=$oldWindows;$data.newWindows=$newWindows
$data.settingsBackup=(Join-Path $backup.FullName 'settings.json');$data.settingsHash=(HashOf $plan.settings)
$data.layoutBackup=$layoutBackup;$data.layoutHash=$layoutHash;$data.backupFiles=$backupFiles;$data.dependencies=$dependencies
$data.candidateFiles=@($newWindows|ForEach-Object {[ordered]@{path=$_.path;bytes=$_.bytes;sha256=$_.sha256}})+@($newPackages|ForEach-Object {[ordered]@{path=$_.path;bytes=$_.bytes;sha256=$_.sha256}})
$data.guardTask='Rightpad Fixed Regions Recovery '+$Task+$(if($ProtectionAttempt){' '+$ProtectionAttempt}else{''});$data.mutex='Local\RightpadFixedRegions-'+[guid]::NewGuid().ToString('N')
$data.createdUtc=[DateTime]::UtcNow.ToString('o');$data.recovery=$recovery
$data|ConvertTo-Json -Depth 15|Set-Content -LiteralPath (Join-Path $recovery 'plan.json') -Encoding UTF8
Save 'pair-backup.json' $data
Save 'initial-phone.json' ([ordered]@{serial=$serial;model=$model;device=$Device;packages=$installed;wmSize=(Adb 'shell wm size');wmDensity=(Adb 'shell wm density');
 package=(Adb 'shell dumpsys package com.rightpad.capture');activity=(Adb 'shell dumpsys activity activities');layoutHash=$layoutHash})
"PASS backup/candidate/signature plan prepared: $recovery"
