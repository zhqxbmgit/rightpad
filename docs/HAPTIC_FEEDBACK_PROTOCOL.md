# Haptic Feedback v1

Windows is the sole gesture authority. Android provides one short semantic
confirmation for a normal click that Windows has accepted and started outputting.
It does not classify taps, moves or drags, and receives no gesture settings.

## Design choice

- Android-local tap recognition is faster but duplicates the Windows state machine
  and can vibrate when no Windows click occurred; rejected.
- Windows-confirmed feedback over independent UDP is selected. It adds a LAN
  return trip after the raw touch reaches Windows, without a new gesture truth.
- Discovery piggybacking would couple discovery readiness/lifecycle to clicks;
  rejected. Discovery v1 stays unchanged.
- Receiving on the Touch sender socket would couple receive scheduling to the
  existing send/heartbeat loop. An independent, small listener keeps those
  responsibilities separate and is independently testable.

## Wire contract

Direction: **Windows → Android**. IPv4 UDP destination port **50002**. Windows
uses an ephemeral source port; no fixed source-port check. Touch v2 remains UDP
50000 and Discovery v1 remains UDP 50001. One best-effort datagram per event;
no repeats, acknowledgments, retransmission, clock sync or handshake.

Each datagram is exactly **24 bytes**, little endian, no trailing data:

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | ASCII `RPHF` magic |
| 4 | 1 | version = 1 |
| 5 | 1 | eventType = 1 (`CLICK`, the only event) |
| 6 | 2 | reserved = 0 |
| 8 | 8 | uint64 senderRunId, all bit patterns legal |
| 16 | 4 | uint32 sessionId of the qualifying normal-click UP |
| 20 | 4 | uint32 sequence of that UP |

The tuple `(senderRunId, sessionId, upSequence)` is the event identity. There is
no new Windows event counter that could restart and collide. Touch v2's existing
no-sequence-wrap rule applies. A shared golden fixture covers unsigned/endian
interop: `52504846010100001032547698BADCFEEFCDAB8998BADCFE`.

## Windows output boundary

`UdpReceiver` still decodes and applies run, sequence and presence gates before
`GestureProcessor.Process`. Only its final normal-tap branch passes the accepted
UP packet to the runtime's normal-click output callback. The runtime snapshots
the accepted presence source IP and the packet identity for that request.

`LeftButtonController.Press` first succeeds at LEFT DOWN and schedules its usual
LEFT UP timer, then invokes that request's feedback callback. An overlapping
normal click retains its own callback in the existing click queue and invokes it
only when its actual DOWN succeeds. Cancelling a queued click also cancels that
callback. BeginDrag/EndDrag have no feedback callbacks. NormalClick remains
immediate with the existing 25 ms default hold and unchanged double-tap behavior.

This reports **successful normal-click DOWN with release scheduled**, not proof
that a game consumed a full click. A later LEFT UP failure remains a visible
Receiver Error and retains existing best-effort release; an already delivered
haptic cannot be retracted. Feedback is not delayed until the hold expires.

The callback only calls `TryWrite` on a bounded 32-item channel with synchronous
continuations disabled. It performs no encoding, socket I/O, logging, waiting or
UI dispatch. A separate task encodes/sends, drops pending entries aged >=1 second,
and isolates socket/ICMP/startup failures from mouse output. Full/closed queues
drop feedback. Runtime cleanup first releases buttons, then cancels/awaits the
feedback worker and closes its socket. No reliable-UDP machinery is present.

## Android validation and dedupe

The Activity owns `HapticFeedbackListener`. Since Phase 4 its worker shares a
229-byte buffer with config snapshots (228-byte maximum plus one overflow byte).
RPHF decoding still requires exactly 24 bytes, so oversized packets cannot
masquerade as valid CLICK events. The
pure Java decoder validates exact length, magic, version, event type and both
reserved bytes. The main-thread callback then checks:

1. The same active listener generation still belongs to this foreground Activity.
2. Datagram source IP equals the current discovery OFFER source IPv4.
3. senderRunId equals the current `UdpTouchSender` owned run.
4. The session/UP sequence matches a recent locally submitted raw UP.

`UdpTouchSender` remains the only run/sequence owner. A UI-thread observer reports
the actual encoded raw UP identity once per logical submission; DOWN, MOVE,
heartbeat and UDP copies do not register feedback. No tap duration, movement
threshold, double-tap interval or drag state is stored on Android.

The pure Java gate retains at most 32 UP identities for **less than 1000 ms**,
measured on Android's local monotonic clock from submission through UI execution.
This is a stale-feedback lifetime, not a gesture parameter or latency claim.
Matching removes the identity before callback: duplicates cannot vibrate twice;
distinct feedback may arrive out of order. Unissued/mismatched/expired identities
are rejected. Removing an identity also means a false/no-support haptic result
is never retried. Under overload or extreme delay feedback may be missing.

Source/run/session matching is correlation for stale-target rejection on the
trusted LAN, not source authentication. Touch admission is unchanged.

## Lifecycle and API

Foreground plus fresh discovery confirmation activates the listener. Repeated
same-target confirmations preserve pending entries. Pause disables validation,
clears pending UI callbacks and closes the socket immediately. Resume waits for
a fresh OFFER before reopening, with an empty pending set even if runId persists.
Target/receiver identity changes disable feedback before sender target rotation;
old run packets and old posted callbacks remain invalid. Destroy and Power exit
close the socket and wake the worker to exit. No service, WakeLock or persistent
background listener is used.

Phase 5A.4 restores only the main-thread execution after acceptance:
`TouchCaptureView.performClickHaptic()` invokes `TouchpadClickFeedback.acceptedClick()`.
Its separate Android backend calls
`View.performHapticFeedback(HapticFeedbackConstants.CONFIRM)` exactly once.
There is no Touchpad Vibrator/one-shot path, retry, fallback or automatic parameter
change. minSdk remains 34. The existing VIBRATE manifest permission remains for
Screen Controls, without a runtime dialog. A system false result or RuntimeException
returns failure without affecting mouse, future feedback, Touch sessions,
connection or gamepad. Consumed identities stay consumed on failure.

Screen Control local PRESS/DIRECTION_COMMIT remains a separate 10 ms / 255 policy,
with no shared global intensity and no change in this phase. RPHF wire bytes,
Windows emission and all correlation/dedupe rules above are unchanged.

## Diagnostics and validation

Windows worker logs `haptic_sent` with the complete identity and destination,
throttled `haptic_send_error`, optional `haptic_worker_error`, and shutdown totals.
Android logs `listener_active`, `click_accepted`, `click_requested effect=CONFIRM performed=...`,
throttled `feedback_rejected`, socket errors and worker exit. These are existing
development log/Logcat diagnostics, without additional product settings or UI.

Windows tests cover codec/unsigned golden bytes, accepted-input gates, normal
click versus NoMoveDrag/RearmDrag, cancellation and failed DOWN, queued output,
blocked/full/error feedback isolation, cancellation and actual Runtime UDP with
fake Virtual HID. Android tests cover codec, source/run/session/UP correlation,
duplicates, reordering, stale deadline/capacity, raw Sender identity, and actual
UDP listener main-thread dispatch/pause/resume/transition/port release/thread exit.
Real deployment and native Raw Input evidence are recorded in PROJECT_STATE.md.

## Phase 5A.4 verification and acceptance

All existing Android JVM suites passed, including 7 CONFIRM policy checks and the
real UDP listener's accepted/duplicate/stale/session/rejected/failure cases.
Device instrumentation passed 80 checks, including the actual production Android
backend calling the View with CONFIRM exactly once, false-result handling and
exception recovery. Screen Control feedback checks remain 30 and its source is
byte-identical; RPHF protocol/gate/listener and Sender are also unchanged.
Build/lint passed (0 errors, 14 existing warnings); Windows regression 511/511.

Overwrite deployment preserved app data, layout, settings, Receiver PID 29348 and
Runtime RunId 2. Actual mouse single tap and double-tap drag passed, with accepted
CLICK logging effect=CONFIRM performed=true. XInput B/Y/A, Design A, minimum dwell
and lease passed. Details and the additional GUI Save smoke limitation are in
PROJECT_STATE.md; evidence is ignored under `windows/test-results/gamepad-phase5a4/`.

**RIGHTPAD DISTINCT HAPTIC IDENTITIES COMPLETE (2026-09-20).** The user tested the
installed build and confirmed **"A–D 全部满意，盲操作能明显区分"**: system Touchpad
crispness restored, Screen Control DOWN noticeably stronger, blind distinction by
haptic type, and clear second Slide-commit feedback. This is human acceptance;
API success alone was not used to establish it. No further waveforms were changed.

## Phase 5A.3 verification and acceptance (historical)

All Android JVM suites passed, including 34 new Touchpad policy checks, the real
UDP listener with exact one-shot counting, duplicate/stale/session rejection and
backend-failure recovery, unchanged RPHF golden vectors, and 30 Screen Control
feedback checks. Device instrumentation passed 72. Build/lint passed with zero
errors and 14 existing warnings. Windows regression passed 511/511.

Mouse movement/single click passed. Real double-tap drag held LEFT for 1678 ms,
observed movement while held, and released; the test window saw three DOWN/UP
pairs across the standalone click, first tap and drag. Receiver confirmation and
Android accepted CLICK execution were observed. B/Y/A, Design A, minimum dwell,
lease, config sync and layout regression passed. Thirteen protocol/gate/listener/
ScreenControl source files were byte-identical to phase start.

**Human acceptance: BLOCKED.** The user answered **"Touchpad 不够清脆，或仍不满意"**
after comparing the installed 6 ms / 120 effect with Screen Control 10 ms / 255.
Do not infer a satisfied blind comparison or a specific amplitude diagnosis from
that answer. No further parameter changes were made; wait for the user's next
candidate. Evidence: ignored `windows/test-results/gamepad-phase5a3/`.
