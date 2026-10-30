. (Join-Path $PSScriptRoot 'Common.ps1')
$taskName=[string]$plan.guardTask
$service=New-Object -ComObject Schedule.Service;$service.Connect();$folder=$service.GetFolder('\')
try{$existing=$folder.GetTask($taskName)}catch{if($_.Exception.HResult -ne -2147024894){throw};$existing=$null}
if($existing){throw 'This round guard task already exists; do not overwrite'}
$definition=$service.NewTask(0)
$definition.RegistrationInfo.Description='rightpad fixed Mode regions independent pair recovery '+$plan.task
$definition.Principal.UserId=$plan.user;$definition.Principal.LogonType=3;$definition.Principal.RunLevel=0
$definition.Settings.ExecutionTimeLimit='PT30M';$definition.Settings.DisallowStartIfOnBatteries=$false
$definition.Settings.StopIfGoingOnBatteries=$false;$definition.Settings.Enabled=$true;$definition.Settings.AllowDemandStart=$true
$definition.Settings.RestartCount=0;$definition.Settings.MultipleInstances=2;$definition.Settings.Hidden=$true
$action=$definition.Actions.Create(0);$action.Path=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action.Arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'Guard.ps1')+'"'
$action.WorkingDirectory=$PSScriptRoot
$nonce=[guid]::NewGuid().ToString('N')
Save 'guard-start-request.json' ([ordered]@{task=$plan.task;nonce=$nonce;starterPid=$PID;starterCreatedUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');at=[DateTime]::UtcNow.ToString('o')})
$task=$folder.RegisterTaskDefinition($taskName,$definition,2,$plan.user,$null,3)
$null=$task.Run($null)
[pscustomobject]@{task=$taskName;nonce=$nonce;starterPid=$PID;state=$task.State}
