. (Join-Path $PSScriptRoot 'Common.ps1')
$armed=$false
try {
 $ready=ReadRecord 'guard-ready.json';$heartbeat=ReadRecord 'guard-heartbeat.json';$request=ReadRecord 'guard-start-request.json'
 $guard=Get-Process -Id $ready.context.pid -ErrorAction Stop
 if(!$ready.passed -or $ready.nonce -ne $request.nonce -or $heartbeat.nonce -ne $ready.nonce -or
    $heartbeat.pid -ne $guard.Id -or $heartbeat.createdUtc -ne $ready.context.createdUtc -or
    $guard.StartTime.ToUniversalTime().ToString('o') -ne $ready.context.createdUtc -or
    ([DateTime]::UtcNow-[DateTime]::Parse($heartbeat.at).ToUniversalTime()).TotalSeconds -gt 10 -or
    (HashOf $planPath) -ne $ready.planHash){throw 'Live independent guard identity/heartbeat not verified'}
 $starter=Get-Process -Id $request.starterPid -ErrorAction SilentlyContinue
 if($starter -and $starter.StartTime.ToUniversalTime().ToString('o') -eq $request.starterCreatedUtc){throw 'Short starter still alive; independent lifetime not demonstrated'}
 With-Lock {Assert-Backup;Assert-Device;Assert-Formal $true;Assert-PackageSet (PhonePackages) $plan.oldPackages;Assert-Identity $plan.oldReceiver|Out-Null;if((LayoutHash) -ne $plan.layoutHash){throw 'Layout changed'}}
 Save 'deployment-armed.json' ([ordered]@{nonce=$ready.nonce;guardPid=$guard.Id;guardCreatedUtc=$ready.context.createdUtc;planHash=$ready.planHash;
  at=[DateTime]::UtcNow.ToString('o');recoverAfterUtc=[DateTime]::UtcNow.AddSeconds(420).ToString('o')})
 $armed=$true;$until=[DateTime]::UtcNow.AddSeconds(15);$confirmed=$false
 while([DateTime]::UtcNow -lt $until){
  if(Test-Path -LiteralPath (Join-Path $evidence 'guard-armed.json')){
   $ack=ReadRecord 'guard-armed.json'
   if($ack.nonce -eq $ready.nonce -and $ack.pid -eq $guard.Id -and $ack.createdUtc -eq $ready.context.createdUtc){$confirmed=$true;break}
  };Start-Sleep -Milliseconds 250
 }
 if(!$confirmed){throw 'Same guard armed acknowledgement missing: Stop/install refused'}
 $new=With-Lock {
  Assert-Backup;Assert-Device;Assert-Formal $true;Assert-Identity $plan.oldReceiver|Out-Null
  Save 'windows-update-started.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');nonce=$ready.nonce;guardConfirmation=$ack})
  Stop-Receiver $plan.oldReceiver
  foreach($file in $plan.newWindows){Copy-Item -LiteralPath $file.path -Destination (Join-Path $plan.formal $file.name) -Force}
  Assert-Formal
  Start-Receiver 'candidate'
 } 1000
 # This is the final Receiver. Never restart it during Android install/reconnect.
 $initialSnapshot=$null;$until=[DateTime]::UtcNow.AddSeconds(30)
 while([DateTime]::UtcNow -lt $until){$s=Snapshot $new.pid;if($s -and $s.runtimeState -eq 'Running' -and !$s.lastError){$initialSnapshot=$s;break};Start-Sleep -Milliseconds 500}
 if(!$initialSnapshot){throw 'Final Receiver runtime not ready'}
 With-Lock {
  Assert-Identity $new|Out-Null;Assert-Backup;Assert-Signatures;Assert-Device;Assert-PackageSet (PhonePackages) $plan.oldPackages
  if((LayoutHash) -ne $plan.layoutHash){throw 'Layout changed before APK install'}
  Save 'android-update-started.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');nonce=$ready.nonce;receiver=$new;runtimeRunId=$initialSnapshot.runtimeRunId})
  $installed=Adb ('install -r "'+$plan.newPackages[0].path+'"') 45000
  Save 'android-install.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');result=$installed})
  if($installed -notmatch 'Success'){throw 'APK overlay install failed'}
  if((LayoutHash) -ne $plan.layoutHash){throw 'Layout bytes changed by install'}
  $start=Adb 'shell am start -S -n com.rightpad.capture/.MainActivity' 20000
  Save 'android-start.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');result=$start})
 } 1000
 $until=[DateTime]::UtcNow.AddSeconds(60);$health=$null
 while([DateTime]::UtcNow -lt $until){try{$health=With-Lock {Health $new};break}catch{Log ('natural reconnect pending '+$_);Start-Sleep -Seconds 2}}
 if(!$health -or $health.runtimeRunId -ne $initialSnapshot.runtimeRunId){throw 'Same Receiver natural reconnect not established'}
 Save 'deployment-published.json' ([ordered]@{nonce=$ready.nonce;at=[DateTime]::UtcNow.ToString('o');receiver=$new;runtimeRunId=$health.runtimeRunId;health=$health})
 'PASS pair updated and naturally reconnected; same final Receiver remains running; independent sustained health observation pending'
}catch{
 Save 'deployment-error.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');armed=$armed;error=$_.Exception.Message})
 if($armed){Save 'deployment-failed.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');reason=$_.Exception.Message})}
 throw
}
