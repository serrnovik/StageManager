using StageManager.Native;
using StageManager.Native.Window;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StageManager.Model
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class WindowModel : INotifyPropertyChanged
	{
		//If you get 'dllimport unknown'-, then add 'using System.Runtime.InteropServices;'
		[DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DeleteObject([In] IntPtr hObject);

		private IWindow _window;
		private ImageSource _iconSource;

		public event PropertyChangedEventHandler PropertyChanged;

		public WindowModel(IWindow window)
		{
			Window = window ?? throw new ArgumentNullException(nameof(window));
		}

		private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}

		public string Title => _window.Title.Length > 20 ? _window.Title.Substring(0, 17) + " ..." : _window.Title;

		public ImageSource ImageSourceFromBitmap(System.Drawing.Bitmap bmp)
		{
			if (bmp is null)
				return null;

			var handle = bmp.GetHbitmap();
			try
			{
				return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			}
			finally { DeleteObject(handle); }
		}

		public static ImageSource IconToImageSource(System.Drawing.Icon icon)
		{
			if (icon is null)
				return null;

			var imageSource = Imaging.CreateBitmapSourceFromHIcon(
				icon.Handle,
				Int32Rect.Empty,
				BitmapSizeOptions.FromEmptyOptions());

			imageSource.Freeze();
			return imageSource;
		}

		public ImageSource Icon
		{
			get
			{
				if (_iconSource is null && Window is WindowsWindow window)
				{
					using var icon = window.ExtractIcon();
					_iconSource = IconToImageSource(icon);
				}

				return _iconSource;
			}
		}

		public IWindow Window
		{
			get => _window;
			set
			{
				_window = value;

				RaisePropertyChanged();
				RaisePropertyChanged(nameof(Title));
				RaisePropertyChanged(nameof(Handle));
				RaisePropertyChanged(nameof(Icon));
			}
		}

		public IntPtr Handle => _window?.Handle ?? IntPtr.Zero;

		public void Focus() => _window?.Focus();
	}
}
