using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SerialApp.Desktop.ViewModels;
using FormsPanel = System.Windows.Forms.Panel;

namespace SerialApp.Desktop.Views;

public partial class GitBashPanelView : System.Windows.Controls.UserControl
{
    private readonly FormsPanel _terminalPanel = new()
    {
        Dock = System.Windows.Forms.DockStyle.Fill,
        BackColor = System.Drawing.Color.FromArgb(17, 24, 39),
    };

    private GitBashPanelViewModel? _boundViewModel;

    public GitBashPanelView()
    {
        InitializeComponent();
        TerminalHost.Child = _terminalPanel;
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(HandlePanelPreviewMouseLeftButtonDown), true);
        Loaded += GitBashPanelView_Loaded;
        Unloaded += GitBashPanelView_Unloaded;
        SizeChanged += GitBashPanelView_SizeChanged;
        DataContextChanged += GitBashPanelView_DataContextChanged;
    }

    private void HandlePanelPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GitBashPanelViewModel viewModel)
        {
            viewModel.Activate();
        }
    }

    private async void GitBashPanelView_Loaded(object sender, RoutedEventArgs e)
    {
        await AttachHostAsync();
    }

    private void GitBashPanelView_Unloaded(object sender, RoutedEventArgs e)
    {
        _boundViewModel = null;
    }

    private async void GitBashPanelView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _boundViewModel = e.NewValue as GitBashPanelViewModel;
        await AttachHostAsync();
    }

    private void GitBashPanelView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeEmbeddedWindow();
    }

    private async System.Threading.Tasks.Task AttachHostAsync()
    {
        if (!IsLoaded || _boundViewModel is null || _terminalPanel.Handle == IntPtr.Zero)
        {
            return;
        }

        await _boundViewModel.AttachHostAsync(_terminalPanel.Handle);
        ResizeEmbeddedWindow();
    }

    private void ResizeEmbeddedWindow()
    {
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.ResizeHost(Math.Max(0, _terminalPanel.ClientSize.Width), Math.Max(0, _terminalPanel.ClientSize.Height));
    }
}