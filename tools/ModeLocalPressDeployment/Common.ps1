$ErrorActionPreference='Stop'
$plan=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'plan.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($plan.task -ne '20261024-mode-local-press-r1'){throw 'Wrong round plan'}
$evidence=$plan.evidence
function HashOf([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Save([string]$name,$value){
 $path=Join-Path $evidence $name;$temp=$path+'.'+$PID+'.tmp'
 $value|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $temp -Encoding UTF8
 Move-Item -LiteralPath $temp -Destination $path -Force
}
function ReadRecord([string]$name){Get-Content -LiteralPath (Join-Path $evidence $name) -Raw -Encoding UTF8|ConvertFrom-Json}
function Log([string]$text){Add-Content -LiteralPath (Join-Path $evidence ('operations-'+$PID+'.log')) -Value ([DateTime]::UtcNow.ToString('o')+' '+$text)}
function Adb([string]$arguments,[int]$timeout=15000,[string]$binaryOutput='',[byte[]]$inputBytes=$null){
 $si=New-Object Diagnostics.ProcessStartInfo
 $si.FileName=$plan.adb;$si.Arguments='-s '+$plan.device+' '+$arguments
 $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true
 $si.RedirectStandardInput=($null -ne $inputBytes)
 $p=[Diagnostics.Process]::Start($si);$err=$p.StandardError.ReadToEndAsync();$file=$null
 try {
  if($binaryOutput){$file=[IO.File]::Create($binaryOutput);$read=$p.StandardOutput.BaseStream.CopyToAsync($file)}else{$read=$p.StandardOutput.ReadToEndAsync()}
  if($null -ne $inputBytes){$p.StandardInput.BaseStream.Write($inputBytes,0,$inputBytes.Length);$p.StandardInput.Close()}
  if(!$p.WaitForExit($timeout)){$p.Kill();$p.WaitForExit();throw ('ADB timed out: '+$arguments)}
  $out=$read.GetAwaiter().GetResult();$errorText=$err.GetAwaiter().GetResult()
  if($p.ExitCode -ne 0){throw ('ADB failed: '+$arguments+' '+$errorText)}
  if(!$binaryOutput){return ([string]$out).Trim()}
 } finally {if($file){$file.Dispose()};$p.Dispose()}
}
function Assert-Device {
 if((Adb 'get-state') -ne 'device' -or (Adb 'shell getprop ro.serialno') -ne $plan.hardwareSerial -or
    (Adb 'shell getprop ro.product.model') -ne $plan.model){throw 'ADB target identity/reachability mismatch'}
}
function PhonePackages {
 $paths=@((Adb 'shell pm path com.rightpad.capture') -split '\r?\n'|Where-Object {$_ -like 'package:*'}|ForEach-Object {$_.Substring(8).Trim()})
 if(!$paths.Count){throw 'No installed APKs'}
 @(foreach($path in $paths){[pscustomobject]@{name=[IO.Path]::GetFileName($path);devicePath=$path;sha256=((Adb ('shell sha256sum "'+$path+'"'))-split '\s+')[0].ToUpperInvariant()}})
}
function LayoutHash {((Adb 'shell run-as com.rightpad.capture sha256sum files/screen-controls.properties')-split '\s+')[0].ToUpperInvariant()}
function Assert-PackageSet($actual,$expected){
 if(@($actual).Count -ne @($expected).Count){throw 'Installed APK set count mismatch'}
 foreach($e in $expected){$a=@($actual|Where-Object {$_.name -eq $e.name});if($a.Count -ne 1 -or $a[0].sha256 -ne $e.sha256){throw ('APK mismatch: '+$e.name)}}
}
function Snapshot {
 $latest=$null
 foreach($line in @(Get-Content -LiteralPath $plan.flightLog -Tail 220 -Encoding UTF8)){
  try{$r=$line|ConvertFrom-Json;if($r.type -eq 'snapshot' -and $r.receiverPid -eq $plan.receiver.pid){$latest=$r}}catch{}
 }
 $latest
}
function Assert-Receiver {
 $p=Get-Process -Id $plan.receiver.pid -ErrorAction Stop
 if($p.StartTime.ToUniversalTime().ToString('o') -ne ([DateTime]$plan.receiver.createdUtc).ToUniversalTime().ToString('o') -or $p.Path -ne $plan.receiver.path){throw 'Receiver process identity changed'}
 foreach($e in $plan.receiverFiles){if((HashOf $e.path) -ne $e.sha256){throw ('Receiver file changed: '+$e.path)}}
 if((HashOf $plan.settingsPath) -ne $plan.settingsHash){throw 'Receiver settings changed'}
 $owners=@(Get-NetUDPEndpoint -LocalPort 50000 -ErrorAction Stop|Select-Object -ExpandProperty OwningProcess -Unique)
 if($owners.Count -ne 1 -or $owners[0] -ne $p.Id){throw 'Receiver UDP ownership changed'}
 $s=Snapshot
 if(!$s -or ([DateTimeOffset]::Now-[DateTimeOffset]::Parse($s.wallTime)).TotalSeconds -ge 15 -or
    $s.runtimeState -ne 'Running' -or $s.runtimeRunId -ne $plan.baseline.runtimeRunId -or $s.lastError -or
    $s.mouseOutputFailures -ne $plan.baseline.mouseOutputFailures -or $s.gamepadFailures -ne $plan.baseline.gamepadFailures){throw 'Receiver health/identity mismatch'}
 return $s
}
function Assert-Backups {
 foreach($e in $plan.files){if(!(Test-Path -LiteralPath $e.path) -or (Get-Item -LiteralPath $e.path).Length -ne $e.bytes -or (HashOf $e.path) -ne $e.sha256){throw ('Backup/dependency mismatch: '+$e.path)}}
 if((HashOf $plan.candidate) -ne $plan.candidateHash -or (HashOf $plan.layout) -ne $plan.layoutHash){throw 'Candidate/layout mismatch'}
 foreach($e in $plan.oldPackages){if((HashOf $e.path) -ne $e.sha256){throw 'Old APK mismatch'}}
}
function With-DeviceLock([scriptblock]$action,[int]$timeout=1000){
 $mutex=New-Object Threading.Mutex($false,[string]$plan.mutex);$held=$false
 try {
  try{$held=$mutex.WaitOne($timeout)}catch [Threading.AbandonedMutexException]{$held=$true}
  if(!$held){throw 'Device deployment/recovery mutex busy'}
  & $action
 }finally{if($held){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
function Health([bool]$old=$false){
 Assert-Device
 $s=Assert-Receiver
 if($old){Assert-PackageSet (PhonePackages) $plan.oldPackages}else{
  Assert-PackageSet (PhonePackages) @([pscustomobject]@{name='base.apk';sha256=$plan.candidateHash})
 }
 if((LayoutHash) -ne $plan.layoutHash){throw 'Layout changed'}
 $activity=Adb 'shell dumpsys activity activities'
 if($activity -notmatch '(topResumedActivity|mResumedActivity|ResumedActivity:).*com.rightpad.capture/\.MainActivity'){throw 'MainActivity not resumed'}
 if(!(Adb 'shell pidof com.rightpad.capture')){throw 'Android process missing'}
 if($s.connectionState -ne 'Connected' -or $s.lastHeartbeatAgeMs -gt 2000){throw 'Natural discovery/heartbeat not connected'}
 if(!$old -and $s.senderRunId -eq $plan.baseline.senderRunId){throw 'New Android sender run not admitted'}
 return $s
}
function Guard-Context {
 $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($plan.launcher,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'Launcher parse failed'}
 $helper=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-DesktopIdentity'},$true)
 if(!$helper){throw 'Missing approved context helper'}
 . ([scriptblock]::Create($helper.Extent.Text))
 $self=Get-Process -Id $PID;$ctx=Get-DesktopIdentity $self
 $cim=Get-CimInstance Win32_Process -Filter "ProcessId=$PID";$scheduler=Get-CimInstance Win32_Service -Filter "Name='Schedule'"
 $result=[ordered]@{pid=$PID;createdUtc=$self.StartTime.ToUniversalTime().ToString('o');parent=[int]$cim.ParentProcessId;scheduler=[int]$scheduler.ProcessId;
 user=$ctx.User;session=$ctx.SessionId;desktop=$ctx.Desktop;integrity=$ctx.IntegritySid;jobFlags=[RightpadDevToken]::CurrentJobFlags()}
 if($result.parent -ne $result.scheduler -or $result.user -ne $plan.user -or $result.session -ne $plan.session -or
    $result.desktop -ne 'Default' -or $result.integrity -ne 'S-1-16-8192' -or ($result.jobFlags -band 0x2000)){throw 'Guard independence/context verification failed'}
 $result
}
