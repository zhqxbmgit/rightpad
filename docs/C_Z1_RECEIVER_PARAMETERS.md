# C-Z1 Receiver parameters — source implementation, 2026-10-10

Current status (2026-10-16 source audit): the Receiver-local mode selector and
C parameter panel were deployed on 2026-10-10. The 2026-10-15 remote-layout
delivery also deployed the cleaned source without FIR or Tau10. Current Tau
remains 20–100 ms. The source-only cleanup and original validation stages below
are historical records. No deployment or parameter change accompanies this Git
delivery. See [GIT_DELIVERY_20261016.md](GIT_DELIVERY_20261016.md).

Cleanup (2026-10-15): the unused M-R1 FIR experiment and Tau10 range expansion
have been removed from source at the user's request. Tau accepts **20–100 ms**;
the default remains 35 ms and the editor step remains 1 ms. M-R1 uses its direct
reconstructed-position path without an additional FIR. Receiver-local M/C
selection and the C parameter panel are retained. No deployment, restart or
saved-settings change accompanies this cleanup.

The Tau10 expansion failed its stationary-output safety gate: with damping .25,
Amax20000 and Vmax30000, a C12 held-contact reversal still emitted reversing
motion after 40 seconds. Its implementation and experiment-only tests are
retired, not reclassified as passing. Historical evidence remains in
[C_Z1_TAU10_VALIDATION.md](C_Z1_TAU10_VALIDATION.md) and
[M_R1_FIXED_FIR_EXPERIMENT.md](M_R1_FIXED_FIR_EXPERIMENT.md).

## Panel and units

Motion retains its existing M/C selector and Sensitivity X/Y editors. A separate
`C-Z1 模式参数` card stays available in either mode. It presents five primary
numeric editors and an advanced expander for the two stop thresholds. Amax's
80000/160000 shortcuts only edit the candidate. Enter normalizes, Escape restores
the last saved field, and Up/Down changes by the field's fixed step.

| Field / JSON key in `cz1` | Production default | Allowed inclusive range | Unit |
|---|---:|---:|---|
| Amax / maxAcceleration | 80000 | 20000–320000 | counts/s² |
| Vmax / maxVelocity | 15000 | 100–30000 | counts/s |
| Tau / tauMs | 35 | 20–100 | ms |
| Damping / dampingRatio | 1 | 0.25–2 | dimensionless |
| Glide / glideDeceleration | 120000 | 10000–240000 | counts/s² |
| Position stop / positionThreshold | 0.5 | 0.01–2 | counts |
| Velocity stop / velocityThreshold | 2 | 0.1–10 | counts/s |

Counts are Receiver logical displacement, not Windows desktop pixels. Tau is
stored/displayed in ms and divided by 1000 once when used as seconds. Decimal
input, including fractional Tau, uses the invariant decimal point. No NaN,
Infinity, negative numbers, incomplete text or out-of-range values can publish.

The shared immutable `Cz1Parameters` model supplies the ranges and validation to
the editor, JSON load, RuntimeSettings validation, memory Apply, Motion request
and dynamics Configure. At fixed DT=.004 and tau>=.020, r=DT/tau<=.2;
r²+4*damping*r<4 throughout the allowed range for the uncapped semi-implicit
spring. This linear check alone does not prove nonlinear capped behavior. The
historical offline corner test also exercises tau/damping/A/V extremes against a fixed
1000/-500 count step, verifies convergence and finite integer output, and tests
glide at maximum speed/minimum deceleration. The initial Amin=1000 corner with
tau20/damping.25/V30000 did not settle within 40 seconds, so Amin was tightened
to 20000. That failed exploratory result did not become an accepted range.

The retired lower-bound audit tested Tau10/12/15/20/35/100 and
all other parameter endpoints, with eight damping levels. The 16,896 sampled
trajectories are finite, capped and eventually stationary within 120 seconds.
However, the separate stationary-output deployment gate fails at Tau10. Those
numerical checks do not establish that the expanded range is safe to deploy or
that all points of the continuous nonlinear parameter space have been proved.

V<=30000 bounds ordinary integrated displacement to 120 counts per fixed step;
the test allows two extra counts for fractional output and snap differences.
Glide>=10000 bounds a full-speed glide to at most 750 valid steps (3 seconds of
integration), subject to the original skipped-deadline wall-clock behavior.
These are engineering limits, not human acceptance of every allowed setting.
Existing checked int32 classification, successful-submit ledger and failure
handling remain authoritative for exceptional accumulated displacement.

## Candidate, saved, requested and active state

- Editing changes only the C candidate, independently of all other UI drafts.
- Apply validates and submits a whole immutable memory snapshot. It does not
  write settings.json. It survives internal Runtime restarts in this application
  lifetime; a new application process loads the disk values.
- Save writes only the candidate C values into the current committed full
  settings, using the existing atomic-file SaveNowAsync mechanism. Successful
  disk Save then publishes and requests the same whole snapshot. Failure leaves
  committed and requested values unchanged and keeps the candidate retryable.
- Restore C defaults edits only the seven C candidate fields. Apply/Save is still
  required. Existing sensitivity (including the user's 10/10), mode, Controls,
  hidden historical Tau/Support and other drafts are unaffected.
- The card shows saved disk values, active C snapshot and separate unsaved,
  pending and applied labels. When M is active, an installed C snapshot is
  described as the snapshot for the next C touch. A stopped Receiver is labeled
  as stopped rather than claiming an active engine.

The page footer's existing Save continues handling the original settings draft.
It does not save an independent C draft. Saving sensitivity or local touchMode
also cannot undo a C memory-only Apply. Saving C cannot save a sensitivity draft.
UI Save transactions share the existing busy gate.

## Real application and safe boundary

```text
MotionView / Cz1ParameterEditor
 -> Cz1SettingsViewModel validated candidate
 -> RuntimeSettingsStore.RequestedCz1 + Cz1Published
 -> ReceiverRuntime run-scoped handler
 -> ResampledMotion.RequestCz1 under the existing motion gate
 -> cinematic.Configure only when session == null
 -> ZhqTrackpadDynamics.Step reads one immutable Parameters reference
```

RequestCz1, Enqueue, Tick, synchronous output/Commit, Clear, Reset and Dispose use
the same existing motion gate. No Step can observe some new and some old values.
Touching (including stationary touch), UP waiting for C12 reconstruction, glide,
and M's unfinished reconstruction output all defer the request. The latest
validated request replaces earlier pending requests. An interrupting C DOWN keeps
the busy snapshot, old natural carry policy and generation fence. It does not
provide an artificial parameter-apply boundary. Natural C stop submits the final
integer output, saves natural carry, parks through the existing Clear and installs
the new snapshot without resetting the preserved carry. Existing hard lifecycle
abort already clears input without output; its resulting idle boundary may also
install pending parameters. Applying parameters itself never calls Reset, switches
mode, recreates the mouse device or discards earned displacement.

There is no transported Touch CANCEL. Android-local CANCEL alone is not assumed
to stop a Receiver chain or apply parameters. Existing presence/run/lifecycle
invalidation still owns hard abort behavior.

| Parameter | Actual use in Step |
|---|---|
| Tau, damping | omega=1/TauSeconds; a=(R-P)*omega² - v*2*damping*omega |
| Amax | Scale the acceleration vector when its norm exceeds Amax |
| Vmax | After v+=a*DT, scale the velocity vector when its norm exceeds Vmax |
| Glide | After the mapped UP marker, decel=Glide*DT; reduce vector speed |
| Position threshold | Strict distance < threshold in the existing touching snap |
| Velocity threshold | Strict speed < threshold in touching snap; speed <= decel+threshold in glide stop |

Position still integrates using the new velocity. C12, fixed DT=.004,
skip/no-catch-up, Java-compatible rounding, successful-submit accounting, carry,
release/glide state machine and hard aborts are unchanged. Static historical
constants remain reference defaults for existing regression code; Step uses
Parameters. M-R1's math, M8 and Q0-C do not use C tuning values.

Active diagnostics no longer print hard-coded caps/Tau for customized C. The
snapshot Tau observation supports fractional ms. Optional MotionTrace stores the
actual complete C snapshot, and creates a segment when active C parameters change
at an idle boundary; historical profile-only calls still use baseline defaults.

## Persistence compatibility

The new `RuntimeSettings.Cz1` object serializes as `cz1` in the existing file.
Missing whole objects load the exact production defaults with no upgrade warning.
Invalid/incomplete objects fall back per field, preserve valid siblings and other
settings, and return a C-Z1 warning through the existing Settings notice. Load
never writes the file. Explicit Save recursively merges known values into the
existing JSON, preserving unknown root and nested settings. The existing known
retired `motionCadenceHz` migration still removes that field on Save. No persisted
field changes mode ownership or the original Saved Tau/Support semantics.

## Validation and evidence

Source cleanup verification (2026-10-15): isolated managed Release and Debug
builds passed with zero warnings/errors, reusing an existing native bridge
without rebuilding or deploying it. Release full suite: **719/719 passed**.
Debug C parameter suite: **37/37 passed**; Debug M-R1/default-Z1-related suite:
**159/159 passed**. The fifteen FIR and six Tau10 experiment groups were retired
with their implementations; the pre-removal Tau10 failure remains archived.
All 266 unrelated source files matched their pre-cleanup hashes. Nine protected
installation/settings files matched, and the original Receiver PID/start time
remained unchanged. Saved Tau30 remains valid and was not rewritten.
Evidence: ignored `windows/test-results/remove-fir-tau10-20261015/`.

Historical C parameter implementation verification:

Isolated Windows Release build: 0 warnings, 0 errors. Final full test run:
**734 passed / 734 total / 0 failed**, including **37 new C parameter cases**.
The existing default C binary64/oracle, integer outputs/timestamps, release,
missed wakes, carry and failure tests pass without changing their math oracle.
Existing M-R1, historical M12/M-F1 and Receiver-local mode regressions pass.
These counts describe the historical C parameter implementation before cleanup;
the FIR-specific cases have since been retired with the unused experiment.

Each of the seven parameters has both a single-variable dynamics check and a
real ResampledMotion.Tick injection check. Amax-only synthetic reversal starts
both candidates at 15000 counts/s and a large opposite target: A80000 first
reverses at step47/188ms with 1376.32 old-direction counts, A160000 at
step24/96ms with 673.44 counts. This is offline numerical evidence, not real
network latency or game acceptance. Preserve the historical Z2 human preference
for A80000's stronger gimbal character.

Other tests cover invariant de-DE round-trip, old/malformed JSON, unknown values,
invalid keyboard drafts, Save failure, unrelated draft isolation, exact old-chain
outputs after touching/pending/glide requests, interrupted DOWN, mode boundaries,
hard Reset/Dispose, Android-local CANCEL absence, concurrent native-submit gate,
M isolation, and a UI disk Save through a busy fake Runtime. All new output
callbacks are in-memory; Runtime tests inject fake native implementations on
ephemeral loopback endpoints. They never load/connect the production driver.

`Cz1PanelRenderTests` renders the real WPF MotionView at 610 DIP page width with
collapsed advanced controls and expanded advanced/error controls. It creates no
shown Window or running Receiver. PNGs were visually inspected for clipping.

Evidence is under ignored `windows/test-results/cz1-parameters-20261010/`:
build/restore logs, all-tests.log, the initial source diff/backups, protection
hashes/process snapshots and panel-preview PNGs. Full-suite execution uses the
isolated test assembly with no smoke/ADB arguments. The task-local MSBuild target
overrides BuildVirtualHidBridge and reuses the existing DLL read-only; managed
outputs go to `--artifacts-path`. Task-local APPDATA/DOTNET_CLI_HOME and a cleared
NuGet config avoid touching the user's tool configuration.

## Historical deployment and rollback plan

This plan was recorded before the subsequent 2026-10-15 delivery documented in
[REMOTE_LAYOUT_DELIVERY_20261015.md](REMOTE_LAYOUT_DELIVERY_20261015.md).
It is retained for context, not as an instruction to redeploy during Git delivery.

Only after separate deployment authorization: back up the current installation,
settings.json and current worktree; retain Receiver-owned mode behavior and
verify the candidate excludes the retired experiments; build with the isolated target; verify logs and
the ordinary M/C launch behavior; then perform the authorized controlled Receiver
shutdown and install/relaunch using the approved procedure. Native bridge,
Broker/Driver and Android need no change for this C-only feature. Validate default
C first, then one fixed parameter at a time in real games.

Once that version is deployed, subsequent C adjustments require neither rebuild
nor Receiver restart: edit, Apply or Save, and wait for the visible safe boundary.
80000 and 160000 shortcuts do not auto-apply or auto-save.

For feel rollback on the new version, Restore C defaults and Save, then wait for
idle. For binary rollback, separately authorize controlled shutdown, restore the
backed-up complete old installation and its exact settings.json, and relaunch.
Do not use Git reset/restore/clean/stash to roll back a workspace with unrelated
uncommitted changes. The old Receiver does not consume the new C object; restoring
the exact backed-up settings avoids any old-schema rewrite of unknown fields.

The original source-only stage preserved its then-running Receiver. Subsequent
deployment status is recorded at the top of this document.
