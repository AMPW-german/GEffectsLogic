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

using GEffectsLogic.Logging;
#if PERFDEBUG
using System.Diagnostics;
#endif

namespace GEffectsLogic;

// Main logic class for each vessel/kitten
public class GEffectsLogicInstance
{
    protected double stabilizationTime;
    protected double stabilizedGx;
    protected double stabilizedGy;
    protected double stabilizedGz;

    // Track if G-forces remain stable to disable physmodel updates at high timewarp in orbit
    // Stabilized conditions:
    // 1. the model reports a stable equilibrium under the current forces
    // 2. that equilibrium holds for the stabilization dwell
    // Stabilization is lost if any force component changes from the recorded vector
    protected bool stable;
    protected bool stableRecorded;
    private const double StabilizationStateError = 0.00025;

    private readonly Logger? logger;
    public Logger? Logger => logger;

    // physModel will always be set by the SetPhysiologicalModel method which is called in the constructor but the compiler doesn't recognize this
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public GEffectsLogicInstance(Logger? logger = null, LogicSettings? settings = null)
    {
        this.logger = logger;
        Settings = settings ?? LogicSettings.Default;
        SetPhysiologicalModel();
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    /// <summary>Do not change during runtime!</summary>
    public PhysiologicalModel PhysModel { get; private set; }

    public LogicSettings Settings { get; private set; }

    public void ApplySettings(LogicSettings settings)
    {
#if NET481
        if (settings is null) throw new ArgumentNullException(nameof(settings));
#else
        ArgumentNullException.ThrowIfNull(settings);
#endif
        if (ReferenceEquals(Settings, settings)) return;
        Settings = settings;
        InvalidateStabilization();
    }

    private void InvalidateStabilization()
    {
        stable = false;
        stableRecorded = false;
        stabilizationTime = 0.0;
    }

    public virtual void Reset()
    {
        time = 0;
        lastGx = 0;
        lastGy = 0;
        lastGz = 0;
        InvalidateStabilization();
        PhysModel.Reset();
    }

    public virtual void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
    {
#if PERFDEBUG
        var sw = Stopwatch.StartNew();
#endif
        if (deltaTime <= 0)
            Logger.Log($"Negative deltaTime detected: {deltaTime}s", this, Logger.LogLevel.Error);

        // Update last G-forces
        lastGx = currentGx;
        lastGy = currentGy;
        lastGz = currentGz;
        // Update time
        time += deltaTime;

        if (stable)
        {
            if (currentGx == stabilizedGx && currentGy == stabilizedGy &&
                currentGz == stabilizedGz)
                // No Gn change, physmodel can't change
                return;

            Logger.Log(
                $"Instance has destabilized at Gx: {currentGx:f2} ({stabilizedGx}), Gy: {currentGy:f2} ({stabilizedGy}), Gz: {currentGz:f2} ({stabilizedGz}). PhysModel updates resumed.",
                this, Logger.LogLevel.Info);
            InvalidateStabilization();
        }

        if (deltaTime <= 0) return;

        // Single full-interval model call for the whole caller step
        PhysModel.Update(deltaTime, currentGx, currentGy, currentGz);

        if (PhysModel.CanStabilize(currentGx, currentGy, currentGz, StabilizationStateError))
        {
            stabilizationTime += deltaTime;
            if (stabilizationTime > Settings.StabilizationTimeThreshold)
            {
                stable = true;
                stableRecorded = true;
                stabilizedGx = currentGx;
                stabilizedGy = currentGy;
                stabilizedGz = currentGz;
                Logger.Log(
                    $"Instance has stabilized at Gx: {stabilizedGx:f2}, Gy: {stabilizedGy:f2}, Gz: {stabilizedGz:f2}. PhysModel updates paused until destabilization.",
                    this, Logger.LogLevel.Info);
            }
        }
        else
        {
            stabilizationTime = 0.0;
        }

        Logger.Log(
            $"Gz: {currentGz:f2}, headBlood: {PhysModel.BloodHead:f4}, brainO2: {PhysModel.BloodO2Head:f4}, HR: {PhysModel.HeartRateMultiplier:f2}, consciousness: {ConsciousnessLevel:f4}, dT: {deltaTime:f4}",
            this);

#if PERFDEBUG
        sw.Stop();
        Logger.Log($"[PERF] Instance update: {sw.Elapsed.TotalMicroseconds:f1} µs", this, Logging.Logger.LogLevel.Debug);
#endif
    }

    protected virtual void SetPhysiologicalModel()
    {
        PhysModel = new PhysiologicalModel(this);
    }

    #region inputValues

    protected double time;
    protected double lastGx;
    protected double lastGy;
    protected double lastGz;

    public double Time => time;
    public double LastGx => lastGx;
    public double LastGy => lastGy;
    public double LastGz => lastGz;

    #endregion

    #region outputValues

    //public double ConfusionLevel => physModel.ConfusionLevel;
    public double VisualTunnelVisionLevel => PhysModel.VisualTunnelVisionLevel;
    public double VisualRedoutLevel => PhysModel.VisualRedoutLevel;
    public double VisualLoCLevel => PhysModel.VisualLoCLevel;
    public double VisualGrayscaleLevel => PhysModel.VisualGrayscaleLevel;
    public double VisualFilmGrainLevel => PhysModel.VisualFilmGrainLevel;
    public double VisualBlurLevel => PhysModel.VisualBlurLevel;
    public double ConsciousnessLevel => PhysModel.ConsciousnessLevel;
    public bool IsStable => stable;
    public bool IsUnconscious => PhysModel.IsUnconscious;

    // Expanded oxygen model properties
    public double BloodO2Head => PhysModel.BloodO2Head;
    public double BloodO2Core => PhysModel.BloodO2Core;
    public double BloodO2Lower => PhysModel.BloodO2Lower;
    public double BrainO2 => PhysModel.BrainO2;
    public double ArterialOxygenation => PhysModel.ArterialOxygenation;

    // Respiratory and tolerance properties
    public double RespiratoryFatigue => PhysModel.RespiratoryFatigue;
    public double GxEffectiveTolerance => PhysModel.GxEffectiveTolerance;
    public double GyEffectiveTolerance => PhysModel.GyEffectiveTolerance;

    // Sudden G-LOC properties
    public double SuddenLoCAccumulator => PhysModel.SuddenLoCAccumulator;
    public bool InSuddenLoC => PhysModel.InSuddenLoC;

    // Gy neck side fatigue properties
    public double GyNeckFatigue => PhysModel.GyNeckFatigue;
    public bool IsDead => PhysModel.IsDead;

    // Gy lung compression and pain properties
    public double LungCompressionLevel => PhysModel.LungCompressionLevel;
    public double PainLevel => PhysModel.PainLevel;

    #endregion
}
