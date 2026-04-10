using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using SerialApp.Desktop.Models;
using SerialApp.Desktop.Services;
using ControlOrientation = System.Windows.Controls.Orientation;

namespace SerialApp.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly AppStateService _appStateService;
    private readonly SemaphoreSlim _deviceRefreshLock = new(1, 1);
    private LayoutNodeViewModel _rootNode;
    private SerialPanelViewModel? _activePanel;
    private int _panelSequence = 1;
    private int _refreshAllPortsVersion;
    private bool _isRefreshingAllPorts;

    public MainWindowViewModel()
    {
        _appStateService = new AppStateService();
        var initialPanel = CreatePanel();
        _rootNode = initialPanel;
        ActivatePanel(initialPanel);
    }

    public string CurrentLogDirectory => _appStateService.LogDirectory;

    public string DefaultLogDirectory => _appStateService.DefaultLogDirectory;

    public LayoutNodeViewModel RootNode
    {
        get => _rootNode;
        private set => SetProperty(ref _rootNode, value);
    }

    public SerialPanelViewModel? ActivePanel
    {
        get => _activePanel;
        private set
        {
            if (ReferenceEquals(_activePanel, value))
            {
                return;
            }

            if (_activePanel is not null)
            {
                _activePanel.PropertyChanged -= ActivePanel_PropertyChanged;
            }

            _activePanel = value;

            if (_activePanel is not null)
            {
                _activePanel.PropertyChanged += ActivePanel_PropertyChanged;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActivePanelSummary));
        }
    }

    public string ActivePanelSummary => ActivePanel is null
        ? "未选中 Panel"
        : $"当前 Panel: {ActivePanel.Title} | 状态: {ActivePanel.StatusMessage}";

    public int PanelCount => EnumeratePanels(RootNode).Count();

    public string StartupWarning => _appStateService.LastWarning ?? string.Empty;

    public bool IsRefreshingAllPorts
    {
        get => _isRefreshingAllPorts;
        private set => SetProperty(ref _isRefreshingAllPorts, value);
    }

    public Task RefreshAllPortsAsync()
    {
        var refreshVersion = Interlocked.Increment(ref _refreshAllPortsVersion);
        IsRefreshingAllPorts = true;

        return RefreshAllPortsCoreAsync(refreshVersion);
    }

    private async Task RefreshAllPortsCoreAsync(int refreshVersion)
    {
        try
        {
            var ports = await QueryAvailablePortsAsync();

            if (refreshVersion != Volatile.Read(ref _refreshAllPortsVersion))
            {
                return;
            }

            foreach (var panel in EnumeratePanels(RootNode).ToArray())
            {
                panel.ApplyAvailablePorts(ports);
            }
        }
        finally
        {
            if (refreshVersion == Volatile.Read(ref _refreshAllPortsVersion))
            {
                IsRefreshingAllPorts = false;
            }
        }
    }

    public async Task HandleSerialDevicesChangedAsync()
    {
        var ports = await QueryAvailablePortsAsync();
        await _deviceRefreshLock.WaitAsync();

        try
        {
            foreach (var panel in EnumeratePanels(RootNode).ToArray())
            {
                await panel.HandlePortInventoryChangedAsync(ports);
            }
        }
        finally
        {
            _deviceRefreshLock.Release();
        }
    }

    public async Task UpdateLogDirectoryAsync(string directory)
    {
        if (!_appStateService.SetLogDirectory(directory))
        {
            OnPropertyChanged(nameof(StartupWarning));
            return;
        }

        foreach (var panel in EnumeratePanels(RootNode).ToArray())
        {
            await panel.RotateLogDirectoryAsync(_appStateService.LogDirectory);
        }

        OnPropertyChanged(nameof(CurrentLogDirectory));
        OnPropertyChanged(nameof(StartupWarning));
    }

    public Task ResetLogDirectoryAsync()
    {
        return UpdateLogDirectoryAsync(_appStateService.DefaultLogDirectory);
    }

    public void SplitActivePanel(ControlOrientation orientation)
    {
        if (ActivePanel is null)
        {
            return;
        }

        var existingPanel = ActivePanel;
        var existingParent = existingPanel.Parent;
        var newPanel = CreatePanel();
        var splitNode = new SplitPanelNodeViewModel(orientation, existingPanel, newPanel);

        if (existingParent is null)
        {
            splitNode.Parent = null;
            RootNode = splitNode;
        }
        else if (ReferenceEquals(existingParent.First, existingPanel))
        {
            existingParent.First = splitNode;
        }
        else
        {
            existingParent.Second = splitNode;
        }

        OnPropertyChanged(nameof(PanelCount));
        ActivatePanel(newPanel);
    }

    public async Task CloseActivePanelAsync()
    {
        if (ActivePanel is null)
        {
            return;
        }

        if (ReferenceEquals(RootNode, ActivePanel))
        {
            return;
        }

        var panelToRemove = ActivePanel;
        await panelToRemove.DisconnectIfNeededAsync();
        await RemovePanelAsync(panelToRemove);
    }

    public async Task ShutdownAsync()
    {
        foreach (var panel in EnumeratePanels(RootNode).ToArray())
        {
            await panel.DisconnectIfNeededAsync();
            await panel.DisposeAsync();
        }

        await _appStateService.FlushAsync();
    }

    public void ActivatePanel(SerialPanelViewModel panel)
    {
        foreach (var candidate in EnumeratePanels(RootNode))
        {
            candidate.IsActive = ReferenceEquals(candidate, panel);
        }

        ActivePanel = panel;
    }

    private SerialPanelViewModel CreatePanel()
    {
        return new SerialPanelViewModel(_panelSequence++, ActivatePanel, _appStateService);
    }

    private void ActivePanel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SerialPanelViewModel.StatusMessage))
        {
            OnPropertyChanged(nameof(ActivePanelSummary));
        }
    }

    private async Task RemovePanelAsync(SerialPanelViewModel panelToRemove)
    {
        var parent = panelToRemove.Parent;

        if (parent is null)
        {
            return;
        }

        var sibling = ReferenceEquals(parent.First, panelToRemove)
            ? parent.Second
            : parent.First;

        var grandParent = parent.Parent;

        if (grandParent is null)
        {
            sibling.Parent = null;
            RootNode = sibling;
        }
        else if (ReferenceEquals(grandParent.First, parent))
        {
            grandParent.First = sibling;
        }
        else
        {
            grandParent.Second = sibling;
        }

        await panelToRemove.DisposeAsync();
        OnPropertyChanged(nameof(PanelCount));
        ActivatePanel(FindFirstPanel(sibling));
    }

    private static SerialPanelViewModel FindFirstPanel(LayoutNodeViewModel node)
    {
        return node switch
        {
            SerialPanelViewModel panel => panel,
            SplitPanelNodeViewModel split => FindFirstPanel(split.First),
            _ => throw new System.InvalidOperationException("未知的布局节点类型。"),
        };
    }

    private static IEnumerable<SerialPanelViewModel> EnumeratePanels(LayoutNodeViewModel node)
    {
        if (node is SerialPanelViewModel panel)
        {
            yield return panel;
            yield break;
        }

        if (node is SplitPanelNodeViewModel split)
        {
            foreach (var child in EnumeratePanels(split.First))
            {
                yield return child;
            }

            foreach (var child in EnumeratePanels(split.Second))
            {
                yield return child;
            }
        }
    }

    private static Task<SerialPortOption[]> QueryAvailablePortsAsync()
    {
        return Task.Run(SerialPortCatalogService.GetAvailablePorts);
    }
}