using System;
using System.Threading.Tasks;

namespace SerialApp.Desktop.ViewModels;

public abstract class PanelViewModelBase : LayoutNodeViewModel, IAsyncDisposable
{
    private readonly Action<PanelViewModelBase> _activateCallback;
    private bool _isActive;
    private string _statusMessage;

    protected PanelViewModelBase(int panelIndex, Action<PanelViewModelBase> activateCallback, string initialStatusMessage)
    {
        PanelIndex = panelIndex;
        _activateCallback = activateCallback;
        _statusMessage = initialStatusMessage;
    }

    public int PanelIndex { get; }

    public abstract string Title { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }

    public void Activate()
    {
        _activateCallback(this);
    }

    public abstract ValueTask DisposeAsync();
}