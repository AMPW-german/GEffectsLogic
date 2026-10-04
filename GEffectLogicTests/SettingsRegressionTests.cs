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
        new(4.000000000000002, 0, 0, 1, false, new(0.19999999999999976, 0, 0.3500000000000003, 0.4499999999999999, 0.9008182709951343, 0.9778795006033733, 0.9258891029881453, 0.9008182709951343, 1, 1.0000000000000004, 0.9999999999999988, 1, 0, 0, 0, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, false)),
        new(8.999999999999984, 0, 0, 5, false, new(0.06422282613794268, 0, 0.45628025407453765, 0.47949691978751957, 0.7255895532704872, 0.978082111562534, 0.9499122175028082, 0.7255895532704872, 1, 1.7347888210565001, 0.3211141306897134, 0.8525782108027549, 0, 0.03423269079611877, 0.011311246061007876, 0, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.18823238397202333, 0, 0, 0.06069055232398336, 0.08166604317079372, 0.2542435315868356, false, false, false)),
        new(49.000000000000426, 0, 0, 5, false, new(0.07130781979139571, 0, 0.4468121307584997, 0.4818800494501046, 0.4975785559105134, 0.9781636923777715, 0.9766031580989027, 0.4975785559105134, 1, 2.5317500044829266, 0.3565390989569785, 0.0005183497007895734, 0, 0.6333877254003063, 0.17119781818181917, 0.249681120880168, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.762533975088794, 0, 1, 0.7139172329845486, 0.6658690023213447, 0.7139172329845486, true, false, false)),
        new(69.00000000000036, 0, 0, 1, false, new(0.19999965376779866, 0, 0.3500002636359913, 0.45000008259621, 0.9238389657698818, 0.9789987208270388, 0.9753628077791657, 0.9238389657698818, 1, 1.0727351551283861, 0.9999982688389932, 0.7919723948520639, 0, 0.5787835405846011, 0.16650165539267975, 0.24399949583896197, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.057253974159084134, 0, 0, 5.2356290158820424E-05, 0.013699617969199784, 5.2356290158820424E-05, false, false, false)),
        new(78.99999999999979, 0, 0, -5, false, new(0.21830569425494145, 0.0915284712747072, 0.016563536669836537, 0.765130769075222, 0.8300203311491259, 0.978158584669835, 0.9751335062651222, 0.8300203311491259, 1, 0.8474925056798925, 1, 1.686000263523834E-05, 1, 0.5414440074650904, 0.16104218697253628, 0.23403971178062613, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.01522554154846238, 0.9999999877050312, 1, 3.981428848385133E-07, 0.0018787073010015757, 3.981428848385133E-07, true, false, false)),
        new(98.99999999999865, 0, 0, 1, false, new(0.19999999999999996, 0, 0.34996226617028303, 0.45003773382971696, 0.8931331095615084, 0.9786991033065692, 0.9743219701350818, 0.8931331095615084, 1, 1.051738616870638, 0.9999999999999998, 0.002242763943363839, 0.44932685876157397, 0.4738362798661, 0.15065441647823152, 0.21532319030419445, 1.25, 0, 1, 1, 0, 0, 0, 0, 0.0010767303900766916, 6.126398850073827E-05, 1, 2.302391349760072E-11, 3.533136825448474E-05, 2.302391349760072E-11, true, false, false)),
        new(178.99999999999412, 15, 0, 0, false, new(0.20081574493929322, 0.0040787246964660295, 0.34533717083890675, 0.45384708422180003, 0.3323179326664767, 0.09957320178079569, 0.09530088825462736, 0.3323179326664767, 0.6280754275944286, 1.0265339973518601, 1, 0.04210315000146088, 0.018560421602483453, 0.2779248270254628, 0.11538535738845479, 0.15427509084848906, 1.25, 1, 3.25, 1, 0, 0, 0.7499999011620215, 1, 2.6930465455922753E-08, 0.0003125782313907177, 1, 2.574778588688924E-28, 4.4194252177379685E-12, 2.574778588688924E-28, true, false, true)),
        new(188.99999999999355, 0, 4, 1, false, new(0.02, 0, 0.5307723098674114, 0.4492276901325885, 0.18, 0.40871955172463714, 0.3892895222934161, 0.18, 0.8926130064872697, 2.577307236175341, 0.09999999999999999, 1.6853540119596424E-07, 0.012431446904815737, 0.259994836699598, 0.1116019552755835, 0.18399862275662274, 1.25, 0.9200124514390622, 1, 0.1, 0.511266348952706, 0.15408454739295765, 0.9654917445576643, 1, 0.9921561605162345, 2.3769980341637037E-06, 1, 0.7089947876895495, 0.9882573432075298, 0.7089947876895495, true, false, true))
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
