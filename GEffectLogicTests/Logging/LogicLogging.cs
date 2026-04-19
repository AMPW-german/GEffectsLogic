using GEffectsLogic.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests.Logging
{
    public class LogicLogging : Logger
    {
        public const string LogPrefix = "[GEffectsLogic] ";

        private readonly ITestOutputHelper _output;

        public override bool LogStr(string message, LogLevel level = LogLevel.Debug)
        {
            if (_output == null) return false;

            switch (level)
            {
                case LogLevel.Debug:
                    if (GEffectsLogic.LogicSettings.DebugMode)
                        _output.WriteLine(LogPrefix + "Debug: " + message);
                    break;
                case LogLevel.Info:
                    if (!GEffectsLogic.LogicSettings.SuppresInfoLogs)
                        _output.WriteLine(LogPrefix + "Info: " + message);
                    break;
                case LogLevel.Warning:
                    _output.WriteLine(LogPrefix + "Warning: " + message);
                    break;
                case LogLevel.Error:
                    _output.WriteLine(LogPrefix + "Error: " + message);
                    break;
                default:
                    _output.WriteLine(LogPrefix + "Unknown LogLevel: " + message);
                    break;
            }
            return true;
        }

        public LogicLogging(ITestOutputHelper output)
        {
            _output = output;
            Logger.Instance = this;
        }
    }
}
