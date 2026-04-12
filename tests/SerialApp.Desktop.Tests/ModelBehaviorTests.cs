using System.IO.Ports;
using SerialApp.Desktop.Models;

namespace SerialApp.Desktop.Tests;

public sealed class ModelBehaviorTests
{
    [Fact]
    public void SerialPortOption_DisplayNameFallsBackToPortNameWhenFriendlyNameIsBlank()
    {
        var option = new SerialPortOption
        {
            PortName = "COM9",
            FriendlyName = " ",
            DetailText = "USB Serial",
        };

        Assert.Equal("COM9", option.DisplayName);
        Assert.Equal("COM9   USB Serial", option.SearchText);
    }

    [Fact]
    public void SerialPortOption_DisplayNameIncludesFriendlyNameWhenPresent()
    {
        var option = new SerialPortOption
        {
            PortName = "COM5",
            FriendlyName = "Debugger",
            DetailText = "Vendor",
        };

        Assert.Equal("COM5  Debugger", option.DisplayName);
        Assert.Equal("COM5 Debugger Vendor", option.SearchText);
    }

    [Fact]
    public void SerialPortSettings_StartBitsDefaultsToOne()
    {
        var settings = new SerialPortSettings
        {
            PortName = "COM1",
            BaudRate = 115200,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
        };

        Assert.Equal(1, settings.StartBits);
    }

    [Fact]
    public void PanelFontSettings_DefaultsMatchApplicationDefaults()
    {
        var settings = new PanelFontSettings();

        Assert.Equal("Consolas", settings.FamilyName);
        Assert.Equal(13d, settings.Size);
        Assert.False(settings.Bold);
        Assert.False(settings.Italic);
    }

    [Fact]
    public void SerialAdvancedSettings_RetainsAssignedValues()
    {
        var settings = new SerialAdvancedSettings
        {
            DataBits = 7,
            StartBits = "1",
            StopBits = StopBits.Two,
            Parity = Parity.Even,
        };

        Assert.Equal(7, settings.DataBits);
        Assert.Equal("1", settings.StartBits);
        Assert.Equal(StopBits.Two, settings.StopBits);
        Assert.Equal(Parity.Even, settings.Parity);
    }
}
