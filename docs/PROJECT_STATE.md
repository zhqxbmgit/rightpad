# rightpad Project State

Updated: 2026-10-01

## Current product

rightpad is an Android-to-Windows 11 gaming input system for one user, one phone
and a trusted LAN. It is a blind, portrait, one-finger touch surface. Accounts,
cloud, security handshakes, streaming, remote desktop, multi-touch, sensor input,
automatic game detection and runtime-adaptive feel are outside the approved scope.

Mouse input uses one product pipeline:

```text
Raw Touch -> live committed Sensitivity -> 12 ms reconstruction
 -> product Finite-Critical with committed run Tau/Support
 -> Q0-C -> immediate libvirtualhid Mouse output
1000 Hz / Earned-Settle
```

M is production. C is an identical future experiment slot, currently only a label.
The Mode UI, v2 type7 transport and Requested/Active infrastructure remain.
Changes defer across held contact and unfinished settlement, including joining
DOWN. No profile change rebuilds the kernel or changes Motion/native behavior.
Deterministic Profile M/C Motion Equivalence covers position, every logical/native
output, actual submission time, settlement, missed ticks and endpoints.

Tau/Support are current committed Receiver settings snapshotted at Runtime Start,
not hardcoded 18/90. The most recent deployed observation was 18/90 ms.
Sensitivity Save applies the committed X/Y pair atomically to new real displacement
without rescaling old targets/pending/history. Tau/Support remain frozen per run.
See [MOTION_ENGINE.md](MOTION_ENGINE.md).

## C11B cleanup status

Active builds no longer include DLS, C4 30/150, ETAP, JETAP or C11A120Hz batching.
Their production source, dedicated tests/fixtures and separate research docs have
been deleted. Temporary Compile Remove items are gone. Removed planner/jerk/clamp/
batching fields no longer appear in Diagnostics. All 43 explicitly authorized
audited targets have been permanently deleted, without expanding the list.
Experimental raw data and harnesses are not retained. The sole retained
research document is [MOTION_RND_CONCLUSIONS.md](MOTION_RND_CONCLUSIONS.md).
No Git staging, commit, push, reset, restore or clean is performed.

Motion R&D is paused. Wobble is deferred to separate work. Camera image-tracking
branch is CLOSED / INCONCLUSIVE. No new Motion planner or camera tracking tool
development is selected.

## Mouse and gestures

Production Mouse backend is the single MouseBackendDefaults.Production
libvirtualhid Virtual HID Mouse. Initialization failure is visible Runtime Error;
there is no silent SendInput fallback, backend selector or persisted backend choice.
The explicit --dev-mouse-backend sendinput is diagnostic only. Native fake tests
remain independent of installed drivers. Raw Input visibility and Medium Receiver
input into High-integrity foreground have been validated; lower latency or higher
hardware polling rate are not established. Driver/broker/license/deployment and
broader game compatibility remain evidence-driven.

Windows owns Single Tap and double-tap drag. Accepted Drag UP rearms the next
contact from Android event time; button release and lifecycle invalidation remain
independent of Motion. Mouse click confirmation uses RPHF and system CONFIRM on
the actual Android Touchpad. No local Touchpad DOWN/MOVE vibration is added.
See [GESTURE_ENGINE.md](GESTURE_ENGINE.md),
[VIRTUAL_HID_MOUSE_POC.md](VIRTUAL_HID_MOUSE_POC.md) and
[HAPTIC_FEEDBACK_PROTOCOL.md](HAPTIC_FEEDBACK_PROTOCOL.md).

## Android capture, transport and discovery

Android captures real historical/current samples and timestamps without deciding
mouse Motion or gestures. The foreground Activity prevents automatic screen-off;
it releases foreground keep-awake behavior on pause. No WakeLock/background service.
Existing Sender, socket and single-finger routing remain.

Touch v2 UDP50000 uses random senderRunId per Sender runtime and independent
sequence/presence. New run admission on valid HEARTBEAT/DOWN clears the input
baseline and retires the prior run for that Receiver Runtime. Presence timeout
is 2000 ms; accepted input/500 ms heartbeat renew it. Ordinary stationary touch
does not clear input merely because no MOVE arrives. Sender restart recovers
on the existing Receiver; it does not require Windows restart.

Independent Receiver Discovery v1 UDP50001 selects OFFER source IPv4, without
fixed production IP, first-NIC heuristic or manual selector. Target transition
rotates senderRunId, resets Touch sequence and safely releases input. Same-target
confirmed pause/resume retains run identity and sequence. Second-location PC
acceptance remains a separate discovery validation item.
See [INPUT_PROTOCOL.md](INPUT_PROTOCOL.md), [DISCOVERY_PROTOCOL.md](DISCOVERY_PROTOCOL.md)
and [MOTION_PROFILE_PROTOCOL.md](MOTION_PROFILE_PROTOCOL.md).

## Screen Controls and Xbox360

Current product contains Touchpad Mouse, B SlideControl and X SlideControlLR.
The shared generic layout editor supports move/resize/numeric rectangles and
Save/Cancel/Reset; Visual Rect equals Hit Rect, and layout persists by stable ID.
Current user layout must be preserved exactly. No right-stick gameplay or
DualSense/Steam Trackpad route is active.

B maps BASE/Tap/LongPress to B, Up to Y, Down to A with Design A replacement.
X maps BASE to X, Left/Right/Up to D-pad directions, with horizontal priority,
first commit wins and Design A. Receiver Controls are the authoritative behavior
source; defaults and legal semantics remain in [SCREEN_CONTROLS.md](SCREEN_CONTROLS.md).
Each Android DOWN snapshots synchronized in-memory behavior; active gestures do
not change during Save. B remains STRONG_ONE_SHOT 10 ms/255; X uses its approved
SYSTEM_CLICK policy. Touchpad remains separate system CONFIRM.

GAMEPAD_STATE v2 type5 is exactly 30 bytes, full Xbox state with minimum dwell,
FORCE_NEUTRAL and zero reserved. Held refresh is 100 ms; Receiver lease 300 ms.
Independent monotonic dwell honors ordinary Tap Neutral; safety Neutral/new
non-Neutral bypass old dwell. The single libvirtualhid Xbox360 backend uses ABI2;
failure does not stop Mouse. Full neutralization covers lifecycle/error cleanup.
RPCT v2 carries complete B+X behavior in 62 bytes over shared UDP50002; type6
requests recover lost updates via existing Sender. Disk Save precedes publication;
failed Save publishes nothing. No extra transport socket/worker is added.
See [GAMEPAD_PROTOCOL.md](GAMEPAD_PROTOCOL.md) and
[CONTROL_CONFIG_PROTOCOL.md](CONTROL_CONFIG_PROTOCOL.md).

Phases 1–6C received actual-device human acceptance on 2026-09-20. Preserve
accepted behavior; parameter/gesture/haptic changes require fresh acceptance.

## Protected reliability work

Phase 10A's targetless Sender guard is restored: under the existing sendGate,
setForeground(false) creates safety Neutral only when the current route has a
target. Without a target it still disables foreground/heartbeat, clears pending
Touch and activeSession, and interrupts the worker without creating gamepad work
or a zero deadline. Target-present pause, old-route retired Neutral and close's
used-target final Neutral rules remain unchanged. The formal
android/tests/TargetlessSenderTests.java is compiled and executed by the full JVM
runner; gate-held state/deadline assertions prove the targetless invariant, and
loopback checks cover pause, target removal/replacement and close safety output.
The historical phase10a evidence remains protected.

Phase 10B's test-only LayoutTestFileGuard remains: instrumentation safely snapshots
the actual user layout, applies a controlled fixture and restores exact original
bytes in finally, including failure/cleanup paths. ScreenControlsSmoke,
LayoutTestFileGuardTests and phase10b evidence are protected. No user layout reset.

Other formal fixes, protocol validation, stuck-input release, Sender/network error
handling, settings transactions, Safe Restart recovery and health status remain.
No Android source/build/install/restart is required for C11B Windows-only changes.

## Diagnostics and lifecycle

RPST v1 reuses UDP50002 for INPUT/XBOX/CONFIG health. Receiver backend/config
identities are authoritative; Android rejects stale target/run snapshots and
expires status after 1500 ms. Health does not route input or tune Motion.
See [STATUS_PROTOCOL.md](STATUS_PROTOCOL.md).

H1 Manual Hitch Flight Recorder remains passive, bounded, in memory, with
262144 records, 112-byte schema and a retained 60-second window. Manual Freeze
exports observations without restarting/resetting/reconfiguring Receiver.
windows/test-results/hitch and existing traces are protected. Ordinary test-results,
Phase10A/10B evidence and independent non-Motion diagnostic tools are protected.
See [HITCH_FLIGHT_RECORDER_H1.md](HITCH_FLIGHT_RECORDER_H1.md).

WPF Receiver runs in the current user's interactive session, Medium integrity,
Default desktop. Persistent Receiver launches use windows/tools/RightpadReceiverTask.ps1,
not a Codex-shell child. Runtime changes require clean Stop, old PID/port release,
tested Release deployment, fresh launch/identity verification and natural Android
reconnect. Mouse/gesture/B/X/H1 smoke ends with Requested M, Active M, all health
GOOD, Xbox Neutral and LEFT released. Start-with-Windows retains its existing
quoted-EXE HKCU product behavior.

## Verification and next work

Formal regression retains finite-critical, Q0-C, reconstruction, Earned-Settle,
MotionClock, live sensitivity/settings, protocol, gesture, Mouse/Xbox native fake,
B/X and H1. Run full Debug/Release and git diff --check after runtime edits.
Preserve existing dirty work and user data; do not stage or commit without request.

C11B baseline validation on 2026-10-01: normal Debug and Release each passed
556/556 managed tests with zero build warnings/errors; native fake passed 1/1
in both configurations. The exact M/C gate covers continuous position, Q0-C,
native sequence/time, settlement, missed ticks, six saved kernels, live sensitivity
and deferred profile boundaries. Existing M output fingerprints remain unchanged.

The final tested Release without temporary exclusions was deployed after PID22264
exited cleanly and UDP50000/50001 were free. New PID28280 runs as Z88888888\zhqqq,
Session1 matching Explorer,
Medium, Default desktop, with libvirtualhid Mouse and Xbox360 Available.
Android PID6065 naturally reconnected without rebuild/install/restart. M/C
movement/click/double-tap drag, B/X and H1 Freeze passed. Final Requested/Active
M, actual Saved18/90, 1000Hz, all health GOOD, Xbox Neutral and LEFT released.
Deployed files match the tested Release SHA256 values. Protected source and
existing non-runtime test evidence passed preservation checks. User settings/layout
SHA256 access was denied; no workaround or settings/layout writes were performed.

All 43 deletion targets are gone. Two research dependency bin subdirectories
initially denied access; after explicit separate authorization, only their inherited
ACLs were restored and the original scoped deletion completed. No user data ACL
was changed. The deletion list was not expanded.

Future work is evidence-driven game compatibility/reliability and separately
scoped wobble investigation. New Motion planners and camera-image tracking
investment are paused. Reference implementations inform understanding but do not
select product parameters or override AGENTS.md.
