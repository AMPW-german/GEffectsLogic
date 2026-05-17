using Avalonia.Controls;
using Avalonia.Threading;
using GEffectLogicTests.Logging;
using GraphicLogicTest.Logging;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GraphicLogicTest
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private double _gx;
        public double Gx { get => _gx; set { _gx = value; OnPropertyChanged(); } }
        private double _gy;
        public double Gy { get => _gy; set { _gy = value; OnPropertyChanged(); } }
        private double _gz = 1.0;
        public double Gz { get => _gz; set { _gz = value; OnPropertyChanged(); } }

        private readonly ObservableCollection<ObservablePoint> _gxPoints = new();
        private readonly ObservableCollection<ObservablePoint> _gyPoints = new();
        private readonly ObservableCollection<ObservablePoint> _gzPoints = new();

        private readonly ObservableCollection<ObservablePoint> _consciounessPoints = new();
        private readonly ObservableCollection<ObservablePoint> _brainO2HeadPoints = new();
        private readonly ObservableCollection<ObservablePoint> _bloodHeadPoints = new();
        private readonly ObservableCollection<ObservablePoint> _greyScalePoints = new();
        private readonly ObservableCollection<ObservablePoint> _perfusionPoints = new();

        private ISeries[] _sliderSeries = null!;
        public ISeries[] SliderSeries { get => _sliderSeries; set { _sliderSeries = value; OnPropertyChanged(); } }

        private Axis[] _sliderXAxes = null!;
        public Axis[] SliderXAxes { get => _sliderXAxes; set { _sliderXAxes = value; OnPropertyChanged(); } }

        private Axis[] _sliderYAxes = null!;
        public Axis[] SliderYAxes { get => _sliderYAxes; set { _sliderYAxes = value; OnPropertyChanged(); } }

        private ISeries[] _sliderSeries2 = null!;
        public ISeries[] SliderSeries2 { get => _sliderSeries2; set { _sliderSeries2 = value; OnPropertyChanged(); } }

        private Axis[] _sliderXAxes2 = null!;
        public Axis[] SliderXAxes2 { get => _sliderXAxes2; set { _sliderXAxes2 = value; OnPropertyChanged(); } }

        private Axis[] _sliderYAxes2 = null!;
        public Axis[] SliderYAxes2 { get => _sliderYAxes2; set { _sliderYAxes2 = value; OnPropertyChanged(); } }

        private DispatcherTimer _timer = null!;
        private DateTime _lastTime;

        private TestLogging loggerInstance = null!;
        private GEffectsLogic.GEffectsLogic logicInstance = null!;
        public double HydrostaticShiftRate { get => GEffectsLogic.LogicSettings.HydrostaticShiftRate; set { GEffectsLogic.LogicSettings.HydrostaticShiftRate = value; OnPropertyChanged(); } }
        public double HeadCoreShiftFraction { get => GEffectsLogic.LogicSettings.HeadCoreShiftFraction; set { GEffectsLogic.LogicSettings.HeadCoreShiftFraction = value; OnPropertyChanged(); } }
        public double CoreLowerShiftFraction { get => GEffectsLogic.LogicSettings.CoreLowerShiftFraction; set { GEffectsLogic.LogicSettings.CoreLowerShiftFraction = value; OnPropertyChanged(); } }

        public double PassiveReturnRate { get => GEffectsLogic.LogicSettings.PassiveReturnRate; set { GEffectsLogic.LogicSettings.PassiveReturnRate = value; OnPropertyChanged(); } }
        public double O2DeliveryRate { get => GEffectsLogic.LogicSettings.BrainO2DepletionTauMild; set { GEffectsLogic.LogicSettings.BrainO2DepletionTauMild = value; OnPropertyChanged(); } }
        public double O2ConsumptionRate { get => GEffectsLogic.LogicSettings.BrainO2DepletionTauSevere; set { GEffectsLogic.LogicSettings.BrainO2DepletionTauSevere = value; OnPropertyChanged(); } }
        public double O2PerfusionCurveStrength
        {
            get => GEffectsLogic.LogicSettings.O2PerfusionCurveStrength;
            set { GEffectsLogic.LogicSettings.O2PerfusionCurveStrength = value; OnPropertyChanged(); }
        }

        public double O2PerfusionCurvePivot
        {
            get => GEffectsLogic.LogicSettings.O2PerfusionCurvePivot;
            set { GEffectsLogic.LogicSettings.O2PerfusionCurvePivot = value; OnPropertyChanged(); }
        }
        private double _timeMultiplier = 1.0;
        public double TimeMultiplier { get => _timeMultiplier; set { _timeMultiplier = value; OnPropertyChanged(); } }

        private int _updateMultiplier = 1;
        public int UpdateMultiplier { get => _updateMultiplier; set { _updateMultiplier = value; OnPropertyChanged(); } }

        private bool _isPaused = false;
        public bool IsPaused
        {
            get => _isPaused;
            set { _isPaused = value; OnPropertyChanged(); OnPropertyChanged(nameof(PauseButtonText)); }
        }
        public string PauseButtonText => IsPaused ? "Resume" : "Pause";

        private double _sequenceStartGz = 1.0;
        public double SequenceStartGz { get => _sequenceStartGz; set { _sequenceStartGz = value; OnPropertyChanged(); } }

        private double _sequenceEndGz = 9.0;
        public double SequenceEndGz { get => _sequenceEndGz; set { _sequenceEndGz = value; OnPropertyChanged(); } }

        private double _sequenceDuration = 10.0;
        public double SequenceDuration { get => _sequenceDuration; set { _sequenceDuration = value; OnPropertyChanged(); } }

        private bool _isSequenceRunning;
        private double _sequenceElapsed;

        private double _recordedTime = 60.0;
        public double RecordedTime
        {
            get => _recordedTime;
            set
            {
                _recordedTime = value;
                OnPropertyChanged();
                if (SliderXAxes?.Length > 0) SliderXAxes[0].MinLimit = -value;
                if (SliderXAxes2?.Length > 0) SliderXAxes2[0].MinLimit = -value;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            loggerInstance = new TestLogging();
            GEffectsLogic.Logging.Logger.Instance = new LogicLogging();
            GEffectsLogic.LogicSettings.DebugMode = true;
            logicInstance = new GEffectsLogic.GEffectsLogic();

            SliderSeries = new ISeries[]
            {
                CreateSeries("Gx", SKColors.Red, _gxPoints),
                CreateSeries("Gy", SKColors.Green, _gyPoints),
                CreateSeries("Gz", SKColors.Blue, _gzPoints),
            };

            SliderXAxes = new[]
            {
                CreateAxis("Time (s)", -RecordedTime, 0)
            };
            SliderYAxes = new[]
            {
                CreateAxis("Value", 0, 12)
            };

            SliderSeries2 = new ISeries[]
            {
                CreateSeries("Consciousness", SKColors.Red, _consciounessPoints),
                CreateSeries("BloodHeadLevel", SKColors.Green, _bloodHeadPoints),
                CreateSeries("BrainO2", SKColors.Violet, _brainO2HeadPoints),
                CreateSeries("GreyScaleLevel", SKColors.Blue, _greyScalePoints),
                CreateSeries("PerfusionLevel", SKColors.Orange, _perfusionPoints),
            };

            SliderXAxes2 = new[]
            {
                CreateAxis("Time (s)", -RecordedTime, 0)
            };
            SliderYAxes2 = new[]
            {
                new Axis
                {
                    Name = "CummulatedValues",
                    MinLimit = 0,
                    MaxLimit = 1.1,
                    Position = AxisPosition.End,
                    LabelsPaint = new SolidColorPaint(SKColors.White),
                    NamePaint = new SolidColorPaint(SKColors.White),
                    SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 40))
                }
            };

            _lastTime = DateTime.Now;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += UpdateGraph;
            _timer.Start();
        }

        private static LineSeries<ObservablePoint> CreateSeries(
            string name,
            SKColor color,
            ObservableCollection<ObservablePoint> values,
            int scalesYAt = 0)
        {
            return new LineSeries<ObservablePoint>
            {
                Name = name,
                Values = values,
                ScalesYAt = scalesYAt,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                XToolTipLabelFormatter = chartPoint => $"t: {-chartPoint.Model!.X:F2}s",
                YToolTipLabelFormatter = chartPoint => $"{chartPoint.Model!.Y:F3}"
            };
        }

        private static Axis CreateAxis(string name, double min, double max)
        {
            return new Axis
            {
                Name = name,
                MinLimit = min,
                MaxLimit = max,
                LabelsPaint = new SolidColorPaint(SKColors.White),
                NamePaint = new SolidColorPaint(SKColors.White),
                SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 40))
            };
        }

        private void UpdateGraph(object? sender, EventArgs e)
        {
            double dt = (DateTime.Now - _lastTime).TotalSeconds;
            _lastTime = DateTime.Now;

            if (IsPaused)
                return;

            double scaledDt = dt * TimeMultiplier;
            int iterations = Math.Max(1, UpdateMultiplier);
            double dtIterationTime = scaledDt / iterations;

            for (int i = 0; i < iterations; i++)
            {
                if (_isSequenceRunning)
                {
                    if (SequenceDuration <= 0)
                    {
                        Gz = SequenceEndGz;
                        _isSequenceRunning = false;
                        IsPaused = true;
                    }
                    else
                    {
                        _sequenceElapsed += dtIterationTime;
                        double sequenceProgress = Math.Clamp(_sequenceElapsed / SequenceDuration, 0.0, 1.0);
                        Gz = SequenceStartGz + ((SequenceEndGz - SequenceStartGz) * sequenceProgress);
                        if (sequenceProgress >= 1.0)
                        {
                            _isSequenceRunning = false;
                            IsPaused = true;
                            break;
                        }
                    }
                }

                logicInstance.Update(dtIterationTime, Gx, Gy, Gz);
                UpdateSeriesPoints(_gxPoints, dtIterationTime, Gx, RecordedTime);
                UpdateSeriesPoints(_gyPoints, dtIterationTime, Gy, RecordedTime);
                UpdateSeriesPoints(_gzPoints, dtIterationTime, Gz, RecordedTime);
                UpdateSeriesPoints(_consciounessPoints, dtIterationTime, logicInstance.ConsciousnessLevel, RecordedTime);
                UpdateSeriesPoints(_bloodHeadPoints, dtIterationTime, logicInstance.BloodHead, RecordedTime);
                UpdateSeriesPoints(_brainO2HeadPoints, dtIterationTime, logicInstance.physModel.BrainO2, RecordedTime);
                UpdateSeriesPoints(_greyScalePoints, dtIterationTime, logicInstance.GreyScaleLevel, RecordedTime);
                UpdateSeriesPoints(_perfusionPoints, dtIterationTime, logicInstance.PerfusionLevel, RecordedTime);
            }
        }

        private static void UpdateSeriesPoints(ObservableCollection<ObservablePoint> points, double dt, double currentValue, double recordedTime)
        {
            for (int i = 0; i < points.Count; i++)
            {
                points[i].X -= dt;
            }

            while (points.Count > 0 && points[0].X < -recordedTime)
            {
                points.RemoveAt(0);
            }

            points.Add(new ObservablePoint(0, currentValue));
        }

        private void PauseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            IsPaused = !IsPaused;
        }

        private void SequenceStartButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ResetGModel();

            _sequenceElapsed = 0.0;
            _isSequenceRunning = true;
            Gz = SequenceStartGz;
            IsPaused = false;
            _lastTime = DateTime.Now;
        }

        private void ResetGModel()
        {
            logicInstance.Time = 0;
            logicInstance.LastGx = 0;
            logicInstance.LastGy = 0;
            logicInstance.LastGz = 0;
            logicInstance.BloodHead = 1.0;
            logicInstance.perfusionLevel = 1.0;
            logicInstance.ConsciousnessLevel = 1.0;
            logicInstance.ConfusionLevel = 0.0;
            logicInstance.TunnelVisionLevel = 0.0;
            logicInstance.GreyScaleLevel = 0.0;
            logicInstance.PrimaryColor = true;
            logicInstance.PhysModel.Reset();

            _gxPoints.Clear();
            _gyPoints.Clear();
            _gzPoints.Clear();
            _consciounessPoints.Clear();
            _bloodHeadPoints.Clear();
            _brainO2HeadPoints.Clear();
            _greyScalePoints.Clear();
            _perfusionPoints.Clear();
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
