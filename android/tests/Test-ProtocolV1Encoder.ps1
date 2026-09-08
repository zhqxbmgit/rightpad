$ErrorActionPreference = 'Stop'
$androidRoot = Split-Path $PSScriptRoot -Parent
$outputDirectory = Join-Path $androidRoot 'app/build/encoder-tests'
$sourceDirectory = Join-Path $androidRoot 'app/src/main/java/com/rightpad/capture'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$javacPath = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin/javac.exe' } else { 'javac' }
$javaPath = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin/java.exe' } else { 'java' }
& $javacPath --release 17 -d $outputDirectory (Join-Path $sourceDirectory 'TouchSample.java') (Join-Path $sourceDirectory 'ProtocolV1Encoder.java') (Join-Path $PSScriptRoot 'ProtocolV1EncoderTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Encoder test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.ProtocolV1EncoderTest
if ($LASTEXITCODE -ne 0) { throw 'Encoder tests failed.' }
