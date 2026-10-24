# Start the round's protection, then exit. The guard must continue independently.
$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'guard-plan.json') -Raw|ConvertFrom-Json
$evidence=$plan.evidence
$powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
& $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'RunGuardTask.ps1') -Mode Guard -DeadlineSeconds $plan.guardDeadlineSeconds -RegisterOnly | Out-File -LiteralPath (Join-Path $evidence 'guard-registration-continuity.txt')
if($LASTEXITCODE -ne 0){throw 'Guard registration failed'}
$service=New-Object -ComObject Schedule.Service;$service.Connect();$task=$service.GetFolder('\').GetTask([string]$plan.guardTask)
if($task.State -eq 4){throw 'Guard already running'}
$now=[DateTime]::UtcNow
$request=[ordered]@{task=$plan.task;at=$now.ToString('o');nonce=[Guid]::NewGuid().ToString('N');starterPid=$PID;starterCreatedUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')}
$temp=Join-Path $evidence 'guard-start-request.tmp'
$request|ConvertTo-Json|Set-Content -LiteralPath $temp -Encoding UTF8
Move-Item -LiteralPath $temp -Destination (Join-Path $evidence 'guard-start-request.json') -Force
$null=$task.Run($null)
$request|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'guard-starter.json') -Encoding UTF8
'PASS starter exits now; no Receiver or configuration mutation'
