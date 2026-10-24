# Shared by project tools. No directories or environment variables are changed on import.
$script:RightpadSourceRoot = Split-Path $PSScriptRoot -Parent
function Get-RightpadArtifactRoot([string]$Root = '') {
    if (!$Root) { $Root = [IO.File]::ReadAllText((Join-Path $script:RightpadSourceRoot 'artifact-root.txt')).Trim() }
    if ($Root -notmatch '^[A-Za-z]:[\\/]' -and $Root -notmatch '^\\\\[^\\]+\\[^\\]+') { throw "Artifact root must be absolute: $Root" }
    if ($Root.Contains('"')) { throw 'Artifact root cannot contain quotes.' }
    $absolute = [IO.Path]::GetFullPath($Root)
    if ($absolute -eq [IO.Path]::GetPathRoot($absolute)) { return $absolute }
    $absolute.TrimEnd('\','/')
}
function Assert-RightpadPathSegment([string]$Value) {
    if (!$Value -or $Value -in @('.', '..') -or $Value -match '[^a-zA-Z0-9._-]') { throw "Invalid artifact identity: $Value" }
}
function New-RightpadDirectory([string]$Path) {
    $null = [IO.Directory]::CreateDirectory($Path)
    # Fail before a build, backup or launch when a directory is not writable.
    $probe = Join-Path $Path ('.write-probe-' + [guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe, '') } finally { if ([IO.File]::Exists($probe)) { [IO.File]::Delete($probe) } }
    $Path
}
function New-RightpadTask([string]$Task = ((Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-development'), [string]$Root = '') {
    Assert-RightpadPathSegment $Task
    $directory = Join-Path (Get-RightpadArtifactRoot $Root) "test-results\$Task"
    foreach ($name in @('evidence', 'candidate', 'recovery')) { $null = New-RightpadDirectory (Join-Path $directory $name) }
    $directory
}
