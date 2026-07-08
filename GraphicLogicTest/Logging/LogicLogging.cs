using GEffectsLogic;
using GEffectsLogic.Logging;

namespace GraphicLogicTest.Logging;

public class LogicLogging : Logger
{
    public const string LogPrefix = "[GEffectsLogicInstance] ";

    public LogicLogging()
    {
        Instance = this;
    }

    public override bool LogStr(string message, int id, LogLevel level = LogLevel.Debug)
    {
        switch (level)
        {
            case LogLevel.Debug:
                if (LogicSettings.DebugMode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{LogPrefix}Debug ({id}): {message}");
                }

                break;
            case LogLevel.Info:
                if (!LogicSettings.SuppresInfoLogs)
                    Console.WriteLine($"{LogPrefix}Info ({id}): {message}");
                break;
            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{LogPrefix}Warning ({id}): {message}");
                break;
            case LogLevel.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{LogPrefix}Error ({id}): {message}");
                break;
            default:
                Console.WriteLine($"{LogPrefix}Unknown LogLevel ({id}): {message}");
                break;
        }

        Console.ResetColor();
        return true;
    }
}