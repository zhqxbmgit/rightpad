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
$motionBuilder = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-MotionArguments'
}, $true).Extent.Text
$motionProbe = [scriptblock]::Create($motionBuilder + "`n" + 'Get-MotionArguments @args')
if ((& $motionProbe '') -cne '') { throw 'An ordinary launch must use the EXE-owned fixed product mode.' }
if ((& $motionProbe 'RAW') -cne ' --dev-motion-mode RAW') { throw 'Explicit RAW development override mismatch.' }
if ((& $motionProbe 'RESAMPLED_250HZ') -cne ' --dev-motion-mode RESAMPLED_250HZ') { throw 'B mode argument mismatch.' }
if ((& $motionProbe '' 'C:\trace space') -cne ' --dev-motion-trace-dir "C:\trace space"') { throw 'Trace quoting mismatch.' }
'PASS motion launcher fixed product default, experiment selection and trace path (4 cases)'
foreach ($mode in @('RESAMPLED_250HZ_BOXCAR_4MS','RESAMPLED_250HZ_BOXCAR_8MS')) {
    if ((& $motionProbe $mode) -cne " --dev-motion-mode $mode") { throw "Fixed boxcar argument mismatch: $mode" }
    $paramProbe = [scriptblock]::Create($parameters + "`n" + '$DevMotionMode')
    if ((& $paramProbe -DevMotionMode $mode) -cne $mode) { throw "Launcher mode validation mismatch: $mode" }
}
'PASS fixed F4 / F8 launcher validation and arguments'
foreach ($mode in @('RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5','RESAMPLED_250HZ_FINITE_CRITICAL_K35_R4', 'RESAMPLED_250HZ_FINITE_CRITICAL_K24_R5_SETTLE', 'RESAMPLED_500HZ_FINITE_CRITICAL_K24_R5_SETTLE', 'RESAMPLED_1000HZ_FINITE_CRITICAL_K24_R5_SETTLE')) {
    if ((& $motionProbe $mode) -cne " --dev-motion-mode $mode") { throw "Fixed finite critical argument mismatch: $mode" }
    $paramProbe = [scriptblock]::Create($parameters + "`n" + '$DevMotionMode')
    if ((& $paramProbe -DevMotionMode $mode) -cne $mode) { throw "Launcher finite mode validation mismatch: $mode" }
}
'PASS fixed K24-r5 / K35-r4 launcher validation and arguments'
