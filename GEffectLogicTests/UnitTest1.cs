using GEffectLogicTests.Logging;
using GEffectsLogic;
using Xunit.Abstractions;

namespace GEffectLogicTests
{
    public class UnitTest1
    {
        private readonly ITestOutputHelper _output;
        private TestLogging loggerInstance;
        private GEffectsLogic.GEffectsLogic logicInstance;

        public UnitTest1(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Test1()
        {
            loggerInstance = new TestLogging(_output);
            GEffectsLogic.Logging.Logger.Instance = new LogicLogging(_output);
            GEffectsLogic.LogicSettings.DebugMode = true;
            logicInstance = new GEffectsLogic.GEffectsLogic();

            for (double t = 0; t < 10; t += 0.1)
            {
                logicInstance.Update(0.1, 0, 0, t);
                loggerInstance.LogStr($"Time: {logicInstance.Time}, LastGz: {logicInstance.LastGz}, CummulatedGz: {logicInstance.CummulatedGz}, ConsiousnessLevel: {logicInstance.ConsiousnessLevel}, ConfusionLevel: {logicInstance.ConfusionLevel}, TunnelVisionLevel: {logicInstance.TunnelVisionLevel}, GreyScaleLevel: {logicInstance.GreyScaleLevel}");
            }
            for (double t = 10; t > 0; t -= 0.1)
            {
                logicInstance.Update(0.1, 0, 0, t);
                loggerInstance.LogStr($"Time: {logicInstance.Time}, LastGz: {logicInstance.LastGz}, CummulatedGz: {logicInstance.CummulatedGz}, ConsiousnessLevel: {logicInstance.ConsiousnessLevel}, ConfusionLevel: {logicInstance.ConfusionLevel}, TunnelVisionLevel: {logicInstance.TunnelVisionLevel}, GreyScaleLevel: {logicInstance.GreyScaleLevel}");
            }
        }
    }
}
