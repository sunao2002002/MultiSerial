using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialApp.Desktop.Services;

public sealed class MemoryDiagnosticsService
{
    private const string SnapshotFileName = "memory-snapshots.log";

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _logDirectory;

    public MemoryDiagnosticsService(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public string SnapshotFilePath => Path.Combine(_logDirectory, SnapshotFileName);

    public void UpdateLogDirectory(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public async Task<MemorySnapshotResult> CaptureSnapshotAsync(
        string trigger,
        bool compactBeforeCapture,
        int panelCount,
        PanelMemorySnapshot[] panels)
    {
        if (compactBeforeCapture)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var gcInfo = GC.GetGCMemoryInfo();
        var timestamp = DateTime.Now;
        var managedTotalBytes = GC.GetTotalMemory(forceFullCollection: false);
        var totalVisibleCharacters = panels.Sum(panel => panel.VisibleCharacterCount);
        var totalPendingBytes = panels.Sum(panel => panel.PendingReceiveBytes);
        var totalDroppedBytes = panels.Sum(panel => panel.DroppedReceiveBytes);

        var snapshot = new MemorySnapshot(
            timestamp,
            trigger,
            compactBeforeCapture,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.PagedMemorySize64,
            process.VirtualMemorySize64,
            managedTotalBytes,
            gcInfo.HeapSizeBytes,
            gcInfo.FragmentedBytes,
            gcInfo.TotalCommittedBytes,
            gcInfo.MemoryLoadBytes,
            gcInfo.HighMemoryLoadThresholdBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            panelCount,
            totalVisibleCharacters,
            totalPendingBytes,
            totalDroppedBytes,
            panels);

        var details = BuildDetails(snapshot);
        await AppendSnapshotAsync(details);

        return new MemorySnapshotResult(
            BuildSummary(snapshot),
            details,
            SnapshotFilePath);
    }

    private async Task AppendSnapshotAsync(string details)
    {
        var directory = Path.GetDirectoryName(SnapshotFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await _writeLock.WaitAsync();

        try
        {
            await File.AppendAllTextAsync(SnapshotFilePath, details + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string BuildSummary(MemorySnapshot snapshot)
    {
        var mode = snapshot.CompactedBeforeCapture ? "GC后" : "即时";
        return string.Format(
            CultureInfo.InvariantCulture,
            "内存快照 {0:HH:mm:ss} {1} | WS {2} | Private {3} | Heap {4} | Panels {5} | Visible {6:N0} chars | Pending {7}",
            snapshot.Timestamp,
            mode,
            FormatMiB(snapshot.WorkingSetBytes),
            FormatMiB(snapshot.PrivateBytes),
            FormatMiB(snapshot.HeapSizeBytes),
            snapshot.PanelCount,
            snapshot.TotalVisibleCharacters,
            FormatMiB(snapshot.TotalPendingBytes));
    }

    private static string BuildDetails(MemorySnapshot snapshot)
    {
        var builder = new StringBuilder(1024 + snapshot.Panels.Length * 128);
        builder.AppendLine($"[{snapshot.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {snapshot.Trigger} {(snapshot.CompactedBeforeCapture ? "(GC compact)" : "(live)")}");
        builder.AppendLine($"WorkingSet: {FormatMiB(snapshot.WorkingSetBytes)}");
        builder.AppendLine($"Private: {FormatMiB(snapshot.PrivateBytes)}");
        builder.AppendLine($"Paged: {FormatMiB(snapshot.PagedBytes)}");
        builder.AppendLine($"Virtual: {FormatMiB(snapshot.VirtualBytes)}");
        builder.AppendLine($"ManagedTotal: {FormatMiB(snapshot.ManagedTotalBytes)}");
        builder.AppendLine($"HeapSize: {FormatMiB(snapshot.HeapSizeBytes)}");
        builder.AppendLine($"Fragmented: {FormatMiB(snapshot.FragmentedBytes)}");
        builder.AppendLine($"Committed: {FormatMiB(snapshot.CommittedBytes)}");
        builder.AppendLine($"MemoryLoad: {FormatMiB(snapshot.MemoryLoadBytes)} / {FormatMiB(snapshot.HighMemoryLoadThresholdBytes)}");
        builder.AppendLine($"GC Collections: gen0={snapshot.Gen0Collections}, gen1={snapshot.Gen1Collections}, gen2={snapshot.Gen2Collections}");
        builder.AppendLine($"Panels: {snapshot.PanelCount}, VisibleChars={snapshot.TotalVisibleCharacters:N0}, PendingBytes={snapshot.TotalPendingBytes:N0}, DroppedBytes={snapshot.TotalDroppedBytes:N0}");

        if (snapshot.Panels.Length > 0)
        {
            builder.AppendLine("Panel details:");

            foreach (var panel in snapshot.Panels)
            {
                builder.Append("  - ");
                builder.Append($"Panel {panel.PanelIndex:D2}");
                builder.Append(panel.IsActive ? " active" : " idle");
                builder.Append(panel.IsConnected ? " connected" : " disconnected");
                builder.Append($", visible={panel.VisibleCharacterCount:N0}");
                builder.Append($", pending={panel.PendingReceiveBytes:N0}");
                builder.Append($", dropped={panel.DroppedReceiveBytes:N0}");

                if (!string.IsNullOrWhiteSpace(panel.SelectedPortName))
                {
                    builder.Append($", port={panel.SelectedPortName}");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatMiB(long bytes)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:N2} MiB", bytes / 1024d / 1024d);
    }
}

public sealed record PanelMemorySnapshot(
    int PanelIndex,
    bool IsActive,
    bool IsConnected,
    int VisibleCharacterCount,
    long PendingReceiveBytes,
    long DroppedReceiveBytes,
    string SelectedPortName);

public sealed record MemorySnapshotResult(string Summary, string Details, string FilePath);

internal sealed record MemorySnapshot(
    DateTime Timestamp,
    string Trigger,
    bool CompactedBeforeCapture,
    long WorkingSetBytes,
    long PrivateBytes,
    long PagedBytes,
    long VirtualBytes,
    long ManagedTotalBytes,
    long HeapSizeBytes,
    long FragmentedBytes,
    long CommittedBytes,
    long MemoryLoadBytes,
    long HighMemoryLoadThresholdBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int PanelCount,
    int TotalVisibleCharacters,
    long TotalPendingBytes,
    long TotalDroppedBytes,
    PanelMemorySnapshot[] Panels);