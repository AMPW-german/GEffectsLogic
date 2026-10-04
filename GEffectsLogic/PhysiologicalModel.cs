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

    private static double StepTowardsLinear(double current, double target, double tau, double dt)
    {
        if (tau <= 1e-9) return target;

        // Backward Euler blend (A-stable, same fixed point for different dt)
        var alpha = dt / (tau + dt);
        return current + (target - current) * alpha;
    }

    private double GetHeadBloodOverfill(double headBlood) =>
        Math.Max((headBlood - Settings.RestingBloodHead) / Settings.RestingBloodHead, 0.0);

    private double PressureImpairmentBuildRate(double overfill) =>
        Settings.CerebralPressureImpairmentMaxBuildRate /
        (1.0 + Math.Exp(-Settings.CerebralPressureImpairmentExponent *
                        (overfill - Settings.CerebralPressureImpairmentMidOverfill)));

    private double StepHeadBloodImplicit(
    double current,
    double hydrostaticRate,
    double returnRate,
    double dt)
    {
        var resting = Settings.RestingBloodHead;
        var passiveFactor = 1.0 + dt * returnRate;
        var drivenHeadBlood = current + dt * (hydrostaticRate + returnRate * resting);
        var noPressureHeadBlood = drivenHeadBlood / passiveFactor;

        if (noPressureHeadBlood <= resting) return noPressureHeadBlood;

        var lower = resting;
        var upper = noPressureHeadBlood;

        for (var i = 0; i < 48; i++)
        {
            var candidate = 0.5 * (lower + upper);
            var overfill = GetHeadBloodOverfill(candidate);
            var pressureReturnRate = Settings.HeadPressureReturnRate *
                                     (Math.Exp(Settings.HeadPressureReturnExponent * overfill) - 1.0);
            var residual = passiveFactor * candidate + dt * pressureReturnRate - drivenHeadBlood;

            if (residual > 0.0)
                upper = candidate;
            else
                lower = candidate;
        }

        return 0.5 * (lower + upper);
    }

    /// <summary>
    ///     Advance the model by deltaTime seconds under the given G-force vector.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <param name="gx">Current Gx (positive = chest-to-back).</param>
    /// <param name="gy">Current Gy (lateral).</param>
    /// <param name="gz">Current Gz (positive = headward-to-footward).</param>
    public virtual void Update(double dt, double gx, double gy, double gz)
    {
        // TODO:
        // Shorter GLoC time at high Gz-, longer GLoC time at moderate (3) Gz-, less consciousness loss at low Gz- (0.5-2 Gz-)

        // Keep dt untouched here (guarded by LogicInstance).

        // Calculate G magnitudes for tolerance modifiers
        var gxMagnitude = Math.Abs(gx);
        var gyMagnitude = Math.Abs(gy);

        // Gx: Improves short-term Gz tolerance
        gxEffectiveTolerance = 1.0 + Settings.GxToleranceImprovementFactor * gxMagnitude;

        // Gy: Severely reduces Gz tolerance (lowest tolerance axis) - non-linear scaling
        var gyToleranceReduction = Settings.GyToleranceReductionBase * Math.Pow(gyMagnitude, Settings.GyToleranceNonlinearity);
        gyEffectiveTolerance = 1.0 - gyToleranceReduction;
        gyEffectiveTolerance = Clamp(gyEffectiveTolerance, 0.1, 1.0); // Minimum 10% tolerance

        // Combine tolerance modifiers
        var combinedTolerance = gxEffectiveTolerance * gyEffectiveTolerance;

        // Positive Gz pushes blood from head → lower body
        // Negative Gz pushes blood from lower body → head
        // The shift rate is proportional to Gz magnitude beyond the 1G baseline. Subtracting the
        // 1G-equivalent makes normal upright 1G the neutral point (head blood ~ resting), so level
        // flight settles at near-full consciousness while high +Gz still pools strongly.
        var gzNetScaled = Math.Sign(gz) * Math.Pow(Math.Abs(gz) / combinedTolerance, Settings.HydrostaticShiftExponent) - 1.0;

        // Drive straining level from +Gz with first-order lag
        var targetStraining = 0.0;
        if (gz > Settings.StrainingStartGz) targetStraining = (gz - Settings.StrainingStartGz) / (Settings.StrainingFullGz - Settings.StrainingStartGz);
        targetStraining = Clamp(targetStraining, 0.0, 1.0);
        strainingLevel = StepTowardsLinear(strainingLevel, targetStraining, Settings.StrainingTau, dt);

        // Fatigue: straining fatigue fills while strainingLevel is high, drains slowly at rest
        var strainingFatigueBuildRate = Settings.StrainingFatigueBuildRate * strainingLevel * strainingLevel;
        var strainingFatigueDecayRate = 1.0 / Settings.StrainingFatigueRecoveryTau;
        strainingFatigue += strainingLevel > 0.01 ? strainingFatigueBuildRate * dt : -strainingFatigueDecayRate * strainingFatigue * dt;
        strainingFatigue = Clamp(strainingFatigue, 0.0, 1.0);

        // G-suit fatigue builds more slowly (mechanical, outlasts the pilot's AGSM), also drains slowly
        var gSuitFatigueBuildRate = Settings.GSuitFatigueBuildRate * strainingLevel;
        var gSuitFatigueDecayRate = 1.0 / Settings.GSuitFatigueRecoveryTau;
        gSuitFatigue += strainingLevel > 0.01 ? gSuitFatigueBuildRate * dt : -gSuitFatigueDecayRate * gSuitFatigue * dt;
        gSuitFatigue = Clamp(gSuitFatigue, 0.0, 1.0);

        // Effective straining: human AGSM component fully degrades with strainingFatigue
        var effectiveStraining = strainingLevel * (1.0 - strainingFatigue);

        // Effective g-suit: mechanical suit retains a passive fraction, only the active compression degrades
        var gSuitActiveFraction = 1.0 - Settings.GSuitPassiveFraction;
        var effectiveGSuit = Settings.GSuitEffectiveness * (Settings.GSuitPassiveFraction + gSuitActiveFraction * (1.0 - gSuitFatigue));

        // Suit effect only for +Gz loading, coupled to effective straining
        var suitActivation = Clamp(effectiveGSuit * effectiveStraining, 0.0, 1.0);
        var suit = gz > 0.0 ? suitActivation : 0.0;

        // Mild global scaling + targeted redistribution
        var effectiveGzShift = gzNetScaled * (1.0 - Settings.GSuitGlobalShiftReductionMax * suit);
        var coreLowerFractionEffective = Clamp(Settings.CoreLowerShiftFraction * (1.0 - Settings.GSuitCoreLowerReductionMax * suit), 0.05, 0.95);

        Logger.Log($"effectiveGzShift: {effectiveGzShift}, coreLowerFractionEffective: {coreLowerFractionEffective}", logicInstance);

        // Blood flow rate between compartments
        var shiftRate = Settings.HydrostaticShiftRate * effectiveGzShift;
        var shiftHeadRate = -shiftRate;
        var shiftCoreRate = shiftRate;
        var shiftLowerRate = shiftRate * coreLowerFractionEffective;

        // Passive return (boost lower pool return under suit)
        var returnRate = Settings.PassiveReturnRate * heartRateMultiplier;
        var lowerReturnRate = returnRate * (1.0 + Settings.GSuitLowerReturnBoostMax * suit);

        bloodHead = StepHeadBloodImplicit(
            bloodHead,
            shiftHeadRate,
            returnRate,
            dt);

        bloodCore = (bloodCore + (shiftCoreRate + returnRate * Settings.RestingBloodCore) * dt) / (1.0 + returnRate * dt);
        bloodLower = (bloodLower + (shiftLowerRate + lowerReturnRate * Settings.RestingBloodLower) * dt) / (1.0 + lowerReturnRate * dt);

        // Enforce conservation (redistribute any numerical drift)
        var total = bloodHead + bloodCore + bloodLower;
        bloodHead /= total;
        bloodCore /= total;
        bloodLower /= total;

        // Clamp to prevent negative volumes
        bloodHead = Math.Max(bloodHead, 0.0);
        bloodCore = Math.Max(bloodCore, 0.0);
        bloodLower = Math.Max(bloodLower, 0.0);

        // Re-normalize after clamp
        total = bloodHead + bloodCore + bloodLower;
        bloodHead /= total;
        bloodCore /= total;
        bloodLower /= total;

        // enforce residual head blood floor while preserving total = 1.0
        if (bloodHead < Settings.MinHeadBloodFraction)
        {
            bloodHead = Settings.MinHeadBloodFraction;
            var remaining = 1.0 - bloodHead;
            var coreLower = bloodCore + bloodLower;

            if (coreLower > 1e-9)
            {
                bloodCore = remaining * (bloodCore / coreLower);
                bloodLower = remaining * (bloodLower / coreLower);
            }
            else
            {
                bloodCore = remaining * 0.45;
                bloodLower = remaining * 0.55;
            }
        }

        var headOverfill = GetHeadBloodOverfill(bloodHead);

        // Impairment accrues faster the more the head is overfilled. The build rate follows a
        // smooth logistic in overfill: it is negligible near resting, rises steeply through the
        // physiological mid-overfill, and saturates at high overfill so extreme negative-G G-LOC
        // times flatten out instead of collapsing toward zero. The resting-overfill baseline is
        // subtracted so no impairment accrues (and recovery is complete) at rest. A second,
        // capped term driven by the sustained -Gz input models low-level congestion discomfort
        // (headache) that keeps prolonged mild negative G below full consciousness.
        var pressureImpairmentBuildRate =
            Math.Max(PressureImpairmentBuildRate(headOverfill) - PressureImpairmentBuildRate(0.0), 0.0) +
            Settings.CerebralPressureImpairmentNegativeGzRate *
            Math.Min(Math.Max(0.0, -gz), Settings.CerebralPressureImpairmentNegativeGzCap);
        var pressureImpairmentRecoveryRate = 1.0 / Math.Max(Settings.CerebralPressureImpairmentRecoveryTau, 1e-9);
        var pressureImpairmentRate = pressureImpairmentBuildRate + pressureImpairmentRecoveryRate;
        var pressureImpairmentTarget = pressureImpairmentBuildRate / pressureImpairmentRate;
        var pressureImpairmentAlpha = 1.0 - Math.Exp(-pressureImpairmentRate * dt);

        cerebralPressureImpairment += (pressureImpairmentBuildRate - cerebralPressureImpairment * pressureImpairmentRecoveryRate) * dt;
        cerebralPressureImpairment = Clamp(cerebralPressureImpairment, 0.0, 1.0);

        // O2 delivery depends on head blood volume relative to resting
        var perfusionRatio = bloodHead / Settings.RestingBloodHead;
        var perfusionRatioClamped = Clamp(perfusionRatio, 0.0, 1.0);

        // Perfusion shaping
        var s = Settings.O2PerfusionCurveStrength;
        var pivot = Settings.O2PerfusionCurvePivot;
        var shapedPerfusion = perfusionRatioClamped - s * perfusionRatioClamped * (1.0 - perfusionRatioClamped) * (perfusionRatioClamped - pivot);
        shapedPerfusion = Clamp(shapedPerfusion, 0.0, 1.0);

        // Cardiovascular fatigue: hrFatigue (0..1) accumulates with time × HR elevation,
        // decays slowly at rest. It is independent of current HR so it always wins eventually.
        var hrElevation = Math.Max(0.0, heartRateMultiplier - 1.0);
        var hrFatigueElevation = Math.Max(0.0, hrElevation - Settings.CardioFatigueHrElevationThreshold);
        var hrFatigueBuildRate = Settings.CardioFatigueBuildRate * hrFatigueElevation;
        hrFatigue += hrFatigueElevation > 0.0 ? hrFatigueBuildRate * dt : -(hrFatigue / Settings.CardioFatigueRecoveryTau) * dt;
        hrFatigue = Clamp(hrFatigue, 0.0, 1.0);

        // fatigueHeartRateFloor is a fixed setting: the resting HR offset the cardiovascular
        // system is stuck at once fully fatigued (independent of feedback).
        // Baroreceptor target from current perfusion deficit.
        double baroTarget;

        double hrTau;
        if (bloodHead > Settings.RestingBloodHead)
        {
            var bradycardia = Clamp(GetHeadBloodOverfill(bloodHead) / Settings.NegativeGHeartStopOverfill, 0.0, 1.0);
            baroTarget = 1.0 - bradycardia;
            hrTau = Settings.BaroreceptorTimeConstantNegativeMin + (Settings.BaroreceptorTimeConstantNegativeMax - Settings.BaroreceptorTimeConstantNegativeMin) * baroTarget;
        }
        else
        {
            baroTarget = 1.0 + Settings.BaroreceptorGain * (1.0 - perfusionRatioClamped);
            hrTau = Settings.BaroreceptorTimeConstantPositive;
        }

        baroTarget = Clamp(baroTarget, 0.0, Settings.MaxHeartRateMultiplier);

        // hrFatigue suppresses baroreceptor response: at hrFatigue=1 the target is pinned to the floor.
        var targetHR = baroTarget + hrFatigue * (fatigueHeartRateFloor - baroTarget);
        heartRateMultiplier = StepTowardsLinear(heartRateMultiplier, targetHR, hrTau, dt);

        // convert perfusion -> effective O2 delivery (non-linear + mild sustained hypoperfusion penalty)
        var effectiveDelivery = Math.Pow(shapedPerfusion, Settings.BrainO2PerfusionExponent);

        var threshold = Clamp(Settings.BrainO2HypoperfusionThreshold, 0.01, 1.0);
        var hypoperfusion = Math.Max(0.0, threshold - shapedPerfusion) / threshold;
        var hypoperfusionPenalty = Settings.BrainO2HypoperfusionPenaltyStrength * hypoperfusion * hypoperfusion;

        effectiveDelivery = Clamp(effectiveDelivery - hypoperfusionPenalty, 0.0, 1.0) * heartRateMultiplier * arterialOxygenation;

        // Respiratory fatigue (chest muscles for breathing)
        // Gx accelerates respiratory fatigue (thoracic compression makes breathing harder)
        var respiratoryEffort = Math.Max(0.0, gxMagnitude - Settings.GxRespiratoryFatigueThreshold) * 0.5; // Breathing effort increases with Gx chest compression
        var respiratoryFatigueMultiplier = 1.0 + (Settings.GxRespiratoryFatigueAccelerationFactor - 1.0) * gxMagnitude;

        var respiratoryFatigueBuildRate = Settings.RespiratoryFatigueBuildRate * respiratoryEffort * respiratoryFatigueMultiplier;
        respiratoryFatigue += respiratoryFatigueBuildRate * dt;

        // Recover respiratory fatigue (higher recovery rate when Gx is low)
        var respiratoryRecoveryRate = respiratoryFatigue / Settings.RespiratoryFatigueRecoveryTau;
        respiratoryFatigue -= respiratoryRecoveryRate * dt;
        respiratoryFatigue = Clamp(respiratoryFatigue, 0.0, 1.0);

        // Respiratory fatigue affects heart rate (breathing rate limits)
        var respiratoryHrMultiplier = 1.0 - respiratoryFatigue * 0.2; // Breathing rate limits HR
        heartRateMultiplier *= Math.Max(respiratoryHrMultiplier, Settings.RespiratoryFatigueHrFloor);

        // Gy lung compression and oxygen exchange reduction
        var compressionTarget = gyMagnitude > Settings.GyLungCompressionThreshold
            ? Math.Pow((gyMagnitude - Settings.GyLungCompressionThreshold) / 3.0, Settings.GyToleranceNonlinearity)
            : 0.0;
        compressionTarget = Clamp(compressionTarget, 0.0, 1.0);

        var compressionTau = gyMagnitude > Settings.GyLungCompressionThreshold
            ? Settings.GyLungCompressionTau
            : Settings.GyLungCompressionRecoveryTau;
        lungCompressionLevel = StepTowardsLinear(lungCompressionLevel, compressionTarget, compressionTau, dt);
        lungCompressionLevel = Clamp(lungCompressionLevel, 0.0, 1.0);

        // Oxygen exchange reduction affects lung oxygenation
        oxygenExchangeReduction = lungCompressionLevel * Settings.GyLungCompressionSeverity;

        // Lung oxygenation in core compartment
        // Lungs refresh blood O2 toward resting level, reduced by Gx (thoracic compression) and respiratory fatigue
        var gxLungImpairment = 0.0;
        if (gxMagnitude > Settings.GxLungOxygenationImpairmentThreshold)
        {
            gxLungImpairment = Settings.GxLungOxygenationImpairmentSeverity *
                Math.Pow((gxMagnitude - Settings.GxLungOxygenationImpairmentThreshold) / 4.0, 2.0);
        }
        gxLungImpairment = Clamp(gxLungImpairment, 0.0, 1.0);

        // Gx respiratory hypoxia: blood stays level with the brain so perfusion is unaffected, but
        // under high sustained Gx the chest wall becomes too heavy to lift and blood pools in the
        // dependent lung while air stays trapped - a ventilation-perfusion mismatch where blood
        // circulates without picking up oxygen. Ventilation failure saturates around ~15Gx
        // (GxLungOxygenationImpairmentFullGx), and arterial oxygenation then decays on the slow
        // timescale of the body's O2 reserves (~1-2 min to GLoC at >=15Gx) rather than the fast
        // perfusion dynamics used for Gz. Fighter-jet Gx (~1-1.5G) stays below the impairment
        // threshold and is tolerable indefinitely.
        var gxVentilationFailureRange = Math.Max(
            Settings.GxLungOxygenationImpairmentFullGx - Settings.GxLungOxygenationImpairmentThreshold, 1e-9);
        var gxVentilationFailure = 0.0;
        if (gxMagnitude > Settings.GxLungOxygenationImpairmentThreshold)
        {
            gxVentilationFailure = Settings.GxLungOxygenationImpairmentSeverity *
                Math.Pow((gxMagnitude - Settings.GxLungOxygenationImpairmentThreshold) / gxVentilationFailureRange, 3.0);
        }
        gxVentilationFailure = Clamp(gxVentilationFailure, 0.0, 1.0);

        var arterialOxygenationTarget = 1.0 - gxVentilationFailure;
        var arterialOxygenationTau = arterialOxygenationTarget < arterialOxygenation
            ? Settings.GxHypoxiaDepletionTau
            : Settings.GxHypoxiaRecoveryTau;
        arterialOxygenation = StepTowardsLinear(arterialOxygenation, arterialOxygenationTarget, arterialOxygenationTau, dt);
        arterialOxygenation = Clamp(arterialOxygenation, 0.0, 1.0);

        var lungEffectiveness = 1.0 - oxygenExchangeReduction - gxLungImpairment - (respiratoryFatigue * 0.5);
        lungEffectiveness = Clamp(lungEffectiveness, 0.1, 1.0);
        var targetCoreO2 = Settings.CoreBloodO2Resting * lungEffectiveness;
        bloodO2Core = StepTowardsLinear(bloodO2Core, targetCoreO2, Settings.LungOxygenationRate, dt);
        bloodO2Core = Clamp(bloodO2Core, 0.0, 1.0);

        // Oxygen consumption in each compartment
        bloodO2Head -= Settings.OxygenConsumptionRateHead * dt;
        bloodO2Core -= Settings.OxygenConsumptionRateCore * dt;
        bloodO2Lower -= Settings.OxygenConsumptionRateLower * dt;

        // Oxygen transport between compartments via blood flow (heart-rate-dependent)
        // Effective transport rate scales with heart rate and with the blood actually reaching
        // each compartment: when +Gz drains the head or -Gz overfills it, flow scales accordingly.
        var effectiveTransportRate = Settings.OxygenTransportBaseRate *
            (1.0 + Settings.OxygenTransportHeartRateSensitivity * (heartRateMultiplier - 1.0));
        var headPerfusionRatio = bloodHead / Settings.RestingBloodHead;
        var lowerPerfusionRatio = bloodLower / Settings.RestingBloodLower;

        // Core → Head (arterial flow)
        var o2FlowCoreToHead = (bloodO2Core - bloodO2Head) * effectiveTransportRate * headPerfusionRatio * dt;
        bloodO2Core -= o2FlowCoreToHead * (bloodHead / (bloodHead + bloodCore + bloodLower));
        bloodO2Head += o2FlowCoreToHead;

        // Core → Lower (arterial flow)
        var o2FlowCoreToLower = (bloodO2Core - bloodO2Lower) * effectiveTransportRate * lowerPerfusionRatio * dt;
        bloodO2Core -= o2FlowCoreToLower * (bloodLower / (bloodHead + bloodCore + bloodLower));
        bloodO2Lower += o2FlowCoreToLower;

        // Head → Core (venous return)
        var o2FlowHeadToCore = (bloodO2Head - bloodO2Core) * effectiveTransportRate * dt * 0.8 * headPerfusionRatio; // Venous return slower
        bloodO2Head -= o2FlowHeadToCore;
        bloodO2Core += o2FlowHeadToCore * (bloodHead / (bloodHead + bloodCore + bloodLower));

        // Lower → Core (venous return)
        var o2FlowLowerToCore = (bloodO2Lower - bloodO2Core) * effectiveTransportRate * dt * 0.8 * lowerPerfusionRatio;
        bloodO2Lower -= o2FlowLowerToCore;
        bloodO2Core += o2FlowLowerToCore * (bloodLower / (bloodHead + bloodCore + bloodLower));

        // Clamp O2 values
        bloodO2Head = Clamp(bloodO2Head, 0.0, 1.0);
        bloodO2Core = Clamp(bloodO2Core, 0.0, 1.0);
        bloodO2Lower = Clamp(bloodO2Lower, 0.0, 1.0);

        // For consciousness, use head O2 (brain oxygen)
        // Target O2 is bounded by floor, then approached with time constants
        var targetBrainO2 = Settings.BrainO2Floor + (1.0 - Settings.BrainO2Floor) * effectiveDelivery;

        // Severity-based depletion tau: high perfusion loss => faster depletion
        var severity = 1.0 - effectiveDelivery;
        var depletionTau = Settings.BrainO2DepletionTauMild + (Settings.BrainO2DepletionTauSevere - Settings.BrainO2DepletionTauMild) * severity;

        var o2Tau = targetBrainO2 < bloodO2Head ? depletionTau : Settings.BrainO2RecoveryTau;

        bloodO2Head = StepTowardsLinear(bloodO2Head, targetBrainO2, o2Tau, dt);
        bloodO2Head = Clamp(bloodO2Head, Settings.BrainO2Floor, 1.0);

        // Map brain O2 to consciousness
        var o2Normalized = Clamp(
            (BrainO2 - Settings.BrainO2Blackout) / (Settings.BrainO2Full - Settings.BrainO2Blackout),
            0.0, 1.0);

        var perfRatio = Clamp(bloodHead / Settings.RestingBloodHead, 0.0, 1.0);
        perfusionLevel = perfRatio;

        // Use soft minimum for consciousness mapping (not blackout threshold)
        var perfNorm = Clamp(
            (perfRatio - Settings.ConsciousnessPerfusionSoftMinRatio) /
            (1.0 - Settings.ConsciousnessPerfusionSoftMinRatio),
            0.0, 1.0);

        // "Weakest-link" blend: either low O2 or low perfusion can drive LOC
        var o2Term = Math.Pow(o2Normalized, Settings.ConsciousnessO2Exponent);
        var perfTerm = Math.Pow(SmoothStep(perfNorm), Settings.ConsciousnessPerfusionExponent);

        // Geometric blend: both channels matter strongly, avoids high flat plateau
        var targetConsciousness = o2Term * perfTerm;

        // Temporary cerebral pressure impairment acts as an independent weakest-link reserve.
        // A small deadband ignores negligible impairment (e.g. the tiny residual overfill at 0G),
        // then rescales so sustained negative-G impairment still reaches full effect.
        var effectivePressureImpairment = Clamp(
            (cerebralPressureImpairment - Settings.CerebralPressureImpairmentDeadband) /
            Math.Max(1.0 - Settings.CerebralPressureImpairmentDeadband, 1e-9),
            0.0, 1.0);
        var pressureConsciousnessReserve = Math.Pow(Clamp(1.0 - effectivePressureImpairment, 0.0, 1.0), Settings.CerebralPressureConsciousnessExponent);

        // sustained hypoxia/hypoperfusion bias (prevents 5G plateau like 0.09)
        var combinedDeficit = 1.0 - (0.5 * o2Normalized + 0.5 * perfNorm);
        targetConsciousness = Math.Max(0.0, targetConsciousness - Settings.ConsciousnessDeficitBias * combinedDeficit * combinedDeficit);

        // hard cap when perfusion is critically low
        if (perfNorm < 0.25) targetConsciousness = Math.Min(targetConsciousness, perfNorm * 0.75);

        targetConsciousness = Math.Min(targetConsciousness, pressureConsciousnessReserve);

        // Dynamic loss tau (non-linear so mid-G loses slower)
        var lossSeverity = Math.Pow(1.0 - targetConsciousness, Settings.ConsciousnessLossSeverityExponent);

        var baseLossTau = Settings.ConsciousnessLossTauMax + (Settings.ConsciousnessLossTauMin - Settings.ConsciousnessLossTauMax) * lossSeverity;

        // Critical collapse accelerator (mostly affects extreme +G)
        var criticalPerf = 1.0 - Clamp(perfNorm / Settings.ConsciousnessCriticalPerfusionNorm, 0.0, 1.0);

        var criticalO2 = 1.0 - Clamp(o2Normalized / Settings.ConsciousnessCriticalO2Norm, 0.0, 1.0);

        var criticalPressure = Clamp(
            (effectivePressureImpairment - Settings.ConsciousnessCriticalPressureNorm) /
            Math.Max(1.0 - Settings.ConsciousnessCriticalPressureNorm, 1e-9),
            0.0, 1.0);
        var critical = Math.Max(Math.Max(criticalPerf, criticalO2), criticalPressure);
        var criticalTauMultiplier = 1.0 - (1.0 - Settings.ConsciousnessCriticalTauMultiplierMin) * SmoothStep(critical);

        var lossTau = baseLossTau * criticalTauMultiplier;
        var tau = targetConsciousness < consciousnessLevel ? lossTau : Settings.ConsciousnessRecoveryTau;

        consciousnessLevel = StepTowardsLinear(consciousnessLevel, targetConsciousness, tau, dt);
        consciousnessLevel = Clamp(consciousnessLevel, 0.0, 1.0);

        if (isUnconscious)
        {
            if (consciousnessLevel > Settings.ConsciousnessRecoveryThreshold) isUnconscious = false;
        }
        else if (consciousnessLevel <= Settings.ConsciousnessLossThreshold)
        {
            isUnconscious = true;
        }

        var consciousnessVisualRange = Math.Max(
            Settings.ConsciousnessRecoveryThreshold - Settings.ConsciousnessLossThreshold,
            1e-9);
        var visualLoCMaximum = Math.Pow(Clamp(
            (Settings.ConsciousnessRecoveryThreshold - consciousnessLevel) / consciousnessVisualRange,
            0.0,
            1.0), Settings.VisualLoCConsciousnessExponent);
        if (isUnconscious)
        {
            visualLoCLevel = 1.0;
        }
        else
        {
            var visualLoCRate = visualLoCMaximum > visualLoCLevel
                ? Settings.VisualLoCIncreaseRate
                : Settings.VisualLoCDecreaseRate;
            var visualLoCMaximumDelta = Math.Max(visualLoCRate, 0.0) * dt;
            visualLoCLevel += Clamp(
                visualLoCMaximum - visualLoCLevel,
                -visualLoCMaximumDelta,
                visualLoCMaximumDelta);
            visualLoCLevel = Clamp(visualLoCLevel, 0.0, 1.0);
        }

        var visualPerf = Clamp((perfRatio - 0.45) / 0.55, 0.0, 1.0);
        var visualO2 = Clamp((o2Normalized - 0.15) / 0.85, 0.0, 1.0);
        var visualReserve = 0.7 * visualPerf + 0.3 * visualO2;
        var visualDeficit = 1.0 - visualReserve;
        var physiologicalVisualTarget = bloodHead < Settings.RestingBloodHead
            ? Math.Pow(Clamp((visualDeficit - 0.18) / 0.82, 0.0, 1.0), 2.2)
            : 0.0;

        #region Tunnel Vision
        var tunnelTau = physiologicalVisualTarget > visualTunnelVisionLevel
            ? Settings.VisualTunnelVisionInTau
            : Settings.VisualTunnelVisionOutTau;
        visualTunnelVisionLevel = StepTowardsLinear(
            visualTunnelVisionLevel,
            physiologicalVisualTarget,
            tunnelTau,
            dt);
        visualTunnelVisionLevel = Clamp(visualTunnelVisionLevel, 0.0, 1.0);
        #endregion

        #region Redout
        var redoutRange = Math.Max(
            Settings.VisualRedoutFullHeadBloodOverfill - Settings.VisualRedoutOnsetHeadBloodOverfill,
            1e-9);
        var redoutTarget = SmoothStep(Clamp(
            (headOverfill - Settings.VisualRedoutOnsetHeadBloodOverfill) / redoutRange,
            0.0,
            1.0));
        var redoutTau = redoutTarget > visualRedoutLevel
            ? Settings.VisualRedoutInTau
            : Settings.VisualRedoutOutTau;
        visualRedoutLevel = StepTowardsLinear(visualRedoutLevel, redoutTarget, redoutTau, dt);
        visualRedoutLevel = Clamp(visualRedoutLevel, 0.0, 1.0);
        #endregion

        #region Grayscale
        var grayscaleTau = physiologicalVisualTarget > visualGrayscaleLevel
            ? Settings.VisualGrayscaleInTau
            : Settings.VisualGrayscaleOutTau;
        visualGrayscaleLevel = StepTowardsLinear(
            visualGrayscaleLevel,
            physiologicalVisualTarget,
            grayscaleTau,
            dt);
        visualGrayscaleLevel = Clamp(visualGrayscaleLevel, 0.0, 1.0);
        #endregion

        #region Blur

        var earlyBlurTarget = 0.23 * (1.0 - Math.Exp(-8.0 * physiologicalVisualTarget));
        var earlyBlurInfluence = 1.0 - SmoothStep(Clamp((visualGrayscaleLevel - 0.2) / 0.3, 0.0, 1.0));
        var earlyBlurBoost = Math.Max(earlyBlurTarget - visualGrayscaleLevel * 0.5, 0.0) * earlyBlurInfluence;
        visualBlurLevel = Clamp(visualGrayscaleLevel + earlyBlurBoost, 0.0, 1.0);

        #endregion

        #region Film Grain

        visualFilmGrainLevel = Clamp(Math.Pow(VisualTunnelVisionLevel, 1.5), 0.0, 1.0);

        #endregion

        // Pain accumulation from Gy (and Gx for future)
        var gyPainTarget = Settings.GyPainBaseFactor * Math.Pow(gyMagnitude, Settings.GyPainNonlinearity);
        var gxPainTarget = Settings.GxPainFactor * gxMagnitude; // Future: may increase
        var totalPainTarget = gyPainTarget + gxPainTarget;
        totalPainTarget = Clamp(totalPainTarget, 0.0, 1.0);

        var painTau = totalPainTarget > painLevel
            ? Settings.GyPainAccumulationTau
            : Settings.GyPainRecoveryTau;
        painLevel = StepTowardsLinear(painLevel, totalPainTarget, painTau, dt);
        painLevel = Clamp(painLevel, 0.0, 1.0);

        // Future: pain may reduce consciousness (not implemented in v1)

        // Gy neck side fatigue (damage/death with ceiling)
        // Low Gy can't reach death level even with infinite time (ceiling)
        var fatigueBuildRate = gyMagnitude > Settings.GyNeckFatigueThreshold
            ? Settings.GyNeckFatigueBuildRate *
              Math.Pow((gyMagnitude - Settings.GyNeckFatigueThreshold) / 3.0, Settings.GyNeckFatigueNonlinearity)
            : 0.0;
        gyNeckFatigue += fatigueBuildRate * dt;

        // Apply ceiling - low Gy can't reach death level
        var maxFatigueAtCurrentGy = Settings.GyNeckFatigueCeiling +
            (1.0 - Settings.GyNeckFatigueCeiling) *
            Math.Pow((gyMagnitude - Settings.GyNeckFatigueThreshold) / 8.0, Settings.GyNeckFatigueNonlinearity);
        maxFatigueAtCurrentGy = Clamp(maxFatigueAtCurrentGy, Settings.GyNeckFatigueCeiling, 1.0);
        gyNeckFatigue = Clamp(gyNeckFatigue, 0.0, maxFatigueAtCurrentGy);

        // Recover from neck fatigue
        gyNeckFatigue -= gyNeckFatigue / Settings.GyNeckFatigueRecoveryTau * dt;
        gyNeckFatigue = Clamp(gyNeckFatigue, 0.0, 1.0);

        // Check for death
        if (gyNeckFatigue >= Settings.GyNeckFatigueDeathLevel)
        {
            // Accumulate time at death level
            gyNeckFatigueDeathAccumulatedTime += dt / Settings.GyNeckFatigueDeathDelay;

            if (gyNeckFatigueDeathAccumulatedTime >= 1.0)
            {
                isDead = true;
                consciousnessLevel = 0.0;
                isUnconscious = true;
                Logger.Log("Death from Gy neck side fatigue", logicInstance, Logger.LogLevel.Error);
            }
        }
        else
        {
            // Reset death accumulation if recovering
            gyNeckFatigueDeathAccumulatedTime = 0.0;
        }

        // Calculate sudden G-LOC risk from multi-axis combination
        var gxSuddenRisk = gxMagnitude > Settings.GxSuddenLoCThreshold
            ? Settings.GxSuddenLoCSeverity * (gxMagnitude - Settings.GxSuddenLoCThreshold) / 2.0
            : 0.0;
        var gySuddenRisk = gyMagnitude > Settings.GySuddenLoCThreshold
            ? Settings.GySuddenLoCSeverity * (gyMagnitude - Settings.GySuddenLoCThreshold) / 1.0
            : 0.0;

        var totalSuddenRisk = gxSuddenRisk + gySuddenRisk;
        totalSuddenRisk = Clamp(totalSuddenRisk, 0.0, 1.0);

        // Accumulate sudden G-LOC risk (automatic recovery when below threshold)
        if (totalSuddenRisk > Settings.MultiAxisSuddenLoCThreshold)
        {
            suddenLoCAccumulator += (totalSuddenRisk - Settings.MultiAxisSuddenLoCThreshold) * dt;
        }
        suddenLoCAccumulator -= suddenLoCAccumulator / Settings.SuddenLoCRecoveryTau * dt;
        suddenLoCAccumulator = Clamp(suddenLoCAccumulator, 0.0, 1.0);

        // Trigger sudden G-LOC
        if (suddenLoCAccumulator > 0.8 && !inSuddenLoC && !isDead)
        {
            inSuddenLoC = true;
            consciousnessLevel = Math.Max(0.0, consciousnessLevel - Settings.SuddenLoCConsciousnessDrop);
            Logger.Log("Sudden G-LOC triggered by multi-axis G-forces", logicInstance, Logger.LogLevel.Warning);
        }

        // Recover from sudden G-LOC state
        if (inSuddenLoC && suddenLoCAccumulator < 0.3)
        {
            inSuddenLoC = false;
            Logger.Log("Recovering from sudden G-LOC", logicInstance, Logger.LogLevel.Info);
        }
    }

    public PhysiologicalModel(GEffectsLogicInstance logicInstance)
    {
        this.logicInstance = logicInstance;
        Reset();
    }
}
