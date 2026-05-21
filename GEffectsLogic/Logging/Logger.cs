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

        public static Logger? Instance { get; set; }

        public static bool Log(string message, int id, LogLevel level = LogLevel.Debug) => Instance?.LogStr(message, id, level) ?? false;


        public LogLevel Level;
        public abstract bool LogStr(string message, int id, LogLevel level = LogLevel.Debug);
    }
}
