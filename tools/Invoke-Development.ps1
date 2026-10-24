param(
    [ValidateSet('NewTask','BuildWindows','TestWindows','BuildNative','TestAndroidJvm','Backup')][string]$Action = 'NewTask',
    [string]$Task = ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-development'),
    [string]$ArtifactRoot = '',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string[]]$TestArguments = @(),
    [switch]$ReuseDeployedNative
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ArtifactPaths.ps1')
$root = Get-RightpadArtifactRoot $ArtifactRoot
$taskDirectory = New-RightpadTask $Task $root
$evidence = Join-Path $taskDirectory 'evidence'
$repo = $script:RightpadSourceRoot
function Invoke-Recorded([string]$Command, [string[]]$Arguments, [string]$Name) {
    & $Command @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $evidence $Name)
    if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE); see $evidence\$Name" }
}
switch ($Action) {
    'NewTask' { $taskDirectory }
    'BuildWindows' {
        $candidate = Join-Path $taskDirectory "candidate\$Configuration"
        if (Test-Path -LiteralPath $candidate) { throw "Candidate exists; use a fresh task identity: $candidate" }
        $extra=@()
        if($ReuseDeployedNative) {
            if($Configuration -ne 'Release') {throw 'Deployed native reuse is Release only'}
            $native=New-RightpadDirectory (Join-Path $root "builds\native\$Task\Release\bin")
            $formal=Join-Path $repo 'windows\Rightpad.Receiver\bin\Release\net8.0-windows'
            $record=@(foreach($name in @('Rightpad.VirtualHid.dll','libvirtualhid-LICENSE.txt','lizardbyte-common-LICENSE.txt')) {
                $source=Join-Path $formal $name
                Copy-Item -LiteralPath $source -Destination $native -Force
                $hash=(Get-FileHash -LiteralPath $source).Hash
                if((Get-FileHash -LiteralPath (Join-Path $native $name)).Hash -ne $hash) {throw "Native copy mismatch: $name"}
                [ordered]@{path=$source;sha256=$hash;reused=$true}
            })
            $record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'native-reuse.json') -Encoding UTF8
            $extra=@('-p:RightpadReuseNative=true')
        }
        Invoke-Recorded 'dotnet' (@('build', (Join-Path $repo 'windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj'), '-c', $Configuration, "-p:RightpadArtifactsRoot=$root", "-p:RightpadArtifactScope=$Task", '-m:1')+$extra) 'windows-build.log'
        $output = Join-Path $root "builds\windows\$Task\Rightpad.Receiver\bin\$Configuration\net8.0-windows"
        Copy-Item -LiteralPath $output -Destination $candidate -Recurse
        Get-ChildItem -LiteralPath $candidate -File -Recurse | ForEach-Object { [ordered]@{path=$_.FullName.Substring($candidate.Length+1);bytes=$_.Length;writtenUtc=$_.LastWriteTimeUtc.ToString('o');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'candidate.json') -Encoding UTF8
    }
    'TestWindows' {
        $test = Join-Path $root "builds\windows\$Task\Rightpad.Receiver.Tests\bin\$Configuration\net8.0-windows\Rightpad.Receiver.Tests.dll"
        if (!(Test-Path -LiteralPath $test)) { throw "Build this task first: $test" }
        $previousTemp=$env:TEMP; $previousTmp=$env:TMP
        try {
            $env:TEMP=New-RightpadDirectory (Join-Path $evidence 'temporary-fixtures')
            $env:TMP=$env:TEMP
            Invoke-Recorded 'dotnet' (@($test) + $TestArguments) 'windows-tests.log'
        } finally { $env:TEMP=$previousTemp; $env:TMP=$previousTmp }
    }
    'BuildNative' { & (Join-Path $repo 'windows\tools\BuildVirtualHid.ps1') -Configuration $Configuration -ArtifactRoot $root -Scope $Task }
    'TestAndroidJvm' { & (Join-Path $repo 'android\tests\Test-ProtocolV2Encoder.ps1') -ArtifactRoot $root -Scope $Task 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'android-jvm.log') }
    'Backup' { & (Join-Path $PSScriptRoot 'New-RecoveryBackup.ps1') -TaskDirectory $taskDirectory }
}
