using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using SerialApp.Desktop.Models;

namespace SerialApp.Desktop.Views;

public partial class SerialAdvancedSettingsWindow : Window
{
    public SerialAdvancedSettingsWindow(SerialAdvancedSettings settings)
    {
        InitializeComponent();
        DataContext = this;

        DataBitsOptions = new ObservableCollection<int>(new[] { 5, 6, 7, 8 });
        StartBitsOptions = new ObservableCollection<string>(new[] { "1" });
        StopBitsOptions = new ObservableCollection<StopBits>(new[] { StopBits.One, StopBits.OnePointFive, StopBits.Two });
        ParityOptions = new ObservableCollection<Parity>(System.Enum.GetValues<Parity>());

        SelectedDataBits = settings.DataBits;
        SelectedStartBits = settings.StartBits;
        SelectedStopBits = settings.StopBits;
        SelectedParity = settings.Parity;
    }

    public ObservableCollection<int> DataBitsOptions { get; }

    public ObservableCollection<string> StartBitsOptions { get; }

    public ObservableCollection<StopBits> StopBitsOptions { get; }

    public ObservableCollection<Parity> ParityOptions { get; }

    public int SelectedDataBits { get; set; }

    public string SelectedStartBits { get; set; } = "1";

    public StopBits SelectedStopBits { get; set; } = StopBits.One;

    public Parity SelectedParity { get; set; } = Parity.None;

    public SerialAdvancedSettings? Result { get; private set; }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Result = new SerialAdvancedSettings
        {
            DataBits = SelectedDataBits,
            StartBits = SelectedStartBits,
            StopBits = SelectedStopBits,
            Parity = SelectedParity,
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}