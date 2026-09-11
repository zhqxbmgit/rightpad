# libvirtualhid mouse integration POC

This is a development-only Windows x64 output backend. Default GUI, login startup,
and RAW development launches still select SendInput. No UI setting or automatic
fallback is introduced. Android, Protocol v2, motion math and Single Tap semantics
are unchanged.

## Pinned dependency and build

- libvirtualhid: `6fdb8bd4de3b68d96c30e5303ac2ebb333c09746`
- Its lizardbyte-common dependency: `f9d91e1d29b7473f58e43acde4579da4e56c4abe`
- Validated installed driver: `2026.905.2300.20`; licensed local broker required.
- Public API/source: [pinned runtime header](https://github.com/LizardByte/libvirtualhid/blob/6fdb8bd4de3b68d96c30e5303ac2ebb333c09746/src/include/libvirtualhid/runtime.hpp),
  [pinned Windows backend](https://github.com/LizardByte/libvirtualhid/blob/6fdb8bd4de3b68d96c30e5303ac2ebb333c09746/src/platform/windows/windows_backend.cpp).

`dotnet build windows/Rightpad.Receiver.Tests -c Release` automatically builds the
native bridge and copies it and dependency license notices to the output. Native
source/build/downloads are under ignored `windows/native/Rightpad.VirtualHid/obj/`;
bridge output is under ignored `bin/`. `windows/tools/BuildVirtualHid.ps1` resolves
the pinned sources, checks tracked source cleanliness and builds with CMake. It
uses an available CMake command or the installed MSYS2 UCRT64 toolchain at
`C:\msys64\ucrt64\bin`. The tested compiler is GCC 16.2.0; MinGW compiler runtimes
are statically linked. The resulting DLL depends only on Windows system DLLs.
The script also accepts an MSVC CMake generator, but MSVC has not been validated
on this machine.

Run `powershell -File windows/tools/BuildVirtualHid.ps1 -Configuration Release -Test`
for the native fake test. Run the built `Rightpad.Receiver.Tests.exe` without
arguments for the managed suite; these tests do not inject OS mouse input.
In the restricted Codex build environment the existing restored assets were used
with `--no-restore`, because the user NuGet.Config is inaccessible there.

The build consumes the normal C++ library only. Driver/broker/tools/examples/docs
build targets are disabled. No driver MSI, license payload, Sunshine binary or
activation data is copied into rightpad. Dependency license notices are included;
the installed driver and its existing activation remain external prerequisites.

## Output boundary and native ABI

`IMouseOutput` exposes `Move(int,int)`, `LeftDown`, `LeftUp`, `Dispose`, identity
and a small statistics snapshot. `WindowsMouseOutput` retains its implementation
and native-call test seam. `ReceiverRuntime` accepts `Func<IMouseOutput>` for tests
and creates a fresh output for every Runtime Run before binding UDP.

`LibVirtualHidMouseOutput` serializes calls from the receiving loop and existing
button timer. `NativeVirtualHidMouse` owns an opaque SafeHandle through C ABI 1:
create, move, left-down, left-up, destroy and ABI-version check. Each operation
returns a status and bounded caller-owned error buffer; C++ exceptions never
cross P/Invoke. The bridge owns a public `lvh::Runtime` and `lvh::Mouse`.

Only the public C++ headers/library are consumed. No C# broker protocol, IOCTL,
descriptor, license authorization, VHF implementation or Sunshine private code is
introduced. The library's stock mouse profile identifies VID `1209`, PID `0003`,
REV `0001`. That upstream descriptor advertises other mouse controls, but the
rightpad ABI only exposes relative X/Y and LEFT; no wheel, absolute motion or
additional button functionality is exposed.

The bridge calls `Mouse::move_relative(int32_t,int32_t)` unchanged. The pinned
backend splits large deltas into signed 16-bit reports. Rightpad does not clamp,
truncate again, rescale or alter fractional residuals. A partially failed large
submission is an error, never retried; counters count successful public API calls,
not individual HID chunks or input consumed by a foreground application.

## Enforced identity and failure behavior

Select explicitly using:

```powershell
windows/tools/RightpadReceiverTask.ps1 -Mode Start -DevMouseBackend sendinput
windows/tools/RightpadReceiverTask.ps1 -Mode Start -DevMouseBackend virtualhid
```

The independent task wrapper passes `--dev-mouse-backend sendinput|virtualhid` to
the GUI. Its default is always `sendinput`; it does not change the user's login
startup command. A tiny ignored launch-options file transports the explicit
development choice to the scheduler wrapper. Use the launcher, never a persistent
Receiver child of the Codex shell.

The pinned library can return a successful SendInput mouse on some creation
failures, and its Runtime defaults to the fake backend. Therefore the bridge:

1. Reads local license status through the public API, without remote validation
   or activation and without printing account/provider details.
2. Explicitly creates `BackendKind::platform_default` and requires Virtual HID
   capability.
3. Creates the stock mouse with consumer stable identity `rightpad.mouse.poc`.
4. Requires a nonempty public device node before exposing the handle or accepting
   any input. At this commit the SendInput mouse exposes no nodes; the actual HID
   mouse exposes its driver logical path. A fallback object is destroyed without
   a movement or button report.

`deviceIdentity` in startup logs is the public library logical path (for example
`\\.\LibVirtualHid#9`), not a PnP instance path. PnP/Raw Input lifecycle enumeration
independently identifies the rightpad device; do not use VID/PID alone to
distinguish it from Sunshine's mouse.

Missing bridge/ABI mismatch, unavailable broker/license/driver, unsuccessful HID
creation, report failure and cleanup failure reach Receiver Error. Driver context
creation in this pinned API collapses some low-level failures; the diagnostic
honestly groups driver unavailable and protocol/version mismatch rather than
inventing a precise cause. There is no rightpad fallback or report retry.

Stop/Error first disposes `LeftButtonController`, then releases a possibly held
LEFT and destroys the native mouse/runtime. Ambiguous DOWN failure also triggers
best-effort UP. Destruction proceeds even after UP failure, and failures remain
visible. SafeHandle is the managed finalization backstop; the broker's existing
client PID supervision handles process termination.

## Diagnostics and measurement limits

The existing WPF Mouse Backend value reports the actual output. Flight Recorder
adds `mouseBackend`, `mouseOutputSuccesses`, `mouseOutputFailures`,
`lastSuccessfulMouseOutputAgeMs`, and `lastFailedMouseOutputAgeMs`. Historical
`sendInput*` fields retain SendInput-only meanings and are zero/null under Virtual
HID. Intended dx/dy totals now refer to the selected output boundary. Failure
events include backend identity. No Flight Recorder schema overhaul is performed.

RawAccel remains a potential A/B confounder: it is present and Running and is
attached to the Virtual HID mouse function stack. The saved
`C:\RawAccel\settings.json` default profile says `noaccel`; this is a disk
configuration observation, not proof of the driver's currently loaded formula.
Neither RawAccel nor Windows pointer settings were changed. Cursor pixels need
not equal raw HID counts; the smoke test verifies actual movement and source
identity without tuning or benchmarking.

## Verification evidence

The local run report and raw evidence are in ignored
`windows/test-results/rightpad-virtualhid-poc/`. The initial baseline contains the
Sunshine keyboard and mouse. Starting rightpad adds one HID mouse (two PnP nodes,
one Raw Input entry). Stopping only its Runtime must return the exact baseline,
and restarting must reconnect through the unchanged Android Sender heartbeat.
See that run's `REPORT.md` for measured PIDs, identities, tests, lifecycle results
and human acceptance results.

## Human acceptance and result

The following results were reported by the human tester; they are not automated
test results:

| Foreground / integrity | Move | Single Tap | Obvious feel anomaly |
|---|---|---|---|
| Chrome / Medium | PASS | PASS | None observed |
| Administrator Notepad / High | PASS | PASS | None observed |

**Virtual HID Mouse POC = PASSED.** A Medium-integrity Receiver using the
libvirtualhid backend produced input through a rightpad-owned mouse visible to
Raw Input, including in a High-integrity foreground. No SendInput fallback was
used, and no obvious subjective regression was observed.

This POC does not by itself establish broad gaming compatibility, latency or
jitter. Those remain subjects for the separate formal SendInput vs Virtual HID
A/B measurement. No benchmark was performed as part of this POC acceptance.
