using System;
using System.IO;
using System.Threading.Tasks;
using SerialApp.Desktop.Services;

namespace SerialApp.Desktop.Tests;

[Collection(nameof(NonParallelCollection))]
public sealed class PanelLogWriterTests
{
    [Fact]
    public async Task WriteLineAsync_CreatesLogFileAndWritesContent()
    {
        using var scope = new TestEnvironmentScope();
        var logDirectory = Path.Combine(scope.RootDirectory, "logs");
        await using var writer = new PanelLogWriter(3, logDirectory, "COM3");
        var changedCount = 0;
        writer.FilePathChanged += (_, _) => changedCount++;

        await writer.WriteLineAsync("hello logger");

        Assert.True(writer.HasActiveFile);
        Assert.Equal(1, changedCount);
        Assert.StartsWith(Path.GetFullPath(logDirectory), writer.FilePath, StringComparison.Ordinal);
        Assert.Contains("panel-03-COM3-", Path.GetFileName(writer.FilePath), StringComparison.Ordinal);
        Assert.Contains("hello logger", await File.ReadAllTextAsync(writer.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotateDirectoryAsync_CreatesNewFileInRequestedDirectoryWithSanitizedPortName()
    {
        using var scope = new TestEnvironmentScope();
        var firstDirectory = Path.Combine(scope.RootDirectory, "first");
        var secondDirectory = Path.Combine(scope.RootDirectory, "second");
        await using var writer = new PanelLogWriter(7, firstDirectory, "COM/7");

        await writer.WriteLineAsync("before rotate");
        var firstPath = writer.FilePath;

        await writer.RotateDirectoryAsync(secondDirectory, "tty/USB0");
        await writer.WriteLineAsync("after rotate");

        Assert.NotEqual(firstPath, writer.FilePath);
        Assert.StartsWith(Path.GetFullPath(secondDirectory), writer.FilePath, StringComparison.Ordinal);
        Assert.Contains("panel-07-COM_7-", Path.GetFileName(firstPath), StringComparison.Ordinal);
        Assert.Contains("panel-07-tty_USB0-", Path.GetFileName(writer.FilePath), StringComparison.Ordinal);
        Assert.Contains("after rotate", await File.ReadAllTextAsync(writer.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteLineAsync_InvalidRequestedDirectoryFallsBackToLocalApplicationData()
    {
        using var scope = new TestEnvironmentScope();
        await using var writer = new PanelLogWriter(1, "\0invalid");

        await writer.WriteLineAsync("fallback works");

        var expectedPrefix = Path.Combine(scope.LocalApplicationDataDirectory, "Logs");
        Assert.StartsWith(Path.GetFullPath(expectedPrefix), writer.FilePath, StringComparison.Ordinal);
        Assert.True(File.Exists(writer.FilePath));
    }

    [Fact]
    public async Task CloseLogFileAsync_ClosesCurrentWriterUntilNextWrite()
    {
        using var scope = new TestEnvironmentScope();
        var logDirectory = Path.Combine(scope.RootDirectory, "logs");
        await using var writer = new PanelLogWriter(9, logDirectory);

        await writer.WriteLineAsync("line one");
        var firstPath = writer.FilePath;

        await writer.CloseLogFileAsync();

        Assert.False(writer.HasActiveFile);

        await writer.WriteLineAsync("line two");

        Assert.True(writer.HasActiveFile);
        Assert.NotEqual(firstPath, writer.FilePath);
    }
}
