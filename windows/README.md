# rightpad Receiver for Windows 11

C#, .NET 8, WPF; one GUI process owns UDP reception, RAW motion, Single Tap
and SendInput. No third-party packages, Core project, service or IPC.

## Normal use

Double-click the built `Rightpad.Receiver.exe`. The GUI opens on **Motion** and
automatically listens on IPv4 `0.0.0.0:50000`. A matching .NET 8 Desktop Runtime
is required for this framework-dependent build; no SDK, PowerShell, Task Scheduler
or Codex is needed for daily use.

- Overview: actual connection observations, traffic counters, a compact Start with
  Windows toggle and one Start/Stop.
- Motion: RAW text and independently editable X/Y sensitivity.
- Tap: duration, axis-aligned movement threshold and click hold.
- Diagnostics: actual Receiver counters/state only.
- Minimize keeps receiving and remains a normal taskbar minimize. The title-bar X
  hides MainWindow to the system tray without stopping ReceiverRuntime or releasing
  UDP 50000. Double-click the tray icon or choose **Open rightpad Receiver** to
  restore and activate the window. Tray **Exit** stops input, releases buttons
  best-effort, flushes pending settings, disposes the tray icon and exits. Page
  changes never restart input.

Full UI contract: [RECEIVER_UI.md](../docs/RECEIVER_UI.md).

Header states are Receiver Stopped, Starting…, Waiting for Android, Connected,
Disconnected, Stopping… and Error. Connected is green and stays stable during
touch or idle heartbeats; Disconnected/Error are red, other states gray.
Current-run heartbeat or accepted Touch renews a local monotonic 2000 ms presence
deadline. Expiry clears old input once, while retaining senderRunId/sequence.
The independent two-second Touch silence timeout remains diagnostic only.
Overview Last Seen shows presence age or Never. No runId is shown in the UI.

Runtime defaults are 7/7 sensitivity and Single Tap 300 ms / 8 px / 25 ms.
Valid edits apply without Apply, Save or restart. Sensitivity is sampled once
per accepted packet; duration/threshold are captured at DOWN; each click request
owns its hold. Small tap motion remains normal RAW output. No 1:1 desktop-pixel claim.

Settings auto-save after 500 ms to
`%LocalAppData%\rightpad\settings.json`. Only the five input settings are stored.
Missing/bad fields fall back to defaults; file errors do not stop input.
Save failure is nonmodal and keeps the in-memory settings active.

Start with Windows writes the current executable as a quoted command to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name
`rightpad Receiver`. Registry is the source of truth; an absent, malformed,
stale or different executable displays Off. Enabling repairs the value and
disabling deletes only this value. Failures are nonmodal and never stop input.
The setting is not stored in settings.json. A GUI-only named Mutex prevents a
second manual GUI launch from creating another runtime or binding UDP 50000.
Hiding to the tray retains that mutex. Start with Windows remains independent:
login startup continues to show MainWindow normally rather than starting hidden.
The 2026-09-09 implementation check passed a warning-free Release build and all
110 Windows tests, real WPF Enable/Disable/Enable with final On, GUI Stop/Start,
200% DPI minimum layout, second-instance rejection, Protocol v2 recovery and
native Android RAW/Single Tap smoke. Three real Windows reboot/login validations
then passed. One transient input-loss observation during the first run recovered
without restarting either side, did not recur in the next two runs, and has no
confirmed root cause; no speculative workaround was added.

## Build and development launch

```powershell
dotnet build C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj -c Release
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj -c Release --no-build
```

The test executable is dependency-free and returns nonzero on failure. Ordinary
tests use fake mouse output and temporary loopback ports; they do not inject input.

Persistent Receiver launched by Codex must use the independent project launcher:

```powershell
& C:\rightpad\windows\tools\RightpadReceiverTask.ps1 -Mode Stop
# Build Release after stopping the previous binary.
& C:\rightpad\windows\tools\RightpadReceiverTask.ps1 -Mode Start
& C:\rightpad\windows\tools\RightpadReceiverTask.ps1 -Mode Status
```

Stop an existing development runtime before rebuilding its Release files. Start
ensures the fixed `Rightpad Receiver Dev` task and starts the **WPF GUI**, passing
`--dev-log-dir`. The GUI reads the normal user settings file; automated tuning
checks must restore 7/7 and 300/8/25 before leaving it for acceptance.

The task uses current-user InteractiveToken, Limited/LUA, no triggers, no stored
password, no time limit and no automatic restart. The Receiver must match the
current user's explorer SessionId, Medium integrity and Default desktop.
Persistent Receiver must not be a direct or indirect Codex-shell child: the
previous launch method inherited KILL_ON_JOB_CLOSE. There is no fallback child
launch, product service or watchdog.

This `Rightpad Receiver Dev` Scheduled Task remains a Codex-only development
harness. Product login startup uses only the HKCU Run value; the WPF application
does not call the launcher or modify the development task.

The independent wrapper explicitly retains/waits for the WinExe process handle.
It writes receiver.log, start.json, receiver.json and exit evidence under ignored
`windows/test-results/receiver-runtime/`. Status returns RuntimeLog, identity,
PID and UDP ownership. Stop requests the verified WPF window to close normally
before stopping the task/forcing only a verified residual. Use it with input idle;
forced termination cannot guarantee LEFT UP or final settings/log flush.
Ensure/Start/Stop/Status remain public modes; Run is scheduler-internal.

## Explicit development CLI

No arguments now means GUI. Protocol diagnostics must be explicitly selected:

```powershell
# Human-owned interactive console only; stop any listener first.
.\Rightpad.Receiver.exe --diagnostics --dev-log-dir C:\rightpad\windows\test-results\protocol
.\Rightpad.Receiver.exe --raw-mouse --sensitivity-x 5 --sensitivity-y 6 --tap-max-duration-ms 300 --tap-movement-threshold-px 8 --click-hold-ms 25 --dev-log-dir C:\rightpad\windows\test-results\raw
```

These modes reuse ReceiverRuntime without a WPF window. WinExe does not imply a
console: a parent console is attached when available for Ctrl+C, while diagnostics
are explicitly written to receiver.log. The default dev log directory is
`%LocalAppData%\rightpad\diagnostics`. Use a waiting process API when automating a
bounded dev run; do not rely on shell GUI-process wait/redirection behavior.
`--dev-settings-path <path>` optionally isolates GUI persistence for tests.
It is not a profile feature. Dev RAW options do not write user settings.

The dev CLI retains finite-positive gains/thresholds and positive int32 duration
values. GUI/JSON product ranges are sensitivity .1..30, duration 50..1500 ms,
threshold .5..100 px, hold 1..200 ms. Errors in dev runtime return nonzero;
GUI errors stop only the internal runtime and leave an Error/retry view.

## Frozen input behavior

[INPUT_PROTOCOL.md](../docs/INPUT_PROTOCOL.md) is authoritative:
UDP 50000, version 2 only, DOWN=1/MOVE=2/UP=3, little-endian 20-byte header plus
16 bytes per sample. senderRunId is uint64 at offset 2, count/session/sequence
at offsets 10/12/16. DOWN/UP have one sample, MOVE has one or more. HEARTBEAT=4
is exactly 10 bytes (version/type/runId), sent once every 500 ms while foreground.
No CANCEL wire event, handshake, reorder buffer or generic reconnect framework.
The decoder rejects malformed lengths, version/events/counts and non-finite
coordinates; original sample order and uint64 nanoseconds are retained.

Sequence ordering is ordinary uint32 comparison across touch sessions:
first valid current-run Touch establishes baseline, greater is accepted, forward gaps accumulate
delta-1, equal is duplicate, lower is old. No wraparound support. Only accepted
packets enter motion/gesture. Remote addresses are diagnostics only, with no
sender locking or source authentication.

Unknown senderRunId switches only on fully validated HEARTBEAT/DOWN. A runtime-local
HashSet permanently retires previous IDs for that runtime. Switching clears input
and Touch sequence baseline; new sequence 0 is accepted without restarting WPF,
ReceiverRuntime or socket. Counters/settings survive. Retired packets cannot alter
presence, IP, sequence or output. Presence timeout preserves the sequence baseline;
subsequent orphan MOVE/UP can prove presence but cannot move/click until a new DOWN.

| Counter | Meaning |
|---|---|
| receivedPackets | All datagrams, including rejected and duplicate |
| invalidPackets | Rejected by decoder |
| acceptedPackets / acceptedSamples | Valid baseline/in-order/gap input |
| sequenceGapEstimate | Cumulative forward gaps, not final loss |
| duplicatePackets / oldPackets | Equal / lower than last accepted sequence |
| inputTimeouts | Once after each rearmed two-second accepted-input silence |
| heartbeatPackets | Valid current-run heartbeats; no Touch/sample/sequence contribution |
| outdatedRunPackets | Decoded datagrams from a retired run |
| presenceTimeouts | Once per current-run presence expiry, independently of inputTimeouts |

UI Hz uses counter deltas over real Stopwatch time, sampled at 5 Hz with an
approximately one-second history. This is received throughput, not hardware scan
rate or one-way latency. UI never dispatches per sample or locks input processing.

RAW: matching DOWN resets the baseline/residual without output; MOVE/UP samples
produce ordered absolute-position differences, fixed axis gain and truncation
toward zero with independent residuals. Final UP delta is processed; residual
is discarded at session end. Wrong/orphan sessions have no motion effect.
No sample merging, output ticker, clamping, synthetic split, acceleration or filter.
An unrepresentable int32 output stops the runtime.

Single Tap: each raw sample is tested relative to DOWN per axis (inclusive);
once outside either bound, the candidate stays rejected even if it returns.
Matching UP must also be within bounds and event-time duration nonnegative and
within the DOWN snapshot. A valid tap requests one LEFT DOWN then locally timed
LEFT UP. Overlapping requests are serialized with their own hold durations.
System timer scheduling can release later than requested. Motion is independent.

Every SendInput call checks the actual inserted count and available Win32 error.
Failure stops input, clears pending clicks and best-effort releases LEFT UP.
Timer failures cancel idle receive too. Forced kill or persistent native failure
cannot guarantee release. Windows pointer settings and app behavior can still
affect output; rightpad never changes them automatically.

## Native and Android smoke

Run native checks in the current interactive user context, outside the restricted
Codex SendInput sandbox. The default test suite never calls native SendInput.

```powershell
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj -c Release --no-build -- --sendinput-smoke
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj -c Release --no-build -- --sendinput-button-smoke
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj -c Release --no-build -- --android-mouse-smoke
dotnet run --project C:\rightpad\windows\Rightpad.Receiver.Tests\Rightpad.Receiver.Tests.csproj -c Release --no-build -- --android-tap-smoke
```

Move smoke submits +8/-8 counts and checks inserted=1, not cursor displacement.
Button smoke uses an inert test window and validates native DOWN/UP. Android
mouse/tap checks own UDP 50000 for bounded 15/25-second runs, so stop the GUI
listener first. Start the unchanged phone app and inject a small swipe/tap after
the readiness signal. These verify plumbing, not human gaming feel.

For GUI E2E, `Rightpad.Receiver.Tests.exe --gui-android-smoke` uses the already-running GUI and an inert click target for a 10 px swipe and one tap. A test-only native hook verifies injected movement and exactly one LEFT DOWN/UP without stopping the Receiver. It owns no UDP listener. Android must be awake with rightpad foreground. Development touch_start/touch_end log lines also expose sender sequence and cumulative RAW/click counters.

For GUI E2E, use the independent launcher, edit real controls, verify settings
round-trip/default restoration, exercise GUI Stop/Start and inspect per-run
motion/button summaries. A small native UI Automation check needs no added
framework. Real-finger RAW and Single Tap human validation passed before WPF;
this UI migration still awaits user visual/live-tuning/feel acceptance.

After Android Sender restart or reinstall, keep the existing v2 WPF Receiver
running. Verify Disconnected/Connected, a new senderRunId and sequence 0 acceptance,
unchanged WPF PID/Runtime RunId, foreground Android, no Sender error/overflow,
UDP and native RAW/Single Tap. Pause/resume keeps the same Sender run and sequence.
Use the independent launcher if Windows itself needs to be started or updated.

## Implementation files

- Existing decoder, packet models, statistics, UDP, motion, gesture, buttons and
  SendInput classes remain in Rightpad.Receiver.
- Runtime/ReceiverRuntime.cs owns runs; RuntimeStatsSnapshot is the UI read model.
- Settings contains immutable settings/store and JSON/debounce persistence.
- Startup contains the small HKCU Run access boundary and source-of-truth logic.
- App/MainWindow, the framework-provided WinForms NotifyIcon, four Views,
  NumericEditor and DarkTheme form the WPF surface.
- MainViewModel, SettingsViewModel, StartupViewModel and RuntimeStatsViewModel share page state.
- Existing tests remain, plus settings/runtime/settings-boundary regression files.

No filter, FIR/Second Order, Double Tap Drag, right click, scroll, HID/driver,
profiles/discovery/multi-device, generic reconnect frameworks, cloud/accounts/plugins,
tray notifications/telemetry/runtime controls, updates, graphs/log viewer, theme
selector or custom title bar.
