using StageManager.Native.PInvoke;
using StageManager.Native.Window;

namespace StageManager.Strategies
{
    internal class NormalizeAndMinimizeWindowStrategy : IWindowStrategy
	{
		public void Show(IWindow window)
		{
			// only restore minimized windows, calling ShowWindow on a visible (maximized) window may change its state
			if (Win32.IsIconic(window.Handle))
				Win32.ShowWindow(window.Handle, Win32.SW.SW_SHOWNOACTIVATE);
		}

		public void Hide(IWindow window)
		{
			if (Win32.IsIconic(window.Handle))
				return;

			// minimizing background windows must not move the activation around, which would emit
			// foreground events for windows of other scenes. Only the foreground window itself has to
			// give up the activation when it is minimized.
			var showCommand = window.Handle == Win32.GetForegroundWindow() ? Win32.SW.SW_MINIMIZE : Win32.SW.SW_SHOWMINNOACTIVE;
			Win32.ShowWindow(window.Handle, showCommand);
		}
	}
}
