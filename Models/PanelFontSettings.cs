namespace SerialApp.Desktop.Models;

public sealed class PanelFontSettings
{
    public string FamilyName { get; set; } = "Consolas";

    public double Size { get; set; } = 13d;

    public bool Bold { get; set; }

    public bool Italic { get; set; }
}