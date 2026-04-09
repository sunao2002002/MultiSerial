using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SerialApp.Desktop.Models;

namespace SerialApp.Desktop.Services;

public sealed class AppStateService
{
    private const int MaxHistoryCount = 16;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _settingsFilePath;
    private string _logDirectory;

    public AppStateService()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SerialApp");

        Directory.CreateDirectory(appDirectory);

        DefaultLogDirectory = Path.Combine(appDirectory, "Logs");
        _settingsFilePath = Path.Combine(appDirectory, "settings.json");

        var preferences = LoadPreferences();
        _logDirectory = ResolveWritableDirectory(preferences.LogDirectory, out var warningMessage);
        LastWarning = warningMessage;
        RecentSendHistory = new ObservableCollection<string>(preferences.RecentSendHistory.Take(MaxHistoryCount));
    }

    public string DefaultLogDirectory { get; }

    public string LogDirectory => _logDirectory;

    public string? LastWarning { get; private set; }

    public ObservableCollection<string> RecentSendHistory { get; }

    public bool SetLogDirectory(string directory)
    {
        var normalizedDirectory = ResolveWritableDirectory(directory, out var warningMessage);

        if (string.Equals(_logDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            LastWarning = warningMessage;
            return false;
        }

        _logDirectory = normalizedDirectory;
        LastWarning = warningMessage;
        _ = SaveAsync();
        return true;
    }

    public void RememberSend(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var existing = RecentSendHistory.FirstOrDefault(item => string.Equals(item, content, StringComparison.Ordinal));

        if (existing is not null)
        {
            RecentSendHistory.Remove(existing);
        }

        RecentSendHistory.Insert(0, content);

        while (RecentSendHistory.Count > MaxHistoryCount)
        {
            RecentSendHistory.RemoveAt(RecentSendHistory.Count - 1);
        }

        _ = SaveAsync();
    }

    public Task FlushAsync()
    {
        return SaveAsync();
    }

    private AppPreferences LoadPreferences()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new AppPreferences { LogDirectory = DefaultLogDirectory };
        }

        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences { LogDirectory = DefaultLogDirectory };
        }
        catch
        {
            return new AppPreferences { LogDirectory = DefaultLogDirectory };
        }
    }

    private async Task SaveAsync()
    {
        var snapshot = new AppPreferences
        {
            LogDirectory = _logDirectory,
            RecentSendHistory = RecentSendHistory.ToList(),
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        await _saveLock.WaitAsync();

        try
        {
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private string ResolveWritableDirectory(string? directory, out string? warningMessage)
    {
        warningMessage = null;

        if (TryPrepareDirectory(directory, out var resolvedDirectory))
        {
            return resolvedDirectory;
        }

        if (!string.IsNullOrWhiteSpace(directory))
        {
            warningMessage = $"日志目录不可用，已回退到默认目录: {directory}";
        }

        if (TryPrepareDirectory(DefaultLogDirectory, out resolvedDirectory))
        {
            return resolvedDirectory;
        }

        resolvedDirectory = Path.Combine(Path.GetTempPath(), "SerialApp", "Logs");
        Directory.CreateDirectory(resolvedDirectory);
        warningMessage = warningMessage is null
            ? $"默认日志目录不可用，已回退到临时目录: {resolvedDirectory}"
            : $"{warningMessage}; 默认目录也不可用，已回退到临时目录: {resolvedDirectory}";
        return resolvedDirectory;
    }

    private bool TryPrepareDirectory(string? directory, out string resolvedDirectory)
    {
        resolvedDirectory = string.Empty;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            resolvedDirectory = Path.GetFullPath(directory);
            Directory.CreateDirectory(resolvedDirectory);

            var probePath = Path.Combine(resolvedDirectory, ".write-test");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch
        {
            resolvedDirectory = string.Empty;
            return false;
        }
    }
}