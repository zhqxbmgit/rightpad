# Source and Git delivery audit — 2026-10-16

This delivery collects completed source, tests, tooling and documentation after
HEAD `0a73b046d98198235d3bd8bbbc648c5f222d7e06`. Receiver-local mode authority and
the C-Z1 panel are already in that HEAD; this delivery adds their Android
reserved-control counterpart and current documentation, without duplicating
the existing Receiver implementation. It also includes the complete RPLY remote
layout feature and final dedicated editor. The only runtime-source edit during
this audit removes an extra blank line at the end of `RemoteLayoutView.cs` so
the staged whitespace check passes; no executable logic changes.

## Current implementation and historical evidence

Android owns the formal layout. Windows edits an isolated draft; explicit Try
waits for idle and begins a phone-owned 60-second trial. Keep requires atomic
phone persistence acknowledgement; timeout, Restore and lifecycle invalidation
recover the formal layout. The existing UDP workers/ports carry independent RPLY
messages with bounded retries, reconciliation and stale-message rejection.

The single dedicated editor defaults to maximized, fits the complete layout at
its actual aspect ratio and uses a black mouse-area preview. Close/Return retain
the draft and stop hidden-window polling. Reopen reconciles actual phone state.
The user has accepted the current layout editor appearance. This acceptance
does not establish additional human game-feel or blind-operation results.

Deployment records, in order, are
[the complete feature](REMOTE_LAYOUT_DELIVERY_20261015.md),
[the preview change](REMOTE_LAYOUT_PREVIEW_20261015.md) and
[the final editor window](REMOTE_LAYOUT_WINDOW_20261015.md).
Their PIDs, layout coordinates, hashes and no-Git statements describe those
dated tasks, not today's process or user data. Earlier source-only status and
deferred-deployment sections in mode/C documentation are historical stages.

FIR and Tau10 remain retired. Their historical reports and failed Tau10 safety
gate remain preserved; no failed candidate is restored or declared passing.
The current Receiver retains Tau20–100 and direct M-R1. No additional Motion
implementation change is part of this delivery.

## Evidence correspondence and verification

- Existing final window Release log reports zero warnings/errors and 730/730
  passing Receiver tests. The earlier feature record includes Debug/Release
  726/726, Android JVM regressions (307 remote-layout checks), device tests
  (22 remote-layout and 275 existing screen-control checks), Android builds/lint,
  native fake-backend checks and device-injected input-chain verification.
- Before that whitespace-only edit, read-only Portable PDB checks matched all
  **83 Receiver C# source documents** against the final window build. The current deployment-directory Receiver DLL
  matches that isolated build byte-for-byte. This is source/artifact evidence,
  not a claim that Git delivery restarted or reverified the live input chain.
- The final window test PDB matches **56/57 current test C# source documents**;
  `RemoteLayoutWindowTests.cs` differs from its recorded checksum. Therefore
  730/730 is reported as an existing run, not a newly executed full suite of
  every current test source file.
- To cover that discrepancy, a new isolated managed harness compiles the current
  `RemoteLayoutWindowTests.cs` against the verified Receiver assembly and runs
  its four cases: **4/4 pass**, build zero warnings/errors. It uses fake clients
  and off-screen WPF hosts, with no Receiver Runtime, native output, ADB, phone
  file access or real input injection. Outputs and logs remain ignored under
  `windows/test-results/git-submit-audit/`.
- The first harness build failed because the sandbox could not read the user's
  NuGet configuration. Retrying with task-local APPDATA and DOTNET_CLI_HOME plus
  an empty package-source configuration succeeded. No user configuration changed.
- Android and complete Receiver/native/device suites are not rerun for this Git
  operation. Existing Android evidence is retained; no new exact-source Android
  build or device verification is claimed. Other DPI values retain isolated
  rendering coverage rather than new physical-display testing.
- The staged check initially found that trailing blank line; it was removed
  without changing logic. Working-tree and staged whitespace checks, explicit-path
  staging and a full staged-content review complete the commit audit. Git delivery does not imply
  deployment or new human game acceptance.

## Preserved local materials and operation boundary

Build products, caches, screenshots, test outputs, recovery packages, logs and
user settings/layout files remain outside this commit under the existing
tracking rules. The unrelated root `pelican-bicycle.html` is left unchanged and
untracked. No ignore rules or historical tracked evidence are removed.

This task only reads running-process identity and deployed file hashes. It does
not stop/restart/redeploy Receiver, build/install/restart Android, edit settings
or the phone layout, or inject mouse/gamepad input. Isolated harness outputs do
not overwrite the deployment directory. The existing branch/upstream and Git
identity are retained for a normal, non-forced push.
