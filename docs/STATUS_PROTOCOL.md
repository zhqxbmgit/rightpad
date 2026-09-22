# Receiver Status v1 — Phase 8A

RPST supplements Android's existing Discovery connection title/address with a
passive `INPUT ●   XBOX ●   CONFIG ●` row. Discovery presence is not proof of
backend health. The row is drawn at 9 sp, 17 dp below the address baseline, with
2 dp radius solid dots in the existing green/amber/muted red/gray palette.
It adds no View, hit rectangle, click action, touch owner, animation or card.
Settings, Battery, Power and B/X geometry/routing remain unchanged.

## Wire contract

Windows → Android, existing UDP **50002**, little endian, exactly **36 bytes**.
RPST is a separate magic, not an RPCT message kind. RPHF and RPCT are unchanged.

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | ASCII `RPST` |
| 4 | 1 | version = 1 |
| 5 | 1 | message = 1 (`STATUS_SNAPSHOT`) |
| 6 | 2 | reserved = 0 |
| 8 | 8 | uint64 destinationSenderRunId; every bit pattern is legal |
| 16 | 8 | uint64 configEpoch; nonzero |
| 24 | 8 | uint64 configRevision; nonzero |
| 32 | 1 | receiverState |
| 33 | 1 | flags |
| 34 | 2 | reserved = 0 |

States: 0 STOPPED, 1 STARTING, 2 RUNNING, 3 STOPPING, 4 ERROR.

| Flag | Meaning |
|---|---|
| bit 0 | MOUSE_INPUT_HEALTHY |
| bit 1 | GAMEPAD_AVAILABLE |
| bit 2 | RUNTIME_ERROR_PRESENT |
| bit 3 | MOUSE_OUTPUT_FAILURE_PRESENT |
| bit 4 | GAMEPAD_FAILURE_PRESENT |
| bits 5–7 | reserved, must be zero |

Shared Java/C# golden (run `0807060504030201`, epoch `1817161514131211`, revision
`2827262524232221`, RUNNING, both backends healthy):

```text
525053540101000001020304050607081112131415161718212223242526272802030000
```

Exact size, magic, version, message, reserved fields, known state/flag bits and
nonzero config identities are mandatory. Truncated/oversized/malformed status
cannot affect the other protocols. No strings or counters are transmitted.

## Source of truth and scheduling

`ReceiverRuntime.CaptureSnapshot()` supplies RuntimeState, Presence, LastError,
MouseOutputFailures, GamepadAvailable, GamepadFailures and LastGamepadError.
The minimal MouseAvailable field means a backend was created and this runtime
is Running; it does not require a successful movement or click. Mouse healthy
requires Running, MouseAvailable, no LastError and zero MouseOutputFailures.
Runtime Error or a non-null LastError sets bit 2. Any mouse output failure sets
bit 3. Gamepad failure count or a non-null LastGamepadError sets bit 4; backend
availability supplies bit 1. These are existing run-local observations/counters,
not a new health counter or an error-string parser.

`ControlConfigChannel.CaptureVersion()` reads the existing committed epoch and
revision together under its existing lock. Pending edits and failed Saves are
not advertised. Each Controls publication uses the existing single revision.

The existing HapticFeedbackSender async worker/socket refreshes RPST every
**500 ms**, best effort. No new socket, listener port or sender thread/task is
created. Phase 8A uses periodic refresh only. A delayed send resumes the same
interval without catch-up bursts. Each refresh reads the current presence route
at send time, without storing a queued RPST for a historical sender. It requires
Connected, a current run/source and presence age less than the existing 2000 ms
deadline. Without an active sender it sends nothing. Network errors remain
isolated from input. Status never admits/renews presence, Touch sequence or
gamepad lease, and never changes dwell or Motion scheduling.

Stop/error cleanup can end feedback before a terminal snapshot is sent. A final
STOPPED/ERROR datagram is therefore not guaranteed; local expiry plus Discovery
loss are the required recovery path. UDP may lose/reorder status. A later refresh
corrects transient observations; RPST adds no acknowledgement or ordering domain.

## Android lifecycle and semantic model

The existing `HapticFeedbackListener` name is retained to avoid unrelated
refactoring. It dispatches RPST alongside RPHF/RPCT. Before UI execution it checks
the listener generation, foreground activation, current Discovery source IP and
destination run against the Sender-owned identity. A queued callback cannot
cross pause, receiver change or sender transition. These are stale-data checks,
not authentication. The receive timestamp is captured before UI dispatch.

`InputHealthTracker` owns received state and produces immutable
`InputHealthStatus` values; TouchCaptureView consumes only GOOD/PENDING/ERROR/
OFFLINE. It never parses protocol bytes. RPST and accepted RPCT both recalculate
the display. An existing UI Handler schedules one expiry callback; no per-frame
polling or activity blinking is used. Repeated states do not invalidate the View.

When RPST age is **greater than 1500 ms** and Discovery is still connected, all
three items become PENDING (amber). Equality is still fresh. Discovery loss and
pause immediately make all items OFFLINE (gray); resume requires fresh status.
A sender change clears old status and rejects packets for the previous run.

Fresh-state mapping, in priority order:

| Item | GOOD (green) | PENDING (amber) | ERROR (red) | OFFLINE (gray) |
|---|---|---|---|---|
| INPUT | Running + mouse healthy, no runtime/mouse failure | First status missing; Starting/Stopping; Running without healthy proof | Error state, runtime error flag, or mouse failure | Disconnected; fresh Stopped without explicit failure |
| XBOX | Running + available, no runtime/gamepad failure | Starting/Stopping; stale snapshot | Explicit gamepad failure; Running without available backend | Disconnected; first status missing; Stopped/Error/runtime failure without explicit gamepad failure |
| CONFIG | Complete RPCT v2 B+X cache, exact epoch and revision match | Unknown/incomplete cache, first status missing, mismatch, or stale status | Not used | Disconnected |

Explicit failure takes precedence over a healthy flag. Runtime failure prevents
XBOX GOOD even if availability is present. XBOX first-status gray follows the
specific Xbox availability requirement; after a previously received snapshot
expires it is amber like the other two items. CONFIG reports version agreement,
independently of runtime/backend health, while status is fresh.

Matching a v1 B-only cache is never enough for CONFIG GOOD. A newer RPST does not
trigger another request mechanism or accelerate requests. Existing type6 polling
(unknown approximately 1 s, known approximately 5 s) recovers a missed RPCT push.

## Frozen contracts and verification

Touch types 1–4, heartbeat/presence, Discovery v1, RPHF 24 bytes, GAMEPAD_STATE
v2/type5 30 bytes, CONTROL_CONFIG_REQUEST v2/type6 26 bytes, RPCT v2 62 bytes,
gamepad lease/minimum dwell, B/X thresholds, haptic policies and saved layout are
unchanged. Portable export/import Phase 7A remains paused and unimplemented.

C#/Java tests share the golden semantics and cover malformed framing, unsigned
maxima, backend health before any input, config mismatch/v1 incompleteness,
runtime failures, timeout boundary, identity changes and pause/resume. Real UDP
listener tests prove wrong source/run rejection, queued callback invalidation,
and RPHF/RPCT operation after malformed RPST. Existing worker tests verify idle
suppression, periodic refresh, current destination and disposal. Device
instrumentation checks all four solid colors and Mouse routing at the dot,
preserving exact layout bytes through its existing finally restoration.

Current execution results and remaining human acceptance are recorded in
[PROJECT_STATE.md](PROJECT_STATE.md).
