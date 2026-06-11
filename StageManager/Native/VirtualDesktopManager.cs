using StageManager.Native.PInvoke;
using System;
using System.Runtime.InteropServices;

namespace StageManager.Native
{
	internal sealed class VirtualDesktopManager
	{
		private readonly IVirtualDesktopManager _manager;

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

			if (_manager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isOnCurrentDesktop) == 0 && isOnCurrentDesktop)
				return;

			if (_manager.GetWindowDesktopId(foregroundHandle, out var currentDesktopId) != 0)
				return;

			_manager.MoveWindowToDesktop(windowHandle, ref currentDesktopId);
		}

		public bool IsWindowOnCurrentDesktop(IntPtr windowHandle)
		{
			if (windowHandle == IntPtr.Zero)
				return false;

			return _manager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var isOnCurrentDesktop) == 0 && isOnCurrentDesktop;
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
