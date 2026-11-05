param([string]$Task='20261102-sampling-identity-r1')
$ErrorActionPreference='Stop'
$root=[IO.File]::ReadAllText('C:\rightpad\artifact-root.txt').Trim()
$round=Join-Path $root ('test-results\'+$Task)
$apkRoot=Join-Path $root ('builds\android\'+$Task+'\app\build\outputs\apk')
$env:JAVA_HOME='C:\Program Files\Android\Android Studio\jbr'
$buildTools='C:\Users\zhq\AppData\Local\Android\Sdk\build-tools\36.0.0'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inventory=@()
foreach($variant in @('control','pubg','dfm')) {
    $apk=Join-Path $round ('candidate\'+$variant+'.apk')
    Copy-Item -LiteralPath (Join-Path $apkRoot ($variant+'\debug\app-'+$variant+'-debug.apk')) -Destination $apk
    $badging=& (Join-Path $buildTools 'aapt.exe') dump badging $apk
    if($LASTEXITCODE -ne 0){throw 'Badging failed'}
    $badging | Set-Content (Join-Path $round ('evidence\'+$variant+'-badging.txt')) -Encoding UTF8
    $cert=& (Join-Path $buildTools 'apksigner.bat') verify --verbose --print-certs $apk 2>&1
    if($LASTEXITCODE -ne 0){throw 'Signature verification failed'}
    $cert | Set-Content (Join-Path $round ('evidence\'+$variant+'-signature.txt')) -Encoding UTF8
    $zip=[IO.Compression.ZipFile]::OpenRead($apk)
    try {
        $dex=@(foreach($entry in $zip.Entries | Where-Object FullName -match '^classes.*\.dex$') {
            $stream=$entry.Open();$hash=[Security.Cryptography.SHA256]::Create()
            try { [pscustomobject]@{name=$entry.FullName;sha256=([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-','')} }
            finally {$stream.Dispose();$hash.Dispose()}
        })
        $inventory+=@([pscustomobject]@{variant=$variant;apk=$apk;sha256=(Get-FileHash $apk).Hash;dex=$dex;identity=@($badging|Select-String 'package:|launchable-activity:') -join "`n"})
    } finally {$zip.Dispose()}
}
$inventory|ConvertTo-Json -Depth 8|Set-Content (Join-Path $round 'evidence\apk-inventory.json') -Encoding UTF8
if(@($inventory|ForEach-Object {$_.dex|ConvertTo-Json -Compress}|Select-Object -Unique).Count -ne 1){throw 'Experiment executable code differs'}
$inventory|Select-Object variant,identity|Format-List
'PASS all three APKs have identical DEX code'
