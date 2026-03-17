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
            // Update cummulated G-forces
            cummulatedGx += currentGx * deltaTime;
            cummulatedGy += currentGy * deltaTime;
            cummulatedGz += currentGz * deltaTime;
            // Update last G-forces
            lastGx = currentGx;
            lastGy = currentGy;
            lastGz = currentGz;
            // Update time
            time += deltaTime;
        }


        public override int GetHashCode() => UniqueID;

        public GEffectsLogic()
        {
            instances.Add(UniqueID, this);
        }
    }
}
