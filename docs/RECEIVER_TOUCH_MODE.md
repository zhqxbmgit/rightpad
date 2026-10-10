# Receiver-owned touch mode (2026-10-08)

Receiver implementation deployed and accepted 2026-10-10. This document covers
Windows mode ownership only; Android drafts and FIR experiments are excluded.

## Audited old control chain

Android ScreenControlRouter began NORMAL/M in Activity memory. Matching Mode UP
toggled M/C without Android mode persistence. TouchCaptureView published the
profile to UdpTouchSender; the existing Sender sent three type7 copies after
heartbeat/admission, resume and target/run changes, using independent sequence
ordering. UdpReceiver validated/de-duplicated those packets, called RequestProfile
and reset the request to M on a new admitted sender run. ResampledMotion selected
M-R1 or C-Z1 only at its idle boundary before submitting through libvirtualhid.
Receiver GUI only displayed Requested/Active; SettingsFileStore had no mode field.
Both Android modes already shared raw capture, samples/format and fixed unbuffered
Mouse DOWN acquisition. Mode had no gamepad/haptic mapping, although its common
DOWN/cancel lifecycle path could emit safety Neutral.

## Local authority and persistence

Normal GUI still selects the M_R1 container, without consulting historical
MotionModes.ProductionMode (M-F1). RuntimeSettings adds only `touchMode`, serialized
as the exact string `M_R1` or `C_Z1`. Missing field/no file defaults to M_R1;
unknown, non-string or illegal values fall back only that field to M_R1 and retain
the existing settings warning. Existing sensitivity, Tau/Support, Tap, B/X Controls
and other formal fields retain their values and validation.

Motion-page M/C buttons call MainViewModel.SelectTouchModeAsync and
SettingsViewModel.SelectTouchModeAsync. The latter snapshots current committed
settings, changes only TouchMode, awaits successful disk SaveNowAsync, then
publishes. It retains other numeric/Controls drafts without silently saving them.
Failed Save cannot update Selected/Pending/Active. An ordinary later Save retains
the selected mode. There is no runtime restart, reconnect, new HID or Xbox device.
Explicit development motion overrides remain authoritative and disable the
production mode selector with a visible explanation; legacy research identities
and fingerprints are not redefined.

ReceiverRuntime subscribes the current production run to settings publication,
applies the saved selection before input/clock startup and unsubscribes/joins the
handler before disposing motion. The handler serializes reading the newest
committed setting and requesting its profile. Type7 is validated telemetry only;
its profile is never copied into this selection. Android startup, retransmission,
value changes, new run, pause/reconnect and presence expiry cannot replace it.
Receiver restart reads SettingsFileStore and restores the local selection.

## Selected, Pending, Active and safe switching

Selected is the successfully persisted local choice. Active is the actual current
ResampledMotion profile while Running; stopped/starting states are displayed
without claiming an active production engine. Pending is Selected when it differs
from Active. A single motion-lock capture observes Selected, Active and algorithm
together; GUI never uses Android telemetry to populate those states.

RequestProfile retains the old Active through touching, M reconstruction-completion
release, and C release/glide. M joining DOWN and C interrupted DOWN keep the same
old chain. Existing Clear finishes/aborts according to the existing lifecycle,
clears reconstruction/ledgers and applies the most recent request at safe idle.
Cross-mode ApplyRequestedProfile clears C velocity/carry. Missed wakes retain
the original skip/no-catch-up rules. No motion writer, engine or protocol is added.

ResampledMotion's only algorithm-file addition is a locked observation method;
RequestProfile, ApplyRequestedProfile, interpolation, Tick/Emit, Clear, C servo,
rounding, history bookkeeping and release behavior are unchanged. C dynamics and
all quantizer/kernel/clock source files are unchanged. M keeps direct M8, Q0-C,
1 ms, reconstruction completion and no glide. C keeps C12, 4 ms/250 Hz, tau35,
damping1, Amax80000, Vmax15000, deceleration120000, thresholds0.5/2, Java rounding,
successful-submit ledger, natural carry and true glide.

## Android compatibility

Android source is unchanged in this commit. Its existing control and Sender may
still transmit legacy type7. Windows validates those packets as telemetry only.
Receiver-local persisted selection remains authoritative on reconnect/new run.
The independent Android reserved-control draft is not included or deployed.

## Offline verification and deferred deployment

ReceiverTouchModeTests covers strict schema/fallback, disk-first failure handling,
other draft preservation, real fake-backed runtime/GUI selection, restart, old
type7 in both directions, new-run reconnect, held-contact Pending/Active,
unchanged device creation counts, shared raw input to native Mouse, X/B full-state
output, exact old-chain output/timing through M completion or C glide, fresh
new-mode reconstruction/ledger equality and finite parking without drift.

Old tests that reset engine selection to M on sender-run change conflict with the
new local-authority requirement; they now retain local C12 while still asserting
abort/no output and stale-generation fences. The C runtime cleanup/failure test
selects C through local settings instead of type7, retaining its output/Stop/error
assertions. JSON field-count tests now require the additional explicit touchMode.
All independent M-R1/M-F1/M12 goldens and C oracle expectations are unchanged.

Evidence/build outputs: `windows/test-results/receiver-owned-mode-20261008/`.
Windows managed Release build uses `--artifacts-path` and a task-local target
override that copies the existing native bridge without rebuilding/publishing it.
Default test execution uses fake backends and temporary loopback endpoints.
Real ADB/instrumentation/input/HID smoke is excluded to preserve current usage.

Windows Receiver was deployed and accepted on 2026-10-10 together with the C-Z1
parameter panel. Broker/driver and Android were preserved. No input injection or
human game A/B was performed. M-R1 and sensitivity10/10 remained unchanged.
The formal commit excludes FIR research, keeping its default OFF behavior.
