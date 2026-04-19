using System;
using System.Collections.Generic;
using System.Text;

namespace GEffectsLogic.Logging
{
    public abstract class Logger
    {
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        public static Logger Instance;

        public static bool Log(string message, LogLevel level = LogLevel.Debug) => Instance?.LogStr(message, level) ?? false;

        
        public LogLevel Level;
        public abstract bool LogStr(string message, LogLevel level = LogLevel.Debug);
    }
}
