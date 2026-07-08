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
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GEffectsLogic;
using GEffectsLogic.Logging;
using GraphicLogicTest.Logging;
using GraphicLogicTest.Views.GLoCPlot;
using GraphicLogicTest.Views.SimulationView;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace GraphicLogicTest;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly SimulationViewModel _simulationViewModel;
    private WindowViewEntry? _activeWindow;

    public MainWindow()
    {
        _ = new TestLogging();
        Logger.Instance = new LogicLogging();
        LogicSettings.DebugMode = true;

        _simulationViewModel = new SimulationViewModel();

        InitializeComponent();
        DataContext = this;
        RegisterWindowViews();
    }

    public ObservableCollection<WindowViewEntry> WindowViews { get; } = [];

    public Control? ActiveWindowContent => _activeWindow?.Content;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RegisterWindowViews()
    {
        WindowViews.Clear();

        // Register new windows only here.
        WindowViews.Add(new WindowViewEntry(0, "Simulation",
            new SimulationView { DataContext = _simulationViewModel }));
        WindowViews.Add(new WindowViewEntry(1, "GLoC Plot", new GLoCPlot()));

        SetActiveWindow(0);
    }

    private void SetActiveWindow(int index)
    {
        if (index < 0 || index >= WindowViews.Count) return;

        var next = WindowViews[index];
        if (ReferenceEquals(_activeWindow, next)) return;

        _activeWindow = next;

        foreach (var item in WindowViews) item.IsActive = ReferenceEquals(item, _activeWindow);

        OnPropertyChanged(nameof(ActiveWindowContent));
    }

    private void SelectWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        if (b.Tag is null) return;
        if (!int.TryParse(b.Tag.ToString(), out var index)) return;

        SetActiveWindow(index);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class WindowViewEntry : INotifyPropertyChanged
{
    private bool _isActive;

    public WindowViewEntry(int index, string title, Control content)
    {
        Index = index;
        Title = title;
        Content = content;
    }

    public int Index { get; }
    public string Title { get; }
    public Control Content { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ButtonBackground));
        }
    }

    public string ButtonBackground => IsActive ? "#FF2D6FDB" : "#FF606060";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class LogicSettingEntry : INotifyPropertyChanged
{
    private readonly PropertyInfo _property;

    public LogicSettingEntry(PropertyInfo property)
    {
        _property = property;
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SimulationInstanceViewModel : INotifyPropertyChanged
{
    private static readonly string[] ActiveLegendColors =
    [
        "#FFD93A3A", // Consciousness
        "#FF9C5CFF", // BrainO2
        "#FF3D79FF", // GreyScale
        "#FF2CC8D6", // TunnelVision
        "#FFE6942E", // Perfusion
        "#FFF2C14E", // HeartRateMultiplier
        "#FFFF69B4", // FilmGrain
        "#FF90EE90"  // Blur
    ];

    private static readonly string[] DimLegendColors =
    [
        "#66402020",
        "#66332655",
        "#66202E55",
        "#66203E44",
        "#66553C1F",
        "#66543F1A",
        "#66552040",
        "#66304830"
    ];

    private readonly ObservableCollection<ObservablePoint> _brainO2Points = [];

    private readonly ObservableCollection<ObservablePoint> _consciousnessPoints = [];
    private readonly ObservableCollection<ObservablePoint> _greyScalePoints = [];

    private readonly ObservableCollection<ObservablePoint> _gxPoints = [];
    private readonly ObservableCollection<ObservablePoint> _gyPoints = [];
    private readonly ObservableCollection<ObservablePoint> _gzPoints = [];

    private readonly ObservableCollection<ObservablePoint> _heartRateMultiplierPoints = [];
    private readonly GEffectsLogicInstance _logic = new();
    private readonly ObservableCollection<ObservablePoint> _perfusionPoints = [];

    private readonly List<SequenceSegment> _segments = [];
    private readonly ObservableCollection<ObservablePoint> _stabilityPoints = [];
    private readonly ObservableCollection<ObservablePoint> _tunnelVisionPoints = [];
    private readonly ObservableCollection<ObservablePoint> _filmGrainPoints = [];
    private readonly ObservableCollection<ObservablePoint> _blurPoints = [];
    private double _currentGz = 1.0;

    private bool _isPaused = true;
    private double _segmentElapsed;
    private int _segmentIndex;
    private bool _sequenceFinished;

    private string _sequenceText;

    public SimulationInstanceViewModel(string title, string defaultSequence, double recordedTime)
    {
        Title = title;
        _sequenceText = defaultSequence;

        GSeries =
        [
            CreateSeries("Gx", SKColors.Red, _gxPoints),
            CreateSeries("Gy", SKColors.Green, _gyPoints),
            CreateSeries("Gz", SKColors.DeepSkyBlue, _gzPoints),
            CreateSeries("Stability", SKColors.Gold, _stabilityPoints)
        ];
        GXAxes = [CreateAxis("Time (s)", -recordedTime, 0, 9, 9)];
        GYAxes = [CreateAxis("G", -10, 12, 9, 9)];

        MetricSeries =
        [
            CreateSeries("Consciousness", SKColors.Red, _consciousnessPoints),
            CreateSeries("BrainO2", SKColors.Violet, _brainO2Points),
            CreateSeries("GreyScale", SKColors.Blue, _greyScalePoints),
            CreateSeries("TunnelVision", SKColors.Cyan, _tunnelVisionPoints),
            CreateSeries("Perfusion", SKColors.Orange, _perfusionPoints),
            CreateSeries("HeartRateMultiplier", SKColors.Gold, _heartRateMultiplierPoints),
            CreateSeries("FilmGrain", SKColors.HotPink, _filmGrainPoints),
            CreateSeries("Blur", SKColors.LightGreen, _blurPoints)
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

    public string Title { get; }

    public string SequenceText
    {
        get => _sequenceText;
        set
        {
            _sequenceText = value;
            OnPropertyChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PauseButtonText));
        }
    }

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";
    public bool HasStarted { get; private set; }

    public ISeries[] GSeries { get; }
    public Axis[] GXAxes { get; }
    public Axis[] GYAxes { get; }

    public ISeries[] MetricSeries { get; }
    public Axis[] MXAxes { get; }
    public Axis[] MYAxes { get; }

    public string Legend0Background => GetLegendBackground(0);
    public string Legend1Background => GetLegendBackground(1);
    public string Legend2Background => GetLegendBackground(2);
    public string Legend3Background => GetLegendBackground(3);
    public string Legend4Background => GetLegendBackground(4);
    public string Legend5Background => GetLegendBackground(5);
    public string Legend6Background => GetLegendBackground(6);
    public string Legend7Background => GetLegendBackground(7);

    public string Legend0Foreground => GetLegendForeground(0);
    public string Legend1Foreground => GetLegendForeground(1);
    public string Legend2Foreground => GetLegendForeground(2);
    public string Legend3Foreground => GetLegendForeground(3);
    public string Legend4Foreground => GetLegendForeground(4);
    public string Legend5Foreground => GetLegendForeground(5);
    public string Legend6Foreground => GetLegendForeground(6);
    public string Legend7Foreground => GetLegendForeground(7);

    public event PropertyChangedEventHandler? PropertyChanged;

    private string GetLegendBackground(int i)
    {
        return MetricSeries[i].IsVisible ? ActiveLegendColors[i] : DimLegendColors[i];
    }

    private string GetLegendForeground(int i)
    {
        return MetricSeries[i].IsVisible ? "#FFFFFFFF" : "#FF9A9A9A";
    }

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
        _logic.PhysModel.Reset();

        _segmentIndex = 0;
        _segmentElapsed = 0;
        _sequenceFinished = _segments.Count == 0;
        _currentGz = _segments.Count > 0 ? _segments[0].StartGz : 1.0;

        _gxPoints.Clear();
        _gyPoints.Clear();
        _gzPoints.Clear();
        _stabilityPoints.Clear();
        _consciousnessPoints.Clear();
        _brainO2Points.Clear();
        _greyScalePoints.Clear();
        _tunnelVisionPoints.Clear();
        _perfusionPoints.Clear();
        _heartRateMultiplierPoints.Clear();
        _filmGrainPoints.Clear();
        _blurPoints.Clear();
    }

    public void Step(double dt, double recordedTime)
    {
        AdvanceSequence(dt);

        _logic.Update(dt, 0, 0, _currentGz);

        //UpdateSeriesPoints(_gxPoints, dt, gx, recordedTime);
        //UpdateSeriesPoints(_gyPoints, dt, gy, recordedTime);
        UpdateSeriesPoints(_gzPoints, dt, _currentGz, recordedTime);
        UpdateSeriesPoints(_stabilityPoints, dt, _logic.IsStable ? 1.0 : 0.0, recordedTime);

        UpdateSeriesPoints(_consciousnessPoints, dt, _logic.ConsciousnessLevel, recordedTime);
        UpdateSeriesPoints(_brainO2Points, dt, _logic.PhysModel.BrainO2, recordedTime);
        UpdateSeriesPoints(_greyScalePoints, dt, _logic.GreyScaleLevel, recordedTime);
        UpdateSeriesPoints(_tunnelVisionPoints, dt, _logic.TunnelVisionLevel, recordedTime);
        UpdateSeriesPoints(_perfusionPoints, dt, _logic.PhysModel.PerfusionLevel, recordedTime);
        UpdateSeriesPoints(_heartRateMultiplierPoints, dt, _logic.PhysModel.HeartRateMultiplier, recordedTime);
        UpdateSeriesPoints(_filmGrainPoints, dt, _logic.FilmGrainLevel, recordedTime);
        UpdateSeriesPoints(_blurPoints, dt, _logic.BlurLevel, recordedTime);
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
        OnPropertyChanged(nameof(Legend6Background));
        OnPropertyChanged(nameof(Legend7Background));

        OnPropertyChanged(nameof(Legend0Foreground));
        OnPropertyChanged(nameof(Legend1Foreground));
        OnPropertyChanged(nameof(Legend2Foreground));
        OnPropertyChanged(nameof(Legend3Foreground));
        OnPropertyChanged(nameof(Legend4Foreground));
        OnPropertyChanged(nameof(Legend5Foreground));
        OnPropertyChanged(nameof(Legend6Foreground));
        OnPropertyChanged(nameof(Legend7Foreground));
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
        _currentGz = current.StartGz + (current.EndGz - current.StartGz) * progress;

        if (progress >= 1.0) MoveToNextSegment();
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

        var previousEnd = 1.0;

        foreach (Match match in matches)
        {
            var raw = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            if (tokens.Count > 0 && tokens[0].Equals("Gz", StringComparison.OrdinalIgnoreCase)) tokens.RemoveAt(0);

            if (tokens.Count == 0) continue;

            if (tokens.Count == 1 && tokens[0] == "-")
            {
                _segments.Add(new SequenceSegment(previousEnd, previousEnd, 0, true));
                break;
            }

            if (tokens.Count == 1)
            {
                var duration = ParseDouble(tokens[0]);
                _segments.Add(new SequenceSegment(previousEnd, previousEnd, duration, false));
                continue;
            }

            if (tokens.Count == 2)
            {
                var end = ParseDouble(tokens[0]);
                var duration = ParseDouble(tokens[1]);
                _segments.Add(new SequenceSegment(previousEnd, end, duration, false));
                previousEnd = end;
                continue;
            }

            var start = ParseDouble(tokens[0]);
            var endValue = ParseDouble(tokens[1]);
            var durationValue = ParseDouble(tokens[2]);

            _segments.Add(new SequenceSegment(start, endValue, durationValue, false));
            previousEnd = endValue;
        }
    }

    private static double ParseDouble(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
    }

    private static void UpdateSeriesPoints(ObservableCollection<ObservablePoint> points, double dt, double currentValue,
        double recordedTime)
    {
        for (var i = 0; i < points.Count; i++) points[i].X -= dt;
        while (points.Count > 0 && points[0].X < -recordedTime) points.RemoveAt(0);
        points.Add(new ObservablePoint(0, currentValue));
    }

    private static LineSeries<ObservablePoint> CreateSeries(string name, SKColor color,
        ObservableCollection<ObservablePoint> values)
    {
        return new LineSeries<ObservablePoint>
        {
            Name = name,
            Values = values,
            GeometrySize = 0,
            Stroke = new SolidColorPaint(color, 2),
            Fill = null,
            XToolTipLabelFormatter = p => $"t: {-p.Model!.X:F2}s",
            YToolTipLabelFormatter = p => $"{p.Model!.Y:F3}"
        };
    }

    private static Axis CreateAxis(string name, double min, double max, double axisTextSize = 11,
        double axisNameTextSize = 11)
    {
        return new Axis
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
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private readonly record struct SequenceSegment(double StartGz, double EndGz, double Duration, bool IsInfinite);
}