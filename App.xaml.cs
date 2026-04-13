using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SerialApp.Desktop;

public partial class App : System.Windows.Application
{
	private readonly string _startupLogPath = Path.Combine(
		AppDomain.CurrentDomain.BaseDirectory,
		"startup.log");

	protected override void OnStartup(StartupEventArgs e)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_startupLogPath)!);
		Log("Application startup begin.");

		DispatcherUnhandledException += App_DispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

		try
		{
			ShutdownMode = ShutdownMode.OnMainWindowClose;
			base.OnStartup(e);

			var mainWindow = new MainWindow
			{
				WindowStartupLocation = WindowStartupLocation.CenterScreen,
				ShowInTaskbar = true,
			};

			MainWindow = mainWindow;
			mainWindow.Show();
			mainWindow.Activate();
			Log("Main window shown.");
		}
		catch (Exception ex)
		{
			Log($"Startup failure: {ex}");
			System.Windows.MessageBox.Show(
				$"应用启动失败，请查看日志：{_startupLogPath}\n\n{ex.Message}",
				"Serial App",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
			Shutdown(-1);
		}
	}

	private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		Log($"Dispatcher exception: {e.Exception}");
	}

	private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		Log($"Domain exception: {e.ExceptionObject}");
	}

	private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		Log($"Task exception: {e.Exception}");
	}

	private void Log(string message)
	{
		File.AppendAllText(_startupLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
	}
}

