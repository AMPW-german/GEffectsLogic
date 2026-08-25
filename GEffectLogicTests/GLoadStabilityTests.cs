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

using GEffectLogicTests.Logging;
using GEffectsLogic;
using GEffectsLogic.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests;

public class GLoadStabilityTests
{
    private const double TimeStep = 0.25;
    private const double MaximumStabilizationTime = 3600.0;

    private readonly ITestOutputHelper _output;
    private TestLogging _testLogger;

    public GLoadStabilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private GEffectsLogicInstance RunUntilStable(double targetGz)
    {
        _testLogger = new TestLogging(_output);
        LogicLogging logicLogger = new(_output);
        LogicSettings.DebugMode = false;
        GEffectsLogicInstance logicInstance = new(logicLogger);
        var rampDuration = Math.Abs(targetGz);

        for (var rampTime = 0.0; rampTime < rampDuration && !logicInstance.IsStable; rampTime += TimeStep)
        {
            var currentGz = Math.Clamp(rampTime, 0.0, rampDuration) * Math.Sign(targetGz);
            logicInstance.Update(TimeStep, 0, 0, currentGz);
        }

        while (!logicInstance.IsStable && logicInstance.Time < MaximumStabilizationTime)
{
            logicInstance.Update(TimeStep, 0, 0, targetGz);
            _testLogger.LogStr($"Time: {logicInstance.Time:F1}s, Gz: {targetGz:F1}, Consciousness Level: {logicInstance.ConsciousnessLevel:F3}, IsStable: {logicInstance.IsStable}", Logger.LogLevel.Info);

        }            

        Assert.True(logicInstance.IsStable,
            $"The model did not stabilize at {targetGz:F1} Gz within {MaximumStabilizationTime:F0} seconds.");
        return logicInstance;
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void NoGLoCStable(double targetGz)
    {
        var logicInstance = RunUntilStable(targetGz);

        Assert.False(logicInstance.IsUnconsciouss,
            $"The model lost consciousness before stabilizing at {targetGz:F1} Gz.");
    }

    [Theory]
    [InlineData(-6.0)]
    [InlineData(-5.0)]
    [InlineData(-4.0)]
    [InlineData(-3.0)]
    [InlineData(4.0)]
    [InlineData(5.0)]
    [InlineData(6.0)]
    [InlineData(7.0)]
    [InlineData(8.0)]
    [InlineData(9.0)]
    public void GLoCStable(double targetGz)
    {
        var logicInstance = RunUntilStable(targetGz);

        Assert.True(logicInstance.IsUnconsciouss,
            $"The model did not lose consciousness before stabilizing at {targetGz:F1} Gz.");
    }
}
