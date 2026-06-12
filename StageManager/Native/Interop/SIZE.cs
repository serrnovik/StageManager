using System.Runtime.InteropServices;

namespace StageManager.Native.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }
}
