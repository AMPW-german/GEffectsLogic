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

internal sealed record DtLegacyEventBracket(double OpenEndSeconds, double ClosedEndSeconds);

internal sealed record DtLegacyVectorBaseline(
    double Gx,
    double Gy,
    double Gz,
    DtLegacyEventBracket? UnconsciousOnset,
    DtLegacyEventBracket? ConsciousnessFloorOnset,
    DtLegacyEventBracket? SuddenLoCOnset,
    DtLegacyEventBracket? DeathOnset,
    DtLegacyEventBracket[] UnconsciousRecoveries,
    DtLegacyEventBracket[] SuddenLoCRecoveries,
    bool EndIsUnconscious,
    bool EndConsciousnessAtFloor,
    bool EndInSuddenLoC,
    bool EndIsDead,
    SettingsRegressionTests.ModelSnapshot Endpoint);

internal static class DtLegacyBaselineRecipe
{
    internal const string ProvenanceCommit = "a1cc1dcbeadc81df94c5cb6193be2adec7b311ce";
    internal const double ObservationDtSeconds = 0.25;
    internal const int ObservationUpdates = 1200;
    internal const double ConsciousnessFloor = 0.01;
    internal const double LegacyDriftFloorSeconds = 0.5;
    internal const double LegacyDriftFraction = 0.02;

    internal static readonly (double Gx, double Gy, double Gz)[] Vectors =
    [
        (0.0, 0.0, 1.0),
        (0.0, 0.0, -2.0),
        (0.0, 0.0, -3.0),
        (0.0, 0.0, -5.0),
        (0.0, 0.0, 5.0),
        (15.0, 0.0, 0.0),
        (0.0, 10.0, 0.0),
        (0.0, 11.0, 0.0),
        (0.0, 20.0, 0.0),
        (2.0, 1.0, 5.0),
        (10.0, 2.5, 0.0),
        (8.0, 2.0, 0.0)
    ];

    internal static readonly LogicSettings BaselineSettings =
        LogicSettings.Default with { StabilizationTimeThreshold = double.MaxValue };

    internal static DtLegacyVectorBaseline Run(double gx, double gy, double gz)
    {
        GEffectsLogicInstance instance = new(new DtStabilityLogger(), BaselineSettings);

        DtLegacyEventBracket? unconsciousOnset = null;
        DtLegacyEventBracket? consciousnessFloorOnset = null;
        DtLegacyEventBracket? suddenLoCOnset = null;
        DtLegacyEventBracket? deathOnset = null;
        List<DtLegacyEventBracket> unconsciousRecoveries = [];
        List<DtLegacyEventBracket> suddenLoCRecoveries = [];

        var wasUnconscious = instance.IsUnconscious;
        var wasAtConsciousnessFloor = instance.ConsciousnessLevel <= ConsciousnessFloor;
        var wasInSuddenLoC = instance.InSuddenLoC;
        var wasDead = instance.IsDead;

        for (var updateIndex = 0; updateIndex < ObservationUpdates; updateIndex++)
        {
            var previousTime = instance.Time;
            instance.Update(ObservationDtSeconds, gx, gy, gz);
            var currentTime = instance.Time;

            Track(instance.IsUnconscious, ref wasUnconscious, ref unconsciousOnset, unconsciousRecoveries);
            Track(instance.ConsciousnessLevel <= ConsciousnessFloor, ref wasAtConsciousnessFloor,
                ref consciousnessFloorOnset, null);
            Track(instance.InSuddenLoC, ref wasInSuddenLoC, ref suddenLoCOnset, suddenLoCRecoveries);
            Track(instance.IsDead, ref wasDead, ref deathOnset, null);

            void Track(bool current, ref bool previous, ref DtLegacyEventBracket? onset,
                List<DtLegacyEventBracket>? recoveries)
            {
                if (current && !previous)
                    onset ??= new DtLegacyEventBracket(previousTime, currentTime);
                else if (!current && previous)
                    recoveries?.Add(new DtLegacyEventBracket(previousTime, currentTime));
                previous = current;
            }
        }

        return new DtLegacyVectorBaseline(
            gx, gy, gz,
            unconsciousOnset,
            consciousnessFloorOnset,
            suddenLoCOnset,
            deathOnset,
            unconsciousRecoveries.ToArray(),
            suddenLoCRecoveries.ToArray(),
            instance.IsUnconscious,
            instance.ConsciousnessLevel <= ConsciousnessFloor,
            instance.InSuddenLoC,
            instance.IsDead,
            SettingsRegressionTests.CaptureModel(instance.PhysModel));
    }
}

[Trait("Category", "DtStability")]
public class DtLegacyBaselineTests
{
    private static readonly DtLegacyVectorBaseline[] Expected =
    [
        new DtLegacyVectorBaseline(0, 0, 1, null, null, null, null, [], [], false, false, false, false, new SettingsRegressionTests.ModelSnapshot(0.2, 0, 0.35, 0.45, 0.8740601349730179, 0.9780053050218525, 0.9736573251868129, 0.8740601349730179, 1, 1, 1, 1, 0, 0, 0, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false)),
        new DtLegacyVectorBaseline(0, 0, -2, null, null, null, null, [], [], false, false, false, false, new SettingsRegressionTests.ModelSnapshot(0.20549930163554123, 0.027496508177706075, 0.31941028552837897, 0.4750904128360798, 0.8162172591504404, 0.9774398184923826, 0.9728586281370234, 0.8162172591504404, 1, 0.8900139672891765, 1, 0.5929761436387889, 0.06842443434439152, 0, 0, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0, 0.3879287770792624, 0, 0, 0, 0, false, false, false)),
        new DtLegacyVectorBaseline(0, 0, -3, new DtLegacyEventBracket(19.75, 20), new DtLegacyEventBracket(23, 23.25), null, null, [], [], true, true, false, false, new SettingsRegressionTests.ModelSnapshot(0.21100058260745377, 0.055002913037268814, 0.2634012862475378, 0.5255981311450084, 0.7531873839975368, 0.9769248218344448, 0.9722491500708379, 0.7531873839975368, 1, 0.7799883478509264, 1, 8.134202705937727E-141, 1, 0, 0, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0, 0.9779166466283529, 1, 0, 0, 0, true, false, false)),
        new DtLegacyVectorBaseline(0, 0, -5, new DtLegacyEventBracket(3.5, 3.75), new DtLegacyEventBracket(5, 5.25), null, null, [], [], true, true, false, false, new SettingsRegressionTests.ModelSnapshot(0.22271601128258445, 0.11358005641292218, 0, 0.7772839887174156, 0.5955361351827707, 0.9761200268683037, 0.971719306351417, 0.5955361351827707, 1, 0.5456797743483126, 1, 4.2499934055372585E-150, 1, 0, 0, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0, 0.9999999999999999, 1, 0, 0, 0, true, false, false)),
        new DtLegacyVectorBaseline(0, 0, 5, new DtLegacyEventBracket(5, 5.25), new DtLegacyEventBracket(23.75, 24), null, null, [], [], true, true, false, false, new SettingsRegressionTests.ModelSnapshot(0.021015971005891466, 0, 0.4802068499213898, 0.49877717907271885, 0.18, 0.9791205373525553, 0.9769835312052019, 0.18, 1, 1.8177998602951377, 0.10507985502945733, 4.54335281831965E-124, 0, 1, 1, 0.6761186544281165, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.9999999999999996, 0, 1, 0.999999999999984, 0.9999999999999993, 0.999999999999984, true, false, false)),
        new DtLegacyVectorBaseline(15, 0, 0, new DtLegacyEventBracket(73.75, 74), new DtLegacyEventBracket(83.25, 83.5), new DtLegacyEventBracket(4, 4.25), null, [], [], true, true, true, false, new SettingsRegressionTests.ModelSnapshot(0.20110392320576678, 0.005519616028833857, 0.34499102118994135, 0.45390505560429184, 0.18, 0.09819522207720767, 0.0935422792457677, 0.18, 0.222004701448134, 0.9779215358846662, 1, 2.624931022613154E-110, 0.0005765529644911448, 0, 0, 0, 1.25, 1, 3.25, 1, 0, 0, 0.7499999999999989, 1, 0, 0.003741858064512256, 1, 0, 0, 0, true, false, true)),
        new DtLegacyVectorBaseline(0, 10, 0, null, null, new DtLegacyEventBracket(4, 4.25), null, [], [], false, false, true, false, new SettingsRegressionTests.ModelSnapshot(0.20110392320576678, 0.005519616028833857, 0.34499102118994135, 0.45390505560429184, 0.5312582276015128, 0.19916548880778806, 0.19420568080518544, 0.5312582276015128, 1, 0.9779215358846662, 1, 0.45248926328939804, 0.0005765529644911448, 0, 0, 0, 1.25, 0, 1, 0.1, 0.9491536458333333, 0.9999526793308331, 0.9999999999999989, 1, 0, 0.003741858064512256, 0, 0, 0, 0, false, false, true)),
        new DtLegacyVectorBaseline(0, 11, 0, new DtLegacyEventBracket(1.75, 2), new DtLegacyEventBracket(1.75, 2), null, new DtLegacyEventBracket(1.75, 2), [], [], true, true, false, true, new SettingsRegressionTests.ModelSnapshot(0.20110392320576678, 0.005519616028833857, 0.34499102118994135, 0.45390505560429184, 0.5312582276015128, 0.19916548880778806, 0.19420568080518544, 0.5312582276015128, 1, 0.9779215358846662, 1, 0, 0.0005765529644911448, 0, 0, 0, 1.25, 0, 1, 0.1, 0.9958333333333333, 0.9999526793308331, 0.9999999999999989, 1, 0, 0.003741858064512256, 1, 0, 0, 0, true, true, false)),
        new DtLegacyVectorBaseline(0, 20, 0, new DtLegacyEventBracket(1.5, 1.75), new DtLegacyEventBracket(1.5, 1.75), null, new DtLegacyEventBracket(1.5, 1.75), [], [], true, true, false, true, new SettingsRegressionTests.ModelSnapshot(0.20110392320576678, 0.005519616028833857, 0.34499102118994135, 0.45390505560429184, 0.5312582276015128, 0.19916548880778806, 0.19420568080518544, 0.5312582276015128, 1, 0.9779215358846662, 1, 0, 0.0005765529644911448, 0, 0, 0, 1.25, 0, 1, 0.1, 0.9958333333333333, 0.9999526793308331, 0.9999999999999989, 1, 0, 0.003741858064512256, 1, 0, 0, 0, true, true, false)),
        new DtLegacyVectorBaseline(2, 1, 5, new DtLegacyEventBracket(3.5, 3.75), new DtLegacyEventBracket(5, 5.25), null, null, [], [], true, true, false, false, new SettingsRegressionTests.ModelSnapshot(0.02, 0, 0.48867081091441517, 0.49132918908558476, 0.18, 0.48924670013890476, 0.48706065297595186, 0.18, 1, 1.8162533711094873, 0.09999999999999999, 1.1286573442513232E-146, 0, 1, 1, 0.6769523429946275, 1.25, 1, 1.3, 0.7, 0, 0, 0.2999999999999995, 0, 0.9999999999999996, 0, 1, 0.9999999999999982, 0.9999999999999993, 0.9999999999999982, true, false, false)),
        new DtLegacyVectorBaseline(10, 2.5, 0, null, null, new DtLegacyEventBracket(4, 4.25), null, [], [], false, false, true, false, new SettingsRegressionTests.ModelSnapshot(0.20110392320576678, 0.005519616028833857, 0.34499102118994135, 0.45390505560429184, 0.4062692635965572, 0.1008162793306952, 0.09588346420233078, 0.4062692635965572, 0.8186920378431695, 0.9779215358846662, 1, 0.19336844123776958, 0.0005765529644911448, 0, 0, 0, 1.25, 1, 2.5, 0.1, 0, 0.06803816198026201, 0.9999999999999989, 1, 0, 0.003741858064512256, 0.1423226579084652, 0, 0, 0, false, false, true)),
        new DtLegacyVectorBaseline(8, 2, 0, null, null, null, null, [], [], false, false, false, false, new SettingsRegressionTests.ModelSnapshot(0.20110392320576678, 0.005519616028833857, 0.34499102118994135, 0.45390505560429184, 0.4545565689916021, 0.1013749812313418, 0.09637528198018726, 0.4545565689916021, 0.9235107034650872, 0.9779215358846662, 1, 0.29394522104782356, 0.0005765529644911448, 0, 0, 0, 1.25, 1, 2.2, 0.15147186257614298, 0, 0, 0.9999999999999989, 0, 0, 0.003741858064512256, 0.006523402395173997, 0, 0, 0, false, false, false))
    ];

    private readonly ITestOutputHelper _output;

    public DtLegacyBaselineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> VectorIndices =>
        Enumerable.Range(0, Expected.Length).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(VectorIndices))]
    public void LegacyEventTimesMatchFrozenBaseline(int vectorIndex)
    {
        var frozen = Expected[vectorIndex];
        var observed = DtLegacyBaselineRecipe.Run(frozen.Gx, frozen.Gy, frozen.Gz);
        var failures = new List<string>();
        var label = $"({frozen.Gx:R},{frozen.Gy:R},{frozen.Gz:R})";

        CompareEvent($"{label} IsUnconscious onset", frozen.UnconsciousOnset, observed.UnconsciousOnset, failures);
        CompareEvent($"{label} ConsciousnessLevel<=0.01 onset", frozen.ConsciousnessFloorOnset,
            observed.ConsciousnessFloorOnset, failures);
        CompareEvent($"{label} InSuddenLoC onset", frozen.SuddenLoCOnset, observed.SuddenLoCOnset, failures);
        CompareEvent($"{label} IsDead onset", frozen.DeathOnset, observed.DeathOnset, failures);
        CompareRecoveries($"{label} IsUnconscious recovery", frozen.UnconsciousRecoveries,
            observed.UnconsciousRecoveries, failures);
        CompareRecoveries($"{label} InSuddenLoC recovery", frozen.SuddenLoCRecoveries,
            observed.SuddenLoCRecoveries, failures);

        var frozenOrder = OrderedEventNames(frozen).ToArray();
        var observedOrder = OrderedEventNames(observed).ToArray();
        if (!frozenOrder.SequenceEqual(observedOrder))
            failures.Add($"{label} event order changed: frozen [{string.Join(", ", frozenOrder)}], " +
                         $"observed [{string.Join(", ", observedOrder)}].");

        if (observed.EndIsUnconscious != frozen.EndIsUnconscious)
            failures.Add($"{label} endpoint IsUnconscious: frozen {frozen.EndIsUnconscious}, observed {observed.EndIsUnconscious}.");
        if (observed.EndConsciousnessAtFloor != frozen.EndConsciousnessAtFloor)
            failures.Add($"{label} endpoint ConsciousnessLevel<=0.01: frozen {frozen.EndConsciousnessAtFloor}, observed {observed.EndConsciousnessAtFloor}.");
        if (observed.EndInSuddenLoC != frozen.EndInSuddenLoC)
            failures.Add($"{label} endpoint InSuddenLoC: frozen {frozen.EndInSuddenLoC}, observed {observed.EndInSuddenLoC}.");
        if (observed.EndIsDead != frozen.EndIsDead)
            failures.Add($"{label} endpoint IsDead: frozen {frozen.EndIsDead}, observed {observed.EndIsDead}.");

        _output.WriteLine(
            $"{label}: frozen endpoint from {DtLegacyBaselineRecipe.ProvenanceCommit} versus observed:");
        foreach (var property in typeof(SettingsRegressionTests.ModelSnapshot)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var frozenValue = property.GetValue(frozen.Endpoint);
            var observedValue = property.GetValue(observed.Endpoint);
            _output.WriteLine(frozenValue is double
                ? $"  {property.Name}: frozen {frozenValue:R}, observed {observedValue:R}"
                : $"  {property.Name}: frozen {frozenValue}, observed {observedValue}");
        }

        Assert.True(failures.Count == 0,
            $"Legacy baseline drift at {label}:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static IEnumerable<string> OrderedEventNames(DtLegacyVectorBaseline baseline)
    {
        return Events(baseline).OrderBy(e => e.Bracket.ClosedEndSeconds).Select(e => e.Name);
    }

    private static IEnumerable<(string Name, DtLegacyEventBracket Bracket)> Events(
        DtLegacyVectorBaseline baseline)
    {
        if (baseline.UnconsciousOnset is { } unconsciousOnset)
            yield return ("IsUnconscious onset", unconsciousOnset);
        if (baseline.ConsciousnessFloorOnset is { } consciousnessFloorOnset)
            yield return ("ConsciousnessLevel<=0.01 onset", consciousnessFloorOnset);
        if (baseline.SuddenLoCOnset is { } suddenLoCOnset)
            yield return ("InSuddenLoC onset", suddenLoCOnset);
        if (baseline.DeathOnset is { } deathOnset)
            yield return ("IsDead onset", deathOnset);
        for (var i = 0; i < baseline.UnconsciousRecoveries.Length; i++)
            yield return ($"IsUnconscious recovery {i + 1}", baseline.UnconsciousRecoveries[i]);
        for (var i = 0; i < baseline.SuddenLoCRecoveries.Length; i++)
            yield return ($"InSuddenLoC recovery {i + 1}", baseline.SuddenLoCRecoveries[i]);
    }

    private static void CompareRecoveries(string name, DtLegacyEventBracket[] frozen,
        DtLegacyEventBracket[] observed, List<string> failures)
    {
        if (frozen.Length != observed.Length)
        {
            failures.Add($"{name}: frozen count {frozen.Length}, observed count {observed.Length}.");
            return;
        }

        for (var i = 0; i < frozen.Length; i++)
            CompareEvent($"{name} {i + 1}", frozen[i], observed[i], failures);
    }

    private static void CompareEvent(string name, DtLegacyEventBracket? frozen,
        DtLegacyEventBracket? observed, List<string> failures)
    {
        if (frozen is null || observed is null)
        {
            if (frozen is not null || observed is not null)
                failures.Add($"{name}: presence changed; frozen {Format(frozen)}, observed {Format(observed)}.");
            return;
        }

        var tolerance = Math.Max(DtLegacyBaselineRecipe.LegacyDriftFloorSeconds,
            DtLegacyBaselineRecipe.LegacyDriftFraction * frozen.ClosedEndSeconds);
        if (Math.Abs(observed.ClosedEndSeconds - frozen.ClosedEndSeconds) > tolerance)
            failures.Add($"{name}: first-observed {observed.ClosedEndSeconds:R}s deviates from frozen " +
                         $"{frozen.ClosedEndSeconds:R}s beyond {tolerance:R}s.");
        if (Math.Abs(observed.OpenEndSeconds - frozen.OpenEndSeconds) > tolerance)
            failures.Add($"{name}: bracket open end {observed.OpenEndSeconds:R}s deviates from frozen " +
                         $"{frozen.OpenEndSeconds:R}s beyond {tolerance:R}s.");
    }

    private static string Format(DtLegacyEventBracket? bracket) =>
        bracket is null ? "absent" : $"({bracket.OpenEndSeconds:R}, {bracket.ClosedEndSeconds:R}]";
}
