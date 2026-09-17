$ErrorActionPreference = 'Stop'

$checkScript = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\CheckVirtualHidCompatibility.ps1'
. $checkScript

$pin = '53e1a949fc0784af716b782ddfa6c647cafd1f05'
$otherPin = '1111111111111111111111111111111111111111'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ("rightpad-vhid-compatibility-{0}" -f [guid]::NewGuid().ToString('N'))

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

function Assert-Contains {
    param([string[]]$Lines, [string]$Expected, [string]$Message)
    if ($Lines -notcontains $Expected) { throw "$Message Missing '$Expected'." }
}

function New-PinFixture {
    param([string]$CMakePin, [string]$BuildPin)

    $root = Join-Path $fixtureRoot ([guid]::NewGuid().ToString('N'))
    $native = Join-Path $root 'windows\native\Rightpad.VirtualHid'
    $tools = Join-Path $root 'windows\tools'
    $null = New-Item -ItemType Directory -Path $native -Force
    $null = New-Item -ItemType Directory -Path $tools -Force
    Set-Content -LiteralPath (Join-Path $native 'CMakeLists.txt') -Encoding ASCII -Value @(
        'FetchContent_Declare(libvirtualhid'
        "    GIT_TAG $CMakePin"
        ')'
    )
    Set-Content -LiteralPath (Join-Path $tools 'BuildVirtualHid.ps1') -Encoding ASCII -Value (
        "Get-PinnedSource `$dependency 'https://github.com/LizardByte/libvirtualhid.git' '$BuildPin'"
    )
    $root
}

function New-HeaderReader {
    param([string]$CurrentHeader, [string]$TargetHeader)

    {
        param([string]$Reference)
        if ($Reference -eq $pin) { return $CurrentHeader }
        $TargetHeader
    }.GetNewClosure()
}

function Invoke-TestCase {
    param(
        [string]$Name,
        [string]$CMakePin,
        [string]$BuildPin,
        [string]$CurrentHeader,
        [string]$TargetHeader,
        [int]$ExpectedExitCode,
        [string[]]$ExpectedLines
    )

    $root = New-PinFixture $CMakePin $BuildPin
    $reader = New-HeaderReader $CurrentHeader $TargetHeader
    $result = Invoke-VirtualHidCompatibilityCheck -TargetTag 'fixture-target' -RepositoryRoot $root -RemoteHeaderReader $reader
    Assert-Equal $ExpectedExitCode $result.ExitCode "$Name exit code."
    foreach ($line in $ExpectedLines) { Assert-Contains $result.Lines $line "$Name output." }
    "PASS $Name"
}

try {
    $header5 = 'inline constexpr uint32_t LVH_WINDOWS_BROKER_PROTOCOL_VERSION = 5u;'
    $header4 = 'inline constexpr uint32_t LVH_WINDOWS_BROKER_PROTOCOL_VERSION = 4u;'

    Invoke-TestCase 'matching pins' $pin $pin $header5 $header5 0 @($pin, 'SAFE TO UPDATE')
    Invoke-TestCase 'pin mismatch' $pin $otherPin $header5 $header5 4 @('RIGHTPAD CONFIG ERROR', $pin, $otherPin)
    Invoke-TestCase 'protocol 5 vs 5' $pin $pin $header5 $header5 0 @('MATCH', 'SAFE TO UPDATE')
    Invoke-TestCase 'protocol 5 vs 4' $pin $pin $header5 $header4 2 @('MISMATCH', 'RIGHTPAD UPDATE REQUIRED BEFORE DRIVER UPGRADE')
    Invoke-TestCase 'malformed protocol header' $pin $pin 'not a protocol header' $header5 3 @('CHECK INCONCLUSIVE')
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

'PASS Virtual HID compatibility guard regression (5 cases)'
