# AGENTS.md

# rightpad Project Instructions

## 1. Project Identity

Project name:

```
rightpad
```

rightpad is a dedicated Android-to-Windows gaming input system.

The purpose is to turn an Android phone into a high-quality touch input device for Windows 11 gaming.

The project is focused on:

- Low latency
- Stable input feeling
- Predictable behavior
- Long-term maintainability

This is NOT:

- A remote desktop application
- A streaming application
- A general office mouse replacement
- A cloud service

---

# 2. Target Platform

## Supported

Android client:

- Touch input device

Windows receiver:

- Windows 11 only

## Not a current goal

Do not add support for:

- macOS
- Linux
- iOS
- Cloud platforms
- Mobile-to-mobile usage

unless explicitly requested.

---

# 3. Core User Requirements

## Touch Operation

The device is operated with:

- One finger only
- Portrait phone orientation
- Phone screen is not viewed during use
- Phone acts as a dedicated touch surface

Do not add:

- Multi-touch
- Two finger gestures
- Three finger gestures
- Pinch gestures
- Gyroscope input
- Accelerometer input

---

# 4. Input Behavior Requirements

## Mouse Movement

The primary input mode is:

Relative mouse movement.

Expected behavior:

```
Finger moves
        ↓
Mouse moves

Finger stops
        ↓
Mouse stops

Finger lifts
        ↓
Movement stops
```

The system must NOT behave like a joystick.

Do not implement:

- Cursor drifting
- Continuous movement after release
- Auto-centering
- Momentum movement

---

# 5. Motion Feeling Requirements

The target feeling:

- Smooth
- Stable
- Accurate
- Predictable
- Suitable for games

The same physical finger movement should always create the same response.

Avoid hidden behavior changes.

---

# 6. Forbidden Features Unless Explicitly Approved

Do not add:

## Motion related

- Mouse acceleration
- Dynamic sensitivity
- Speed-based gain
- Adaptive feeling
- Prediction
- AI trajectory prediction
- Inertia simulation
- Glide movement

## Product related

- Accounts
- Cloud synchronization
- Online services
- Multi-device management
- Plugin marketplace
- Automatic game detection

## Input related

- Extra gestures
- Extra buttons
- Unrequested control modes

---

# 7. Architecture Rules

## Security intentionally out of scope

rightpad is a personal-use system for one user, one Android phone, and one
Windows 11 PC on a trusted local network. Do not implement authentication,
pairing, PINs, encryption, TLS/DTLS, tokens, certificates, signatures, HMAC,
anti-replay systems, multi-user permissions, or security handshakes.
Do not add first-sender locking or source authentication/isolation.

Packet length/version/field validation, malformed packet rejection, and
timeout/stuck-input fail-safes remain required reliability measures.

## Android Responsibility

Android is only an input sensor.

Android is responsible for:

- Touch capture
- Historical touch samples
- Timestamp collection
- Raw data transmission

Android should NOT decide:

- Mouse movement behavior
- Sensitivity
- Smoothing
- Gesture meaning
- Windows input generation

---

## Windows Receiver Responsibility

Windows receiver owns:

- Input processing
- Motion algorithms
- Gesture recognition
- Configuration
- Mouse output

The receiver is the main control center.

---

# 8. Configuration Rules

User-adjustable parameters should normally exist on Windows receiver.

Reason:

- Easier tuning
- No Android rebuild
- Better testing workflow

Avoid placing important tuning parameters only inside Android.

---

# 9. Engineering Philosophy

## Simple First

Prefer:

- Small modules
- Clear responsibilities
- Easy debugging
- Minimal dependencies

Avoid:

- Complex frameworks without need
- Large abstractions
- Premature optimization
- Excessive design patterns

---

## Measure Before Optimizing

Before adding an optimization, answer:

1. What problem does it solve?
2. How can it be measured?
3. What is the expected improvement?
4. Is there a simpler solution?

If the benefit cannot be demonstrated, do not add the complexity.

---

# 10. Development Process Rules

Before implementing any feature:

1. Confirm that the feature is required.
2. Check existing documentation.
3. Consider the simplest implementation.
4. Explain architectural impact if significant.
5. Do not expand the project scope.

Do not implement features because:

- They may be useful someday.
- Other products have them.
- They look technically impressive.

---

# 11. Documentation Priority

Read documents in this order:

```
AGENTS.md

↓

docs/ARCHITECTURE.md

↓

Related design documents

↓

Reference documents
```

Priority:

```
AGENTS.md
    >
Design documents
    >
Reference documents
```

Reference documents are informational only.

They must not override current requirements.

---

# 12. Coding Rules

Prefer:

- Clear naming
- Small functions
- Predictable behavior
- Comments explaining design reasons

Avoid:

- Unnecessary comments
- Dead code
- Temporary hacks
- Fake placeholder implementations

Do not create:

- Empty classes without purpose
- Unused frameworks
- Future-proof abstractions without current use

---

# 13. Performance Rules

Performance matters.

However:

Do not optimize before measurement.

Avoid premature:

- Lock-free structures
- Custom memory allocators
- Complex threading systems
- Low-level optimization

Use simple implementations first.

Only optimize after profiling shows a real issue.

---

# 14. Testing Rules

Important behavior must be measurable.

Prefer:

- Automated tests
- Logging
- Diagnostics
- Repeatable experiments

Avoid judging input quality only by subjective feeling.

---

# 15. Scope Control

When receiving a task:

Implement only the requested scope.

Do not add:

- Extra features
- UI improvements
- Refactoring unrelated code
- Additional architecture layers

If a better approach requires changing scope:

Explain first.

Do not silently expand implementation.

---

# 16. Current Project Priority Order

Priority:

1. Correct input capture
2. Reliable Android-to-Windows communication
3. Stable motion processing
4. Correct gesture handling
5. Windows mouse output
6. Tuning and diagnostics

Do not prioritize:

- Visual design
- Extra features
- Convenience functions

before core input quality is proven.

---

# 17. Final Principle

rightpad should become:

A simple, precise, predictable, high-quality gaming input system.

The best solution is not the most complicated solution.

The best solution is the simplest design that achieves the required input quality.

---

# 18. Android Build / Install / Launch Rule

Whenever an Android build that produces the rightpad APK succeeds, Codex must
automatically deploy and reopen the app if the configured test Android device is
reachable through ADB.

Required sequence:

1. Complete the Android build successfully.
2. Check ADB device availability.
3. Install the newly built APK using overwrite/reinstall mode.
4. Restart and launch `com.rightpad.capture/.MainActivity`.
5. Verify that the Activity is running in the foreground.
6. Only then report the Android task as ready or completed.

For this project, the normal deployment commands are equivalent to:

```text
adb install -r <latest-debug-apk>
adb shell am start -S -n com.rightpad.capture/.MainActivity
```

A successful Gradle build alone is not sufficient completion for an Android
implementation task. Do not assume that an already-running app contains the
newly built code, and do not skip overwrite installation merely because rightpad
is already installed. Do not ask the user to run ADB commands that Codex can run.

If no configured test device is reachable through ADB, explicitly report that
installation and relaunch could not be performed. If Android requires a
system-level installation confirmation that Codex cannot operate, stop only at
that permission boundary, ask the user for that single confirmation, and then
continue the remaining deployment and verification automatically.
