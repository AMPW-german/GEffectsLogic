using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GraphicLogicTest
{
    public sealed class SimulationViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime _lastTime;
        private int _instanceCounter = 1;

        public ObservableCollection<SimulationInstanceViewModel> Instances { get; } = [];
        public ObservableCollection<LogicSettingEntry> LogicSettingsEntries { get; } = [];

        private double _timeMultiplier = 5.0;
        public double TimeMultiplier { get => _timeMultiplier; set { _timeMultiplier = value; OnPropertyChanged(); } }

        private int _updateMultiplier = 5;
        public int UpdateMultiplier { get => _updateMultiplier; set { _updateMultiplier = value; OnPropertyChanged(); } }

        private double _recordedTime = 30.0;
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

        public SimulationViewModel()
        {
            BuildLogicSettingsEntries();

            AddInstanceInternal("[1 5 5],[25]");
            AddInstanceInternal("[1 9 9],[21]");

            _lastTime = DateTime.Now;
            _timer.Tick += UpdateGraph;
            _timer.Start();
        }

        private void BuildLogicSettingsEntries()
        {
            var props = typeof(GEffectsLogic.LogicSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var prop in props)
            {
                LogicSettingsEntries.Add(new LogicSettingEntry(prop));
            }
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

            for (int i = 0; i < iterations; i++)
            {
                foreach (var vm in Instances)
                {
                    if (vm.IsPaused) continue;
                    vm.Step(dtIteration, RecordedTime);
                }
            }
        }

        internal void GlobalStart_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var vm in Instances)
            {
                if (!vm.HasStarted)
                    vm.StartInstance();
                else
                    vm.IsPaused = false;
            }
        }

        internal void GlobalStop_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var item in Instances)
            {
                item.IsPaused = true;
            }
        }

        internal void ResetAll_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var vm in Instances)
            {
                vm.ResetModel();
            }
        }

        internal void AddInstance_Click(object? sender, RoutedEventArgs e)
        {
            AddInstanceInternal("[1 5 5],[25]");
        }

        internal void InstanceStart_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm)
            {
                vm.StartInstance();
            }
        }

        internal void InstancePause_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm)
            {
                vm.IsPaused = !vm.IsPaused;
            }
        }

        internal void InstanceDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm)
            {
                Instances.Remove(vm);
            }
        }

        internal void ToggleMetricSeries_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.DataContext is not SimulationInstanceViewModel vm) return;
            if (b.Tag is null) return;
            if (!int.TryParse(b.Tag.ToString(), out var index)) return;

            vm.ToggleMetricSeries(index);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
