---
agent: devin-local
session: lemon-paper
created: 2026-10-03T16:50:05Z
---
# Issue #8: immutable per-instance settings profiles

Use immutable record-class profiles for per-instance physiological parameters, keep logging flags global, and support independent parallel creation without any profile registry while preserving default physiological behavior.

## Summary

Implement issue [#8](https://github.com/AMPW-german/GEffectsLogic/issues/8), currently titled "Per instance settings" with no body/comments. The requirements below come from the user discussion. This document records only the chosen solution; alternatives were compared in chat.

Convert `LogicSettings.cs` into a non-static, immutable record class containing the 122 numeric model parameters. Keep the two logging flags as static, mutable global properties on that same type. Every `GEffectsLogicInstance` holds its selected profile by reference, and all numeric model settings resolve through that instance.

Common physiological defaults are caller-owned, with one fixed built-in `LogicSettings.Default` available as a convenience. Changing a caller's common profile does not change existing instances. There is no library-owned list, registry, interning table, cache, or pool of custom profiles or instances.

This is a settings/ownership refactor, not physiological tuning. Document future long-term condition attributes as a separate layer, without implementing modifier APIs or effects.

## Approval and artifact boundary

- [x] Research the issue, skills, architecture, settings reads, GUI consumers, verification infrastructure, and language/framework requirements without changing repository files.
- [x] Resolve ownership, runtime application, GUI scope, validation scope, global logging, compatibility, parallel creation, and intended scale with the user.
- [x] Obtain approval to publish this chosen plan as `.agents/plans/issue-8-per-instance-settings.md`. The session planning artifact is `C:\Users\AMPW\.devin\plans\plan-c83579aba2a47ff3.md`.
- [x] Following publication approval, create only the requested repository plan artifact and verify it. Do not implement the planned code until the user separately requests implementation.

## Confirmed requirements and constraints

- Backwards compatibility can be completely ignored. The only external consumer is another project owned by the user, and main already contains breaking changes. Do not add compatibility overloads, old static numeric facades, obsolete members, or migration infrastructure solely to preserve old callers.
- Maintaining the two current framework targets is still required: `net481` and `net10.0`. Framework support is distinct from backwards API compatibility.
- Use immutable profiles. Live inheritance from changing global numeric defaults is not important and is deliberately absent.
- `DebugMode` and `SuppresInfoLogs` remain global static flags, not per-instance settings, and retain their current names and behavior.
- Parallel construction of many independent instances must not require shared mutable profile bookkeeping. No registry, deduplication, interning, custom-profile list, or library instance tracking is permitted.
- A fixed immutable built-in default is allowed. Callers may explicitly pass the same immutable profile reference to multiple instances, but the library never records or manages such sharing.
- Normal game use is below 100 instances; expected upper usage is approximately 1,000–10,000. The client's theoretical design guideline is 100,000 instances, not a required library test workload. Test creation/update bursts use 4,096 live instances. Do not impose a library instance-count limit.
- Memory optimization is not a high priority at this scale. Prefer simple full profiles to sparse storage machinery. Measure allocations and retain reasonable gates rather than assuming heap allocation is a defect.
- Explicit runtime profile replacement preserves accumulated physiological state. Reset is a separate action.
- Provide common and per-instance GUI draft editors with explicit Apply. Common numeric-default edits affect new simulations and future GLoC plot runs, not existing simulations. Global logging controls apply immediately to all logging, independently of numeric profile application.
- Implement only settings. Document the future attributes boundary; do not add exposure tracking, microgravity deconditioning, recovery, modifier stacking, or arbitrary physiological coefficients.
- Preserve current default numeric values, equations, integration order, force profiles, physiological timing assertions, and visual-channel semantics. Do not reconcile existing timing/documentation disagreements in this issue.
- Defer full numeric parameter-domain validation. Preserve library acceptance of parameter values; reject null replacement profiles and malformed/non-finite GUI numeric text.
- Keep existing logger selection behavior and axis ordering: instance `Update(dt, gx, gy, gz)` delegates to model `Update(dt, gx, gy, gz)`.
- No new package dependency is needed. Preserve existing comments, GPL headers, and style; avoid unrelated refactoring.
- No commits, pushes, releases, version bump, CI-policy changes, or changes in the external client repository are requested.

## Verified current architecture

- `GEffectsLogic/LogicSettings.cs` contains 122 public static doubles and 2 public static boolean logging flags. Its current numeric literals are the calibration source to preserve.
- `GEffectsLogicInstance` accepts an optional logger and constructs its model through the protected virtual `SetPhysiologicalModel()` hook. It reads the global numeric stabilization threshold during updates.
- `PhysiologicalModel` reads numeric globals in field initializers, reset, update, and derived-output calculations. Three private static helpers depend on settings: `GetHeadBloodOverfill`, `PressureImpairmentBuildRate`, and `StepHeadBloodImplicit`.
- The cardiovascular fatigue floor is cached in a field during initialization/reset. It must follow an explicitly applied profile without clearing fatigue.
- The stable fast path returns early when G-forces are unchanged. Applying numeric settings must invalidate this optimization.
- `GEffectsLogicInstance.Reset()` does not currently clear stabilization bookkeeping. The GUI's `SimulationInstanceViewModel.ResetModel()` calls `PhysModel.Reset()` directly. Narrow corrections to these behaviors are required for dependable application/reset.
- GUI reflection currently finds public static settings and writes them with a null target. `LogicSettingEntry` and `SimulationInstanceViewModel` are declared inside `GraphicLogicTest/MainWindow.xaml.cs`.
- `GLoCPlot` owns a separate instance and resets it for repeated calculations; it needs an explicit common-numeric-profile provider after removing global numeric settings.
- Existing test/GUI loggers read the static boolean flags. Those accesses should remain unchanged. GUI startup currently sets `LogicSettings.DebugMode = true`; this remains valid.
- The production project uses `LangVersion=latest`, targets `net481;net10.0`, and has no existing `IsExternalInit` shim. SDK 10.0.401 is installed locally.
- The deterministic allocation test retains 4,096 parameterless instances and gates allocation at 1,024 bytes each. Release update timing is a separate Performance gate. The 4,096-sample workload also covers the new profile allocation cases without changing the per-instance thresholds.
- The GUI is already part of the solution and targets .NET 10. Pure editor view-model logic can be tested without creating controls or starting Avalonia.
- No restore, build, test, or allocation measurement was run during planning. Establish the baseline before implementation; distinguish calculated payload size from measured allocation.

## Selected representation and framework support

### Record class, not record struct

Use `public sealed record class LogicSettings`. This explicitly declares a reference type, equivalent to `public sealed record LogicSettings`.

- A profile is an ordinary managed-heap object. The record modifier generates copying, equality, and formatting methods; it does not use a special buffer or allocate a table per object.
- Each instance stores a reference to its profile. Supplying an existing immutable profile does not clone it.
- A unique full profile stores 122 doubles: 976 bytes of numeric payload, plus object overhead. The two logging flags are static and contribute no per-profile payload. This is a layout calculation, not a measured construction allocation.
- Profile creation or `with` replacement allocates one new profile. Ordinary physiological updates do not construct profiles.
- Do not change to a struct to pursue stack allocation: a struct stored in an existing class instance would live inline inside that heap object, and would duplicate its large payload per entity. It would also introduce large value-copy risks.
- Do not call generated whole-profile equality or hashing in the frame-update path. Profile replacement uses reference identity for its no-op check.

### `init` support on .NET Framework 4.8.1

`init` is a C# 9 language feature compiled by the modern SDK. .NET Framework 4.8.1 lacks the `System.Runtime.CompilerServices.IsExternalInit` metadata marker used to encode init-only setters, but the compiler can use a marker declared inside the project.

- [x] Add `GEffectsLogic/Compatibility/IsExternalInit.cs` with the canonical GPL header and the following target-conditional definition:

```csharp
#if NET481
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
#endif
```

- The class is empty and provides a type identity for a required metadata modifier; it performs no initialization and is never instantiated by this design.
- It compiles into the existing library assembly. No separate runtime assembly or NuGet package is required.
- .NET 10 supplies the marker already, so do not define the shim on that target.
- `init` mutation restrictions are compiler-enforced, not runtime protection against reflection. The GUI must only reflectively populate fresh unpublished candidates.
- Both target assemblies must build with the resulting generated record/init code. Do not infer support solely from .NET 10 tests.

Verified language references: [records](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/record), [init-only metadata encoding](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/init.md), and [framework marker definition](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.isexternalinit?view=net-10.0).

## Public API and ownership contract

### Settings type

- [x] Convert all 122 numeric properties to public instance `get; init;` properties, preserving exact names, types, literal defaults, order, and existing comments.
- [x] Keep `public static bool DebugMode { get; set; }` and `public static bool SuppresInfoLogs { get; set; }` unchanged on the non-static settings type. Static members are legal on a record class.
- [x] Add `public static LogicSettings Default { get; } = new();`, representing the fixed immutable built-in numeric profile.
- [x] Use `LogicSettings.Default with { GSuitEffectiveness = 0.25 }` for individual variation. Unspecified numeric values are copied, not live inherited overrides. Static flags are not copied or represented in profile equality.
- [x] Do not add profile IDs, registration, interning, a static profile collection, automatic sharing, or instance tracking.

### Instance construction/application

Use this surface for convenient logger/profile selection, without promising old API compatibility:

```csharp
public GEffectsLogicInstance(Logger? logger = null, LogicSettings? settings = null)
public LogicSettings Settings { get; private set; }
public void ApplySettings(LogicSettings settings)
```

- Null/omitted optional constructor settings selects the built-in default.
- Assign `Settings` before invoking `SetPhysiologicalModel()` so construction/reset uses the selected profile.
- Store the supplied reference directly. Concurrent callers independently constructing customized records do not coordinate through any library mutable collection.
- Reject null in `ApplySettings` using `ArgumentNullException` before touching state, with APIs supported by both target frameworks.
- Same-reference application is a no-op. Otherwise replace the reference and clear only `stable`, `stableRecorded`, and `stabilizationTime`.
- Do not reset time, last forces, blood volumes, oxygenation, fatigue, heart rate, death status, consciousness, or visual levels during Apply. The next update reads the new numeric profile.
- Derived settings-relative outputs, such as normalized head overfill and the configured fatigue floor, can reflect the new profile immediately.
- Changed resting fractions become new targets/normalization references, without instantly redistributing blood; `Reset()` reinitializes actual compartments from the selected profile.
- Distinct instances support independent concurrent construction/update. Concurrent Apply/Reset/Update on the same instance remains caller-serialized; do not add locks or imply same-instance thread safety.
- Global logging changes do not alter profiles or require waking numeric stabilization. Keep their existing concurrency semantics; arbitrary cross-thread flag changes are not a new synchronization feature in this issue.

```csharp
var individual = new GEffectsLogicInstance(settings: LogicSettings.Default with
{
    GSuitEffectiveness = 0.25,
    BaroreceptorGain = 2.8
});
individual.ApplySettings(individual.Settings with { GSuitEffectiveness = 0.3 });
LogicSettings.DebugMode = true;
```

## Implementation steps

### 1. Establish and freeze the pre-change baseline

- [x] Run existing normal Debug tests and Release performance tests before rewiring numeric settings. Record existing failures rather than modifying their expectations.
- [x] Record warmed parameterless construction allocation using the existing test and preserve output evidence. Current 4,096-sample measurement should be retained as stress test, it's a higher load than later expected which offers valuable insights.
- [x] Add a compact `SettingsRegressionTests` fixture before changing numeric resolution. Capture the old model with `new GEffectsLogicInstance()` and no logger. Freeze reviewed expected numeric snapshots as constants/test data, not values calculated from the implementation under test.
- [x] Use this exact sequence: 40 updates at `dt=0.1, gx=0, gy=0, gz=1`; 50 updates at `dt=0.1, gz=1+4*(i+1)/50` for `i=0..49`; 400 at `dt=0.1, gz=5`; 200 at `dt=0.1, gz=1`; 100 at `dt=0.1, gz=-5`; 200 at `dt=0.1, gz=1`; 800 at `dt=0.1, gx=15, gy=0, gz=0`; 100 at `dt=0.1, gx=0, gy=4, gz=1`. Unspecified `gx`/`gy` are zero.
- [x] Capture one snapshot after each segment: time/last forces, blood fractions, compartment O2, arterial oxygenation, HR, perfusion, consciousness, pressure impairment, exposed straining/suit/cardio/respiratory/neck fatigue, compression, pain, sudden-LoC state, all visual channels, and unconscious/dead/stable flags. Assert booleans exactly and numeric values to absolute `1e-12` on the same target/runtime. Avoid a large per-frame golden file.
- [x] Review the recipe and resulting baseline before accepting expected values; never regenerate them merely to make the refactor pass.

### 2. Introduce numeric profiles and instance routing

- [x] Implement the record-class settings type and target-conditional marker defined above.
- [x] Add instance profile selection/application without introducing a registry or lock. Preserve current logger behavior and the existing virtual model-construction hook as architecture, not a compatibility obligation.
- [x] Add one private stabilization-invalidation helper used by real profile replacement and `Reset()`.
- [x] Make instance `Reset()` clear time/last forces as before, clear stabilization, and invoke `PhysModel.Reset()` without replacing the numeric profile.
- [x] Add a protected read-only settings accessor in `PhysiologicalModel` resolving `logicInstance.Settings` and replace every static numeric parameter read with it, including reset, output getters, and helper calculations.
- [x] Remove numeric-settings-dependent blood field initializers. The existing model constructor assigns `logicInstance` and then invokes `Reset()`; use that initialization path only.
- [x] Replace cached `fatigueHeartRateFloor` with a protected expression-bodied property resolving current `CardioFatigueMaxHrFloor`, retaining the existing local name to minimize mechanical changes. Remove its old initialization/reset assignments. Its public getter must reflect Apply immediately without clearing accumulated fatigue.
- [x] Convert the three settings-dependent private static helpers to instance methods. Keep `Clamp`, `SmoothStep`, and settings-independent stepping helpers static.
- [x] Preserve equations, ordering, constants, normalization/clamps, and existing comments. Do not fix unrelated unused locals, deadbands, or integration concerns.

### 3. Preserve global logging and migrate numeric test consumers

- [x] Leave existing flag accesses in all four test/GUI logging helper files unchanged; do not give loggers a profile parameter or move filtering into the abstract logger.
- [x] Preserve GUI startup's `LogicSettings.DebugMode = true` and existing tests' static logging assignments. They remain valid after changing the containing type to a record class.
- [x] Change numeric expectation calculations in `GLoadStabilityTests` to read the tested instance's `Settings`, including consciousness thresholds and visual rates, without changing expected values or force profiles.
- [x] After migration, search for remaining static numeric reads/writes. Intentional static accesses are only `LogicSettings.Default`, `LogicSettings.DebugMode`, and `LogicSettings.SuppresInfoLogs`, plus ordinary type references.

### 4. Implement the reusable numeric draft editor

Create `GraphicLogicTest/LogicSettingsEditorViewModel.cs` for the common/per-instance numeric editor. Reuse the existing `LogicSettingEntry` class in `MainWindow.xaml.cs`; do not extract unrelated classes.

```csharp
public LogicSettingsEditorViewModel(
    LogicSettings initialSettings,
    Action<LogicSettings> applySettings)
public IReadOnlyList<LogicSettingEntry> Entries { get; }
public string? ErrorText { get; }
public bool Apply()
```

- [x] Reflect once over `BindingFlags.Public | BindingFlags.Instance` numeric properties. Enumerate exactly 122 entries in deterministic declaration/metadata order. Static `Default` and both logging flags are excluded.
- [x] Initialize each `LogicSettingEntry` from its `PropertyInfo` and initial scalar value; store editable text locally. Typing does not mutate any applied profile.
- [x] Parse invariant-culture finite doubles. Keep invalid text visible and reject malformed input, NaN, and both infinities; no new physiological range validation is introduced.
- [x] Validate all entries before publishing anything. On failure, set a useful `ErrorText`, return false, and do not invoke the callback or modify active profiles.
- [x] On success, clone the editor's last applied numeric profile with `with { }`, populate all parsed instance properties reflectively on that fresh unpublished candidate, then invoke the callback exactly once. Update the editor's last-applied reference and clear errors.
- [x] Each subsequent Apply creates another candidate. Never mutate `Default`, a published profile, or static logging flags during numeric Apply.
- [x] Follow current `INotifyPropertyChanged` patterns; no new validation framework/package. Preserve existing conversion helpers unless made unused by these changes.

### 5. Integrate common/per-instance numeric editors and global logging controls

- [x] `SimulationViewModel` owns private `_defaultSettings`, exposes `public LogicSettings DefaultSettings`, and exposes `DefaultsEditor` whose callback replaces only that common reference. Optional initial numeric settings selects the built-in default when omitted.
- [x] Remove the old all-static numeric-entry collection/build path after replacing its consumers. New simulations receive the current common reference; existing simulations do not follow subsequent common edits.
- [x] Forward optional numeric settings through `NamedGEffectsLogicInstance` and `SimulationInstanceViewModel` construction. Each card exposes `SettingsEditor` whose callback calls only its owned `_logic.ApplySettings`.
- [x] Change card `ResetModel()` to `_logic.Reset()`, retaining the applied profile and existing chart/sequence reset behavior.
- [x] Replace the sidebar editor with the numeric common-draft editor, labeled as defaults for new simulations/future plot runs, with Apply/error display. Add a collapsed per-card numeric editor with the same presentation. Do not redesign charts or unrelated layout.
- [x] Add common/per-card Apply event forwarding in `SimulationView.axaml.cs` and handlers in `SimulationViewModel`. Resolve the per-card view model from sender DataContext and invoke only its editor.
- [x] Expose `GlobalDebugMode` and `GlobalSuppressInfoLogs` boolean properties on `SimulationViewModel`, whose getters/setters directly delegate to the two static flags and notify bindings. Keep the underlying `SuppresInfoLogs` spelling unchanged.
- [x] Add two clearly labeled global logging checkboxes in the sidebar, outside numeric profile editors. Checkboxes update static flags immediately; they are neither draft parameters nor part of numeric Apply. No per-card logging control is added.
- [x] Construct `GLoCPlot` with a `Func<LogicSettings>` provider. A default constructor may select the built-in profile for normal view construction, not to preserve backwards compatibility. `MainWindow.RegisterWindowViews()` passes `() => _simulationViewModel.DefaultSettings`.
- [x] Capture the provider once at the start of each plot run and apply that profile before the existing resets/loop. All samples in the run use that profile. Do not alter plot equations, termination logic, or unrelated input-validation behavior.
- [x] Existing simulation resets/restarts preserve selected numeric profiles; common Apply does not replace them. No apply-to-all action or profile synchronization is added.

### 6. Add focused automated tests

Add `InstanceSettingsTests.cs`, `SettingsRegressionTests.cs`, and `LogicSettingsEditorTests.cs` to the existing test project. Add a local project reference to `GraphicLogicTest.csproj` for pure editor tests; no new test package, controls, dispatcher startup, or native rendering is needed.

- [x] Settings shape: exactly 122 public instance numeric properties; the two logging flags are public static booleans; `Default` is a static get-only reference. Preserve current numeric names/literals and static flag initial behavior. Record copies do not carry flags as instance data.
- [x] Default profile: parameterless construction uses the fixed default reference. A customized copy leaves original numeric settings unchanged.
- [x] Initialization/reset: use resting fractions `(0.25, 0.35, 0.40)` and different resting compartment O2 values. Verify both constructor and reset select them, conserve blood sum, and preserve profile identity.
- [x] Isolation: A uses default hydrostatic shift; B uses `HydrostaticShiftRate = 0`. Identical positive-G updates produce different blood-head results; B remains near resting. Compare A after each update to an independent default control so B cannot contaminate it.
- [x] Explicit reference sharing: caller passes one immutable profile to two instances with different forces; their model states diverge independently. Applying a replacement to one does not alter the other or original profile. No production registry is involved.
- [x] Default-copy equivalence: default-reference and value-identical copied-profile instances receive identical inputs; compare all observable physiological/visual values and flags per frame.
- [x] Helper routing: customize resting head fractions and negative-G settings; verify normalized overfill and negative-G paths use the selected profile rather than default numeric values.
- [x] Apply continuity: after nontrivial input, snapshot accumulated state/time, apply changed settings including the fatigue floor, assert accumulated values are unchanged and the configured floor getter changes immediately.
- [x] Stable wake-up: use threshold zero, bound stabilization to 100 updates at `dt=0.1, gz=1`, then apply a profile with threshold 600 and changed resting fractions `(0.25, 0.35, 0.40)`. Assert immediate unstable status and resumed updates at unchanged forces. For exact model comparison, stabilize two identical instances, apply the same profile to both, advance the control's model directly for one `0.1` step and the subject through instance Update, and compare model outputs; compare only model outputs because control time is not advanced by direct model update.
- [x] Stable reset: stabilize with the same bounded helper, reset, verify zero time/last forces and unstable status, then verify unchanged-force updates execute. Do not alter unrelated delta-time handling.
- [x] Null replacement: exception leaves state/profile unchanged. Constructor null selects defaults. Same-reference Apply leaves state/stable status unchanged.
- [x] Concurrent creation burst: use `Parallel.For` to construct exactly 4,096 instances, each with a locally created distinct profile whose `GSuitEffectiveness` is `0.1 + index * 0.0002`. Store results only in a test-owned preallocated array, not a library collection. Assert distinct profile identities and per-index values.
- [x] Independently advance these 4,096 instances in parallel with 200 `0.1`-second updates and `gz=1+(index%8)`; compare complete snapshots to serial controls. Create/control/check one serial control at a time. Replace each finished burst array slot with its final immutable snapshot and release that instance before creating its serial control. Do not change logging globals in this test. This tests distinct-instance concurrency, not concurrent operations on one instance.
- [x] Editor enumeration: 122 numeric entries, no global flags or static Default.
- [x] Editor isolation: typing does not change the input reference or invoke the callback. Apply publishes a new fully populated candidate once; second Apply does not mutate the first published candidate.
- [x] Editor rejection: malformed double, NaN, and positive/negative infinity fail atomically with errors/no callback. Finite out-of-domain numbers remain accepted under deferred domain validation. Assert invariant-culture round-tripping.
- [x] Numeric editor/profile application never changes global logging flags. If tests deliberately alter flags, restore them in `finally` and use the existing non-parallel `Global logger` collection rather than weakening global semantics.
- [x] Run frozen pre-change regression snapshots and existing physiological tests without retuning or changing expectations.

### 7. Measure allocation and the 4,096-instance burst

- [x] Retain the existing 4,096 retained-instance construction sampling for all allocation cases; keep warmup outside the measured region and release the warmup reference before sampling. Keep the existing default-instance threshold of 1,024 bytes per instance.
- [x] Measure custom-profile-plus-instance allocation using 4,096 serial constructions, each creating `LogicSettings.Default with { GSuitEffectiveness = 0.25 }` inside measurement. Allocate retention arrays outside measurement and use the existing per-thread allocation-counter recipe. Customized threshold is 2,048 bytes per instance including its unique profile; actual numbers must be reported.
- [x] Also measure 4,096 constructions with one caller-created custom profile supplied by reference, outside profile creation measurement, to separate model/reference costs from unique-profile costs. This does not introduce a profile registry.
- [x] Retain existing Release frame timing thresholds/workload. Add the same workload using a pre-created custom profile, reusing timing helpers; exclude profile construction and Apply from frame timing.
- [x] For a 4,096-instance parallel construction-burst measurement, warm the exact `Parallel.For` path first and release warmup instances. Preallocate the result array, then measure wall time around `Parallel.For` with local custom-profile construction. Serialize this performance test with the existing non-parallel collection. Report time without inventing an unrequested startup deadline. The deterministic per-instance allocation gate remains the serial test; do not misattribute scheduler/task allocation to profile size or use per-thread counters to total a parallel workload.
- [x] Release burst instances before another 4,096-instance workload; do not add a 100,000-instance stress test or a library count limit.
- [x] If any existing threshold or proposed custom allocation gate fails, investigate actual cost and report evidence. Do not weaken checks silently. Memory is secondary, so request approval for a justified further budget change rather than introducing pooling/registries.

### 8. Update focused existing documentation

- [x] Update README Configuration/Architecture/Performance sections with record profiles, constructor injection, explicit Apply/reset, global flags, no registry, and measured allocation numbers.
- [x] Update relevant Settings Overview/Architecture/Performance sections of `GEffectsLogic/Logic.md`, including the future attributes boundary below. Leave unrelated stale physiology prose and known timing disagreements untouched.
- [x] Update factual architecture guidance in `AGENTS.md`: numeric settings are per-instance immutable profiles; logging flags remain static; the library does not track instances/profiles. Do not change shared behavioral/security/physiological policies.
- [x] Explain the new API for the user's other project, without promising source/binary compatibility or adding compatibility adapters. No changes to that external repository are included.
- [x] Do not change `VersionPrefix`, release/changelog automation, or CI ordering/policies in this issue.

## Future long-term attribute boundary: documentation only

- Baseline numeric profiles represent calibrated/resting parameters, including individual differences.
- Existing fatigue, blood distribution, oxygenation, and impairment remain internally integrated short-term state.
- Future long-term attributes represent caller-owned condition, such as cardiovascular/muscular deconditioning from extended microgravity. They are not baseline-settings overrides and must not rewrite settings.
- A future attribute input/application path must wake stabilization, preserve baseline profiles, and survive reset of short-term physiology unless callers explicitly clear the attributes.
- Neutral attributes must reproduce current behavior. Apply future effects at affected mechanisms, not through a blanket multiplier on final consciousness/visual channels or arbitrary G-force scaling.
- Settle units/ranges, mechanism mapping, accumulation/recovery ownership, composition, time integration, and acceptance tests in a separate feature before implementation. No empty attribute record, modifier dictionary, exposure tracker, registry, or unvalidated weakness coefficient is added here.

## Files to modify

Existing library:

- `GEffectsLogic/LogicSettings.cs`: 122 immutable instance numeric parameters, fixed default, unchanged static logging flags.
- `GEffectsLogic/GEffectsLogicInstance.cs`: profile selection/application and stabilization reset.
- `GEffectsLogic/PhysiologicalModel.cs`: instance numeric reads, initialization, helper routing, dynamic configured fatigue floor.

Existing GUI:

- `GraphicLogicTest/MainWindow.xaml.cs`: draft-entry behavior, simulation settings/reset/editor wiring, common-profile provider.
- `GraphicLogicTest/SimulationViewModel.cs`: caller-owned common profile, Apply handlers, two global logging bindings.
- `GraphicLogicTest/NamedGEffectsLogicInstance.cs`: forward selected profile.
- `GraphicLogicTest/Views/SimulationView/SimulationView.axaml`: common/per-card numeric editors and separate global logging checkboxes.
- `GraphicLogicTest/Views/SimulationView/SimulationView.axaml.cs`: Apply forwarding.
- `GraphicLogicTest/Views/GLoCPlot/GLoCPlot.axaml.cs`: capture common numeric profile per run.

Existing tests/documentation:

- `GEffectLogicTests/GEffectLogicTests.csproj`: local GUI project reference for pure editor tests.
- `GEffectLogicTests/GLoadStabilityTests.cs`: numeric expectations use active profile, static logging assignment unchanged.
- `GEffectLogicTests/LogicInstancePerformanceTests.cs`: 4,096-instance allocation/burst verification and custom frame timing.
- `README.md`, `GEffectsLogic/Logic.md`, `AGENTS.md`: focused architecture/API/global-logging facts.

Required new files:

- `GEffectsLogic/Compatibility/IsExternalInit.cs`.
- `GraphicLogicTest/LogicSettingsEditorViewModel.cs`.
- `GEffectLogicTests/InstanceSettingsTests.cs`.
- `GEffectLogicTests/SettingsRegressionTests.cs` with compact fixed baseline data.
- `GEffectLogicTests/LogicSettingsEditorTests.cs`.
- `.agents/plans/issue-8-per-instance-settings.md`: requested artifact after approval.

No changes are planned to the four test/GUI logger helper files, static flag use in duration tests, abstract logger routing, external client code, numeric calibration, CI policy, or release configuration.

## Verification

### Plan publication only

- [x] Verify the artifact contains the selected solution, resolved decisions, and unchecked implementation steps, not rejected-option comparisons.
- [x] Check Markdown structure/links and repository status: only the requested new plan artifact may change. `.agents/plans/**` is excluded by normal Markdown-lint configuration; publication received manual structure/link review, not an automated Markdown-lint run.
- [x] Do not build/test or change code merely to publish the plan.

### Narrow implementation iteration

- [x] Establish frozen baseline evidence first, then run focused new tests as the numeric routing/editor/concurrency steps land.

```text
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "FullyQualifiedName~InstanceSettingsTests|FullyQualifiedName~SettingsRegressionTests|FullyQualifiedName~LogicSettingsEditorTests"
```

- [x] Build both library targets after adding record/init support and changing model routing; do not wait until final verification to discover target-framework incompatibility.

### Final implementation gate

Run separately from repository root with .NET 10. Preserve evidence of pre-existing failures and compare against baseline rather than adjusting expectations.

```text
dotnet restore GEffectsLogic.slnx
dotnet format whitespace GEffectsLogic.slnx --verify-no-changes
dotnet format style GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet format analyzers GEffectsLogic.slnx --verify-no-changes --severity warn
dotnet build GEffectsLogic.slnx -c Release -warnaserror
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category!=Experimental&Category!=Performance"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Release --filter "Category=Performance"
dotnet test GEffectLogicTests/GEffectLogicTests.csproj -c Debug --filter "Category=Experimental"
bash scripts/enforce-license-headers.sh --check
npx --yes markdownlint-cli2@0.23.2 "**/*.md"
```

- [x] Experimental results remain separate/non-blocking as in current CI. Normal settings/regression/editor/concurrency and Release performance checks are blocking.
- [x] If PowerShell cannot locate Bash, verify the actual Git-for-Windows Bash path before using the call operator; do not assume its installation path.
- [x] Validate modified AXAML/CSProj XML using the repository workflow's parsing approach and check compiled-binding build output; no workflow change is required.
- [x] Search for any remaining static numeric setting reads/writes, while allowing static Default and the two logging flags.
- [x] Review the full diff: no formula/default/timing-bound changes, no library-owned instance/custom-profile collection, no compatibility facade, and no accidental logging conversion.

### Manual GUI acceptance

- [x] Launch `dotnet run --project GraphicLogicTest` after implementation, not during plan publication.
- [x] Two equal-profile simulations: edit one numeric draft, confirm neither changes before Apply, then Apply and confirm only the chosen instance changes after updates.
- [x] Change common numeric defaults: existing simulations retain their profiles; a new simulation uses the newly applied common profile.
- [x] Apply to a stable simulation at unchanged forces: it wakes and resumes numeric updates.
- [x] Reset/restart preserves its selected profile, clears time/stability correctly, and leaves global logging unchanged. UI/log observation covers profile retention across restart and Reset All plus unchanged flags; exact Time/last-force clearing is asserted by `ResetClearsStabilizationAndKeepsProfile`, not read from the UI.
- [x] Malformed/non-finite numeric text produces an Apply error without partial publication; corrected text applies successfully.
- [x] Global debug/info checkboxes affect logging for all instances immediately; they do not appear in numeric profile editors and are not overwritten by numeric Apply/reset.
- [x] The next GLoC plot run captures the latest common numeric profile, with repeated internal resets retaining it.

Coverage by kind: UI Automation plus the console log stream verified stabilization wake-up on Apply at unchanged forces (debug updates resumed at constant Gz 1 and headBlood moved 0.2000 toward the new RestingBloodHead 0.25), restart/Reset All restabilization cycles at headBlood 0.2500, and two consecutive GLoC plot runs whose internal samples each began at Gz 1 with headBlood 0.2500 and then 0.2000 after re-applying common defaults, so the next run used the newly applied profile and retained it across internal resets. `InstanceSettingsTests` covers Apply semantics, stabilization invalidation, and exact Reset clearing of time/last forces/state; `LogicSettingsEditorTests` covers drafts, atomic publication, and global-flag independence. The single provider invocation per plot run is verified by code inspection only; these logs do not prove reference identity or call count.

## Acceptance criteria

- [x] Numeric settings can differ per instance, including in a 4,096-instance parallel creation/update burst, with no cross-instance contamination.
- [x] No registry, profile interning/deduplication table, pool, shared custom-profile list, instance tracking, or count limit exists in the library.
- [x] Applied profiles are immutable and shared only when callers explicitly pass the same reference; model state remains independent.
- [x] Construction, reset, stabilization, numeric calculations, and derived outputs use the selected profile. Global logging continues reading static flags.
- [x] Explicit Apply preserves accumulated state, updates configured derived values, and invalidates stabilization without changing global flags.
- [x] Common numeric-default changes do not silently propagate to existing instances.
- [x] GUI numeric drafts apply atomically; global logging controls remain separate and immediate.
- [x] Frozen default snapshots and existing physiological tests pass without retuning.
- [x] Both framework targets and GUI build; normal/performance gates pass; actual allocation and 4,096-instance burst timings are reported.
- [x] Future long-term attributes are documented separately without implementing modifiers or modifying numeric settings.
- [x] The new API is documented without a backwards-compatibility obligation.

## Risks and considerations

- Full unique profiles add approximately 976 bytes of numeric payload each before headers; class versus record class does not materially remove this storage. This cost is acceptable to the user at the intended scale, subject to verification. No estimate is presented as a measured allocation.
- Records/init require the modern compiler and conditional marker on `net481`; the shim is metadata support, not a runtime immutability mechanism or per-instance allocation.
- Reflection-based GUI construction must write only fresh unpublished candidates. Published profile mutation remains forbidden by design.
- Arbitrary changes to resting fractions alter targets/normalization while preserving state; this refactor does not promise all profile transitions are physiologically calibrated.
- Full physiological domain validation remains deferred. Existing accepted pathological numeric parameters are not made safe by immutability.
- The local test-to-GUI reference brings existing GUI dependencies into test resolution; keep automated editor tests free of controls/native rendering.
- Global logging flags remain mutable by design. Do not imply deterministic logging behavior under unsynchronized concurrent flag changes, or expand this issue into logger-thread-safety redesign.
- Existing model-description/timing disagreements remain separate work. Do not change assertions to accommodate unrelated physiological behavior.
- A later PR may exceed the repository 800-line threshold due to property conversion/tests/UI. Follow actual size/label policies if a PR is requested; no PR creation is part of planning.
