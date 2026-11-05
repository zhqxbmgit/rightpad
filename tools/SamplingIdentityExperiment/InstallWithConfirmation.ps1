param([ValidateSet('control','pubg','dfm')][string]$Variant)
$ErrorActionPreference='Stop'
$base='C:\rightpad-artifacts\test-results\20261102-sampling-identity-r1'
. (Join-Path $base 'recovery\Common.ps1')
$ready=ReadRecord 'guard-ready.json';$armed=ReadRecord 'guard-armed.json';$heartbeat=ReadRecord 'guard-heartbeat.json'
if($ready.nonce -ne $armed.nonce -or $heartbeat.nonce -ne $armed.nonce -or
   ([DateTime]::UtcNow-([DateTime]$heartbeat.at).ToUniversalTime()).TotalSeconds -gt 10 -or
   [DateTime]::UtcNow -ge ([DateTime]$armed.deadline).ToUniversalTime()) {throw 'Recovery guard not live/armed'}
Assert-Backups
Assert-Device
$apk=Join-Path $base ('candidate\'+$Variant+'.apk')
$out=Join-Path $evidence ($Variant+'-install-stdout.txt');$err=Join-Path $evidence ($Variant+'-install-stderr.txt')
$installer=Start-Process -FilePath $plan.adb -ArgumentList ('-s '+$plan.device+' install "'+$apk+'"') -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
$deadline=[DateTime]::UtcNow.AddSeconds(45);$last=''
try {
 while(!$installer.HasExited -and [DateTime]::UtcNow -lt $deadline){
  $null=Adb 'shell uiautomator dump /data/local/tmp/rightpad-install-confirmation.xml' 10000
  $xml=Adb 'shell cat /data/local/tmp/rightpad-install-confirmation.xml'
  $ui=[xml]$xml
  $nodes=@($ui.SelectNodes('//node'))
  $labels=@($nodes|Where-Object {$_.text -or $_.'content-desc'}|ForEach-Object {@{text=$_.text;description=$_.'content-desc';id=$_.'resource-id';package=$_.package;enabled=$_.enabled;bounds=$_.bounds}})|ConvertTo-Json -Depth 4 -Compress
  if($labels -ne $last){Write-Output $labels;Add-Content (Join-Path $evidence ($Variant+'-install-ui.jsonl')) $labels -Encoding UTF8;$last=$labels}
  $context=($xml -match 'rightpad Sample|com\.rightpad\.sampling\.control|com\.tencent\.tmgp\.(pubgmhd|dfm)|USB.{0,15}安装|安装.{0,15}USB')
  if($context){
   $allow=@($nodes|Where-Object {$_.package -match '^com\.(miui\.(securitycenter|packageinstaller)|android\.packageinstaller|google\.android\.packageinstaller)$' -and $_.enabled -eq 'true' -and $_.text -match '^(安装|允许|继续安装|仍要安装|允许本次安装)$'})
   if($allow.Count -eq 1 -and $allow[0].bounds -match '^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$'){
    $x=[int]( ([int]$Matches[1]+[int]$Matches[3])/2 );$y=[int]( ([int]$Matches[2]+[int]$Matches[4])/2 )
    Write-Output ('Allowing experiment installation: '+$allow[0].text+' '+$allow[0].bounds)
    $null=Adb ('shell input tap '+$x+' '+$y)
   }
  }
  Start-Sleep -Milliseconds 250
 }
 if(!$installer.HasExited){$installer.Kill();throw 'Installation confirmation timeout'}
 $installer.WaitForExit()
 $stdout=Get-Content $out -Raw;$stderr=Get-Content $err -Raw
 Write-Output $stdout;Write-Output $stderr
 if($installer.ExitCode -ne 0 -or $stdout -notmatch 'Success'){throw 'Experiment installation did not succeed'}
} finally {if(!$installer.HasExited){$installer.Kill()};$installer.Dispose()}
