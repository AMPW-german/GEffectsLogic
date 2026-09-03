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

using System.Diagnostics;
using GEffectsLogic;
using GEffectsLogic.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests;

[Collection("Global logger")]
public class LogicInstancePerformanceTests
{
    private const int FrameCount = 1_000;
    private const int ConstructionSampleCount = 4_096;
    private const long TimingNoGcRegionBytes = 64 * 1024 * 1024;
    private const double MaximumConstructionBytesPerInstance = 1_024.0;
    private const double MaximumAverageUpdateMilliseconds = 0.1;
    private const double MaximumSingleUpdateMilliseconds = 0.5;
    private const double MinimumDistributionFraction = 0.025;
    private const double MaximumDistributionFraction = 0.05;
    private const double MinimumMiddleDistributionFraction = 0.50;
    private const double MaximumAdjacentDeltaTimeChangeSeconds = 0.05;
    private const double MaximumAdjacentGzChange = 0.10;

    private readonly ITestOutputHelper output;

    public LogicInstancePerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void ConstructionAllocationRemainsWithinPerInstanceBudget()
    {
        var warmupInstance = new GEffectsLogicInstance();
        var retainedInstances = new GEffectsLogicInstance[ConstructionSampleCount];
        GC.KeepAlive(warmupInstance);

        var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < retainedInstances.Length; index++)
            retainedInstances[index] = new GEffectsLogicInstance();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;

        GC.KeepAlive(retainedInstances);
        var bytesPerInstance = allocatedBytes / (double)retainedInstances.Length;
        output.WriteLine($"Construction allocation: {bytesPerInstance:F1} bytes per instance.");
        Assert.True(bytesPerInstance <= MaximumConstructionBytesPerInstance,
            $"Construction allocated {bytesPerInstance:F1} bytes per instance; budget is {MaximumConstructionBytesPerInstance:F0} bytes.");
    }

    [Fact]
    public void GeneratedWorkloadHasRequiredDistribution()
    {
        var workload = CreateWorkload();

        Assert.Equal(FrameCount, workload.Length);
        AssertFractionInRange(workload.Count(frame => frame.DeltaTime < 0.1),
            MinimumDistributionFraction, MaximumDistributionFraction);
        Assert.True(workload.Count(frame => frame.DeltaTime is >= 0.15 and <= 0.3) >=
                    FrameCount * MinimumMiddleDistributionFraction);
        AssertFractionInRange(workload.Count(frame => frame.DeltaTime > 1.0),
            MinimumDistributionFraction, MaximumDistributionFraction);

        for (var index = 0; index < workload.Length; index++)
        {
            var nextIndex = (index + 1) % workload.Length;
            Assert.InRange(Math.Abs(workload[nextIndex].DeltaTime - workload[index].DeltaTime),
                0.0, MaximumAdjacentDeltaTimeChangeSeconds);
            Assert.InRange(Math.Abs(workload[nextIndex].Gz - workload[index].Gz),
                0.0, MaximumAdjacentGzChange);
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void UpdateExecutionTimeRemainsWithinFrameBudget()
    {
#if DEBUG
        output.WriteLine("The execution-time budget is only measured in a Release build.");
        return;
#endif
        var workload = CreateWorkload();
        var elapsedTimestamps = new long[workload.Length];
        var results = new FrameResult[workload.Length];
        var originalLogger = Logger.Instance;
        Logger.Instance = null;

        try
        {
            WarmUpdatePath(workload);
            var logicInstance = new GEffectsLogicInstance();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.True(GC.TryStartNoGCRegion(TimingNoGcRegionBytes),
                "The runtime could not reserve the no-GC region required for timing.");

            try
            {
                for (var index = 0; index < workload.Length; index++)
                {
                    var frame = workload[index];
                    var startedAt = Stopwatch.GetTimestamp();
                    logicInstance.Update(frame.DeltaTime, 0.0, 0.0, frame.Gz);
                    elapsedTimestamps[index] = Stopwatch.GetTimestamp() - startedAt;
                    results[index] = new FrameResult(
                        logicInstance.Time,
                        logicInstance.LastGz,
                        logicInstance.ConsciousnessLevel,
                        logicInstance.VisualTunnelVisionLevel,
                        logicInstance.VisualRedoutLevel,
                        logicInstance.VisualLoCLevel,
                        logicInstance.VisualGrayscaleLevel,
                        logicInstance.VisualFilmGrainLevel,
                        logicInstance.VisualBlurLevel);
                }
            }
            finally
            {
                GC.EndNoGCRegion();
            }

            ValidateResults(workload, results);

            var elapsedMilliseconds = elapsedTimestamps
                .Select(timestamp => timestamp * 1_000.0 / Stopwatch.Frequency)
                .ToArray();
            var averageMilliseconds = elapsedMilliseconds.Average();
            var maximumMilliseconds = elapsedMilliseconds.Max();
            output.WriteLine($"Update timing over {FrameCount} frames: average {averageMilliseconds:F4} ms; maximum {maximumMilliseconds:F4} ms.");

            Assert.True(averageMilliseconds <= MaximumAverageUpdateMilliseconds,
                $"Average update time was {averageMilliseconds:F4} ms; budget is {MaximumAverageUpdateMilliseconds:F1} ms.");
            Assert.True(maximumMilliseconds <= MaximumSingleUpdateMilliseconds,
                $"Maximum update time was {maximumMilliseconds:F4} ms; every frame must remain within {MaximumSingleUpdateMilliseconds:F1} ms.");
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
    }

    private static WorkloadFrame[] CreateWorkload()
    {
        var workload = new WorkloadFrame[FrameCount];

        for (var index = 0; index < workload.Length; index++)
        {
            var progress = index / (double)workload.Length;
            var baseDeltaTime = 0.225 + 0.06 * Math.Sin(2.0 * Math.PI * progress);
            var highPulse = 1.1 * GaussianPulse(index, 250.0, 15.0);
            var lowPulse = 0.16 * GaussianPulse(index, 750.0, 18.0);
            var deltaTime = baseDeltaTime + highPulse - lowPulse;
            var gz = 2.0 + 6.0 * Math.Sin(2.0 * Math.PI * progress) +
                     1.5 * Math.Sin(6.0 * Math.PI * progress);

            workload[index] = new WorkloadFrame(deltaTime, gz);
        }

        return workload;
    }

    private static double GaussianPulse(int index, double center, double standardDeviation)
    {
        var directDistance = Math.Abs(index - center);
        var wrappedDistance = Math.Min(directDistance, FrameCount - directDistance);
        var normalizedDistance = wrappedDistance / standardDeviation;
        return Math.Exp(-0.5 * normalizedDistance * normalizedDistance);
    }

    private static void WarmUpdatePath(WorkloadFrame[] workload)
    {
        var logicInstance = new GEffectsLogicInstance();
        foreach (var frame in workload)
            logicInstance.Update(frame.DeltaTime, 0.0, 0.0, frame.Gz);
        GC.KeepAlive(logicInstance);
    }

    private static void ValidateResults(WorkloadFrame[] workload, FrameResult[] results)
    {
        var expectedTime = 0.0;

        for (var index = 0; index < results.Length; index++)
        {
            expectedTime += workload[index].DeltaTime;
            var result = results[index];
            Assert.InRange(Math.Abs(result.Time - expectedTime), 0.0, Math.Max(1.0, expectedTime) * 1e-12);
            Assert.Equal(workload[index].Gz, result.LastGz);
            Assert.InRange(result.ConsciousnessLevel, 0.0, 1.0);
            Assert.InRange(result.VisualTunnelVisionLevel, 0.0, 1.0);
            Assert.InRange(result.VisualRedoutLevel, 0.0, 1.0);
            Assert.InRange(result.VisualLoCLevel, 0.0, 1.0);
            Assert.InRange(result.VisualGrayscaleLevel, 0.0, 1.0);
            Assert.InRange(result.VisualFilmGrainLevel, 0.0, 1.0);
            Assert.InRange(result.VisualBlurLevel, 0.0, 1.0);
        }
    }

    private static void AssertFractionInRange(int count, double minimum, double maximum)
    {
        var fraction = count / (double)FrameCount;
        Assert.InRange(fraction, minimum, maximum);
    }

    private readonly record struct WorkloadFrame(double DeltaTime, double Gz);

    private readonly record struct FrameResult(
        double Time,
        double LastGz,
        double ConsciousnessLevel,
        double VisualTunnelVisionLevel,
        double VisualRedoutLevel,
        double VisualLoCLevel,
        double VisualGrayscaleLevel,
        double VisualFilmGrainLevel,
        double VisualBlurLevel);
}
