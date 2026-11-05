param([ValidateSet('original','control','pubg','dfm')][string]$Variant,[int]$Seconds=40,
    [string]$Base='C:\rightpad-artifacts\test-results\20261102-sampling-identity-r1',
    [ValidatePattern('^[a-zA-Z0-9._-]+$')][string]$CaptureName='',
    [string]$PriorIdentityCapture='')
$ErrorActionPreference='Stop'
. (Join-Path $base 'recovery\Common.ps1')
$identities=@{original='com.rightpad.capture';control='com.rightpad.sampling.control';pubg='com.tencent.tmgp.pubgmhd';dfm='com.tencent.tmgp.dfm'}
$package=$identities[$Variant]
$activity=Adb 'shell dumpsys activity activities'
if($activity -notmatch ('topResumedActivity=.*'+[regex]::Escape($package)+'/(?:com\.rightpad\.capture\.|\.)MainActivity')) {throw 'Wrong foreground for capture'}
$snapshot=Assert-Receiver
if($snapshot.connectionState -ne 'Connected' -or $snapshot.lastHeartbeatAgeMs -gt 2000){throw 'Experiment not connected'}
$appPid=Adb ('shell pidof '+$package)
$phoneLog=Adb ('logcat -d --pid='+$appPid+' -t 350 -v threadtime')
if($phoneLog -notmatch ('(?i)senderRunId=0*'+([string]$snapshot.senderRunId).TrimStart('0'))){
 $phoneLog=Adb ('logcat -d --pid='+$appPid+' -v threadtime') 20000
}
$identitySource='live phone log'
if($phoneLog -notmatch ('(?i)senderRunId=0*'+([string]$snapshot.senderRunId).TrimStart('0'))){
 if(!$PriorIdentityCapture){throw 'Phone and Receiver sender run mismatch'}
 $priorAfter=Get-Content (Join-Path $PriorIdentityCapture 'receiver-after.json') -Raw|ConvertFrom-Json
 $priorLog=Get-Content (Join-Path $PriorIdentityCapture 'phone-before.log') -Raw
 if($priorAfter.senderRunId -ne $snapshot.senderRunId -or $priorAfter.receiverPid -ne $snapshot.receiverPid -or
    $priorLog -notmatch ('(?m)\s'+[regex]::Escape($appPid)+'\s+\d+\s+.*senderRunId=0*'+([string]$snapshot.senderRunId).TrimStart('0'))){throw 'Prior live identity attestation mismatch'}
 $identitySource='same live Receiver Sender and phone PID as '+$PriorIdentityCapture
}
$actualHash=((Adb ('shell run-as '+$package+' sha256sum files/screen-controls.properties')) -split '\s+')[0]
if($actualHash -ne $plan.layoutHash){throw 'Experiment layout changed'}
$dir=Join-Path $evidence $(if($CaptureName){$CaptureName}else{$Variant})
if(Test-Path $dir){throw 'Do not overwrite a previous capture'}
$null=New-Item -ItemType Directory $dir
@{phonePid=$appPid;sender=$snapshot.senderRunId;identitySource=$identitySource;at=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $dir 'identity.json') -Encoding UTF8
$activity | Set-Content (Join-Path $dir 'activity-before.txt') -Encoding UTF8
$snapshot|ConvertTo-Json -Depth 10|Set-Content (Join-Path $dir 'receiver-before.json') -Encoding UTF8
$phoneLog|Set-Content (Join-Path $dir 'phone-before.log') -Encoding UTF8
Adb 'shell dumpsys activity service com.miui.securitycenter/com.miui.gamebooster.service.GameBoosterService' | Set-Content (Join-Path $dir 'gamebooster-before.txt') -Encoding UTF8
$raw=Join-Path $dir 'getevent.txt';$err=Join-Path $dir 'getevent-stderr.txt'
$capture=Start-Process -FilePath $plan.adb -ArgumentList ('-s '+$plan.device+' shell getevent -lt /dev/input/event7') -WindowStyle Hidden -RedirectStandardOutput $raw -RedirectStandardError $err -PassThru
Write-Output ('CAPTURE_STARTED variant='+$Variant+' package='+$package+' pid='+$appPid+' sender='+$snapshot.senderRunId+' seconds='+$Seconds)
try{Start-Sleep -Seconds $Seconds}finally{if(!$capture.HasExited){$capture.Kill();$capture.WaitForExit()};$capture.Dispose()}
$after=Assert-Receiver
$after|ConvertTo-Json -Depth 10|Set-Content (Join-Path $dir 'receiver-after.json') -Encoding UTF8
Adb 'shell dumpsys activity activities'|Set-Content (Join-Path $dir 'activity-after.txt') -Encoding UTF8
Adb ('logcat -d --pid='+$appPid+' -t 350 -v threadtime')|Set-Content (Join-Path $dir 'phone-after.log') -Encoding UTF8
Write-Output ('CAPTURE_FINISHED bytes='+(Get-Item $raw).Length+' acceptedSamplesDelta='+($after.acceptedSamples-$snapshot.acceptedSamples))
Get-Content $err
