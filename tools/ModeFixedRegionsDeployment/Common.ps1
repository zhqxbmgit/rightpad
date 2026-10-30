$ErrorActionPreference='Stop'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$planPath=Join-Path $PSScriptRoot 'plan.json'
$plan=if(Test-Path -LiteralPath $planPath){Get-Content -LiteralPath $planPath -Raw -Encoding UTF8|ConvertFrom-Json}else{$null}
if($plan -and $plan.task -ne '20261029-mode-fixed-regions-r2'){throw 'Wrong fixed-region deployment plan'}
if($plan){$evidence=$plan.evidence}
function HashOf([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Save([string]$name,$value){
 $path=Join-Path $evidence $name;$temp=$path+'.'+$PID+'.tmp'
 $value|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $temp -Encoding UTF8
 Move-Item -LiteralPath $temp -Destination $path -Force
}
function ReadRecord([string]$name){Get-Content -LiteralPath (Join-Path $evidence $name) -Raw -Encoding UTF8|ConvertFrom-Json}
function Log([string]$message){Add-Content -LiteralPath (Join-Path $evidence ('operations-'+$PID+'.log')) -Value ([DateTime]::UtcNow.ToString('o')+' '+$message)}
function Adb([string]$arguments,[int]$timeout=15000,[string]$binaryOutput='',[byte[]]$inputBytes=$null){
 $si=New-Object Diagnostics.ProcessStartInfo
 $si.FileName=$plan.adb;$si.Arguments='-s '+$plan.device+' '+$arguments
 $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true
 $si.RedirectStandardInput=($null -ne $inputBytes)
 $p=[Diagnostics.Process]::Start($si);$err=$p.StandardError.ReadToEndAsync();$file=$null
 try {
  if($binaryOutput){$file=[IO.File]::Create($binaryOutput);$read=$p.StandardOutput.BaseStream.CopyToAsync($file)}else{$read=$p.StandardOutput.ReadToEndAsync()}
  if($null -ne $inputBytes){$p.StandardInput.BaseStream.Write($inputBytes,0,$inputBytes.Length);$p.StandardInput.Close()}
  if(!$p.WaitForExit($timeout)){$p.Kill();$p.WaitForExit();throw ('ADB timeout: '+$arguments)}
  $out=$read.GetAwaiter().GetResult();$errorText=$err.GetAwaiter().GetResult()
  if($p.ExitCode -ne 0){throw ('ADB failed: '+$arguments+' '+$errorText)}
  if(!$binaryOutput){return ([string]$out).Trim()}
 }finally{if($file){$file.Dispose()};$p.Dispose()}
}
function Assert-Device {
 if((Adb 'get-state') -ne 'device' -or (Adb 'shell getprop ro.serialno') -ne $plan.hardwareSerial -or
    (Adb 'shell getprop ro.product.model') -ne $plan.model){throw 'Device identity/reachability mismatch'}
}
function PhonePackages {
 $paths=@((Adb 'shell pm path com.rightpad.capture') -split '\r?\n'|Where-Object {$_ -like 'package:*'}|ForEach-Object {$_.Substring(8).Trim()})
 if(!$paths.Count){throw 'Installed APK set missing'}
 @(foreach($path in $paths){[pscustomobject]@{name=[IO.Path]::GetFileName($path);devicePath=$path;sha256=((Adb ('shell sha256sum "'+$path+'"'))-split '\s+')[0].ToUpperInvariant()}})
}
function Assert-PackageSet($actual,$expected){
 if(@($actual).Count -ne @($expected).Count){throw 'APK split count mismatch'}
 foreach($entry in $expected){$match=@($actual|Where-Object {$_.name -eq $entry.name});if($match.Count -ne 1 -or $match[0].sha256 -ne $entry.sha256){throw ('APK mismatch: '+$entry.name)}}
}
function LayoutHash {((Adb 'shell run-as com.rightpad.capture sha256sum files/screen-controls.properties')-split '\s+')[0].ToUpperInvariant()}
function Assert-Files($files){
 foreach($entry in $files){if(!(Test-Path -LiteralPath $entry.path) -or (Get-Item -LiteralPath $entry.path).Length -ne $entry.bytes -or (HashOf $entry.path) -ne $entry.sha256){throw ('File/hash mismatch: '+$entry.path)}}
}
function Assert-Backup {
 Assert-Files $plan.backupFiles;Assert-Files $plan.dependencies;Assert-Files $plan.candidateFiles
 if((HashOf $plan.layoutBackup) -ne $plan.layoutHash){throw 'Layout backup mismatch'}
}
function Certificate([string]$file){
 $output=(& $plan.java -jar $plan.apksignerJar verify --verbose --print-certs $file 2>&1|Out-String)
 if($LASTEXITCODE -ne 0){throw ('APK signature verification failed: '+$file+' '+$output)}
 $match=[regex]::Match($output,'(?m)^(?:Signer #1|V2 Signer):? certificate SHA-256 digest: ([0-9a-fA-F]{64})\s*$')
 if($output -notmatch 'Number of signers: 1\s'){throw 'Unexpected APK signer count'}
 if(!$match.Success){throw 'APK signing certificate missing'}
 [pscustomobject]@{digest=$match.Groups[1].Value.ToUpperInvariant();report=$output}
}
function Assert-Signatures {
 foreach($entry in @($plan.oldPackages)+@($plan.newPackages)){
  if((Certificate $entry.path).digest -ne $plan.certificate){throw 'APK signing certificate differs from installed baseline'}
 }
}
function Assert-Formal([bool]$old=$false){
 $files=if($old){$plan.oldWindows}else{$plan.newWindows}
 foreach($entry in $files){$path=Join-Path $plan.formal $entry.name;if((HashOf $path) -ne $entry.sha256){throw ('Formal file mismatch: '+$path)}}
 if((HashOf $plan.settings) -ne $plan.settingsHash){throw 'Formal settings changed'}
}
function Import-DesktopHelper {
 if(Get-Command Get-DesktopIdentity -ErrorAction SilentlyContinue){return}
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($plan.launcher,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'Launcher parse failed'}
 $helper=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-DesktopIdentity'},$true)
 if(!$helper){throw 'Approved desktop identity helper missing'}
 . ([scriptblock]::Create($helper.Extent.Text.Replace('$identity.Name','$script:identity.Name')))
 # Export into this script scope after the import function returns.
 Set-Item Function:script:Get-DesktopIdentity (Get-Item Function:Get-DesktopIdentity).ScriptBlock
}
function Find-Receiver {
 $all=@(Get-CimInstance Win32_Process -Filter "Name='Rightpad.Receiver.exe'"|Where-Object {$_.ExecutablePath -eq (Join-Path $plan.formal 'Rightpad.Receiver.exe')})
 if($all.Count -gt 1){throw 'Multiple formal Receiver instances'}
 if(!$all.Count){return $null}
 $p=Get-Process -Id $all[0].ProcessId
 [pscustomobject]@{pid=$p.Id;createdUtc=$p.StartTime.ToUniversalTime().ToString('o');path=$all[0].ExecutablePath;session=$p.SessionId;commandLine=$all[0].CommandLine}
}
function Assert-Context($receiver){
 Import-DesktopHelper
 $context=Get-DesktopIdentity (Get-Process -Id $receiver.pid)
 if($context.User -ne $plan.user -or $context.SessionId -ne $plan.session -or $context.SessionId -eq 0 -or
    $context.SessionId -notin $context.ExplorerSessionIds -or $context.IntegritySid -ne 'S-1-16-8192' -or $context.Desktop -ne 'Default'){throw 'Receiver interactive context mismatch'}
 $context
}
function Assert-Identity($expected){
 $actual=Find-Receiver
 if(!$actual -or $actual.pid -ne $expected.pid -or $actual.createdUtc -ne ([DateTime]$expected.createdUtc).ToUniversalTime().ToString('o')){throw 'Receiver instance identity mismatch'}
 $owners=@(Get-NetUDPEndpoint -LocalPort 50000 -ErrorAction SilentlyContinue|Select-Object -ExpandProperty OwningProcess -Unique)
 if($owners.Count -ne 1 -or $owners[0] -ne $actual.pid){throw 'Receiver UDP 50000 ownership mismatch'}
 Assert-Context $actual|Out-Null
 $actual
}
function Snapshot([int]$receiverPid){
 $latest=$null
 foreach($line in @(Get-Content -LiteralPath $plan.flightLog -Tail 250 -Encoding UTF8)){
  try{$r=$line|ConvertFrom-Json;if($r.type -eq 'snapshot' -and $r.receiverPid -eq $receiverPid){$latest=$r}}catch{}
 }
 $latest
}
function Health($expectedReceiver,[bool]$old=$false){
 Assert-Device;Assert-Formal $old
 $live=Assert-Identity $expectedReceiver
 Assert-PackageSet (PhonePackages) $(if($old){$plan.oldPackages}else{$plan.newPackages})
 if((LayoutHash) -ne $plan.layoutHash){throw 'Phone layout raw bytes changed'}
 $activity=Adb 'shell dumpsys activity activities'
 if($activity -notmatch '(topResumedActivity|mResumedActivity|ResumedActivity:).*com.rightpad.capture/\.MainActivity'){throw 'MainActivity not foreground'}
 $appPid=Adb 'shell pidof com.rightpad.capture';if($appPid -notmatch '^\d+$'){throw 'Android process missing'}
 $s=Snapshot $live.pid
 if(!$s -or ([DateTimeOffset]::Now-[DateTimeOffset]::Parse($s.wallTime)).TotalSeconds -ge 8 -or
    $s.runtimeState -ne 'Running' -or $s.connectionState -ne 'Connected' -or $s.lastError -or $s.lastHeartbeatAgeMs -ge 2000 -or
    $s.mouseOutputFailures -ne 0 -or $s.gamepadFailures -ne 0 -or !$s.gamepadAvailable){throw 'Receiver/Sender health not established'}
 if(!$old -and $s.senderRunId -eq $plan.baseline.senderRunId){throw 'New Android Sender run not admitted'}
 $phoneLog=Adb ('logcat -d --pid='+$appPid+' -t 350 -v threadtime')
 if($phoneLog -match 'queue_overflow|sender_open_failed|heartbeat_send_failed|\s(send_failed|mode_send_failed|gamepad_send_failed)'){throw 'Android Sender failure/overflow observed'}
 if(!$old -and $phoneLog -notmatch ('(?i)senderRunId=0*'+([string]$s.senderRunId).TrimStart('0'))){throw 'Android and Receiver Sender run evidence do not match'}
 [pscustomobject]@{receiver=$live;runtimeRunId=$s.runtimeRunId;senderRunId=$s.senderRunId;appPid=$appPid;snapshot=$s}
}
function With-Lock([scriptblock]$action,[int]$timeout=1000){
 $mutex=New-Object Threading.Mutex($false,[string]$plan.mutex);$held=$false
 try{try{$held=$mutex.WaitOne($timeout)}catch [Threading.AbandonedMutexException]{$held=$true};if(!$held){throw 'Deployment/recovery lock busy'};& $action}
 finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
function Stop-Receiver($expected){
 $actual=Find-Receiver;if(!$actual){return}
 if($expected -and ($actual.pid -ne $expected.pid -or $actual.createdUtc -ne ([DateTime]$expected.createdUtc).ToUniversalTime().ToString('o'))){throw 'Refuse to stop changed Receiver'}
 Assert-Context $actual|Out-Null
 $p=Get-Process -Id $actual.pid
 # Explicit authorized Exit closes settings/devices; the ordinary window close hides to tray.
 # The approved launcher owns normal tracked instances. The baseline may have a stale record.
 $null=$p.CloseMainWindow();$null=$p.WaitForExit(1500)
 if(!$p.HasExited){
  $again=Find-Receiver;if(!$again -or $again.pid -ne $actual.pid -or $again.createdUtc -ne $actual.createdUtc){throw 'Identity changed while stopping'}
  $p.Kill();if(!$p.WaitForExit(8000)){throw 'Receiver did not exit'}
 }
 & $plan.launcher -Mode Stop|Out-File -LiteralPath (Join-Path $evidence ('launcher-stop-'+$PID+'.txt')) -Encoding UTF8
 if(Find-Receiver){throw 'Receiver remains'}
 if(@(Get-NetUDPEndpoint -LocalPort 50000,50001 -ErrorAction SilentlyContinue).Count){throw 'Input/discovery ports not released'}
 Save ('receiver-stopped-'+$PID+'.json') ([ordered]@{old=$actual;at=[DateTime]::UtcNow.ToString('o');portsReleased=$true})
}
function Start-Receiver([string]$reason){
 if(Find-Receiver){throw 'Refuse duplicate Receiver start'}
 & $plan.launcher -Mode Start|Out-File -LiteralPath (Join-Path $evidence ('launcher-start-'+$reason+'.txt')) -Encoding UTF8
 $live=Find-Receiver;if(!$live){throw 'Approved launcher did not start Receiver'}
 Assert-Identity $live|Out-Null
 Save ('receiver-started-'+$reason+'.json') ([ordered]@{receiver=$live;context=(Assert-Context $live);at=[DateTime]::UtcNow.ToString('o')})
 $live
}
function Guard-Context {
 Import-DesktopHelper
 $self=Get-Process -Id $PID;$ctx=Get-DesktopIdentity $self
 $cim=Get-CimInstance Win32_Process -Filter "ProcessId=$PID";$scheduler=Get-CimInstance Win32_Service -Filter "Name='Schedule'"
 $context=[ordered]@{pid=$PID;createdUtc=$self.StartTime.ToUniversalTime().ToString('o');parent=[int]$cim.ParentProcessId;scheduler=[int]$scheduler.ProcessId;
  user=$ctx.User;session=$ctx.SessionId;desktop=$ctx.Desktop;integrity=$ctx.IntegritySid;jobFlags=[RightpadDevToken]::CurrentJobFlags()}
 if($context.parent -ne $context.scheduler -or $context.user -ne $plan.user -or $context.session -ne $plan.session -or
    $context.desktop -ne 'Default' -or $context.integrity -ne 'S-1-16-8192' -or ($context.jobFlags -band 0x2000)){throw 'Independent Task Scheduler context not established'}
 $context
}
