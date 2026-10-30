. (Join-Path $PSScriptRoot 'Common.ps1')
$armed=$null
function Recover([string]$reason){
 Save 'recovery-started.json' ([ordered]@{reason=$reason;at=[DateTime]::UtcNow.ToString('o');pid=$PID})
 $restored=With-Lock {
  Assert-Backup
  $live=Find-Receiver
  if($live){Stop-Receiver $live}
  foreach($file in $plan.oldWindows){Copy-Item -LiteralPath $file.backup -Destination (Join-Path $plan.formal $file.name) -Force}
  Copy-Item -LiteralPath $plan.settingsBackup -Destination $plan.settings -Force
  Assert-Formal $true
  $old=Start-Receiver 'recovery'
  Assert-Device
  $arguments=if(@($plan.oldPackages).Count -eq 1){'install -r "'+$plan.oldPackages[0].path+'"'}else{
   'install-multiple -r '+(($plan.oldPackages|ForEach-Object {'"'+$_.path+'"'}) -join ' ')
  }
  for($attempt=1;$attempt -le 3;$attempt++){
   try{$result=Adb $arguments 45000;Log ('old APK install '+$result);if($result -notmatch 'Success'){throw 'Old APK installation failed'};break}
   catch{if($attempt -eq 3){throw};Log ('bounded restore retry '+$_);Start-Sleep -Seconds 2}
  }
  Assert-PackageSet (PhonePackages) $plan.oldPackages
  $hash=$null;try{$hash=LayoutHash}catch{}
  if($hash -ne $plan.layoutHash){$null=Adb 'shell "run-as com.rightpad.capture sh -c ''cat > files/screen-controls.properties''"' 15000 '' ([IO.File]::ReadAllBytes($plan.layoutBackup))}
  if((LayoutHash) -ne $plan.layoutHash){throw 'Restored layout mismatch'}
  $null=Adb 'shell am start -S -n com.rightpad.capture/.MainActivity' 20000
  $old
 } 60000
 $until=[DateTime]::UtcNow.AddSeconds(60);$healthy=$null
 while([DateTime]::UtcNow -lt $until){try{$healthy=With-Lock {Health $restored $true};break}catch{Log ('restore health pending '+$_);Start-Sleep -Seconds 2}}
 if(!$healthy){throw 'Old pair restoration health not established'}
 Save 'recovery-applied.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');actualRollback=$true;healthy=$healthy;reason=$reason})
}
try {
 $context=Guard-Context;$request=ReadRecord 'guard-start-request.json'
 if($request.task -ne $plan.task -or [DateTime]::Parse($request.at).ToUniversalTime() -gt [DateTime]::Parse($context.createdUtc).ToUniversalTime()){throw 'Launch request/nonce mismatch'}
 $checks=With-Lock {
  Assert-Backup;Assert-Signatures;Assert-Device;Assert-Formal $true;Assert-PackageSet (PhonePackages) $plan.oldPackages
  if((LayoutHash) -ne $plan.layoutHash){throw 'Layout changed before guard'}
  Assert-Identity $plan.oldReceiver|Out-Null
  $null=Adb 'shell run-as com.rightpad.capture id'
  $s=Snapshot $plan.oldReceiver.pid
  if(!$s -or $s.runtimeState -ne 'Running' -or $s.connectionState -ne 'Connected' -or $s.lastError){throw 'Old pair not healthy before guard'}
  $fullHealth=Health $plan.oldReceiver $true
  # Read/hash the exact packages, scripts, settings, layout and launcher under
  # this independent token. No Stop, install, settings or layout write in preflight.
  [ordered]@{passed=$true;backupFiles=@($plan.backupFiles).Count;candidateFiles=@($plan.candidateFiles).Count;
   mutexAcquiredReleased=$true;deviceIdentity=$true;fullSplits=@($plan.oldPackages).Count;mutations=$false;snapshot=$s;fullHealth=$fullHealth}
 }
 Save 'independent-preflight.json' ([ordered]@{context=$context;checks=$checks;at=[DateTime]::UtcNow.ToString('o')})
 $ready=[ordered]@{nonce=$request.nonce;context=$context;at=[DateTime]::UtcNow.ToString('o');passed=$true;planHash=(HashOf $planPath)}
 Save 'guard-ready.json' $ready
 $idleUntil=[DateTime]::UtcNow.AddMinutes(15);$healthySince=$null;$healthChecks=0
 while($true){
  Save 'guard-heartbeat.json' ([ordered]@{nonce=$ready.nonce;pid=$PID;createdUtc=$context.createdUtc;at=[DateTime]::UtcNow.ToString('o')})
  if(!$armed -and (Test-Path -LiteralPath (Join-Path $evidence 'deployment-armed.json'))){
   $a=ReadRecord 'deployment-armed.json'
   if($a.nonce -ne $ready.nonce -or $a.guardPid -ne $PID -or $a.guardCreatedUtc -ne $context.createdUtc -or
      $a.planHash -ne $ready.planHash -or [DateTime]::Parse($a.at).ToUniversalTime() -lt [DateTime]::Parse($ready.at).ToUniversalTime()){throw 'Armed record not this guard'}
   $armed=$a
   Save 'guard-armed.json' ([ordered]@{nonce=$ready.nonce;pid=$PID;createdUtc=$context.createdUtc;at=[DateTime]::UtcNow.ToString('o');readyAt=$ready.at;recoverAfterUtc=$armed.recoverAfterUtc})
  }
  if($armed){
   if(Test-Path -LiteralPath (Join-Path $evidence 'deployment-failed.json')){Recover 'Deployment failed';exit 0}
   if(Test-Path -LiteralPath (Join-Path $evidence 'deployment-published.json')){
    try {
     $published=ReadRecord 'deployment-published.json';if($published.nonce -ne $ready.nonce){throw 'Publication nonce mismatch'}
     $health=With-Lock {Health $published.receiver}
     if($health.runtimeRunId -ne $published.runtimeRunId){throw 'Receiver Runtime changed during Android recovery'}
     if(!$healthySince){$healthySince=[DateTime]::UtcNow};$healthChecks++
     Save 'guard-health.json' ([ordered]@{nonce=$ready.nonce;checks=$healthChecks;healthySince=$healthySince.ToString('o');at=[DateTime]::UtcNow.ToString('o');health=$health})
     if(([DateTime]::UtcNow-$healthySince).TotalSeconds -ge 60){
      Save 'guard-released.json' ([ordered]@{nonce=$ready.nonce;context=$context;at=[DateTime]::UtcNow.ToString('o');healthy=$true;checks=$healthChecks;
       observationSeconds=([DateTime]::UtcNow-$healthySince).TotalSeconds;actualRollback=$false;health=$health});exit 0
     }
    }catch{$healthySince=$null;Log ('health pending '+$_)}
   }
   if([DateTime]::UtcNow -ge [DateTime]::Parse($armed.recoverAfterUtc).ToUniversalTime()){Recover 'Armed deadline without sustained health';exit 0}
  }elseif([DateTime]::UtcNow -ge $idleUntil){Save 'guard-idle.json' ([ordered]@{nonce=$ready.nonce;at=[DateTime]::UtcNow.ToString('o');armed=$false;mutations=$false});exit 0}
  Start-Sleep -Seconds 2
 }
}catch{
 Log ('ERROR '+$_);Save 'guard-error.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message;pid=$PID;armed=[bool]$armed})
 if($armed -and !(Test-Path -LiteralPath (Join-Path $evidence 'recovery-started.json'))){try{Recover 'Unexpected armed guard failure'}catch{Save 'recovery-error.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message})}}
 exit 1
}
