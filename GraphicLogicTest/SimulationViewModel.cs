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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
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

    public SimulationViewModel()
    {
        BuildLogicSettingsEntries();

        AddInstanceInternal("[1 5 5],[25]");
        AddInstanceInternal("[1 9 9],[21]");

        _lastTime = DateTime.Now;
        _timer.Tick += UpdateGraph;
        _timer.Start();
    }

    public ObservableCollection<SimulationInstanceViewModel> Instances { get; } = [];
    public ObservableCollection<LogicSettingEntry> LogicSettingsEntries { get; } = [];

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void BuildLogicSettingsEntries()
    {
        var props = typeof(LogicSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanRead && p.CanWrite);

        foreach (var prop in props) LogicSettingsEntries.Add(new LogicSettingEntry(prop));
    }

    private void AddInstanceInternal(string defaultSequence)
    {
        var vm = new SimulationInstanceViewModel($"Instance {_instanceCounter++}", defaultSequence, RecordedTime);
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
        if (sender is not Button b || b.DataContext is not SimulationInstanceViewModel vm) return;
        if (b.Tag is null) return;
        if (!int.TryParse(b.Tag.ToString(), out var index)) return;

        vm.ToggleMetricSeries(index);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}