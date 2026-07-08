// GEffectsLogic
// Copyright (C) 2026 AMPW
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using GEffectsLogic;
using Xunit.Abstractions;
using static GEffectsLogic.Logging.Logger;

namespace GEffectLogicTests.Logging;

internal class TestLogging
{
    public const string LogPrefix = "[LogicTests] ";

    private readonly ITestOutputHelper _output;

    public TestLogging(ITestOutputHelper output)
    {
        _output = output;
    }

    public bool LogStr(string message, LogLevel level = LogLevel.Debug)
    {
        if (_output == null) return false;

        switch (level)
        {
            case LogLevel.Debug:
                if (LogicSettings.DebugMode)
                    _output.WriteLine(LogPrefix + "Debug: " + message);
                break;
            case LogLevel.Info:
                if (!LogicSettings.SuppresInfoLogs)
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
}