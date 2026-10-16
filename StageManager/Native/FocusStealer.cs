using StageManager.Native.PInvoke;
using System;

namespace StageManager.Native
{
    public static class FocusStealer
    {
        public static void Steal(IntPtr windowToFocus)
        {
            if (Win32.SetForegroundWindow(windowToFocus) && Win32.GetForegroundWindow() == windowToFocus)
                return;

            // Windows only lets the foreground thread hand out activation. Temporarily share the input
            // state with that thread instead of injecting fake keystrokes, which used to leak into
            // IMEs and shell panels like the emoji picker (Win+.).
            var foregroundThread = Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out _);
            var currentThread = Win32.GetCurrentThreadId();

            if (foregroundThread == 0 || foregroundThread == currentThread)
            {
                Win32.SetForegroundWindow(windowToFocus);
                return;
            }

            if (!Win32.AttachThreadInput(currentThread, foregroundThread, true))
            {
                Win32.SetForegroundWindow(windowToFocus);
                return;
            }

            try
            {
                Win32.BringWindowToTop(windowToFocus);
                Win32.SetForegroundWindow(windowToFocus);
            }
            finally
            {
                Win32.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }
}
