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

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using GEffectsLogic;
using GraphicLogicTest;

namespace GEffectLogicTests;

[Collection("Global logger")]
public class LogicSettingsEditorTests
{
    [Fact]
    public void EntriesEnumerateAllNumericPropertiesInDeclarationOrder()
    {
        var editor = new LogicSettingsEditorViewModel(LogicSettings.Default, _ => { });

        var expectedNames = typeof(LogicSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.MetadataToken)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(122, editor.Entries.Count);
        Assert.Equal(expectedNames, editor.Entries.Select(entry => entry.Name).ToArray());
        Assert.DoesNotContain(editor.Entries, entry =>
            entry.Name is nameof(LogicSettings.Default) or nameof(LogicSettings.DebugMode)
                or nameof(LogicSettings.SuppresInfoLogs));
    }

    [Fact]
    public void DraftAndSuccessiveApplicationsNeverMutatePublishedProfiles()
    {
        var input = LogicSettings.Default with { GSuitEffectiveness = 0.2 };
        var callbacks = new List<LogicSettings>();
        var editor = new LogicSettingsEditorViewModel(input, callbacks.Add);
        var gSuitEntry = editor.Entries.Single(entry => entry.Name == nameof(LogicSettings.GSuitEffectiveness));

        gSuitEntry.ValueText = "0.25";
        Assert.Equal(0.2, input.GSuitEffectiveness);
        Assert.Equal(0.3, LogicSettings.Default.GSuitEffectiveness);
        Assert.Empty(callbacks);

        Assert.True(editor.Apply());
        var first = Assert.Single(callbacks);
        Assert.NotSame(input, first);
        Assert.Equal(0.25, first.GSuitEffectiveness);
        foreach (var property in typeof(LogicSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == nameof(LogicSettings.GSuitEffectiveness)) continue;
            Assert.Equal(property.GetValue(input), property.GetValue(first));
        }

        gSuitEntry.ValueText = "0.3";
        Assert.True(editor.Apply());
        Assert.Equal(2, callbacks.Count);
        Assert.Equal(0.25, callbacks[0].GSuitEffectiveness);
        Assert.Equal(0.3, callbacks[1].GSuitEffectiveness);
        Assert.NotSame(callbacks[0], callbacks[1]);

        Assert.True(editor.Apply());
        Assert.Equal(3, callbacks.Count);
        Assert.NotSame(callbacks[1], callbacks[2]);
        Assert.Equal(0.3, callbacks[2].GSuitEffectiveness);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e9999")]
    [InlineData("1,5")]
    public void MalformedEntriesAreRejectedWithoutPublishing(string invalidText)
    {
        var input = LogicSettings.Default;
        var callbacks = new List<LogicSettings>();
        var editor = new LogicSettingsEditorViewModel(input, callbacks.Add);
        var errorNotifications = new List<string?>();
        ((INotifyPropertyChanged)editor).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LogicSettingsEditorViewModel.ErrorText))
                errorNotifications.Add(editor.ErrorText);
        };

        var gSuitEntry = editor.Entries.Single(entry => entry.Name == nameof(LogicSettings.GSuitEffectiveness));
        var lastEntry = editor.Entries[^1];
        var originalText = lastEntry.ValueText;
        var valueNotifications = 0;
        lastEntry.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LogicSettingEntry.ValueText)) valueNotifications++;
        };

        gSuitEntry.ValueText = "0.25";
        lastEntry.ValueText = invalidText;
        Assert.True(valueNotifications > 0);

        Assert.False(editor.Apply());
        Assert.NotNull(editor.ErrorText);
        Assert.Contains(lastEntry.Name, editor.ErrorText, StringComparison.Ordinal);
        Assert.Empty(callbacks);
        Assert.Equal(0.3, input.GSuitEffectiveness);
        Assert.Equal(invalidText, lastEntry.ValueText);

        lastEntry.ValueText = originalText;
        Assert.True(editor.Apply());
        Assert.Single(callbacks);
        Assert.Null(editor.ErrorText);
        Assert.True(errorNotifications.Count >= 2);
    }

    [Fact]
    public void DraftTextUsesInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var initial = LogicSettings.Default with { GSuitEffectiveness = 0.12345678901234566 };
            var applied = new List<LogicSettings>();
            var editor = new LogicSettingsEditorViewModel(initial, applied.Add);
            var entry = editor.Entries.Single(e => e.Name == nameof(LogicSettings.GSuitEffectiveness));

            Assert.Equal(
                initial.GSuitEffectiveness.ToString("R", CultureInfo.InvariantCulture),
                entry.ValueText);
            Assert.True(editor.Apply());
            var candidate = Assert.Single(applied);
            Assert.Equal(0.12345678901234566, candidate.GSuitEffectiveness);

            entry.ValueText = "-123.5";
            Assert.True(editor.Apply());
            Assert.Equal(-123.5, applied[1].GSuitEffectiveness);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GlobalLoggingFlagsAreIndependentOfProfiles(bool debugMode, bool suppressInfoLogs)
    {
        var originalDebugMode = LogicSettings.DebugMode;
        var originalSuppressInfoLogs = LogicSettings.SuppresInfoLogs;
        try
        {
            LogicSettings.DebugMode = debugMode;
            LogicSettings.SuppresInfoLogs = suppressInfoLogs;

            var instance = new GEffectsLogicInstance();
            var applied = new List<LogicSettings>();
            var editor = new LogicSettingsEditorViewModel(instance.Settings, applied.Add);
            Assert.True(editor.Apply());
            instance.ApplySettings(applied[0]);
            instance.Reset();

            Assert.Equal(debugMode, LogicSettings.DebugMode);
            Assert.Equal(suppressInfoLogs, LogicSettings.SuppresInfoLogs);
            Assert.Equal(LogicSettings.Default with { }, applied[0]);
        }
        finally
        {
            LogicSettings.DebugMode = originalDebugMode;
            LogicSettings.SuppresInfoLogs = originalSuppressInfoLogs;
        }
    }
}
