param([string]$Task = '20261022-artifact-defaults', [string]$ArtifactRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ArtifactPaths.ps1')
$taskDirectory = New-RightpadTask $Task $ArtifactRoot
$evidence = Join-Path $taskDirectory 'evidence'
$fake = New-RightpadDirectory (Join-Path $evidence ('fake environment ' + [guid]::NewGuid().ToString('N')))
$passes = New-Object Collections.Generic.List[string]
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $passes.Add($Message) }
Check ((Get-RightpadArtifactRoot) -eq 'C:\rightpad-artifacts') 'shared default root'
Check ((Get-RightpadArtifactRoot 'C:\') -eq 'C:\') 'drive root is never collapsed to a drive-relative path'
$override = Join-Path $fake 'root with spaces'
$generated = New-RightpadTask 'task-one' $override
Check (Test-Path -LiteralPath (Join-Path $generated 'recovery')) 'override and spaces create task siblings'
$blocked = Join-Path $fake 'blocked-file'
[IO.File]::WriteAllText($blocked, 'fixture')
$failed = $false
try { $null = New-RightpadTask 'cannot-write' $blocked } catch { $failed = $true }
Check $failed 'file-as-directory fails without fallback'
$protected = New-RightpadDirectory (Join-Path $fake 'write denied')
$originalAcl = Get-Acl -LiteralPath $protected
$denyAcl = Get-Acl -LiteralPath $protected
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, 'CreateFiles', 'Deny')
$denyAcl.AddAccessRule($rule)
try {
    Set-Acl -LiteralPath $protected -AclObject $denyAcl
    $failed = $false; try { $null = New-RightpadDirectory $protected } catch { $failed = $true }
    Check $failed 'existing directory with denied file writes fails explicitly'
} finally { Set-Acl -LiteralPath $protected -AclObject $originalAcl }
foreach ($invalid in @('relative', '..\escape', 'C:drive-relative')) {
    $failed = $false; try { $null = Get-RightpadArtifactRoot $invalid } catch { $failed = $true }
    Check $failed "relative root rejected: $invalid"
}
$failed = $false; try { $null = New-RightpadTask '..' $override } catch { $failed = $true }
Check $failed 'task traversal rejected'
$repo = $script:RightpadSourceRoot
$scripts = @('tools\ArtifactPaths.ps1','tools\Invoke-Development.ps1','tools\New-RecoveryBackup.ps1','tools\Recovery.ps1','tools\Test-ArtifactPaths.ps1','windows\tools\BuildVirtualHid.ps1','windows\tools\FreezeRightpadFlightRecorder.ps1','windows\tools\RightpadReceiverTask.ps1','android\tests\Test-ProtocolV2Encoder.ps1')
foreach ($script in $scripts) {
    $tokens=$null; $parseErrors=$null
    $null = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo $script), [ref]$tokens, [ref]$parseErrors)
    Check ($parseErrors.Count -eq 0) "PowerShell $($PSVersionTable.PSVersion) parse: $script"
}
$launcher = Join-Path $repo 'windows\tools\RightpadReceiverTask.ps1'
$tokens=$null; $parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($launcher,[ref]$tokens,[ref]$parseErrors)
$reader=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Read-Run'},$true).Extent.Text
& {
    param($Reader,$Fake)
    $currentPath=Join-Path $Fake 'new-current.json'; $legacyCurrentPath=Join-Path $Fake 'legacy-current.json'
    @{StartUtc='2026-10-22T10:00:00Z';Directory='new-stale';Live=$false} | ConvertTo-Json | Set-Content $currentPath
    @{StartUtc='2026-10-22T09:00:00Z';Directory='legacy-live';Live=$true} | ConvertTo-Json | Set-Content $legacyCurrentPath
    function Get-OwnedReceiver($run) { if ($run.Live) { [pscustomobject]@{ Id=123 } } }
    . ([scriptblock]::Create($Reader))
    Check ((Read-Run).Directory -eq 'legacy-live') 'live legacy instance wins over stale new record'
    [IO.File]::Delete($currentPath)
    Check ((Read-Run).Directory -eq 'legacy-live') 'missing new record still locates legacy instance'
    @{StartUtc='2026-10-22T10:00:00Z';Directory='new-live';Live=$true} | ConvertTo-Json | Set-Content $currentPath
    $failed=$false; try { $null=Read-Run } catch { $failed=$true }
    Check $failed 'ambiguous live records fail without process actions'
} $reader $fake
$plan = & (Join-Path $repo 'windows\tools\BuildVirtualHid.ps1') -ArtifactRoot $override -Scope 'round-one' -Configuration Debug -Plan
Check ($plan.Build -eq (Join-Path $override 'builds\native\round-one\Debug\obj')) 'native plan separates scope and configuration'
$release=New-RightpadDirectory (Join-Path $fake 'production release')
foreach ($name in @('Rightpad.Receiver.exe','Rightpad.Receiver.dll','Rightpad.Receiver.deps.json','Rightpad.Receiver.runtimeconfig.json','Rightpad.VirtualHid.dll')) { [IO.File]::WriteAllText((Join-Path $release $name), 'FAKE offline fixture, never executable') }
$settings=Join-Path $fake 'formal settings.json'; [IO.File]::WriteAllText($settings, '{"fixture":true}')
$fakeLauncher=Join-Path $fake 'launcher.ps1'; [IO.File]::WriteAllText($fakeLauncher, 'throw "Do not execute fixture launcher"')
$fakeTask = New-RightpadDirectory (Join-Path $fake 'fixture task')
$backup = @(& (Join-Path $repo 'tools\New-RecoveryBackup.ps1') -TaskDirectory $fakeTask -Release $release -Settings $settings -Launcher $fakeLauncher)[-1]
$relocated=Join-Path $fake 'relocated package'
Copy-Item -LiteralPath $backup -Destination $relocated -Recurse
$preflight = & (Join-Path $relocated 'Recovery.ps1') -Mode Preflight
Check ($preflight -like 'PASS*') 'relocated recovery package preflight independent of working directory'
[IO.File]::AppendAllText((Join-Path $relocated 'release\Rightpad.Receiver.dll'), 'tampered fixture')
$failed=$false; try { & (Join-Path $relocated 'Recovery.ps1') -Mode Preflight } catch { $failed=$true }
Check $failed 'recovery hash failure rejected before any launcher call'
# JVM runner path execution with command fakes: no Java, packets or device access.
& {
    param($Repo,$Override)
    $previousJavaHome=$env:JAVA_HOME
    try {
        $env:JAVA_HOME=''
        $global:RightpadArtifactFakeJvmCounts=@{javac=0;java=0}
        function javac { $global:RightpadArtifactFakeJvmCounts.javac++; $global:LASTEXITCODE=0 }
        function java { $global:RightpadArtifactFakeJvmCounts.java++; $global:LASTEXITCODE=0 }
        & (Join-Path $Repo 'android\tests\Test-ProtocolV2Encoder.ps1') -ArtifactRoot $Override -Scope 'jvm-offline'
        Check ($global:RightpadArtifactFakeJvmCounts.javac -gt 0 -and $global:RightpadArtifactFakeJvmCounts.java -gt 0) 'JVM runner offline execution uses command fakes'
        Check (Test-Path -LiteralPath (Join-Path $Override 'builds\android\jvm-offline\jvm-tests')) 'JVM runner uses external override with spaces'
    } finally { $env:JAVA_HOME=$previousJavaHome; Remove-Variable RightpadArtifactFakeJvmCounts -Scope Global -ErrorAction SilentlyContinue }
} $repo $override
[ordered]@{engine=$PSVersionTable.PSVersion.ToString();createdUtc=[DateTime]::UtcNow.ToString('o');checks=$passes.ToArray();fakeRoot=$fake} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'path-tests.json') -Encoding UTF8
$passes
