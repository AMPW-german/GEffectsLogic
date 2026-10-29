// GEffectsLogic
// Copyright (C) 2026 AMPW
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System.Runtime.CompilerServices;
using GEffectsLogic.Logging;

namespace GEffectsLogic;

/// <summary>
///     Simplified physiological model for G-force effects.
///     Design:
///     - 3 blood compartments: head, core (thorax/heart), lower body (abdomen + legs)
///     - G-forces create hydrostatic pressure that shifts blood between compartments
///     - Total blood volume is conserved
///     - Brain oxygen saturation depends on head blood volume (perfusion)
///     - Heart rate increases via baroreceptor reflex to compensate for reduced head perfusion
///     - Consciousness is derived from brain oxygen saturation
///     Coordinate system (pilot-body-centric):
///     +Gz = headward-to-footward (eyeballs down, blood pools in legs → blackout)
///     -Gz = footward-to-headward (eyeballs up, blood pools in head → redout)
///     +Gx = chest-to-back (eyeballs in, pilot pushed into seat back)
///     -Gx = back-to-chest (eyeballs out)
///     Gy  = lateral
///     Gz acts through hydrostatic blood shift (perfusion), Gx through respiratory hypoxia
///     (slow arterial O2 depletion at unbreatheable chest loads), Gy through tolerance
///     reduction, lung compression and neck fatigue.
/// </summary>
public partial class PhysiologicalModel
{
    // MethodImplOptions.AggressiveOptimization is not exposed by the net481
    // reference assemblies; the JIT reads the flag value from metadata on
    // runtimes that support it.
    private const MethodImplOptions AggressiveOptimization = (MethodImplOptions)512;

    // Per-thread reusable diagnostics lists for per-solve scratch lists whose
    // contents are copied out via ToArray/AddRange before return; the list
    // objects never escape into retained results. The pool is a stack so
    // simultaneously live lists at different call depths never alias.
    [ThreadStatic]
    private static List<List<string>>? diagnosticsListPool;

    private static List<string> AcquireDiagnosticsList()
    {
        var pool = diagnosticsListPool ??= [];
        var index = pool.Count - 1;
        if (index < 0) return [];
        var list = pool[index];
        pool.RemoveAt(index);
        list.Clear();
        return list;
    }

    private static void ReleaseDiagnosticsList(List<string> list)
    {
        list.Clear();
        (diagnosticsListPool ??= []).Add(list);
    }

    // --- Compartment blood volumes (fraction of total, sum = 1.0) ---
    protected double bloodHead;
    protected double bloodCore;
    protected double bloodLower;

    // Expanded oxygen model
    protected double bloodO2Head; // O2 saturation in head compartment
    protected double bloodO2Core; // O2 saturation in core compartment (lungs)
    protected double bloodO2Lower; // O2 saturation in lower body compartment

    protected double heartRateMultiplier = 1.0; // Baroreceptor reflex: heart rate multiplier (1.0 = resting)

    protected double
        strainingLevel; // Straining effort (0..1): pilot anti-G straining maneuver including g-suit inflation

    protected double strainingFatigue; // Accumulated AGSM fatigue (0..1): degrades the human straining component
    protected double gSuitFatigue; // Accumulated g-suit fatigue (0..1): degrades mechanical suit compression

    protected double
        hrFatigue; // Cardiovascular fatigue (0..1): suppresses baroreceptor HR toward fatigueHeartRateFloor

    protected double fatigueHeartRateFloor => Settings.CardioFatigueMaxHrFloor;
    protected double perfusionLevel;

    // Respiratory fatigue (separate from cardiovascular)
    protected double respiratoryFatigue; // Respiratory muscle fatigue (0-1)

    // Tolerance modifiers
    protected double gxEffectiveTolerance; // Current Gx tolerance modifier
    protected double gyEffectiveTolerance; // Current Gy tolerance modifier

    // Sudden G-LOC
    protected double suddenLoCAccumulator; // Accumulator for sudden G-LOC (0-1)
    protected bool inSuddenLoC; // Flag for sudden G-LOC state

    // Gy neck side fatigue (damage/death with ceiling)
    protected double gyNeckFatigue; // Neck side fatigue level (0-1)
    protected double gyNeckFatigueDeathAccumulatedTime; // Accumulated time at death level (0-1, 1 = death delay)
    protected bool isDead; // Permanent unconsciousness from Gy neck fatigue

    // Gy lung compression
    protected double lungCompressionLevel; // Lung compression (0-1)
    protected double oxygenExchangeReduction; // Oxygen exchange reduction (0-1)

    // Gx respiratory hypoxia
    protected double arterialOxygenation = 1.0; // Arterial blood oxygenation relative to normal (V/Q mismatch under sustained high Gx)

    // Pain
    protected double painLevel; // Current pain level (0-1)

    protected double consciousnessLevel = 1.0;
    protected double cerebralPressureImpairment;

    //protected double confusionLevel = 0.0;
    protected double visualGrayscaleLevel;
    protected double visualTunnelVisionLevel;
    protected double visualRedoutLevel;
    protected double visualLoCLevel;
    protected double visualBlurLevel; // Rises early, then slowly approaches grayscale
    protected double visualFilmGrainLevel; // 25% influence on film grain, 75% from vignette alpha
    protected bool isUnconscious;

    #region Public read-only state

    private readonly GEffectsLogicInstance logicInstance;

    protected LogicSettings Settings => logicInstance.Settings;

    /// <summary>Fraction of total blood in the head compartment.</summary>
    public double BloodHead => bloodHead;

    public double BloodHeadOverfill => GetHeadBloodOverfill(BloodHead);

    /// <summary>Fraction of total blood in the core compartment.</summary>
    public double BloodCore => bloodCore;

    /// <summary>Fraction of total blood in the lower body compartment.</summary>
    public double BloodLower => bloodLower;

    /// <summary>O2 saturation in head compartment (brain oxygen).</summary>
    public double BloodO2Head => bloodO2Head;

    /// <summary>O2 saturation in core compartment (lungs).</summary>
    public double BloodO2Core => bloodO2Core;

    /// <summary>Arterial blood oxygenation relative to normal (1 = fully oxygenated). Depletes under sustained high Gx (respiratory hypoxia).</summary>
    public double ArterialOxygenation => arterialOxygenation;

    /// <summary>O2 saturation in lower body compartment.</summary>
    public double BloodO2Lower => bloodO2Lower;

    /// <summary>Brain oxygen saturation (same as BloodO2Head).</summary>
    public double BrainO2 => bloodO2Head;

    /// <summary>Current heart rate multiplier from baroreceptor reflex.</summary>
    public double HeartRateMultiplier => heartRateMultiplier;

    /// <summary>Accumulated AGSM fatigue (0 = fresh, 1 = fully exhausted).</summary>
    public double StrainingFatigue => strainingFatigue;

    /// <summary>Accumulated g-suit mechanical fatigue (0 = full effectiveness, 1 = degraded to passive fraction).</summary>
    public double GSuitFatigue => gSuitFatigue;

    /// <summary>Cardiovascular fatigue accumulator (0 = fresh, 1 = fully fatigued — HR pinned to floor).</summary>
    public double HrFatigue => hrFatigue;

    /// <summary>Fixed HR floor the baroreceptor is suppressed toward when fully fatigued.</summary>
    public double FatigueHeartRateFloor => fatigueHeartRateFloor;

    /// <summary>Current straining level (0 = none, 1 = max).</summary>
    //public double StrainingLevel => strainingLevel;

    /// <summary>Current level of head perfusion relative to resting (0 = none, 1 = normal).</summary>
    public double PerfusionLevel => perfusionLevel;

    /// <summary>Current level of consciousness (0 = unconscious, 1 = fully conscious).</summary>
    public double ConsciousnessLevel => consciousnessLevel;

    /// <summary>Temporary functional impairment caused by sustained excess head pressure.</summary>
    public double CerebralPressureImpairment => cerebralPressureImpairment;

    //public double ConfusionLevel { get { return confusionLevel; } set { confusionLevel = value; } }

    /// <summary>Current level of grayscale vision (0 = normal, 1 = fully gray).</summary>
    public double VisualGrayscaleLevel => visualGrayscaleLevel;

    /// <summary>Current level of tunnel vision (0 = none, 1 = blackout).</summary>
    public double VisualTunnelVisionLevel => visualTunnelVisionLevel;

    /// <summary>Current level of redout (0 = none, 1 = full redout).</summary>
    public double VisualRedoutLevel => visualRedoutLevel;

    /// <summary>Current visual loss-of-consciousness override (0 = none, 1 = full blackout).</summary>
    public double VisualLoCLevel => visualLoCLevel;

    /// <summary>Current level of film grain, based on VisualTunnelVisionLevel (intended: 25% film grain level, 75% tunnel vision alpha)</summary>
    public double VisualFilmGrainLevel => visualFilmGrainLevel;

    /// <summary>Current blur percentage (currently recommended: 0.0 - 5.0 pixels gaussian blur)</summary>
    public double VisualBlurLevel => visualBlurLevel;

    public bool IsUnconscious => isUnconscious;

    /// <summary>Respiratory fatigue level (0 = fresh, 1 = exhausted).</summary>
    public double RespiratoryFatigue => respiratoryFatigue;

    /// <summary>Current Gx tolerance modifier (>1 = improved tolerance).</summary>
    public double GxEffectiveTolerance => gxEffectiveTolerance;

    /// <summary>Current Gy tolerance modifier (<1 = reduced tolerance).</summary>
    public double GyEffectiveTolerance => gyEffectiveTolerance;

    /// <summary>Sudden G-LOC accumulator (0 = safe, 1 = imminent).</summary>
    public double SuddenLoCAccumulator => suddenLoCAccumulator;

    /// <summary>Currently in sudden G-LOC state.</summary>
    public bool InSuddenLoC => inSuddenLoC;

    /// <summary>Gy neck side fatigue level (0 = no fatigue, 1 = maximum).</summary>
    public double GyNeckFatigue => gyNeckFatigue;

    /// <summary>Permanently dead from Gy neck side fatigue.</summary>
    public bool IsDead => isDead;

    /// <summary>Lung compression level from sustained Gy (0 = none, 1 = severe).</summary>
    public double LungCompressionLevel => lungCompressionLevel;

    /// <summary>Current pain level (0 = no pain, 1 = maximum pain).</summary>
    public double PainLevel => painLevel;

    #endregion

    /// <summary>Reset all state to resting equilibrium.</summary>
    [MethodImpl(AggressiveOptimization)]
    public virtual void Reset()
    {
        bloodHead = Settings.RestingBloodHead;
        bloodCore = Settings.RestingBloodCore;
        bloodLower = Settings.RestingBloodLower;
        bloodO2Head = Settings.HeadBloodO2Resting;
        bloodO2Core = Settings.CoreBloodO2Resting;
        bloodO2Lower = Settings.LowerBloodO2Resting;
        heartRateMultiplier = 1.0;
        strainingLevel = 0.0;
        strainingFatigue = 0.0;
        gSuitFatigue = 0.0;
        hrFatigue = 0.0;
        visualTunnelVisionLevel = 0.0;
        visualRedoutLevel = 0.0;
        visualLoCLevel = 0.0;
        visualGrayscaleLevel = 0.0;
        visualFilmGrainLevel = 0.0;
        visualBlurLevel = 0.0;
        isUnconscious = false;
        consciousnessLevel = 1.0;
        cerebralPressureImpairment = 0.0;
        perfusionLevel = 0.0;
        respiratoryFatigue = 0.0;
        gxEffectiveTolerance = 1.0;
        gyEffectiveTolerance = 1.0;
        suddenLoCAccumulator = 0.0;
        inSuddenLoC = false;
        gyNeckFatigue = 0.0;
        gyNeckFatigueDeathAccumulatedTime = 0.0;
        isDead = false;
        lungCompressionLevel = 0.0;
        oxygenExchangeReduction = 0.0;
        arterialOxygenation = 1.0;
        painLevel = 0.0;
    }

    [MethodImpl(AggressiveOptimization)]
    public static double Clamp(double value, double min, double max) =>
#if NET481
    Math.Max(Math.Min(value, max), min);
#else
        Math.Clamp(value, min, max);
#endif


    [MethodImpl(AggressiveOptimization)]
    public static double SmoothStep(double x)
    {
        x = Clamp(x, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }

    [MethodImpl(AggressiveOptimization)]
    public static double SmoothStep(double x, double min, double max)
    {
        return SmoothStep(x) * (max - min) + min;
    }

    [MethodImpl(AggressiveOptimization)]
    private double GetHeadBloodOverfill(double headBlood) =>
        Math.Max((headBlood - Settings.RestingBloodHead) / Settings.RestingBloodHead, 0.0);

    /// <summary>
    ///     Advance the model by deltaTime seconds under the given G-force vector.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <param name="gx">Current Gx (positive = chest-to-back).</param>
    /// <param name="gy">Current Gy (lateral).</param>
    /// <param name="gz">Current Gz (positive = headward-to-footward).</param>
    [MethodImpl(AggressiveOptimization)]
    public virtual void Update(double dt, double gx, double gy, double gz)
    {
        if (dt <= 0.0) return;

        var initial = CaptureIntervalState();
        var settings = Settings;
        IntegrationResult result;

        var initialViolation = DownstreamViolation(initial, settings);
        if (initialViolation.Length != 0)
        {
            result = new IntegrationResult(initial, initial, [], 0, 0.0, false, [],
                [$"initial state: {initialViolation}"], [], [], [], [], [], [], [], [],
                [], [], []);
        }
        else
        {
            // Channel ownership: the circulation solve produces the blood
            // compartments, heart rate, straining, respiratory, fatigue,
            // pressure impairment, and neck/sudden dwell trajectories, which
            // pieces consume through upstream; each piece's downstream solve
            // produces blood O2, consciousness, and the visual channels
            // (grayscale, tunnel vision, redout, LoC); CommitIntervalState then
            // writes the state and derives blur, film grain, perfusion, and the
            // axis tolerances.
            var circulation = AdvanceCirculationInterval(in initial, dt, gx, gy, gz,
                settings);
            if (!circulation.Converged || circulation.ModeCrossings.Length != 0 ||
                circulation.StateViolations.Length != 0 || circulation.Segments.Length == 0)
            {
                result = circulation;
            }
            else
            {
                var upstream = new IntervalUpstream(circulation, in initial, gx, gy, gz,
                    settings);
                try
                {
                    var ctx = new IntervalLoopContext(in initial, dt, circulation, upstream,
                        settings);
                    if (ctx.state.IsDead) upstream.PinDwell();

                    if (upstream.NeckInitiallyClamped)
                    {
                        ctx.state = ctx.state with
                        {
                            GyNeckFatigue = upstream.NeckCeiling
                        };
                        ctx.events.Add(new IntegrationEvent("NeckCeilingClamp", 0.0,
                            ctx.state));
                    }
                    if (!ctx.state.IsDead &&
                        ctx.state.GyNeckFatigue < settings.GyNeckFatigueDeathLevel &&
                        ctx.state.GyNeckFatigueDeathDwell > 0.0)
                    {
                        ctx.state = ctx.state with { GyNeckFatigueDeathDwell = 0.0 };
                        ctx.events.Add(new IntegrationEvent("NeckDeathExit", 0.0,
                            ctx.state));
                    }

                    var guard = 0;
                    while (ctx.cursor < dt - 1e-12)
                    {
                        if (++guard > 512)
                        {
                            ctx.violations.Add(
                                "downstream event loop did not terminate at " +
                                $"cursor={ctx.cursor:R}; " +
                                $"headO2={ctx.state.BloodO2Head:R}; " +
                                "headFloorResidual=" +
                                $"{ctx.state.BloodO2Head - settings.BrainO2Floor:R}; " +
                                $"lastO2Root={ctx.lastO2RootEvidence}");
                            break;
                        }

                        var immediate = "";
                        if (!ctx.state.IsDead &&
                            upstream.DeathCompletionTime() <= ctx.cursor)
                            immediate = "Death";
                        else if (upstream.SuddenTriggerTime(ctx.state.InSuddenLoC,
                                     ctx.state.IsDead) <= ctx.cursor)
                            immediate = "SuddenTrigger";
                        else if (!ctx.state.IsDead &&
                                 upstream.SuddenRecoveryTime(ctx.state.InSuddenLoC) <=
                                 ctx.cursor)
                            immediate = "SuddenRecovery";
                        else if (!ctx.state.IsDead && !ctx.state.IsUnconscious &&
                                 ctx.state.ConsciousnessLevel <=
                                 settings.ConsciousnessLossThreshold)
                            immediate = "ConsciousnessLost";
                        else if (!ctx.state.IsDead && ctx.state.IsUnconscious &&
                                 ctx.state.ConsciousnessLevel >=
                                 settings.ConsciousnessRecoveryThreshold)
                            immediate = "ConsciousnessRecovered";
                        if (immediate.Length != 0)
                        {
                            ctx.ApplyAndRecordEvent(immediate, ctx.cursor);
                            continue;
                        }

                        // Advance one piece: hold the upstream quantities constant,
                        // project an incoming consciousness-mode switch, solve the
                        // downstream channels (blood O2, consciousness, visuals),
                        // then localize events and accept the piece.
                        var span = dt - ctx.cursor;
                        var end = ctx.cursor + span;
                        var hold = ComputeHold(ctx.state,
                            c => upstream.Evaluate(ctx.cursor + c * span), span,
                            upstream.GxMagnitude, settings);
                        if (!TryProjectIncomingMode(ctx, span, ref hold)) break;
                        var piece = SolveDownstream(ctx.state, upstream, ctx.cursor,
                            span, hold, settings);
                        // Same under-resolution guard as the circulation interval:
                        // a converged consciousness solve can still miss the
                        // target's fast collapse inside a long piece; retry on a
                        // defect-sized span.
                        for (var retry = 0;
                             piece.Valid && retry < 32 &&
                             TryDownstreamResolutionSplit(in ctx.state, upstream, piece,
                                 ctx.cursor, span, hold, settings,
                                 out var resolutionSpan);
                             retry++)
                        {
                            span = resolutionSpan;
                            end = ctx.cursor + span;
                            hold = ComputeHold(ctx.state,
                                c => upstream.Evaluate(ctx.cursor + c * span), span,
                                upstream.GxMagnitude, settings);
                            piece = SolveDownstream(ctx.state, upstream, ctx.cursor,
                                span, hold, settings);
                        }

                        if (!piece.Valid)
                        {
                            ctx.violations.Add(
                                $"downstream piece invalid at offset {ctx.cursor:R}");
                            break;
                        }

                        var loc = ChaseLoC(ctx.state, piece.Consciousness, ctx.cursor,
                            span, settings);
                        if (!ScanAndAcceptPiece(ctx, piece, loc, hold, span, end))
                            break;
                    }

                    foreach (var diagnostic in upstream.Diagnostics)
                        ctx.violations.Add(diagnostic);
                    var finalBounds =
                        circulation.Segments[circulation.Segments.Length - 1].Bounds;
                    var finalViolation = DownstreamViolation(ctx.state, settings,
                        finalBounds);
                    if (finalViolation.Length != 0) ctx.violations.Add(finalViolation);
                    foreach (var segment in ctx.segments)
                    {
                        foreach (var stage in segment.Stages)
                        {
                            var violation = DownstreamViolation(stage, settings,
                                segment.Bounds);
                            if (violation.Length != 0)
                            {
                                ctx.violations.Add(violation);
                                break;
                            }
                        }
                        if (ctx.violations.Count != 0) break;
                    }

                    var converged = ctx.violations.Count == 0 &&
                                    ctx.cursor >= dt - 1e-12;
                    result = new IntegrationResult(initial, ctx.state,
                        ctx.stages.ToArray(), circulation.Iterations,
                        circulation.MaxScaledResidual, converged,
                        circulation.ModeCrossings, ctx.violations.ToArray(),
                        circulation.PressureBoundEntries, circulation.PressureBoundReleases,
                        circulation.CoreBoundEntries, circulation.CoreBoundReleases,
                        circulation.HeadBoundEntries, circulation.HeadBoundReleases,
                        circulation.LowerBoundEntries, circulation.LowerBoundReleases,
                        circulation.ModeTransitions, ctx.segments.ToArray(),
                        ctx.events.ToArray());
                    ReleaseDiagnosticsList(ctx.violations);
                }
                finally
                {
                    upstream.Release();
                }
            }
        }

        LastIntervalResult = result;
        if (!result.Converged || result.ModeCrossings.Length != 0 ||
            result.StateViolations.Length != 0)
        {
            var diagnostics = string.Join("; ",
                result.ModeCrossings.Concat(result.StateViolations));
            throw new InvalidOperationException(
                $"physiological interval rejected over {dt:R}s: {diagnostics}");
        }

        var final = result.Final;
        CommitIntervalState(in final, gx, gy, gz);
        foreach (var transition in result.Events)
        {
            switch (transition.Kind)
            {
                case "ConsciousnessLost":
                    Logger.Log("Instance has lost consciousness.", logicInstance,
                        Logger.LogLevel.Info);
                    break;
                case "ConsciousnessRecovered":
                    Logger.Log("Instance has regained consciousness.", logicInstance,
                        Logger.LogLevel.Info);
                    break;
                case "SuddenTrigger":
                    Logger.Log("Sudden G-LOC triggered by multi-axis G-forces",
                        logicInstance, Logger.LogLevel.Warning);
                    break;
                case "SuddenRecovery":
                    Logger.Log("Recovering from sudden G-LOC", logicInstance,
                        Logger.LogLevel.Info);
                    break;
                case "Death":
                    Logger.Log("Death from Gy neck side fatigue", logicInstance,
                        Logger.LogLevel.Error);
                    break;
            }
        }
    }

    [MethodImpl(AggressiveOptimization)]
    private void CommitIntervalState(in IntegrationState state, double gx, double gy,
        double gz)
    {
        bloodHead = state.BloodHead;
        bloodLower = state.BloodLower;
        bloodCore = state.BloodCore;
        heartRateMultiplier = state.HeartRateMultiplier;
        hrFatigue = state.CardioFatigue;
        cerebralPressureImpairment = state.CerebralPressureImpairment;
        respiratoryFatigue = state.RespiratoryFatigue;
        strainingLevel = state.StrainingLevel;
        strainingFatigue = state.StrainingFatigue;
        gSuitFatigue = state.GSuitFatigue;
        bloodO2Head = state.BloodO2Head;
        bloodO2Core = state.BloodO2Core;
        bloodO2Lower = state.BloodO2Lower;
        arterialOxygenation = state.ArterialOxygenation;
        consciousnessLevel = state.ConsciousnessLevel;
        lungCompressionLevel = state.LungCompressionLevel;
        painLevel = state.PainLevel;
        gyNeckFatigue = state.GyNeckFatigue;
        gyNeckFatigueDeathAccumulatedTime = state.GyNeckFatigueDeathDwell;
        suddenLoCAccumulator = state.SuddenLoCAccumulator;
        visualGrayscaleLevel = state.VisualGrayscaleLevel;
        visualTunnelVisionLevel = state.VisualTunnelVisionLevel;
        visualRedoutLevel = state.VisualRedoutLevel;
        visualLoCLevel = state.VisualLoCLevel;
        isUnconscious = state.IsUnconscious;
        inSuddenLoC = state.InSuddenLoC;
        isDead = state.IsDead;

        gxEffectiveTolerance = 1.0 + Settings.GxToleranceImprovementFactor * Math.Abs(gx);
        gyEffectiveTolerance = Clamp(
            1.0 - Settings.GyToleranceReductionBase *
            Math.Pow(Math.Abs(gy), Settings.GyToleranceNonlinearity), 0.1, 1.0);
        perfusionLevel = Clamp(bloodHead / Settings.RestingBloodHead, 0.0, 1.0);
        oxygenExchangeReduction = lungCompressionLevel * Settings.GyLungCompressionSeverity;

        var physiologicalVisualTarget = PhysiologicalVisualTarget(
            bloodHead, O2Normalized(bloodO2Head, Settings), Settings);
        var earlyBlurTarget = 0.23 * (1.0 - Math.Exp(-8.0 * physiologicalVisualTarget));
        var earlyBlurInfluence = 1.0 - SmoothStep(
            Clamp((visualGrayscaleLevel - 0.2) / 0.3, 0.0, 1.0));
        var earlyBlurBoost =
            Math.Max(earlyBlurTarget - visualGrayscaleLevel * 0.5, 0.0) * earlyBlurInfluence;
        visualBlurLevel = Clamp(visualGrayscaleLevel + earlyBlurBoost, 0.0, 1.0);
        visualFilmGrainLevel = Clamp(Math.Pow(visualTunnelVisionLevel, 1.5), 0.0, 1.0);
    }

    [MethodImpl(AggressiveOptimization)]
    internal void LoadIntervalState(in IntegrationState state) =>
        CommitIntervalState(in state, 0.0, 0.0, 1.0);

    /// <summary>Result of the most recent Update interval; for tests.</summary>
    internal IntegrationResult? LastIntervalResult { get; private set; }


    public PhysiologicalModel(GEffectsLogicInstance logicInstance)
    {
        this.logicInstance = logicInstance;
        Reset();
    }

}
