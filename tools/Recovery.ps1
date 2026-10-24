param([ValidateSet('Preflight','Recover')][string]$Mode = 'Preflight')
$ErrorActionPreference = 'Stop'
# Package files always follow this script, including after moving the entire package.
$package = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
foreach ($path in @($manifest.releaseTarget, $manifest.settingsTarget, $manifest.launcher)) {
    if (![IO.Path]::IsPathRooted($path) -or !(Test-Path -LiteralPath $path)) { throw "Missing external production dependency: $path" }
}
if ((Get-FileHash -LiteralPath $manifest.launcher).Hash -ne $manifest.launcherSha256) { throw 'External launcher changed; repeat independent recovery review.' }
foreach ($entry in $manifest.externalFiles) {
    if (!(Test-Path -LiteralPath $entry.path) -or (Get-FileHash -LiteralPath $entry.path).Hash -ne $entry.sha256) { throw "External dependency changed: $($entry.path)" }
}
foreach ($entry in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $package $entry.relative))
    if (!$path.StartsWith($package.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup path escapes package.' }
    if ((Get-Item -LiteralPath $path).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $path).Hash -ne $entry.sha256) { throw "Backup mismatch: $path" }
}
foreach ($name in @('Rightpad.Receiver.exe','Rightpad.Receiver.dll','Rightpad.Receiver.deps.json','Rightpad.Receiver.runtimeconfig.json','Rightpad.VirtualHid.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $package "release\$name"))) { throw "Missing runtime dependency: $name" }
}
if ($Mode -eq 'Preflight') { 'PASS offline recovery paths and hashes; no process or configuration changes'; return }
# Explicit future recovery only. This command is never an independent guard by itself.
& $manifest.launcher -Mode Stop
Copy-Item -Path (Join-Path $package 'release\*') -Destination $manifest.releaseTarget -Recurse -Force
Copy-Item -LiteralPath (Join-Path $package 'settings.json') -Destination $manifest.settingsTarget -Force
& $manifest.launcher -Mode Start
