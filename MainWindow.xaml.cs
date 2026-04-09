using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Interop;
using SerialApp.Desktop.ViewModels;
using ControlOrientation = System.Windows.Controls.Orientation;

namespace SerialApp.Desktop;

public partial class MainWindow : Window
{
    private const string HelpDocumentRelativePath = "Docs\\UserGuide.txt";
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;

    private readonly MainWindowViewModel _viewModel;
    private bool _isShutdownComplete;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    private void SplitRightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SplitActivePanel(ControlOrientation.Horizontal);
    }

    private void SplitDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SplitActivePanel(ControlOrientation.Vertical);
    }

    private async void CloseCurrentMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.CloseActivePanelAsync();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshPortsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshAllPorts();
    }

    private async void SelectLogDirectoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择日志保存目录",
            UseDescriptionForTitle = true,
            InitialDirectory = _viewModel.CurrentLogDirectory,
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        await _viewModel.UpdateLogDirectoryAsync(dialog.SelectedPath);
    }

    private async void ResetLogDirectoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ResetLogDirectoryAsync();
    }

    private void OpenHelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var helpFilePath = Path.Combine(AppContext.BaseDirectory, HelpDocumentRelativePath);

        if (!File.Exists(helpFilePath))
        {
            System.Windows.MessageBox.Show(this, "未找到帮助文档，请确认程序目录中的 Docs/UserGuide.txt 文件存在。", "帮助", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{helpFilePath}\"",
            UseShellExecute = true,
        });
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isShutdownComplete)
        {
            return;
        }

        e.Cancel = true;
        await _viewModel.ShutdownAsync();
        _isShutdownComplete = true;
        Close();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange)
        {
            var eventType = wParam.ToInt32();

            if (eventType == DbtDeviceArrival || eventType == DbtDeviceRemoveComplete)
            {
                _ = _viewModel.HandleSerialDevicesChangedAsync();
            }
        }

        return IntPtr.Zero;
    }
}