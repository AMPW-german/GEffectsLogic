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
using static GEffectLogicTests.SettingsRegressionTests;

namespace GEffectLogicTests;

[Collection("Global logger")]
public class InstanceSettingsTests
{
    [Fact]
    public void SettingsAreInitOnlyNumericRecordsWithGlobalLogging()
    {
        var instanceProperties = typeof(LogicSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(122, instanceProperties.Length);
        foreach (var property in instanceProperties)
        {
            Assert.Equal(typeof(double), property.PropertyType);
            Assert.True(property.CanRead);
            Assert.NotNull(property.SetMethod);
            Assert.True(property.SetMethod!.IsPublic);
            Assert.Contains(property.SetMethod.ReturnParameter.GetRequiredCustomModifiers(),
                type => type.FullName == "System.Runtime.CompilerServices.IsExternalInit");
        }

        var staticProperties = typeof(LogicSettings).GetProperties(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(3, staticProperties.Length);
        var defaultProperty = Assert.Single(staticProperties, property => property.Name == nameof(LogicSettings.Default));
        Assert.Null(defaultProperty.SetMethod);
        foreach (var name in new[] { nameof(LogicSettings.DebugMode), nameof(LogicSettings.SuppresInfoLogs) })
        {
            var flag = Assert.Single(staticProperties, property => property.Name == name);
            Assert.Equal(typeof(bool), flag.PropertyType);
            Assert.NotNull(flag.SetMethod);
            Assert.True(flag.SetMethod!.IsPublic);
            Assert.True(flag.SetMethod.IsStatic);
        }

        Assert.True(typeof(LogicSettings).IsSealed);
    }

    [Fact]
    public void DefaultAndCopiedProfilesHaveCallerOwnedIdentity()
    {
        Assert.Same(LogicSettings.Default, new GEffectsLogicInstance().Settings);
        Assert.Same(LogicSettings.Default, new GEffectsLogicInstance(settings: null).Settings);

        var copy = LogicSettings.Default with { GSuitEffectiveness = 0.25 };
        Assert.NotSame(LogicSettings.Default, copy);
        Assert.Equal(0.3, LogicSettings.Default.GSuitEffectiveness);
        Assert.Equal(0.25, copy.GSuitEffectiveness);
        Assert.Equal(LogicSettings.Default.BaroreceptorGain, copy.BaroreceptorGain);

        Assert.Same(copy, new GEffectsLogicInstance(settings: copy).Settings);
    }

    [Fact]
    public void CustomRestingStateIsUsedByConstructionAndReset()
    {
        var profile = LogicSettings.Default with
        {
            RestingBloodHead = 0.25,
            RestingBloodCore = 0.35,
            RestingBloodLower = 0.40,
            HeadBloodO2Resting = 0.91,
            CoreBloodO2Resting = 0.96,
            LowerBloodO2Resting = 0.85
        };
        var instance = new GEffectsLogicInstance(settings: profile);

        Assert.Equal(0.25, instance.PhysModel.BloodHead);
        Assert.Equal(0.35, instance.PhysModel.BloodCore);
        Assert.Equal(0.40, instance.PhysModel.BloodLower);
        Assert.Equal(0.91, instance.PhysModel.BloodO2Head);
        Assert.Equal(0.96, instance.PhysModel.BloodO2Core);
        Assert.Equal(0.85, instance.PhysModel.BloodO2Lower);
        Assert.True(Math.Abs(
            instance.PhysModel.BloodHead + instance.PhysModel.BloodCore + instance.PhysModel.BloodLower - 1.0) <= 1e-12);

        for (var step = 0; step < 200; step++)
            instance.Update(0.1, 15.0, 4.0, 5.0);

        instance.Reset();

        Assert.Same(profile, instance.Settings);
        Assert.Equal(Capture(new GEffectsLogicInstance(settings: profile)), Capture(instance));
    }

    [Fact]
    public void IndependentProfilesDoNotContaminateDefaultUpdates()
    {
        var instanceA = new GEffectsLogicInstance();
        var instanceB = new GEffectsLogicInstance(settings: LogicSettings.Default with { HydrostaticShiftRate = 0.0 });
        var control = new GEffectsLogicInstance();

        for (var step = 0; step < 200; step++)
        {
            instanceA.Update(0.1, 0.0, 0.0, 5.0);
            instanceB.Update(0.1, 0.0, 0.0, 5.0);
            control.Update(0.1, 0.0, 0.0, 5.0);
            Assert.Equal(Capture(control), Capture(instanceA));
        }

        Assert.True(Math.Abs(instanceB.PhysModel.BloodHead - instanceB.Settings.RestingBloodHead) <= 1e-12);
        Assert.True(instanceA.PhysModel.BloodHead < instanceB.PhysModel.BloodHead);
    }

    [Fact]
    public void SharedProfileDoesNotSharePhysiologicalState()
    {
        var profile = LogicSettings.Default with { GSuitEffectiveness = 0.25 };
        var instanceA = new GEffectsLogicInstance(settings: profile);
        var instanceB = new GEffectsLogicInstance(settings: profile);
        Assert.Same(instanceA.Settings, instanceB.Settings);

        for (var step = 0; step < 50; step++)
        {
            instanceA.Update(0.1, 0.0, 0.0, 5.0);
            instanceB.Update(0.1, 0.0, 0.0, -5.0);
        }

        Assert.NotEqual(CaptureModel(instanceA.PhysModel), CaptureModel(instanceB.PhysModel));

        var beforeB = CaptureModel(instanceB.PhysModel);
        var replacement = profile with { GSuitEffectiveness = 0.2 };
        instanceA.ApplySettings(replacement);
        Assert.Same(replacement, instanceA.Settings);
        Assert.Same(profile, instanceB.Settings);
        Assert.Equal(0.25, instanceB.Settings.GSuitEffectiveness);
        Assert.Equal(0.25, profile.GSuitEffectiveness);
        Assert.Equal(beforeB, CaptureModel(instanceB.PhysModel));
    }

    [Fact]
    public void IdenticalProfileCopiesMatchEveryFrame()
    {
        var subject = new GEffectsLogicInstance();
        var copy = new GEffectsLogicInstance(settings: LogicSettings.Default with { });

        for (var step = 0; step < 300; step++)
        {
            var gx = step < 100 ? 15.0 : 0.0;
            var gy = step >= 200 ? 4.0 : 0.0;
            var gz = step < 100 ? 0.0 : step < 200 ? -5.0 : 5.0;
            subject.Update(0.1, gx, gy, gz);
            copy.Update(0.1, gx, gy, gz);
            Assert.Equal(Capture(subject), Capture(copy));
        }
    }

    [Fact]
    public void NegativeGHelpersResolveSelectedProfile()
    {
        var profile = LogicSettings.Default with
        {
            RestingBloodHead = 0.25,
            RestingBloodCore = 0.35,
            RestingBloodLower = 0.40,
            HeadPressureReturnRate = 0.0,
            CerebralPressureImpairmentMaxBuildRate = 0.0,
            CerebralPressureImpairmentNegativeGzRate = 0.0,
            NegativeGHeartStopOverfill = 0.05
        };
        var controlProfile = LogicSettings.Default with
        {
            RestingBloodHead = 0.25,
            RestingBloodCore = 0.35,
            RestingBloodLower = 0.40
        };
        var custom = new GEffectsLogicInstance(settings: profile);
        var control = new GEffectsLogicInstance(settings: controlProfile);

        for (var step = 0; step < 50; step++)
        {
            custom.Update(0.1, 0.0, 0.0, -5.0);
            control.Update(0.1, 0.0, 0.0, -5.0);
        }

        Assert.True(custom.PhysModel.BloodHead > control.PhysModel.BloodHead);
        Assert.True(custom.PhysModel.HeartRateMultiplier < control.PhysModel.HeartRateMultiplier);
        Assert.Equal(0.0, custom.PhysModel.CerebralPressureImpairment);
        Assert.True(control.PhysModel.CerebralPressureImpairment > 0.0);
        Assert.Equal(
            Math.Max((custom.PhysModel.BloodHead - 0.25) / 0.25, 0.0),
            custom.PhysModel.BloodHeadOverfill);

        var before = CaptureModel(custom.PhysModel);
        custom.ApplySettings(profile with
        {
            RestingBloodHead = 0.20,
            RestingBloodCore = 0.35,
            RestingBloodLower = 0.45
        });
        Assert.Equal(
            before with { BloodHeadOverfill = Math.Max((before.BloodHead - 0.20) / 0.20, 0.0) },
            CaptureModel(custom.PhysModel));
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(20.0)]
    public void ApplyPreservesAccumulatedState(double gy)
    {
        var instance = new GEffectsLogicInstance();
        for (var step = 0; step < 200; step++)
            instance.Update(0.1, 6.0, gy, 5.0);
        if (gy == 20.0)
            Assert.True(instance.IsDead);

        var before = Capture(instance);
        var replacement = LogicSettings.Default with
        {
            CardioFatigueMaxHrFloor = 1.1,
            GSuitEffectiveness = 0.25
        };
        instance.ApplySettings(replacement);
        Assert.Same(replacement, instance.Settings);
        Assert.Equal(
            before with { IsStable = false, Model = before.Model with { FatigueHeartRateFloor = 1.1 } },
            Capture(instance));
    }

    [Fact]
    public void ApplyWakesStableInstanceAtUnchangedForces()
    {
        var subject = CreateStableInstance();
        var control = CreateStableInstance();
        var replacement = LogicSettings.Default with
        {
            RestingBloodHead = 0.25,
            RestingBloodCore = 0.35,
            RestingBloodLower = 0.40
        };
        subject.ApplySettings(replacement);
        control.ApplySettings(replacement);
        Assert.False(subject.IsStable);

        var bloodBefore = subject.PhysModel.BloodHead;
        control.PhysModel.Update(0.1, 0.0, 0.0, 1.0);
        subject.Update(0.1, 0.0, 0.0, 1.0);
        Assert.Equal(CaptureModel(control.PhysModel), CaptureModel(subject.PhysModel));
        Assert.NotEqual(bloodBefore, subject.PhysModel.BloodHead);
    }

    [Fact]
    public void ResetClearsStabilizationAndKeepsProfile()
    {
        var instance = CreateStableInstance();
        var profile = instance.Settings;

        instance.Reset();

        Assert.Same(profile, instance.Settings);
        Assert.Equal(0.0, instance.Time);
        Assert.Equal(0.0, instance.LastGx);
        Assert.Equal(0.0, instance.LastGy);
        Assert.Equal(0.0, instance.LastGz);
        Assert.False(instance.IsStable);

        var fresh = new GEffectsLogicInstance(settings: profile);
        instance.Update(0.1, 0.0, 0.0, 1.0);
        fresh.Update(0.1, 0.0, 0.0, 1.0);
        Assert.Equal(Capture(fresh), Capture(instance));
    }

    [Fact]
    public void NullAndSameReferenceApplicationsLeaveStateUntouched()
    {
        var instance = CreateStableInstance();
        var profile = instance.Settings;
        var before = Capture(instance);

        Assert.Throws<ArgumentNullException>(() => instance.ApplySettings(null!));
        Assert.Same(profile, instance.Settings);
        Assert.Equal(before, Capture(instance));

        instance.ApplySettings(profile);
        Assert.Same(profile, instance.Settings);
        Assert.Equal(before, Capture(instance));

        instance.ApplySettings(profile with { });
        Assert.False(instance.IsStable);
    }

    [Fact]
    public void ParallelProfilesAndUpdatesMatchSerialControls()
    {
        const int count = 4_096;
        var results = new object[count];
        Parallel.For(0, count, index =>
        {
            var profile = LogicSettings.Default with { GSuitEffectiveness = 0.1 + index * 0.0002 };
            results[index] = new GEffectsLogicInstance(settings: profile);
        });
        var profiles = new HashSet<LogicSettings>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < count; index++)
        {
            var instance = Assert.IsType<GEffectsLogicInstance>(results[index]);
            Assert.Equal(0.1 + index * 0.0002, instance.Settings.GSuitEffectiveness);
            Assert.True(profiles.Add(instance.Settings));
        }
        Parallel.For(0, count, index =>
        {
            var instance = (GEffectsLogicInstance)results[index];
            for (var step = 0; step < 200; step++)
                instance.Update(0.1, 0.0, 0.0, 1.0 + index % 8);
        });
        for (var index = 0; index < count; index++)
        {
            var snapshot = Capture((GEffectsLogicInstance)results[index]);
            results[index] = snapshot;
            var control = new GEffectsLogicInstance(settings: LogicSettings.Default with
            {
                GSuitEffectiveness = 0.1 + index * 0.0002
            });
            for (var step = 0; step < 200; step++)
                control.Update(0.1, 0.0, 0.0, 1.0 + index % 8);
            Assert.Equal(snapshot, Capture(control));
        }
    }

    private static GEffectsLogicInstance CreateStableInstance()
    {
        var instance = new GEffectsLogicInstance(settings: LogicSettings.Default with { StabilizationTimeThreshold = 0.0 });
        for (var step = 0; step < 100 && !instance.IsStable; step++)
            instance.Update(0.1, 0.0, 0.0, 1.0);
        Assert.True(instance.IsStable);
        return instance;
    }
}
