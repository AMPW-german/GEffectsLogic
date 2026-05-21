using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GraphicLogicTest.Logging;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace GraphicLogicTest
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private DateTime _lastTime;
        private bool _globalPaused;

        private int _instanceCounter = 1;

        public ObservableCollection<SimulationInstanceViewModel> Instances { get; } = [];
        public ObservableCollection<LogicSettingEntry> LogicSettingsEntries { get; } = [];

        private double _gx;
        public double Gx { get => _gx; set { _gx = value; OnPropertyChanged(); } }

        private double _gy;
        public double Gy { get => _gy; set { _gy = value; OnPropertyChanged(); } }

        private double _gz = 1.0;
        public double Gz { get => _gz; set { _gz = value; OnPropertyChanged(); } }

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

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _ = new TestLogging();
            GEffectsLogic.Logging.Logger.Instance = new LogicLogging();
            GEffectsLogic.LogicSettings.DebugMode = true;

            BuildLogicSettingsEntries();

            AddInstanceInternal("[1 5 5]");
            AddInstanceInternal("[1 9 9]");

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

            if (_globalPaused) return;

            var scaledDt = dt * TimeMultiplier;
            var iterations = Math.Max(1, UpdateMultiplier);
            var dtIteration = scaledDt / iterations;

            for (int i = 0; i < iterations; i++)
            {
                foreach (var vm in Instances)
                {
                    if (vm.IsPaused) continue;
                    vm.Step(dtIteration, Gx, Gy, RecordedTime);
                }
            }
        }

        private void GlobalStart_Click(object? sender, RoutedEventArgs e)
        {
            _globalPaused = false;

            foreach (var vm in Instances)
            {
                if (!vm.HasStarted)
                    vm.StartInstance();
                else
                    vm.IsPaused = false;
            }
        }

        private void GlobalStop_Click(object? sender, RoutedEventArgs e)
        {
            _globalPaused = true;
        }

        private void ResetAll_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var vm in Instances)
            {
                vm.ResetModel();
            }
        }

        private void AddInstance_Click(object? sender, RoutedEventArgs e)
        {
            AddInstanceInternal("[1 5 5]");
        }

        private void InstanceStart_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm)
            {
                vm.StartInstance();
            }
        }

        private void InstancePause_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm)
            {
                vm.IsPaused = !vm.IsPaused;
            }
        }

        private void InstanceDelete_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.DataContext is SimulationInstanceViewModel vm)
            {
                Instances.Remove(vm);
            }
        }

        private void ToggleMetricSeries_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.DataContext is not SimulationInstanceViewModel vm) return;
            if (b.Tag is null) return;
            if (!int.TryParse(b.Tag.ToString(), out var index)) return;

            vm.ToggleMetricSeries(index);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class LogicSettingEntry : INotifyPropertyChanged
    {
        private readonly PropertyInfo _property;

        public string Name => _property.Name;

        public string ValueText
        {
            get => Convert.ToString(_property.GetValue(null), CultureInfo.InvariantCulture) ?? string.Empty;
            set
            {
                if (!TryConvert(value, _property.PropertyType, out var parsed)) return;
                _property.SetValue(null, parsed);
                OnPropertyChanged();
            }
        }

        public LogicSettingEntry(PropertyInfo property)
        {
            _property = property;
        }

        private static bool TryConvert(string input, Type targetType, out object? value)
        {
            value = null;

            if (targetType == typeof(double))
            {
                if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    value = d;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    value = f;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                {
                    value = i;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(input, out var b))
                {
                    value = b;
                    return true;
                }
                return false;
            }

            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class SimulationInstanceViewModel : INotifyPropertyChanged
    {
        private readonly GEffectsLogic.GEffectsLogicInstance _logic = new();

        private readonly ObservableCollection<ObservablePoint> _gxPoints = [];
        private readonly ObservableCollection<ObservablePoint> _gyPoints = [];
        private readonly ObservableCollection<ObservablePoint> _gzPoints = [];

        private readonly ObservableCollection<ObservablePoint> _consciousnessPoints = [];
        private readonly ObservableCollection<ObservablePoint> _bloodHeadPoints = [];
        private readonly ObservableCollection<ObservablePoint> _brainO2Points = [];
        private readonly ObservableCollection<ObservablePoint> _greyScalePoints = [];
        private readonly ObservableCollection<ObservablePoint> _tunnelVisionPoints = [];
        private readonly ObservableCollection<ObservablePoint> _perfusionPoints = [];

        private readonly List<SequenceSegment> _segments = [];
        private int _segmentIndex;
        private double _segmentElapsed;
        private bool _sequenceFinished;
        private double _currentGz = 1.0;

        public string Title { get; }

        private string _sequenceText;
        public string SequenceText { get => _sequenceText; set { _sequenceText = value; OnPropertyChanged(); } }

        private bool _isPaused = true;
        public bool IsPaused
        {
            get => _isPaused;
            set { _isPaused = value; OnPropertyChanged(); OnPropertyChanged(nameof(PauseButtonText)); }
        }

        public string PauseButtonText => IsPaused ? "Resume" : "Pause";
        public bool HasStarted { get; private set; }

        public ISeries[] GSeries { get; }
        public Axis[] GXAxes { get; }
        public Axis[] GYAxes { get; }

        public ISeries[] MetricSeries { get; }
        public Axis[] MXAxes { get; }
        public Axis[] MYAxes { get; }

        private static readonly string[] ActiveLegendColors =
        [
            "#FFD93A3A", // Consciousness
            "#FF2FAF5A", // BloodHead
            "#FF9C5CFF", // BrainO2
            "#FF3D79FF", // GreyScale
            "#FF2CC8D6", // TunnelVision
            "#FFE6942E"  // Perfusion
        ];

        private static readonly string[] DimLegendColors =
        [
            "#66402020",
            "#66203A2A",
            "#66332655",
            "#66202E55",
            "#66203E44",
            "#66553C1F"
        ];

        public SimulationInstanceViewModel(string title, string defaultSequence, double recordedTime)
        {
            Title = title;
            _sequenceText = defaultSequence;

            GSeries =
            [
                CreateSeries("Gx", SKColors.Red, _gxPoints),
                CreateSeries("Gy", SKColors.Green, _gyPoints),
                CreateSeries("Gz", SKColors.DeepSkyBlue, _gzPoints)
            ];
            GXAxes = [CreateAxis("Time (s)", -recordedTime, 0, axisTextSize: 9, axisNameTextSize: 9)];
            GYAxes = [CreateAxis("G", -10, 12, axisTextSize: 9, axisNameTextSize: 9)];

            MetricSeries =
            [
                CreateSeries("Consciousness", SKColors.Red, _consciousnessPoints),
                CreateSeries("BloodHead", SKColors.Green, _bloodHeadPoints),
                CreateSeries("BrainO2", SKColors.Violet, _brainO2Points),
                CreateSeries("GreyScale", SKColors.Blue, _greyScalePoints),
                CreateSeries("TunnelVision", SKColors.Cyan, _tunnelVisionPoints),
                CreateSeries("Perfusion", SKColors.Orange, _perfusionPoints)
            ];
            MXAxes = [CreateAxis("Time (s)", -recordedTime, 0)];
            MYAxes =
            [
                new Axis
                {
                    Name = "Value",
                    MinLimit = 0,
                    MaxLimit = 1.1,
                    Position = AxisPosition.End,
                    LabelsPaint = new SolidColorPaint(SKColors.White),
                    NamePaint = new SolidColorPaint(SKColors.White),
                    SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 40))
                }
            ];
        }

        private string GetLegendBackground(int i) => MetricSeries[i].IsVisible ? ActiveLegendColors[i] : DimLegendColors[i];
        private string GetLegendForeground(int i) => MetricSeries[i].IsVisible ? "#FFFFFFFF" : "#FF9A9A9A";

        public string Legend0Background => GetLegendBackground(0);
        public string Legend1Background => GetLegendBackground(1);
        public string Legend2Background => GetLegendBackground(2);
        public string Legend3Background => GetLegendBackground(3);
        public string Legend4Background => GetLegendBackground(4);
        public string Legend5Background => GetLegendBackground(5);

        public string Legend0Foreground => GetLegendForeground(0);
        public string Legend1Foreground => GetLegendForeground(1);
        public string Legend2Foreground => GetLegendForeground(2);
        public string Legend3Foreground => GetLegendForeground(3);
        public string Legend4Foreground => GetLegendForeground(4);
        public string Legend5Foreground => GetLegendForeground(5);

        public void UpdateRecordedTime(double recordedTime)
        {
            GXAxes[0].MinLimit = -recordedTime;
            MXAxes[0].MinLimit = -recordedTime;
            OnPropertyChanged(nameof(GXAxes));
            OnPropertyChanged(nameof(MXAxes));
        }

        public void StartInstance()
        {
            ParseSequence(SequenceText);
            ResetModel();
            HasStarted = true;
            IsPaused = false;
        }

        public void ResetModel()
        {
            _logic.Time = 0;
            _logic.LastGx = 0;
            _logic.LastGy = 0;
            _logic.LastGz = 0;
            _logic.BloodHead = 1.0;
            _logic.perfusionLevel = 1.0;
            _logic.ConsciousnessLevel = 1.0;
            _logic.ConfusionLevel = 0.0;
            _logic.TunnelVisionLevel = 0.0;
            _logic.GreyScaleLevel = 0.0;
            _logic.PrimaryColor = true;
            _logic.PhysModel.Reset();

            _segmentIndex = 0;
            _segmentElapsed = 0;
            _sequenceFinished = _segments.Count == 0;
            _currentGz = _segments.Count > 0 ? _segments[0].StartGz : 1.0;

            _gxPoints.Clear();
            _gyPoints.Clear();
            _gzPoints.Clear();
            _consciousnessPoints.Clear();
            _bloodHeadPoints.Clear();
            _brainO2Points.Clear();
            _greyScalePoints.Clear();
            _tunnelVisionPoints.Clear();
            _perfusionPoints.Clear();
        }

        public void Step(double dt, double gx, double gy, double recordedTime)
        {
            AdvanceSequence(dt);

            _logic.Update(dt, gx, gy, _currentGz);

            UpdateSeriesPoints(_gxPoints, dt, gx, recordedTime);
            UpdateSeriesPoints(_gyPoints, dt, gy, recordedTime);
            UpdateSeriesPoints(_gzPoints, dt, _currentGz, recordedTime);

            UpdateSeriesPoints(_consciousnessPoints, dt, _logic.ConsciousnessLevel, recordedTime);
            UpdateSeriesPoints(_bloodHeadPoints, dt, _logic.BloodHead, recordedTime);
            UpdateSeriesPoints(_brainO2Points, dt, _logic.PhysModel.BrainO2, recordedTime);
            UpdateSeriesPoints(_greyScalePoints, dt, _logic.GreyScaleLevel, recordedTime);
            UpdateSeriesPoints(_tunnelVisionPoints, dt, _logic.TunnelVisionLevel, recordedTime);
            UpdateSeriesPoints(_perfusionPoints, dt, _logic.PerfusionLevel, recordedTime);
        }

        public void ToggleMetricSeries(int index)
        {
            if (index < 0 || index >= MetricSeries.Length) return;
            MetricSeries[index].IsVisible = !MetricSeries[index].IsVisible;
            OnPropertyChanged(nameof(MetricSeries));

            OnPropertyChanged(nameof(Legend0Background));
            OnPropertyChanged(nameof(Legend1Background));
            OnPropertyChanged(nameof(Legend2Background));
            OnPropertyChanged(nameof(Legend3Background));
            OnPropertyChanged(nameof(Legend4Background));
            OnPropertyChanged(nameof(Legend5Background));

            OnPropertyChanged(nameof(Legend0Foreground));
            OnPropertyChanged(nameof(Legend1Foreground));
            OnPropertyChanged(nameof(Legend2Foreground));
            OnPropertyChanged(nameof(Legend3Foreground));
            OnPropertyChanged(nameof(Legend4Foreground));
            OnPropertyChanged(nameof(Legend5Foreground));
        }

        private void AdvanceSequence(double dt)
        {
            if (_segments.Count == 0 || _sequenceFinished) return;

            var current = _segments[_segmentIndex];
            if (current.IsInfinite)
            {
                _currentGz = current.EndGz;
                return;
            }

            if (current.Duration <= 0)
            {
                _currentGz = current.EndGz;
                MoveToNextSegment();
                return;
            }

            _segmentElapsed += dt;
            var progress = Math.Clamp(_segmentElapsed / current.Duration, 0.0, 1.0);
            _currentGz = current.StartGz + ((current.EndGz - current.StartGz) * progress);

            if (progress >= 1.0)
            {
                MoveToNextSegment();
            }
        }

        private void MoveToNextSegment()
        {
            _segmentIndex++;
            _segmentElapsed = 0;

            if (_segmentIndex >= _segments.Count)
            {
                _sequenceFinished = true;
                IsPaused = true; // default end behavior == ",[-]"
                return;
            }

            var next = _segments[_segmentIndex];
            _currentGz = next.StartGz;
        }

        private void ParseSequence(string input)
        {
            _segments.Clear();

            var matches = Regex.Matches(input ?? string.Empty, "\\[(.*?)\\]");
            if (matches.Count == 0) return;

            double previousEnd = 1.0;

            foreach (Match match in matches)
            {
                var raw = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

                if (tokens.Count > 0 && tokens[0].Equals("Gz", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.RemoveAt(0);
                }

                if (tokens.Count == 0) continue;

                if (tokens.Count == 1 && tokens[0] == "-")
                {
                    _segments.Add(new SequenceSegment(previousEnd, previousEnd, 0, IsInfinite: true));
                    break;
                }

                if (tokens.Count == 1)
                {
                    var duration = ParseDouble(tokens[0]);
                    _segments.Add(new SequenceSegment(previousEnd, previousEnd, duration, IsInfinite: false));
                    continue;
                }

                if (tokens.Count == 2)
                {
                    var end = ParseDouble(tokens[0]);
                    var duration = ParseDouble(tokens[1]);
                    _segments.Add(new SequenceSegment(previousEnd, end, duration, IsInfinite: false));
                    previousEnd = end;
                    continue;
                }

                var start = ParseDouble(tokens[0]);
                var endValue = ParseDouble(tokens[1]);
                var durationValue = ParseDouble(tokens[2]);

                _segments.Add(new SequenceSegment(start, endValue, durationValue, IsInfinite: false));
                previousEnd = endValue;
            }
        }

        private static double ParseDouble(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;

        private static void UpdateSeriesPoints(ObservableCollection<ObservablePoint> points, double dt, double currentValue, double recordedTime)
        {
            for (int i = 0; i < points.Count; i++) points[i].X -= dt;
            while (points.Count > 0 && points[0].X < -recordedTime) points.RemoveAt(0);
            points.Add(new ObservablePoint(0, currentValue));
        }

        private static LineSeries<ObservablePoint> CreateSeries(string name, SKColor color, ObservableCollection<ObservablePoint> values)
            => new()
            {
                Name = name,
                Values = values,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                XToolTipLabelFormatter = p => $"t: {-p.Model!.X:F2}s",
                YToolTipLabelFormatter = p => $"{p.Model!.Y:F3}"
            };

        private static Axis CreateAxis(string name, double min, double max, double axisTextSize = 11, double axisNameTextSize = 11)
            => new()
            {
                Name = name,
                MinLimit = min,
                MaxLimit = max,
                TextSize = axisTextSize,
                NameTextSize = axisNameTextSize,
                LabelsPaint = new SolidColorPaint(SKColors.White),
                NamePaint = new SolidColorPaint(SKColors.White),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 40))
            };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private readonly record struct SequenceSegment(double StartGz, double EndGz, double Duration, bool IsInfinite);
    }
}
