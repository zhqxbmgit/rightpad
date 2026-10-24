param([Parameter(Mandatory)][string]$Package,[Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$stream=[IO.File]::OpenRead((Join-Path $Package 'Rightpad.Receiver.dll'))
$pe=[System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata=[System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $types=@($metadata.TypeDefinitions|ForEach-Object {$t=$metadata.GetTypeDefinition($_);$metadata.GetString($t.Namespace)+'.'+$metadata.GetString($t.Name)})
    $resources=@($metadata.ManifestResources|ForEach-Object {$metadata.GetString($metadata.GetManifestResource($_).Name)})
    $imports=@($metadata.MethodDefinitions|ForEach-Object {$m=$metadata.GetMethodDefinition($_);if(([int]$m.Attributes -band 8192) -ne 0){$import=$m.GetImport();[ordered]@{method=$metadata.GetString($m.Name);module=$metadata.GetString($metadata.GetModuleReference($import.Module).Name)}}})
    $pattern='(?i)ModeSound|ModeAudio|mode_audio|mode-on-complete135|mode-off-minus7-complete170|winmm'
    $hits=@($types+$resources+@($imports|ForEach-Object {$_.module+'!'+$_.method})|Where-Object {$_ -match $pattern})
    $fileHits=@(Get-ChildItem -LiteralPath $Package -File -Recurse|Where-Object {$_.FullName.Substring($Package.TrimEnd('\').Length+1) -match $pattern -or $_.Extension -eq '.wav'}|ForEach-Object FullName)
    if($hits.Count -or $fileHits.Count){throw 'Removed facility remains in candidate metadata/package'}
    if(Test-Path -LiteralPath (Join-Path $Package 'Resources\ModeSound')){throw 'Retired resource directory remains'}
    [ordered]@{at=[DateTime]::UtcNow.ToString('o');package=$Package;receiverSha256=(Get-FileHash -LiteralPath (Join-Path $Package 'Rightpad.Receiver.dll')).Hash;
      passed=$true;types=$types;manifestResources=$resources;nativeImports=$imports;removedFacilityHits=$hits;resourceFileHits=$fileHits;
      note='Metadata inspection only; no assembly execution, GUI launch, devices or input'}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $Evidence -Encoding UTF8
    'PASS compiled type/resource/PInvoke and complete-package removal audit'
} finally {$pe.Dispose();$stream.Dispose()}
