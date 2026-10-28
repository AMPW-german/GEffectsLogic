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
    private sealed class IntervalUpstream
    {
        private readonly IntegrationSegment[] segments;
        private readonly bool zeroPressureSource;
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
        private readonly double[]? pressureSourceConstants;
        private readonly double pressureRestLogistic;
        private readonly double pressureNegativeGzSource;
        private int segmentHint;

        [MethodImpl(AggressiveOptimization)]
        internal IntervalUpstream(IntegrationResult circulation, in IntegrationState initial,
            double gx, double gy, double gz, LogicSettings settings)
        {
            evaluateCache = pooledEvaluateCache ?? new(128);
            pooledEvaluateCache = null;
            evaluateCache.Clear();
            pressureCache = pooledPressureCache ?? new(128);
            pooledPressureCache = null;
            pressureCache.Clear();
            this.settings = settings;
            this.gx = gx;
            this.gz = gz;
            pressureDecayRate =
                1.0 / Math.Max(settings.CerebralPressureImpairmentRecoveryTau, 1e-9);
            segments = circulation.Segments;
            zeroPressureSource = segments.Length > 0 &&
                PressureSource(0.0, gz, settings) == 0.0 &&
                PhysiologicalModel.TryHeadRange(segments, segments[0].StartOffset,
                    segments[segments.Length - 1].StartOffset +
                    segments[segments.Length - 1].Duration, out _,
                    out var maximumHead) &&
                maximumHead <= settings.RestingBloodHead;
            // Constant-source certificate: the overfill sigmoid is evaluated at
            // the certified head-range extremes; when both ends round to the
            // same sigmoid the source is constant over that range and the
            // free-pressure convolution collapses to a closed form that
            // reproduces the quadrature accumulation bit-for-bit.
            pressureNegativeGzSource =
                settings.CerebralPressureImpairmentNegativeGzRate *
                Math.Min(Math.Max(0.0, -gz),
                    settings.CerebralPressureImpairmentNegativeGzCap);
            pressureRestLogistic =
                settings.CerebralPressureImpairmentMaxBuildRate *
                NumericalMath.Sigmoid(
                    -settings.CerebralPressureImpairmentExponent *
                    settings.CerebralPressureImpairmentMidOverfill);
            pressureSourceConstants = zeroPressureSource
                ? null
                : new double[segments.Length];
            if (pressureSourceConstants != null)
                for (var index = 0; index < segments.Length; index++)
                    pressureSourceConstants[index] =
                        SegmentPressureSourceConstant(index);
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

        [MethodImpl(AggressiveOptimization)]
        private IntegrationState IndependentAt(double t) =>
            IndependentCirculationStateAt(callerInitial, t, gx, gz, settings);

        [MethodImpl(AggressiveOptimization)]
        internal double StrainLevelAt(double t) => IndependentAt(t).StrainingLevel;
        [MethodImpl(AggressiveOptimization)]
        internal double StrainFatigueAt(double t) => IndependentAt(t).StrainingFatigue;
        [MethodImpl(AggressiveOptimization)]
        internal double SuitFatigueAt(double t) => IndependentAt(t).GSuitFatigue;
        [MethodImpl(AggressiveOptimization)]
        internal double RespiratoryAt(double t) => IndependentAt(t).RespiratoryFatigue;
        internal double RespiratoryBoundTime => respiratory.UpCrossing(1.0);
        [MethodImpl(AggressiveOptimization)]
        internal double ArterialAt(double t) => arterial.At(t);
        [MethodImpl(AggressiveOptimization)]
        internal double CompressionAt(double t) => compression.At(t);
        [MethodImpl(AggressiveOptimization)]
        internal double PainAt(double t) => pain.At(t);
        [MethodImpl(AggressiveOptimization)]
        internal double SuddenAt(double t) => Clamp(sudden.At(t), 0.0, 1.0);
        internal double SuddenBoundTime => sudden.UpCrossing(1.0);

        [MethodImpl(AggressiveOptimization)]
        internal double NeckAt(double t)
        {
            if (t == 0.0) return neckInitial;
            if (t >= neckClampTime) return neckCeiling;
            return neckEquilibrium +
                   (neckInitial - neckEquilibrium) * Math.Exp(-neckInverseTau * t);
        }

        private bool dwellPinned;
        [ThreadStatic]
        private static Dictionary<double, IntervalTrialState>? pooledEvaluateCache;
        [ThreadStatic]
        private static Dictionary<double, double>? pooledPressureCache;
        private readonly Dictionary<double, IntervalTrialState> evaluateCache;
        private readonly Dictionary<double, double> pressureCache;

        [MethodImpl(AggressiveOptimization)]
        internal void PinDwell() => dwellPinned = true;

        [MethodImpl(AggressiveOptimization)]
        internal double NeckRateAt(double t)
        {
            if (t >= neckClampTime) return 0.0;
            return neckInverseTau * (neckEquilibrium - NeckAt(t));
        }

        [MethodImpl(AggressiveOptimization)]
        internal double SuddenRateAt(double t) => sudden.RateAt(t);

        [MethodImpl(AggressiveOptimization)]
        internal double NeckDwellAt(double t)
        {
            if (dwellPinned) return 1.0;
            if (double.IsInfinity(neckDeathEntry)) return 0.0;
            if (t <= neckDeathEntry) return neckDeathEntry == 0.0 ? dwellBase : 0.0;
            if (t <= neckDeathExit)
                return Math.Min(dwellBase + (t - neckDeathEntry) / dwellDelay, 1.0);
            return 0.0;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double DeathCompletionTime()
        {
            if (double.IsInfinity(neckDeathEntry)) return double.PositiveInfinity;
            var completion = neckDeathEntry + (1.0 - dwellBase) * dwellDelay;
            return completion <= neckDeathExit ? completion : double.PositiveInfinity;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double SuddenTriggerTime(bool inSudden, bool dead)
        {
            if (inSudden || dead) return double.PositiveInfinity;
            if (sudden.At(0.0) > 0.8) return 0.0;
            return sudden.UpCrossing(0.8);
        }

        [MethodImpl(AggressiveOptimization)]
        internal double SuddenRecoveryTime(bool inSudden)
        {
            if (!inSudden) return double.PositiveInfinity;
            if (sudden.At(0.0) < 0.3) return 0.0;
            return sudden.DownCrossing(0.3);
        }

        internal HashSet<string> Diagnostics { get; } = [];

        internal (double Entry, double Release)[] PressureEpisodes => pressureEpisodes;

        [MethodImpl(AggressiveOptimization)]
        internal double HeadAt(double t)
        {
            var segment = segments[SegmentIndexAt(t)];
            return NumericalMath.EvaluateCubic(segment.HeadCoefficients,
                t - segment.CoefficientStart);
        }

        [MethodImpl(AggressiveOptimization)]
        internal bool TryHeadRange(double from, double to, out double minimum,
            out double maximum) =>
            PhysiologicalModel.TryHeadRange(segments, from, to, out minimum, out maximum);

        [MethodImpl(AggressiveOptimization)]
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

        [MethodImpl(AggressiveOptimization)]
        internal IntervalTrialState Evaluate(double t)
        {
            if (evaluateCache.TryGetValue(t, out var cached)) return cached;
            var segment = segments[SegmentIndexAt(t)];
            var local = t - segment.CoefficientStart;
            var result = new IntervalTrialState
            {
                Head = NumericalMath.EvaluateCubic(segment.HeadCoefficients,
                    local),
                Lower = NumericalMath.EvaluateCubic(segment.LowerCoefficients,
                    local),
                HeartRate = NumericalMath.EvaluateCubic(segment.RateCoefficients,
                    local),
                Straining = IndependentStrainingLevelAt(
                    callerInitial.StrainingLevel, t, gz, settings),
                Respiratory = IndependentRespiratoryAt(
                    callerInitial.RespiratoryFatigue, t, gx, settings),
                Pressure = PressureAt(t),
                Arterial = ArterialAt(t),
                Compression = CompressionAt(t)
            };
            evaluateCache[t] = result;
            return result;
        }

        // Consciousness-target evaluations read only head perfusion and the
        // cerebral pressure impairment; this lean variant skips the remaining
        // trial fields while producing the same Head and Pressure values as
        // Evaluate.
        [MethodImpl(AggressiveOptimization)]
        internal IntervalTrialState EvaluateHeadPressure(double t) =>
            new IntervalTrialState
            {
                Head = HeadAt(t),
                Pressure = PressureAt(t)
            };

        // Oxygen-system trials read only the transport fields (see
        // BuildOxygenSystem and LungEffectiveness); this lean variant skips
        // straining and pressure evaluations without writing partial states
        // into the full-state cache.
        [MethodImpl(AggressiveOptimization)]
        internal IntervalTrialState EvaluateOxygen(double t)
        {
            if (evaluateCache.TryGetValue(t, out var cached)) return cached;
            var segment = segments[SegmentIndexAt(t)];
            var local = t - segment.CoefficientStart;
            return new IntervalTrialState
            {
                Head = NumericalMath.EvaluateCubic(segment.HeadCoefficients, local),
                Lower = NumericalMath.EvaluateCubic(segment.LowerCoefficients, local),
                HeartRate = NumericalMath.EvaluateCubic(segment.RateCoefficients,
                    local),
                Respiratory = IndependentRespiratoryAt(
                    callerInitial.RespiratoryFatigue, t, gx, settings),
                Arterial = ArterialAt(t),
                Compression = CompressionAt(t)
            };
        }

        [MethodImpl(AggressiveOptimization)]
        private int SegmentIndexAt(double t)
        {
            var index = Math.Min(segmentHint, segments.Length - 1);
            while (index + 1 < segments.Length && segments[index + 1].StartOffset <= t) index++;
            while (index > 0 && segments[index].StartOffset > t) index--;
            segmentHint = index;
            return index;
        }

        [MethodImpl(AggressiveOptimization)]
        // Returns the constant PressureSource value shared by every head-blood
        // level inside [minimumHead, maximumHead], or NaN when the source can
        // vary. The overfill sigmoid is monotone in head blood, so if its
        // evaluations at the two range extremes are equal the sigmoid, and
        // therefore the whole source term, is constant over the range.
        private double PressureSourceConstantForRange(double minimumHead,
            double maximumHead)
        {
            var resting = settings.RestingBloodHead;
            var overfillMinimum =
                Math.Max((minimumHead - resting) / resting, 0.0);
            var overfillMaximum =
                Math.Max((maximumHead - resting) / resting, 0.0);
            var sigmoidMinimum = NumericalMath.Sigmoid(
                settings.CerebralPressureImpairmentExponent *
                (overfillMinimum - settings.CerebralPressureImpairmentMidOverfill));
            var sigmoidMaximum = NumericalMath.Sigmoid(
                settings.CerebralPressureImpairmentExponent *
                (overfillMaximum - settings.CerebralPressureImpairmentMidOverfill));
            return sigmoidMinimum == sigmoidMaximum
                ? Math.Max(settings.CerebralPressureImpairmentMaxBuildRate *
                           sigmoidMinimum - pressureRestLogistic, 0.0) +
                    pressureNegativeGzSource
                : double.NaN;
        }

        private double SegmentPressureSourceConstant(int index)
        {
            var segment = segments[index];
            return PhysiologicalModel.TryHeadRange(segments, segment.StartOffset,
                segment.StartOffset + segment.Duration, out var minimumHead,
                out var maximumHead)
                ? PressureSourceConstantForRange(minimumHead, maximumHead)
                : double.NaN;
        }

        // Closed form of ExponentialConvolution for a source that is provably
        // constant over the span: every node evaluates to the same source
        // value, so the quadrature sum is computed with identical products
        // and ordering while skipping the per-node offset and source work.
        private static double ConstantFreePressure(double pressureStart,
            double from, double to, double decayRate, double source)
        {
            var span = to - from;
            if (span <= 0.0) return pressureStart;
            var x = decayRate * span;
            var decay = Math.Exp(-x);
            var w = -NumericalMath.ExpMinusOne(-x);
            var mass = decayRate == 0.0 || x == 0.0 ? span : w / decayRate;
            var weights = NumericalMath.GaussLegendre16Weights;
            var sum = 0.0;
            for (var node = 0; node < weights.Length; node++)
                sum += weights[node] * source;
            return pressureStart * decay + mass * sum;
        }

        private double FreePressureSpan(double pressure, double from, double to)
        {
            var diagnostics = AcquireDiagnosticsList();
            var position = from;
            var value = pressure;
            while (position < to - 1e-15)
            {
                var index = SegmentIndexAt(position);
                var segment = segments[index];
                var spanEnd = Math.Min(segment.StartOffset + segment.Duration, to);
                if (zeroPressureSource)
                {
                    value = value * Math.Exp(-pressureDecayRate *
                        ((spanEnd - segment.CoefficientStart) -
                         (position - segment.CoefficientStart)));
                }
                else
                {
                    var constants = pressureSourceConstants!;
                    var constant = constants[index];
                    if (!IsFiniteNumber(constant) &&
                        PhysiologicalModel.TryHeadRange(segments, position,
                            spanEnd, out var spanMinimum, out var spanMaximum))
                    {
                        constant = PressureSourceConstantForRange(spanMinimum,
                            spanMaximum);
                    }
                    if (IsFiniteNumber(constant))
                    {
                        value = ConstantFreePressure(value,
                            position - segment.CoefficientStart,
                            spanEnd - segment.CoefficientStart,
                            pressureDecayRate, constant);
                    }
                    else
                    {
                        value = EvaluateFreePressure(value,
                            position - segment.CoefficientStart,
                            spanEnd - segment.CoefficientStart, pressureDecayRate,
                            segment.HeadCoefficients, gz, settings, diagnostics);
                    }
                }
                position = spanEnd;
            }
            foreach (var diagnostic in diagnostics) Diagnostics.Add(diagnostic);
            ReleaseDiagnosticsList(diagnostics);
            return value;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double PressureAt(double t)
        {
            if (pressureCache.TryGetValue(t, out var cached)) return cached;
            var result = ComputePressureAt(t);
            pressureCache[t] = result;
            return result;
        }

        [MethodImpl(AggressiveOptimization)]
        private double ComputePressureAt(double t)
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

        [MethodImpl(AggressiveOptimization)]
        internal void Release()
        {
            evaluateCache.Clear();
            pressureCache.Clear();
            pooledEvaluateCache = evaluateCache;
            pooledPressureCache = pressureCache;
        }
    }

}
