namespace SerialApp.Desktop.ViewModels;

public abstract class LayoutNodeViewModel : ViewModelBase
{
    private SplitPanelNodeViewModel? _parent;

    public SplitPanelNodeViewModel? Parent
    {
        get => _parent;
        set => SetProperty(ref _parent, value);
    }
}