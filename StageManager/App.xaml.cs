using AsyncAwaitBestPractices;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private const long MAX_LOG_SIZE_BYTES = 1024 * 1024;
		private static readonly object LogLock = new object();

		/// <summary>
		/// The active scene manager, used to restore minimized windows if the app goes down unexpectedly
		/// </summary>
		internal static SceneManager? SceneManager { get; set; }

		internal static string LogFilePath { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"StageManager",
			"StageManager.log");

		protected override void OnStartup(StartupEventArgs e)
		{
			// Stage Manager minimizes other apps' windows, so it must never vanish silently and leave them minimized
			DispatcherUnhandledException += (_, args) =>
			{
				Log(args.Exception);
				args.Handled = true;
			};

			AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			{
				Log(args.ExceptionObject as Exception);
				if (args.IsTerminating)
					RestoreWindows();
			};

			TaskScheduler.UnobservedTaskException += (_, args) =>
			{
				Log(args.Exception);
				args.SetObserved();
			};

			SafeFireAndForgetExtensions.SetDefaultExceptionHandling(Log);

			base.OnStartup(e);
		}

		private static void RestoreWindows()
		{
			try
			{
				SceneManager?.RestoreWindowsAfterCrash();
			}
			catch (Exception ex)
			{
				Log(ex);
			}
		}

		internal static void Log(Exception? exception)
		{
			if (exception is null)
				return;

			try
			{
				lock (LogLock)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);

					var file = new FileInfo(LogFilePath);
					if (file.Exists && file.Length > MAX_LOG_SIZE_BYTES)
						file.Delete();

					File.AppendAllText(LogFilePath, $"[{DateTime.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
				}
			}
			catch
			{
				// logging must never take the app down
			}
		}
	}
}
