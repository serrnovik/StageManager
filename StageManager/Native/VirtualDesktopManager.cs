using StageManager.Native.PInvoke;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
	internal sealed class VirtualDesktopManager
	{
		private readonly IVirtualDesktopManager _manager;
		private Guid? _lastKnownCurrentDesktopId;

		public VirtualDesktopManager()
		{
			_manager = (IVirtualDesktopManager)new CVirtualDesktopManager();
		}

		public void MoveWindowToCurrentDesktop(IntPtr windowHandle)
		{
			MoveWindowToCurrentDesktop(windowHandle, Enumerable.Empty<IntPtr>());
		}

		public void MoveWindowToCurrentDesktop(IntPtr windowHandle, IEnumerable<IntPtr> candidateWindowHandles)
		{
			if (windowHandle == IntPtr.Zero)
				return;

			var currentDesktopId = GetCurrentDesktopId(candidateWindowHandles, windowHandle);
			if (currentDesktopId is null)
				return;

			if (TryGetWindowDesktopId(windowHandle, out var windowDesktopId) && windowDesktopId == currentDesktopId.Value)
				return;

			var targetDesktopId = currentDesktopId.Value;
			_manager.MoveWindowToDesktop(windowHandle, ref targetDesktopId);
		}

		public Guid? GetCurrentDesktopId(IntPtr excludedWindowHandle = default)
		{
			return GetCurrentDesktopId(Enumerable.Empty<IntPtr>(), excludedWindowHandle);
		}

		public Guid? GetCurrentDesktopId(IEnumerable<IntPtr> candidateWindowHandles, IntPtr excludedWindowHandle = default)
		{
			// The foreground window is a poor witness: shell panels (Start, Win+., notifications) and windows
			// pinned to all desktops report desktop ids that are not the current desktop. So known app windows
			// that Windows confirms to be on the current desktop vote first, the foreground window is a fallback.
			var votes = new List<Guid>();
			foreach (var handle in candidateWindowHandles.Where(h => h != IntPtr.Zero && h != excludedWindowHandle).Distinct())
			{
				if (TryGetDesktopIdOfCurrentDesktopWindow(handle, out var desktopId))
					votes.Add(desktopId);

				if (votes.Count >= MAX_DESKTOP_VOTES)
					break;
			}

			if (PickMostCommonDesktopId(votes) is Guid votedDesktopId)
				return _lastKnownCurrentDesktopId = votedDesktopId;

			var foregroundHandle = Win32.GetForegroundWindow();
			if (foregroundHandle != excludedWindowHandle && TryGetDesktopIdOfCurrentDesktopWindow(foregroundHandle, out var foregroundDesktopId))
				return _lastKnownCurrentDesktopId = foregroundDesktopId;

			return _lastKnownCurrentDesktopId;
		}

		private const int MAX_DESKTOP_VOTES = 5;

		internal static Guid? PickMostCommonDesktopId(IEnumerable<Guid> desktopIds)
		{
			return desktopIds
				.Where(id => id != Guid.Empty)
				.GroupBy(id => id)
				.OrderByDescending(group => group.Count())
				.Select(group => (Guid?)group.Key)
				.FirstOrDefault();
		}

		private bool TryGetDesktopIdOfCurrentDesktopWindow(IntPtr windowHandle, out Guid desktopId)
		{
			desktopId = default;

			if (!IsWindowOnCurrentDesktop(windowHandle))
				return false;

			return TryGetWindowDesktopId(windowHandle, out desktopId) && desktopId != Guid.Empty;
		}

		public bool IsWindowOnCurrentDesktop(IntPtr windowHandle)
		{
			if (windowHandle == IntPtr.Zero)
				return false;

			if (Win32Helper.IsCloaked(windowHandle))
				return false;

			return _manager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isOnCurrentDesktop) == 0 && isOnCurrentDesktop;
		}

		public bool IsWindowOnDesktop(IntPtr windowHandle, Guid? desktopId)
		{
			if (windowHandle == IntPtr.Zero)
				return false;

			if (IsWindowOnCurrentDesktop(windowHandle))
				return true;

			if (desktopId is object && TryGetWindowDesktopId(windowHandle, out var windowDesktopId))
				return windowDesktopId == desktopId.Value;

			return IsWindowOnCurrentDesktop(windowHandle);
		}

		private bool TryGetWindowDesktopId(IntPtr windowHandle, out Guid desktopId)
		{
			desktopId = default;
			return windowHandle != IntPtr.Zero && _manager.GetWindowDesktopId(windowHandle, out desktopId) == 0;
		}

		[ComImport]
		[Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
		private class CVirtualDesktopManager
		{
		}

		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
		private interface IVirtualDesktopManager
		{
			[PreserveSig]
			int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);

			[PreserveSig]
			int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

			[PreserveSig]
			int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
		}
	}
}
