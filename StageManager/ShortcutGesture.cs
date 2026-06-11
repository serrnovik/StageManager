using System;
using System.Linq;
using System.Windows.Input;

namespace StageManager
{
	public sealed class ShortcutGesture
	{
		public static readonly ShortcutGesture None = new ShortcutGesture(Key.None, false, false, false, false);

		public ShortcutGesture(Key key, bool control, bool alt, bool shift, bool windows)
		{
			Key = NormalizeKey(key);
			Control = control;
			Alt = alt;
			Shift = shift;
			Windows = windows;
		}

		public Key Key { get; }

		public bool Control { get; }

		public bool Alt { get; }

		public bool Shift { get; }

		public bool Windows { get; }

		public bool IsEmpty => Key == Key.None;

		public string DisplayText
		{
			get
			{
				if (IsEmpty)
					return "None";

				var parts = new[]
				{
					Control ? "Ctrl" : null,
					Alt ? "Alt" : null,
					Shift ? "Shift" : null,
					Windows ? "Win" : null,
					KeyToText(Key)
				};

				return string.Join("+", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
			}
		}

		public static ShortcutGesture FromKeyboard(Key key, ModifierKeys modifiers)
		{
			key = NormalizeKey(key);
			if (key == Key.None || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
				return None;

			return new ShortcutGesture(
				key,
				modifiers.HasFlag(ModifierKeys.Control),
				modifiers.HasFlag(ModifierKeys.Alt),
				modifiers.HasFlag(ModifierKeys.Shift),
				modifiers.HasFlag(ModifierKeys.Windows));
		}

		public static ShortcutGesture Parse(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return None;

			var control = false;
			var alt = false;
			var shift = false;
			var windows = false;
			var key = Key.None;

			foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var part = rawPart.Trim();
				if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
					control = true;
				else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
					alt = true;
				else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
					shift = true;
				else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
					windows = true;
				else if (Enum.TryParse(part, ignoreCase: true, out Key parsedKey))
					key = NormalizeKey(parsedKey);
			}

			return key == Key.None ? None : new ShortcutGesture(key, control, alt, shift, windows);
		}

		public override string ToString() => DisplayText;

		private static Key NormalizeKey(Key key) => key == Key.System ? Keyboard.PrimaryDevice?.ActiveSource is object ? Key.System : key : key;

		private static string KeyToText(Key key) => key switch
		{
			Key.Return => "Enter",
			Key.Escape => "Esc",
			Key.Prior => "PageUp",
			Key.Next => "PageDown",
			_ => key.ToString()
		};
	}
}
