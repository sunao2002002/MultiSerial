using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SerialApp.Desktop.Models;
using SerialApp.Desktop.Services;

namespace SerialApp.Desktop.Tests;

[Collection(nameof(NonParallelCollection))]
public sealed class AppStateServiceTests
{
    [Fact]
    public async Task RememberSend_PersistsHistoryWithDeduplicationAndLimit()
    {
        using var scope = new TestEnvironmentScope();
        var service = new AppStateService();

        foreach (var value in Enumerable.Range(0, 35).Select(index => $"cmd-{index:D2}"))
        {
            service.RememberSend(value);
        }

        service.RememberSend("cmd-10");
        await service.FlushAsync();

        Assert.Equal(32, service.RecentSendHistory.Count);
        Assert.Equal("cmd-10", service.RecentSendHistory[0]);
        Assert.DoesNotContain("cmd-00", service.RecentSendHistory);
        Assert.DoesNotContain("cmd-01", service.RecentSendHistory);
        Assert.Single(service.RecentSendHistory, item => item == "cmd-10");

        var saved = await LoadPreferencesAsync(scope.SettingsFilePath);
        Assert.Equal(service.LogDirectory, saved.LogDirectory);
        Assert.Equal(service.RecentSendHistory, saved.RecentSendHistory);
    }

    [Fact]
    public async Task Constructor_LoadsSavedPreferencesAndNormalizesMissingFontSettings()
    {
        using var scope = new TestEnvironmentScope();
        Directory.CreateDirectory(scope.LocalApplicationDataDirectory);
        var configuredDirectory = Path.Combine(scope.RootDirectory, "configured-logs");
        var preferencesJson = """
            {
              "LogDirectory": "__LOG_DIR__",
              "PanelFontSettings": null,
              "RecentSendHistory": ["ping", "pong"]
            }
            """.Replace("__LOG_DIR__", configuredDirectory.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal);
        await File.WriteAllTextAsync(scope.SettingsFilePath, preferencesJson);

        var service = new AppStateService();

        Assert.Equal(Path.GetFullPath(configuredDirectory), service.LogDirectory);
        Assert.Equal(new[] { "ping", "pong" }, service.RecentSendHistory);
        Assert.Equal("Consolas", service.PanelFontSettings.FamilyName);
        Assert.Equal(13d, service.PanelFontSettings.Size);
    }

    [Fact]
    public void Constructor_InvalidSettingsFileFallsBackToDefaults()
    {
        using var scope = new TestEnvironmentScope();
        Directory.CreateDirectory(scope.LocalApplicationDataDirectory);
        File.WriteAllText(scope.SettingsFilePath, "{ invalid json");

        var service = new AppStateService();

        Assert.Equal(service.DefaultLogDirectory, service.LogDirectory);
        Assert.Empty(service.RecentSendHistory);
        Assert.Null(service.LastWarning);
    }

    [Fact]
    public async Task SetLogDirectory_InvalidPathFallsBackToDefaultAndStoresWarning()
    {
        using var scope = new TestEnvironmentScope();
        var service = new AppStateService();
        var customDirectory = Path.Combine(scope.RootDirectory, "custom-logs");

        Assert.True(service.SetLogDirectory(customDirectory));
        Assert.Equal(Path.GetFullPath(customDirectory), service.LogDirectory);

        Assert.True(service.SetLogDirectory("\0invalid"));
        await service.FlushAsync();

        Assert.Equal(service.DefaultLogDirectory, service.LogDirectory);
        Assert.Contains("已回退到默认目录", service.LastWarning, StringComparison.Ordinal);

        var saved = await LoadPreferencesAsync(scope.SettingsFilePath);
        Assert.Equal(service.DefaultLogDirectory, saved.LogDirectory);
    }

    [Fact]
    public void SetLogDirectory_SameNormalizedDirectoryReturnsFalse()
    {
        using var scope = new TestEnvironmentScope();
        var service = new AppStateService();
        var equivalentPath = Path.Combine(service.LogDirectory, ".");

        Assert.False(service.SetLogDirectory(equivalentPath));
        Assert.Null(service.LastWarning);
    }

    [Fact]
    public void SetPanelFontSettings_NormalizesValuesRaisesEventAndReturnsClone()
    {
        using var scope = new TestEnvironmentScope();
        var service = new AppStateService();
        var changedCount = 0;
        service.PanelFontSettingsChanged += (_, _) => changedCount++;

        Assert.True(service.SetPanelFontSettings(new PanelFontSettings
        {
            FamilyName = "  ",
            Size = -5d,
            Bold = true,
            Italic = true,
        }));

        var snapshot = service.PanelFontSettings;
        Assert.Equal("Consolas", snapshot.FamilyName);
        Assert.Equal(13d, snapshot.Size);
        Assert.True(snapshot.Bold);
        Assert.True(snapshot.Italic);
        Assert.Equal(1, changedCount);

        snapshot.FamilyName = "Mutated";
        snapshot.Size = 99d;
        Assert.Equal("Consolas", service.PanelFontSettings.FamilyName);
        Assert.Equal(13d, service.PanelFontSettings.Size);

        Assert.False(service.SetPanelFontSettings(new PanelFontSettings
        {
            FamilyName = "consolas",
            Size = 13.05d,
            Bold = true,
            Italic = true,
        }));
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void RememberSend_IgnoresBlankValues()
    {
        using var scope = new TestEnvironmentScope();
        var service = new AppStateService();

        service.RememberSend("   ");

        Assert.Empty(service.RecentSendHistory);
    }

    [Fact]
    public void RemoveSendHistory_RejectsBlankValuesAndRemovesMatchingEntry()
    {
        using var scope = new TestEnvironmentScope();
        var service = new AppStateService();
        service.RememberSend("alpha");
        service.RememberSend("beta");

        Assert.False(service.RemoveSendHistory(" "));
        Assert.True(service.RemoveSendHistory("alpha"));
        Assert.False(service.RemoveSendHistory("missing"));
        Assert.DoesNotContain("alpha", service.RecentSendHistory);
    }

    private static async Task<AppPreferences> LoadPreferencesAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<AppPreferences>(json)
            ?? throw new InvalidOperationException("Failed to deserialize saved settings.");
    }
}
