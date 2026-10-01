# Manual Touchpad Hitch Flight Recorder H1

H1 is always-on, bounded, in-memory instrumentation in the Windows Receiver.
It does not tune Motion, change admission, schedule output, modify protocols,
change B/X, or save settings/layout. There is no automatic hitch trigger.
The existing low-frequency `FlightRecorder` log and opt-in experiment `MotionTrace`
are separate systems and retain their existing behavior.

## Manual capture

Open **Diagnostics** and click **Freeze Hitch Trace**. The button reports either
the saved directory or an export error. Mouse input and recording continue, and
another manual freeze is allowed after the previous export completes.

For a repository build the output is:

```text
windows/test-results/hitch/<UTC timestamp>-<unique suffix>/
    trace.csv
    gaps.csv
    summary.txt
```

`windows/test-results/` is already ignored. A standalone executable outside a
repository uses `%LOCALAPPDATA%/rightpad/diagnostics/hitch/`. Path resolution and
directory creation happen only during manual export. Export failure is displayed
in Diagnostics; it never stops, restarts, resets or reconfigures the Receiver.
An interrupted/failed export may leave partial files in its own unique directory.

## Memory and snapshot contract

- Preallocated ring: **262,144 records**, **112 bytes** per record on the current
  .NET 8 Windows build, **29,360,128 bytes (28 MiB)** of array payload.
- Each write uses scalar fields and a short monitor section. No per-record managed
  allocation, formatting, console output, disk I/O, timer or additional worker.
- The exported logical window contains only records recorded in the last
  **60 seconds**. Array slots are reused; old slot bytes are not proactively erased
  while idle. The ring never grows. At 4,000 records/second a complete minute fits;
  above approximately 4,369 records/second sustained, capacity can shorten coverage.
  Summary reports actual oldest-record age and lifetime capacity overwrites.
- A manual export uses one temporary thread-pool task, with no persistent worker.
  It allocates a snapshot array before taking the snapshot boundary. This adds up
  to another 28 MiB plus bounded export working data while exporting; no snapshot
  allocation happens on the input/ticker thread.
- Under the recorder gate, capture fixes the ordinal range and monotonic cutoff.
  Copying then takes at most **64 records per gate acquisition**. If input is about
  to overwrite an uncopied captured slot, it preserves that one old record in the
  snapshot first. Thus even a descheduled exporter cannot produce torn records,
  include post-boundary input, or lose pre-boundary records to ring overwrite.
- Snapshot/serialization never acquires the Motion, backend or Runtime lifecycle
  gates. It does not reset the ring or pause recording. Disk work holds no recorder
  gate. Simultaneous manual exports are rejected until the current export finishes.

## Record fields and observation points

The CSV includes ordinal, kind, recording QPC, observation QPC, native end QPC,
Android timestampNs, Runtime run ID, senderRunId, session, sequence, event type,
status, sampleCount, sampleIndex, dx/dy, queue depth, deadline, generation and missed
ticks. Unused fields are zero; enum names are formatted only during export.

| Kind | Observation |
| --- | --- |
| Receive | Existing Stopwatch receive timestamp and Touch-path admission result: Accepted, Invalid, RetiredRun, UnknownRun, Duplicate or Old. Heartbeat included; B/X/config excluded. Malformed header fields are raw/untrusted when present. |
| Sample | Every decoded `TouchSample.TimestampNs`, packet count/index and packet identity/status, including decoded rejected packets. No Android changes. |
| Tick | Actual observation time at the existing tick, scheduled deadline, queue depth, generation and missed ticks. No timer/cadence changes. |
| Output | Existing quantized dx/dy, including zero output opportunities; observed time, sender/session and queue/deadline state. No extra submission. |
| NativeSubmit | Timestamp immediately before the managed native adapter call, completion time, requested dx/dy and API success/failure. Production `IVirtualHidMouse.Move` includes adapter overhead; the diagnostic SendInput override is also instrumented. No native C++/ABI changes. |

Output records are observations of the existing quantizer path; on Q0-C, its
successful native call precedes the Output record for both M and C. API success is not evidence of
game consumption, individual HID report timing or a hardware polling rate. Button
submissions are outside this movement recorder. RAW development mode has no playout
clock; its receive/sample/native records remain available.

## Summary and clock domains

`summary.txt` reports nearest-rank p50/p95/p99/max in milliseconds for Android sample,
Windows UDP receive, playout tick, Motion output opportunity and native submit
intervals. Empty series safely report N/A.

Android intervals use only accepted samples within the same Runtime/sender/session,
in original receive order. Equal timestamps give zero intervals; backwards intervals
are omitted, with raw values retained in the trace. Windows intervals use Stopwatch
frequency, ordered by observation time, and never cross Runtime runs. Idle/contact
gaps are included and labelled: native calls naturally stop at zero displacement.
These distributions are observations, not automatic root-cause classifications.

**Android and Windows clocks are unsynchronized. No Android-to-Windows one-way
latency is calculated.** Retention uses Windows recording time exclusively.

All intervals strictly greater than 5 ms are listed in `gaps.csv`, with source and
destination ordinals and separate `>5`, `>10`, `>12`, `>20`, `>50` ms flags. Summary
also reports counts for every threshold. A gap alone does not establish a hitch.

## Validation

`HitchTraceTests` covers bounded overwrite/retention, empty buffers, exact snapshot
boundaries under repeated concurrent overwrite, zero per-record allocation,
60-second capacity at 4,000 records/second, receive rejection identities, native
success/failure, clock separation, exact percentiles/gap thresholds, export failure
isolation with a running UDP Receiver, and deterministic output parity during
repeated freezes at production 1000 Hz/Q0-C/Earned-Settle.

Run the complete `Rightpad.Receiver.Tests` executable in Debug and Release. The
ordinary project build also builds/checks the existing native dependency; this
feature does not require Android builds or changes to native production sources.

AUTO HITCH DETECTION: NOT IMPLEMENTED

MOTION TUNING: UNCHANGED
