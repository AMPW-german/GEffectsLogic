using static GEffectsLogic.Logging.Logger;

namespace GEffectLogicTests.Logging
{
    internal class TestLogging
    {

        public const string LogPrefix = "[LogicTests] ";

        public bool LogStr(string message, LogLevel level = LogLevel.Debug)
        {
            switch (level)
            {
                case LogLevel.Debug:
                    if (GEffectsLogic.LogicSettings.DebugMode)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(LogPrefix + "Debug: " + message);
                    }
                    break;
                case LogLevel.Info:
                    if (!GEffectsLogic.LogicSettings.SuppresInfoLogs)
                        Console.WriteLine(LogPrefix + "Info: " + message);
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(LogPrefix + "Warning: " + message);
                    break;
                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(LogPrefix + "Error: " + message);
                    break;
                default:
                    Console.WriteLine(LogPrefix + "Unknown LogLevel: " + message);
                    break;
            }
            Console.ResetColor();
            return true;
        }

        public TestLogging()
        {
        }
    }
}
