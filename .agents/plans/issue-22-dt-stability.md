---
agent: devin-local
session: historical-quartz
created: 2026-10-04T16:16:23Z
---
# Issue #22: equation-based timestep stability

Make constant multiaxial physiology agree across 10–1000 ms timesteps with physiological integration owned by PhysiologicalModel, preserve measured GLoC timing, and enforce representative local plus dense blocking CI verification.

## Summary

Issue: <https://github.com/AMPW-german/GEffectsLogic/issues/22>.

Replace caller-step-dependent physiological updates with a coherent interval formulation: exact integrations for independently solvable processes, a model-specific implicit solve for coupled processes, and localized events for discontinuities. Keep physiological control flow, state ownership, and value computation in `PhysiologicalModel`; `GEffectsLogicInstance` remains its wrapper. Do not hide instability behind fixed/adaptive numerical substeps. Preserve physiological mechanisms and measured 250 ms GLoC behavior, allowing only the minimum evidence-backed calibration needed to reconcile corrected numerics with that behavior.

Legacy API compatibility is not required: `main` already contains multiple unreleased breaking changes. Do not retain obsolete public/protected members, constructors, virtual hooks, or compatibility adapters solely for old callers. This does not relax the numerical, physiological, GLoC timing, or resource requirements, and does not authorize unrelated API redesign.

This is an implementation plan, not an implementation or a claim that the selected solver already meets the requirements. The numerical/performance feasibility gate is mandatory. Failure requires an evidence-based report and a new decision, not an unapproved fallback.

## Artifact and authorization

- Implementation branch: `fix/22-dt-instability`. Create it from `main` only when implementation is separately requested; saving this plan does not create or switch branches.
- Session artifact: `C:\Users\AMPW\.devin\plans\plan-0011f1de242f0fdb.md`.
- Requested repository artifact: `.agents/plans/issue-22-dt-stability.md`, consistent with existing repository plans. This is a plan document, not new tool configuration.
- Planning currently permits only the session artifact to be written. After authorization to leave planning mode, copy this approved document to the repository artifact before any other work.
- The current request is to produce and save a plan. Approval to save its repository copy does not itself request physiological implementation. Stop after saving the copy unless implementation is separately requested.
- No repository source, tests, settings, or workflows were changed during investigation. No local build/test/prototype was run. A read-only scalar stability-function calculation was performed to check the proposed numerical design.

## Confirmed decisions

- [x] Official supported caller dt is inclusive: 0.010–1.000 seconds.
- [x] Constant loads must agree at matched elapsed times, not merely approach a common eventual equilibrium.
- [x] Maximum absolute spread is 0.001 for normalized physiological/visual states. HR multiplier and resting-relative head overfill also use absolute 0.001. Algebraic equal-input outputs should agree to roundoff.
- [x] Guarantee covers rested and identical preconditioned states, including fatigue, impairment, recovery, push-pull, sudden-LoC, and death histories.
- [x] Timing anchor is current 250 ms behavior. Preserve current duration-test bounds, not the separate desired timings in `AGENTS.md`.
- [x] Legacy timing drift allowance is `max(0.5 seconds, 0.02 * legacy event time)` on identical profiles/schedules. This is a behavioral requirement, not an API-compatibility requirement.
- [x] Equation changes only: exact solutions and a model-specific high-order implicit interval solve are permitted. Fixed/adaptive internal numerical timesteps are not.
- [x] Remove subdivision within the supported range. Splits at actual physiological events/constraint boundaries are permitted, not splits disguised as an integration limit.
- [x] Correct stabilization caching if it freezes hidden state or exceeds the state-error budget.
- [x] Preserve meaningful caller-owned profile support; test representative custom and faster/slower profiles. No general settings-validation feature.
- [x] Minimal justified rate/default calibration is permitted to retain measured timing. No new G-force deadzones, fitted effect bands, or pursuit of unrelated physiological tuning targets.
- [x] Frozen 100 ms snapshots may be rebaselined only after independent checks and full old/new review.
- [x] Supported-range dT checks become blocking CI checks.
- [x] Default local tests use representative timesteps. CI additionally runs the dense integer-ms suite plus long representative cases; dense execution is manually opt-in locally.
- [x] Stop with evidence if equation-only accuracy and unchanged performance budgets cannot both be met. Do not weaken either.
- [x] Update only comments/documentation made inaccurate by this work; no unrelated cleanup.
- [x] Legacy API compatibility is not required; scoped breaking changes are allowed without compatibility shims.
- [x] `PhysiologicalModel` contains the actual physiological logic, value computation, integration orchestration, state, and event handling. External helpers may perform numerical arithmetic only; they must not become another physiological model/service.

## Acceptance criteria

### State and invariant checks

For identical starting state, settings, constant `(Gx,Gy,Gz)`, and elapsed time:

- [ ] `max(value)-min(value) <= 0.001` across tested partitions for every continuous public state at common checkpoints/endpoints.
- [ ] Compare every channel in `SettingsRegressionTests.ModelSnapshot`: blood volumes/overfill, all O2 pools/brain O2, arterial O2, HR, perfusion, consciousness, pressure impairment, AGSM/suit/cardio/respiratory fatigue, neck fatigue, compression, pain, sudden accumulator, and all visual channels. Adapt capture access if scoped API changes require it; keep channel coverage.
- [ ] Equal load/profile algebraic outputs (`GxEffectiveTolerance`, `GyEffectiveTolerance`, settings HR floor) agree within `1e-12`. Brain O2 remains the head-O2 alias.
- [ ] Inspect hidden predictors as well: strain, neck-death dwell, oxygen-exchange reduction, and discrete modes. Use internal test visibility, not new public state APIs solely for tests.
- [ ] Check actual variable/awkward partitions, not equal call counts or dt rounded to reciprocal integers.
- [ ] All supported-range accepted state is finite; volumes nonnegative, head floor respected, blood sum within `1e-12` of 1.
- [ ] Preserve existing state bounds, brain-O2 floor, and relevant HR bounds. Perfusion remains capped at 1; overfill cannot supply extra cerebral oxygen.
- [ ] Preserve negative-G overfill, pressure impairment, bradycardia/near-stop behavior, push-pull memory, independent visual channels, sudden-LoC, and permanent death.
- [ ] Check equivalence with stabilization disabled and enabled. Freezing cannot disguise numerical divergence.

### Events and timing

Distinguish public `IsUnconscious` onset/recovery (currently 0.05/0.35), the existing test's separate `ConsciousnessLevel <= 0.01` event, `InSuddenLoC`, and neck-fatigue death.

- [ ] For identical constant loads/preconditioned histories, refined event brackets have width <=0.005 s and numerical cross-partition event spread <=0.05 s.
- [ ] Keep raw observation brackets `(last pre-event time, first post-event time]`. Refined events must lie within them; observation brackets must be compatible with the common numerical event window.
- [ ] Overlapping one-second observation brackets alone do not prove 0.05-second accuracy. Use localized internal events or test-only root refinement/replay of the same pre-call state.
- [ ] Boolean/jump differences at a checkpoint are allowed only inside a measured event window <=0.05 s wide. Outside it, flags and event-applied overrides agree and continuous states retain 0.001 tolerance. Event-induced fade differences do not receive an unlimited exception.
- [ ] At the legacy 250 ms observation cadence, with identical input/prehistory, event times stay within `max(0.5 s, 2%)` of frozen pre-change values. Compare brackets as well as first-observed times.
- [ ] Existing `GLoCDurationTests` bounds and dT report thresholds pass unchanged; do not remove difficult scenarios or increase limits. Mechanical caller adaptations for approved breaking changes must preserve the tested schedules and assertions.
- [ ] Preserve event/no-event classifications and event order. Recovery tests use the same scheduled switch or identical preconditioning, not switches at different observed onset times.

### Architecture, API policy, and cost

- [ ] No legacy API-compatibility gate. Public signatures, constructors, virtual hooks, protected fields, and internal state layout may change where needed for this work. Update affected in-repository callers/tests directly; do not add shims or preserve obsolete members solely for unreleased compatibility.
- [ ] `PhysiologicalModel` owns physiological rates/targets, coupled block sequencing, Newton/stage orchestration, constraints, event selection/application, accepted-state commit, derived outputs, and physiological reset. `GEffectsLogicInstance` remains a wrapper for time/force bookkeeping, logging integration, settings application, and stabilization scheduling.
- [ ] External computation helpers are limited to reusable mathematical operations; no physiology-aware `Advance` entry point, owned physiological state, `LogicSettings` interpretation, rate/target computation, or event/control policy outside `PhysiologicalModel`.
- [ ] Retain `net481` and `net10.0`, the same numerical equations on both, no new numerical package, and no unsafe code.
- [ ] Keep immutable caller-owned settings and global logging flags. The existing 122 numeric settings are the planned parameter set; no profile registry, solver settings surface, or numerical configuration feature is needed.
- [ ] Existing budgets remain: average complete update <=0.1 ms, every measured update <=0.5 ms; default/shared-profile construction <=1024 bytes, unique-profile construction <=2048 bytes.
- [ ] Representative suite blocks by default locally; dense cases explicitly skip unless opted in. CI dense execution is blocking and not skipped.
- [ ] Outside 10–1000 ms, use best-effort whole-interval stability without an out-of-range accuracy claim or new stability tests. Existing performance frames above one second still run unchanged.

## Investigation and grounded baseline

Investigated commit: `a1cc1dcbeadc81df94c5cb6193be2adec7b311ce`; working tree clean. Installed Windows SDK/runtime: .NET SDK 10.0.401 / runtime 10.0.12.

Read root instructions and both relevant skills, complete model/wrapper/settings, `Logic.md`, all dT/duration/load tests, settings snapshots/ownership/wake/reset/concurrency tests, logging tests, performance tests, GUI callers, project files, contribution guidance, and build/lint workflows.

### Existing CI evidence

Read-only Build run `37196481324`, test job `111419412163`, at the exact investigated commit: normal 105/105 pass; experimental dT 7/18 pass and 11 fail; performance 3/3 pass. Workflow is green because experimental failure is ignored.

Frozen report observations below use the report's approximately 1 G/s Gz ramp from +1 and its `consciousness <= 0.01` threshold. They are not constant-load timings or public `IsUnconscious` onset times.

| Target Gz | 250 ms report event time |
| --- | ---: |
| -6 | 10.000 s |
| -5 | 10.000 s |
| -4 | 10.000 s |
| -3 | 26.750 s |
| +4 | 319.500 s |
| +5 | 29.250 s |
| +6 | 8.500 s |
| +7 | 8.500 s |
| +8 | 8.500 s |
| +9 | 8.500 s |

Other verified observations:

- -3 Gz: 10 through approximately 143 ms never reach the report threshold within 1800 s; 250 ms reaches it at 26.75 s.
- -4 Gz: 10 ms never reaches that threshold within 1800 s.
- -5 Gz report spread: 5.18 s.
- -2 Gz final consciousness: approximately 0.71833 at 10 ms, 0.59296 at 250 ms, 0.20820 at 500/1000 ms; final spread approximately 0.51014.
- Positive-G report spreads include 6.62 s at +4 Gz and 2.13 s at +6/+7/+8/+9 Gz.

### Exact change seams

| Location | Current behavior / implication |
| --- | --- |
| `GEffectsLogicInstance.Update`, lines 98–225 | Allocates dt list; splits above 0.5 s; updates time/forces; checks stabilization before physics; delegates/logs. |
| Wrapper stabilization fields and lines 130–214 | Uses 0.025 thresholds on a subset; ignores fatigue/respiration/core/lower O2/neck/sudden state; unchanged loads can skip forever. Default dwell is 600 s, despite older prose. |
| `PhysiologicalModel`, lines 271–319 | Backward-Euler response helper and nonlinear head root solve. A-stability/common scalar fixed point is not trajectory equivalence. |
| Model lines 357–445 | End-step strain drives fatigue/suit; blood updated separately and normalize/clamp/renormalize. Raw drives do not sum to zero; normalization is not merely roundoff. |
| Model lines 447–508 | Explicit-Euler pressure accumulation; unused target/alpha describe another law; HR/cardio feedback is ordered by calls. |
| Model lines 519–641 | Add-then-decay respiration; custom per-call HR multiplier; in-place sequential oxygen transfers, refresh, consumption, and brain relaxation. |
| Model lines 643–809 | Changing coupled state drives consciousness, hysteresis, rate-limited LoC, lagged visuals and pain. |
| Model lines 814–885 | Neck ceiling is clipped before recovery; death dwell counts whole dt; sudden source/recovery and discontinuous drop occur at endpoints. |
| `DtStabilitySimulationResults.cs` | Shared cached Gz-only ramps; 16 reciprocal-integer dt; 1800 s; 250 ms report anchor. |
| Three existing dT classes | All Experimental; mostly consciousness/events, not complete state; report has an additional dt-scaled baseline limit. |
| `SettingsRegressionTests.cs` | Eight 100 ms profile snapshots at `1e-12`; reuse capture records, but deliberately migrate expected values after independent review. |
| `InstanceSettingsTests.cs` | Profile ownership, identical runs, direct/wrapper equality, reset/wake; zero-dwell fixture currently permits premature stability. |
| `build.yml`, lines 125–154 | Test job already depends on artifact build; experimental dT is `continue-on-error`; Release performance is blocking. |
| GUI simulation/plot callers | Variable dt/settings through the instance API; no GUI feature change is needed. Adapt call sites directly if scoped API changes require it. |

## Numerical design

### PhysiologicalModel owns the integration

Implement the interval formulation directly in `GEffectsLogic/PhysiologicalModel.cs`. Do not extract its state, physiological value calculations, or update orchestration into a separate `PhysiologicalIntegration` class/service. `GEffectsLogicInstance` already provides the wrapper boundary.

Use model-local internal value types for trial continuous state, mode, event offsets, and accepted result. They can be nested types in `PhysiologicalModel` and support pure trial evaluation without duplicating persistent physiology. The existing field layout may be consolidated or replaced if that makes the integration simpler; protected-field compatibility is not a constraint.

Use this model-owned internal seam for production orchestration and mathematical tests:

```csharp
internal IntegrationResult AdvanceInterval(
    in IntegrationState initial,
    double dt,
    double gx,
    double gy,
    double gz,
    LogicSettings settings);
```

`AdvanceInterval` is an instance method of `PhysiologicalModel`, not an entry point on an external integration engine. The trial state contains actual accumulators: blood/O2, HR/strain/fatigues, pressure, respiration/compression/arterial O2, consciousness/lagged visuals, pain, sudden/neck/death dwell. The model has one authoritative persistent state; trial snapshots do not create a second owned model.

`PhysiologicalModel.Update` captures its current state and selected settings, calls its own interval method, commits the accepted endpoint/modes, computes perfusion/tolerances/blur/grain/exchange reduction, and logs accepted events once. Model methods compute physiological rates, targets, Jacobians, block order, active constraints, event roots/policy, and accepted results. Stage/root/Newton evaluation uses explicit trial state and must not mutate persistent state, log, change live flags, or call consumer callbacks.

Reusable arithmetic may be extracted to `GEffectsLogic/NumericalMath.cs`: stable exponential/affine/convolution expressions, Radau coefficients, and small pivoted linear-system arithmetic. These helpers take numeric arguments/buffers, not a model or settings profile. Keep physiological coefficient/source construction, stage/Newton orchestration, branching, event decisions, and state/output computation in `PhysiologicalModel`. Do not add a generic public solver framework or move the whole update algorithm into a helper under another name.

Add `Properties/AssemblyInfo.cs` with canonical GPL header and `InternalsVisibleTo("GEffectLogicTests")`. Tests access the model-owned interval seam and its trial/result types. Localized actual event offsets support internal tests/logging, not a new public timestamp API. The separate 0.01 test crossing can be found by test-only replay/dense-output root refinement.

### Exact independent equations

For constant coefficients:

```text
x' = a-b*x
x(h) = x(0)*exp(-b*h) + a*(1-exp(-b*h))/b
```

Use the `b=0` limit `x+a*h`; target relaxation is `target+(current-target)*exp(-h/tau)`. Preserve instantaneous-tau semantics. Use common-framework `Math.Exp` and a small-argument series where `1-exp(-x)` or its integral suffers cancellation; no .NET-10-only math API. Mathematical expressions can be shared helpers; the model computes their physiological inputs and selects the appropriate law.

Solve as time functions throughout the interval:

- Strain lag, G-only respiratory source/recovery, compression, arterial oxygenation, pain.
- Sudden source/recovery and neck source/recovery with existing ceilings/bounds.
- AGSM/suit fatigue driven by exact strain, retaining build-versus-rest rules.

For `s(t)=S+D*exp(-t/tau)`:

```text
integral(s)  = S*h + D*tau*(1-exp(-h/tau))
integral(s²) = S²*h + 2*S*D*tau*(1-exp(-h/tau))
               + D²*tau/2*(1-exp(-2*h/tau))
```

Localize real build/rest switches (`s=0.01`) and bound entry/release. Do not turn a source-minus-recovery process into a new `(1-x)` saturation driver.

Pressure stays `p'=build(overfill,gz)-p/recoveryTau`, projected onto `[0,1]`. The unused current `build/(build+recovery)` target would change physiology and is not the fix.

Replace the custom-profile per-call respiratory HR multiplication with its bounded modifier applied to the continuous baroreceptor target. Defaults currently make it a no-op (`RespiratoryFatigueHrFloor=1`); explicitly test a 0.8 profile. Preserve negative-G near-stop targets.

### Conservative circulation equations

Use the existing raw drives:

```text
qHead  = -shift + return*(restHead-head) - pressureReturn(head)
qCore  =  shift + return*(restCore-core)
qLower =  shift*coreLowerFraction + lowerReturn*(restLower-lower)
Q      = qHead+qCore+qLower
v_i'   = q_i-v_i*Q
```

This is the infinitesimal form of existing normalization and conserves total blood. Use head/lower as independent coordinates; core is `1-head-lower`. Correcting the finite-step normalization can alter equilibria, hence the behavioral calibration gate.

Use conservative active constraints: zero outward derivative at an active floor/zero-volume bound and distribute the compensating derivative over free compartments in proportion to their available volume. Repeat active-set identification if compensation activates another bound. A release is a real event. Do not normalize gross invalid stages or use endpoint clipping to mask solver error.

HR/cardio/circulation must use contemporaneous stage state. Preserve current baroreceptor and cardio build/rest branches; do not add a physiological deadband to suppress branch chatter. All these physiological rate/constraint computations reside in `PhysiologicalModel`.

### Simultaneous oxygen rates, preserving model scope

Combine existing infinitesimal forward/venous exchange terms from one stage state:

```text
kHead  = 1.8*transportRate*head/restHead
kLower = 1.8*transportRate*lower/restLower
headExchange  = kHead *(coreO2-headO2)
lowerExchange = kLower*(coreO2-lowerO2)
coreExchange  = -head*headExchange-lower*lowerExchange
```

Blood sums to one, matching current total-volume denominators. Combine exchange, consumption, lung refresh, and brain-target relaxation simultaneously, not sequential in-place passes. Effective cerebral delivery uses bounded perfusion shaping/penalty and contemporaneous HR/arterial O2; preserve existing exponents and tau branches.

**Scope distinction:** these formulas preserve the current model's core transfer weighting; they do not conserve physical volume-weighted oxygen content. Adding division by core volume would change the physiological model. Do not add a conservation assertion that contradicts the selected pre-change equations, or silently redesign oxygen transport. Test the selected coupled rates directly; physical oxygen-content conservation is a separate feature. Waiving API compatibility does not add that physiological redesign to this task.

### Fast refresh must be exact, not bare Radau

`LungOxygenationRate` is currently passed as a time constant (default 0.3 s), notwithstanding its name. Preserve that interpretation.

A read-only check of three-stage Radau's stability function at `h=1`, `tau=0.3` gives exact decay 0.0356739933472524 versus Radau decay 0.0420560747663551. For a 0.98-to-0.098 core-O2 relaxation, error is 0.0056289958116486: already above the 0.001 contract. Thus bare Radau on the whole core-O2 equation is explicitly rejected.

Integrate lung refresh/consumption analytically inside the simultaneous O2 block. On each real unclipped effectiveness branch, exact respiratory/compression functions make the known source:

```text
core' = -k*core + a0 + sum_j(a_j*exp(-mu_j*t)) + exchange
k = 1 / lungRefreshTau
```

The no-exchange particular solution is:

```text
P(t) = core0*exp(-k*t) + a0*(1-exp(-k*t))/k
       + sum_j(a_j*(exp(-mu_j*t)-exp(-k*t))/(k-mu_j))
```

Use the `k=mu` limit `a_j*t*exp(-k*t)` and stable small-difference formulas. Localize actual lung-effectiveness clamp crossings; do not freeze a changing target at the beginning/end. The model constructs these sources and selects clamp branches; a helper may only evaluate the numerical expression.

Transform only the core residual:

```text
zCore(t) = exp(k*t)*(core(t)-P(t))
zCore'   = exp(k*t)*coreExchange(t, state)
core(t)  = P(t)+exp(-k*t)*zCore(t)
```

Integrate `headO2`, `lowerO2`, and `zCore` simultaneously; transport sees reconstructed core O2 at each stage. This removes the known fast refresh error without an operator-split pass or smaller timesteps. Isolated refresh must match closed form to roundoff for every supported dt. Extend exact affine treatment to other known constant-target fast lags rather than accepting an avoidable rational-decay error.

### Remaining coupled solver: three-stage Radau IIA

Apply fifth-order, stiffly accurate Radau IIA to unresolved coupled rates/transformed O2 residual. Stage evaluations/Newton iterations do not accept intermediate numerical time steps. `PhysiologicalModel` drives the stages, block solves, and acceptance; shared coefficients and linear arithmetic alone may live in a helper.

```text
Y_i = y0 + h*sum_j(A_ij*F(t0+c_j*h,Y_j))
y1 = Y_3
r = sqrt(6)
c = [(4-r)/10, (4+r)/10, 1]
A = [ (88-7r)/360,    (296-169r)/1800, (-2+3r)/225;
      (296+169r)/1800, (88+7r)/360,     (-2-3r)/225;
      (16-r)/36,      (16+r)/36,       1/9 ]
b = last row of A
```

Solve model dependency blocks at matching nodes:

1. Head/lower blood, HR, cardio fatigue, pressure impairment.
2. Head/lower O2 and transformed core residual, using solved circulation stages and exact independent time functions.
3. Consciousness, using O2/pressure/circulation stages.
4. Lagged visual channels and bounded LoC rate state, with exact constant-target segments where applicable.

No downstream block feeds upstream in the current model. Do not freeze upstream values at endpoints. Use analytic block Jacobians, pivoted small linear solves, damped Newton, at most 12 iterations initially, scaled residual <=`1e-10`. Validate Jacobians away from switching surfaces. Measure actual endpoint/trajectory accuracy against independent references; a small algebraic residual is not a truncation-error estimate.

Allocate call-local buffers once per advance, reuse across iterations, and do not add large owned matrices to instances. No new dependency, unsafe storage, shared mutable physiological state, or profile/solver registry. Same equations on both targets.

**Gate, not a theorem:** L-stability/fifth order do not prove 0.001 agreement, positivity, or accuracy through steep logistic/branch/constraint behavior. Radau has no unconditional positivity guarantee. Exact fast-process treatment, real event localization, active constraints, and measured trajectory errors are mandatory. If this selected method fails, stop; no hidden step halving, solver-order increase, alternative method, or relaxed tolerance without a revised approved plan.

### Real physiological events

`PhysiologicalModel` identifies and applies actual law/mode changes: active bounds/releases; strain/cardio build/rest; depletion/recovery tau changes; sudden trigger/recovery; neck death-level entry/exit and dwell completion; consciousness hysteresis; bounded LoC rate/ceiling intersections; true clamped visual/respiratory target branch boundaries.

Find the earliest crossing using exact roots or reconstructed collocation output. Refine its bracket and re-solve a prefix at the physical crossing if necessary, apply the event once, then advance the remainder under the new law. This is permitted event splitting, not a fixed/adaptive numerical timestep scheme. The model owns this control flow and event policy, not a mathematical helper or the instance wrapper.

- Count only time actually above neck death level.
- Apply sudden drop once at its trigger and evolve the remaining time afterward.
- Death permanently pins consciousness/unconsciousness/final LoC override consistently.
- Reconcile hysteresis after event-induced drops in the same interval; final visuals reflect final flags.
- Handle equality, coincident events, zero-time re-triggers, and bound releases deterministically.
- Do not invent a split at the pressure logistic midpoint merely to improve resolution; that is not a law/constraint change.
- Cardio branch chatter is a specific stop risk. Do not add a new deadband, fake equilibrium, or ignore crossings. A required sliding-mode/mechanism change needs a separate decision.
- Nonconvergence cannot return a clamped approximation, discard remaining time, or invoke numerical substeps.

### Wrapper and safe stabilization

Remove regular dt-list/LINQ subdivision. Each supported caller interval is one model advance, apart from actual physical events. Prefer the same whole-interval method above one second as best effort; retain existing large-dt performance verification without a new accuracy claim.

Keep useful wrapper responsibilities: time/last-force bookkeeping, `(Gx,Gy,Gz)` physical axis semantics, nonpositive-dt logging policy, reset/apply integration, logger precedence, and separate-instance concurrency. Constructors/signatures/virtual/protected hooks need not be preserved for legacy compatibility; simplify them only where this implementation warrants it and adapt in-repository users. General NaN/infinity/settings-domain validation is out of scope.

Stabilization:

- Wrapper checks complete-state change, schedules elapsed-time dwell, and decides whether to call the model. `PhysiologicalModel` computes physiological equilibrium/remaining-excursion bounds and pending-event eligibility.
- Expose a model-owned internal predicate such as `CanStabilize(gx, gy, gz, maximumStateError)` for the wrapper. It evaluates current physiology without advancing persistent state; the wrapper must not duplicate physiological rates, target calculations, or equilibrium solving.
- Integrate the last interval before freezing, or localize eligibility; do not drop elapsed time when dwell expires.
- Dwell uses seconds and existing `StabilizationTimeThreshold`, not calls.
- Reserve <=0.00025 state error for freezing. Use analytic remaining excursions for independent processes and a checked same-mode equilibrium/attraction bound for coupled state. A small per-call derivative is insufficient.
- Never freeze growing fatigue, slow oxygen/pressure recovery, death dwell, pending sudden trigger, or nonstationary cardio oscillation. If a bound is unavailable, continue integration.
- Existing load-stability deadline/zone tests still must pass; inability to meet both safe caching and those assertions is a reported blocker, not permission to freeze incorrectly.
- Preserve wake on forces/settings and state retention after wake.
- Replace the settings-test zero-dwell fixture with a genuine neutral-load equilibrium: equal resting O2 pools, zero consumption, zero pressure build, neutral +1 Gz. Keep ownership/wake/reset assertions intact.

## Implementation checklist and verification gates

### 0. Repository delivery — current request only

- [ ] After approval to leave planning mode, save this full document as `.agents/plans/issue-22-dt-stability.md`.
- [ ] Verify it matches the approved session artifact and is the only repository change for the plan-only request.
- [ ] Stop unless implementation is separately requested.

### 1. Freeze legacy evidence; write failing tests

- [ ] Before production changes, capture original dT failures, timings/brackets/classifications, and frozen snapshots.
- [ ] Add `DtLegacyBaselineTests.cs` with measured pre-change constant-load, existing duration-schedule, sudden/death/no-event results at 250 ms. Store commit, exact profile/schedule/prehistory, threshold, time origin, horizon and observation cadence.
- [ ] Keep the already-frozen CI report table above separate; never relabel it as constant-load or regenerate it for presentation.
- [ ] Add representative all-state/identical-history tests that fail on the old model; reuse existing capture records.
- [ ] Keep old 100 ms expected snapshot data for explicit comparison; no rebaseline yet.

Verification: reproduce known divergence and new supported-range failures before production edits.

### 2. Mandatory numerical/performance feasibility prototype

- [ ] Implement model-owned trial state/rates, exact affine/strain/refresh solutions, conservative circulation, simultaneous O2, and selected Radau residual orchestration inside `PhysiologicalModel`.
- [ ] Extract only reusable numerical arithmetic to `NumericalMath.cs`; it must not own a physiological state, settings interpretation, event loop, or model-wide advance.
- [ ] Add internal mathematical/invariant/Jacobian/event tests through the model-owned interval seam; do not permanently switch production updates until evidence is reviewed.
- [ ] Exercise 10/100/250/500/1000 ms on rested and preconditioned sentinels, including custom respiratory HR floor, fast isolated core refresh, stiff negative G and multiaxial load.
- [ ] Measure all-state trajectory spread, localized events, accepted bounds, solver residuals/reference error, and complete Release update cost on existing and multiaxial workloads.
- [ ] Identify expected legacy timing differences separately from numerical error.

**Hard gate:** <=0.001 state spread, <=0.05 s event spread, correct invariants, no numerical substeps, unchanged performance budgets. If it fails, stop with scenario/profile/dt/channel/time, error/residual/convergence/constraint/event evidence and measured cost. Do not proceed to bulk migration or snapshot replacement.

#### Implementation progress: stopped at the numerical gate

Implementation was separately authorized on 2026-10-04. Work is on `fix/22-dt-instability`, based on the investigated commit. This is a partial implementation, not a completed timestep-stability fix.

- [x] Captured pre-change experimental failures, existing duration/stabilization tests, original 100 ms snapshots, and 12 constant-vector behavioral baselines with provenance.
- [x] Added frozen constant-vector guards in `DtLegacyBaselineTests.cs`; all 12 reproduce the original model. Duration-schedule output is retained in TRX; its additional frozen guard implementation remains pending.
- [x] Added representative public all-state reproductions in `ConstantLoadDtStabilityTests.cs`; all 12 load cases fail on the original model. No thresholds or snapshots were relaxed.
- [x] Added a circulation-only model-owned trial seam, conservative blood rates, pressure/HR/cardio rates, analytic Jacobian, and three-stage Radau IIA Newton solve. Numerical helpers contain arithmetic only; internal visibility supports tests.
- [x] Evaluated the first numerical feasibility increment and stopped on failure before downstream migration.
- [ ] Complete exact independent processes, simultaneous oxygen, physiological constraints/events, safe stabilization, calibration, dense coverage, and CI promotion. These remain blocked; the prototype is not connected to production `Update`.

The decisive case is rested `LogicSettings.Default`, `(Gx,Gy,Gz)=(0,0,-3)`, at elapsed time 1 s. Each path uses its nominated dt uninterrupted, with 100/10/4/2/1 calls for 10/100/250/500/1000 ms. All solves converge, every stage passes the implemented bounds, and no mode crossings are detected.

| Quantity | Measured value |
| --- | ---: |
| Resting-relative head overfill, 10 ms | 0.026636297887043525 |
| Resting-relative head overfill, 1000 ms | 0.0249876182644361 |
| Cross-partition overfill spread | 0.001648679622607424 |
| Allowed spread | 0.001 |
| 1000 ms final scaled residual | 3.8129059881594874e-17 |
| 1000 ms Newton iterations | 9 |
| 10 ms versus 20 ms overfill difference | 3.191891195797325e-14 |
| 10 ms versus 20 ms HR difference | 5.502265310042276e-12 |
| 10 ms versus 20 ms pressure difference | 5.912429617074832e-11 |

Thus the selected circulation prototype violates the endpoint accuracy contract even with a converged nonlinear solve, before downstream physiology or event handling can repair it. This is evidence against this selected formulation, not proof that every equation-only formulation is infeasible.

At -5 Gz, the 1000 ms trial also fails Newton convergence after 12 iterations (scaled residual 0.009657036529427544). First-stage pressure is negative at 250/500/1000 ms, including -0.005125188147494896 at 1000 ms. These paths are explicitly marked invalid; their endpoint spreads are diagnostics, not accepted-solution accuracy measurements. Numerical collocation undershoot is not evidence of a real physiological boundary crossing and does not authorize a fabricated event split. The -2 Gz case and all three Jacobian finite-difference cases pass.

Final verification:

- [x] Narrow integration checks: 4 pass, 2 fail; the numerical gate remains failed.
- [x] Existing normal regressions plus frozen legacy guards: 117/117 pass, using an explicit filter excluding the newly failing constant-load and integration tests as well as Experimental/Performance. This is not a passing default gate.
- [x] Original Release performance checks: 3/3 pass. Complete production updates average 0.0033 ms / maximum 0.0363 ms for default and 0.0031 ms / maximum 0.0313 ms for the existing custom profile.
- [x] Release solution build: both library targets, GUI, and tests; zero warnings/errors. Whitespace/style/analyzers and GPL-header verification pass.
- [ ] Complete prototype/whole-model Release performance and allocation gate. A discarded Debug prototype benchmark used invalid states and cannot establish this requirement; its historical log is retained but its test was removed. Linear-solve buffer cloning remains an unfinished prototype allocation detail.

Evidence is retained under `%TEMP%\issue22-baseline\`, especially `trx\final-circulation.trx`, `trx\final-normal-regression.trx`, `trx\final-performance-release.trx`, `trx\constant-reproducer.trx`, `trx\experimental-baseline.trx`, and `trx\duration-stability-settings.trx`. Original captures were not regenerated for presentation. Production `Update`, wrapper scheduling, settings defaults, frozen snapshot expectations, and workflows are unchanged. New reproductions intentionally remain blocking failures. No commits or pushes were made. A revised approved numerical decision is required before continuing.

#### Follow-up: blood-return coefficient screening

The user subsequently requested an attempt to adjust blood movement. A scoped experiment tested `(HeadPressureReturnRate, HeadPressureReturnExponent)` pairs `(0.0625,11)`, `(0.125,11)`, `(0.1875,11)`, `(0.375,11)`, `(0.5,11)`, `(1,11)`, `(2,11)`, `(4,11)`, `(0.5,5.5)`, and `(0.125,22)`. All other numeric settings were unchanged, with stabilization disabled. No overfill clamp, tolerance relaxation, numerical substeps, or production-default changes were introduced.

Each profile ran rested circulation at -2/-3/-5 Gz to exactly 1 s using nominated dt 10/20/100/250/500/1000 ms: 180 paths total. The 20 ms path is a separate fine-reference check, not part of the five-candidate spread. Each profile also ran all 12 frozen constant-vector scenarios for 300 s at 250 ms: 120 behavioral paths. The latter use unmigrated production `Update` and are screening evidence only, not proof of corrected-model timing preservation.

Increasing the return rate from 0.25 to 0.5 reduces -3 Gz overfill spread from 0.001648679622607424 to 0.0008584933612622303, with valid solves for that case. However, that profile removes the frozen -3 Gz unconsciousness event within the 300 s behavioral horizon (original onset bracket `(19.75,20]`); its -5 Gz 1000 ms solve also fails convergence. Rates 1/2/4 likewise pass the -3 Gz overfill spread but fail the same legacy event classification and retain -5 Gz nonconvergence. The other tested pairs fail the -3 Gz spread; some also have invalid stages or shifted event times.

None of these ten coefficient adjustments is acceptable under the joint numerical/behavioral guards. This finite experiment does not prove that every blood-flow equation change is infeasible. No candidate was retained. The temporary probe was removed, and the previously reviewed source remained unchanged. Raw invariant-culture endpoint, residual, stage, spread, reference, and event rows are retained in `%TEMP%\issue22-flow-adjustment\numerical-rows.txt`, `behavior-rows.txt`, and `flow-probe.trx`. Continuing requires a scoped equation/formulation decision rather than treating reduced impairment as a numerical fix.

#### User-selected blood-flow equation experiment

After the coefficient screen, the user selected `Revise blood-flow equations`, retaining the accuracy, timing, conservation, and no-substep requirements. A scalar check of a simple smooth maximum-flow limiter still exceeded the error budget; that limiter was not implemented. The implemented experiment instead changes head-reservoir mobility in the isolated circulation seam. This is a physiological-law hypothesis, not a confirmed correction of a units bug, and is not a maximum-flux cap.

For the existing normalized raw rates `fHead`, `fCore`, and `fLower`, and `m = settings.RestingBloodHead`, the new interior-domain equations are:

```text
d = (1-m)*fHead
gHead  = m*fHead
gCore  = fCore  + d*core /(1-head)
gLower = fLower + d*lower/(1-head)
```

The slowed head-flow difference is redistributed proportionally among the other compartments. For `0<m<1` and `head<1`, total blood and head-flow direction are preserved, and the original interior equilibrium set is unchanged. Transient kinetics change; neither constrained equilibria nor legacy event timings have been validated for this revised law. The analytic Jacobian applies the same transformation. No defaults, new parameters, overfill clamp, solver coefficients/order/tolerance, predictor, or numerical substeps were changed.

At matched elapsed time 1 s, across the unchanged nominated 10/100/250/500/1000 ms paths:

| Revised-law measurement | Value |
| --- | ---: |
| -3 Gz head-overfill spread, all paths valid | 0.0000869662194502574 |
| -5 Gz head-overfill endpoint spread | 0.0004114407284090682 |
| -5 Gz 1000 ms Newton iterations | 5 |
| -5 Gz 1000 ms final scaled residual | 1.8074833783580816e-15 |
| -5 Gz pressure-impairment spread | 0.0029340389091312633 |
| -5 Gz 1000 ms first-stage pressure impairment | -0.001157052534677214 |

The -5 Gz 1000 ms full path remains invalid because of pressure-stage undershoot; its head endpoint must not be presented as proof of an accepted all-state interval. The -2/-3 Gz numerical cases pass, all three finite-difference Jacobian checks pass, and three independent-rate conservation/sign checks plus neutral-rate verification pass. Overall narrow integration tests: 9 pass, 1 fail. Fine-reference comparisons (10 versus 20 ms) retain the unchanged 1e-6 limits. Both library targets build in Release with zero warnings/errors; formatting/analyzers and GPL-header checks pass.

The new law is retained only in the prototype, not production `Update`. The remaining pressure error prevents a full feasibility pass or production migration. Revised-law legacy timing, full trajectory/history/profile/event coverage, and complete-update performance remain unverified. Evidence: `%TEMP%\issue22-head-mobility\head-mobility-final.trx` and `head-mobility-final-console.log`. Earlier evidence is historical and has not been overwritten; no commits or pushes were made.

#### Pressure interval revision and calibration order (2026-10-05)

The user authorized revising pressure equations and requested physiological parameter calibration only after all stability tests pass. Keep the frozen legacy measurements, event classifications/order, timing margins, supported dt range, state/event tolerances, no-substep rule, and performance budgets unchanged. A pressure-only prototype pass is not authorization to claim the complete model stable or begin calibration.

Inspection confirms pressure impairment does not feed back into the prototype blood/HR/cardio equations. First preserve the existing physiological pressure law `p'=B(head,gz)-k*p` and replace its bare collocation integration with model-owned exponential convolution along the same interval's cubic collocation blood trajectory. Solve only the four unresolved circulation coordinates; retain the pressure rate/Jacobian for mathematical checks. A fixed positive 16-node Gauss-Legendre convolution rule evaluates the source without advancing or committing intermediate physiological states. It is quadrature, not numerical time stepping. Normalize the exponential integration measure so constant-source/recovery limits are exact, including zero/small recovery rates. Add reusable scalar arithmetic only to `NumericalMath`.

Localize actual pressure upper-bound entry/release, not the logistic midpoint. Unconstrained pressure is nonnegative from the positive convolution; saturation at 1 must be a projected-law event, never endpoint clipping. Search bound events using the cubic head trajectory's extrema and roots of `B=k`; those are root-search brackets, not accepted artificial time splits. Retain blood/HR/cardio validity diagnostics; pressure treatment must not conceal their failures.

A lead-authored independent arithmetic screen (`%TEMP%\issue22-pressure-screen.py`) reproduces the existing first-second blood/HR endpoints and estimates pressure spread 0.0007224036059884989 at -5 Gz using direct exponential convolution, versus the previous 0.0029340389091312633. Estimated -2/-3 pressure spreads are 0.0000003638630909699736 / 0.000003883828780627124. These are formulation-screen results, not passing C# tests, full-trajectory guarantees, or calibrated behavior. Implement and verify the C# seam next, then exercise identical histories, awkward/dense dt, bounds/events, custom profiles, and performance before any production activation. If further gates fail, report their evidence without changing thresholds or beginning calibration.

- [x] Implement isolated pressure convolution, projected-bound events, and focused mathematical tests.
- [x] Verify all prototype channels on representative and dense first-second paths plus identical preconditioned load/recovery histories.

Pressure verification now includes constant-source/zero-source/fast-recovery/zero-recovery limits, projected upper entry and release, coincident stage crossings, equality regimes, independent dynamic-release/re-entry references, analytic Jacobians, and actual opt-in dense skip reporting. The first-second dense gate executed 991 integer-ms candidates for each of -2/-3/-5 Gz, including genuine 997 ms calls. Consolidated focused result: 196 pass, 1 fail (197 total). The sole failure is the new 30 s circulation-history gate at -5 Gz; -2/-3 complete all representative paths/checkpoints. Pressure spread at the original -5 Gz first-second gate is approximately 0.000722404. Neither production activation nor parameter calibration has occurred.

The extended -5 Gz trajectory reaches the empty-core boundary even at 10 ms: the first rejected 10 ms call ends at 10.08 s with core -0.00007517380128096995 and converged residual approximately 2.79e-17. The 1 s path first rejects its interval ending at 11 s with core -0.024412771288075397. All three pressure, blood, and HR endpoint channels agree within their unchanged limits at common valid checkpoints through 10 s, but missing valid coverage after that is a blocking failure, not a passing history test. Evidence: `%TEMP%\issue22-pressure-consolidated\trx\consolidated.trx` and `consolidated-console.log`.

The next implemented step was the already-planned physical empty-core active constraint and localized entry/release in the prototype. It sets outward core rate to zero at the active bound and redistributes its compensation among free head/lower compartments in proportion to available volume above their floors, retaining tangent conservation and an analytic transformed Jacobian. Explicit core-bound coordinates (`lower=1-head`) hold the core exactly at zero. Entry is localized against a re-solved free prefix; only actual bound events split an interval. Event trial solves never commit invalid stages or persistent state. This completes the empty-core portion of the approved constraint/event mechanism, not a new numerical-step fallback. Head/lower bound and other mode-event coverage remain incomplete until separately exercised.

After core-bound implementation, all three 30 s negative-G sentinel histories (-2/-3/-5) pass their representative checkpoint/invariant checks. A second dense window uses identical 10 s negative-G preconditioning and then exercises all 991 integer-ms candidates through empty-core entry, with genuine 997 ms calls. Entry/release, constrained-rate/Jacobian, accepted interval coverage, and rejected-initial-state tests pass. Cross-cadence core-entry spread in the 30 s -5 history is approximately 0.000083 s, below 0.05 s. Whole-interval `Converged` now requires complete time coverage and no blocking diagnostics; a partial or invalid result cannot claim successful advancement. Accepted Newton residual/iteration diagnostics exclude rejected event probes. Evidence: `%TEMP%\issue22-core-bound\trx\core-bound.trx`, followed by the reviewed final source and `%TEMP%\issue22-core-review\trx\core-review.trx`.

#### Broader feasibility gate: blocked again (2026-10-05)

Expanding rested first-second coverage without changing tolerances exposes three genuine accuracy failures. All compared paths converge, have valid accepted states, and complete the interval; the fine 10/20 ms reference check remains unchanged.

| Input `(Gx,Gy,Gz)` | Channel at 1 s | Minimum / candidate dt | Maximum / candidate dt | Spread / limit |
| --- | --- | --- | --- | --- |
| `(0,0,-6)` | Pressure impairment | 0.21396541094050786 / 0.5 s | 0.2210941056045163 / 1 s | 0.00712869466400845 / 0.001 |
| `(2,1,-5)` | Pressure impairment | 0.09589262047315879 / 0.01 s | 0.09857136243819681 / 1 s | 0.00267874196503802 / 0.001 |
| `(0,0,-8)` | Resting-relative head overfill | 0.10620269677310917 / 1 s | 0.10866260392306162 / 0.01 s | 0.0024599071499524505 / 0.001 |

The broader -1/-4 sentinels and fixed identical preconditioned respiratory-HR-floor 0.8 case pass. Focused Debug verification with dense execution: 208 passed, 3 failed, 1 intentionally skipped Release-only timing screen, 212 total. These are prototype results, not a passing full-model stability suite. The exact contribution of blood dense-reconstruction error versus pressure quadrature error has not yet been discriminated; do not label either alone the confirmed numerical cause.

A warmed Release circulation-only timing screen now measures rested, core-held, core-entry, and within-interval core-release cases (256 warmups and 128 samples each, no GC during sampling). Core-release mean is 1.3185234375000006 ms and maximum is 2.1852 ms, exceeding the unchanged every-complete-update maximum of 0.5 ms before downstream physiology is included. Repeated held-prefix re-solves remain a clear cost hotspot in the current code. Rested/core-held/core-entry maxima are 0.1847/0.1811/0.4975 ms. This is not the original complete-update workload and does not establish its 0.1 ms average; complete-update average performance remains unverified. An earlier timing receipt incorrectly asserted 0.1 ms independently for each repeated scenario and aborted before core release; its result is historical diagnostic-only, not a valid original-budget failure. The corrected receipt executes all four cases and checks the unchanged 0.5 ms maximum.

Release library builds for net481/net10.0 report zero warnings/errors; formatting/analyzers and the actual GPL header script pass (Git Bash verified at `D:\programms\Git\bin\bash.exe`). The last change only corrects the timing receipt; its Release test and formatting checks were rerun. Raw final evidence and concise command summary: `%TEMP%\issue22-core-review\SUMMARY.txt`, `trx\core-review.trx`, and `trx\timing-screen.trx`.

The mandatory accuracy/performance gate is therefore still failed. Production `Update`, wrapper scheduling, settings defaults, snapshots, and CI remain unchanged; nothing is committed or pushed. Physiological calibration is deliberately not started, because the user's prerequisite of all stability tests passing is not met. A further approved numerical/formulation decision is required before continuing bulk migration or replacing snapshots; no hidden substeps, tolerance changes, or reduced legacy margins have been introduced.
- [ ] Review feasibility and complete the remaining model/wrapper/event integration only when established.
- [ ] Pass complete representative/history/dense stability coverage before selecting calibration parameters.
- [ ] Calibrate minimally against frozen legacy event/no-event behavior within `max(0.5 s, 2%)`, then rerun stability and regression/performance gates.

#### Authorized event-cost and trajectory prototype follow-up

The user authorized trying cheaper core release and alternative numerical formulations while retaining existing limits. Production activation and parameter calibration remain deferred. Lead-authored parsing of the existing complete 30 s history receipt (`%TEMP%\issue22-core-review\trx\core-review.trx`, SHA-256 `cffd63c2c6449981aacbb06fd673298567e84e73fce5e7140541422f6940683b`) verifies every expected call for each of the five cadences (0.01/0.1/0.25/0.5/1 s) and all three loads (-2/-3/-5). Each -5 path has one core entry at 10.077102792 to 10.077186050 s and zero releases through 30 s; -2/-3 have no entries or releases. There are no within-interval releases in those fifteen histories. This frequency describes the isolated prototype, not production or all Gz profiles. The expensive release timing fixture is deliberately preconditioned with cardiovascular fatigue and heart rate just below the refill threshold.

- [x] Separate blood-only event probes from pressure completion in `PhysiologicalModel`: solve and validate blood/HR/cardio for discovery and root trials, then reconstruct and validate pressure once for each accepted segment. Do not accept a provisional blood-only segment as a complete physiological interval.
- [x] Refine held release using the dense heart-rate guess and a sign-bracketed safeguarded secant/Illinois search instead of midpoint-only iteration. Preserve the 1e-8 s bracket tolerance, negative-drive accepted prefix, full coverage, conservation, equality/instant-release behavior, unsupported-mode diagnostics, and trial non-mutation. Use actual re-solved prefix drive values for sign tests; invalid trials are failures, not root evidence.
- [x] Execute and review original entry/held/release and pressure mathematical tests, all existing representative/dense/history checks, and the unchanged warmed Release four-scenario timing screen. Add release near-start/near-end and partition-composition regressions using the existing state/event tolerances. Record the three retained broad accuracy failures without removing or weakening them; this execution is not a complete feasibility pass.
- [x] Screen higher-order whole-interval blood/head trajectories using lead-authored arithmetic and independently checked Radau collocation/quadrature identities. Keep physiological rate laws/defaults, 16-node pressure convolution, no-substep rule, and all gates unchanged; report rejected candidates as such.
- [ ] Select and implement an acceptable trajectory revision; the screened candidates below are not adopted.

The implemented event-cost revision preserves three-stage Radau and the cubic trajectory. Pressure reconstruction and pressure-bound validation happen only for accepted segments; the four-coordinate kernel also skips the unused fifth pressure rate/Jacobian row. Newton elimination uses a new in-place helper on solver-owned buffers while interpolation retains the input-preserving helper. Illinois refinement retains actual sign evidence, a saved nonpositive positive-duration prefix, and the 1e-8 s time bracket; a near-start root cannot be replaced with an offset-zero release. Regression cases include HR offsets 0.001, 1e-6 and 1e-12 below the refill threshold, near-end release, five caller cadences, clipped references, conservation, pressure validity and segment coverage.

Timing receipts are cumulative evidence, not selected reruns: the initial event-cost implementation measured release mean/max 0.242959375/0.4811 ms; after the near-start guard fix, two unchanged receipts failed (release maxima 0.5948/0.5775 ms; one core-held maximum 1.2806 ms). Their mechanism is unproven; do not dismiss these as confirmed scheduler noise. After removing discarded pressure work and Newton copies, one unchanged warmed Release receipt passes: rested mean/max 0.0524265625/0.0912 ms, core-held 0.0405078125/0.0895 ms, core-entry 0.17823203125/0.2774 ms, core-release 0.1793890625/0.3211 ms. All are circulation/pressure-only prototype costs, not complete-update performance guarantees. Frozen receipts: `%TEMP%\issue22-event-cost\trx`, `%TEMP%\issue22-root-guard\trx`, `%TEMP%\issue22-mech-opt\trx`. Narrow verification: 190/190 pass; both Release library targets and touched-file whitespace/style/analyzers pass.

The final retained-source gate (`%TEMP%\issue22-final-gate\trx\final-gate.trx`) reports 224 passed, 3 failed, 1 Release-only skip, 228 total. All twelve frozen legacy tests pass. Lead-authored receipt audit verifies all 991 accepted integer-ms candidates for each of -2/-3/-5 first-second paths and all 991 accepted empty-core-window candidates; the dense cases genuinely executed. The same three broad numerical spreads remain bit-for-bit unchanged. Header enforcement and `git diff --check` pass; existing Markdown configuration excludes this plan from linting, so no plan-file lint pass is claimed. No source change occurred during this final gate, and the frozen post-mechanical-optimization timing receipt was not rerun. Overall numerical feasibility, production activation and calibration remain blocked on the unchanged accuracy failures and incomplete full-model coverage.

Lead-authored independent screens (`%TEMP%\issue22-whole-interval-screen.py`, `issue22-radau4-dense-screen.py`, `issue22-derivative-extension-screen.py`) test unchanged rate laws with whole-interval collocation. The four-stage/quartic candidate reduces representative -6 pressure spread to about 0.000514, mixed-axis pressure to 0.000149, and -8 head-overfill to 0.000175, but expanded 991-candidate -8 coverage produces pressure spread 0.0010641855454466276 (minimum at 0.721 s cadence), above 0.001. Five-stage representative -8 pressure spread is 0.0012613953813681578. Four-stage start-derivative and start/end-derivative matched extensions worsen representative -6/-8 pressure spread (about 0.00374/0.00738 and 0.00264/0.00601 respectively). These are rejected arithmetic candidates, not passing C# tests or a complete trajectory/invariant proof. No trajectory change, physiological retuning, production activation, hidden substeps or gate relaxation is retained.

### 3. Complete physiological/event migration

- [ ] Complete `PhysiologicalModel.Update` and its model-owned interval/control/value methods; reset new state/modes/events correctly. Consolidate or remove obsolete fields/hooks if useful, without compatibility shims.
- [ ] Migrate every evolving path; remove only replaced helpers/variables made unused, not unrelated code.
- [ ] Implement actual bound/threshold events, partial death dwell, one-shot sudden LoC, permanent death and coherent final visuals in the model.
- [ ] Derive outputs and log accepted events once; no stage logs/callbacks/live mutation.
- [ ] Adapt affected in-repository callers/capture access directly if a scoped breaking change is made; retain numerical and behavioral assertions.
- [ ] Update only numerical comments made inaccurate.

Verification: internal math, representative all-state, visual/recovery/sudden/death and raw-model/wrapper checks. Verify helper files contain mathematical computation only, not relocated physiology.

### 4. Whole-interval scheduling and stabilization

- [ ] Remove dt subdivision/allocation; implement complete-state change/dwell scheduling in the wrapper and physiological eligibility/error-bound calculation in the model.
- [ ] Preserve useful time/forces/wake/reset/logging/concurrency semantics, without keeping obsolete APIs solely for compatibility.
- [ ] Update genuine-equilibrium settings fixture without weakening assertions.
- [ ] Add cached-versus-running pairs through 1800 s and later load/profile changes.

Verification: load stability, settings, logging/concurrency, memory budgets and history checks. No freeze may hide divergence; wrapper must not contain duplicated physiological computations.

### 5. Constrained calibration and reviewed snapshots

- [ ] Compare corrected behavior on frozen legacy schedules/cadences against timing tables and unchanged duration bounds.
- [ ] Fix wrong units, weights, missing mechanisms, event timing and solver error before considering calibration.
- [ ] Adjust only minimum justified existing rates/defaults; evaluate all axis, mixed, negative-G, sudden/death and recovery guards jointly.
- [ ] Explicitly check Gy 10/11 G death/no-death boundary: removing pre-recovery clipping otherwise changes classification.
- [ ] Use the planned existing settings/mechanisms, and no caller-dt-dependent constants or G-specific fitted bands.
- [ ] Report every old/new calibrated value, rationale, affected timing/classification and state spread.
- [ ] After independent gates pass, regenerate 100 ms snapshots; review every changed channel against old data, then update `Expected` deliberately. Identical-run deterministic assertions remain exact.

Verification: legacy drift <=`max(0.5 s,2%)`, unchanged bounds/classifications pass, all numerical gates still pass. Calibration never substitutes for a convergence fix; waived API compatibility does not waive timing preservation.

### 6. Local representative and opt-in dense suites

- [ ] Extend dT infrastructure with constant-load definitions, actual-duration schedules and streaming all-state maxima; preserve existing ramp records/report thresholds.
- [ ] Add `ConstantLoadDtStabilityTests.cs` and `DenseDtStabilityTests.cs`.
- [ ] Dense facts use a custom xUnit fact attribute that explicitly skips unless `GEFFECTS_DENSE_DT_TESTS=1`. Use existing pinned xUnit 2.9.3, no new package.
- [ ] Representative category is `DtStability`; dense is `DtDense`. Remove `Experimental` from the three supported-range classes after they pass.
- [ ] Keep strict constant-input results separate from historical varying-input ramp/report metrics; existing varying-input tests still pass their original limits.
- [ ] Cache summary results only in tests, keyed by profile/prehistory/vector/schedule/mode. Stream comparisons; retain maxima/channel/time, final state and event brackets rather than millions of full snapshots.
- [ ] Assert all 991 dense candidates/endpoints execute; do not hide missing coverage behind checkpoint adjustment.

Verification: default local dense cases explicitly skip, representative failures block; opted-in dense runs full coverage.

### 7. Blocking CI and scoped documentation

- [ ] Keep `test.needs: build-artifacts` and blocking Release performance checks.
- [ ] Promoted representative/long tests run in normal Debug gate.
- [ ] Replace non-blocking experimental dT step with blocking dense step, env `GEFFECTS_DENSE_DT_TESTS: "1"`, filter `Category=DtDense`.
- [ ] Verify dense cases executed, not skipped, in CI discovery/execution.
- [ ] Add warmed multiaxial performance workload at identical budgets without changing original workload/distribution/default/custom allocation tests.
- [ ] Update affected `Logic.md`, `README.md`, and `AGENTS.md` integration/cache/test commands/status, including model-owned physiological control and any scoped API changes. Leave separate desired timing targets/unrelated stale prose alone.
- [ ] Preserve final numeric/calibration evidence in tests/output and plan progress, not an unrelated narrative document.

Verification: workflow/YAML/Markdown, both library targets, GUI solution build, normal/representative/dense tests, Release performance, license headers.

## Precise test matrix

### Duration definitions and fair sampling

Default representative values: existing 16 (10,20,25,40,50,62.5,100,approximately 111.111,125,approximately 142.857,approximately 166.667,200,250,approximately 333.333,500,1000 ms), plus 73,127,257,499,501,733,997 ms. Arbitrary durations must not be rounded into integer `StepsPerSecond`.

Dense: integer 10–1000 ms inclusive, 991 candidates, short sentinels. CI also executes long representative cases; do not multiply all 1800-second cases by 991 unnecessarily.

Variable schedules: repeating `[10,1000,73,257] ms`, `[499,501,20,733,127] ms`, reversed schedules and seeded deterministic in-range partitions.

**Preserve nominated cadence:** reciprocal-integer cases have natural common one-second boundaries. Other fixed-dt candidates run their actual nominated steps uninterrupted; compare every natural candidate endpoint with a fine reference advanced to that same time using only supported durations. Candidate-versus-reference tolerance <=0.0005 reserves the 0.001 cross-candidate budget. Validate reference convergence with the existing finer representative values and independent closed-form cases; do not silently choose a divergent 10 ms result as truth.

For awkward/dense fixed-dt runs, common terminal checkpoints are 10/30 s (and long-case endpoints). Adjust only the last one/two candidate steps to hit the terminal endpoint, with every duration in range and most updates retaining nominated dt. Run shorter 1/2/5 s aligned-checkpoint tests separately where needed; do not alter the main cadence at every second. In particular, 997 ms must actually be exercised, not replaced with two 500 ms calls. No tiny out-of-range remainder or floating-point end-loop overshoot.

Record candidate-reference and actual common-endpoint cross-candidate errors distinctly. Do not claim all natural sample times are the same across dt. A fine reference is a convergence oracle for the new model; the old 250 ms baseline is a separate behavioral timing oracle.

### Loads and horizons

Vectors use `(Gx,Gy,Gz)`.

| Group | Cases | Horizon/purpose |
| --- | --- | --- |
| Gz | Existing no-LoC zones `-2,-1,0,1,2,3` and LoC zones `-6,-5,-4,-3,4,5,6,7,8,9` | 300 s all-state; extend no-LoC zones and +4 to 1800 s. |
| Negative sensitivity | `(0,0,-2.5)`, `(0,0,-2.75)`, `(0,0,-3.5)` | 300 s; steep pressure logistic/equilibrium/HR, baseline classification without invented timing target. |
| Gx | `(±1.5,0,0)`, `(±5,0,0)`, `(8,0,0)`, `(12,0,0)`, `(13,0,0)`, `(±15,0,0)`, `(±20,0,0)` | 300 s; arterial O2, respiration, sudden trigger versus public LoC, sign symmetry. |
| Gy | `(0,±1.5,1)`, `(0,2,1)`, `(0,±4,1)`, `(0,3.5,0)`, `(0,4,0)`, `(0,5,0)`, `(0,10,0)`, `(0,11,0)`, `(0,20,0)` | 180 s; tolerance/compression/pain/sudden/neck/death boundaries. |
| Mixed | `(2,1,5)`, `(4,1,5)`, `(2,2,4)`, `(13,1.5,5)`, `(2,0,-4)`, `(2,0.5,-4)`, `(10,2.5,0)`, `(8,2,0)`; selected Gx/Gy sign mirrors | 300 s; competing tolerance/respiration/circulation, sudden boundary. |
| Dense sentinels | `(0,0,1)`, `(0,0,5)`, `(0,0,-2)`, `(0,0,-3)`, `(0,0,-5)`, `(15,0,0)`, `(0,11,0)`, `(2,1,5)` | Every natural endpoint and 10 s terminal, plus 30 s for respiration/strain; 991 nominal dt. |

All-state checks continue after an event to the defined horizon; event-only early termination must not hide later divergence.

### Identical prehistory

Each candidate replays the same 250 ms preparation and checks equal initial snapshots before its test cadence; do not independently ramp at candidate dt.

1. `(0,0,5)` 60 s, then constant positive/mixed load 180 s: fatigue.
2. `(0,0,-5)` 5 s, then `(0,0,5)` 120 s: push-pull; compare qualitatively with rested control without an invented target.
3. `(15,0,0)` 120 s, then `(0,0,1)` 180 s: oxygen recovery.
4. `(0,0,-5)` 10 s, then `(0,0,1)` 180 s: pressure/redout recovery.
5. `(10,2.5,0)` 30 s, then `(0,0,1)` 120 s: sudden-mode recovery.
6. `(0,20,0)` 30 s, then `(0,0,1)` 120 s: permanent death/override.
7. Rest/load/recovery >=1800 s, cached/continuously-integrated pair, later force/profile switch.

Profiles: full default matrix; focused sentinel/history coverage on shared/custom suit 0.25, no suit, existing valid custom resting/pressure-helper profile, and respiratory HR floor 0.8. Focused 10/250/1000 ms plus awkward dt on coherent half/double strain/baroreceptor/O2/visual time constants. Test live apply/reset between plateaus. These are meaningful samples, not a promise that every unvalidated combination of 122 arbitrary doubles is meaningful.

### Mathematical tests

- [ ] Affine closed form/composition, zero-source/rate, instantaneous tau and small-argument accuracy.
- [ ] Exact strain integrals, real build/rest transitions and caps.
- [ ] Source/recovery equilibrium and accumulator bounds.
- [ ] Isolated fast core refresh at one second matches exact solution; equal-exponent convolution limit and changing source/clamp branches.
- [ ] Radau coefficients/stability function, independently solved linear reference and smooth nonlinear fifth-order convergence; transformed O2 reference comparison through the model-owned seam.
- [ ] Analytic Jacobians versus finite differences away from switches; separate residual and actual truncation-error evidence.
- [ ] Blood tangent conservation, conservative floors/zero-volume entry/release and admissible stages.
- [ ] Coupled O2 rates match selected pre-change infinitesimal equations; isolated consumption/refresh tested separately, without an incorrect physical oxygen-content conservation claim.
- [ ] Exact-boundary/coincident events, no duplicate sudden drop, partial death dwell, mode consistency, full elapsed-time consumption.
- [ ] Accepted-interval instrumentation proves each split has a crossed named physical boundary. Stage/Newton evaluation is not a time subdivision.
- [ ] Identical profiles/runs deterministic, separate-instance parallel versus serial equality.

## Files to modify/create

### Production

- `GEffectsLogic/PhysiologicalModel.cs`: actual physiological state and trial/result types; interval control flow; rates/targets/Jacobians; block/stage/Newton orchestration; event/constraint handling; reset, accepted-state commit, derived values, and physiological stabilization eligibility. Existing field/API layout may be simplified without compatibility shims.
- `GEffectsLogic/GEffectsLogicInstance.cs`: retain wrapper role; whole dt, no list subdivision, time/forces/settings/logging integration and safe stabilization scheduling. No physiological equation/target/solver control flow should be moved here.
- `GEffectsLogic/LogicSettings.cs`: only justified existing default calibration if needed; no new properties planned.
- New `GEffectsLogic/NumericalMath.cs`: reusable mathematical expressions, Radau coefficients, and small linear-system arithmetic only. No physiological state, `LogicSettings`, model-wide advance, domain rates/targets, or event/acceptance policy.
- New `GEffectsLogic/Properties/AssemblyInfo.cs`: internal test visibility and GPL header.

Do not create the previously proposed `GEffectsLogic/PhysiologicalIntegration.cs` physiological service. Its planned domain responsibilities now belong to `PhysiologicalModel`.

### Tests

- `DtStabilitySimulationResults.cs`: preserve old ramp results; shared constant-load/schedule/streaming summary/dense attribute.
- `GLoCDtStabilityTests.cs`, `NonGLoCDtStabilityTests.cs`, `DtStabilityReportTests.cs`: promote existing checks; preserve thresholds; separate strict constant-input diagnostics.
- `SettingsRegressionTests.cs`: reuse captures, reviewed expected rebaseline only after gates; adapt direct access for any scoped API change.
- `InstanceSettingsTests.cs`: genuine equilibrium fixture, apply/reset/cache regression; no assertion requiring obsolete hooks or field visibility to survive.
- `GLoadStabilityTests.cs`: add full-state/cache comparisons, keep existing zones/deadline.
- `LogicInstancePerformanceTests.cs`: multiaxial workload, same budgets/original cases.
- New `PhysiologicalIntegrationTests.cs`, `ConstantLoadDtStabilityTests.cs`, `DenseDtStabilityTests.cs`, `DtLegacyBaselineTests.cs`: model-owned math/integration, representative, opt-in dense and frozen behavioral timing guards respectively. The integration test filename does not imply a separate production integration service.

All test paths are under `GEffectLogicTests/`; new C# files use canonical GPL headers. Reuse existing test infrastructure; do not create a competing snapshot system.

### CI/documentation/delivery

- `.github/workflows/build.yml`: blocking dense/representative coverage with existing artifact/performance ordering.
- `GEffectsLogic/Logic.md`, `README.md`, `AGENTS.md`: affected numerical/cache/test commands/status and model-owned architecture only; preserve separate physiological targets. Document scoped API changes rather than supporting old APIs.
- `.agents/plans/issue-22-dt-stability.md`: requested plan/progress.
- Affected existing GUI/test call sites: mechanical adaptations only if scoped API changes require them; no GUI feature or sequence behavior changes.

Out of scope: legacy compatibility adapters, a second physiological integration service, GUI feature/sequence interpolation, public solver/event APIs, profile registry, general validation, package/framework/security-policy changes, unrelated logging/formatting cleanup, physical oxygen-transport redesign, release/version changes, commits/pushes/PR creation.

## Verification commands

Run separately from the repository root under .NET 10 after implementation authorization. Use narrow development checks and one final full gate. These commands are planned, not executed during planning.

### Baseline and narrow iteration

```text
dotnet restore GEffectsLogic.slnx
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~GLoCDurationTests|FullyQualifiedName~GLoadStabilityTests|FullyQualifiedName~SettingsRegressionTests"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category=Experimental"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~PhysiologicalIntegrationTests"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category=DtStability|FullyQualifiedName~DtLegacyBaselineTests"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~InstanceSettingsTests|FullyQualifiedName~LoggingAndInstanceConcurrencyTests|FullyQualifiedName~GLoadStabilityTests"
```

Experimental command is pre-promotion baseline and expected to fail; preserve evidence rather than treating it as a passing prerequisite.

### Default local final gate

```text
dotnet format whitespace GEffectsLogic.slnx --verify-no-changes
dotnet format style GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet format analyzers GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet build GEffectsLogic.slnx -c Release -warnaserror
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category!=Experimental&Category!=Performance&Category!=DtDense"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Release --filter "Category=Performance"
bash scripts/enforce-license-headers.sh --check
```

Verify Git for Windows Bash location if PowerShell cannot resolve `bash`; do not assume installation path. Ordinary unfiltered local `dotnet test` also skips dense facts explicitly unless opted in.

### Manual dense run

Save/restore any previous environment value in PowerShell; set before test discovery:

```powershell
$previousDenseMode = $env:GEFFECTS_DENSE_DT_TESTS
$env:GEFFECTS_DENSE_DT_TESTS = '1'
```

Run separately:

```text
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category=DtDense"
```

Then restore:

```powershell
$env:GEFFECTS_DENSE_DT_TESTS = $previousDenseMode
```

CI sets the same switch in step env and uses the same filter with `--no-build` after Debug build. Normal blocking step already executes representative long cases.

### Other verification

- [ ] Follow existing lint pins: `yamllint==1.38.0`, `actionlint@v1.7.12`, `markdownlint-cli2@0.23.2`, relevant existing JSON/XML checks.
- [ ] Do not weaken lint configuration. Plans under `.agents/plans/**` are currently ignored by Markdown config; separately inspect fences/tables/paths or force a plan-only lint.
- [ ] Confirm both framework assemblies and GUI build, including direct adaptations to any scoped API changes.
- [ ] Review full diff for model-owned physiological control flow/value calculation, math-only helpers, no unnecessary compatibility scaffolding, unreviewed defaults/snapshots, removed scenarios/weakened thresholds, numerical substeps and unrelated churn.

## Risks / mandatory stop conditions

1. Timestep-independent equilibrium is not preservation of the old equilibrium. Negative-G evidence makes this material; minimal approved calibration may be needed. Failure of joint timing/classification guards is a blocker, even though API compatibility is not required.
2. Even after exact fast refresh, implicit high-order integration can miss steep pressure transients and violate positivity. Accepted bounds, actual reference error and trajectory tests are required, not residual alone.
3. Cardio switches/constraints can require a sliding-mode or mechanism change. No hidden deadband/chatter suppression/step halving; report a reproducible conflict for a new decision.
4. Safe caching may conflict with existing 3600 s stabilization expectations. Do not falsely freeze to pass; report the conflict.
5. Coarse event observation can conceal drift or create false failures. Keep raw/refined brackets, force schedule, and discontinuity window separate.
6. Snapshot changes can conceal a regression. Freeze old evidence and pass independent numerical/timing guards before rebaselining.
7. Custom HR damping and Gy ceilings encode call-order semantics; verify targeted profile/boundary behavior, not defaults alone.
8. Performance/memory are hard requirements, including complete multiaxial updates. Do not hide cost in solver-only microbenchmarks or large owned workspaces.
9. Dense tests must actually execute every nominated dt in CI and remain opt-in locally. Cadence/checkpoint adjustments must not erase coverage.
10. Differently sampled acceleration ramps remain different inputs. No interpolation/invented force history; strict equivalence applies to identical constant loads and identically prepared/scheduled histories. Existing ramp tests retain their limits.
11. A math-helper extraction must not recreate a separate physiological engine. Physiological state, rates, target/value computations, integration/Newton/event orchestration, and accepted-state policy remain in `PhysiologicalModel`.

## Completion evidence

- [ ] Repository plan saved/reviewed; no model implementation for the current plan-only request.
- [ ] Original failures, legacy behavioral timings/classifications/snapshots frozen with provenance.
- [ ] Equation-only prototype passes hard gate or stops with complete feasibility report.
- [ ] Physiological computation/control remains model-owned, helpers are math-only, and no obsolete API is retained solely for compatibility.
- [ ] Every public/hidden state, invariant, event class, approved profile/history and real timestep schedule covered.
- [ ] Representative default local and opt-in dense tests pass; CI runs both as blocking checks.
- [ ] Legacy drift/classifications/unchanged bounds pass; each calibrated value explained.
- [ ] Snapshot changes independently verified/reviewed.
- [ ] Framework/GUI build, formatting/analyzers/license, workflow/Markdown and unchanged performance/allocation budgets pass.
- [ ] Final implementation report states worst channel/time spread, event-versus-observation delay, legacy timing drift, calibration, measured cost and unresolved limitations; no unsupported-range accuracy claim.

## Calibration authorization and narrow accuracy exception (2026-10-06)

- [x] User authorized proceeding with legacy-behavior recalibration while deferring the three known first-second case/channel accuracy failures: pressure impairment at `(0,0,-6)` and `(2,1,-5)`, and head overfill at `(0,0,-8)`.
- [x] Retain unchanged accuracy assertions as `Experimental` diagnostics in the existing non-blocking CI step. The blocking broad gate still checks those loads for physical validity, convergence, conservation, reference agreement, and every other channel.
- [ ] Calibrate the new formulation against frozen legacy behavior within `max(0.5 s, 2%)`; production currently remains the legacy path, so the calibration execution path must first be settled.

This narrow exception supersedes the previous requirement to stop calibration for these three accuracy failures only. It does not constitute an accuracy fix, a full-model feasibility pass, authorization to relax other tolerances, or a complete-update performance guarantee. Frozen baselines, event/no-event classifications and ordering, the no-numerical-substep rule, and existing performance budgets remain unchanged. Historical failing receipts and rejected trajectories remain retained.

## Local-branch migration and pre-tuning stop

The user clarified that this branch must completely replace the legacy public update calculations before merging into `main`, and requested continuation of migration followed by a pause for their input at the tuning phase. Here, the public/library execution path means this local branch, not `main` or a deployed release. Do not retain an optional legacy fallback. This supersedes the earlier activation deferral, not the numerical acceptance requirements.

- [ ] Hold the starting cardio build/recovery law during each trial segment, use exact fatigue recovery, and localize the real HR-threshold transition before accepting its prefix. Keep strict accepted-stage validity checks.
- [ ] Complete model-owned interval integration for oxygen, consciousness, visuals, independent accumulators, death dwell and sudden-LoC events.
- [ ] Use a full three-compartment affine exponential base with three-node residual collocation for the oxygen block; preserve the existing simultaneous transport weights and target laws. Matrix exponentials and convolution moments are arithmetic, not physiological substeps. Constant-coefficient blocks must match their closed form; changing coefficients remain subject to the unchanged accuracy gate.
- [ ] Replace the public update body and remove routine wrapper subdivision. Commit only fully accepted interval state; failed trials must not fall back to legacy equations or discard elapsed time silently.
- [ ] Permit stabilization only with model-owned remaining-excursion/equilibrium evidence; otherwise continue integrating and report any deadline conflict.
- [ ] Verify the untuned migration and report numerical failures separately from legacy timing, classification and snapshot differences.
- [ ] Stop before changing physiological defaults/rates or snapshot expectations. Obtain the user's input before tuning. The subsequent checkpoint request below authorizes a WIP commit, not a push or merge.

## Resume checkpoint: interrupted full migration, before tuning

This is the latest status and supersedes older statements that the public update path is still legacy. The user requested a portable progress/WIP/TODO checkpoint and a commit on the current branch so work can continue on another computer. Implementation was stopped during review rework; do not interpret this checkpoint as a completed or merge-ready fix.

### User intent and authority

- Implement Issue #22 on `fix/22-dt-instability`; `main` remains untouched until the branch is ready.
- The local public `GEffectsLogicInstance.Update` / `PhysiologicalModel.Update` path must use the new interval calculations, not retain an optional old engine.
- Continue numerical migration and verification, then STOP at parameter tuning and ask the user for input. Do not recalibrate defaults/rates or replace snapshots before that input.
- Commit the current WIP checkpoint as requested. No push, merge, release, or PR creation was requested.
- Keep exactly the three authorized first-second accuracy diagnostics non-blocking. No additional waivers, relaxed state/event tolerances, fake events, or hidden numerical substeps.

### Completed and retained

- [x] Original 250 ms behavior is frozen with provenance in `DtLegacyBaselineTests`; original settings snapshots and duration assertions are unchanged.
- [x] Representative all-state timestep reproductions and circulation/invariant/event tests exist.
- [x] Circulation uses conservative three-compartment rates, head-reservoir mobility, three-stage Radau/Newton, active blood constraints, and physical bound/mode events. Pressure uses exponential convolution over accepted blood trajectories.
- [x] The three original accuracy failures remain strict executable `Experimental` diagnostics with a detailed explanation; the blocking broad gate keeps all other checks.
- [x] The HR=1.8 fatigue transition blocker was fixed and tested before the full migration: hold the starting cardio build/recovery/ceiling law, use exact exponential recovery, localize the threshold, then solve the remainder. Natural modes still detect crossings. The localization NaN-upper-bound bug was also fixed.
- [x] Public `PhysiologicalModel.Update` now captures full state, calls full `AdvanceInterval`, rejects invalid results, commits accepted state, and logs accepted transitions. The old sequential update body, backward-Euler lag, and old head-blood helper are removed.
- [x] Wrapper routine 0.5 s subdivision/dt-list is removed; one model call receives each positive caller interval.
- [x] Full trial state and downstream oxygen/consciousness/visual/independent-accumulator/event orchestration have been written. This is implementation presence, NOT numerical verification.
- [x] Review rework began replacing the artificial `750/span` lag treatment with algebraic instantaneous constraints and replacing derivative-times-dwell stabilization with a conservative neutral certificate.
- [x] No physiological defaults, frozen baselines, snapshot expectations, or CI workflow were changed. No tuning has started.

### WIP state and actual latest build

The interrupted rework is intentionally preserved. It does NOT compile currently.

Executed at this checkpoint using .NET SDK `10.0.401`:

```text
dotnet build GEffectsLogic/GEffectsLogic.csproj -c Release --no-restore
```

Result: FAILED, 24 compiler errors, zero warnings across `net481` and `net10.0`.

Concrete compiler failures to fix first (line numbers are checkpoint locations and will shift):

1. `NumericalMath.cs` approximately 244–381 and `PhysiologicalModel.cs` approximately 3547/3940/3975/4039 use `double.IsFinite`, unavailable on `net481`. Use shared-framework finite arithmetic (`!IsNaN && !IsInfinity`), retaining identical equations.
2. `PhysiologicalModel.cs` approximately 388 references nonexistent `cardioFatigue`; the persistent field is `hrFatigue`.
3. `ScanCrossing` approximately 3996/4000/4007 captures its `out direction` parameter in a local function. Use a local direction accumulator and assign the out parameter outside the captured function.
4. `ComputeHold` approximately 4506 still calls the old signature. New signature is `(IntegrationState state, Func<double, IntervalTrialState> trialAt, double span, double gxMagnitude, LogicSettings settings)`. Pass a trajectory callback anchored at the current cursor, not the old single `trial0` sample.
5. The earlier CS8156 error at `CommitIntervalState(in result.Final, ...)` has already been fixed with a local `final`; do not redo that fix.

No full-migration tests have executed successfully after activation. Do not run old binaries with `--no-build` and label their results current. No process owned by this work was running when the interruption checkpoint was reported; no server needs restarting.

### Last verified evidence, strictly historical

- Immediately before full migration: `PhysiologicalIntegrationTests`, excluding Experimental/Performance with dense off: 259 passed, zero failed, two dense tests skipped. Receipt was `%TEMP%/issue22-cardio-fix/cardio-fix-narrow.trx` on the original computer. It does not validate the new full model.
- Earlier expanded circulation rework: 267 passed, three positive/mixed-load failures; the subsequent cardio fix resolves those three.
- Latest pre-migration circulation timing receipt still exceeded 0.5 ms: rested mean/max 0.0895/0.1635 ms; core-held 0.0785/0.1438; core-entry 0.2919/1.4838; core-release 0.3691/0.7011. The older passing 0.321 ms release maximum is not current evidence.
- Complete migrated-update performance is unmeasured. Do not infer compliance from circulation-only measurements.
- Deferred strict diagnostics originally measured: `(0,0,-6)` pressure spread 0.00712869466400845; `(2,1,-5)` pressure 0.00267874196503802; `(0,0,-8)` resting-relative overfill 0.0024599071499524505. They remain genuine errors, not a numerical fix or justification for new waivers.
- Temporary TRX, Python screens, logs, and prior conversation summaries are machine-local, not part of this checkpoint. The source, mathematical design, frozen baseline constants, and resume requirements here are the portable authority. Rerun checks on the next computer; historical receipts cannot certify its current checkout.

### Settled full-model design to finish, not redesign silently

`PhysiologicalModel` owns state, physiological targets/rates, block sequencing, constraints, events, trial acceptance, reset, derived outputs, and logging. `NumericalMath` contains only arithmetic. There is one authoritative persistent state; trials are pure snapshots.

- `AdvanceCirculationInterval` is the circulation mathematical seam. Full `AdvanceInterval` consumes its accepted blood segments, then downstream physiology at contemporaneous dense blood/HR and exact independent time functions. Downstream has no upstream feedback; reuse blood trajectories for downstream root queries instead of repeatedly solving circulation.
- Independent strain/AGSM/suit/respiratory laws use the existing exact evaluator. Preserve source-minus-recovery laws and actual ceilings; never clamp the equilibrium before evolving a trajectory.
- Compression, arterial oxygenation, pain, sudden source/recovery, and neck source/recovery use exact constant-load functions. Count only actual neck time above the death level; death permanently pins consciousness/unconsciousness/final LoC, including hidden completed dwell. Apply sudden drop once at its localized trigger and evolve the remaining interval.
- Oxygen uses a full 3x3 affine exponential base plus three-node residual collocation, not core-only transformed Radau or sequential transport passes. Order is `[headO2, lowerO2, coreO2]`. With contemporaneous transport `r`, `kH=1.8*r*head/restHead`, `kL=1.8*r*lower/restLower`, lung `k=1/LungOxygenationRate`, and current brain target/tau:

```text
M = [ -kH-1/tau,  0,   kH;
       0,       -kL,   kL;
       head*kH, lower*kL, -k-head*kH-lower*kL ]
b = [ -consumeHead+brainTarget/tau,
      -consumeLower,
       k*restCoreO2*lungEffectiveness-consumeCore ]
```

These are the approved legacy infinitesimal exchange weights. Do NOT divide by core volume or claim physical oxygen-content conservation. LungOxygenationRate is a time constant despite its name. Perfusion is capped at one before delivery; head overfill cannot become extra oxygen perfusion.

For interval length `h`, base `M0,b0`, `A=h*M0`, Radau nodes `c_i`, and quadratic Lagrange basis coefficients `basis[j,m]`:

```text
J_m(A,c) = integral_0^c exp((c-s)*A)*s^m ds
W_ij = sum_m basis[j,m]*J_m(A,c_i)
p_i = y0 + h*J_0(A,c_i)*(M0*y0+b0)
Y_i = p_i + h*sum_j W_ij*((M_j-M0)*Y_j + b_j-b0)
```

Solve the linear stage block and validate the original residual. Dense evaluation uses the same convolution expression, not an unvalidated cubic fit of fast oxygen. Scalar consciousness/visual lags use the one-dimensional equivalent, exact for constant coefficients; hold actual loss/recovery modes until a localized real crossing. Math-only exponential scaling/squaring is not accepted physiological stepping.

Instantaneous lag/oxygen rows must be exact algebraic targets at nodes/dense queries, with a physical zero-time limit jump before free rows are solved. NEVER recreate `750/span` or any caller-dt-dependent artificial time constant. Current `AffineSystemSpec`/`AffineSolution` instantaneous callbacks and row overrides are partly implemented and need completion/testing.

### Outstanding review rework (resume in this order)

- [ ] Fix the concrete compilation failures above, then build both frameworks without changing targets or packages.
- [ ] Finish the changed `ComputeHold` call and remove its obsolete `trial0` variable. Review right-side equality selection for oxygen, consciousness and lag buildup/recovery; derivative probes are trial arithmetic, not accepted substeps. The new `RightProbeFraction=1e-4` implementation is unverified.
- [ ] Validate the full initial state before any advance. `DownstreamViolation` still has unauthorized blanket `1e-9` bound tolerances and omits actual blood/cardio bound checks. Keep raw stages/endpoints/dense extrema finite, physically admissible, conserved to 1e-12; reject rather than blanket-clamp or relax checks.
- [ ] Reconstruct accepted dense held blood bounds on their exact manifold with measured roundoff correction and residual checks. `CoefficientStart` records the original blood polynomial origin when downstream segments are shorter.
- [ ] Finish event payloads with explicit coordinate/level/direction. Current `ApplyEvent` still snaps oxygen to the nearest bound without checking correction and uses fixed `1e-6` threshold snaps. Require actual root-slope/time-tolerance correction checks; do not hide invalid states.
- [ ] Refine events against RE-SOLVED valid prefix endpoint residuals, not just roots on the discarded whole-interval trial. Invalid trials cannot supply fabricated sign evidence. Confirm bracket width 1e-8 s and complete elapsed-time consumption.
- [ ] Include events at the exact endpoint: current `Offer` uses `t < end`. Apply all same-time events deterministically; death suppresses sudden drop and pins final state. Record consciousness onset induced by sudden/death once, without duplicate wrapper logs. Tangencies/zero plateaus must not create fake mode splits.
- [ ] Emit `DownstreamSolution.InitialEvents` for algebraic instantaneous limits. `RecordPiece` does not yet emit them. Store piece start/span where event residual verification needs them.
- [ ] Invoke `IntervalUpstream.PinDwell()` when already dead or death occurs. The helper is added but not called yet. Hidden death dwell must remain one after unloading, not reset to zero.
- [ ] Finish safe stabilization: wrapper still passes 0.025; use at most 0.00025. The rewritten neutral-only certificate is unverified; oxygen nonexpansive bounds do not automatically bound nonlinear downstream target excursions. Return false when no certificate exists. Do not restore derivative-times-dwell caching to pass deadline tests.
- [ ] Remove obsolete stabilized subset fields/assignments made unused by this migration. Update `InstanceSettingsTests.CreateStableInstance` to genuine neutral equilibrium: equal resting O2=1, zero consumption and pressure sources, +1 Gz, zero dwell. Keep all state retention/settings/wake/reset assertions.
- [ ] Review new `NumericalMath` finite/nonconvergence/overflow guards and scalar/matrix moment limits, including extent zero, NaN entries and both-framework support. Both moment helpers currently reject extent zero, but downstream setup queries `Oxygen.Evaluate(0)`; finite channels then yield NaN instead of initial state. Fix dense start evaluation before interpreting any runtime failure. Stable affine base and dense/upstream caching were partly added; validate rather than assume finished.
- [ ] Capture the visual-loop channel index per iteration before storing instantaneous target callbacks; retained callbacks must not all observe the final loop index and turn instantaneous redout into the hypoperfusion target. Keep instantaneous derivative queries within the accepted interval and propagate invalid derivatives instead of treating them as zero.
- [ ] Check actual blood-segment/pressure-boundary processing. Whole speculative solves may span future events but accepted prefixes must stop at actual physical boundaries and preserve trajectory origins.
- [ ] Check permanent death, correct sudden trigger/recovery direction, fractional dwell, endpoint/equality/coincident events, and visual rate/ceiling contact semantics. Current new tests cover only a small subset of the intended cases.

### Test additions/fixes still required

- [ ] Matrix/scalar affine closed form and composition; scalar/matrix moments for zero/diagonal inputs and instantaneous/finite lags.
- [ ] Isolated fast core refresh for tau 0.3/0.15/0.6 and dt 0.01/0.1/0.25/0.5/1 matches closed form within 1e-12; zero exchange/consumption profile isolates the law.
- [ ] Simultaneous oxygen derivative/Jacobian checks preserve selected weights; no incorrect volume-weighted conservation assertion.
- [ ] Strengthen the sudden fixture: exactly one trigger/drop, compare whole versus 100x0.01 full endpoint, not an OR allowing unrelated dead/unconscious flags. Add recovery/no-recovery direction cases.
- [ ] Fractional neck death dwell, exact endpoint completion, death coincident with sudden trigger, and permanent hidden dwell/LoC after unload.
- [ ] LoC ceiling entry/exit/rate bounds, hysteresis equality, independent tunnel/redout overlap and no unconscious override of physiological tunnel.
- [ ] Full rejected-update atomicity and no trial logs; direct-model/wrapper full state equality for 1 s and 997 ms, with exactly one model call.
- [ ] `RunFullPath` currently constructs a default model despite supplied settings; construct with the selected profile.
- [ ] Awkward schedule must assert every actual dt in [0.01,1], nominated dt genuinely occurs, and exact total duration. Avoid floating-point tiny tails. Compare true pairwise min/max across all candidates, not only each versus 250 ms (which could allow 0.002 pairwise spread). Include flags/events outside localized event windows.
- [ ] Focused custom respiratory-HR floor 0.8 and half/double supported time constants; identical preconditioning/recovery and cache-versus-running histories.
- [ ] Complete representative and 991-candidate dense full-model checks, existing regressions, formatting/analyzers, GUI/both-framework build, license checks, and complete-update/per-event performance. Keep all current budgets/assertions.

### Suggested next commands after finishing compile/review fixes

Run separately from repository root with .NET 10. Restore only if the new computer needs it. Do not repeatedly rerun the full suite during narrow development.

```text
dotnet restore GEffectsLogic.slnx
dotnet build GEffectsLogic/GEffectsLogic.csproj -c Release --no-restore
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~FullInterval"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~ConstantLoadDtStabilityTests"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~InstanceSettingsTests|FullyQualifiedName~LoggingAndInstanceConcurrencyTests"
```

Then run the existing full/Release/dense verification commands earlier in this plan once the narrow gate is credible. Experimental diagnostics remain separately reported. Root `AGENTS.md` and `Logic.md` remain mandatory reading; their descriptive references to the old wrapper subdivision have not yet been updated.

### Expected untuned behavior is not numerical evidence

Removing caller-order clipping changes neck dynamics: current Gy=10 ceiling is 0.953125, above the 0.95 death threshold. Exact projection may therefore change the frozen Gy10 no-death classification; retain the guard and report it rather than adding a 250 ms reference clip or tuning now. Other frozen timing/duration/snapshot differences are possible. They must be distinguished from convergence, physical validity, trajectory accuracy, event ordering and cost failures. No recalibration or snapshot replacement is authorized at this checkpoint.

### Transfer to the other computer

The requested commit is local unless separately pushed/transferred. On the next computer, start from the checkpoint on `fix/22-dt-instability`, read this latest section first, fix the compilation/review TODOs, and then verify. No branch creation, reset, history rewrite, compatibility fallback, or parameter fitting is needed to resume. If the checkpoint is not on the remote, it must be explicitly pushed or otherwise transferred before the other computer can obtain it.