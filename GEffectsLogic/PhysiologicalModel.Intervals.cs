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
    internal readonly record struct IntegrationState(
        double BloodHead,
        double BloodLower,
        double HeartRateMultiplier,
        double CardioFatigue,
        double CerebralPressureImpairment,
        double RespiratoryFatigue)
    {
        internal double BloodCore => 1.0 - BloodHead - BloodLower;
        internal double StrainingLevel { get; init; }
        internal double StrainingFatigue { get; init; }
        internal double GSuitFatigue { get; init; }
        internal double BloodO2Head { get; init; }
        internal double BloodO2Core { get; init; }
        internal double BloodO2Lower { get; init; }
        internal double ArterialOxygenation { get; init; }
        internal double ConsciousnessLevel { get; init; }
        internal double LungCompressionLevel { get; init; }
        internal double PainLevel { get; init; }
        internal double GyNeckFatigue { get; init; }
        internal double GyNeckFatigueDeathDwell { get; init; }
        internal double SuddenLoCAccumulator { get; init; }
        internal double VisualGrayscaleLevel { get; init; }
        internal double VisualTunnelVisionLevel { get; init; }
        internal double VisualRedoutLevel { get; init; }
        internal double VisualLoCLevel { get; init; }
        internal bool IsUnconscious { get; init; }
        internal bool InSuddenLoC { get; init; }
        internal bool IsDead { get; init; }
    }

    internal sealed record IntegrationEvent(
        string Kind,
        double Offset,
        IntegrationState State);

    internal sealed record IntegrationResult(
        IntegrationState Initial,
        IntegrationState Final,
        IntegrationState[] Stages,
        int Iterations,
        double MaxScaledResidual,
        bool Converged,
        string[] ModeCrossings,
        string[] StateViolations,
        double[] PressureBoundEntries,
        double[] PressureBoundReleases,
        double[] CoreBoundEntries,
        double[] CoreBoundReleases,
        double[] HeadBoundEntries,
        double[] HeadBoundReleases,
        double[] LowerBoundEntries,
        double[] LowerBoundReleases,
        double[] ModeTransitions,
        IntegrationSegment[] Segments,
        IntegrationEvent[] Events);

    internal sealed record IntegrationSegment(
        double StartOffset,
        double Duration,
        IntegrationState Initial,
        IntegrationState Final,
        IntegrationState[] Stages,
        BloodBounds Bounds,
        double[] HeadCoefficients,
        double[] LowerCoefficients,
        double[] RateCoefficients,
        double[] CardioCoefficients)
    {
        internal double CoefficientStart { get; init; }
        internal bool CoreBound => (Bounds & BloodBounds.Core) != 0;
        internal bool HeadBound => (Bounds & BloodBounds.Head) != 0;
        internal bool LowerBound => (Bounds & BloodBounds.Lower) != 0;
    }

    private sealed record SegmentSolve(
        IntegrationState Final,
        IntegrationState[] Stages,
        int Iterations,
        double MaxScaledResidual,
        bool Converged,
        string[] ModeCrossings,
        string[] StateViolations,
        double[] PressureBoundEntries,
        double[] PressureBoundReleases,
        double[] HeadCoefficients,
        double[] LowerCoefficients,
        double[] RateCoefficients,
        double[] CardioCoefficients,
        bool CoreRootSearchValid,
        BloodBounds BoundViolations,
        Func<IntegrationState[], double> RecomputeResidual);

    [Flags]
    internal enum CirculationMode
    {
        None = 0,
        HeadOverfilled = 1,
        BradycardiaClamped = 2,
        PerfusionRangeClamped = 4,
        BaroTargetClamped = 8,
        CardioBuild = 16,
        UnsupportedCoreBound = 32,
        CardioCeilingBound = 64
    }

    [Flags]
    internal enum BloodBounds
    {
        None = 0,
        Core = 1,
        Head = 2,
        Lower = 4
    }

    private const CirculationMode CrossingBits = CirculationMode.HeadOverfilled |
        CirculationMode.BradycardiaClamped | CirculationMode.PerfusionRangeClamped |
        CirculationMode.BaroTargetClamped | CirculationMode.CardioBuild |
        CirculationMode.CardioCeilingBound;

    [MethodImpl(AggressiveOptimization)]
    internal IntegrationState CaptureIntervalState() => new(
        bloodHead, bloodLower, heartRateMultiplier, hrFatigue, cerebralPressureImpairment,
        respiratoryFatigue)
    {
        StrainingLevel = strainingLevel,
        StrainingFatigue = strainingFatigue,
        GSuitFatigue = gSuitFatigue,
        BloodO2Head = bloodO2Head,
        BloodO2Core = bloodO2Core,
        BloodO2Lower = bloodO2Lower,
        ArterialOxygenation = arterialOxygenation,
        ConsciousnessLevel = consciousnessLevel,
        LungCompressionLevel = lungCompressionLevel,
        PainLevel = painLevel,
        GyNeckFatigue = gyNeckFatigue,
        GyNeckFatigueDeathDwell = gyNeckFatigueDeathAccumulatedTime,
        SuddenLoCAccumulator = suddenLoCAccumulator,
        VisualGrayscaleLevel = visualGrayscaleLevel,
        VisualTunnelVisionLevel = visualTunnelVisionLevel,
        VisualRedoutLevel = visualRedoutLevel,
        VisualLoCLevel = visualLoCLevel,
        IsUnconscious = isUnconscious,
        InSuddenLoC = inSuddenLoC,
        IsDead = isDead
    };

    // state without materializing a capture. Mirrors IntegrationState's field
    // list; flags must match exactly.
    [MethodImpl(AggressiveOptimization)]
    internal bool ApproximatelyCurrent(in IntegrationState reference, double tolerance) =>
        Math.Abs(bloodHead - reference.BloodHead) <= tolerance &&
        Math.Abs(bloodLower - reference.BloodLower) <= tolerance &&
        Math.Abs(heartRateMultiplier - reference.HeartRateMultiplier) <= tolerance &&
        Math.Abs(hrFatigue - reference.CardioFatigue) <= tolerance &&
        Math.Abs(cerebralPressureImpairment - reference.CerebralPressureImpairment) <=
            tolerance &&
        Math.Abs(respiratoryFatigue - reference.RespiratoryFatigue) <= tolerance &&
        Math.Abs(strainingLevel - reference.StrainingLevel) <= tolerance &&
        Math.Abs(strainingFatigue - reference.StrainingFatigue) <= tolerance &&
        Math.Abs(gSuitFatigue - reference.GSuitFatigue) <= tolerance &&
        Math.Abs(bloodO2Head - reference.BloodO2Head) <= tolerance &&
        Math.Abs(bloodO2Core - reference.BloodO2Core) <= tolerance &&
        Math.Abs(bloodO2Lower - reference.BloodO2Lower) <= tolerance &&
        Math.Abs(arterialOxygenation - reference.ArterialOxygenation) <= tolerance &&
        Math.Abs(consciousnessLevel - reference.ConsciousnessLevel) <= tolerance &&
        Math.Abs(lungCompressionLevel - reference.LungCompressionLevel) <=
            tolerance &&
        Math.Abs(painLevel - reference.PainLevel) <= tolerance &&
        Math.Abs(gyNeckFatigue - reference.GyNeckFatigue) <= tolerance &&
        Math.Abs(gyNeckFatigueDeathAccumulatedTime - reference.GyNeckFatigueDeathDwell) <=
            tolerance &&
        Math.Abs(suddenLoCAccumulator - reference.SuddenLoCAccumulator) <=
            tolerance &&
        Math.Abs(visualGrayscaleLevel - reference.VisualGrayscaleLevel) <=
            tolerance &&
        Math.Abs(visualTunnelVisionLevel - reference.VisualTunnelVisionLevel) <=
            tolerance &&
        Math.Abs(visualRedoutLevel - reference.VisualRedoutLevel) <= tolerance &&
        Math.Abs(visualLoCLevel - reference.VisualLoCLevel) <= tolerance &&
        isUnconscious == reference.IsUnconscious &&
        inSuddenLoC == reference.InSuddenLoC &&
        isDead == reference.IsDead;

    private const double FullIntervalEventScanTolerance = 1e-9;

    private struct IntervalTrialState
    {
        internal double Head;
        internal double Lower;
        internal double HeartRate;
        internal double Straining;
        internal double Respiratory;
        internal double Pressure;
        internal double Arterial;
        internal double Compression;
        internal double Core => 1.0 - Head - Lower;
    }

    private enum FullIntervalEventProbeNeeds
    {
        None = 0,
        Head = 1,
        Trial = 2,
        Oxygen = 4,
        Consciousness = 8,
        Pressure = 16,
        Compression = 32,
        Respiratory = 64,
        TunnelVision = 128,
        Redout = 256,
        Grayscale = 512,
        NaturalOxygenDerivative = 1024
    }

    private struct FullIntervalEventProbe
    {
        internal double Time;
        internal double Head;
        internal IntervalTrialState Trial;
        internal double HeadOxygen;
        internal double LowerOxygen;
        internal double CoreOxygen;
        internal double Consciousness;
        internal double Pressure;
        internal double Compression;
        internal double Respiratory;
        internal double TunnelVision;
        internal double Redout;
        internal double Grayscale;
        internal double TunnelVisionTarget;
        internal double RedoutTarget;
        internal double GrayscaleTarget;
        internal double NaturalOxygenDerivative;

        [MethodImpl(AggressiveOptimization)]
        internal double OxygenAt(int coordinate) => coordinate switch
        {
            0 => HeadOxygen,
            1 => LowerOxygen,
            2 => CoreOxygen,
            _ => double.NaN
        };

        [MethodImpl(AggressiveOptimization)]
        internal double VisualValue(int channel) => channel switch
        {
            0 => TunnelVision,
            1 => Redout,
            2 => Grayscale,
            _ => double.NaN
        };

        [MethodImpl(AggressiveOptimization)]
        internal double VisualTarget(int channel) => channel switch
        {
            0 => TunnelVisionTarget,
            1 => RedoutTarget,
            2 => GrayscaleTarget,
            _ => double.NaN
        };
    }

    private readonly struct CrossingSamples
    {
        private readonly double start;
        private readonly double probe0;
        private readonly double probe1;
        private readonly double probe2;
        private readonly double probe3;
        private readonly double probe4;
        private readonly double end;

        internal CrossingSamples(double start, double probe0, double probe1, double probe2,
            double probe3, double probe4, double end)
        {
            this.start = start;
            this.probe0 = probe0;
            this.probe1 = probe1;
            this.probe2 = probe2;
            this.probe3 = probe3;
            this.probe4 = probe4;
            this.end = end;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double ValueAt(int index) => index switch
        {
            0 => start,
            1 => probe0,
            2 => probe1,
            3 => probe2,
            4 => probe3,
            5 => probe4,
            6 => end,
            _ => double.NaN
        };
    }

    private readonly struct ExponentialChannel
    {
        private readonly double initial;
        private readonly double equilibrium;
        private readonly double inverseTau;

        internal ExponentialChannel(double initial, double equilibrium, double inverseTau)
        {
            this.initial = initial;
            this.equilibrium = equilibrium;
            this.inverseTau = inverseTau;
        }

        // inverseTau >= 1e9 comes from a tau <= 1e-9 source: the channel is at
        // its equilibrium for every positive offset (instantaneous limit).
        private bool Instant => inverseTau >= 1e9;

        [MethodImpl(AggressiveOptimization)]
        internal double At(double t) =>
            t == 0.0
                ? initial
                : Instant
                    ? (t < 0.0 ? initial : equilibrium)
                    : equilibrium + (initial - equilibrium) * Math.Exp(-inverseTau * t);

        [MethodImpl(AggressiveOptimization)]
        internal double RateAt(double t) => Instant ? 0.0 : (equilibrium - At(t)) * inverseTau;

        [MethodImpl(AggressiveOptimization)]
        internal double Crossing(double level)
        {
            var denominator = initial - equilibrium;
            if (denominator == 0.0) return double.PositiveInfinity;
            if (Instant)
            {
                var inJump = (level - initial) * (equilibrium - level) >= 0.0 &&
                             level != initial;
                return inJump ? 0.0 : double.PositiveInfinity;
            }
            var ratio = (level - equilibrium) / denominator;
            if (ratio is <= 0.0 or > 1.0) return double.PositiveInfinity;
            var t = -Math.Log(ratio) / inverseTau;
            return t >= 0.0 ? t : double.PositiveInfinity;
        }

        [MethodImpl(AggressiveOptimization)]
        internal double UpCrossing(double level)
        {
            if (equilibrium <= level) return double.PositiveInfinity;
            return Crossing(level);
        }

        [MethodImpl(AggressiveOptimization)]
        internal double DownCrossing(double level)
        {
            if (equilibrium >= level) return double.PositiveInfinity;
            return Crossing(level);
        }
    }


    private sealed class IntervalLoopContext
    {
        internal readonly IntegrationResult circulation;
        internal readonly IntervalUpstream upstream;
        internal readonly LogicSettings settings;
        internal readonly List<IntegrationEvent> events;
        internal readonly List<string> violations;
        internal readonly List<IntegrationSegment> segments;
        internal readonly List<IntegrationState> stages;
        internal readonly FullIntervalEventProbe[] eventProbes;
        internal readonly List<(double Time, string Kind, int Coordinate, double Level,
            int Direction)> candidates;
        internal readonly List<(double Time, string Kind, int Coordinate, double Level,
            int Direction)> due;
        internal IntegrationState state;
        internal double cursor;
        internal readonly double dt;
        internal string lastO2RootEvidence = "none";

        internal IntervalLoopContext(in IntegrationState initial, double dt,
            IntegrationResult circulation, IntervalUpstream upstream,
            LogicSettings settings)
        {
            state = initial;
            this.dt = dt;
            this.circulation = circulation;
            this.upstream = upstream;
            this.settings = settings;
            events = new List<IntegrationEvent>(circulation.Events);
            violations = AcquireDiagnosticsList();
            segments = new List<IntegrationSegment>();
            stages = new List<IntegrationState>();
            eventProbes = new FullIntervalEventProbe[ScanProbes.Length + 2];
            // Iteration-local candidate/due buffers are reused across loop
            // iterations; they never escape into recorded results.
            candidates = new List<(double Time, string Kind, int Coordinate,
                double Level, int Direction)>();
            due = new List<(double Time, string Kind, int Coordinate, double Level,
                int Direction)>();
        }

        [MethodImpl(AggressiveOptimization)]
        internal void ApplyAndRecordEvent(string kind, double offset)
        {
            state = ApplyEvent(state, kind, settings);
            if (state.IsDead) upstream.PinDwell();
            events.Add(new IntegrationEvent(kind, offset, state));
        }

        [MethodImpl(AggressiveOptimization)]
        internal bool RecordPiece(DownstreamSolution piece, LoCTrajectory loc, double acceptEnd,
            IReadOnlyList<(double Time, string Kind, int Coordinate, double Level,
                int Direction)>? oxygenBoundEntries, out IntegrationState acceptedEnd,
            bool locContactAtEnd = false)
        {
            acceptedEnd = state;
            if (acceptEnd <= cursor + 1e-12) return true;
            var s = state;
            var pieceSpan = acceptEnd - cursor;
            IntegrationSegment bloodSegment = null!;
            foreach (var candidate in circulation.Segments)
                if (candidate.StartOffset <= cursor) bloodSegment = candidate;

            var stageStates = new IntegrationState[NumericalMath.RadauStageCount];
            for (var i = 0; i < NumericalMath.RadauStageCount; i++)
            {
                var stageTime = i == NumericalMath.RadauStageCount - 1
                    ? acceptEnd
                    : cursor + NumericalMath.RadauC[i] * pieceSpan;
                if (TryFullStateAt(stageTime, s, upstream, piece, loc, cursor,
                        pieceSpan, bloodSegment.Bounds, settings, out stageStates[i],
                        out var correction))
                    continue;
                violations.Add($"accepted downstream stage {i + 1} blood correction " +
                    $"{correction:R} exceeded {ActiveManifoldCorrectionTolerance:R}");
                return false;
            }
            if (!TryFullStateAt(acceptEnd, s, upstream, piece, loc, cursor, pieceSpan,
                    bloodSegment.Bounds, settings, out acceptedEnd, out var finalCorrection))
            {
                violations.Add($"accepted downstream endpoint blood correction " +
                    $"{finalCorrection:R} exceeded {ActiveManifoldCorrectionTolerance:R}");
                return false;
            }

            if (locContactAtEnd && !s.IsUnconscious && !s.IsDead)
            {
                var ceiling = LoCCeiling(acceptedEnd.ConsciousnessLevel, settings);
                var ceilingRate = CeilingDerivative(piece.Consciousness, 1.0, settings);
                var allowed = Math.Abs(loc.Rate - ceilingRate) *
                    PressureBoundRootToleranceSeconds + 1e-9;
                var correction = Math.Abs(acceptedEnd.VisualLoCLevel - ceiling);
                // The contact event already certifies the level reached the ceiling;
                // the accepted endpoint's reconstructed consciousness can shift the
                // ceiling by more than the nominal allowance, so snap up to a bounded
                // mismatch instead of leaving the level beside it to re-enter forever.
                if (IsFiniteNumber(ceiling) && ceiling >= 0.0 && ceiling <= 1.0 &&
                    IsFiniteNumber(ceilingRate) && IsFiniteNumber(allowed) &&
                    IsFiniteNumber(correction) &&
                    correction <= Math.Max(allowed, 1e-5))
                {
                    acceptedEnd = acceptedEnd with { VisualLoCLevel = ceiling };
                    var lastStage = stageStates.Length - 1;
                    stageStates[lastStage] =
                        stageStates[lastStage] with { VisualLoCLevel = ceiling };
                }
            }

            if (oxygenBoundEntries != null)
            {
                foreach (var entry in oxygenBoundEntries)
                {
                    if (entry.Coordinate is < 0 or > 2 ||
                        !IsFiniteNumber(entry.Level))
                    {
                        violations.Add($"invalid oxygen-bound event payload {entry.Kind}");
                        return false;
                    }
                    acceptedEnd = SetOxygenCoordinate(acceptedEnd, entry.Coordinate,
                        entry.Level);
                    var lastStage = stageStates.Length - 1;
                    stageStates[lastStage] = SetOxygenCoordinate(stageStates[lastStage],
                        entry.Coordinate, entry.Level);
                }
            }
            for (var i = 0; i < stageStates.Length; i++)
            {
                var violation = DownstreamViolation(stageStates[i], settings,
                    bloodSegment.Bounds);
                if (violation.Length == 0) continue;
                violations.Add($"accepted downstream stage {i + 1} at " +
                    $"{cursor + NumericalMath.RadauC[i] * pieceSpan:R} in " +
                    $"[{cursor:R},{acceptEnd:R}], bounds={bloodSegment.Bounds}: " +
                    $"{violation}; lastO2Root={lastO2RootEvidence}");
                return false;
            }
            var endpointViolation = DownstreamViolation(acceptedEnd, settings,
                bloodSegment.Bounds);
            if (endpointViolation.Length != 0)
            {
                violations.Add($"accepted downstream endpoint at {acceptEnd:R} in " +
                    $"[{cursor:R},{acceptEnd:R}], bounds={bloodSegment.Bounds}: " +
                    $"{endpointViolation}; lastO2Root={lastO2RootEvidence}");
                return false;
            }

            segments.Add(new IntegrationSegment(cursor, pieceSpan, s,
                acceptedEnd,
                stageStates, bloodSegment.Bounds, bloodSegment.HeadCoefficients,
                bloodSegment.LowerCoefficients, bloodSegment.RateCoefficients,
                bloodSegment.CardioCoefficients)
            {
                CoefficientStart = bloodSegment.StartOffset
            });
            stages.AddRange(stageStates);
            return true;
        }
    }
}
