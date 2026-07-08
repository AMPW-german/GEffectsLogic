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
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GEffectsLogic;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace GraphicLogicTest.Views.GLoCPlot;

public partial class GLoCPlot : UserControl
{
    private readonly ObservableCollection<ObservablePoint> GLoCPoints = [];

    public GLoCPlot()
    {
        AvaloniaXamlLoader.Load(this);
        GLoCPoints.Add(new ObservablePoint(1, 0));
        GLoCPoints.Add(new ObservablePoint(0.25, 0.5));
        GLoCPoints.Add(new ObservablePoint(0.0, 1.0));

        Series =
        [
            new LineSeries<ObservablePoint>
            {
                Name = "GLoC",
                Values = GLoCPoints,
                GeometrySize = 0,
                LineSmoothness = 0,
                Stroke = new SolidColorPaint(SKColors.Red),
                Fill = null
            }
        ];
        DataContext = this;

        LogicInstance = new GEffectsLogicInstance();
    }

    public GEffectsLogicInstance LogicInstance { get; }

    public string StartG { get; set; } = "1";
    public string EndG { get; set; } = "9";
    public string Iterations { get; set; } = "10";

    public List<ISeries> Series { get; }

    public Axis[] XAxes { get; } = [new() { MinLimit = 0 }];
    public Axis[] YAxes { get; } = [new() { MinLimit = 0 }];

    private void AddGAccelerationLine(double startG, double endG, double endTime)
    {
        Series.Add(new LineSeries<ObservablePoint>
        {
            Name = "GAcceleration",
            Values = new ObservableCollection<ObservablePoint>
            {
                new(0.0, startG),
                new(endTime, endG)
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
            new LineSeries<ObservablePoint>
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
            var stepSize = (endG - startG) / (iterations - 1);
            for (var i = startG; i <= endG; i += stepSize)
            {
                // run LogicInstance with a constant acceleration of i until GLoC
                var currentG = 1.0;
                var dT = 0.2;
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