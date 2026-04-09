using System.IO.Ports;

namespace SerialApp.Desktop.Models;

public sealed class SerialPortSettings
{
    public required string PortName { get; init; }

    public required int BaudRate { get; init; }

    public required int DataBits { get; init; }

    public int StartBits { get; init; } = 1;

    public required Parity Parity { get; init; }

    public required StopBits StopBits { get; init; }
}