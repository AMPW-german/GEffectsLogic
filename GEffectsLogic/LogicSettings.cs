namespace GEffectsLogic
{
    public static class LogicSettings
    {
        // --- Physiological model parameters ---

        // Resting blood distribution (fractions, must sum to 1.0)
        public static double RestingBloodHead { get; set; } = 0.2;
        public static double RestingBloodCore { get; set; } = 0.35;
        public static double RestingBloodLower { get; set; } = 0.45;

        // Hydrostatic shift: make mid-G less aggressive, keep high-G strong
        public static double HydrostaticShiftRate { get; set; } = 0.01;
        public static double HydrostaticShiftExponent { get; set; } = 2.2;

        // Keep these summing to ~1.0
        public static double HeadCoreShiftFraction { get; set; } = 0.45;
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
        public static double BaroreceptorGain { get; set; } = 3.0;           // HR increase per unit perfusion deficit
        public static double MaxHeartRateMultiplier { get; set; } = 3.0;     // max HR multiplier

        // Brain O2 thresholds for consciousness mapping
        public static double BrainO2Blackout { get; set; } = 0.3;  // below this → unconscious
        public static double BrainO2Full { get; set; } = 0.8;      // above this → fully conscious

        public static bool DebugMode { get; set; } = false;
        public static bool SuppresInfoLogs { get; set; } = false;

        // --- Hydrostatic/autoregulation ---
        public static double CerebralAutoregulationGzTolerance { get; set; } = 0.55; // G beyond 1G baseline

        // keep a small residual head blood fraction (avoids perfusion = 0 at high +G)
        public static double MinHeadBloodFraction { get; set; } = 0.02; // 2% of total blood

        // --- Brain O2 dynamics ---
        public static double BrainO2Floor { get; set; } = 0.18;
        public static double BrainO2DepletionTauMild { get; set; } = 10.5;   // mild perfusion loss
        public static double BrainO2DepletionTauSevere { get; set; } = 4.5;  // severe perfusion loss
        public static double BrainO2RecoveryTau { get; set; } = 9.0;

        // stronger non-linearity + sustained mild-loss penalty
        public static double BrainO2PerfusionExponent { get; set; } = 2.2;           // >1 lowers delivery at mid perfusion
        public static double BrainO2HypoperfusionThreshold { get; set; } = 0.92;     // penalty starts below this perfusion
        public static double BrainO2HypoperfusionPenaltyStrength { get; set; } = 0.75;

        // --- Consciousness mapping ---
        public static double ConsciousnessLossTauMin { get; set; } = 5.0;
        public static double ConsciousnessLossTauMax { get; set; } = 22.0;
        public static double ConsciousnessRecoveryTau { get; set; } = 12.0;
        public static double ConsciousnessPerfusionExponent { get; set; } = 1.4;
        public static double ConsciousnessO2Exponent { get; set; } = 1.0;

        // subtractive bias so mid-G sustained deficit does not plateau above zero
        public static double ConsciousnessDeficitBias { get; set; } = 0.22;

        // softer perfusion normalization for consciousness target
        public static double ConsciousnessPerfusionSoftMinRatio { get; set; } = 0.18;

        // non-linear loss + critical collapse gate
        public static double ConsciousnessLossSeverityExponent { get; set; } = 2.2;
        public static double ConsciousnessCriticalPerfusionNorm { get; set; } = 0.16;
        public static double ConsciousnessCriticalO2Norm { get; set; } = 0.28;
        public static double ConsciousnessCriticalTauMultiplierMin { get; set; } = 0.38;

        // Vision effects
        // Faster buildup than recovery so short rebounds do not immediately reopen vision.
        public static double VisualInTau { get; set; } = 2.0;
        public static double VisualOutTau { get; set; } = 7.5;
    }
}