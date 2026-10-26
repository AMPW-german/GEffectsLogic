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

#define TEMPORARY_PROFILER

using System.Diagnostics;
using System.Reflection;
using System.Text;
using GEffectsLogic;
using GEffectsLogic.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests;

// TEMPORARY issue-22 diagnostic harness. Reproduces the unchanged
// LogicInstancePerformanceTests workload/warmup without modifying it and writes
// raw per-frame records to OutputRoot. Remove this file after the profiling
// pass. For the stage moment-cache reuse trial the paired root is
// "stage-moment-cache-paired" and the baseline is expected byte-identical to
// the retained o2-skip-taylor-paired baseline captures.
[Collection("Global logger")]
public class TemporaryPerformanceDiagnostics
{
    private const int FrameCount = 1_000;
    private const int TimingBatchFrameCount = FrameCount / 4;
    private const long TimingNoGcRegionReservationBytes = 128L * 1024 * 1024;
    private const int DiagnosticRunCount = 3;
    private static readonly string OutputRoot =
        Path.Combine(Path.GetTempPath(), "GEffectsDiag");
    private const string ControlSubdir = "dense-extent-control-20261023";
    private const string PairedSubdir = "target-only-focused-paired-20261023";
    private const string BaselineSubdir = "o2-skip-taylor-paired";
    private const string ReferenceSubdir = "optimized-cache";
    private const string TargetedSubdir = "cache-reference";
    private const double StateTolerance = 0.001;
    private const double OffsetTolerance = 1e-6;

    private static readonly (string Profile, LogicSettings Settings)[] Profiles =
    [
        ("default", LogicSettings.Default),
        ("custom", LogicSettings.Default with { GSuitEffectiveness = 0.25 })
    ];

    private static readonly MethodInfo CreateWorkloadMethod =
        typeof(LogicInstancePerformanceTests).GetMethod("CreateWorkload",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo WarmUpdatePathMethod =
        typeof(LogicInstancePerformanceTests).GetMethod("WarmUpdatePath",
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly ITestOutputHelper output;

    public TemporaryPerformanceDiagnostics(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void UninstrumentedFrameTimingDiagnostics()
    {
#if DEBUG
        output.WriteLine("Diagnostic timing runs only in a Release build.");
        return;
#else
        var originalLogger = Logger.Instance;
        Logger.Instance = null;
        try
        {
            Directory.CreateDirectory(OutputRoot);
            var workload = CreateWorkload();
            var frames = ExtractFrames(workload);
            for (var run = 1; run <= DiagnosticRunCount; run++)
                foreach (var (profile, settings) in Profiles)
                    RunUninstrumented(run, profile, settings, workload, frames);
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
#endif
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void IntervalEventAndStateCapture()
    {
#if DEBUG
        output.WriteLine("Diagnostic capture runs only in a Release build.");
        return;
#else
        var originalLogger = Logger.Instance;
        Logger.Instance = null;
        try
        {
            Directory.CreateDirectory(OutputRoot);
            var workload = CreateWorkload();
            var frames = ExtractFrames(workload);
            foreach (var (profile, settings) in Profiles)
                RunCapture(profile, settings, workload, frames);
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
#endif
    }

#if TEMPORARY_PROFILER
    // Paired A/B for the affine stage moment-cache reuse candidate:
    // TemporaryLeanUpstream toggles SolveAffineCollocation between
    // creating MomentPowers after the stage solve with uncached stage moment
    // evaluation (baseline, matching the retained O2-skip build) and creating
    // it once before the stage loop so all three collocation nodes reuse the
    // cached matrix powers (refined), in one process so both variants share
    // JIT and system conditions. One warmup per profile, then three run pairs
    // in alternating order. Requires the temporary PhysiologicalModel switch.
    [Fact]
    [Trait("Category", "Performance")]
    public void PairedStageMomentCacheTimingDiagnostics()
    {
#if DEBUG
        output.WriteLine("Diagnostic timing runs only in a Release build.");
        return;
#else
        var originalLogger = Logger.Instance;
        Logger.Instance = null;
        try
        {
            Directory.CreateDirectory(OutputRoot);
            var workload = CreateWorkload();
            var frames = ExtractFrames(workload);
            foreach (var (profile, settings) in Profiles)
            {
                PhysiologicalModel.TemporaryTargetOnly = false;
                WarmUpdatePathMethod.Invoke(null, [workload, settings]);
                PhysiologicalModel.TemporaryTargetOnly = true;
                WarmUpdatePathMethod.Invoke(null, [workload, settings]);
                for (var run = 1; run <= 3; run++)
                {
                    var first = run % 2 == 1;
                    RunPaired(run, profile, settings, workload, frames,
                        refined: !first);
                    RunPaired(run, profile, settings, workload, frames,
                        refined: first);
                }
            }
        }
        finally
        {
            PhysiologicalModel.TemporaryTargetOnly = false;
            Logger.Instance = originalLogger;
        }
#endif
    }

    // Untimed replay writing the full frame/event captures for each variant.
    [Fact]
    [Trait("Category", "Performance")]
    public void PairedStageMomentCacheCaptures()
    {
#if DEBUG
        output.WriteLine("Diagnostic capture runs only in a Release build.");
        return;
#else
        var originalLogger = Logger.Instance;
        Logger.Instance = null;
        try
        {
            Directory.CreateDirectory(OutputRoot);
            var workload = CreateWorkload();
            var frames = ExtractFrames(workload);
            foreach (var (profile, settings) in Profiles)
                foreach (var refined in new[] { false, true })
                    RunPairedCapture(profile, settings, workload, frames,
                        refined ? "refined" : "baseline");
        }
        finally
        {
            PhysiologicalModel.TemporaryTargetOnly = false;
            Logger.Instance = originalLogger;
        }
#endif
    }

    private void RunPaired(int run, string profile, LogicSettings settings,
        Array workload, (double DeltaTime, double Gz)[] frames, bool refined)
    {
        PhysiologicalModel.TemporaryTargetOnly = refined;
        var logicInstance = new GEffectsLogicInstance(settings: settings);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var elapsedTicks = new long[frames.Length];
        var allocatedBytes = new long[frames.Length];
        var gc0 = new int[frames.Length];
        var gc1 = new int[frames.Length];
        var gc2 = new int[frames.Length];
        var batchCount =
            (frames.Length + TimingBatchFrameCount - 1) / TimingBatchFrameCount;
        var noGcValid = new bool[batchCount];

        for (var batchStart = 0; batchStart < frames.Length;
             batchStart += TimingBatchFrameCount)
        {
            var batch = batchStart / TimingBatchFrameCount;
            noGcValid[batch] = GC.TryStartNoGCRegion(TimingNoGcRegionReservationBytes);
            try
            {
                var batchEnd = Math.Min(batchStart + TimingBatchFrameCount,
                    frames.Length);
                for (var index = batchStart; index < batchEnd; index++)
                {
                    var (dt, gz) = frames[index];
                    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    var startedAt = Stopwatch.GetTimestamp();
                    logicInstance.Update(dt, 0.0, 0.0, gz);
                    elapsedTicks[index] = Stopwatch.GetTimestamp() - startedAt;
                    allocatedBytes[index] =
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                    gc0[index] = GC.CollectionCount(0);
                    gc1[index] = GC.CollectionCount(1);
                    gc2[index] = GC.CollectionCount(2);
                }
            }
            finally
            {
                if (noGcValid[batch]) GC.EndNoGCRegion();
            }
        }

        var variant = refined ? "refined" : "baseline";
        var frameRows = new StringBuilder(frames.Length * 120);
        frameRows.AppendLine(
            "run,profile,variant,index,dt,gz,ticks,frequency,allocatedBytes,gc0,gc1,gc2,batch,noGcValid");
        for (var index = 0; index < frames.Length; index++)
        {
            var (dt, gz) = frames[index];
            var batch = index / TimingBatchFrameCount;
            frameRows.AppendLine(FormattableString.Invariant(
                $"{run},{profile},{variant},{index},{dt:R},{gz:R},{elapsedTicks[index]},{Stopwatch.Frequency},{allocatedBytes[index]},{gc0[index]},{gc1[index]},{gc2[index]},{batch},{noGcValid[batch]}"));
        }

        var directory = Path.Combine(OutputRoot, PairedSubdir);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,
            $"run{run}_{profile}_{variant}_frames.csv"), frameRows.ToString());
        output.WriteLine($"Wrote paired run {run} profile {profile} variant " +
            variant + " to " + directory);
    }

    private void RunPairedCapture(string profile, LogicSettings settings,
        Array workload, (double DeltaTime, double Gz)[] frames, string variant)
    {
        PhysiologicalModel.TemporaryTargetOnly =
            variant == "refined";
        WarmUpdatePathMethod.Invoke(null, [workload, settings]);
        var logicInstance = new GEffectsLogicInstance(settings: settings);
        var model = logicInstance.PhysModel;

        var (frameRows, eventRows) =
            CaptureRows(logicInstance, model, settings, frames);

        var directory = Path.Combine(OutputRoot, PairedSubdir);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,
            $"capture_{profile}_{variant}_frames.csv"), frameRows.ToString());
        File.WriteAllText(Path.Combine(directory,
            $"capture_{profile}_{variant}_events.csv"), eventRows.ToString());
        output.WriteLine($"Wrote paired capture {profile}/{variant} to " +
            directory);
    }

    // Paired parity: baseline captures must be byte-identical to the retained
    // O2-skip baseline captures from the previous paired root; the refined
    // variant is compared against baseline with the same tolerances as the
    // loc-contact trial.
    [Fact]
    [Trait("Category", "Performance")]
    public void PairedCaptureParityCheck()
    {
        foreach (var profile in new[] { "default", "custom" })
        {
            var baselineFrames = Path.Combine(OutputRoot, PairedSubdir,
                $"capture_{profile}_baseline_frames.csv");
            var baselineEvents = Path.Combine(OutputRoot, PairedSubdir,
                $"capture_{profile}_baseline_events.csv");
            var approvedFrames = Path.Combine(OutputRoot, BaselineSubdir,
                $"capture_{profile}_refined_frames.csv");
            var approvedEvents = Path.Combine(OutputRoot, BaselineSubdir,
                $"capture_{profile}_refined_events.csv");
            Assert.Equal(File.ReadAllBytes(approvedFrames),
                File.ReadAllBytes(baselineFrames));
            Assert.Equal(File.ReadAllBytes(approvedEvents),
                File.ReadAllBytes(baselineEvents));
            output.WriteLine($"{profile}: baseline captures byte-identical " +
                "to the retained O2-skip captures.");

            CompareCapturePair(PairedSubdir, $"capture_{profile}_baseline",
                $"capture_{profile}_refined");
        }
    }
#endif

    private void RunUninstrumented(int run, string profile, LogicSettings settings,
        Array workload, (double DeltaTime, double Gz)[] frames)
    {
        WarmUpdatePathMethod.Invoke(null, [workload, settings]);
        var logicInstance = new GEffectsLogicInstance(settings: settings);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Only preallocated scalar-array writes happen inside the timed batches;
        // CSV serialization and all file I/O occur after every batch completes.
        var elapsedTicks = new long[frames.Length];
        var allocatedBytes = new long[frames.Length];
        var gc0 = new int[frames.Length];
        var gc1 = new int[frames.Length];
        var gc2 = new int[frames.Length];
        var batchCount =
            (frames.Length + TimingBatchFrameCount - 1) / TimingBatchFrameCount;
        var noGcValid = new bool[batchCount];

        for (var batchStart = 0; batchStart < frames.Length;
             batchStart += TimingBatchFrameCount)
        {
            var batch = batchStart / TimingBatchFrameCount;
            noGcValid[batch] = GC.TryStartNoGCRegion(TimingNoGcRegionReservationBytes);
            try
            {
                var batchEnd = Math.Min(batchStart + TimingBatchFrameCount,
                    frames.Length);
                for (var index = batchStart; index < batchEnd; index++)
                {
                    var (dt, gz) = frames[index];
                    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    var startedAt = Stopwatch.GetTimestamp();
                    logicInstance.Update(dt, 0.0, 0.0, gz);
                    elapsedTicks[index] = Stopwatch.GetTimestamp() - startedAt;
                    allocatedBytes[index] =
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                    gc0[index] = GC.CollectionCount(0);
                    gc1[index] = GC.CollectionCount(1);
                    gc2[index] = GC.CollectionCount(2);
                }
            }
            finally
            {
                if (noGcValid[batch]) GC.EndNoGCRegion();
            }
        }

        var frameRows = new StringBuilder(frames.Length * 120);
        frameRows.AppendLine(
            "run,profile,index,dt,gz,ticks,frequency,allocatedBytes,gc0,gc1,gc2,batch,noGcValid");
        for (var index = 0; index < frames.Length; index++)
        {
            var (dt, gz) = frames[index];
            var batch = index / TimingBatchFrameCount;
            frameRows.AppendLine(FormattableString.Invariant(
                $"{run},{profile},{index},{dt:R},{gz:R},{elapsedTicks[index]},{Stopwatch.Frequency},{allocatedBytes[index]},{gc0[index]},{gc1[index]},{gc2[index]},{batch},{noGcValid[batch]}"));
        }

        var directory = Path.Combine(OutputRoot, ControlSubdir);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"run{run}_{profile}_frames.csv"),
            frameRows.ToString());
        output.WriteLine($"Wrote uninstrumented run {run} profile {profile} to " +
            directory);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TargetedRegimeCapture()
    {
#if DEBUG
        output.WriteLine("Diagnostic capture runs only in a Release build.");
        return;
#else
        var originalLogger = Logger.Instance;
        Logger.Instance = null;
        try
        {
            var directory = Path.Combine(OutputRoot, TargetedSubdir);
            Directory.CreateDirectory(directory);
            foreach (var (name, settings, frames) in TargetedScenarios)
            {
                output.WriteLine("capturing " + name);
                var logicInstance = new GEffectsLogicInstance(settings: settings);
                var model = logicInstance.PhysModel;
                var (frameRows, eventRows) = CaptureRows(logicInstance, model,
                    settings, frames);
                File.WriteAllText(Path.Combine(directory,
                    $"targeted_{name}_frames.csv"), frameRows.ToString());
                File.WriteAllText(Path.Combine(directory,
                    $"targeted_{name}_events.csv"), eventRows.ToString());
            }
            output.WriteLine("Wrote targeted captures to " + directory);
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
#endif
    }

    // Tolerance parity check of the loc-contact captures against the frozen
    // optimized-cache control. Structural columns (convergence, counts, event
    // kinds/order, flags) must match exactly; state doubles use the existing
    // StateSpreadTolerance and event offsets a small absolute time tolerance.
    [Fact]
    [Trait("Category", "Performance")]
    public void CaptureParityCheck()
    {
        foreach (var profile in new[] { "default", "custom" })
        {
            CompareFrameCaptures(
                Path.Combine(OutputRoot, ReferenceSubdir,
                    $"capture_{profile}_frames.csv"),
                Path.Combine(OutputRoot, ControlSubdir,
                    $"capture_{profile}_frames.csv"));
            CompareEventCaptures(
                Path.Combine(OutputRoot, ReferenceSubdir,
                    $"capture_{profile}_events.csv"),
                Path.Combine(OutputRoot, ControlSubdir,
                    $"capture_{profile}_events.csv"));
        }
    }

    private void CompareCapturePair(string subdir, string referenceName,
        string candidateName)
    {
        CompareFrameCaptures(
            Path.Combine(OutputRoot, subdir, referenceName + "_frames.csv"),
            Path.Combine(OutputRoot, subdir, candidateName + "_frames.csv"));
        CompareEventCaptures(
            Path.Combine(OutputRoot, subdir, referenceName + "_events.csv"),
            Path.Combine(OutputRoot, subdir, candidateName + "_events.csv"));
    }

    private void CompareFrameCaptures(string referencePath,
        string candidatePath)
    {
        var name = Path.GetFileName(candidatePath);
        var reference = ReadRows(referencePath);
        var candidate = ReadRows(candidatePath);
        Assert.Equal(reference.Length, candidate.Length);
        var header = reference[0].Split(',');
        for (var row = 1; row < reference.Length; row++)
        {
            var expected = reference[row].Split(',');
            var actual = candidate[row].Split(',');
            Assert.Equal(expected.Length, actual.Length);
            for (var column = 0; column < expected.Length; column++)
            {
                var columnName = header[column];
                if (columnName is "maxScaledResidual" or "iterations")
                    continue;
                if (columnName.EndsWith("_IsUnconscious", StringComparison.Ordinal) ||
                    columnName.EndsWith("_InSuddenLoC", StringComparison.Ordinal) ||
                    columnName.EndsWith("_IsDead", StringComparison.Ordinal) ||
                    columnName is "converged" or "segmentCount" or
                        "stageCount" or "eventCount" or "modeCrossings" or
                        "stateViolations" or "pressureBoundEntries" or
                        "pressureBoundReleases" or "coreBoundEntries" or
                        "coreBoundReleases" or "headBoundEntries" or
                        "headBoundReleases" or "lowerBoundEntries" or
                        "lowerBoundReleases" or "modeTransitions" or
                        "index" or "dt" or "gz")
                {
                    Assert.True(expected[column] == actual[column],
                        $"{name} row {row} column {columnName}: " +
                        $"{expected[column]} vs {actual[column]}.");
                }
                else
                {
                    var expectedValue = double.Parse(expected[column],
                        System.Globalization.CultureInfo.InvariantCulture);
                    var actualValue = double.Parse(actual[column],
                        System.Globalization.CultureInfo.InvariantCulture);
                    var difference = Math.Abs(expectedValue - actualValue);
                    Assert.True(difference <= StateTolerance,
                        $"{name} row {row} column {columnName}: " +
                        $"{expectedValue:R} vs {actualValue:R} differs by " +
                        $"{difference:R}, exceeding {StateTolerance:R}.");
                }
            }
        }
        output.WriteLine($"{name}: {reference.Length - 1} frames within " +
            $"state tolerance {StateTolerance:R}.");
    }

    private void CompareEventCaptures(string referencePath,
        string candidatePath)
    {
        var name = Path.GetFileName(candidatePath);
        var reference = ReadRows(referencePath);
        var candidate = ReadRows(candidatePath);
        Assert.Equal(reference.Length, candidate.Length);
        var header = reference[0].Split(',');
        for (var row = 1; row < reference.Length; row++)
        {
            var expected = reference[row].Split(',');
            var actual = candidate[row].Split(',');
            Assert.Equal(expected.Length, actual.Length);
            for (var column = 0; column < expected.Length; column++)
            {
                var columnName = header[column];
                if (columnName is "index" or "sequence" or "kind")
                {
                    Assert.True(expected[column] == actual[column],
                        $"{name} row {row} column {columnName}: " +
                        $"{expected[column]} vs {actual[column]}.");
                }
                else if (columnName == "offset")
                {
                    var expectedValue = double.Parse(expected[column],
                        System.Globalization.CultureInfo.InvariantCulture);
                    var actualValue = double.Parse(actual[column],
                        System.Globalization.CultureInfo.InvariantCulture);
                    var difference = Math.Abs(expectedValue - actualValue);
                    Assert.True(difference <= OffsetTolerance,
                        $"{name} row {row}: event offset {expectedValue:R} vs " +
                        $"{actualValue:R} differs by {difference:R}, " +
                        $"exceeding {OffsetTolerance:R}.");
                }
                else if (columnName.EndsWith("_IsUnconscious", StringComparison.Ordinal) ||
                         columnName.EndsWith("_InSuddenLoC", StringComparison.Ordinal) ||
                         columnName.EndsWith("_IsDead", StringComparison.Ordinal))
                {
                    Assert.True(expected[column] == actual[column],
                        $"{name} row {row} column {columnName}: " +
                        $"{expected[column]} vs {actual[column]}.");
                }
                else
                {
                    var expectedValue = double.Parse(expected[column],
                        System.Globalization.CultureInfo.InvariantCulture);
                    var actualValue = double.Parse(actual[column],
                        System.Globalization.CultureInfo.InvariantCulture);
                    var difference = Math.Abs(expectedValue - actualValue);
                    Assert.True(difference <= StateTolerance,
                        $"{name} row {row} column {columnName}: " +
                        $"{expectedValue:R} vs {actualValue:R} differs by " +
                        $"{difference:R}, exceeding {StateTolerance:R}.");
                }
            }
        }
        output.WriteLine($"{name}: {reference.Length - 1} events within " +
            $"offset tolerance {OffsetTolerance:R} and state tolerance " +
            $"{StateTolerance:R}.");
    }

    private static string[] ReadRows(string path)
    {
        Assert.True(File.Exists(path), $"missing capture {path}");
        return File.ReadAllLines(path);
    }

    private static readonly (string Name, LogicSettings Settings,
        (double DeltaTime, double Gz)[] Frames)[] TargetedScenarios =
    [
        ("pressure-pulses", LogicSettings.Default, SegmentFrames(
            (-5.0, 2.5), (2.0, 2.5), (-5.0, 2.5), (2.0, 2.5),
            (-5.0, 2.5), (2.0, 2.5), (-5.0, 2.5), (2.0, 2.5),
            (-5.0, 2.5), (2.0, 2.5), (-5.0, 2.5), (2.0, 2.5))),
        ("visual-cycle", LogicSettings.Default, SegmentFrames(
            (5.0, 4.0), (-5.0, 2.5), (5.0, 4.0))),
        ("visual-unequal-taus", LogicSettings.Default with
        {
            VisualTunnelVisionInTau = 1.0,
            VisualGrayscaleInTau = 20.0,
            VisualTunnelVisionOutTau = 12.0,
            VisualGrayscaleOutTau = 1.0
        }, SegmentFrames((5.0, 4.0), (-5.0, 2.5), (5.0, 4.0)))
    ];

    private static (double DeltaTime, double Gz)[] SegmentFrames(
        params (double Gz, double Seconds)[] segments)
    {
        const double dt = 0.225;
        var frames = new List<(double DeltaTime, double Gz)>();
        foreach (var (gz, seconds) in segments)
            for (var t = 0.0; t < seconds - 1e-9; t += dt)
                frames.Add((dt, gz));
        return frames.ToArray();
    }

    private void RunCapture(string profile, LogicSettings settings, Array workload,
        (double DeltaTime, double Gz)[] frames)
    {
        WarmUpdatePathMethod.Invoke(null, [workload, settings]);
        var logicInstance = new GEffectsLogicInstance(settings: settings);
        var model = logicInstance.PhysModel;
        var (frameRows, eventRows) = CaptureRows(logicInstance, model,
            settings, frames);

        var directory = Path.Combine(OutputRoot, ControlSubdir);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,
            $"capture_{profile}_frames.csv"), frameRows.ToString());
        File.WriteAllText(Path.Combine(directory,
            $"capture_{profile}_events.csv"), eventRows.ToString());
        output.WriteLine($"Wrote capture for profile {profile} to " + directory);
    }

    private static (StringBuilder FrameRows, StringBuilder EventRows)
        CaptureRows(
        GEffectsLogicInstance logicInstance, PhysiologicalModel model,
        LogicSettings settings, (double DeltaTime, double Gz)[] frames)
    {
        var frameRows = new StringBuilder(frames.Length * 640);
        var eventRows = new StringBuilder(frames.Length * 512);
        AppendCaptureHeaders(frameRows, eventRows);

        for (var index = 0; index < frames.Length; index++)
        {
            var (dt, gz) = frames[index];
            var initial = model.CaptureIntervalState();
            logicInstance.Update(dt, 0.0, 0.0, gz);
            // Re-evaluate the same interval on the captured pre-frame state to
            // recover the IntegrationResult the model committed; the replay is a
            // pure function of initial state and settings, so the events match
            // the accepted update. This pass is untimed by design.
            var result = model.AdvanceInterval(in initial, dt, 0.0, 0.0, gz,
                settings);
            AppendResultRows(frameRows, eventRows, index, dt, gz, result);
        }
        return (frameRows, eventRows);
    }

    private static void AppendCaptureHeaders(StringBuilder frameRows,
        StringBuilder eventRows)
    {
        frameRows.AppendLine(
            "index,dt,gz,converged,iterations,maxScaledResidual,segmentCount," +
            "stageCount,eventCount,modeCrossings,stateViolations," +
            "pressureBoundEntries,pressureBoundReleases,coreBoundEntries," +
            "coreBoundReleases,headBoundEntries,headBoundReleases," +
            "lowerBoundEntries,lowerBoundReleases,modeTransitions," +
            IntegrationStateColumns("final_"));
        eventRows.AppendLine("index,sequence,offset,kind," +
            IntegrationStateColumns("state_"));
    }

    private static void AppendResultRows(StringBuilder frameRows,
        StringBuilder eventRows, int index, double dt, double gz,
        PhysiologicalModel.IntegrationResult result)
    {
        frameRows.Append(index).Append(',')
            .Append(dt.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(gz.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(result.Converged).Append(',')
            .Append(result.Iterations).Append(',')
            .Append(result.MaxScaledResidual.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
            .Append(result.Segments.Length).Append(',')
            .Append(result.Stages.Length).Append(',')
            .Append(result.Events.Length).Append(',')
            .Append(Join(result.ModeCrossings)).Append(',')
            .Append(Join(result.StateViolations)).Append(',')
            .Append(result.PressureBoundEntries.Length).Append(',')
            .Append(result.PressureBoundReleases.Length).Append(',')
            .Append(result.CoreBoundEntries.Length).Append(',')
            .Append(result.CoreBoundReleases.Length).Append(',')
            .Append(result.HeadBoundEntries.Length).Append(',')
            .Append(result.HeadBoundReleases.Length).Append(',')
            .Append(result.LowerBoundEntries.Length).Append(',')
            .Append(result.LowerBoundReleases.Length).Append(',')
            .Append(result.ModeTransitions.Length);
        AppendIntegrationState(frameRows, result.Final, false);
        frameRows.AppendLine();

        for (var sequence = 0; sequence < result.Events.Length; sequence++)
        {
            var integrationEvent = result.Events[sequence];
            eventRows.Append(index).Append(',')
                .Append(sequence).Append(',')
                .Append(integrationEvent.Offset.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(integrationEvent.Kind);
            AppendIntegrationState(eventRows, integrationEvent.State, true);
            eventRows.AppendLine();
        }
    }

    private static Array CreateWorkload() =>
        (Array)CreateWorkloadMethod.Invoke(null, null)!;

    private static (double DeltaTime, double Gz)[] ExtractFrames(Array workload)
    {
        var frameType = workload.GetType().GetElementType()!;
        var deltaTime = frameType.GetProperty("DeltaTime")!;
        var gz = frameType.GetProperty("Gz")!;
        var frames = new (double DeltaTime, double Gz)[workload.Length];
        for (var index = 0; index < workload.Length; index++)
        {
            var frame = workload.GetValue(index)!;
            frames[index] = ((double)deltaTime.GetValue(frame)!,
                (double)gz.GetValue(frame)!);
        }
        return frames;
    }

    private static string IntegrationStateColumns(string prefix) =>
        prefix + "BloodHead," + prefix + "BloodLower," + prefix + "BloodCore," +
        prefix + "HeartRateMultiplier," + prefix + "CardioFatigue," +
        prefix + "CerebralPressureImpairment," + prefix + "RespiratoryFatigue," +
        prefix + "StrainingLevel," + prefix + "StrainingFatigue," +
        prefix + "GSuitFatigue," + prefix + "BloodO2Head," + prefix +
        "BloodO2Core," + prefix + "BloodO2Lower," + prefix +
        "ArterialOxygenation," + prefix + "ConsciousnessLevel," + prefix +
        "LungCompressionLevel," + prefix + "PainLevel," + prefix +
        "GyNeckFatigue," + prefix + "GyNeckFatigueDeathDwell," + prefix +
        "SuddenLoCAccumulator," + prefix + "VisualGrayscaleLevel," + prefix +
        "VisualTunnelVisionLevel," + prefix + "VisualRedoutLevel," + prefix +
        "VisualLoCLevel," + prefix + "IsUnconscious," + prefix +
        "InSuddenLoC," + prefix + "IsDead";

    private static void AppendIntegrationState(StringBuilder rows,
        in PhysiologicalModel.IntegrationState state, bool leadingComma)
    {
        if (leadingComma) rows.Append(',');
        var values = new object?[]
        {
            state.BloodHead, state.BloodLower, state.BloodCore,
            state.HeartRateMultiplier, state.CardioFatigue,
            state.CerebralPressureImpairment, state.RespiratoryFatigue,
            state.StrainingLevel, state.StrainingFatigue, state.GSuitFatigue,
            state.BloodO2Head, state.BloodO2Core, state.BloodO2Lower,
            state.ArterialOxygenation, state.ConsciousnessLevel,
            state.LungCompressionLevel, state.PainLevel, state.GyNeckFatigue,
            state.GyNeckFatigueDeathDwell, state.SuddenLoCAccumulator,
            state.VisualGrayscaleLevel, state.VisualTunnelVisionLevel,
            state.VisualRedoutLevel, state.VisualLoCLevel, state.IsUnconscious,
            state.InSuddenLoC, state.IsDead
        };
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0) rows.Append(',');
            var value = values[index];
            rows.Append(value switch
            {
                double number => number.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture),
                bool flag => flag ? "1" : "0",
                _ => ""
            });
        }
    }

    private static string Join(string[] values) =>
        values.Length == 0 ? "" : "\"" + string.Join(";", values) + "\"";
}

