// GEffectsLogic
// Copyright (C) 2026 AMPW
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using Xunit.Abstractions;

namespace GEffectLogicTests;

[Trait("Category", "Experimental")]
public class DtStabilityReportTests
{
    private readonly ITestOutputHelper _output;

    public DtStabilityReportTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DtDependencyReportMeetsAllStabilityThresholds()
    {
        var results = DtStabilitySimulationResults.Get();
        var definitions = DtStabilityConfiguration.Definitions;
        var baselineIndex = Array.FindIndex(definitions,
            definition => definition.StepsPerSecond == DtStabilityConfiguration.BaselineStepsPerSecond);
        var candidatePasses = new bool[definitions.Length];
        var dependencyPercentages = definitions.Select(_ => new List<double>()).ToArray();
        var thresholdFailures = new List<string>();

        for (var definitionIndex = 0; definitionIndex < definitions.Length; definitionIndex++)
        {
            var definition = definitions[definitionIndex];
            var candidateFailures = new List<string>();

            foreach (var zone in results.GLoCZones)
            {
                var baseline = zone.Results[baselineIndex].TimeToGLoCSeconds;
                var candidate = zone.Results[definitionIndex].TimeToGLoCSeconds;
                if (baseline is null || candidate is null)
                {
                    candidateFailures.Add($"{zone.TargetGz:F1} Gz did not reach GLoC at baseline or candidate dt");
                    continue;
                }

                var deviationSeconds = Math.Abs(candidate.Value - baseline.Value);
                var dependencyPercentage = deviationSeconds / baseline.Value * 100.0;
                dependencyPercentages[definitionIndex].Add(dependencyPercentage);
                var allowedDeviationSeconds = DtStabilityConfiguration.GLoCSpreadToleranceSeconds *
                                              Math.Max(definition.Seconds, definitions[baselineIndex].Seconds);
                _output.WriteLine(
                    $"{definition.DisplayName}, {zone.TargetGz:F1} Gz: GLoC {candidate.Value:F3}s; baseline {baseline.Value:F3}s; deviation {deviationSeconds:F3}s ({dependencyPercentage:F3}%).");
                if (deviationSeconds > allowedDeviationSeconds)
                    candidateFailures.Add($"{zone.TargetGz:F1} Gz GLoC deviation {deviationSeconds:F3}s exceeds {allowedDeviationSeconds:F3}s");
            }

            foreach (var zone in results.NonGLoCZones)
            {
                var baseline = zone.Results[baselineIndex].ConsciousnessSamples;
                var candidate = zone.Results[definitionIndex].ConsciousnessSamples;
                var maximumDifference = 0.0;

                for (var sampleIndex = 0; sampleIndex < baseline.Count; sampleIndex++)
                {
                    var difference = Math.Abs(candidate[sampleIndex] - baseline[sampleIndex]);
                    maximumDifference = Math.Max(maximumDifference, difference);
                    dependencyPercentages[definitionIndex].Add(difference * 100.0);
                }

                var finalDifference = Math.Abs(candidate[^1] - baseline[^1]);
                _output.WriteLine(
                    $"{definition.DisplayName}, {zone.TargetGz:F1} Gz: maximum consciousness deviation {maximumDifference:F5}; final deviation {finalDifference:F5}.");
                if (maximumDifference > DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance)
                    candidateFailures.Add($"{zone.TargetGz:F1} Gz maximum consciousness deviation {maximumDifference:F5} exceeds {DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance:F5}");
                if (finalDifference > DtStabilityConfiguration.FinalConsciousnessSpreadTolerance)
                    candidateFailures.Add($"{zone.TargetGz:F1} Gz final consciousness deviation {finalDifference:F5} exceeds {DtStabilityConfiguration.FinalConsciousnessSpreadTolerance:F5}");
            }

            candidatePasses[definitionIndex] = candidateFailures.Count == 0;
            var dependencies = dependencyPercentages[definitionIndex];
            var meanDependency = dependencies.Count == 0 ? double.NaN : dependencies.Average();
            var worstDependency = dependencies.Count == 0 ? double.NaN : dependencies.Max();
            _output.WriteLine(
                $"{definition.DisplayName}: {(candidatePasses[definitionIndex] ? "PASS" : "FAIL")}; mean dependency {meanDependency:F3}%; worst dependency {worstDependency:F3}%.");
            foreach (var failure in candidateFailures)
                _output.WriteLine($"  {failure}");

            thresholdFailures.AddRange(candidateFailures.Select(failure => $"{definition.DisplayName}: {failure}"));
        }

        ReportCrossDtThresholdFailures(results, thresholdFailures);
        ReportPassingDtMargin(definitions, baselineIndex, candidatePasses);

        var allDependencies = dependencyPercentages.SelectMany(percentages => percentages).ToArray();
        _output.WriteLine($"Overall mean dt dependency: {allDependencies.Average():F3}%; worst dt dependency: {allDependencies.Max():F3}%.");

        Assert.True(thresholdFailures.Count == 0,
            $"dt stability thresholds failed:{Environment.NewLine}{string.Join(Environment.NewLine, thresholdFailures)}");
    }

    private void ReportCrossDtThresholdFailures(DtStabilityResults results, List<string> thresholdFailures)
    {
        foreach (var zone in results.GLoCZones)
        {
            var eventTimes = zone.Results.Where(result => result.TimeToGLoCSeconds is not null)
                .Select(result => result.TimeToGLoCSeconds!.Value)
                .ToArray();
            if (eventTimes.Length != zone.Results.Count)
            {
                thresholdFailures.Add($"{zone.TargetGz:F1} Gz: GLoC was not reached for at least one dt.");
                continue;
            }

            var spread = eventTimes.Max() - eventTimes.Min();
            _output.WriteLine($"{zone.TargetGz:F1} Gz cross-dt GLoC spread: {spread:F3}s.");
            if (spread > DtStabilityConfiguration.GLoCSpreadToleranceSeconds)
                thresholdFailures.Add($"{zone.TargetGz:F1} Gz: cross-dt GLoC spread {spread:F3}s exceeds {DtStabilityConfiguration.GLoCSpreadToleranceSeconds:F3}s.");
        }

        foreach (var zone in results.NonGLoCZones)
        {
            var maximumSpread = Enumerable.Range(0, zone.Results[0].ConsciousnessSamples.Count)
                .Select(sampleIndex => zone.Results.Max(result => result.ConsciousnessSamples[sampleIndex]) - zone.Results.Min(result => result.ConsciousnessSamples[sampleIndex]))
                .Max();
            var finalSpread = zone.Results.Max(result => result.ConsciousnessSamples[^1]) - zone.Results.Min(result => result.ConsciousnessSamples[^1]);
            _output.WriteLine($"{zone.TargetGz:F1} Gz cross-dt consciousness spread: maximum {maximumSpread:F5}; final {finalSpread:F5}.");

            if (maximumSpread > DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance)
                thresholdFailures.Add($"{zone.TargetGz:F1} Gz: cross-dt consciousness spread {maximumSpread:F5} exceeds {DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance:F5}.");
            if (finalSpread > DtStabilityConfiguration.FinalConsciousnessSpreadTolerance)
                thresholdFailures.Add($"{zone.TargetGz:F1} Gz: final cross-dt consciousness spread {finalSpread:F5} exceeds {DtStabilityConfiguration.FinalConsciousnessSpreadTolerance:F5}.");
        }
    }

    private void ReportPassingDtMargin(IReadOnlyList<DtDefinition> definitions, int baselineIndex, IReadOnlyList<bool> candidatePasses)
    {
        var lowerIndex = baselineIndex;
        var upperIndex = baselineIndex;

        while (lowerIndex > 0 && candidatePasses[lowerIndex - 1])
            lowerIndex--;
        while (upperIndex < definitions.Count - 1 && candidatePasses[upperIndex + 1])
            upperIndex++;

        var baselineStatus = candidatePasses[baselineIndex] ? "passes" : "fails";
        _output.WriteLine(
            $"250 ms baseline {baselineStatus}; contiguous passing dt margin: {definitions[lowerIndex].DisplayName} to {definitions[upperIndex].DisplayName}.");
    }
}
