param([string]$TargetTag)

$ErrorActionPreference = 'Stop'
$script:BrokerProtocolHeaderPath = 'src/platform/windows/shared/lvh_windows_broker_protocol.h'

function New-CompatibilityCheckResult {
    param([int]$ExitCode, [string[]]$Lines)

    [pscustomobject]@{
        ExitCode = $ExitCode
        Lines = $Lines
    }
}

function Get-CMakePin {
    param([string]$Path)

    $content = Get-Content -Raw -LiteralPath $Path
    $match = [regex]::Match($content, '(?im)^\s*GIT_TAG\s+([0-9a-f]{40})\s*$')
    if (!$match.Success) { throw "Could not parse libvirtualhid GIT_TAG from $Path" }
    $match.Groups[1].Value.ToLowerInvariant()
}

function Get-BuildScriptPin {
    param([string]$Path)

    $content = Get-Content -Raw -LiteralPath $Path
    $pattern = '(?im)^\s*Get-PinnedSource\s+\$dependency\s+[''\"][^''\"]+[''\"]\s+[''\"]([0-9a-f]{40})[''\"]\s*$'
    $match = [regex]::Match($content, $pattern)
    if (!$match.Success) { throw "Could not parse libvirtualhid pin from $Path" }
    $match.Groups[1].Value.ToLowerInvariant()
}

function Get-BrokerProtocolVersion {
    param([string]$Content, [string]$Source)

    $name = 'LVH_WINDOWS_BROKER_PROTOCOL_VERSION'
    $assignment = "(?m)^\s*(?:inline\s+constexpr\s+(?:std::)?uint32_t\s+$name\s*=\s*|#define\s+$name\s+)(\d+)[uUlL]*\s*;?\s*$"
    $match = [regex]::Match($Content, $assignment)
    if (!$match.Success) { throw "Could not parse $name from $Source" }
    [uint32]::Parse($match.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-GitHubBrokerProtocolHeader {
    param([string]$Reference)

    $escapedReference = [uri]::EscapeDataString($Reference)
    $uri = "https://raw.githubusercontent.com/LizardByte/libvirtualhid/$escapedReference/$script:BrokerProtocolHeaderPath"
    try {
        (Invoke-WebRequest -UseBasicParsing -Uri $uri -ErrorAction Stop).Content
    } catch {
        throw "Failed to read $script:BrokerProtocolHeaderPath at '$Reference' from GitHub: $($_.Exception.Message)"
    }
}

function Get-RightpadBrokerProtocolHeader {
    param(
        [string]$DependencyCheckout,
        [string]$Pin,
        [scriptblock]$RemoteHeaderReader
    )

    $gitMetadata = Join-Path $DependencyCheckout '.git'
    if (Test-Path -LiteralPath $gitMetadata) {
        $head = $null
        $safeDirectory = $DependencyCheckout -replace '\\', '/'
        try {
            $head = & git -c "safe.directory=$safeDirectory" -C $DependencyCheckout rev-parse HEAD 2>$null
        } catch {
            $head = $null
        }
        if ($LASTEXITCODE -eq 0 -and $head -and $head.Trim().ToLowerInvariant() -eq $Pin) {
            $header = Join-Path $DependencyCheckout ($script:BrokerProtocolHeaderPath -replace '/', '\')
            if (Test-Path -LiteralPath $header) {
                return [pscustomobject]@{
                    Content = Get-Content -Raw -LiteralPath $header
                    Source = $header
                }
            }
        }
    }

    [pscustomobject]@{
        Content = [string](& $RemoteHeaderReader $Pin)
        Source = "GitHub libvirtualhid $Pin/$script:BrokerProtocolHeaderPath"
    }
}

function Invoke-VirtualHidCompatibilityCheck {
    param(
        [Parameter(Mandatory = $true)][string]$TargetTag,
        [string]$RepositoryRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
        [scriptblock]$RemoteHeaderReader
    )

    $heading = @(
        '=================================================='
        'Rightpad Virtual HID Compatibility Check'
        '=================================================='
        ''
    )

    if ([string]::IsNullOrWhiteSpace($TargetTag)) {
        return New-CompatibilityCheckResult 3 ($heading + @('VERDICT:', 'CHECK INCONCLUSIVE', '', 'Reason:', 'TargetTag must not be empty.'))
    }
    if (!$RemoteHeaderReader) { $RemoteHeaderReader = ${function:Get-GitHubBrokerProtocolHeader} }

    $cmakePath = Join-Path $RepositoryRoot 'windows\native\Rightpad.VirtualHid\CMakeLists.txt'
    $buildScriptPath = Join-Path $RepositoryRoot 'windows\tools\BuildVirtualHid.ps1'
    try {
        $cmakePin = Get-CMakePin $cmakePath
        $buildScriptPin = Get-BuildScriptPin $buildScriptPath
    } catch {
        return New-CompatibilityCheckResult 3 ($heading + @('VERDICT:', 'CHECK INCONCLUSIVE', '', 'Reason:', $_.Exception.Message))
    }

    if ($cmakePin -ne $buildScriptPin) {
        return New-CompatibilityCheckResult 4 ($heading + @(
            'CMake pin:'
            $cmakePin
            ''
            'Build script pin:'
            $buildScriptPin
            ''
            'VERDICT:'
            'RIGHTPAD CONFIG ERROR'
        ))
    }

    try {
        $dependencyCheckout = Join-Path $RepositoryRoot 'windows\native\Rightpad.VirtualHid\obj\libvirtualhid'
        $rightpadHeader = Get-RightpadBrokerProtocolHeader $dependencyCheckout $cmakePin $RemoteHeaderReader
        $rightpadProtocol = Get-BrokerProtocolVersion $rightpadHeader.Content $rightpadHeader.Source
        $targetContent = [string](& $RemoteHeaderReader $TargetTag)
        $targetSource = "GitHub libvirtualhid $TargetTag/$script:BrokerProtocolHeaderPath"
        $targetProtocol = Get-BrokerProtocolVersion $targetContent $targetSource
    } catch {
        return New-CompatibilityCheckResult 3 ($heading + @('VERDICT:', 'CHECK INCONCLUSIVE', '', 'Reason:', $_.Exception.Message))
    }

    $matches = $rightpadProtocol -eq $targetProtocol
    $compatibility = if ($matches) { 'MATCH' } else { 'MISMATCH' }
    $verdict = if ($matches) { 'SAFE TO UPDATE' } else { 'RIGHTPAD UPDATE REQUIRED BEFORE DRIVER UPGRADE' }
    $exitCode = if ($matches) { 0 } else { 2 }
    New-CompatibilityCheckResult $exitCode ($heading + @(
        'Rightpad pin:'
        $cmakePin
        ''
        'Rightpad broker protocol:'
        [string]$rightpadProtocol
        ''
        'Target release:'
        $TargetTag
        ''
        'Target broker protocol:'
        [string]$targetProtocol
        ''
        'Protocol compatibility:'
        $compatibility
        ''
        'VERDICT:'
        $verdict
    ))
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $result = Invoke-VirtualHidCompatibilityCheck -TargetTag $TargetTag
        $result.Lines | Write-Output
        exit $result.ExitCode
    } catch {
        Write-Output 'VERDICT:'
        Write-Output 'CHECK FAILED WITH UNHANDLED ERROR'
        Write-Output ''
        Write-Output 'Reason:'
        Write-Output $_.Exception.Message
        exit 1
    }
}
