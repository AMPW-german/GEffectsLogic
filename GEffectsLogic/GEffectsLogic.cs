using GEffectsLogic.Logging;

namespace GEffectsLogic
{
    // Main logic class for each vessel/kitten
    public class GEffectsLogic
    {
        private static Dictionary<int, GEffectsLogic> instances = [];
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
        public double perfusionLevel = 0.0;
        public readonly PhysiologicalModel physModel = new();

        public double PerfusionLevel { get { return perfusionLevel; } }
        public PhysiologicalModel PhysModel => physModel;
        #endregion


        #region outputValues
        private double bloodHead = 1.0;
        private double confusionLevel = 0.0;
        private double tunnelVisionLevel = 0.0;
        private double greyScaleLevel = 0.0;
        private bool primaryColor = true; // true = normal (blackout), false = inverted (redout)

        public double BloodHead { get { return bloodHead; } set { bloodHead = value; } }
        public double ConfusionLevel { get { return confusionLevel; } set { confusionLevel = value; } }
        public double TunnelVisionLevel { get { return tunnelVisionLevel; } set { tunnelVisionLevel = value; } }
        public double GreyScaleLevel { get { return greyScaleLevel; } set { greyScaleLevel = value; } }
        public bool PrimaryColor { get { return primaryColor; } set { primaryColor = value; } }
        #endregion

        private double consciousnessLevel = 1.0;
        public double ConsciousnessLevel { get => consciousnessLevel; set => consciousnessLevel = value; }

        public static double SmoothStep(double x)
        {
            x = Math.Clamp(x, 0.0, 1.0);
            return x * x * (3.0 - (2.0 * x));
        }

        public static double SmoothStep(double x, double min, double max) => (SmoothStep(x) * (max - min)) + min;

        public void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
#if PERFDEBUG
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

            // Determine blackout vs redout from head blood volume
            primaryColor = physModel.BloodHead <= LogicSettings.RestingBloodHead;

            // Map brain O2 to consciousness via smooth step
            double o2Normalized = Math.Clamp(
                (physModel.BrainO2 - LogicSettings.BrainO2Blackout) / (LogicSettings.BrainO2Full - LogicSettings.BrainO2Blackout),
                0.0, 1.0);

            double perfRatio = Math.Clamp(physModel.BloodHead / LogicSettings.RestingBloodHead, 0.0, 1.0);
            perfusionLevel = perfRatio;

            // Use soft minimum for consciousness mapping (not blackout threshold)
            double perfNorm = Math.Clamp(
                (perfRatio - LogicSettings.ConsciousnessPerfusionSoftMinRatio) /
                (1.0 - LogicSettings.ConsciousnessPerfusionSoftMinRatio),
                0.0, 1.0);

            // "Weakest-link" blend: either low O2 or low perfusion can drive LOC
            double o2Term = Math.Pow(o2Normalized, LogicSettings.ConsciousnessO2Exponent);
            double perfTerm = Math.Pow(perfNorm, LogicSettings.ConsciousnessPerfusionExponent);

            // Geometric blend: both channels matter strongly, avoids high flat plateau
            double targetConsciousness = o2Term * perfTerm;

            // New: sustained hypoxia/hypoperfusion bias (prevents 5G plateau like 0.09)
            double combinedDeficit = 1.0 - ((0.5 * o2Normalized) + (0.5 * perfNorm));
            targetConsciousness = Math.Max(
                0.0,
                targetConsciousness - (LogicSettings.ConsciousnessDeficitBias * combinedDeficit * combinedDeficit));

            // Optional: hard cap when perfusion is critically low
            if (perfNorm < 0.25)
            {
                targetConsciousness = Math.Min(targetConsciousness, perfNorm * 0.75);
            }

            // Dynamic loss tau (non-linear so mid-G loses slower)
            double lossSeverity = Math.Pow(1.0 - targetConsciousness, LogicSettings.ConsciousnessLossSeverityExponent);

            double baseLossTau = LogicSettings.ConsciousnessLossTauMax +
                ((LogicSettings.ConsciousnessLossTauMin - LogicSettings.ConsciousnessLossTauMax) * lossSeverity);

            // Critical collapse accelerator (mostly affects extreme +G)
            double criticalPerf = 1.0 - Math.Clamp(
                perfNorm / LogicSettings.ConsciousnessCriticalPerfusionNorm, 0.0, 1.0);

            double criticalO2 = 1.0 - Math.Clamp(
                o2Normalized / LogicSettings.ConsciousnessCriticalO2Norm, 0.0, 1.0);

            double critical = Math.Max(criticalPerf, criticalO2);
            double criticalTauMultiplier = 1.0 -
                ((1.0 - LogicSettings.ConsciousnessCriticalTauMultiplierMin) * SmoothStep(critical));

            double lossTau = baseLossTau * criticalTauMultiplier;
            double tau = targetConsciousness < consciousnessLevel ? lossTau : LogicSettings.ConsciousnessRecoveryTau;

            // Remove hard acceleration branch to avoid sudden drop at mid-G
            consciousnessLevel += (targetConsciousness - consciousnessLevel) * (1.0 - Math.Exp(-deltaTime / tau));
            consciousnessLevel = Math.Clamp(consciousnessLevel, 0.0, 1.0);

            if (consciousnessLevel < 0.01)
            {
                consciousnessLevel = 0.0;
            }

            bloodHead = physModel.BloodHead;

            // Visual symptoms should start from physiology, but once consciousness is collapsing
            // they should continue toward full obscuration even if perfusion briefly rebounds.
            double visualPerf = Math.Clamp((perfRatio - 0.45) / 0.55, 0.0, 1.0);
            double visualO2 = Math.Clamp((o2Normalized - 0.15) / 0.85, 0.0, 1.0);

            // Fast visual reserve from physiology.
            double visualReserve = (0.7 * visualPerf) + (0.3 * visualO2);
            double visualDeficit = 1.0 - visualReserve;

            // Early/mid visual impairment path.
            // Keeps onset before LOC, but avoids saturating too early.
            double physiologicalTunnelTarget = Math.Clamp((visualDeficit - 0.18) / 0.82, 0.0, 1.0);
            physiologicalTunnelTarget = Math.Pow(physiologicalTunnelTarget, 2.2);

            // Blackout path: if consciousness gets close to zero, tunnel vision must approach 1.
            // This also reduces sensitivity to short perfusion recoveries.
            double blackoutTunnelTarget = Math.Clamp(1.0 - consciousnessLevel, 0.0, 1.0);
            blackoutTunnelTarget = Math.Pow(blackoutTunnelTarget, 0.65);

            // Use whichever impairment is worse.
            double tunnelTarget = Math.Max(physiologicalTunnelTarget, blackoutTunnelTarget);

            // Greyscale can remain the same in v1.
            double greyTarget = tunnelTarget;

            double tunnelTau = tunnelTarget > tunnelVisionLevel ? LogicSettings.VisualInTau : LogicSettings.VisualOutTau;
            double greyTau = greyTarget > greyScaleLevel ? LogicSettings.VisualInTau : LogicSettings.VisualOutTau;

            tunnelVisionLevel += (tunnelTarget - tunnelVisionLevel) * (1.0 - Math.Exp(-deltaTime / tunnelTau));
            greyScaleLevel += (greyTarget - greyScaleLevel) * (1.0 - Math.Exp(-deltaTime / greyTau));

            tunnelVisionLevel = Math.Clamp(tunnelVisionLevel, 0.0, 1.0);
            greyScaleLevel = Math.Clamp(greyScaleLevel, 0.0, 1.0);

            // Hard guarantee: near total LOC means near total visual loss.
            double tunnelFloorFromConsciousness = SmoothStep(Math.Clamp((1.0 - consciousnessLevel) / 0.92, 0.0, 1.0));
            tunnelVisionLevel = Math.Max(tunnelVisionLevel, tunnelFloorFromConsciousness);
            greyScaleLevel = Math.Max(greyScaleLevel, tunnelFloorFromConsciousness);

            Logger.Log($"Gz: {currentGz:f2}, headBlood: {physModel.BloodHead:f4}, brainO2: {physModel.BrainO2:f4}, HR: {physModel.HeartRateMultiplier:f2}, consciousness: {consciousnessLevel:f4}, dT: {deltaTime:f4}");

#if PERFDEBUG
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
