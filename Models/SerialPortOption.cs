namespace SerialApp.Desktop.Models;

public sealed class SerialPortOption
{
    public required string PortName { get; init; }

    public required string FriendlyName { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName)
        ? PortName
        : $"{PortName}  {FriendlyName}";

    public string DetailText { get; init; } = string.Empty;

    public string SearchText => $"{PortName} {FriendlyName} {DetailText}";
}