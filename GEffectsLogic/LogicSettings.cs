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

namespace GEffectsLogic;

public sealed record class LogicSettings
{
    public static LogicSettings Default { get; } = new();

    public double StabilizationTimeThreshold { get; init; } = 600.0;

    // --- Physiological model parameters ---

    // Resting blood distribution (fractions, must sum to 1.0)
    public double RestingBloodHead { get; init; } = 0.2;
    public double RestingBloodCore { get; init; } = 0.35;
    public double RestingBloodLower { get; init; } = 0.45;

    // Hydrostatic shift: make mid-G less aggressive, keep high-G strong
    public double HydrostaticShiftRate { get; init; } = 0.0063;
    public double HydrostaticShiftExponent { get; init; } = 2.0;

    public double CoreLowerShiftFraction { get; init; } = 0.55;

    // Passive return / compensation
    public double PassiveReturnRate { get; init; } = 0.47;
    public double HeadPressureReturnRate { get; init; } = 0.25;
    public double HeadPressureReturnExponent { get; init; } = 11.0;

    // G-suit effectiveness (0 = none, 1 = perfect). Scales with straining level.
    public double GSuitEffectiveness { get; init; } = 0.3;

    // Brain oxygen model

    // Perfusion shaping for O2 depletion curve:
    // 0.0 = disabled (current behavior)
    // Higher = earlier onset + flatter tail
    public double O2PerfusionCurveStrength { get; init; } = 1.2; // was 3

    // Pivot where shaping changes sign:
    // above pivot -> less delivery, below pivot -> more delivery
    public double O2PerfusionCurvePivot { get; init; } = 0.82;

    // Baroreceptor reflex
    public double BaroreceptorGain { get; init; } = 3.0; // HR increase per unit perfusion deficit
    public double BaroreceptorTimeConstantPositive { get; init; } = 3.8; // Baroreceptor reflex time for Gz+
    public double BaroreceptorTimeConstantNegativeMin { get; init; } = 0.75;
    public double BaroreceptorTimeConstantNegativeMax { get; init; } = 10.0;
    public double NegativeGHeartStopOverfill { get; init; } = 0.25;
    public double MaxHeartRateMultiplier { get; init; } = 3.0; // max HR multiplier

    // Brain O2 thresholds for consciousness mapping
    public double BrainO2Blackout { get; init; } = 0.3; // below this → unconscious
    public double BrainO2Full { get; init; } = 0.8; // above this → fully conscious

    public static bool DebugMode { get; set; }
    public static bool SuppresInfoLogs { get; set; }

    // keep a small residual head blood fraction (avoids perfusion = 0 at high +G)
    public double MinHeadBloodFraction { get; init; } = 0.02;

    // --- Brain O2 dynamics ---
    public double BrainO2Floor { get; init; } = 0.18;
    public double BrainO2DepletionTauMild { get; init; } = 12.5; // mild perfusion loss
    public double BrainO2DepletionTauSevere { get; init; } = 4.5; // severe perfusion loss
    public double BrainO2RecoveryTau { get; init; } = 7.0;

    // stronger non-linearity + sustained mild-loss penalty
    public double BrainO2PerfusionExponent { get; init; } = 1.9; // >1 lowers delivery at mid perfusion
    public double BrainO2HypoperfusionThreshold { get; init; } = 0.92; // penalty starts below this perfusion
    public double BrainO2HypoperfusionPenaltyStrength { get; init; } = 0.75;

    // --- Consciousness mapping ---
    public double ConsciousnessLossTauMin { get; init; } = 5.0;
    public double ConsciousnessLossTauMax { get; init; } = 24.0;
    public double ConsciousnessRecoveryTau { get; init; } = 12.0;
    public double ConsciousnessPerfusionExponent { get; init; } = 1.4;
    public double ConsciousnessO2Exponent { get; init; } = 1.0;
    public double ConsciousnessCriticalPressureNorm { get; init; } = 0.6;

    // subtractive bias so mid-G sustained deficit does not plateau above zero
    public double ConsciousnessDeficitBias { get; init; } = 0.14;

    // softer perfusion normalization for consciousness target
    public double ConsciousnessPerfusionSoftMinRatio { get; init; } = 0.18;

    // non-linear loss + critical collapse gate
    public double ConsciousnessLossSeverityExponent { get; init; } = 2.9; // was 2.6
    public double ConsciousnessCriticalPerfusionNorm { get; init; } = 0.16;
    public double ConsciousnessCriticalO2Norm { get; init; } = 0.28;
    public double ConsciousnessCriticalTauMultiplierMin { get; init; } = 0.15;

    public double CerebralPressureImpairmentMaxBuildRate { get; init; } = 0.65;
    public double CerebralPressureImpairmentExponent { get; init; } = 150.0;
    public double CerebralPressureImpairmentMidOverfill { get; init; } = 0.07;
    // Sustained negative Gz causes cephalic venous congestion (redout headache): a mild extra
    // impairment build rate driven by the sustained -Gz load itself. Driving this off the input
    // rather than simulated head overfill keeps it exact across dt (the overfill equilibrium is
    // dt-sensitive) and continuous for mixed-axis inputs. The cap keeps it from stacking on top
    // of the logistic term that already dominates at high -Gz.
    public double CerebralPressureImpairmentNegativeGzRate { get; init; } = 0.0011;
    public double CerebralPressureImpairmentNegativeGzCap { get; init; } = 1.5;
    public double CerebralPressureImpairmentRecoveryTau { get; init; } = 25.0;
    public double CerebralPressureConsciousnessExponent { get; init; } = 7.7;

    // Small impairment tolerance: below this the temporary pressure impairment causes no consciousness
    // loss. Set just under the 0G residual overfill so weightless/lying-down stays near-full, while a
    // sustained -1Gz still incurs a small (~5%) decrease simulating head-pressure headache/discomfort.
    public double CerebralPressureImpairmentDeadband { get; init; } = 0.003;

    // Vision effects
    // Faster buildup than recovery so short rebounds do not immediately reopen vision.
    public double VisualTunnelVisionInTau { get; init; } = 2.0;
    public double VisualTunnelVisionOutTau { get; init; } = 7.5;
    public double VisualRedoutOnsetHeadBloodOverfill { get; init; } = 0.0035;
    public double VisualRedoutFullHeadBloodOverfill { get; init; } = 0.06;
    public double VisualRedoutInTau { get; init; } = 0.5;
    public double VisualRedoutOutTau { get; init; } = 2.0;
    public double VisualGrayscaleInTau { get; init; } = 8.0;
    public double VisualGrayscaleOutTau { get; init; } = 2.0;
    public double ConsciousnessLossThreshold { get; init; } = 0.05;
    public double ConsciousnessRecoveryThreshold { get; init; } = 0.35;
    public double VisualLoCConsciousnessExponent { get; init; } = 3.0;
    public double VisualLoCIncreaseRate { get; init; } = 0.5;
    public double VisualLoCDecreaseRate { get; init; } = 1.0;

    // --- Straining / G-suit activation ---
    public double StrainingStartGz { get; init; } = 1.5; // starts building
    public double StrainingFullGz { get; init; } = 2.5; // reaches 1.0 target
    public double StrainingTau { get; init; } = 1.0; // ~1s to approach target

    // --- G-suit coupling strengths ---
    public double GSuitGlobalShiftReductionMax { get; init; } = 0.20; // optional mild global scaling
    public double GSuitCoreLowerReductionMax { get; init; } = 0.60; // reduce core->lower pooling
    public double GSuitLowerReturnBoostMax { get; init; } = 0.80; // increase lower return

    // --- Fatigue / resistance reduction ---

    // Fraction of GSuitEffectiveness that the suit retains passively (hardware inflation) regardless of fatigue
    public double GSuitPassiveFraction { get; init; } = 0.25;

    // Straining (AGSM) fatigue: rate at which the human straining component degrades
    // Build rate is per-second at strainingLevel=1 (quadratic: actual rate = BuildRate * strainingLevel²)
    // ~60-90s of max straining to saturate (1/BuildRate ≈ saturation time)
    public double StrainingFatigueBuildRate { get; init; } = 0.015; // saturates ~67s at full strain
    public double StrainingFatigueRecoveryTau { get; init; } = 150.0; // ~2.5 min to recover

    // G-suit mechanical fatigue: slower than straining fatigue (suit outlasts the pilot's AGSM)
    // Build rate is per-second at strainingLevel=1 (linear: actual rate = BuildRate * strainingLevel)
    public double GSuitFatigueBuildRate { get; init; } = 0.004; // saturates ~250s at full strain
    public double GSuitFatigueRecoveryTau { get; init; } = 300.0; // ~5 min to recover

    // Cardiovascular fatigue: hrFatigue (0..1) accumulates at CardioFatigueBuildRate × hrElevation per second
    public double CardioFatigueBuildRate { get; init; } = 0.008; // ~125s at max HR elevation to fully fatigue
    public double CardioFatigueHrElevationThreshold { get; init; } = 0.8; // HR elevation (above resting) required before cardiovascular fatigue accumulates
    public double CardioFatigueRecoveryTau { get; init; } = 240.0; // ~4 min to fully recover
    public double CardioFatigueMaxHrFloor { get; init; } = 1.25; // HR floor when fully fatigued

    // --- Expanded oxygen model parameters ---
    public double LungOxygenationRate { get; init; } = 0.3; // Rate at which lungs refresh blood oxygen (slower for pure Gz timing)
    public double CoreBloodO2Resting { get; init; } = 0.98; // Resting O2 in core (after lung oxygenation)
    public double HeadBloodO2Resting { get; init; } = 0.95; // Resting O2 in head
    public double LowerBloodO2Resting { get; init; } = 0.90; // Resting O2 in lower body
    public double OxygenConsumptionRateHead { get; init; } = 0.03; // O2 consumption rate in head (increased for pure Gz timing)
    public double OxygenConsumptionRateCore { get; init; } = 0.001; // O2 consumption rate in core (minimal for pure Gz)
    public double OxygenConsumptionRateLower { get; init; } = 0.0005; // O2 consumption rate in lower body (minimal for pure Gz)

    // Heart-rate-dependent oxygen transport parameters
    public double OxygenTransportBaseRate { get; init; } = 0.06; // Base rate of O2 transport between compartments (minimal for pure Gz)
    public double OxygenTransportHeartRateSensitivity { get; init; } = 1.0; // How strongly HR affects O2 transport

    // Respiratory fatigue parameters (separate from cardiovascular)
    public double RespiratoryFatigueBuildRate { get; init; } = 0.01; // Build rate per second at max effort
    public double RespiratoryFatigueRecoveryTau { get; init; } = 120.0; // Recovery time constant (2 minutes)
    public double RespiratoryFatigueHrFloor { get; init; } = 1.0; // HR floor when respiratory fatigue maxes out (breathing rate limit)

    // Gx (transverse) parameters
    public double GxToleranceImprovementFactor { get; init; } = 0.15; // Gz tolerance improvement per Gx
    public double GxRespiratoryFatigueThreshold { get; init; } = 1.0; // Gx level where breathing effort starts building respiratory fatigue
    public double GxRespiratoryFatigueAccelerationFactor { get; init; } = 2.5; // Multiplier for respiratory fatigue under Gx
    public double GxLungOxygenationImpairmentThreshold { get; init; } = 2.0; // Gx level where lung oxygenation begins to fail
    public double GxLungOxygenationImpairmentSeverity { get; init; } = 0.9; // Max lung oxygenation reduction at extreme Gx
    public double GxLungOxygenationImpairmentFullGx { get; init; } = 15.0; // Gx level where respiratory impairment saturates (~15G: chest wall can no longer be lifted)
    public double GxHypoxiaDepletionTau { get; init; } = 150.0; // Arterial O2 reserve depletion time constant under respiratory failure (GLoC after ~1-2 min at >=15Gx)
    public double GxHypoxiaRecoveryTau { get; init; } = 8.0; // Arterial O2 reoxygenation time constant once the Gx load is relieved
    public double GxSuddenLoCThreshold { get; init; } = 4.0; // Gx level contributing to sudden G-LOC
    public double GxSuddenLoCSeverity { get; init; } = 0.2; // Severity of Gx contribution to sudden G-LOC
    public double GxPainFactor { get; init; } = 0.05; // Pain contribution per Gx (for future use)

    // Gy (lateral) parameters - non-linear scaling
    public double GyToleranceReductionBase { get; init; } = 0.3; // Base Gz tolerance reduction per Gy
    public double GyToleranceNonlinearity { get; init; } = 1.5; // Exponent for non-linear scaling
    public double GySuddenLoCThreshold { get; init; } = 1.5; // Gy level contributing to sudden G-LOC (low tolerance)
    public double GySuddenLoCSeverity { get; init; } = 0.4; // Severity of Gy contribution to sudden G-LOC (high impact)

    // Gy neck side fatigue parameters (damage/death with ceiling)
    public double GyNeckFatigueBuildRate { get; init; } = 0.5; // Build rate per second at extreme Gy
    public double GyNeckFatigueRecoveryTau { get; init; } = 60.0; // Recovery time constant
    public double GyNeckFatigueThreshold { get; init; } = 3.0; // Gy level where neck fatigue begins
    public double GyNeckFatigueCeiling { get; init; } = 0.8; // Maximum fatigue level (ceiling - can't reach death below this)
    public double GyNeckFatigueNonlinearity { get; init; } = 2.0; // Exponent for non-linear scaling
    public double GyNeckFatigueDeathLevel { get; init; } = 0.95; // Fatigue level causing death
    public double GyNeckFatigueDeathDelay { get; init; } = 1.5; // Delay (seconds) from reaching death level to actual death

    // Gy lung compression parameters
    public double GyLungCompressionThreshold { get; init; } = 2.0; // Gy level where lung compression begins
    public double GyLungCompressionSeverity { get; init; } = 0.8; // Oxygen exchange reduction at high sustained Gy
    public double GyLungCompressionTau { get; init; } = 30.0; // Time constant for lung compression effects (minutes)
    public double GyLungCompressionRecoveryTau { get; init; } = 60.0; // Recovery time constant

    // Gy pain parameters
    public double GyPainBaseFactor { get; init; } = 0.2; // Base pain per Gy
    public double GyPainNonlinearity { get; init; } = 2.0; // Exponent for pain scaling
    public double GyPainAccumulationTau { get; init; } = 5.0; // Pain accumulation time constant
    public double GyPainRecoveryTau { get; init; } = 20.0; // Pain recovery time constant

    // Multi-axis sudden G-LOC parameters
    public double MultiAxisSuddenLoCThreshold { get; init; } = 0.7; // Combined threshold for sudden G-LOC
    public double SuddenLoCConsciousnessDrop { get; init; } = 0.3; // Consciousness drop when sudden G-LOC triggers
    public double SuddenLoCRecoveryTau { get; init; } = 5.0; // Recovery time from sudden G-LOC
}
