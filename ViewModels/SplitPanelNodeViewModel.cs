using System.Windows.Controls;
using ControlOrientation = System.Windows.Controls.Orientation;

namespace SerialApp.Desktop.ViewModels;

public sealed class SplitPanelNodeViewModel : LayoutNodeViewModel
{
    private LayoutNodeViewModel _first;
    private LayoutNodeViewModel _second;

    public SplitPanelNodeViewModel(ControlOrientation orientation, LayoutNodeViewModel first, LayoutNodeViewModel second)
    {
        Orientation = orientation;
        _first = first;
        _second = second;

        _first.Parent = this;
        _second.Parent = this;
    }

    public ControlOrientation Orientation { get; }

    public LayoutNodeViewModel First
    {
        get => _first;
        set
        {
            if (SetProperty(ref _first, value))
            {
                value.Parent = this;
            }
        }
    }

    public LayoutNodeViewModel Second
    {
        get => _second;
        set
        {
            if (SetProperty(ref _second, value))
            {
                value.Parent = this;
            }
        }
    }
}