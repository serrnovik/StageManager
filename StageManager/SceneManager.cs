using AsyncAwaitBestPractices;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using StageManager.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace StageManager
{
	public class SceneManager
	{
		private readonly Desktop _desktop;
		private List<Scene>? _scenes;
		private Scene? _current;
		private bool _suspend = false;
		private bool _enabled = true;
		private Guid? _reentrancyLockSceneId;
		private IntPtr _currentWindowHandle = IntPtr.Zero;
		private readonly VirtualDesktopManager _virtualDesktopManager = new VirtualDesktopManager();

		public event EventHandler<SceneChangedEventArgs>? SceneChanged;
		public event EventHandler<CurrentSceneSelectionChangedEventArgs>? CurrentSceneSelectionChanged;

		private IWindowStrategy WindowStrategy { get; } = new NormalizeAndMinimizeWindowStrategy(); // new WindowNormalizeStrategy/OpacityWindowStrategy/ShowAndHideWindowStrategy

		public WindowsManager WindowsManager { get; }

		public bool IsEnabled => _enabled;

		public SceneManager(WindowsManager windowsManager)
		{
			WindowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
			_desktop = new Desktop();
			_desktop.HideIcons();
		}

		public async Task Start()
		{
			if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
				throw new NotSupportedException("Start has to be called on the main thread, otherwise events won't be fired.");

			WindowsManager.WindowCreated += WindowsManager_WindowCreated;
			WindowsManager.WindowUpdated += WindowsManager_WindowUpdated;
			WindowsManager.WindowDestroyed += WindowsManager_WindowDestroyed;
			WindowsManager.UntrackedFocus += WindowsManager_UntrackedFocus;

			await WindowsManager.Start();
		}

		internal void Stop()
		{
			_enabled = false;
			WindowsManager.Stop();
			ShowAllWindows();
			_desktop.ShowIcons();
		}

		public void Disable()
		{
			if (!_enabled)
				return;

			_enabled = false;
			_suspend = true;
			ShowAllWindows();
			_desktop.ShowIcons();
		}

		public async Task Enable()
		{
			if (_enabled)
				return;

			_enabled = true;
			_suspend = false;
			_desktop.HideIcons();

			var foregroundScene = FindSceneForWindow(Win32.GetForegroundWindow());
			var scene = foregroundScene ?? _current;
			_current = null;
			await SwitchTo(scene).ConfigureAwait(true);
		}

		private void ShowAllWindows()
		{
			if (_scenes is null)
				return;

			foreach (var scene in _scenes)
			{
				foreach (var w in scene.Windows)
					WindowStrategy.Show(w);
			}
		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (_suspend || !_enabled)
				return;

			if (type == WindowUpdateType.Foreground)
				SwitchToSceneByWindow(window).SafeFireAndForget();
		}

		private void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			if (_suspend || !_enabled)
				return;

			var ownerScene = FindSceneForOwnerWindow(e);
			if (ownerScene is object)
			{
				SwitchTo(ownerScene).SafeFireAndForget();
				return;
			}

			if (!_desktop.HasDesktopView)
				_desktop.TrySetDesktopView(e);

			if (_desktop.HasDesktopView && _desktop.DesktopViewHandle == e)
				SwitchTo(null).SafeFireAndForget();
		}

		private void WindowsManager_WindowDestroyed(IWindow window)
		{
			if (!_enabled)
				return;

			var scene = FindSceneForWindow(window);

			if (scene is not null)
			{
				scene.Remove(window);

				if (scene.Windows.Any())
				{
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
				}
				else
				{
					_scenes?.Remove(scene);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Removed));
				}
			}
		}

		public Scene? FindSceneForWindow(IWindow window) => FindSceneForWindow(window.Handle);

		public Scene? FindSceneForWindow(IntPtr handle) => _scenes?.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));

		private Scene? FindSceneForProcess(string processName)
		{
			var currentDesktopId = GetCurrentDesktopId();
			return _scenes?.FirstOrDefault(s => string.Equals(s.Key, processName, StringComparison.OrdinalIgnoreCase) && s.Windows.Any(w => IsWindowOnCurrentDesktop(w, currentDesktopId)));
		}

		private async void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			if (!_enabled)
				return;

			if (!IsSceneableWindow(window))
				return;

			SwitchToSceneByNewWindow(window).SafeFireAndForget();
		}

		private async Task SwitchToSceneByWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
			{
				scene = new Scene(GetWindowGroupKey(window), window);
				_scenes?.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}

			await SwitchTo(scene, window);
		}

		private async Task SwitchToSceneByNewWindow(IWindow window)
		{
			var existentScene = FindSceneForProcess(GetWindowGroupKey(window));
			var scene = existentScene ?? new Scene(window.ProcessName, window);

			if (existentScene is null)
			{
				_scenes?.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
			}
			else
			{
				scene.Add(window);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
			}

			await SwitchTo(scene, window).ConfigureAwait(true);
		}

		/// <summary>
		/// Determines if a scene is switched back to shortly after it has been hidden.
		/// This can happen if an app activates one of it's windows after being hidde,
		/// like Microsoft Teams does if there's a small floating window for a current call.
		/// </summary>
		/// <param name="scene"></param>
		/// <returns></returns>
		private bool IsReentrancy(Scene? scene)
		{
			if (scene is null)
				return false;

			if (Guid.Equals(scene.Id, _reentrancyLockSceneId))
				return true;

			if (_current is object)
			{
				_reentrancyLockSceneId = _current.Id;

				Task.Run(async () =>
				{
					await Task.Delay(1000).ConfigureAwait(false);
					_reentrancyLockSceneId = null;
				}).SafeFireAndForget();
			}

			return false;
		}

		public async Task SwitchTo(Scene? scene, IWindow? selectedWindow = null)
		{
			if (!_enabled)
				return;

			var currentDesktopId = GetCurrentDesktopId();
			var sceneWindows = scene?.Windows.Where(w => IsWindowOnCurrentDesktop(w, currentDesktopId)).ToArray() ?? Array.Empty<IWindow>();
			var activeSceneWindow = SelectSceneWindow(sceneWindows, selectedWindow);
			var isSameScene = object.Equals(scene, _current);

			if (isSameScene && activeSceneWindow?.Handle == _currentWindowHandle)
				return;

			if (!isSameScene && IsReentrancy(scene))
				return;

			try
			{
				_suspend = true;

				var visibleSceneWindows = activeSceneWindow is null ? Array.Empty<IWindow>() : new[] { activeSceneWindow };
				var otherWindows = GetSceneableWindows(currentDesktopId)
					.Except(visibleSceneWindows)
					.Where(w => !w.HasVisibleOwnedPopup)
					.ToArray();

				var prior = _current;
				_current = scene;
				_currentWindowHandle = activeSceneWindow?.Handle ?? IntPtr.Zero;

				if (_scenes is not null)
				{
					foreach (var s in _scenes)
						s.IsSelected = s.Equals(scene);
				}

				if (scene is object)
				{
					foreach (var w in visibleSceneWindows)
						WindowStrategy.Show(w);
				}

				foreach (var o in otherWindows)
					WindowStrategy.Hide(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));

				if (scene is null)
					_desktop.ShowIcons();
				else
					_desktop.HideIcons();
			}
			finally
			{
				_suspend = false;
			}
		}

		private IWindow? SelectSceneWindow(IReadOnlyCollection<IWindow> sceneWindows, IWindow? selectedWindow)
		{
			if (!sceneWindows.Any())
				return null;

			if (selectedWindow is object)
			{
				var selected = sceneWindows.FirstOrDefault(w => w.Handle == selectedWindow.Handle);
				if (selected is object)
					return selected;
			}

			if (_currentWindowHandle != IntPtr.Zero)
			{
				var current = sceneWindows.FirstOrDefault(w => w.Handle == _currentWindowHandle);
				if (current is object)
					return current;
			}

			var focused = sceneWindows.FirstOrDefault(w => w.IsFocused);
			return focused ?? sceneWindows.FirstOrDefault();
		}

		public Task MoveWindow(Scene sourceScene, IWindow window, Scene targetScene)
		{
			try
			{
				_suspend = true;

				if (sourceScene is null || sourceScene.Equals(targetScene))
					return Task.CompletedTask;

				sourceScene.Remove(window);
				targetScene.Add(window);

				SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Updated));
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(targetScene, window, ChangeType.Updated));

				if (!sourceScene.Windows.Any())
				{
					_scenes?.Remove(sourceScene);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(sourceScene, window, ChangeType.Removed));
				}

				if (targetScene.Equals(_current))
				{
					WindowStrategy.Show(window);
					window.Focus();
				}
				else
				{
					WindowStrategy.Hide(window);

					// reset window position after move so that the window is back at the starting position on the new scene
					if (window is WindowsWindow w && w.PopLastLocation() is IWindowLocation l)
						Win32.SetWindowPos(window.Handle, IntPtr.Zero, l.X, l.Y, 0, 0, Win32.SetWindowPosFlags.IgnoreResize);
				}

				return Task.CompletedTask;
			}
			finally
			{
				_suspend = false;
			}
		}

		public async Task MoveWindow(IntPtr handle, Scene targetScene)
		{
			var source = FindSceneForWindow(handle);

			if (source is null || source.Equals(targetScene))
				return;

			var window = source.Windows.First(w => w.Handle == handle);
			await MoveWindow(source, window, targetScene);
		}

		public async Task PopWindowFrom(Scene sourceScene)
		{
			if (sourceScene is null || _current is null || sourceScene.Equals(_current))
				return;

			var window = sourceScene.Windows.LastOrDefault();

			if (window is object)
				await MoveWindow(sourceScene, window, _current).ConfigureAwait(false);
		}

		private bool IsSceneableWindow(IWindow window)
		{
			return window is object
				&& !string.IsNullOrEmpty(window.ProcessFileName)
				&& !string.IsNullOrEmpty(window.Title)
				&& !window.IsOwnedWindow
				&& IsWindowOnCurrentDesktop(window, GetCurrentDesktopId());
		}

		private Guid? GetCurrentDesktopId()
		{
			try
			{
				return _virtualDesktopManager.GetCurrentDesktopId(GetKnownWindowHandles());
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

		private IEnumerable<IntPtr> GetKnownWindowHandles()
		{
			return WindowsManager?.Windows?.Select(window => window.Handle).ToArray() ?? Enumerable.Empty<IntPtr>();
		}

		private bool IsWindowOnCurrentDesktop(IWindow window, Guid? currentDesktopId)
		{
			try
			{
				return _virtualDesktopManager.IsWindowOnDesktop(window.Handle, currentDesktopId);
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

		private IEnumerable<IWindow> GetSceneableWindows() => GetSceneableWindows(GetCurrentDesktopId());

		private IEnumerable<IWindow> GetSceneableWindows(Guid? currentDesktopId)
		{
			return WindowsManager?.Windows?.Where(w =>
				w is object
				&& !string.IsNullOrEmpty(w.ProcessFileName)
				&& !string.IsNullOrEmpty(w.Title)
				&& !w.IsOwnedWindow
				&& IsWindowOnCurrentDesktop(w, currentDesktopId)) ?? Enumerable.Empty<IWindow>();
		}

		public IEnumerable<Scene> GetScenes()
		{
			if (_scenes is null)
			{
				_scenes = GetSceneableWindows()
							.GroupBy(GetWindowGroupKey)
							.Select(group => new Scene(group.Key, group.ToArray()))
							.ToList();
			}

			return _scenes;
		}

		public IEnumerable<IWindow> GetCurrentWindows()
		{
			var currentDesktopId = GetCurrentDesktopId();
			return _current?.Windows.Where(w => IsWindowOnCurrentDesktop(w, currentDesktopId)) ?? GetSceneableWindows(currentDesktopId);
		}

		private string GetWindowGroupKey(IWindow window) => window.ProcessName;

		private Scene? FindSceneForOwnerWindow(IntPtr handle)
		{
			var ownerHandle = Win32.GetWindow(handle, Win32.GW.GW_OWNER);
			if (ownerHandle != IntPtr.Zero && FindSceneForWindow(ownerHandle) is Scene ownerScene)
				return ownerScene;

			var rootOwnerHandle = Win32.GetAncestor(handle, Win32.GA.GA_ROOTOWNER);
			if (rootOwnerHandle != IntPtr.Zero && rootOwnerHandle != handle)
				return FindSceneForWindow(rootOwnerHandle);

			return null;
		}
	}
}
