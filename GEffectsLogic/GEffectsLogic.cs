using GEffectsLogic.Logging;

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
        private double cummulatedGx = 0.0;
        private double cummulatedGy = 0.0;
        private double cummulatedGz = 0.0;

        public double CummulatedGx { get { return cummulatedGx; } }
        public double CummulatedGy { get { return cummulatedGy; } }
        public double CummulatedGz { get { return cummulatedGz; } }
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


        public void Update(double deltaTime, double currentGx, double currentGy, double currentGz)
        {
            // dT limits/warnings are probably necessary at some point but for now they are not necessary and unknown
            // For now we will only use Gz forces

            // Update last G-forces
            //lastGx = currentGx;
            //lastGy = currentGy;
            lastGz = currentGz;
            // Update time
            time += deltaTime;

            // Update cummulated G-forces
            // A certain cummulated Gn reduces consiousness level
            // Having a sharp thershold over which Gn forces increase the cummulated Gn is very unrealistic
            // Higher Gn forces (e.g. +2Gz) should increase the cummulated Gn to a certain level but then flatten out
            /// The cummulated Gz at +2Gz should reduce consiousness by a bit but no further effects (and no G-LOC) should occur
            /// Maybe it would be more realistic to overshoot the constant cummulated Gn at a constant Gn force and then slowly reduce it to the constant cummulated Gn, but for now we will just flatten it out at a certain level
            // This is meant to take into account that the human body can compensate for small increases in G-forces but it reduces the ability to compensate a further increase in G-forces
            // Recovering from a e.g. +5Gz G-LOC should take significantly more time at higher continued Gz forces than at normal Gz forces

            // Best approach is probably to add the current Gz directly to the cummulated Gz and then apply a decay to the cummulated Gz over time, which is faster at higher cummulated Gz levels
            // This way the cummulated Gz will increase faster at higher Gz forces and will also decrease faster at higher cummulated Gz levels, which is more realistic than a sharp thershold or a flattening out at a certain level
            // The decay will also level out the cummulated Gz at constant medium Gz forces (e.g. +2Gz) at a certain level
            // Maybe ax^3 decay would be a better decay function than an exponential decay

            Logger.Log($"CurrentGz: {currentGz:f2}, cummulatedGz: {cummulatedGz:f4}, dT: {deltaTime:f4}");

            cummulatedGz += Math.Pow(currentGz, 2) * deltaTime; // Add current Gz to cummulated Gz
            cummulatedGz -= Math.Pow(Math.E, (cummulatedGz >= 0 ? LogicSettings.GzPTolerance : LogicSettings.GzMTolerance) * cummulatedGz) * deltaTime; // Apply decay to cummulated Gz
        }


        public override int GetHashCode() => UniqueID;

        public GEffectsLogic()
        {
            if (Logger.Instance == null) throw new NullReferenceException("Logger instance is not set. Please initialize a Logger before creating GEffectsLogic instances.");

            instances.Add(UniqueID, this);
        }
    }
}
