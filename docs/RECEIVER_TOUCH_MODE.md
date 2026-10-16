# Receiver-owned touch mode (2026-10-08)

Current status (2026-10-16 source audit): Receiver-local selection was deployed
on 2026-10-10; the Android reserved no-op control was deployed with remote layout
on 2026-10-15. The implementation and deferred-deployment sections below record
their original 2026-10-08 stage. The historical layout coordinates are test
fixtures, not the phone's current authoritative layout. No deployment or user
data change accompanies this Git delivery. See
[GIT_DELIVERY_20261016.md](GIT_DELIVERY_20261016.md).

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

## Android reserved control and user layout

The original definition, stable ID `rightpad.input.mode`, static M drawing identity,
green/white appearance, Mode editor label, rectangular hit area, drag/resize and
normalized persistence remain. Router has no mutable selected mode. Matching UP
only clears ownership. DOWN remains consumed, MOVE never transfers it, and
CANCEL/wrong/second pointer clears it. Orphan events after reserved release/cancel
are consumed until the next DOWN, avoiding indirect safety packets.

The idle reserved contact emits no type7, Mouse, Xbox, haptic or other control
action and does not cancel an X/B tap pulse. A malformed replacement of an actually
live input owner retains existing safety cancellation. X/B definitions, gesture
engines, mappings, feedback policies and gamepad transport are unchanged. The same
control hit helper is used for the no-op check and actual router selection,
preserving overlap order and Settings/Power priority. No mapping framework is added.

Accepted user geometry at View 1200x2670 remains M=(513,772,264,1881),
X=(0,636,68,2034), B=(1041,506,159,2164). Left-side X is intentional.
No code migrates, initializes or writes this layout. LayoutStore and Editor are
unchanged. The complete source/backup SHA256 before work was
`d133f20e7a2fac6a886028af8f7679c23a25ea0149c4e305f5737c82ed68c522`.
Backup: `C:\RightpadBackups\rightpad-layout-20261008-172148.properties`.

## Historical offline verification and deferred deployment

ReceiverTouchModeTests covers strict schema/fallback, disk-first failure handling,
other draft preservation, real fake-backed runtime/GUI selection, restart, old
type7 in both directions, new-run reconnect, held-contact Pending/Active,
unchanged device creation counts, shared raw input to native Mouse, X/B full-state
output, exact old-chain output/timing through M completion or C glide, fresh
new-mode reconstruction/ledger equality and finite parking without drift.

Android's formal JVM entry includes ReservedControlTests with the exact accepted
layout fixture in a temporary directory, untouched source bytes, no-op owner and
pointer cleanup, generic drag/numeric resize/Cancel and unchanged X/B mappings.
Existing protocol/sender/config/haptic/gesture and fixed unbuffered regressions
remain. ScreenControlsSmoke now asserts no-op tap/hold/fault cleanup, no actual
type7/Mouse/Xbox packets or local haptics, and static identity. It is compiled but
not run on the user's phone in this stage.

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

Only after explicit deployment authorization: snapshot/re-hash phone layout and
Receiver settings, safely update Android in place without clear/uninstall, stop
the Receiver at an agreed idle point, replace its binaries, start normal GUI,
verify restored Selected/Active, then exercise both modes/no-op button/Xbox and
confirm exact layout bytes and INPUT/XBOX/CONFIG health. No Broker/driver/license
change is needed. The current task does not perform any of those steps.
