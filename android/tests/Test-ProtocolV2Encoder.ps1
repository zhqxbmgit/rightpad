$ErrorActionPreference = 'Stop'
$androidRoot = Split-Path $PSScriptRoot -Parent
$outputDirectory = Join-Path $androidRoot 'app/build/jvm-tests'
$sourceDirectory = Join-Path $androidRoot 'app/src/main/java/com/rightpad/capture'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$javacPath = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin/javac.exe' } else { 'javac' }
$javaPath = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin/java.exe' } else { 'java' }
& $javacPath --release 17 -d $outputDirectory (Join-Path $sourceDirectory 'TouchSample.java') (Join-Path $sourceDirectory 'ProtocolV2Encoder.java') (Join-Path $PSScriptRoot 'ProtocolV2EncoderTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Encoder test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.ProtocolV2EncoderTest
if ($LASTEXITCODE -ne 0) { throw 'Encoder tests failed.' }
& $javacPath --release 17 -cp $outputDirectory -d $outputDirectory (Join-Path $PSScriptRoot 'stubs/android/util/Log.java') (Join-Path $sourceDirectory 'HeartbeatSchedule.java') (Join-Path $sourceDirectory 'UdpTouchSender.java') (Join-Path $PSScriptRoot 'UdpTouchSenderTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Sender test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.UdpTouchSenderTest
if ($LASTEXITCODE -ne 0) { throw 'Sender tests failed.' }
& $javacPath --release 17 -cp $outputDirectory -d $outputDirectory (Join-Path $sourceDirectory 'BatteryDisplay.java') (Join-Path $sourceDirectory 'TopToolLayout.java') (Join-Path $sourceDirectory 'PowerGestureTracker.java') (Join-Path $PSScriptRoot 'UiStatusAndPowerTest.java')
if ($LASTEXITCODE -ne 0) { throw 'UI status/power test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.UiStatusAndPowerTest
if ($LASTEXITCODE -ne 0) { throw 'UI status/power tests failed.' }
