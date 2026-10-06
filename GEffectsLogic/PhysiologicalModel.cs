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
public class PhysiologicalModel
{
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

    public static double Clamp(double value, double min, double max) =>
#if NET481
    Math.Max(Math.Min(value, max), min);
#else
        Math.Clamp(value, min, max);
#endif


    public static double SmoothStep(double x)
    {
        x = Clamp(x, 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }

    public static double SmoothStep(double x, double min, double max)
    {
        return SmoothStep(x) * (max - min) + min;
    }

    private double GetHeadBloodOverfill(double headBlood) =>
        Math.Max((headBlood - Settings.RestingBloodHead) / Settings.RestingBloodHead, 0.0);

    /// <summary>
    ///     Advance the model by deltaTime seconds under the given G-force vector.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <param name="gx">Current Gx (positive = chest-to-back).</param>
    /// <param name="gy">Current Gy (lateral).</param>
    /// <param name="gz">Current Gz (positive = headward-to-footward).</param>
    public virtual void Update(double dt, double gx, double gy, double gz)
    {
        if (dt <= 0.0) return;

        var initial = CaptureIntervalState();
        var result = AdvanceInterval(in initial, dt, gx, gy, gz, Settings);
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

    internal bool CanStabilize(double gx, double gy, double gz, double maximumStateError)
    {
        if (!IsFiniteNumber(maximumStateError) || maximumStateError < 0.0) return false;
        var settings = Settings;

        // The only certified freeze is the exact neutral resting fixed point:
        // (0,0,1) G with every persistent accumulator at its resting value. Any
        // force or state deviation leaves residual dynamics and is never frozen.
        if (gx != 0.0 || gy != 0.0 || gz != 1.0) return false;
        if (isDead || isUnconscious || inSuddenLoC) return false;
        if (bloodHead != settings.RestingBloodHead ||
            bloodLower != settings.RestingBloodLower ||
            heartRateMultiplier != 1.0 ||
            cardioFatigue != 0.0 ||
            strainingLevel != 0.0 ||
            strainingFatigue != 0.0 ||
            gSuitFatigue != 0.0 ||
            respiratoryFatigue != 0.0 ||
            cerebralPressureImpairment != 0.0 ||
            suddenLoCAccumulator != 0.0 ||
            gyNeckFatigue != 0.0 ||
            gyNeckFatigueDeathAccumulatedTime != 0.0 ||
            arterialOxygenation != 1.0 ||
            lungCompressionLevel != 0.0 ||
            painLevel != 0.0)
            return false;

        // Confirm resting circulation is an exact fixed point under (0,0,1):
        // every coordinate rate must vanish, not merely stay under a bound.
        var state = CaptureIntervalState();
        var rates = new double[CirculationDimensions];
        EvaluateCirculation(in state, 0.0, 0.0, 1.0, settings, rates, null, true);
        for (var coordinate = 0; coordinate < CirculationDimensions; coordinate++)
            if (rates[coordinate] != 0.0) return false;
        if (PressureSource(bloodHead, gz, settings) != 0.0) return false;

        // Oxygen equilibrium under the stationary upstream: the coefficient
        // matrix is a constant Metzler matrix with nonpositive row sums, so the
        // infinity-norm distance from the equilibrium can never grow. Solve the
        // equilibrium without clamping; a singular or out-of-bounds equilibrium
        // is not a certificate.
        var trial = new IntervalTrialState
        {
            Head = bloodHead,
            Lower = bloodLower,
            HeartRate = heartRateMultiplier,
            Straining = strainingLevel,
            Respiratory = respiratoryFatigue,
            Pressure = cerebralPressureImpairment,
            Arterial = arterialOxygenation,
            Compression = lungCompressionLevel
        };
        var delivery = EffectiveDelivery(ShapedPerfusion(bloodHead, settings),
            heartRateMultiplier, arterialOxygenation, settings);
        var depleting = BrainO2Target(delivery, settings) < bloodO2Head;
        var spec = BuildOxygenSystem(_ => trial, 0.0, depleting, false, false,
            false, 0.0, settings);
        var m = spec.M;
        var b = spec.B;
        var metzler = true;
        for (var row = 0; row < 3; row++)
        {
            var rowSum = 0.0;
            for (var column = 0; column < 3; column++)
            {
                if (!IsFiniteNumber(m[row][column])) return false;
                rowSum += m[row][column];
                if (column != row && m[row][column] < 0.0) metzler = false;
            }
            if (rowSum > 0.0) metzler = false;
        }
        if (!metzler) return false;
        var negated = new[] { -b[0], -b[1], -b[2] };
        for (var row = 0; row < 3; row++)
        {
            if (!spec.Instant[row]) continue;
            for (var column = 0; column < 3; column++) m[row][column] = 0.0;
            m[row][row] = 1.0;
            negated[row] = spec.InstantTargets[row]!(0.0);
        }
        var equilibrium = new double[3];
        if (!NumericalMath.TrySolveLinear(m, negated, equilibrium)) return false;
        var oxygen = new[] { bloodO2Head, bloodO2Lower, bloodO2Core };
        var lowBounds = new[] { settings.BrainO2Floor, 0.0, 0.0 };
        var distance = 0.0;
        for (var row = 0; row < 3; row++)
        {
            if (!IsFiniteNumber(equilibrium[row]) ||
                equilibrium[row] < lowBounds[row] || equilibrium[row] > 1.0)
                return false;
            var gap = Math.Abs(equilibrium[row] - oxygen[row]);
            if (gap > distance) distance = gap;
        }
        if (distance > 0.5 * maximumStateError) return false;

        var (consciousnessTarget, _) = ConsciousnessTargetAndTau(
            O2Normalized(bloodO2Head, settings),
            PerfusionNormalized(bloodHead, settings),
            cerebralPressureImpairment, settings);
        if (Math.Abs(consciousnessTarget - consciousnessLevel) > maximumStateError)
            return false;

        var physiologicalTarget = PhysiologicalVisualTarget(
            bloodHead, O2Normalized(bloodO2Head, settings), settings);
        var redoutTarget = RedoutTarget(GetHeadBloodOverfill(bloodHead), settings);
        if (Math.Abs(physiologicalTarget - visualTunnelVisionLevel) > maximumStateError ||
            Math.Abs(redoutTarget - visualRedoutLevel) > maximumStateError ||
            Math.Abs(physiologicalTarget - visualGrayscaleLevel) > maximumStateError)
            return false;

        return Math.Abs(LoCCeiling(consciousnessLevel, settings) - visualLoCLevel) <=
               maximumStateError;
    }


    public PhysiologicalModel(GEffectsLogicInstance logicInstance)
    {
        this.logicInstance = logicInstance;
        Reset();
    }

    #region intervalIntegration

    internal const int CirculationDimensions = 5;
    private const int CirculationSolvedDimensions = 4;
    private const int MaxNewtonIterations = 12;
    private const int MaxDampingHalvings = 8;
    private const double PressureBoundRootToleranceSeconds = 1e-8;
    private const double ScaledResidualTolerance = 1e-10;

    internal readonly record struct IntegrationState(
        double BloodHead,
        double BloodLower,
        double HeartRateMultiplier,
        double CardioFatigue,
        double CerebralPressureImpairment,
        double RespiratoryFatigue)
    {
        internal double BloodCore => 1.0 - BloodHead - BloodLower;
        internal double StrainingLevel { get; init; }
        internal double StrainingFatigue { get; init; }
        internal double GSuitFatigue { get; init; }
        internal double BloodO2Head { get; init; }
        internal double BloodO2Core { get; init; }
        internal double BloodO2Lower { get; init; }
        internal double ArterialOxygenation { get; init; }
        internal double ConsciousnessLevel { get; init; }
        internal double LungCompressionLevel { get; init; }
        internal double PainLevel { get; init; }
        internal double GyNeckFatigue { get; init; }
        internal double GyNeckFatigueDeathDwell { get; init; }
        internal double SuddenLoCAccumulator { get; init; }
        internal double VisualGrayscaleLevel { get; init; }
        internal double VisualTunnelVisionLevel { get; init; }
        internal double VisualRedoutLevel { get; init; }
        internal double VisualLoCLevel { get; init; }
        internal bool IsUnconscious { get; init; }
        internal bool InSuddenLoC { get; init; }
        internal bool IsDead { get; init; }
    }

    internal sealed record IntegrationEvent(
        string Kind,
        double Offset,
        IntegrationState State);

    internal sealed record IntegrationResult(
        IntegrationState Initial,
        IntegrationState Final,
        IntegrationState[] Stages,
        int Iterations,
        double MaxScaledResidual,
        bool Converged,
        string[] ModeCrossings,
        string[] StateViolations,
        double[] PressureBoundEntries,
        double[] PressureBoundReleases,
        double[] CoreBoundEntries,
        double[] CoreBoundReleases,
        double[] HeadBoundEntries,
        double[] HeadBoundReleases,
        double[] LowerBoundEntries,
        double[] LowerBoundReleases,
        double[] ModeTransitions,
        IntegrationSegment[] Segments,
        IntegrationEvent[] Events);

    internal sealed record IntegrationSegment(
        double StartOffset,
        double Duration,
        IntegrationState Initial,
        IntegrationState Final,
        IntegrationState[] Stages,
        BloodBounds Bounds,
        double[] HeadCoefficients,
        double[] LowerCoefficients,
        double[] RateCoefficients,
        double[] CardioCoefficients)
    {
        internal double CoefficientStart { get; init; }
        internal bool CoreBound => (Bounds & BloodBounds.Core) != 0;
        internal bool HeadBound => (Bounds & BloodBounds.Head) != 0;
        internal bool LowerBound => (Bounds & BloodBounds.Lower) != 0;
    }

    private sealed record SegmentSolve(
        IntegrationState Final,
        IntegrationState[] Stages,
        int Iterations,
        double MaxScaledResidual,
        bool Converged,
        string[] ModeCrossings,
        string[] StateViolations,
        double[] PressureBoundEntries,
        double[] PressureBoundReleases,
        double[] HeadCoefficients,
        double[] LowerCoefficients,
        double[] RateCoefficients,
        double[] CardioCoefficients,
        bool CoreRootSearchValid,
        BloodBounds BoundViolations);

    [Flags]
    internal enum CirculationMode
    {
        None = 0,
        HeadOverfilled = 1,
        BradycardiaClamped = 2,
        PerfusionRangeClamped = 4,
        BaroTargetClamped = 8,
        CardioBuild = 16,
        UnsupportedCoreBound = 32,
        CardioCeilingBound = 64
    }

    [Flags]
    internal enum BloodBounds
    {
        None = 0,
        Core = 1,
        Head = 2,
        Lower = 4
    }

    private const CirculationMode CrossingBits = CirculationMode.HeadOverfilled |
        CirculationMode.BradycardiaClamped | CirculationMode.PerfusionRangeClamped |
        CirculationMode.BaroTargetClamped | CirculationMode.CardioBuild |
        CirculationMode.CardioCeilingBound;

    internal IntegrationState CaptureIntervalState() => new(
        bloodHead, bloodLower, heartRateMultiplier, hrFatigue, cerebralPressureImpairment,
        respiratoryFatigue)
    {
        StrainingLevel = strainingLevel,
        StrainingFatigue = strainingFatigue,
        GSuitFatigue = gSuitFatigue,
        BloodO2Head = bloodO2Head,
        BloodO2Core = bloodO2Core,
        BloodO2Lower = bloodO2Lower,
        ArterialOxygenation = arterialOxygenation,
        ConsciousnessLevel = consciousnessLevel,
        LungCompressionLevel = lungCompressionLevel,
        PainLevel = painLevel,
        GyNeckFatigue = gyNeckFatigue,
        GyNeckFatigueDeathDwell = gyNeckFatigueDeathAccumulatedTime,
        SuddenLoCAccumulator = suddenLoCAccumulator,
        VisualGrayscaleLevel = visualGrayscaleLevel,
        VisualTunnelVisionLevel = visualTunnelVisionLevel,
        VisualRedoutLevel = visualRedoutLevel,
        VisualLoCLevel = visualLoCLevel,
        IsUnconscious = isUnconscious,
        InSuddenLoC = inSuddenLoC,
        IsDead = isDead
    };

    internal static IntegrationState IndependentCirculationStateAt(
        in IntegrationState initial, double t, double gx, double gz, LogicSettings settings)
    {
        if (t <= 0.0) return initial;

        var strainTarget = Clamp(
            (gz - settings.StrainingStartGz) /
            (settings.StrainingFullGz - settings.StrainingStartGz), 0.0, 1.0);
        var instant = settings.StrainingTau <= 1e-9;
        var strain = instant
            ? strainTarget
            : strainTarget + (initial.StrainingLevel - strainTarget) *
              Math.Exp(-t / settings.StrainingTau);
        var strainFatigue = EvolveThresholdFatigue(initial.StrainingLevel, strainTarget,
            settings.StrainingTau, t, initial.StrainingFatigue,
            settings.StrainingFatigueBuildRate, settings.StrainingFatigueRecoveryTau,
            quadratic: true);
        var suitFatigue = EvolveThresholdFatigue(initial.StrainingLevel, strainTarget,
            settings.StrainingTau, t, initial.GSuitFatigue,
            settings.GSuitFatigueBuildRate, settings.GSuitFatigueRecoveryTau,
            quadratic: false);

        var respiratoryBuild = settings.RespiratoryFatigueBuildRate *
            Math.Max(0.0, Math.Abs(gx) - settings.GxRespiratoryFatigueThreshold) * 0.5 *
            (1.0 + (settings.GxRespiratoryFatigueAccelerationFactor - 1.0) * Math.Abs(gx));
        var respiratoryTau = settings.RespiratoryFatigueRecoveryTau;
        var respiratory = respiratoryTau <= 1e-9
            ? respiratoryBuild * respiratoryTau
            : respiratoryBuild * respiratoryTau +
              (initial.RespiratoryFatigue - respiratoryBuild * respiratoryTau) *
              Math.Exp(-t / respiratoryTau);

        return initial with
        {
            StrainingLevel = Clamp(strain, 0.0, 1.0),
            StrainingFatigue = Clamp(strainFatigue, 0.0, 1.0),
            GSuitFatigue = Clamp(suitFatigue, 0.0, 1.0),
            RespiratoryFatigue = Clamp(respiratory, 0.0, 1.0)
        };
    }

    private static double EvolveThresholdFatigue(
        double strain0, double strainTarget, double strainTau, double t,
        double fatigue0, double buildRate, double recoveryTau, bool quadratic)
    {
        if (strainTau <= 1e-9)
        {
            var constantIntegral = quadratic
                ? strainTarget * strainTarget * t
                : strainTarget * t;
            return strainTarget > StrainActiveThreshold
                ? Math.Min(fatigue0 + buildRate * constantIntegral, 1.0)
                : fatigue0 * Math.Exp(-t / recoveryTau);
        }

        var building = strain0 > StrainActiveThreshold ||
                       (strain0 == StrainActiveThreshold &&
                        strainTarget > StrainActiveThreshold);
        var crossing = StrainThresholdCrossing(strain0, strainTarget, strainTau, t);
        var fatigue = EvolveThresholdPhase(strain0, strainTarget, strainTau,
            crossing ?? t, fatigue0, buildRate, recoveryTau, quadratic, building);
        return crossing.HasValue
            ? EvolveThresholdPhase(StrainActiveThreshold, strainTarget, strainTau,
                t - crossing.Value, fatigue, buildRate, recoveryTau, quadratic, !building)
            : fatigue;
    }

    private static double EvolveThresholdPhase(
        double strainStart, double strainTarget, double strainTau, double h,
        double fatigue, double buildRate, double recoveryTau, bool quadratic,
        bool building)
    {
        if (!building) return fatigue * Math.Exp(-h / recoveryTau);
        var d = strainStart - strainTarget;
        var integral = quadratic
            ? strainTarget * strainTarget * h +
              2.0 * strainTarget * d * strainTau * (1.0 - Math.Exp(-h / strainTau)) +
              d * d * strainTau * 0.5 * (1.0 - Math.Exp(-2.0 * h / strainTau))
            : strainTarget * h + d * strainTau * (1.0 - Math.Exp(-h / strainTau));
        return Math.Min(fatigue + buildRate * integral, 1.0);
    }

    private static double? StrainThresholdCrossing(
        double strain0, double strainTarget, double strainTau, double until)
    {
        if (strainTau <= 1e-9) return null;
        var delta = strain0 - strainTarget;
        var ratio = (StrainActiveThreshold - strainTarget) / delta;
        if (ratio <= 0.0) return null;
        var crossing = -strainTau * Math.Log(ratio);
        return crossing > 0.0 && crossing < until ? crossing : null;
    }

    private const double StrainActiveThreshold = 0.01;

    private static double SuitActivation(in IntegrationState state, double gz,
        LogicSettings settings)
    {
        if (gz <= 0.0) return 0.0;
        var effectiveStrain = state.StrainingLevel * (1.0 - state.StrainingFatigue);
        var effectiveSuit = settings.GSuitEffectiveness *
            (settings.GSuitPassiveFraction +
             (1.0 - settings.GSuitPassiveFraction) * (1.0 - state.GSuitFatigue));
        return Clamp(effectiveStrain * effectiveSuit, 0.0, 1.0);
    }

    private static double AdjustedShiftRate(in IntegrationState state, double gx, double gy,
        double gz, LogicSettings settings) =>
        HydrostaticShiftRate(gx, gy, gz, settings) *
        (1.0 - settings.GSuitGlobalShiftReductionMax * SuitActivation(in state, gz, settings));

    internal static CirculationMode EvaluateCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian) =>
        EvaluateCirculation(in state, gx, gy, gz, settings, rates, jacobian, true);

    private static CirculationMode EvaluateCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian,
        bool includePressure)
    {
        var suit = SuitActivation(in state, gz, settings);
        var shiftRate = HydrostaticShiftRate(gx, gy, gz, settings) *
                        (1.0 - settings.GSuitGlobalShiftReductionMax * suit);
        var coreLowerFraction = Clamp(
            settings.CoreLowerShiftFraction *
            (1.0 - settings.GSuitCoreLowerReductionMax * suit), 0.05, 0.95);

        var head = state.BloodHead;
        var lower = state.BloodLower;
        var core = state.BloodCore;
        var rate = state.HeartRateMultiplier;
        var cardio = state.CardioFatigue;
        var pressure = state.CerebralPressureImpairment;
        var restHead = settings.RestingBloodHead;
        var restCore = settings.RestingBloodCore;
        var restLower = settings.RestingBloodLower;

        var overfill = Math.Max((head - restHead) / restHead, 0.0);

        var returnRate = settings.PassiveReturnRate * rate;
        var lowerReturnRate = returnRate * (1.0 + settings.GSuitLowerReturnBoostMax * suit);
        var pressureReturn = settings.HeadPressureReturnRate *
                             NumericalMath.ExpMinusOne(settings.HeadPressureReturnExponent * overfill);

        var qHead = -shiftRate + returnRate * (restHead - head) - pressureReturn;
        var qCore = shiftRate + returnRate * (restCore - core);
        var qLower = shiftRate * coreLowerFraction + lowerReturnRate * (restLower - lower);
        var qTotal = qHead + qCore + qLower;
        var fHead = qHead - head * qTotal;
        var headward = overfill > 0.0 || (head == restHead && fHead > 0.0);

        double baroTarget;
        double baroDerivative;
        double hrTau;
        double tauDerivative;
        var bradycardiaClamped = false;
        var perfusionClamped = false;
        if (headward)
        {
            var bradycardia = Clamp(overfill / settings.NegativeGHeartStopOverfill, 0.0, 1.0);
            bradycardiaClamped = overfill >= settings.NegativeGHeartStopOverfill;
            baroTarget = 1.0 - bradycardia;
            baroDerivative = bradycardiaClamped
                ? 0.0
                : -1.0 / (restHead * settings.NegativeGHeartStopOverfill);
            hrTau = settings.BaroreceptorTimeConstantNegativeMin +
                    (settings.BaroreceptorTimeConstantNegativeMax -
                     settings.BaroreceptorTimeConstantNegativeMin) * baroTarget;
            tauDerivative = (settings.BaroreceptorTimeConstantNegativeMax -
                             settings.BaroreceptorTimeConstantNegativeMin) * baroDerivative;
        }
        else
        {
            var perfusionRatioClamped = Clamp(head / restHead, 0.0, 1.0);
            perfusionClamped = head <= 0.0 || head > restHead;
            baroTarget = 1.0 + settings.BaroreceptorGain * (1.0 - perfusionRatioClamped);
            baroDerivative = perfusionClamped ? 0.0 : -settings.BaroreceptorGain / restHead;
            hrTau = settings.BaroreceptorTimeConstantPositive;
            tauDerivative = 0.0;
        }

        var unclampedBaroTarget = baroTarget;
        baroTarget = Clamp(baroTarget, 0.0, settings.MaxHeartRateMultiplier);
        var baroClamped = baroTarget != unclampedBaroTarget;
        if (baroClamped) baroDerivative = 0.0;

        var respiratoryHrModifier = Math.Max(1.0 - 0.2 * state.RespiratoryFatigue,
            settings.RespiratoryFatigueHrFloor);
        var modifiedBaro = baroTarget * respiratoryHrModifier;
        var hrTarget = modifiedBaro + cardio * (settings.CardioFatigueMaxHrFloor - modifiedBaro);

        var fLower = qLower - lower * qTotal;
        rates[0] = restHead * fHead;
        rates[1] = fLower + (1.0 - restHead) * fHead * lower / (1.0 - head);
        rates[2] = (hrTarget - rate) / hrTau;
        var cardioElevation = rate - 1.0 - settings.CardioFatigueHrElevationThreshold;
        var cardioBuild = cardioElevation > 0.0 ||
                          cardioElevation == 0.0 && rates[2] > 0.0;
        var cardioPinned = cardio >= 1.0 && cardioBuild;
        rates[3] = cardioPinned
            ? 0.0
            : cardioBuild
                ? settings.CardioFatigueBuildRate * cardioElevation
                : -cardio / settings.CardioFatigueRecoveryTau;
        rates[4] = includePressure
            ? PressureSource(head, gz, settings) -
              pressure / settings.CerebralPressureImpairmentRecoveryTau
            : 0.0;

        var mode = CirculationMode.None;
        if (headward) mode |= CirculationMode.HeadOverfilled;
        if (headward && (overfill > settings.NegativeGHeartStopOverfill ||
                         overfill == settings.NegativeGHeartStopOverfill && rates[0] >= 0.0))
            mode |= CirculationMode.BradycardiaClamped;
        if (perfusionClamped)
            mode |= CirculationMode.PerfusionRangeClamped;
        if (baroClamped ||
            unclampedBaroTarget == settings.MaxHeartRateMultiplier && rates[0] < 0.0)
            mode |= CirculationMode.BaroTargetClamped;
        if (cardioBuild) mode |= CirculationMode.CardioBuild;
        if (cardioPinned) mode |= CirculationMode.CardioCeilingBound;

        if (jacobian is not null)
        {
            var pressureReturnDerivative = headward
                ? settings.HeadPressureReturnRate * settings.HeadPressureReturnExponent / restHead *
                  Math.Exp(settings.HeadPressureReturnExponent * overfill)
                : 0.0;
            var dqTotalHead = -pressureReturnDerivative;
            var dqTotalLower = returnRate - lowerReturnRate;
            var dqTotalRate = settings.PassiveReturnRate *
                              ((restHead - head) + (restCore - core) +
                               (1.0 + settings.GSuitLowerReturnBoostMax * suit) *
                               (restLower - lower));

            jacobian[0][0] = -returnRate - pressureReturnDerivative - qTotal - head * dqTotalHead;
            jacobian[0][1] = -head * dqTotalLower;
            jacobian[0][2] = settings.PassiveReturnRate * (restHead - head) - head * dqTotalRate;
            jacobian[0][3] = 0.0;
            jacobian[0][4] = 0.0;

            jacobian[1][0] = -lower * dqTotalHead;
            jacobian[1][1] = -lowerReturnRate - qTotal - lower * dqTotalLower;
            jacobian[1][2] = settings.PassiveReturnRate *
                             (1.0 + settings.GSuitLowerReturnBoostMax * suit) *
                             (restLower - lower) - lower * dqTotalRate;
            jacobian[1][3] = 0.0;
            jacobian[1][4] = 0.0;

            var targetDerivativeHead = respiratoryHrModifier * (1.0 - cardio) * baroDerivative;
            var targetDerivativeCardio = settings.CardioFatigueMaxHrFloor - modifiedBaro;
            jacobian[2][0] = targetDerivativeHead / hrTau -
                             (hrTarget - rate) * tauDerivative / (hrTau * hrTau);
            jacobian[2][1] = 0.0;
            jacobian[2][2] = -1.0 / hrTau;
            jacobian[2][3] = targetDerivativeCardio / hrTau;
            jacobian[2][4] = 0.0;

            if (cardioPinned)
                for (var variable = 0; variable < CirculationDimensions; variable++)
                    jacobian[3][variable] = 0.0;
            else
            {
                jacobian[3][0] = 0.0;
                jacobian[3][1] = 0.0;
                jacobian[3][2] = cardioBuild ? settings.CardioFatigueBuildRate : 0.0;
                jacobian[3][3] = cardioBuild ? 0.0 : -1.0 / settings.CardioFatigueRecoveryTau;
                jacobian[3][4] = 0.0;
            }

            if (includePressure)
            {
                var logisticSigma = NumericalMath.Sigmoid(settings.CerebralPressureImpairmentExponent *
                    (overfill - settings.CerebralPressureImpairmentMidOverfill));
                var restLogisticSigma = NumericalMath.Sigmoid(
                    -settings.CerebralPressureImpairmentExponent * settings.CerebralPressureImpairmentMidOverfill);
                jacobian[4][0] = logisticSigma > restLogisticSigma
                    ? settings.CerebralPressureImpairmentMaxBuildRate *
                      settings.CerebralPressureImpairmentExponent * logisticSigma * (1.0 - logisticSigma) /
                      restHead
                    : 0.0;
                jacobian[4][4] = -1.0 / settings.CerebralPressureImpairmentRecoveryTau;
            }
            else
            {
                jacobian[4][0] = 0.0;
                jacobian[4][4] = 0.0;
            }

            jacobian[4][1] = 0.0;
            jacobian[4][2] = 0.0;
            jacobian[4][3] = 0.0;

            var availability = lower / (1.0 - head);
            for (var variable = 0; variable < CirculationDimensions; variable++)
            {
                var oldHead = jacobian[0][variable];
                var availabilityDerivative = variable switch
                {
                    0 => lower / ((1.0 - head) * (1.0 - head)),
                    1 => 1.0 / (1.0 - head),
                    _ => 0.0
                };
                jacobian[0][variable] = restHead * oldHead;
                jacobian[1][variable] += (1.0 - restHead) *
                    (availability * oldHead + fHead * availabilityDerivative);
            }
        }

        return mode;
    }

    private static double HydrostaticShiftRate(double gx, double gy, double gz,
        LogicSettings settings)
    {
        var gxEffectiveTolerance = 1.0 + settings.GxToleranceImprovementFactor * Math.Abs(gx);
        var gyEffectiveTolerance = Clamp(1.0 - settings.GyToleranceReductionBase *
                                       Math.Pow(Math.Abs(gy), settings.GyToleranceNonlinearity),
            0.1, 1.0);
        var combinedTolerance = gxEffectiveTolerance * gyEffectiveTolerance;
        var gzNetScaled = Math.Sign(gz) *
                          Math.Pow(Math.Abs(gz) / combinedTolerance, settings.HydrostaticShiftExponent) - 1.0;
        return settings.HydrostaticShiftRate * gzNetScaled;
    }

    private static double HeldCoreDrive(double rate, double shiftRate, LogicSettings settings) =>
        shiftRate + settings.PassiveReturnRate * rate * settings.RestingBloodCore;

    internal static CirculationMode EvaluateCoreConstrainedCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian) =>
        EvaluateCoreConstrainedCirculation(in state, gx, gy, gz, settings, rates, jacobian, true);

    private static CirculationMode EvaluateCoreConstrainedCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian,
        bool includePressure) =>
        EvaluateBoundConstrainedCirculation(in state, gx, gy, gz, settings, rates, jacobian,
            includePressure, BloodBounds.Core);

    internal static CirculationMode EvaluateBoundConstrainedCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian,
        bool includePressure, BloodBounds bounds)
    {
        var mode = EvaluateCirculation(in state, gx, gy, gz, settings, rates, jacobian,
            includePressure);
        var rawHeadRate = rates[0];
        var rawLowerRate = rates[1];

        if (bounds == BloodBounds.Core)
        {
            var rawCoreRate = -(rawHeadRate + rawLowerRate);
            var headFree = state.BloodHead - settings.MinHeadBloodFraction;
            var lowerFree = state.BloodLower;
            var width = headFree + lowerFree;
            if (!IsFiniteNumber(width) || width <= 0.0 ||
                state.BloodHead <= settings.MinHeadBloodFraction || state.BloodLower <= 0.0)
                return mode | CirculationMode.UnsupportedCoreBound;

            var share = headFree / width;
            rates[0] = rawHeadRate + rawCoreRate * share;
            rates[1] = -rates[0];
            if (jacobian is not null)
            {
                for (var variable = 0; variable < CirculationDimensions; variable++)
                {
                    var rawHeadDerivative = jacobian[0][variable];
                    var rawCoreDerivative = -(rawHeadDerivative + jacobian[1][variable]);
                    var shareDerivative = variable switch
                    {
                        0 => state.BloodLower / (width * width),
                        1 => -headFree / (width * width),
                        _ => 0.0
                    };
                    jacobian[0][variable] = rawHeadDerivative + rawCoreDerivative * share +
                                            rawCoreRate * shareDerivative;
                    jacobian[1][variable] = -jacobian[0][variable];
                }
            }

            return mode;
        }

        var headBound = (bounds & BloodBounds.Head) != 0;
        var lowerBound = (bounds & BloodBounds.Lower) != 0;
        if (!headBound && !lowerBound)
            return mode;

        if (headBound && lowerBound || bounds != BloodBounds.Head && bounds != BloodBounds.Lower)
        {
            rates[0] = 0.0;
            rates[1] = 0.0;
            if (jacobian is not null)
                for (var variable = 0; variable < CirculationDimensions; variable++)
                {
                    jacobian[0][variable] = 0.0;
                    jacobian[1][variable] = 0.0;
                }

            return mode;
        }

        if (headBound)
        {
            var freeWidth = state.BloodCore + state.BloodLower;
            if (!IsFiniteNumber(freeWidth) || freeWidth <= 0.0)
                return mode | CirculationMode.UnsupportedCoreBound;
            var lowerShare = state.BloodLower / freeWidth;
            rates[0] = 0.0;
            rates[1] = rawLowerRate + rawHeadRate * lowerShare;
            if (jacobian is not null)
                for (var variable = 0; variable < CirculationDimensions; variable++)
                {
                    var rawHeadDerivative = jacobian[0][variable];
                    var shareDerivative = variable switch
                    {
                        0 => state.BloodLower / (freeWidth * freeWidth),
                        1 => 1.0 / freeWidth,
                        _ => 0.0
                    };
                    jacobian[0][variable] = 0.0;
                    jacobian[1][variable] = jacobian[1][variable] +
                        rawHeadDerivative * lowerShare + rawHeadRate * shareDerivative;
                }

            return mode;
        }

        var lowerFreeWidth = 1.0 - settings.MinHeadBloodFraction - state.BloodLower;
        var lowerHeadFree = state.BloodHead - settings.MinHeadBloodFraction;
        if (!IsFiniteNumber(lowerFreeWidth) || lowerFreeWidth <= 0.0)
            return mode | CirculationMode.UnsupportedCoreBound;
        var headShare = lowerHeadFree / lowerFreeWidth;
        rates[0] = rawHeadRate + rawLowerRate * headShare;
        rates[1] = 0.0;
        if (jacobian is not null)
            for (var variable = 0; variable < CirculationDimensions; variable++)
            {
                var rawLowerDerivative = jacobian[1][variable];
                var shareDerivative = variable switch
                {
                    0 => 1.0 / lowerFreeWidth,
                    1 => lowerHeadFree / (lowerFreeWidth * lowerFreeWidth),
                    _ => 0.0
                };
                jacobian[0][variable] = jacobian[0][variable] +
                    rawLowerDerivative * headShare + rawLowerRate * shareDerivative;
                jacobian[1][variable] = 0.0;
            }

        return mode;
    }

    private static double PressureSource(double head, double gz, LogicSettings settings)
    {
        var overfill = Math.Max((head - settings.RestingBloodHead) / settings.RestingBloodHead, 0.0);
        var logistic = settings.CerebralPressureImpairmentMaxBuildRate * NumericalMath.Sigmoid(
            settings.CerebralPressureImpairmentExponent *
            (overfill - settings.CerebralPressureImpairmentMidOverfill));
        var restLogistic = settings.CerebralPressureImpairmentMaxBuildRate * NumericalMath.Sigmoid(
            -settings.CerebralPressureImpairmentExponent * settings.CerebralPressureImpairmentMidOverfill);
        return Math.Max(logistic - restLogistic, 0.0) +
               settings.CerebralPressureImpairmentNegativeGzRate *
               Math.Min(Math.Max(0.0, -gz), settings.CerebralPressureImpairmentNegativeGzCap);
    }

#pragma warning disable CA1822
    internal IntegrationResult AdvanceCirculationInterval(
        in IntegrationState initial,
        double dt,
        double gx,
        double gy,
        double gz,
        LogicSettings settings)
#pragma warning restore CA1822
    {
        var crossings = new List<string>();
        var violations = new List<string>();
        var pressureEntries = new List<double>();
        var pressureReleases = new List<double>();
        var coreEntries = new List<double>();
        var coreReleases = new List<double>();
        var headEntries = new List<double>();
        var headReleases = new List<double>();
        var lowerEntries = new List<double>();
        var lowerReleases = new List<double>();
        var modeTransitions = new List<double>();
        var segments = new List<IntegrationSegment>();
        var allStages = new List<IntegrationState>();
        var events = new List<IntegrationEvent>();
        var rateBuffer = new double[CirculationDimensions];

        var converged = true;
        var iterations = 0;
        var maxScaledResidual = 0.0;
        var state = initial;
        var offset = 0.0;
        var remaining = dt;
        var bounds = BloodBounds.None;
        if (initial.BloodCore <= 0.0) bounds |= BloodBounds.Core;
        if (initial.BloodHead <= settings.MinHeadBloodFraction) bounds |= BloodBounds.Head;
        if (initial.BloodLower <= 0.0) bounds |= BloodBounds.Lower;
        if (initial.BloodCore < 0.0)
            violations.Add($"initial core {initial.BloodCore:R} below zero");
        if (initial.BloodHead < settings.MinHeadBloodFraction)
            violations.Add($"initial head {initial.BloodHead:R} below head floor");
        if (initial.BloodLower < 0.0)
            violations.Add($"initial lower {initial.BloodLower:R} below zero");
        if (initial.CardioFatigue is < 0.0 or > 1.0)
            violations.Add($"initial cardio fatigue {initial.CardioFatigue:R} outside [0,1]");

        var guard = 0;
        while (remaining > 0.0 && converged && violations.Count == 0 && crossings.Count == 0)
        {
            if (++guard > 64)
            {
                violations.Add("interval exceeded accepted segment limit");
                break;
            }

            var released = false;
            foreach (var bound in HeldBounds(bounds))
            {
                var drive = BoundInwardDrive(bound, in state, gx, gy, gz, settings,
                    rateBuffer);
                var release = drive > 0.0;
                if (!release && drive == 0.0 && bound == BloodBounds.Core)
                {
                    EvaluateCirculation(in state, gx, gy, gz, settings, rateBuffer, null,
                        false);
                    release = rateBuffer[2] > 0.0;
                }

                if (!release) continue;
                if (bound == BloodBounds.Core) coreReleases.Add(offset);
                else if (bound == BloodBounds.Head) headReleases.Add(offset);
                else lowerReleases.Add(offset);
                bounds &= ~bound;
                released = true;
            }

            if (released) continue;

            var solve = AdvanceCirculationSegment(in state, remaining, gx, gy, gz, settings,
                bounds);
            if (!solve.Converged)
            {
                converged = false;
                crossings.AddRange(solve.ModeCrossings);
                violations.AddRange(solve.StateViolations);
                break;
            }

            var earliestTime = double.PositiveInfinity;
            var kind = IntervalEventKind.None;
            var eventBound = BloodBounds.None;
            var modeCoordinate = -1;
            var modeLevel = 0.0;
            (double Time, double HiBound) releaseGuess = default;

            if ((bounds & BloodBounds.Core) == 0)
            {
                var entry = EarliestCoreRoot(solve.HeadCoefficients, solve.LowerCoefficients,
                    remaining);
                if (entry.HasValue && entry.Value < earliestTime)
                {
                    earliestTime = entry.Value;
                    kind = IntervalEventKind.BoundEntry;
                    eventBound = BloodBounds.Core;
                }
            }

            if ((bounds & BloodBounds.Head) == 0)
            {
                var entry = EarliestLevelEntry(solve.HeadCoefficients,
                    settings.MinHeadBloodFraction, remaining);
                if (entry.HasValue && entry.Value < earliestTime)
                {
                    earliestTime = entry.Value;
                    kind = IntervalEventKind.BoundEntry;
                    eventBound = BloodBounds.Head;
                }
            }

            if ((bounds & BloodBounds.Lower) == 0)
            {
                var entry = EarliestLevelEntry(solve.LowerCoefficients, 0.0, remaining);
                if (entry.HasValue && entry.Value < earliestTime)
                {
                    earliestTime = entry.Value;
                    kind = IntervalEventKind.BoundEntry;
                    eventBound = BloodBounds.Lower;
                }
            }

            foreach (var bound in HeldBounds(bounds))
            {
                var guess = EarliestBoundRelease(bound, in state, solve, remaining, gx, gy,
                    gz, settings);
                if (guess.HasValue && guess.Value.Time < earliestTime)
                {
                    earliestTime = guess.Value.Time;
                    releaseGuess = guess.Value;
                    kind = IntervalEventKind.BoundRelease;
                    eventBound = bound;
                }
            }

            var transition = EarliestModeTransition(in state, solve, remaining,
                gx, gy, gz, settings);
            if (transition.HasValue && transition.Value.Time < earliestTime)
            {
                earliestTime = transition.Value.Time;
                kind = IntervalEventKind.ModeTransition;
                modeCoordinate = transition.Value.Coordinate;
                modeLevel = transition.Value.Level;
            }

            if (kind != IntervalEventKind.None)
            {
                double eventTime;
                SegmentSolve? prefix;
                bool localized;
                if (kind == IntervalEventKind.BoundEntry)
                    localized = LocalizeBoundEntry(eventBound, in state, remaining,
                        earliestTime, solve, bounds, gx, gy, gz, settings, violations,
                        out eventTime, out prefix);
                else if (kind == IntervalEventKind.BoundRelease)
                    localized = LocalizeBoundRelease(eventBound, in state, remaining,
                        releaseGuess, solve, bounds, gx, gy, gz, settings, violations,
                        out eventTime, out prefix);
                else
                    localized = LocalizeModeTransition(in state, remaining, earliestTime,
                        solve, modeCoordinate, modeLevel, bounds, gx, gy, gz, settings,
                        violations, out eventTime, out prefix);
                if (!localized) break;

                var completed = CompleteSegmentPressure(in state, prefix!, eventTime,
                    gz, settings);
                if (!completed.Converged || completed.ModeCrossings.Length != 0 ||
                    completed.StateViolations.Length != 0)
                {
                    converged &= completed.Converged;
                    crossings.AddRange(completed.ModeCrossings);
                    violations.AddRange(completed.StateViolations);
                    break;
                }

                var final = completed.Final;
                var acceptedStages = completed.Stages.ToArray();
                if (kind == IntervalEventKind.BoundEntry)
                {
                    var snapped = SnapToBounds(in final, bounds | eventBound, settings,
                        out var snapError);
                    if (!IsFiniteNumber(snapError) || snapError > 1e-10)
                    {
                        violations.Add($"bound entry snap volume error {snapError:R} " +
                                       $"at offset {eventTime:R}");
                        break;
                    }

                    final = snapped;
                    acceptedStages[acceptedStages.Length - 1] = final;
                    if (eventBound == BloodBounds.Core) coreEntries.Add(offset + eventTime);
                    else if (eventBound == BloodBounds.Head) headEntries.Add(offset + eventTime);
                    else lowerEntries.Add(offset + eventTime);
                    events.Add(new IntegrationEvent(eventBound + "Entry",
                        offset + eventTime, final));
                }
                else if (kind == IntervalEventKind.BoundRelease)
                {
                    if (eventBound == BloodBounds.Core) coreReleases.Add(offset + eventTime);
                    else if (eventBound == BloodBounds.Head) headReleases.Add(offset + eventTime);
                    else lowerReleases.Add(offset + eventTime);
                    events.Add(new IntegrationEvent(eventBound + "Release",
                        offset + eventTime, final));
                }
                else
                {
                    var modeCoefficients = modeCoordinate switch
                    {
                        0 => completed.HeadCoefficients,
                        2 => completed.RateCoefficients,
                        _ => completed.CardioCoefficients
                    };
                    var modeSlope = modeCoefficients[1] +
                        2.0 * modeCoefficients[2] * eventTime +
                        3.0 * modeCoefficients[3] * eventTime * eventTime;
                    var modeError = Math.Abs(
                        ModeCoordinateValue(in final, modeCoordinate) - modeLevel);
                    if (!IsFiniteNumber(modeError) || modeError >
                        Math.Abs(modeSlope) * PressureBoundRootToleranceSeconds + 1e-12)
                    {
                        violations.Add($"mode transition coordinate {modeCoordinate} " +
                                       $"snap correction {modeError:R} at offset " +
                                       $"{eventTime:R}");
                        break;
                    }

                    final = SnapModeLevel(in final, modeCoordinate, modeLevel);
                    acceptedStages[acceptedStages.Length - 1] = final;
                    modeTransitions.Add(offset + eventTime);
                    events.Add(new IntegrationEvent("ModeTransition" + modeCoordinate,
                        offset + eventTime, final));
                }

                iterations += completed.Iterations;
                maxScaledResidual = Math.Max(maxScaledResidual, completed.MaxScaledResidual);
                segments.Add(new IntegrationSegment(offset, eventTime, state, final,
                    acceptedStages, bounds, completed.HeadCoefficients,
                    completed.LowerCoefficients, completed.RateCoefficients,
                    completed.CardioCoefficients)
                {
                    CoefficientStart = offset
                });
                allStages.AddRange(acceptedStages);
                foreach (var entry in completed.PressureBoundEntries)
                    pressureEntries.Add(offset + entry);
                foreach (var release in completed.PressureBoundReleases)
                    pressureReleases.Add(offset + release);
                foreach (var entry in completed.PressureBoundEntries)
                    events.Add(new IntegrationEvent("PressureBoundEntry", offset + entry,
                        final));
                foreach (var release in completed.PressureBoundReleases)
                    events.Add(new IntegrationEvent("PressureBoundRelease", offset + release,
                        final));
                state = final;
                offset += eventTime;
                remaining -= eventTime;
                if (kind == IntervalEventKind.BoundEntry) bounds |= eventBound;
                else if (kind == IntervalEventKind.BoundRelease) bounds &= ~eventBound;
                continue;
            }

            if (solve.ModeCrossings.Length != 0 || solve.StateViolations.Length != 0)
            {
                crossings.AddRange(solve.ModeCrossings);
                violations.AddRange(solve.StateViolations);
                break;
            }

            var whole = CompleteSegmentPressure(in state, solve, remaining, gz, settings);
            if (!whole.Converged || whole.ModeCrossings.Length != 0 ||
                whole.StateViolations.Length != 0)
            {
                converged &= whole.Converged;
                crossings.AddRange(whole.ModeCrossings);
                violations.AddRange(whole.StateViolations);
                break;
            }

            iterations += whole.Iterations;
            maxScaledResidual = Math.Max(maxScaledResidual, whole.MaxScaledResidual);
            segments.Add(new IntegrationSegment(offset, remaining, state, whole.Final,
                whole.Stages, bounds, whole.HeadCoefficients, whole.LowerCoefficients,
                whole.RateCoefficients, whole.CardioCoefficients)
            {
                CoefficientStart = offset
            });
            allStages.AddRange(whole.Stages);
            foreach (var entry in whole.PressureBoundEntries)
            {
                pressureEntries.Add(offset + entry);
                events.Add(new IntegrationEvent("PressureBoundEntry", offset + entry,
                    whole.Final));
            }

            foreach (var release in whole.PressureBoundReleases)
            {
                pressureReleases.Add(offset + release);
                events.Add(new IntegrationEvent("PressureBoundRelease", offset + release,
                    whole.Final));
            }
            state = whole.Final;
            offset += remaining;
            remaining = 0.0;
        }

        var fullyAdvanced = converged && remaining <= 0.0 && violations.Count == 0 &&
                            crossings.Count == 0;
        return new IntegrationResult(initial, state, allStages.ToArray(), iterations,
            maxScaledResidual, fullyAdvanced, crossings.ToArray(), violations.ToArray(),
            pressureEntries.ToArray(), pressureReleases.ToArray(), coreEntries.ToArray(),
            coreReleases.ToArray(), headEntries.ToArray(), headReleases.ToArray(),
            lowerEntries.ToArray(), lowerReleases.ToArray(), modeTransitions.ToArray(),
            segments.ToArray(), events.ToArray());
    }

    private enum IntervalEventKind
    {
        None,
        BoundEntry,
        BoundRelease,
        ModeTransition
    }

    private static IEnumerable<BloodBounds> HeldBounds(BloodBounds bounds)
    {
        if ((bounds & BloodBounds.Core) != 0) yield return BloodBounds.Core;
        if ((bounds & BloodBounds.Head) != 0) yield return BloodBounds.Head;
        if ((bounds & BloodBounds.Lower) != 0) yield return BloodBounds.Lower;
    }

    private static double BoundInwardDrive(BloodBounds bound, in IntegrationState state,
        double gx, double gy, double gz, LogicSettings settings, double[] rateBuffer)
    {
        if (bound == BloodBounds.Core)
            return HeldCoreDrive(state.HeartRateMultiplier,
                AdjustedShiftRate(in state, gx, gy, gz, settings), settings);
        EvaluateCirculation(in state, gx, gy, gz, settings, rateBuffer, null, false);
        return bound == BloodBounds.Head ? rateBuffer[0] : rateBuffer[1];
    }

    private static IntegrationState DenseSegmentState(in IntegrationState initial,
        SegmentSolve solve, double offset, double gx, double gz, LogicSettings settings)
    {
        var independent = IndependentCirculationStateAt(in initial, offset, gx, gz, settings);
        return independent with
        {
            BloodHead = NumericalMath.EvaluateCubic(solve.HeadCoefficients, offset),
            BloodLower = NumericalMath.EvaluateCubic(solve.LowerCoefficients, offset),
            HeartRateMultiplier = NumericalMath.EvaluateCubic(solve.RateCoefficients, offset),
            CardioFatigue = NumericalMath.EvaluateCubic(solve.CardioCoefficients, offset)
        };
    }

    private static (double Time, double HiBound)? EarliestBoundRelease(
        BloodBounds bound, in IntegrationState initial, SegmentSolve solve,
        double duration, double gx, double gy, double gz, LogicSettings settings)
    {
        var nodes = new[]
        {
            0.0, NumericalMath.RadauC[0] * duration, NumericalMath.RadauC[1] * duration,
            duration
        };
        var rateBuffer = new double[CirculationDimensions];
        var drives = new double[4];
        for (var node = 0; node < nodes.Length; node++)
        {
            var dense = DenseSegmentState(in initial, solve, nodes[node], gx, gz, settings);
            drives[node] = BoundInwardDrive(bound, in dense, gx, gy, gz, settings,
                rateBuffer);
        }

        var driveCoefficients = NumericalMath.CubicCoefficients(nodes, drives);
        var monotoneBounds = MonotoneBounds(driveCoefficients, duration);
        for (var segment = 0; segment < monotoneBounds.Count - 1; segment++)
        {
            var lo = monotoneBounds[segment];
            var hi = monotoneBounds[segment + 1];
            var loValue = NumericalMath.EvaluateCubic(driveCoefficients, lo);
            var hiValue = NumericalMath.EvaluateCubic(driveCoefficients, hi);
            if (loValue <= 0.0 && hiValue > 0.0)
                return (BisectCubicLevel(driveCoefficients, 0.0, lo, hi, loValue), hi);
        }

        return null;
    }

    private static double? EarliestLevelEntry(double[] coefficients, double level,
        double duration)
    {
        var bounds = MonotoneBounds(coefficients, duration);
        for (var segment = 0; segment < bounds.Count - 1; segment++)
        {
            var lo = bounds[segment];
            var hi = bounds[segment + 1];
            var loValue = NumericalMath.EvaluateCubic(coefficients, lo);
            var hiValue = NumericalMath.EvaluateCubic(coefficients, hi);
            if (loValue == level && lo > 0.0) return lo;
            if (loValue > level && hiValue <= level)
                return hiValue == level
                    ? hi
                    : BisectCubicLevel(coefficients, level, lo, hi, loValue);
        }

        return null;
    }

    private static double? EarliestSignCrossing(double[] coefficients, double level,
        double duration)
    {
        var bounds = MonotoneBounds(coefficients, duration);
        for (var segment = 0; segment < bounds.Count - 1; segment++)
        {
            var lo = bounds[segment];
            var hi = bounds[segment + 1];
            var loValue = NumericalMath.EvaluateCubic(coefficients, lo) - level;
            var hiValue = NumericalMath.EvaluateCubic(coefficients, hi) - level;
            if (loValue == 0.0 && lo > 0.0) return lo;
            if (hiValue == 0.0) return hi;
            if (loValue != 0.0 && Math.Sign(hiValue) != 0 &&
                Math.Sign(loValue) != Math.Sign(hiValue))
                return BisectCubicLevel(coefficients, level, lo, hi, loValue + level);
        }

        return null;
    }

    private static (double Time, int Coordinate, double Level)? EarliestModeTransition(
        in IntegrationState initial, SegmentSolve solve, double duration,
        double gx, double gy, double gz, LogicSettings settings)
    {
        var headSurfaces = new List<double> { settings.RestingBloodHead };
        var bradyLevel = settings.RestingBloodHead * (1.0 + settings.NegativeGHeartStopOverfill);
        if (bradyLevel > settings.RestingBloodHead) headSurfaces.Add(bradyLevel);
        if (settings.BaroreceptorGain > 0.0 && settings.MaxHeartRateMultiplier > 1.0)
        {
            var headStar = settings.RestingBloodHead *
                (1.0 - (settings.MaxHeartRateMultiplier - 1.0) / settings.BaroreceptorGain);
            if (headStar > 0.0 && headStar < settings.RestingBloodHead)
                headSurfaces.Add(headStar);
        }

        (double Time, int Coordinate, double Level)? earliest = null;
        foreach (var level in headSurfaces)
        {
            var crossing = EarliestSignCrossing(solve.HeadCoefficients, level, duration);
            if (!crossing.HasValue ||
                !ModeFlipsAcross(in initial, solve, duration, crossing.Value,
                    gx, gy, gz, settings))
                continue;
            if (earliest == null || crossing.Value < earliest.Value.Time)
                earliest = (crossing.Value, 0, level);
        }

        var rateLevel = 1.0 + settings.CardioFatigueHrElevationThreshold;
        var rateCrossing = EarliestSignCrossing(solve.RateCoefficients, rateLevel, duration);
        if (rateCrossing.HasValue &&
            ModeFlipsAcross(in initial, solve, duration, rateCrossing.Value,
                gx, gy, gz, settings) &&
            (earliest == null || rateCrossing.Value < earliest.Value.Time))
            earliest = (rateCrossing.Value, 2, rateLevel);

        var cardioCrossing = EarliestSignCrossing(solve.CardioCoefficients, 1.0, duration);
        if (cardioCrossing.HasValue &&
            ModeFlipsAcross(in initial, solve, duration, cardioCrossing.Value,
                gx, gy, gz, settings) &&
            (earliest == null || cardioCrossing.Value < earliest.Value.Time))
            earliest = (cardioCrossing.Value, 3, 1.0);

        return earliest;
    }

    private static bool ModeFlipsAcross(in IntegrationState initial, SegmentSolve solve,
        double duration, double time, double gx, double gy, double gz,
        LogicSettings settings)
    {
        var epsilon = Math.Min(1e-4, Math.Max(duration * 1e-4, 1e-8));
        var before = DenseSegmentState(in initial, solve, Math.Max(0.0, time - epsilon),
            gx, gz, settings);
        var after = DenseSegmentState(in initial, solve, Math.Min(duration, time + epsilon),
            gx, gz, settings);
        var rateBuffer = new double[CirculationDimensions];
        var modeBefore = EvaluateCirculation(in before, gx, gy, gz, settings, rateBuffer,
            null, false);
        var modeAfter = EvaluateCirculation(in after, gx, gy, gz, settings, rateBuffer,
            null, false);
        return ((modeBefore ^ modeAfter) & CrossingBits) != 0;
    }

    private static double ModeCoordinateValue(in IntegrationState state, int coordinate) =>
        coordinate switch
        {
            0 => state.BloodHead,
            2 => state.HeartRateMultiplier,
            _ => state.CardioFatigue
        };

    private static IntegrationState SnapModeLevel(in IntegrationState state, int coordinate,
        double level)
    {
        if (coordinate == 0)
        {
            var free = 1.0 - state.BloodHead;
            var scale = free > 0.0 ? (1.0 - level) / free : 0.0;
            return state with
            {
                BloodHead = level,
                BloodLower = state.BloodLower * scale
            };
        }

        return coordinate == 2
            ? state with { HeartRateMultiplier = level }
            : state with { CardioFatigue = level };
    }

    private static IntegrationState SnapToBounds(in IntegrationState state,
        BloodBounds bounds, LogicSettings settings, out double snapError)
    {
        var boundSum = (bounds & BloodBounds.Head) != 0
            ? settings.MinHeadBloodFraction
            : 0.0;
        var freeSum = ((bounds & BloodBounds.Head) == 0 ? state.BloodHead : 0.0) +
                      ((bounds & BloodBounds.Lower) == 0 ? state.BloodLower : 0.0) +
                      ((bounds & BloodBounds.Core) == 0 ? state.BloodCore : 0.0);
        var scale = freeSum > 0.0 ? (1.0 - boundSum) / freeSum : 0.0;
        var head = (bounds & BloodBounds.Head) != 0
            ? settings.MinHeadBloodFraction
            : state.BloodHead * scale;
        var lower = (bounds & BloodBounds.Lower) != 0
            ? 0.0
            : state.BloodLower * scale;
        var core = 1.0 - head - lower;
        snapError = Math.Max(Math.Abs(head - state.BloodHead),
            Math.Max(Math.Abs(lower - state.BloodLower),
                Math.Abs(core - state.BloodCore)));
        return state with { BloodHead = head, BloodLower = lower };
    }

    private static SegmentSolve AdvanceCirculationSegment(
        in IntegrationState initial,
        double dt,
        double gx,
        double gy,
        double gz,
        LogicSettings settings,
        BloodBounds bounds)
    {
        const int stageCount = NumericalMath.RadauStageCount;
        const int dimensions = CirculationSolvedDimensions;
        const int unknowns = stageCount * dimensions;
        var initialValues = new[]
        {
            initial.BloodHead, initial.BloodLower, initial.HeartRateMultiplier,
            initial.CardioFatigue
        };
        var stages = new double[unknowns];
        var trialStages = new double[unknowns];
        var stageRates = new double[unknowns];
        var stageJacobians = new double[unknowns * dimensions];
        var residual = new double[unknowns];
        var newtonMatrix = NumericalMath.CreateMatrix(unknowns);
        var newtonStep = new double[unknowns];
        var rateBuffer = new double[CirculationDimensions];
        var jacobianBuffer = NumericalMath.CreateMatrix(CirculationDimensions);
        var initialPressure = initial.CerebralPressureImpairment;
        var generalViolations = new List<string>();
        var boundViolations = new List<string>();
        var boundFlags = BloodBounds.None;
        var initialState = initial;

        var initialMode = Evaluate(in initialState, rateBuffer, null);
        if ((initialMode & CirculationMode.UnsupportedCoreBound) != 0)
            generalViolations.Add("unsupported bound state at segment start");
        var cardioHeldBuild = (initialMode & CirculationMode.CardioBuild) != 0 &&
                              (initialMode & CirculationMode.CardioCeilingBound) == 0;
        var cardioExact = new double[stageCount];
        for (var stage = 0; stage < stageCount; stage++)
            cardioExact[stage] = (initialMode & CirculationMode.CardioCeilingBound) != 0
                ? initial.CardioFatigue
                : initial.CardioFatigue * Math.Exp(-NumericalMath.RadauC[stage] * dt /
                                                   settings.CardioFatigueRecoveryTau);
        for (var stage = 0; stage < stageCount; stage++)
            for (var k = 0; k < dimensions; k++)
                stages[stage * dimensions + k] = k == 3 && !cardioHeldBuild
                    ? cardioExact[stage]
                    : initialValues[k] + NumericalMath.RadauC[stage] * dt * rateBuffer[k];

        var iterations = 0;
        var maxScaledResidual = double.MaxValue;
        var converged = false;

        while (iterations < MaxNewtonIterations)
        {
            maxScaledResidual = ComputeResidual(stages);
            if (maxScaledResidual <= ScaledResidualTolerance || double.IsNaN(maxScaledResidual)) break;

            for (var j = 0; j < stageCount; j++)
            {
                var stageState = StageState(stages, j);
                Evaluate(in stageState, rateBuffer, jacobianBuffer);
                for (var k = 0; k < dimensions; k++)
                    for (var m = 0; m < dimensions; m++)
                        stageJacobians[(j * dimensions + k) * dimensions + m] =
                            jacobianBuffer[k][m];
                for (var m = 0; m < dimensions; m++)
                    stageJacobians[(j * dimensions + 3) * dimensions + m] =
                        cardioHeldBuild && m == 2 ? settings.CardioFatigueBuildRate : 0.0;
            }

            for (var i = 0; i < stageCount; i++)
                for (var j = 0; j < stageCount; j++)
                    for (var k = 0; k < dimensions; k++)
                        for (var m = 0; m < dimensions; m++)
                            newtonMatrix[i * dimensions + k][j * dimensions + m] =
                                (i == j && k == m ? 1.0 : 0.0) -
                                dt * NumericalMath.RadauA[i][j] *
                                stageJacobians[(j * dimensions + k) * dimensions + m];

            if (!NumericalMath.TrySolveLinearInPlace(newtonMatrix, residual, newtonStep)) break;

            var lambda = 1.0;
            var accepted = false;
            for (var halving = 0; halving <= MaxDampingHalvings; halving++)
            {
                for (var u = 0; u < unknowns; u++) trialStages[u] = stages[u] - lambda * newtonStep[u];
                if (ComputeResidual(trialStages) < maxScaledResidual)
                {
                    (stages, trialStages) = (trialStages, stages);
                    maxScaledResidual = ComputeResidual(stages);
                    accepted = true;
                    break;
                }

                lambda *= 0.5;
            }

            if (!accepted) break;
            iterations++;
        }

        maxScaledResidual = ComputeResidual(stages);
        converged = IsFiniteNumber(maxScaledResidual) && maxScaledResidual <= ScaledResidualTolerance;

        if (bounds != BloodBounds.None)
        {
            for (var stage = 0; stage < stageCount; stage++)
            {
                var head = stages[stage * dimensions];
                var lower = stages[stage * dimensions + 1];
                if ((bounds & BloodBounds.Head) != 0)
                {
                    var headCorrection = settings.MinHeadBloodFraction - head;
                    if (Math.Abs(headCorrection) > ScaledResidualTolerance)
                        generalViolations.Add(
                            $"held stage {stage + 1} head off floor by {headCorrection:R}");
                    head = settings.MinHeadBloodFraction;
                }

                if ((bounds & BloodBounds.Lower) != 0)
                {
                    if (Math.Abs(lower) > ScaledResidualTolerance)
                        generalViolations.Add(
                            $"held stage {stage + 1} lower off zero by {lower:R}");
                    lower = 0.0;
                }

                if ((bounds & BloodBounds.Core) != 0)
                {
                    var correction = 1.0 - head - lower;
                    if (Math.Abs(correction) > ScaledResidualTolerance)
                        generalViolations.Add(
                            $"held stage {stage + 1} off blood manifold by {correction:R}");
                    if ((bounds & BloodBounds.Head) != 0)
                        lower = 1.0 - head;
                    else if ((bounds & BloodBounds.Lower) != 0)
                        head = 1.0;
                    else
                        lower = 1.0 - head;
                }

                stages[stage * dimensions] = head;
                stages[stage * dimensions + 1] = lower;
            }

            var substitutedResidual = ComputeResidual(stages);
            maxScaledResidual = Math.Max(maxScaledResidual, substitutedResidual);
            if (converged && (!IsFiniteNumber(substitutedResidual) ||
                              substitutedResidual > ScaledResidualTolerance))
            {
                converged = false;
                generalViolations.Add(
                    $"held-manifold substitution residual {substitutedResidual:R}");
            }
        }

        var headNodes = new[]
        {
            0.0, NumericalMath.RadauC[0] * dt, NumericalMath.RadauC[1] * dt, dt
        };
        var headValues = new[]
        {
            initial.BloodHead, stages[0], stages[dimensions], stages[2 * dimensions]
        };
        var headCoefficients = NumericalMath.CubicCoefficients(headNodes, headValues);
        var lowerValues = new[]
        {
            initial.BloodLower, stages[1], stages[dimensions + 1], stages[2 * dimensions + 1]
        };
        var lowerCoefficients = bounds == BloodBounds.Core
            ? new[]
            {
                1.0 - headCoefficients[0], -headCoefficients[1], -headCoefficients[2],
                -headCoefficients[3]
            }
            : NumericalMath.CubicCoefficients(headNodes, lowerValues);
        var rateValues = new[]
        {
            initial.HeartRateMultiplier, stages[2], stages[dimensions + 2],
            stages[2 * dimensions + 2]
        };
        var rateCoefficients = NumericalMath.CubicCoefficients(headNodes, rateValues);
        var cardioValues = new[]
        {
            initial.CardioFatigue, stages[3], stages[dimensions + 3],
            stages[2 * dimensions + 3]
        };
        var cardioCoefficients = NumericalMath.CubicCoefficients(headNodes, cardioValues);
        boundFlags |= ValidateDenseBlood(headCoefficients, lowerCoefficients, dt, settings,
            bounds, generalViolations, boundViolations);

        var stageStates = new IntegrationState[stageCount];
        for (var stage = 0; stage < stageCount; stage++)
            stageStates[stage] = StageState(stages, stage);

        List<string> crossings = [];
        for (var stage = 0; stage < stageCount; stage++)
        {
            var stageState = stageStates[stage];
            var stageMode = Evaluate(in stageState, rateBuffer, null);
            var difference = stageMode ^ initialMode;
            if ((difference & CrossingBits) != 0)
                crossings.Add($"stage {stage + 1}: mode {stageMode} differs from initial {initialMode}");
            if ((stageMode & CirculationMode.UnsupportedCoreBound) != 0)
                generalViolations.Add($"stage {stage + 1}: unsupported bound state");

            boundFlags |= CheckStageValidity(stageState, stage, settings, bounds,
                generalViolations, boundViolations);
        }

        var violations = generalViolations.Concat(boundViolations).ToArray();
        var rootSearchValid = converged && crossings.Count == 0 &&
                              generalViolations.Count == 0;
        return new SegmentSolve(stageStates[stageCount - 1], stageStates, iterations,
            maxScaledResidual, converged, crossings.ToArray(), violations,
            [], [], headCoefficients,
            lowerCoefficients, rateCoefficients, cardioCoefficients, rootSearchValid,
            boundFlags);

        CirculationMode Evaluate(in IntegrationState candidate, double[] rates,
            double[][]? jacobian) =>
            bounds == BloodBounds.None
                ? EvaluateCirculation(in candidate, gx, gy, gz, settings, rates, jacobian, false)
                : EvaluateBoundConstrainedCirculation(in candidate, gx, gy, gz, settings,
                    rates, jacobian, false, bounds);

        IntegrationState StageState(double[] vector, int stage)
        {
            var independent = IndependentCirculationStateAt(in initialState,
                NumericalMath.RadauC[stage] * dt, gx, gz, settings);
            return independent with
            {
                BloodHead = vector[stage * dimensions],
                BloodLower = vector[stage * dimensions + 1],
                HeartRateMultiplier = vector[stage * dimensions + 2],
                CardioFatigue = vector[stage * dimensions + 3],
                CerebralPressureImpairment = initialPressure
            };
        }

        double ComputeResidual(double[] vector)
        {
            for (var j = 0; j < stageCount; j++)
            {
                var stageState = StageState(vector, j);
                Evaluate(in stageState, rateBuffer, null);
                for (var k = 0; k < dimensions; k++)
                    stageRates[j * dimensions + k] = rateBuffer[k];
                if (cardioHeldBuild)
                    stageRates[j * dimensions + 3] = settings.CardioFatigueBuildRate *
                        (stageState.HeartRateMultiplier - 1.0 -
                         settings.CardioFatigueHrElevationThreshold);
            }

            var maximum = 0.0;
            for (var i = 0; i < stageCount; i++)
                for (var k = 0; k < dimensions; k++)
                {
                    double component;
                    if (k == 3 && !cardioHeldBuild)
                    {
                        component = vector[i * dimensions + 3] - cardioExact[i];
                    }
                    else
                    {
                        var sum = 0.0;
                        for (var j = 0; j < stageCount; j++)
                            sum += NumericalMath.RadauA[i][j] * stageRates[j * dimensions + k];
                        component = vector[i * dimensions + k] - initialValues[k] - dt * sum;
                    }

                    residual[i * dimensions + k] = component;
                    maximum = Math.Max(maximum, Math.Abs(component) / (1.0 + Math.Abs(vector[i * dimensions + k])));
                }

            return maximum;
        }
    }
#pragma warning restore CA1822

    private static SegmentSolve CompleteSegmentPressure(
        in IntegrationState initial,
        SegmentSolve bloodSolve,
        double dt,
        double gz,
        LogicSettings settings)
    {
        var diagnostics = new List<string>(bloodSolve.StateViolations);
        var stageOffsets = new[]
        {
            NumericalMath.RadauC[0] * dt, NumericalMath.RadauC[1] * dt,
            NumericalMath.RadauC[2] * dt
        };
        var stagePressures = ReconstructIntervalPressure(
            initial.CerebralPressureImpairment,
            1.0 / settings.CerebralPressureImpairmentRecoveryTau,
            bloodSolve.HeadCoefficients, stageOffsets, dt, gz, settings, diagnostics,
            out var pressureBoundEntries, out var pressureBoundReleases);

        var bloodStages = bloodSolve.Stages;
        var stageStates = new IntegrationState[bloodStages.Length];
        for (var stage = 0; stage < bloodStages.Length; stage++)
        {
            var pressure = stagePressures[stage];
            if (!IsFiniteNumber(pressure) || pressure is < 0.0 or > 1.0)
                diagnostics.Add($"stage {stage + 1}: CerebralPressureImpairment " +
                                $"{pressure:R} outside [0,1]");
            stageStates[stage] = bloodStages[stage] with
            {
                CerebralPressureImpairment = pressure
            };
        }

        return bloodSolve with
        {
            Final = stageStates[stageStates.Length - 1],
            Stages = stageStates,
            StateViolations = diagnostics.ToArray(),
            PressureBoundEntries = pressureBoundEntries,
            PressureBoundReleases = pressureBoundReleases,
            CoreRootSearchValid = false
        };
    }

    private static BloodBounds ValidateDenseBlood(
        double[] headCoefficients,
        double[] lowerCoefficients,
        double dt,
        LogicSettings settings,
        BloodBounds bounds,
        List<string> generalDiagnostics,
        List<string> boundDiagnostics)
    {
        var boundFlags = BloodBounds.None;
        var criticalBuffer = new double[2];
        var sampleOffsets = new List<double> { 0.0, dt };
        var headCritical = NumericalMath.QuadraticRootsInInterval(
            3.0 * headCoefficients[3], 2.0 * headCoefficients[2], headCoefficients[1],
            0.0, dt, criticalBuffer);
        for (var root = 0; root < headCritical; root++) sampleOffsets.Add(criticalBuffer[root]);
        var lowerCritical = NumericalMath.QuadraticRootsInInterval(
            3.0 * lowerCoefficients[3], 2.0 * lowerCoefficients[2], lowerCoefficients[1],
            0.0, dt, criticalBuffer);
        for (var root = 0; root < lowerCritical; root++) sampleOffsets.Add(criticalBuffer[root]);
        var coreCritical = NumericalMath.QuadraticRootsInInterval(
            -3.0 * (headCoefficients[3] + lowerCoefficients[3]),
            -2.0 * (headCoefficients[2] + lowerCoefficients[2]),
            -(headCoefficients[1] + lowerCoefficients[1]),
            0.0, dt, criticalBuffer);
        for (var root = 0; root < coreCritical; root++) sampleOffsets.Add(criticalBuffer[root]);
        foreach (var node in NumericalMath.GaussLegendre16Nodes)
            sampleOffsets.Add(node * dt);

        foreach (var offset in sampleOffsets)
        {
            var head = NumericalMath.EvaluateCubic(headCoefficients, offset);
            var lower = NumericalMath.EvaluateCubic(lowerCoefficients, offset);
            var core = 1.0 - head - lower;
            if (!IsFiniteNumber(head) || !IsFiniteNumber(lower) || !IsFiniteNumber(core))
                generalDiagnostics.Add($"dense blood non-finite at offset {offset:R}: " +
                                       $"head {head:R}, lower {lower:R}, core {core:R}");
            CheckBoundResidual(BloodBounds.Head, head - settings.MinHeadBloodFraction,
                $"dense head blood {head:R} below head floor at offset {offset:R}");
            CheckBoundResidual(BloodBounds.Lower, lower,
                $"dense lower blood {lower:R} negative at offset {offset:R}");
            CheckBoundResidual(BloodBounds.Core, core,
                $"dense core blood {core:R} negative at offset {offset:R}");
        }

        return boundFlags;

        void CheckBoundResidual(BloodBounds bound, double residual, string message)
        {
            var limit = (bounds & bound) != 0 ? -1e-12 : 0.0;
            if (residual >= limit) return;
            boundFlags |= bound;
            ((bounds & bound) != 0 ? generalDiagnostics : boundDiagnostics).Add(message);
        }
    }

    private static double? EarliestCoreRoot(
        double[] headCoefficients, double[] lowerCoefficients, double duration)
    {
        var coreCoefficients = new[]
        {
            1.0 - headCoefficients[0] - lowerCoefficients[0],
            -(headCoefficients[1] + lowerCoefficients[1]),
            -(headCoefficients[2] + lowerCoefficients[2]),
            -(headCoefficients[3] + lowerCoefficients[3])
        };
        var bounds = MonotoneBounds(coreCoefficients, duration);
        for (var segment = 0; segment < bounds.Count - 1; segment++)
        {
            var lo = bounds[segment];
            var hi = bounds[segment + 1];
            var loValue = NumericalMath.EvaluateCubic(coreCoefficients, lo);
            var hiValue = NumericalMath.EvaluateCubic(coreCoefficients, hi);
            if (loValue == 0.0 && lo > 0.0) return lo;
            if (loValue > 0.0 && hiValue <= 0.0)
                return hiValue == 0.0
                    ? hi
                    : BisectCubicLevel(coreCoefficients, 0.0, lo, hi, loValue);
        }

        return null;
    }

    private static List<double> MonotoneBounds(double[] coefficients, double duration)
    {
        var criticalBuffer = new double[2];
        var criticalCount = NumericalMath.QuadraticRootsInInterval(
            3.0 * coefficients[3], 2.0 * coefficients[2], coefficients[1],
            0.0, duration, criticalBuffer);
        var bounds = new List<double>(criticalCount + 2) { 0.0 };
        for (var root = 0; root < criticalCount; root++) bounds.Add(criticalBuffer[root]);
        bounds.Add(duration);
        return bounds;
    }

    private static double BisectCubicLevel(
        double[] coefficients, double level, double lo, double hi, double loValue)
    {
        var value = loValue - level;
        while (hi - lo > PressureBoundRootToleranceSeconds)
        {
            var mid = 0.5 * (lo + hi);
            var midValue = NumericalMath.EvaluateCubic(coefficients, mid) - level;
            if (midValue == 0.0) return mid;
            if ((midValue < 0.0) == (value < 0.0))
            {
                lo = mid;
                value = midValue;
            }
            else
            {
                hi = mid;
            }
        }

        return 0.5 * (lo + hi);
    }

    private static double BoundResidual(in IntegrationState state, BloodBounds bound,
        LogicSettings settings) => bound switch
        {
            BloodBounds.Core => state.BloodCore,
            BloodBounds.Head => state.BloodHead - settings.MinHeadBloodFraction,
            _ => state.BloodLower
        };

    private static bool LocalizeBoundEntry(
        BloodBounds bound,
        in IntegrationState state,
        double duration,
        double denseGuess,
        SegmentSolve fullSolve,
        BloodBounds bounds,
        double gx,
        double gy,
        double gz,
        LogicSettings settings,
        List<string> diagnostics,
        out double entryTime,
        out SegmentSolve? prefix)
    {
        var rateBuffer = new double[CirculationDimensions];
        var lowerBound = 0.0;
        var lowerResidual = BoundResidual(in state, bound, settings);
        var upperBound = double.NaN;
        var upperResidual = double.NaN;
        var fullFinal = fullSolve.Final;
        var fullResidual = BoundResidual(in fullFinal, bound, settings);
        if (fullSolve.CoreRootSearchValid && fullResidual < 0.0 &&
            (fullSolve.BoundViolations & ~bound) == 0)
        {
            upperBound = duration;
            upperResidual = fullResidual;
        }

        SegmentSolve? best = null;
        var bestTime = 0.0;
        var bestResidual = double.NaN;
        var localized = false;

        for (var iteration = 0; iteration < 80 && !localized; iteration++)
        {
            double candidate;
            if (iteration == 0)
            {
                candidate = Math.Min(Math.Max(denseGuess, 1e-10),
                    IsFiniteNumber(upperBound) ? upperBound - 1e-10 : duration);
            }
            else if (IsFiniteNumber(upperBound))
            {
                candidate = IsFiniteNumber(upperResidual) && upperResidual < 0.0 &&
                            lowerResidual > 0.0
                    ? lowerBound + (upperBound - lowerBound) * lowerResidual /
                      (lowerResidual - upperResidual)
                    : double.NaN;
                if (!IsFiniteNumber(candidate) || candidate <= lowerBound ||
                    candidate >= upperBound)
                    candidate = 0.5 * (lowerBound + upperBound);
            }
            else
            {
                candidate = 0.5 * (lowerBound + duration);
            }

            var trial = AdvanceCirculationSegment(in state, candidate, gx, gy, gz, settings,
                bounds);
            var acceptable = trial.Converged && trial.ModeCrossings.Length == 0 &&
                             trial.StateViolations.Length == 0;
            var trialFinal = trial.Final;
            var endResidual = BoundResidual(in trialFinal, bound, settings);
            if (acceptable && endResidual >= 0.0)
            {
                lowerBound = candidate;
                lowerResidual = endResidual;
                if (best == null || endResidual < bestResidual)
                {
                    best = trial;
                    bestTime = candidate;
                    bestResidual = endResidual;
                }
            }
            else
            {
                upperBound = candidate;
                upperResidual = trial.CoreRootSearchValid && endResidual < 0.0 &&
                                (trial.BoundViolations & ~bound) == 0
                    ? endResidual
                    : double.NaN;
            }

            if (best != null)
            {
                if (bestResidual <= 1e-12)
                {
                    var bestFinal = best.Final;
                    var closingRate = BoundClosingRate(bound, in bestFinal, bounds,
                        gx, gy, gz, settings, rateBuffer);
                    if (closingRate < 0.0 &&
                        Math.Abs(bestResidual / closingRate) <=
                        PressureBoundRootToleranceSeconds)
                        localized = true;
                }

                if (IsFiniteNumber(upperBound) &&
                    upperBound - lowerBound <= PressureBoundRootToleranceSeconds &&
                    bestResidual <= 1e-10)
                    localized = true;
            }

            if (!localized && IsFiniteNumber(upperBound) &&
                upperBound - lowerBound <= 1e-12)
                break;
        }

        if (!localized || best == null || bestResidual > 1e-10)
        {
            diagnostics.Add($"bound {bound} entry could not produce an in-bound prefix");
            entryTime = 0.0;
            prefix = null;
            return false;
        }

        entryTime = bestTime;
        prefix = best;
        return true;
    }

    private static double BoundClosingRate(BloodBounds bound, in IntegrationState state,
        BloodBounds bounds, double gx, double gy, double gz, LogicSettings settings,
        double[] rateBuffer)
    {
        if (bounds == BloodBounds.None)
            EvaluateCirculation(in state, gx, gy, gz, settings, rateBuffer, null, false);
        else
            EvaluateBoundConstrainedCirculation(in state, gx, gy, gz, settings, rateBuffer,
                null, false, bounds);
        return bound switch
        {
            BloodBounds.Head => rateBuffer[0],
            BloodBounds.Lower => rateBuffer[1],
            _ => -(rateBuffer[0] + rateBuffer[1])
        };
    }

    private static bool LocalizeBoundRelease(
        BloodBounds bound,
        in IntegrationState state,
        double duration,
        (double Time, double HiBound) guess,
        SegmentSolve fullSolve,
        BloodBounds bounds,
        double gx,
        double gy,
        double gz,
        LogicSettings settings,
        List<string> diagnostics,
        out double releaseTime,
        out SegmentSolve? prefix)
    {
        var rateBuffer = new double[CirculationDimensions];
        var lowerBound = 0.0;
        var lowerDrive = BoundInwardDrive(bound, in state, gx, gy, gz, settings, rateBuffer);
        var lowerWeight = lowerDrive;
        var upperBound = double.NaN;
        var upperDrive = double.NaN;
        var upperWeight = double.NaN;
        var lastSide = 0;
        SegmentSolve? best = null;
        var bestTime = 0.0;

        if (fullSolve.Converged && fullSolve.ModeCrossings.Length == 0 &&
            fullSolve.StateViolations.Length == 0)
        {
            var fullFinal = fullSolve.Final;
            var fullDrive = BoundInwardDrive(bound, in fullFinal, gx, gy, gz,
                settings, rateBuffer);
            if (fullDrive > 0.0)
            {
                upperBound = duration;
                upperDrive = fullDrive;
                upperWeight = upperDrive;
            }
        }

        var localized = false;
        var probeIndex = 0;
        double? retryCandidate = null;
        for (var iteration = 0; iteration < 60 && !localized; iteration++)
        {
            double candidate;
            if (retryCandidate.HasValue)
            {
                candidate = retryCandidate.Value;
                retryCandidate = null;
            }
            else if (!IsFiniteNumber(upperBound))
            {
                if (probeIndex > 1) break;
                candidate = probeIndex == 0 ? guess.Time : guess.HiBound;
                probeIndex++;
                candidate = Math.Min(Math.Max(candidate, 1e-10), duration);
                if (candidate <= lowerBound) continue;
            }
            else if (iteration == 0 && guess.Time > lowerBound && guess.Time < upperBound)
            {
                candidate = guess.Time;
            }
            else
            {
                candidate = lowerBound + (upperBound - lowerBound) * lowerWeight /
                    (lowerWeight - upperWeight);
                if (!IsFiniteNumber(candidate) || candidate <= lowerBound ||
                    candidate >= upperBound)
                    candidate = 0.5 * (lowerBound + upperBound);
            }

            var trial = AdvanceCirculationSegment(in state, candidate, gx, gy, gz, settings,
                bounds);
            if (!trial.Converged || trial.ModeCrossings.Length != 0 ||
                trial.StateViolations.Length != 0)
            {
                var retry = lowerBound + (candidate - lowerBound) * 0.5;
                if (retry <= lowerBound || candidate - lowerBound <= 1e-12)
                {
                    diagnostics.Add(
                        $"bound {bound} release trial {candidate:R}s invalid: " +
                        $"converged {trial.Converged}, crossings [{string.Join("; ", trial.ModeCrossings)}], " +
                        $"violations [{string.Join("; ", trial.StateViolations)}]");
                    releaseTime = 0.0;
                    prefix = null;
                    return false;
                }

                retryCandidate = retry;
                continue;
            }

            var trialFinal = trial.Final;
            var drive = BoundInwardDrive(bound, in trialFinal, gx, gy, gz, settings,
                rateBuffer);
            if (drive <= 0.0)
            {
                lowerBound = candidate;
                lowerDrive = drive;
                lowerWeight = drive;
                best = trial;
                bestTime = candidate;
                if (lastSide == -1) upperWeight *= 0.5;
                else upperWeight = upperDrive;
                lastSide = -1;
            }
            else
            {
                upperBound = candidate;
                upperDrive = drive;
                upperWeight = drive;
                if (lastSide == 1) lowerWeight *= 0.5;
                else lowerWeight = lowerDrive;
                lastSide = 1;
            }

            if (IsFiniteNumber(upperBound) && best != null &&
                upperBound - lowerBound <= PressureBoundRootToleranceSeconds)
            {
                localized = true;
                continue;
            }

            if (IsFiniteNumber(upperBound) && best == null &&
                upperBound - lowerBound <= 1e-12)
                break;

            if (bound != BloodBounds.Core) continue;

            EvaluateCirculation(in trialFinal, gx, gy, gz, settings, rateBuffer, null, false);
            var hrRate = rateBuffer[2];
            if (hrRate <= 0.0) continue;
            var driveDerivative = settings.PassiveReturnRate *
                settings.RestingBloodCore * hrRate;
            if (Math.Abs(drive) > driveDerivative * 0.25 *
                PressureBoundRootToleranceSeconds)
                continue;

            var probeCandidate = drive <= 0.0
                ? Math.Min(candidate + 0.5 * PressureBoundRootToleranceSeconds,
                    IsFiniteNumber(upperBound) ? upperBound : duration)
                : Math.Max(candidate - 0.5 * PressureBoundRootToleranceSeconds, lowerBound);
            if (probeCandidate <= lowerBound ||
                (IsFiniteNumber(upperBound) && probeCandidate >= upperBound) ||
                probeCandidate == candidate)
                continue;

            var probe = AdvanceCirculationSegment(in state, probeCandidate, gx, gy, gz,
                settings, bounds);
            if (!probe.Converged || probe.ModeCrossings.Length != 0 ||
                probe.StateViolations.Length != 0)
            {
                diagnostics.Add(
                    $"bound {bound} release probe {probeCandidate:R}s invalid: " +
                    $"converged {probe.Converged}, crossings [{string.Join("; ", probe.ModeCrossings)}], " +
                    $"violations [{string.Join("; ", probe.StateViolations)}]");
                releaseTime = 0.0;
                prefix = null;
                return false;
            }

            var probeFinal = probe.Final;
            var probeDrive = BoundInwardDrive(bound, in probeFinal, gx, gy, gz, settings,
                rateBuffer);
            if (probeDrive <= 0.0)
            {
                if (probeCandidate > lowerBound)
                {
                    lowerBound = probeCandidate;
                    lowerDrive = probeDrive;
                    lowerWeight = probeDrive;
                    best = probe;
                    bestTime = probeCandidate;
                    upperWeight *= 0.5;
                    lastSide = -1;
                }
            }
            else
            {
                if (!IsFiniteNumber(upperBound) || probeCandidate < upperBound)
                {
                    upperBound = probeCandidate;
                    upperDrive = probeDrive;
                    upperWeight = probeDrive;
                    lowerWeight *= 0.5;
                    lastSide = 1;
                }
            }

            if (IsFiniteNumber(upperBound) && best != null &&
                upperBound - lowerBound <= PressureBoundRootToleranceSeconds)
                localized = true;
        }

        if (!localized || best == null)
        {
            diagnostics.Add(
                $"bound {bound} release localization failed to reach 1e-8s bracket at {lowerBound:R}");
            releaseTime = 0.0;
            prefix = null;
            return false;
        }

        releaseTime = bestTime;
        prefix = best;
        return true;
    }

    private static bool LocalizeModeTransition(
        in IntegrationState state,
        double duration,
        double denseGuess,
        SegmentSolve fullSolve,
        int coordinate,
        double level,
        BloodBounds bounds,
        double gx,
        double gy,
        double gz,
        LogicSettings settings,
        List<string> diagnostics,
        out double transitionTime,
        out SegmentSolve? prefix)
    {
        var coefficients = coordinate switch
        {
            0 => fullSolve.HeadCoefficients,
            2 => fullSolve.RateCoefficients,
            _ => fullSolve.CardioCoefficients
        };
        var residual0 = ModeCoordinateValue(in state, coordinate) - level;
        var side = Math.Sign(residual0);
        if (side == 0)
            side = Math.Sign(NumericalMath.EvaluateCubic(coefficients,
                Math.Min(denseGuess, duration)) - level);
        if (side == 0) side = 1;

        var lowerBound = 0.0;
        var lowerResidual = residual0;
        var lowerWeight = Math.Abs(residual0);
        var upperBound = double.NaN;
        var upperResidual = double.NaN;
        var upperWeight = double.NaN;
        var lastSide = 0;
        SegmentSolve? best = null;
        var bestTime = 0.0;
        var bestSlope = 0.0;

        var fullFinal = fullSolve.Final;
        var fullResidual = ModeCoordinateValue(in fullFinal, coordinate) - level;
        if (fullSolve.Converged && fullSolve.StateViolations.Length == 0 &&
            Math.Sign(fullResidual) != 0 && Math.Sign(fullResidual) != side)
        {
            upperBound = duration;
            upperResidual = fullResidual;
            upperWeight = Math.Abs(fullResidual);
        }

        var localized = false;
        var invalidBound = double.NaN;
        double? rootCandidate = null;
        for (var iteration = 0; iteration < 60 && !localized; iteration++)
        {
            var hiSearch = IsFiniteNumber(upperBound) && IsFiniteNumber(invalidBound)
                ? Math.Min(upperBound, invalidBound)
                : IsFiniteNumber(upperBound)
                    ? upperBound
                    : invalidBound;
            double candidate;
            if (iteration == 0)
            {
                candidate = Math.Min(Math.Max(denseGuess, 1e-10),
                    IsFiniteNumber(hiSearch) ? hiSearch - 1e-10 : duration);
            }
            else if (rootCandidate.HasValue && rootCandidate.Value > lowerBound &&
                     IsFiniteNumber(hiSearch) && rootCandidate.Value < hiSearch)
            {
                candidate = rootCandidate.Value;
            }
            else if (IsFiniteNumber(upperBound))
            {
                candidate = lowerBound + (upperBound - lowerBound) * lowerWeight /
                    (lowerWeight + upperWeight);
                if (!IsFiniteNumber(candidate) || candidate <= lowerBound ||
                    candidate >= hiSearch)
                    candidate = 0.5 * (lowerBound + hiSearch);
            }
            else
            {
                candidate = IsFiniteNumber(hiSearch)
                    ? 0.5 * (lowerBound + hiSearch)
                    : 0.5 * (lowerBound + duration);
            }

            rootCandidate = null;
            if (candidate <= lowerBound || candidate <= 0.0 || candidate > duration)
                break;

            var trial = AdvanceCirculationSegment(in state, candidate, gx, gy, gz, settings,
                bounds);
            if (!trial.Converged || trial.StateViolations.Length != 0)
            {
                if (!IsFiniteNumber(invalidBound) || candidate < invalidBound)
                    invalidBound = candidate;
                if (candidate - lowerBound <= 1e-12)
                {
                    var invalidFinal = trial.Final;
                    diagnostics.Add(
                        $"mode transition trial {candidate:R}s invalid: " +
                        $"converged {trial.Converged}, violations " +
                        $"[{string.Join("; ", trial.StateViolations)}] " +
                        $"crossings [{string.Join("; ", trial.ModeCrossings)}] " +
                        $"endcoord {ModeCoordinateValue(in invalidFinal, coordinate):R}");
                    transitionTime = 0.0;
                    prefix = null;
                    return false;
                }

                continue;
            }

            var trialFinal = trial.Final;
            var endResidual = ModeCoordinateValue(in trialFinal, coordinate) - level;
            if (Math.Sign(endResidual) == side)
            {
                lowerBound = candidate;
                lowerResidual = endResidual;
                lowerWeight = Math.Abs(endResidual);
                best = trial;
                bestTime = candidate;
                var bestCoefficients = coordinate switch
                {
                    0 => trial.HeadCoefficients,
                    2 => trial.RateCoefficients,
                    _ => trial.CardioCoefficients
                };
                bestSlope = bestCoefficients[1] + 2.0 * bestCoefficients[2] * candidate +
                    3.0 * bestCoefficients[3] * candidate * candidate;
                if (bestSlope * side < 0.0)
                {
                    var root = bestTime - endResidual / bestSlope;
                    if (root > lowerBound)
                        rootCandidate = root;
                }

                if (lastSide == -1) upperWeight *= 0.5;
                lastSide = -1;
            }
            else
            {
                upperBound = candidate;
                upperResidual = endResidual;
                upperWeight = Math.Abs(endResidual);
                if (lastSide == 1) lowerWeight *= 0.5;
                else lowerWeight = Math.Abs(lowerResidual);
                lastSide = 1;
            }

            if (IsFiniteNumber(upperBound) && best != null &&
                upperBound - lowerBound <= PressureBoundRootToleranceSeconds)
                localized = true;

            if (!localized && best != null &&
                Math.Abs(lowerResidual) <=
                Math.Abs(bestSlope) * PressureBoundRootToleranceSeconds + 1e-12)
                localized = true;

            if (!localized && IsFiniteNumber(upperBound) &&
                upperBound - lowerBound <= 1e-12)
                break;
        }

        if (!localized || best == null)
        {
            diagnostics.Add(
                $"mode transition for coordinate {coordinate} level {level:R} " +
                $"failed to reach 1e-8s bracket at {lowerBound:R} " +
                $"(upper {upperBound:R})");
            transitionTime = 0.0;
            prefix = null;
            return false;
        }

        transitionTime = bestTime;
        prefix = best;
        return true;
    }

    internal static double[] ReconstructIntervalPressure(
        double initialPressure,
        double decayRate,
        double[] headCoefficients,
        double[] stageOffsets,
        double dt,
        double gz,
        LogicSettings settings,
        List<string> diagnostics,
        out double[] entries,
        out double[] releases)
    {
        var entryList = new List<double>();
        var releaseList = new List<double>();
        var segments = new List<(double Start, double End, double StartPressure, bool Bound)>();

        var initialSource = PressureSource(EvaluateDenseHead(headCoefficients, 0.0, diagnostics), gz, settings);
        var bound = initialPressure >= 1.0 && initialSource >= decayRate;
        if (initialPressure >= 1.0 && !bound)
            releaseList.Add(0.0);

        var segmentStart = 0.0;
        var freePressure = initialPressure;
        var candidates = FindPressureSourceCrossings(headCoefficients, dt, gz, decayRate,
            settings, diagnostics);

        foreach (var (offset, downward) in candidates)
        {
            if (bound)
            {
                if (!downward) continue;
                releaseList.Add(offset);
                segments.Add((segmentStart, offset, 0.0, true));
                segmentStart = offset;
                freePressure = 1.0;
                bound = false;
                continue;
            }

            var freeAtCandidate = EvaluateFreePressure(freePressure, segmentStart, offset,
                decayRate, headCoefficients, gz, settings, diagnostics);
            if (freeAtCandidate < 1.0) continue;

            var entry = BisectPressureEntry(freePressure, segmentStart, offset, decayRate,
                headCoefficients, gz, settings, diagnostics);
            entryList.Add(entry);
            segments.Add((segmentStart, entry, freePressure, false));
            if (downward)
            {
                releaseList.Add(offset);
                segments.Add((entry, offset, 0.0, true));
                segmentStart = offset;
                freePressure = 1.0;
            }
            else
            {
                segmentStart = entry;
                bound = true;
            }
        }

        if (!bound)
        {
            var freeAtEnd = EvaluateFreePressure(freePressure, segmentStart, dt,
                decayRate, headCoefficients, gz, settings, diagnostics);
            if (freeAtEnd >= 1.0)
            {
                var entry = BisectPressureEntry(freePressure, segmentStart, dt, decayRate,
                    headCoefficients, gz, settings, diagnostics);
                entryList.Add(entry);
                segments.Add((segmentStart, entry, freePressure, false));
                segmentStart = entry;
                bound = true;
            }
        }

        segments.Add((segmentStart, dt, bound ? 0.0 : freePressure, bound));
        entries = entryList.ToArray();
        releases = releaseList.ToArray();

        var stagePressures = new double[stageOffsets.Length];
        var segmentIndex = 0;
        for (var stage = 0; stage < stageOffsets.Length; stage++)
        {
            var offset = stageOffsets[stage];
            while (segmentIndex < segments.Count - 1 && segments[segmentIndex].End <= offset)
                segmentIndex++;
            var segment = segments[segmentIndex];
            stagePressures[stage] = segment.Bound
                ? 1.0
                : EvaluateFreePressure(segment.StartPressure, segment.Start, offset, decayRate,
                    headCoefficients, gz, settings, diagnostics);
        }

        return stagePressures;
    }

    private static List<(double Offset, bool Downward)> FindPressureSourceCrossings(
        double[] headCoefficients,
        double dt,
        double gz,
        double decayRate,
        LogicSettings settings,
        List<string> diagnostics)
    {
        var criticalRoots = new double[2];
        var criticalCount = NumericalMath.QuadraticRootsInInterval(
            3.0 * headCoefficients[3], 2.0 * headCoefficients[2], headCoefficients[1],
            0.0, dt, criticalRoots);
        var boundaryCount = criticalCount + 2;
        var bounds = new double[boundaryCount];
        bounds[0] = 0.0;
        Array.Copy(criticalRoots, 0, bounds, 1, criticalCount);
        bounds[boundaryCount - 1] = dt;

        var midpointSigns = new int[boundaryCount - 1];
        for (var segment = 0; segment < boundaryCount - 1; segment++)
        {
            var midpoint = 0.5 * (bounds[segment] + bounds[segment + 1]);
            var value = PressureSource(EvaluateDenseHead(headCoefficients, midpoint, diagnostics),
                gz, settings) - decayRate;
            if (!IsFiniteNumber(value))
                diagnostics.Add($"pressure source level non-finite at offset {midpoint:R}");
            midpointSigns[segment] = Math.Sign(value);
        }

        var roots = new List<(double Offset, bool Downward)>();
        for (var segment = 0; segment < boundaryCount - 1; segment++)
        {
            var lo = bounds[segment];
            var hi = bounds[segment + 1];
            var loValue = PressureSource(EvaluateDenseHead(headCoefficients, lo, diagnostics),
                gz, settings) - decayRate;
            var hiValue = PressureSource(EvaluateDenseHead(headCoefficients, hi, diagnostics),
                gz, settings) - decayRate;
            var loSign = Math.Sign(loValue);
            var hiSign = Math.Sign(hiValue);

            if (loSign == 0 && hiSign == 0 && midpointSigns[segment] == 0) continue;

            if (loSign == 0 && (roots.Count == 0 || roots[roots.Count - 1].Offset != lo))
            {
                var downward = segment == 0
                    ? midpointSigns[0] < 0
                    : midpointSigns[segment - 1] >= 0 && midpointSigns[segment] < 0;
                roots.Add((lo, downward));
            }

            if (loSign != 0 && hiSign != 0 && loSign != hiSign)
            {
                roots.Add((BisectPressureSourceLevel(headCoefficients, gz, decayRate, settings,
                    lo, hi, loValue, diagnostics), hiSign < 0));
            }
        }

        return roots;
    }

    private static double BisectPressureSourceLevel(
        double[] headCoefficients,
        double gz,
        double decayRate,
        LogicSettings settings,
        double lo,
        double hi,
        double loValue,
        List<string> diagnostics)
    {
        while (hi - lo > PressureBoundRootToleranceSeconds)
        {
            var mid = 0.5 * (lo + hi);
            var midValue = PressureSource(EvaluateDenseHead(headCoefficients, mid, diagnostics),
                gz, settings) - decayRate;
            if (midValue == 0.0) return mid;
            if ((midValue < 0.0) == (loValue < 0.0))
            {
                lo = mid;
                loValue = midValue;
            }
            else
            {
                hi = mid;
            }
        }

        return 0.5 * (lo + hi);
    }

    private static double BisectPressureEntry(
        double pressureStart,
        double freeStart,
        double high,
        double decayRate,
        double[] headCoefficients,
        double gz,
        LogicSettings settings,
        List<string> diagnostics)
    {
        var lo = freeStart;
        var hi = high;
        while (hi - lo > PressureBoundRootToleranceSeconds)
        {
            var mid = 0.5 * (lo + hi);
            var pressure = EvaluateFreePressure(pressureStart, freeStart, mid, decayRate,
                headCoefficients, gz, settings, diagnostics);
            if (pressure > 1.0) hi = mid; else lo = mid;
        }

        return lo;
    }

    private static double EvaluateFreePressure(
        double pressureStart,
        double from,
        double to,
        double decayRate,
        double[] headCoefficients,
        double gz,
        LogicSettings settings,
        List<string> diagnostics)
    {
        var span = to - from;
        if (span <= 0.0) return pressureStart;

        var x = decayRate * span;
        var w = -NumericalMath.ExpMinusOne(-x);
        var mass = decayRate == 0.0 ? span : w / decayRate;
        var nodes = NumericalMath.GaussLegendre16Nodes;
        var weights = NumericalMath.GaussLegendre16Weights;
        var sum = 0.0;
        for (var node = 0; node < nodes.Length; node++)
        {
            var offset = decayRate == 0.0
                ? from + nodes[node] * span
                : to + (x >= 1.0
                    ? Math.Log(Math.Exp(-x) + w * nodes[node])
                    : NumericalMath.LogOnePlus(-w * (1.0 - nodes[node]))) / decayRate;
            sum += weights[node] * PressureSource(
                EvaluateDenseHead(headCoefficients, offset, diagnostics), gz, settings);
        }

        return pressureStart * Math.Exp(-x) + mass * sum;
    }

    private static double EvaluateDenseHead(double[] headCoefficients, double offset,
        List<string> diagnostics)
    {
        var head = NumericalMath.EvaluateCubic(headCoefficients, offset);
        if (!IsFiniteNumber(head) || head < 0.0 || head > 1.0)
            diagnostics.Add($"dense head blood {head:R} out of bounds at offset {offset:R}");
        return head;
    }

    private static BloodBounds CheckStageValidity(IntegrationState state, int stage,
        LogicSettings settings, BloodBounds bounds, List<string> generalViolations,
        List<string> boundViolations)
    {
        var boundFlags = BloodBounds.None;
        if (!IsFiniteNumber(state.BloodHead) || !IsFiniteNumber(state.BloodCore) ||
            !IsFiniteNumber(state.BloodLower) || !IsFiniteNumber(state.HeartRateMultiplier) ||
            !IsFiniteNumber(state.CardioFatigue) || !IsFiniteNumber(state.CerebralPressureImpairment) ||
            !IsFiniteNumber(state.RespiratoryFatigue) || !IsFiniteNumber(state.StrainingLevel) ||
            !IsFiniteNumber(state.StrainingFatigue) || !IsFiniteNumber(state.GSuitFatigue))
            generalViolations.Add($"stage {stage + 1}: non-finite state");
        ReportBound(BloodBounds.Head, state.BloodHead - settings.MinHeadBloodFraction,
            $"stage {stage + 1}: BloodHead {state.BloodHead:R} below head floor");
        ReportBound(BloodBounds.Lower, state.BloodLower,
            $"stage {stage + 1}: BloodLower {state.BloodLower:R} below zero");
        ReportBound(BloodBounds.Core, state.BloodCore,
            $"stage {stage + 1}: BloodCore {state.BloodCore:R} below zero");
        if (state.CardioFatigue is < 0.0 or > 1.0)
            generalViolations.Add($"stage {stage + 1}: CardioFatigue {state.CardioFatigue:R} outside [0,1]");
        if (state.CerebralPressureImpairment is < 0.0 or > 1.0)
            generalViolations.Add($"stage {stage + 1}: CerebralPressureImpairment {state.CerebralPressureImpairment:R} outside [0,1]");
        if (state.RespiratoryFatigue is < 0.0 or > 1.0)
            generalViolations.Add($"stage {stage + 1}: RespiratoryFatigue {state.RespiratoryFatigue:R} outside [0,1]");
        if (state.StrainingLevel is < 0.0 or > 1.0)
            generalViolations.Add($"stage {stage + 1}: StrainingLevel {state.StrainingLevel:R} outside [0,1]");
        if (state.StrainingFatigue is < 0.0 or > 1.0)
            generalViolations.Add($"stage {stage + 1}: StrainingFatigue {state.StrainingFatigue:R} outside [0,1]");
        if (state.GSuitFatigue is < 0.0 or > 1.0)
            generalViolations.Add($"stage {stage + 1}: GSuitFatigue {state.GSuitFatigue:R} outside [0,1]");
        return boundFlags;

        void ReportBound(BloodBounds bound, double residual, string message)
        {
            var limit = (bounds & bound) != 0 ? -1e-12 : 0.0;
            if (residual >= limit) return;
            boundFlags |= bound;
            ((bounds & bound) != 0 ? generalViolations : boundViolations).Add(message);
        }
    }

    private static bool IsFiniteNumber(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    #endregion

    #region fullInterval

    private const double FullIntervalEventScanTolerance = 1e-9;

    private sealed class IntervalTrialState
    {
        internal double Head;
        internal double Lower;
        internal double HeartRate;
        internal double Straining;
        internal double Respiratory;
        internal double Pressure;
        internal double Arterial;
        internal double Compression;
        internal double Core => 1.0 - Head - Lower;
    }

    private readonly struct ExponentialChannel
    {
        private readonly double initial;
        private readonly double equilibrium;
        private readonly double inverseTau;

        internal ExponentialChannel(double initial, double equilibrium, double inverseTau)
        {
            this.initial = initial;
            this.equilibrium = equilibrium;
            this.inverseTau = inverseTau;
        }

        // inverseTau >= 1e9 comes from a tau <= 1e-9 source: the channel is at
        // its equilibrium for every positive offset (instantaneous limit).
        private bool Instant => inverseTau >= 1e9;

        internal double At(double t) =>
            Instant
                ? (t <= 0.0 ? initial : equilibrium)
                : equilibrium + (initial - equilibrium) * Math.Exp(-inverseTau * t);

        internal double RateAt(double t) => Instant ? 0.0 : (equilibrium - At(t)) * inverseTau;

        internal double Crossing(double level)
        {
            var denominator = initial - equilibrium;
            if (denominator == 0.0) return double.PositiveInfinity;
            if (Instant)
            {
                var inJump = (level - initial) * (equilibrium - level) >= 0.0 &&
                             level != initial;
                return inJump ? 0.0 : double.PositiveInfinity;
            }
            var ratio = (level - equilibrium) / denominator;
            if (ratio is <= 0.0 or > 1.0) return double.PositiveInfinity;
            var t = -Math.Log(ratio) / inverseTau;
            return t >= 0.0 ? t : double.PositiveInfinity;
        }

        internal double UpCrossing(double level)
        {
            if (equilibrium <= level) return double.PositiveInfinity;
            return Crossing(level);
        }

        internal double DownCrossing(double level)
        {
            if (equilibrium >= level) return double.PositiveInfinity;
            return Crossing(level);
        }
    }

    private sealed class IntervalUpstream
    {
        private readonly IntegrationSegment[] segments;
        private readonly (double Entry, double Release)[] pressureEpisodes;
        private readonly LogicSettings settings;
        private readonly IntegrationState callerInitial;
        private readonly double gx;
        private readonly double gz;
        private readonly double pressureDecayRate;
        private readonly ExponentialChannel respiratory;
        private readonly ExponentialChannel arterial;
        private readonly ExponentialChannel compression;
        private readonly ExponentialChannel pain;
        private readonly ExponentialChannel sudden;
        private readonly double neckEquilibrium;
        private readonly double neckInverseTau;
        private readonly double neckCeiling;
        private readonly double neckInitial;
        private readonly double neckClampTime;
        private readonly double neckDeathEntry;
        private readonly double neckDeathExit;
        private readonly double dwellBase;
        private readonly double dwellDelay;
        private int segmentHint;

        internal IntervalUpstream(IntegrationResult circulation, in IntegrationState initial,
            double gx, double gy, double gz, LogicSettings settings)
        {
            this.settings = settings;
            this.gx = gx;
            this.gz = gz;
            pressureDecayRate =
                1.0 / Math.Max(settings.CerebralPressureImpairmentRecoveryTau, 1e-9);
            segments = circulation.Segments;
            callerInitial = initial;
            var marks = new List<(double Time, bool Entry)>();
            foreach (var entry in circulation.PressureBoundEntries)
                marks.Add((entry, true));
            foreach (var release in circulation.PressureBoundReleases)
                marks.Add((release, false));
            marks.Sort((a, b) => a.Time.CompareTo(b.Time));
            var episodes = new List<(double, double)>();
            var bound = initial.CerebralPressureImpairment >= 1.0 &&
                        PressureSource(initial.BloodHead, gz, settings) >= pressureDecayRate;
            var episodeStart = bound ? 0.0 : double.NaN;
            foreach (var (time, isEntry) in marks)
            {
                if (bound && !isEntry)
                {
                    episodes.Add((episodeStart, time));
                    bound = false;
                }
                else if (!bound && isEntry)
                {
                    episodeStart = time;
                    bound = true;
                }
            }
            if (bound) episodes.Add((episodeStart, double.PositiveInfinity));
            pressureEpisodes = episodes.ToArray();

            var gxMagnitude = Math.Abs(gx);
            var gyMagnitude = Math.Abs(gy);
            GxMagnitude = gxMagnitude;
            GyMagnitude = gyMagnitude;

            var respiratoryBuild = settings.RespiratoryFatigueBuildRate *
                Math.Max(0.0, gxMagnitude - settings.GxRespiratoryFatigueThreshold) * 0.5 *
                (1.0 + (settings.GxRespiratoryFatigueAccelerationFactor - 1.0) * gxMagnitude);
            var respiratoryTau = Math.Max(settings.RespiratoryFatigueRecoveryTau, 1e-9);
            respiratory = new ExponentialChannel(initial.RespiratoryFatigue,
                respiratoryBuild * respiratoryTau, 1.0 / respiratoryTau);

            var ventilationRange = Math.Max(
                settings.GxLungOxygenationImpairmentFullGx -
                settings.GxLungOxygenationImpairmentThreshold, 1e-9);
            var ventilationFailure = gxMagnitude > settings.GxLungOxygenationImpairmentThreshold
                ? Clamp(settings.GxLungOxygenationImpairmentSeverity *
                        Math.Pow((gxMagnitude - settings.GxLungOxygenationImpairmentThreshold) /
                                 ventilationRange, 3.0), 0.0, 1.0)
                : 0.0;
            var arterialTarget = 1.0 - ventilationFailure;
            arterial = new ExponentialChannel(initial.ArterialOxygenation, arterialTarget,
                1.0 / (arterialTarget < initial.ArterialOxygenation
                    ? settings.GxHypoxiaDepletionTau
                    : settings.GxHypoxiaRecoveryTau));

            var compressionTarget = gyMagnitude > settings.GyLungCompressionThreshold
                ? Clamp(Math.Pow(
                    (gyMagnitude - settings.GyLungCompressionThreshold) / 3.0,
                    settings.GyToleranceNonlinearity), 0.0, 1.0)
                : 0.0;
            compression = new ExponentialChannel(initial.LungCompressionLevel, compressionTarget,
                1.0 / (gyMagnitude > settings.GyLungCompressionThreshold
                    ? settings.GyLungCompressionTau
                    : settings.GyLungCompressionRecoveryTau));

            var painTarget = Clamp(
                settings.GyPainBaseFactor * Math.Pow(gyMagnitude, settings.GyPainNonlinearity) +
                settings.GxPainFactor * gxMagnitude, 0.0, 1.0);
            pain = new ExponentialChannel(initial.PainLevel, painTarget,
                1.0 / (painTarget > initial.PainLevel
                    ? settings.GyPainAccumulationTau
                    : settings.GyPainRecoveryTau));

            var gxRisk = gxMagnitude > settings.GxSuddenLoCThreshold
                ? settings.GxSuddenLoCSeverity * (gxMagnitude - settings.GxSuddenLoCThreshold) / 2.0
                : 0.0;
            var gyRisk = gyMagnitude > settings.GySuddenLoCThreshold
                ? settings.GySuddenLoCSeverity * (gyMagnitude - settings.GySuddenLoCThreshold)
                : 0.0;
            var suddenSource = Math.Max(
                Clamp(gxRisk + gyRisk, 0.0, 1.0) - settings.MultiAxisSuddenLoCThreshold, 0.0);
            sudden = new ExponentialChannel(initial.SuddenLoCAccumulator,
                suddenSource * settings.SuddenLoCRecoveryTau,
                1.0 / Math.Max(settings.SuddenLoCRecoveryTau, 1e-9));

            var neckBuild = gyMagnitude > settings.GyNeckFatigueThreshold
                ? settings.GyNeckFatigueBuildRate * Math.Pow(
                    (gyMagnitude - settings.GyNeckFatigueThreshold) / 3.0,
                    settings.GyNeckFatigueNonlinearity)
                : 0.0;
            neckCeiling = Clamp(
                settings.GyNeckFatigueCeiling + (1.0 - settings.GyNeckFatigueCeiling) * Math.Pow(
                    (gyMagnitude - settings.GyNeckFatigueThreshold) / 8.0,
                    settings.GyNeckFatigueNonlinearity),
                settings.GyNeckFatigueCeiling, 1.0);
            neckInverseTau = 1.0 / settings.GyNeckFatigueRecoveryTau;
            neckEquilibrium = neckBuild * settings.GyNeckFatigueRecoveryTau;
            neckInitial = Math.Min(initial.GyNeckFatigue, neckCeiling);
            NeckInitiallyClamped = initial.GyNeckFatigue > neckCeiling;
            var free = new ExponentialChannel(neckInitial, neckEquilibrium, neckInverseTau);
            neckClampTime = neckEquilibrium > neckCeiling
                ? neckInitial >= neckCeiling ? 0.0 : free.Crossing(neckCeiling)
                : double.PositiveInfinity;

            var level = settings.GyNeckFatigueDeathLevel;
            var startAbove = NeckAt(0.0) >= level;
            if (startAbove)
            {
                neckDeathEntry = 0.0;
                neckDeathExit = free.DownCrossing(level);
                dwellBase = initial.GyNeckFatigueDeathDwell;
            }
            else
            {
                var cross = free.UpCrossing(level);
                neckDeathEntry = cross < neckClampTime
                    ? cross
                    : neckClampTime < double.PositiveInfinity && neckCeiling >= level
                        ? neckClampTime
                        : double.PositiveInfinity;
                neckDeathExit = double.PositiveInfinity;
                dwellBase = 0.0;
            }
            dwellDelay = settings.GyNeckFatigueDeathDelay;
        }

        internal double GxMagnitude { get; }
        internal double GyMagnitude { get; }
        internal bool NeckInitiallyClamped { get; }
        internal double NeckClampTime => neckClampTime;
        internal double NeckDeathEntry => neckDeathEntry;
        internal double NeckDeathExit => neckDeathExit;
        internal double NeckCeiling => neckCeiling;

        private IntegrationState IndependentAt(double t) =>
            IndependentCirculationStateAt(callerInitial, t, gx, gz, settings);

        internal double StrainLevelAt(double t) => IndependentAt(t).StrainingLevel;
        internal double StrainFatigueAt(double t) => IndependentAt(t).StrainingFatigue;
        internal double SuitFatigueAt(double t) => IndependentAt(t).GSuitFatigue;
        internal double RespiratoryAt(double t) => IndependentAt(t).RespiratoryFatigue;
        internal double RespiratoryBoundTime => respiratory.UpCrossing(1.0);
        internal double ArterialAt(double t) => arterial.At(t);
        internal double CompressionAt(double t) => compression.At(t);
        internal double PainAt(double t) => pain.At(t);
        internal double SuddenAt(double t) => Clamp(sudden.At(t), 0.0, 1.0);
        internal double SuddenBoundTime => sudden.UpCrossing(1.0);

        internal double NeckAt(double t)
        {
            if (t >= neckClampTime) return neckCeiling;
            return neckEquilibrium +
                   (neckInitial - neckEquilibrium) * Math.Exp(-neckInverseTau * t);
        }

        private bool dwellPinned;
        private readonly Dictionary<double, IntervalTrialState> evaluateCache = new();

        internal void PinDwell() => dwellPinned = true;

        internal double NeckRateAt(double t)
        {
            if (t >= neckClampTime) return 0.0;
            return neckInverseTau * (neckEquilibrium - NeckAt(t));
        }

        internal double SuddenRateAt(double t) => sudden.RateAt(t);

        internal double NeckDwellAt(double t)
        {
            if (dwellPinned) return 1.0;
            if (double.IsInfinity(neckDeathEntry)) return 0.0;
            if (t <= neckDeathEntry) return neckDeathEntry == 0.0 ? dwellBase : 0.0;
            if (t <= neckDeathExit)
                return Math.Min(dwellBase + (t - neckDeathEntry) / dwellDelay, 1.0);
            return 0.0;
        }

        internal double DeathCompletionTime()
        {
            if (double.IsInfinity(neckDeathEntry)) return double.PositiveInfinity;
            var completion = neckDeathEntry + (1.0 - dwellBase) * dwellDelay;
            return completion <= neckDeathExit ? completion : double.PositiveInfinity;
        }

        internal double SuddenTriggerTime(bool inSudden, bool dead)
        {
            if (inSudden || dead) return double.PositiveInfinity;
            if (sudden.At(0.0) > 0.8) return 0.0;
            return sudden.UpCrossing(0.8);
        }

        internal double SuddenRecoveryTime(bool inSudden)
        {
            if (!inSudden) return double.PositiveInfinity;
            if (sudden.At(0.0) < 0.3) return 0.0;
            return sudden.DownCrossing(0.3);
        }

        internal HashSet<string> Diagnostics { get; } = [];

        internal (double Entry, double Release)[] PressureEpisodes => pressureEpisodes;

        internal double HeadAt(double t)
        {
            var segment = segments[SegmentIndexAt(t)];
            return NumericalMath.EvaluateCubic(segment.HeadCoefficients,
                t - segment.CoefficientStart);
        }

        internal IntegrationState CirculationStateAt(double t)
        {
            var index = SegmentIndexAt(t);
            var segment = segments[index];
            var local = t - segment.CoefficientStart;
            var independent = IndependentAt(t);
            var head = NumericalMath.EvaluateCubic(segment.HeadCoefficients, local);
            var lower = NumericalMath.EvaluateCubic(segment.LowerCoefficients, local);
            return new IntegrationState(head, lower,
                NumericalMath.EvaluateCubic(segment.RateCoefficients, local),
                NumericalMath.EvaluateCubic(segment.CardioCoefficients, local),
                0.0, independent.RespiratoryFatigue)
            {
                StrainingLevel = independent.StrainingLevel,
                StrainingFatigue = independent.StrainingFatigue,
                GSuitFatigue = independent.GSuitFatigue
            };
        }

        internal IntervalTrialState Evaluate(double t)
        {
            if (evaluateCache.TryGetValue(t, out var cached)) return cached;
            var state = CirculationStateAt(t);
            var result = new IntervalTrialState
            {
                Head = state.BloodHead,
                Lower = state.BloodLower,
                HeartRate = state.HeartRateMultiplier,
                Straining = state.StrainingLevel,
                Respiratory = state.RespiratoryFatigue,
                Pressure = PressureAt(t),
                Arterial = ArterialAt(t),
                Compression = CompressionAt(t)
            };
            evaluateCache[t] = result;
            return result;
        }

        private int SegmentIndexAt(double t)
        {
            var index = Math.Min(segmentHint, segments.Length - 1);
            while (index + 1 < segments.Length && segments[index + 1].StartOffset <= t) index++;
            while (index > 0 && segments[index].StartOffset > t) index--;
            segmentHint = index;
            return index;
        }

        private double FreePressureSpan(double pressure, double from, double to)
        {
            var diagnostics = new List<string>();
            var position = from;
            var value = pressure;
            while (position < to - 1e-15)
            {
                var segment = segments[SegmentIndexAt(position)];
                var spanEnd = Math.Min(segment.StartOffset + segment.Duration, to);
                value = EvaluateFreePressure(value,
                    position - segment.CoefficientStart,
                    spanEnd - segment.CoefficientStart, pressureDecayRate,
                    segment.HeadCoefficients, gz, settings, diagnostics);
                position = spanEnd;
            }
            foreach (var diagnostic in diagnostics) Diagnostics.Add(diagnostic);
            return value;
        }

        internal double PressureAt(double t)
        {
            var pressure = callerInitial.CerebralPressureImpairment;
            var cursor = 0.0;
            foreach (var (entry, release) in pressureEpisodes)
            {
                if (t <= entry) return FreePressureSpan(pressure, cursor, t);
                pressure = FreePressureSpan(pressure, cursor, entry);
                if (t <= release) return 1.0;
                cursor = release;
                pressure = 1.0;
            }
            return FreePressureSpan(pressure, cursor, t);
        }
    }

    private static double ShapedPerfusion(double headBlood, LogicSettings settings)
    {
        var ratio = Clamp(headBlood / settings.RestingBloodHead, 0.0, 1.0);
        var shaped = ratio - settings.O2PerfusionCurveStrength * ratio * (1.0 - ratio) *
            (ratio - settings.O2PerfusionCurvePivot);
        return Clamp(shaped, 0.0, 1.0);
    }

    private static double DeliveryCore(double shapedPerfusion, LogicSettings settings)
    {
        var delivery = Math.Pow(shapedPerfusion, settings.BrainO2PerfusionExponent);
        var threshold = Clamp(settings.BrainO2HypoperfusionThreshold, 0.01, 1.0);
        var hypoperfusion = Math.Max(0.0, threshold - shapedPerfusion) / threshold;
        var penalty = settings.BrainO2HypoperfusionPenaltyStrength * hypoperfusion * hypoperfusion;
        return delivery - penalty;
    }

    private static double EffectiveDelivery(double shapedPerfusion, double heartRate,
        double arterial, LogicSettings settings)
    {
        return Clamp(DeliveryCore(shapedPerfusion, settings), 0.0, 1.0) * heartRate * arterial;
    }

    private static double BrainO2Target(double effectiveDelivery, LogicSettings settings) =>
        settings.BrainO2Floor + (1.0 - settings.BrainO2Floor) * effectiveDelivery;

    private static double BrainDepletionTau(double effectiveDelivery, LogicSettings settings)
    {
        var severity = 1.0 - effectiveDelivery;
        return settings.BrainO2DepletionTauMild +
               (settings.BrainO2DepletionTauSevere - settings.BrainO2DepletionTauMild) * severity;
    }

    private static double GxLungImpairment(double gxMagnitude, LogicSettings settings)
    {
        if (gxMagnitude <= settings.GxLungOxygenationImpairmentThreshold) return 0.0;
        return Clamp(settings.GxLungOxygenationImpairmentSeverity * Math.Pow(
            (gxMagnitude - settings.GxLungOxygenationImpairmentThreshold) / 4.0, 2.0), 0.0, 1.0);
    }

    private static double O2Normalized(double headO2, LogicSettings settings) => Clamp(
        (headO2 - settings.BrainO2Blackout) /
        (settings.BrainO2Full - settings.BrainO2Blackout), 0.0, 1.0);

    private static double PerfusionNormalized(double headBlood, LogicSettings settings)
    {
        var ratio = Clamp(headBlood / settings.RestingBloodHead, 0.0, 1.0);
        return Clamp(
            (ratio - settings.ConsciousnessPerfusionSoftMinRatio) /
            (1.0 - settings.ConsciousnessPerfusionSoftMinRatio), 0.0, 1.0);
    }

    private static (double Target, double LossTau) ConsciousnessTargetAndTau(
        double o2Normalized, double perfNorm, double pressure, LogicSettings settings)
    {
        var o2Term = Math.Pow(o2Normalized, settings.ConsciousnessO2Exponent);
        var perfTerm = Math.Pow(SmoothStep(perfNorm), settings.ConsciousnessPerfusionExponent);
        var target = o2Term * perfTerm;

        var effectivePressure = Clamp(
            (pressure - settings.CerebralPressureImpairmentDeadband) /
            Math.Max(1.0 - settings.CerebralPressureImpairmentDeadband, 1e-9), 0.0, 1.0);
        var pressureReserve = Math.Pow(Clamp(1.0 - effectivePressure, 0.0, 1.0),
            settings.CerebralPressureConsciousnessExponent);

        var combinedDeficit = 1.0 - (0.5 * o2Normalized + 0.5 * perfNorm);
        target = Math.Max(0.0,
            target - settings.ConsciousnessDeficitBias * combinedDeficit * combinedDeficit);
        if (perfNorm < 0.25) target = Math.Min(target, perfNorm * 0.75);
        target = Math.Min(target, pressureReserve);

        var lossSeverity = Math.Pow(1.0 - target, settings.ConsciousnessLossSeverityExponent);
        var baseLossTau = settings.ConsciousnessLossTauMax +
            (settings.ConsciousnessLossTauMin - settings.ConsciousnessLossTauMax) * lossSeverity;
        var criticalPerf = 1.0 - Clamp(
            perfNorm / settings.ConsciousnessCriticalPerfusionNorm, 0.0, 1.0);
        var criticalO2 = 1.0 - Clamp(
            o2Normalized / settings.ConsciousnessCriticalO2Norm, 0.0, 1.0);
        var criticalPressure = Clamp(
            (effectivePressure - settings.ConsciousnessCriticalPressureNorm) /
            Math.Max(1.0 - settings.ConsciousnessCriticalPressureNorm, 1e-9), 0.0, 1.0);
        var critical = Math.Max(Math.Max(criticalPerf, criticalO2), criticalPressure);
        var multiplier = 1.0 - (1.0 - settings.ConsciousnessCriticalTauMultiplierMin) *
            SmoothStep(critical);
        return (target, baseLossTau * multiplier);
    }

    private static double PhysiologicalVisualTarget(double headBlood, double o2Normalized,
        LogicSettings settings)
    {
        var perfRatio = Clamp(headBlood / settings.RestingBloodHead, 0.0, 1.0);
        var visualPerf = Clamp((perfRatio - 0.45) / 0.55, 0.0, 1.0);
        var visualO2 = Clamp((o2Normalized - 0.15) / 0.85, 0.0, 1.0);
        var visualDeficit = 1.0 - (0.7 * visualPerf + 0.3 * visualO2);
        return headBlood < settings.RestingBloodHead
            ? Math.Pow(Clamp((visualDeficit - 0.18) / 0.82, 0.0, 1.0), 2.2)
            : 0.0;
    }

    private static double RedoutTarget(double headOverfill, LogicSettings settings)
    {
        var range = Math.Max(
            settings.VisualRedoutFullHeadBloodOverfill -
            settings.VisualRedoutOnsetHeadBloodOverfill, 1e-9);
        return SmoothStep(Clamp(
            (headOverfill - settings.VisualRedoutOnsetHeadBloodOverfill) / range, 0.0, 1.0));
    }

    private static double LoCCeiling(double consciousness, LogicSettings settings)
    {
        var range = Math.Max(
            settings.ConsciousnessRecoveryThreshold - settings.ConsciousnessLossThreshold, 1e-9);
        return Math.Pow(Clamp(
            (settings.ConsciousnessRecoveryThreshold - consciousness) / range, 0.0, 1.0),
            settings.VisualLoCConsciousnessExponent);
    }

    // Affine system y' = M(t) y + b(t) over one piece, with optional coordinates
    // constrained algebraically to a time-varying target (instantaneous tau
    // limit). Instant coordinates carry a zero row/column in M; their target is
    // folded into B of the free rows by the builder.
    private sealed class AffineSystemSpec
    {
        internal double[][] M = [];
        internal double[] B = [];
        internal bool[] Instant = [];
        internal Func<double, double>?[] InstantTargets = [];
    }

    private sealed class AffineSolution
    {
        internal bool Valid;
        internal double[][] M0 = [];
        internal double[] B0 = [];
        internal double[] BaseSource = [];
        internal double[] Y0 = [];
        internal double Duration;
        internal double[][] Stages = [];
        internal double[][] Residuals = [];
        internal Func<double, double>?[] InstantTargets = [];
        private readonly Dictionary<double, double[]> cache = new();

        internal double[] Evaluate(double c)
        {
            if (cache.TryGetValue(c, out var cached)) return cached;
            var n = Y0.Length;
            var y = new double[n];
            var allInstant = InstantTargets.Length == n;
            for (var i = 0; i < n && allInstant; i++)
                allInstant &= InstantTargets[i] != null;
            if (!allInstant && !EvaluateFree(c, y))
                for (var i = 0; i < n; i++) y[i] = double.NaN;
            for (var i = 0; i < n; i++)
                if (i < InstantTargets.Length && InstantTargets[i] != null)
                    y[i] = InstantTargets[i]!(c);
            cache[c] = y;
            return y;
        }

        private bool EvaluateFree(double c, double[] y)
        {
            var n = Y0.Length;
            var stageCount = NumericalMath.RadauStageCount;
            if (n == 1)
            {
                var scalar = new double[3];
                if (!NumericalMath.ScalarMoments(M0[0][0] * Duration, c, scalar))
                    return false;
                var value = Y0[0] + Duration * scalar[0] * BaseSource[0];
                for (var j = 0; j < stageCount; j++)
                {
                    var basis = LagrangePolynomialBasis[j];
                    var w = basis[0] * scalar[0] + basis[1] * scalar[1] +
                            basis[2] * scalar[2];
                    value += Duration * w * Residuals[j][0];
                }
                y[0] = value;
                return true;
            }

            var a = NumericalMath.CreateMatrix(n);
            for (var i = 0; i < n; i++)
                for (var k = 0; k < n; k++)
                    a[i][k] = M0[i][k] * Duration;
            var moments = NumericalMath.MomentMatrices(a, c);
            if (moments is null) return false;
            for (var i = 0; i < n; i++)
            {
                var value = Y0[i];
                for (var k = 0; k < n; k++)
                    value += Duration * moments[0][i][k] * BaseSource[k];
                for (var j = 0; j < stageCount; j++)
                    for (var k = 0; k < n; k++)
                    {
                        var w = 0.0;
                        for (var m = 0; m < 3; m++)
                            w += LagrangePolynomialBasis[j][m] * moments[m][i][k];
                        value += Duration * w * Residuals[j][k];
                    }
                y[i] = value;
            }
            return true;
        }

        internal double[] Derivative(double c)
        {
            var n = Y0.Length;
            var y = Evaluate(c);
            var f = new double[n];
            for (var i = 0; i < n; i++)
            {
                if (i < InstantTargets.Length && InstantTargets[i] != null)
                {
                    var delta = Math.Min(1e-6, Math.Max(1e-9, 0.5 * (1.0 - c)));
                    var slope = (InstantTargets[i]!(c + delta) - y[i]) / delta;
                    f[i] = double.IsFinite(slope) ? slope / Duration : 0.0;
                    continue;
                }
                var value = B0[i];
                for (var k = 0; k < n; k++) value += M0[i][k] * y[k];
                for (var j = 0; j < NumericalMath.RadauStageCount; j++)
                {
                    var basis = LagrangePolynomialBasis[j];
                    var l = basis[0] + c * (basis.Length > 1 ? basis[1] + c * basis[2] : 0.0);
                    value += l * Residuals[j][i];
                }
                f[i] = value;
            }
            return f;
        }
    }

    private static readonly double[][] LagrangePolynomialBasis = NumericalMath.LagrangeBasis();

    private static AffineSolution SolveAffineCollocation(double[] y0, double h,
        AffineSystemSpec s0,
        Func<double, AffineSystemSpec> stageSystem)
    {
        var n = y0.Length;
        var stageCount = NumericalMath.RadauStageCount;
        var initial = (double[])y0.Clone();
        for (var row = 0; row < n; row++)
            if (row < s0.Instant.Length && s0.Instant[row])
                initial[row] = s0.InstantTargets[row]!(0.0);

        var solution = new AffineSolution
        {
            M0 = s0.M,
            B0 = s0.B,
            Y0 = initial,
            Duration = h,
            InstantTargets = s0.InstantTargets
        };

        var m0 = s0.M;
        var b0 = s0.B;
        var baseSource = new double[n];
        for (var row = 0; row < n; row++)
        {
            var source = b0[row];
            for (var k = 0; k < n; k++) source += m0[row][k] * initial[k];
            baseSource[row] = source;
        }
        solution.BaseSource = baseSource;

        var scalarMoments = new double[stageCount][];
        var matrixMoments = new double[stageCount][][][];
        var p = new double[stageCount][];
        var w = new double[stageCount][][][];
        var dm = new double[stageCount][][];
        var db = new double[stageCount][];
        for (var i = 0; i < stageCount; i++)
        {
            var c = NumericalMath.RadauC[i];
            if (n == 1)
            {
                var scalar = new double[3];
                if (!NumericalMath.ScalarMoments(m0[0][0] * h, c, scalar))
                    return solution;
                scalarMoments[i] = scalar;
            }
            else
            {
                var a = NumericalMath.CreateMatrix(n);
                for (var row = 0; row < n; row++)
                    for (var k = 0; k < n; k++)
                        a[row][k] = m0[row][k] * h;
                var moments = NumericalMath.MomentMatrices(a, c);
                if (moments is null) return solution;
                matrixMoments[i] = moments;
            }

            p[i] = new double[n];
            for (var row = 0; row < n; row++)
            {
                var value = initial[row];
                if (n == 1)
                    value += h * scalarMoments[i][0] * baseSource[0];
                else
                    for (var k = 0; k < n; k++)
                        value += h * matrixMoments[i][0][row][k] * baseSource[k];
                p[i][row] = value;
            }

            var specJ = stageSystem(c);
            dm[i] = NumericalMath.CreateMatrix(n);
            db[i] = new double[n];
            for (var row = 0; row < n; row++)
            {
                for (var k = 0; k < n; k++) dm[i][row][k] = specJ.M[row][k] - m0[row][k];
                db[i][row] = specJ.B[row] - b0[row];
            }

            w[i] = new double[stageCount][][];
            for (var j = 0; j < stageCount; j++)
            {
                w[i][j] = NumericalMath.CreateMatrix(n);
                for (var m = 0; m < 3; m++)
                {
                    var coefficient = LagrangePolynomialBasis[j][m];
                    for (var row = 0; row < n; row++)
                        for (var k = 0; k < n; k++)
                            w[i][j][row][k] += coefficient *
                                (n == 1
                                    ? (row == 0 && k == 0 ? scalarMoments[i][m] : 0.0)
                                    : matrixMoments[i][m][row][k]);
                }
            }
        }

        var size = stageCount * n;
        var lhs = NumericalMath.CreateMatrix(size);
        var rhs = new double[size];
        for (var i = 0; i < stageCount; i++)
            for (var row = 0; row < n; row++)
            {
                var blockRow = i * n + row;
                lhs[blockRow][blockRow] = 1.0;
                rhs[blockRow] = p[i][row];
                for (var j = 0; j < stageCount; j++)
                    for (var k = 0; k < n; k++)
                    {
                        var wij = w[i][j][row][k];
                        rhs[blockRow] += h * wij * db[j][k];
                        for (var l = 0; l < n; l++)
                            lhs[blockRow][j * n + l] -= h * wij * dm[j][k][l];
                    }
            }

        for (var i = 0; i < stageCount; i++)
            for (var row = 0; row < n; row++)
            {
                if (row >= s0.Instant.Length || !s0.Instant[row]) continue;
                var blockRow = i * n + row;
                for (var column = 0; column < size; column++) lhs[blockRow][column] = 0.0;
                lhs[blockRow][blockRow] = 1.0;
                rhs[blockRow] = s0.InstantTargets[row]!(NumericalMath.RadauC[i]);
            }

        var flat = new double[size];
        if (!NumericalMath.TrySolveLinear(lhs, rhs, flat)) return solution;

        var maxResidual = 0.0;
        var maxRhs = 1.0;
        for (var i = 0; i < size; i++)
        {
            var sum = 0.0;
            for (var k = 0; k < size; k++) sum += lhs[i][k] * flat[k];
            var error = Math.Abs(sum - rhs[i]);
            if (error > maxResidual) maxResidual = error;
            if (Math.Abs(rhs[i]) > maxRhs) maxRhs = Math.Abs(rhs[i]);
        }
        if (maxResidual > ScaledResidualTolerance * maxRhs) return solution;

        solution.Stages = new double[stageCount][];
        for (var i = 0; i < stageCount; i++)
        {
            solution.Stages[i] = new double[n];
            for (var row = 0; row < n; row++) solution.Stages[i][row] = flat[i * n + row];
        }

        solution.Residuals = new double[stageCount][];
        for (var j = 0; j < stageCount; j++)
        {
            solution.Residuals[j] = new double[n];
            for (var row = 0; row < n; row++)
            {
                var value = db[j][row];
                for (var k = 0; k < n; k++)
                    value += dm[j][row][k] * solution.Stages[j][k];
                solution.Residuals[j][row] = value;
            }
        }

        solution.Valid = true;
        foreach (var stage in solution.Stages)
            foreach (var value in stage)
                if (!IsFiniteNumber(value)) solution.Valid = false;
        return solution;
    }

    private struct DownstreamHold
    {
        internal bool BrainDepleting;
        internal bool HeadPinned;
        internal bool LowerPinned;
        internal bool CorePinned;
        internal bool ConsciousnessLosing;
        internal bool[] Increasing;
    }

    private sealed class DownstreamSolution
    {
        internal bool Valid;
        internal AffineSolution Oxygen = new();
        internal AffineSolution Consciousness = new();
        internal AffineSolution[] Visuals = [];
        internal List<string> InitialEvents = [];
    }

    private sealed class LoCTrajectory
    {
        internal bool Contact;
        internal double StartValue;
        internal double Rate;
        internal AffineSolution Consciousness = new();
        internal double PieceStart;
        internal LogicSettings Settings = LogicSettings.Default;
        internal double FirstEventTime = double.PositiveInfinity;
        internal string FirstEventKind = "";
        internal bool Pinned;

        internal double CeilingAt(double t) =>
            LoCCeiling(Consciousness.Evaluate((t - PieceStart) / Consciousness.Duration)[0],
                Settings);

        internal double ValueAt(double t)
        {
            if (Pinned) return 1.0;
            if (t >= FirstEventTime) t = FirstEventTime;
            if (Contact) return CeilingAt(t);
            return StartValue + Rate * (t - PieceStart);
        }
    }

    private static double CeilingDerivative(AffineSolution consciousness, double c,
        LogicSettings settings)
    {
        var level = consciousness.Evaluate(c)[0];
        var range = Math.Max(
            settings.ConsciousnessRecoveryThreshold - settings.ConsciousnessLossThreshold, 1e-9);
        var fraction = (settings.ConsciousnessRecoveryThreshold - level) / range;
        if (fraction is <= 0.0 or >= 1.0) return 0.0;
        var dcDc = -settings.VisualLoCConsciousnessExponent *
            Math.Pow(fraction, settings.VisualLoCConsciousnessExponent - 1.0) / range;
        return dcDc * consciousness.Derivative(c)[0];
    }

    private static double LungEffectiveness(IntervalTrialState trial,
        double gxMagnitude, LogicSettings settings) =>
        Clamp(1.0 - trial.Compression * settings.GyLungCompressionSeverity -
              GxLungImpairment(gxMagnitude, settings) - 0.5 * trial.Respiratory,
            0.1, 1.0);

    private static AffineSystemSpec BuildOxygenSystem(
        Func<double, IntervalTrialState> trialAt, double c,
        bool depleting, bool headPinned, bool lowerPinned, bool corePinned,
        double gxMagnitude, LogicSettings settings)
    {
        var spec = new AffineSystemSpec
        {
            M = NumericalMath.CreateMatrix(3),
            B = new double[3],
            Instant = new bool[3],
            InstantTargets = new Func<double, double>?[3]
        };
        var trial = trialAt(c);
        var transport = settings.OxygenTransportBaseRate *
            (1.0 + settings.OxygenTransportHeartRateSensitivity * (trial.HeartRate - 1.0));
        var kH = 1.8 * transport * trial.Head / settings.RestingBloodHead;
        var kL = 1.8 * transport * trial.Lower / settings.RestingBloodLower;
        var delivery = EffectiveDelivery(ShapedPerfusion(trial.Head, settings),
            trial.HeartRate, trial.Arterial, settings);
        var target = BrainO2Target(delivery, settings);
        var tau = depleting
            ? BrainDepletionTau(delivery, settings)
            : settings.BrainO2RecoveryTau;
        var headInstant = !headPinned && tau <= 1e-9;
        var coreInstant = !corePinned && settings.LungOxygenationRate <= 1e-9;
        var inverseTau = headInstant ? 0.0 : 1.0 / tau;
        var k = coreInstant ? 0.0 : 1.0 / settings.LungOxygenationRate;

        var m = spec.M;
        var b = spec.B;
        m[0][0] = -kH - inverseTau;
        m[0][2] = kH;
        m[1][1] = -kL;
        m[1][2] = kL;
        m[2][0] = trial.Head * kH;
        m[2][1] = trial.Lower * kL;
        m[2][2] = -k - trial.Head * kH - trial.Lower * kL;
        b[0] = target * inverseTau - settings.OxygenConsumptionRateHead;
        b[1] = -settings.OxygenConsumptionRateLower;
        b[2] = k * settings.CoreBloodO2Resting *
               LungEffectiveness(trial, gxMagnitude, settings) -
               settings.OxygenConsumptionRateCore;
        if (headInstant)
        {
            spec.Instant[0] = true;
            spec.InstantTargets[0] = node =>
            {
                var at = trialAt(node);
                return Clamp(BrainO2Target(EffectiveDelivery(
                        ShapedPerfusion(at.Head, settings), at.HeartRate, at.Arterial,
                        settings), settings), settings.BrainO2Floor, 1.0);
            };
        }
        if (coreInstant)
        {
            spec.Instant[2] = true;
            spec.InstantTargets[2] = node =>
                settings.CoreBloodO2Resting *
                LungEffectiveness(trialAt(node), gxMagnitude, settings);
        }
        for (var a = 0; a < 3; a++)
        {
            if (!spec.Instant[a]) continue;
            var targetValue = spec.InstantTargets[a]!(c);
            for (var f = 0; f < 3; f++)
            {
                if (f == a || spec.Instant[f]) continue;
                b[f] += m[f][a] * targetValue;
                m[f][a] = 0.0;
            }
            for (var column = 0; column < 3; column++) m[a][column] = 0.0;
            b[a] = 0.0;
        }
        if (headPinned) { m[0][0] = 0.0; m[0][2] = 0.0; b[0] = 0.0; }
        if (lowerPinned) { m[1][1] = 0.0; m[1][2] = 0.0; b[1] = 0.0; }
        if (corePinned)
        {
            m[2][0] = 0.0; m[2][1] = 0.0; m[2][2] = 0.0; b[2] = 0.0;
        }
        return spec;
    }

    private static AffineSystemSpec BuildConsciousnessSystem(
        Func<double, IntervalTrialState> trialAt, Func<double, double> headO2At,
        double c, bool losing, bool pinned, LogicSettings settings)
    {
        var spec = new AffineSystemSpec
        {
            M = NumericalMath.CreateMatrix(1),
            B = new double[1],
            Instant = new bool[1],
            InstantTargets = new Func<double, double>?[1]
        };
        if (pinned) return spec;
        var trial = trialAt(c);
        var (target, lossTau) = ConsciousnessTargetAndTau(
            O2Normalized(headO2At(c), settings),
            PerfusionNormalized(trial.Head, settings), trial.Pressure, settings);
        var tau = losing ? lossTau : settings.ConsciousnessRecoveryTau;
        if (tau <= 1e-9)
        {
            spec.Instant[0] = true;
            spec.InstantTargets[0] = node => ConsciousnessTargetAndTau(
                O2Normalized(headO2At(node), settings),
                PerfusionNormalized(trialAt(node).Head, settings),
                trialAt(node).Pressure, settings).Target;
            return spec;
        }
        var k = 1.0 / tau;
        spec.M[0][0] = -k;
        spec.B[0] = target * k;
        return spec;
    }

    private static AffineSystemSpec BuildLagSystem(Func<double, double> targetAt,
        double c, double tau)
    {
        var spec = new AffineSystemSpec
        {
            M = NumericalMath.CreateMatrix(1),
            B = new double[1],
            Instant = new bool[1],
            InstantTargets = new Func<double, double>?[1]
        };
        if (tau <= 1e-9)
        {
            spec.Instant[0] = true;
            spec.InstantTargets[0] = targetAt;
            return spec;
        }
        var k = 1.0 / tau;
        spec.M[0][0] = -k;
        spec.B[0] = targetAt(c) * k;
        return spec;
    }

    private static double BisectSign(Func<double, double> f, double lo, double hi,
        int loSign, out int direction)
    {
        direction = -loSign;
        for (var i = 0; i < 200 && hi - lo > PressureBoundRootToleranceSeconds; i++)
        {
            var mid = 0.5 * (lo + hi);
            var fMid = f(mid);
            if (!double.IsFinite(fMid)) return double.NaN;
            if (fMid == 0.0) return mid;
            if ((fMid > 0.0) == (loSign > 0)) lo = mid;
            else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    private static readonly string[] VisualTransitionNames =
        ["VisualTunnelTransition", "VisualRedoutTransition", "VisualGrayscaleTransition"];

    private static readonly string[] OxygenCoordinateNames = ["Head", "Lower", "Core"];

    private static readonly double[] ScanProbes =
    [
        0.5 * NumericalMath.RadauC[0], NumericalMath.RadauC[0],
        0.5 * (NumericalMath.RadauC[0] + NumericalMath.RadauC[1]),
        NumericalMath.RadauC[1],
        0.5 * (NumericalMath.RadauC[1] + NumericalMath.RadauC[2])
    ];

    private static double ScanCrossing(Func<double, double> f, double lo, double hi,
        out int direction)
    {
        direction = 0;
        var span = hi - lo;
        if (span <= 0.0) return double.PositiveInfinity;

        var lastSign = 0;
        var lastT = lo;
        var zeroStart = double.NaN;
        var startZero = false;

        double? Process(double t, double value)
        {
            if (!double.IsFinite(value))
            {
                lastSign = 0;
                zeroStart = double.NaN;
                return null;
            }
            if (value == 0.0)
            {
                if (double.IsNaN(zeroStart))
                {
                    zeroStart = t;
                    startZero = lastSign == 0;
                }
                return null;
            }
            var sign = value > 0.0 ? 1 : -1;
            if (!double.IsNaN(zeroStart))
            {
                double? result = null;
                if (startZero)
                {
                    direction = sign;
                    result = lo;
                }
                else if (lastSign != 0 && sign == -lastSign)
                    result = BisectSign(f, lastT, t, lastSign, out direction);
                zeroStart = double.NaN;
                lastSign = sign;
                lastT = t;
                return result;
            }
            if (lastSign != 0 && sign == -lastSign)
                return BisectSign(f, lastT, t, lastSign, out direction);
            lastSign = sign;
            lastT = t;
            return null;
        }

        var first = Process(lo, f(lo));
        if (first.HasValue)
            return double.IsNaN(first.Value) ? double.PositiveInfinity : first.Value;
        foreach (var probe in ScanProbes)
        {
            var t = lo + probe * span;
            var hit = Process(t, f(t));
            if (hit.HasValue)
                return double.IsNaN(hit.Value) ? double.PositiveInfinity : hit.Value;
        }
        var endHit = Process(hi, f(hi));
        if (endHit.HasValue)
            return double.IsNaN(endHit.Value) ? double.PositiveInfinity : endHit.Value;
        if (!double.IsNaN(zeroStart) && lastSign != 0)
        {
            direction = -lastSign;
            return hi;
        }
        return double.PositiveInfinity;
    }

    private const double RightProbeFraction = 1e-4;

    private static double RightSlope(Func<double, double> f)
    {
        var slope = (f(RightProbeFraction) - f(0.0)) / RightProbeFraction;
        return double.IsFinite(slope) ? slope : 0.0;
    }

    private static bool TargetRises(double target0, double level,
        Func<double, double> targetAt) =>
        target0 > level || (target0 == level && RightSlope(targetAt) > 0.0);

    private static DownstreamHold ComputeHold(IntegrationState state,
        Func<double, IntervalTrialState> trialAt, double span, double gxMagnitude,
        LogicSettings settings)
    {
        var hold = new DownstreamHold { Increasing = new bool[3] };
        var trial = trialAt(0.0);
        var delivery = EffectiveDelivery(ShapedPerfusion(trial.Head, settings),
            trial.HeartRate, trial.Arterial, settings);
        var headO2 = state.BloodO2Head;
        var margin0 = BrainO2Target(delivery, settings) - headO2;
        var probe = BuildOxygenSystem(trialAt, 0.0, margin0 < 0.0, false, false,
            false, gxMagnitude, settings);
        var m = probe.M;
        var b = probe.B;
        var hDot = m[0][0] * headO2 + m[0][2] * state.BloodO2Core + b[0];
        var lDot = m[1][1] * state.BloodO2Lower + m[1][2] * state.BloodO2Core + b[1];
        var cDot = m[2][0] * headO2 + m[2][1] * state.BloodO2Lower +
                   m[2][2] * state.BloodO2Core + b[2];
        hold.BrainDepleting = margin0 < 0.0 ||
            (margin0 == 0.0 && RightSlope(c => BrainO2Target(EffectiveDelivery(
                    ShapedPerfusion(trialAt(c).Head, settings), trialAt(c).HeartRate,
                    trialAt(c).Arterial, settings), settings)) - hDot * span < 0.0);
        hold.HeadPinned =
            (headO2 <= settings.BrainO2Floor && hDot <= 0.0) ||
            (headO2 >= 1.0 && hDot >= 0.0);
        hold.LowerPinned =
            (state.BloodO2Lower <= 0.0 && lDot <= 0.0) ||
            (state.BloodO2Lower >= 1.0 && lDot >= 0.0);
        hold.CorePinned =
            (state.BloodO2Core <= 0.0 && cDot <= 0.0) ||
            (state.BloodO2Core >= 1.0 && cDot >= 0.0);

        double HeadO2Probe(double c) => headO2 + hDot * c * span;
        var cMargin = ConsciousnessTargetAndTau(O2Normalized(headO2, settings),
            PerfusionNormalized(trial.Head, settings), trial.Pressure,
            settings).Target - state.ConsciousnessLevel;
        hold.ConsciousnessLosing = cMargin < 0.0 ||
            (cMargin == 0.0 && RightSlope(c => ConsciousnessTargetAndTau(
                O2Normalized(HeadO2Probe(c), settings),
                PerfusionNormalized(trialAt(c).Head, settings), trialAt(c).Pressure,
                settings).Target) < 0.0);

        double PhysTargetAt(double c) => PhysiologicalVisualTarget(trialAt(c).Head,
            O2Normalized(HeadO2Probe(c), settings), settings);
        double RedoutAt(double c) => RedoutTarget(Math.Max(
            (trialAt(c).Head - settings.RestingBloodHead) / settings.RestingBloodHead,
            0.0), settings);
        hold.Increasing[0] = TargetRises(PhysTargetAt(0.0),
            state.VisualTunnelVisionLevel, PhysTargetAt);
        hold.Increasing[1] = TargetRises(RedoutAt(0.0), state.VisualRedoutLevel,
            RedoutAt);
        hold.Increasing[2] = TargetRises(PhysTargetAt(0.0),
            state.VisualGrayscaleLevel, PhysTargetAt);
        return hold;
    }

    private static DownstreamSolution SolveDownstream(IntegrationState state,
        IntervalUpstream upstream, double cursor, double span, DownstreamHold hold,
        LogicSettings settings)
    {
        var piece = new DownstreamSolution();
        IntervalTrialState TrialAt(double c) => upstream.Evaluate(cursor + c * span);
        var oxygenY0 = new[] { state.BloodO2Head, state.BloodO2Lower, state.BloodO2Core };
        var oxygenSpec0 = BuildOxygenSystem(TrialAt, 0.0, hold.BrainDepleting,
            hold.HeadPinned, hold.LowerPinned, hold.CorePinned, upstream.GxMagnitude,
            settings);
        for (var coord = 0; coord < 3; coord++)
            if (oxygenSpec0.Instant[coord] &&
                oxygenY0[coord] != oxygenSpec0.InstantTargets[coord]!(0.0))
                piece.InitialEvents.Add(
                    $"Instantaneous{OxygenCoordinateNames[coord]}OxygenLimit");
        piece.Oxygen = SolveAffineCollocation(oxygenY0, span, oxygenSpec0,
            c => BuildOxygenSystem(TrialAt, c, hold.BrainDepleting, hold.HeadPinned,
                hold.LowerPinned, hold.CorePinned, upstream.GxMagnitude, settings));
        if (!piece.Oxygen.Valid) return piece;

        var dead = state.IsDead;
        var cSpec0 = BuildConsciousnessSystem(TrialAt,
            c => piece.Oxygen.Evaluate(c)[0], 0.0, hold.ConsciousnessLosing, dead,
            settings);
        if (!dead && cSpec0.Instant[0] &&
            state.ConsciousnessLevel != cSpec0.InstantTargets[0]!(0.0))
            piece.InitialEvents.Add("InstantaneousConsciousnessLimit");
        piece.Consciousness = SolveAffineCollocation(
            new[] { dead ? 0.0 : state.ConsciousnessLevel }, span, cSpec0,
            c => BuildConsciousnessSystem(TrialAt,
                c2 => piece.Oxygen.Evaluate(c2)[0], c, hold.ConsciousnessLosing,
                dead, settings));
        if (!piece.Consciousness.Valid) return piece;

        var visualTaus = new (double In, double Out)[]
        {
            (settings.VisualTunnelVisionInTau, settings.VisualTunnelVisionOutTau),
            (settings.VisualRedoutInTau, settings.VisualRedoutOutTau),
            (settings.VisualGrayscaleInTau, settings.VisualGrayscaleOutTau)
        };
        Func<int, double, double> visualTarget = (channel, c) =>
        {
            var trial = upstream.Evaluate(cursor + c * span);
            if (channel == 1)
                return RedoutTarget(Math.Max(
                    (trial.Head - settings.RestingBloodHead) / settings.RestingBloodHead,
                    0.0), settings);
            return PhysiologicalVisualTarget(trial.Head,
                O2Normalized(piece.Oxygen.Evaluate(c)[0], settings), settings);
        };
        var visualInitial = new[]
        {
            state.VisualTunnelVisionLevel, state.VisualRedoutLevel,
            state.VisualGrayscaleLevel
        };
        var visualInstantNames = new[]
        {
            "InstantaneousTunnelVisionLimit", "InstantaneousRedoutLimit",
            "InstantaneousGrayscaleLimit"
        };
        piece.Visuals = new AffineSolution[3];
        for (var channel = 0; channel < 3; channel++)
        {
            var tau = hold.Increasing[channel]
                ? visualTaus[channel].In
                : visualTaus[channel].Out;
            var lagSpec0 = BuildLagSystem(c => visualTarget(channel, c), 0.0, tau);
            if (lagSpec0.Instant[0] &&
                visualInitial[channel] != lagSpec0.InstantTargets[0]!(0.0))
                piece.InitialEvents.Add(visualInstantNames[channel]);
            piece.Visuals[channel] = SolveAffineCollocation(
                new[] { visualInitial[channel] }, span, lagSpec0,
                c => BuildLagSystem(node => visualTarget(channel, node), c, tau));
            if (!piece.Visuals[channel].Valid) return piece;
        }

        piece.Valid = true;
        return piece;
    }

    private static LoCTrajectory ChaseLoC(IntegrationState state,
        AffineSolution consciousness, double cursor, double span, LogicSettings settings)
    {
        var trajectory = new LoCTrajectory
        {
            Consciousness = consciousness,
            PieceStart = cursor,
            Settings = settings,
            StartValue = state.VisualLoCLevel
        };
        if (state.IsUnconscious || state.IsDead)
        {
            trajectory.Pinned = true;
            return trajectory;
        }

        var end = cursor + span;
        var ceiling0 = trajectory.CeilingAt(cursor);
        var ceilingDerivative0 = CeilingDerivative(consciousness, 0.0, settings);
        var increase = settings.VisualLoCIncreaseRate;
        var decrease = settings.VisualLoCDecreaseRate;
        trajectory.Contact = Math.Abs(state.VisualLoCLevel - ceiling0) <= 1e-9 &&
                             ceilingDerivative0 <= increase &&
                             ceilingDerivative0 >= -decrease;
        if (trajectory.Contact)
        {
            var exitUp = ScanCrossing(
                t => CeilingDerivative(consciousness, (t - cursor) / span, settings) - increase,
                cursor, end, out _);
            var exitDown = ScanCrossing(
                t => -decrease -
                     CeilingDerivative(consciousness, (t - cursor) / span, settings),
                cursor, end, out _);
            var exit = Math.Min(exitUp, exitDown);
            if (exit <= end)
            {
                trajectory.FirstEventTime = exit;
                trajectory.FirstEventKind = "LoCContactExit";
            }
        }
        else
        {
            trajectory.Rate = state.VisualLoCLevel < ceiling0 ? increase : -decrease;
            var v0 = state.VisualLoCLevel;
            var rate = trajectory.Rate;
            var contact = ScanCrossing(
                t => v0 + rate * (t - cursor) - trajectory.CeilingAt(t), cursor, end,
                out _);
            if (contact <= end)
            {
                trajectory.FirstEventTime = contact;
                trajectory.FirstEventKind = "LoCContactEntry";
            }
        }
        return trajectory;
    }

    private static IntegrationState FullStateAt(double t, IntegrationState baseState,
        IntervalUpstream upstream, DownstreamSolution piece, LoCTrajectory loc,
        double cursor, double span)
    {
        var circ = upstream.CirculationStateAt(t);
        var c = span > 0.0 ? (t - cursor) / span : 0.0;
        var oxygen = piece.Oxygen.Evaluate(c);
        var consciousness = baseState.IsDead
            ? 0.0
            : piece.Consciousness.Evaluate(c)[0];
        var locValue = baseState.IsUnconscious || baseState.IsDead
            ? 1.0
            : loc.ValueAt(t);
        return new IntegrationState(circ.BloodHead, circ.BloodLower,
            circ.HeartRateMultiplier, circ.CardioFatigue, upstream.PressureAt(t),
            circ.RespiratoryFatigue)
        {
            StrainingLevel = circ.StrainingLevel,
            StrainingFatigue = circ.StrainingFatigue,
            GSuitFatigue = circ.GSuitFatigue,
            BloodO2Head = oxygen[0],
            BloodO2Lower = oxygen[1],
            BloodO2Core = oxygen[2],
            ArterialOxygenation = upstream.ArterialAt(t),
            ConsciousnessLevel = consciousness,
            LungCompressionLevel = upstream.CompressionAt(t),
            PainLevel = upstream.PainAt(t),
            GyNeckFatigue = upstream.NeckAt(t),
            GyNeckFatigueDeathDwell = upstream.NeckDwellAt(t),
            SuddenLoCAccumulator = upstream.SuddenAt(t),
            VisualTunnelVisionLevel = piece.Visuals[0].Evaluate(c)[0],
            VisualRedoutLevel = piece.Visuals[1].Evaluate(c)[0],
            VisualGrayscaleLevel = piece.Visuals[2].Evaluate(c)[0],
            VisualLoCLevel = locValue,
            IsUnconscious = baseState.IsUnconscious,
            InSuddenLoC = baseState.InSuddenLoC,
            IsDead = baseState.IsDead
        };
    }

    private static int EventRank(string kind) => kind switch
    {
        "NeckDeathExit" => 0,
        "Death" => 1,
        "SuddenTrigger" => 2,
        "SuddenRecovery" => 3,
        "ConsciousnessLost" => 4,
        "ConsciousnessRecovered" => 5,
        _ => 6
    };

    private static string DownstreamViolation(IntegrationState stage,
        LogicSettings settings)
    {
        const double tolerance = 1e-9;
        if (!IsFiniteNumber(stage.BloodHead) || !IsFiniteNumber(stage.BloodLower) ||
            !IsFiniteNumber(stage.BloodCore) || !IsFiniteNumber(stage.HeartRateMultiplier) ||
            !IsFiniteNumber(stage.CardioFatigue) ||
            !IsFiniteNumber(stage.CerebralPressureImpairment) ||
            !IsFiniteNumber(stage.RespiratoryFatigue) ||
            !IsFiniteNumber(stage.StrainingLevel) ||
            !IsFiniteNumber(stage.StrainingFatigue) ||
            !IsFiniteNumber(stage.GSuitFatigue) ||
            !IsFiniteNumber(stage.BloodO2Head) || !IsFiniteNumber(stage.BloodO2Core) ||
            !IsFiniteNumber(stage.BloodO2Lower) ||
            !IsFiniteNumber(stage.ArterialOxygenation) ||
            !IsFiniteNumber(stage.ConsciousnessLevel) ||
            !IsFiniteNumber(stage.LungCompressionLevel) ||
            !IsFiniteNumber(stage.PainLevel) ||
            !IsFiniteNumber(stage.GyNeckFatigue) ||
            !IsFiniteNumber(stage.GyNeckFatigueDeathDwell) ||
            !IsFiniteNumber(stage.SuddenLoCAccumulator) ||
            !IsFiniteNumber(stage.VisualTunnelVisionLevel) ||
            !IsFiniteNumber(stage.VisualRedoutLevel) ||
            !IsFiniteNumber(stage.VisualGrayscaleLevel) ||
            !IsFiniteNumber(stage.VisualLoCLevel))
            return "nonfinite downstream state";
        if (stage.BloodO2Head < settings.BrainO2Floor - tolerance ||
            stage.BloodO2Head > 1.0 + tolerance ||
            stage.BloodO2Core is < -tolerance or > 1.0 + tolerance ||
            stage.BloodO2Lower is < -tolerance or > 1.0 + tolerance ||
            stage.ArterialOxygenation is < -tolerance or > 1.0 + tolerance ||
            stage.ConsciousnessLevel is < -tolerance or > 1.0 + tolerance ||
            stage.LungCompressionLevel is < -tolerance or > 1.0 + tolerance ||
            stage.PainLevel is < -tolerance or > 1.0 + tolerance ||
            stage.GyNeckFatigue is < -tolerance or > 1.0 + tolerance ||
            stage.GyNeckFatigueDeathDwell is < -tolerance or > 1.0 + tolerance ||
            stage.SuddenLoCAccumulator is < -tolerance or > 1.0 + tolerance ||
            stage.VisualTunnelVisionLevel is < -tolerance or > 1.0 + tolerance ||
            stage.VisualRedoutLevel is < -tolerance or > 1.0 + tolerance ||
            stage.VisualGrayscaleLevel is < -tolerance or > 1.0 + tolerance ||
            stage.VisualLoCLevel is < -tolerance or > 1.0 + tolerance)
            return "downstream state outside bounds";
        return "";
    }

    private static IntegrationState ApplyEvent(IntegrationState state, string kind,
        LogicSettings settings)
    {
        switch (kind)
        {
            case "Death":
                return state with
                {
                    IsDead = true,
                    ConsciousnessLevel = 0.0,
                    IsUnconscious = true,
                    VisualLoCLevel = 1.0,
                    GyNeckFatigueDeathDwell = 1.0
                };
            case "SuddenTrigger":
            {
                var dropped = Math.Max(0.0,
                    state.ConsciousnessLevel - settings.SuddenLoCConsciousnessDrop);
                var unconscious = state.IsUnconscious ||
                                  dropped <= settings.ConsciousnessLossThreshold;
                return state with
                {
                    InSuddenLoC = true,
                    SuddenLoCAccumulator = Math.Abs(state.SuddenLoCAccumulator - 0.8) <= 1e-6
                        ? 0.8
                        : state.SuddenLoCAccumulator,
                    ConsciousnessLevel = dropped,
                    IsUnconscious = unconscious,
                    VisualLoCLevel = unconscious ? 1.0 : state.VisualLoCLevel
                };
            }
            case "SuddenRecovery":
                return state with
                {
                    InSuddenLoC = false,
                    SuddenLoCAccumulator = Math.Abs(state.SuddenLoCAccumulator - 0.3) <= 1e-6
                        ? 0.3
                        : state.SuddenLoCAccumulator
                };
            case "ConsciousnessLost":
                return state with
                {
                    IsUnconscious = true,
                    VisualLoCLevel = 1.0,
                    ConsciousnessLevel =
                        Math.Abs(state.ConsciousnessLevel -
                                 settings.ConsciousnessLossThreshold) <= 1e-6
                            ? settings.ConsciousnessLossThreshold
                            : state.ConsciousnessLevel
                };
            case "ConsciousnessRecovered":
                return state with
                {
                    IsUnconscious = false,
                    ConsciousnessLevel =
                        Math.Abs(state.ConsciousnessLevel -
                                 settings.ConsciousnessRecoveryThreshold) <= 1e-6
                            ? settings.ConsciousnessRecoveryThreshold
                            : state.ConsciousnessLevel
                };
            case "NeckDeathExit":
                return state with { GyNeckFatigueDeathDwell = 0.0 };
            case "OxygenBoundEntryHead":
                return state with
                {
                    BloodO2Head = Math.Abs(state.BloodO2Head - settings.BrainO2Floor) <
                                  Math.Abs(state.BloodO2Head - 1.0)
                        ? settings.BrainO2Floor
                        : 1.0
                };
            case "OxygenBoundEntryLower":
                return state with { BloodO2Lower = state.BloodO2Lower < 0.5 ? 0.0 : 1.0 };
            case "OxygenBoundEntryCore":
                return state with { BloodO2Core = state.BloodO2Core < 0.5 ? 0.0 : 1.0 };
            default:
                return state;
        }
    }

    internal IntegrationResult AdvanceInterval(
        in IntegrationState initial,
        double dt,
        double gx,
        double gy,
        double gz,
        LogicSettings settings)
    {
        var circulation = AdvanceCirculationInterval(in initial, dt, gx, gy, gz, settings);
        if (!circulation.Converged || circulation.ModeCrossings.Length != 0 ||
            circulation.StateViolations.Length != 0 || circulation.Segments.Length == 0)
        {
            return circulation;
        }

        var upstream = new IntervalUpstream(circulation, in initial, gx, gy, gz, settings);
        var events = new List<IntegrationEvent>(circulation.Events);
        var violations = new List<string>();
        var segments = new List<IntegrationSegment>();
        var stages = new List<IntegrationState>();

        var state = initial;
        var cursor = 0.0;

        if (upstream.NeckInitiallyClamped)
        {
            state = state with { GyNeckFatigue = upstream.NeckCeiling };
            events.Add(new IntegrationEvent("NeckCeilingClamp", 0.0, state));
        }
        if (state.GyNeckFatigue < settings.GyNeckFatigueDeathLevel &&
            state.GyNeckFatigueDeathDwell > 0.0)
        {
            state = state with { GyNeckFatigueDeathDwell = 0.0 };
            events.Add(new IntegrationEvent("NeckDeathExit", 0.0, state));
        }

        void RecordPiece(DownstreamSolution piece, LoCTrajectory loc, double acceptEnd)
        {
            if (acceptEnd <= cursor + 1e-12) return;
            var s = state;
            var pieceSpan = acceptEnd - cursor;
            var stageStates = new IntegrationState[NumericalMath.RadauStageCount];
            for (var i = 0; i < NumericalMath.RadauStageCount; i++)
                stageStates[i] = FullStateAt(
                    cursor + NumericalMath.RadauC[i] * pieceSpan, s, upstream, piece,
                    loc, cursor, pieceSpan);
            IntegrationSegment bloodSegment = null!;
            foreach (var candidate in circulation.Segments)
                if (candidate.StartOffset <= cursor) bloodSegment = candidate;
            segments.Add(new IntegrationSegment(cursor, pieceSpan, s,
                FullStateAt(acceptEnd, s, upstream, piece, loc, cursor, pieceSpan),
                stageStates, bloodSegment.Bounds, bloodSegment.HeadCoefficients,
                bloodSegment.LowerCoefficients, bloodSegment.RateCoefficients,
                bloodSegment.CardioCoefficients)
            {
                CoefficientStart = bloodSegment.StartOffset
            });
            stages.AddRange(stageStates);
        }

        var guard = 0;
        while (cursor < dt - 1e-12)
        {
            if (++guard > 512)
            {
                violations.Add("downstream event loop did not terminate");
                break;
            }

            var immediate = "";
            if (!state.IsDead && upstream.DeathCompletionTime() <= cursor)
                immediate = "Death";
            else if (upstream.SuddenTriggerTime(state.InSuddenLoC, state.IsDead) <= cursor)
                immediate = "SuddenTrigger";
            else if (upstream.SuddenRecoveryTime(state.InSuddenLoC) <= cursor)
                immediate = "SuddenRecovery";
            else if (!state.IsDead && !state.IsUnconscious &&
                     state.ConsciousnessLevel <= settings.ConsciousnessLossThreshold)
                immediate = "ConsciousnessLost";
            else if (!state.IsDead && state.IsUnconscious &&
                     state.ConsciousnessLevel >= settings.ConsciousnessRecoveryThreshold)
                immediate = "ConsciousnessRecovered";
            if (immediate.Length != 0)
            {
                state = ApplyEvent(state, immediate, settings);
                events.Add(new IntegrationEvent(immediate, cursor, state));
                continue;
            }

            var span = dt - cursor;
            var end = cursor + span;
            var trial0 = upstream.Evaluate(cursor);
            var hold = ComputeHold(state, trial0, upstream.GxMagnitude, settings);
            var piece = SolveDownstream(state, upstream, cursor, span, hold, settings);
            if (!piece.Valid)
            {
                violations.Add($"downstream piece invalid at offset {cursor:R}");
                break;
            }

            var loc = ChaseLoC(state, piece.Consciousness, cursor, span, settings);

            var candidates = new List<(double Time, string Kind)>();
            void Offer(double t, string eventKind)
            {
                if (t > cursor + 1e-12 && t < end)
                    candidates.Add((t, eventKind));
            }

            Offer(upstream.NeckClampTime, "NeckCeilingClamp");
            Offer(upstream.NeckDeathEntry, "NeckDeathEntry");
            Offer(upstream.NeckDeathExit, "NeckDeathExit");
            if (!state.IsDead) Offer(upstream.DeathCompletionTime(), "Death");
            Offer(upstream.SuddenTriggerTime(state.InSuddenLoC, state.IsDead),
                "SuddenTrigger");
            Offer(upstream.SuddenRecoveryTime(state.InSuddenLoC), "SuddenRecovery");
            Offer(upstream.RespiratoryBoundTime, "RespiratoryCeiling");
            Offer(upstream.SuddenBoundTime, "SuddenAccumulatorBound");
            foreach (var blood in circulation.Segments)
                if (blood.StartOffset > cursor)
                    Offer(blood.StartOffset, "BloodSegmentBoundary");
            foreach (var (entry, release) in upstream.PressureEpisodes)
            {
                Offer(entry, "PressureBoundBoundary");
                Offer(release, "PressureBoundBoundary");
            }
            if (loc.FirstEventTime > cursor)
                Offer(loc.FirstEventTime, loc.FirstEventKind);

            double HeadO2(double t) => piece.Oxygen.Evaluate((t - cursor) / span)[0];
            double Consciousness(double t) =>
                piece.Consciousness.Evaluate((t - cursor) / span)[0];

            double BrainMargin(double t)
            {
                var trial = upstream.Evaluate(t);
                return BrainO2Target(EffectiveDelivery(
                    ShapedPerfusion(trial.Head, settings), trial.HeartRate,
                    trial.Arterial, settings), settings) - HeadO2(t);
            }

            Offer(ScanCrossing(BrainMargin, cursor, end, out _),
                "BrainO2ModeTransition");

            var coordPins = new[] { hold.HeadPinned, hold.LowerPinned, hold.CorePinned };
            for (var coord = 0; coord < 3; coord++)
            {
                var index = coord;
                var lowBound = index == 0 ? settings.BrainO2Floor : 0.0;
                double Value(double t) => piece.Oxygen.Evaluate((t - cursor) / span)[index];
                if (!coordPins[index])
                {
                    var low = ScanCrossing(t => Value(t) - lowBound, cursor, end, out _);
                    var high = ScanCrossing(t => 1.0 - Value(t), cursor, end, out _);
                    Offer(Math.Min(low, high),
                        $"OxygenBoundEntry{OxygenCoordinateNames[index]}");
                }
                else
                {
                    var atLower = Math.Abs(Value(cursor) - lowBound) <
                                  Math.Abs(Value(cursor) - 1.0);
                    double NaturalDerivative(double t)
                    {
                        var trial = upstream.Evaluate(t);
                        var natural = BuildOxygenSystem(_ => trial, 0.0,
                            hold.BrainDepleting, false, false, false,
                            upstream.GxMagnitude, settings);
                        var y = piece.Oxygen.Evaluate((t - cursor) / span);
                        var derivative = natural.B[index];
                        for (var k = 0; k < 3; k++)
                            derivative += natural.M[index][k] * y[k];
                        return atLower ? derivative : -derivative;
                    }

                    Offer(ScanCrossing(NaturalDerivative, cursor, end, out _),
                        $"OxygenBoundRelease{OxygenCoordinateNames[index]}");
                }
            }

            if (!state.IsDead)
            {
                double ConsciousnessMargin(double t)
                {
                    var trial = upstream.Evaluate(t);
                    var (target, _) = ConsciousnessTargetAndTau(
                        O2Normalized(HeadO2(t), settings),
                        PerfusionNormalized(trial.Head, settings), trial.Pressure,
                        settings);
                    return target - Consciousness(t);
                }

                Offer(ScanCrossing(ConsciousnessMargin, cursor, end, out _),
                    "ConsciousnessModeTransition");

                var threshold = state.IsUnconscious
                    ? settings.ConsciousnessRecoveryThreshold
                    : settings.ConsciousnessLossThreshold;
                var hysteresis = ScanCrossing(t => Consciousness(t) - threshold, cursor,
                    end, out var hysteresisDirection);
                var validCrossing = state.IsUnconscious
                    ? hysteresisDirection > 0
                    : hysteresisDirection < 0;
                if (validCrossing)
                    Offer(hysteresis,
                        state.IsUnconscious
                            ? "ConsciousnessRecovered"
                            : "ConsciousnessLost");
            }

            for (var channel = 0; channel < 3; channel++)
            {
                var index = channel;
                double VisualTarget(double t)
                {
                    var trial = upstream.Evaluate(t);
                    return index == 1
                        ? RedoutTarget(Math.Max(
                            (trial.Head - settings.RestingBloodHead) /
                            settings.RestingBloodHead, 0.0), settings)
                        : PhysiologicalVisualTarget(trial.Head,
                            O2Normalized(HeadO2(t), settings), settings);
                }

                Offer(ScanCrossing(
                    t => VisualTarget(t) -
                         piece.Visuals[index].Evaluate((t - cursor) / span)[0],
                    cursor, end, out _), VisualTransitionNames[index]);
            }

            var restHead = settings.RestingBloodHead;
            var softMin = settings.ConsciousnessPerfusionSoftMinRatio;
            double VisualReserve(double t)
            {
                var perf = Clamp(upstream.HeadAt(t) / restHead, 0.0, 1.0);
                var o2n = O2Normalized(HeadO2(t), settings);
                return 0.7 * Clamp((perf - 0.45) / 0.55, 0.0, 1.0) +
                       0.3 * Clamp((o2n - 0.15) / 0.85, 0.0, 1.0);
            }

            Offer(ScanCrossing(t => upstream.HeadAt(t) - softMin * restHead, cursor, end,
                out _), "PerfusionSoftMinBound");
            Offer(ScanCrossing(t => upstream.HeadAt(t) - restHead, cursor, end, out _),
                "PerfusionRangeBound");
            Offer(ScanCrossing(
                t => upstream.HeadAt(t) - (0.25 * (1.0 - softMin) + softMin) * restHead,
                cursor, end, out _), "PerfusionCapBranch");
            Offer(ScanCrossing(
                t => upstream.HeadAt(t) -
                    (settings.ConsciousnessCriticalPerfusionNorm * (1.0 - softMin) +
                     softMin) * restHead, cursor, end, out _), "CriticalPerfusionBranch");
            Offer(ScanCrossing(t => HeadO2(t) - settings.BrainO2Blackout, cursor, end,
                out _), "O2NormFloor");
            Offer(ScanCrossing(t => HeadO2(t) - settings.BrainO2Full, cursor, end, out _),
                "O2NormCeiling");
            Offer(ScanCrossing(
                t => HeadO2(t) - (settings.BrainO2Blackout +
                    settings.ConsciousnessCriticalO2Norm *
                    (settings.BrainO2Full - settings.BrainO2Blackout)),
                cursor, end, out _), "CriticalO2Branch");
            Offer(ScanCrossing(
                t => upstream.PressureAt(t) - settings.CerebralPressureImpairmentDeadband,
                cursor, end, out _), "PressureDeadbandBranch");
            Offer(ScanCrossing(
                t => upstream.PressureAt(t) -
                    (settings.CerebralPressureImpairmentDeadband +
                     settings.ConsciousnessCriticalPressureNorm *
                     (1.0 - settings.CerebralPressureImpairmentDeadband)),
                cursor, end, out _), "CriticalPressureBranch");
            Offer(ScanCrossing(
                t => upstream.HeadAt(t) -
                    restHead * (1.0 + settings.VisualRedoutOnsetHeadBloodOverfill),
                cursor, end, out _), "RedoutOnset");
            Offer(ScanCrossing(
                t => upstream.HeadAt(t) -
                    restHead * (1.0 + settings.VisualRedoutFullHeadBloodOverfill),
                cursor, end, out _), "RedoutFull");
            Offer(ScanCrossing(t => upstream.HeadAt(t) - 0.45 * restHead, cursor, end,
                out _), "VisualPerfBranch");
            Offer(ScanCrossing(
                t => HeadO2(t) - (settings.BrainO2Blackout +
                    0.15 * (settings.BrainO2Full - settings.BrainO2Blackout)),
                cursor, end, out _), "VisualO2Branch");
            Offer(ScanCrossing(
                t => 0.9 - upstream.CompressionAt(t) *
                    settings.GyLungCompressionSeverity -
                    GxLungImpairment(upstream.GxMagnitude, settings) -
                    0.5 * upstream.RespiratoryAt(t),
                cursor, end, out _), "LungEffectivenessFloor");
            Offer(ScanCrossing(t => VisualReserve(t) - 0.82, cursor, end, out _),
                "VisualDeficitRamp");
            Offer(ScanCrossing(t => VisualReserve(t), cursor, end, out _),
                "VisualDeficitCeiling");
            Offer(ScanCrossing(
                t => ShapedPerfusion(upstream.HeadAt(t), settings) -
                    Clamp(settings.BrainO2HypoperfusionThreshold, 0.01, 1.0),
                cursor, end, out _), "HypoperfusionThreshold");
            Offer(ScanCrossing(
                t => DeliveryCore(ShapedPerfusion(upstream.HeadAt(t), settings), settings),
                cursor, end, out _), "DeliveryFloor");
            Offer(ScanCrossing(
                t => DeliveryCore(ShapedPerfusion(upstream.HeadAt(t), settings),
                    settings) - 1.0, cursor, end, out _), "DeliveryCeiling");

            if (candidates.Count == 0)
            {
                RecordPiece(piece, loc, end);
                state = FullStateAt(end, state, upstream, piece, loc, cursor, span);
                cursor = end;
                continue;
            }

            var bestTime = candidates[0].Time;
            foreach (var candidate in candidates)
                if (candidate.Time < bestTime) bestTime = candidate.Time;
            var due = new List<string>();
            foreach (var candidate in candidates)
            {
                if (candidate.Time - bestTime > FullIntervalEventScanTolerance) continue;
                if (!due.Contains(candidate.Kind)) due.Add(candidate.Kind);
            }
            due.Sort((a, b) => EventRank(a).CompareTo(EventRank(b)));

            var prefixSpan = bestTime - cursor;
            var prefixPiece = SolveDownstream(state, upstream, cursor, prefixSpan,
                hold, settings);
            if (!prefixPiece.Valid)
            {
                violations.Add($"downstream prefix invalid at offset {cursor:R}");
                break;
            }
            var prefixLoc = ChaseLoC(state, prefixPiece.Consciousness, cursor,
                prefixSpan, settings);
            RecordPiece(prefixPiece, prefixLoc, bestTime);
            state = FullStateAt(bestTime, state, upstream, prefixPiece, prefixLoc,
                cursor, prefixSpan);
            cursor = bestTime;
            foreach (var kind in due)
            {
                state = ApplyEvent(state, kind, settings);
                events.Add(new IntegrationEvent(kind, bestTime, state));
            }
        }

        foreach (var diagnostic in upstream.Diagnostics) violations.Add(diagnostic);
        var finalViolation = DownstreamViolation(state, settings);
        if (finalViolation.Length != 0) violations.Add(finalViolation);
        foreach (var stage in stages)
        {
            var violation = DownstreamViolation(stage, settings);
            if (violation.Length != 0)
            {
                violations.Add(violation);
                break;
            }
        }

        var converged = violations.Count == 0 && cursor >= dt - 1e-12;
        return new IntegrationResult(initial, state, stages.ToArray(),
            circulation.Iterations, circulation.MaxScaledResidual, converged,
            circulation.ModeCrossings, violations.ToArray(),
            circulation.PressureBoundEntries, circulation.PressureBoundReleases,
            circulation.CoreBoundEntries, circulation.CoreBoundReleases,
            circulation.HeadBoundEntries, circulation.HeadBoundReleases,
            circulation.LowerBoundEntries, circulation.LowerBoundReleases,
            circulation.ModeTransitions, segments.ToArray(), events.ToArray());
    }
    #endregion
}
