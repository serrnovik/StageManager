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
		private Guid? _recentlyLeftSceneId;
		private DateTime _recentlyLeftSceneUtc = DateTime.MinValue;
		private readonly HashSet<IntPtr> _minimizedByStageManager = new HashSet<IntPtr>();
		private static readonly TimeSpan ReentrancyWindow = TimeSpan.FromSeconds(1);
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

		/// <summary>
		/// Restores the windows Stage Manager minimized itself. Windows the user minimized stay minimized.
		/// </summary>
		private void ShowAllWindows()
		{
			if (_scenes is null)
				return;

			foreach (var scene in _scenes)
			{
				foreach (var w in scene.Windows.Where(w => _minimizedByStageManager.Contains(w.Handle)))
					WindowStrategy.Show(w);
			}

			_minimizedByStageManager.Clear();
		}

		/// <summary>
		/// Restores minimized windows if the app is going down unexpectedly.
		/// Only relies on the handle set, so it works even if the scene state is broken.
		/// </summary>
		internal void RestoreWindowsAfterCrash()
		{
			foreach (var handle in _minimizedByStageManager.ToArray())
			{
				if (Win32.IsWindow(handle) && Win32.IsIconic(handle))
					Win32.ShowWindow(handle, Win32.SW.SW_SHOWNOACTIVATE);
			}

			_minimizedByStageManager.Clear();
		}

		private void ShowWindow(IWindow window)
		{
			_minimizedByStageManager.Remove(window.Handle);
			WindowStrategy.Show(window);
		}

		private void HideWindow(IWindow window)
		{
			if (!window.IsMinimized)
			{
				_minimizedByStageManager.Add(window.Handle);
				WindowSnapshotCache.Capture(window.Handle);
			}

			WindowStrategy.Hide(window);
		}

		private void WindowsManager_WindowUpdated(IWindow window, WindowUpdateType type)
		{
			if (_suspend || !_enabled)
				return;

			if (type == WindowUpdateType.Foreground)
				SwitchToSceneByWindow(window).SafeFireAndForget();
			else if (type == WindowUpdateType.MinimizeEnd)
				_minimizedByStageManager.Remove(window.Handle);
		}

		private void WindowsManager_UntrackedFocus(object? sender, IntPtr e)
		{
			if (_suspend || !_enabled)
				return;

			if (FindOwnerWindow(e) is (Scene ownerScene, IWindow ownerWindow))
			{
				// keep the dialog's owner visible, not just any window of the owner's scene
				if (!IsReentrancy(ownerScene))
					SwitchTo(ownerScene, ownerWindow).SafeFireAndForget();
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

			_minimizedByStageManager.Remove(window.Handle);
			WindowSnapshotCache.Remove(window.Handle);
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

		public Scene? FindSceneForWindow(IntPtr handle)
		{
			EnsureSceneIntegrity();
			return _scenes?.FirstOrDefault(s => s.Windows.Any(w => w.Handle == handle));
		}

		private Scene? FindSceneForProcess(string processName)
		{
			ConsolidateDuplicateScenes();
			return _scenes?.FirstOrDefault(s => string.Equals(s.Key, processName, StringComparison.OrdinalIgnoreCase));
		}

		private void WindowsManager_WindowCreated(IWindow window, bool firstCreate)
		{
			if (!_enabled)
				return;

			if (!IsSceneableWindow(window))
				return;

			var scene = AddToScene(window);

			// a new window only takes over the stage if it actually got the focus,
			// background apps opening windows must not minimize what the user is working on
			if (!_suspend && window.IsFocused)
				SwitchTo(scene, window).SafeFireAndForget();
		}

		private async Task SwitchToSceneByWindow(IWindow window)
		{
			var scene = FindSceneForWindow(window);
			if (scene is null)
			{
				if (!IsSceneableWindow(window))
					return;

				scene = AddToScene(window);
			}

			if (IsReentrancy(scene))
				return;

			await SwitchTo(scene, window).ConfigureAwait(true);
		}

		private Scene AddToScene(IWindow window)
		{
			if (FindSceneForWindow(window) is Scene existing)
				return existing;

			var existentScene = FindSceneForProcess(GetWindowGroupKey(window));

			if (existentScene is null)
			{
				var scene = new Scene(GetWindowGroupKey(window), window);
				_scenes?.Add(scene);
				SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Created));
				return scene;
			}

			existentScene.Add(window);
			SceneChanged?.Invoke(this, new SceneChangedEventArgs(existentScene, window, ChangeType.Updated));
			return existentScene;
		}

		/// <summary>
		/// Determines if a scene is activated by an app shortly after Stage Manager switched away from it.
		/// This can happen if an app activates one of it's windows after being hidden,
		/// like Microsoft Teams does if there's a small floating window for a current call.
		/// Only used for switches caused by window events, explicit user switches are never blocked.
		/// </summary>
		private bool IsReentrancy(Scene? scene)
		{
			return IsReentrancy(scene?.Id, _current?.Id, _recentlyLeftSceneId, _recentlyLeftSceneUtc, DateTime.UtcNow);
		}

		internal static bool IsReentrancy(Guid? targetSceneId, Guid? currentSceneId, Guid? recentlyLeftSceneId, DateTime recentlyLeftUtc, DateTime nowUtc)
		{
			if (targetSceneId is null || recentlyLeftSceneId is null)
				return false;

			if (targetSceneId == currentSceneId)
				return false;

			return targetSceneId == recentlyLeftSceneId && nowUtc - recentlyLeftUtc < ReentrancyWindow;
		}

		public async Task<IWindow?> SwitchTo(Scene? scene, IWindow? selectedWindow = null, bool cycleSameScene = false)
		{
			if (!_enabled)
				return null;

			var currentDesktopId = GetCurrentDesktopId();
			EnsureSceneIntegrity();
			var sceneWindows = scene?.Windows.Where(w => IsLiveWindow(w) && IsWindowOnCurrentDesktop(w, currentDesktopId)).ToArray() ?? Array.Empty<IWindow>();
			var isSameScene = object.Equals(scene, _current);
			var activeSceneWindow = SelectSceneWindow(sceneWindows, selectedWindow, isSameScene && cycleSameScene);

			if (isSameScene && activeSceneWindow?.Handle == _currentWindowHandle)
				return activeSceneWindow;

			try
			{
				_suspend = true;

				var visibleSceneWindows = activeSceneWindow is null ? Array.Empty<IWindow>() : new[] { activeSceneWindow };
				var otherWindows = GetSceneableWindows(currentDesktopId)
					.Where(w => !visibleSceneWindows.Any(v => v.Handle == w.Handle))
					.Where(w => !w.HasVisibleOwnedPopup)
					.ToArray();

				var prior = _current;
				if (prior is object && !object.Equals(prior, scene))
				{
					_recentlyLeftSceneId = prior.Id;
					_recentlyLeftSceneUtc = DateTime.UtcNow;
				}

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
						ShowWindow(w);
				}

				// if the focused window is about to be minimized, hand the focus to the new scene first.
				// Otherwise Windows activates an arbitrary window, possibly one of another scene.
				var foreground = Win32.GetForegroundWindow();
				if (activeSceneWindow is object && otherWindows.Any(w => w.Handle == foreground))
					activeSceneWindow.Focus();

				foreach (var o in otherWindows)
					HideWindow(o);

				CurrentSceneSelectionChanged?.Invoke(this, new CurrentSceneSelectionChangedEventArgs(prior, _current));

				if (scene is null)
					_desktop.ShowIcons();
				else
					_desktop.HideIcons();

				return activeSceneWindow;
			}
			finally
			{
				_suspend = false;
			}
		}

		private IWindow? SelectSceneWindow(IReadOnlyCollection<IWindow> sceneWindows, IWindow? selectedWindow, bool cycleSameScene)
		{
			if (!sceneWindows.Any())
				return null;

			var orderedWindows = sceneWindows.ToArray();

			if (cycleSameScene && orderedWindows.Length > 1)
			{
				var currentIndex = Array.FindIndex(orderedWindows, w => w.Handle == _currentWindowHandle);
				if (currentIndex >= 0)
					return orderedWindows[(currentIndex + 1) % orderedWindows.Length];
			}

			if (selectedWindow is object)
			{
				var selected = orderedWindows.FirstOrDefault(w => w.Handle == selectedWindow.Handle);
				if (selected is object)
					return selected;
			}

			if (_currentWindowHandle != IntPtr.Zero)
			{
				var current = orderedWindows.FirstOrDefault(w => w.Handle == _currentWindowHandle);
				if (current is object)
					return current;
			}

			var focused = orderedWindows.FirstOrDefault(w => w.IsFocused);
			return focused ?? orderedWindows.FirstOrDefault();
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
					ShowWindow(window);
					window.Focus();
				}
				else
				{
					HideWindow(window);

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
				&& IsLiveWindow(window)
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
				&& IsLiveWindow(w)
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

			PruneDeadWindows();
			ConsolidateDuplicateScenes();
			return _scenes;
		}

		public IEnumerable<IWindow> GetCurrentWindows()
		{
			var currentDesktopId = GetCurrentDesktopId();
			EnsureSceneIntegrity();
			return _current?.Windows.Where(w => IsLiveWindow(w) && IsWindowOnCurrentDesktop(w, currentDesktopId)) ?? GetSceneableWindows(currentDesktopId);
		}

		private string GetWindowGroupKey(IWindow window) => window.ProcessName;

		private void EnsureSceneIntegrity()
		{
			PruneDeadWindows();
			ConsolidateDuplicateScenes();
		}

		private void PruneDeadWindows()
		{
			if (_scenes is null)
				return;

			foreach (var scene in _scenes.ToArray())
			{
				foreach (var window in scene.Windows.Where(w => !IsLiveWindow(w)).ToArray())
				{
					scene.Remove(window);
					if (scene.Windows.Any())
						SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Updated));
					else
					{
						_scenes.Remove(scene);
						SceneChanged?.Invoke(this, new SceneChangedEventArgs(scene, window, ChangeType.Removed));
					}
				}
			}
		}

		private void ConsolidateDuplicateScenes()
		{
			if (_scenes is null)
				return;

			foreach (var group in _scenes
				.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
				.Where(g => g.Count() > 1)
				.ToArray())
			{
				var keeper = group
					.OrderByDescending(s => s.HasFocus)
					.ThenByDescending(s => s.Windows.Count())
					.First();

				foreach (var duplicate in group.Where(s => !ReferenceEquals(s, keeper)).ToArray())
				{
					foreach (var window in duplicate.Windows.ToArray())
						keeper.Add(window);

					_scenes.Remove(duplicate);
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(duplicate, duplicate.Windows.FirstOrDefault() ?? keeper.Windows.First(), ChangeType.Removed));
					SceneChanged?.Invoke(this, new SceneChangedEventArgs(keeper, keeper.Windows.First(), ChangeType.Updated));
				}
			}
		}

		private bool IsLiveWindow(IWindow window) => window.Handle != IntPtr.Zero && Win32.IsWindow(window.Handle);

		private (Scene Scene, IWindow Window)? FindOwnerWindow(IntPtr handle)
		{
			var ownerHandle = Win32.GetWindow(handle, Win32.GW.GW_OWNER);
			if (FindTrackedWindow(ownerHandle) is (Scene, IWindow) owner)
				return owner;

			var rootOwnerHandle = Win32.GetAncestor(handle, Win32.GA.GA_ROOTOWNER);
			if (rootOwnerHandle != handle && FindTrackedWindow(rootOwnerHandle) is (Scene, IWindow) rootOwner)
				return rootOwner;

			return null;
		}

		private (Scene Scene, IWindow Window)? FindTrackedWindow(IntPtr handle)
		{
			if (handle == IntPtr.Zero || FindSceneForWindow(handle) is not Scene scene)
				return null;

			var window = scene.Windows.FirstOrDefault(w => w.Handle == handle);
			return window is null ? null : (scene, window);
		}
	}
}
