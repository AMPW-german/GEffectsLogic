using GEffectLogicTests.Logging;
using Xunit.Abstractions;

namespace GEffectLogicTests
{
    public class GLoCDurationTests
    {
        private readonly ITestOutputHelper _output;

        public GLoCDurationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static void LogEndOfTest(double time, double consciousnessLevel, double lastGz, double expectedStart, double expectedEnd, List<string> infoStrings, TestLogging logger)
        {
            GEffectsLogic.Logging.Logger.LogLevel assertLevel = GEffectsLogic.Logging.Logger.LogLevel.Info;
            if (time < expectedStart || time >= expectedEnd)
            {
                Assert.Fail($"GLoC occurred at {time:F1}s, which is outside the expected range of {expectedStart:F1}s to {expectedEnd:F1}s.");
                assertLevel = GEffectsLogic.Logging.Logger.LogLevel.Error;
            }

            logger.LogStr($"GLoC after {time:F1}s: consciousness level dropped to {consciousnessLevel:F4} at Gz {lastGz:F2}", assertLevel);
            logger.LogStr($"Expected GLoC time range: {expectedStart:F1}s to {expectedEnd:F1}s", assertLevel);
            logger.LogStr("", assertLevel);
            infoStrings.ForEach(s => logger.LogStr(s, GEffectsLogic.Logging.Logger.LogLevel.Info));
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
        public void GLoC5G() => PlataueSequenceGLoC(1.0, 5.0, 5.0, 25.0, 30.0, _output);


        /// <summary>
        /// [1 9 5],[-]
        /// </summary>
        [Fact]
        public void GLoC9G() => PlataueSequenceGLoC(1.0, 9.0, 5.0, 5.0, 14.0, _output);
    }
}
