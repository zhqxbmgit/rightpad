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
& $javacPath --release 17 -cp $outputDirectory -d $outputDirectory (Join-Path $PSScriptRoot 'stubs/android/util/Log.java') (Join-Path $sourceDirectory 'SlideControlGesture.java') (Join-Path $sourceDirectory 'SlideControlLRGesture.java') (Join-Path $sourceDirectory 'ControlConfigProtocol.java') (Join-Path $sourceDirectory 'ControlConfigCache.java') (Join-Path $sourceDirectory 'GamepadState.java') (Join-Path $sourceDirectory 'GamepadStateSubmission.java') (Join-Path $sourceDirectory 'GamepadProtocol.java') (Join-Path $sourceDirectory 'GamepadSendState.java') (Join-Path $sourceDirectory 'HeartbeatSchedule.java') (Join-Path $sourceDirectory 'UdpTouchSender.java') (Join-Path $PSScriptRoot 'UdpTouchSenderTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Sender test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.UdpTouchSenderTest
if ($LASTEXITCODE -ne 0) { throw 'Sender tests failed.' }
& $javacPath --release 17 -cp $outputDirectory -d $outputDirectory (Join-Path $sourceDirectory 'BatteryDisplay.java') (Join-Path $sourceDirectory 'TopToolLayout.java') (Join-Path $sourceDirectory 'PowerGestureTracker.java') (Join-Path $PSScriptRoot 'UiStatusAndPowerTest.java')
if ($LASTEXITCODE -ne 0) { throw 'UI status/power test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.UiStatusAndPowerTest
if ($LASTEXITCODE -ne 0) { throw 'UI status/power tests failed.' }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $sourceDirectory 'DiscoveryProtocol.java') (Join-Path $sourceDirectory 'DiscoverySelection.java') (Join-Path $sourceDirectory 'DiscoveryBroadcasts.java') (Join-Path $sourceDirectory 'DiscoverySchedule.java') (Join-Path $sourceDirectory 'ConnectionDisplay.java') (Join-Path $PSScriptRoot 'DiscoveryTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Discovery test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.DiscoveryTest
if ($LASTEXITCODE -ne 0) { throw 'Discovery tests failed.' }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $sourceDirectory 'HapticFeedbackProtocol.java') (Join-Path $sourceDirectory 'HapticFeedbackGate.java') (Join-Path $PSScriptRoot 'HapticFeedbackTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Haptic test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.HapticFeedbackTest
if ($LASTEXITCODE -ne 0) { throw 'Haptic tests failed.' }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $PSScriptRoot 'stubs/android/os/Looper.java') (Join-Path $PSScriptRoot 'stubs/android/os/Handler.java') (Join-Path $sourceDirectory 'TouchpadClickFeedback.java') (Join-Path $sourceDirectory 'HapticFeedbackListener.java') (Join-Path $PSScriptRoot 'HapticListenerTest.java') (Join-Path $PSScriptRoot 'TouchpadClickFeedbackTests.java')
if ($LASTEXITCODE -ne 0) { throw 'Haptic listener test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.HapticListenerTest
if ($LASTEXITCODE -ne 0) { throw 'Haptic listener tests failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.TouchpadClickFeedbackTests
if ($LASTEXITCODE -ne 0) { throw 'Touchpad click feedback tests failed.' }

$controlSources = @('ControlRect', 'SlideControlGesture', 'SlideControlLRGesture', 'SlideControlLRDefinition', 'ScreenControlFeedback', 'ScreenControlDefinition', 'ScreenControlInstance', 'ScreenControlRouter', 'ScreenControlLayoutStore', 'ScreenControlLayoutEditor') | ForEach-Object { Join-Path $sourceDirectory ($_ + '.java') }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory $controlSources (Join-Path $PSScriptRoot 'ScreenControlsTest.java')
if ($LASTEXITCODE -ne 0) { throw 'Screen controls test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.ScreenControlsTest
if ($LASTEXITCODE -ne 0) { throw 'Screen controls tests failed.' }

& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $sourceDirectory 'GamepadAggregator.java') (Join-Path $PSScriptRoot 'GamepadProtocolTests.java') (Join-Path $PSScriptRoot 'GamepadSenderTests.java') (Join-Path $PSScriptRoot 'GamepadAggregatorTests.java')
if ($LASTEXITCODE -ne 0) { throw 'Gamepad test compilation failed.' }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $PSScriptRoot 'GamepadWireHoldTests.java')
if ($LASTEXITCODE -ne 0) { throw 'Gamepad wire hold test compilation failed.' }
foreach ($test in @('GamepadProtocolTests', 'GamepadSenderTests', 'GamepadAggregatorTests', 'GamepadWireHoldTests')) {
    & $javaPath -cp $outputDirectory ('com.rightpad.capture.' + $test)
    if ($LASTEXITCODE -ne 0) { throw "$test failed." }
}
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $PSScriptRoot 'ControlConfigTests.java')
if ($LASTEXITCODE -ne 0) { throw 'Control config test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.ControlConfigTests
if ($LASTEXITCODE -ne 0) { throw 'Control config tests failed.' }

& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $sourceDirectory 'ScreenControlFeedback.java') (Join-Path $PSScriptRoot 'ScreenControlFeedbackTests.java')
if ($LASTEXITCODE -ne 0) { throw 'Screen control feedback test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.ScreenControlFeedbackTests
if ($LASTEXITCODE -ne 0) { throw 'Screen control feedback tests failed.' }

$lrSources = @('SlideControlLRGesture', 'SlideControlLRDefinition', 'SlideControlLRInstance') | ForEach-Object { Join-Path $sourceDirectory ($_ + '.java') }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory $lrSources (Join-Path $PSScriptRoot 'SlideControlLRTests.java')
if ($LASTEXITCODE -ne 0) { throw 'LR test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.SlideControlLRTests
if ($LASTEXITCODE -ne 0) { throw 'LR tests failed.' }
& $javacPath --release 17 -encoding UTF-8 -cp $outputDirectory -d $outputDirectory (Join-Path $PSScriptRoot 'SlideControlLRTransportTests.java')
if ($LASTEXITCODE -ne 0) { throw 'LR transport test compilation failed.' }
& $javaPath -cp $outputDirectory com.rightpad.capture.SlideControlLRTransportTests
if ($LASTEXITCODE -ne 0) { throw 'LR transport tests failed.' }
