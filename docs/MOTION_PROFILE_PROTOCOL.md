# MOTION_PROFILE_STATE v2

Current selected baseline (2026-10-03): Touchpad R&D is paused. The deployed
Receiver uses the explicit `M_R1` identity, and Android uses fixed unbuffered
acquisition with the normal M/C control. NORMAL selects direct M8 reconstruction
with Q0-C and reconstruction-completion release; CINEMATIC selects unchanged
C-Z1/C12. The source default `ProductionMode` remains M-F1. Original stage-only
deployment restrictions below are historical; later deployment and this final
source commit/push were separately authorized. The type7 wire contract is unchanged.

Android transports the manual M/C label over the existing Sender socket.
M-F1 retains Saved Finite-Critical, 1000 Hz, Q0-C and Earned-Settle, changing only
M reconstruction/playout from 12 to 8 ms. C source is recovered to C-Z1:
zhq-derived servo / Amax 80000 / Vmax 15000, tau 35 ms, 4 ms / 250 Hz nominal,
Java-compatible rounding and true glide, with unchanged 12 ms reconstruction.
Both retain live committed Sensitivity and immediate libvirtualhid output.
The idle Active chain boundary selects delay; Requested never retimes a busy
chain. M joining DOWN retains M8; C interruption retains C12. Z2 human validation
is complete and the user preferred Z1 gimbal character. No deployment in M-F1.
The wire format and admission/order/source contracts are unchanged.

Phase M-R1 (2026-10-03) adds an explicit Windows development container `M_R1`;
the wire values remain NORMAL/M and CINEMATIC/C. Production still selects M-F1.
M in this container uses direct reconstructed R(t), M8, Q0-C and 1 ms opportunities.
Touching and ReleasedAwaitingPlayout are busy. UP seals the real target and keeps
the existing final mapped boundary; its first valid matured evaluation submits
through Q0-C and clears without Finite-Critical settlement. Same-run pending DOWN
joins the target/ledger/phase and cannot apply Requested C early. A completed chain
starts fresh without fractional carry. C lifecycle and C12 remain EXACT, including
deferred requests through touching/release/glide and interrupted-DOWN fences.
Diagnostics and MotionTrace distinguish M-R1 from M-F1, with Active Tau/Support N/A
and unchanged Saved settings. H1 schema, type7 ordering and source correlation are
unchanged. This implementation stage excludes deployment and live Receiver restart.

Android Mode control remains M/C (rightpad.input.mode): matching UP changes
the authoritative Motion Profile and publishes type7; it has no GAMEPAD_STATE
or haptic output and retains the user's generic layout/editor geometry.
Touchpad acquisition is fixed unbuffered in both M and C, in debug and release:
each admitted single-finger Mouse DOWN calls the actual TouchCaptureView's
requestUnbufferedDispatch(event) once before normal extraction of that same
event. MOVE/UP/CANCEL and controls never request it; samples, timestamps and
Sender bytes remain unchanged. There is no acquisition selector or persisted
acquisition state. M-R8H human A/B preferred U for less jitter and greater
stability without obvious sluggishness; its B/U UI is retired, and historical
evidence remains under phase-mr8-unbuffered and phase-mr8h.

## Wire format

Android -> Windows, existing UDP 50000; exactly 16 bytes, little endian.

| Offset | Type | Value |
|---:|---|---|
| 0 | uint8 | version = 2 |
| 1 | uint8 | type = 7 |
| 2 | uint64 | senderRunId |
| 10 | uint32 | profileSequence |
| 14 | uint8 | 0 = NORMAL / M; 1 = CINEMATIC / C |
| 15 | uint8 | reserved = 0 |

Exact length, version, type, profile and reserved are validated before applying
state. All 64 run-ID bits are preserved. Shared golden C fixture:
`0207080706050403028198BADCFE0100`.
Types 1..6, RPHF, RPCT and RPST keep their existing bytes.

## Android ownership and scheduling

`ScreenControlRouter.profile` remains the single UI authority. Fresh Activity
starts M; only matching Mode UP toggles. TouchCaptureView publishes that committed
value to UdpTouchSender's guarded transport mirror; send failure never rolls back
UI. A fresh Sender starts M, sequence zero. Each actual value change consumes one
sequence modulo 2^32 and requests three identical datagrams. Repeating the same
value is a no-op. The latest state can coalesce before the worker claims it, as
with ordinary gamepad state; there is no replay queue or ACK.

One existing worker/socket sends the claimed immutable version three times under
the existing route gate, after heartbeat. No profile packets are queued or sent
without a target or while paused. A pending value remains available for resume.
Touch and gamepad sequences are independent. Profile work has no input or haptic
side effects and does not participate in config requests.

Same-run pause/resume retains M/C and sequence and requests the same triple again.
Target transition rotates senderRunId and resets profileSequence to zero while
retaining the authoritative value. The ready route sends heartbeat followed by
the current value, even without another Mode gesture. Delivery remains best
effort; a successful UDP send is not an admission acknowledgement, and all copies
can be lost. This stage adds no periodic profile refresh or reliability framework.

## Receiver authority and lifecycle

UdpReceiver owns the current profile and independent nullable sequence watermark.
Fresh Runtime and every HEARTBEAT/DOWN admission of a new run immediately start M
with no profile watermark. Type7 cannot admit a run. It requires Connected current
presence, the admitted run ID and the same source IP correlation already used by
gamepad. This adds no source locking or authentication to Touch admission.

For an existing watermark, accept only unsigned delta in `1..0x7fffffff`.
Duplicates, stale values and the ambiguous half-range are ignored. Rejected
source/run/malformed packets never consume the watermark. Normal presence expiry
retains profile and watermark with the existing run identity, rejects profile
updates while disconnected and clears input using the existing lifecycle. Fresh
same-run heartbeat reconnects without resetting profile ordering. New run resets M.

Type7 never renews presence or changes Touch/gamepad ordering, lease/dwell,
config identities or gestures. It now requests a Motion profile; the active
profile changes only when the entire Motion chain is idle/settled. Existing
received/invalid datagram counters include it. Diagnostics projects read-only
`Requested Profile: M/C` and `Active Motion Profile: M/C` separately;
the read-only algorithm/kernel/cadence lines identify Saved Finite-Critical/
Tau/Support/Q0-C/1 ms/1000 Hz/Earned-Settle/Reconstruction 8 ms/M-F1 for M and C-Z1/Reconstruction 12 ms/zhq-derived servo/Amax 80000/Vmax 15000/35 ms/4 ms/Java-compatible rounding/
true glide for C. Touching, ReleasedAwaitingPlayout and Gliding are busy C
states; latest Requested applies only at natural stop. New DOWN interrupting
C release/glide retains Active C and discards old scheduled work. C -> M clears
all C carry/state; M -> C waits for existing earned settlement. There is no
Windows selector or persistence. H1 has no profile record or field.

## Regression gates

Android JVM tests cover golden bytes, encoder validation, real loopback triple
identity, M/C changes, no-target behavior, route republish, pause/resume and Touch
sequence isolation. Existing guarded device instrumentation checks matching UP,
cancellation, identical Mouse bytes and unchanged layout persistence.
Windows tests cover strict decoding, run/source correlation, wrap-aware ordering,
presence retention/reset, gamepad/Touch/config isolation and Diagnostics mapping.
Deterministic M tests retain both M12 historical fingerprints in a frozen test-only reference, pin independent M-F1 fingerprints after structural/behavior review, and
compare every continuous/pending/kernel/deadline/generation/logical/native/time
state with deferred C requests across multiple Saved configurations. C uses an
independent oracle for servo order/vector caps, Java rounding, reconstruction,
fixed-step missed-wake handling, release/glide/carry and lifecycle/profile fences.
H1 remains passive with unchanged records; C Output includes zero logical events.
See MOTION_ENGINE.md for the experiment's reference normalization contract.
