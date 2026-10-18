using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StageManager.Native
{
	/// <summary>
	/// DWM cannot render live thumbnails of minimized windows, so a snapshot is taken right before
	/// StageManager minimizes a window and shown instead while the window stays minimized.
	/// </summary>
	internal static class WindowSnapshotCache
	{
		private const int MAX_SNAPSHOT_WIDTH = 480;
		private const uint PW_CLIENTONLY = 0x1;
		private const uint PW_RENDERFULLCONTENT = 0x2;

		private static readonly ConcurrentDictionary<IntPtr, ImageSource> _snapshots = new();

		public static event Action<IntPtr>? SnapshotChanged;

		public static ImageSource? Get(IntPtr handle) => _snapshots.TryGetValue(handle, out var snapshot) ? snapshot : null;

		public static void Remove(IntPtr handle)
		{
			if (_snapshots.TryRemove(handle, out _))
				SnapshotChanged?.Invoke(handle);
		}

		public static void Capture(IntPtr handle)
		{
			try
			{
				var snapshot = TryCapture(handle);
				if (snapshot is null)
					return;

				_snapshots[handle] = snapshot;
				SnapshotChanged?.Invoke(handle);
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
		}

		private static ImageSource? TryCapture(IntPtr handle)
		{
			if (!IsWindowVisible(handle) || IsIconic(handle) || !GetClientRect(handle, out var rect))
				return null;

			var width = rect.right - rect.left;
			var height = rect.bottom - rect.top;
			if (width <= 0 || height <= 0)
				return null;

			var screenDc = GetDC(IntPtr.Zero);
			var memoryDc = CreateCompatibleDC(screenDc);
			var bitmap = CreateCompatibleBitmap(screenDc, width, height);
			var previous = SelectObject(memoryDc, bitmap);

			try
			{
				if (!PrintWindow(handle, memoryDc, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
					return null;

				SelectObject(memoryDc, previous);
				previous = IntPtr.Zero;

				BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
				if (width > MAX_SNAPSHOT_WIDTH)
				{
					var scale = MAX_SNAPSHOT_WIDTH / (double)width;
					source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
				}

				// materialize the (scaled) pixels so the full size capture can be released
				var snapshot = new WriteableBitmap(source);
				snapshot.Freeze();
				return snapshot;
			}
			finally
			{
				if (previous != IntPtr.Zero)
					SelectObject(memoryDc, previous);

				DeleteObject(bitmap);
				DeleteDC(memoryDc);
				ReleaseDC(IntPtr.Zero, screenDc);
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT
		{
			public int left;
			public int top;
			public int right;
			public int bottom;
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsIconic(IntPtr hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

		[DllImport("gdi32.dll")]
		private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

		[DllImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DeleteObject(IntPtr hObject);

		[DllImport("gdi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool DeleteDC(IntPtr hdc);
	}
}
