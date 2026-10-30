. (Join-Path $PSScriptRoot 'Common.ps1')
$armed=$false
try {
 $ready=ReadRecord 'guard-ready.json';$heartbeat=ReadRecord 'guard-heartbeat.json';$request=ReadRecord 'guard-start-request.json'
 $guard=Get-Process -Id $ready.context.pid -ErrorAction Stop
 if(!$ready.passed -or $ready.nonce -ne $request.nonce -or $heartbeat.nonce -ne $ready.nonce -or
    $heartbeat.pid -ne $ready.context.pid -or $heartbeat.createdUtc -ne $ready.context.createdUtc -or
    $guard.StartTime.ToUniversalTime().ToString('o') -ne $ready.context.createdUtc -or
    ([DateTime]::UtcNow-[DateTime]::Parse($heartbeat.at).ToUniversalTime()).TotalSeconds -gt 10 -or
    ([DateTime]::UtcNow-[DateTime]::Parse($ready.at).ToUniversalTime()).TotalMinutes -gt 10 -or
    (HashOf (Join-Path $PSScriptRoot 'plan.json')) -ne $ready.planHash){throw 'Fresh independent ready/heartbeat identity not verified'}
 if(Get-Process -Id $request.starterPid -ErrorAction SilentlyContinue){throw 'Short starter has not exited (or PID reused); verify before deploying'}
 With-DeviceLock {Assert-Backups;Assert-Device;Assert-PackageSet (PhonePackages) $plan.oldPackages;Assert-Receiver|Out-Null;if((LayoutHash) -ne $plan.layoutHash){throw 'Layout changed'}}
 Save 'deployment-armed.json' ([ordered]@{nonce=$ready.nonce;guardPid=$guard.Id;guardCreatedUtc=$ready.context.createdUtc;planHash=$ready.planHash;
  at=[DateTime]::UtcNow.ToString('o');recoverAfterUtc=[DateTime]::UtcNow.AddSeconds(240).ToString('o')})
 $armed=$true;$until=[DateTime]::UtcNow.AddSeconds(15);$confirmed=$false
 while([DateTime]::UtcNow -lt $until){
  if(Test-Path -LiteralPath (Join-Path $evidence 'guard-armed.json')){
   $ack=ReadRecord 'guard-armed.json'
   if($ack.nonce -eq $ready.nonce -and $ack.pid -eq $guard.Id -and $ack.createdUtc -eq $ready.context.createdUtc){$confirmed=$true;break}
  };Start-Sleep -Milliseconds 300
 }
 if(!$confirmed){throw 'Same guard did not confirm armed; installation refused'}
 With-DeviceLock {
  Assert-Device;Assert-Backups
  Save 'installation-started.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');nonce=$ready.nonce;candidateHash=(HashOf $plan.candidate);guardConfirmation=$ack})
  $result=Adb ('install -r "'+$plan.candidate+'"') 45000
  Save 'installation-result.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');result=$result})
  if($result -notmatch 'Success'){throw 'Overlay install not successful'}
  $start=Adb 'shell am start -S -n com.rightpad.capture/.MainActivity' 20000
  Save 'activity-start.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');result=$start})
 }
 $until=[DateTime]::UtcNow.AddSeconds(60);$s=$null
 while([DateTime]::UtcNow -lt $until){try{$s=With-DeviceLock {Health};break}catch{Log ('deploy health pending: '+$_);Start-Sleep -Seconds 2}}
 if(!$s){throw 'Installation/start/reconnect/layout health check failed'}
 Save 'deployment-published.json' ([ordered]@{nonce=$ready.nonce;at=[DateTime]::UtcNow.ToString('o');snapshot=$s;candidateHash=$plan.candidateHash;layoutHash=$plan.layoutHash})
 'PASS candidate installed, foreground, natural new sender Connected and layout preserved; independent observation pending'
}catch{
 Save 'deployment-error.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');armed=$armed;error=$_.Exception.Message})
 if($armed){Save 'deployment-failed.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');reason=$_.Exception.Message})}
 throw
}
