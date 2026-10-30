. (Join-Path $PSScriptRoot 'Common.ps1')
$service=New-Object -ComObject Schedule.Service;$service.Connect();$folder=$service.GetFolder('\')
$existing=$null;try{$existing=$folder.GetTask($plan.guardTask)}catch{if($_.Exception.HResult -ne -2147024894){throw}}
if($existing){throw 'Fresh guard task already exists; refusing replacement'}
$ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$definition=$service.NewTask(0);$definition.RegistrationInfo.Description=$plan.task+' Android-only independent recovery protection'
$definition.Principal.UserId=$plan.user;$definition.Principal.LogonType=3;$definition.Principal.RunLevel=0
$definition.Settings.ExecutionTimeLimit='PT30M';$definition.Settings.DisallowStartIfOnBatteries=$false;$definition.Settings.StopIfGoingOnBatteries=$false
$definition.Settings.MultipleInstances=2;$definition.Settings.Hidden=$true;$definition.Settings.AllowDemandStart=$true
$action=$definition.Actions.Create(0);$action.Path=$ps;$action.Arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'Guard.ps1')+'"';$action.WorkingDirectory=$PSScriptRoot
$null=$folder.RegisterTaskDefinition($plan.guardTask,$definition,2,$plan.user,$null,3)
$request=[ordered]@{task=$plan.task;nonce=[Guid]::NewGuid().ToString('N');at=[DateTime]::UtcNow.ToString('o');starterPid=$PID;starterCreatedUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o')}
Save 'guard-start-request.json' $request
$null=$folder.GetTask($plan.guardTask).Run($null)
Save 'guard-starter.json' $request
'PASS short starter exits; guard runs under Task Scheduler'
