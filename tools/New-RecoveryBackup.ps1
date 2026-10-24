param(
    [Parameter(Mandatory)][string]$TaskDirectory,
    [string]$Release = '', [string]$Settings = '', [string]$Launcher = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ArtifactPaths.ps1')
$repo = $script:RightpadSourceRoot
if (!$Release) { $Release = Join-Path $repo 'windows\Rightpad.Receiver\bin\Release\net8.0-windows' }
if (!$Settings) { $Settings = Join-Path $env:LOCALAPPDATA 'rightpad\settings.json' }
if (!$Launcher) { $Launcher = Join-Path $repo 'windows\tools\RightpadReceiverTask.ps1' }
$externalDependencies = @($Launcher)
if (!$PSBoundParameters.ContainsKey('Launcher')) {
    $externalDependencies += @((Join-Path $repo 'tools\ArtifactPaths.ps1'), (Join-Path $repo 'artifact-root.txt'))
}
foreach ($path in @($TaskDirectory, $Release, $Settings, $Launcher)) {
    if (![IO.Path]::IsPathRooted($path) -or !(Test-Path -LiteralPath $path)) { throw "Missing absolute input: $path" }
}
$package = Join-Path $TaskDirectory ('recovery\backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $package) { throw "Backup exists: $package" }
$null = New-RightpadDirectory $package
Copy-Item -LiteralPath $Release -Destination (Join-Path $package 'release') -Recurse
Copy-Item -LiteralPath $Settings -Destination (Join-Path $package 'settings.json')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Recovery.ps1') -Destination (Join-Path $package 'Recovery.ps1')
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($package.Length + 1)
    $source = if ($relative.StartsWith('release\')) { Join-Path $Release $relative.Substring(8) } elseif ($relative -eq 'settings.json') { $Settings } else { Join-Path $PSScriptRoot 'Recovery.ps1' }
    $hash = (Get-FileHash -LiteralPath $_.FullName).Hash
    if ($hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw "Source changed while backing up: $source" }
    [ordered]@{relative=$relative;bytes=$_.Length;sha256=$hash}
})
$externalFiles = @($externalDependencies | ForEach-Object { [ordered]@{path=[IO.Path]::GetFullPath($_);sha256=(Get-FileHash -LiteralPath $_).Hash} })
$manifest = [ordered]@{ createdUtc=[DateTime]::UtcNow.ToString('o'); releaseTarget=[IO.Path]::GetFullPath($Release); settingsTarget=[IO.Path]::GetFullPath($Settings); launcher=[IO.Path]::GetFullPath($Launcher); launcherSha256=(Get-FileHash -LiteralPath $Launcher).Hash; externalFiles=$externalFiles; files=$files; note='Preflight only in this task. Before actual deployment validate independent scheduled recovery protection.' }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding UTF8
& (Join-Path $package 'Recovery.ps1') -Mode Preflight
$package
