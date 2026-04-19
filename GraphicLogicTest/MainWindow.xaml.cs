using GEffectLogicTests.Logging;
using GraphicLogicTest.Logging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace GraphicLogicTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private double _gx;
        public double Gx { get => _gx; set { _gx = value; OnPropertyChanged(); } }
        private double _gy;
        public double Gy { get => _gy; set { _gy = value; OnPropertyChanged(); } }
        private double _gz;
        public double Gz { get => _gz; set { _gz = value; OnPropertyChanged(); } }

        private PlotModel _sliderPlotModel;
        public PlotModel SliderPlotModel { get => _sliderPlotModel; set { _sliderPlotModel = value; OnPropertyChanged(); } }
        private LineSeries _gxSeries;
        private LineSeries _gySeries;
        private LineSeries _gzSeries;

        private PlotModel _sliderPlotModel2;
        public PlotModel SliderPlotModel2 { get => _sliderPlotModel2; set { _sliderPlotModel2 = value; OnPropertyChanged(); } }
        private LineSeries _consiousnessLevel;
        private LineSeries _greyScaleLevel;
        private LineSeries _cummulatedGx;
        private LineSeries _cummulatedGy;
        private LineSeries _cummulatedGz;

        private DispatcherTimer _timer;
        private DateTime _lastTime;

        private TestLogging loggerInstance;
        private GEffectsLogic.GEffectsLogic logicInstance;
        public double GzPT { get => GEffectsLogic.LogicSettings.GzPTolerance; set { GEffectsLogic.LogicSettings.GzPTolerance = value; OnPropertyChanged(); } }
        public double GzMT { get => GEffectsLogic.LogicSettings.GzMTolerance; set { GEffectsLogic.LogicSettings.GzMTolerance = value; OnPropertyChanged(); } }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            loggerInstance = new TestLogging();
            GEffectsLogic.Logging.Logger.Instance = new LogicLogging();
            GEffectsLogic.LogicSettings.DebugMode = true;
            logicInstance = new GEffectsLogic.GEffectsLogic();

            // Initialize the plot model
            SliderPlotModel = new PlotModel { Title = "Slider Values Over Time" };
            SliderPlotModel2 = new PlotModel { Title = "Slider Values Over Time" };

            // Create the line series for Gx, Gy, and Gz
            _gxSeries = new LineSeries { Title = "Gx", Color = OxyColors.Red };
            _gySeries = new LineSeries { Title = "Gy", Color = OxyColors.Green };
            _gzSeries = new LineSeries { Title = "Gz", Color = OxyColors.Blue };

            _cummulatedGz = new LineSeries { Title = "CummulatedGz", Color = OxyColors.Red };
            _consiousnessLevel = new LineSeries { Title = "ConsiousnessLevel", Color = OxyColors.Green };
            _greyScaleLevel = new LineSeries { Title = "GreyScaleLevel", Color = OxyColors.Blue };
            _cummulatedGy = new LineSeries { Title = "CummulatedGy", Color = OxyColors.Purple }; // Initialize cummulatedGy series
            _cummulatedGx = new LineSeries { Title = "CummulatedGx", Color = OxyColors.Orange }; // Initialize cummulatedGx series

            // Add the series to the plot model
            SliderPlotModel.Series.Add(_gxSeries);
            SliderPlotModel.Series.Add(_gySeries);
            SliderPlotModel.Series.Add(_gzSeries);

            SliderPlotModel.PlotAreaBorderColor = OxyColors.White;

            // Add axes with inverted colors for dark mode and visible axis lines
            SliderPlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Value",
                Minimum = -8,
                Maximum = 12,
                TextColor = OxyColors.White,
                TitleColor = OxyColors.White,
                TicklineColor = OxyColors.White,
            });
            SliderPlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                Minimum = -30,
                Maximum = 0,
                MajorStep = 10,
                MinorStep = 5,
                TextColor = OxyColors.White,
                TitleColor = OxyColors.White,
                TicklineColor = OxyColors.White,
            });

            // Add the series to the plot model
            SliderPlotModel2.Series.Add(_cummulatedGz);
            SliderPlotModel2.Series.Add(_consiousnessLevel);
            SliderPlotModel2.Series.Add(_greyScaleLevel);
            SliderPlotModel2.Series.Add(_cummulatedGy); // Add cummulatedGy to SliderPlotModel2
            SliderPlotModel2.Series.Add(_cummulatedGx); // Add cummulatedGx to SliderPlotModel2

            SliderPlotModel2.PlotAreaBorderColor = OxyColors.White;

            var logarithmicAxis = new LinearAxis
            {
                Position = AxisPosition.Right,
                Title = "CummulatedValues",
                Minimum = 0,
                TextColor = OxyColors.White,
                TitleColor = OxyColors.White,
                TicklineColor = OxyColors.White,
                Key = "LogarithmicAxis",
            };
            SliderPlotModel2.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Value",
                Minimum = 0,
                Maximum = 1.2,
                TextColor = OxyColors.White,
                TitleColor = OxyColors.White,
                TicklineColor = OxyColors.White,
            });
            SliderPlotModel2.Axes.Add(logarithmicAxis); // Assign the LogarithmicAxis to a variable
            SliderPlotModel2.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time (s)",
                Minimum = -30,
                Maximum = 0,
                MajorStep = 10,
                MinorStep = 5,
                TextColor = OxyColors.White,
                TitleColor = OxyColors.White,
                TicklineColor = OxyColors.White,
            });

            // Assign cummulated values to the LogarithmicAxis
            _cummulatedGy.YAxisKey = logarithmicAxis.Key;
            _cummulatedGz.YAxisKey = logarithmicAxis.Key;
            _cummulatedGx.YAxisKey = logarithmicAxis.Key;

            // Start tracking time
            _lastTime = DateTime.Now;

            // Set up a timer to update the graph every second
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            _timer.Tick += UpdateGraph;
            _timer.Start();
        }

        private void UpdateGraph(object sender, EventArgs e)
        {
            double dt = (DateTime.Now - _lastTime).TotalSeconds;
            _lastTime = DateTime.Now;

            // Temporary list to store updated points
            var updatedPoints = new System.Collections.Generic.List<DataPoint>();

            // Update Gx series
            updatedPoints.AddRange(_gxSeries.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, Gx));
            _gxSeries.Points.Clear();
            _gxSeries.Points.AddRange(updatedPoints);

            // Clear the temporary list and reuse it for Gy series
            updatedPoints.Clear();
            updatedPoints.AddRange(_gySeries.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, Gy)); // Add the new data point
            _gySeries.Points.Clear();
            _gySeries.Points.AddRange(updatedPoints);

            // Clear the temporary list and reuse it for Gz series
            updatedPoints.Clear();
            updatedPoints.AddRange(_gzSeries.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, Gz)); // Add the new data point
            _gzSeries.Points.Clear();
            _gzSeries.Points.AddRange(updatedPoints);

            logicInstance.Update(dt, Gx, Gy, Gz);

            updatedPoints.Clear();
            updatedPoints.AddRange(_cummulatedGz.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, logicInstance.CummulatedGz));
            _cummulatedGz.Points.Clear();
            _cummulatedGz.Points.AddRange(updatedPoints);

            updatedPoints.Clear();
            updatedPoints.AddRange(_consiousnessLevel.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, logicInstance.ConsiousnessLevel)); // Add the new data point
            _consiousnessLevel.Points.Clear();
            _consiousnessLevel.Points.AddRange(updatedPoints);

            updatedPoints.Clear();
            updatedPoints.AddRange(_greyScaleLevel.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, logicInstance.GreyScaleLevel)); // Add the new data point
            _greyScaleLevel.Points.Clear();
            _greyScaleLevel.Points.AddRange(updatedPoints);

            updatedPoints.Clear();
            updatedPoints.AddRange(_cummulatedGy.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, logicInstance.CummulatedGy)); // Add the new data point
            _cummulatedGy.Points.Clear();
            _cummulatedGy.Points.AddRange(updatedPoints);

            updatedPoints.Clear();
            updatedPoints.AddRange(_cummulatedGx.Points.Select(p => new DataPoint(p.X - dt, p.Y)).Where(p => p.X >= -30));
            updatedPoints.Add(new DataPoint(0, logicInstance.CummulatedGx)); // Add the new data point
            _cummulatedGx.Points.Clear();
            _cummulatedGx.Points.AddRange(updatedPoints);

            // Refresh the plot
            SliderPlotModel.InvalidatePlot(true);
            SliderPlotModel2.InvalidatePlot(true);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}