using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SerialApp.Desktop.ViewModels;

namespace SerialApp.Desktop.Views;

public partial class SerialPanelView : System.Windows.Controls.UserControl
{
    private SerialPanelViewModel? _boundViewModel;

    public SerialPanelView()
    {
        InitializeComponent();
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(HandlePanelPreviewMouseLeftButtonDown), true);
        DataContextChanged += SerialPanelView_DataContextChanged;
        Unloaded += SerialPanelView_Unloaded;
    }

    private void HandlePanelPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SerialPanelViewModel viewModel)
        {
            viewModel.Activate();
        }
    }

    private void SerialPanelView_DataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();

        if (e.NewValue is SerialPanelViewModel viewModel)
        {
            AttachViewModel(viewModel);
        }
    }

    private void SerialPanelView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachViewModel();
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

    private async void SendComboBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || Keyboard.Modifiers != ModifierKeys.None
            || sender is not System.Windows.Controls.ComboBox comboBox
            || comboBox.IsDropDownOpen)
        {
            return;
        }

        if (DataContext is not SerialPanelViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        await viewModel.SendAsync();
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

    private void AttachViewModel(SerialPanelViewModel viewModel)
    {
        _boundViewModel = viewModel;
        _boundViewModel.ReceiveTextChanged += BoundViewModel_ReceiveTextChanged;
        ReceiveTextBox.Text = _boundViewModel.GetReceiveTextSnapshot();
        ReceiveTextBox.ScrollToEnd();
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.ReceiveTextChanged -= BoundViewModel_ReceiveTextChanged;
        _boundViewModel = null;
    }

    private void BoundViewModel_ReceiveTextChanged(object? sender, ReceiveTextChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => BoundViewModel_ReceiveTextChanged(sender, e));
            return;
        }

        if (e.ReplaceAll)
        {
            ReceiveTextBox.Text = e.Text;
        }
        else
        {
            ReceiveTextBox.AppendText(e.Text);
        }

        ReceiveTextBox.ScrollToEnd();
    }
}