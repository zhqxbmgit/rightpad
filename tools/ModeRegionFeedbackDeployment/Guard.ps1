. (Join-Path $PSScriptRoot 'Common.ps1')
function Recover([string]$reason){
 Save 'recovery-started.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');reason=$reason;pid=$PID})
 # Only Android is restored. Receiver and Windows configuration are never restarted or written.
 With-DeviceLock {
  Assert-Backups;Assert-Device
  $arguments=if(@($plan.oldPackages).Count -eq 1){'install -r "'+$plan.oldPackages[0].path+'"'}else{
   'install-multiple -r '+(($plan.oldPackages|ForEach-Object {'"'+$_.path+'"'}) -join ' ')
  }
  $installed=$false
  for($attempt=1;$attempt -le 3;$attempt++){
   try{$result=Adb $arguments 45000;Log ('restore install: '+$result);if($result -notmatch 'Success'){throw 'Restore install not successful'};$installed=$true;break}
   catch{Log ('restore attempt '+$attempt+': '+$_);if($attempt -eq 3){throw};Start-Sleep -Seconds 2}
  }
  if(!$installed){throw 'Old APK set not installed'}
  Assert-PackageSet (PhonePackages) $plan.oldPackages
  $currentLayout=$null;try{$currentLayout=LayoutHash}catch{Log ('layout needs restoration: '+$_)}
  if($currentLayout -ne $plan.layoutHash){
   $null=Adb 'shell "run-as com.rightpad.capture sh -c ''cat > files/screen-controls.properties''"' 15000 '' ([IO.File]::ReadAllBytes($plan.layout))
  }
  if((LayoutHash) -ne $plan.layoutHash){throw 'Restored layout mismatch'}
  $null=Adb 'shell am start -S -n com.rightpad.capture/.MainActivity' 20000
 } 60000
 $until=[DateTime]::UtcNow.AddSeconds(60);$healthy=$null
 while([DateTime]::UtcNow -lt $until){
  try{$healthy=With-DeviceLock {Health $true};break}catch{Log ('restore health pending: '+$_);Start-Sleep -Seconds 2}
 }
 if(!$healthy){throw 'Android restoration health verification failed'}
 Save 'recovery-applied.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');reason=$reason;actualRollback=$true;healthy=$true;snapshot=$healthy;receiverUntouched=$true})
}
try {
 $context=Guard-Context
 $request=ReadRecord 'guard-start-request.json'
 if($request.task -ne $plan.task -or [DateTime]::Parse($request.at).ToUniversalTime() -gt [DateTime]::Parse($context.createdUtc).ToUniversalTime()){throw 'Guard launch nonce/request mismatch'}
 $checks=With-DeviceLock {
  Assert-Backups;Assert-Device;Assert-PackageSet (PhonePackages) $plan.oldPackages
  if((LayoutHash) -ne $plan.layoutHash){throw 'Layout changed before protection'}
  # Run the same complete health path used after install/recovery, in the real independent task.
  $s=Health $true
  if($s.connectionState -ne 'Connected'){throw 'Receiver not Connected before update'}
  # Non-destructive read checks the exact binary layout input and restoration command; no phone write.
  $stream=[IO.File]::OpenRead($plan.layout);try{if($stream.Length -le 0){throw 'Empty layout backup'}}finally{$stream.Dispose()}
  $null=Adb 'shell run-as com.rightpad.capture id'
  $script=Get-Content -LiteralPath $PSCommandPath -Raw
  foreach($required in @('install-multiple -r','install -r','cat > files/screen-controls.properties','am start -S -n com.rightpad.capture/.MainActivity')){
   if(!$script.Contains($required)){throw 'Recovery command missing'}
  }
  $cases=@(@($false,$false,$false,'idle'),@($true,$false,$false,'wait'),@($true,$true,$false,'release'),@($true,$false,$true,'recover'))
  foreach($case in $cases){$decision=if(!$case[0]){'idle'}elseif($case[1]){'release'}elseif($case[2]){'recover'}else{'wait'};if($decision -ne $case[3]){throw 'Decision test failed'}}
  [ordered]@{passed=$true;deviceIdentity=$true;backupFiles=@($plan.files).Count;fullApkSet=@($plan.oldPackages).Count;layoutRead=$true;
   recoveryCommandsReviewed=$true;decisionCases=$cases.Count;mutexAcquiredReleased=$true;phoneMutations=$false;snapshot=$s}
 }
 Save 'independent-preflight.json' ([ordered]@{context=$context;checks=$checks;at=[DateTime]::UtcNow.ToString('o')})
 $ready=[ordered]@{nonce=$request.nonce;context=$context;at=[DateTime]::UtcNow.ToString('o');passed=$true;planHash=(HashOf (Join-Path $PSScriptRoot 'plan.json'))}
 Save 'guard-ready.json' $ready
 $idleUntil=[DateTime]::UtcNow.AddMinutes(15);$armed=$null;$healthySince=$null;$healthChecks=0
 while($true){
  Save 'guard-heartbeat.json' ([ordered]@{nonce=$ready.nonce;pid=$PID;createdUtc=$context.createdUtc;at=[DateTime]::UtcNow.ToString('o')})
  if(!$armed -and (Test-Path -LiteralPath (Join-Path $evidence 'deployment-armed.json'))){
   $a=ReadRecord 'deployment-armed.json'
   if($a.nonce -ne $ready.nonce -or $a.guardPid -ne $PID -or $a.guardCreatedUtc -ne $context.createdUtc -or
      $a.planHash -ne $ready.planHash -or [DateTime]::Parse($a.at).ToUniversalTime() -lt [DateTime]::Parse($ready.at).ToUniversalTime()){
    throw 'Armed record does not reference this live guard'
   }
   $armed=$a
   Save 'guard-armed.json' ([ordered]@{nonce=$ready.nonce;pid=$PID;createdUtc=$context.createdUtc;at=[DateTime]::UtcNow.ToString('o');readyAt=$ready.at;recoverAfterUtc=$armed.recoverAfterUtc})
  }
  if($armed){
   if(Test-Path -LiteralPath (Join-Path $evidence 'deployment-failed.json')){Recover 'deployment failed';exit 0}
   if(Test-Path -LiteralPath (Join-Path $evidence 'deployment-published.json')){
    try {
     $published=ReadRecord 'deployment-published.json'
     if($published.nonce -ne $ready.nonce){throw 'Publication nonce mismatch'}
     $s=With-DeviceLock {Health}
     if(!$healthySince){$healthySince=[DateTime]::UtcNow};$healthChecks++
     Save 'guard-health.json' ([ordered]@{nonce=$ready.nonce;checks=$healthChecks;healthySince=$healthySince.ToString('o');at=[DateTime]::UtcNow.ToString('o');snapshot=$s})
     if(([DateTime]::UtcNow-$healthySince).TotalSeconds -ge 60){
      Save 'guard-released.json' ([ordered]@{nonce=$ready.nonce;context=$context;at=[DateTime]::UtcNow.ToString('o');healthy=$true;healthChecks=$healthChecks;
       observationSeconds=([DateTime]::UtcNow-$healthySince).TotalSeconds;actualRollback=$false;snapshot=$s})
      exit 0
     }
    }catch{$healthySince=$null;Log ('health pending: '+$_)}
   }
   if([DateTime]::UtcNow -ge [DateTime]::Parse($armed.recoverAfterUtc).ToUniversalTime()){Recover 'armed deadline without sustained health release';exit 0}
  }elseif([DateTime]::UtcNow -ge $idleUntil){Save 'guard-idle.json' ([ordered]@{nonce=$ready.nonce;at=[DateTime]::UtcNow.ToString('o');armed=$false;mutations=$false});exit 0}
  Start-Sleep -Seconds 2
 }
}catch{
 Log ('ERROR '+$_)
 Save 'guard-error.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message;pid=$PID;armed=[bool]$armed})
 # An unexpected post-arm error must still invoke Android recovery.
 if($armed -and !(Test-Path -LiteralPath (Join-Path $evidence 'recovery-started.json'))){try{Recover 'unexpected armed guard failure'}catch{Save 'recovery-error.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message})}}
 exit 1
}
