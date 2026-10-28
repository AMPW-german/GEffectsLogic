# Issue #22 follow-up: structure cleanup and review findings

## Objective

Restructure `PhysiologicalModel.cs` (7,802 lines) so `Update` owns the
interval logic flow, remove the `AdvanceInterval`/`Update` duplication by
making tests drive `Update`, simplify stabilization to measured
stationarity, and split the file into themed `partial`-class files.

## Agreed decisions

- `Update` contains the interval logic flow. Complicated or shared
  sub-steps may live in private methods; `Update`'s body must read as the
  algorithm: capture → validate → circulation → upstream → piece loop →
  result → commit + logs.
- `AdvanceInterval` is deleted. Tests migrate to `Update` via two small
  internal seams (see below) so production and tests share one path.
- Legacy stabilization is restored: the wrapper's channel-snapshot
  comparison (11 public model properties, `0.025` drift tolerance) is the
  sole criterion — cheap, works at any constant G, no model internals.
  `CanStabilize`, `TrackStationary`, and `ApproximatelyCurrent` are all
  removed.
- Force margins follow legacy: `0.05` per axis for the `stable`
  early-return check, `0.025` per axis for the recorded-force drift check
  (the consumer's physics engine is not fully deterministic).
- `dt > 1.0 s` caller-step splitting is restored in
  `GEffectsLogicInstance.Update` (equal chunks of `deltaTime/ceil(dt)`, 1 s
  chunk size instead of the legacy 0.5 s, warning log restored). Each
  `PhysiologicalModel.Update` call still receives one supported-envelope
  interval.
- `DtLegacyBaselineTests.cs` is deleted; `Vectors` and `BaselineSettings`
  move into `ConstantLoadDtStabilityTests.cs`, the only other consumer.
- File split uses `partial class PhysiologicalModel` across themed files:
  all `PhysiologicalModel.X` type names, visibility, and the ~100 test
  references are preserved; no API change.
- `TemporaryTargetOnly` is removed as part of the cleanup; the cheaper
  `ConsciousnessTargetValueCore` path becomes unconditional (outputs are
  proven identical by the existing A/B test, which is then deleted).

## Out of scope (maintainer-owned)

- Performance max-frame gate: maintainer will adjust the budget on the CI
  runner for now and revisit the worst-frame event path later.
- Allocation budget stays as-is (maintainer accepts the batched harness).

## File layout

`GEffectsLogic/PhysiologicalModel.cs` becomes `partial` across six files.
No type moves between namespaces; nested types stay nested.

- `PhysiologicalModel.cs` (~1,500 lines): instance fields, public
  properties, ctor, `Reset`, `Update` + the private flow methods it calls
  (`AdvanceIntervalPiece`, `AcceptIntervalPiece`, `ApplyIntervalEvent`,
  `ProcessIntervalCandidate`, the candidate/offer scan cluster),
  `CommitIntervalState`, `LoadIntervalState`, `LastIntervalResult`,
  `Clamp`/`SmoothStep`, event logging switch.
- `PhysiologicalModel.Intervals.cs` (~900 lines): `IntegrationState`,
  `IntegrationEvent`, `IntegrationResult`, `IntegrationSegment`,
  `SegmentSolve`, `CirculationMode`, `BloodBounds`, `IntervalEventKind`,
  `IntervalTrialState`, `FullIntervalEventProbe(Needs)`,
  `CaptureIntervalState`.
- `PhysiologicalModel.Circulation.cs` (~2,900 lines): independent
  channels, `AdvanceCirculationInterval`, defect-driven splitting,
  `AdvanceCirculationSegment` (Newton/Radau), manifold projection, bound
  entry/release localization, trajectory reconstruction.
- `PhysiologicalModel.Upstream.cs` (~900 lines): `IntervalUpstream`,
  pooled caches, head-range, trial evaluation helpers.
- `PhysiologicalModel.Downstream.cs` (~2,000 lines): `AffineSystemSpec`,
  `AffineSolution`, `ScalarLagSolution`, `AffineSolveScratch`,
  `SolveAffineCollocation`, dense Taylor, `DownstreamHold`,
  `DownstreamSolution`, `LoCTrajectory`, oxygen/consciousness system
  builders, `ComputeHold`, `SolveDownstream`, `ChaseLoC`, `ScanCrossing`,
  `BisectSign`, `DownstreamViolation`, `TryFullStateAt`.
- `PhysiologicalModel.Laws.cs` (~400 lines): pure target/law functions —
  `ShapedPerfusion`, `DeliveryCore`, `EffectiveDelivery`, `BrainO2Target`,
  `O2Normalized`, `PerfusionNormalized`, `ConsciousnessRawReserve`,
  `ConsciousnessTargetValueCore`, `ConsciousnessTargetValue`,
  `ConsciousnessTargetAndTau`, `PhysiologicalVisualTarget`,
  `RedoutTarget`, `LungEffectiveness`, `GxLungImpairment`, etc.

`NumericalMath.cs` stays as-is.

## Steps

- [x] 1. Split `PhysiologicalModel.cs` into the six `partial` files above
  by moving regions verbatim; no code changes. Verify build + full Debug
  suite.
- [x] 2. Remove `TemporaryTargetOnly` and the A/B test
  (`PhysiologicalIntegrationTests` around line 1840); make
  `ConsciousnessTargetValue` call `ConsciousnessTargetValueCore`
  unconditionally.
- [x] 3. Restructure `Update` to own the flow:
  - Inline the orchestration body of `AdvanceInterval` into `Update`:
    initial-violation check, circulation solve, `IntervalUpstream`
    construction, neck-clamp/death-exit pre-events, the `while (cursor <
    dt)` loop with immediate-event checks.
  - Extract the per-piece body as `private bool AdvanceIntervalPiece(...)`
    (hold computation, mode projection, `SolveDownstream` + resolution
    retry, `ChaseLoC`, event offers, crossing scans, candidate
    processing). `RecordPiece`/`ApplyAndRecordEvent` become
    `AcceptIntervalPiece`/`ApplyIntervalEvent` methods.
  - `Update` ends with: assemble `IntegrationResult`, assign
    `LastIntervalResult`, throw `InvalidOperationException` on rejection
    (unchanged), else `CommitIntervalState` + event logging (unchanged).
  - Delete `AdvanceInterval`.
- [x] 4. Add the two test seams:
  - `internal void LoadIntervalState(in IntegrationState state)` — thin
    wrapper over `CommitIntervalState(in state, 0, 0, 1)` (derived fields
    are recomputed identically; tolerance fields use neutral G).
  - `internal IntegrationResult? LastIntervalResult { get; private set; }`
    — assigned before the rejection throw so failed results are
    inspectable.
- [x] 5. Migrate the 39 `AdvanceInterval` call sites in
  `PhysiologicalIntegrationTests.cs` to a shared test helper
  (`LoadIntervalState` → `Update` → `LastIntervalResult`, catching
  `InvalidOperationException` for expected rejections). Sites passing
  non-default `settings` construct the instance with those settings
  instead of per-call (`new GEffectsLogicInstance(settings: s).PhysModel`,
  the existing pattern). Same for `RunFullPath`.
- [x] 6. Restore legacy stabilization in `GEffectsLogicInstance`:
  - Re-add the snapshot fields: `stableRecorded`, `stabilizedBloodHead/
    Core/Lower`, `stabilizedBrainO2`, `stabilizedHeartRateMultiplier`,
    `stabilizedPerfusionLevel`, `stabilizedConsciousnessLevel`,
    `stabilizedVisualGrayscale/TunnelVision/Redout/LoCLevel` (11 channels).
  - Restore the legacy logic per model step: record the snapshot on the
    first candidate step; force deviation `> 0.025` per axis or any
    channel deviation `> 0.025` resets `stabilizationTime`,
    `stable`, `stableRecorded` (with the existing destabilize Info log);
    otherwise accumulate `stabilizationTime` and set `stable` once the
    threshold dwell completes (stabilize Info log).
  - Restore the `stable` early-return with the legacy `0.05` per-axis
    margin before the step loop.
  - Delete `TrackStationary`, `stationaryReference`,
    `stationaryGx/Gy/Gz`, `stationaryAnchored`, and
    `StabilizationStateError`.
  - Delete `PhysiologicalModel.CanStabilize` and
    `PhysiologicalModel.ApproximatelyCurrent` (only consumer was the
    removed check); `CaptureIntervalState` stays for the test seams.
  - Keep consciousness lost/regained logging in the model (event-driven,
    already moved); do not re-add the wrapper's `wasUnconscious` tracking.
  - Verify `InstanceSettingsTests` stabilization tests still pass; the
    `CreateStableInstance` true-equilibrium profile stays valid under the
    channel comparison.
- [x] 7. Restore `dt > 1.0 s` splitting in `GEffectsLogicInstance.Update`:
  `stepCount = (int)Math.Ceiling(deltaTime / 1.0)`, equal chunks, the
  legacy "High deltaTime detected" Warning log, per-chunk stabilize-check
  + `PhysModel.Update(chunk)` — i.e. the legacy `dtList` loop with 1.0 s
  replacing 0.5 s. Update the stale `AGENTS.md` sentence ("splits positive
  time steps greater than 0.5 seconds") to describe the 1.0 s split and
  model-owned intervals.
- [x] 8. Delete `GEffectLogicTests/DtLegacyBaselineTests.cs`; move
  `Vectors` (the 12-vector list) and `BaselineSettings`
  (`LogicSettings.Default with { StabilizationTimeThreshold =
  double.MaxValue }`) into `ConstantLoadDtStabilityTests.cs`; delete
  `DtLegacyEventBracket`, `DtLegacyVectorBaseline`, and
  `DtLegacyBaselineRecipe` (no other consumers remain).
- [x] 9. Sweep for leftovers: `TemporaryTargetOnly` references, unused
  locals/fields, `AdvanceInterval`/`CanStabilize`/`TrackStationary`
  mentions in comments and tests.
- [x] 10. Verify: `dotnet build GEffectsLogic.slnx -c Release
  -warnaserror`, Debug suite
  (`Category!=Experimental&Category!=Performance`), Experimental suite,
  `dotnet format` all three verifies, license headers,
  `git diff --check`. Report Release performance timings without changing
  budgets.

## Verification

```bash
dotnet build GEffectsLogic.slnx -c Release -warnaserror
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category!=Experimental&Category!=Performance"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category=Experimental"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Release --filter "Category=Performance"
dotnet format whitespace GEffectsLogic.slnx --verify-no-changes
dotnet format style GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet format analyzers GEffectsLogic.slnx --verify-no-changes --severity warn
```

## Risks and compatibility

- Test-migration semantics: `AdvanceInterval` returned without committing;
  migrated tests load state, run `Update`, and read `LastIntervalResult` —
  the model commits, which is fine because every test reloads state
  explicitly per case. `LoadIntervalState` must set every
  `IntegrationState` field (reuse `CommitIntervalState`).
- `Update` now runs the identical code path tests assert against; the
  throw-on-reject contract is unchanged, and `LastIntervalResult` keeps
  rejection diagnostics inspectable.
- Legacy stabilization is restored verbatim on public properties only;
  its known blind spots (e.g. channels not in the 11-field snapshot can
  drift unnoticed) are accepted legacy semantics, not new regressions.
- The 1.0 s split restores bounded-cost calls for out-of-envelope steps
  (e.g. 300 s → 300 × 1 s chunks) instead of the current single-shot solve
  that can throw for very large dt.
- `partial`-class split is a pure move; net481 + net10 builds unchanged.
