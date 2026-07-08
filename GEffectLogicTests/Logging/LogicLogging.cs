using GEffectsLogic;
using GEffectsLogic.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests.Logging;

public class LogicLogging : Logger
{
    public const string LogPrefix = "[GEffectsLogicInstance] ";

    private readonly ITestOutputHelper _output;

    public LogicLogging(ITestOutputHelper output)
    {
        _output = output;
        Instance = this;
    }

    public override bool LogStr(string message, int id, LogLevel level = LogLevel.Debug)
    {
        if (_output == null) return false;

        switch (level)
        {
            case LogLevel.Debug:
                if (LogicSettings.DebugMode)
                    _output.WriteLine($"{LogPrefix}Debug ({id}): {message}");
                break;
            case LogLevel.Info:
                if (!LogicSettings.SuppresInfoLogs)
                    _output.WriteLine($"{LogPrefix}Info ({id}): {message}");
                break;
            case LogLevel.Warning:
                _output.WriteLine($"{LogPrefix}Warning ({id}): {message}");
                break;
            case LogLevel.Error:
                _output.WriteLine($"{LogPrefix}Error ({id}): {message}");
                break;
            default:
                _output.WriteLine($"{LogPrefix}Unknown LogLevel ({id}): {message}");
                break;
        }

        return true;
    }
}