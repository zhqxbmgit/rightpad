# MOTION_PROFILE_STATE v2

Android transports the manual M/C label over the existing Sender socket.
M is production; C is a reserved future experiment slot with identical current
Motion and native output. Both use live committed sensitivity, 12 ms reconstruction,
Saved Finite-Critical, 1000 Hz, Q0-C and Earned-Settle. Label changes do not change
any parameter, kernel or output behavior.

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
the read-only algorithm/kernel/cadence lines show Finite-Critical, actual Saved
Tau/Support and 1000 Hz for both labels. There is no
Windows selector or persistence. H1 has no profile record or field.

## Regression gates

Android JVM tests cover golden bytes, encoder validation, real loopback triple
identity, M/C changes, no-target behavior, route republish, pause/resume and Touch
sequence isolation. Existing guarded device instrumentation checks matching UP,
cancellation, identical Mouse bytes and unchanged layout persistence.
Windows tests cover strict decoding, run/source correlation, wrap-aware ordering,
presence retention/reset, gamepad/Touch/config isolation and Diagnostics mapping.
Deterministic tests inject M or C through UdpReceiver and compare every native
integer/time event and every continuous/pending/deadline/settlement state, retaining
the production M golden fingerprints for ordinary and missed-tick traces.
Profile Motion Equivalence additionally compares every Q0-C logical output,
including zero events, native sequence/timing, missed ticks and final endpoints
for slow/medium/fast/reversal/stop/micro input, live sensitivity and multiple legal
Saved Tau/Support pairs. Stationary contact, earned settlement, joining DOWN,
latest requested label and lifecycle Reset retain the deferred chain contract.
No experimental trajectory is selected by type7.
