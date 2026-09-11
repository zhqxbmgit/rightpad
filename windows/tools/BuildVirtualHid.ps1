param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$Test)
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'native\Rightpad.VirtualHid'
$dependency = Join-Path $source 'obj\libvirtualhid'
$build = Join-Path $source "obj\$Configuration"
$destination = Join-Path $source "bin\$Configuration"
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE" }
}
function Get-PinnedSource([string]$Path, [string]$Url, [string]$Commit) {
    if (!(Test-Path (Join-Path $Path '.git'))) { Invoke-Checked 'git' @('clone','--no-checkout',$Url,$Path) }
    $revision = & git -C $Path rev-parse HEAD 2>$null
    if ($revision -ne $Commit) { Invoke-Checked 'git' @('-C',$Path,'checkout','--detach',$Commit) }
    if ((& git -C $Path rev-parse HEAD) -ne $Commit) { throw "Dependency pin mismatch: $Path" }
    Invoke-Checked 'git' @('-C',$Path,'diff','--exit-code','HEAD','--','src','CMakeLists.txt')
}
# Explicit clones also work with minimal Git distributions lacking git-submodule shell utilities.
Get-PinnedSource $dependency 'https://github.com/LizardByte/libvirtualhid.git' '6fdb8bd4de3b68d96c30e5303ac2ebb333c09746'
Get-PinnedSource (Join-Path $dependency 'third-party\lizardbyte-common') 'https://github.com/LizardByte/lizardbyte-common.git' 'f9d91e1d29b7473f58e43acde4579da4e56c4abe'
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
$cmake = if ($cmakeCommand) { $cmakeCommand.Source } else { 'C:\msys64\ucrt64\bin\cmake.exe' }
if (!(Test-Path $cmake)) { throw 'CMake 3.24+ and a Windows x64 C++23 toolchain are required.' }
$toolDirectory = Split-Path $cmake -Parent
$arguments = @('-S',$source,'-B',$build,"-DCMAKE_BUILD_TYPE=$Configuration","-DFETCHCONTENT_SOURCE_DIR_LIBVIRTUALHID=$dependency")
if (Test-Path (Join-Path $toolDirectory 'g++.exe')) {
    $env:PATH = "$toolDirectory;$env:PATH"
    $arguments += @('-G','Ninja',"-DCMAKE_CXX_COMPILER=$toolDirectory\g++.exe","-DCMAKE_MAKE_PROGRAM=$toolDirectory\ninja.exe")
} else {
    $arguments += @('-A','x64','-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded')
}
Invoke-Checked $cmake $arguments
Invoke-Checked $cmake @('--build',$build,'--config',$Configuration,'--parallel','4')
if ($Test) { Invoke-Checked (Join-Path $toolDirectory 'ctest.exe') @('--test-dir',$build,'-C',$Configuration,'--output-on-failure') }
$dll = Join-Path $build 'Rightpad.VirtualHid.dll'
if (!(Test-Path $dll)) { $dll = Join-Path $build "$Configuration\Rightpad.VirtualHid.dll" }
$null = New-Item -ItemType Directory -Path $destination -Force
Copy-Item -LiteralPath $dll -Destination $destination -Force
@('LICENSE.md','LICENSES\license-map.md','LICENSES\MIT.md','LICENSES\LicenseRef-LizardByte-SAL-1.0.md') |
    ForEach-Object { Get-Content -LiteralPath (Join-Path $dependency $_) } |
    Set-Content -LiteralPath (Join-Path $destination 'libvirtualhid-LICENSE.txt') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $dependency 'third-party\lizardbyte-common\LICENSE') -Destination (Join-Path $destination 'lizardbyte-common-LICENSE.txt') -Force
