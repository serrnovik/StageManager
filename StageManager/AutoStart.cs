using System;
using System.IO;
using Microsoft.Win32;

namespace StageManager
{
	public static class AutoStart
	{
		private const string REG_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

		public static void SetStartup(string appName, bool startup)
		{
			using var key = Registry.CurrentUser.CreateSubKey(REG_KEY, writable: true);
			SetStartupRegistration(key, appName, startup, GetAppPath(), GetLegacyStartupShortcutPath(appName));
		}

		public static bool IsStartup(string appName)
		{
			using var key = Registry.CurrentUser.OpenSubKey(REG_KEY);
			return HasStartupRegistration(key, appName, GetAppPath(), File.Exists(GetLegacyStartupShortcutPath(appName)));
		}

		public static bool IsStartup(RegistryKey? key, string appName) => GetValueAsString(key, appName).Equals(GetAppPath(), StringComparison.OrdinalIgnoreCase);

		internal static bool HasStartupRegistration(RegistryKey? key, string appName, string appPath, bool hasLegacyStartupShortcut) =>
			GetValueAsString(key, appName).Equals(appPath, StringComparison.OrdinalIgnoreCase) || hasLegacyStartupShortcut;

		internal static void SetStartupRegistration(RegistryKey? key, string appName, bool startup, string appPath, string legacyStartupShortcutPath)
		{
			if (startup)
				key?.SetValue(appName, appPath);
			else
			{
				key?.DeleteValue(appName, throwOnMissingValue: false);
				if (File.Exists(legacyStartupShortcutPath))
					File.Delete(legacyStartupShortcutPath);
			}
		}

		private static string GetValueAsString(RegistryKey? key, string appName) => key?.GetValue(appName)?.ToString() ?? "";

		private static string GetAppPath() => $@"""{Environment.ProcessPath}""";

		private static string GetLegacyStartupShortcutPath(string appName) =>
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{appName}.lnk");

	}
}
