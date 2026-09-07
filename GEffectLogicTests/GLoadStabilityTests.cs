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
    internal static readonly double[] NoGLoCZones = [-2.0, -1.0, 0.0, 1.0, 2.0, 3.0];
    internal static readonly double[] GLoCZones = [-6.0, -5.0, -4.0, -3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0];

    public static IEnumerable<object[]> NoGLoCZoneData => NoGLoCZones.Select(targetGz => new object[] { targetGz });
    public static IEnumerable<object[]> GLoCZoneData => GLoCZones.Select(targetGz => new object[] { targetGz });

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
    [MemberData(nameof(NoGLoCZoneData))]
    public void NoGLoCStable(double targetGz)
    {
        var logicInstance = RunUntilStable(targetGz);

        Assert.False(logicInstance.IsUnconscious,
            $"The model lost consciousness before stabilizing at {targetGz:F1} Gz.");
    }

    [Theory]
    [MemberData(nameof(GLoCZoneData))]
    public void GLoCStable(double targetGz)
    {
        var logicInstance = RunUntilStable(targetGz);

        Assert.True(logicInstance.IsUnconscious,
            $"The model did not lose consciousness before stabilizing at {targetGz:F1} Gz.");
    }

    [Fact]
    public void VisualEffectsAreDirectionalAndGraded()
    {
        var mildRedout = RunFor(-1.0, 5.0);
        var moderateRedout = RunFor(-3.0, 5.0);
        var strongRedout = RunFor(-5.0, 5.0);
        var tunnelVision = RunFor(6.0, 12.0);

        Assert.True(mildRedout.VisualRedoutLevel > 0.0);
        Assert.True(moderateRedout.VisualRedoutLevel > mildRedout.VisualRedoutLevel);
        Assert.True(strongRedout.VisualRedoutLevel > moderateRedout.VisualRedoutLevel);
        Assert.True(tunnelVision.VisualTunnelVisionLevel > 0.1);
        Assert.InRange(tunnelVision.VisualRedoutLevel, 0.0, 1e-6);
        Assert.InRange(strongRedout.VisualTunnelVisionLevel, 0.0, 1e-6);
    }

    [Fact]
    public void TunnelVisionAndRedoutCanOverlap()
    {
        var logicInstance = RunFor(6.0, 12.0);

        Advance(logicInstance, -5.0, 1.0);

        Assert.True(logicInstance.VisualTunnelVisionLevel > 0.0);
        Assert.True(logicInstance.VisualRedoutLevel > 0.0);
    }

    [Fact]
    public void VisualLoCUsesConsciousnessHysteresis()
    {
        var logicInstance = CreateLogicInstance();

        while (logicInstance.ConsciousnessLevel > 0.3 && logicInstance.Time < 30.0)
            logicInstance.Update(0.1, 0.0, 0.0, 9.0);

        Assert.True(logicInstance.ConsciousnessLevel <= 0.3);
        Assert.False(logicInstance.IsUnconscious);
        Assert.InRange(logicInstance.VisualLoCLevel, 0.0, 1.0);
        Assert.NotEqual(0.0, logicInstance.VisualLoCLevel);
        Assert.NotEqual(1.0, logicInstance.VisualLoCLevel);
        var visualLoCMaximum = Math.Pow(
            (LogicSettings.ConsciousnessRecoveryThreshold - logicInstance.ConsciousnessLevel) /
            (LogicSettings.ConsciousnessRecoveryThreshold - LogicSettings.ConsciousnessLossThreshold),
            LogicSettings.VisualLoCConsciousnessExponent);
        Assert.True(logicInstance.VisualLoCLevel <= visualLoCMaximum);

        while (!logicInstance.IsUnconscious && logicInstance.Time < 60.0)
            logicInstance.Update(0.1, 0.0, 0.0, 9.0);

        Assert.True(logicInstance.IsUnconscious);
        Assert.Equal(1.0, logicInstance.VisualLoCLevel);

        var recoveryStartTime = logicInstance.Time;
        while (logicInstance.ConsciousnessLevel <= 0.3 && logicInstance.Time - recoveryStartTime < 120.0)
            logicInstance.Update(0.1, 0.0, 0.0, 1.0);

        Assert.True(logicInstance.ConsciousnessLevel > 0.3);
        Assert.True(logicInstance.IsUnconscious);
        Assert.Equal(1.0, logicInstance.VisualLoCLevel);

        var visualLoCBeforeRecovery = logicInstance.VisualLoCLevel;
        while (logicInstance.IsUnconscious && logicInstance.Time - recoveryStartTime < 120.0)
            logicInstance.Update(0.1, 0.0, 0.0, 1.0);

        Assert.False(logicInstance.IsUnconscious);
        Assert.True(logicInstance.ConsciousnessLevel > LogicSettings.ConsciousnessRecoveryThreshold);
        Assert.InRange(logicInstance.VisualLoCLevel, 0.0, visualLoCBeforeRecovery);
        Assert.NotEqual(0.0, logicInstance.VisualLoCLevel);

        var visualLoCAfterRecovery = logicInstance.VisualLoCLevel;
        logicInstance.Update(0.1, 0.0, 0.0, 1.0);
        Assert.InRange(
            visualLoCAfterRecovery - logicInstance.VisualLoCLevel,
            0.0,
            LogicSettings.VisualLoCDecreaseRate * 0.1 + 1e-9);

        while (logicInstance.VisualLoCLevel > 0.0 && logicInstance.Time - recoveryStartTime < 120.0)
            logicInstance.Update(0.1, 0.0, 0.0, 1.0);

        Assert.Equal(0.0, logicInstance.VisualLoCLevel);
    }

    [Fact]
    public void ResetClearsVisualEffectsAndUnconsciousState()
    {
        var logicInstance = RunFor(9.0, 20.0);

        logicInstance.Reset();

        Assert.Equal(0.0, logicInstance.VisualTunnelVisionLevel);
        Assert.Equal(0.0, logicInstance.VisualRedoutLevel);
        Assert.Equal(0.0, logicInstance.VisualLoCLevel);
        Assert.Equal(0.0, logicInstance.VisualGrayscaleLevel);
        Assert.Equal(0.0, logicInstance.VisualFilmGrainLevel);
        Assert.Equal(0.0, logicInstance.VisualBlurLevel);
        Assert.False(logicInstance.IsUnconscious);
    }

    private GEffectsLogicInstance RunFor(double gz, double duration)
    {
        var logicInstance = CreateLogicInstance();
        Advance(logicInstance, gz, duration);
        return logicInstance;
    }

    private GEffectsLogicInstance CreateLogicInstance() => new(new LogicLogging(_output));

    private static void Advance(GEffectsLogicInstance logicInstance, double gz, double duration)
    {
        for (var elapsed = 0.0; elapsed < duration; elapsed += 0.1)
            logicInstance.Update(0.1, 0.0, 0.0, gz);
    }
}
