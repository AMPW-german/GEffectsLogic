namespace GEffectsLogic;

public static class LogicSettings
{
    public static double StabilizationTimeThreshold { get; set; } = 600.0;

    // --- Physiological model parameters ---

    // Resting blood distribution (fractions, must sum to 1.0)
    public static double RestingBloodHead { get; set; } = 0.2;
    public static double RestingBloodCore { get; set; } = 0.35;
    public static double RestingBloodLower { get; set; } = 0.45;

    // Hydrostatic shift: make mid-G less aggressive, keep high-G strong
    public static double HydrostaticShiftRate { get; set; } = 0.0053;
    public static double HydrostaticShiftExponent { get; set; } = 2.2;

    // Keep these summing to ~1.0
    public static double CoreLowerShiftFraction { get; set; } = 0.55;

    // Passive return / compensation
    public static double PassiveReturnRate { get; set; } = 0.27;
    public static double BaroreceptorTimeConstant { get; set; } = 3.8;

    // G-suit effectiveness (0 = none, 1 = perfect). Scales with straining level.
    public static double GSuitEffectiveness { get; set; } = 0.3;

    // Brain oxygen model

    // Perfusion shaping for O2 depletion curve:
    // 0.0 = disabled (current behavior)
    // Higher = earlier onset + flatter tail
    public static double O2PerfusionCurveStrength { get; set; } = 1.2; // was 3

    // Pivot where shaping changes sign:
    // above pivot -> less delivery, below pivot -> more delivery
    public static double O2PerfusionCurvePivot { get; set; } = 0.82;

    // Baroreceptor reflex
    public static double BaroreceptorGain { get; set; } = 3.0; // HR increase per unit perfusion deficit
    public static double MaxHeartRateMultiplier { get; set; } = 3.0; // max HR multiplier

    // Brain O2 thresholds for consciousness mapping
    public static double BrainO2Blackout { get; set; } = 0.3; // below this → unconscious
    public static double BrainO2Full { get; set; } = 0.8; // above this → fully conscious

    public static bool DebugMode { get; set; } = false;
    public static bool SuppresInfoLogs { get; set; } = false;

    // --- Hydrostatic/autoregulation ---
    public static double CerebralAutoregulationGzTolerance { get; set; } = 0.65; // G beyond 1G baseline

    // keep a small residual head blood fraction (avoids perfusion = 0 at high +G)
    public static double MinHeadBloodFraction { get; set; } = 0.02; // 2% of total blood

    // --- Brain O2 dynamics ---
    public static double BrainO2Floor { get; set; } = 0.18;
    public static double BrainO2DepletionTauMild { get; set; } = 12.5; // mild perfusion loss
    public static double BrainO2DepletionTauSevere { get; set; } = 4.5; // severe perfusion loss
    public static double BrainO2RecoveryTau { get; set; } = 9.0;

    // stronger non-linearity + sustained mild-loss penalty
    public static double BrainO2PerfusionExponent { get; set; } = 1.9; // >1 lowers delivery at mid perfusion
    public static double BrainO2HypoperfusionThreshold { get; set; } = 0.92; // penalty starts below this perfusion
    public static double BrainO2HypoperfusionPenaltyStrength { get; set; } = 0.55;

    // --- Consciousness mapping ---
    public static double ConsciousnessLossTauMin { get; set; } = 5.0;
    public static double ConsciousnessLossTauMax { get; set; } = 24.0; // was 22.0
    public static double ConsciousnessRecoveryTau { get; set; } = 12.0;
    public static double ConsciousnessPerfusionExponent { get; set; } = 1.4;
    public static double ConsciousnessO2Exponent { get; set; } = 1.0;

    // subtractive bias so mid-G sustained deficit does not plateau above zero
    public static double ConsciousnessDeficitBias { get; set; } = 0.14; // was 0.16

    // softer perfusion normalization for consciousness target
    public static double ConsciousnessPerfusionSoftMinRatio { get; set; } = 0.18;

    // non-linear loss + critical collapse gate
    public static double ConsciousnessLossSeverityExponent { get; set; } = 2.9; // was 2.6
    public static double ConsciousnessCriticalPerfusionNorm { get; set; } = 0.16;
    public static double ConsciousnessCriticalO2Norm { get; set; } = 0.28;
    public static double ConsciousnessCriticalTauMultiplierMin { get; set; } = 0.15;

    // Vision effects
    // Faster buildup than recovery so short rebounds do not immediately reopen vision.
    public static double TunnelVisualInTau { get; set; } = 2.0;
    public static double TunnelVisualOutTau { get; set; } = 7.5;
    public static double GreyscaleVisualInTau { get; set; } = 8.0;
    public static double GreyscaleVisualOutTau { get; set; } = 2.0;

    // --- Straining / G-suit activation ---
    public static double StrainingStartGz { get; set; } = 1.5; // starts building
    public static double StrainingFullGz { get; set; } = 2.5; // reaches 1.0 target
    public static double StrainingTau { get; set; } = 1.0; // ~1s to approach target

    // --- G-suit coupling strengths ---
    public static double GSuitGlobalShiftReductionMax { get; set; } = 0.20; // optional mild global scaling
    public static double GSuitCoreLowerReductionMax { get; set; } = 0.60; // reduce core->lower pooling
    public static double GSuitLowerReturnBoostMax { get; set; } = 0.80; // increase lower return

    // --- Fatigue / resistance reduction ---

    // Fraction of GSuitEffectiveness that the suit retains passively (hardware inflation) regardless of fatigue
    public static double GSuitPassiveFraction { get; set; } = 0.25;

    // Straining (AGSM) fatigue: rate at which the human straining component degrades
    // Build rate is per-second at strainingLevel=1 (quadratic: actual rate = BuildRate * strainingLevel²)
    // ~60-90s of max straining to saturate (1/BuildRate ≈ saturation time)
    public static double StrainingFatigueBuildRate { get; set; } = 0.015; // saturates ~67s at full strain
    public static double StrainingFatigueRecoveryTau { get; set; } = 150.0; // ~2.5 min to recover

    // G-suit mechanical fatigue: slower than straining fatigue (suit outlasts the pilot's AGSM)
    // Build rate is per-second at strainingLevel=1 (linear: actual rate = BuildRate * strainingLevel)
    public static double GSuitFatigueBuildRate { get; set; } = 0.004; // saturates ~250s at full strain
    public static double GSuitFatigueRecoveryTau { get; set; } = 300.0; // ~5 min to recover

    // Cardiovascular fatigue: hrFatigue (0..1) accumulates at CardioFatigueBuildRate × hrElevation per second
    public static double CardioFatigueBuildRate { get; set; } = 0.008; // ~125s at max HR elevation to fully fatigue
    public static double CardioFatigueRecoveryTau { get; set; } = 240.0; // ~4 min to fully recover
    public static double CardioFatigueMaxHrFloor { get; set; } = 1.25; // HR floor when fully fatigued
}