$ErrorActionPreference = 'Stop'
$launcher = Join-Path $PSScriptRoot 'RightpadReceiverTask.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw "Launcher parse errors: $errors" }
# Execute the real parameter defaults and argument builder without contacting Task Scheduler.
$parameters = $ast.ParamBlock.Extent.Text
$builder = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-ReceiverArguments'
}, $true).Extent.Text
$probe = [scriptblock]::Create($parameters + "`n" + $builder + "`n" + 'Get-ReceiverArguments ''C:\test logs'' $DevMouseBackend')
$expected = '--dev-log-dir "C:\test logs"'
foreach ($case in @(
    @{ Arguments = @{}; Expected = $expected },
    @{ Arguments = @{ DevMouseBackend = 'production' }; Expected = $expected },
    @{ Arguments = @{ DevMouseBackend = 'sendinput' }; Expected = "$expected --dev-mouse-backend sendinput" },
    @{ Arguments = @{ DevMouseBackend = 'virtualhid' }; Expected = "$expected --dev-mouse-backend virtualhid" }
)) {
    $arguments = $case.Arguments
    $actual = & $probe @arguments
    if ($actual -cne $case.Expected) { throw "Unexpected Receiver arguments: $actual" }
}
# Ensure the scheduler path uses the tested builder and preserves Start's explicit selection.
$source = $ast.Extent.Text
foreach ($required in @(
    '$start.Arguments = Get-ReceiverArguments $runDirectory $selectedBackend',
    '$selectedBackend = ''production''',
    '@{ MouseBackend = $DevMouseBackend }',
    '$selectedBackend = (Get-Content -LiteralPath $launchOptionsPath -Raw | ConvertFrom-Json).MouseBackend'
)) { if (!$source.Contains($required)) { throw "Launcher no longer uses verified selection path: $required" } }
'PASS launcher production inheritance and both explicit overrides (4 cases)'
