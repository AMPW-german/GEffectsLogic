using System;
using System.Collections.Generic;
using System.Text;

namespace GEffectsLogic
{
    public static class LogicSettings
    {
        public static double GxPTolerance { get; private set; } // Gx+ tolerance
        public static double GxMTolerance { get; private set; } // Gx- tolerance
        public static double PushPullLimitModifier { get; private set; }
        public static double GzPTolerance { get; private set; }
        public static double GzMTolerance { get; private set; }
        public static double GyTolerance { get; private set; }
    }
}
