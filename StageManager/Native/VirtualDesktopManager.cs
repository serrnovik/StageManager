using StageManager.Native.PInvoke;
using System;
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
			if (windowHandle == IntPtr.Zero)
				return;

			var foregroundHandle = Win32.GetForegroundWindow();
			if (foregroundHandle == IntPtr.Zero || foregroundHandle == windowHandle)
				return;

			var currentDesktopId = GetCurrentDesktopId(windowHandle);
			if (currentDesktopId is null)
				return;

			if (TryGetWindowDesktopId(windowHandle, out var windowDesktopId) && windowDesktopId == currentDesktopId.Value)
				return;

			var targetDesktopId = currentDesktopId.Value;
			_manager.MoveWindowToDesktop(windowHandle, ref targetDesktopId);
		}

		public Guid? GetCurrentDesktopId(IntPtr excludedWindowHandle = default)
		{
			var foregroundHandle = Win32.GetForegroundWindow();
			if (foregroundHandle != IntPtr.Zero
				&& foregroundHandle != excludedWindowHandle
				&& TryGetWindowDesktopId(foregroundHandle, out var currentDesktopId))
			{
				_lastKnownCurrentDesktopId = currentDesktopId;
			}

			return _lastKnownCurrentDesktopId;
		}

		public bool IsWindowOnCurrentDesktop(IntPtr windowHandle)
		{
			return IsWindowOnDesktop(windowHandle, GetCurrentDesktopId());
		}

		public bool IsWindowOnDesktop(IntPtr windowHandle, Guid? desktopId)
		{
			if (windowHandle == IntPtr.Zero)
				return false;

			if (Win32Helper.IsCloaked(windowHandle))
				return false;

			if (desktopId is object && TryGetWindowDesktopId(windowHandle, out var windowDesktopId))
				return windowDesktopId == desktopId.Value;

			return _manager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isOnCurrentDesktop) == 0 && isOnCurrentDesktop;
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
