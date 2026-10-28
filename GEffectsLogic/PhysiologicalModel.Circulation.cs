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


using System.Globalization;
using System.Runtime.CompilerServices;

namespace GEffectsLogic;

public partial class PhysiologicalModel
{
    #region intervalIntegration

    internal const int CirculationDimensions = 5;
    private const int CirculationSolvedDimensions = 4;
    private const int MaxNewtonIterations = 12;
    private const int MaxDampingHalvings = 8;
    internal const double PressureBoundRootToleranceSeconds = 1e-8;
    private const double ScaledResidualTolerance = 1e-10;
    private const double ZeroTargetTolerance = 1e-6;
    // Collocation defect budget |z' - f(z)| * span in state units. A converged
    // solve whose dense trajectory exceeds it is retried on a shorter span
    // sized from the defect magnitude, so refinement follows the trajectory's
    // curvature instead of a fixed substep size.
    private const double ResolutionDefectTolerance = 4e-4;
    private const double MinimumResolutionSpan = PressureBoundRootToleranceSeconds;

    // Seed bracket half-width around the dense (polynomial-level) root-event
    // estimate for the downstream prefix re-solve search. The re-solved
    // prefix root has been observed within well under 1e-6 s of the dense
    // guess, so a seed bracket of this width usually straddles it
    // immediately; when it does not, the previous scan bracket is used
    // unchanged.
    private const double BoundEventSeedPadSeconds = 4e-6;

    internal static IntegrationState IndependentCirculationStateAt(
        in IntegrationState initial, double t, double gx, double gz, LogicSettings settings)
    {
        if (t <= 0.0) return initial;

        var strainTarget = Clamp(
            (gz - settings.StrainingStartGz) /
            (settings.StrainingFullGz - settings.StrainingStartGz), 0.0, 1.0);
        var strainFatigue = EvolveThresholdFatigue(initial.StrainingLevel, strainTarget,
            settings.StrainingTau, t, initial.StrainingFatigue,
            settings.StrainingFatigueBuildRate, settings.StrainingFatigueRecoveryTau,
            quadratic: true);
        var suitFatigue = EvolveThresholdFatigue(initial.StrainingLevel, strainTarget,
            settings.StrainingTau, t, initial.GSuitFatigue,
            settings.GSuitFatigueBuildRate, settings.GSuitFatigueRecoveryTau,
            quadratic: false);

        return initial with
        {
            StrainingLevel = IndependentStrainingLevelAt(initial.StrainingLevel,
                t, gz, settings),
            StrainingFatigue = Clamp(strainFatigue, 0.0, 1.0),
            GSuitFatigue = Clamp(suitFatigue, 0.0, 1.0),
            RespiratoryFatigue = IndependentRespiratoryAt(initial.RespiratoryFatigue,
                t, gx, settings)
        };
    }

    [MethodImpl(AggressiveOptimization)]
    private static double IndependentStrainingLevelAt(double initialLevel, double t,
        double gz, LogicSettings settings)
    {
        if (t <= 0.0) return initialLevel;
        var strainTarget = Clamp(
            (gz - settings.StrainingStartGz) /
            (settings.StrainingFullGz - settings.StrainingStartGz), 0.0, 1.0);
        var instant = settings.StrainingTau <= 1e-9;
        var strain = instant
            ? strainTarget
            : strainTarget + (initialLevel - strainTarget) *
              Math.Exp(-t / settings.StrainingTau);
        return Clamp(strain, 0.0, 1.0);
    }

    [MethodImpl(AggressiveOptimization)]
    private static double IndependentRespiratoryAt(double initialFatigue, double t,
        double gx, LogicSettings settings)
    {
        if (t <= 0.0) return initialFatigue;
        var respiratoryBuild = settings.RespiratoryFatigueBuildRate *
            Math.Max(0.0, Math.Abs(gx) - settings.GxRespiratoryFatigueThreshold) * 0.5 *
            (1.0 + (settings.GxRespiratoryFatigueAccelerationFactor - 1.0) * Math.Abs(gx));
        var respiratoryTau = settings.RespiratoryFatigueRecoveryTau;
        var respiratory = respiratoryTau <= 1e-9
            ? respiratoryBuild * respiratoryTau
            : respiratoryBuild * respiratoryTau +
              (initialFatigue - respiratoryBuild * respiratoryTau) *
              Math.Exp(-t / respiratoryTau);
        return Clamp(respiratory, 0.0, 1.0);
    }

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
    private static double AdjustedShiftRate(in IntegrationState state, double gx, double gy,
        double gz, LogicSettings settings) =>
        HydrostaticShiftRate(gx, gy, gz, settings) *
        (1.0 - settings.GSuitGlobalShiftReductionMax * SuitActivation(in state, gz, settings));

    [MethodImpl(AggressiveOptimization)]
    internal static CirculationMode EvaluateCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian) =>
        EvaluateCirculation(in state, gx, gy, gz, settings, rates, jacobian, true);

    [MethodImpl(AggressiveOptimization)]
    private static CirculationMode EvaluateCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian,
        bool includePressure, bool? headwardOverride = null)
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
        var headward = headwardOverride ??
            (overfill > 0.0 || (head == restHead && fHead > 0.0));

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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
    private static double HeldCoreDrive(double rate, double shiftRate, LogicSettings settings) =>
        shiftRate + settings.PassiveReturnRate * rate * settings.RestingBloodCore;

    [MethodImpl(AggressiveOptimization)]
    internal static CirculationMode EvaluateCoreConstrainedCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian) =>
        EvaluateCoreConstrainedCirculation(in state, gx, gy, gz, settings, rates, jacobian, true);

    [MethodImpl(AggressiveOptimization)]
    private static CirculationMode EvaluateCoreConstrainedCirculation(
        in IntegrationState state, double gx, double gy, double gz,
        LogicSettings settings, double[] rates, double[][]? jacobian,
        bool includePressure) =>
        EvaluateBoundConstrainedCirculation(in state, gx, gy, gz, settings, rates, jacobian,
            includePressure, BloodBounds.Core);

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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
    [MethodImpl(AggressiveOptimization)]
    internal IntegrationResult AdvanceCirculationInterval(
        in IntegrationState initial,
        double dt,
        double gx,
        double gy,
        double gz,
        LogicSettings settings)
#pragma warning restore CA1822
    {
        var crossings = AcquireDiagnosticsList();
        var violations = AcquireDiagnosticsList();
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
        var span = dt;
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

        // Per-iteration scratch for bound-entry stage checks; contents are
        // flushed into violations each use and never escape.
        var boundStageViolations = AcquireDiagnosticsList();
        var guard = 0;
        while (remaining > 0.0 && converged && violations.Count == 0 && crossings.Count == 0)
        {
            if (++guard > 512)
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

            var solve = AdvanceCirculationSegment(in state, span, gx, gy, gz, settings,
                bounds);
            if (!solve.Converged)
            {
                if (span <= MinimumResolutionSpan)
                {
                    converged = false;
                    crossings.AddRange(solve.ModeCrossings);
                    violations.AddRange(solve.StateViolations);
                    break;
                }

                span *= 0.5;
                continue;
            }

            // A converged cubic can still under-resolve a fast transient: check
            // the collocation defect before trusting it for event detection or
            // acceptance, and retry on a prefix ending at the worst probe.
            if (TryResolutionSplit(in state, solve, span, bounds, gx, gy, gz,
                    settings, out var resolutionSplit))
            {
                span = resolutionSplit;
                continue;
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
                    span);
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
                    settings.MinHeadBloodFraction, span);
                if (entry.HasValue && entry.Value < earliestTime)
                {
                    earliestTime = entry.Value;
                    kind = IntervalEventKind.BoundEntry;
                    eventBound = BloodBounds.Head;
                }
            }

            if ((bounds & BloodBounds.Lower) == 0)
            {
                var entry = EarliestLevelEntry(solve.LowerCoefficients, 0.0, span);
                if (entry.HasValue && entry.Value < earliestTime)
                {
                    earliestTime = entry.Value;
                    kind = IntervalEventKind.BoundEntry;
                    eventBound = BloodBounds.Lower;
                }
            }

            foreach (var bound in HeldBounds(bounds))
            {
                var guess = EarliestBoundRelease(bound, in state, solve, span, gx, gy,
                    gz, settings);
                if (guess.HasValue && guess.Value.Time < earliestTime)
                {
                    earliestTime = guess.Value.Time;
                    releaseGuess = guess.Value;
                    kind = IntervalEventKind.BoundRelease;
                    eventBound = bound;
                }
            }

            var transition = EarliestModeTransition(in state, solve, span,
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
                    localized = LocalizeBoundEntry(eventBound, in state, span,
                        earliestTime, solve, bounds, gx, gy, gz, settings, violations,
                        out eventTime, out prefix);
                else if (kind == IntervalEventKind.BoundRelease)
                    localized = LocalizeBoundRelease(eventBound, in state, span,
                        releaseGuess, solve, bounds, gx, gy, gz, settings, violations,
                        out eventTime, out prefix);
                else
                    localized = LocalizeModeTransition(in state, span, earliestTime,
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
                    var rawEventState = final;
                    var headCoefficients = completed.HeadCoefficients;
                    var lowerCoefficients = completed.LowerCoefficients;
                    var headSlope = headCoefficients[1] +
                        2.0 * headCoefficients[2] * eventTime +
                        3.0 * headCoefficients[3] * eventTime * eventTime;
                    var lowerSlope = lowerCoefficients[1] +
                        2.0 * lowerCoefficients[2] * eventTime +
                        3.0 * lowerCoefficients[3] * eventTime * eventTime;
                    var eventSlope = eventBound switch
                    {
                        BloodBounds.Head => headSlope,
                        BloodBounds.Lower => lowerSlope,
                        BloodBounds.Core => -headSlope - lowerSlope,
                        _ => double.NaN
                    };
                    var rawCoordinate = BloodBoundCoordinate(in final, eventBound);
                    var allowedEventCorrection =
                        Math.Abs(eventSlope) * PressureBoundRootToleranceSeconds + 1e-12;
                    if (!IsFiniteNumber(eventSlope) ||
                        !IsFiniteNumber(allowedEventCorrection))
                    {
                        violations.Add(
                            $"bound entry {eventBound} rawCoordinate={rawCoordinate.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"correction=unavailable " +
                            $"allowed={allowedEventCorrection.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"slope={eventSlope.ToString("R", CultureInfo.InvariantCulture)}");
                        break;
                    }
                    if (!TryProjectActiveBloodState(in final, bounds | eventBound,
                            settings, allowedEventCorrection, out var snapped,
                            out var snapError))
                    {
                        var coordinateCorrection = Math.Abs(
                            BloodBoundCoordinate(in snapped, eventBound) - rawCoordinate);
                        violations.Add(
                            $"bound entry {eventBound} rawCoordinate={rawCoordinate.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"correction={coordinateCorrection.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"maxCorrection={snapError.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"allowed={allowedEventCorrection.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"slope={eventSlope.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"at offset {eventTime:R}");
                        break;
                    }

                    final = snapped;
                    acceptedStages[acceptedStages.Length - 1] = final;
                    var heldCorrection = 0.0;
                    foreach (var heldBound in HeldBounds(bounds))
                        heldCorrection = Math.Max(heldCorrection, Math.Abs(
                            BloodBoundCoordinate(in snapped, heldBound) -
                            BloodBoundCoordinate(in rawEventState, heldBound)));
                    if (!IsFiniteNumber(heldCorrection) ||
                        heldCorrection > ActiveManifoldCorrectionTolerance)
                    {
                        var coordinateCorrection = Math.Abs(
                            BloodBoundCoordinate(in snapped, eventBound) - rawCoordinate);
                        violations.Add(
                            $"bound entry {eventBound} rawCoordinate={rawCoordinate.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"correction={coordinateCorrection.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"allowed={allowedEventCorrection.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"slope={eventSlope.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"heldCorrection={heldCorrection.ToString("R", CultureInfo.InvariantCulture)} " +
                            $"exceeded {ActiveManifoldCorrectionTolerance:R}");
                        break;
                    }
                    boundStageViolations.Clear();
                    CheckStageValidity(final, acceptedStages.Length - 1, settings,
                        bounds | eventBound, boundStageViolations, boundStageViolations);
                    if (boundStageViolations.Count != 0)
                    {
                        violations.AddRange(boundStageViolations);
                        break;
                    }
                    var projectedResidual =
                        completed.RecomputeResidual(acceptedStages);
                    var maxResidual = Math.Max(completed.MaxScaledResidual, projectedResidual);
                    if (!IsFiniteNumber(projectedResidual) ||
                        maxResidual > 10.0 * ScaledResidualTolerance)
                    {
                        violations.Add(
                            $"bound entry residual {projectedResidual:R} " +
                            $"outside {10.0 * ScaledResidualTolerance:R} at offset {eventTime:R}");
                        break;
                    }
                    completed = completed with { MaxScaledResidual = maxResidual };
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
                span = remaining;
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

            var whole = CompleteSegmentPressure(in state, solve, span, gz, settings);
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
            segments.Add(new IntegrationSegment(offset, span, state, whole.Final,
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
            offset += span;
            remaining -= span;
            span = remaining;
        }

        var fullyAdvanced = converged && remaining <= 0.0 && violations.Count == 0 &&
                            crossings.Count == 0;
        var result = new IntegrationResult(initial, state, allStages.ToArray(), iterations,
            maxScaledResidual, fullyAdvanced, crossings.ToArray(), violations.ToArray(),
            pressureEntries.ToArray(), pressureReleases.ToArray(), coreEntries.ToArray(),
            coreReleases.ToArray(), headEntries.ToArray(), headReleases.ToArray(),
            lowerEntries.ToArray(), lowerReleases.ToArray(), modeTransitions.ToArray(),
            segments.ToArray(), events.ToArray());
        ReleaseDiagnosticsList(crossings);
        ReleaseDiagnosticsList(violations);
        ReleaseDiagnosticsList(boundStageViolations);
        return result;
    }

    private enum IntervalEventKind
    {
        None,
        BoundEntry,
        BoundRelease,
        ModeTransition
    }

    [MethodImpl(AggressiveOptimization)]
    private static IEnumerable<BloodBounds> HeldBounds(BloodBounds bounds)
    {
        if ((bounds & BloodBounds.Core) != 0) yield return BloodBounds.Core;
        if ((bounds & BloodBounds.Head) != 0) yield return BloodBounds.Head;
        if ((bounds & BloodBounds.Lower) != 0) yield return BloodBounds.Lower;
    }

    [MethodImpl(AggressiveOptimization)]
    private static double BoundInwardDrive(BloodBounds bound, in IntegrationState state,
        double gx, double gy, double gz, LogicSettings settings, double[] rateBuffer)
    {
        if (bound == BloodBounds.Core)
            return HeldCoreDrive(state.HeartRateMultiplier,
                AdjustedShiftRate(in state, gx, gy, gz, settings), settings);
        EvaluateCirculation(in state, gx, gy, gz, settings, rateBuffer, null, false);
        return bound == BloodBounds.Head ? rateBuffer[0] : rateBuffer[1];
    }

    [MethodImpl(AggressiveOptimization)]
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

    /// <summary>
    ///     Measures the collocation defect |z' - f(z)|, scaled to state units by
    ///     the span, at the Gauss-Legendre probes of a converged solve. When the
    ///     defect exceeds <see cref="ResolutionDefectTolerance" />, returns a
    ///     smaller span sized from the defect's cubic scaling so the caller can
    ///     re-solve a shorter prefix; refinement follows the solved trajectory
    ///     rather than a fixed substep size.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private static bool TryResolutionSplit(in IntegrationState state,
        SegmentSolve solve, double span, BloodBounds bounds, double gx, double gy,
        double gz, LogicSettings settings, out double resolutionSpan)
    {
        var coefficientSets = new[]
        {
            solve.HeadCoefficients, solve.LowerCoefficients, solve.RateCoefficients,
            solve.CardioCoefficients
        };
        // Cheap screen before probing: the defect vanishes at the collocation
        // nodes, so a span whose cubic slope barely swings cannot defect more
        // than the budget. Near-equilibrium solves skip the evaluations.
        var slopeSwing = 0.0;
        foreach (var coefficients in coefficientSets)
        {
            var swing = Math.Abs(2.0 * coefficients[2] * span +
                                 3.0 * coefficients[3] * span * span);
            if (coefficients[3] != 0.0)
            {
                var vertex = -coefficients[2] / (3.0 * coefficients[3]);
                if (vertex > 0.0 && vertex < span)
                    swing = Math.Max(swing, Math.Abs(
                        coefficients[2] * coefficients[2] /
                        (3.0 * coefficients[3])));
            }

            slopeSwing = Math.Max(slopeSwing, swing);
        }

        if (slopeSwing * span <= ResolutionDefectTolerance * 0.5)
        {
            resolutionSpan = 0.0;
            return false;
        }

        var rateBuffer = new double[CirculationDimensions];
        var maxDefect = 0.0;
        var sawInvalid = false;
        foreach (var fraction in NumericalMath.GaussLegendre16Nodes)
        {
            var t = fraction * span;
            var dense = DenseSegmentState(in state, solve, t, gx, gz, settings);
            if (bounds == BloodBounds.None)
                EvaluateCirculation(in dense, gx, gy, gz, settings, rateBuffer, null,
                    false);
            else
                EvaluateBoundConstrainedCirculation(in dense, gx, gy, gz, settings,
                    rateBuffer, null, false, bounds);
            for (var coordinate = 0; coordinate < CirculationSolvedDimensions;
                 coordinate++)
            {
                var coefficients = coefficientSets[coordinate];
                var derivative = coefficients[1] +
                    (2.0 * coefficients[2] + 3.0 * coefficients[3] * t) * t;
                var defect = Math.Abs(derivative - rateBuffer[coordinate]) * span;
                if (!IsFiniteNumber(defect))
                {
                    sawInvalid = true;
                    continue;
                }

                maxDefect = Math.Max(maxDefect, defect);
            }
        }

        if (maxDefect <= ResolutionDefectTolerance && !sawInvalid)
        {
            resolutionSpan = 0.0;
            return false;
        }

        resolutionSpan = maxDefect > 0.0
            ? span * Clamp(
                Math.Pow(ResolutionDefectTolerance / maxDefect, 1.0 / 3.0), 0.25, 0.95)
            : span * 0.5;
        return true;
    }

    /// <summary>
    ///     Same defect gate as <see cref="TryResolutionSplit" /> for the
    ///     downstream consciousness solve: compares the piece's derivative to
    ///     the state-dependent affine right-hand side at the Gauss-Legendre
    ///     probes and returns a defect-sized shorter span when exceeded.
    /// </summary>
    [MethodImpl(AggressiveOptimization)]
    private static bool TryDownstreamResolutionSplit(in IntegrationState state,
        IntervalUpstream upstream, DownstreamSolution piece, double cursor,
        double span, DownstreamHold hold, LogicSettings settings,
        out double resolutionSpan)
    {
        // Cheap screen before probing, as in TryResolutionSplit: a piece whose
        // derivative barely swings cannot defect beyond the budget.
        var derivativeStart = piece.Consciousness.Derivative(0.0)[0];
        var derivativeMid = piece.Consciousness.Derivative(0.5)[0];
        var derivativeEnd = piece.Consciousness.Derivative(1.0)[0];
        var slopeSwing = Math.Max(
            Math.Abs(derivativeEnd - derivativeStart),
            Math.Max(Math.Abs(derivativeMid - derivativeStart),
                     Math.Abs(derivativeEnd - derivativeMid)));
        if (slopeSwing * span <= ResolutionDefectTolerance * 0.5)
        {
            resolutionSpan = 0.0;
            return false;
        }

        [MethodImpl(AggressiveOptimization)]
        IntervalTrialState TargetTrialAt(double c) =>
            upstream.EvaluateHeadPressure(cursor + c * span);
        [MethodImpl(AggressiveOptimization)]
        double HeadO2At(double c) => piece.Oxygen.EvaluateFirst(c);
        var dead = state.IsDead;
        var maxDefect = 0.0;
        var sawInvalid = false;
        foreach (var fraction in DownstreamDefectProbes)
        {
            var consciousness = piece.Consciousness.Evaluate(fraction)[0];
            var derivative = piece.Consciousness.Derivative(fraction)[0];
            var rhs = 0.0;
            if (!dead)
            {
                var trial = TargetTrialAt(fraction);
                var (target, lossTau) = ConsciousnessTargetAndTau(
                    O2Normalized(HeadO2At(fraction), settings),
                    PerfusionNormalized(trial.Head, settings), trial.Pressure,
                    settings);
                var tau = hold.ConsciousnessLosing
                    ? lossTau
                    : settings.ConsciousnessRecoveryTau;
                if (tau <= 1e-9) continue;
                rhs = (target - consciousness) / tau;
            }
            var defect = Math.Abs(derivative - rhs) * span;
            if (!IsFiniteNumber(defect))
            {
                sawInvalid = true;
                continue;
            }

            maxDefect = Math.Max(maxDefect, defect);
        }

        if (maxDefect <= ResolutionDefectTolerance && !sawInvalid)
        {
            resolutionSpan = 0.0;
            return false;
        }

        resolutionSpan = maxDefect > 0.0
            ? span * Clamp(
                Math.Pow(ResolutionDefectTolerance / maxDefect, 1.0 / 3.0), 0.25, 0.95)
            : span * 0.5;
        return true;
    }

    [MethodImpl(AggressiveOptimization)]
    private static (double Time, double HiBound)? EarliestBoundRelease(
        BloodBounds bound, in IntegrationState initial, SegmentSolve solve,
        double duration, double gx, double gy, double gz, LogicSettings settings)
    {
        var rateBuffer = new double[CirculationDimensions];
        var previousTime = 0.0;
        var previousDrive = BoundInwardDrive(bound, in initial, gx, gy, gz, settings,
            rateBuffer);
        foreach (var fraction in BoundReleaseScanFractions)
        {
            var t = fraction * duration;
            var dense = DenseSegmentState(in initial, solve, t, gx, gz, settings);
            var drive = BoundInwardDrive(bound, in dense, gx, gy, gz, settings,
                rateBuffer);
            if (previousDrive <= 0.0 && drive > 0.0)
                return (previousTime, t);
            previousTime = t;
            previousDrive = drive;
        }

        return null;
    }

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
    private static double ModeCoordinateValue(in IntegrationState state, int coordinate) =>
        coordinate switch
        {
            0 => state.BloodHead,
            2 => state.HeartRateMultiplier,
            _ => state.CardioFatigue
        };

    [MethodImpl(AggressiveOptimization)]
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

    private const double ActiveManifoldCorrectionTolerance = 1e-12;

    [MethodImpl(AggressiveOptimization)]
    private static double BloodBoundCoordinate(in IntegrationState state, BloodBounds bound) =>
        bound switch
        {
            BloodBounds.Head => state.BloodHead,
            BloodBounds.Lower => state.BloodLower,
            BloodBounds.Core => state.BloodCore,
            _ => double.NaN
        };

    [MethodImpl(AggressiveOptimization)]
    private static bool TryProjectActiveBloodState(in IntegrationState state,
        BloodBounds bounds, LogicSettings settings, double maximumAllowedCorrection,
        out IntegrationState projected, out double maximumCorrection)
    {
        projected = state;
        maximumCorrection = double.PositiveInfinity;
        if (!IsFiniteNumber(maximumAllowedCorrection) ||
            maximumAllowedCorrection < 0.0)
            return false;
        if (!IsFiniteNumber(state.BloodHead) || !IsFiniteNumber(state.BloodLower) ||
            !IsFiniteNumber(state.BloodCore))
            return false;

        var head = (bounds & BloodBounds.Head) != 0
            ? settings.MinHeadBloodFraction
            : state.BloodHead;
        var lower = (bounds & BloodBounds.Lower) != 0 ? 0.0 : state.BloodLower;
        if ((bounds & BloodBounds.Core) != 0)
        {
            if ((bounds & BloodBounds.Lower) != 0)
            {
                if ((bounds & BloodBounds.Head) != 0 || head != 1.0) return false;
                lower = 0.0;
            }
            else
            {
                lower = 1.0 - head;
                var core = 1.0 - head - lower;
                if (core < 0.0)
                {
                    if (lower <= 0.0) return false;
                    lower = BitConverter.Int64BitsToDouble(
                        BitConverter.DoubleToInt64Bits(lower) - 1L);
                    core = 1.0 - head - lower;
                    if (core < 0.0) return false;
                }
                else if (core > 0.0 && lower > 0.0)
                {
                    var nextLower = BitConverter.Int64BitsToDouble(
                        BitConverter.DoubleToInt64Bits(lower) + 1L);
                    var nextCore = 1.0 - head - nextLower;
                    if (nextCore >= 0.0 && nextCore < core)
                    {
                        lower = nextLower;
                        core = nextCore;
                    }
                }
            }
        }
        if ((bounds & BloodBounds.Head) == 0 &&
            head < settings.MinHeadBloodFraction &&
            head >= settings.MinHeadBloodFraction - 1e-12)
            head = settings.MinHeadBloodFraction;
        if ((bounds & BloodBounds.Lower) == 0 &&
            lower < 0.0 && lower >= -1e-12)
            lower = 0.0;
        if ((bounds & BloodBounds.Core) == 0)
        {
            var residualCore = 1.0 - head - lower;
            if (residualCore < 0.0 && residualCore >= -1e-12 && lower > 0.0)
                lower += residualCore;
        }

        projected = state with { BloodHead = head, BloodLower = lower };
        var coreVolume = projected.BloodCore;
        maximumCorrection = Math.Max(Math.Abs(head - state.BloodHead),
            Math.Max(Math.Abs(lower - state.BloodLower),
                Math.Abs(coreVolume - state.BloodCore)));
        var bloodSumError = Math.Abs(head + coreVolume + lower - 1.0);
        return IsFiniteNumber(maximumCorrection) &&
               maximumCorrection <= maximumAllowedCorrection &&
               IsFiniteNumber(bloodSumError) &&
               bloodSumError <= ActiveManifoldCorrectionTolerance &&
               head >= settings.MinHeadBloodFraction && head <= 1.0 &&
               lower >= 0.0 && lower <= 1.0 &&
               coreVolume >= 0.0 && coreVolume <= 1.0;
    }

    [MethodImpl(AggressiveOptimization)]
    private static SegmentSolve AdvanceCirculationSegment(
        in IntegrationState initial,
        double dt,
        double gx,
        double gy,
        double gz,
        LogicSettings settings,
        BloodBounds bounds, bool? headwardOverride = null)
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
        var generalViolations = AcquireDiagnosticsList();
        var boundViolations = AcquireDiagnosticsList();
        var boundFlags = BloodBounds.None;
        var initialState = initial;
        var independentStageStates = new IntegrationState[stageCount];
        for (var stage = 0; stage < stageCount; stage++)
            independentStageStates[stage] = IndependentCirculationStateAt(in initialState,
                NumericalMath.RadauC[stage] * dt, gx, gz, settings);

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
        var maxScaledResidual = ComputeResidual(stages);
        var converged = false;

        while (iterations < MaxNewtonIterations)
        {
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
                var trialResidual = ComputeResidual(trialStages);
                if (trialResidual < maxScaledResidual)
                {
                    (stages, trialStages) = (trialStages, stages);
                    maxScaledResidual = trialResidual;
                    accepted = true;
                    break;
                }

                lambda *= 0.5;
            }

            if (!accepted) break;
            iterations++;
        }

        converged = IsFiniteNumber(maxScaledResidual) && maxScaledResidual <= ScaledResidualTolerance;

        if (bounds != BloodBounds.None)
        {
            for (var stage = 0; stage < stageCount; stage++)
            {
                var stageState = StageState(stages, stage);
                if (!TryProjectActiveBloodState(in stageState, bounds, settings,
                        ActiveManifoldCorrectionTolerance, out var projected,
                        out var correction))
                {
                    generalViolations.Add(
                        $"held stage {stage + 1} blood correction {correction:R} " +
                        $"outside active manifold tolerance " +
                        $"{ActiveManifoldCorrectionTolerance:R}");
                    continue;
                }
                stages[stage * dimensions] = projected.BloodHead;
                stages[stage * dimensions + 1] = projected.BloodLower;
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

        List<string> crossings = AcquireDiagnosticsList();
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
        var solve = new SegmentSolve(stageStates[stageCount - 1], stageStates, iterations,
            maxScaledResidual, converged, crossings.ToArray(), violations,
            [], [], headCoefficients,
            lowerCoefficients, rateCoefficients, cardioCoefficients, rootSearchValid,
            boundFlags, RecomputeResidual);
        ReleaseDiagnosticsList(generalViolations);
        ReleaseDiagnosticsList(boundViolations);
        ReleaseDiagnosticsList(crossings);
        return solve;

        [MethodImpl(AggressiveOptimization)]
        CirculationMode Evaluate(in IntegrationState candidate, double[] rates,
            double[][]? jacobian) =>
            bounds == BloodBounds.None
                ? EvaluateCirculation(in candidate, gx, gy, gz, settings, rates, jacobian,
                    false, headwardOverride)
                : EvaluateBoundConstrainedCirculation(in candidate, gx, gy, gz, settings,
                    rates, jacobian, false, bounds);

        [MethodImpl(AggressiveOptimization)]
        IntegrationState StageState(double[] vector, int stage)
        {
            return independentStageStates[stage] with
            {
                BloodHead = vector[stage * dimensions],
                BloodLower = vector[stage * dimensions + 1],
                HeartRateMultiplier = vector[stage * dimensions + 2],
                CardioFatigue = vector[stage * dimensions + 3],
                CerebralPressureImpairment = initialPressure
            };
        }

        [MethodImpl(AggressiveOptimization)]
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

        [MethodImpl(AggressiveOptimization)]
        double RecomputeResidual(IntegrationState[] acceptedStages)
        {
            var vector = new double[unknowns];
            for (var stage = 0; stage < stageCount; stage++)
            {
                var offset = stage * dimensions;
                vector[offset] = acceptedStages[stage].BloodHead;
                vector[offset + 1] = acceptedStages[stage].BloodLower;
                vector[offset + 2] = acceptedStages[stage].HeartRateMultiplier;
                vector[offset + 3] = acceptedStages[stage].CardioFatigue;
            }
            return ComputeResidual(vector);
        }
    }
#pragma warning restore CA1822

    [MethodImpl(AggressiveOptimization)]
    private static SegmentSolve CompleteSegmentPressure(
        in IntegrationState initial,
        SegmentSolve bloodSolve,
        double dt,
        double gz,
        LogicSettings settings)
    {
        var diagnostics = AcquireDiagnosticsList();
        diagnostics.AddRange(bloodSolve.StateViolations);
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

        var completed = bloodSolve with
        {
            Final = stageStates[stageStates.Length - 1],
            Stages = stageStates,
            StateViolations = diagnostics.ToArray(),
            PressureBoundEntries = pressureBoundEntries,
            PressureBoundReleases = pressureBoundReleases,
            CoreRootSearchValid = false
        };
        ReleaseDiagnosticsList(diagnostics);
        return completed;
    }

    [MethodImpl(AggressiveOptimization)]
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
                head, "head", "below head floor");
            CheckBoundResidual(BloodBounds.Lower, lower,
                lower, "lower", "negative");
            CheckBoundResidual(BloodBounds.Core, core,
                core, "core", "negative");

            [MethodImpl(AggressiveOptimization)]
            void CheckBoundResidual(BloodBounds bound, double residual, double value,
                string metric, string condition)
            {
                const double limit = -1e-12;
                if (residual >= limit) return;
                boundFlags |= bound;
                ((bounds & bound) != 0 ? generalDiagnostics : boundDiagnostics).Add(
                    $"dense {metric} blood {value:R} {condition} at offset {offset:R}");
            }
        }

        return boundFlags;
    }

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
    private static List<double> MonotoneBounds(double[] coefficients, double duration)
    {
        return MonotoneBounds(coefficients, 0.0, duration);
    }

    [MethodImpl(AggressiveOptimization)]
    private static List<double> MonotoneBounds(double[] coefficients, double start,
        double end)
    {
        var criticalBuffer = new double[2];
        var criticalCount = NumericalMath.QuadraticRootsInInterval(
            3.0 * coefficients[3], 2.0 * coefficients[2], coefficients[1],
            start, end, criticalBuffer);
        var bounds = new List<double>(criticalCount + 2) { start };
        for (var root = 0; root < criticalCount; root++) bounds.Add(criticalBuffer[root]);
        bounds.Add(end);
        return bounds;
    }

    [MethodImpl(AggressiveOptimization)]
    internal static bool TryHeadRange(IntegrationSegment[] segments, double from,
        double to, out double minimum, out double maximum)
    {
        minimum = double.PositiveInfinity;
        maximum = double.NegativeInfinity;
        if (!IsFiniteNumber(from) || !IsFiniteNumber(to) || to < from ||
            segments.Length == 0)
            return false;

        var coveredTo = from;
        var found = false;
        foreach (var segment in segments)
        {
            var segmentStart = segment.StartOffset;
            var segmentEnd = segmentStart + segment.Duration;
            if (!IsFiniteNumber(segmentStart) || !IsFiniteNumber(segmentEnd) ||
                !IsFiniteNumber(segment.CoefficientStart) ||
                !IsFiniteNumber(segment.Duration) || segment.Duration < 0.0)
                return false;
            if (segmentEnd < from || segmentStart > to) continue;

            var overlapStart = Math.Max(from, segmentStart);
            var overlapEnd = Math.Min(to, segmentEnd);
            if (overlapEnd < overlapStart) continue;
            if (overlapStart != coveredTo) return false;

            var localStart = overlapStart - segment.CoefficientStart;
            var localEnd = overlapEnd - segment.CoefficientStart;
            if (!IsFiniteNumber(localStart) || !IsFiniteNumber(localEnd) ||
                localEnd < localStart || segment.HeadCoefficients.Length < 4)
                return false;
            for (var coefficient = 0; coefficient < 4; coefficient++)
                if (!IsFiniteNumber(segment.HeadCoefficients[coefficient]))
                    return false;

            foreach (var localTime in MonotoneBounds(segment.HeadCoefficients,
                         localStart, localEnd))
            {
                var head = NumericalMath.EvaluateCubic(
                    segment.HeadCoefficients, localTime);
                if (!IsFiniteNumber(head) || head < 0.0 || head > 1.0)
                    return false;
                if (head < minimum) minimum = head;
                if (head > maximum) maximum = head;
            }
            coveredTo = overlapEnd;
            found = true;
        }

        return found && coveredTo == to &&
               IsFiniteNumber(minimum) && IsFiniteNumber(maximum);
    }

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
    private static double BoundResidual(in IntegrationState state, BloodBounds bound,
        LogicSettings settings) => bound switch
        {
            BloodBounds.Core => state.BloodCore,
            BloodBounds.Head => state.BloodHead - settings.MinHeadBloodFraction,
            _ => state.BloodLower
        };

    [MethodImpl(AggressiveOptimization)]
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

        // A tangential contact never produces a violating trial: every prefix solve ends
        // in-bound and upperBound stays unset. The last in-bound endpoint is then the
        // correct entry point; accepting it pins the coordinate to the bound it grazes.
        if (!localized && best != null && bestResidual <= 1e-10 &&
            !IsFiniteNumber(upperBound))
            localized = true;

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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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
        var initialState = state;
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
        var nextUnilateralProbe = 0;

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
        var firstUnilateralCorrectionAttempted = false;

        [MethodImpl(AggressiveOptimization)]
        bool TryUnilateralCorrection(bool requireMonotone, out double time,
            out SegmentSolve? result)
        {
            time = 0.0;
            result = null;
            var downward = side > 0;
            var eventTime = bestTime - lowerResidual / bestSlope;
            for (var probeIndex = 0; probeIndex < 12 &&
                 eventTime > bestTime && eventTime < duration; probeIndex++)
            {
                var unilateral = AdvanceCirculationSegment(in initialState, eventTime,
                    gx, gy, gz, settings, bounds, headwardOverride: downward);
                var unilateralValid = unilateral.Converged &&
                    unilateral.StateViolations.Length == 0 &&
                    unilateral.ModeCrossings.Length == 0;
                if (!unilateralValid)
                    break;
                var headError = unilateral.Final.BloodHead - level;
                var headCoefficients = unilateral.HeadCoefficients;
                var slope = headCoefficients[1] +
                    2.0 * headCoefficients[2] * eventTime +
                    3.0 * headCoefficients[3] * eventTime * eventTime;
                if (!IsFiniteNumber(headError) || !IsFiniteNumber(slope) ||
                    (downward ? slope >= 0.0 : slope <= 0.0))
                    break;
                if (requireMonotone)
                {
                    var denseEnd = NumericalMath.EvaluateCubic(
                        headCoefficients, eventTime);
                    var monotone = IsFiniteNumber(headCoefficients[1]) &&
                        (downward
                            ? headCoefficients[1] < 0.0
                            : headCoefficients[1] > 0.0) &&
                        IsFiniteNumber(slope) && IsFiniteNumber(denseEnd) &&
                        (downward ? denseEnd >= level : denseEnd <= level);
                    if (monotone && (downward
                            ? headCoefficients[3] < 0.0
                            : headCoefficients[3] > 0.0))
                    {
                        var vertex = -headCoefficients[2] /
                                     (3.0 * headCoefficients[3]);
                        if (vertex > 0.0 && vertex < eventTime)
                        {
                            var vertexSlope = headCoefficients[1] +
                                2.0 * headCoefficients[2] * vertex +
                                3.0 * headCoefficients[3] * vertex * vertex;
                            monotone = IsFiniteNumber(vertexSlope) &&
                                       (downward ? vertexSlope < 0.0 : vertexSlope > 0.0);
                        }
                    }

                    if (!monotone) return false;
                }

                var allowance =
                    Math.Abs(slope) * PressureBoundRootToleranceSeconds + 1e-12;
                if (Math.Abs(headError) <= allowance &&
                    unilateral.Stages.All(stage => downward
                        ? stage.BloodHead >= level - allowance
                        : stage.BloodHead <= level + allowance))
                {
                    time = eventTime;
                    result = unilateral;
                    return true;
                }
                if (downward ? headError <= 0.0 : headError >= 0.0) break;
                eventTime -= headError / slope;
            }

            return false;
        }

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
                var gap = candidate - lowerBound;
                var terminalGap = gap <= 1e-12;
                // A failed trial essentially on the lower bound carries no
                // bracket information (the solve straddles the discontinuity
                // itself); recording it would collapse the searchable bracket
                // onto numerical noise.
                if (!terminalGap && (!IsFiniteNumber(invalidBound) ||
                        candidate < invalidBound))
                    invalidBound = candidate;
                var unilateralCorrectionEligible =
                    coordinate == 0 && level == settings.RestingBloodHead &&
                    bounds == BloodBounds.None && best != null &&
                    bestSlope * side < 0.0;
                var earlyUnilateralThreshold = nextUnilateralProbe switch
                {
                    0 => 1e-4,
                    1 => 1e-6,
                    2 => PressureBoundRootToleranceSeconds,
                    _ => double.PositiveInfinity
                };
                var earlyUnilateralAttempt =
                    unilateralCorrectionEligible && nextUnilateralProbe < 3 &&
                    gap <= earlyUnilateralThreshold && !terminalGap;
                if (earlyUnilateralAttempt) nextUnilateralProbe++;
                if (earlyUnilateralAttempt ||
                    unilateralCorrectionEligible && terminalGap)
                {
                    if (TryUnilateralCorrection(false, out var correctionTime,
                            out var correction))
                    {
                        transitionTime = correctionTime;
                        prefix = correction;
                        return true;
                    }
                }

                if (terminalGap)
                {
                    var remainingHi = IsFiniteNumber(upperBound) &&
                                      IsFiniteNumber(invalidBound)
                        ? Math.Min(upperBound, invalidBound)
                        : IsFiniteNumber(upperBound) ? upperBound : invalidBound;
                    if (IsFiniteNumber(remainingHi) &&
                        remainingHi - lowerBound > 1e-12)
                    {
                        // The failed trial landed on the lower bound but the
                        // converged bracket above it is still resolvable: keep
                        // bisecting at its midpoint instead of abandoning the
                        // localization.
                        rootCandidate = lowerBound +
                                        (remainingHi - lowerBound) * 0.5;
                        continue;
                    }

                    var invalidFinal = trial.Final;
                    var bestFinal = best?.Final ?? default;
                    diagnostics.Add(
                        $"mode transition trial {candidate:R}s invalid: " +
                        $"converged {trial.Converged}, violations " +
                        $"[{string.Join("; ", trial.StateViolations)}] " +
                        $"crossings [{string.Join("; ", trial.ModeCrossings)}] " +
                        $"endcoord {ModeCoordinateValue(in invalidFinal, coordinate):R}; " +
                        $"coordinate={coordinate}, level={level:R}, " +
                        $"lower={lowerBound:R}, upper={upperBound:R}, " +
                        $"invalid={invalidBound:R}, best={bestTime:R}, " +
                        $"lowerResidual={lowerResidual:R}, bestSlope={bestSlope:R}, " +
                        $"bestEnd={(best == null ? double.NaN : ModeCoordinateValue(in bestFinal, coordinate)):R}, " +
                        $"trialResidual={trial.MaxScaledResidual:R}, " +
                        $"bestResidual={(best == null ? double.NaN : best.MaxScaledResidual):R}");
                    transitionTime = 0.0;
                    prefix = null;
                    return false;
                }

                continue;
            }

            var trialFinal = trial.Final;
            var endResidual = ModeCoordinateValue(in trialFinal, coordinate) - level;
            if (trial.ModeCrossings.Length == 0 && Math.Sign(endResidual) == side)
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
                var unilateralCorrectionEligible =
                    coordinate == 0 && level == settings.RestingBloodHead &&
                    bounds == BloodBounds.None && bestSlope * side < 0.0;
                if (!firstUnilateralCorrectionAttempted && unilateralCorrectionEligible)
                {
                    firstUnilateralCorrectionAttempted = true;
                    if (TryUnilateralCorrection(true, out var correctionTime,
                            out var correction))
                    {
                        transitionTime = correctionTime;
                        prefix = correction;
                        return true;
                    }
                }

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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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

    [MethodImpl(AggressiveOptimization)]
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
        return NumericalMath.ExponentialConvolution(pressureStart, from, to, decayRate,
            (headCoefficients, gz, settings, diagnostics),
            static (s, offset) => PressureSource(
                EvaluateDenseHead(s.headCoefficients, offset, s.diagnostics),
                s.gz, s.settings));
    }

    [MethodImpl(AggressiveOptimization)]
    private static double EvaluateDenseHead(double[] headCoefficients, double offset,
        List<string> diagnostics)
    {
        var head = NumericalMath.EvaluateCubic(headCoefficients, offset);
        if (!IsFiniteNumber(head) || head < 0.0 || head > 1.0)
            diagnostics.Add($"dense head blood {head:R} out of bounds at offset {offset:R}");
        return head;
    }

    [MethodImpl(AggressiveOptimization)]
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
            "BloodHead", state.BloodHead, "below head floor");
        ReportBound(BloodBounds.Lower, state.BloodLower,
            "BloodLower", state.BloodLower, "below zero");
        ReportBound(BloodBounds.Core, state.BloodCore,
            "BloodCore", state.BloodCore, "below zero");
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

        [MethodImpl(AggressiveOptimization)]
        void ReportBound(BloodBounds bound, double residual, string metric,
            double value, string condition)
        {
            const double limit = -1e-12;
            if (residual >= limit) return;
            boundFlags |= bound;
            ((bounds & bound) != 0 ? generalViolations : boundViolations).Add(
                $"stage {stage + 1}: {metric} {value:R} {condition}");
        }
    }

    [MethodImpl(AggressiveOptimization)]
    private static bool IsFiniteNumber(double value) => NumericalMath.IsFinite(value);

    #endregion

}
