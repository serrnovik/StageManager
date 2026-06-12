using AsyncAwaitBestPractices;
using Microsoft.Xaml.Behaviors.Core;
using SharpHook;
using StageManager.Model;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Interop;
using StageManager.Native.Window;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window, INotifyPropertyChanged
	{
		private const int TIMERINTERVAL_MILLISECONDS = 500;
		private const int MIN_VISIBLE_SCENES = 1;
		private const int FALLBACK_VISIBLE_SCENES = 6;
		private const int MAX_OVERFLOW_GROUPS = 4;
		private const int OVERFLOW_TARGET_GROUP_SIZE = 3;
		private const double SCENE_SLOT_HEIGHT = 184.0;
		private const double BOTTOM_WORK_AREA_GUARD = 36.0;
		private const string APP_NAME = "StageManager";
		private const string AUTO_HIDE_ICONS_VALUE = "AutoHideStageManagerIcons";
		private const string TOGGLE_STAGE_MANAGER_SHORTCUT_VALUE = "ToggleStageManagerShortcut";
		private const string TOGGLE_AUTO_HIDE_SHORTCUT_VALUE = "ToggleAutoHideShortcut";
		private const string REVEAL_STAGE_MANAGER_SHORTCUT_VALUE = "RevealStageManagerShortcut";
		private const int WM_HOTKEY = 0x0312;
		private const int TOGGLE_STAGE_MANAGER_HOTKEY_ID = 1001;
		private const int TOGGLE_AUTO_HIDE_HOTKEY_ID = 1002;
		private const int REVEAL_STAGE_MANAGER_HOTKEY_ID = 1003;
		private const uint MOD_ALT = 0x0001;
		private const uint MOD_CONTROL = 0x0002;
		private const uint MOD_SHIFT = 0x0004;
		private const uint MOD_WIN = 0x0008;
		private const uint MOD_NOREPEAT = 0x4000;
		private static readonly TimeSpan RevealDuration = TimeSpan.FromSeconds(4);
		public static readonly ShortcutGesture DefaultToggleStageManagerShortcut = ShortcutGesture.Parse("Ctrl+F11");
		public static readonly ShortcutGesture DefaultToggleAutoHideShortcut = ShortcutGesture.Parse("Ctrl+Alt+F11");
		public static readonly ShortcutGesture DefaultRevealStageManagerShortcut = ShortcutGesture.Parse("Ctrl+Shift+F11");
		private IntPtr _thisHandle;
		private HwndSource? _hwndSource;
		private TaskPoolGlobalHook? _hook;
		private WindowMode _mode;
		private double _lastWidth;
		private int _visibleSceneCapacity = FALLBACK_VISIBLE_SCENES;
		private Timer _overlapCheckTimer;
		private Point _mouse = new Point(0, 0);
		private SceneModel? _removedCurrentScene;
		private SceneModel? _mouseDownScene;
		private readonly List<SceneModel> _overflowScenes = new List<SceneModel>();
		private readonly VirtualDesktopManager _virtualDesktopManager = new VirtualDesktopManager();
		private ShortcutGesture _toggleStageManagerShortcut = ReadShortcutSetting(TOGGLE_STAGE_MANAGER_SHORTCUT_VALUE, DefaultToggleStageManagerShortcut);
		private ShortcutGesture _toggleAutoHideShortcut = ReadShortcutSetting(TOGGLE_AUTO_HIDE_SHORTCUT_VALUE, DefaultToggleAutoHideShortcut);
		private ShortcutGesture _revealStageManagerShortcut = ReadShortcutSetting(REVEAL_STAGE_MANAGER_SHORTCUT_VALUE, DefaultRevealStageManagerShortcut);
		private bool _autoHideStageManagerIcons = AppSettings.ReadBool(AUTO_HIDE_ICONS_VALUE, defaultValue: true);
		private bool _isStageManagerEnabled;
		private DateTime _revealUntilUtc = DateTime.MinValue;
		private SettingsWindow? _settingsWindow;

		public bool EnableWindowDropToScene = false;
		public bool EnableWindowPullToScene = true;

		public MainWindow()
		{
			InitializeComponent();

			DataContext = this;

			_overlapCheckTimer = new Timer(OverlapCheck, null, Timeout.Infinite, Timeout.Infinite);

			SwitchSceneCommand = new ActionCommand(async model =>
			{
				var sceneModel = (SceneModel)model;
				if (sceneModel.IsOverflowGroup)
				{
					ToggleSceneWindowPicker(sceneModel);
					return;
				}

				CloseWindowPickers();
				await SceneManager!.SwitchTo(sceneModel.Scene, sceneModel.PrimaryDisplayWindow?.Window);
				sceneModel.PrimaryDisplayWindow?.Focus();
			});
			ToggleSceneWindowPickerCommand = new ActionCommand(model => ToggleSceneWindowPicker((SceneModel)model));
			ActivateAppIconCommand = new ActionCommand(async model => await ActivateAppIcon((WindowModel)model));
			ActivateWindowCommand = new ActionCommand(async model => await ActivateWindow((WindowModel)model));
		}

		protected override void OnInitialized(EventArgs e)
		{
			base.OnInitialized(e);

			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
			_lastWidth = Width;
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);

			_thisHandle = new WindowInteropHelper(this).Handle;
			_hwndSource = HwndSource.FromHwnd(_thisHandle);
			_hwndSource?.AddHook(WndProc);
			RegisterGlobalHotkeys();
		}

		protected override void OnClosed(EventArgs e)
		{
			UnregisterGlobalHotkeys();
			_hwndSource?.RemoveHook(WndProc);
			StopHook();
			_overlapCheckTimer.Dispose();

			trayIcon.Dispose();

			SceneManager?.Stop();

			base.OnClosed(e);

			Environment.Exit(0);
		}

		protected override async void OnContentRendered(EventArgs e)
		{
			base.OnContentRendered(e);

			_thisHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

			var windowsManager = new WindowsManager();
			SceneManager = new SceneManager(windowsManager);
			await SceneManager.Start().ConfigureAwait(true);

			SceneManager.SceneChanged += SceneManager_SceneChanged;
			SceneManager.CurrentSceneSelectionChanged += SceneManager_CurrentSceneSelectionChanged;
			SceneManager.WindowsManager.DesktopChanged += WindowsManager_DesktopChanged;

			UpdateVisibleSceneCapacity();
			AddInitialScenes();
			SyncVisibilityByUpdatedTimeStamp();

			var foreground = Win32.GetForegroundWindow();
			var foregroundScene = SceneManager.FindSceneForWindow(foreground);
			if (foregroundScene is object)
				await SceneManager.SwitchTo(foregroundScene).ConfigureAwait(true);

			_isStageManagerEnabled = true;
			OnPropertyChanged(nameof(IsStageManagerEnabled));
			OnPropertyChanged(nameof(StageManagerStatus));
			StartHook();
			_overlapCheckTimer.Change(0, TIMERINTERVAL_MILLISECONDS);
			EnsureStageManagerOnCurrentDesktop();
		}

		private void AddInitialScenes()
		{
			var initialScenes = SceneManager?.GetScenes().ToArray() ?? Array.Empty<Scene>();
			for (int i = 0; i < initialScenes.Length; i++)
			{
				var model = SceneModel.FromScene(initialScenes[i]);
				model.IsVisible = i < _visibleSceneCapacity;
				Scenes.Add(model);
			}
		}

		private void SceneManager_CurrentSceneSelectionChanged(object? sender, CurrentSceneSelectionChangedEventArgs args)
		{
			if (_removedCurrentScene is object && !Scenes.Contains(_removedCurrentScene))
				Scenes.Add(_removedCurrentScene);

			_removedCurrentScene = null;

			SyncVisibilityByUpdatedTimeStamp();
		}

		protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
		{
			base.OnRenderSizeChanged(sizeInfo);
			var area = this.GetMonitorWorkSize();
			this.Left = 0;
			this.Top = 0;
			this.Height = area.Height;
			UpdateVisibleSceneCapacity();
		}

		private void SceneManager_SceneChanged(object? sender, SceneChangedEventArgs e)
		{
			this.Dispatcher.Invoke(() =>
			{
				switch (e.Change)
				{
					case ChangeType.Created:
						Scenes.Add(SceneModel.FromScene(e.Scene));
						SyncVisibilityByUpdatedTimeStamp();
						break;
					case ChangeType.Updated:
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toUpdate)
							toUpdate.UpdateFromScene(e.Scene);
						SyncVisibilityByUpdatedTimeStamp();
						break;
					case ChangeType.Removed:
						if (AllScenes.FirstOrDefault(s => s.Id == e.Scene.Id) is SceneModel toRemove)
						{
							if (toRemove.Equals(_removedCurrentScene))
								_removedCurrentScene = null;
							else
								Scenes.Remove(toRemove);
						}
						SyncVisibilityByUpdatedTimeStamp();
						break;
				}
			});
		}

		private void OnMousePressed(object? sender, MouseHookEventArgs e)
		{
			// if it's allowed to drag windows into scenes, we cannot hide the scenes
			if (EnableWindowDropToScene)
				_overlapCheckTimer.Change(TimeSpan.Zero, TimeSpan.Zero);

			var foregroundWindow = Win32.GetForegroundWindow();
			if (foregroundWindow != _thisHandle)
				return;

			if (EnableWindowPullToScene)
			{
				var screenPoint = new Point(e.Data.X, e.Data.Y);
				this.Dispatcher.Invoke(() =>
				{
					_mouseDownScene = FindSceneByPoint(screenPoint);
				});
			}
		}

		private void OnMouseReleased(object? sender, MouseHookEventArgs e)
		{
			// if it's allowed to drag windows into scenes, we cannot hide the scenes
			if (EnableWindowDropToScene)
			{
				_overlapCheckTimer.Change(0, TIMERINTERVAL_MILLISECONDS);

				var foregroundWindow = Win32.GetForegroundWindow();

				if (foregroundWindow == _thisHandle)
					return;

				var screenPoint = new Point(e.Data.X, e.Data.Y);
				this.Dispatcher.Invoke(() =>
				{
					var sceneModel = FindSceneByPoint(screenPoint);

					if (sceneModel is object && !sceneModel.IsOverflowGroup && sceneModel.Scene is object)
						SceneManager?.MoveWindow(foregroundWindow, sceneModel.Scene).SafeFireAndForget();
				});
			}

			if (EnableWindowPullToScene)
			{
				if (e.Data.X > _lastWidth && _mouseDownScene is object && !_mouseDownScene.IsOverflowGroup && _mouseDownScene.Scene is object)
				{
					this.Dispatcher.Invoke(() =>
					{
						SceneManager?.PopWindowFrom(_mouseDownScene.Scene).SafeFireAndForget();
					});
				}
			}
		}

		private SceneModel? FindSceneByPoint(Point p)
		{
			var thisWindow = new WindowsWindow(_thisHandle);
			var pointOnWindow = new Point(p.X - thisWindow.Location.X, p.Y - thisWindow.Location.Y);

			var dpi = VisualTreeHelper.GetDpi(this);

			pointOnWindow.X /= dpi.DpiScaleX;
			pointOnWindow.Y /= dpi.DpiScaleY;

			SceneModel? model = null;

			var element = VisualTreeHelper.HitTest(this, pointOnWindow)?.VisualHit;

			while (element is not null)
			{
				if (element is FrameworkElement { DataContext: SceneModel m })
				{
					model = m;
					break;
				}

				element = element.GetParentObject();
			}

			return model;
		}
		private void SyncVisibilityByUpdatedTimeStamp()
		{
			var currentDesktopId = GetCurrentDesktopId();
			var regularScenes = Scenes.Where(s => !s.IsOverflowGroup).ToArray();
			foreach (var scene in regularScenes)
				scene.UpdateDisplayWindows(w => IsWindowOnCurrentDesktop(w.Handle, currentDesktopId));

			var currentDesktopScenes = regularScenes
				.Where(s => s.DisplayWindows.Any())
				.OrderByDescending(s => s.Updated)
				.ToArray();

			foreach (var scene in regularScenes.Except(currentDesktopScenes))
				scene.IsVisible = false;

			if (currentDesktopScenes.Length <= _visibleSceneCapacity)
			{
				foreach (var scene in currentDesktopScenes)
					scene.IsVisible = true;

				SyncOverflowGroups(Array.Empty<IReadOnlyList<SceneModel>>());
				return;
			}

			var overflowGroupCount = CalculateOverflowGroupCount(currentDesktopScenes.Length, _visibleSceneCapacity);
			var visibleSceneCount = Math.Max(0, _visibleSceneCapacity - overflowGroupCount);

			for (int i = 0; i < currentDesktopScenes.Length; i++)
				currentDesktopScenes[i].IsVisible = i < visibleSceneCount;

			var hiddenScenes = currentDesktopScenes.Skip(visibleSceneCount).ToArray();
			var overflowGroups = BuildOverflowGroups(hiddenScenes, overflowGroupCount);
			SyncOverflowGroups(overflowGroups);
		}

		private void UpdateVisibleSceneCapacity()
		{
			var area = this.GetMonitorWorkSize();
			var height = area.Height > 0 ? area.Height : SystemParameters.WorkArea.Height;
			height = Math.Max(0, height - BOTTOM_WORK_AREA_GUARD);
			var capacity = Math.Max(MIN_VISIBLE_SCENES, (int)Math.Floor(height / SCENE_SLOT_HEIGHT));

			if (capacity == _visibleSceneCapacity)
				return;

			_visibleSceneCapacity = capacity;
			SyncVisibilityByUpdatedTimeStamp();
		}

		private Guid? GetCurrentDesktopId()
		{
			try
			{
				return _virtualDesktopManager.GetCurrentDesktopId(GetKnownWindowHandles(), _thisHandle);
			}
			catch (COMException)
			{
				return null;
			}
			catch (InvalidCastException)
			{
				return null;
			}
		}

		private bool IsWindowOnCurrentDesktop(IntPtr handle, Guid? currentDesktopId)
		{
			try
			{
				return _virtualDesktopManager.IsWindowOnDesktop(handle, currentDesktopId);
			}
			catch (COMException)
			{
				return true;
			}
			catch (InvalidCastException)
			{
				return true;
			}
		}

		private int CalculateOverflowGroupCount(int sceneCount, int capacity)
		{
			if (sceneCount <= capacity)
				return 0;

			var maxGroupCount = Math.Max(1, Math.Min(MAX_OVERFLOW_GROUPS, capacity));
			for (int groupCount = 1; groupCount <= maxGroupCount; groupCount++)
			{
				var visibleSceneCount = Math.Max(0, capacity - groupCount);
				var hiddenSceneCount = sceneCount - visibleSceneCount;
				if (hiddenSceneCount <= groupCount * OVERFLOW_TARGET_GROUP_SIZE)
					return groupCount;
			}

			return maxGroupCount;
		}

		private IReadOnlyList<IReadOnlyList<SceneModel>> BuildOverflowGroups(IReadOnlyList<SceneModel> hiddenScenes, int groupCount)
		{
			if (!hiddenScenes.Any() || groupCount <= 0)
				return Array.Empty<IReadOnlyList<SceneModel>>();

			groupCount = Math.Min(groupCount, hiddenScenes.Count);
			var orderedScenes = hiddenScenes
				.OrderBy(GetSceneGroupName, StringComparer.CurrentCultureIgnoreCase)
				.ThenByDescending(s => s.Updated)
				.ToArray();

			var groups = new List<IReadOnlyList<SceneModel>>();
			var baseSize = orderedScenes.Length / groupCount;
			var extra = orderedScenes.Length % groupCount;
			var offset = 0;

			for (int i = 0; i < groupCount; i++)
			{
				var size = baseSize + (i < extra ? 1 : 0);
				groups.Add(orderedScenes.Skip(offset).Take(size).ToArray());
				offset += size;
			}

			return groups;
		}

		private void SyncOverflowGroups(IReadOnlyList<IReadOnlyList<SceneModel>> groupedScenes)
		{
			while (_overflowScenes.Count < groupedScenes.Count)
				_overflowScenes.Add(SceneModel.CreateOverflowGroup());

			for (int i = 0; i < groupedScenes.Count; i++)
			{
				var overflowScene = _overflowScenes[i];
				overflowScene.UpdateOverflowWindows(groupedScenes[i], GetOverflowGroupTitle(groupedScenes[i]));
				overflowScene.IsVisible = true;

				if (!Scenes.Contains(overflowScene))
					Scenes.Add(overflowScene);
			}

			for (int i = groupedScenes.Count; i < _overflowScenes.Count; i++)
			{
				var overflowScene = _overflowScenes[i];
				overflowScene.IsWindowPickerOpen = false;
				overflowScene.IsVisible = false;
				if (Scenes.Contains(overflowScene))
					Scenes.Remove(overflowScene);
			}
		}

		private string GetOverflowGroupTitle(IReadOnlyList<SceneModel> groupedScenes)
		{
			if (!groupedScenes.Any())
				return "More windows";

			var first = GetSceneGroupName(groupedScenes.First());
			var last = GetSceneGroupName(groupedScenes.Last());
			return string.Equals(first, last, StringComparison.CurrentCultureIgnoreCase)
				? $"More: {first}"
				: $"More: {first} - {last}";
		}

		private string GetSceneGroupName(SceneModel scene)
		{
			var window = scene.DisplayWindows.FirstOrDefault() ?? scene.Windows.FirstOrDefault();
			return window?.Window?.ProcessName ?? scene.Title ?? "";
		}

		public ObservableCollection<SceneModel> Scenes { get; } = new ObservableCollection<SceneModel>();

		public IEnumerable<SceneModel> AllScenes
		{
			get
			{
				var removed = _removedCurrentScene;
				return removed is null ? Scenes : Scenes.Concat(new[] { removed });
			}
		}

		public ICommand SwitchSceneCommand { get; }

		public ICommand ToggleSceneWindowPickerCommand { get; }

		public ICommand ActivateAppIconCommand { get; }

		public ICommand ActivateWindowCommand { get; }

		public SceneManager? SceneManager { get; private set; }

		public IntPtr Handle => _thisHandle;

		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsStageManagerEnabled
		{
			get => _isStageManagerEnabled;
			set
			{
				if (value == _isStageManagerEnabled)
					return;

				_isStageManagerEnabled = value;
				OnPropertyChanged(nameof(IsStageManagerEnabled));
				OnPropertyChanged(nameof(StageManagerStatus));
				ApplyStageManagerEnabled(value).SafeFireAndForget();
			}
		}

		public string StageManagerStatus => IsStageManagerEnabled ? "Stage Manager: On" : "Stage Manager: Off";

		public bool AutoHideStageManagerIcons
		{
			get => _autoHideStageManagerIcons;
			set
			{
				if (value == _autoHideStageManagerIcons)
					return;

				_autoHideStageManagerIcons = value;
				AppSettings.WriteBool(AUTO_HIDE_ICONS_VALUE, value);
				OnPropertyChanged(nameof(AutoHideStageManagerIcons));

				if (!value)
					Mode = WindowMode.OnScreen;
			}
		}

		public ShortcutGesture ToggleStageManagerShortcut => _toggleStageManagerShortcut;

		public ShortcutGesture ToggleAutoHideShortcut => _toggleAutoHideShortcut;

		public ShortcutGesture RevealStageManagerShortcut => _revealStageManagerShortcut;

		public WindowMode Mode
		{
			get => _mode;
			set
			{
				if (value == _mode)
					return;

				_mode = value;

				this.Topmost = value == WindowMode.Flyover;

				ApplyWindowMode();
			}
		}

		private void ApplyWindowMode()
		{
			var newLeft = Mode == StageManager.WindowMode.OffScreen ? (-1 * Width) : 0.0;
			if (Left == newLeft)
				return;

			var isIncoming = newLeft > Left;
			var easingMode = isIncoming ? EasingMode.EaseOut : EasingMode.EaseIn;

			var animation = new DoubleAnimationUsingKeyFrames();
			animation.Duration = new Duration(TimeSpan.FromSeconds(0.5));
			var easingFunction = new PowerEase { EasingMode = easingMode };
			animation.KeyFrames.Add(new EasingDoubleKeyFrame(Left, KeyTime.FromPercent(0)));
			animation.KeyFrames.Add(new EasingDoubleKeyFrame(newLeft, KeyTime.FromPercent(1.0), easingFunction));

			BeginAnimation(LeftProperty, animation);
		}

		private void StartHook()
		{
			if (!IsStageManagerEnabled || _hook is object)
				return;

			_hook = new TaskPoolGlobalHook();

			_hook.MousePressed += OnMousePressed;
			_hook.MouseReleased += OnMouseReleased;
			_hook.MouseMoved += _hook_MouseMoved;

			Task.Run(_hook.Run);
		}

		private void StopHook()
		{
			if (_hook is null)
				return;

			_hook.MousePressed -= OnMousePressed;
			_hook.MouseReleased -= OnMouseReleased;
			_hook.MouseMoved -= _hook_MouseMoved;

			try
			{
				_hook.Dispose();
			}
			catch (HookException)
			{
			}
			finally
			{
				_hook = null;
			}
		}

		private void _hook_MouseMoved(object? sender, MouseHookEventArgs e)
		{
			_mouse.X = e.Data.X;
			_mouse.Y = e.Data.Y;

			if (Mode == WindowMode.OffScreen && e.Data.X <= 44)
			{
				Dispatcher.Invoke(() => Mode = WindowMode.Flyover);
			}
		}

		private void OverlapCheck(object? _)
		{
			if (!IsStageManagerEnabled || SceneManager is null)
				return;

			EnsureStageManagerOnCurrentDesktop();

			var currentWindows = SceneManager.GetCurrentWindows().ToArray(); // in case the enumeration changes
			UpdateModeByWindows(currentWindows);
		}

		private void WindowsManager_DesktopChanged(object? sender, EventArgs e)
		{
			RefreshAfterDesktopChanged().SafeFireAndForget();
		}

		private async Task RefreshAfterDesktopChanged()
		{
			await Task.Delay(250).ConfigureAwait(false);
			RefreshForCurrentDesktop();

			await Task.Delay(500).ConfigureAwait(false);
			RefreshForCurrentDesktop();
		}

		private void RefreshForCurrentDesktop()
		{
			Dispatcher.Invoke(() =>
			{
				EnsureStageManagerOnCurrentDesktop();
				SyncVisibilityByUpdatedTimeStamp();
			});
		}

		private void UpdateModeByWindows(IEnumerable<IWindow> windows)
		{
			if (DateTime.UtcNow < _revealUntilUtc)
			{
				Dispatcher.Invoke(() => Mode = WindowMode.Flyover);
				return;
			}

			if (!AutoHideStageManagerIcons)
			{
				Dispatcher.Invoke(() => Mode = WindowMode.OnScreen);
				return;
			}

			bool doesOverlap(IWindowLocation loc) => loc.State == Native.Window.WindowState.Maximized || (loc.State == Native.Window.WindowState.Normal && (loc.X * 2) < _lastWidth);

			var anyOverlappingWindows = windows.Any(w => doesOverlap(w.Location));

			var containsMouse = _mouse.X <= _lastWidth;
			var setMode = Mode == WindowMode.OnScreen && !containsMouse
							|| Mode == WindowMode.OffScreen
							|| (Mode == WindowMode.Flyover && !containsMouse);

			if (setMode)
			{
				Dispatcher.Invoke(() =>
				{
					Mode = anyOverlappingWindows ? WindowMode.OffScreen : WindowMode.OnScreen;
				});
			}
		}

		private void NavigateToProjectPage()
		{
			Process.Start(new ProcessStartInfo("https://github.com/SERRNOVIK/StageManager")
			{
				UseShellExecute = true
			});
		}

		public static bool StartsWithWindows
		{ 
			get => AutoStart.IsStartup(APP_NAME);
			set => AutoStart.SetStartup(APP_NAME, value);
		}

		public void UpdateShortcutSettings(
			ShortcutGesture toggleStageManagerShortcut,
			ShortcutGesture toggleAutoHideShortcut,
			ShortcutGesture revealStageManagerShortcut,
			bool autoHideStageManagerIcons)
		{
			_toggleStageManagerShortcut = toggleStageManagerShortcut;
			_toggleAutoHideShortcut = toggleAutoHideShortcut;
			_revealStageManagerShortcut = revealStageManagerShortcut;

			AppSettings.WriteString(TOGGLE_STAGE_MANAGER_SHORTCUT_VALUE, toggleStageManagerShortcut.DisplayText);
			AppSettings.WriteString(TOGGLE_AUTO_HIDE_SHORTCUT_VALUE, toggleAutoHideShortcut.DisplayText);
			AppSettings.WriteString(REVEAL_STAGE_MANAGER_SHORTCUT_VALUE, revealStageManagerShortcut.DisplayText);

			AutoHideStageManagerIcons = autoHideStageManagerIcons;

			OnPropertyChanged(nameof(ToggleStageManagerShortcut));
			OnPropertyChanged(nameof(ToggleAutoHideShortcut));
			OnPropertyChanged(nameof(RevealStageManagerShortcut));

			RegisterGlobalHotkeys();
		}

		private static ShortcutGesture ReadShortcutSetting(string name, ShortcutGesture defaultValue)
		{
			var shortcut = ShortcutGesture.Parse(AppSettings.ReadString(name, defaultValue.DisplayText));
			return shortcut.IsEmpty ? defaultValue : shortcut;
		}

		private void MenuItem_ProjectPage_Click(object sender, RoutedEventArgs e)
		{
			NavigateToProjectPage();
		}

		private void MenuItem_Settings_Click(object sender, RoutedEventArgs e)
		{
			ShowSettingsWindow();
		}

		private void MenuItem_Quit_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void WindowPickerButton_Click(object sender, RoutedEventArgs e)
		{
			if (sender is FrameworkElement { DataContext: SceneModel scene })
				ToggleSceneWindowPicker(scene);

			e.Handled = true;
		}

		private async Task ApplyStageManagerEnabled(bool enabled)
		{
			if (SceneManager is null)
				return;

			if (enabled)
			{
				Show();
				EnsureStageManagerOnCurrentDesktop();
				await SceneManager.Enable().ConfigureAwait(true);
				StartHook();
				_overlapCheckTimer.Change(0, TIMERINTERVAL_MILLISECONDS);
			}
			else
			{
				_overlapCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
				StopHook();
				SceneManager.Disable();
				Hide();
			}
		}

		private void ToggleSceneWindowPicker(SceneModel scene)
		{
			if (scene is null || !scene.HasMultipleWindows)
				return;

			var shouldOpen = !scene.IsWindowPickerOpen;
			CloseWindowPickers();
			scene.IsWindowPickerOpen = shouldOpen;
		}

		private async Task ActivateWindow(WindowModel window)
		{
			if (window is null || SceneManager is null)
				return;

			CloseWindowPickers();

			var scene = SceneManager.FindSceneForWindow(window.Handle);
			if (scene is object)
				await SceneManager.SwitchTo(scene, window.Window).ConfigureAwait(true);

			window.Focus();
		}

		private async Task ActivateAppIcon(WindowModel window)
		{
			if (window is null)
				return;

			var scene = AllScenes
				.Where(s => s.DisplayWindows.Any(w => w.Handle == window.Handle))
				.OrderByDescending(s => s.IsVisible)
				.FirstOrDefault();

			var sameAppWindowCount = scene?.DisplayWindows
				.Count(w => string.Equals(w.Window?.ProcessName, window.Window?.ProcessName, StringComparison.OrdinalIgnoreCase)) ?? 1;

			if (scene is object && sameAppWindowCount > 1)
			{
				ToggleSceneWindowPicker(scene);
				return;
			}

			await ActivateWindow(window).ConfigureAwait(true);
		}

		private void CloseWindowPickers()
		{
			foreach (var scene in AllScenes.Where(s => s is object))
				scene.IsWindowPickerOpen = false;
		}

		private void ShowSettingsWindow()
		{
			if (_settingsWindow is { IsVisible: true })
			{
				_settingsWindow.Activate();
				return;
			}

			UnregisterGlobalHotkeys();
			_settingsWindow = new SettingsWindow(this);
			_settingsWindow.Closed += (_, _) =>
			{
				_settingsWindow = null;
				RegisterGlobalHotkeys();
			};
			_settingsWindow.Show();
			_settingsWindow.Activate();
		}

		private void RegisterGlobalHotkeys()
		{
			if (_thisHandle == IntPtr.Zero)
				return;

			UnregisterGlobalHotkeys();
			RegisterGlobalHotkey(TOGGLE_STAGE_MANAGER_HOTKEY_ID, ToggleStageManagerShortcut);
			RegisterGlobalHotkey(TOGGLE_AUTO_HIDE_HOTKEY_ID, ToggleAutoHideShortcut);
			RegisterGlobalHotkey(REVEAL_STAGE_MANAGER_HOTKEY_ID, RevealStageManagerShortcut);
		}

		private void RegisterGlobalHotkey(int id, ShortcutGesture shortcut)
		{
			if (shortcut.IsEmpty)
				return;

			var virtualKey = KeyInterop.VirtualKeyFromKey(shortcut.Key);
			if (virtualKey == 0)
				return;

			Win32.RegisterHotKey(_thisHandle, id, GetHotkeyModifiers(shortcut), (uint)virtualKey);
		}

		private void UnregisterGlobalHotkeys()
		{
			if (_thisHandle == IntPtr.Zero)
				return;

			Win32.UnregisterHotKey(_thisHandle, TOGGLE_STAGE_MANAGER_HOTKEY_ID);
			Win32.UnregisterHotKey(_thisHandle, TOGGLE_AUTO_HIDE_HOTKEY_ID);
			Win32.UnregisterHotKey(_thisHandle, REVEAL_STAGE_MANAGER_HOTKEY_ID);
		}

		private static uint GetHotkeyModifiers(ShortcutGesture shortcut)
		{
			uint modifiers = MOD_NOREPEAT;
			if (shortcut.Control)
				modifiers |= MOD_CONTROL;
			if (shortcut.Alt)
				modifiers |= MOD_ALT;
			if (shortcut.Shift)
				modifiers |= MOD_SHIFT;
			if (shortcut.Windows)
				modifiers |= MOD_WIN;

			return modifiers;
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg != WM_HOTKEY)
				return IntPtr.Zero;

			handled = true;
			switch (wParam.ToInt32())
			{
				case TOGGLE_STAGE_MANAGER_HOTKEY_ID:
					IsStageManagerEnabled = !IsStageManagerEnabled;
					break;
				case TOGGLE_AUTO_HIDE_HOTKEY_ID:
					AutoHideStageManagerIcons = !AutoHideStageManagerIcons;
					break;
				case REVEAL_STAGE_MANAGER_HOTKEY_ID:
					RevealStageManagerItems();
					break;
			}

			return IntPtr.Zero;
		}

		private void RevealStageManagerItems()
		{
			_revealUntilUtc = DateTime.UtcNow.Add(RevealDuration);

			if (!IsStageManagerEnabled)
				IsStageManagerEnabled = true;

			Show();
			EnsureStageManagerOnCurrentDesktop();
			Mode = WindowMode.Flyover;
			_overlapCheckTimer.Change(0, TIMERINTERVAL_MILLISECONDS);
		}

		private void EnsureStageManagerOnCurrentDesktop()
		{
			try
			{
				_virtualDesktopManager.MoveWindowToCurrentDesktop(_thisHandle, GetKnownWindowHandles());
			}
			catch (COMException)
			{
			}
			catch (InvalidCastException)
			{
			}
		}

		private IEnumerable<IntPtr> GetKnownWindowHandles()
		{
			return AllScenes
				.SelectMany(scene => scene.Windows)
				.Select(window => window.Handle)
				.Where(handle => handle != IntPtr.Zero)
				.ToArray();
		}

		private void ContextMenu_Closed(object sender, RoutedEventArgs e)
		{
			StartHook();
		}

		private void ContextMenu_Opened(object sender, RoutedEventArgs e)
		{
			StopHook();
		}

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public enum WindowMode
	{
		OnScreen,
		OffScreen,
		Flyover
	}
}
