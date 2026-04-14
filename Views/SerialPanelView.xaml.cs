using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Data;
using System.Globalization;
using System.ComponentModel;
using SerialApp.Desktop.ViewModels;

namespace SerialApp.Desktop.Views;

public partial class SerialPanelView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IsScrollLockedProperty =
        DependencyProperty.Register(
            nameof(IsScrollLocked),
            typeof(bool),
            typeof(SerialPanelView),
            new PropertyMetadata(false, OnIsScrollLockedChanged));

    public bool IsScrollLocked
    {
        get => (bool)GetValue(IsScrollLockedProperty);
        set => SetValue(IsScrollLockedProperty, value);
    }

    private static void OnIsScrollLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SerialPanelView view && e.NewValue is false)
        {
            view.ReceiveMetaTextBox.ScrollToEnd();
            view.ReceiveDataTextBox.ScrollToEnd();
        }
    }

    private SerialPanelViewModel? _boundViewModel;
    private bool _isSyncingReceiveScroll;

    public SerialPanelView()
    {
        InitializeComponent();
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(HandlePanelPreviewMouseLeftButtonDown), true);
        DataContextChanged += SerialPanelView_DataContextChanged;
        Unloaded += SerialPanelView_Unloaded;
        ReceiveMetaTextBox.PreviewMouseWheel += ReceiveMetaTextBox_PreviewMouseWheel;
        ReceiveMetaTextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ReceivePane_ScrollChanged));
        ReceiveDataTextBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ReceivePane_ScrollChanged));
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
            _ = viewModel.RefreshAvailablePortsAsync();
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

    private void SendComboBox_DropDownOpened(object sender, EventArgs e)
    {
        AttachSendHistoryContextMenus();
    }

    private void DeleteSendHistoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem menuItem
            || menuItem.Parent is not System.Windows.Controls.ContextMenu { PlacementTarget: ComboBoxItem { DataContext: string historyItem } }
            || DataContext is not SerialPanelViewModel viewModel)
        {
            return;
        }

        viewModel.DeleteSendHistoryItem(historyItem);
    }

    private void AttachSendHistoryContextMenus()
    {
        foreach (var item in SendComboBox.Items)
        {
            if (SendComboBox.ItemContainerGenerator.ContainerFromItem(item) is not ComboBoxItem comboBoxItem)
            {
                continue;
            }

            if (comboBoxItem.ContextMenu is not null)
            {
                continue;
            }

            var deleteMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "删除",
            };
            deleteMenuItem.Click += DeleteSendHistoryMenuItem_Click;

            var contextMenu = new System.Windows.Controls.ContextMenu();
            contextMenu.Items.Add(deleteMenuItem);
            comboBoxItem.ContextMenu = contextMenu;
        }
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
        _boundViewModel.PropertyChanged += BoundViewModel_PropertyChanged;
        ReceiveMetaTextBox.Text = _boundViewModel.GetReceiveMetadataSnapshot();
        ReceiveDataTextBox.Text = _boundViewModel.GetReceivePayloadSnapshot();
        UpdateReceiveMetaColumnWidth();
        ReceiveMetaTextBox.ScrollToEnd();
        ReceiveDataTextBox.ScrollToEnd();
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.ReceiveTextChanged -= BoundViewModel_ReceiveTextChanged;
        _boundViewModel.PropertyChanged -= BoundViewModel_PropertyChanged;
        _boundViewModel = null;
        ReceiveMetaTextBox.Clear();
        ReceiveDataTextBox.Clear();
    }

    private void BoundViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SerialPanelViewModel.ShowTimestamps))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(UpdateReceiveMetaColumnWidth);
            return;
        }

        UpdateReceiveMetaColumnWidth();
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
            ReceiveMetaTextBox.Text = e.MetadataText;
            ReceiveDataTextBox.Text = e.PayloadText;
        }
        else
        {
            ReceiveMetaTextBox.AppendText(e.MetadataText);
            ReceiveDataTextBox.AppendText(e.PayloadText);
        }

        if (!IsScrollLocked)
        {
            ReceiveMetaTextBox.ScrollToEnd();
            ReceiveDataTextBox.ScrollToEnd();
        }
    }

    private void ReceiveMetaTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var dataScrollViewer = FindDescendant<ScrollViewer>(ReceiveDataTextBox);

        if (dataScrollViewer is null)
        {
            return;
        }

        var nextOffset = e.Delta > 0
            ? dataScrollViewer.VerticalOffset - SystemParameters.WheelScrollLines
            : dataScrollViewer.VerticalOffset + SystemParameters.WheelScrollLines;

        dataScrollViewer.ScrollToVerticalOffset(Math.Max(0, nextOffset));
        e.Handled = true;
    }

    private void ReceiveTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var control = e.OriginalSource as DependencyObject;
        while (control != null)
        {
            if (control is System.Windows.Controls.Primitives.ScrollBar)
            {
                return;
            }

            control = VisualTreeHelper.GetParent(control);
        }

        IsScrollLocked = !IsScrollLocked;
    }

    private void ToggleScrollMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            IsScrollLocked = menuItem.IsChecked;
        }
    }

    private void ToggleScrollMenuItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            menuItem.IsChecked = IsScrollLocked;
        }
    }

    private void ReceivePane_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingReceiveScroll)
        {
            return;
        }

        if (sender is not System.Windows.Controls.TextBox sourceTextBox)
        {
            return;
        }

        var targetTextBox = ReferenceEquals(sourceTextBox, ReceiveMetaTextBox)
            ? ReceiveDataTextBox
            : ReceiveMetaTextBox;

        var sourceScrollViewer = FindDescendant<ScrollViewer>(sourceTextBox);
        var targetScrollViewer = FindDescendant<ScrollViewer>(targetTextBox);

        if (sourceScrollViewer is null || targetScrollViewer is null)
        {
            return;
        }

        _isSyncingReceiveScroll = true;

        try
        {
            targetScrollViewer.ScrollToVerticalOffset(sourceScrollViewer.VerticalOffset);
        }
        finally
        {
            _isSyncingReceiveScroll = false;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            var descendant = FindDescendant<T>(child);

            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void UpdateReceiveMetaColumnWidth()
    {
        if (_boundViewModel is null)
        {
            return;
        }

        var samplePrefix = _boundViewModel.ShowTimestamps
            ? "[2026-04-10 23:59:59.999] [RX] "
            : "[RX] ";

        var typeface = new Typeface(
            ReceiveMetaTextBox.FontFamily,
            ReceiveMetaTextBox.FontStyle,
            ReceiveMetaTextBox.FontWeight,
            FontStretches.Normal);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var formattedText = new FormattedText(
            samplePrefix,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            ReceiveMetaTextBox.FontSize,
            ReceiveMetaTextBox.Foreground,
            dpi);

        var paddingWidth = ReceiveMetaTextBox.Padding.Left
            + ReceiveMetaTextBox.Padding.Right
            + ReceiveMetaTextBox.BorderThickness.Left
            + ReceiveMetaTextBox.BorderThickness.Right
            + 18;

        ReceiveMetaColumn.Width = new GridLength(Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace + paddingWidth));
    }
}