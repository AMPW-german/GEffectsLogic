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
        WindowViews.Add(new WindowViewEntry(1, "GLoC Plot",
            new GLoCPlot(() => _simulationViewModel.DefaultSettings)));

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

public readonly record struct LegendItemDefinition(
    string Name,
    string TargetSeriesName,
    string ActiveColor,
    string DisabledColor);

public sealed class LegendItemViewModel : INotifyPropertyChanged
{
    private readonly Action _seriesChanged;
    private readonly ISeries _targetSeries;

    public LegendItemViewModel(LegendItemDefinition definition, ISeries targetSeries, Action seriesChanged)
    {
        Definition = definition;
        _targetSeries = targetSeries;
        _seriesChanged = seriesChanged;
    }

    public LegendItemDefinition Definition { get; }
    public string Name => Definition.Name;
    public string TargetSeriesName => Definition.TargetSeriesName;
    public string Background => _targetSeries.IsVisible ? Definition.ActiveColor : Definition.DisabledColor;
    public string Foreground => _targetSeries.IsVisible ? "#FFFFFFFF" : "#FF9A9A9A";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Toggle()
    {
        _targetSeries.IsVisible = !_targetSeries.IsVisible;
        _seriesChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
    }
}

public sealed class LogicSettingEntry : INotifyPropertyChanged
{
    private readonly PropertyInfo _property;
    private string _valueText;

    public LogicSettingEntry(PropertyInfo property, double initialValue)
    {
        _property = property;
        _valueText = initialValue.ToString("R", CultureInfo.InvariantCulture);
    }

    public string Name => _property.Name;

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value) return;
            _valueText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SimulationInstanceViewModel : INotifyPropertyChanged
{
    private static readonly LegendItemDefinition[] LegendLayout =
    [
        new("Consciousness", "Consciousness", "#FFD93A3A", "#66402020"),
        new("BloodHead", "BloodHead", "#FF2FAF5A", "#66203A2A"),
        new("BrainO2", "BrainO2", "#FF9C5CFF", "#66332655"),
        new("VisualGrayscale", "VisualGrayscale", "#FF3D79FF", "#66202E55"),
        new("VisualTunnelVision", "VisualTunnelVision", "#FF2CC8D6", "#66203E44"),
        new("VisualRedout", "VisualRedout", "#FFFF6347", "#66552820"),
        new("VisualLoC", "VisualLoC", "#FFFFFFFF", "#66505050"),
        new("Perfusion", "Perfusion", "#FFE6942E", "#66553C1F"),
        new("HeartRateMultiplier", "HeartRateMultiplier", "#FFF2C14E", "#66543F1A"),
        new("VisualFilmGrain", "VisualFilmGrain", "#FFFF69B4", "#66552040"),
        new("VisualBlur", "VisualBlur", "#FF90EE90", "#66304830")
    ];

    private readonly ObservableCollection<ObservablePoint> _bloodHeadPoints = [];
    private readonly ObservableCollection<ObservablePoint> _brainO2Points = [];

    private readonly ObservableCollection<ObservablePoint> _consciousnessPoints = [];
    private readonly ObservableCollection<ObservablePoint> _grayscalePoints = [];

    private readonly ObservableCollection<ObservablePoint> _gxPoints = [];
    private readonly ObservableCollection<ObservablePoint> _gyPoints = [];
    private readonly ObservableCollection<ObservablePoint> _gzPoints = [];

    private readonly ObservableCollection<ObservablePoint> _heartRateMultiplierPoints = [];
    private readonly GEffectsLogicInstance _logic;
    private readonly ObservableCollection<ObservablePoint> _perfusionPoints = [];

    private readonly AxisSequenceState _gxAxis = new(0.0);
    private readonly AxisSequenceState _gyAxis = new(0.0);
    private readonly AxisSequenceState _gzAxis = new(1.0);
    private readonly ObservableCollection<ObservablePoint> _stabilityPoints = [];
    private readonly ObservableCollection<ObservablePoint> _tunnelVisionPoints = [];
    private readonly ObservableCollection<ObservablePoint> _redoutPoints = [];
    private readonly ObservableCollection<ObservablePoint> _visualLoCPoints = [];
    private readonly ObservableCollection<ObservablePoint> _filmGrainPoints = [];
    private readonly ObservableCollection<ObservablePoint> _blurPoints = [];

    private bool _isPaused = true;
    private bool _sequenceFinished;

    private string _sequenceText;

    public SimulationInstanceViewModel(string title, string defaultSequence, double recordedTime,
        LogicSettings? settings = null)
    {
        Title = title;
        _logic = new NamedGEffectsLogicInstance(title, settings);
        SettingsEditor = new LogicSettingsEditorViewModel(_logic.Settings, _logic.ApplySettings);
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
            CreateSeries("BloodHead", SKColors.Green, _bloodHeadPoints),
            CreateSeries("BrainO2", SKColors.Violet, _brainO2Points),
            CreateSeries("VisualGrayscale", SKColors.Blue, _grayscalePoints),
            CreateSeries("VisualTunnelVision", SKColors.Cyan, _tunnelVisionPoints),
            CreateSeries("VisualRedout", SKColors.Tomato, _redoutPoints),
            CreateSeries("VisualLoC", SKColors.White, _visualLoCPoints),
            CreateSeries("Perfusion", SKColors.Orange, _perfusionPoints),
            CreateSeries("HeartRateMultiplier", SKColors.Gold, _heartRateMultiplierPoints),
            CreateSeries("VisualFilmGrain", SKColors.HotPink, _filmGrainPoints),
            CreateSeries("VisualBlur", SKColors.LightGreen, _blurPoints)
        ];
        LegendItems = LegendLayout
            .Select(item => new LegendItemViewModel(
                item,
                MetricSeries.Single(series => series.Name == item.TargetSeriesName),
                () => OnPropertyChanged(nameof(MetricSeries))))
            .ToArray();
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

    public LogicSettingsEditorViewModel SettingsEditor { get; }

    public ISeries[] GSeries { get; }
    public Axis[] GXAxes { get; }
    public Axis[] GYAxes { get; }

    public ISeries[] MetricSeries { get; }
    public LegendItemViewModel[] LegendItems { get; }
    public Axis[] MXAxes { get; }
    public Axis[] MYAxes { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

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
        _logic.Reset();

        ResetAxis(_gxAxis);
        ResetAxis(_gyAxis);
        ResetAxis(_gzAxis);
        _sequenceFinished = _gxAxis.Segments.Count == 0 && _gyAxis.Segments.Count == 0 &&
                            _gzAxis.Segments.Count == 0;

        _gxPoints.Clear();
        _gyPoints.Clear();
        _gzPoints.Clear();
        _stabilityPoints.Clear();
        _consciousnessPoints.Clear();
        _bloodHeadPoints.Clear();
        _brainO2Points.Clear();
        _grayscalePoints.Clear();
        _tunnelVisionPoints.Clear();
        _redoutPoints.Clear();
        _visualLoCPoints.Clear();
        _perfusionPoints.Clear();
        _heartRateMultiplierPoints.Clear();
        _filmGrainPoints.Clear();
        _blurPoints.Clear();
    }

    public void Step(double dt, double recordedTime)
    {
        AdvanceSequence(dt);

        _logic.Update(dt, _gxAxis.Current, _gyAxis.Current, _gzAxis.Current);

        UpdateSeriesPoints(_gxPoints, dt, _gxAxis.Current, recordedTime);
        UpdateSeriesPoints(_gyPoints, dt, _gyAxis.Current, recordedTime);
        UpdateSeriesPoints(_gzPoints, dt, _gzAxis.Current, recordedTime);
        UpdateSeriesPoints(_stabilityPoints, dt, _logic.IsStable ? 1.0 : 0.0, recordedTime);

        UpdateSeriesPoints(_consciousnessPoints, dt, _logic.ConsciousnessLevel, recordedTime);
        UpdateSeriesPoints(_bloodHeadPoints, dt, _logic.PhysModel.BloodHeadOverfill, recordedTime);
        UpdateSeriesPoints(_brainO2Points, dt, _logic.PhysModel.BrainO2, recordedTime);
        UpdateSeriesPoints(_grayscalePoints, dt, _logic.VisualGrayscaleLevel, recordedTime);
        UpdateSeriesPoints(_tunnelVisionPoints, dt, _logic.VisualTunnelVisionLevel, recordedTime);
        UpdateSeriesPoints(_redoutPoints, dt, _logic.VisualRedoutLevel, recordedTime);
        UpdateSeriesPoints(_visualLoCPoints, dt, _logic.VisualLoCLevel, recordedTime);
        UpdateSeriesPoints(_perfusionPoints, dt, _logic.PhysModel.PerfusionLevel, recordedTime);
        UpdateSeriesPoints(_heartRateMultiplierPoints, dt, _logic.PhysModel.HeartRateMultiplier, recordedTime);
        UpdateSeriesPoints(_filmGrainPoints, dt, _logic.VisualFilmGrainLevel, recordedTime);
        UpdateSeriesPoints(_blurPoints, dt, _logic.VisualBlurLevel, recordedTime);
    }

    private void AdvanceSequence(double dt)
    {
        if (_sequenceFinished) return;

        AdvanceAxis(_gxAxis, dt);
        AdvanceAxis(_gyAxis, dt);
        AdvanceAxis(_gzAxis, dt);

        if (_gxAxis.Finished && _gyAxis.Finished && _gzAxis.Finished)
        {
            _sequenceFinished = true;
            IsPaused = true; // default end behavior == ",[-]"
        }
    }

    private static void AdvanceAxis(AxisSequenceState axis, double dt)
    {
        if (axis.Finished) return;

        var current = axis.Segments[axis.SegmentIndex];
        if (current.IsInfinite)
        {
            axis.Current = current.EndG;
            return;
        }

        if (current.Duration <= 0)
        {
            axis.Current = current.EndG;
            MoveToNextSegment(axis);
            return;
        }

        axis.SegmentElapsed += dt;
        var progress = Math.Clamp(axis.SegmentElapsed / current.Duration, 0.0, 1.0);
        axis.Current = current.StartG + (current.EndG - current.StartG) * progress;

        if (progress >= 1.0) MoveToNextSegment(axis);
    }

    private static void MoveToNextSegment(AxisSequenceState axis)
    {
        axis.SegmentIndex++;
        axis.SegmentElapsed = 0;

        if (axis.Finished) return;

        axis.Current = axis.Segments[axis.SegmentIndex].StartG;
    }

    private static void ResetAxis(AxisSequenceState axis)
    {
        axis.SegmentIndex = 0;
        axis.SegmentElapsed = 0;
        axis.Current = axis.Segments.Count > 0 ? axis.Segments[0].StartG : axis.InitialValue;
    }

    // Sequence notation: [Axis startG endG duration] tracks separated by ';'.
    // The axis token may be omitted (defaults to Gz) and applies until the next axis token or ';'.
    private void ParseSequence(string input)
    {
        _gxAxis.Segments.Clear();
        _gyAxis.Segments.Clear();
        _gzAxis.Segments.Clear();

        foreach (var track in (input ?? string.Empty).Split(';'))
        {
            var currentAxis = _gzAxis;

            foreach (Match match in Regex.Matches(track, "\\[(.*?)\\]"))
            {
                var raw = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

                if (tokens.Count > 0 && ParseAxis(tokens[0]) is { } axis)
                {
                    currentAxis = axis;
                    tokens.RemoveAt(0);
                }

                if (tokens.Count == 0) continue;

                var previousEnd = currentAxis.Segments.Count > 0
                    ? currentAxis.Segments[^1].EndG
                    : currentAxis.InitialValue;

                if (tokens.Count == 1 && tokens[0] == "-")
                {
                    currentAxis.Segments.Add(new SequenceSegment(previousEnd, previousEnd, 0, true));
                    continue;
                }

                if (tokens.Count == 1)
                {
                    var duration = ParseDouble(tokens[0]);
                    currentAxis.Segments.Add(new SequenceSegment(previousEnd, previousEnd, duration, false));
                    continue;
                }

                double start;
                double end;
                double durationValue;

                if (tokens.Count == 2)
                {
                    start = previousEnd;
                    end = ParseDouble(tokens[0]);
                    durationValue = ParseDouble(tokens[1]);
                }
                else
                {
                    start = ParseDouble(tokens[0]);
                    end = ParseDouble(tokens[1]);
                    durationValue = ParseDouble(tokens[2]);
                }

                currentAxis.Segments.Add(new SequenceSegment(start, end, durationValue, false));
            }
        }
    }

    private AxisSequenceState? ParseAxis(string token)
    {
        if (token.Equals("Gx", StringComparison.OrdinalIgnoreCase)) return _gxAxis;
        if (token.Equals("Gy", StringComparison.OrdinalIgnoreCase)) return _gyAxis;
        if (token.Equals("Gz", StringComparison.OrdinalIgnoreCase)) return _gzAxis;
        return null;
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

    private readonly record struct SequenceSegment(double StartG, double EndG, double Duration, bool IsInfinite);

    private sealed class AxisSequenceState(double initialValue)
    {
        public List<SequenceSegment> Segments { get; } = [];
        public double InitialValue { get; } = initialValue;
        public double Current { get; set; } = initialValue;
        public int SegmentIndex { get; set; }
        public double SegmentElapsed { get; set; }
        public bool Finished => SegmentIndex >= Segments.Count;
    }
}
