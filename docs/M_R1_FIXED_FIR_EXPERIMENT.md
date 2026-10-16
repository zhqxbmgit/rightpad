# M-R1 fixed FIR experiment (2026-10-09)

**RETIRED FROM SOURCE — 2026-10-15, at the user's request.**

The unused FIR_3/FIR_5 implementation, motion integration, internal selection
API and fifteen experiment-only test groups have been removed. M-R1 again
uses its original direct reconstruction path. C parameters retain their
M-isolation regression without FIR variants. Removed experiment source is
archived under `windows/test-results/remove-fir-tau10-20261015/retired-source/`;
the original evidence referenced below is retained. No production binary,
saved setting or running Receiver was changed. The remainder is the historical
experiment report; its implementation descriptions refer to the retired code.

Status: isolated implementation and deterministic evidence only. NOT DEPLOYED,
NOT COMMITTED, NOT PUSHED. Production selection remains unchanged. Final decision:
**result C — do not recommend enabling an additional FIR on present evidence**.
This is offline feasibility validation; human micro-control improvement is unproven.

## Scope and configuration

Only M-R1 can select the internal `FixedFirMode` values OFF / FIR_3 / FIR_5.
`ResampledMotion` defaults to OFF. Isolated tests pass `firMode` and gain X/Y=10;
`RequestFir` changes only the selected internal experiment at safe idle.
`CaptureFirState` reports Selected, Active, Pending under the same motion lock.
No GUI field, settings JSON field, Runtime wiring, CLI product option, Android
change, protocol change, new thread or native output implementation was added.
Receiver-owned persisted M/C and legacy type7 telemetry keep their existing code.

M sensitivity -> unchanged M8 causal reconstruction -> fixed FIR of high-precision
cumulative position -> unchanged Q0-C and successful-submit ledger -> existing
output callback. C returns through its original TickC path before FIR computation.
C Active FIR is OFF; a selected FIR is suspended/pending until an idle return to M.

## Fixed-time taps and math

Let R_realized(t) be the high-precision reconstruction trajectory recorded by
M-R1's existing bounded Add/Trim history, as it was known at each time. The current
tap uses unchanged R(t); past taps query that realized record, never later-arrived
samples interpolated backwards into an already-observed prefix.

- FIR_3: F(t)=[R(t)+2R_realized(t-1ms)+R_realized(t-2ms)]/4.
- FIR_5: F(t)=[R(t)+4R_realized(t-1ms)+6R_realized(t-2ms)+
  4R_realized(t-3ms)+R_realized(t-4ms)]/16.

On the nominal 1 ms grid these are exactly the requested discrete binomial filters.
At jittered wakes the same fixed time offsets are evaluated through piecewise
linear recorded history. This is the continuous-time fixed-delay extension;
it does not filter the last N irregular wakes or retune coefficients/window.
A missed wake computes once at actual time and submits at most once; no catch-up
HID or FIR iteration is replayed. The measured cadence is nominal 1000 Hz in
source and deterministic exact 1 ms in the normal fixture, not a measured real
Windows timer, HID report rate, screen-pixel rate or end-to-end latency.

Weights are fixed, positive, sum to 1, and independent on X/Y. Anchor-form explicit
sums preserve a constant binary64 endpoint exactly. The pure core uses at most
five stack samples. A FIR object adds 32 bytes on this runtime; no new history
array is allocated. OFF allocates no FIR object/history and has no FIR waiting.
The container adds two enum fields and a nullable FIR reference; its object layout
has small fixed metadata overhead even when OFF. Existing history limits and
capacity faults remain; enabled FIR requires at least 4 ms of retained history.
No Finite-Critical Position/IsSettled call is used for M-R1, and Tau/Support do not
shape FIR weights, timing or endpoint.

## Chain and completion contract

Fresh DOWN invokes existing Clear/Reset, so history starts from legal cumulative
zero; DOWN emits no movement. Duplicate active DOWN, wrong owner, new sender,
Reset, disposal and failed submit preserve existing hard-abort fences. No tail or
completed-chain fractional carry crosses a new chain. Same-run DOWN during a
release-pending chain remains the existing joining DOWN: it retains that chain's
ledger/history/phase and frozen active FIR, rather than inventing a fresh chain.

Five distinct boundaries are retained in the evidence: raw UP admission; final
mapped reconstruction marker; FIR history becoming constant at the exact earned
endpoint; that opportunity's Q0-C evaluation/commit; safe idle/parking. An enabled
FIR waits until existing reconstruction completion and its whole fixed history
span is constant. This takes at most an additional 2/4 ms of trajectory support
with timely opportunities; already-constant history can finish with no extra wait.
Missed wakes can defer actual completion but do not stretch the fixed filter.
There is no endpoint snap or forced number of HID reports. Zero integer output
is not proof of FIR completion. Q0-C's final fractional error remains below one
count per axis and can depend on the filtered path, including reversals.

Receiver lifecycle abort/Reset discards all remaining motion, with zero later
output. Android ACTION_CANCEL is a different existing contract: Touch v2 has no
CANCEL and Android sends no synthetic UP, so Windows cannot know the local cancel
occurred. Only already-known reconstruction/filter motion may finish and park;
new DOWN or existing lifecycle invalidation clears it. The experiment does not
add a cancellation message or blindly flush a cancelled endpoint.

## Verification and evidence

Original frozen uncommitted source: 682/682 regressions passed. Candidate: 697/697,
including 15 FIR groups. Independent binomial/linearity/axis oracles, dyadic Q
oracle, 51 deterministic scenario variants, 108 synthetic sinusoid variants,
UP/hold/abort/ownership/joining/mode/filter boundaries, irregular input dt,
same-admission packet grouping and jitter/missed wakes passed. OFF's 47 existing
fake replay traces are byte-identical to the frozen original. M-R1/M-F1/M12 goldens,
C oracles, Receiver-owned mode/type7/reconnect and Xbox360 regressions remain.
Zero hot-loop managed bytes were measured across 240000 valid ticks per mode.
Only fake callbacks and temporary loopback test ports were used.

The full Chinese report, comparison.csv, opportunities.csv, reversals.csv,
stage-metrics.csv, noise.csv, performance.csv, cancel-contract.json,
test-summary.txt, source/production protection evidence and reproducible scripts
are in `C:\RightpadResearch\M-R1-FixedFIR-20261009\`.

Low-frequency synthetic noise (10-25 Hz) changed little, while both FIRs increased
micro tracking error and delayed reverse HID output. FIR-5 can suppress a real
short sub-count reversal peak that OFF/FIR-3 would cross, despite retaining the
same high-precision endpoint. Fast net displacement remains intact. There is no
real Xiaomi noise capture or human A/B in this stage. Keep OFF; if separately
authorized to explore human A/B, FIR_3 at gain10/10, M8, 1 ms taps is the smaller
initial response-cost candidate. This is not an acceptance or deployment decision.
