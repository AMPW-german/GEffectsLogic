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

using GEffectLogicTests.Logging;
using GEffectsLogic;
using GEffectsLogic.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests;

public class GLoCDurationTests
{
    // TODO:
    // - more test cases
    // - extension to the sequence solver to support a "GLoC" flag at the end that indicates that in the given sequence step the GLoC is expected to occur, e.g. [1 9 9 GLoC],[1 GLoC],[-] (this would fail after 10 seconds)

    private readonly ITestOutputHelper _output;

    public GLoCDurationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string StatusLine(GEffectsLogicInstance logicInstance) =>
        $"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, " +
        $"lastGx: {logicInstance.LastGx:F2}, lastGy: {logicInstance.LastGy:F2}, lastGz: {logicInstance.LastGz:F2}";

    private static void LogEndOfTest(GEffectsLogicInstance logicInstance, double expectedStart,
        double expectedEnd, List<string> infoStrings, TestLogging logger)
    {
        var assertLevel = Logger.LogLevel.Info;
        logger.LogStr($"{logicInstance.Time:F1}s: consciousness level at {logicInstance.ConsciousnessLevel:F4} " +
            $"at Gx {logicInstance.LastGx:F2}, Gy {logicInstance.LastGy:F2}, Gz {logicInstance.LastGz:F2}", assertLevel);
        logger.LogStr($"Expected event time range: {expectedStart:F1}s to {expectedEnd:F1}s", assertLevel);
        logger.LogStr("", assertLevel);
        foreach (var s in infoStrings) logger.LogStr(s, Logger.LogLevel.Info);

        if (logicInstance.Time < expectedStart || logicInstance.Time >= expectedEnd)
        {
            Assert.Fail(
                $"Event at {logicInstance.Time:F1}s, which is outside the expected range of {expectedStart:F1}s to {expectedEnd:F1}s.");
            assertLevel = Logger.LogLevel.Error;
        }
    }

    private static double RampAxis(double start, double end, double time)
    {
        var duration = Math.Abs(end - start);
        return duration < 1e-9 ? end : start + (end - start) * Math.Min(time / duration, 1.0);
    }

    /// <summary>
    ///     Ramps each axis from its start value to its end value at ~1 G/s (per axis), then holds the
    ///     end values until <paramref name="eventPredicate"/> fires or <paramref name="maxTime"/> is
    ///     reached. The per-step status lines are exposed via <paramref name="infoStrings"/> for
    ///     failure diagnostics.
    /// </summary>
    private static GEffectsLogicInstance RunPlataueSequence(
        double startGx, double endGx, double startGy, double endGy, double startGz, double endGz,
        double maxTime, ITestOutputHelper output, Func<GEffectsLogicInstance, bool> eventPredicate,
        out List<string> infoStrings)
    {
        LogicLogging logicLogger = new(output);
        LogicSettings.DebugMode = false;
        GEffectsLogicInstance logicInstance = new(logicLogger);
        infoStrings = [];

        var rampDuration = Math.Max(Math.Abs(endGx - startGx),
            Math.Max(Math.Abs(endGy - startGy), Math.Abs(endGz - startGz)));

        for (double t = 0; t < rampDuration; t += 0.1)
        {
            logicInstance.Update(0.1,
                RampAxis(startGx, endGx, t),
                RampAxis(startGy, endGy, t),
                RampAxis(startGz, endGz, t));
            infoStrings.Add(StatusLine(logicInstance));
            if (eventPredicate(logicInstance)) break;
        }

        while (!eventPredicate(logicInstance) && logicInstance.Time < maxTime)
        {
            logicInstance.Update(0.25, endGx, endGy, endGz);
            infoStrings.Add(StatusLine(logicInstance));
        }

        return logicInstance;
    }

    private static void PlataueSequenceGLoC(
        double startGx, double endGx, double startGy, double endGy, double startGz, double endGz,
        double expectedGLoCTimeStart, double expectedGLoCTimeEnd,
        ITestOutputHelper output, Func<GEffectsLogicInstance, bool> gLocPredicate)
    {
        TestLogging loggerInstance = new(output);
        var logicInstance = RunPlataueSequence(startGx, endGx, startGy, endGy, startGz, endGz,
            expectedGLoCTimeEnd, output, gLocPredicate, out var infoStrings);
        LogEndOfTest(logicInstance, expectedGLoCTimeStart, expectedGLoCTimeEnd, infoStrings, loggerInstance);
    }

    private static void PlataueSequenceGLoC(double startGz, double endGz,
        double expectedGLoCTimeStart, double expectedGLoCTimeEnd, ITestOutputHelper output)
    {
        PlataueSequenceGLoC(0.0, 0.0, 0.0, 0.0, startGz, endGz,
            expectedGLoCTimeStart, expectedGLoCTimeEnd, output,
            logicInstance => logicInstance.ConsciousnessLevel <= 0.01);
    }

    private void AssertNoGLoCEvent(double endGx, double endGy, double startGz, double endGz, double holdTime)
    {
        var logicInstance = RunPlataueSequence(0.0, endGx, 0.0, endGy, startGz, endGz, holdTime, _output,
            instance => instance.InSuddenLoC || instance.IsUnconscious || instance.IsDead, out _);
        Assert.False(logicInstance.InSuddenLoC || logicInstance.IsUnconscious || logicInstance.IsDead,
            $"Unexpected GLoC event at {logicInstance.Time:F1}s under Gx {endGx}, Gy {endGy}, Gz {endGz} " +
            $"(consciousness: {logicInstance.ConsciousnessLevel:F4}).");
    }

    [Theory]
    [InlineData(5.0, 25.0, 35.0)]
    [InlineData(9.0, 5.0, 14.0)]
    [InlineData(-3.0, 20.0, 250.0)]
    [InlineData(-4.0, 6.0, 11.0)]
    [InlineData(-5.0, 6.0, 11.0)]
    [InlineData(-6.0, 7.0, 11.0)]
    public void GLoCDuration(double endG, double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(1.0, endG, expectedGLoCTimeStart, expectedGLoCTimeEnd, _output);
    }

    /// <summary>
    ///     Extreme sustained Gx pushes the multi-axis sudden G-LOC accumulator over its trigger
    ///     threshold. Pure Gx never reaches full unconsciousness on its own (lung oxygenation
    ///     impairment plateaus consciousness around 0.37), so the GLoC event here is the
    ///     InSuddenLoC trigger. Gz stays at 0 to isolate the Gx axis.
    /// </summary>
    [Theory]
    [InlineData(13.0, 15.0, 28.0)]
    [InlineData(15.0, 14.0, 22.0)]
    [InlineData(20.0, 14.0, 22.0)]
    public void GxSuddenGLoCDuration(double endGx, double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, endGx, 0.0, 0.0, 0.0, 0.0, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.InSuddenLoC);
    }

    /// <summary>
    ///     Moderate Gx keeps the sudden G-LOC risk below the accumulation threshold; the residual
    ///     lung impairment only degrades consciousness partially, so no GLoC occurs.
    /// </summary>
    [Theory]
    [InlineData(5.0)]
    [InlineData(8.0)]
    [InlineData(12.0)]
    public void GxBelowSuddenLoCThresholdNoGLoC(double endGx)
    {
        AssertNoGLoCEvent(endGx, 0.0, 0.0, 0.0, 100.0);
    }

    /// <summary>
    ///     Sustained Gy during level flight (Gz = 1): Gy cuts the Gz tolerance so heavily that even
    ///     the 1 G baseline becomes a GLoC-level effective load. The sign of Gy is irrelevant - the
    ///     model only uses its magnitude.
    /// </summary>
    [Theory]
    [InlineData(2.0, 3.5, 7.5)]
    [InlineData(-4.0, 3.5, 7.0)]
    [InlineData(5.0, 3.0, 6.5)]
    [InlineData(8.0, 3.0, 6.5)]
    public void GyGLoCDuration(double endGy, double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, 0.0, 0.0, endGy, 1.0, 1.0, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.IsUnconscious);
    }

    /// <summary>
    ///     Below the tolerance-collapse point, Gy during level flight (Gz = 1) only degrades
    ///     consciousness partially without causing GLoC. At Gz = 0 there is no Gz load for the
    ///     tolerance reduction to act on, so the same Gy levels stay fully conscious.
    /// </summary>
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(2.0, 0.0)]
    [InlineData(3.0, 0.0)]
    [InlineData(3.5, 0.0)]
    public void GyBelowThresholdNoGLoC(double endGy, double gz)
    {
        AssertNoGLoCEvent(0.0, endGy, gz, gz, 60.0);
    }

    /// <summary>
    ///     Pure Gy without Gz loading only acts through the sudden G-LOC accumulator; sustained
    ///     Gy above ~3.65 keeps the risk above the accumulation threshold long enough to trigger.
    /// </summary>
    [Theory]
    [InlineData(4.0, 6.0, 10.5)]
    [InlineData(5.0, 6.0, 10.5)]
    public void GySuddenGLoCDuration(double endGy, double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, 0.0, 0.0, endGy, 0.0, 0.0, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.InSuddenLoC);
    }

    /// <summary>
    ///     Gy at or above ~11 G lets the neck fatigue accumulator hold the death level long enough,
    ///     which results in a permanent GLoC after the configured death delay. (10 G only brushes
    ///     the death level transiently and never completes it.)
    /// </summary>
    [Theory]
    [InlineData(11.0, 9.0, 15.0)]
    [InlineData(12.0, 9.0, 15.0)]
    [InlineData(20.0, 9.0, 15.0)]
    public void GyNeckFatigueDeathDuration(double endGy, double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, 0.0, 0.0, endGy, 0.0, 0.0, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.IsDead);
    }

    /// <summary>
    ///     Gy on top of a Gz load divides the Gz tolerance and collapses the GLoC time compared to
    ///     pure Gz (pure Gz 5 takes ~16 s to unconsciousness).
    /// </summary>
    [Theory]
    [InlineData(1.0, 5.0, 4.0, 8.0)]
    [InlineData(-1.0, 5.0, 4.0, 8.0)]
    [InlineData(2.0, 5.0, 3.0, 6.5)]
    [InlineData(1.0, 4.0, 4.0, 8.5)]
    [InlineData(0.5, -4.0, 6.5, 12.0)]
    [InlineData(1.0, -4.0, 6.0, 11.0)]
    public void GyPlusGzGLoCDuration(double endGy, double endGz,
        double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, 0.0, 0.0, endGy, 1.0, endGz, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.IsUnconscious);
    }

    /// <summary>
    ///     Gx raises the Gz tolerance and stretches the GLoC time compared to pure Gz (pure Gz 5
    ///     takes ~16 s, pure Gz 7 ~7 s to unconsciousness).
    /// </summary>
    [Theory]
    [InlineData(1.0, 7.0, 6.0, 10.5)]
    [InlineData(2.0, 7.0, 7.0, 12.5)]
    [InlineData(4.0, 7.0, 17.0, 30.0)]
    [InlineData(2.0, 5.0, 90.0, 200.0)]
    [InlineData(1.0, -4.0, 8.0, 14.0)]
    public void GxPlusGzGLoCDuration(double endGx, double endGz,
        double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, endGx, 0.0, 0.0, 1.0, endGz, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.IsUnconscious);
    }

    /// <summary>
    ///     Gx + Gy with no Gz load: each axis alone stays below the sudden G-LOC risk threshold, but
    ///     the summed risk pushes the accumulator over the trigger.
    /// </summary>
    [Theory]
    [InlineData(10.0, 2.5, 10.0, 17.0)]
    [InlineData(7.0, 3.0, 11.0, 20.0)]
    [InlineData(10.0, 3.5, 7.0, 12.0)]
    public void GxPlusGySuddenGLoCDuration(double endGx, double endGy,
        double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, endGx, 0.0, endGy, 0.0, 0.0, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.InSuddenLoC);
    }

    /// <summary>
    ///     8 Gx + 2 Gy sums to a sudden G-LOC risk just below the accumulation threshold, so nothing
    ///     happens even over a long hold.
    /// </summary>
    [Fact]
    public void GxPlusGyBelowThresholdNoGLoC()
    {
        AssertNoGLoCEvent(8.0, 2.0, 0.0, 0.0, 60.0);
    }

    /// <summary>
    ///     All three axes together: Gy still dominates via the tolerance collapse, while large Gx
    ///     partially compensates by raising the tolerance.
    /// </summary>
    [Theory]
    [InlineData(2.0, 1.0, 5.0, 5.0, 9.5)]
    [InlineData(4.0, 1.0, 5.0, 15.0, 28.0)]
    [InlineData(2.0, 2.0, 4.0, 3.0, 6.5)]
    [InlineData(13.0, 1.5, 5.0, 4.0, 7.5)]
    public void MultiAxisGLoCDuration(double endGx, double endGy, double endGz,
        double expectedGLoCTimeStart, double expectedGLoCTimeEnd)
    {
        PlataueSequenceGLoC(0.0, endGx, 0.0, endGy, 1.0, endGz, expectedGLoCTimeStart,
            expectedGLoCTimeEnd, _output, instance => instance.IsUnconscious);
    }

    /// <summary>
    ///     Ordering check on the tolerance modifiers: the same 5 Gz load loses consciousness fastest
    ///     with added Gy, slowest with added Gx.
    /// </summary>
    [Fact]
    public void GxAndGyModulateGzGLoCTime()
    {
        var withGy = RunPlataueSequence(0.0, 0.0, 0.0, 1.0, 1.0, 5.0, 200.0, _output,
            instance => instance.IsUnconscious, out _).Time;
        var pureGz = RunPlataueSequence(0.0, 0.0, 0.0, 0.0, 1.0, 5.0, 200.0, _output,
            instance => instance.IsUnconscious, out _).Time;
        var withGx = RunPlataueSequence(0.0, 2.0, 0.0, 0.0, 1.0, 5.0, 200.0, _output,
            instance => instance.IsUnconscious, out _).Time;

        Assert.True(withGy < pureGz,
            $"Gy-accelerated GLoC at {withGy:F1}s should precede the pure Gz GLoC at {pureGz:F1}s.");
        Assert.True(pureGz < withGx,
            $"Pure Gz GLoC at {pureGz:F1}s should precede the Gx-protected GLoC at {withGx:F1}s.");
    }

    /// <summary>
    ///     Enough Gx lifts the effective Gz tolerance so much that a Gz 5 level GLoC does not occur
    ///     within several times the pure-Gz GLoC window (pure Gz 5 GLoCs in ~16-35 s).
    /// </summary>
    [Fact]
    public void GxProtectionPreventsGzGLoC()
    {
        AssertNoGLoCEvent(4.0, 0.0, 1.0, 5.0, 60.0);
    }

    /// <summary>
    ///     [1 9 9],[6],[9 1 9],[-]
    ///     Find when consciousness is restored after GLoC (consciousness > 0.75)
    /// </summary>
    [Fact]
    public void GLoC9GRecovery()
    {
        TestLogging loggerInstance = new(_output);
        LogicLogging logicLogger = new(_output);
        LogicSettings.DebugMode = false;
        GEffectsLogicInstance logicInstance = new(logicLogger);
        List<string> infoStrings = [];
        var consciousnessRecoveryStartTime = 0.0;
        // First phase: 1 to 9 Gz over 9 seconds
        for (var t = 0.0; t < 9.0; t += 0.1)
        {
            logicInstance.Update(0.1, 0, 0, 1.0 + (9.0 - 1.0) * (t / 9.0));
            infoStrings.Add(StatusLine(logicInstance));
        }

        // Second phase: hold at 9 Gz for 6 seconds
        for (var t = 0.0; t < 6.0; t += 0.1)
        {
            logicInstance.Update(0.1, 0, 0, 9.0);
            infoStrings.Add(StatusLine(logicInstance));
        }

        // Third phase: 9 to 1 Gz over 9 seconds
        for (var t = 9.0; t > 0.0; t -= 0.1)
        {
            logicInstance.Update(0.1, 0, 0, 1.0 + (9.0 - 1.0) * (t / 9.0));
            infoStrings.Add(StatusLine(logicInstance));
            if (logicInstance.ConsciousnessLevel > 0.0001 && consciousnessRecoveryStartTime == 0.0)
                consciousnessRecoveryStartTime = logicInstance.Time;
        }

        // Fourth phase: hold at 1 Gz until recovery
        var recoveryStartTime = logicInstance.Time;
        var recoveryStartConsciousness = logicInstance.ConsciousnessLevel;
        while (logicInstance.ConsciousnessLevel <= 0.75)
        {
            logicInstance.Update(0.1, 0, 0, 1.0);
            infoStrings.Add(StatusLine(logicInstance));
            if (logicInstance.Time - recoveryStartTime > 60) // fail if recovery takes too long
            {
                LogEndOfTest(logicInstance, recoveryStartTime, recoveryStartTime + 60, infoStrings, loggerInstance);
                break;
            }
        }

        loggerInstance.LogStr(
            $"Consciousness recovery started at {consciousnessRecoveryStartTime:F2}s with consciousness level {recoveryStartConsciousness:F4} at {recoveryStartTime:F2}s",
            Logger.LogLevel.Info);
        LogEndOfTest(logicInstance, recoveryStartTime, recoveryStartTime + 60, infoStrings, loggerInstance);
    }
}
