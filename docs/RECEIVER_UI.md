# rightpad Receiver UI v1

Implemented 2026-09-09. WPF visual/feel acceptance by the user remains separate
from automated verification. AGENTS.md remains authoritative.

Protocol v2 connection update verified 2026-09-09: 100 Windows tests passed;
605.6 seconds foreground idle stayed Connected without new presence timeouts.
Android background/resume and force-stop/reopen recovered with the same WPF PID
and Receiver Runtime; real native RAW/Single Tap checks passed. Overview and
Diagnostics fit at the minimum 860×600 DIP on 200% DPI. The user subsequently
accepted the Connected/Disconnected lamp, foreground/background behavior,
restart recovery, unchanged RAW feel, Single Tap and no recovery jump. Detailed
evidence is in PROJECT_STATE.md; v1 records below remain historical.

## Product and architecture

Windows 11 only, C#, .NET 8, WPF. Existing Rightpad.Receiver is a WinExe; the
tests continue referencing that assembly through InternalsVisibleTo. No Core
project, external MVVM/DI packages, service, IPC or second product process.

Double-click Rightpad.Receiver.exe: load settings, show MainWindow on **Motion**,
then automatically start its internal ReceiverRuntime. No CLI or scheduler is
needed for normal use. Minimize keeps receiving; page changes do not start or
stop input. The title-bar X hides MainWindow to a system tray icon while the
ReceiverRuntime and UDP listener continue unchanged. Double-clicking the icon or
choosing **Open rightpad Receiver** restores and activates the window. Only the
tray **Exit** command stops the runtime, flushes settings, disposes the tray icon
and shuts down the application. Standard minimize continues to use the taskbar.

Existing UDP -> decoder -> sequence acceptance is retained. Accepted packets
feed TouchSessionProcessor / RawMotionProcessor and independent GestureProcessor.
LeftButtonController performs locally timed SendInput clicks. ReceiverRuntime
owns their lifetime in the same process as the GUI.

## Visual and page contract

Primary visual reference: `C:\Users\zhq\Desktop\接收器.png`. Use its restrained
dark technical panel, sidebar, blue selection, green input activity, subtle
borders and small rounded cards. Do not copy its unavailable data or controls.
Standard Windows title bar with the minimal DWM immersive-dark attribute; no
custom chrome, effects, gradients or animations. Native window buttons, resize,
Snap and system behavior remain intact.

Initial size 1100 x 720 DIP, minimum 860 x 600 DIP, resizable. Initial size is
limited to the working area. One persistent header, 180 DIP navigation, and
scrollable content. Background #10151C, cards #19212B, blue #3478F6, green
#4DCB68. Main text #E8EDF4, secondary #A0ADBF. The native title bar follows Windows.

Four UserControls selected by a ListBox / ReceiverPage enum / ContentControl:

| Page | Contents |
|---|---|
| Overview | Connection: status, current presence Android IP, Last Seen, listener, sample/packet Hz, Gap/Old/Invalid. Receiver: runtime state, a compact Start with Windows toggle, and one Start/Stop button. |
| Motion (default) | Sensitivity X/Y plus integer Smoothing Tau and Support numeric editors, followed by a read-only summary of the fixed production algorithm. No cadence selector, slider or mode dropdown. |
| Tap | Tap settings: duration, movement threshold, click hold. No Enabled row or toggle. |
| Diagnostics | Input/Transport: sample/packet Hz, Gap/Old/Invalid/Duplicate/Input Timeout, Heartbeat Packets, Presence Timeouts, Outdated Run Packets. Receiver: state, backend, touch session, last accepted age, last remote IP. |

Device model, Sender Errors and Queue Overflow do not appear: Protocol v2 has
no source for these values. No N/A placeholders, fake zero counters, graphs,
packet viewer, Settings/About pages or unimplemented controls.

## State semantics

| Header | Meaning |
|---|---|
| Receiver Stopped (gray) | Internal runtime stopped, no listener. |
| Starting… | Establishing a fresh runtime. |
| Waiting for Android (gray) | Listening, no admissible sender run yet. |
| Connected (green) | Current-run heartbeat or accepted Touch within 2000 ms. |
| Disconnected (red) | Established sender run has no presence for 2000 ms. |
| Stopping… | Cancellation and cleanup in progress. |
| Error (red) | Actual bind, receive or mouse output failure; GUI stays open. |

Connected stays stable during Touch and idle heartbeat reception. Last Seen is
the local Stopwatch presence age (for example 0.3 s ago), or Never. Presence
expiry clears session/residual/gesture/pending and held button input. The separate
two-second Touch silence timeout remains diagnostic-only. Starting/Stopping are gray.
Touch Session describes local state only. `0.0.0.0:50000` is a listener, not an
address for Android to target. Start/Stop transitions disable the operation;
errors leave a Start retry in Overview and a concise global error message.

## Parameters and editing

| Parameter | Default | Display precision | Product range | Button/key step |
|---|---:|---:|---:|---:|
| Sensitivity X/Y | 7.0 | 1 decimal place | 0.1..30.0 | 0.5 |
| Smoothing Tau | 24 ms | Integer | 8..60 ms, integer | 1 ms |
| Support | 120 ms | Integer | 40..300 ms, integer | 5 ms |
| Tap Max Duration | 300 ms | Integer | 50..1500 ms, integer | 10 ms |
| Movement Threshold | 8 px | Up to 2 decimal places | 0.5..100 px | 0.5 px |
| Click Hold | 25 ms | Integer | 1..200 ms, integer | 1 ms |

Native TextBox supports typing and copy/paste; +/- and keyboard Up/Down step.
Sensitivity displays one decimal place and accepts at most one decimal place in
direct input; Tau and Support accept integers only; threshold displays up to two. Enter/blur formats,
Escape restores the last successfully saved value. Editors operate only on one
shared draft across Motion and Tap. Empty/unfinished decimal text remains a UI
draft; invalid/out-of-range text has an inline hint and disables Save. Only the
single footer Save button commits settings; Enter, blur, page navigation and Exit
do not apply or persist a draft. Step size does not restrict direct integer entries
such as 301 ms.

Ranges for Tap are verified from the old Moonlight PreferenceConfiguration.
Sensitivity's product range was explicitly approved for rightpad; it is not the
old project's combined effective gain range. Core/dev CLI retains its broader
finite-positive values and positive int32 milliseconds.

RAW means fixed gain without filtering, not 1:1 desktop pixels. Threshold checks
X and Y separately against DOWN using Android touch coordinate pixels. It is
neither Euclidean distance nor desktop pixels; small RAW motion is not suppressed.

## Runtime settings boundaries

RuntimeSettings is an immutable eight-field record. RuntimeSettingsStore publishes
the complete reference using Interlocked.Exchange; readers use Volatile.Read.

- Sensitivity: successful disk Save followed by committed publication atomically
  replaces an immutable X/Y pair. Production Motion reads that pair at each real
  sample's canonical target accumulation, including mid-contact MOVE and the UP
  endpoint. Draft/failed Save never changes live gain. Existing targets, pending
  displacement, realized history and Q0-C state are preserved. No settings read
  is added to the 1000 Hz output tick.
- Smoothing Tau and Support: captured once when a production Receiver run is
  constructed. Tau is the finite-critical time constant; Support is the maximum
  retained history/truncation duration, not a fixed added delay. They are
  independently configurable and no Tau/Support ratio is imposed. A successful
  Save updates the committed snapshot but never rebuilds or changes the current
  run's kernel; Stop/Start constructs the next run from the saved values.
- Motion shows Active from the actual running MotionConfiguration and Saved from
  committed settings. A mismatch while running displays Restart required.
  Restart Receiver is enabled only for that mismatch, outside Save and lifecycle
  work. During async restart it reads Restarting..., and Save, numeric editors
  and Start/Stop are disabled. A stopped receiver retains its ordinary Start.
- Restart holds the Runtime lifecycle lock across complete Stop/cleanup and new
  Start. It retries the same committed target once, then attempts the previously
  active Motion configuration with current committed sensitivity and gesture
  settings. Recovery never writes settings.json or republishes old settings.
  The WPF process remains alive. Successful recovery shows the old Active/new
  Saved mismatch and a nonmodal error with both target failures. Failed recovery
  shows a high-priority error with all failures and leaves manual Start available.
- Duration/threshold: capture at accepted DOWN, retain for that entire gesture.
- Click Hold: read when requesting the click. Every queued request owns its hold
  duration; later changes cannot retime an active or queued click.

Changes to the proven processors are small parameter overloads and a queue of
click durations. No packet reordering, filtering or output scheduling is added.

## Persistence

`%LocalAppData%\rightpad\settings.json`:

```json
{
  "sensitivityX": 7.0,
  "sensitivityY": 7.0,
  "smoothingTauMs": 24,
  "smoothingSupportMs": 120,
  "tapMaxDurationMs": 300,
  "tapMovementThresholdPx": 8.0,
  "clickHoldMs": 25,
  "doubleTapIntervalMs": 130
}
```

System.Text.Json only. Missing file uses defaults. Invalid JSON/root or read
failure uses defaults; invalid/missing/null/wrong-type/out-of-range fields fall
back individually, preserving valid fields. Unknown fields are ignored. Settings
problems never prevent Receiver startup; recoverable warnings are nonmodal.
Older six-field files that omit `smoothingTauMs` or `smoothingSupportMs` load the
24/120 validated baseline without a migration file. An invalid Tau or Support
falls back independently while every other valid field remains loaded.
`motionCadenceHz` is not part of the eight-field product schema. Older files
that contain either 250 or 1000 are accepted without warning; the field is ignored,
ordinary startup still uses fixed production 1000 Hz, and the next normal settings
save omits it. Explicit `--dev-motion-mode` remains authoritative and can select
the retained fixed 250/500/1000 development modes with their original fixed
kernel parameters; product Tau/Support never replace a dev mode's parameters.

UI edits change only the shared draft. Save validates all eight fields, immediately
writes a same-directory temporary file and replaces the target on background I/O,
then atomically publishes one complete RuntimeSettings snapshot only after the
write succeeds. A failed save displays `Settings save failed. Changes were not
applied.`, leaves Runtime unchanged and retains the retryable draft. Unsaved draft
is discarded on application exit; shutdown FlushAsync only completes explicitly
queued or saved committed data. Tap settings from a successful Save affect
subsequent input actions. The low-level 500 ms Schedule/debounce path remains for
existing non-UI compatibility and tests, but SettingsViewModel does not use it.

## Statistics and threading

One global 200 ms DispatcherTimer pulls a RuntimeStatsSnapshot and updates UI
properties. RuntimeState, RunId, last accepted Stopwatch ticks, cumulative received
and accepted packets/samples, Gap/Old/Duplicate/Invalid/Timeout, touch session,
last remote IP, backend, real error, SenderPresence, HeartbeatPackets,
OutdatedRunPackets and PresenceTimeouts are observed. SenderPresence publishes
one immutable reference containing run identity/time/connected/IP. Numeric counters use atomic
operations. Snapshot fields are independent observations, not a transaction.

Sample Hz = accepted sample delta / real Stopwatch interval. UDP Hz = all received
datagram delta / real interval, including heartbeats, duplicates and malformed packets. A
roughly one-second history is used; delayed UI ticks use actual elapsed time.
Rates reset for new RunId and show a dash while stopped. These are received
throughput, not Android hardware scan rate. Gap is cumulative forward sequence
gap estimate, not final loss; Timeout counts diagnostic silence, not disconnects.

ReceiverRuntime starts one sequential async input loop via Task.Run; receiver
awaits use ConfigureAwait(false). No input call references Dispatcher, WPF controls
or PropertyChanged. UI never acquires the button lock. The existing local button
timer continues without GUI polling. No per-sample GUI allocations or dispatch.
Existing decoder/receive allocations are not reworked for this UI feature.

Three main ViewModels share settings and stats across pages. A small NumericField
model handles editing; NumericEditor code-behind handles only input interaction.
MainWindow handles navigation, the UI timer and the minimal tray integration.
A small testable behavior object distinguishes X-to-hide from explicit Exit and
restores hidden or minimized windows without changing ReceiverRuntime state.

## Windows login startup

The compact Overview toggle controls the current-user value
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` / `rightpad Receiver`.
The command is the quoted `Environment.ProcessPath`; it does not depend on the
working directory and supports spaces and Unicode. The Registry value is the
source of truth: absent, malformed, stale and other-executable commands display
Off. Enabling writes or repairs only that value; disabling safely deletes only
that value. A failed Registry operation leaves ReceiverRuntime running, re-reads
the actual value and shows a short nonmodal notice. This state is not stored in
settings.json.

The GUI entry uses a small named Mutex. A second GUI instance does not create a
second ReceiverRuntime or compete for UDP 50000; it displays a short notice and
exits. Development diagnostics and RAW command-line modes remain bounded,
explicit processes and do not take the GUI mutex.

## Runtime lifecycle and development

Start/Stop requests are serialized by a low-frequency async gate. Every Start
creates fresh counters, socket, processors, cancellation source and RunId. Stop
cancels and awaits input, clears gesture and queued clicks, attempts LEFT UP,
resets motion/session and releases the port. A new Run cannot start until the old
task and its cleanup complete. Errors stop only the runtime; GUI can retry.

Ordinary GUI startup constructs the Runtime with the sole 1000 Hz finite-critical
Q0-C Earned-Settle product algorithm and fixed 12 ms playout. Cadence does not
change during that process lifetime. Every Start snapshots committed Tau/Support
once into an immutable run configuration; the 1 ms tick path never reads the
settings store, parses strings, resizes history or rebuilds the kernel. General
Stop/Start uses the same serialized cleanup path and applies the latest saved
Tau/Support to the fresh run; explicit development modes are chosen only by launch
arguments and keep their fixed parameters.

No-argument entry is GUI. Explicit `--diagnostics` preserves protocol diagnostics;
`--raw-mouse` preserves RAW dev CLI and tuning options. Both reuse ReceiverRuntime.
Dev modes attach to a parent console when available for Ctrl+C and explicitly
write receiver.log (`--dev-log-dir`, default LocalAppData/rightpad/diagnostics).
`--dev-settings-path` supports an isolated GUI settings file for testing only.

`windows/tools/RightpadReceiverTask.ps1` remains development-only. Its independent
InteractiveToken/Limited Scheduled Task starts GUI with --dev-log-dir, retains the
process handle, waits for exit, and records receiver.log / identity / PID / exit.
Status checks user, explorer SessionId, Medium integrity, Default desktop and UDP
50000. With no explicit development Motion option it uses the fixed 1000 Hz product
mode; an explicit mode (including retained 250/500 Hz modes) remains a development
override. Normal product exit is the tray **Exit** command.
No Codex-shell persistent child, watchdog or product scheduler. Product login
startup is the separate HKCU Run value and never calls or modifies this launcher.

Protocol v2 Sender restart is recognized by senderRunId on valid HEARTBEAT/DOWN.
Android restart/redeployment retains the existing WPF PID, Runtime RunId, socket,
settings and cumulative counters; only the sender input baseline is replaced.
Retired runs cannot switch back. Android pause/resume keeps senderRunId/sequence.
The independent interactive launcher requirement remains unchanged.

## Verification and non-goals

Retain all existing valid tests, update entry coverage, and test settings fallback,
eight-field persistence, legacy cadence-field removal, debounce/latest-wins/flush/
failure, atomic publication,
packet consistency and residual retention, gesture and click snapshot boundaries,
Start/Stop/error recovery/old callback isolation, statistics and UI independence.
No large UI automation framework. Real WPF and SendInput/Android checks are
additional to unit/loopback tests; the first formal UI baseline has completed
human visual and input-feel acceptance.

No FIR/Second Order/filter, extra gestures, drag, right click, scroll, HID/driver,
profiles, discovery/multi-device, generic reconnect frameworks, cloud/account/plugins,
tray notifications/telemetry/runtime controls, updates, charts/log viewer, theme
selector or custom title bar.

## Implementation verification (2026-09-09)

- Release build: zero warnings/errors; automated suite: 79 passed, 0 failed.
- Real WPF inspected via native UI Automation and window captures: default Motion,
  all four pages, dark resources and minimum 860 x 600 DIP at 200% DPI. Overview
  and Diagnostics use compact columns so all fields/operation fit at minimum size.
- Real GUI edits saved 6.95/7.10 and 310/8.5/26, then a new GUI loaded those five
  values. Default settings restored to 7/7 and 300/8/25.
- Within one GUI runtime, two 20-unit deltas with live gain 6.95 then 7.10 yielded
  exactly 281 successful native X counts. GUI Stop released UDP; Start made a fresh Run.
- Standalone existing native movement/button smoke passed with no native failures.
- Android through the WPF runtime: 39 received datagrams, 31 accepted packets,
  58 samples, 8 expected duplicates, zero Gap/Old/Invalid. A 10 px swipe yielded
  70 X counts via 36 successful movement calls; one tap yielded one successful
  LEFT DOWN and one LEFT UP, zero button failures. Inactivity showed Ready · Idle.
- Android was not rebuilt or modified; awake MainActivity and sender logs verified.
- Human UI/live-tuning/RAW/Single Tap acceptance passed for the first formal UI baseline.

## Visual polish verification (2026-09-09)

- The native title bar uses DWM immersive dark mode with the documented legacy
  attribute fallback; no WindowChrome or custom caption was introduced.
- Cards use content-driven height, compact 32–34 DIP rows and fixed-width numeric
  editor columns. Motion and Tap parameter controls no longer spread across a
  wide window; Tap help text stays beside its related parameter and wraps at the
  minimum width.
- Page, card, normal and secondary typography now form a 30 / 18 / 14 / 12–13
  DIP hierarchy. Diagnostics values are right-aligned and accepted age switches
  from seconds to compact minutes and seconds after one minute.
- All four pages were captured at 1100 x 720 DIP and inspected at the 860 x 600
  DIP minimum on a 200% DPI display. Release build remained warning-free and all
  79 Windows tests passed. Live sensitivity, settings persistence, GUI Stop/Start,
  Android input acceptance and UDP 50000 were rechecked. Human visual acceptance
  passed for the first formal UI baseline.

## Start with Windows verification (2026-09-09)

- Final Release build: zero warnings/errors; automated suite: 110 passed, 0 failed.
- The real HKCU value began absent. The final WPF toggle performed Enable,
  Disable and Enable; reads confirmed the exact quoted current executable,
  deletion, and final On state.
- PID 20988 was stopped before the final build; its exit and UDP 50000 release
  were confirmed. The independent interactive launcher started fresh PID 9088
  as the current user in explorer Session 1, Medium integrity and Default desktop;
  UDP `0.0.0.0:50000` belongs to PID 9088.
- Receiver Stop/Start kept the WPF process, released/rebound UDP and recovered
  Connected from the unchanged foreground Android Sender heartbeat. The runtime
  log orders listener startup before sender admission and Connected presence.
- Overview was captured and inspected at 860 x 600 DIP on the 200% DPI display.
  The compact toggle is complete and the existing scroll layout remains usable.
- A bounded second GUI launch displayed the single-instance notice, exited 0,
  left one Receiver process, and did not change UDP ownership. GUI Android smoke
  produced 29 native movement events and exactly one LEFT DOWN / LEFT UP.
- Human validation passed three real Windows reboots: HKCU Run started the WPF
  Receiver, Runtime started, UDP 50000 listened, Android connected, and RAW Mouse
  and Single Tap worked. One transient input-loss episode occurred during the
  first validation while WPF remained Connected, then recovered without restarting
  Receiver, ReceiverRuntime or Android. It did not recur in the next two reboot
  validations; its root cause remains unconfirmed and no speculative workaround
  was added.
