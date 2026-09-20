# AGENTS.md

# rightpad Project Instructions

## 1. Project Identity

Project name:

```
rightpad
```

rightpad is a dedicated Android-to-Windows gaming input system.

The purpose is to turn an Android phone into a high-quality touch input device for Windows 11 gaming.

The project is focused on:

- Low latency
- Stable input feeling
- Predictable behavior
- Long-term maintainability

This is NOT:

- A remote desktop application
- A streaming application
- A general office mouse replacement
- A cloud service

---

# 2. Target Platform

## Supported

Android client:

- Touch input device

Windows receiver:

- Windows 11 only

## Not a current goal

Do not add support for:

- macOS
- Linux
- iOS
- Cloud platforms
- Mobile-to-mobile usage

unless explicitly requested.

---

# 3. Core User Requirements

## Touch Operation

The device is operated with:

- One finger only
- Portrait phone orientation
- Phone screen is not viewed during use
- Phone acts as a dedicated touch surface

Do not add:

- Multi-touch
- Two finger gestures
- Three finger gestures
- Pinch gestures
- Gyroscope input
- Accelerometer input

While the rightpad Android Activity is in the foreground, prevent automatic
screen timeout / screen-off. Do not keep the device awake after rightpad leaves
the foreground. Do not use WakeLock or background services unless explicitly
approved.

---

# 4. Input Behavior Requirements

## Mouse Movement

The primary input mode is:

Relative mouse movement.

Expected behavior:

```
Finger moves
        ↓
Mouse moves

Finger stops
        ↓
Mouse stops

Finger lifts
        ↓
Movement stops
```

The system must NOT behave like a joystick.

Do not implement:

- Cursor drifting
- Continuous movement after release
- Auto-centering
- Momentum movement

`Mouse stops` means that the Motion Engine does not create new artificial
movement, drift indefinitely or continue after release as a velocity-controlled
device. A selected fixed deterministic filter may have a short, measurable
settling period in which it finishes displacement already earned by the finger.
That settling must converge to the existing target, remain fixed for the selected
configuration and be validated in real games. It must not integrate old velocity
to invent additional distance.

---

# 5. Motion Feeling Requirements

The target feeling:

- Smooth
- Stable
- Accurate
- Predictable
- Suitable for games

The same physical finger movement should always create the same response.

Avoid hidden behavior changes.

## Motion Quality Priority / Not Esports-Latency-First

Rightpad is game-first, but it is not esports-latency-first. The leading Motion
objective is continuous camera quality: smooth motion, low wobble, stable velocity,
natural continuity and gimbal-like visual stability. Precise micro-control,
natural control feel and long-session comfort follow closely. Fixed deterministic
feel, predictability and muscle-memory consistency remain mandatory foundations.

Fast-turn responsiveness, reversal response, stop response and absolute minimum
latency still matter and must be measured, reported and validated with human game
testing. They are not the sole ranking criteria. Do not reject a candidate merely
because it adds a fixed 10, 20, 30 or other number of milliseconds relative to the
current baseline. A fixed, deterministic and predictable response cost may be
accepted when it produces a meaningful improvement in trajectory stability and
does not noticeably damage control in real games. No universal acceptable or
unacceptable millisecond threshold is established; objective evidence and human
game experience decide the tradeoff together.

Stronger smoothing does not authorize adaptive feel. It means selecting one
stronger fixed configuration whose gain, tau, damping, coefficients, window,
cadence, playout delay and quantization behavior remain fixed for the run.

## Settling Is Not Glide

A fixed filter or follower may finish displacement already accumulated from real
finger movement after the true target displacement becomes fixed. This is filter
settling or completion of previously accumulated real displacement. It is allowed
only when it is deterministic, measurable, converges to that existing target and
does not continue indefinitely.

Glide or artificial inertia instead integrates old velocity or otherwise creates
distance beyond the real target. It remains prohibited. A fixed filter may finish
already-earned displacement; it may not invent new displacement. This distinction
preserves Relative Mouse semantics and does not permit momentum or joystick
behavior.

## Fixed Feel / No Runtime Adaptation

Explicit manual-Save exception (2026-09-18): the user may hot-apply committed
Sensitivity X/Y after a successful disk Save and settings publication. The pair
switches atomically for new real input displacement, including mid-contact.
Existing targets, pending displacement and realized history are never rescaled.
This is a deliberate user configuration change, never automatic adaptation.
Tau/Support and all other Motion shaping parameters remain frozen per run.

Runtime motion feel must be stationary. Once a Motion configuration is selected,
every parameter that affects control feel must remain fixed for that run. The same
physical finger movement under the same fixed configuration must produce the same
motion response. This is required for stable muscle memory, predictable control,
fair A/B comparisons and reproducible experiments.

If present in an algorithm, feel-affecting parameters such as sensitivity, gain,
smoothing strength, filter coefficients, filter window, follower time constant,
damping parameters, output cadence and playout delay must not change according to
finger velocity or acceleration, detected noise, touch sampling rate, packet-arrival
jitter, network conditions, CPU or GPU load, game frame rate, game type, game state,
estimated user intent or any other runtime condition.

In particular, do not implement behavior such as:

```text
Slow movement -> stronger smoothing
Fast movement -> weaker smoothing

Low speed -> one gain / tau / window
High speed -> another gain / tau / window

Detected noise -> automatically increase smoothing
More network jitter -> automatically enlarge playout delay or jitter buffer
Less network jitter -> automatically reduce delay

Low FPS / high FPS -> different Motion parameters
Different games -> automatically selected Motion feel
```

This also prohibits runtime-adaptive sensitivity, gain, smoothing, tau, damping,
FIR coefficients, window length, deadzone, playout delay, jitter-buffer depth and
velocity-, noise-, FPS-, network-, game- or system-load-based Motion mode switching.
A speed threshold that selects one feel for micro-aiming and another for fast turns
is prohibited even if its benchmark or theoretical tradeoff appears better.

Development may compare multiple fixed candidates, for example tau values of 3,
5 and 8 ms, fixed windows of 4, 6 and 8 ms, fixed playout delays of 8, 12 and 16 ms,
or fixed output rates of 250 and 500 Hz. Each candidate must keep its own values
fixed. Offline data, automated tests and human game A/B may then select one fixed
configuration. Experiment modes are explicit development choices; they do not
authorize automatic runtime switching.

Fixed feel does not require a simple algorithm. A measured, deterministic FIR,
fixed-window reconstruction, fixed second-order follower or fixed resampler may be
evaluated and adopted when all feel-affecting parameters remain fixed. The product
goal remains relative displacement semantics with gimbal-like trajectory quality:
smooth motion must not be obtained through velocity control, dynamic gain, glide
or inertia.

Deterministic fallbacks may react to correctness, safety and lifecycle conditions,
including malformed input, missed deadlines, buffer overflow, session or sender-run
invalidation and shutdown. For example, a late ticker may skip an obsolete tick and
resume the same fixed cadence. Such handling must be specified and repeatable; it
must not change the gain, tau, window, delay, mapping or other normal Motion feel
after recovery, and faults must not be used as a pretext for adaptive feel.

This section is the binding project-level rule. `docs/MOTION_ENGINE.md` must conform
to it, and experiment or reference documents must conform to both. An adaptive
candidate is ineligible as a product Motion solution unless the user explicitly
changes this requirement.

## Reference Implementations Are Not Targets

Moonlight TrackpadContext is a reference, a known-smoother baseline and design
evidence. It is not Rightpad's target, upper bound, gold standard, required
architecture or required parameter set. Its fixed-rate output, follower and other
trajectory-shaping choices may inform experiments, but its second-order model,
time constant, caps, glide and output semantics do not become Rightpad requirements.
Glide remains incompatible with the current Rightpad product constraints.

Rightpad should not merely match Moonlight. The long-term goal is to find the best
fixed-feel Motion system under Rightpad's own Relative Mouse, control-quality and
gaming requirements, and where practical to outperform both the current Rightpad
baseline and the current Moonlight reference. This is a product goal, not a claim
that either comparison has already been won. No reference implementation or
experimental candidate preselects the final algorithm.

---

# 6. Forbidden Features Unless Explicitly Approved

Do not add:

## Motion related

- Mouse acceleration
- Dynamic sensitivity
- Speed-based gain
- Adaptive feeling
- Prediction
- AI trajectory prediction
- Inertia simulation
- Glide movement

## Product related

- Accounts
- Cloud synchronization
- Online services
- Multi-device management
- Plugin marketplace
- Automatic game detection

## Input related

- Extra gestures
- Extra buttons
- Unrequested control modes

---

# 7. Architecture Rules

## Security intentionally out of scope

rightpad is a personal-use system for one user, one Android phone, and one
Windows 11 PC on a trusted local network. Do not implement authentication,
pairing, PINs, encryption, TLS/DTLS, tokens, certificates, signatures, HMAC,
anti-replay systems, multi-user permissions, or security handshakes.
Do not add first-sender locking or source authentication/isolation.

Explicitly approved LAN discovery exception (2026-09-12): one Android phone may
automatically find either of two Windows PCs at different locations, normally
not simultaneously on one LAN. Use independent Receiver Discovery v1 UDP 50001;
Touch v2 UDP 50000 stays unchanged. OFFER source IPv4 is authoritative; no fixed
production IP, first-NIC heuristic, manual host selector or cloud. See
docs/DISCOVERY_PROTOCOL.md for the selection and clean target-transition contract.

Packet length/version/field validation, malformed packet rejection, and
timeout/stuck-input fail-safes remain required reliability measures.

Explicitly approved click haptics exception (2026-09-13): independent Windows →
Android Haptic Feedback v1 UDP 50002 confirms only Windows normal-click output.
Android validates current discovery source IP, Sender-owned run and raw UP
identity for stale rejection/dedupe; this is not authentication and does not
change Touch admission. A small independent sender task/listener thread is
authorized for this best-effort side effect. No Android gesture recognition,
input dependency on feedback, background service or haptic setting is added.
See docs/HAPTIC_FEEDBACK_PROTOCOL.md.

Phase 5A.4 restores only the final accepted Touchpad CLICK execution to
performHapticFeedback(HapticFeedbackConstants.CONFIRM), using system tuning.
Receiver confirmation, RPHF bytes, identity validation and dedupe remain unchanged;
never vibrate locally on Touchpad DOWN/MOVE. TouchpadClickFeedback is independent
of ScreenControlFeedback's 10 ms / 255 policy. Failure remains best-effort, with
no one-shot fallback or retry. The user rejected the Phase 5A.3 one-shot feel and
selected system CONFIRM and confirmed blind distinction on the actual device.
Do not adjust waveforms automatically. VIBRATE remains for Screen Controls.

## Android Responsibility

### Approved LR gesture, transport and config (2026-09-20, Phases 6A–6C)

An independent reusable SlideControlLRGesture may express BASE/LEFT/RIGHT/UP,
with horizontal threshold priority, first commit wins, Design A takeover and
downward movement doing nothing to pending Tap/LongPress. Defaults are Left 12 dp,
Right 3 dp, Up 2 dp, Tap Hold 25 ms, Long Press 400 ms, snapshotted at DOWN.
Its per-definition SYSTEM_CLICK feedback uses EFFECT_CLICK on API 29+, 20 ms / 120
on API 26–28, legacy 20 ms otherwise, only on DOWN/first direction commitment.
Phase 6B registers xbox.x.slide_lr (label X) through the same layout/editor/router
as B. Definition mapping is BASE=X, LEFT=DpadLeft, RIGHT=DpadRight, UP=DpadUp;
stable-ID contributions merge in GamepadAggregator. Existing logical bits 2/13/14/11
and the 30-byte v2 type5 packet, dwell, FORCE_NEUTRAL and single Xbox360 device are
reused unchanged. Design A publishes one full replacement. Phase 6C adds separate
SlideControlLRSettings under controls.x, registry wire ID 2 / kind 2 and five
Receiver Controls fields. Disk-first explicit Save publishes one global revision
with B+X. RPCT v2 uses length-prefixed records (B 12 bytes, X 14 bytes; total 62).
Android validates complete B+X before atomic cache replacement and snapshots at
DOWN. v1 B-only transition reading is retained without acknowledging complete
B+X sync. No GAMEPAD_STATE, type6 request, layout, dwell or haptic change is allowed.
Existing B STRONG_ONE_SHOT stays 10 ms / 255 and Touchpad stays system CONFIRM.
See docs/SCREEN_CONTROLS.md for the current contract and validation scope.

### Approved Screen Controls exception (2026-09-19, Phases 1–4)

Phase 5A.2 specifies local generic Screen Control PRESS and first
DIRECTION_COMMIT haptics: fixed 10 ms amplitude 255 one-shot on API 26+, or
legacy 10 ms on older APIs. Do not prefer HEAVY_CLICK or increase the duration
without a new user instruction. VIBRATE is a manifest permission, with
no runtime prompt. LongPress, UP, CANCEL, Tap pulse, editor, Settings and Power
produce no Screen Control haptic. Feedback failure never controls input. Existing
Windows-confirmed Touchpad RPHF semantics remain unchanged; its separate final
effect follows the explicit Phase 5A.4 rule above. Distinct haptic identities must
be confirmed by the human on the actual device before claiming acceptance.

Registered on-screen controls may recognize their own single-finger SlideControl
gestures on Android. The current `xbox.b.slide` instance maps Tap/LongPress to B,
Slide Up to Y and Slide Down to A. Design A replaces held B with Y/A immediately.
This exception does not move mouse gesture recognition or Motion to Android.

Receiver Controls settings are the sole behavior source: defaults are Up 0.7 dp,
Down 3.0 dp, Tap Hold 25 ms and Long Press 400 ms. Explicit successful disk Save
publishes a full config epoch/revision snapshot over the existing UDP 50002
worker/listener. Type6 requests use the existing Android Sender/UDP 50000 and
recover lost updates. Failed Save never publishes. Android caches behavior only
in memory and snapshots it at DOWN; the active gesture remains unchanged.

The generic layout editor supports move, edge/corner resize, integer X/Y/W/H,
Save/Cancel/Reset and bounds validation. Visual Rect equals Hit Rect. Defaults
are square; saved rectangles are allowed. Android persists layout independently
by stable ID. Preserve the current user layout during builds and tests; never
assume a historical test rectangle is still current. Instrumentation snapshots
the user's file, temporarily exercises a fixture and restores the exact file in
finally. Do not reset user layout without instruction.

Generic GAMEPAD_STATE is v2 type5, exactly 30 bytes, carrying full Xbox state,
minimumDwellMs, flags including FORCE_NEUTRAL and zero reserved. Touch types 1–4
remain byte-for-byte unchanged. Held state refreshes every 100 ms; Receiver uses
an independent 300 ms lease and a low-overhead monotonic dwell deadline task.
Ordinary Tap Neutral respects dwell; safety Neutral and new non-Neutral states
bypass the old dwell. Do not increase Android Tap Hold to hide network jitter.

The independent libvirtualhid xbox_360 backend uses ABI2; its failure must not
stop mouse input. Lifecycle release includes cancel/pause/editor/target/run change,
disconnect/lease/Stop/Safe Restart/Dispose/error. No B-specific transport logic,
extra gesture, socket/thread, service, authentication or Motion change is approved
by this exception. Existing shared UDP 50002 and its worker/listener are reused.
See docs/SCREEN_CONTROLS.md, docs/GAMEPAD_PROTOCOL.md and
docs/CONTROL_CONFIG_PROTOCOL.md. Automated ADB/XInput tests are functional evidence.
The user explicitly confirmed final real-person acceptance of Phases 1–6C on
2026-09-20 before authorizing the combined source/docs/tests commit and normal push.
Preserve the accepted parameters; future behavior changes require fresh acceptance.

For the mouse path, Android is only an input sensor.

Android is responsible for:

- Touch capture
- Historical touch samples
- Timestamp collection
- Raw data transmission

Android should NOT decide:

- Mouse movement behavior
- Sensitivity
- Smoothing
- Mouse gesture meaning
- Windows input generation

---

## Windows Receiver Responsibility

Windows receiver owns:

- Input processing
- Motion algorithms
- Gesture recognition
- Configuration
- Mouse output

The receiver is the main control center.

---

# 8. Configuration Rules

User-adjustable parameters should normally exist on Windows receiver.

Reason:

- Easier tuning
- No Android rebuild
- Better testing workflow

Avoid placing important tuning parameters only inside Android.

---

# 9. Engineering Philosophy

## Game-First / Necessary Complexity Rule

rightpad's formal product position is:

> A top-tier gaming touchpad built for games.

It is not a general office touchpad. Product and engineering decisions must
prioritize, in order:

1. Gaming feel
2. Gaming compatibility
3. Stability and reliability
4. Latency and jitter
5. Input consistency
6. Ease of use
7. Development difficulty and code volume
8. Monetary cost

Development difficulty and code volume may affect implementation order,
development phases and test planning. They must not, by themselves, reject a
gaming-grade capability whose necessity and benefit have been demonstrated.

**Practicality First does NOT mean Simplicity First.** It does not mean always
choosing the least code, the easiest implementation or a weaker solution merely
because the stronger solution requires native code, a Windows driver, Virtual
HID, driver signing, an installer component or lower-level Windows APIs.

Necessary complexity is allowed and required when it buys measurable gaming
compatibility, latency, jitter, consistency, reliability, correct Windows input
semantics or user-experience benefits. Do not reject a necessary gaming-grade
feature merely because it is difficult, low-level, driver-based or increases
implementation complexity. Complexity without measurable product benefit is
prohibited.

Large complexity remains evidence-driven. It requires at least one of:

- A verified limitation
- A measured compatibility problem
- A measured performance or input-quality benefit
- A clear current product requirement

“Theoretically better” is not sufficient. Measure the current limitation, define
the expected benefit and validate the candidate with real games and human input
where applicable.

### Monetary Cost vs Dependency Risk

**Practicality First does NOT mean Lowest Monetary Cost.** Monetary cost is not
an optimization target for this personal project. Reasonable spending on
software, drivers, licenses, hardware, development tools and testing tools is
acceptable when it produces measurable improvements in gaming feel, gaming
compatibility, latency, jitter, input consistency, reliability, usability or
future capability.

Do not reject a technically superior solution merely because it costs money.
For rightpad, monetary price is normally lower priority than achieving the
product goal: a top-tier gaming touchpad built for games. This does not justify
buying or adopting something because it is expensive, advanced or complex; the
benefit must still be evidence-driven.

Distinguish monetary price from technical, vendor and deployment dependency
risk. The following remain material architecture factors even when their direct
financial cost is acceptable:

- Vendor dependency and abandonment risk
- License availability and activation reliability
- Offline availability
- Service or broker dependency
- Driver deployment, signing and installation reliability
- Upgrade compatibility and long-term maintainability
- Game and anti-cheat compatibility
- Failure and recovery behavior

A paid Virtual HID, libvirtualhid, commercial driver or machine license must not
be rejected merely because it is paid, driver-based or installation-intensive
when it demonstrably improves Raw Input behavior, HID semantics, gaming
compatibility, latency potential, consistency, reliability or future virtual
gamepad support. Vendor lock-in, activation availability, driver maintenance and
deployment reliability must still be evaluated independently. This rule does not
preselect libvirtualhid or any other candidate.

### Virtual HID

Production mouse backend (approved 2026-09-12): **libvirtualhid Virtual HID Mouse**.
The single product default is `MouseBackendDefaults.Production`. Program passes
the backend explicitly to ReceiverRuntime; normal GUI, quoted-EXE HKCU startup
and the independent launcher without an override inherit that default.
SendInput remains an explicit development/diagnostic compatibility override
(`--dev-mouse-backend sendinput`), never an automatic production fallback.
Do not add a user backend selector, persisted backend setting or silent fallback.
Virtual HID initialization failure must remain Runtime Error with visible LastError
and diagnostics. Keep fake tests independent of the installed native driver.

Real testing has established a structural limitation worth formal evaluation:

```text
Medium-integrity Receiver
→ High-integrity foreground
→ SendInput success count remains normal
→ actual cursor does not move

High-integrity Receiver
→ High-integrity foreground
→ cursor movement works again
```

Adoption evidence: human A/B found no obvious subjective feel degradation;
Virtual HID is Raw Input-visible; Medium Receiver → High-integrity foreground
movement and Single Tap work; SendInput has the measured limitation above;
POC/build/runtime and Driver/Broker/Lifetime license prerequisites are validated.
Objective latency benchmarking is not complete: do not claim lower latency or
higher polling rate. Broader game compatibility, latency, jitter and dependency/
deployment maintenance risk remain evidence-driven. See docs/VIRTUAL_HID_MOUSE_POC.md.

### Avoid Unnecessary Complexity

Prefer:

- Small modules
- Clear responsibilities
- Easy debugging
- Minimal dependencies

Avoid:

- Complex frameworks without need
- Large abstractions
- Premature optimization
- Excessive design patterns

Necessary complexity does not authorize scope expansion. Do not add a framework
for its own sake, plugin system, generic abstraction hierarchy, connection manager,
unnecessary service or IPC, telemetry platform, automatic discovery, profile
system, security system or compatibility layer without a demonstrated current
need.

---

## Measure Before Optimizing

Before adding an optimization, answer:

1. What problem does it solve?
2. How can it be measured?
3. What is the expected improvement?
4. Is there a simpler solution?

If the benefit cannot be demonstrated, do not add the complexity.

---

# 10. Development Process Rules

Before implementing any feature:

1. Confirm that the feature is required.
2. Check existing documentation.
3. Consider the simplest implementation.
4. Explain architectural impact if significant.
5. Do not expand the project scope.

Do not implement features because:

- They may be useful someday.
- Other products have them.
- They look technically impressive.

---

# 11. Documentation Priority

Read documents in this order:

```
AGENTS.md

↓

docs/ARCHITECTURE.md

↓

Related design documents

↓

Reference documents
```

Priority:

```
AGENTS.md
    >
Design documents
    >
Reference documents
```

Reference documents are informational only.

They must not override current requirements.

---

# 12. Coding Rules

Prefer:

- Clear naming
- Small functions
- Predictable behavior
- Comments explaining design reasons

Avoid:

- Unnecessary comments
- Dead code
- Temporary hacks
- Fake placeholder implementations

Do not create:

- Empty classes without purpose
- Unused frameworks
- Future-proof abstractions without current use

---

# 13. Performance Rules

Performance matters.

However:

Do not optimize before measurement.

Avoid premature:

- Lock-free structures
- Custom memory allocators
- Complex threading systems
- Low-level optimization

Use simple implementations first.

Only optimize after profiling shows a real issue.

---

# 14. Testing Rules

Important behavior must be measurable.

Prefer:

- Automated tests
- Logging
- Diagnostics
- Repeatable experiments

Avoid judging input quality only by subjective feeling.

---

# 15. Scope Control

When receiving a task:

Implement only the requested scope.

Do not add:

- Extra features
- UI improvements
- Refactoring unrelated code
- Additional architecture layers

If a better approach requires changing scope:

Explain first.

Do not silently expand implementation.

---

# 16. Current Project Priority Order

Priority:

1. Correct input capture
2. Reliable Android-to-Windows communication
3. Stable motion processing
4. Correct gesture handling
5. Windows mouse output
6. Tuning and diagnostics

Do not prioritize:

- Visual design
- Extra features
- Convenience functions

before core input quality is proven.

---

# 17. Final Principle

rightpad should become:

A precise, predictable, reliable, top-tier gaming touchpad built for games.

The best solution is not the most complicated solution.

The best solution is the least complex design that fully achieves the measured
gaming-grade requirements. Necessary complexity must remain when removing it would
sacrifice demonstrated compatibility, latency, jitter, consistency, reliability,
correct input semantics or user experience.

---

# 18. Android Build / Deploy / Restart / Receiver Recovery Rule

Whenever a rightpad Android APK build succeeds and the configured test Android
device is reachable through ADB, Codex must complete the full deployment and
runtime recovery sequence.

Required sequence:

1. Build the Android APK successfully.
2. Check that the configured Android device is available through ADB.
3. Overwrite/reinstall the newly built APK.
4. Stop the old Android app instance.
5. Relaunch `com.rightpad.capture/.MainActivity`.
6. Verify that MainActivity is resumed and in the foreground.
7. Keep the current Protocol v2 WPF Receiver process and Receiver Runtime running.
   Do not restart Windows to recover from an Android Sender restart.
8. Verify Disconnected/Connected transitions and admission of the new senderRunId.
   If Windows itself needs an initial launch or binary update, use
   `windows/tools/RightpadReceiverTask.ps1 -Mode Start` after the required build;
   complete subsequent Android recovery checks without restarting that Receiver.
9. Run the Receiver in the current Windows user's interactive desktop session,
   with the same active SessionId as explorer.exe, not in the restricted Codex
   SendInput sandbox.
10. Verify the current user, explorer SessionId, Medium integrity, Default desktop,
    and UDP 50000 listener using the launcher `-Mode Status`.
11. Verify that the Android UDP Sender is active and reports no sender error or
    queue overflow.
12. Verify that the existing Receiver accepts the new Sender run and its Touch
    sequence 0 baseline, with unchanged WPF PID and Receiver Runtime RunId.
    Settings, socket and cumulative Receiver statistics must survive.
13. If the current task includes mouse or gesture output, perform a minimal
    end-to-end functional smoke check that Codex can execute.
14. Only after the Android app and Windows Receiver are both restored and the
    end-to-end path is functional may the Android task be reported as ready or
    completed.

Persistent / user-facing Rightpad.Receiver must NOT be launched as a direct or
indirect persistent child of the Codex execution shell. Codex execution jobs may
use Windows Job Objects with KILL_ON_JOB_CLOSE, which can terminate Receiver when
the Codex host is replaced or cleaned up.

Use the project-approved independent interactive Windows task launcher:
`windows/tools/RightpadReceiverTask.ps1`. It uses the `Rightpad Receiver Dev`
Scheduled Task, current-user InteractiveToken, Limited / LUA run level, and no
triggers. Do not fall back to plain Start-Process from the Codex shell for a
Receiver that must remain alive after the tool command returns. Runtime logs stay
under ignored `windows/test-results/receiver-runtime/`. This is a development
launch mechanism, not a product service, watchdog, or automatic startup feature.

For this project, Android deployment normally includes commands equivalent to:

```text
adb install -r <latest-debug-apk>
adb shell am start -S -n com.rightpad.capture/.MainActivity
```

A successful Gradle build alone is not completion. A successful APK installation
alone is not completion. Verify foreground, presence, new Sender acceptance and
functional E2E on the existing v2 Receiver after Android restart/reinstallation.

Current Protocol v2 behavior (validated 2026-09-09):

- Android creates a random 64-bit senderRunId per UdpTouchSender runtime and
  rotates it on every discovery target transition, clearing Touch queue/session
  and resetting sequence to zero. Same-target confirmed pause/resume retains it.
- onPause stops heartbeat/capture without destroying Sender or resetting sequence;
  onResume probes immediately and enables heartbeat after a fresh discovery OFFER
  confirms the unchanged target, keeping the same runId and sequence.
- Sender recreation gives a new runId and Touch sequence starts at zero.
- Receiver admits an unknown run only on a fully valid HEARTBEAT or DOWN, retires
  the prior ID for its Runtime lifetime, and resets only the input baseline.
- Heartbeat (500 ms) or accepted current-run Touch renews monotonic presence.
  A 2000 ms presence timeout clears stale input once, keeping runId/sequence.
- Android restarts no longer require restarting WPF or ReceiverRuntime.

The v1 development workaround requiring a fresh Receiver has been replaced by
the explicitly approved v2 run/presence behavior. The independent interactive
launcher / Codex Job lifetime rule remains mandatory. Practicality First: add
only functionality solving a current demonstrated problem. This change does not
authorize handshake frameworks, reconnect managers, TCP/ACK/reliable UDP,
retransmission/FEC, pairing/security, unapproved config sync, extra transport
threads/sockets or network optimization beyond the separately approved independent
LAN discovery socket/thread described in docs/DISCOVERY_PROTOCOL.md and the
independent click-feedback side channel in docs/HAPTIC_FEEDBACK_PROTOCOL.md.
The separately approved Screen Controls exception above also reuses that channel
for behavior config sync and the existing Sender for type6 requests, as specified
in docs/CONTROL_CONFIG_PROTOCOL.md; it adds no socket or worker thread.

Do not ask the user to run commands that Codex can run. If no configured test
device is reachable through ADB, explicitly report that deployment and runtime
recovery could not be completed. If Android requires a system-level installation
confirmation that Codex cannot operate, stop only at that permission boundary,
ask the user for that single confirmation, and then continue automatically.

---

# 19. Windows Receiver Change / Mandatory Restart and Verification Rule

This is a permanent engineering rule. It applies whenever a completed change can
alter the actual Windows Receiver runtime code or binary behavior, including but
not limited to:

- C# production code
- WPF XAML
- ResourceDictionary
- View or ViewModel
- Runtime
- Settings
- UDP or Protocol
- Motion or Gesture
- SendInput
- Receiver project configuration
- Receiver executable build output

After any such change, Codex must complete this sequence:

1. Complete the applicable build and tests.
2. Stop the currently running old Receiver.
3. Verify that the old Receiver process has exited.
4. Verify that the old process has released UDP port 50000.
5. Start the latest build through the project-approved independent interactive
   launcher, normally `windows/tools/RightpadReceiverTask.ps1`.
6. Never launch a persistent Receiver as a direct or indirect long-running child
   of the Codex shell.
7. Record and verify the new Receiver PID, or otherwise provide explicit evidence
   that this is a new process instance running the latest build.
8. Verify that the Receiver runs as the current user, in the interactive Session,
   at Medium integrity, on the Default desktop.
9. Verify that `0.0.0.0:50000` is owned by the new Receiver PID.
10. If the Android rightpad app is currently available, wait for the existing
    Protocol v2 heartbeat and verify the header transition from
    `Waiting for Android` to `Connected`.
11. Run the minimum end-to-end smoke check required by the change's scope.
12. Only after all applicable checks pass may the Receiver change be reported as
    verified or complete.

`build succeeded`, `tests passed`, and `a new executable was generated` are each
insufficient on their own. If a change affects actual Receiver runtime code, the
old process must exit and the latest build must start. A Receiver that is already
Connected, still has a PID, owns UDP 50000, or appears healthy does not waive this
restart requirement. Do not attribute later runtime observations to the new code
while a pre-change process is still running.

The mandatory action is Stop -> verify exited and port released -> Start fresh.
A new PID or equally explicit new-instance evidence is part of the verification.
When it is unclear whether a change affects Receiver runtime behavior, perform
the mandatory restart by default.

The restart is not required for changes limited to:

- documentation only
- comments only
- README only
- test instruction files that do not participate in build or runtime
- Git metadata

Protocol v2 keeps the Android and Windows lifecycle rules separate. A Windows
Receiver code change does not justify restarting Android. If Android remains in
the foreground after the Receiver restart, its existing heartbeat must naturally
drive `Waiting for Android` -> `Connected`; this is an important Receiver restart
regression check. If one task changes both Android and Windows runtime code,
restart the Receiver once after its final build, then keep that new Receiver
instance running throughout the Android deployment/restart recovery checks.

Continue using `windows/tools/RightpadReceiverTask.ps1` or an explicitly approved
equivalent independent interactive launcher. Do not return to a persistent
`Codex shell -> Start-Process -> Receiver` chain because it can inherit the Codex
Windows Job lifetime and terminate unexpectedly.
