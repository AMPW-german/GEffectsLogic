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

using GEffectsLogic;
using GEffectsLogic.Logging;

namespace GEffectLogicTests;

internal static class DtStabilityConfiguration
{
    internal const double RampStartGz = 1.0;
    internal const double RampRateGzPerSecond = 1.0;
    internal const double GLoCConsciousnessThreshold = 0.01;
    internal const int SimulationDurationSeconds = 30 * 60;
    internal const int SampleIntervalSeconds = 1;
    internal const int BaselineStepsPerSecond = 4;
    internal const double GLoCSpreadToleranceSeconds = 1.5;
    internal const double IntervalConsciousnessSpreadTolerance = 0.1;
    internal const double FinalConsciousnessSpreadTolerance = 0.025;

    internal static readonly DtDefinition[] Definitions =
    [
        new(100, "10 ms"),
        new(50, "20 ms"),
        new(40, "25 ms"),
        new(25, "40 ms"),
        new(20, "50 ms"),
        new(16, "62.5 ms"),
        new(10, "100 ms"),
        new(9, "111.111 ms"),
        new(8, "125 ms"),
        new(7, "142.857 ms"),
        new(6, "166.667 ms"),
        new(5, "200 ms"),
        new(BaselineStepsPerSecond, "250 ms"),
        new(3, "333.333 ms"),
        new(2, "500 ms"),
        new(1, "1000 ms")
    ];
}

internal sealed record DtDefinition(int StepsPerSecond, string DisplayName)
{
    internal double Seconds => 1.0 / StepsPerSecond;
    internal double Milliseconds => Seconds * 1000.0;
}

internal sealed record GLoCDtResult(DtDefinition Dt, double? TimeToGLoCSeconds);

internal sealed record NonGLoCDtResult(DtDefinition Dt, IReadOnlyList<double> ConsciousnessSamples);

internal sealed record GLoCZoneDtResults(double TargetGz, IReadOnlyList<GLoCDtResult> Results);

internal sealed record NonGLoCZoneDtResults(double TargetGz, IReadOnlyList<NonGLoCDtResult> Results);

internal sealed class DtStabilityResults
{
    internal DtStabilityResults(
        IReadOnlyList<GLoCZoneDtResults> gLoCZones,
        IReadOnlyList<NonGLoCZoneDtResults> nonGLoCZones)
    {
        GLoCZones = gLoCZones;
        NonGLoCZones = nonGLoCZones;
    }

    internal IReadOnlyList<GLoCZoneDtResults> GLoCZones { get; }
    internal IReadOnlyList<NonGLoCZoneDtResults> NonGLoCZones { get; }
}

internal static class DtStabilitySimulationResults
{
    private static readonly Lazy<DtStabilityResults> CachedResults = new(CreateResults);
    private static readonly Logger SilentLogger = new DtStabilityLogger();

    internal static DtStabilityResults Get() => CachedResults.Value;

    private static DtStabilityResults CreateResults()
    {
        var gLoCZones = GLoadStabilityTests.GLoCZones
            .Select(targetGz => new GLoCZoneDtResults(targetGz, DtStabilityConfiguration.Definitions
                .Select(dt => RunGLoCScenario(targetGz, dt))
                .ToArray()))
            .ToArray();

        var nonGLoCZones = GLoadStabilityTests.NoGLoCZones
            .Select(targetGz => new NonGLoCZoneDtResults(targetGz, DtStabilityConfiguration.Definitions
                .Select(dt => RunNonGLoCScenario(targetGz, dt))
                .ToArray()))
            .ToArray();

        return new DtStabilityResults(gLoCZones, nonGLoCZones);
    }

    private static GLoCDtResult RunGLoCScenario(double targetGz, DtDefinition dt)
    {
        GEffectsLogicInstance logicInstance = new(SilentLogger);
        var maximumUpdates = DtStabilityConfiguration.SimulationDurationSeconds * dt.StepsPerSecond;

        for (var updateIndex = 0; updateIndex < maximumUpdates; updateIndex++)
        {
            logicInstance.Update(dt.Seconds, 0, 0, GetGzAtUpdate(targetGz, dt, updateIndex));
            if (logicInstance.ConsciousnessLevel <= DtStabilityConfiguration.GLoCConsciousnessThreshold)
                return new GLoCDtResult(dt, logicInstance.Time);
        }

        return new GLoCDtResult(dt, null);
    }

    private static NonGLoCDtResult RunNonGLoCScenario(double targetGz, DtDefinition dt)
    {
        GEffectsLogicInstance logicInstance = new(SilentLogger);
        var samples = new double[DtStabilityConfiguration.SimulationDurationSeconds / DtStabilityConfiguration.SampleIntervalSeconds];

        for (var updateIndex = 0; updateIndex < DtStabilityConfiguration.SimulationDurationSeconds * dt.StepsPerSecond; updateIndex++)
        {
            logicInstance.Update(dt.Seconds, 0, 0, GetGzAtUpdate(targetGz, dt, updateIndex));

            if ((updateIndex + 1) % (dt.StepsPerSecond * DtStabilityConfiguration.SampleIntervalSeconds) == 0)
                samples[(updateIndex + 1) / (dt.StepsPerSecond * DtStabilityConfiguration.SampleIntervalSeconds) - 1] = logicInstance.ConsciousnessLevel;
        }

        return new NonGLoCDtResult(dt, samples);
    }

    private static double GetGzAtUpdate(double targetGz, DtDefinition dt, int updateIndex)
    {
        var rampDurationSeconds = Math.Abs(targetGz - DtStabilityConfiguration.RampStartGz) /
                                  DtStabilityConfiguration.RampRateGzPerSecond;
        var elapsedSeconds = updateIndex * dt.Seconds;

        if (elapsedSeconds >= rampDurationSeconds)
            return targetGz;

        return DtStabilityConfiguration.RampStartGz +
               Math.Sign(targetGz - DtStabilityConfiguration.RampStartGz) *
               elapsedSeconds * DtStabilityConfiguration.RampRateGzPerSecond;
    }
}

internal sealed class DtStabilityLogger : Logger
{
    public override bool LogStr(string message, GEffectsLogicInstance logicInstance, LogLevel level = LogLevel.Debug)
    {
        return false;
    }
}
