using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GEffectsLogic;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace GraphicLogicTest.Views.GLoCPlot
{
    public partial class GLoCPlot : UserControl
    {
        public GEffectsLogicInstance LogicInstance { get; private set; }

        public string StartG { get; set; } = "1";
        public string EndG { get; set; } = "9";
        public string Iterations { get; set; } = "10";

        private ObservableCollection<ObservablePoint> GLoCPoints = new();

        public List<ISeries> Series { get; private set; }

        public Axis[] XAxes { get; } = [new Axis { MinLimit = 0 }];
        public Axis[] YAxes { get; } = [new Axis { MinLimit = 0 }];

        public GLoCPlot()
        {
            AvaloniaXamlLoader.Load(this);
            GLoCPoints.Add(new ObservablePoint(1, 0));
            GLoCPoints.Add(new ObservablePoint(0.25, 0.5));
            GLoCPoints.Add(new ObservablePoint(0.0, 1.0));

            Series =
            [
                new LineSeries<ObservablePoint>()
                {
                    Name = "GLoC",
                    Values = GLoCPoints,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Stroke = new SolidColorPaint(SKColors.Red),
                    Fill = null
                },
            ];
            DataContext = this;

            LogicInstance = new GEffectsLogicInstance();
        }

        private void AddGAccelerationLine(double startG, double endG, double endTime)
        {
            Series.Add(new LineSeries<ObservablePoint>()
            {
                Name = "GAcceleration",
                Values = new ObservableCollection<ObservablePoint>()
                {
                    new ObservablePoint(0.0, startG),
                    new ObservablePoint(endTime, endG)
                },
                GeometrySize = 0,
                Stroke = new SolidColorPaint(SKColors.DarkViolet),
                Fill = null
            });
        }

        private void OnStartClicked(object? sender, RoutedEventArgs e)
        {
            GLoCPoints.Clear();
            Series.Clear();
            Series.Add(
                new LineSeries<ObservablePoint>()
                {
                    Name = "GLoC",
                    Values = GLoCPoints,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Stroke = new SolidColorPaint(SKColors.Red),
                    Fill = null
                }
            );

            if (
                double.TryParse(StartG, out var startG) &&
                double.TryParse(EndG, out var endG) &&
                int.TryParse(Iterations, out var iterations)
            )
            {
                double stepSize = (endG - startG) / (iterations - 1);
                for (double i = startG; i <= endG; i += stepSize)
                {
                    // run LogicInstance with a constant acceleration of i until GLoC
                    double currentG = 1.0;
                    double dT = 0.2;
                    LogicInstance.Reset();
                    while (LogicInstance.ConsciousnessLevel >= 0.05)
                    {
                        LogicInstance.Update(dT, 0.0, 0.0, currentG);
                        currentG += i * dT;
                    }

                    GLoCPoints.Add(new ObservablePoint(LogicInstance.Time, currentG));
                    AddGAccelerationLine(startG, currentG, LogicInstance.Time);
                }
            }
        }
    }
}
