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