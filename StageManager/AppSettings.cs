using Microsoft.Win32;

namespace StageManager
{
	public static class AppSettings
	{
		private const string SETTINGS_REG_KEY = @"SOFTWARE\StageManager";

		public static bool ReadBool(string name, bool defaultValue)
		{
			using var key = Registry.CurrentUser.OpenSubKey(SETTINGS_REG_KEY);
			var value = key?.GetValue(name)?.ToString();
			return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
		}

		public static void WriteBool(string name, bool value)
		{
			using var key = Registry.CurrentUser.CreateSubKey(SETTINGS_REG_KEY, writable: true);
			key?.SetValue(name, value.ToString());
		}

		public static string ReadString(string name, string defaultValue)
		{
			using var key = Registry.CurrentUser.OpenSubKey(SETTINGS_REG_KEY);
			return key?.GetValue(name)?.ToString() ?? defaultValue;
		}

		public static void WriteString(string name, string value)
		{
			using var key = Registry.CurrentUser.CreateSubKey(SETTINGS_REG_KEY, writable: true);
			key?.SetValue(name, value);
		}
	}
}
