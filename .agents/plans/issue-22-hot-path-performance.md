# Issue 22 Hot-Path Performance

## Goal
Reduce warmed Release complete-update time below 0.1 ms/frame for both existing profiles without changing the benchmark, numerical equations, event semantics, or supported-dT accuracy requirements.

## Average-Cost Follow-Up (Approved; Autonomous Goal Active)

User explicitly approved continuation and subsequently set the autonomous goal:
optimize the average below 0.1 ms, do not wait after status updates, do not commit.
That goal is NOT achieved yet. No commits have been made.

### Current checkpoint after additional trials

Retained new source changes beyond the earlier pressure/visual caches, Brent
contact scans and oxygen-only Taylor skip:

- [x] Extend certified dense oxygen Taylor extent to 1 with at most 64
  coefficients, retaining the existing 1e-15 tail bound and exact fallback.
  Paired event captures are byte-identical; only oxygen final-state roundoff
  differs (maximum 4.44e-16). Added coupled/diagonal/nilpotent reference tests.
- [x] Certify an entirely underfilled interval with `TryHeadRange` and
  `PressureSource(0,gz)==0`, then use exact pressure exponential decay.
  Interior overfill and negative-G source regressions cover rejection.
- [x] Extract unchanged independent strain/respiratory formulas and avoid
  computing unused fatigue/cardio state in `IntervalUpstream.Evaluate`.
  Reflection regression compares required fields exactly to full-state output.
- [x] Skip exact-zero Gaussian elimination multipliers.
- [x] Evaluate only oxygen coordinate zero when requested. Current
  `AffineSolution.EvaluateFirst` returns the same Horner value without caching
  first-only arrays; full-state cache remains intact. Instantaneous and exact
  fallback paths are tested.
- [x] Flatten affine stage weights into a contiguous array, preserving every
  accumulation and read order.
- [x] Pre-size caches to avoid repeated resizing: upstream trial/pressure 64,
  affine full-state 16, scalar lag 16, physiological visual target 64.
- [ ] Meet the official average: latest THREE unchanged normal-environment
  official Release receipts under
  `%TEMP%/GEffectsDiag/average-uncached-production-20261023/` still fail:
  default avg/max 0.2926/3.9259, 0.2977/3.6756, 0.2947/3.1431 ms;
  custom 0.1145/0.8433, 0.1067/0.6351, 0.1155/0.6537 ms.

All temporary gates, instrumentation, diagnostic test files, sleep probes and
AggressiveOptimization attributes are REMOVED from the current worktree.
Only the original four files remain dirty. No settings, calibration, snapshots,
benchmark, CI budgets, or numeric tolerances were changed. The dense extent
regression's extent assertion now correctly expects 1; accuracy assertions stay.

Rejected trials: target/tau tuple memoization; bypassing full upstream evaluation
for consciousness targets (`lean-target-paired-20261023`); shared cubic matrix
factorization (`batch-cubic-paired-20261023`); 10-, 34-, and 62-site JIT
AggressiveOptimization hints (all removed, no reliable sufficient gain).

Raw paired evidence directories under `%TEMP%/GEffectsDiag/`:
`dense-extent-paired-20261023`, `zero-pressure-paired-20261023`,
`lean-upstream-paired-20261023`, `zero-elimination-paired-20261023`,
`first-coordinate-paired-20261023`, `flat-weights-paired-20261023`,
`sized-caches-paired-20261023`, `uncached-first-paired-20261023`.
Each has baseline/refined captures and 3 alternating timing pairs/profile;
all non-dense-extent retained candidates had byte-identical captures.
Some first/default timings remain substantially slower than later pairs.
Latest uncached-first paired means default baseline/candidate
0.2588061/0.2261776, 0.1418398/0.1530534, 0.1093539/0.0789749 ms;
custom 0.0825047/0.078068, 0.0816046/0.0734931,
0.0778413/0.0728464 ms. These are diagnostic 128 MiB/both-path-warmed
measurements, NOT official passes. One stale-binary run overwrote earlier
zero-pressure timing CSVs; this was disclosed to the user. Later evidence and
repository source were not lost.

CPU investigation: process-only `DOTNET_TieredCompilation=0` diagnostic with
then-current code yielded default/custom avg 0.1267/0.1118 ms vs normal
0.3249/0.1173 (`current-tier-comparison-20261023`). Do not count the override
as acceptance. Thread CPU (15.625 ms quantum) was close to wall totals in one
run (343.75 vs 352.1597 ms default; 375 vs 370.2944 ms custom), so scheduling
alone does NOT explain the gap. A contemporaneous 3-second process sample
showed active Titanfall2 ~2.6 cores, Devin ~1.1, Firefox ~1.0, total CPU ~33%;
user was informed, no programs stopped or reprioritized.

Sampling tool dotnet-trace 9.0.661903 installed ONLY to
`%TEMP%/GEffectsTrace/`. Valid resolved trace and speedscope JSON:
`cpu-sampling-rundown-20261023/` (earlier `cpu-sampling-20261023` trace was
unresolved and unusable). Sampling includes warmup and timed batches, not an
official timing receipt. Lead analysis found active-model samples ~188 ms,
including PollGC ~86 ms, Memmove ~29 ms, SolveAffineCollocation ~27 ms and
CubicCoefficients ~22 ms; these are sampled inclusive/exclusive observations,
not exact per-update costs. No server or diagnostic process remains running.

Latest verification in `average-uncached-production-20261023/`:
Release SOLUTION build `-warnaserror` zero warnings/errors; narrow 8/8;
full normal Debug gate 422 passed / 39 failed / 2 skipped (`normal.trx`).
Lead still needs exact failure-set comparison to
`official-final-receipts/normal-gate-final.trx`, although reported counts match.
Earlier full integration class: 319 passed / 3 skipped / 3 known deferred
negative-G accuracy failures (`average-flat-production-20261023/integration.trx`).
Whitespace/style/analyzer verifies and diff check pass. License script result
is still UNVERIFIED because output tail was truncated; rerun/check exit code.
Review also noticed several extra blank lines before PrepareDenseMomentTaylor
left from temporary-field removal; clean up only this own formatting churn.

NEXT: continue autonomously from this productionized checkpoint; do not rerun
the broad Debug gate on every experiment (one complete final pass already done).
No candidate is currently in flight. Investigate a different, justified cost
reduction or discriminate the persistent cold/default-vs-warm/custom timing
gap, rather than repeating rejected trials or claiming a diagnostic pass is
official. Author measurement recipes/analysis as lead; delegate mechanical
implementation and ordinary verification. Review full diffs and raw evidence.

The next pass targets complete-workload average cost, not only worst frames. The
quiet official reruns still fail: default averages 0.4009/0.4071/0.3353 ms and
custom averages 0.1819/0.2582/0.1613 ms. Raw receipts are in
`%TEMP%/GEffectsDiag/official-quiet-rerun-20261020-215750/`.
Existing upstream-state and pressure caches must not be duplicated.

- [ ] Capture current Release workload state/event outputs before changes and collect calculation call counts in a separate untimed replay; preserve the official workload, one-pass warmup, no-GC batching, and assertions. Verify: baseline capture and uninstrumented official receipts saved in a new evidence directory.
- [ ] Measure aggregate cost in scalar lag evaluation, consciousness target/tau calculation, oxygen dense evaluation, convolution coordinate/weight preparation, and event scans. Instrumentation is diagnostic only; collect timing separately from capture and counting. Verify: inclusive times explicitly labeled and total complete-update time reported for each profile; inspect raw evidence before selecting a trial.
- [ ] First candidate, only if counts establish material duplication: share exact consciousness target/loss-tau results between `TargetAt` and `LossRateAt` inside `TryCreateHomogeneousZeroTargetLoss`. Use a per-piece dictionary keyed by the unchanged normalized coordinate; retain separate validation, endpoint checks, arithmetic, and fallback behavior. Verify: unchanged target-zero acceptance/rejection, nonfinite handling, endpoint-branch allowance, and full workload state/event parity.
- [ ] If convolution preparation dominates instead, plan the exact sharing seam before implementation; do not substitute a new quadrature, fewer nodes, different root policy, approximate trajectory, or hidden substeps. A candidate not covered above needs a concrete design and approval before implementation.
- [ ] Retain a candidate only after alternating baseline/candidate measurements demonstrate a reproducible complete-workload average improvement for both profiles, with no accuracy or event regression. Keep byte-identical captures where arithmetic is unchanged; otherwise stop for review rather than relaxing parity.
- [ ] Remove temporary hooks and run targeted homogeneous-consciousness/scalar-lag and interval-event regressions, Release solution build, diff hygiene, and three sequential unchanged official benchmark runs. Success requires each profile below 0.1 ms average in all three runs; report the unchanged 0.5 ms maximum gate separately even if averages pass.

Scope remains the files listed at the end of this plan. No calibration, settings,
snapshot, benchmark, CI, or tolerance changes. Allocation is low priority, but
existing official assertions remain intact. No concurrent timing jobs or server
is needed. A finite unsuccessful trial is reported as such, not as completion.

## Tasks
### Average-cost trial evidence

- [x] Capture the current workload and count homogeneous target/tau duplication.
  Counts include both committed Update and untimed AdvanceInterval replay:
  default 204507 calls / 79818 unique piece-coordinate pairs; custom
  206841 / 80796. These are diagnostic counts, not production timing.
- [x] Trial exact per-piece target/tau memoization and compare raw captures.
  All four state/event capture pairs are byte-identical, and baseline captures
  are byte-identical to the fresh pretrial captures.
- [x] Reject the trial after two sets of alternating paired diagnostics show
  no reproducible complete-workload average benefit. In the second set, both
  paths were explicitly warmed before measurement. Default baseline/candidate
  averages were 0.314955/0.2956624, 0.2812251/0.3305094,
  0.2335723/0.2425643 ms; custom were 0.2223933/0.2530673,
  0.2505536/0.200441, 0.2047588/0.2508613 ms. Candidate was slower in
  two of three runs for each profile. These 128 MiB diagnostic batches are not
  official acceptance results.
- [x] Archive and remove the rejected candidate, temporary regression, and
  harness; preserve all previously retained optimizations. Three targeted
  regressions pass and the Release library build passes both targets with
  zero warnings/errors; diff hygiene is clean.
- [ ] Reach the required official average. Fresh pretrial official receipt:
  default average 0.4879 / maximum 3.4624 ms; custom average 0.3793 /
  maximum 5.8341 ms. No new optimization is retained, so another official
  run of the unchanged code was not used to imply progress. Broader
  convolution-cost investigation remains open; no changed quadrature,
  approximation, or new optimization design has been approved.

Raw evidence: `%TEMP%/GEffectsDiag/average-followup-baseline-20261020/`,
`average-target-baseline-20261023/`, `average-target-paired-20261023/`,
and `average-target-paired-bothwarm-20261023/`. The latter retains the
rejected source and diagnostic harness. Recorded averages above are computed
directly from the saved per-frame ticks/frequency; no source workload is rerun
to reproduce these records.

- [x] Locate the current shared-trajectory and event-scan seams, inspect the unchanged performance gate, and preserve the existing WIP — verify: `PhysiologicalModel.cs` event scans and `LogicInstancePerformanceTests.cs` budgets identified; worktree recorded.
- [x] Share downstream trajectory samples across event surfaces and retain existing root refinement — verify: 34 focused tests pass, including endpoint and simultaneous-event regressions.
- [x] Share matrix-power work across all three exact affine moments without changing numerical bounds or scaling — verify: 32 focused tests including analytic diagonal, nilpotent, and symmetric-coupled moment checks pass; Release solution builds both library targets.
- [x] Reuse per-piece affine residual combinations without approximating trajectories — verify: 33 focused affine, scalar-lag, and full-interval tests pass; dense stage-expansion and instantaneous-coordinate regressions pass.
- [x] Measure the unchanged workload using a separate, twice-warmed GC-allowed diagnostic and remove its temporary source — verify: three runs report median 0.2679/0.2186 ms per frame and 185.7/185.0 kB per frame for default/custom; unchanged official no-GC tests fail for both profiles.
- [x] Stop the accuracy-preserving optimization pass at the unchanged timing gate with evidence — verify: 33 focused numerical tests pass; all three twice-warmed diagnostic runs exceed 0.1 ms/frame for both profiles, and no comparable pre-change run with this diagnostic establishes a net speedup.
- [x] Measure the unchanged warmed Release workload for default and custom profiles and iterate on the event hotspot — verify: three GC-allowed diagnostic runs report median 0.3468/0.2287 ms per frame and about 185/184 kB per frame; both unchanged official benchmark cases lose their no-GC region.
- [x] Check the wider numerical/behavioral gates and diff hygiene without retuning or weakening tests — verify: Release build, whitespace/style/analyzers, and `git diff --check` pass; final normal Debug gate reports 400 passed, the same 39 previously failing, 2 skipped.
- [x] Classify downstream and event work before further simplification — verify: internal mode/bound/target-branch event splits and dense 16-node scalar lag evaluations identified; external consumers only observe accepted state and logs.
- [x] Remove repeated exponential calculation inside each scalar convolution without changing nodes or event handling — verify: 29 focused tests pass, including constant-source/composition cases for decay rates 0, 0.7, 2 and 10 at the existing 2e-15 tolerance; library Release builds, but the user-commented benchmark causes a solution CA1823 failure.
- [x] Measure the changed convolution path without modifying the user's benchmark file or CI gates — verify: three twice-warmed GC-allowed runs report 0.2523/0.2188 ms per default/custom frame (medians); custom speed is unchanged within noise and both remain above the 0.1 ms target.
- [x] Avoid full upstream circulation/pressure evaluation when variable visual targets only need head blood — verify: the new overfill-to-hypoperfusion regression passes; 175 pressure/dense-head tests pass and three broader visual failures match the existing WIP.
- [x] Consolidate the overfill-to-hypoperfusion visual regression through the existing path helper — verify: coarse/fine paths use identical custom initial state; new and existing partition tests pass without duplicated simulation loops.
- [x] Benchmark the changed visual-target path using the same separate diagnostic and remove its temporary source — verify: three twice-warmed Release runs report median 0.1740/0.1675 ms per default/custom frame and 113.7/112.9 kB per frame; the user's official benchmark file remains untouched.
- [x] Reject unmeasured local changes that do not establish a speedup — verify: closure-free event scanning and target-only consciousness calculations were tested against the same workload and reverted after inconsistent/slower results; the interrupted pressure-source shortcut was reverted before profiling.
- [x] Profile blood circulation, pressure, O2, consciousness, each visual channel, event sampling/scans, and accepted-state work on the unchanged warmed Release workload — verify: three raw Release diagnostic logs per profile show matrix moments 0.0871/0.0877 ms per frame inside oxygen dense 0.1164/0.1149, each variable visual solve about 0.023 and event fixed probes about 0.049/0.048; all overlapping rows are labeled inclusive. Earlier GC-allowed uninstrumented runs are the control.
- [x] Optimize the largest measured execution-time cost while keeping the same equations and internal event semantics — verify: the 32-power per-piece cache passes 32 focused and three supported-dT tests; three uninstrumented Release runs give 0.1750/0.1358 ms median versus comparable 0.1967/0.1550 ms without the cache.
- [x] Determine whether the cache redesign has a demonstrable numerical and runtime advantage before adoption — verify: cached/uncached matrix moments agree within 2e-13 over coupled matrices up to norm 7.83, original fallback is retained above norm 8, and both warmed Release profiles improve in median time.
- [x] Remove temporary diagnostics and check broad WIP regression gates without changing known failures — verify: Release solution build has 0 warnings/errors; format, license and diff checks pass; normal Debug gate has 403 passed, the same 39 WIP failures, 2 skipped; two opt-in circulation-only dense tests pass with `GEFFECTS_DENSE_DT_TESTS=1`.
- [x] Re-profile the expanded-cache workload without changing the official gate — verify: three warmed GC-allowed Release runs show default/custom median oxygen dense evaluation 0.0809/0.0795 ms per frame, including matrix moments 0.0585/0.0579; fixed event probes 0.0375/0.0367 and tunnel/grayscale solves about 0.018 each. Rows overlap; temporary profiling source was removed after analysis.
- [x] Remove redundant visual O2 queries and intermediate moment matrices, then measure against unchanged equations — verify: 32 focused numerical/full-interval tests and Release build pass; the separate paired 128 MiB no-GC diagnostic shows custom median 0.1391 to 0.1309 ms/frame, while default results remain too noisy to establish a gain.
- [x] Replace eligible dense O2 moment-matrix construction with a certified per-piece vector Taylor evaluation — verify: 32 focused tests, three supported-dT tests and 1,000-frame parity checks pass; all measured state deltas remain about 1.5e-15 or less and the original moment path remains for uncertified queries.
- [x] Shorten the certified vector series to the first degree meeting its existing 1e-15 absolute tail bound, capped at 32 — verify: default/custom 1,000-frame workload parity has maximum state delta about 1.1e-15; 32 focused coupled/full-interval tests and three supported-dT checks pass.
- [x] Remove the temporary diagnostic source after accepting the certified vector evaluation — verify: matched primed 128 MiB no-GC control medians are 0.1307/0.1258 ms per default/custom frame versus final code's 0.0887/0.0756; 32 focused and three supported-dT tests pass, frame-level state parity stays within 1.5e-15, all temporary switches/source are removed, and Release/style/license/diff checks pass.
- [x] Distinguish reproducible expensive frames from runtime or system latency — verify: three no-GC runs repeat frames 546 and 561 above 0.5 ms for both profiles with zero collections; traces show 24–26 downstream prefix re-solves at frames 495/116–117, nine downstream solves at default frame 546 and 42–43 circulation mode-root trials at frame 561; extra 8–9 ms default spikes at frame 561 vary despite identical event counts.
- [x] Reject a safeguarded-secant replacement for downstream root bisection — verify: first traced run raised root prefix trials at frame 495 from 24 to 26 and changed the emitted simultaneous O2 event at frame 117; the exact change-only patch was reversed and three focused tests passed.
- [x] Probe the existing unilateral headward mode correction once at the certified 1e-8 s invalid/valid gap, retaining the original 1e-12 s retry/failure path — verify: frame 561 circulation mode trials decrease from 42–43 to 27, mode-event offsets differ by under 5e-15 s, 26 focused and three supported-dT tests pass; broader Debug gate still pending.
- [x] Project a certified LoC-contact endpoint and assess remaining repeated contacts — verify: frame 546's accepted first LoC contact has exact ceiling value 0 within the slope times 1e-8 s correction allowance, custom path falls from four to three segments, 25 focused and three supported-dT tests pass; default still repeats hypoperfusion crossings before refinement.
- [x] Localize the head-only hypoperfusion threshold more precisely without changing other event surfaces — verify: bisection stops at 1e-12 s only for that margin, both frame 546 profiles now have one hypoperfusion event and three segments/five downstream solves, focused full-interval and supported-dT checks pass.
- [x] Localize the head/resting-perfusion crossing precisely at the circulation mode boundary — verify: 1e-12 s bisection applies only to that head level; frame 561's downstream segments drop from four/three to two, final state differs from the prior path by at most 2.3e-16 across compared channels, and 25 focused/three supported-dT checks pass.
- [x] Try the existing strictly validated unilateral correction at earlier invalid/valid gaps without changing its root allowance — verify: 1e-4/1e-6/1e-8 s attempts share existing prefix validation and 1e-12 terminal fallback; three traces show frame 561 mode trials drop from 27 to 15–16, with unchanged logged final state and sub-femtosecond mode-event offset changes; 26 focused and three supported-dT tests pass.
- [x] Cover the accepted frame-561 mode/perfusion boundary with a focused fixture and remove all tail instrumentation — verify: captured default pre-frame state yields one mode/resting-head crossing and two full segments with stable partitioned endpoint; no temporary counters/source remain; Release/style/license checks pass and normal Debug gate reports 405 passed, unchanged 39 WIP failures, 2 skipped.
- [x] Solve only oxygen in intermediate O2-bound/target-zero root brackets, retaining a full accepted-prefix solve — verify: original sign bracket and 1e-8 s tolerance remain; 24 focused event and three supported-dT tests pass; three primed 128 MiB no-GC diagnostics reduce median average from 0.0865/0.0805 to 0.0673/0.0643 ms per default/custom frame and frames 116–117/495 to about 0.27–0.33 ms in clean runs.
- [ ] Satisfy the unchanged official performance gate in a future pass without relaxing accuracy — verify: all warmed Release runs average below 0.1 ms/update, every update respects the 0.5 ms maximum, and allocation fits the restored 64 MiB no-GC region; currently one primed default run averaged 0.1064 ms, observed maxima exceed 0.5 ms and allocation exceeds the region.

## Explanation
### Why
The existing WIP spends most of its measured time in downstream oxygen/consciousness/visual evaluation and approximately 36 repeated event-surface scans per frame; the official Release no-GC performance gate still fails.

### Changes
- Reuse each piece's trajectory state at the same fixed scan probes across event surfaces; retain direction, zero-plateau, endpoint, and re-solved-prefix root validation.
- Reuse matrix powers across the three moment orders and per-piece dense-evaluation coefficients where numerically equivalent; measure the effect rather than infer speed from arithmetic counts.
- Avoid oxygen dense queries when head blood already fixes the tunnel/grayscale target at zero, and scale-and-add matrix terms in one helper without duplicating the moment series.
- For certified small-norm dense queries, use the existing matrix-power cache to precompute the affine solution's vector Taylor coefficients; keep the current moment-matrix method for unsupported or uncertified queries.
- Cache 32 matrix powers per eligible 3x3 oxygen piece for finite scaled norms up to 8; reuse the existing moment series and halving logic, then fall back to the original recurrence above the limit or outside supported dense extent.
- Keep independent visual channels, physiological equations, unchanged Release benchmark thresholds, and caller-owned model state.
- Keep the official blocking CI performance/allocations gate intact despite its current failure; use separate non-blocking diagnostics while tuning execution time.
- Keep physiological event localization internal: callers do not need offsets, but moving mode, constraint, or target transitions to caller endpoints changes within-interval physiology and dT stability.
- The user restored the official benchmark's allocation assertions and no-GC start after earlier diagnostic runs. Preserve the restored gate; GC-allowed timing and allocation diagnostics are not passing official results.
- Optimize only demonstrated downstream hotspots; report measured results and remaining blockers rather than claim an unverified speedup.

## Scope
### In scope
- Temporary, removable per-task timing instrumentation; `PhysiologicalModel` and mathematical helpers used by downstream/event evaluation; focused regression tests.

### Out of scope
- Physiological parameter tuning, snapshots, CI threshold changes, numerical substeps, commit/push, and unrelated issue-22 migration work. Profiling scopes are not behavioral changes and must be removed after measurements.

## Verification
The final report must distinguish the official no-GC gate from GC-allowed profiling runs and separate inclusive helper cost from mutually exclusive top-level phases.

## Renewed Performance Pass (2026-10-20)

Status: approved. The lead approved the profiling pass and selected the exact-coordinate cache design below.
Performance takes priority over calibration. No calibration is authorized by this pass.

- [x] Inspect the current official workload and prior optimization evidence. The current benchmark reserves 64 MiB per 250-frame batch, with a 256 MiB total allocation limit, not one 64 MiB region for the entire workload. Historical timings are hypotheses for current hotspots, not current measurements.
- [x] Capture current uninstrumented Release results for both profiles using the unchanged official benchmark. Also capture three sequential diagnostic runs of the identical 1,000-frame workload after its existing warmup; report every frame over 0.5 ms, profile, zero-based index, dt, Gz, elapsed time, allocated bytes, GC counts, and batch. Keep logging and record serialization outside the timed update. If a no-GC region fails, preserve that failure and label any GC-allowed continuation diagnostic-only.
- [x] Freeze those raw per-frame records before analysis. Summarize per-run mean, maximum, allocation and all over-budget frames; distinguish repeatable expensive frame indices from sporadic outliers without assuming the latter are scheduler noise. Historical indices 116, 117, 495, 546 and 561 are candidates, not a filter on discovery.
- [x] Profile an identical replay separately from acceptance timing. Measure exclusive top-level circulation, downstream, and accepted-state work; report inclusive nested pressure, oxygen setup/dense evaluation, scalar visual/consciousness convolution, event scanning, and root-prefix solves with call counts. Correlate these with each expensive frame and allocation; do not add overlapping inclusive rows into a total.
- [x] Select the smallest optimization supported by those measurements. Settle the exact change after reviewing current evidence, before implementation. Prefer removing duplicate evaluations or short-lived buffers; preserve equations, event ordering, root tolerances, certified error bounds, trial purity and per-instance isolation. Do not adopt a new solver, approximate event policy, shared mutable scratch storage, or hidden numerical substeps.
- [x] Implement the selected change and targeted numerical/event regressions. Compare full-state endpoints and event sequences on the frozen workload inputs against the pre-change execution; record actual deltas and enforce existing tolerances. Preserve the three authorized Experimental exceptions without adding exceptions.
- [ ] Remove temporary profiling code, then run three sequential unchanged official Release timing receipts for both profiles. Require average <=0.1 ms and maximum <=0.5 ms in every receipt, intact no-GC regions and unchanged allocation assertions. Report all receipts, not a selected best run. Temporary code and the diagnostic harness are removed and the six optimized-cache receipts exist, but the timing requirement still fails: default means 0.3833443/0.1154931/0.1175109 ms (maxima 2.8494/0.8588/0.8321) and custom means 0.188649/0.1185928/0.1073754 ms (maxima 1.0521/0.9425/0.6367).
- [x] Run one final broad regression gate, both-framework/GUI Release build, formatting/analyzers and license checks. Compare failure identities with the pre-change baseline rather than assuming the historical 39 failures are unchanged. Record remaining blockers and do not calibrate or rebaseline snapshots. The normal Debug gate reports 407 passed, 39 failed, 2 skipped; every failure was reproduced with bit-identical residuals and drift values on a clean-HEAD worktree (`158bc1b`), so none are introduced by this change.

### Implementation log — exact-coordinate caches (selected design)

- Implemented `IntervalUpstream.PressureAt` memoization: a per-upstream `Dictionary<double, double>` beside `evaluateCache` caches the bounded return value under the exact `double` coordinate; the existing episode traversal and `FreePressureSpan` quadrature moved unchanged into `ComputePressureAt`. Diagnostics remain produced only on cache misses inside `FreePressureSpan`.
- Implemented a per-downstream-piece `Dictionary<double, double>` for the shared `PhysiologicalVisualTarget` used by tunnel vision and grayscale, keyed by exact `c`, inside the `SolveDownstream` visual-target delegate. Redout keeps its separate `RedoutTarget` path and the `head >= RestingBloodHead` zero shortcut is evaluated before the cache. The dictionary is created fresh per downstream solve, so no values are shared across pieces or settings.
- Targeted evidence: `TemporaryPerformanceDiagnostics.PressureCacheEpisodesCheck` synthesizes circulation results with bound entry/release marks through reflection and asserts bound-episode returns of exactly 1.0, repeated-coordinate identity, per-instance cache isolation, and the initial-bound path. `TargetedRegimeCapture` writes `targeted_pressure-pulses`, `targeted_visual-cycle`, and `targeted_visual-unequal-taus` frame/event captures under `cache-reference`; the default-profile pulse scenario covers sustained negative-G overfill/recovery cycles and the visual scenarios cover unequal taus and overfill-to-hypoperfusion transitions. End-to-end bound entry could not be produced under stable default-settings transients; bound episodes are covered at the `IntervalUpstream` level instead.
- `CaptureParityCheck` byte-compares `optimized-cache` capture frames/events against the frozen pre-optimization capture root for both profiles and passes.
- Permanent regressions added to `PhysiologicalIntegrationTests`: `IntervalUpstreamMemoizedPressurePreservesBoundEpisodes` (the migrated episode check; `IntervalUpstream` is private so the single unavoidable reflection seam is used directly, no abstraction) and `FullIntervalUnequalVisualTausKeepChannelsIndependentAcrossPartition` (unequal tunnel/grayscale in/out taus, asserts coarse 0.25 s vs fine 0.02 s partition agreement within `StateSpreadTolerance`, channel independence, and redout separation). The temporary harness was exported to `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\TemporaryPerformanceDiagnostics.final.cs` and removed from the repository.
- Timing status: the optimization target is NOT met. Optimized-control receipts (lead-reported means/maxima above) keep the default and custom averages above 0.1 ms/frame; no causal attribution across runs is claimed. Full-state and event parity with the frozen pre-optimization captures remains byte-identical.
- Evidence root: `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\optimized-cache\` (three sequential uninstrumented runs per profile plus capture CSVs). Timing analysis remains lead-owned.

- [ ] Trial: safeguarded Brent-Dekker refinement for LoC contact-entry roots only — verify: narrow LoC/FullInterval/ScalarLag run reports 106 passed and 31 failed, all failures matching the pre-existing clean-HEAD failure set (GLoC duration/stability families, bit-identical residual `1.0283333961047089E-10` at offset `0.0882676512002945`; the `GLoCDtStabilityTests` failures are in the Experimental category excluded from the normal gate); tolerance parity against the frozen optimized-cache captures passes; three sequential uninstrumented controls per profile are recorded under `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\loc-contact\`. Timing receipts await lead analysis; retain or reject the candidate after review, do not extend the refiner to other call sites.

### Trial log — LoC contact-entry safeguarded refinement (under review)

- `NumericalMath.BrentDekkerSign` added: bracket-preserving Brent-Dekker refinement taking the numeric function, bounds, already-evaluated endpoint values, and the existing `toleranceSeconds`. Secant/inverse-quadratic steps are accepted only inside the current sign bracket with bisection fallback; success requires the true sign bracket to shrink to `toleranceSeconds` (exact `f==0` returns immediately); nonfinite evaluations or non-bracket inputs return `NaN`; 200-iteration cap; returns the final bracket midpoint, matching `BisectSign`'s contract. No residual-only termination.
- `ScanCrossing` (private nullable `CrossingSamples` overload) gained an optional `safeguardedRefine` parameter defaulting `false`. Scan sampling, exact-equality, plateau, direction, midpoint and invalid-handling semantics are unchanged; sampled endpoint values are carried into the refiner to avoid reevaluation. Only the `ChaseLoC` contact-entry scan passes `safeguardedRefine: true`; contact exits, downstream O2 roots, and every other caller remain on `BisectSign`.
- Tests added to `PhysiologicalIntegrationTests`: `BrentDekkerSignLocalizesRootsAcrossShapes` (linear, asymmetric nonlinear, flat cubic, near-start, near-end, exact-endpoint roots; evaluation-count bound) and `BrentDekkerSignRejectsInvalidInputs` (same-sign endpoints, nonfinite endpoint, nonfinite mid-bracket); `FullIntervalLocContactEntryLocalizesMovingCeiling` (moving-ceiling entry offset/level, partition spread) and `FullIntervalLocContactCoincidentCeilingTracksWithoutEntry` (coincident-contact endpoint, no spurious entry).
- Diagnostic evidence: `TemporaryPerformanceDiagnostics` recreated from `TemporaryPerformanceDiagnostics.final.cs` with `ControlSubdir = "loc-contact"` and `CaptureParityCheck` rewritten as a tolerance comparison against `optimized-cache` (structural columns and event kind/order exact; state within `StateSpreadTolerance`; event offsets within 1e-6 s). Three sequential uninstrumented controls per profile plus captures pass; every `noGcValid` is true.
- Evidence root: `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\loc-contact\` (six run CSVs, four capture CSVs, console-parity.txt); harness copy at `TemporaryPerformanceDiagnostics.loc-contact.cs`. The timing target remains unmet pending lead analysis; no causal attribution across runs is claimed.

- [x] Trial: extend safeguarded refinement to both LoC contact-exit derivative scans — verify: `BrentDekkerSign` now validates finite ordered bounds, a positive finite tolerance, and returns `NaN` when the 200-iteration cap is exhausted without meeting tolerance; ChaseLoC contact-entry and both contact-exit scans use `safeguardedRefine: true` (temporary `TemporaryLocSafeguardedRefine`/evaluation-counter hooks were used only for the paired diagnostics and have been removed); narrow PhysiologicalIntegrationTests run reports 37 passed / 0 failed including new upward/downward exit-threshold, nonfinite, and plateau tests; paired evidence under `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\loc-all-contact-paired\` records baseline-vs-refined captures (baseline byte-identical to optimized-cache, refined within tolerances), per-frame LoC evaluation counts, and 3 alternating run pairs per profile. Lead reviewed the raw paired captures: event kind/order exact, maximum event offset delta 4.5367e-9 s, maximum endpoint state delta 3.322e-9 — numerical result approved and the candidate is provisionally retained; frame-77 median improved 1.1693→0.5078 (default) and 1.0133→0.6509 (custom) though the overall limits still fail.

### Trial log — all-LoC-contact safeguarded refinement (under review)

- `BrentDekkerSign` hardened per review: unordered/nonfinite bounds, nonpositive/nonfinite tolerance, non-bracket inputs, nonfinite evaluations, and iteration exhaustion without meeting `toleranceSeconds` all return `NaN`; converged brackets still return the midpoint, matching `BisectSign`.
- Both `ChaseLoC` contact-exit derivative scans now use `safeguardedRefine: true`, alongside contact entry; every other `ScanCrossing`/`BisectSign` caller remains on bisection.
- New tests in `PhysiologicalIntegrationTests`: `BrentDekkerSignRejectsInvalidInputs` covers unordered/degenerate/nonfinite bounds, zero/negative/nonfinite tolerance, and unreachable-tolerance exhaustion; `ChaseLocContactExitLocalizesUpwardRateThreshold` (exact ln(1.5) analytic offset), `ChaseLocContactExitLocalizesDownwardRateThreshold` (finite-difference-replicated reference), `ChaseLocContactSkipsEventWhenConsciousnessGoesNonfinite`, `FullIntervalLocContactExitMatchesRecordedInterval` (workload frame-77 seeded exit, offset 0.17839209855720917), `ScanCrossingSafeguardedRefinePreservesPlateauAndDirection` (plateau, direction, endpoint-zero, and nonfinite parity between bisection and refinement).
- Paired diagnostics: one-process A/B via the temporary switch (`baseline` = bisection on all three scans, `refined` = Brent-Dekker); 3 run pairs per profile in alternating order, all `noGcValid`; a separate counted replay records per-frame LoC scan evaluations (e.g. default frame 77: 86 baseline vs 46 refined evaluations; 407 frames with nonzero LoC evaluations per profile).
- Evidence root: `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\loc-all-contact-paired\` (12 run CSVs, 8 capture CSVs, 4 counts CSVs); harness copy at `TemporaryPerformanceDiagnostics.loc-all-contact.cs`. The paired test code is gated behind the file's `TEMPORARY_PROFILER` define and the model hooks are removed. The timing target remains unmet pending lead analysis; no causal attribution is claimed.

- [x] Trial: extend safeguarded refinement to scalar consciousness/visual transition surfaces — verify: REJECTED. The paired captures show event-sequence changes at 5 default and 3 custom workload frames, all added/removed boundary re-detection duplicates on the newly refined surfaces (details in the trial log below), violating the exact event kind/order parity gate. All production markings were reverted; the rejected candidate diff is preserved at `scalar-surface-rejected-candidate.diff` in the evidence root. Narrow PhysiologicalIntegrationTests run reports 41 passed / 0 failed on the reverted tree.

### Trial log — scalar consciousness/visual surface refinement (rejected)

- Design tried: `ScanSurface` gained `safeguardedRefine`/`counted` plumbing to the nullable `ScanCrossing`, enabled at three call sites — `ConsciousnessModeTransition`, `ConsciousnessLost`/`ConsciousnessRecovered` hysteresis, and the three `Visual*Transition` channels. Paired A/B used a temporary `TemporarySafeguardedRefine` gate inside `ScanCrossing.Refine` toggling all six marked surfaces (LoC + scalar) together.
- Rejection evidence: baseline captures are byte-identical to `optimized-cache` (baseline = bisection everywhere), while refined captures change event sequences: default frames 488 (2+2 PerfBranch/Tunnel pairs + Grayscale becomes 2 PerfBranch + 1 Tunnel + 2 Grayscale), 643/781/866 (duplicate `VisualRedoutTransition` pairs collapse to one), 964 (duplicate `ConsciousnessModeTransition` pair collapses to one); custom frames 488 (adds a Grayscale duplicate), 643 (adds a Redout duplicate), 964 (adds a ModeTransition duplicate). All diffs are boundary re-detection duplicates created/destroyed by ~4e-9 s root shifts at piece boundaries — inherent to any root approximation that differs from the bisect midpoint within tolerance. The seeded single-frame fixtures reproduce this exactly (frame 645: baseline one `ConsciousnessModeTransition`, candidate two; frame 488: baseline two tunnels, candidate one; Debug vs Release codegen can itself flip a duplicate — Debug replay of frame 645 showed the duplicate while the Release capture did not).
- Relevance counts (marked-scan margin evaluations, all six surfaces counted): default total 49413→48946, custom 49301→49151; hot frames 77: 156→116, 81: 249→194, 541: 107→87, 643: 148→75, 964: 204→167 — most per-frame evals are the fixed 7-probe sampling, so the attainable reduction was modest regardless of parity.
- Kept from the trial: recorded-fixture tests `FullIntervalConsciousnessLossMatchesRecordedInterval` (frame 81), `FullIntervalConsciousnessRecoveryMatchesRecordedInterval` (frame 541, includes half-step partition check), `FullIntervalVisualTransitionsMatchRecordedInterval` (frame 488, partition check), `FullIntervalConsciousnessModeTransitionMatchesRecordedInterval` (frame 645) — these caught the regression and remain as baseline-behavior gates; `RecordedState`/`AdvanceRecorded` helpers compact the fixtures and the frame-77 test was converted to them.
- Evidence root: `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\scalar-surface-paired\` (4 capture CSVs, 4 counts CSVs, rejected-candidate diff); harness copy at `TemporaryPerformanceDiagnostics.scalar-surface.cs`. No paired timing was run: the candidate failed the parity gate first. The four recorded scalar fixtures and the `RecordedState`/`AdvanceRecorded` helpers were removed from the test file after exporting them into that evidence root; the frame-77 LoC fixture is inlined again.

- [ ] Trial: skip dense-Taylor preparation for oxygen-only trial prefixes — verify: `SolveAffineCollocation` gained an internal optional `prepareDenseTaylor` parameter; `SolveDownstream`'s oxygen call passes `!oxygenOnly` so rejected/trial O2 root prefixes skip `PrepareDenseMomentTaylor` while still building `MomentPowers` and keeping the exact same convolution fallback for their endpoint `Evaluate(1)`/`Derivative(1)` queries; new `SolveAffineCollocationSkippedDenseTaylorMatchesPreparedEndpoint` covers prepared-vs-skipped endpoint/derivative parity including instant coordinates; narrow PhysiologicalIntegrationTests run reports 43 passed / 0 failed; paired evidence under `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\o2-skip-taylor-paired\` (3 alternating run pairs per profile, all `noGcValid`, plus full captures) shows refined captures byte-identical to baseline and baseline byte-identical to the approved all-LoC captures — zero event/state divergence. Timing receipts await lead analysis.

### Trial log — oxygen-only dense-Taylor skip (under review)

- Motivation: lead profile of frame 117 showed `iOxygenDenseSolve` 24565 ticks, `iRootRefine` 22473, `iMomentPowerCacheCreate` 8927, and 26 oxygen-only prefix solves; those trial prefixes only query `Evaluate(1)`/`Derivative(1)` and never dense-sample, so preparing the certified Taylor coefficient cache was wasted work.
- Production change is six lines: an optional `prepareDenseTaylor` parameter on `SolveAffineCollocation` defaulting `true`, gating only the `PrepareDenseMomentTaylor` call inside the existing `n == 3` cache block, and `prepareDenseTaylor: !oxygenOnly` at the oxygen call site. `MomentPowers` construction, stage solve, residual validation, and the dense fallback path are unchanged; the analytic moment convolution is the same equation with the same certified bound, not a new solver.
- Paired diagnostics: temporary `TemporarySkipOxygenDenseTaylor` switch toggled the call site between always-prepare (baseline, equal to the approved all-LoC build) and oxygen-only-skip (refined) in one process; three run pairs per profile in alternating AB/BA/AB order, all 128 MiB no-GC batches valid; captures in a separate untimed replay. The switch has been removed; the paired harness code remains behind the `TEMPORARY_PROFILER` define.
- Evidence root: `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\o2-skip-taylor-paired\` (12 run CSVs, 8 capture CSVs); harness copy at `TemporaryPerformanceDiagnostics.o2-skip-taylor.cs`. Parity is byte-identical for all four capture comparisons — the strongest possible parity result. The timing target remains unmet pending lead analysis; no causal attribution is claimed.

- [x] Trial: reuse MomentPowers for affine stage-moment evaluation — verify: REJECTED for no demonstrated execution-time gain. Lead review of the raw paired receipts: custom means baseline 0.138488/0.129338/0.131159 vs refined 0.139142/0.133522/0.137852 (all three slower), maxima mixed, default mixed. Captures were byte-identical for all comparisons (refined vs baseline and baseline vs the retained o2-skip-taylor-paired captures), so parity held but the candidate did not earn its keep. The stage-reuse hunk and its `MomentPowerCacheMatchesUncachedCollocationMoments` test were reverted; the rejected candidate source is preserved at `stage-moment-cache-paired\PhysiologicalModel.stage-moment-cache-rejected.cs` with the trial-only diff at `stage-moment-cache-rejected.diff`. The approved all-LoC refinement and the oxygen-only dense-Taylor skip remain in place; narrow PhysiologicalIntegrationTests run reports 43 passed / 0 failed after the revert.

### Trial log — affine stage moment-cache reuse (rejected)

- Motivation: lead instrumentation showed `iMomentPowerCacheCreate` ~8927 ticks on frame 117; the stage loop computed uncached `TryMomentMatrices(m0, h, c, ...)` at all three Radau nodes and only then created the 32-power cache, so the same matrix powers were assembled three times per solve plus once more for the post-solve cache.
- Candidate (reverted): for `n == 3`, `solution.MomentPowers` populated by `CreateMomentPowerCache(m0, h)` before the stage loop; node moment evaluation via `TryMomentCaches(solution.MomentPowers, c, ...)` with uncached fallback; end-of-solve cache creation removed. Same moments algorithm/tolerances/scaling; captures byte-identical.
- Rejection evidence: raw paired CSVs under `C:\Users\AMPW\AppData\Local\Temp\GEffectsDiag\stage-moment-cache-paired\` (12 run CSVs, 8 capture CSVs, rejected-candidate source + trial diff); harness copy at `TemporaryPerformanceDiagnostics.stage-moment-cache.cs`. Lead verdict: no demonstrated execution-time gain — custom refined slower in all three runs; default mixed.
- Cleanup applied with the rejection: the temporary `TemporaryStageMomentCache` switch and the `TemporaryPerformanceDiagnostics.cs` harness were removed (final harness copy at `GEffectsDiag\TemporaryPerformanceDiagnostics.final.cs` and inside the paired evidence root). `BrentDekkerSign` iteration exhaustion was additionally hardened per lead review to return `NaN` unconditionally after the loop — the prior post-loop bracket-width check read `c`/`b` after the final update, where `c` can be same-side stale because the same-sign repair only runs at loop top; exact-zero returns stay inside the loop and width success stays at loop top, covered by the existing unreachable-tolerance test.

### Final state after all trials

- Retained production optimizations: exact per-interval `IntervalUpstream.PressureAt` memoization; per-downstream-piece physiological visual-target memoization shared by tunnel/grayscale (redout separate); `NumericalMath.BrentDekkerSign` safeguarded refinement on all three `ChaseLoC` contact scans (entry + both exit derivative scans) with all other scans on bisection; `prepareDenseTaylor` seam letting oxygen-only trial prefixes skip `PrepareDenseMomentTaylor`.
- Rejected: scalar consciousness/visual surface refinement (event-sequence changes, `scalar-surface-paired\`); affine stage moment-cache reuse (no timing gain, `stage-moment-cache-paired\`).
- Remaining blocker: the maximum-frame budget is still unmet. Three sequential unchanged official Release receipts (`official-final-receipts\`, raw console + TRX): run 1 default avg 0.8172 ms / max 7.0010 ms, custom avg 0.4157 ms / max 3.8371 ms; run 2 default avg 0.5006 ms / max 5.6431 ms, custom avg 0.2566 ms / max 1.5542 ms; run 3 default avg 0.4376 ms / max 5.7851 ms, custom avg 0.2947 ms / max 2.7321 ms — all fail the 0.1 ms average / 0.5 ms maximum budgets. Finite experiments exhausted the approved scope; no approved approximate fallback exists. Correctness metrics analysis remains lead-owned.
- Final verification: narrow PhysiologicalIntegrationTests subset 43 passed / 0 failed; normal Debug gate (`Category!=Experimental&Category!=Performance`, TRX `normal-gate-final.trx`) 417 passed / 39 failed / 2 skipped with the failure set identical to the pre-existing clean-HEAD set; `dotnet build GEffectsLogic.slnx -c Release -warnaserror` clean for net10.0 + net481 + GUI + tests; `dotnet format` whitespace/style/analyzers `--verify-no-changes` clean; license-header check clean; `git diff --check` clean.

Execution ownership: profiling instrumentation, implementation and ordinary verification may be delegated. Measurement recipes, raw-record analysis, hotspot selection, optimization design and final evidence review remain lead-owned. No server or long-running process is required; check for existing diagnostic processes before starting and avoid concurrent timing runs.

Files in scope: temporary diagnostics under `GEffectLogicTests`, `GEffectsLogic/PhysiologicalModel.cs`, `GEffectsLogic/NumericalMath.cs`, targeted regressions in `GEffectLogicTests/PhysiologicalIntegrationTests.cs`, and this progress artifact. Preserve `LogicInstancePerformanceTests.cs`, settings, snapshots and CI gates.

Initial official command: `dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Release --filter "FullyQualifiedName~UpdateExecutionTimeRemainsWithinFrameBudget"`. Narrow regression selection will follow the measured optimization seam; do not repeatedly rerun the broad suite during profiling.

### Resume — allocation attribution + bundles (2026-10-25)

**Attribution (GCAllocationTick trace `alloc-tick-20261025\`, phase probe `alloc-phases-20261025\`):** ~100KB/frame: strings ~54MB trace (eager violation messages built unconditionally in `ValidateDenseBlood`/`CheckStageValidity` local fns), dict resizes ~65MB (`physiologicalTargetCache` cap 64, `IntervalUpstream` caches), upstream ctor dicts ~40MB, delegate closures ~45MB (`ExponentialConvolution` source funcs, `BuildOxygenSystem` TrialAt), solver buffers ~37MB, moment cache 14MB, circulation/event bodies ~80MB combined.

**NoGCRegion finding (`nogc-check-20261025\`):** `TryStartNoGCRegion(64MB)` tolerates ~69.3MB with ZERO GCs inside the region (gen0/1/2 deltas 0); `EndNoGCRegion` does not throw. Outliers are pure compute (event-localization solves), not GC pauses — GC-driven-outlier hypothesis disproved.

**Applied bundle 1:** deferred violation strings (local fns take metric/value/condition, build post-guard); pooled `IntervalUpstream` evaluate/pressure dicts (`[ThreadStatic]` slots, `Release()` under try/finally in `AdvanceInterval`, capacity 128); generic `ExponentialConvolution<TState>` + static lambdas at `EvaluateFreePressure`/`ScalarLagSolution.Evaluate`. Captures byte-identical; alloc ~96→~71KB/frame; paired ~20% faster.

**Applied bundle 2:** `AffineSolution.cache` 16→32, `physiologicalTargetCache` 64→128, flat `DenseMomentCoefficients` (double[192]), pooled `AffineSolveScratch` for n==3 (lhs/lhsWork jagged 9x9, rhs/rhsWork/flat/p/deltaF=9, w=81, dm=27, `InUse` guard, `TrySolveLinearInPlace`, residual check on originals), `VisualInstantNames` static. Captures byte-identical; alloc ~63-69KB/frame; official default ~0.222-0.227.

**Applied bundle 3:** hoisted `candidates`/`due`/`boundStageViolations` lists; pooled 9 local `List<string>` diagnostics via `diagnosticsListPool`; +25 `[MethodImpl(AggressiveOptimization)]` (const-512 pattern; total ~63 attrs incl. earlier 38) on second-tier circulation/event/matrix helpers. Captures byte-identical; official run1-3: default 0.1856/0.1905/0.1846 max 1.35-1.71; custom 0.0880/0.0935/0.0913 (PASSES avg, max 0.62-0.71 fails).

**Status:** custom avg passes 3/3; default avg ~0.19 (target 0.1); both maxima fail (target 0.5). Identified driver: `LocalizeBoundEntry`/`LocalizeBoundRelease` run up to 60 full `AdvanceCirculationSegment` solves per bound event via bisection. In flight: cubic-bracket seeding / guarded secant to cut solve count (event times within tolerance per precedent). Delegate-closure refactor skipped (signature fan-out); `AdvanceCirculationSegment` buffers escape via `RecomputeResidual` (unpoolable); `IntervalTrialState` already struct; `MomentPowerCache` powers[32] not shrunk (PowerAt path changes arithmetic past PowerCount).

**Pre-existing:** `PairedCaptureParityCheck` vs frozen `o2-skip-taylor-paired` reference fails on 1-ULP digits since earlier stages — stale frozen reference, not a regression.

### Attribute completion + pressure/lean-eval fixes (2026-10-25 late)

- `DOTNET_TieredCompilation=0` official run PASSED both profiles → proved remaining gap was JIT tier-0/background-promotion on the single measured pass, not compute. Total ~225 `[MethodImpl(AggressiveOptimization)]` attributes now across PhysiologicalModel (~191), NumericalMath (~31), GEffectsLogicInstance (3) via const-512 pattern.
- `TryRefineRootEvent` seed bracket (`BoundEventSeedPadSeconds=4e-6`): refine solves −48% (50→26 default). First non-byte-identical change: event times ≤0.98ns, state ripples ≤19ulp confined rows ~496-654 (within precedent tolerances).
- Pressure-source constancy certificate (`IntervalUpstream`): sigmoid-flat spans replace 16-node convolution with bit-identical `ConstantFreePressure`; `EvaluateHeadPressure` lean path for consciousness targets. Byte-identical captures restored after this change.
- Worst-frame instrumentation (`worst-frames-20261025\`): dominant sinks were ~430-650 pressure misses/frame and ~500-1140 upstream evals/frame — both fixed; residual worst frames are +Gz event re-detection (5-9 SolveDownstream/pieces) — piece-count reduction would change emitted event sequence → parity-blocked.
- Official ×3 (worst-frames-20261025 + attr-complete runs): default avg 0.0794-0.0853 / max 0.63-0.69; custom avg 0.0836-0.0867 / max ~3.1-4.05 sporadic. AVG gate PASSES both profiles 6/6; MAX gate still fails (default genuine compute ~0.66; custom sporadic jitter-class spikes seen only under NoGCRegion/official — diagnostics consistently ≤0.63).
- Harness `TemporaryPerformanceDiagnostics.cs` deleted by subagent then restored from `flat-solve-paired-20261023\TemporaryPerformanceDiagnostics.pre-flat.cs` (user `PairedSubdir` value intact).
- Open: `TemporaryTargetOnly = true` provisional switch pending productionize/revert decision; ~225 AggressiveOptimization attributes are effective but need maintainer-style review; pre-existing `PairedCaptureParityCheck` stale-reference failure; 39 pre-existing Debug gate failures unchanged.
