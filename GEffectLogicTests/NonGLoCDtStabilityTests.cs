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
public class NonGLoCDtStabilityTests
{
    private readonly ITestOutputHelper _output;

    public NonGLoCDtStabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> NoGLoCZoneData => GLoadStabilityTests.NoGLoCZoneData;

    [Theory]
    [MemberData(nameof(NoGLoCZoneData))]
    public void ConsciousnessIsStableAcrossDt(double targetGz)
    {
        var zoneResults = DtStabilitySimulationResults.Get().NonGLoCZones.Single(zone => zone.TargetGz == targetGz);
        var sampleCount = zoneResults.Results[0].ConsciousnessSamples.Count;
        var maximumSpread = 0.0;
        var maximumSpreadSampleIndex = 0;

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var samples = zoneResults.Results.Select(result => result.ConsciousnessSamples[sampleIndex]).ToArray();
            var spread = samples.Max() - samples.Min();
            if (spread > maximumSpread)
            {
                maximumSpread = spread;
                maximumSpreadSampleIndex = sampleIndex;
            }
        }

        var finalSamples = zoneResults.Results.Select(result => result.ConsciousnessSamples[^1]).ToArray();
        var finalSpread = finalSamples.Max() - finalSamples.Min();

        foreach (var result in zoneResults.Results)
            _output.WriteLine($"{result.Dt.DisplayName}: final consciousness {result.ConsciousnessSamples[^1]:F5}");

        _output.WriteLine(
            $"Maximum consciousness spread at {targetGz:F1} Gz: {maximumSpread:F5} at {(maximumSpreadSampleIndex + 1) * DtStabilityConfiguration.SampleIntervalSeconds}s (limit {DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance:F5}).");
        _output.WriteLine(
            $"Final consciousness spread at {targetGz:F1} Gz: {finalSpread:F5} (limit {DtStabilityConfiguration.FinalConsciousnessSpreadTolerance:F5}).");

        Assert.True(maximumSpread <= DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance,
            $"Consciousness spread at {targetGz:F1} Gz reached {maximumSpread:F5} at {(maximumSpreadSampleIndex + 1) * DtStabilityConfiguration.SampleIntervalSeconds}s, exceeding the {DtStabilityConfiguration.IntervalConsciousnessSpreadTolerance:F5} interval limit.");
        Assert.True(finalSpread <= DtStabilityConfiguration.FinalConsciousnessSpreadTolerance,
            $"Final consciousness spread at {targetGz:F1} Gz was {finalSpread:F5}, exceeding the {DtStabilityConfiguration.FinalConsciousnessSpreadTolerance:F5} limit.");
    }
}
