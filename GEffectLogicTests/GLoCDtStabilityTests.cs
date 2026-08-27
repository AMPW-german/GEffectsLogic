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
public class GLoCDtStabilityTests
{
    private readonly ITestOutputHelper _output;

    public GLoCDtStabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> GLoCZoneData => GLoadStabilityTests.GLoCZoneData;

    [Theory]
    [MemberData(nameof(GLoCZoneData))]
    public void GLoCEventTimeIsStableAcrossDt(double targetGz)
    {
        var zoneResults = DtStabilitySimulationResults.Get().GLoCZones.Single(zone => zone.TargetGz == targetGz);
        var missingGLoC = zoneResults.Results.Where(result => result.TimeToGLoCSeconds is null).ToArray();

        foreach (var result in zoneResults.Results)
        {
            var eventTime = result.TimeToGLoCSeconds is { } time ? $"{time:F3}s" : "not reached";
            _output.WriteLine($"{result.Dt.DisplayName}: GLoC at {eventTime}");
        }

        Assert.True(missingGLoC.Length == 0,
            $"GLoC was not reached at {targetGz:F1} Gz within {DtStabilityConfiguration.SimulationDurationSeconds}s for: {string.Join(", ", missingGLoC.Select(result => result.Dt.DisplayName))}.");

        var eventTimes = zoneResults.Results.Select(result => result.TimeToGLoCSeconds!.Value).ToArray();
        var spreadSeconds = eventTimes.Max() - eventTimes.Min();
        _output.WriteLine($"GLoC time spread at {targetGz:F1} Gz: {spreadSeconds:F3}s (limit {DtStabilityConfiguration.GLoCSpreadToleranceSeconds:F3}s).");

        Assert.True(spreadSeconds <= DtStabilityConfiguration.GLoCSpreadToleranceSeconds,
            $"GLoC time spread at {targetGz:F1} Gz was {spreadSeconds:F3}s, exceeding the {DtStabilityConfiguration.GLoCSpreadToleranceSeconds:F3}s limit.");
    }
}
