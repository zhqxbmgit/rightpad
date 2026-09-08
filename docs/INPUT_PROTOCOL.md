# rightpad Input Protocol Design

## 1. Purpose

This document defines communication between:

```text id="p9n2vk"
Android Touch Client

        ↓

Local Network

        ↓

Windows Receiver
```

The protocol transports raw touch information.

The receiver is responsible for converting touch data into mouse input.

---

# 2. Design Goals

The protocol should provide:

- Low latency
- Small packets
- Simple implementation
- High sampling rate support
- Robust handling of packet loss

The protocol is designed for:

- One Android device
- One Windows PC
- Local network
- Gaming input

---

# 3. Transport

Protocol v1 transport (frozen): UDP, receiver port 50000.

Transport:

```text id="m4f8qp"
UDP
```

Reason:

- Low latency
- Small messages
- Latest input data is usually more valuable than old data

---

# 4. Non-Goals

The initial protocol does NOT implement:

- TCP
- WebSocket
- HTTP
- Cloud communication
- Compression
- Forward error correction
- Congestion control
- Multi-device support
- Internet routing

---

# 5. Data Philosophy

The protocol transmits:

```text id="7xj6o4"
Touch state
+
Absolute touch position
+
Timestamp
```

The protocol does NOT transmit:

```text id="h0v2bx"
Mouse dx/dy
Sensitivity
Gesture result
Filtered movement
```

Reason:

The Windows receiver must own input processing.

---

# 6. Packet Types

Protocol v1 has exactly three event types: DOWN = 1, MOVE = 2, UP = 3.
DOWN and UP each contain exactly one sample; MOVE contains at least one.
CANCEL is not part of Protocol v1.

---

## DOWN

Generated when finger touches the screen.

Purpose:

- Start a touch session
- Establish a new origin

Example:

```text id="wq0m8p"
DOWN
sessionId = 100
```

---

## MOVE

Generated while finger moves.

Contains:

- Touch samples
- Position
- Timestamp

---

## UP

Generated when finger leaves the screen.

Purpose:

- End current touch session
- Release possible drag state

---

# 7. Touch Session

Every touch interaction has a session ID.

Example:

```text id="44lqf7"
Session 100

DOWN

MOVE

MOVE

UP


Session 101

DOWN

MOVE
```

A new session must never inherit previous coordinates.

This prevents:

```text id="u9x6bh"
Previous finger position:
900

New finger position:
200

Incorrect output:
-700 movement
```

---

# 8. Packet Structure

Protocol v1 binary layout is frozen. Version is 1. All multi-byte fields use
Little Endian. Fields are packed without alignment padding. One UDP datagram
contains one complete packet, with no trailing bytes or additional fields.

```c id="h79v0q"
PacketHeader
{
    uint8_t  version;

    uint8_t  eventType;

    uint16_t sampleCount;

    uint32_t sessionId;

    uint32_t sequence;
}
```

---

Each sample:

```c id="iqw0p8"
TouchSample
{
    uint64_t timestampNs;

    float x;

    float y;
}
```

---

# 9. Timestamp

`timestampNs` is required: unsigned 64-bit event time in nanoseconds, not
milliseconds. The implemented Android UDP Sender uses
`MotionEvent.getEventTimeNanos()` for current samples and
`MotionEvent.getHistoricalEventTimeNanos(...)` for historical samples. Android
`TouchSample`, CSV, Logcat diagnostics, and Protocol v1 all use
`eventTimeNs` / `timestampNs`; there is no `eventTimeMs` compatibility path.

The 12-byte PacketHeader is followed by sampleCount 16-byte TouchSamples:

| Byte offset | Field | Type / size |
|---|---|---|
| 0 | version | uint8 / 1 byte |
| 1 | eventType | uint8 / 1 byte |
| 2 | sampleCount | uint16 / 2 bytes |
| 4 | sessionId | uint32 / 4 bytes |
| 8 | sequence | uint32 / 4 bytes |
| 12 + 16 * i | sample[i].timestampNs | uint64 / 8 bytes |
| 20 + 16 * i | sample[i].x | IEEE 754 float32 / 4 bytes |
| 24 + 16 * i | sample[i].y | IEEE 754 float32 / 4 bytes |

Exact packet size: `12 + sampleCount * 16` bytes.
Reject unsupported versions/events, invalid sample counts, length mismatches,
and non-finite coordinates. Preserve sample order and repeated timestamps.
Receiver-local receive time is diagnostic metadata, not a protocol field;
unsynchronized Android and Windows clocks cannot directly measure one-way latency.

Purpose:

- Measure sampling interval
- Analyze latency
- Support future timing improvements

The timestamp should use a monotonic clock source.

Do not use:

- Wall clock time
- System date/time

---

# 10. Sequence Number

Each packet has:

```text id="j3kw90"
sequence
```

Purpose:

Detect:

- Lost packets
- Duplicate packets
- Out-of-order packets

The receiver should record packet statistics. Sequence is uint32 and increases
across touch sessions during one sender run. Repeated copies of the same packet
retain its sequence number. The current Prototype uses ordinary unsigned
comparison and intentionally does not handle uint32 wraparound. Restart the
Receiver to establish a new baseline after restarting the Sender.

The first valid packet establishes a baseline. Forward jumps contribute to a
cumulative sequence-gap estimate, not an exact final network-loss count.
Equal sequences are duplicates; lower sequences are old/out-of-order (possibly
older duplicates). These do not advance the baseline or contribute new samples.
Late packets do not subtract from the gap estimate. There is no retransmission
or reordering layer. Remote IP/port is diagnostic only; no sender locking or
other-source rejection is implemented. Statistics assume the intended single sender.

---

# 11. Packet Loss Handling

## MOVE

MOVE packets are not retransmitted.

Reason:

The next absolute position can recover movement.

Example:

Received:

```text id="5h7f3a"
x=100

(packet lost)

x=110
```

Receiver can calculate:

```text id="j2v88h"
110 - 100 = 10
```

---

## DOWN / UP

DOWN and UP are more important.

A lost UP packet can cause:

- Stuck drag
- Stuck button state

Therefore:

Initial implementation may send:

```text id="1l7d6v"
DOWN
DOWN
DOWN
```

and:

```text id="a1gc7p"
UP
UP
UP
```

Duplicate packets are ignored by sequence/session validation.

Do not build a general reliability layer.

---

# 12. Receiver Timeout Safety

The current Prototype has a 2-second input timeout. It is diagnostic only: the
receiver emits a diagnostic and increments the timeout statistic.

A timeout does not change TouchSession or motion state. In particular, it does
not clear the active session, previous X/Y position, or fractional residual.
A stationary held finger may produce no new MotionEvent, so packet absence alone
cannot reliably distinguish:

- Stationary touch
- Sender disconnect

After Gesture or mouse-button output is implemented, a stuck-button fail-safe
will still be required. That work is **DEFERRED** because Protocol v1 currently
does not provide enough information to make the distinction above reliably.
This document does not prescribe an unapproved connection-state mechanism or
wire event.

---

# 13. Historical Samples

Android may receive multiple samples in one MotionEvent.

Example:

```text id="9qk4kp"
Historical Sample

Historical Sample

Current Sample
```

The client should preserve these samples.

A packet may contain:

```text id="kqj6zs"
sampleCount = 3
```

instead of creating three separate packets.

---

# 14. Coordinate Format

Initial implementation:

Use Android raw coordinates:

```text id="d2a5l9"
float x

float y
```

Do not initially implement:

- Fixed point compression
- Coordinate normalization
- Delta compression

Reason:

Bandwidth is not a problem.

Simplicity is more valuable.

---

# 15. Network Threading

Initial design:

```text id="2g7t1n"
Touch Capture

↓

Packet Encode

↓

UDP Send
```

Avoid:

- Complex queues
- Multiple network workers
- Async frameworks

unless measurement shows a problem.

---

# 16. Future Compatibility

The protocol should allow future additions:

Possible future fields:

- Device information
- Diagnostics
- Configuration synchronization

However:

Do not add unused fields in version 1.

---

# 17. Security

Security is intentionally out of scope: one user, one Android phone, one
Windows 11 PC, personal use on a trusted local network. Do not implement
authentication, pairing, PINs, encryption, TLS/DTLS, tokens, certificates,
signatures, HMAC, anti-replay, multi-user permissions, or security handshakes.
Do not lock onto the first sender or reject sources by IP/port.
Length/version/field validation and malformed packet rejection remain current
reliability requirements. Timeout diagnostics are implemented; the future
stuck-input fail-safe remains a deferred reliability requirement.

---

# 18. Protocol Principle

The protocol should follow:

```text id="q5sv0y"
Transmit facts.

Process decisions on Windows.
```

Android reports:

- What happened

Windows decides:

- What it means
- How it feels
- How it becomes mouse input

This keeps the architecture clean and tunable.
