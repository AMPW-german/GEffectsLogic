using GEffectLogicTests.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests
{
    public class GLoCDurationTests
    {
        // TODO:
        // - more test cases
        // - extension to the sequence solver to support a "GLoC" flag at the end that indicates that in the given sequence step the GLoC is expected to occur, e.g. [1 9 9 GLoC],[1 GLoC],[-] (this would fail after 10 seconds)

        private readonly ITestOutputHelper _output;

        public GLoCDurationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static void LogEndOfTest(double time, double consciousnessLevel, double lastGz, double expectedStart, double expectedEnd, List<string> infoStrings, TestLogging logger)
        {
            GEffectsLogic.Logging.Logger.LogLevel assertLevel = GEffectsLogic.Logging.Logger.LogLevel.Info;
            logger.LogStr($"{time:F1}s: consciousness level at {consciousnessLevel:F4} at Gz {lastGz:F2}", assertLevel);
            logger.LogStr($"Expected event time range: {expectedStart:F1}s to {expectedEnd:F1}s", assertLevel);
            logger.LogStr("", assertLevel);
            infoStrings.ForEach(s => logger.LogStr(s, GEffectsLogic.Logging.Logger.LogLevel.Info));

            if (time < expectedStart || time >= expectedEnd)
            {
                Assert.Fail($"Event at {time:F1}s, which is outside the expected range of {expectedStart:F1}s to {expectedEnd:F1}s.");
                assertLevel = GEffectsLogic.Logging.Logger.LogLevel.Error;
            }
        }

        private static void PlataueSequenceGLoC(double startG, double endG, double duration, double expectedGLoCTimeStart, double expectedGLoCTimeEnd, ITestOutputHelper output)
        {
            TestLogging loggerInstance = new(output);
            new LogicLogging(output);
            GEffectsLogic.LogicSettings.DebugMode = false;
            GEffectsLogic.GEffectsLogicInstance logicInstance = new();
            List<string> infoStrings = [];

            for (double t = 0; t < duration; t += 0.1)
            {
                logicInstance.Update(0.1, 0, 0, startG + ((endG - startG) * (t / duration)));
                infoStrings.Add($"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, lastGz: {logicInstance.LastGz:F2}");
                if (logicInstance.ConsciousnessLevel <= 0.01) break;
            }
            while (logicInstance.ConsciousnessLevel > 0.01)
            {
                logicInstance.Update(0.1, 0, 0, endG);
                infoStrings.Add($"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, lastGz: {logicInstance.LastGz:F2}");
            }
            LogEndOfTest(logicInstance.Time, logicInstance.ConsciousnessLevel, logicInstance.LastGz, expectedGLoCTimeStart, expectedGLoCTimeEnd, infoStrings, loggerInstance);
        }

        /// <summary>
        /// [1 5 5],[-]
        /// </summary>
        [Fact]
        public void GLoC5G() => PlataueSequenceGLoC(1.0, 5.0, 5.0, 25.0, 35.0, _output);


        /// <summary>
        /// [1 9 5],[-]
        /// </summary>
        [Fact]
        public void GLoC9G() => PlataueSequenceGLoC(1.0, 9.0, 5.0, 5.0, 14.0, _output);


        /// <summary>
        /// [1 9 9],[6],[9 1 9],[-]
        /// Find when consciousness is restored after GLoC (consciousness > 0.75)
        /// </summary>
        [Fact]
        public void GLoC9GRecovery()
        {
            TestLogging loggerInstance = new(_output);
            new LogicLogging(_output);
            GEffectsLogic.LogicSettings.DebugMode = false;
            GEffectsLogic.GEffectsLogicInstance logicInstance = new();
            List<string> infoStrings = [];
            double consciousnessRecoveryStartTime = 0.0;
            // First phase: 1 to 9 Gz over 9 seconds
            for (double t = 0.0; t < 9.0; t += 0.1)
            {
                logicInstance.Update(0.1, 0, 0, 1.0 + ((9.0 - 1.0) * (t / 9.0)));
                infoStrings.Add($"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, lastGz: {logicInstance.LastGz:F2}");
            }
            // Second phase: hold at 9 Gz for 6 seconds
            for (double t = 0.0; t < 6.0; t += 0.1)
            {
                logicInstance.Update(0.1, 0, 0, 9.0);
                infoStrings.Add($"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, lastGz: {logicInstance.LastGz:F2}");
            }
            // Third phase: 9 to 1 Gz over 9 seconds
            for (double t = 9.0; t > 0.0; t -= 0.1)
            {
                logicInstance.Update(0.1, 0, 0, 1.0 + ((9.0 - 1.0) * (t / 9.0)));
                infoStrings.Add($"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, lastGz: {logicInstance.LastGz:F2}");
                if (logicInstance.ConsciousnessLevel > 0.0001 && consciousnessRecoveryStartTime == 0.0) consciousnessRecoveryStartTime = logicInstance.Time;
            }
            // Fourth phase: hold at 1 Gz until recovery
            double recoveryStartTime = logicInstance.Time;
            double recoveryStartConsciousness = logicInstance.ConsciousnessLevel;
            while (logicInstance.ConsciousnessLevel <= 0.75)
            {
                logicInstance.Update(0.1, 0, 0, 1.0);
                infoStrings.Add($"Time: {logicInstance.Time:F1}, consciousness: {logicInstance.ConsciousnessLevel:F4}, lastGz: {logicInstance.LastGz:F2}");
                if (logicInstance.Time - recoveryStartTime > 60) // fail if recovery takes too long
                {
                    LogEndOfTest(logicInstance.Time, logicInstance.ConsciousnessLevel, logicInstance.LastGz, recoveryStartTime, recoveryStartTime + 60, infoStrings, loggerInstance);
                    break;
                }
            }
            loggerInstance.LogStr($"Consciousness recovery started at {consciousnessRecoveryStartTime:F2}s with consciousness level {recoveryStartConsciousness:F4} at {recoveryStartTime:F2}s", GEffectsLogic.Logging.Logger.LogLevel.Info);
            LogEndOfTest(logicInstance.Time, logicInstance.ConsciousnessLevel, logicInstance.LastGz, recoveryStartTime, recoveryStartTime + 60, infoStrings, loggerInstance);
        }
    }
}
