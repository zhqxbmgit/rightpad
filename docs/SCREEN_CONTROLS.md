# Screen Controls — current contract through Phase 6C

Updated: 2026-09-20. Phases 1–6C are implemented. Automated functional verification
and final real-person acceptance are complete. The user explicitly reported
"通过" for the final Screen Controls/Xbox/SlideControlLR functionality before
authorizing the combined commit and push. This confirmation is human evidence;
the ADB/XInput checks below remain separate functional evidence. Earlier phase
entries preserve their historical observations and do not override final acceptance.

## Control, appearance and ownership

### Phase 6A reusable LR gesture, Phase 6B production integration

`SlideControlLRGesture` is independent of the existing B `SlideControlGesture`,
which is unchanged. `SlideControlLRDefinition.phaseSix()` declares stable ID
`xbox.x.slide_lr`, label X and SYSTEM_CLICK feedback. Phase 6B adds it to the
production visible registry, normalized layout persistence and existing editor.
The saved B rectangle remains untouched. X uses the same solid green/red/white
visual contract and one rectangle for drawing, hit testing and editing, with no
invisible hit slop. Gesture `active()` expresses red while pending/held/directional,
and idle green after release, including during the remaining logical Tap pulse.

Defaults match the inspected Moonlight `moonlight-noir` branch:
Left 12.0 dp, Right 3.0 dp, Up 2.0 dp, Tap Hold 25 ms, Long Press 400 ms.
Config validates finite thresholds 0.1..50.0 dp, Tap Hold 1..200 ms and Long Press
50..2000 ms. An immutable config is captured at DOWN; density converts thresholds
once, and runtime config replacement applies only to the next DOWN.

DOWN enters pending with no BASE output, saves downX/downY, emits one PRESS
feedback and schedules LongPress. A short UP produces BASE then NEUTRAL after the
snapshot Tap Hold, measured from actual output time; no sleep or pulse queue is
used. The gesture hold is logical intent; Phase 6B also carries the existing
minimumDwellMs metadata so Receiver/backend dwell is enforced independently.
LongPress emits BASE at its deadline, with no haptic. UP/CANCEL release held state;
pending CANCEL never produces Tap. New DOWN cancels an unfinished prior pulse.

Only MOVE detects directions. For dx < 0 use Left threshold, otherwise Right;
if abs(dx) reaches that threshold, commit LEFT/RIGHT **before** considering UP.
Only when horizontal has not reached threshold, dy < 0 and abs(dy) >= Up may
commit UP. Thus dx=+4 dp, dy=-10 dp commits RIGHT at defaults. First commit wins
until UP/CANCEL; no angle, ratio or largest-displacement selection is used.
There is no DOWN direction or movement-slop cancellation: even large downward
movement remains eligible for Tap or LongPress. After BASE LongPress, Design A
emits NEUTRAL (BASE release) before the new direction; BASE is never combined
with a direction. UP itself does not create a new direction.

The standalone `SlideControlLRInstance` separates feedback and scheduling from logical gesture
state. Its one removable deadline callback uses the existing postDelayed/
removeCallbacks model; callbacks call `advance(now)`. It schedules state before
requesting best-effort feedback. Production Phase 6B uses `ScreenControlInstance`
to bridge either independent engine to `ScreenControls`' existing shared UI
deadline callback and transaction boundary. DOWN requests PRESS once; the first MOVE commit
requests DIRECTION_COMMIT once. LongPress, Tap pulse, UP, CANCEL and repeated MOVE
request no extra haptic, including Design A. Hardware/service/vibration exceptions
cannot change gesture state or timing, and do not select another effect or retry.

Feedback is selected by definition policy, never ID or label:

| Style | API >=29 | API 26–28 | API <26 |
|---|---|---|---|
| STRONG_ONE_SHOT (existing B) | oneShot(10, 255) | oneShot(10, 255) | vibrate(10) |
| SYSTEM_CLICK (LR) | createPredefined(EFFECT_CLICK) | oneShot(20, 120) | vibrate(20) |

Production minSdk remains 34; older branches are covered by injected API policy
tests. Touchpad remains a separate system CONFIRM path. VIBRATE remains declared.

Reference inspected: [SlideButtonLR.java](https://github.com/zhqxbmgit/zhq/blob/moonlight-noir/app/src/main/java/com/limelight/binding/input/virtual_controller/SlideButtonLR.java),
blob `ac47b7f9e437c2995b0792ea0f5133f1b81333a7`; defaults checked in
[PreferenceConfiguration.java](https://github.com/zhqxbmgit/zhq/blob/moonlight-noir/app/src/main/java/com/limelight/preferences/PreferenceConfiguration.java).
Reference settings, preference gates and visuals are not copied into Rightpad.

Phase 6B mapping belongs to `ScreenControlDefinition`, never the LR engine:
BASE=X, LEFT=D-pad Left, RIGHT=D-pad Right, UP=D-pad Up. The existing stable-ID
aggregator unions B and X contributions, including synthetic concurrent
contributors; releasing X never removes B. Design A is batched into one full
gamepad replacement. X Tap requests the existing 25 ms minimum dwell; directions
have zero dwell. GAMEPAD_STATE remains 30 bytes (v2/type5), reusing existing bits.
One existing Xbox360 device produces real XInput flags; no second controller,
keyboard, stick or mouse mapping is used. All lifecycle safety paths apply to LR.

X default geometry uses the existing definition placement: square 64 dp, origin
15% of View width and 55% of View height, clamped to View bounds, minimum 20 px.
On the current 1200×2670 View at density 3 this is X=180, Y=1469, W=192, H=192.
Normalized geometry is x=0.15, y=1469/2670, width=0.16, height=192/2670.
Missing X geometry is loaded from this default without writing the layout file.
User Save persists X under its own stable ID through the existing generic editor;
B's stored geometry is never replaced by an X default or historical fixture.
DOWN chooses B/X/Mouse ownership once, and crossing rectangles never reroutes;
second pointer continues global safe cancellation.

**Phase 6C:** Receiver Controls now edits LR Left/Right/Up, Tap Hold and Long Press
through the common explicit Save. Separate `controls.x` settings default to
12/3/2 dp and 25/400 ms; thresholds accept 0.1..50.0 dp in tenths, Tap Hold
1..200 ms and Long Press 50..2000 ms. Direct integer Long Press values such as
437 are valid; excess threshold precision is invalid without rounding.
Registry protocol ID 2 / behavior kind 2 identifies X. RPCT v2 includes complete
B+X in one 62-byte snapshot; Android commits it atomically, keeps behavior only
in memory and snapshots it at DOWN. Active gestures keep old configuration.
Controls Save never changes layout/haptics or requires Receiver restart.
See CONTROL_CONFIG_PROTOCOL.md and PROJECT_STATE.md for protocol and acceptance.

### Existing production B control

The existing B registration is stable ID `xbox.b.slide`, protocol ID 1, label B.
It uses reusable definitions, instances, router, SlideControl state machine,
aggregator, layout editor and store. No B-specific code exists in the transport
or Receiver dwell processor; those consume complete generic state and metadata.

| Gesture | Logical/full Xbox output | Release |
|---|---|---|
| Short Tap | B pulse, default 25 ms minimum dwell | Ordinary Neutral after pulse |
| LongPress, default 400 ms | Held B | Neutral on UP |
| Slide Up, default 0.7 dp | Held Y | Neutral on UP |
| Slide Down, default 3.0 dp | Held A | Neutral on UP |
| LongPress B then Up/Down (Design A) | Immediate full B→Y/A replacement | Neutral on UP |

Only one finger is supported. DOWN chooses one owner; crossing the rectangle never
transfers a contact between mouse and control. Additional/missing pointers cancel.
Once a slide direction is selected, it remains selected until UP/CANCEL.

Idle is solid green `#00C853`, active contact solid red `#FF3B30`, with white B.
Visual Rect equals Hit Rect: the same integer ControlRect, no gameplay hit slop.
The default is a 64 dp square, clamped to bounds; saved rectangles are supported.
UP restores idle appearance even while a Tap output pulse completes.

## Layout editor and persistence

Settings → Edit Controls Layout supports body move, four edges, four corners,
integer X/Y/W/H px input, Save, Cancel and Reset. One draft drives fields and
preview. Dragging clamps to the screen; invalid numeric input retains the last
valid preview and disables Save. Minimum size is 20×20 px. Save validates and
atomically persists before committing; failed persistence retains the draft.
Cancel/back discards it. Reset affects only the draft until Save. Editor entry
neutralizes gameplay and prevents mouse/control input during editing.

Android stores normalized geometry by stable ID in
`files/screen-controls.properties`, independently of behavior. Load converts to
integer View coordinates and validates/clamps. The current test phone's saved
rectangle at the Phase 5A audit was **X=100, Y=600, W=240, H=160 px** on its
1200×2670 View. This is historical evidence, not a forced product layout. At the
Phase 5A.1 start the user had saved X=1059/Y=700/W=141/H=1970. Preserve the actual
current file; instrumentation now restores its exact initial contents in finally
after exercising the temporary editor fixture. Never restore a historical or
default rectangle over a newer user layout.

## Behavior settings and transport

### Current local Screen Control feedback (Phase 5A.2)

The UI adapter emits generic `PRESS` once after a control DOWN and
`DIRECTION_COMMIT` once after the first MOVE that commits Up/Down, including
Design A after held B. Direction locking prevents repetition. LongPress, UP,
CANCEL and Tap pulse/expiry emit no feedback; a direction resolved only during
UP does not vibrate. Mouse ownership, editor operations, Settings and Power never
enter this feedback path. SlideControlGesture remains pure logic and unchanged.

`ScreenControlFeedback` applies a best-effort policy through an injectable backend;
`ScreenControlHapticFeedback` implements local Android vibration. API 26+ uses
exactly `createOneShot(10, 255)`, older APIs `vibrate(10)`, matching the fixed
Moonlight SlideButton parameters explicitly supplied by the user. HEAVY_CLICK
and its capability query are removed. No retry, duration increase or stronger
alternate effect is selected on failure. Product minSdk is unchanged. Missing vibrator/service and vibration
exceptions never alter logical/gamepad state, transport or mouse processing.
The manifest declares `android.permission.VIBRATE`; no runtime permission dialog
is added. Logs report the requested event/API path, not perceived strength.

Touchpad click still uses the existing Windows RPHF confirmation. Phase 5A.4
restores only its accepted-feedback execution to system CONFIRM, with no Touchpad
one-shot fallback. Screen Control source and its 10 ms / 255 policy remain unchanged.
The Screen Control path is local and independent, with no wire/Receiver/config
change. Actual strength depends on the device: **manual comparison of Touchpad vs Screen Control
10 ms / 255 is required**, including a second vibration on Up/Down and none at
LongPress recognition. API success alone cannot establish blind distinguishability.

**Phase 5A.4 — RIGHTPAD DISTINCT HAPTIC IDENTITIES COMPLETE (2026-09-20).**
After overwrite deployment the user confirmed **"A–D 全部满意，盲操作能明显区分"**:
Touchpad system CONFIRM crispness, stronger Screen Control DOWN, blind distinction
by haptic type, and a clear second Slide-commit 10 ms / 255. Screen Control source
and parameters were unchanged. This closes this specific haptic comparison; it
does not replace broader real-game Screen Controls acceptance.

Phase 5A.3's 6 ms / 120 Touchpad effect did not pass human crispness/satisfaction
acceptance ("Touchpad 不够清脆，或仍不满意"). Screen Control 10 ms / 255 was not
changed. See [HAPTIC_FEEDBACK_PROTOCOL.md](HAPTIC_FEEDBACK_PROTOCOL.md) for the
accepted-CLICK-only execution contract and regression evidence.

**Phase 5A.2 COMPLETE — human acceptance passed (2026-09-19).** The user tested
the installed 10 ms / 255 version and answered **"四项都符合，能明显区分"**:
Touchpad click retained its original feel, control DOWN was noticeably stronger,
the two could be distinguished without looking, and the second Slide-commit
vibration was clearly felt. This confirms the specified haptic comparison on
this device; it does not replace other outstanding game-feel acceptance items.

Phase 5A.2 verification: feedback policy tests 30, all previous Android JVM suites,
and device instrumentation 72 passed. Builds/lint passed with zero errors and the
same 14 warnings. Windows final full run passed 511/511. Its first run had a
Discovery test observation race: the client received the fifth OFFER before the
server's post-SendAsync counter increment was observed (expected 5, actual 4).
The unchanged binary passed the independent full rerun; the first failure log is
retained, and Windows production code was not changed in this phase.

Actual device logs show PRESS and DIRECTION_COMMIT using ONE_SHOT_10_255, with
the unchanged mouse CONFIRM performed=true. Eight XInput B holds (ms): 25.585,
25.652, 25.765, 27.814, 26.621, 38.830, 31.421, 31.205. All sender/backend minimum
holds stayed >=25 ms. B/Y/A, both Design A replacements, LongPress 1.306 s, lease,
Controls Save/current-DOWN snapshot, mouse movement and single click passed.
Config revision 7→8→9 restored 0.7/3.0/25/400. Current user layout and all saved
settings were preserved. Evidence: ignored `windows/test-results/gamepad-phase5a2/`.

Historical Phase 5A.1 automatic verification: 19 feedback policy checks passed (predefined,
unsupported/exception fallback, API 26/29/pre-26 paths, missing vibrator/service,
failure containment), all existing Android JVM suites passed, and real View
instrumentation passed 72 checks including event counts, both Design A directions,
Mouse/Power/Settings/editor isolation and failed-feedback logical/full-state Y.
The device used HEAVY_CLICK; fallback execution is verified through the injected
backend, not claimed as the actual device path. APK/test APK/lint builds passed
with the same 14 warnings and zero errors. Windows regression passed 511/511.

Eight fresh production XInput Tap durations with strong haptics enabled (ms):
34.428, 26.842, 25.522, 35.054, 26.324, 25.442, 26.674, 33.370; all sender/backend
minimum holds also stayed >=25 ms. LongPress 1.303 s, B/Y/A, both Design A
replacements, lease and Controls Save/snapshot/recovery passed. Config revision
5→6→7 restored 0.7/3.0/25/400; original settings and current user layout were
preserved. Mouse movement, one DOWN/UP and the unchanged CONFIRM path passed.
Evidence: ignored `windows/test-results/gamepad-phase5a1/`.

**Phase 5A.1 human acceptance: BLOCKED (2026-09-19).** After installing and testing
the actual phone (Android API 36), the user reported **"振动太轻了"**. The device
executed HEAVY_CLICK, not the fallback. The required noticeably stronger/blindly
distinguishable Screen Control feedback is therefore not accepted. This answer
does not establish separate human passes for all A–E event rules. Automated event
counts and input regressions remain passes, but cannot override the human result.
Further strength tuning needs another fixed-effect comparison and human acceptance;
no increased duration/amplitude or alternate pattern was silently introduced.

Receiver Controls is the sole behavior source. Defaults/current accepted values:
Up **0.7 dp**, Down **3.0 dp**, Tap Hold **25 ms**, Long Press **400 ms**.
Draft editing has no transport effect. Successful disk Save publishes one immutable
config epoch/revision; failed Save publishes nothing. Android stores behavior only
in a runtime cache, converting dp to px and copying the current snapshot at DOWN.
Changes during contact take effect on the next DOWN. Layout Save never stores
behavior. Epoch/revision validation prevents stale rollback; periodic type6
requests recover lost pushes on existing ports/workers.

GAMEPAD_STATE is v2 type5, exactly 30 bytes: full logical Xbox state plus generic
minimumDwellMs, flags and zero reserved. Touch types 1–4 bytes remain unchanged.
State changes request three identical copies; held state refreshes every 100 ms
with a newer sequence. Receiver independently applies serial ordering, 300 ms
lease and local monotonic minimum dwell. Its low-overhead deadline task is separate
from the mouse MotionClock. No network-jitter adaptation or B-specific dwell exists.

Tap supplies the DOWN snapshot's TapHoldMs. The Sender minimum wire hold remains
as an additional protection. LongPress/Slide supply zero dwell. Ordinary Neutral
can wait; a newer non-Neutral full state immediately replaces old state/pending
release. FORCE_NEUTRAL immediately clears pending/deadline after normal authority
and sequence validation. Duplicate/stale packets cannot mutate pending release.
The independent libvirtualhid `xbox_360` backend uses ABI2 and explicit logical
button mapping (B→XInput 0x2000, Y→0x8000, A→0x1000).

See [GAMEPAD_PROTOCOL.md](GAMEPAD_PROTOCOL.md) and
[CONTROL_CONFIG_PROTOCOL.md](CONTROL_CONFIG_PROTOCOL.md) for exact wire formats.

## Neutral/lifecycle audit

| Path | Mechanism reviewed and regression coverage |
|---|---|
| Tap expiry | UI pulse deadline → ordinary Neutral; sender hold and Receiver dwell protect it |
| LongPress/Slide UP | Complete Neutral, zero dwell; held refresh stops |
| ACTION_CANCEL / extra or lost pointer | stopCapture → gesture cancel + aggregator safetyClear → FORCE_NEUTRAL |
| Pause / foreground false | Cancel contact/deadline, clear Touch queue; final FORCE_NEUTRAL permitted |
| Target/run change | Cancel old state; old-route forced Neutral, clear hold/queue, new run starts Neutral |
| Editor/settings / View detach | stopCapture cancels callbacks and routes safety Neutral; editor consumes input |
| Sender close | Same worker attempts final forced Neutral before socket close; lease covers lost delivery |
| Presence disconnect / source or run replacement | Processor lock clears pending/dwell and neutralizes immediately |
| Lease expiry | Independent task clears held state after 300 ms without valid newer state |
| Receiver Stop / Safe Restart / Dispose | Stop processor/join maintenance, Neutral and destroy gamepad before mouse cleanup |
| Gamepad error | Clear processor pending state; backend attempts Neutral and destroys device; mouse remains active |

Source audit covered lifecycle callback invalidation, Sender sendGate/version
checks, late send completion, pending sequence consumption, timer/output locking,
config listener identity recheck after UI posting, channel disposal and disk-first
publication. Tests cover duplicate/stale/wrap, early/late Neutral, FORCE_NEUTRAL,
replacement, lease, restart and backend failure isolation. No correctness defect
requiring production code changes was found in Phase 5A. Failed native cleanup
remains a visible error/best-effort device-removal boundary, not a claim of immunity
to arbitrary driver or OS failure.

## Phase 5A automated and device evidence

Fresh Windows Debug and isolated Release each passed **511/511**, zero warnings
and errors. Native Debug and Release `bridge_fake_api` each passed; production
bridge matches the ABI2 Release build. All Android JVM suites passed: encoder 16,
Sender 4, UI/power 36, discovery 166, haptic 85 plus real UDP listener suite,
ScreenControls 49, gamepad 97 and config 84 checks. Instrumentation passed 49.
assembleDebug/assembleDebugAndroidTest/lintDebug succeeded; lint retained the same
14 existing warnings, zero errors and no new categories.

APK reinstall and instrumentation preserved the saved rectangle. Receiver remained
PID 23096 / Runtime RunId 1 on the current user's interactive Medium/Default desktop,
owning 0.0.0.0:50000 throughout Android recovery. The new Sender was admitted with
Touch sequence 0. Real Controls Save advanced revision 3→4 (Up 1.5 dp)→5 (restored
0.7 dp), epoch 06DA73520047E304, without Receiver restart. A 3 px movement retained
the old 2.1 px threshold during contact; the next DOWN used 4.5 px. Failed Save and
dropped-push recovery passed deterministic/integration tests.

Eight fresh production XInput Tap durations (ms): **29.560, 34.369, 28.295,
26.035, 32.784, 26.126, 39.819, 26.637**. Sender intervals and conservative backend
holds were all >=25 ms, despite valid-receive spacing as short as 12.004 ms.
Maximum observer polling gap was 1.724 ms. LongPress held for 1.312 s; Up Y, Down A,
both Design A replacements and release passed. Suspended Sender traffic caused
lease release at 324.493 ms after the last accepted state. Mouse moved and produced
exactly one LEFT DOWN/UP; Android logged click haptic `performed=true`. These are measured
functional observations, not a hard-real-time or subjective game-feel claim.

Read-only coexistence inventory found the Rightpad libvirtualhid Xbox360 device,
XInput slot 0, and no running Leftpad process or present DS4. No conflict was
observed in this state; simultaneous Leftpad/Rightpad coexistence was not exercised.
Leftpad was neither started nor modified.

Raw logs, exact monotonic timestamps/sequence correlation, observer, build/test
outputs and final diff inventory are under ignored
`windows/test-results/gamepad-phase5a/`. Generated APK/bin/obj/logs remain ignored.

A later repeated mouse observation missed window events; further retries found
Android no longer foreground and the PC pointer outside the test target. Those
attempts are not passes. After the user restored the phone and left input idle,
a guarded smoke verified MainActivity RESUMED, WindowFromPoint matching the inert
target, actual movement and exactly one DOWN/UP. Only the ignored observer script
was strengthened; no production input code changed to accommodate the test.

## Human acceptance — final user confirmation passed

The user confirmed final real-person testing passed for Phases 1–6C. The original
B/mouse/layout checklist below is retained as the covered acceptance scope;
the confirmation also includes X/LR and the frozen haptic identities. No game
title, quantitative comfort score or per-trial timing was supplied by the user.
Automated ADB results do not substitute for that explicit human confirmation.

| Item | Real operation | Required observation | Status |
|---|---|---|---|
| A | Short Tap B repeatedly | Each Tap recognized once, no missing or extra action | Covered by final user pass |
| B | Hold B >400 ms, then release | B remains held; release clears B without a stuck button | Covered by final user pass |
| C | Slide Up | Y held, no unintended B; UP clears Y | Covered by final user pass |
| D | Slide Down | A held, no unintended B; UP clears A | Covered by final user pass |
| E | Establish long-held B, then slide Up | Immediate B→Y replacement, no combined held state | Covered by final user pass |
| F | Establish long-held B, then slide Down | Immediate B→A replacement, no combined held state | Covered by final user pass |
| G | Normal mouse area movement/click/drag | Existing mouse operation and feel remain unaffected | Covered by final user pass |
| H | Open layout editor; numeric fields and mouse dragging | Move/edge/corner/numeric behavior and Save/Cancel/Reset are usable and bounded | Covered by final user pass |

For H, prefer Cancel after exploratory changes. If testing Save, restore the
user's starting geometry before finishing. Verify green idle/red active/white B and
that visual edges match touch edges. The final separate user instruction explicitly
authorizes committing and normally pushing the audited Phases 1–6C implementation.
