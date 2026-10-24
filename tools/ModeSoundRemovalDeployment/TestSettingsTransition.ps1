param([Parameter(Mandatory)][string]$FixtureRoot)
$ErrorActionPreference='Stop'
$null=New-Item -ItemType Directory -Path $FixtureRoot -Force
function HashOf([string]$path){(Get-FileHash -LiteralPath $path).Hash}
function Save([string]$name,$value){$value|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $evidence $name) -Encoding UTF8}
. (Join-Path $PSScriptRoot 'SettingsTransition.ps1')
$results=@()
foreach($present in @($false,$true)) {
    $evidence=Join-Path $FixtureRoot ([string]$present);New-Item -ItemType Directory -Path $evidence -Force|Out-Null
    $target=Join-Path $evidence 'settings.json';$prepared=Join-Path $evidence 'prepared.json';$record=Join-Path $evidence 'preparation.json'
    $extra=if($present){'"modeSound":{"enabled":true,"volumePercent":85},'}else{''}
    $text='{'+$extra+'"modeControl":{"kind":3,"key":"left","behavior":1},"unknown":[900719925474099312345,{"number":1.2300e+19}],"sensitivityX":12}'
    [IO.File]::WriteAllText($target,$text,(New-Object Text.UTF8Encoding($false)))
    & node (Join-Path $PSScriptRoot 'PrepareSettings.cjs') $target $prepared $record
    if($LASTEXITCODE -ne 0){throw 'Fixture preparation failed'}
    $plan=[pscustomobject]@{preparedSettings=$prepared;settingsTarget=$target;oldSettingsSha256=(HashOf $target);expectedSettingsSha256=(HashOf $prepared);settingsNeedsCleanup=$present}
    $before=[IO.File]::GetLastWriteTimeUtc($target)
    Apply-PreparedSettings
    $expected=if($present){$text.Replace($extra,'')}else{$text}
    if([IO.File]::ReadAllText($target) -ne $expected){throw 'Fields or number lexemes changed'}
    if(!$present -and [IO.File]::GetLastWriteTimeUtc($target) -ne $before){throw 'Absent-field file was rewritten'}
    $results += [ordered]@{present=$present;passed=$true;atomicBranch=$present;otherValuesAndLexemesPreserved=$true;absentBranchDidNotWrite=(!$present)}
}
$results|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $FixtureRoot 'results.json') -Encoding UTF8
'PASS isolated present-field atomic replacement and absent-field no-write fixtures'
