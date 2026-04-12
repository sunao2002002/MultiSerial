using System;
using System.IO;

namespace SerialApp.Desktop.Tests;

[CollectionDefinition(nameof(NonParallelCollection), DisableParallelization = true)]
public sealed class NonParallelCollection;

public sealed class TestEnvironmentScope : IDisposable
{
    private readonly string _originalHome;
    private readonly string _originalXdgDataHome;
    private readonly string _originalTmpDir;

    public TestEnvironmentScope()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "multiserial-tests", Guid.NewGuid().ToString("N"));
        HomeDirectory = Path.Combine(RootDirectory, "home");
        DataDirectory = Path.Combine(RootDirectory, "xdg-data");
        TempDirectory = Path.Combine(RootDirectory, "tmp");

        Directory.CreateDirectory(HomeDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(TempDirectory);

        _originalHome = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        _originalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? string.Empty;
        _originalTmpDir = Environment.GetEnvironmentVariable("TMPDIR") ?? string.Empty;

        Environment.SetEnvironmentVariable("HOME", HomeDirectory);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", DataDirectory);
        Environment.SetEnvironmentVariable("TMPDIR", TempDirectory);
    }

    public string RootDirectory { get; }

    public string HomeDirectory { get; }

    public string DataDirectory { get; }

    public string TempDirectory { get; }

    public string LocalApplicationDataDirectory => Path.Combine(DataDirectory, "SerialApp");

    public string SettingsFilePath => Path.Combine(LocalApplicationDataDirectory, "settings.json");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", string.IsNullOrEmpty(_originalHome) ? null : _originalHome);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", string.IsNullOrEmpty(_originalXdgDataHome) ? null : _originalXdgDataHome);
        Environment.SetEnvironmentVariable("TMPDIR", string.IsNullOrEmpty(_originalTmpDir) ? null : _originalTmpDir);

        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
