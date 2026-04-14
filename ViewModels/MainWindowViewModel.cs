using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
    private readonly MemoryDiagnosticsService _memoryDiagnosticsService;
    private readonly SemaphoreSlim _deviceRefreshLock = new(1, 1);
    private LayoutNodeViewModel _rootNode;
    private SerialPanelViewModel? _activePanel;
    private SerialPortOption[] _lastKnownPorts = Array.Empty<SerialPortOption>();
    private int _panelSequence = 1;
    private int _refreshAllPortsVersion;
    private bool _isRefreshingAllPorts;
    private string _latestMemorySnapshotSummary = "未记录内存快照";

    public MainWindowViewModel()
    {
        _appStateService = new AppStateService();
        _memoryDiagnosticsService = new MemoryDiagnosticsService(_appStateService.LogDirectory);
        _lastKnownPorts = QueryAvailablePorts(forceRefresh: true);
        var initialPanel = CreatePanel(_lastKnownPorts);
        _rootNode = initialPanel;
        ActivatePanel(initialPanel);
    }

    public string CurrentLogDirectory => _appStateService.LogDirectory;

    public string DefaultLogDirectory => _appStateService.DefaultLogDirectory;

    public bool IsFirstRun => _appStateService.IsFirstRun;

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

    public string LatestMemorySnapshotSummary => _latestMemorySnapshotSummary;

    public string MemorySnapshotLogPath => _memoryDiagnosticsService.SnapshotFilePath;

    public bool IsRefreshingAllPorts
    {
        get => _isRefreshingAllPorts;
        private set => SetProperty(ref _isRefreshingAllPorts, value);
    }

    public PanelFontSettings GetPanelFontSettings()
    {
        return _appStateService.PanelFontSettings;
    }

    public bool UpdatePanelFontSettings(PanelFontSettings settings)
    {
        return _appStateService.SetPanelFontSettings(settings);
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
            var ports = await QueryAvailablePortsAsync(forceRefresh: true);

            if (refreshVersion != Volatile.Read(ref _refreshAllPortsVersion))
            {
                return;
            }

            _lastKnownPorts = ports;

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
        var ports = await QueryAvailablePortsAsync(forceRefresh: true);
        await _deviceRefreshLock.WaitAsync();

        try
        {
            _lastKnownPorts = ports;

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

        _memoryDiagnosticsService.UpdateLogDirectory(_appStateService.LogDirectory);

        OnPropertyChanged(nameof(CurrentLogDirectory));
        OnPropertyChanged(nameof(MemorySnapshotLogPath));
        OnPropertyChanged(nameof(StartupWarning));
    }

    public Task ResetLogDirectoryAsync()
    {
        return UpdateLogDirectoryAsync(_appStateService.DefaultLogDirectory);
    }

    public async Task<string> CaptureMemorySnapshotAsync(bool compactBeforeCapture)
    {
        var panels = EnumeratePanels(RootNode)
            .Select(panel => panel.GetMemorySnapshot())
            .ToArray();
        var result = await _memoryDiagnosticsService.CaptureSnapshotAsync(
            trigger: compactBeforeCapture ? "manual-capture-compacted" : "manual-capture",
            compactBeforeCapture,
            panels.Length,
            panels);

        _latestMemorySnapshotSummary = result.Summary;
        OnPropertyChanged(nameof(LatestMemorySnapshotSummary));
        OnPropertyChanged(nameof(MemorySnapshotLogPath));
        return result.Details;
    }

    public bool HasMemorySnapshotLog()
    {
        return File.Exists(MemorySnapshotLogPath);
    }

    public void SplitActivePanel(ControlOrientation orientation)
    {
        if (ActivePanel is null)
        {
            return;
        }

        var existingPanel = ActivePanel;
        var existingParent = existingPanel.Parent;
        var newPanel = CreatePanel(existingPanel.GetAvailablePortsSnapshot());
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

    private SerialPanelViewModel CreatePanel(IEnumerable<SerialPortOption>? initialPorts = null)
    {
        return new SerialPanelViewModel(_panelSequence++, ActivatePanel, _appStateService, initialPorts);
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

    private static Task<SerialPortOption[]> QueryAvailablePortsAsync(bool forceRefresh = false)
    {
        return Task.Run(() => SerialPortCatalogService.GetAvailablePorts(forceRefresh));
    }

    private static SerialPortOption[] QueryAvailablePorts(bool forceRefresh = false)
    {
        return SerialPortCatalogService.GetAvailablePorts(forceRefresh);
    }
}