
# Reuse the reviewed protection/transition flow with current-round health checks.
# Select the newest matching snapshot across both rolling files, not file order.
function Get-FlightSnapshot([int]$receiverPid) {
 $found=$null
 foreach($name in @('flight-recorder.log','flight-recorder.previous.log')){
  $path=Join-Path $env:LOCALAPPDATA ('rightpad\diagnostics\'+$name)
  if(!(Test-Path -LiteralPath $path)){continue}
  foreach($line in @(Get-Content -LiteralPath $path -Tail 300 -ErrorAction SilentlyContinue)){
   try{$r=$line|ConvertFrom-Json;if($r.type -eq 'snapshot' -and [int]$r.receiverPid -eq $receiverPid -and
      (!$found -or [DateTimeOffset]::Parse($r.wallTime) -gt [DateTimeOffset]::Parse($found.wallTime))){$found=$r}}catch{}
  }
 }
 $found
}
function Invoke-Preflight {
 $result=Invoke-BasePreflight
 # Exercise actual restored-health code in the real independent Limited task.
 $health=Test-RestoredHealth
 if(!$health){throw 'Full independent baseline health failed'}
 $snapshot=Get-FlightSnapshot $result.live.Pid
 if(!$snapshot -or $snapshot.connectionState -ne 'Connected' -or $snapshot.lastHeartbeatAgeMs -ge 2000 -or
    !$snapshot.gamepadAvailable -or $snapshot.gamepadFailures -ne 0){throw 'Baseline phone/gamepad health failed'}
 $result['fullHealth']=$health
 $result['baselineSnapshot']=$snapshot
 Save 'independent-preflight.json' $result
 $result
}
function Test-PublishedHealth($release) {
 $health=Test-BasePublishedHealth $release
 $snapshot=if($health){Get-FlightSnapshot $health.pid}else{$null}
 if(!$health -or !$snapshot.gamepadAvailable -or $snapshot.gamepadFailures -ne 0){$script:healthySince=$null;return $null}
 if(!$script:healthySince){$script:healthySince=[DateTime]::UtcNow;$script:healthChecks=0}
 $script:healthChecks++
 $seconds=([DateTime]::UtcNow-$script:healthySince).TotalSeconds
 Save 'guard-health.json' ([ordered]@{at=[DateTime]::UtcNow.ToString('o');observationSeconds=$seconds;checks=$script:healthChecks;snapshot=$snapshot})
 if($seconds -lt 60){return $null}
 $health
}
