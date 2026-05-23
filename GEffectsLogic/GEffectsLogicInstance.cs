using GEffectsLogic.Logging;

#if PERFDEBUG
using System.Diagnostics;
#endif

namespace GEffectsLogic
{
    // Main logic class for each vessel/kitten
    public class GEffectsLogicInstance
    {
        protected static Dictionary<int, GEffectsLogicInstance> instances = [];
        public static IReadOnlyDictionary<int, GEffectsLogicInstance> Instances => instances;

        protected int? uniqueID;
        public int UniqueID
        {
            get
            {
                if (uniqueID == null)
                {
                    int id = 0;
                    while (instances.ContainsKey(id))
                    {
                        id++;
                    }
                    uniqueID = id;
                }
                return uniqueID.Value;
            }
        }

        #region inputValues
        protected double time;
        protected double lastGx = 0.0;
        protected double lastGy = 0.0;
        protected double lastGz = 0.0;

        public double Time => time;
        public double LastGx => lastGx;
        public double LastGy => lastGy;
        public double LastGz => lastGz;
        #endregion


        protected readonly PhysiologicalModel physModel = new();
        public PhysiologicalModel PhysModel => physModel;

        // Track if G-forces remain stable to disable physmodel updates at high timewarp in orbit
        // Stabilized conditions:
        // 1. Gn remains within 0.05 of the last stabilized value for 10 seconds
        // 2. all physmodel values remain within 0.025 of the recorded values for 10 seconds
        // Stabilization is lost if Gn deviates by more than 0.05
        protected bool stable = false;
        protected bool stableRecorded = false;
        protected double stabilizationTime = 0.0;
        protected double stabilizedGx = 0.0;
        protected double stabilizedGy = 0.0;
        protected double stabilizedGz = 0.0;
        protected double stabilizedBloodHead = 0.0;
        protected double stabilizedBloodCore = 0.0;
        protected double stabilizedBloodLower = 0.0;
        protected double stabilizedBrainO2 = 0.0;
        protected double stabilizedHeartRateMultiplier = 0.0;
        protected double stabilizedPerfusionLevel = 0.0;
        protected double stabilizedConsciousnessLevel = 0.0;
        protected double stabilizedGreyScaleLevel = 0.0;
        protected double stabilizedTunnelVisionLevel = 0.0;

        #region outputValues
        public double BloodHead => physModel.BloodHead;
        //public double ConfusionLevel => physModel.ConfusionLevel;
        public double TunnelVisionLevel => physModel.TunnelVisionLevel;
        public double GreyScaleLevel => physModel.GreyScaleLevel;
        public bool PrimaryColor => physModel.PrimaryColor;
        public double ConsciousnessLevel => physModel.ConsciousnessLevel;
        public bool IsStable => stable;

        public bool IsUnconsciouss = false; // Get unconsciousness at 0.1, recover at 0.5. Only used for logging
        #endregion

        public virtual void Reset()
        {
            time = 0;
            lastGx = 0;
            lastGy = 0;
            lastGz = 0;
            physModel.Reset();
        }

        public virtual void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
#if PERFDEBUG
            var sw = Stopwatch.StartNew();
#endif
            List<double> dtList = new();

            if (deltaTime > 1) { 
                int stepCount = (int)deltaTime * 2;
                dtList.AddRange(Enumerable.Repeat(deltaTime / stepCount, stepCount));
                if (!stable) Logger.Log($"High deltaTime detected: {deltaTime}s - splitting it into {stepCount} steps of {deltaTime / stepCount}s each", UniqueID, Logger.LogLevel.Warning);
            }
            else if (deltaTime <= 0) { 
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

            if (stable && Math.Abs(currentGx - stabilizedGx) <= 0.05 && Math.Abs(currentGy - stabilizedGy) <= 0.05 && Math.Abs(currentGz - stabilizedGz) <= 0.05)
            {
                // No Gn change, physmodel can't change
                return;
            }

            dtList.ForEach(dt =>
            {
                if (!stableRecorded)
                {
                    stableRecorded = true;
                    stabilizedGx = currentGx;
                    stabilizedGy = currentGy;
                    stabilizedGz = currentGz;
                    stabilizedBloodHead = physModel.BloodHead;
                    stabilizedBloodCore = physModel.BloodCore;
                    stabilizedBloodLower = physModel.BloodLower;
                    stabilizedBrainO2 = physModel.BrainO2;
                    stabilizedHeartRateMultiplier = physModel.HeartRateMultiplier;
                    stabilizedPerfusionLevel = physModel.PerfusionLevel;
                    stabilizedConsciousnessLevel = physModel.ConsciousnessLevel;
                    stabilizedGreyScaleLevel = physModel.GreyScaleLevel;
                    stabilizedTunnelVisionLevel = physModel.TunnelVisionLevel;
                }
                else if (
                    Math.Abs(currentGx - stabilizedGx) > 0.05 || Math.Abs(currentGy - stabilizedGy) > 0.05 || Math.Abs(currentGz - stabilizedGz) > 0.05
                    || Math.Abs(physModel.BloodHead - stabilizedBloodHead) > 0.025 || Math.Abs(physModel.BloodCore - stabilizedBloodCore) > 0.025 || Math.Abs(physModel.BloodLower - stabilizedBloodLower) > 0.025
                    || Math.Abs(physModel.BrainO2 - stabilizedBrainO2) > 0.025 || Math.Abs(physModel.HeartRateMultiplier - stabilizedHeartRateMultiplier) > 0.025 || Math.Abs(physModel.PerfusionLevel - stabilizedPerfusionLevel) > 0.025
                    || Math.Abs(physModel.ConsciousnessLevel - stabilizedConsciousnessLevel) > 0.025 || Math.Abs(physModel.GreyScaleLevel - stabilizedGreyScaleLevel) > 0.025 || Math.Abs(physModel.TunnelVisionLevel - stabilizedTunnelVisionLevel) > 0.025
                )
                {
                    if (stable)
                    {
                        Logger.Log($"Instance {UniqueID} has destabilized at Gx: {currentGx:f2} ({stabilizedGx}), Gy: {currentGy:f2} ({stabilizedGy}), Gz: {currentGz:f2} ({stabilizedGz}). PhysModel updates resumed.", UniqueID, Logger.LogLevel.Info);
                    }

                    stabilizationTime = 0.0; // Reset stabilization time if G-forces deviate significantly
                    stable = false;
                    stableRecorded = false;
                }
                else
                {
                    stabilizationTime += dt;
                    if (stabilizationTime > 10.0 && !stable)
                    {
                        stable = true; // Consider stabilized if conditions are met for 10 seconds
                        Logger.Log($"Instance {UniqueID} has stabilized at Gx: {stabilizedGx:f2}, Gy: {stabilizedGy:f2}, Gz: {stabilizedGz:f2}. PhysModel updates paused until destabilization.", UniqueID, Logger.LogLevel.Info);
                    }
                }

                if (!stable)
                {
                    // Physiological model update
                    physModel.Update(dt, currentGz, currentGx, currentGy);
                }

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


                Logger.Log($"Gz: {currentGz:f2}, headBlood: {physModel.BloodHead:f4}, brainO2: {physModel.BrainO2:f4}, HR: {physModel.HeartRateMultiplier:f2}, consciousness: {ConsciousnessLevel:f4}, dT: {dt:f4}", UniqueID);

            });

#if PERFDEBUG
            sw.Stop();
            Logger.Log($"[PERF] Instance {UniqueID} update: {sw.Elapsed.TotalMicroseconds:f1} µs", UniqueID, Logging.Logger.LogLevel.Debug);
#endif
        }


        public override int GetHashCode() => UniqueID;

        public GEffectsLogicInstance()
        {
            if (Logger.Instance == null) throw new NullReferenceException("Logger instance is not set. Please initialize a Logger before creating GEffectsLogicInstance instances.");

            instances.Add(UniqueID, this);
        }
    }
}
