using GEffectsLogic.Logging;
using System.Diagnostics;
using static GEffectsLogic.LogicSettings;

namespace GEffectsLogic
{
    // Main logic class for each vessel/kitten
    public class GEffectsLogic
    {
        private static Dictionary<int, GEffectsLogic> instances = new Dictionary<int, GEffectsLogic>();
        private int? uniqueID = null;
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
        private double time;
        private double lastGx = 0.0;
        private double lastGy = 0.0;
        private double lastGz = 0.0;

        public double Time { get { return time; } set { time = value; } }
        public double LastGx { get { return lastGx; } set { lastGx = value; } }
        public double LastGy { get { return lastGy; } set { lastGy = value; } }
        public double LastGz { get { return lastGz; } set { lastGz = value; } }
        #endregion


        #region internalValues
        private double perfusionLevel = 0.0;
        private readonly PhysiologicalModel physModel = new PhysiologicalModel();

        public double PerfusionLevel { get { return perfusionLevel; } }
        public PhysiologicalModel PhysModel => physModel;
        #endregion


        #region outputValues
        private double consiousnessLevel = 1.0;
        private double confusionLevel = 0.0;
        private double tunnelVisionLevel = 0.0;
        private double greyScaleLevel = 0.0;
        private bool primaryColor = true; // true = normal (blackout), false = inverted (redout)

        public double ConsiousnessLevel { get { return consiousnessLevel; } set { consiousnessLevel = value; } }
        public double ConfusionLevel { get { return confusionLevel; } set { confusionLevel = value; } }
        public double TunnelVisionLevel { get { return tunnelVisionLevel; } set { tunnelVisionLevel = value; } }
        public double GreyScaleLevel { get { return greyScaleLevel; } set { greyScaleLevel = value; } }
        public bool PrimaryColor { get { return primaryColor; } set { primaryColor = value; } }
        #endregion


        public static double SmoothStep(double x) => x * x * x / (3.0 * x * x - 3.0 * x + 1.0);

        public static double SmoothStep(double x, double min, double max) => SmoothStep(x) * (max - min) + min;

        public void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
#if DEBUG
            var sw = Stopwatch.StartNew();
#endif

            // Update last G-forces
            lastGx = currentGx;
            lastGy = currentGy;
            lastGz = currentGz;
            // Update time
            time += deltaTime;

            // --- Physiological model update ---
            physModel.Update(deltaTime, currentGz, currentGx, currentGy);

            // Map brain O2 to perfusion level for backward compatibility
            perfusionLevel = physModel.BrainO2;

            // Determine blackout vs redout from head blood volume
            primaryColor = physModel.BloodHead <= LogicSettings.RestingBloodHead;

            // Map brain O2 to consciousness via smooth step
            double o2Normalized = Math.Clamp(
                (physModel.BrainO2 - BrainO2Blackout) / (BrainO2Full - BrainO2Blackout),
                0.0, 1.0);
            consiousnessLevel = SmoothStep(o2Normalized);

            Logger.Log($"Gz: {currentGz:f2}, headBlood: {physModel.BloodHead:f4}, brainO2: {physModel.BrainO2:f4}, HR: {physModel.HeartRateMultiplier:f2}, consciousness: {consiousnessLevel:f4}, dT: {deltaTime:f4}");

#if DEBUG
            sw.Stop();
            Logger.Log($"[PERF] Instance {UniqueID} update: {sw.Elapsed.TotalMicroseconds:f1} µs", Logging.Logger.LogLevel.Debug);
#endif
        }


        public override int GetHashCode() => UniqueID;

        public GEffectsLogic()
        {
            if (Logger.Instance == null) throw new NullReferenceException("Logger instance is not set. Please initialize a Logger before creating GEffectsLogic instances.");

            instances.Add(UniqueID, this);
        }
    }
}
