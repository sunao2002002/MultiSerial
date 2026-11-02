using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SerialApp.Desktop.Services;

public sealed class GitBashWindowHostService : IDisposable
{
    private const int GwlStyle = -16;
    private const nint WsChild = 0x40000000;
    private const nint WsVisible = 0x10000000;
    private const nint WsCaption = 0x00C00000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsMinimizeBox = 0x00020000;
    private const nint WsMaximizeBox = 0x00010000;
    private const nint WsSysMenu = 0x00080000;
    private static readonly nint WsPopup = unchecked((nint)0x80000000);
    private const int SwShow = 5;

    private Process? _process;
    private nint _hostHandle;
    private nint _embeddedWindowHandle;
    private bool _disposed;

    public event EventHandler? ProcessExited;

    public string? ExecutablePath { get; private set; }

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync(nint hostHandle, string workingDirectory, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();

        _hostHandle = hostHandle;
        var executablePath = FindMinttyExecutablePath();
        if (executablePath is null)
        {
            throw new FileNotFoundException("未找到 Git Bash。请确认已安装 Git for Windows。", "mintty.exe");
        }

        ExecutablePath = executablePath;
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "-e /usr/bin/bash --login -i",
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git Bash 启动失败。");
        _process.EnableRaisingEvents = true;
        _process.Exited += Process_Exited;

        try
        {
            _process.WaitForInputIdle(3000);
        }
        catch (InvalidOperationException)
        {
        }

        _embeddedWindowHandle = await WaitForMainWindowAsync(_process, cancellationToken);
        EmbedWindow(_embeddedWindowHandle, _hostHandle);
    }

    public void Resize(int width, int height)
    {
        if (_embeddedWindowHandle == nint.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        MoveWindow(_embeddedWindowHandle, 0, 0, width, height, true);
    }

    public void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            _process.Exited -= Process_Exited;

            if (!_process.HasExited)
            {
                _process.CloseMainWindow();

                if (!_process.WaitForExit(1200))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _embeddedWindowHandle = nint.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        _embeddedWindowHandle = nint.Zero;
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    private static void EmbedWindow(nint childHandle, nint hostHandle)
    {
        var style = GetWindowLongPtr(childHandle, GwlStyle);
        style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu | WsPopup);
        style |= WsChild | WsVisible;

        SetWindowLongPtr(childHandle, GwlStyle, style);
        SetParent(childHandle, hostHandle);
        ShowWindow(childHandle, SwShow);
    }

    private static async Task<nint> WaitForMainWindowAsync(Process process, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();

            if (process.HasExited)
            {
                throw new InvalidOperationException("Git Bash 进程已退出。");
            }

            if (process.MainWindowHandle != nint.Zero)
            {
                return process.MainWindowHandle;
            }

            var windowHandle = FindTopLevelWindowForProcess(process.Id);
            if (windowHandle != nint.Zero)
            {
                return windowHandle;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("等待 Git Bash 窗口创建超时。");
    }

    private static nint FindTopLevelWindowForProcess(int processId)
    {
        nint matchingWindowHandle = nint.Zero;

        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out var ownerProcessId);
            if (ownerProcessId == processId && IsWindowVisible(windowHandle))
            {
                matchingWindowHandle = windowHandle;
                return false;
            }

            return true;
        }, nint.Zero);

        return matchingWindowHandle;
    }

    private static string? FindMinttyExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "mintty.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "usr", "bin", "mintty.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "mintty.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "mintty.exe"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? FindExecutableOnPath("mintty.exe");
    }

    private static string? FindExecutableOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var path in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(path, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static nint GetWindowLongPtr(nint windowHandle, int index)
    {
        return nint.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : GetWindowLongPtr32(windowHandle, index);
    }

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint newLong)
    {
        return nint.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newLong)
            : SetWindowLongPtr32(windowHandle, index, newLong);
    }

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out int processId);

    [DllImport("user32.dll")]
    private static extern nint SetParent(nint childHandle, nint newParentHandle);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(nint windowHandle, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint windowHandle, int commandShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLongPtr32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLongPtr32(nint windowHandle, int index, nint newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint newLong);
}