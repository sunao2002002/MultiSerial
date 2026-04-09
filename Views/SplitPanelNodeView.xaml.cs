using System.Windows;
using System.Windows.Controls;
using SerialApp.Desktop.ViewModels;
using ControlOrientation = System.Windows.Controls.Orientation;

namespace SerialApp.Desktop.Views;

public partial class SplitPanelNodeView : System.Windows.Controls.UserControl
{
    public SplitPanelNodeView()
    {
        InitializeComponent();
        DataContextChanged += SplitPanelNodeView_DataContextChanged;
    }

    private void SplitPanelNodeView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        ConfigureGrid();
    }

    private void ConfigureGrid()
    {
        if (DataContext is not SplitPanelNodeViewModel viewModel)
        {
            return;
        }

        LayoutGrid.RowDefinitions.Clear();
        LayoutGrid.ColumnDefinitions.Clear();

        if (viewModel.Orientation == ControlOrientation.Horizontal)
        {
            FirstContentHost.Margin = new Thickness(0, 0, 6, 0);
            SecondContentHost.Margin = new Thickness(6, 0, 0, 0);
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(FirstContentHost, 0);
            Grid.SetColumn(PanelSplitter, 1);
            Grid.SetColumn(SecondContentHost, 2);
            Grid.SetRow(FirstContentHost, 0);
            Grid.SetRow(PanelSplitter, 0);
            Grid.SetRow(SecondContentHost, 0);

            PanelSplitter.Width = 5;
            PanelSplitter.Height = double.NaN;
            PanelSplitter.ResizeDirection = GridResizeDirection.Columns;
            PanelSplitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            PanelSplitter.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
            return;
        }

        FirstContentHost.Margin = new Thickness(0, 0, 0, 6);
        SecondContentHost.Margin = new Thickness(0, 6, 0, 0);
        LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        LayoutGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        LayoutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(FirstContentHost, 0);
        Grid.SetRow(PanelSplitter, 1);
        Grid.SetRow(SecondContentHost, 2);
        Grid.SetColumn(FirstContentHost, 0);
        Grid.SetColumn(PanelSplitter, 0);
        Grid.SetColumn(SecondContentHost, 0);

        PanelSplitter.Width = double.NaN;
        PanelSplitter.Height = 5;
        PanelSplitter.ResizeDirection = GridResizeDirection.Rows;
        PanelSplitter.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        PanelSplitter.VerticalAlignment = System.Windows.VerticalAlignment.Center;
    }
}