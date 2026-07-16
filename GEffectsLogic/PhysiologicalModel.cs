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

    //protected double confusionLevel = 0.0;
    protected double greyScaleLevel;
    protected double tunnelVisionLevel;
    protected double blurLevel; // Rises early, then slowly approaches greyscale
    protected double filmgrainLevel; // 25% influence on filmgrain, 75% from vignette alpha
    protected bool primaryColor = true; // true = normal (blackout), false = inverted (redout)

    #region Public read-only state

    public readonly int UniqueID;

    /// <summary>Fraction of total blood in the head compartment.</summary>
    public double BloodHead => bloodHead;

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

    //public double ConfusionLevel { get { return confusionLevel; } set { confusionLevel = value; } }

    /// <summary>Current level of grey scale vision (0 = normal, 1 = fully grey).</summary>
    public double GreyScaleLevel => greyScaleLevel;

    /// <summary>Current level of tunnel vision (0 = none, 1 = blackout).</summary>
    public double TunnelVisionLevel => tunnelVisionLevel;
    
    /// <summary>Current level of filmgrain, based on TunnelVisionLevel (intended: 25% filmGrainLevel, 75% tunnelvision alpha)</summary>
    public double FilmGrainLevel => filmgrainLevel;
    
    /// <summary>Current blur percentage (currently recommended: 0.0 - 5.0 pixels gaussian blur)</summary>
    public double BlurLevel => blurLevel;

    /// <summary>True if primary color is normal (blackout), false if inverted (redout).</summary>
    public bool PrimaryColor => primaryColor;

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
        tunnelVisionLevel = 0.0;
        greyScaleLevel = 0.0;
        filmgrainLevel = 0.0;
        blurLevel = 0.0;
        primaryColor = true;
        consciousnessLevel = 1.0;
        perfusionLevel = 0.0;
    }

#if NET481
    public static double Clamp(double value, double min, double max)
    {
        return Math.Max(Math.Min(value, max), min);
    }
#else
    public static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
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

    /// <summary>
    ///     Advance the model by deltaTime seconds under the given G-force vector.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <param name="gz">Current Gz (positive = headward-to-footward).</param>
    /// <param name="gx">Current Gx (unused in v1, reserved).</param>
    /// <param name="gy">Current Gy (unused in v1, reserved).</param>
    public virtual void Update(double dt, double gz, double gx = 0.0, double gy = 0.0)
    {
        // Keep dt untouched here (guarded by LogicInstance).

        // Positive Gz pushes blood from head → lower body
        // Negative Gz pushes blood from lower body → head
        // The shift rate is proportional to Gz magnitude beyond the 1G baseline
        var gzNet = gz - 1.0;

        // Autoregulation dead-zone: moderate G is buffered more than extreme G
        var gzBeyondTolerance = Math.Sign(gzNet) *
                                Math.Max(0.0, Math.Abs(gzNet) - LogicSettings.CerebralAutoregulationGzTolerance);

        var gzNetScaled = Math.Sign(gzBeyondTolerance) *
                          Math.Pow(Math.Abs(gzBeyondTolerance), LogicSettings.HydrostaticShiftExponent);

        // Drive straining level from +Gz with first-order lag
        var targetStraining = 0.0;
        if (gz > LogicSettings.StrainingStartGz)
            targetStraining = (gz - LogicSettings.StrainingStartGz) /
                              (LogicSettings.StrainingFullGz - LogicSettings.StrainingStartGz);
        targetStraining = Clamp(targetStraining, 0.0, 1.0);
        strainingLevel = StepTowardsLinear(strainingLevel, targetStraining, LogicSettings.StrainingTau, dt);

        // Fatigue: straining fatigue fills while strainingLevel is high, drains slowly at rest
        var strainingFatigueBuildRate = LogicSettings.StrainingFatigueBuildRate * strainingLevel * strainingLevel;
        var strainingFatigueDecayRate = 1.0 / LogicSettings.StrainingFatigueRecoveryTau;
        strainingFatigue += strainingLevel > 0.01
            ? strainingFatigueBuildRate * dt
            : -strainingFatigueDecayRate * strainingFatigue * dt;
        strainingFatigue = Clamp(strainingFatigue, 0.0, 1.0);

        // G-suit fatigue builds more slowly (mechanical, outlasts the pilot's AGSM), also drains slowly
        var gSuitFatigueBuildRate = LogicSettings.GSuitFatigueBuildRate * strainingLevel;
        var gSuitFatigueDecayRate = 1.0 / LogicSettings.GSuitFatigueRecoveryTau;
        gSuitFatigue += strainingLevel > 0.01
            ? gSuitFatigueBuildRate * dt
            : -gSuitFatigueDecayRate * gSuitFatigue * dt;
        gSuitFatigue = Clamp(gSuitFatigue, 0.0, 1.0);

        // Effective straining: human AGSM component fully degrades with strainingFatigue
        var effectiveStraining = strainingLevel * (1.0 - strainingFatigue);

        // Effective g-suit: mechanical suit retains a passive fraction, only the active compression degrades
        var gSuitActiveFraction = 1.0 - LogicSettings.GSuitPassiveFraction;
        var effectiveGSuit = LogicSettings.GSuitEffectiveness *
                             (LogicSettings.GSuitPassiveFraction + gSuitActiveFraction * (1.0 - gSuitFatigue));

        // Suit effect only for +Gz loading, coupled to effective straining
        var suitActivation = Clamp(effectiveGSuit * effectiveStraining, 0.0, 1.0);
        var suit = gzNet > 0.0 ? suitActivation : 0.0;

        // Mild global scaling + targeted redistribution
        var effectiveGzShift = gzNetScaled * (1.0 - LogicSettings.GSuitGlobalShiftReductionMax * suit);

        var coreLowerFractionEffective = Clamp(
            LogicSettings.CoreLowerShiftFraction * (1.0 - LogicSettings.GSuitCoreLowerReductionMax * suit),
            0.05, 0.95);

        Logger.Log($"effectiveGzShift: {effectiveGzShift}, coreLowerFractionEffective: {coreLowerFractionEffective}",
            UniqueID);

        // Blood flow rate between compartments
        var shiftRate = LogicSettings.HydrostaticShiftRate * effectiveGzShift;
        var shiftHeadRate = -shiftRate;
        var shiftCoreRate = shiftRate;
        var shiftLowerRate = shiftRate * coreLowerFractionEffective;

        // Passive return (boost lower pool return under suit)
        var returnRate = LogicSettings.PassiveReturnRate * heartRateMultiplier;
        var lowerReturnRate = returnRate * (1.0 + LogicSettings.GSuitLowerReturnBoostMax * suit);

        bloodHead = (bloodHead + (shiftHeadRate + returnRate * LogicSettings.RestingBloodHead) * dt) /
                    (1.0 + returnRate * dt);
        bloodCore = (bloodCore + (shiftCoreRate + returnRate * LogicSettings.RestingBloodCore) * dt) /
                    (1.0 + returnRate * dt);
        bloodLower = (bloodLower + (shiftLowerRate + lowerReturnRate * LogicSettings.RestingBloodLower) * dt) /
                     (1.0 + lowerReturnRate * dt);

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

        // O2 delivery depends on head blood volume relative to resting
        var perfusionRatio = bloodHead / LogicSettings.RestingBloodHead;
        perfusionRatio = Clamp(perfusionRatio, 0.0, 1.0);

        // Perfusion shaping
        var s = LogicSettings.O2PerfusionCurveStrength;
        var pivot = LogicSettings.O2PerfusionCurvePivot;
        var shapedPerfusion =
            perfusionRatio - s * perfusionRatio * (1.0 - perfusionRatio) * (perfusionRatio - pivot);
        shapedPerfusion = Clamp(shapedPerfusion, 0.0, 1.0);

        // convert perfusion -> effective O2 delivery (non-linear + mild sustained hypoperfusion penalty)
        var effectiveDelivery = Math.Pow(shapedPerfusion, LogicSettings.BrainO2PerfusionExponent);

        var threshold = Clamp(LogicSettings.BrainO2HypoperfusionThreshold, 0.01, 1.0);
        var hypoperfusion = Math.Max(0.0, threshold - shapedPerfusion) / threshold;
        var hypoperfusionPenalty = LogicSettings.BrainO2HypoperfusionPenaltyStrength * hypoperfusion * hypoperfusion;

        effectiveDelivery = Clamp(effectiveDelivery - hypoperfusionPenalty, 0.0, 1.0);

        // Target O2 is bounded by floor, then approached with time constants
        var targetBrainO2 = LogicSettings.BrainO2Floor + (1.0 - LogicSettings.BrainO2Floor) * effectiveDelivery;

        // Severity-based depletion tau: high perfusion loss => faster depletion
        var severity = 1.0 - effectiveDelivery;
        var depletionTau =
            LogicSettings.BrainO2DepletionTauMild +
            (LogicSettings.BrainO2DepletionTauSevere - LogicSettings.BrainO2DepletionTauMild) * severity;

        var o2Tau = targetBrainO2 < brainO2
            ? depletionTau
            : LogicSettings.BrainO2RecoveryTau;

        brainO2 = StepTowardsLinear(brainO2, targetBrainO2, o2Tau, dt);
        brainO2 = Clamp(brainO2, LogicSettings.BrainO2Floor, 1.0);

        // Cardiovascular fatigue: hrFatigue (0..1) accumulates with time × HR elevation,
        // decays slowly at rest. It is independent of current HR so it always wins eventually.
        var hrElevation = Math.Max(0.0, heartRateMultiplier - 1.0);
        var hrFatigueBuildRate = LogicSettings.CardioFatigueBuildRate * hrElevation;
        hrFatigue += hrElevation > 0.05
            ? hrFatigueBuildRate * dt
            : -(hrFatigue / LogicSettings.CardioFatigueRecoveryTau) * dt;
        hrFatigue = Clamp(hrFatigue, 0.0, 1.0);

        // fatigueHeartRateFloor is a fixed setting: the resting HR offset the cardiovascular
        // system is stuck at once fully fatigued (independent of feedback).
        // Baroreceptor target from current perfusion deficit.
        var baroTarget = 1.0 + LogicSettings.BaroreceptorGain * Math.Max(0, 1.0 - perfusionRatio);
        baroTarget = Math.Min(baroTarget, LogicSettings.MaxHeartRateMultiplier);

        // hrFatigue suppresses baroreceptor response: at hrFatigue=1 the target is pinned to the floor.
        var targetHR = baroTarget + hrFatigue * (fatigueHeartRateFloor - baroTarget);

        var hrTau = LogicSettings.BaroreceptorTimeConstant;
        heartRateMultiplier = StepTowardsLinear(heartRateMultiplier, targetHR, hrTau, dt);

        // Determine blackout vs redout from head blood volume
        primaryColor = bloodHead <= LogicSettings.RestingBloodHead;

        // Map brain O2 to consciousness via smooth step
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

        // sustained hypoxia/hypoperfusion bias (prevents 5G plateau like 0.09)
        var combinedDeficit = 1.0 - (0.5 * o2Normalized + 0.5 * perfNorm);
        targetConsciousness = Math.Max(
            0.0,
            targetConsciousness - LogicSettings.ConsciousnessDeficitBias * combinedDeficit * combinedDeficit);

        // hard cap when perfusion is critically low
        if (perfNorm < 0.25) targetConsciousness = Math.Min(targetConsciousness, perfNorm * 0.75);

        // Dynamic loss tau (non-linear so mid-G loses slower)
        var lossSeverity = Math.Pow(1.0 - targetConsciousness, LogicSettings.ConsciousnessLossSeverityExponent);

        var baseLossTau = LogicSettings.ConsciousnessLossTauMax +
                          (LogicSettings.ConsciousnessLossTauMin - LogicSettings.ConsciousnessLossTauMax) *
                          lossSeverity;

        // Critical collapse accelerator (mostly affects extreme +G)
        var criticalPerf = 1.0 - Clamp(
            perfNorm / LogicSettings.ConsciousnessCriticalPerfusionNorm, 0.0, 1.0);

        var criticalO2 = 1.0 - Clamp(
            o2Normalized / LogicSettings.ConsciousnessCriticalO2Norm, 0.0, 1.0);

        var critical = Math.Max(criticalPerf, criticalO2);
        var criticalTauMultiplier = 1.0 -
                                    (1.0 - LogicSettings.ConsciousnessCriticalTauMultiplierMin) * SmoothStep(critical);

        var lossTau = baseLossTau * criticalTauMultiplier;
        var tau = targetConsciousness < consciousnessLevel ? lossTau : LogicSettings.ConsciousnessRecoveryTau;

        consciousnessLevel = StepTowardsLinear(consciousnessLevel, targetConsciousness, tau, dt);
        consciousnessLevel = Clamp(consciousnessLevel, 0.0, 1.0);

        // Visual symptoms should start from physiology, but once consciousness is collapsing
        // they should continue toward full obscuration even if perfusion briefly rebounds.
        var visualPerf = Clamp((perfRatio - 0.45) / 0.55, 0.0, 1.0);
        var visualO2 = Clamp((o2Normalized - 0.15) / 0.85, 0.0, 1.0);

        // Fast visual reserve from physiology.
        var visualReserve = 0.7 * visualPerf + 0.3 * visualO2;
        var visualDeficit = 1.0 - visualReserve;

        // Early/mid-visual impairment path.
        // Keeps onset before LOC, but avoids saturating too early.
        var physiologicalVisualTarget = Clamp((visualDeficit - 0.18) / 0.82, 0.0, 1.0);
        physiologicalVisualTarget = Math.Pow(physiologicalVisualTarget, 2.2);

        #region Tunnelvision
        // Blackout path: if consciousness gets close to zero, tunnel vision must approach 1.
        // This also reduces sensitivity to short perfusion recoveries.
        var blackoutTunnelTarget = Clamp(1.0 - consciousnessLevel, 0.0, 1.0);
        blackoutTunnelTarget = Math.Pow(blackoutTunnelTarget, 2);

        // Use whichever impairment is worse.
        if (physiologicalVisualTarget > blackoutTunnelTarget)
        {
            Logger.Log("physiologicalTunnelTarget used", UniqueID);
            blackoutTunnelTarget = physiologicalVisualTarget;
        }
        else
        {
            Logger.Log("blackoutTunnelTarget used", UniqueID);
        }

        var tunnelTau = blackoutTunnelTarget > tunnelVisionLevel
            ? LogicSettings.TunnelVisualInTau
            : LogicSettings.TunnelVisualOutTau;
        tunnelVisionLevel = StepTowardsLinear(tunnelVisionLevel, blackoutTunnelTarget, tunnelTau, dt);
        tunnelVisionLevel = Clamp(tunnelVisionLevel, 0.0, 1.0);
        #endregion

        #region Greyscale
        // physiologicalVisualTarget produces good enough curve, no extra target needed
        var greyTau = physiologicalVisualTarget > greyScaleLevel
            ? LogicSettings.GreyscaleVisualInTau
            : LogicSettings.GreyscaleVisualOutTau;
        greyScaleLevel = StepTowardsLinear(greyScaleLevel, physiologicalVisualTarget, greyTau, dt);
        greyScaleLevel = Clamp(greyScaleLevel, 0.0, 1.0);
        #endregion

        #region Blur

        var earlyBlurTarget = 0.23 * (1.0 - Math.Exp(-8.0 * physiologicalVisualTarget));
        var earlyBlurInfluence = 1.0 - SmoothStep(Clamp((greyScaleLevel - 0.2) / 0.3, 0.0, 1.0));
        var earlyBlurBoost = Math.Max(earlyBlurTarget - greyScaleLevel * 0.5, 0.0) * earlyBlurInfluence;
        blurLevel = Clamp(greyScaleLevel + earlyBlurBoost, 0.0, 1.0);

        #endregion

        #region Filmgrain

        filmgrainLevel = Clamp(Math.Pow(TunnelVisionLevel, 1.5), 0.0, 1.0);

        #endregion

        // Hard guarantee: only enforce near total visual loss when consciousness is very close to zero.
        var nearLoc = Clamp((0.12 - consciousnessLevel) / 0.12, 0.0, 1.0);
        var tunnelFloorFromConsciousness = SmoothStep(nearLoc);

        tunnelVisionLevel = Math.Max(tunnelVisionLevel, tunnelFloorFromConsciousness);
        greyScaleLevel = Math.Max(greyScaleLevel, tunnelFloorFromConsciousness);
    }

    public PhysiologicalModel(int uniqueID)
    {
        UniqueID = uniqueID;
        Reset();
    }
}