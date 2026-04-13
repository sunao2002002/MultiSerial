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
    private const int MaxHistoryCount = 32;
    private const string DefaultPanelFontFamily = "Consolas";
    private const double DefaultPanelFontSize = 13d;

    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _settingsFilePath;
    private string _logDirectory;
    private PanelFontSettings _panelFontSettings;

    public AppStateService()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

        DefaultLogDirectory = Path.Combine(appDirectory, "Logs");
        _settingsFilePath = Path.Combine(appDirectory, "settings.json");

        var preferences = LoadPreferences();
        _logDirectory = ResolveWritableDirectory(preferences.LogDirectory, out var warningMessage);
        _panelFontSettings = NormalizePanelFontSettings(preferences.PanelFontSettings);
        LastWarning = warningMessage;
        RecentSendHistory = new ObservableCollection<string>(preferences.RecentSendHistory.Take(MaxHistoryCount));
    }

    public bool IsFirstRun => !File.Exists(_settingsFilePath);

    public string DefaultLogDirectory { get; }

    public string LogDirectory => _logDirectory;

    public string? LastWarning { get; private set; }

    public ObservableCollection<string> RecentSendHistory { get; }

    public event EventHandler? PanelFontSettingsChanged;

    public PanelFontSettings PanelFontSettings => ClonePanelFontSettings(_panelFontSettings);

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

    public bool RemoveSendHistory(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var existing = RecentSendHistory.FirstOrDefault(item => string.Equals(item, content, StringComparison.Ordinal));

        if (existing is null)
        {
            return false;
        }

        RecentSendHistory.Remove(existing);
        _ = SaveAsync();
        return true;
    }

    public bool SetPanelFontSettings(PanelFontSettings settings)
    {
        var normalizedSettings = NormalizePanelFontSettings(settings);

        if (AreSamePanelFontSettings(_panelFontSettings, normalizedSettings))
        {
            return false;
        }

        _panelFontSettings = normalizedSettings;
        _ = SaveAsync();
        PanelFontSettingsChanged?.Invoke(this, EventArgs.Empty);
        return true;
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
            PanelFontSettings = ClonePanelFontSettings(_panelFontSettings),
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

    private static PanelFontSettings NormalizePanelFontSettings(PanelFontSettings? settings)
    {
        if (settings is null)
        {
            return new PanelFontSettings();
        }

        return new PanelFontSettings
        {
            FamilyName = string.IsNullOrWhiteSpace(settings.FamilyName) ? DefaultPanelFontFamily : settings.FamilyName.Trim(),
            Size = settings.Size > 0d ? settings.Size : DefaultPanelFontSize,
            Bold = settings.Bold,
            Italic = settings.Italic,
        };
    }

    private static PanelFontSettings ClonePanelFontSettings(PanelFontSettings settings)
    {
        return new PanelFontSettings
        {
            FamilyName = settings.FamilyName,
            Size = settings.Size,
            Bold = settings.Bold,
            Italic = settings.Italic,
        };
    }

    private static bool AreSamePanelFontSettings(PanelFontSettings left, PanelFontSettings right)
    {
        return string.Equals(left.FamilyName, right.FamilyName, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(left.Size - right.Size) < 0.1d
            && left.Bold == right.Bold
            && left.Italic == right.Italic;
    }
}