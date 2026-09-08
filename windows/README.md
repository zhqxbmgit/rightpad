# Windows Receiver / RAW Mouse Baseline Prototype

Windows 11 console receiver for Protocol v1 raw touch packets:

```text
UDP receive -> packet decode -> sequence acceptance
                              -> diagnostic logging (default)
                              -> touch session -> RAW delta -> fixed sensitivity
                                 -> fractional accumulator -> relative SendInput (--raw-mouse)
                              -> independent Single Tap recognition -> local LEFT DOWN/UP (--raw-mouse)
```

C#, .NET 8, `net8.0-windows`. Both application and tests use only the .NET
standard library plus Win32 SendInput through P/Invoke. RAW mouse mode includes
Single Tap left-click recognition. It has no motion filter, acceleration, drag,
motion output scheduler, settings UI, configuration file, discovery, or security system.

## Build and run

From the repository root in PowerShell, with .NET 8 SDK installed:

```powershell
dotnet build C:\rightpad\windows\Rightpad.Receiver\Rightpad.Receiver.csproj --configuration Release
dotnet run --project C:\rightpad\windows\Rightpad.Receiver\Rightpad.Receiver.csproj --configuration Release --no-build
```

The console listens on IPv4 `0.0.0.0:50000`. Press Ctrl+C to cancel a pending
receive, print final statistics, close the socket and exit. A bind error (for
example an occupied port) is reported with a nonzero exit code. Windows 11 is
checked at application startup. No firewall rules are installed or changed.

## Protocol v1

The authoritative layout is in [INPUT_PROTOCOL.md](../docs/INPUT_PROTOCOL.md).

- UDP port: 50000; version: 1.
- Events: DOWN=1, MOVE=2, UP=3. No CANCEL event.
- All multi-byte fields: Little Endian; no padding or extra fields.
- Header: version uint8, eventType uint8, sampleCount uint16,
  sessionId uint32, sequence uint32; total 12 bytes.
- Sample: timestampNs uint64, x float32, y float32; total 16 bytes.
- Datagram size must equal `12 + sampleCount * 16` exactly.
- DOWN/UP contain one sample; MOVE contains at least one.
- Coordinates must be finite; no screen-size clipping, scaling or conversion.
- timestampNs is an unsigned nanosecond event timestamp. No millisecond
  conversion is performed. Equal timestamps and original sample order remain.

The packet has no historical/current source field. `sampleIndex` in the console
is a zero-based index inside the packet, not an added wire field. `remote` and
`receiveElapsedMs` are receiver-local diagnostics. Receive time is measured just
after the socket receive completes and before decode, relative to receiver
startup using Stopwatch. It is not a hardware arrival timestamp or a one-way
Android-to-Windows latency measurement.

## Validation and statistics

The decoder rejects short/truncated packets, unsupported versions/events,
invalid sample counts, extra trailing bytes, and NaN/infinite coordinates. A
malformed packet produces a diagnostic, never a partial set of sample logs,
and does not stop reception or change the sequence baseline.

The first valid packet establishes the sequence baseline. Subsequent values
use ordinary uint32 ordering across session IDs:

- greater by one: `in_order`;
- greater by more than one: `gap`, add `delta - 1` to sequenceGapEstimate;
- equal to last accepted: `duplicate`;
- lower than last accepted: `old` (out-of-order or an older duplicate).

Duplicate/old packets log their header and status, but do not add accepted
samples or move the baseline. There is no reorder buffer or retransmission.
The gap estimate is cumulative and does not decrease for late packets; it is
not an exact final loss count. uint32 wraparound is intentionally unsupported.
Restart the receiver after restarting the sender.

Statistics are logged after each datagram in diagnostic mode and at shutdown in both modes:

| Counter | Meaning |
|---|---|
| receivedPackets | All datagrams received, including invalid/duplicate/old |
| invalidPackets | Datagrams rejected by the decoder |
| acceptedPackets | Valid baseline/in-order/gap packets |
| acceptedSamples | Samples in accepted packets |
| sequenceGapEstimate | Cumulative forward sequence gaps |
| duplicatePackets | Packets equal to the last accepted sequence |
| oldPackets | Packets below the last accepted sequence |

Remote endpoints are logged only. There is no sender locking, source rejection,
pairing or authentication. One sequence baseline is used for the intended single
sender; multiple independent sequence streams would make its statistics ambiguous.

After accepted input stops for 2 seconds, the receiver emits one `input_timeout`
diagnostic and keeps listening. Invalid, duplicate and old packets do not refresh
that deadline. A new accepted packet rearms it. Timeout only logs and increments
`inputTimeouts`: it does not clear sequence statistics, active touch session,
previous position, or fractional residual. A held finger can remain stationary
past 2 seconds and continue moving in the same session. This is the explicitly
approved diagnostic-only behavior described in the architecture/protocol documents.
Single Tap release is local to Windows; real connection-state and future drag
safety handling are deferred. There is no heartbeat.

Raw console logging itself has overhead. This prototype verifies data transport
and decoding; it is not a measurement of the final input system's performance.

## Automated verification

```powershell
dotnet build C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj --configuration Release
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj --configuration Release --no-build
```

The test executable returns nonzero if any test fails. It checks independent
fixed-byte fixtures, all three events, nanosecond values, original order and
equal timestamps, malformed inputs, sequence behavior, localhost UDP with two
source ports, reception after bad packets, idle cancellation, socket release,
input timeout/resume, and occupied-port failure. Socket tests use temporary
loopback ports and bounded waits. A sender exists only in the test project.

The real loopback test sends 7 datagrams: two malformed, DOWN sequence 10,
MOVE sequence 13 (two samples), duplicate 13, old 12, and UP sequence 14.
Expected totals: received=7, invalid=2, accepted=3, samples=4, gap estimate=2,
duplicate=1, old=1. All test event times are constructed as nanoseconds.

The Android sender and Protocol v1 remain unchanged. Optional SendInput and Android
LAN smoke tests are described below; ordinary regression tests never inject mouse input.

## Files

| File | Responsibility |
|---|---|
| Rightpad.Receiver/Program.cs | Windows 11 check, fixed endpoint, Ctrl+C and errors |
| Rightpad.Receiver/UdpReceiver.cs | Sequential UDP receive, local receive clock and timeout |
| Rightpad.Receiver/PacketDecoder.cs | Protocol validation and explicit endian decoding |
| Rightpad.Receiver/PacketModels.cs | Header, sample and decoded packet models |
| Rightpad.Receiver/PacketStatistics.cs | Counters and ordinary uint32 sequence comparison |
| Rightpad.Receiver/RawSampleLogger.cs | Console packet/sample/diagnostic/statistics output |
| Rightpad.Receiver/TouchSessionProcessor.cs | Active session, ordered position deltas, final UP delta |
| Rightpad.Receiver/RawMotionProcessor.cs | Fixed axis gains, symmetric truncation, per-session residual |
| Rightpad.Receiver/WindowsMouseOutput.cs | Relative SendInput, native layout and return/error checks |
| Rightpad.Receiver/GestureProcessor.cs | Independent Single Tap candidate, all-sample bounds and event-time duration |
| Rightpad.Receiver/LeftButtonController.cs | Nonblocking local click hold, serialized buttons and best-effort cleanup |
| Rightpad.Receiver.Tests/Program.cs | Dependency-free test runner and assertions |
| Rightpad.Receiver.Tests/PacketDecoderTests.cs | Literal fixtures, field and malformed tests |
| Rightpad.Receiver.Tests/PacketStatisticsTests.cs | Sequence and count tests |
| Rightpad.Receiver.Tests/UdpReceiverTests.cs | Actual socket tests and test-only sender |

Each directory has its own csproj; tests reference the application project.
`.gitignore` excludes build and IDE output.

## RAW mouse mode

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver\Rightpad.Receiver.csproj --configuration Release --no-build -- --raw-mouse
```

Default sensitivityX is 7.0 and default sensitivityY is 7.0. Optional finite positive startup values:

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver\Rightpad.Receiver.csproj --configuration Release --no-build -- --raw-mouse --sensitivity-x 5 --sensitivity-y 6
```

Values use an invariant decimal point and stay fixed for the process lifetime.
Without `--raw-mouse`, the receiver only records the original detailed diagnostics.

Only decoded and sequence-accepted packets enter the motion path. DOWN establishes
the position baseline and clears residuals, without moving. Matching MOVE samples
are processed in packet order; even equal or non-monotonic sample timestamps never
affect the processing order. A matching UP processes its final real delta before
ending the session. Foreign-session MOVE/UP and orphan MOVE/UP have no motion effect.
Every accepted DOWN resets the baseline, even if its session ID equals the active ID.
Duplicate/old packets never reach the processor. A sequence gap alone does not reset
motion: the next matching absolute position recovers the accumulated delta.

Coordinates widen to double before subtraction. Each axis uses
`total = residual + delta * sensitivity`, `integer = truncate(total)` and
`residual = total - integer`. Every nonzero integer pair is immediately submitted
as a single SendInput event. Samples are not combined across or within packets.
Fractional residuals belong only to the active session and are discarded on UP,
new DOWN, shutdown, or output/range failure. No residual is flushed after release.
An unrepresentable int32 movement stops the receiver with an explicit error;
it is not clamped, wrapped or split into artificial movement.

SendInput uses `INPUT_MOUSE`, `MOUSEEVENTF_MOVE`, relative signed dx/dy, zero
mouseData/time/extraInfo and the native INPUT size. Every call must return 1.
Failure logs the returned count and available Win32 error, stops the receiver,
clears local state and exits nonzero. It does not retry. UIPI causes cannot always
be identified from the Win32 error. RAW means unfiltered processing within rightpad;
Windows pointer settings and downstream WM_MOUSEMOVE coalescing can still affect
desktop/app behavior. There is no claim of hardware Raw Input injection or a
one-count-to-one-desktop-pixel mapping.

RAW mode omits routine packet/sample/per-datagram-stat formatting entirely. It logs
startup, invalid packets, sequence gaps/old packets, timeout and final statistics.
Expected DOWN/UP duplicates are counted silently. Final `motion_stats` include
processed MOVE/UP samples, ignored session packets, successful integer output events
and signed X/Y totals; `mouse_stats` records successful native movement events and failed movement calls.
Timeout counters are diagnostic, not network-disconnect classifications.

## Single Tap left click

`--raw-mouse` enables Single Tap alongside the unchanged RAW movement path.
Defaults verified from Moonlight Noir source: tap duration 300 ms, movement
threshold 8 px per axis, click hold 25 ms. The old double-tap interval is 130 ms,
recorded for future work only; Double Tap Drag and double-click recognition are
not implemented. Two independent clicks may still be interpreted as a double click
by Windows/the target application according to its own settings.

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver\Rightpad.Receiver.csproj --configuration Release --no-build -- --raw-mouse --tap-max-duration-ms 300 --tap-movement-threshold-px 8 --click-hold-ms 25
```

The three options require `--raw-mouse`. Duration/hold accept positive int32 whole
milliseconds; threshold accepts a finite positive number. Sensitivity stays 7/7
unless explicitly overridden. There is no double-tap option or saved configuration.

Each accepted DOWN stores its own gesture session, position and eventTimeNs.
All MOVE samples are checked: `abs(x-downX) <= threshold` AND
`abs(y-downY) <= threshold`. Exceeding either axis latches `confirmedMove` even if
a later sample returns in bounds. A matching UP clicks only if still a candidate,
UP is in bounds, and its nonnegative source-event duration is <= tapMaxDurationMs.
Wrong-session MOVE/UP do not mutate the candidate. Existing packet acceptance
removes duplicates, old packets and malformed data before either processing path.
Gesture never suppresses, modifies, or rolls back RAW movement.

Click sends LEFT DOWN immediately when idle and schedules LEFT UP using a one-shot
`System.Threading.Timer`. It does not wait in the receiver and does not need another
Android packet. Windows scheduling may make a 25 ms request last longer. Overlapping
tap requests are serialized after the prior UP so every click retains its hold;
pending clicks are discarded during shutdown. Button synchronization does not lock
the motion path. Normal shutdown and exception unwinding dispose the controller
and attempt release if held. Native failures log the button, return value and Win32
error; timer failure wakes/cancels the receiver and exits nonzero. Cleanup retries
UP best-effort, but forced termination or persistent native failure cannot guarantee
release. Input timeout remains diagnostic only.

Quiet mode adds startup parameters and final `gesture_stats` (tapCandidates,
confirmedMoves, clicksTriggered) and `button_stats` (leftDownSuccess, leftUpSuccess,
leftButtonFailures); no per-sample or per-tap logs. clicksTriggered counts requests;
native down/up counters record actual successful insertion calls separately.

The automatic runner includes the original 31 cases and Single Tap bounds/session/
historical-sample cases, button fields/errors, asynchronous hold, overlapping clicks,
cleanup, UDP progress during hold, exact motion independence and async failure exit.
Real native tests must run as the current Windows interactive user, outside the
Codex SendInput-restricted sandbox:

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj --configuration Release --no-build -- --sendinput-button-smoke
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj --configuration Release --no-build -- --android-tap-smoke
```

Both use an inert test window at the existing cursor position and assert actual
LEFT DOWN/UP returns, not cursor displacement. The Android test listens on 50000
for up to 25 seconds, expecting one ADB tap after `ANDROID_TAP_READY`; the phone
must be awake with rightpad in front. It validates one click, packet acceptance,
duplicates and native success. Human click feel and game compatibility remain pending.

## RAW automated checks

The default runner includes the original 14 tests plus session, accumulator, native
layout/error, argument, accepted-only UDP, quiet logging, output-failure cleanup and
timeout-preserves-motion tests. It uses collected movement commands rather than
moving the system cursor.

Run the actual native smoke test separately in an allowed interactive Windows session:

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj --configuration Release --no-build -- --sendinput-smoke
```

It submits +8 and -8 relative X counts and requires both SendInput calls to return 1.
Native structure sizes/alignment and deterministic error handling are also tested.
GetCursorPos displacement is not an automatic pass/fail condition; visible movement
and game feeling remain manual checks.

For the Android chain, start a fresh Sender after launching this bounded test:

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj --configuration Release --no-build -- --android-mouse-smoke
```

This test uses the production receiver/session/SendInput classes on UDP 50000 for
15 seconds, then cancels cleanly and validates accepted input, successful native
outputs and reliability counters. While it listens, ADB can start the unchanged
Android app and inject a small swipe. The 15-second limit is test lifetime only.
ADB injection verifies plumbing, not real-finger feel. Existing UDP inbound rules
are assumed; no firewall GUI or rule changes are performed.
