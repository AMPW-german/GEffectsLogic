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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GEffectsLogic;

namespace GraphicLogicTest;

public sealed class SimulationViewModel : INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int _instanceCounter = 1;
    private DateTime _lastTime;

    private double _recordedTime = 30.0;

    private double _timeMultiplier = 5.0;

    private int _updateMultiplier = 5;

    private LogicSettings _defaultSettings;

    public SimulationViewModel(LogicSettings? initialSettings = null)
    {
        _defaultSettings = initialSettings ?? LogicSettings.Default;
        DefaultsEditor = new LogicSettingsEditorViewModel(_defaultSettings, settings =>
        {
            _defaultSettings = settings;
            OnPropertyChanged(nameof(DefaultSettings));
        });

        AddInstanceInternal("[1 5 5],[25]");
        AddInstanceInternal("[Gz 1 9 9],[21];[Gy 0 3 8],[0 20]");

        _lastTime = DateTime.Now;
        _timer.Tick += UpdateGraph;
        _timer.Start();
    }

    public ObservableCollection<SimulationInstanceViewModel> Instances { get; } = [];

    public LogicSettings DefaultSettings => _defaultSettings;

    public LogicSettingsEditorViewModel DefaultsEditor { get; }

    public double TimeMultiplier
    {
        get => _timeMultiplier;
        set
        {
            _timeMultiplier = value;
            OnPropertyChanged();
        }
    }

    public int UpdateMultiplier
    {
        get => _updateMultiplier;
        set
        {
            _updateMultiplier = value;
            OnPropertyChanged();
        }
    }

    public double RecordedTime
    {
        get => _recordedTime;
        set
        {
            _recordedTime = value;
            OnPropertyChanged();
            foreach (var vm in Instances) vm.UpdateRecordedTime(value);
        }
    }

    public bool GlobalDebugMode
    {
        get => LogicSettings.DebugMode;
        set
        {
            LogicSettings.DebugMode = value;
            OnPropertyChanged();
        }
    }

    public bool GlobalSuppressInfoLogs
    {
        get => LogicSettings.SuppresInfoLogs;
        set
        {
            LogicSettings.SuppresInfoLogs = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void AddInstanceInternal(string defaultSequence)
    {
        var vm = new SimulationInstanceViewModel($"Instance {_instanceCounter++}", defaultSequence, RecordedTime,
            DefaultSettings);
        Instances.Add(vm);
    }

    private void UpdateGraph(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var dt = (now - _lastTime).TotalSeconds;
        _lastTime = now;

        var scaledDt = dt * TimeMultiplier;
        var iterations = Math.Max(1, UpdateMultiplier);
        var dtIteration = scaledDt / iterations;

        for (var i = 0; i < iterations; i++)
            foreach (var vm in Instances)
            {
                if (vm.IsPaused) continue;
                vm.Step(dtIteration, RecordedTime);
            }
    }

    internal void GlobalStart_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var vm in Instances)
            if (!vm.HasStarted)
                vm.StartInstance();
            else
                vm.IsPaused = false;
    }

    internal void GlobalStop_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var item in Instances) item.IsPaused = true;
    }

    internal void ResetAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var vm in Instances) vm.ResetModel();
    }

    internal void AddInstance_Click(object? sender, RoutedEventArgs e)
    {
        AddInstanceInternal("[1 5 5],[25]");
    }

    internal void ApplyDefaults_Click(object? sender, RoutedEventArgs e)
    {
        DefaultsEditor.Apply();
    }

    internal void ApplyInstanceSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm) vm.SettingsEditor.Apply();
    }

    internal void InstanceStart_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm) vm.StartInstance();
    }

    internal void InstancePause_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm) vm.IsPaused = !vm.IsPaused;
    }

    internal void InstanceDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm) Instances.Remove(vm);
    }

    internal void ToggleMetricSeries_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LegendItemViewModel item }) item.Toggle();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
