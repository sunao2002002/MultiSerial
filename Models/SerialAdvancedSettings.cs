using System.IO.Ports;

namespace SerialApp.Desktop.Models;

public sealed class SerialAdvancedSettings
{
    public required int DataBits { get; init; }

    public required string StartBits { get; init; }

    public required StopBits StopBits { get; init; }

    public required Parity Parity { get; init; }
}