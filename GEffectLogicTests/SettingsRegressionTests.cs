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

using System.Reflection;
using GEffectsLogic;

namespace GEffectLogicTests;

[Collection("Global logger")]
public class SettingsRegressionTests
{
    private static readonly InstanceSnapshot[] Expected =
    [
        new(4.000000000000002, 0, 0, 1, false, new(0.2, 0, 0.35000000000000003, 0.45, 0.9000503270020906, 0.9784186095227382, 0.9259076080944135, 0.9000503270020906, 1, 1, 1, 1, 0, 0, 0, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false)),
        new(8.999999999999984, 0, 0, 5, false, new(0.06788773839089755, 0, 0.4651753227941103, 0.46693693881499215, 0.7400395000774558, 0.9785918377846955, 0.9491572340274416, 0.7400395000774558, 1, 1.6938273823312255, 0.3394386919544877, 0.8773470063291262, 0, 0.034431117410691374, 0.011293288027043885, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.17104521709191814, 0, 0, 0.05360280493518963, 0.07074021930923804, 0.2497775416061847, false, false, false)),
        new(49.000000000000426, 0, 0, 5, false, new(0.0713787560989047, 0, 0.44663889173159516, 0.4819823521695001, 0.498093993116642, 0.9786240862387688, 0.97692673921915, 0.498093993116642, 1, 2.531477754763799, 0.3568937804945235, 0.0005164503547364396, 0, 0.633719359836331, 0.1711978173515131, 0.24935880715298006, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.7617815798130325, 0, 1, 0.7133915448951582, 0.6648837221165227, 0.7133915448951582, true, false, false)),
        new(69.00000000000036, 0, 0, 1, false, new(0.19999893991847173, 0, 0.35000046378566874, 0.45000059629585953, 0.922811087842982, 0.9792579381325465, 0.9755418380573092, 0.922811087842982, 1, 1.0725545790725557, 0.9999946995923586, 0.7910432034212469, 0, 0.578673401490233, 0.16639613177813325, 0.24387420988754313, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.05695236626721698, 0, 0, 4.3356784456093104E-05, 0.013591508448026717, 4.3356784456093104E-05, false, false, false)),
        new(78.99999999999979, 0, 0, -5, false, new(0.22485042993999205, 0.12425214969996018, 0, 0.775149570060008, 0.7716048403065747, 0.9783958984219943, 0.9752668107660594, 0.7716048403065747, 1, 0.7220969375805882, 1, 8.613875515754444E-06, 1, 0.5413530091461193, 0.16094101771373626, 0.23392157098495128, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.015012480756957004, 0.9999999977535937, 1, 2.9213571571592833E-07, 0.0018394106454265007, 2.9213571571592833E-07, true, false, false)),
        new(98.99999999999865, 0, 0, 1, false, new(0.20000000000000007, 2.7755575615628914E-16, 0.3499999999999998, 0.4500000000000001, 0.86090112734861, 0.9788514161776871, 0.9740313136918716, 0.86090112734861, 1, 1.0090482281393827, 1, 0.002129457811561834, 0.45177075911711917, 0.4737777097882976, 0.15056144624929804, 0.21521823484601885, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.0010431189744092623, 5.650912540715184E-05, 1, 1.3262940974616398E-11, 3.3689981149724944E-05, 1.3262940974616398E-11, true, false, false)),
        new(178.99999999999412, 15, 0, 0, false, new(0.202239158817255, 0.011195794086274885, 0.3446185825224376, 0.45314225866030744, 0.33707542121439077, 0.09921959801383115, 0.09490760171767872, 0.33707542121439077, 0.6542114421573354, 1.002574583607075, 1, 0.05344554773498459, 0.01844772221342222, 0.2779399023354285, 0.11531927834750413, 0.15421060387358893, 1.25, 1, 3.25, 1, 0, 0, 0.7499999155986189, 1, 2.431416567862166E-08, 0.050602361158889025, 1, 5.634567172719303E-29, 3.791307914972714E-12, 5.634567172719303E-29, true, false, true)),
        new(188.99999999999355, 0, 4, 1, false, new(0.02, 0, 0.5303875310589989, 0.449612468941001, 0.18, 0.4089089140773925, 0.3880586001652804, 0.18, 0.9009299194787094, 2.578299426929676, 0.09999999999999999, 1.0722845995680278E-07, 0.012365879221897597, 0.2600147200537993, 0.11153866271367198, 0.1839264251068913, 1.25, 0.9200444146293185, 1, 0.1, 0.5117275836979442, 0.15430081047771155, 0.9661661677683621, 1, 0.9928275778211009, 0.0003411019936382028, 1, 0.7090021081536685, 0.9892606812198369, 0.7090021081536685, true, false, true))
    ];

    [Fact]
    public void DefaultProfileRetainsFrozenPhysiologicalBehavior()
    {
        var actual = RunProfile(new GEffectsLogicInstance());
        Assert.Equal(Expected.Length, actual.Length);
        for (var i = 0; i < Expected.Length; i++)
        {
            AssertClose(Expected[i].Time, actual[i].Time, nameof(InstanceSnapshot.Time));
            AssertClose(Expected[i].LastGx, actual[i].LastGx, nameof(InstanceSnapshot.LastGx));
            AssertClose(Expected[i].LastGy, actual[i].LastGy, nameof(InstanceSnapshot.LastGy));
            AssertClose(Expected[i].LastGz, actual[i].LastGz, nameof(InstanceSnapshot.LastGz));
            Assert.Equal(Expected[i].IsStable, actual[i].IsStable);
            foreach (var property in typeof(ModelSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var expectedValue = property.GetValue(Expected[i].Model);
                var actualValue = property.GetValue(actual[i].Model);
                if (expectedValue is double number)
                    AssertClose(number, (double)actualValue!, property.Name);
                else
                    Assert.Equal(expectedValue, actualValue);
            }
        }
    }

    private static void AssertClose(double expected, double actual, string name)
    {
        Assert.True(Math.Abs(expected - actual) <= 1e-12,
            $"{name}: expected {expected:R}, actual {actual:R}.");
    }

    internal static InstanceSnapshot[] RunProfile(GEffectsLogicInstance instance)
    {
        List<InstanceSnapshot> snapshots = [];
        Advance(40, 0.0, 0.0, _ => 1.0);
        Advance(50, 0.0, 0.0, i => 1.0 + 4.0 * (i + 1) / 50.0);
        Advance(400, 0.0, 0.0, _ => 5.0);
        Advance(200, 0.0, 0.0, _ => 1.0);
        Advance(100, 0.0, 0.0, _ => -5.0);
        Advance(200, 0.0, 0.0, _ => 1.0);
        Advance(800, 15.0, 0.0, _ => 0.0);
        Advance(100, 0.0, 4.0, _ => 1.0);
        return snapshots.ToArray();

        void Advance(int count, double gx, double gy, Func<int, double> gz)
        {
            for (var i = 0; i < count; i++)
                instance.Update(0.1, gx, gy, gz(i));
            snapshots.Add(Capture(instance));
        }
    }

    internal static InstanceSnapshot Capture(GEffectsLogicInstance instance) => new(
        instance.Time,
        instance.LastGx,
        instance.LastGy,
        instance.LastGz,
        instance.IsStable,
        CaptureModel(instance.PhysModel));

    internal static ModelSnapshot CaptureModel(PhysiologicalModel model) => new(
        model.BloodHead,
        model.BloodHeadOverfill,
        model.BloodCore,
        model.BloodLower,
        model.BloodO2Head,
        model.BloodO2Core,
        model.BloodO2Lower,
        model.BrainO2,
        model.ArterialOxygenation,
        model.HeartRateMultiplier,
        model.PerfusionLevel,
        model.ConsciousnessLevel,
        model.CerebralPressureImpairment,
        model.StrainingFatigue,
        model.GSuitFatigue,
        model.HrFatigue,
        model.FatigueHeartRateFloor,
        model.RespiratoryFatigue,
        model.GxEffectiveTolerance,
        model.GyEffectiveTolerance,
        model.GyNeckFatigue,
        model.LungCompressionLevel,
        model.PainLevel,
        model.SuddenLoCAccumulator,
        model.VisualTunnelVisionLevel,
        model.VisualRedoutLevel,
        model.VisualLoCLevel,
        model.VisualGrayscaleLevel,
        model.VisualFilmGrainLevel,
        model.VisualBlurLevel,
        model.IsUnconscious,
        model.IsDead,
        model.InSuddenLoC);

    internal sealed record InstanceSnapshot(
        double Time,
        double LastGx,
        double LastGy,
        double LastGz,
        bool IsStable,
        ModelSnapshot Model);

    internal sealed record ModelSnapshot(
        double BloodHead,
        double BloodHeadOverfill,
        double BloodCore,
        double BloodLower,
        double BloodO2Head,
        double BloodO2Core,
        double BloodO2Lower,
        double BrainO2,
        double ArterialOxygenation,
        double HeartRateMultiplier,
        double PerfusionLevel,
        double ConsciousnessLevel,
        double CerebralPressureImpairment,
        double StrainingFatigue,
        double GSuitFatigue,
        double HrFatigue,
        double FatigueHeartRateFloor,
        double RespiratoryFatigue,
        double GxEffectiveTolerance,
        double GyEffectiveTolerance,
        double GyNeckFatigue,
        double LungCompressionLevel,
        double PainLevel,
        double SuddenLoCAccumulator,
        double VisualTunnelVisionLevel,
        double VisualRedoutLevel,
        double VisualLoCLevel,
        double VisualGrayscaleLevel,
        double VisualFilmGrainLevel,
        double VisualBlurLevel,
        bool IsUnconscious,
        bool IsDead,
        bool InSuddenLoC);
}
