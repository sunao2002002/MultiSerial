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
    private static readonly object CacheSync = new();
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);
    private static readonly Regex PortNameRegex = new(@"\((COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static SerialPortOption[] _cachedPorts = Array.Empty<SerialPortOption>();
    private static DateTime _cachedAtUtc = DateTime.MinValue;

    public static SerialPortOption[] GetAvailablePorts(bool forceRefresh = false)
    {
        lock (CacheSync)
        {
            if (!forceRefresh
                && _cachedPorts.Length > 0
                && DateTime.UtcNow - _cachedAtUtc <= CacheLifetime)
            {
                return CloneOptions(_cachedPorts);
            }
        }

        var ports = SerialPort.GetPortNames();
        var metadata = GetMetadataByPortName(ports);

        var options = ports
            .Select(portName => BuildOption(portName, metadata))
            .OrderBy(option => GetPortOrder(option.PortName))
            .ThenBy(option => option.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (CacheSync)
        {
            _cachedPorts = options;
            _cachedAtUtc = DateTime.UtcNow;
        }

        return CloneOptions(options);
    }

    public static void InvalidateCache()
    {
        lock (CacheSync)
        {
            _cachedPorts = Array.Empty<SerialPortOption>();
            _cachedAtUtc = DateTime.MinValue;
        }
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

    private static Dictionary<string, PortMetadata> GetMetadataByPortName(IReadOnlyCollection<string> portNames)
    {
        var result = new Dictionary<string, PortMetadata>(StringComparer.OrdinalIgnoreCase);

        if (portNames.Count == 0)
        {
            return result;
        }

        var knownPorts = new HashSet<string>(portNames, StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, Description, Manufacturer, PNPDeviceID FROM Win32_SerialPort");

            foreach (var instance in searcher.Get().OfType<ManagementObject>())
            {
                var portName = instance["DeviceID"]?.ToString();
                var name = instance["Name"]?.ToString();

                if (string.IsNullOrWhiteSpace(portName))
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var match = PortNameRegex.Match(name);

                    if (!match.Success)
                    {
                        continue;
                    }

                    portName = match.Groups[1].Value.ToUpperInvariant();
                }

                if (!knownPorts.Contains(portName))
                {
                    continue;
                }

                result[portName] = new PortMetadata(
                    FriendlyName: ResolveFriendlyName(name, instance["Description"]?.ToString(), portName),
                    Manufacturer: instance["Manufacturer"]?.ToString() ?? string.Empty,
                    PnpDeviceId: instance["PNPDeviceID"]?.ToString() ?? string.Empty);
            }
        }
        catch
        {
        }

        var unresolvedPorts = knownPorts
            .Where(portName => !result.TryGetValue(portName, out var metadata) || IsFriendlyNameIncomplete(metadata.FriendlyName, portName))
            .ToArray();

        if (unresolvedPorts.Length == 0)
        {
            return result;
        }

        try
        {
            using var fallbackSearcher = new ManagementObjectSearcher(BuildPnPEntityQuery(unresolvedPorts));

            foreach (var instance in fallbackSearcher.Get().OfType<ManagementObject>())
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

                if (!knownPorts.Contains(portName))
                {
                    continue;
                }

                result[portName] = new PortMetadata(
                    FriendlyName: ResolveFriendlyName(name, instance["Description"]?.ToString(), portName),
                    Manufacturer: instance["Manufacturer"]?.ToString() ?? string.Empty,
                    PnpDeviceId: instance["PNPDeviceID"]?.ToString() ?? string.Empty);
            }
        }
        catch
        {
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

    private static SerialPortOption[] CloneOptions(IEnumerable<SerialPortOption> options)
    {
        return options
            .Select(option => new SerialPortOption
            {
                PortName = option.PortName,
                FriendlyName = option.FriendlyName,
                DetailText = option.DetailText,
            })
            .ToArray();
    }

    private static bool IsFriendlyNameIncomplete(string friendlyName, string portName)
    {
        return string.IsNullOrWhiteSpace(friendlyName)
            || string.Equals(friendlyName, portName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(friendlyName, "未识别设备", StringComparison.Ordinal);
    }

    private static string ResolveFriendlyName(string? name, string? description, string portName)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && !string.Equals(name, portName, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        if (!string.IsNullOrWhiteSpace(description)
            && !string.Equals(description, portName, StringComparison.OrdinalIgnoreCase))
        {
            return description;
        }

        return portName;
    }

    private static string BuildPnPEntityQuery(IEnumerable<string> portNames)
    {
        var conditions = portNames
            .Select(portName => $"Name LIKE '%({portName.ToUpperInvariant()})%'")
            .ToArray();

        return $"SELECT Name, Description, Manufacturer, PNPDeviceID FROM Win32_PnPEntity WHERE ({string.Join(" OR ", conditions)})";
    }

    private sealed record PortMetadata(string FriendlyName, string Manufacturer, string PnpDeviceId);
}