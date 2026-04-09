using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using SerialApp.Desktop.ViewModels;

namespace SerialApp.Desktop.Views;

public partial class SerialPanelView : System.Windows.Controls.UserControl
{
    public SerialPanelView()
    {
        InitializeComponent();
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(HandlePanelPreviewMouseLeftButtonDown), true);
        ReceiveTextBox.TextChanged += ReceiveTextBox_TextChanged;
    }

    private void HandlePanelPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SerialPanelViewModel viewModel)
        {
            viewModel.Activate();
        }
    }

    private void ReceiveTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReceiveTextBox.ScrollToEnd();
    }

    private void PortComboBox_DropDownOpened(object sender, System.EventArgs e)
    {
        if (DataContext is SerialPanelViewModel viewModel)
        {
            viewModel.RefreshAvailablePorts();
        }
    }

    private void AdvancedSettingsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not SerialPanelViewModel viewModel)
        {
            return;
        }

        var dialog = new SerialAdvancedSettingsWindow(viewModel.GetAdvancedSettings())
        {
            Owner = System.Windows.Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            viewModel.ApplyAdvancedSettings(dialog.Result);
        }
    }

    private void LogPathTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenLogInNotepad();
    }

    private void OpenLogInNotepadMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        OpenLogInNotepad();
    }

    private void OpenLogFolderMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not SerialPanelViewModel viewModel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.LogFilePath) || !File.Exists(viewModel.LogFilePath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{viewModel.LogFilePath}\"",
            UseShellExecute = true,
        };

        Process.Start(startInfo);
    }

    private void OpenLogInNotepad()
    {
        if (DataContext is not SerialPanelViewModel viewModel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(viewModel.LogFilePath) || !File.Exists(viewModel.LogFilePath))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{viewModel.LogFilePath}\"",
            UseShellExecute = true,
        };

        Process.Start(startInfo);
    }
}