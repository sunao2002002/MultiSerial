using System;
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
    private readonly DispatcherTimer _receiveFlushTimer;
    private readonly StringBuilder _receiveTextBuilder = new();
    private readonly ConcurrentQueue<PendingReceiveFrame> _pendingReceiveFrames = new();
    private readonly PanelLogWriter _logWriter;
    private readonly SerialPortSession _serialSession;
    private long _pendingReceiveBytes;
    private long _droppedReceiveBytes;
    private int _isFlushRunning;

    private bool _isActive;
    private bool _isConnected;
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
    private string _receiveText = string.Empty;
    private string _sendText = string.Empty;
    private string _statusMessage = "未连接";
    private string? _selectedHistory;

    public SerialPanelViewModel(int panelIndex, Action<SerialPanelViewModel> activateCallback, AppStateService appStateService)
    {
        PanelIndex = panelIndex;
        _activateCallback = activateCallback;
        _appStateService = appStateService;
        _serialSession = new SerialPortSession();
        _serialSession.DataReceived += SerialSession_DataReceived;
        _serialSession.ErrorOccurred += SerialSession_ErrorOccurred;
        _logWriter = new PanelLogWriter(panelIndex, _appStateService.LogDirectory);

        AvailablePorts = new ObservableCollection<SerialPortOption>();
        BaudRateOptions = new ObservableCollection<int>(new[] { 9600, 115200, 460800, 921600, 1_000_000, 2_000_000, 3_000_000, 4_000_000 });
        DataBitsOptions = new ObservableCollection<int>(new[] { 5, 6, 7, 8 });
        StartBitsOptions = new ObservableCollection<string>(new[] { "1" });
        ParityOptions = new ObservableCollection<Parity>(Enum.GetValues<Parity>());
        StopBitsOptions = new ObservableCollection<StopBits>(new[] { StopBits.One, StopBits.OnePointFive, StopBits.Two });
        SendHistory = _appStateService.RecentSendHistory;

        RefreshPortsCommand = new RelayCommand(_ => RefreshAvailablePorts());
        ToggleConnectionCommand = new RelayCommand(_ => _ = ToggleConnectionAsync(), _ => CanToggleConnection());
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => CanSend());
        ClearReceiveCommand = new RelayCommand(_ => ClearReceiveText());

        _receiveFlushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _receiveFlushTimer.Tick += ReceiveFlushTimer_Tick;
        _receiveFlushTimer.Start();

        RefreshAvailablePorts();
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

    public string LogFilePath => _logWriter.FilePath;

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

    public string ReceiveText
    {
        get => _receiveText;
        private set => SetProperty(ref _receiveText, value);
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

    public void RefreshAvailablePorts()
    {
        ApplyAvailablePorts(SerialPortCatalogService.GetAvailablePorts());
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
        var ports = portOptions.ToArray();
        var connectedPortName = SelectedPortName;
        ApplyAvailablePorts(ports);

        if (IsConnected
            && !string.IsNullOrWhiteSpace(connectedPortName)
            && !ports.Any(port => string.Equals(port.PortName, connectedPortName, StringComparison.OrdinalIgnoreCase)))
        {
            await _serialSession.CloseAsync();
            IsConnected = false;
            _utf8Decoder.Reset();
            var notice = $"串口 {connectedPortName} 已移除，连接已自动关闭";
            StatusMessage = $"未连接（{connectedPortName} 已移除）";
            AppendUiLine("SYS", notice, DateTime.Now);
            await WriteLogAsync("SYS", notice, DateTime.Now);
        }
    }

    public async Task RotateLogDirectoryAsync(string directory)
    {
        await _logWriter.RotateDirectoryAsync(directory, SelectedPortName);
        OnPropertyChanged(nameof(LogFilePath));
        StatusMessage = IsConnected ? GetConnectedStatus(SelectedPortName, BaudRateText) : "未连接";
        await WriteLogAsync("SYS", $"log directory switched to {directory}", DateTime.Now);
    }

    public async Task ToggleConnectionAsync()
    {
        try
        {
            if (IsConnected)
            {
                await _serialSession.CloseAsync();
                IsConnected = false;
                _utf8Decoder.Reset();
                StatusMessage = "未连接";
                await WriteLogAsync("SYS", "port closed", DateTime.Now);
                return;
            }

            var settings = BuildSettings();
            await _logWriter.RotateDirectoryAsync(_appStateService.LogDirectory, settings.PortName);
            OnPropertyChanged(nameof(LogFilePath));
            await _serialSession.OpenAsync(settings);
            IsConnected = true;
            _utf8Decoder.Reset();
            StatusMessage = GetConnectedStatus(settings.PortName, settings.BaudRate.ToString(CultureInfo.InvariantCulture));
            await WriteLogAsync("SYS", $"port opened {settings.PortName} @ {settings.BaudRate}", DateTime.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开失败：{ex.Message}";
        }
    }

    public async Task DisconnectIfNeededAsync()
    {
        if (IsConnected)
        {
            await ToggleConnectionAsync();
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
            var formatted = FormatOutgoingPayload(payload, SendAsHex);
            AppendUiLine("TX", formatted, DateTime.Now);
            await WriteLogAsync("TX", formatted, DateTime.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = $"发送失败：{ex.Message}";
        }
    }

    public void ClearReceiveText()
    {
        _receiveTextBuilder.Clear();
        ReceiveText = string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        _receiveFlushTimer.Stop();
        _receiveFlushTimer.Tick -= ReceiveFlushTimer_Tick;
        _serialSession.DataReceived -= SerialSession_DataReceived;
        _serialSession.ErrorOccurred -= SerialSession_ErrorOccurred;
        await _serialSession.CloseAsync();
        _serialSession.Dispose();
        await _logWriter.DisposeAsync();
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
        var linesToLog = new List<string>();
        var appendedText = new StringBuilder();
        var flushedBytes = 0;

        while (flushedBytes < MaxBytesPerFlush && _pendingReceiveFrames.TryDequeue(out var frame))
        {
            Interlocked.Add(ref _pendingReceiveBytes, -frame.Buffer.Length);
            flushedBytes += frame.Buffer.Length;

            var formattedPayload = FormatIncomingPayload(frame.Buffer);
            appendedText.AppendLine(FormatUiLine("RX", formattedPayload, ShowTimestamps, frame.Timestamp));
            linesToLog.Add(FormatLogLine("RX", formattedPayload, frame.Timestamp));
        }

        var droppedBytes = Interlocked.Exchange(ref _droppedReceiveBytes, 0);

        if (droppedBytes > 0)
        {
            var warning = $"接收缓存拥塞，丢弃 {droppedBytes} 字节";
            appendedText.AppendLine(FormatUiLine("SYS", warning, ShowTimestamps, DateTime.Now));
            linesToLog.Add(FormatLogLine("SYS", warning, DateTime.Now));
        }

        if (appendedText.Length > 0)
        {
            _receiveTextBuilder.Append(appendedText);
            ReceiveText = TrimReceiveText(_receiveTextBuilder);
        }

        if (linesToLog.Count > 0)
        {
            await _logWriter.WriteLinesAsync(linesToLog);
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

        var charCount = _utf8Decoder.GetCharCount(payload, 0, payload.Length);
        var chars = new char[charCount];
        _utf8Decoder.GetChars(payload, 0, payload.Length, chars, 0);
        return new string(chars);
    }

    private static string TrimReceiveText(StringBuilder builder)
    {
        if (builder.Length > MaxVisibleCharacters)
        {
            var trimLength = builder.Length - MaxVisibleCharacters;
            builder.Remove(0, trimLength);
        }

        return builder.ToString();
    }

    private void AppendUiLine(string direction, string payload, DateTime timestamp)
    {
        _receiveTextBuilder.AppendLine(FormatUiLine(direction, payload, ShowTimestamps, timestamp));
        ReceiveText = TrimReceiveText(_receiveTextBuilder);
    }

    private string FormatUiLine(string direction, string payload, bool includeTimestamp, DateTime timestamp)
    {
        if (includeTimestamp)
        {
            return $"[{timestamp:HH:mm:ss.fff}] [{direction}] {payload}";
        }

        return $"[{direction}] {payload}";
    }

    private string FormatLogLine(string direction, string payload, DateTime timestamp)
    {
        return $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{direction}] {payload}";
    }

    private async Task WriteLogAsync(string direction, string payload, DateTime timestamp)
    {
        await _logWriter.WriteLineAsync(FormatLogLine(direction, payload, timestamp));
    }

    private static string FormatOutgoingPayload(byte[] payload, bool useHex)
    {
        return useHex
            ? string.Join(" ", payload.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)))
            : Encoding.UTF8.GetString(payload)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
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