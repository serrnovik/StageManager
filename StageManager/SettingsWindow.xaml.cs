using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StageManager
{
	public partial class SettingsWindow : Window
	{
		private readonly MainWindow _mainWindow;

		public SettingsWindow(MainWindow mainWindow)
		{
			InitializeComponent();
			_mainWindow = mainWindow;
			Owner = mainWindow;
			LoadCurrentSettings();
		}

		private void LoadCurrentSettings()
		{
			SetShortcut(ToggleStageManagerShortcutBox, _mainWindow.ToggleStageManagerShortcut);
			SetShortcut(ToggleAutoHideShortcutBox, _mainWindow.ToggleAutoHideShortcut);
			SetShortcut(RevealStageManagerShortcutBox, _mainWindow.RevealStageManagerShortcut);
			AutoHideCheckBox.IsChecked = _mainWindow.AutoHideStageManagerIcons;
		}

		private void ShortcutBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
			if (sender is TextBox textBox)
				textBox.SelectAll();
		}

		private void ShortcutBox_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (sender is not TextBox textBox)
				return;

			e.Handled = true;

			var key = e.Key == Key.System ? e.SystemKey : e.Key;
			var shortcut = ShortcutGesture.FromKeyboard(key, Keyboard.Modifiers);
			if (shortcut.IsEmpty)
				return;

			SetShortcut(textBox, shortcut);
		}

		private void DefaultsButton_Click(object sender, RoutedEventArgs e)
		{
			SetShortcut(ToggleStageManagerShortcutBox, MainWindow.DefaultToggleStageManagerShortcut);
			SetShortcut(ToggleAutoHideShortcutBox, MainWindow.DefaultToggleAutoHideShortcut);
			SetShortcut(RevealStageManagerShortcutBox, MainWindow.DefaultRevealStageManagerShortcut);
			AutoHideCheckBox.IsChecked = true;
		}

		private void SaveButton_Click(object sender, RoutedEventArgs e)
		{
			_mainWindow.UpdateShortcutSettings(
				GetShortcut(ToggleStageManagerShortcutBox),
				GetShortcut(ToggleAutoHideShortcutBox),
				GetShortcut(RevealStageManagerShortcutBox),
				AutoHideCheckBox.IsChecked == true);

			Close();
		}

		private void CancelButton_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private static void SetShortcut(TextBox textBox, ShortcutGesture shortcut)
		{
			textBox.Tag = shortcut;
			textBox.Text = shortcut.DisplayText;
		}

		private static ShortcutGesture GetShortcut(TextBox textBox) => textBox.Tag as ShortcutGesture ?? ShortcutGesture.None;
	}
}
