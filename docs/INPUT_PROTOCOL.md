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

Preferred transport:

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

The protocol has three logical event types.

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

The exact binary layout may change, but the logical structure is:

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
    uint64_t timestamp;

    float x;

    float y;
}
```

---

# 9. Timestamp

Timestamp is required.

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

The receiver should record packet statistics.

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

The receiver must have a safety timeout.

If no valid input arrives for a configured period:

Actions:

- Cancel active touch session
- Release left mouse button
- Reset gesture state

Example:

```text id="l2e4e7"
Wi-Fi disconnect

↓

Release input safely
```

This prevents stuck input states.

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

Initial scope:

Local trusted network.

Do not implement initially:

- Accounts
- Cloud authentication
- Complex encryption systems

Security features may be considered after core input quality is proven.

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