# Receiver Discovery Protocol v1

rightpad automatically selects the local Windows Receiver on a trusted Wi-Fi LAN.
The current product has one phone and two PCs at different locations; the PCs are
not expected on the same LAN simultaneously. Opening the Android app requires no
IP entry, saved host list, PC selector or APK rebuild after Windows DHCP changes.
There is no cloud, pairing, authentication or explicit disconnect.

## Wire layout

IPv4 UDP port **50001** is independent of Touch Protocol v2 on **50000**.
All integers are little endian. One datagram is exactly one packet, without
padding or trailing bytes. The nonce correlates the current probe; it is not a
security token. Android uses a new random 64-bit nonce for each round and accepts
only that round's replies, from UDP source port 50001. Every uint64 bit pattern,
including zero, is valid.

| Offset | Size | DISCOVER (16 bytes) | OFFER (40 bytes) |
|---:|---:|---|---|
| 0 | 4 | ASCII `RPAD` | ASCII `RPAD` |
| 4 | 1 | discoveryVersion = 1 | discoveryVersion = 1 |
| 5 | 1 | type = 1 | type = 2 |
| 6 | 2 | reserved = 0 | reserved = 0 |
| 8 | 8 | nonce | echoed nonce |
| 16 | 16 | — | receiverId, opaque bytes |
| 32 | 2 | — | touchPort = 50000 |
| 34 | 1 | — | touchProtocolVersion = 2 |
| 35 | 1 | — | reserved = 0 |
| 36 | 4 | — | capabilities = 0 |

Receivers reject every invalid DISCOVER length/header/type/reserved field.
Android additionally validates exact OFFER length, nonce, touch port, protocol
and reserved fields. Unknown capabilities are ignored, without enabling behavior.
Malformed and unknown datagrams increment discovery diagnostics, never Touch
statistics. A 41-byte Android receive buffer prevents oversized replies from
being truncated into an apparently valid 40-byte OFFER.

**The authoritative Receiver address is the received OFFER datagram's source
IPv4 address.** OFFER carries no IP or hostname. Neither endpoint enumerates
Windows interfaces or picks a first IPv4; Windows TUN/Mihomo interfaces cannot
be accidentally advertised by a payload heuristic. Android rejects non-IPv4,
unspecified, loopback and multicast sources.

## Windows identity and lifecycle

`%LocalAppData%\rightpad\receiver-id` contains exactly 32 ASCII hex characters
representing 16 random opaque bytes. Wire order is that exact byte order, without
GUID field endian conversions. This is current-user installation identity, not
MAC, hostname, SID or username. Normal restart and IP changes retain it.

A short startup-only named mutex serializes create/load/repair across processes.
A new identity is flushed to a unique same-directory temporary file and atomically
renamed into place. Invalid content is diagnostically replaced; storage/lock
failure makes startup fail explicitly rather than advertise an unstable ID.

ReceiverRuntime first creates the mouse backend and successfully binds Touch
50000, then binds DiscoveryResponder to `0.0.0.0:50001`. It marks the runtime
Running and starts the separate asynchronous responder. A valid DISCOVER is
answered by unicast to its datagram source IP and source port, echoing the nonce.
No WPF Dispatcher performs socket I/O. Stop cancels both loops; a fatal loop or
input error cancels its sibling, awaits both, and releases both ports. Startup
failure releases all acquired resources. As with any UDP readiness observation,
an OFFER cannot guarantee that a process remains alive after sending it.

Separate inbound allow rules are used for UDP 50000 and 50001, **Private only**.
Public profile is never opened. Trusted LAN validation and timeout fail-safes
remain reliability measures; discovery adds no source authentication.

## Android network ownership

`ConnectivityManager.NetworkCallback` tracks Wi-Fi availability, loss, link
properties and capabilities. Cached callback payloads are used instead of racing
synchronous network queries. Select only `TRANSPORT_WIFI` with `NOT_VPN`, never
cellular, VPN or a first active network. If more than one qualifying Wi-Fi exists,
retain the selected one while available, otherwise use the first qualifying
callback entry. Network or IPv4 address/prefix changes invalidate selection.
IPv6-only updates and address-list ordering do not reset an IPv4 connection.

One dedicated discovery thread owns its DatagramSocket and schedule. It calls
`Network.bindSocket(DatagramSocket)` for the selected Wi-Fi, enables broadcast,
and binds an ephemeral reply port. It never calls `bindProcessToNetwork`.
Touch keeps its existing independent sender socket and normal OS routing.

Each round sends to `255.255.255.255:50001` and all distinct directed broadcast
addresses calculated from the selected Wi-Fi IPv4 addresses and actual prefix
lengths. Prefixes 1..30 are supported; /0, /31, /32, invalid prefixes and IPv6
are skipped for directed broadcasts, retaining limited broadcast. A failure to
send one destination does not discard successful probes to others.

## Selection, timing and lifecycle

- Searching probes immediately, then at nominal 250, 500, 1000 ms and every
  1000 ms thereafter. OS scheduling/socket wake granularity can delay a probe;
  overdue probes send once, without catch-up bursts.
- First fully valid OFFER wins. Connected retains that receiverId; offers from
  another identity cannot preempt it or renew its deadline. A current-ID OFFER
  refreshes liveness and may update the source IP after DHCP changes.
- Connected continues LAN probes every 1000 ms. At **2500 ms** without a current
  valid OFFER, selection becomes Searching and clears the sender target. The
  independent worker checks deadlines at most every 100 ms while idle. The
  allowance spans more than two normal probe intervals, tolerating one lost round.
- Network loss/change clears selection and closes/rebinds the discovery socket.
  UI notifications are generation-checked so an obsolete network/lifecycle
  callback cannot install a stale target after pause or shutdown.
- Pause suspends discovery sockets/probes and disables Touch/heartbeat. Resume
  immediately probes and enables input only after a fresh valid OFFER. On the
  same Wi-Fi, a 2500 ms confirmation window preserves the old selection while
  input remains disabled; a confirmed identical identity/address retains runId
  and sequence even after a long pause. Failed confirmation or changed network/
  identity/address clears selection and rotates the run. UI shows Searching
  during confirmation. A pause itself never generates a new sender run.
- Power exit closes discovery and sender, then exits the Activity normally. No
  background service, WakeLock or process-wide routing change is used.

Android displays `搜索中` / `—`, or `已连接` / actual OFFER source IPv4. Battery,
visual-only Settings, Power hit area, immersive view and graphics are retained.
Connected describes fresh Receiver discovery readiness, not an ACK of each Touch.

## Clean target transition

For none→A, A→none, A→B, a changed source IP or changed receiver identity, the UI
thread stops capture and invalidates the active pointer before calling setTarget.
The sender clears pending Touch, clears its DOWN/session gate, rotates senderRunId,
resets sequence to zero and the heartbeat schedule, atomically installs a route,
and wakes its worker. Every queued packet retains its immutable route object.

A lifecycle-only gate serializes route changes with actual sends; ACTION_MOVE
does not take that gate or perform discovery/network/DNS work. A packet already
dequeued on the old route is rejected after a transition, and cannot reach either
new or old destination afterward. Previously transmitted UDP may of course
already be in flight. The new target is sent heartbeat before any new Touch;
MOVE/UP without a fresh matching DOWN cannot enter its queue. There is no target
at production startup and no fixed-IP fallback. No target means no sends, no
queued Touch and no sender error.

The old Receiver gets no explicit disconnect. Its unchanged Protocol v2 2000 ms
presence timeout naturally clears session, residual, gesture and held/pending
LEFT input. Touch and heartbeat wire layouts and Receiver admission semantics
remain unchanged.

## Android 16 local-network protection

compileSdk/targetSdk remain 36. Manifest declares INTERNET, ACCESS_NETWORK_STATE
and NEARBY_WIFI_DEVICES with `neverForLocation`; no location permission or normal
startup permission dialog is added. Android 16 LNP is opt-in, but covers UDP
unicast/broadcast send and receive. EPERM/EACCES/SecurityException diagnostics
explicitly identify local-network permission denial, distinct from absent offers.

Optional development validation can enable `RESTRICT_LOCAL_NETWORK`, then grant
Nearby Devices for that test. Record initial state and restore compat override
and permission state afterward; do not leave the user's device altered. This
task's normal-mode verification does not require that opt-in or a grant.
Target 37 migration must separately implement the then-required permission; the
current target 36 app must not request ACCESS_LOCAL_NETWORK unconditionally.
Sources: [Android local-network permission](https://developer.android.com/privacy-and-security/local-network-permission),
[Network.bindSocket](https://developer.android.com/reference/android/net/Network#bindSocket(java.net.DatagramSocket)),
[NetworkCallback](https://developer.android.com/reference/android/net/ConnectivityManager.NetworkCallback).

## Verification and diagnostics

Windows tests cover golden bytes, all truncated/oversized lengths, malformed
fields, nonce/identity, repeated unicast replies, invalid isolation, identity
reload/repair/races, touch readiness, bind failure, stop and runtime-error cleanup.
Android JVM tests cover codec/nonce, selection/deadline, directed prefixes/dedup,
UI text, cadence and real loopback A→B routes. A dequeue barrier proves old queued
and already-dequeued MOVE cannot cross a transition; first heartbeat, new runId,
sequence zero and fresh DOWN are asserted.

Low-frequency Windows logs include discovery_started, discovery_offer_sent
(first and every 60 offers), invalid_discovery_packet (first and every 1024), and
final totals. These events go to the existing Flight Recorder even in normal GUI
launches without a development log directory. Android logs network/lifecycle/target changes, connection time,
receiverId/source, timeout and throttled socket/permission errors. Normal probes
are not logged. Discovery never enters Touch queue, state machine or counters.

Real verification uses the approved independent interactive Receiver launcher,
Private firewall, USB ADB, foreground Android, automatic source-address discovery,
pause/resume, Power reopen, safe Wi-Fi toggle and existing inert-window RAW/Single
Tap smoke. A second physical PC at the other location still needs field testing;
fake A→B coverage does not claim that physical site has been visited.

### Acceptance on 2026-09-12

Windows Debug and Release each passed 145 tests with zero build warnings/errors.
Android passed 16 encoder checks, 4 Sender groups, 36 UI checks and 166 discovery
checks; assembleDebug/lintDebug passed (0 lint errors, 6 existing category warnings).
The final single-site suite used one unchanged Receiver PID 3384 / RuntimeRunId 1,
interactive Session 1, Medium integrity and Default desktop; both UDP ports were
owned by that PID. Production Flight Recorder discovery events were verified.

The final APK found the physical LAN OFFER source automatically in 122 ms from
foreground discovery readiness (21 ms after Wi-Fi properties became available).
Power reopen took 119 ms; same-target pause/resume confirmation took 61 ms;
Wi-Fi recovery discovered in 5 ms after IPv4 readiness. These are observed startup
samples, not latency benchmarks. Mihomo remained Public and was not selected.
Four final inert-window RAW/Single Tap rounds produced movement and exactly one
LEFT DOWN/UP each, with zero invalid/gap/old/SendInput failures and no stale session.
Wi-Fi toggle produced exactly one new non-null target after IPv4 readiness;
subsequent IPv6 updates preserved it.

Moving between PC development environments exposed a debug-certificate mismatch
on the phone. With explicit user approval, the old APK and all 36 persistent files
were backed up, the app reinstalled and every restored file SHA-256 verified.
Later updates used install -r normally. Local evidence is under ignored
windows/test-results/discovery-*; no APK, logs, identity or machine settings are
committed. Android 16 LNP opt-in was not enabled; no permission state/compat flag
was changed for that optional test. Only the second physical location remains
unverified for this feature.
