# Virtual Controls Config v2 (Phase 6C)

Receiver committed settings are the only behavior configuration source. Android
keeps an immutable runtime cache and snapshots it at ACTION_DOWN. Behavior is
never persisted on Android; layout remains Android-local under independent stable
IDs. GAMEPAD_STATE stays v2/type5, exactly 30 bytes. Touch, RPHF, minimum dwell,
Xbox mapping, haptic policies and layout format are unchanged.

## Registry, settings and explicit Save

| Stable ID | JSON key | Protocol control ID | Display | Behavior kind |
|---|---|---:|---|---:|
| xbox.b.slide | b | 1 | B Slide Control | 1 SlideControl |
| xbox.x.slide_lr | x | 2 | X / Slide LR | 2 SlideControlLR |

ID 2 was audited as unallocated before registration. `ControlDefinitions` owns
this mapping. `VirtualControlsSettings` contains separate immutable
`SlideControlSettings B` and `SlideControlLRSettings X`; LR fields are not added
to the B model. The common Controls page enumerates registered field editors.

```json
"controls": {
  "b": {
    "slideUpThresholdDp": 0.7,
    "slideDownThresholdDp": 3.0,
    "tapHoldMs": 25,
    "longPressMs": 400
  },
  "x": {
    "slideLeftThresholdDp": 12.0,
    "slideRightThresholdDp": 3.0,
    "slideUpThresholdDp": 2.0,
    "tapHoldMs": 25,
    "longPressMs": 400
  }
}
```

Thresholds: 0.1..50.0 dp, step 0.1, at most one decimal place. `3.14` remains
invalid and disables Save; it is never silently rounded. Tap Hold: 1..200 ms,
step 1, integer syntax (`25.0` invalid). Long Press: 50..2000 ms, step 10, with
any in-range integer accepted as direct input (`437` valid). Missing X in an old
file uses defaults 12/3/2/25/400. Each missing, wrong-type or invalid field falls
back independently, preserving valid neighboring fields and existing B behavior.

Editing changes draft only. The global Save validates, completes atomic disk
replacement, then publishes the immutable settings; only a changed Controls
publication advances the single global revision and pushes a full snapshot.
Failed disk Save retains draft, shows failure, and does not publish, advance
revision or push. Escape restores the committed field. Controls Save never
requires Receiver restart and does not change layout or Motion Save semantics.

## Windows -> Android, existing UDP 50002

The existing feedback sender worker/socket and Android listener multiplex RPHF
and RPCT by magic. No new socket/thread, service or acknowledgement is added.
Current discovery source IPv4 and destination Sender run are rechecked on the UI
thread after receive/post; this remains lifecycle/stale-data reliability checking.

All integers are little endian. Receiver now sends only RPCT v2.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | ASCII RPCT |
| 4 | 1 | version = 2 |
| 5 | 1 | message = 1, VIRTUAL_CONTROLS_CONFIG |
| 6 | 2 | reserved = 0 |
| 8 | 8 | uint64 destinationSenderRunId |
| 16 | 8 | uint64 configEpoch, nonzero |
| 24 | 8 | uint64 configRevision, nonzero |
| 32 | 2 | uint16 recordCount, 1..16 |
| 34 | 2 | reserved = 0 |

Every record starts with u16 controlId, u8 behaviorKind, u8 recordLength.
recordLength includes the four-byte prefix and must be at least 4.

| Relative offset | B: kind 1, length 12 | X: kind 2, length 14 |
|---:|---|---|
| +0 | u16 ID 1 | u16 ID 2 |
| +2 | u8 kind 1 | u8 kind 2 |
| +3 | u8 length 12 | u8 length 14 |
| +4 | u16 Up tenths-dp | u16 Left tenths-dp |
| +6 | u16 Down tenths-dp | u16 Right tenths-dp |
| +8 | u16 TapHold ms | u16 Up tenths-dp |
| +10 | u16 LongPress ms | u16 TapHold ms |
| +12 | — | u16 LongPress ms |

Threshold wire range is 1..500. Timing ranges match settings. The current full
snapshot is **36 + 12 + 14 = 62 bytes**. Shared C#/Java golden (run
0807060504030201, epoch 1817161514131211, revision 2827262524232221):

```text
525043540201000001020304050607081112131415161718212223242526272802000000
0100010C07001E0019009001
0200020E78001E00140019009001
```

The parser validates magic/version/message/reserved, nonzero epoch/revision,
count, nonzero IDs, prefix bounds, recordLength, exact final packet length,
known-ID uniqueness and registry kind match. Known kinds require their exact
length and valid fields, even for an unknown ID. Unknown IDs are skipped after
validation; unknown kind plus unknown ID is skipped using its bounded length.
Unknown kind for a known ID rejects the whole packet. Truncation, trailing bytes,
missing B or X, duplicate B/X, bad ranges and mismatched kinds all reject the
whole v2 packet. B and X must each occur exactly once before constructing the
immutable snapshot; no partial field/control application is possible.

Maximum framed size is 36 + 16*255 = 4116 bytes. The existing listener allocates
one extra byte to detect oversized datagrams. This only enlarges its bounded
receive buffer; no transport worker or cadence changes.

## v1 transition and ordering

Android also reads legal v1 packets with the original fixed 12-byte record and
zero reserved byte at +3; it never interprets that byte as length. They configure
B only, with LR defaults during transition. v1 is not acknowledged as a complete
B+X config: type6 known epoch/revision remain zero and the existing unknown-config
request cadence continues. A v2 snapshot may complete a v1 snapshot at the same
epoch/revision. Once v2 is active, v1 cannot downgrade it until target/run reset.
Receiver only emits v2 after upgrade.

Within an epoch, older revisions are rejected and duplicate revisions do nothing
(except the v1-to-v2 completeness upgrade). New epoch follows the existing rule;
retired epochs cannot roll back the current target's configuration. Target/new
Sender run clears cache and watermarks. Same-target foreground resume retains
cache but revalidates identity and actively requests current config.

B and X share one epoch/revision and one atomic snapshot. A Save during contact
changes only the cache; the current gesture keeps its captured thresholds and
timing. The next DOWN sees new values. Horizontal priority, first commit wins,
downward no-op and Design A remain byte-identical gesture-engine behavior.

## Android -> Receiver request, unchanged

CONTROL_CONFIG_REQUEST remains Protocol v2 type6, exactly 26 bytes:
version/type at 0/1, u64 senderRunId at 2, u64 knownConfigEpoch at 10 and u64
knownConfigRevision at 18. Epoch/revision are both zero or both nonzero.
Only a Connected current run/source is admitted. Requests cannot admit/renew
presence, affect Touch/gamepad sequence or introduce another revision domain.

The existing Android worker requests immediately on target/new run/foreground,
about every 1 second while config is unknown and every 5 seconds when known.
Receiver replies at most once per second with the complete latest snapshot.
Lost Save pushes recover through later requests without another Save, ACK,
retransmission framework or extra socket/thread.

## Phase 6C verification, 2026-09-20

Windows Debug and Release: 524/524. Android config JVM tests: 238 checks;
all other JVM suites and 139 instrumentation checks passed. Both APK builds and
lint passed (0 errors, 14 existing warnings). Tests cover exact golden bytes,
malformed framing/ranges, completeness, atomic rejection, version transition,
field fallback, UI syntax, failed disk Save, request recovery and DOWN snapshots.

The actual Controls GUI saved X Right 3 -> 10 -> 3 (revision 1 -> 2 -> 3), then
B Up 0.7 -> 1.5 -> 0.7 (revision 3 -> 4 -> 5), epoch 0CEDB408282EF3B5.
Disk, push and Android v2 cache were observed at each step. Real XInput showed
an active X contact retaining the old 9px threshold after Save, while the next
contact required 30px; B similarly retained 2.1px then required 4.5px.
Receiver PID 8008 / Runtime RunId 1 stayed unchanged during Save. Android restart
recovered the full revision 5 into an empty cache by type6 request without Save.
Final B and X defaults and user layout are retained. Evidence is under ignored
`windows/test-results/phase6c/`; see PROJECT_STATE.md for final input regression.

## Phase 4 verification, 2026-09-19

Debug and Release each passed 511/511 Windows tests, including nine new config
groups. Android passed 84 config checks plus all existing tests (97 gamepad checks,
49 logical controls tests, haptic/discovery/sender/UI suites). Device instrumentation
passed 49 checks. Builds succeeded; Windows had zero warnings/errors, Android lint
had zero errors and 14 pre-existing warnings.

The real Controls page initially showed 0.7/3.0/25/400. UIAutomation changed Up to
1.5 while a phone contact was already active, then invoked the common Save button.
The JSON was committed and Android accepted revision 2 of epoch 06DA73520047E304.
The existing contact retained 2.1px (0.7dp at density 3): a 3px move produced Y.
The next contact logged 4.5px (1.5dp): the same 3px move did not produce Y; a larger
move did. Saving 0.7 restored defaults and Android accepted revision 3. Receiver
PID 23096 and Runtime RunId 1 remained unchanged throughout both Saves.

After Android was stopped/restarted, its empty runtime cache automatically obtained
the same epoch/revision 3 via request, without another Save. Presence transitioned
Disconnected/Connected on the unchanged Receiver and new Touch sequence 0 was
accepted. The dropped-push recovery test independently discards a Save push and
verifies a later request returns the latest committed snapshot. Failed-disk-Save
tests verify no publish, no push/revision change and retained draft.

Eight real XInput Tap durations after restoring defaults (ms): 30.646, 38.218,
36.646, 30.169, 33.004, 36.574, 26.742, 30.470. Sender spacing and conservative
backend dwell also stayed >=25ms; maximum observer polling gap was 1.5ms. These
are measured observations, not a hard-real-time timing guarantee. LongPress 1.304s,
both slides, both Design A replacements, lease, mouse movement, one mouse click
and confirmed click haptic passed. Final defaults, legacy settings and layout were
preserved; Receiver output failures, input gaps and invalid packets were zero.

Evidence: `xinput-e2e.txt`, `tap-timing.json`, `config-smoke-android.txt`,
`config-recovery-android.txt`, `config-receiver.txt`, `settings-before.json`,
`settings-final.json`, `final-runtime.json`, and test/build/Git logs in that directory.

## Phase 5A integration recheck, 2026-09-19

Fresh Debug and isolated Release each passed 511/511; Android config checks 84
and all other JVM/instrumentation regressions passed. The real Controls UI again
verified draft isolation, successful disk Save, mid-contact old threshold and
next-DOWN new threshold. Epoch 06DA73520047E304 advanced revision 3→4 (Up 1.5 dp)
and 4→5 (restored 0.7 dp); Down/Tap/LongPress stayed 3.0/25/400. Receiver remained
PID 23096 / Runtime RunId 1. Android restart then recovered revision 5 into an
empty runtime cache without another Save. Local layout and all eight legacy
settings were preserved. Evidence: ignored `windows/test-results/gamepad-phase5a/`.
See [SCREEN_CONTROLS.md](SCREEN_CONTROLS.md) for the complete current contract and
the remaining real-finger acceptance checklist.
