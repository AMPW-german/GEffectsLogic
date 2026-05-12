namespace GEffectsLogic
{
    /// <summary>
    /// Simplified physiological model for G-force effects.
    /// 
    /// Design:
    /// - 3 blood compartments: head, core (thorax/heart), lower body (abdomen + legs)
    /// - G-forces create hydrostatic pressure that shifts blood between compartments
    /// - Total blood volume is conserved
    /// - Brain oxygen saturation depends on head blood volume (perfusion)
    /// - Heart rate increases via baroreceptor reflex to compensate for reduced head perfusion
    /// - Consciousness is derived from brain oxygen saturation
    /// 
    /// Coordinate system (pilot-body-centric):
    ///   +Gz = headward-to-footward (eyeballs down, blood pools in legs → blackout)
    ///   -Gz = footward-to-headward (eyeballs up, blood pools in head → redout)
    ///   +Gx = chest-to-back (eyeballs in, pilot pushed into seat back)
    ///   -Gx = back-to-chest (eyeballs out)
    ///   Gy  = lateral
    /// 
    /// For the first version only Gz is fully modeled. Gx/Gy are stubbed for expansion.
    /// </summary>
    public class PhysiologicalModel
    {
        // --- Compartment blood volumes (fraction of total, sum = 1.0) ---
        private double bloodHead;
        private double bloodCore;
        private double bloodLower;

        // --- Brain oxygen saturation (0..1) ---
        private double brainO2;

        // --- Baroreceptor reflex: heart rate multiplier (1.0 = resting) ---
        private double heartRateMultiplier;

        // --- Straining effort (0..1): pilot anti-G straining maneuver ---
        private double strainingLevel;

        #region Public read-only state

        /// <summary>Fraction of total blood in the head compartment.</summary>
        public double BloodHead => bloodHead;

        /// <summary>Fraction of total blood in the core compartment.</summary>
        public double BloodCore => bloodCore;

        /// <summary>Fraction of total blood in the lower body compartment.</summary>
        public double BloodLower => bloodLower;

        /// <summary>Brain oxygen saturation (0 = no oxygen, 1 = fully saturated).</summary>
        public double BrainO2 => brainO2;

        /// <summary>Current heart rate multiplier from baroreceptor reflex.</summary>
        public double HeartRateMultiplier => heartRateMultiplier;

        /// <summary>Current straining level (0 = none, 1 = max).</summary>
        public double StrainingLevel => strainingLevel;

        #endregion

        public PhysiologicalModel()
        {
            Reset();
        }

        /// <summary>Reset all state to resting equilibrium.</summary>
        public void Reset()
        {
            // Resting blood distribution (approximate physiological ratios)
            bloodHead = LogicSettings.RestingBloodHead;
            bloodCore = LogicSettings.RestingBloodCore;
            bloodLower = LogicSettings.RestingBloodLower;
            brainO2 = 1.0;
            heartRateMultiplier = 1.0;
            strainingLevel = 0.0;
        }

        /// <summary>
        /// Advance the model by deltaTime seconds under the given G-force vector.
        /// </summary>
        /// <param name="dt">Time step in seconds.</param>
        /// <param name="gz">Current Gz (positive = headward-to-footward).</param>
        /// <param name="gx">Current Gx (unused in v1, reserved).</param>
        /// <param name="gy">Current Gy (unused in v1, reserved).</param>
        public void Update(double dt, double gz, double gx = 0.0, double gy = 0.0)
        {
            // --- 1. Hydrostatic blood shift from Gz ---
            // Positive Gz pushes blood from head → lower body
            // Negative Gz pushes blood from lower body → head
            // The shift rate is proportional to Gz magnitude beyond the 1G baseline
            double gzNet = gz - 1.0; // subtract resting 1G (Earth surface sitting upright)

            // G-suit and straining reduce effective Gz on the lower compartment
            double gSuitFactor = 1.0 - LogicSettings.GSuitEffectiveness * strainingLevel;
            double effectiveGzShift = gzNet * gSuitFactor;

            // Blood flow rate between compartments (volume fraction per second per G)
            double shiftRate = LogicSettings.HydrostaticShiftRate * effectiveGzShift;

            // Head ↔ Core shift: head loses blood proportional to positive shiftRate
            double headCoreShift = shiftRate * LogicSettings.HeadCoreShiftFraction * dt;
            // Core ↔ Lower shift: lower gains blood proportional to positive shiftRate
            double coreLowerShift = shiftRate * LogicSettings.CoreLowerShiftFraction * dt;

            bloodHead -= headCoreShift;
            bloodCore += headCoreShift - coreLowerShift;
            bloodLower += coreLowerShift;

            // --- 2. Passive return toward resting distribution ---
            // Blood naturally returns toward equilibrium via venous return, muscle tone, etc.
            // Heart rate multiplier accelerates this return.
            double returnRate = LogicSettings.PassiveReturnRate * heartRateMultiplier;

            double headReturn = returnRate * (LogicSettings.RestingBloodHead - bloodHead) * dt;
            double coreReturn = returnRate * (LogicSettings.RestingBloodCore - bloodCore) * dt;
            double lowerReturn = returnRate * (LogicSettings.RestingBloodLower - bloodLower) * dt;

            bloodHead += headReturn;
            bloodCore += coreReturn;
            bloodLower += lowerReturn;

            // --- 3. Enforce conservation (redistribute any numerical drift) ---
            double total = bloodHead + bloodCore + bloodLower;
            bloodHead /= total;
            bloodCore /= total;
            bloodLower /= total;

            // Clamp to prevent negative volumes
            bloodHead = Math.Max(bloodHead, 0.0);
            bloodCore = Math.Max(bloodCore, 0.0);
            bloodLower = Math.Max(bloodLower, 0.0);

            // Re-normalize after clamp
            total = bloodHead + bloodCore + bloodLower;
            bloodHead /= total;
            bloodCore /= total;
            bloodLower /= total;

            // --- 4. Brain oxygen model ---
            // O2 delivery depends on head blood volume relative to resting
            double perfusionRatio = bloodHead / LogicSettings.RestingBloodHead;
            // O2 consumption is constant; delivery scales with perfusion
            double o2Delivery = perfusionRatio * LogicSettings.O2DeliveryRate;
            double o2Consumption = LogicSettings.O2ConsumptionRate;

            double dO2 = (o2Delivery - o2Consumption) * dt;
            brainO2 = Math.Clamp(brainO2 + dO2, 0.0, 1.0);

            // --- 5. Baroreceptor reflex ---
            // When head perfusion drops, heart rate increases (delayed response)
            double targetHR = 1.0 + LogicSettings.BaroreceptorGain * Math.Max(0, 1.0 - perfusionRatio);
            targetHR = Math.Min(targetHR, LogicSettings.MaxHeartRateMultiplier);

            double hrTau = LogicSettings.BaroreceptorTimeConstant;
            heartRateMultiplier += (targetHR - heartRateMultiplier) * (1.0 - Math.Exp(-dt / hrTau));
        }
    }
}
