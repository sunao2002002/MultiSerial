using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialApp.Desktop.Services;

public sealed class PanelLogWriter : IAsyncDisposable
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private static readonly Encoding LogEncoding = new UTF8Encoding(true);
    private static readonly int PreambleByteCount = LogEncoding.GetPreamble().Length;

    private readonly int _panelIndex;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _baseDirectory;
    private string? _portName;
    private StreamWriter? _writer;
    private long _currentFileSizeBytes;

    public PanelLogWriter(int panelIndex, string baseDirectory, string? portName = null)
    {
        _panelIndex = panelIndex;
        _baseDirectory = baseDirectory;
        _portName = portName;
        FilePath = string.Empty;
    }

    public string FilePath { get; private set; }

    public bool HasActiveFile => _writer is not null;

    public event EventHandler? FilePathChanged;

    public async Task WriteLineAsync(string message)
    {
        await WriteLinesAsync(new[] { message });
    }

    public async Task WriteLinesAsync(IEnumerable<string> messages)
    {
        await _writeLock.WaitAsync();

        try
        {
            foreach (var message in messages)
            {
                EnsureWriterCreated();
                await RotateFileIfNeededAsync(message);
                await _writer!.WriteLineAsync(message);
                _currentFileSizeBytes += EstimateLineByteCount(message);
            }

            if (_writer is not null)
            {
                await _writer.FlushAsync();
            }
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
            _baseDirectory = baseDirectory;
            _portName = portName ?? _portName;

            if (_writer is null)
            {
                return;
            }

            await CloseWriterAsync();
            CreateWriter();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task CloseLogFileAsync()
    {
        await _writeLock.WaitAsync();

        try
        {
            await CloseWriterAsync();
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
            await CloseWriterAsync();
        }
        finally
        {
            _writeLock.Release();
            _writeLock.Dispose();
        }
    }

    private async Task RotateFileIfNeededAsync(string message)
    {
        if (_writer is null)
        {
            return;
        }

        var estimatedSize = EstimateLineByteCount(message);

        if (_currentFileSizeBytes > 0 && _currentFileSizeBytes + estimatedSize > MaxFileSizeBytes)
        {
            await CloseWriterAsync();
            CreateWriter();
        }
    }

    private async Task CloseWriterAsync()
    {
        if (_writer is null)
        {
            return;
        }

        await _writer.FlushAsync();
        _writer.Dispose();
        _writer = null;
        _currentFileSizeBytes = 0;
    }

    private void EnsureWriterCreated()
    {
        if (_writer is null)
        {
            CreateWriter();
        }
    }

    private void CreateWriter()
    {
        foreach (var candidateDirectory in GetCandidateDirectories(_baseDirectory))
        {
            try
            {
                Directory.CreateDirectory(candidateDirectory);

                var portSegment = string.IsNullOrWhiteSpace(_portName)
                    ? string.Empty
                    : $"-{SanitizeFileName(_portName)}";
                var fileName = $"panel-{_panelIndex:D2}{portSegment}-{DateTime.Now:yyyyMMdd-HHmmssfff}.log";
                var filePath = Path.Combine(candidateDirectory, fileName);

                var stream = new FileStream(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                _writer = new StreamWriter(stream, LogEncoding)
                {
                    AutoFlush = false,
                };

                _currentFileSizeBytes = 0;
                UpdateFilePath(filePath);
                return;
            }
            catch
            {
            }
        }

        throw new IOException("无法创建日志文件，请检查本机日志目录权限。");
    }

    private void UpdateFilePath(string filePath)
    {
        if (string.Equals(FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FilePath = filePath;
        FilePathChanged?.Invoke(this, EventArgs.Empty);
    }

    private long EstimateLineByteCount(string message)
    {
        var byteCount = LogEncoding.GetByteCount(message) + LogEncoding.GetByteCount(Environment.NewLine);

        if (_currentFileSizeBytes == 0)
        {
            byteCount += PreambleByteCount;
        }

        return byteCount;
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