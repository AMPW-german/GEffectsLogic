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

using System.Reflection;
using GEffectsLogic;
using Xunit.Abstractions;

namespace GEffectLogicTests;

internal sealed record DtFlagTransition(string Flag, bool IsOnset, double OpenEndSeconds, double ClosedEndSeconds);

internal sealed record DtCandidateRun(
    SettingsRegressionTests.ModelSnapshot[] Snapshots,
    List<DtFlagTransition> FlagTransitions);

[Trait("Category", "DtStability")]
public class ConstantLoadDtStabilityTests
{
    private const int HorizonSeconds = 30;
    private const double StateSpreadTolerance = 0.001;
    private const double AlgebraicSpreadTolerance = 1e-12;
    private const double BloodSumTolerance = 1e-12;

    private static readonly double[] CandidateDt = [0.01, 0.1, 0.25, 0.5, 1.0];

    private static readonly double[] AwkwardDt = [0.073, 0.127, 0.257, 0.499, 0.501, 0.733, 0.997];

    private static readonly string[] AlgebraicChannels =
        [nameof(SettingsRegressionTests.ModelSnapshot.FatigueHeartRateFloor),
         nameof(SettingsRegressionTests.ModelSnapshot.GxEffectiveTolerance),
         nameof(SettingsRegressionTests.ModelSnapshot.GyEffectiveTolerance)];

    private static readonly string[] UnboundedChannels =
        [nameof(SettingsRegressionTests.ModelSnapshot.HeartRateMultiplier),
         nameof(SettingsRegressionTests.ModelSnapshot.BloodHeadOverfill),
         nameof(SettingsRegressionTests.ModelSnapshot.GxEffectiveTolerance),
         nameof(SettingsRegressionTests.ModelSnapshot.GyEffectiveTolerance),
         nameof(SettingsRegressionTests.ModelSnapshot.FatigueHeartRateFloor)];

    private static readonly PropertyInfo[] DoubleChannels = typeof(SettingsRegressionTests.ModelSnapshot)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(double))
        .ToArray();

    private static readonly PropertyInfo[] FlagChannels = typeof(SettingsRegressionTests.ModelSnapshot)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(bool))
        .ToArray();

    private readonly ITestOutputHelper _output;

    public ConstantLoadDtStabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> VectorData =>
        DtLegacyBaselineRecipe.Vectors.Select(vector => new object[] { vector.Gx, vector.Gy, vector.Gz });

    [Theory]
    [MemberData(nameof(VectorData))]
    public void ConstantLoadAgreesAcrossSupportedDt(double gx, double gy, double gz)
    {
        var runs = CandidateDt.Select(dt => RunCandidate(dt, gx, gy, gz)).ToArray();
        var failures = new List<string>();
        var label = $"({gx:R},{gy:R},{gz:R})";

        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
        {
            var dt = CandidateDt[candidateIndex];
            for (var second = 1; second <= HorizonSeconds; second++)
                CheckInvariants(runs[candidateIndex].Snapshots[second - 1], label, dt, second, failures);
        }

        for (var second = 1; second <= HorizonSeconds; second++)
        {
            foreach (var channel in DoubleChannels)
            {
                var values = runs.Select(run => (double)channel.GetValue(run.Snapshots[second - 1])!).ToArray();
                var minimum = values.Min();
                var maximum = values.Max();
                var spread = maximum - minimum;
                var allowed = AlgebraicChannels.Contains(channel.Name)
                    ? AlgebraicSpreadTolerance
                    : StateSpreadTolerance;
                if (spread > allowed)
                {
                    var minDt = CandidateDt[Array.IndexOf(values, minimum)];
                    var maxDt = CandidateDt[Array.IndexOf(values, maximum)];
                    failures.Add($"{label} {channel.Name} at {second}s: min {minimum:R} (dt {minDt:R}), " +
                                 $"max {maximum:R} (dt {maxDt:R}), spread {spread:R} exceeds {allowed:R}.");
                }
            }
        }

        _output.WriteLine($"{label}: raw flag observation brackets at one-second cadence " +
                          "(not refined event times):");
        for (var candidateIndex = 0; candidateIndex < CandidateDt.Length; candidateIndex++)
        {
            var transitions = runs[candidateIndex].FlagTransitions;
            _output.WriteLine(transitions.Count == 0
                ? $"  dt {CandidateDt[candidateIndex]:R}: no flag transitions"
                : $"  dt {CandidateDt[candidateIndex]:R}: " +
                  string.Join("; ", transitions.Select(transition =>
                      $"{transition.Flag} {(transition.IsOnset ? "onset" : "recovery")} " +
                      $"({transition.OpenEndSeconds:R}, {transition.ClosedEndSeconds:R}]")));
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} dt-stability breach(es) at {label}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [MemberData(nameof(VectorData))]
    public void AwkwardSchedulesAgreeAcrossDt(double gx, double gy, double gz)
    {
        var reference = RunScheduled(0.25, gx, gy, gz);
        var failures = new List<string>();
        var label = $"({gx:R},{gy:R},{gz:R})";

        foreach (var dt in AwkwardDt)
        {
            var run = RunScheduled(dt, gx, gy, gz);
            CheckInvariants(run, label, dt, HorizonSeconds, failures);
            foreach (var channel in DoubleChannels)
            {
                var referenceValue = (double)channel.GetValue(reference)!;
                var value = (double)channel.GetValue(run)!;
                var allowed = AlgebraicChannels.Contains(channel.Name)
                    ? AlgebraicSpreadTolerance
                    : StateSpreadTolerance;
                var difference = Math.Abs(value - referenceValue);
                if (difference > allowed)
                    failures.Add($"{label} {channel.Name} after {HorizonSeconds + 4}s: " +
                                 $"dt {dt:R} value {value:R} differs from reference " +
                                 $"{referenceValue:R} by {difference:R} (allowed {allowed:R}).");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} awkward-schedule breach(es) at {label}:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures));
    }

    private static List<double> AwkwardSchedule(double dt, double duration)
    {
        var intervals = new List<double>();
        var remaining = duration;
        while (remaining > 0.0)
        {
            var tail = remaining - dt;
            if (tail > 0.0 && tail < 0.01)
            {
                intervals.Add(remaining / 2.0);
                intervals.Add(remaining / 2.0);
                break;
            }
            var step = Math.Min(dt, remaining);
            intervals.Add(step);
            remaining -= step;
        }
        return intervals;
    }

    private static SettingsRegressionTests.ModelSnapshot RunScheduled(double dt, double gx,
        double gy, double gz)
    {
        var instance = new GEffectsLogicInstance(new DtStabilityLogger(),
            DtLegacyBaselineRecipe.BaselineSettings);
        for (var step = 0; step < 8; step++)
            instance.PhysModel.Update(0.25, gx, gy, gz);
        for (var step = 0; step < 8; step++)
            instance.PhysModel.Update(0.25, 0.0, 0.0, 1.0);
        foreach (var interval in AwkwardSchedule(dt, HorizonSeconds))
            instance.PhysModel.Update(interval, gx, gy, gz);
        return SettingsRegressionTests.CaptureModel(instance.PhysModel);
    }

    private static DtCandidateRun RunCandidate(double dt, double gx, double gy, double gz)
    {
        GEffectsLogicInstance instance = new(new DtStabilityLogger(), DtLegacyBaselineRecipe.BaselineSettings);
        var callsPerSecond = (int)Math.Round(1.0 / dt);
        var snapshots = new SettingsRegressionTests.ModelSnapshot[HorizonSeconds];
        List<DtFlagTransition> transitions = [];
        var previous = SettingsRegressionTests.CaptureModel(instance.PhysModel);

        for (var second = 1; second <= HorizonSeconds; second++)
        {
            for (var call = 0; call < callsPerSecond; call++)
                instance.PhysModel.Update(dt, gx, gy, gz);

            var snapshot = SettingsRegressionTests.CaptureModel(instance.PhysModel);
            snapshots[second - 1] = snapshot;
            foreach (var flag in FlagChannels)
            {
                var wasSet = (bool)flag.GetValue(previous)!;
                var isSet = (bool)flag.GetValue(snapshot)!;
                if (wasSet != isSet)
                    transitions.Add(new DtFlagTransition(flag.Name, isSet, second - 1.0, second));
            }

            previous = snapshot;
        }

        return new DtCandidateRun(snapshots, transitions);
    }

    private static void CheckInvariants(SettingsRegressionTests.ModelSnapshot snapshot, string label,
        double dt, int second, List<string> failures)
    {
        foreach (var channel in DoubleChannels)
        {
            var value = (double)channel.GetValue(snapshot)!;
            if (!double.IsFinite(value))
                failures.Add($"{label} {channel.Name} at {second}s, dt {dt:R}: non-finite {value:R}.");
            else if (!UnboundedChannels.Contains(channel.Name) && (value < 0.0 || value > 1.0))
                failures.Add($"{label} {channel.Name} at {second}s, dt {dt:R}: {value:R} outside [0,1].");
        }

        if (snapshot.BloodHead < DtLegacyBaselineRecipe.BaselineSettings.MinHeadBloodFraction)
            failures.Add($"{label} BloodHead at {second}s, dt {dt:R}: {snapshot.BloodHead:R} below head floor " +
                         $"{DtLegacyBaselineRecipe.BaselineSettings.MinHeadBloodFraction:R}.");
        var bloodSumError = Math.Abs(snapshot.BloodHead + snapshot.BloodCore + snapshot.BloodLower - 1.0);
        if (bloodSumError > BloodSumTolerance)
            failures.Add($"{label} blood sum at {second}s, dt {dt:R}: error {bloodSumError:R}.");
        if (snapshot.BrainO2 != snapshot.BloodO2Head)
            failures.Add($"{label} BrainO2 at {second}s, dt {dt:R}: {snapshot.BrainO2:R} differs from " +
                         $"BloodO2Head {snapshot.BloodO2Head:R}.");
    }
}
