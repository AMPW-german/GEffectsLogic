using System;
using System.Collections.Generic;
using System.Text;

namespace GEffectsLogic
{
    public static class LogicSettings
    {
        // --- Physiological model parameters ---

        // Resting blood distribution (fractions, must sum to 1.0)
        public static double RestingBloodHead { get; set; } = 0.15;
        public static double RestingBloodCore { get; set; } = 0.35;
        public static double RestingBloodLower { get; set; } = 0.50;

        // Hydrostatic shift: volume fraction shifted per second per G beyond 1G
        public static double HydrostaticShiftRate { get; set; } = 0.018;
        // Fraction of the total shift that goes between head ↔ core
        public static double HeadCoreShiftFraction { get; set; } = 0.45;
        // Fraction of the total shift that goes between core ↔ lower
        public static double CoreLowerShiftFraction { get; set; } = 0.55;

        // Passive return toward resting distribution (per second, multiplied by HR multiplier)
        public static double PassiveReturnRate { get; set; } = 0.3;

        // G-suit effectiveness (0 = none, 1 = perfect). Scales with straining level.
        public static double GSuitEffectiveness { get; set; } = 0.3;

        // Brain oxygen model
        public static double O2DeliveryRate { get; set; } = 2;   // O2 units/s at resting perfusion
        public static double O2ConsumptionRate { get; set; } = 1.6; // O2 units/s constant brain demand

        // Baroreceptor reflex
        public static double BaroreceptorGain { get; set; } = 2.0;           // HR increase per unit perfusion deficit
        public static double BaroreceptorTimeConstant { get; set; } = 2.0;   // seconds, response delay
        public static double MaxHeartRateMultiplier { get; set; } = 3.0;     // max HR multiplier

        // Brain O2 thresholds for consciousness mapping
        public static double BrainO2Blackout { get; set; } = 0.3;  // below this → unconscious
        public static double BrainO2Full { get; set; } = 0.8;      // above this → fully conscious

        public static bool DebugMode { get; set; } = false;
        public static bool SuppresInfoLogs { get; set; } = false;
    }
}
