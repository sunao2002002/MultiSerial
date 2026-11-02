namespace SerialApp.Desktop.Models;

public sealed class AppPreferences
{
    public string? LogDirectory { get; set; }

    public PanelFontSettings PanelFontSettings { get; set; } = new();

    public bool ShowTimestamps { get; set; }

    public List<string> RecentSendHistory { get; set; } = new();
}