// GEffectsLogic
// Copyright (C) 2026 AMPW
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System.Collections.Concurrent;
using GEffectsLogic;
using GEffectsLogic.Logging;

namespace GEffectLogicTests;

[CollectionDefinition("Global logger", DisableParallelization = true)]
public class GlobalLoggerCollection;

[Collection("Global logger")]
public class LoggingAndInstanceConcurrencyTests
{
    [Fact]
    public void LocalLoggerTakesPrecedenceOverStaticLogger()
    {
        var originalLogger = Logger.Instance;
        RecordingLogger fallbackLogger = new();
        RecordingLogger localLogger = new();
        Logger.Instance = fallbackLogger;

        try
        {
            GEffectsLogicInstance logicInstance = new(localLogger);
            logicInstance.Update(0.0, 0.0, 0.0, 0.0);

            Assert.Single(localLogger.Messages);
            Assert.Empty(fallbackLogger.Messages);
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
    }

    [Fact]
    public void CanConstructWithoutAnyLogger()
    {
        var originalLogger = Logger.Instance;
        Logger.Instance = null;

        try
        {
            GEffectsLogicInstance logicInstance = new();

            Assert.NotNull(logicInstance);
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
    }

    [Fact]
    public void StaticLoggerIsUsedWithoutALocalLogger()
    {
        var originalLogger = Logger.Instance;
        RecordingLogger fallbackLogger = new();
        Logger.Instance = fallbackLogger;

        try
        {
            Assert.True(Logger.Log("Static fallback", new GEffectsLogicInstance(), Logger.LogLevel.Info));
            Assert.Single(fallbackLogger.Messages);
        }
        finally
        {
            Logger.Instance = originalLogger;
        }
    }

    private sealed class RecordingLogger : Logger
    {
        public ConcurrentQueue<string> Messages { get; } = [];

        public override bool LogStr(string message, GEffectsLogicInstance logicInstance, LogLevel level = LogLevel.Debug)
        {
            Messages.Enqueue(message);
            return true;
        }
    }
}
