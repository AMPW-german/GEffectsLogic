using System;
using System.Collections.Generic;
using System.Text;

namespace GEffectsLogic
{
    public static class LogicSettings
    {
        public static double GxPTolerance { get; set; } = 3; // Gx+ tolerance
        public static double GxMTolerance { get; set; } // Gx- tolerance
        public static double PushPullLimitModifier { get; set; }
        public static double GzPTolerance { get; set; } = 0.1;
        public static double GzMTolerance { get; set; } = 0.1;
        public static double GyTolerance { get; set; }

        public static bool DebugMode { get; set; } = false;
        public static bool SuppresInfoLogs { get; set; } = false;
    }
}
