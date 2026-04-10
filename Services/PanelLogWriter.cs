using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialApp.Desktop.Services;

public sealed class PanelLogWriter : IAsyncDisposable
{
    private static readonly Encoding LogEncoding = new UTF8Encoding(true);

    private readonly int _panelIndex;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _portName;
    private StreamWriter _writer;

    public PanelLogWriter(int panelIndex, string baseDirectory, string? portName = null)
    {
        _panelIndex = panelIndex;
        _portName = portName;
        _writer = CreateWriter(baseDirectory, out var filePath);
        FilePath = filePath;
    }

    public string FilePath { get; private set; }

    public async Task WriteLineAsync(string message)
    {
        await _writeLock.WaitAsync();

        try
        {
            await _writer.WriteLineAsync(message);
            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task WriteLinesAsync(IEnumerable<string> messages)
    {
        await _writeLock.WaitAsync();

        try
        {
            foreach (var message in messages)
            {
                await _writer.WriteLineAsync(message);
            }

            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task WriteBatchAsync(Func<TextWriter, Task> writeBatchAsync)
    {
        ArgumentNullException.ThrowIfNull(writeBatchAsync);

        await _writeLock.WaitAsync();

        try
        {
            await writeBatchAsync(_writer);
            await _writer.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RotateDirectoryAsync(string baseDirectory, string? portName = null)
    {
        await _writeLock.WaitAsync();

        try
        {
            _portName = portName ?? _portName;
            var nextWriter = CreateWriter(baseDirectory, out var filePath);
            await _writer.FlushAsync();
            _writer.Dispose();
            _writer = nextWriter;
            FilePath = filePath;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writeLock.WaitAsync();

        try
        {
            await _writer.FlushAsync();
            _writer.Dispose();
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }

    private StreamWriter CreateWriter(string baseDirectory, out string filePath)
    {
        foreach (var candidateDirectory in GetCandidateDirectories(baseDirectory))
        {
            try
            {
                Directory.CreateDirectory(candidateDirectory);

                var portSegment = string.IsNullOrWhiteSpace(_portName)
                    ? string.Empty
                    : $"-{SanitizeFileName(_portName)}";
                var fileName = $"panel-{_panelIndex:D2}{portSegment}-{DateTime.Now:yyyyMMdd-HHmmssfff}.log";
                filePath = Path.Combine(candidateDirectory, fileName);

                var stream = new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return new StreamWriter(stream, LogEncoding)
                {
                    AutoFlush = false,
                };
            }
            catch
            {
            }
        }

        throw new IOException("无法创建日志文件，请检查本机日志目录权限。");
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static IEnumerable<string> GetCandidateDirectories(string requestedDirectory)
    {
        if (!string.IsNullOrWhiteSpace(requestedDirectory))
        {
            yield return requestedDirectory;
        }

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SerialApp",
            "Logs");

        yield return Path.Combine(Path.GetTempPath(), "SerialApp", "Logs");
    }
}