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
    protected static Dictionary<int, GEffectsLogicInstance> instances = [];


    protected double stabilizationTime;
    protected double stabilizedBloodCore;
    protected double stabilizedBloodHead;
    protected double stabilizedBloodLower;
    protected double stabilizedBrainO2;
    protected double stabilizedConsciousnessLevel;
    protected double stabilizedGreyScaleLevel;
    protected double stabilizedGx;
    protected double stabilizedGy;
    protected double stabilizedGz;
    protected double stabilizedHeartRateMultiplier;
    protected double stabilizedPerfusionLevel;
    protected double stabilizedTunnelVisionLevel;

    // Track if G-forces remain stable to disable physmodel updates at high timewarp in orbit
    // Stabilized conditions:
    // 1. Gn remains within 0.05 of the last stabilized value for 10 seconds
    // 2. all physmodel values remain within 0.025 of the recorded values for 10 seconds
    // Stabilization is lost if Gn deviates by more than 0.05
    protected bool stable;
    protected bool stableRecorded;

    protected int? uniqueID;

    // physModel will always be set by the SetPhysiologicalModel method which is called in the constructor but the compiler doesn't recognize this
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public GEffectsLogicInstance()
    {
        if (Logger.Instance == null)
            throw new NullReferenceException(
                "Logger instance is not set. Please initialize a Logger before creating GEffectsLogicInstance instances.");

        SetPhysiologicalModel();
        instances.Add(UniqueID, this);
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public static IReadOnlyDictionary<int, GEffectsLogicInstance> Instances => instances;

    public int UniqueID
    {
        get
        {
            if (uniqueID == null)
            {
                var id = 0;
                while (instances.ContainsKey(id)) id++;
                uniqueID = id;
            }

            return uniqueID.Value;
        }
    }

    /// <summary>Do not change during runtime!</summary>
    public PhysiologicalModel PhysModel { get; private set; }

    public virtual void Reset()
    {
        time = 0;
        lastGx = 0;
        lastGy = 0;
        lastGz = 0;
        PhysModel.Reset();
    }

    public virtual void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
    {
#if PERFDEBUG
        var sw = Stopwatch.StartNew();
#endif
        List<double> dtList = [];

        if (deltaTime > 1)
        {
            var stepCount = (int)deltaTime * 2;
            dtList.AddRange(Enumerable.Repeat(deltaTime / stepCount, stepCount));
            if (!stable)
                Logger.Log(
                    $"High deltaTime detected: {deltaTime}s - splitting it into {stepCount} steps of {deltaTime / stepCount}s each",
                    UniqueID, Logger.LogLevel.Warning);
        }
        else if (deltaTime <= 0)
        {
            Logger.Log($"Negative deltaTime detected: {deltaTime}s", UniqueID, Logger.LogLevel.Error);
        }
        else
        {
            dtList.Add(deltaTime);
        }

        // Update last G-forces
        lastGx = currentGx;
        lastGy = currentGy;
        lastGz = currentGz;
        // Update time
        time += deltaTime;

        if (stable && Math.Abs(currentGx - stabilizedGx) <= 0.05 && Math.Abs(currentGy - stabilizedGy) <= 0.05 &&
            Math.Abs(currentGz - stabilizedGz) <= 0.05)
            // No Gn change, physmodel can't change
            return;

        foreach (var dt in dtList)
        {
            if (!stableRecorded)
            {
                stableRecorded = true;
                stabilizedGx = currentGx;
                stabilizedGy = currentGy;
                stabilizedGz = currentGz;
                stabilizedBloodHead = PhysModel.BloodHead;
                stabilizedBloodCore = PhysModel.BloodCore;
                stabilizedBloodLower = PhysModel.BloodLower;
                stabilizedBrainO2 = PhysModel.BrainO2;
                stabilizedHeartRateMultiplier = PhysModel.HeartRateMultiplier;
                stabilizedPerfusionLevel = PhysModel.PerfusionLevel;
                stabilizedConsciousnessLevel = PhysModel.ConsciousnessLevel;
                stabilizedGreyScaleLevel = PhysModel.GreyScaleLevel;
                stabilizedTunnelVisionLevel = PhysModel.TunnelVisionLevel;
            }
            else if (Math.Abs(currentGx - stabilizedGx) > 0.025 || Math.Abs(currentGy - stabilizedGy) > 0.025 ||
                     Math.Abs(currentGz - stabilizedGz) > 0.025)
            {
                // Separate check for Gn deviation to reduce deviation checks for the physmodel
                if (stable)
                    Logger.Log(
                        $"Instance {UniqueID} has destabilized at Gx: {currentGx:f2} ({stabilizedGx}), Gy: {currentGy:f2} ({stabilizedGy}), Gz: {currentGz:f2} ({stabilizedGz}). PhysModel updates resumed.",
                        UniqueID, Logger.LogLevel.Info);

                stabilizationTime = 0.0;
                stable = false;
                stableRecorded = false;
            }
            else if (!stable && (
                         Math.Abs(PhysModel.BloodHead - stabilizedBloodHead) > 0.025 ||
                         Math.Abs(PhysModel.BloodCore - stabilizedBloodCore) > 0.025 ||
                         Math.Abs(PhysModel.BloodLower - stabilizedBloodLower) > 0.025
                         || Math.Abs(PhysModel.BrainO2 - stabilizedBrainO2) > 0.025 ||
                         Math.Abs(PhysModel.HeartRateMultiplier - stabilizedHeartRateMultiplier) > 0.025 ||
                         Math.Abs(PhysModel.PerfusionLevel - stabilizedPerfusionLevel) > 0.025
                         || Math.Abs(PhysModel.ConsciousnessLevel - stabilizedConsciousnessLevel) > 0.025 ||
                         Math.Abs(PhysModel.GreyScaleLevel - stabilizedGreyScaleLevel) > 0.025 ||
                         Math.Abs(PhysModel.TunnelVisionLevel - stabilizedTunnelVisionLevel) > 0.025
                     ))
            {
                if (stable)
                    Logger.Log(
                        $"Instance {UniqueID} has destabilized at Gx: {currentGx:f2} ({stabilizedGx}), Gy: {currentGy:f2} ({stabilizedGy}), Gz: {currentGz:f2} ({stabilizedGz}). PhysModel updates resumed.",
                        UniqueID, Logger.LogLevel.Info);

                stabilizationTime = 0.0;
                stable = false;
                stableRecorded = false;
            }
            else
            {
                stabilizationTime += dt;
                if (stabilizationTime > LogicSettings.StabilizationTimeThreshold && !stable)
                {
                    stable = true; // Consider stabilized if conditions are met for the threshold duration
                    Logger.Log(
                        $"Instance {UniqueID} has stabilized at Gx: {stabilizedGx:f2}, Gy: {stabilizedGy:f2}, Gz: {stabilizedGz:f2}. PhysModel updates paused until destabilization.",
                        UniqueID, Logger.LogLevel.Info);
                }
            }

            if (!stable)
                // Physiological model update
                PhysModel.Update(dt, currentGz, currentGx, currentGy);

            if (IsUnconsciouss && ConsciousnessLevel > 0.5)
            {
                IsUnconsciouss = false;
                Logger.Log($"Instance {UniqueID} has regained consciousness.", UniqueID, Logger.LogLevel.Info);
            }
            else if (!IsUnconsciouss && ConsciousnessLevel <= 0.1)
            {
                IsUnconsciouss = true;
                Logger.Log($"Instance {UniqueID} has lost consciousness.", UniqueID, Logger.LogLevel.Info);
            }


            Logger.Log(
                $"Gz: {currentGz:f2}, headBlood: {PhysModel.BloodHead:f4}, brainO2: {PhysModel.BrainO2:f4}, HR: {PhysModel.HeartRateMultiplier:f2}, consciousness: {ConsciousnessLevel:f4}, dT: {dt:f4}",
                UniqueID);
        }

#if PERFDEBUG
        sw.Stop();
        Logger.Log($"[PERF] Instance {UniqueID} update: {sw.Elapsed.TotalMicroseconds:f1} µs", UniqueID, Logging.Logger.LogLevel.Debug);
#endif
    }


    public override int GetHashCode()
    {
        return UniqueID;
    }

    protected virtual void SetPhysiologicalModel()
    {
        PhysModel = new PhysiologicalModel(UniqueID);
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

    public double BloodHead => PhysModel.BloodHead;

    //public double ConfusionLevel => physModel.ConfusionLevel;
    public double TunnelVisionLevel => PhysModel.TunnelVisionLevel;
    public double GreyScaleLevel => PhysModel.GreyScaleLevel;
    public bool PrimaryColor => PhysModel.PrimaryColor;
    public double ConsciousnessLevel => PhysModel.ConsciousnessLevel;
    public bool IsStable => stable;

    public bool IsUnconsciouss; // Get unconsciousness at 0.1, recover at 0.5. Only used for logging

    #endregion
}