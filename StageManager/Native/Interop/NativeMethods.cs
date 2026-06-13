using System;
using System.Runtime.InteropServices;

namespace StageManager.Native.Interop
{
    public static class NativeMethods
    {
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr thumb);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out SIZE size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnail, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, DWMWINDOWATTRIBUTE dwAttribute, out bool pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, DWMWINDOWATTRIBUTE dwAttribute, ref int pvAttribute, int cbAttribute);
    }
}
