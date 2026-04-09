using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using SerialApp.Desktop.Models;

namespace SerialApp.Desktop.Services;

public static class SerialPortCatalogService
{
    private static readonly Regex PortNameRegex = new(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SerialPortOption[] GetAvailablePorts()
    {
        var ports = SerialPort.GetPortNames();
        var metadata = GetMetadataByPortName();

        return ports
            .Select(portName => BuildOption(portName, metadata))
            .OrderBy(option => GetPortOrder(option.PortName))
            .ThenBy(option => option.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SerialPortOption BuildOption(string portName, IReadOnlyDictionary<string, PortMetadata> metadata)
    {
        if (!metadata.TryGetValue(portName, out var item))
        {
            return new SerialPortOption
            {
                PortName = portName,
                FriendlyName = "未识别设备",
                DetailText = "未读取到设备描述，可直接按端口号选择。",
            };
        }

        var detailParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.Manufacturer))
        {
            detailParts.Add(item.Manufacturer);
        }

        if (!string.IsNullOrWhiteSpace(item.PnpDeviceId))
        {
            detailParts.Add(item.PnpDeviceId);
        }

        return new SerialPortOption
        {
            PortName = portName,
            FriendlyName = item.FriendlyName,
            DetailText = detailParts.Count == 0 ? "可直接按端口号选择。" : string.Join("  |  ", detailParts),
        };
    }

    private static Dictionary<string, PortMetadata> GetMetadataByPortName()
    {
        var result = new Dictionary<string, PortMetadata>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Description, Manufacturer, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

            foreach (var instance in searcher.Get().OfType<ManagementObject>())
            {
                var name = instance["Name"]?.ToString();

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var match = PortNameRegex.Match(name);

                if (!match.Success)
                {
                    continue;
                }

                var portName = match.Groups[1].Value.ToUpperInvariant();
                result[portName] = new PortMetadata(
                    FriendlyName: name,
                    Manufacturer: instance["Manufacturer"]?.ToString() ?? string.Empty,
                    PnpDeviceId: instance["PNPDeviceID"]?.ToString() ?? string.Empty);
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static int GetPortOrder(string portName)
    {
        if (portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(portName[3..], out var portNumber))
        {
            return portNumber;
        }

        return int.MaxValue;
    }

    private sealed record PortMetadata(string FriendlyName, string Manufacturer, string PnpDeviceId);
}