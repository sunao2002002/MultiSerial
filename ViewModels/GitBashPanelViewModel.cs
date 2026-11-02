using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SerialApp.Desktop.Commands;
using SerialApp.Desktop.Services;

namespace SerialApp.Desktop.ViewModels;

public sealed class GitBashPanelViewModel : PanelViewModelBase
{
    private readonly GitBashWindowHostService _hostService = new();
    private readonly string _workingDirectory;
    private CancellationTokenSource? _startCancellation;
    private nint _hostHandle;
    private bool _isStarting;
    private bool _isHostAttached;

    public GitBashPanelViewModel(int panelIndex, Action<PanelViewModelBase> activateCallback, string workingDirectory)
        : base(panelIndex, activateCallback, "未启动")
    {
        _workingDirectory = workingDirectory;
        RestartCommand = new RelayCommand(_ => _ = RestartAsync(), _ => IsHostAttached && !IsStarting);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning || IsStarting);
        _hostService.ProcessExited += HostService_ProcessExited;
    }

    public override string Title => $"Git Bash {PanelIndex:D2}";

    public string WorkingDirectory => _workingDirectory;

    public string ExecutablePath => _hostService.ExecutablePath ?? "未定位";

    public bool IsStarting
    {
        get => _isStarting;
        private set
        {
            if (SetProperty(ref _isStarting, value))
            {
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public bool IsHostAttached
    {
        get => _isHostAttached;
        private set => SetProperty(ref _isHostAttached, value);
    }

    public bool IsRunning => _hostService.IsRunning;

    public ICommand RestartCommand { get; }

    public ICommand StopCommand { get; }

    public async Task AttachHostAsync(nint hostHandle)
    {
        if (hostHandle == nint.Zero)
        {
            return;
        }

        _hostHandle = hostHandle;
        IsHostAttached = true;

        if (!_hostService.IsRunning && !IsStarting)
        {
            await RestartAsync();
        }
    }

    public void ResizeHost(int width, int height)
    {
        _hostService.Resize(width, height);
    }

    public async Task RestartAsync()
    {
        if (_hostHandle == nint.Zero || IsStarting)
        {
            return;
        }

        _startCancellation?.Cancel();
        _startCancellation?.Dispose();
        _startCancellation = new CancellationTokenSource();
        IsStarting = true;
        StatusMessage = "正在启动 Git Bash";

        try
        {
            await _hostService.StartAsync(_hostHandle, WorkingDirectory, _startCancellation.Token);
            StatusMessage = "运行中";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ExecutablePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"启动失败：{ex.Message}";
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ExecutablePath));
        }
        finally
        {
            IsStarting = false;
        }
    }

    public void Stop()
    {
        _startCancellation?.Cancel();
        _hostService.Stop();
        StatusMessage = "已停止";
        OnPropertyChanged(nameof(IsRunning));
    }

    public override ValueTask DisposeAsync()
    {
        _startCancellation?.Cancel();
        _startCancellation?.Dispose();
        _hostService.ProcessExited -= HostService_ProcessExited;
        _hostService.Dispose();
        return ValueTask.CompletedTask;
    }

    private void HostService_ProcessExited(object? sender, EventArgs e)
    {
        StatusMessage = "进程已退出";
        OnPropertyChanged(nameof(IsRunning));
    }
}