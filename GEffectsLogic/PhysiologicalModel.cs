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
///     For the first version only Gz is fully modeled. Gx/Gy are stubbed for expansion.
/// </summary>
public class PhysiologicalModel
{
    // --- Compartment blood volumes (fraction of total, sum = 1.0) ---
    protected double bloodHead = LogicSettings.RestingBloodHead;
    protected double bloodCore = LogicSettings.RestingBloodCore;
    protected double bloodLower = LogicSettings.RestingBloodLower;
    protected double brainO2 = 1.0;
    protected double heartRateMultiplier = 1.0; // Baroreceptor reflex: heart rate multiplier (1.0 = resting)

    protected double
        strainingLevel; // Straining effort (0..1): pilot anti-G straining maneuver including g-suit inflation

    protected double strainingFatigue; // Accumulated AGSM fatigue (0..1): degrades the human straining component
    protected double gSuitFatigue; // Accumulated g-suit fatigue (0..1): degrades mechanical suit compression

    protected double
        hrFatigue; // Cardiovascular fatigue (0..1): suppresses baroreceptor HR toward fatigueHeartRateFloor

    protected double fatigueHeartRateFloor = LogicSettings.CardioFatigueMaxHrFloor;
    protected double perfusionLevel;

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

    /// <summary>Fraction of total blood in the head compartment.</summary>
    public double BloodHead => bloodHead;

    public double BloodHeadOverfill => GetHeadBloodOverfill(BloodHead);

    /// <summary>Fraction of total blood in the core compartment.</summary>
    public double BloodCore => bloodCore;

    /// <summary>Fraction of total blood in the lower body compartment.</summary>
    public double BloodLower => bloodLower;

    /// <summary>Brain oxygen saturation (0 = no oxygen, 1 = fully saturated).</summary>
    public double BrainO2 => brainO2;

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

    #endregion

    /// <summary>Reset all state to resting equilibrium.</summary>
    public virtual void Reset()
    {
        bloodHead = LogicSettings.RestingBloodHead;
        bloodCore = LogicSettings.RestingBloodCore;
        bloodLower = LogicSettings.RestingBloodLower;
        brainO2 = 1.0;
        heartRateMultiplier = 1.0;
        strainingLevel = 0.0;
        strainingFatigue = 0.0;
        gSuitFatigue = 0.0;
        hrFatigue = 0.0;
        fatigueHeartRateFloor = LogicSettings.CardioFatigueMaxHrFloor;
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

    private static double GetHeadBloodOverfill(double headBlood) =>
        Math.Max((headBlood - LogicSettings.RestingBloodHead) / LogicSettings.RestingBloodHead, 0.0);

    private static double PressureImpairmentBuildRate(double overfill) =>
        LogicSettings.CerebralPressureImpairmentMaxBuildRate /
        (1.0 + Math.Exp(-LogicSettings.CerebralPressureImpairmentExponent *
                        (overfill - LogicSettings.CerebralPressureImpairmentMidOverfill)));

    private static double StepHeadBloodImplicit(
    double current,
    double hydrostaticRate,
    double returnRate,
    double dt)
    {
        var resting = LogicSettings.RestingBloodHead;
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
            var pressureReturnRate = LogicSettings.HeadPressureReturnRate *
                                     (Math.Exp(LogicSettings.HeadPressureReturnExponent * overfill) - 1.0);
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
    /// <param name="gz">Current Gz (positive = headward-to-footward).</param>
    /// <param name="gx">Current Gx (unused in v1, reserved).</param>
    /// <param name="gy">Current Gy (unused in v1, reserved).</param>
    public virtual void Update(double dt, double gz, double gx = 0.0, double gy = 0.0)
    {
        // TODO:
        // Shorter GLoC time at high Gz-, longer GLoC time at moderate (3) Gz-, less consciousness loss at low Gz- (0.5-2 Gz-)

        // Keep dt untouched here (guarded by LogicInstance).

        // Positive Gz pushes blood from head → lower body
        // Negative Gz pushes blood from lower body → head
        // The shift rate is proportional to Gz magnitude beyond the 1G baseline. Subtracting the
        // 1G-equivalent makes normal upright 1G the neutral point (head blood ~ resting), so level
        // flight settles at near-full consciousness while high +Gz still pools strongly.
        var gzNetScaled = Math.Sign(gz) * Math.Pow(Math.Abs(gz), LogicSettings.HydrostaticShiftExponent) - 1.0;

        // Drive straining level from +Gz with first-order lag
        var targetStraining = 0.0;
        if (gz > LogicSettings.StrainingStartGz) targetStraining = (gz - LogicSettings.StrainingStartGz) / (LogicSettings.StrainingFullGz - LogicSettings.StrainingStartGz);
        targetStraining = Clamp(targetStraining, 0.0, 1.0);
        strainingLevel = StepTowardsLinear(strainingLevel, targetStraining, LogicSettings.StrainingTau, dt);

        // Fatigue: straining fatigue fills while strainingLevel is high, drains slowly at rest
        var strainingFatigueBuildRate = LogicSettings.StrainingFatigueBuildRate * strainingLevel * strainingLevel;
        var strainingFatigueDecayRate = 1.0 / LogicSettings.StrainingFatigueRecoveryTau;
        strainingFatigue += strainingLevel > 0.01 ? strainingFatigueBuildRate * dt : -strainingFatigueDecayRate * strainingFatigue * dt;
        strainingFatigue = Clamp(strainingFatigue, 0.0, 1.0);

        // G-suit fatigue builds more slowly (mechanical, outlasts the pilot's AGSM), also drains slowly
        var gSuitFatigueBuildRate = LogicSettings.GSuitFatigueBuildRate * strainingLevel;
        var gSuitFatigueDecayRate = 1.0 / LogicSettings.GSuitFatigueRecoveryTau;
        gSuitFatigue += strainingLevel > 0.01 ? gSuitFatigueBuildRate * dt : -gSuitFatigueDecayRate * gSuitFatigue * dt;
        gSuitFatigue = Clamp(gSuitFatigue, 0.0, 1.0);

        // Effective straining: human AGSM component fully degrades with strainingFatigue
        var effectiveStraining = strainingLevel * (1.0 - strainingFatigue);

        // Effective g-suit: mechanical suit retains a passive fraction, only the active compression degrades
        var gSuitActiveFraction = 1.0 - LogicSettings.GSuitPassiveFraction;
        var effectiveGSuit = LogicSettings.GSuitEffectiveness * (LogicSettings.GSuitPassiveFraction + gSuitActiveFraction * (1.0 - gSuitFatigue));

        // Suit effect only for +Gz loading, coupled to effective straining
        var suitActivation = Clamp(effectiveGSuit * effectiveStraining, 0.0, 1.0);
        var suit = gz > 0.0 ? suitActivation : 0.0;

        // Mild global scaling + targeted redistribution
        var effectiveGzShift = gzNetScaled * (1.0 - LogicSettings.GSuitGlobalShiftReductionMax * suit);
        var coreLowerFractionEffective = Clamp(LogicSettings.CoreLowerShiftFraction * (1.0 - LogicSettings.GSuitCoreLowerReductionMax * suit), 0.05, 0.95);

        Logger.Log($"effectiveGzShift: {effectiveGzShift}, coreLowerFractionEffective: {coreLowerFractionEffective}", logicInstance);

        // Blood flow rate between compartments
        var shiftRate = LogicSettings.HydrostaticShiftRate * effectiveGzShift;
        var shiftHeadRate = -shiftRate;
        var shiftCoreRate = shiftRate;
        var shiftLowerRate = shiftRate * coreLowerFractionEffective;

        // Passive return (boost lower pool return under suit)
        var returnRate = LogicSettings.PassiveReturnRate * heartRateMultiplier;
        var lowerReturnRate = returnRate * (1.0 + LogicSettings.GSuitLowerReturnBoostMax * suit);

        bloodHead = StepHeadBloodImplicit(
            bloodHead,
            shiftHeadRate,
            returnRate,
            dt);

        bloodCore = (bloodCore + (shiftCoreRate + returnRate * LogicSettings.RestingBloodCore) * dt) / (1.0 + returnRate * dt);
        bloodLower = (bloodLower + (shiftLowerRate + lowerReturnRate * LogicSettings.RestingBloodLower) * dt) / (1.0 + lowerReturnRate * dt);

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
        if (bloodHead < LogicSettings.MinHeadBloodFraction)
        {
            bloodHead = LogicSettings.MinHeadBloodFraction;
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
        // subtracted so no impairment accrues (and recovery is complete) at rest.
        var pressureImpairmentBuildRate =
            Math.Max(PressureImpairmentBuildRate(headOverfill) - PressureImpairmentBuildRate(0.0), 0.0);
        var pressureImpairmentRecoveryRate = 1.0 / Math.Max(LogicSettings.CerebralPressureImpairmentRecoveryTau, 1e-9);
        var pressureImpairmentRate = pressureImpairmentBuildRate + pressureImpairmentRecoveryRate;
        var pressureImpairmentTarget = pressureImpairmentBuildRate / pressureImpairmentRate;
        var pressureImpairmentAlpha = 1.0 - Math.Exp(-pressureImpairmentRate * dt);

        cerebralPressureImpairment += (pressureImpairmentBuildRate - cerebralPressureImpairment * pressureImpairmentRecoveryRate) * dt;
        cerebralPressureImpairment = Clamp(cerebralPressureImpairment, 0.0, 1.0);

        // O2 delivery depends on head blood volume relative to resting
        var perfusionRatio = bloodHead / LogicSettings.RestingBloodHead;
        var perfusionRatioClamped = Clamp(perfusionRatio, 0.0, 1.0);

        // Perfusion shaping
        var s = LogicSettings.O2PerfusionCurveStrength;
        var pivot = LogicSettings.O2PerfusionCurvePivot;
        var shapedPerfusion = perfusionRatioClamped - s * perfusionRatioClamped * (1.0 - perfusionRatioClamped) * (perfusionRatioClamped - pivot);
        shapedPerfusion = Clamp(shapedPerfusion, 0.0, 1.0);

        // Cardiovascular fatigue: hrFatigue (0..1) accumulates with time × HR elevation,
        // decays slowly at rest. It is independent of current HR so it always wins eventually.
        var hrElevation = Math.Max(0.0, heartRateMultiplier - 1.0);
        var hrFatigueBuildRate = LogicSettings.CardioFatigueBuildRate * hrElevation;
        hrFatigue += hrElevation > 0.05 ? hrFatigueBuildRate * dt : -(hrFatigue / LogicSettings.CardioFatigueRecoveryTau) * dt;
        hrFatigue = Clamp(hrFatigue, 0.0, 1.0);

        // fatigueHeartRateFloor is a fixed setting: the resting HR offset the cardiovascular
        // system is stuck at once fully fatigued (independent of feedback).
        // Baroreceptor target from current perfusion deficit.
        double baroTarget;

        double hrTau;
        if (bloodHead > LogicSettings.RestingBloodHead)
        {
            var bradycardia = Clamp(GetHeadBloodOverfill(bloodHead) / LogicSettings.NegativeGHeartStopOverfill, 0.0, 1.0);
            baroTarget = 1.0 - bradycardia;
            hrTau = LogicSettings.BaroreceptorTimeConstantNegativeMin + (LogicSettings.BaroreceptorTimeConstantNegativeMax - LogicSettings.BaroreceptorTimeConstantNegativeMin) * baroTarget;
        }
        else
        {
            baroTarget = 1.0 + LogicSettings.BaroreceptorGain * (1.0 - perfusionRatioClamped);
            hrTau = LogicSettings.BaroreceptorTimeConstantPositive;
        }

        baroTarget = Clamp(baroTarget, 0.0, LogicSettings.MaxHeartRateMultiplier);

        // hrFatigue suppresses baroreceptor response: at hrFatigue=1 the target is pinned to the floor.
        var targetHR = baroTarget + hrFatigue * (fatigueHeartRateFloor - baroTarget);
        heartRateMultiplier = StepTowardsLinear(heartRateMultiplier, targetHR, hrTau, dt);

        // convert perfusion -> effective O2 delivery (non-linear + mild sustained hypoperfusion penalty)
        var effectiveDelivery = Math.Pow(shapedPerfusion, LogicSettings.BrainO2PerfusionExponent);

        var threshold = Clamp(LogicSettings.BrainO2HypoperfusionThreshold, 0.01, 1.0);
        var hypoperfusion = Math.Max(0.0, threshold - shapedPerfusion) / threshold;
        var hypoperfusionPenalty = LogicSettings.BrainO2HypoperfusionPenaltyStrength * hypoperfusion * hypoperfusion;

        effectiveDelivery = Clamp(effectiveDelivery - hypoperfusionPenalty, 0.0, 1.0) * heartRateMultiplier;

        // Target O2 is bounded by floor, then approached with time constants
        var targetBrainO2 = LogicSettings.BrainO2Floor + (1.0 - LogicSettings.BrainO2Floor) * effectiveDelivery;

        // Severity-based depletion tau: high perfusion loss => faster depletion
        var severity = 1.0 - effectiveDelivery;
        var depletionTau = LogicSettings.BrainO2DepletionTauMild + (LogicSettings.BrainO2DepletionTauSevere - LogicSettings.BrainO2DepletionTauMild) * severity;

        var o2Tau = targetBrainO2 < brainO2 ? depletionTau : LogicSettings.BrainO2RecoveryTau;

        brainO2 = StepTowardsLinear(brainO2, targetBrainO2, o2Tau, dt);
        brainO2 = Clamp(brainO2, LogicSettings.BrainO2Floor, 1.0);

        // Map brain O2 to consciousness
        var o2Normalized = Clamp(
            (BrainO2 - LogicSettings.BrainO2Blackout) / (LogicSettings.BrainO2Full - LogicSettings.BrainO2Blackout),
            0.0, 1.0);

        var perfRatio = Clamp(bloodHead / LogicSettings.RestingBloodHead, 0.0, 1.0);
        perfusionLevel = perfRatio;

        // Use soft minimum for consciousness mapping (not blackout threshold)
        var perfNorm = Clamp(
            (perfRatio - LogicSettings.ConsciousnessPerfusionSoftMinRatio) /
            (1.0 - LogicSettings.ConsciousnessPerfusionSoftMinRatio),
            0.0, 1.0);

        // "Weakest-link" blend: either low O2 or low perfusion can drive LOC
        var o2Term = Math.Pow(o2Normalized, LogicSettings.ConsciousnessO2Exponent);
        var perfTerm = Math.Pow(perfNorm, LogicSettings.ConsciousnessPerfusionExponent);

        // Geometric blend: both channels matter strongly, avoids high flat plateau
        var targetConsciousness = o2Term * perfTerm;

        // Temporary cerebral pressure impairment acts as an independent weakest-link reserve.
        // A small deadband ignores negligible impairment (e.g. the tiny residual overfill at 0G),
        // then rescales so sustained negative-G impairment still reaches full effect.
        var effectivePressureImpairment = Clamp(
            (cerebralPressureImpairment - LogicSettings.CerebralPressureImpairmentDeadband) /
            Math.Max(1.0 - LogicSettings.CerebralPressureImpairmentDeadband, 1e-9),
            0.0, 1.0);
        var pressureConsciousnessReserve = Math.Pow(Clamp(1.0 - effectivePressureImpairment, 0.0, 1.0), LogicSettings.CerebralPressureConsciousnessExponent);

        // sustained hypoxia/hypoperfusion bias (prevents 5G plateau like 0.09)
        var combinedDeficit = 1.0 - (0.5 * o2Normalized + 0.5 * perfNorm);
        targetConsciousness = Math.Max(0.0, targetConsciousness - LogicSettings.ConsciousnessDeficitBias * combinedDeficit * combinedDeficit);

        // hard cap when perfusion is critically low
        if (perfNorm < 0.25) targetConsciousness = Math.Min(targetConsciousness, perfNorm * 0.75);

        targetConsciousness = Math.Min(targetConsciousness, pressureConsciousnessReserve);

        // Dynamic loss tau (non-linear so mid-G loses slower)
        var lossSeverity = Math.Pow(1.0 - targetConsciousness, LogicSettings.ConsciousnessLossSeverityExponent);

        var baseLossTau = LogicSettings.ConsciousnessLossTauMax + (LogicSettings.ConsciousnessLossTauMin - LogicSettings.ConsciousnessLossTauMax) * lossSeverity;

        // Critical collapse accelerator (mostly affects extreme +G)
        var criticalPerf = 1.0 - Clamp(perfNorm / LogicSettings.ConsciousnessCriticalPerfusionNorm, 0.0, 1.0);

        var criticalO2 = 1.0 - Clamp(o2Normalized / LogicSettings.ConsciousnessCriticalO2Norm, 0.0, 1.0);

        var criticalPressure = Clamp(
            (effectivePressureImpairment - LogicSettings.ConsciousnessCriticalPressureNorm) /
            Math.Max(1.0 - LogicSettings.ConsciousnessCriticalPressureNorm, 1e-9),
            0.0, 1.0);
        var critical = Math.Max(Math.Max(criticalPerf, criticalO2), criticalPressure);
        var criticalTauMultiplier = 1.0 - (1.0 - LogicSettings.ConsciousnessCriticalTauMultiplierMin) * SmoothStep(critical);

        var lossTau = baseLossTau * criticalTauMultiplier;
        var tau = targetConsciousness < consciousnessLevel ? lossTau : LogicSettings.ConsciousnessRecoveryTau;

        consciousnessLevel = StepTowardsLinear(consciousnessLevel, targetConsciousness, tau, dt);
        consciousnessLevel = Clamp(consciousnessLevel, 0.0, 1.0);

        if (isUnconscious)
        {
            if (consciousnessLevel > LogicSettings.ConsciousnessRecoveryThreshold) isUnconscious = false;
        }
        else if (consciousnessLevel <= LogicSettings.ConsciousnessLossThreshold)
        {
            isUnconscious = true;
        }

        var consciousnessVisualRange = Math.Max(
            LogicSettings.ConsciousnessRecoveryThreshold - LogicSettings.ConsciousnessLossThreshold,
            1e-9);
        var visualLoCMaximum = Math.Pow(Clamp(
            (LogicSettings.ConsciousnessRecoveryThreshold - consciousnessLevel) / consciousnessVisualRange,
            0.0,
            1.0), LogicSettings.VisualLoCConsciousnessExponent);
        if (isUnconscious)
        {
            visualLoCLevel = 1.0;
        }
        else
        {
            var visualLoCRate = visualLoCMaximum > visualLoCLevel
                ? LogicSettings.VisualLoCIncreaseRate
                : LogicSettings.VisualLoCDecreaseRate;
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
        var physiologicalVisualTarget = bloodHead < LogicSettings.RestingBloodHead
            ? Math.Pow(Clamp((visualDeficit - 0.18) / 0.82, 0.0, 1.0), 2.2)
            : 0.0;

        #region Tunnel Vision
        var tunnelTau = physiologicalVisualTarget > visualTunnelVisionLevel
            ? LogicSettings.VisualTunnelVisionInTau
            : LogicSettings.VisualTunnelVisionOutTau;
        visualTunnelVisionLevel = StepTowardsLinear(
            visualTunnelVisionLevel,
            physiologicalVisualTarget,
            tunnelTau,
            dt);
        visualTunnelVisionLevel = Clamp(visualTunnelVisionLevel, 0.0, 1.0);
        #endregion

        #region Redout
        var redoutRange = Math.Max(
            LogicSettings.VisualRedoutFullHeadBloodOverfill - LogicSettings.VisualRedoutOnsetHeadBloodOverfill,
            1e-9);
        var redoutTarget = SmoothStep(Clamp(
            (headOverfill - LogicSettings.VisualRedoutOnsetHeadBloodOverfill) / redoutRange,
            0.0,
            1.0));
        var redoutTau = redoutTarget > visualRedoutLevel
            ? LogicSettings.VisualRedoutInTau
            : LogicSettings.VisualRedoutOutTau;
        visualRedoutLevel = StepTowardsLinear(visualRedoutLevel, redoutTarget, redoutTau, dt);
        visualRedoutLevel = Clamp(visualRedoutLevel, 0.0, 1.0);
        #endregion

        #region Grayscale
        var grayscaleTau = physiologicalVisualTarget > visualGrayscaleLevel
            ? LogicSettings.VisualGrayscaleInTau
            : LogicSettings.VisualGrayscaleOutTau;
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
    }

    public PhysiologicalModel(GEffectsLogicInstance logicInstance)
    {
        this.logicInstance = logicInstance;
        Reset();
    }
}