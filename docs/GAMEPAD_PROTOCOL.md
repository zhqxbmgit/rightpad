# GAMEPAD_STATE v2 (current Phases 3–6B contract)

This approved extension carries generic full Xbox logical state on the existing
Android -> Windows UDP 50000 socket. Touch types 1/2/3 and heartbeat type 4 retain
every existing byte and all their admission/sequence semantics. There is no ACK,
new Android socket/thread or native ABI change in type5. Receiver Controls settings
and configuration sync are implemented separately in
[CONTROL_CONFIG_PROTOCOL.md](CONTROL_CONFIG_PROTOCOL.md), using the existing
workers/ports. Control and layout behavior is specified in
[SCREEN_CONTROLS.md](SCREEN_CONTROLS.md).

## Exact wire format

All fields are little endian; datagram length must be exactly **30 bytes**.
The prior uncommitted 26-byte type5 format is superseded and rejected. Touch and
heartbeat formats are unchanged; Android and Receiver must be updated together.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 1 | version = 2 |
| 1 | 1 | type = 5 (GAMEPAD_STATE) |
| 2 | 8 | uint64 senderRunId |
| 10 | 4 | uint32 gamepadSequence |
| 14 | 2 | uint16 logical buttons |
| 16 | 1 | uint8 leftTrigger |
| 17 | 1 | uint8 rightTrigger |
| 18 | 2 | int16 leftThumbX |
| 20 | 2 | int16 leftThumbY |
| 22 | 2 | int16 rightThumbX |
| 24 | 2 | int16 rightThumbY |
| 26 | 2 | uint16 minimumDwellMs |
| 28 | 1 | uint8 flags: bit0 = FORCE_NEUTRAL |
| 29 | 1 | uint8 reserved = 0 |

Unknown flag bits and nonzero reserved are rejected. Dwell uses the entire uint16
range (0..65535ms); Neutral must carry zero dwell. FORCE_NEUTRAL must carry a fully
Neutral state (including all analog fields), and bypasses dwell only after normal
authority and sequence validation. Stale/duplicate FORCE_NEUTRAL is still rejected.

Button bits are ABI-2 `XboxGamepadButtons`: A=bit0, B=bit1, X=bit2, Y=bit3,
Back=4, Start=5, Guide=6, LeftStick=7, RightStick=8, LeftShoulder=9,
RightShoulder=10, DpadUp=11, Down=12, Left=13, Right=14. Reserved bit15 is rejected;
unknown future bits require an explicit protocol revision. These are not XInput
wire masks. Both languages share a golden all-field byte vector in tests.
Every 64-bit senderRunId pattern (including zero) is valid; identity ownership is
checked after structural decoding. Triggers and axes use their entire integer ranges.

## Android state and scheduling

`GamepadState` is an immutable complete value. Definitions map logical control
actions to button contributions; `GamepadAggregator` unions contributions by ID.
Phase 3 analog values are all zero. `xbox.b.slide` maps BASE=B, SLIDE_UP=Y,
SLIDE_DOWN=A, NEUTRAL=none. Phase 6B adds `xbox.x.slide_lr`: BASE=X (logical
0x0004), LEFT=DpadLeft (0x2000), RIGHT=DpadRight (0x4000), UP=DpadUp (0x0800).
The existing native mapping yields XInput X=0x4000, D-pad Left=0x0004,
Right=0x0008 and Up=0x0001; axes remain zero. No packet, bit assignment, decoder,
ABI or native production change is needed. The packet/sender/receiver do not know
which control contributed these buttons.

One touch callback or gesture deadline is one aggregator transaction. This
preserves Phase 1's logical release-before-direction events but publishes a single
B -> Y/A and X -> D-pad full replacements. Each control releases only its own
stable-ID contribution; B+X or B+D-pad unions retain the other control's buttons.
Global safety cleanup cancels all controls then publishes FORCE_NEUTRAL.
The existing 25ms Tap pulse scheduler and Receiver dwell apply to X exactly as B.
Directions carry zero dwell and release normally on UP. LR config remains local
fixed defaults in Phase 6B; Phase 6C now supplies Receiver-owned RPCT v2 config
snapshotted at DOWN. This changes neither this 30-byte gamepad protocol nor dwell.

Gamepad ordering is independent of Touch ordering. Each state change consumes a
new uint32 gamepadSequence and requests three identical copies. A held non-Neutral
state refreshes approximately every 100ms with a new sequence and one copy.
Neutral does not refresh. Gamepad sequence wraps modulo 2^32; Touch's existing
non-wrapping behavior is unchanged.

The existing UdpTouchSender worker/socket/Route/runId are reused. A coalesced
latest-state slot is separate from the eight-entry Touch queue. Each worker pass
services at most one gamepad version, then one queued Touch packet; the poll wakes
on the earliest heartbeat/refresh/minimum-hold deadline or a gamepad transition.
Ordinary state changes can coalesce under congestion. Tap submissions preserve
one current pulse as described below; this remains best-effort UDP, with no pulse
replay queue or catch-up refresh burst.

### Local minimum wire hold (Phase 3.5)

`GamepadStateSubmission` carries full state, `minimumWireHoldMs` and a safety-Neutral
flag. Phase 3.6 also encodes these as minimumDwellMs and FORCE_NEUTRAL. A Tap
pressed transition supplies its gesture snapshot's TapHoldMs; long press, slide,
ordinary Neutral and refresh do not request a minimum hold. No button is special.

The coalesced sender retains at most one protected pulse. Even if ordinary logical
Neutral arrives before that pulse is sent, the worker sends the pressed version
first. Immediately after the first successful `DatagramSocket.send`, monotonic
`System.nanoTime()` starts its minimum hold. Later redundant copies do not restart
the hold. Ordinary Neutral is eligible only after that deadline. A failed first
send does not start the hold and may retry the same current pulse after 100ms,
without a busy loop or catch-up burst. Replacement/safety still supersedes it.

The existing worker polls until the earliest deadline and continues servicing
Touch during the hold. No sleep, new transport thread or socket is added. A generic
hold longer than the refresh interval keeps refreshing; deferred Neutral receives
a sequence newer than those refreshes. A new non-Neutral full state supersedes the
old pulse immediately, so Design A B-to-Y/A is unaffected. Stale send completion
cannot restore a canceled or replaced pulse.

Control cancel/stop, entering the editor and Activity pause request Neutral.
These safety Neutrals clear the pending hold even when logical Neutral was already
published. Foreground false, target/run replacement and close likewise bypass it.
Receiver disconnect/lease safety is unchanged and independent of Android's hold.
Safety submissions always encode FORCE_NEUTRAL, including when upgrading an
already queued ordinary Neutral. Retired-route and close Neutral also set the flag.
Foreground false prevents further held output/refresh but permits the final Neutral.
Target/run replacement retires the old state, resets gamepad sequence and starts
Neutral. A single old-route final-Neutral slot is the only allowed retired-route
traffic; ordinary Touch and held gamepad packets cannot use the old route.
Close requests a final best-effort Neutral; the same worker sends it and then
closes its socket. No UDP operation runs on Android's UI thread.
If a final packet is lost or an old route is unavailable, the receiver lease clears it.

## Receiver authority and lease

Type5 can never establish a sender or renew presence. Only existing valid
HEARTBEAT/DOWN admission establishes a run. Gamepad packets must match the current
connected presence's runId and source IPv4. Port is not an identity; Touch source
admission remains unchanged, with no authentication or first-sender locking.
A same-run source transition neutralizes gamepad state before adopting the source
already accepted by the Touch/heartbeat presence logic.

GamepadSessionProcessor owns its own serial watermark. `delta = uint32(new-old)`:
0 is duplicate; 1..0x7fffffff is newer; 0x80000000..0xffffffff is stale/ambiguous.
Duplicates, stale packets, invalid packets and rejected sources neither submit
HID state nor extend the lease. A valid newer full state renews the lease even
when its values are unchanged; unchanged values need no extra HID report.

A non-Neutral state expires after **300ms** without a newer valid sequence.
The independent maintenance task checks lease at intervals no longer than **40ms**
and wakes earlier for pending dwell deadlines; no MotionClock integration or WPF
polling is involved. Expiry requests Neutral and records
`gamepad_lease_expired` with the measured elapsed time. Timer scheduling adds
normal OS scheduling margin; this is not a hard real-time guarantee.

Lease expiry and same-run presence disconnect retain the sequence watermark, so
late duplicates/stale packets cannot restore a held state. An admitted new run
immediately neutralizes the old state and resets only gamepad sequence ownership.
Stop joins maintenance and neutralizes before backend disposal. Restart creates
a fresh processor/device. Cleanup leaves cancellation callback threads before
ReceiverRuntime joins the dedicated Motion writer.

Malformed type5 datagrams increment InvalidPackets. All datagrams still count
toward ReceivedPackets; gamepad traffic never changes Touch accepted-sample,
sequence, gap, duplicate or old counters. Phase-2 gamepad backend errors remain
separate from Runtime/mouse errors; its failure cleanup neutralizes/removes the
gamepad without stopping mouse input.

## Receiver minimum dwell (Phase 3.6)

The processor knows only full states and generic transport metadata, never Tap,
SlideControl or any button-specific rule. A non-Neutral state is submitted
immediately. Positive minimumDwellMs establishes a monotonic minimumDwellUntil;
an ordinary Neutral received before it is retained as the single pendingState.
At/after the deadline that Neutral is submitted even if no more UDP packets arrive.
Neutral received after the deadline is immediate. Dwell alone does not invent a
Neutral: without a received release the existing lease remains the safety limit.

Acceptance includes successful backend submission. The deadline is no earlier
than either receive-time + dwell or successful backend-completion-time + dwell,
so backend call time cannot consume the requested visible hold. It never adapts
to network jitter. Deadline arithmetic rounds fractional Stopwatch ticks upward.
The maintenance task waits with a capped SemaphoreSlim timeout, rounding wait
milliseconds upward and rechecking the monotonic deadline under the processor
lock on every wake. There is no busy wait, per-pulse thread or 1000Hz MotionClock use.
Normal timer/system scheduling can make release late; it cannot authorize an
early ordinary release.

A newer non-Neutral full state immediately cancels pending Neutral and uses its
own dwell. A zero-dwell same-state refresh renews the lease without clearing or
restarting the original minimum. A positive dwell on a newer same-state packet
starts a new constraint. Pending Neutral consumes its sequence on receipt; stale,
duplicate and half-range ambiguous packets cannot modify it. A later valid packet
can supersede it, including uint32 wrap under the existing serial arithmetic.

FORCE_NEUTRAL, presence/run/source change, lease expiry, Stop and Dispose clear
pending/deadline before releasing immediately. Restart stops the old processor
and creates a fresh one. Timer wakes cannot replay an old state after cancellation
or failure. All state/output/deadline changes share the existing processor lock.

Diagnostics distinguish `gamepad_received` (valid packet receive timestamp and
metadata) from `gamepad_output` (successful backend submission start/completion).
These are host API timestamps, not an instrumented hardware report timestamp.

## Verification boundary

Automated tests cover wire, serial wrap, authority, coalescing, refresh, idle lease,
restart and output failure isolation. Real XInput verification uses the installed
Android controls and the production Receiver. ADB injection proves the functional
path only; it is not human game-feel or latency-quality evidence.

The logical Tap lifetime and Phase 3.5 minimum socket-send spacing remain on
Android. Phase 3.6 additionally transmits generic minimum dwell and protects its
Receiver output locally. Successful socket-send completion is not a NIC
transmission or remote delivery timestamp. Verification separately records logical,
send, valid receive, backend output and XInput timing; no shared clock is assumed.

### Historical Phase 3.5 evidence (before Receiver dwell)

On 2026-09-19, all eight 25ms Tap trials had successful-send spacing >=25ms, but
seven XInput pulses were compressed to 8-15ms. Maximum XInput polling gap was
0.861ms. XInput tracked Receiver valid-receive spacing within that gap, while
send/receive spacing differed substantially. The sender scheduling requirement
passed; the Phase 3.5 end-to-end timing acceptance **was BLOCKED**. This evidence
does not isolate the remaining variation to a particular network/OS component.
No extra padding, Receiver scheduler, wire change or Motion change was introduced.

| Tap | Logical ms | Successful-send ms | Valid-receive ms | XInput ms |
| --- | ---: | ---: | ---: | ---: |
| 1 | 25.413 | 25.593 | 10.284 | 10.386 |
| 2 | 25.662 | 25.881 | 14.201 | 14.144 |
| 3 | 24.968 | 25.515 | 8.566 | 8.143 |
| 4 | 26.043 | 26.291 | 14.597 | 14.688 |
| 5 | 25.006 | 25.574 | 13.766 | 13.970 |
| 6 | 25.173 | 25.538 | 14.660 | 14.591 |
| 7 | 25.517 | 25.970 | 26.044 | 26.278 |
| 8 | 25.534 | 25.726 | 14.053 | 13.683 |

Ignored evidence lives in `windows/test-results/gamepad-phase35/`: `tap-timing.json`
contains raw monotonic timestamps and correlated sequences; `android-e2e.txt`,
`receiver-e2e.txt`, `xinput-transitions.json` and `xinput-e2e.txt` preserve source
records. The E2E harness's PASS lines assert state transitions and regressions,
not the failed wire-to-XInput timing acceptance; `Correlate.ps1` checks that separately.

### Phase 3.6 acceptance, 2026-09-19

Eight real Android taps through the production libvirtualhid Xbox360 Receiver
passed: successful sender spacing, conservative backend hold (B completion to
Neutral submission start), and observed XInput duration were all >=25ms. Receive
spacing was still only 15.108..20.800ms; local dwell protected output despite that
compression. XInput polling max gap was 0.814ms. Timer scheduling produced positive
overshoot up to approximately 15.4ms; this is not hard-real-time 25.000ms output.

Times below are milliseconds. Receive columns share the first B receive as zero;
HID duration is between successful backend submission completions, not a hardware
timestamp. Absolute monotonic timestamps, both backend call boundaries and sequence
correlation are saved in `windows/test-results/gamepad-phase36/tap-timing.json`.

| Tap | Logical | Send interval | B receive | Neutral receive | HID duration | XInput duration |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 25.505 | 25.710 | 0.000 | 16.173 | 29.989 | 29.834 |
| 2 | 25.255 | 25.699 | 266.851 | 287.017 | 26.799 | 26.453 |
| 3 | 25.349 | 25.732 | 522.568 | 537.981 | 36.948 | 36.715 |
| 4 | 24.831 | 25.679 | 783.120 | 799.068 | 40.028 | 40.322 |
| 5 | 24.506 | 25.681 | 1040.065 | 1057.088 | 33.007 | 32.912 |
| 6 | 24.994 | 25.813 | 1295.495 | 1316.295 | 40.259 | 40.430 |
| 7 | 25.813 | 26.553 | 1569.191 | 1584.299 | 29.262 | 29.285 |
| 8 | 25.341 | 25.643 | 1826.489 | 1843.914 | 32.212 | 32.499 |

Debug and Release each passed 502/502 tests (489 existing cases plus 13 dwell
cases), including deterministic early/late release, full-state replacement,
safety, serial ordering/wrap, backend-call duration and real idle UDP release.
Android JVM tests passed, including 97 gamepad checks and 49 control tests; device
instrumentation passed 49 checks including actual ACTION_CANCEL. Its force Neutral
reached backend completion 0.351ms after valid receive. Long press >1.3s, both
slides, both Design A replacements, lease (322.962ms), mouse move, single click and
click haptic passed through the production path.

Receiver was independently restarted from PID 26892 to 10080, then stayed at
PID 10080 / Runtime RunId 1 through Android deployment and sender recovery.
MainActivity foreground, Connected, new sender admission/Touch sequence zero,
unchanged settings/layout and zero mouse/gamepad output failures were verified.
Evidence and repeatable observer scripts are in the ignored Phase 3.6 directory.

### Phase 5A fresh integration recheck, 2026-09-19

The protocol/production implementation was unchanged in this audit. All 511
Windows tests passed in both Debug and isolated Release, including the Phase 3/3.6
sequence, lease, deadline, replacement and safety regressions. New observations
from the production Xbox360 controller, XInput slot 0, are shown below (ms).
Receiver times are relative to the first valid B receive; HID duration measures
backend completion to completion, not a hardware timestamp.

| Tap | Logical | Send interval | B receive | Neutral receive | HID duration | XInput duration |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 24.959 | 25.671 | 0.000 | 12.004 | 29.604 | 29.560 |
| 2 | 25.714 | 25.558 | 260.367 | 276.902 | 34.338 | 34.369 |
| 3 | 26.089 | 25.566 | 530.154 | 544.185 | 28.557 | 28.295 |
| 4 | 24.323 | 25.600 | 786.018 | 812.035 | 26.063 | 26.035 |
| 5 | 25.483 | 25.585 | 1055.844 | 1071.295 | 32.873 | 32.784 |
| 6 | 25.001 | 25.547 | 1301.847 | 1328.232 | 26.452 | 26.126 |
| 7 | 25.217 | 25.623 | 1577.167 | 1591.258 | 39.375 | 39.819 |
| 8 | 25.113 | 25.559 | 1838.437 | 1855.529 | 26.861 | 26.637 |

All eight successful-send intervals, conservative backend holds (B completion to
Neutral submission start), and observed XInput holds were >=25 ms. Polling max gap
was 1.724 ms. LongPress 1.312 s, both slides, both immediate Design A replacements,
ACTION_CANCEL instrumentation, lease release at 324.493 ms from last accepted
state, mouse move/click and Android click haptic passed. No 8–15 ms XInput pulse
was observed. This permits positive timer/sampling overshoot; it does not claim
25.000 ms precision. Exact unrounded data and raw timestamps are in ignored
`windows/test-results/gamepad-phase5a/tap-timing.json`.

See [SCREEN_CONTROLS.md](SCREEN_CONTROLS.md) for the safety audit and
**MANUAL HUMAN ACCEPTANCE REQUIRED** checklist. Functional automation does not
establish real-finger/game feel or simultaneous Leftpad coexistence.
