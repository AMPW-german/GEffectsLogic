namespace GEffectsLogic.Logging;

public abstract class Logger
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }


    public LogLevel Level;

    public static Logger? Instance { get; set; }

    public static bool Log(string message, int id, LogLevel level = LogLevel.Debug)
    {
        return Instance?.LogStr(message, id, level) ?? false;
    }

    public abstract bool LogStr(string message, int id, LogLevel level = LogLevel.Debug);
}