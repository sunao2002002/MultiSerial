using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SerialApp.Desktop.Commands;
using SerialApp.Desktop.Models;
using SerialApp.Desktop.Services;

namespace SerialApp.Desktop.ViewModels;

public sealed class SerialPanelViewModel : LayoutNodeViewModel, IAsyncDisposable
{
    private const int MaxVisibleCharacters = 200_000;
    private const int MaxPendingReceiveBytes = 512_000;
    private const int MaxBytesPerFlush = 96_000;

    private readonly Action<SerialPanelViewModel> _activateCallback;
    private readonly AppStateService _appStateService;
    private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();
    private readonly SemaphoreSlim _panelOperationLock = new(1, 1);
    private readonly DispatcherTimer _receiveFlushTimer;
    private readonly ConcurrentQueue<PendingReceiveFrame> _pendingReceiveFrames = new();
    private readonly PanelLogWriter _logWriter;
    private readonly SerialPortSession _serialSession;
    private readonly Queue<ReceiveDisplayChunk> _receiveDisplayChunks = new();
    private StringBuilder _receiveMetadataBuffer = new(MaxVisibleCharacters / 2);
    private StringBuilder _receivePayloadBuffer = new(MaxVisibleCharacters);
    private string? _lastUiDirection;
    private bool _isUiLineStart = true;
    private int _receiveDisplayCharacterCount;
    private long _pendingReceiveBytes;
    private long _droppedReceiveBytes;
    private int _isFlushRunning;
    private int _portRefreshVersion;

    private bool _isActive;
    private bool _isConnected;
    private bool _isRefreshingPorts;
    private bool _showTimestamps;
    private bool _receiveAsHex;
    private bool _sendAsHex;
    private bool _appendCrLfOnSend;
    private string _selectedPortName = string.Empty;
    private string _baudRateText = "115200";
    private int _selectedDataBits = 8;
    private string _selectedStartBits = "1";
    private Parity _selectedParity = Parity.None;
    private StopBits _selectedStopBits = StopBits.One;
    private string _sendText = string.Empty;
    private string _statusMessage = "未连接";
    private string? _selectedHistory;

    public SerialPanelViewModel(
        int panelIndex,
        Action<SerialPanelViewModel> activateCallback,
        AppStateService appStateService,
        IEnumerable<SerialPortOption>? initialPortOptions = null)
    {
        PanelIndex = panelIndex;
        _activateCallback = activateCallback;
        _appStateService = appStateService;
        _serialSession = new SerialPortSession();
        _serialSession.DataReceived += SerialSession_DataReceived;
        _serialSession.ErrorOccurred += SerialSession_ErrorOccurred;
        _logWriter = new PanelLogWriter(panelIndex, _appStateService.LogDirectory);
        _logWriter.FilePathChanged += LogWriter_FilePathChanged;
        _appStateService.PanelFontSettingsChanged += AppStateService_PanelFontSettingsChanged;

        AvailablePorts = new ObservableCollection<SerialPortOption>();
        BaudRateOptions = new ObservableCollection<int>(new[] { 9600, 115200, 460800, 921600, 1_000_000, 2_000_000, 3_000_000, 4_000_000 });
        DataBitsOptions = new ObservableCollection<int>(new[] { 5, 6, 7, 8 });
        StartBitsOptions = new ObservableCollection<string>(new[] { "1" });
        ParityOptions = new ObservableCollection<Parity>(Enum.GetValues<Parity>());
        StopBitsOptions = new ObservableCollection<StopBits>(new[] { StopBits.One, StopBits.OnePointFive, StopBits.Two });
        SendHistory = _appStateService.RecentSendHistory;

        RefreshPortsCommand = new RelayCommand(_ => _ = RefreshAvailablePortsAsync(forceRefresh: true));
        ToggleConnectionCommand = new RelayCommand(_ => _ = ToggleConnectionAsync(), _ => CanToggleConnection());
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => CanSend());
        ClearReceiveCommand = new RelayCommand(_ => ClearReceiveText());

        _receiveFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _receiveFlushTimer.Tick += ReceiveFlushTimer_Tick;
        _receiveFlushTimer.Start();

        if (initialPortOptions is not null)
        {
            ApplyAvailablePorts(initialPortOptions);
        }
        else
        {
            _ = RefreshAvailablePortsAsync(forceRefresh: true);
        }
    }

    public int PanelIndex { get; }

    public string Title => $"Panel {PanelIndex:D2}";

    public ObservableCollection<SerialPortOption> AvailablePorts { get; }

    public ObservableCollection<int> BaudRateOptions { get; }

    public ObservableCollection<int> DataBitsOptions { get; }

    public ObservableCollection<string> StartBitsOptions { get; }

    public ObservableCollection<Parity> ParityOptions { get; }

    public ObservableCollection<StopBits> StopBitsOptions { get; }

    public ObservableCollection<string> SendHistory { get; }

    public ICommand RefreshPortsCommand { get; }

    public ICommand ToggleConnectionCommand { get; }

    public ICommand SendCommand { get; }

    public ICommand ClearReceiveCommand { get; }

    public event EventHandler<ReceiveTextChangedEventArgs>? ReceiveTextChanged;

    public string LogFilePath => _logWriter.FilePath;

    public System.Windows.Media.FontFamily PanelTextFontFamily => new(_appStateService.PanelFontSettings.FamilyName);

    public double PanelTextFontSize => _appStateService.PanelFontSettings.Size;

    public System.Windows.FontWeight PanelTextFontWeight => _appStateService.PanelFontSettings.Bold ? FontWeights.Bold : FontWeights.Regular;

    public System.Windows.FontStyle PanelTextFontStyle => _appStateService.PanelFontSettings.Italic ? FontStyles.Italic : FontStyles.Normal;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectionButtonText));
            }
        }
    }

    public string ConnectionButtonText => IsConnected ? "关闭串口" : "打开串口";

    public bool IsRefreshingPorts
    {
        get => _isRefreshingPorts;
        private set => SetProperty(ref _isRefreshingPorts, value);
    }

    public bool ShowTimestamps
    {
        get => _showTimestamps;
        set
        {
            if (SetProperty(ref _showTimestamps, value))
            {
                OnPropertyChanged(nameof(ShowTimestamps));
            }
        }
    }

    public bool ReceiveAsHex
    {
        get => _receiveAsHex;
        set
        {
            if (SetProperty(ref _receiveAsHex, value))
            {
                _utf8Decoder.Reset();
            }
        }
    }

    public bool SendAsHex
    {
        get => _sendAsHex;
        set => SetProperty(ref _sendAsHex, value);
    }

    public bool AppendCrLfOnSend
    {
        get => _appendCrLfOnSend;
        set => SetProperty(ref _appendCrLfOnSend, value);
    }

    public string AdvancedSettingsSummary => $"高级配置: {SelectedDataBits} 数据位 / 起始位 {SelectedStartBits} / 停止位 {FormatStopBits(SelectedStopBits)} / {FormatParity(SelectedParity)}";

    public string SelectedPortName
    {
        get => _selectedPortName;
        set
        {
            if (SetProperty(ref _selectedPortName, value))
            {
                OnPropertyChanged(nameof(SelectedPortDetails));
            }
        }
    }

    public string SelectedPortDetails => AvailablePorts
        .FirstOrDefault(option => string.Equals(option.PortName, SelectedPortName, StringComparison.OrdinalIgnoreCase))?.DetailText
        ?? "未选择串口。";

    public string BaudRateText
    {
        get => _baudRateText;
        set => SetProperty(ref _baudRateText, value);
    }

    public int SelectedDataBits
    {
        get => _selectedDataBits;
        set
        {
            if (SetProperty(ref _selectedDataBits, value))
            {
                OnPropertyChanged(nameof(AdvancedSettingsSummary));
            }
        }
    }

    public string SelectedStartBits
    {
        get => _selectedStartBits;
        set
        {
            if (SetProperty(ref _selectedStartBits, value))
            {
                OnPropertyChanged(nameof(AdvancedSettingsSummary));
            }
        }
    }

    public Parity SelectedParity
    {
        get => _selectedParity;
        set
        {
            if (SetProperty(ref _selectedParity, value))
            {
                OnPropertyChanged(nameof(AdvancedSettingsSummary));
            }
        }
    }

    public StopBits SelectedStopBits
    {
        get => _selectedStopBits;
        set
        {
            if (SetProperty(ref _selectedStopBits, value))
            {
                OnPropertyChanged(nameof(AdvancedSettingsSummary));
            }
        }
    }

    public string SendText
    {
        get => _sendText;
        set => SetProperty(ref _sendText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? SelectedHistory
    {
        get => _selectedHistory;
        set
        {
            if (SetProperty(ref _selectedHistory, value) && !string.IsNullOrWhiteSpace(value))
            {
                SendText = value;
            }
        }
    }

    public void Activate()
    {
        _activateCallback(this);
    }

    public Task RefreshAvailablePortsAsync(bool forceRefresh = false)
    {
        var refreshVersion = Interlocked.Increment(ref _portRefreshVersion);
        IsRefreshingPorts = true;

        return RefreshAvailablePortsCoreAsync(refreshVersion, forceRefresh);
    }

    private async Task RefreshAvailablePortsCoreAsync(int refreshVersion, bool forceRefresh)
    {
        try
        {
            var ports = await Task.Run(() => SerialPortCatalogService.GetAvailablePorts(forceRefresh));

            if (refreshVersion != Volatile.Read(ref _portRefreshVersion))
            {
                return;
            }

            ApplyAvailablePorts(ports);
        }
        catch (Exception ex)
        {
            if (refreshVersion == Volatile.Read(ref _portRefreshVersion))
            {
                StatusMessage = $"串口刷新失败：{ex.Message}";
            }
        }
        finally
        {
            if (refreshVersion == Volatile.Read(ref _portRefreshVersion))
            {
                IsRefreshingPorts = false;
            }
        }
    }

    public SerialPortOption[] GetAvailablePortsSnapshot()
    {
        return AvailablePorts
            .Select(option => new SerialPortOption
            {
                PortName = option.PortName,
                FriendlyName = option.FriendlyName,
                DetailText = option.DetailText,
            })
            .ToArray();
    }

    public SerialAdvancedSettings GetAdvancedSettings()
    {
        return new SerialAdvancedSettings
        {
            DataBits = SelectedDataBits,
            StartBits = SelectedStartBits,
            StopBits = SelectedStopBits,
            Parity = SelectedParity,
        };
    }

    public void ApplyAdvancedSettings(SerialAdvancedSettings settings)
    {
        SelectedDataBits = settings.DataBits;
        SelectedStartBits = settings.StartBits;
        SelectedStopBits = settings.StopBits;
        SelectedParity = settings.Parity;
    }

    public void ApplyAvailablePorts(IEnumerable<SerialPortOption> portOptions)
    {
        var ports = portOptions
            .OrderBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previousSelection = SelectedPortName;

        AvailablePorts.Clear();

        foreach (var port in ports)
        {
            AvailablePorts.Add(port);
        }

        if (!string.IsNullOrWhiteSpace(previousSelection)
            && ports.Any(port => string.Equals(port.PortName, previousSelection, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedPortName = previousSelection;
            return;
        }

        SelectedPortName = ports.FirstOrDefault()?.PortName ?? string.Empty;
    }

    public async Task HandlePortInventoryChangedAsync(IEnumerable<SerialPortOption> portOptions)
    {
        await _panelOperationLock.WaitAsync();

        try
        {
            var ports = portOptions.ToArray();
            var connectedPortName = SelectedPortName;
            ApplyAvailablePorts(ports);

            if (IsConnected
                && !string.IsNullOrWhiteSpace(connectedPortName)
                && !ports.Any(port => string.Equals(port.PortName, connectedPortName, StringComparison.OrdinalIgnoreCase)))
            {
                var timestamp = DateTime.Now;
                var notice = $"串口 {connectedPortName} 已移除，连接已自动关闭";
                await CloseConnectionCoreAsync($"未连接（{connectedPortName} 已移除）", notice, timestamp, appendToUi: true);
            }
        }
        finally
        {
            _panelOperationLock.Release();
        }
    }

    public async Task RotateLogDirectoryAsync(string directory)
    {
        await _panelOperationLock.WaitAsync();

        try
        {
            await _logWriter.RotateDirectoryAsync(directory, SelectedPortName);
            StatusMessage = IsConnected ? GetConnectedStatus(SelectedPortName, BaudRateText) : "未连接";

            if (_logWriter.HasActiveFile)
            {
                await TryWriteLogAsync("SYS", $"log directory switched to {directory}", DateTime.Now);
            }
        }
        finally
        {
            _panelOperationLock.Release();
        }
    }

    public async Task ToggleConnectionAsync()
    {
        await _panelOperationLock.WaitAsync();

        try
        {
            if (IsConnected)
            {
                await CloseConnectionCoreAsync("未连接", "port closed", DateTime.Now, appendToUi: false);
                return;
            }

            var settings = BuildSettings();
            await _logWriter.RotateDirectoryAsync(_appStateService.LogDirectory, settings.PortName);
            await _serialSession.OpenAsync(settings);
            IsConnected = true;
            _utf8Decoder.Reset();
            StatusMessage = GetConnectedStatus(settings.PortName, settings.BaudRate.ToString(CultureInfo.InvariantCulture));
            await TryWriteLogAsync("SYS", $"port opened {settings.PortName} @ {settings.BaudRate}", DateTime.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开失败：{ex.Message}";
        }
        finally
        {
            _panelOperationLock.Release();
        }
    }

    public async Task DisconnectIfNeededAsync()
    {
        await _panelOperationLock.WaitAsync();

        try
        {
            if (IsConnected)
            {
                await CloseConnectionCoreAsync("未连接", "port closed", DateTime.Now, appendToUi: false);
            }
        }
        finally
        {
            _panelOperationLock.Release();
        }
    }

    public async Task SendAsync()
    {
        if (!CanSend())
        {
            return;
        }

        try
        {
            var payload = BuildOutgoingPayload(SendText, SendAsHex, AppendCrLfOnSend);
            await _serialSession.SendAsync(payload);
            _appStateService.RememberSend(SendText);
            var timestamp = DateTime.Now;
            var displayPayload = FormatOutgoingPayloadForDisplay(payload, SendAsHex);
            var logPayload = FormatOutgoingPayloadForLog(payload, SendAsHex);
            AppendUiLine("TX", displayPayload, timestamp);
            await TryWriteLogAsync("TX", logPayload, timestamp);
        }
        catch (Exception ex)
        {
            StatusMessage = $"发送失败：{ex.Message}";
        }
    }

    public void ClearReceiveText()
    {
        _receiveDisplayChunks.Clear();
        _receiveMetadataBuffer = new StringBuilder(MaxVisibleCharacters / 2);
        _receivePayloadBuffer = new StringBuilder(MaxVisibleCharacters);
        _receiveDisplayCharacterCount = 0;
        _lastUiDirection = null;
        _isUiLineStart = true;
        ReceiveTextChanged?.Invoke(this, new ReceiveTextChangedEventArgs(string.Empty, string.Empty, replaceAll: true));
    }

    public async ValueTask DisposeAsync()
    {
        _receiveFlushTimer.Stop();
        _receiveFlushTimer.Tick -= ReceiveFlushTimer_Tick;

        await _panelOperationLock.WaitAsync();

        try
        {
            _serialSession.DataReceived -= SerialSession_DataReceived;
            _serialSession.ErrorOccurred -= SerialSession_ErrorOccurred;
            _logWriter.FilePathChanged -= LogWriter_FilePathChanged;
            _appStateService.PanelFontSettingsChanged -= AppStateService_PanelFontSettingsChanged;
            await _serialSession.CloseAsync();
            _serialSession.Dispose();
            ClearPendingReceiveFrames();
            await _logWriter.DisposeAsync();
        }
        finally
        {
            _panelOperationLock.Release();
            _panelOperationLock.Dispose();
        }
    }

    private bool CanToggleConnection()
    {
        return IsConnected || !string.IsNullOrWhiteSpace(SelectedPortName);
    }

    private bool CanSend()
    {
        return IsConnected && !string.IsNullOrWhiteSpace(SendText);
    }

    private SerialPortSettings BuildSettings()
    {
        if (string.IsNullOrWhiteSpace(SelectedPortName))
        {
            throw new InvalidOperationException("请先选择串口。");
        }

        if (!int.TryParse(BaudRateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var baudRate) || baudRate <= 0)
        {
            throw new InvalidOperationException("波特率必须是正整数。");
        }

        return new SerialPortSettings
        {
            PortName = SelectedPortName,
            BaudRate = baudRate,
            DataBits = SelectedDataBits,
            StartBits = 1,
            Parity = SelectedParity,
            StopBits = SelectedStopBits,
        };
    }

    private async void SerialSession_DataReceived(object? sender, SerialDataChunkEventArgs e)
    {
        var pendingSize = Interlocked.Add(ref _pendingReceiveBytes, e.Buffer.Length);

        if (pendingSize > MaxPendingReceiveBytes)
        {
            Interlocked.Add(ref _pendingReceiveBytes, -e.Buffer.Length);
            Interlocked.Add(ref _droppedReceiveBytes, e.Buffer.Length);
            return;
        }

        _pendingReceiveFrames.Enqueue(new PendingReceiveFrame(e.OccurredAt, e.Buffer));
    }

    private void SerialSession_ErrorOccurred(object? sender, string errorMessage)
    {
        if (System.Windows.Application.Current is null)
        {
            return;
        }

        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = $"串口错误：{errorMessage}";
        });
    }

    private async void ReceiveFlushTimer_Tick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isFlushRunning, 1) == 1)
        {
            return;
        }

        try
        {
            await FlushPendingReceiveAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _isFlushRunning, 0);
        }
    }

    private async Task FlushPendingReceiveAsync()
    {
        if (_pendingReceiveFrames.IsEmpty && Volatile.Read(ref _droppedReceiveBytes) == 0)
        {
            return;
        }

        var appendedMetadata = new StringBuilder();
        var appendedPayload = new StringBuilder();
        var currentUiDirection = _lastUiDirection;
        var isUiLineStart = _isUiLineStart;
        var logLines = new List<string>();
        var flushedBytes = 0;
        long droppedBytes = 0;

        while (flushedBytes < MaxBytesPerFlush && _pendingReceiveFrames.TryDequeue(out var frame))
        {
            Interlocked.Add(ref _pendingReceiveBytes, -frame.Buffer.Length);
            flushedBytes += frame.Buffer.Length;

            var formattedPayload = FormatIncomingPayload(frame.Buffer);
            AppendUiEntry(appendedMetadata, appendedPayload, ref currentUiDirection, ref isUiLineStart, "RX", formattedPayload, ShowTimestamps, frame.Timestamp);
            logLines.Add(FormatLogLine("RX", formattedPayload, frame.Timestamp));
        }

        droppedBytes = Interlocked.Exchange(ref _droppedReceiveBytes, 0);

        if (droppedBytes > 0)
        {
            var warningTimestamp = DateTime.Now;
            var warning = $"接收缓存拥塞，丢弃 {droppedBytes} 字节";
            AppendUiEntry(appendedMetadata, appendedPayload, ref currentUiDirection, ref isUiLineStart, "SYS", warning, ShowTimestamps, warningTimestamp);
            logLines.Add(FormatLogLine("SYS", warning, warningTimestamp));
        }

        if (logLines.Count > 0)
        {
            try
            {
                await _logWriter.WriteLinesAsync(logLines);
            }
            catch (Exception ex)
            {
                StatusMessage = $"日志写入失败：{ex.Message}";
            }
        }

        if (appendedMetadata.Length > 0 || appendedPayload.Length > 0)
        {
            _lastUiDirection = currentUiDirection;
            _isUiLineStart = isUiLineStart;
            AppendReceiveText(appendedMetadata.ToString(), appendedPayload.ToString());
        }

        if (droppedBytes > 0)
        {
            StatusMessage = "接收缓存拥塞";
        }
    }

    private static string GetConnectedStatus(string? portName, string? baudRate)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return "已连接";
        }

        if (string.IsNullOrWhiteSpace(baudRate))
        {
            return $"已连接 {portName}";
        }

        return $"已连接 {portName} @ {baudRate}";
    }

    private string FormatIncomingPayload(byte[] payload)
    {
        if (ReceiveAsHex)
        {
            return string.Join(" ", payload.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        }

        if (payload.Length == 0)
        {
            return string.Empty;
        }

        var rentedChars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(payload.Length));

        try
        {
            _utf8Decoder.Convert(payload, 0, payload.Length, rentedChars, 0, rentedChars.Length, flush: false, out _, out var charsUsed, out _);
            return new string(rentedChars, 0, charsUsed);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rentedChars);
        }
    }

    private void AppendUiLine(string direction, string payload, DateTime timestamp)
    {
        var appendedMetadata = new StringBuilder();
        var appendedPayload = new StringBuilder();
        var currentUiDirection = _lastUiDirection;
        var isUiLineStart = _isUiLineStart;
        AppendUiEntry(appendedMetadata, appendedPayload, ref currentUiDirection, ref isUiLineStart, direction, payload, ShowTimestamps, timestamp);

        if (appendedMetadata.Length == 0 && appendedPayload.Length == 0)
        {
            return;
        }

        _lastUiDirection = currentUiDirection;
        _isUiLineStart = isUiLineStart;
        AppendReceiveText(appendedMetadata.ToString(), appendedPayload.ToString());
    }

    private static void AppendUiEntry(StringBuilder metadataBuilder, StringBuilder payloadBuilder, ref string? currentDirection, ref bool isUiLineStart, string direction, string payload, bool includeTimestamp, DateTime timestamp)
    {
        var needsDirectionSwitch = !string.IsNullOrEmpty(currentDirection)
            && !string.Equals(currentDirection, direction, StringComparison.Ordinal)
            && !isUiLineStart;

        if (needsDirectionSwitch)
        {
            metadataBuilder.AppendLine();
            payloadBuilder.AppendLine();
            isUiLineStart = true;
        }

        if (string.IsNullOrEmpty(payload))
        {
            AppendUiPrefix(metadataBuilder, direction, includeTimestamp, timestamp);
            metadataBuilder.AppendLine();
            payloadBuilder.AppendLine();
            currentDirection = direction;
            isUiLineStart = true;
            return;
        }

        for (var index = 0; index < payload.Length; index++)
        {
            var character = payload[index];

            if (isUiLineStart && character != '\r' && character != '\n')
            {
                AppendUiPrefix(metadataBuilder, direction, includeTimestamp, timestamp);
                currentDirection = direction;
                isUiLineStart = false;
            }

            payloadBuilder.Append(character);

            if (character == '\r' || character == '\n')
            {
                metadataBuilder.Append(character);
                isUiLineStart = true;
            }
        }
    }

    private static void AppendUiPrefix(StringBuilder builder, string direction, bool includeTimestamp, DateTime timestamp)
    {
        if (includeTimestamp)
        {
            builder.Append('[');
            builder.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.Append("] ");
        }

        builder.Append('[');
        builder.Append(direction);
        builder.Append("] ");
    }

    private string FormatLogLine(string direction, string payload, DateTime timestamp)
    {
        return $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{direction}] {SanitizePayloadForLog(payload)}";
    }

    public string GetReceiveMetadataSnapshot()
    {
        return _receiveMetadataBuffer.ToString();
    }

    public string GetReceivePayloadSnapshot()
    {
        return _receivePayloadBuffer.ToString();
    }

    private void LogWriter_FilePathChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(LogFilePath));
    }

    private void AppStateService_PanelFontSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PanelTextFontFamily));
        OnPropertyChanged(nameof(PanelTextFontSize));
        OnPropertyChanged(nameof(PanelTextFontWeight));
        OnPropertyChanged(nameof(PanelTextFontStyle));
    }

    private async Task CloseConnectionCoreAsync(string statusMessage, string? logMessage, DateTime timestamp, bool appendToUi)
    {
        await _serialSession.CloseAsync();
        IsConnected = false;
        _utf8Decoder.Reset();
        StatusMessage = statusMessage;

        if (!string.IsNullOrWhiteSpace(logMessage) && appendToUi)
        {
            AppendUiLine("SYS", logMessage, timestamp);
        }

        if (!string.IsNullOrWhiteSpace(logMessage))
        {
            await TryWriteLogAsync("SYS", logMessage, timestamp);
        }

        await _logWriter.CloseLogFileAsync();
    }

    private async Task TryWriteLogAsync(string direction, string payload, DateTime timestamp)
    {
        try
        {
            await _logWriter.WriteLineAsync(FormatLogLine(direction, payload, timestamp));
        }
        catch (Exception ex)
        {
            StatusMessage = $"日志写入失败：{ex.Message}";
        }
    }

    private void AppendReceiveText(string metadataText, string payloadText)
    {
        if (string.IsNullOrEmpty(metadataText) && string.IsNullOrEmpty(payloadText))
        {
            return;
        }

        var chunk = new ReceiveDisplayChunk(metadataText, payloadText);
        _receiveDisplayChunks.Enqueue(chunk);
        _receiveDisplayCharacterCount += metadataText.Length + payloadText.Length;
        _receiveMetadataBuffer.Append(metadataText);
        _receivePayloadBuffer.Append(payloadText);

        if (_receiveDisplayCharacterCount <= MaxVisibleCharacters * 2)
        {
            ReceiveTextChanged?.Invoke(this, new ReceiveTextChangedEventArgs(metadataText, payloadText, replaceAll: false));
            return;
        }

        while (_receiveDisplayCharacterCount > MaxVisibleCharacters * 2 && _receiveDisplayChunks.Count > 0)
        {
            var removedChunk = _receiveDisplayChunks.Dequeue();
            _receiveDisplayCharacterCount -= removedChunk.MetadataText.Length + removedChunk.PayloadText.Length;
        }

        _receiveMetadataBuffer = new StringBuilder(Math.Min(MaxVisibleCharacters, _receiveDisplayCharacterCount / 2 + 1024));
        _receivePayloadBuffer = new StringBuilder(Math.Min(MaxVisibleCharacters, _receiveDisplayCharacterCount + 1024));

        foreach (var displayChunk in _receiveDisplayChunks)
        {
            _receiveMetadataBuffer.Append(displayChunk.MetadataText);
            _receivePayloadBuffer.Append(displayChunk.PayloadText);
        }

        ReceiveTextChanged?.Invoke(this, new ReceiveTextChangedEventArgs(_receiveMetadataBuffer.ToString(), _receivePayloadBuffer.ToString(), replaceAll: true));
    }

    private void ClearPendingReceiveFrames()
    {
        while (_pendingReceiveFrames.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _pendingReceiveBytes, 0);
        Interlocked.Exchange(ref _droppedReceiveBytes, 0);
    }

    private static string SanitizePayloadForLog(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return payload;
        }

        StringBuilder? builder = null;

        for (var index = 0; index < payload.Length; index++)
        {
            var character = payload[index];
            var replacement = character switch
            {
                '\0' => "\\0",
                '\r' => null,
                '\n' => null,
                '\t' => null,
                _ when char.IsControl(character) => $"\\x{(int)character:X2}",
                _ => null,
            };

            if (replacement is null)
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(payload.Length + 8);
            builder.Append(payload, 0, index);
            builder.Append(replacement);

            for (index++; index < payload.Length; index++)
            {
                character = payload[index];
                replacement = character switch
                {
                    '\0' => "\\0",
                    '\r' => null,
                    '\n' => null,
                    '\t' => null,
                    _ when char.IsControl(character) => $"\\x{(int)character:X2}",
                    _ => null,
                };

                if (replacement is null)
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append(replacement);
                }
            }

            return builder.ToString();
        }

        return payload;
    }

    private static string FormatOutgoingPayloadForDisplay(byte[] payload, bool useHex)
    {
        return useHex
            ? string.Join(" ", payload.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)))
            : Encoding.UTF8.GetString(payload)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string FormatOutgoingPayloadForLog(byte[] payload, bool useHex)
    {
        return useHex
            ? string.Join(" ", payload.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)))
            : Encoding.UTF8.GetString(payload);
    }

    private static byte[] BuildOutgoingPayload(string content, bool useHex, bool appendCrLfOnSend)
    {
        if (!useHex)
        {
            var textToSend = appendCrLfOnSend ? $"{content}\r\n" : content;
            return Encoding.UTF8.GetBytes(textToSend);
        }

        var tokens = content
            .Split(new[] { ' ', '\t', ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..] : token)
            .ToArray();

        if (tokens.Length == 0)
        {
            throw new InvalidOperationException("Hex 模式下没有可发送的数据。");
        }

        var buffer = new List<byte>(tokens.Length);

        foreach (var token in tokens)
        {
            if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException($"非法 Hex 字节: {token}");
            }

            buffer.Add(value);
        }

        return buffer.ToArray();
    }

    private readonly record struct PendingReceiveFrame(DateTime Timestamp, byte[] Buffer);

    private readonly record struct ReceiveDisplayChunk(string MetadataText, string PayloadText);

    private static string FormatStopBits(StopBits stopBits)
    {
        return stopBits switch
        {
            StopBits.None => "0",
            StopBits.One => "1",
            StopBits.OnePointFive => "1.5",
            StopBits.Two => "2",
            _ => stopBits.ToString(),
        };
    }

    private static string FormatParity(Parity parity)
    {
        return parity switch
        {
            Parity.None => "无校验",
            Parity.Odd => "奇校验",
            Parity.Even => "偶校验",
            Parity.Mark => "Mark 校验",
            Parity.Space => "Space 校验",
            _ => parity.ToString(),
        };
    }
}