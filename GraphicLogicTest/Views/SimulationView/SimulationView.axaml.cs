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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GraphicLogicTest.Views.SimulationView;

public partial class SimulationView : UserControl
{
    public SimulationView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private SimulationViewModel? GetSimulationViewModel()
    {
        return DataContext as SimulationViewModel;
    }

    private void GlobalStart_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.GlobalStart_Click(sender, e);
    }

    private void GlobalStop_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.GlobalStop_Click(sender, e);
    }

    private void ResetAll_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.ResetAll_Click(sender, e);
    }

    private void AddInstance_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.AddInstance_Click(sender, e);
    }

    private void InstanceStart_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.InstanceStart_Click(sender, e);
    }

    private void InstancePause_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.InstancePause_Click(sender, e);
    }

    private void InstanceDelete_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.InstanceDelete_Click(sender, e);
    }

    private void ToggleMetricSeries_Click(object? sender, RoutedEventArgs e)
    {
        GetSimulationViewModel()?.ToggleMetricSeries_Click(sender, e);
    }
}