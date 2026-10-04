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
using System.Runtime.CompilerServices;
using GEffectsLogic;

namespace GraphicLogicTest;

public sealed class LogicSettingsEditorViewModel : INotifyPropertyChanged
{
    private static readonly PropertyInfo[] NumericProperties = typeof(LogicSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(double) && property.CanRead && property.CanWrite)
        .OrderBy(property => property.MetadataToken)
        .ToArray();

    private readonly Action<LogicSettings> _applySettings;
    private LogicSettings _appliedSettings;
    private string? _errorText;

    public LogicSettingsEditorViewModel(LogicSettings initialSettings, Action<LogicSettings> applySettings)
    {
        ArgumentNullException.ThrowIfNull(initialSettings);
        ArgumentNullException.ThrowIfNull(applySettings);
        _appliedSettings = initialSettings;
        _applySettings = applySettings;
        Entries = NumericProperties
            .Select(property => new LogicSettingEntry(property, (double)property.GetValue(initialSettings)!))
            .ToArray();
    }

    public IReadOnlyList<LogicSettingEntry> Entries { get; }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText == value) return;
            _errorText = value;
            OnPropertyChanged();
        }
    }

    public bool Apply()
    {
        var values = new double[Entries.Count];
        for (var i = 0; i < Entries.Count; i++)
        {
            if (!double.TryParse(Entries[i].ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                !double.IsFinite(values[i]))
            {
                ErrorText = $"{Entries[i].Name} must be a finite number using invariant-culture notation.";
                return false;
            }
        }

        var candidate = _appliedSettings with { };
        for (var i = 0; i < NumericProperties.Length; i++)
            NumericProperties[i].SetValue(candidate, values[i]);
        _applySettings(candidate);
        _appliedSettings = candidate;
        ErrorText = null;
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
