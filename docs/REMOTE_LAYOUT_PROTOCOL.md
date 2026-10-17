# Remote screen layout (RPLY v1)

Android owns the only formal layout, `files/screen-controls.properties`. Windows
Controls opens a single owned layout editor window, with no persistent layout cache
or automatic push. Opening the editor queries actual View pixels, stable IDs and fixed tool bounds.
Existing normalized ratios and unknown properties are preserved by the original
atomic store. Reading, upgrading and draft edits never write the file.

Controls provides an actual-aspect schematic, B/X/Mode selection, read-only
integer X/Y/Width/Height, large +/- buttons, 1/10/50 px steps, Refresh, Discard,
Try, Keep and Restore. Every necessary operation uses left clicks. Green denotes
editable controls, black the Receiver preview mouse surface, orange fixed circular tools.
This is geometric data, not screen streaming. Ordinary settings Save changes
only behavior. Focus, page close and reconnect never apply a draft.

The Receiver process keeps one editor/view/draft instance. Its normal Windows window
defaults to maximized and has a click-only Return button. Close/Return hide the host;
hidden/minimized hosts stop the existing view timer, without disposing Runtime or input
devices. Before stopping polling, the client moves any unconfirmed operation to
reconciliation, preserving its original run/route, epoch, base revision, geometry and
token. It sends a reconciliation Query, never another mutation. Repeated inactive
notifications do not create another operation. Reopen resumes that same timer and
queries the existing client. Only explicit
Refresh/Discard reloads a dirty draft; phone terminal state and conflict reconciliation
continue through RemoteLayoutDraft.Observe. Closing does not Keep, Revert, extend or
restart a trial; Android still restores independently at its original deadline.

## Transaction authority

Android owns a random nonzero Activity epoch, formal revision, geometry identity,
transaction high-water mark and snapshot serial. Local editor Save advances the
same revision; Android local editor entry first aborts any remote trial. An epoch/revision or
geometry conflict rejects the whole mutation. Refresh discards an old Windows
draft; offline/unknown sizes disable mutation. New Activity/process loads only
the formal file and fences old packets with a new epoch.

| State | Transition |
|---|---|
| Ready / terminal | A valid next-token Try validates the entire candidate, enters Waiting |
| Waiting | Publish one map at contact idle, within 10 seconds; no disk write |
| Trial | Fixed 60000 ms elapsedRealtime deadline starts at actual publication |
| Trial + Keep | Recheck monotonic time at commit entry; original atomic Save succeeds before advancing revision and entering Saved |
| Trial + failed Save | Restore preceding formal map, report Failed/disk error |
| Waiting/Trial + expiry, cancel or lifecycle abort | Restore formal map, retain terminal token |
| Saved + duplicate Keep or delayed timeout | Report saved, no further write or rollback |

Requests, timer, publication and commit entry serialize on the Android UI thread.
A Save admitted before expiry can finish afterwards; timeout cannot interleave
with atomic replacement. Timer wakes at the earlier of 100 ms or the remaining
deadline; every new DOWN checks expiry before hit testing. Windows cannot extend
the deadline. A repeated Try never refreshes it. A Revert arriving before Try
consumes its offered token, so delayed Try cannot resurrect the candidate.

Normal Try waits for contact idle. Recovery changes the whole rectangle map on
the UI thread; an existing contact keeps its original owner ID and gesture
instance and is never re-hit. Drawing and hit testing read the same map. Layout
publication itself produces no Mouse/Xbox input. Existing lifecycle safety
cleanup and leases still apply; layout never invents a Touch CANCEL/UP.

Pause, destruction, target/run change and local editor entry abort trials.
View size change reloads formal ratios and changes geometry identity; fixed-tool
or inset changes restore formal geometry and invalidate old drafts. Neither PC's
cache can overwrite the phone. No unfinished trial survives process recreation.

## Wire format

RPLY was audited as unused. It allocates no Touch type number. Windows -> Android
uses the existing bounded feedback worker and UDP50002 listener; Android ->
Windows uses the existing Sender/socket and UDP50000 receiver. Touch types 1–4,
type5 GAMEPAD_STATE, RPCT, RPHF and RPST retain their formats and sequence domains.
Layout cannot establish or renew Touch presence or gamepad leases.

All integers are little endian, header 88 bytes, maximum packet 4096 bytes and
16 records. Snapshot is the capability-version acknowledgement.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | ASCII RPLY |
| 4 | 1 | Version 1 |
| 5 | 1 | Kind: Query 1, Try 2, Keep 3, Revert 4, Snapshot 128 |
| 6 | 1 | State: Ready 0, Waiting 1, Trial 2, Saved 3, Restored 4, Failed 5 |
| 7 | 1 | Error: none 0, stale 1, invalid 2, busy 3, disk 4, expired 5, cancelled 6 |
| 8 | 8 | Current Sender run |
| 16 | 8 | Android Activity epoch (Query may use zero) |
| 24 | 8 | Formal layout revision |
| 32 | 8 | View geometry identity |
| 40 | 8 | Transaction token / last transaction |
| 48 | 8 | Request correlation, echoed; unsolicited status uses zero |
| 56 | 8 | Next offered token = last + 1 |
| 64 | 8 | Android increasing snapshot serial |
| 72 | 4 | Remaining trial ms, 0..60000 |
| 76 | 4 | Actual View width, integer px |
| 80 | 4 | Actual View height, integer px |
| 84 | 2 | Record count |
| 86 | 2 | Reserved zero |

Record: u8 UTF-8 ID length (1..64), u8 label length (0..64), u16 flags (0 editable,
1 fixed circular tool bounding box), signed i32 X/Y/W/H, then UTF-8 ID and label.
Try carries exactly all registered editable IDs, no tools. Query/Keep/Revert
carry no records. Snapshot carries actual effective controls plus tool geometry.

Validate version, kind, reserved, flags, bounds, exact length, UTF-8 IDs, duplicate
IDs, noncurrent run, epoch/base/geometry, exact known-ID completeness, minimum
20 px size, overflow and View containment. Exact rectangle-union coverage must
leave some mouse surface; no overlap rule, hit-priority change or arbitrary
reserved corner is introduced. Existing reserved Mode meaning remains unchanged.

There is one pending coalesced layout dispatch slot in the listener and one
current-route Sender response slot. Windows holds one operation, with a monotonic
start and a fixed three-second expiry (the expiry instant is ineligible for sending).
It tries at most three times at one-second intervals within that lifetime, then
queries actual state. Force polling cannot bypass the lifetime or mutation cadence.
Capture, Begin, Accept, Poll and inactive-window entry all check expiry; no additional
thread or timer is needed. Query IDs are bounded to eight. A lost Query/result can
be retried on the existing one-second polling cadence, preserving one reconciliation
identity rather than accumulating transactions. Hidden hosts do not poll. Reconnect
is read-only: temporary absence retains reconciliation for the same route/run;
a changed route/run clears the old capability and queries the new authority.
Nonzero replies must match a
request; source/run and epoch/serial checks reject old snapshots. Windows
subtracts observed round-trip time and never extends an observed trial deadline.
Lost Keep/result packets reconcile from Android state rather than guess.
After an unanswered Try expires, exhausts its attempts, or its editor becomes
hidden/closed/minimized/unloaded, the reconciliation Query also carries that
Try's base identities and token. If Android never saw it, the Query consumes the
unused token before reporting Restored. Late first copies cannot then apply.
Queries sent before the operation are fenced; an unconsumed Ready reply on the
same base cannot confirm sealing. Until an authoritative result arrives the UI
reports an uncertain result and a new Try is refused. A Query finding Waiting or
Trial reports that actual state without restarting its phone deadline. Unconfirmed
Keep also switches to querying on inactivity/expiry; Saved, Restored and Failed are
reported from the phone without an automatic Revert or guessed persistence result.

## Tests

Java RemoteLayoutTests exercise the real transaction engine, explicit clocks,
whole-map publication, commit/expiry races, disk errors, duplicates, stale packets,
geometry/local-save conflicts and real temporary atomic storage. Windows
RemoteLayoutTests cover shared golden framing, draft isolation, retry limits,
result reconciliation, serial/run fencing, countdown and presence isolation.
RemoteLayoutSmoke exercises the real View/router/store under LayoutTestFileGuard
and restores exact original bytes in finally. Fake/device automation does not
constitute human usability or game-feel acceptance.
