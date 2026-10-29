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

namespace GEffectsLogic;

public partial class PhysiologicalModel
{
    [MethodImpl(AggressiveOptimization)]
    private static double ShapedPerfusion(double headBlood, LogicSettings settings)
    {
        var ratio = Clamp(headBlood / settings.RestingBloodHead, 0.0, 1.0);
        var shaped = ratio - settings.O2PerfusionCurveStrength * ratio * (1.0 - ratio) *
            (ratio - settings.O2PerfusionCurvePivot);
        return Clamp(shaped, 0.0, 1.0);
    }

    [MethodImpl(AggressiveOptimization)]
    private static double DeliveryCore(double shapedPerfusion, LogicSettings settings)
    {
        var delivery = Math.Pow(shapedPerfusion, settings.BrainO2PerfusionExponent);
        var threshold = Clamp(settings.BrainO2HypoperfusionThreshold, 0.01, 1.0);
        var hypoperfusion = Math.Max(0.0, threshold - shapedPerfusion) / threshold;
        var penalty = settings.BrainO2HypoperfusionPenaltyStrength * hypoperfusion * hypoperfusion;
        return delivery - penalty;
    }

    [MethodImpl(AggressiveOptimization)]
    private static double EffectiveDelivery(double shapedPerfusion, double heartRate,
        double arterial, LogicSettings settings)
    {
        return Clamp(DeliveryCore(shapedPerfusion, settings), 0.0, 1.0) * heartRate * arterial;
    }

    [MethodImpl(AggressiveOptimization)]
    private static double BrainO2Target(double effectiveDelivery, LogicSettings settings) =>
        settings.BrainO2Floor + (1.0 - settings.BrainO2Floor) * effectiveDelivery;

    [MethodImpl(AggressiveOptimization)]
    private static double BrainDepletionTau(double effectiveDelivery, LogicSettings settings)
    {
        var severity = 1.0 - effectiveDelivery;
        return settings.BrainO2DepletionTauMild +
               (settings.BrainO2DepletionTauSevere - settings.BrainO2DepletionTauMild) * severity;
    }

    [MethodImpl(AggressiveOptimization)]
    private static double GxLungImpairment(double gxMagnitude, LogicSettings settings)
    {
        if (gxMagnitude <= settings.GxLungOxygenationImpairmentThreshold) return 0.0;
        return Clamp(settings.GxLungOxygenationImpairmentSeverity * Math.Pow(
            (gxMagnitude - settings.GxLungOxygenationImpairmentThreshold) / 4.0, 2.0), 0.0, 1.0);
    }

    [MethodImpl(AggressiveOptimization)]
    internal static double O2Normalized(double headO2, LogicSettings settings) => Clamp(
        (headO2 - settings.BrainO2Blackout) /
        (settings.BrainO2Full - settings.BrainO2Blackout), 0.0, 1.0);

    [MethodImpl(AggressiveOptimization)]
    internal static double PerfusionNormalized(double headBlood, LogicSettings settings)
    {
        var ratio = Clamp(headBlood / settings.RestingBloodHead, 0.0, 1.0);
        return Clamp(
            (ratio - settings.ConsciousnessPerfusionSoftMinRatio) /
            (1.0 - settings.ConsciousnessPerfusionSoftMinRatio), 0.0, 1.0);
    }

    [MethodImpl(AggressiveOptimization)]
    internal static double ConsciousnessRawReserve(double o2Normalized, double perfNorm,
        LogicSettings settings)
    {
        var o2Term = Math.Pow(o2Normalized, settings.ConsciousnessO2Exponent);
        var perfTerm = Math.Pow(SmoothStep(perfNorm), settings.ConsciousnessPerfusionExponent);
        var combinedDeficit = 1.0 - (0.5 * o2Normalized + 0.5 * perfNorm);
        return o2Term * perfTerm -
               settings.ConsciousnessDeficitBias * combinedDeficit * combinedDeficit;
    }

    [MethodImpl(AggressiveOptimization)]
    private static double ConsciousnessTargetValueCore(double o2Normalized,
        double perfNorm, double pressure, LogicSettings settings,
        out double effectivePressure)
    {
        var rawReserve = ConsciousnessRawReserve(o2Normalized, perfNorm, settings);
        var target = Math.Max(0.0, rawReserve);

        effectivePressure = Clamp(
            (pressure - settings.CerebralPressureImpairmentDeadband) /
            Math.Max(1.0 - settings.CerebralPressureImpairmentDeadband, 1e-9), 0.0, 1.0);
        var pressureReserve = Math.Pow(Clamp(1.0 - effectivePressure, 0.0, 1.0),
            settings.CerebralPressureConsciousnessExponent);

        if (perfNorm < 0.25) target = Math.Min(target, perfNorm * 0.75);
        target = Math.Min(target, pressureReserve);
        return target;
    }

    [MethodImpl(AggressiveOptimization)]
    internal static double ConsciousnessTargetValue(double o2Normalized,
        double perfNorm, double pressure, LogicSettings settings) =>
        ConsciousnessTargetValueCore(o2Normalized, perfNorm, pressure,
            settings, out _);

    [MethodImpl(AggressiveOptimization)]
    internal static (double Target, double LossTau) ConsciousnessTargetAndTau(
        double o2Normalized, double perfNorm, double pressure, LogicSettings settings)
    {
        var target = ConsciousnessTargetValueCore(o2Normalized, perfNorm, pressure,
            settings, out var effectivePressure);

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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
    private static double RedoutTarget(double headOverfill, LogicSettings settings)
    {
        var range = Math.Max(
            settings.VisualRedoutFullHeadBloodOverfill -
            settings.VisualRedoutOnsetHeadBloodOverfill, 1e-9);
        return SmoothStep(Clamp(
            (headOverfill - settings.VisualRedoutOnsetHeadBloodOverfill) / range, 0.0, 1.0));
    }

    [MethodImpl(AggressiveOptimization)]
    private static double LoCCeiling(double consciousness, LogicSettings settings)
    {
        var range = Math.Max(
            settings.ConsciousnessRecoveryThreshold - settings.ConsciousnessLossThreshold, 1e-9);
        return Math.Pow(Clamp(
            (settings.ConsciousnessRecoveryThreshold - consciousness) / range, 0.0, 1.0),
            settings.VisualLoCConsciousnessExponent);
    }

    // Affine system y' = M(t) y + b(t) over one piece, with optional coordinates

}
