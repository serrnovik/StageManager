using StageManager.Native.Interop;
using StageManager.Native.PInvoke;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace StageManager
{
	/// <summary>
	/// Interaction logic for DwmThumbnail.xaml
	/// </summary>
	public partial class DwmThumbnail : UserControl
	{
		public DwmThumbnail()
		{
			InitializeComponent();
			LayoutUpdated += DwmThumbnail_LayoutUpdated;
			Loaded += DwmThumbnail_Loaded;
			Unloaded += DwmThumbnail_Unloaded;
		}

		private IntPtr _dwmThumbnail;
		private IntPtr _registeredPreviewHandle;
		private IntPtr _registeredHostHandle;
		private Window? _window;
		private Point? _dpiScaleFactor;
		private RECT? _lastThumbnailRect;
		private bool? _lastThumbnailVisible;

		public static readonly DependencyProperty PreviewHandleProperty = DependencyProperty.Register(nameof(PreviewHandle),
			   typeof(IntPtr),
			   typeof(DwmThumbnail),
			   new PropertyMetadata(IntPtr.Zero));

		public IntPtr PreviewHandle
		{
			get { return (IntPtr)GetValue(PreviewHandleProperty); }
			set { SetValue(PreviewHandleProperty, value); }
		}

		private Point GetDpiScaleFactor()
		{
			if (_dpiScaleFactor is null)
			{
				var source = PresentationSource.FromVisual(this);
				_dpiScaleFactor = source?.CompositionTarget != null ? new Point(source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22) : new Point(1.0d, 1.0d);
			}

			return _dpiScaleFactor.Value;
		}

		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
		{
			_dpiScaleFactor = null;
			base.OnDpiChanged(oldDpi, newDpi);
		}

		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
		{
			base.OnPropertyChanged(e);

			if (nameof(PreviewHandle).Equals(e.Property.Name))
			{
				ReleaseCapture();
				StartCapture();
				UpdateThumbnailProperties();
			}

			if (nameof(IsVisible).Equals(e.Property.Name) && !(bool)e.NewValue)
			{
				ReleaseCapture();
			}
		}

		private void DwmThumbnail_Loaded(object sender, RoutedEventArgs e)
		{
			StartCapture();
			UpdateThumbnailProperties();
		}

		private void DwmThumbnail_Unloaded(object sender, RoutedEventArgs e)
		{
			ReleaseCapture();
		}

		private void DwmThumbnail_LayoutUpdated(object? sender, EventArgs e)
		{
			StartCapture();
			UpdateThumbnailProperties();
		}

		public static Rect BoundsRelativeTo(FrameworkElement element, Visual relativeTo)
		{
			var topLeft = element.TransformToVisual(relativeTo).Transform(new Point(0, 0));
			return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
		}

		private void StartCapture()
		{
			if (PreviewHandle == IntPtr.Zero)
				return;

			var windowHandle = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
			if (windowHandle == IntPtr.Zero)
				return;

			if (_dwmThumbnail != IntPtr.Zero && _registeredHostHandle == windowHandle && _registeredPreviewHandle == PreviewHandle)
				return;

			ReleaseCapture();

			var hr = NativeMethods.DwmRegisterThumbnail(windowHandle, PreviewHandle, out _dwmThumbnail);
			if (hr != 0)
				return;

			_registeredHostHandle = windowHandle;
			_registeredPreviewHandle = PreviewHandle;
		}

		private void ReleaseCapture()
		{
			if (_dwmThumbnail != IntPtr.Zero)
				NativeMethods.DwmUnregisterThumbnail(_dwmThumbnail);

			_dwmThumbnail = IntPtr.Zero;
			_registeredHostHandle = IntPtr.Zero;
			_registeredPreviewHandle = IntPtr.Zero;
			_lastThumbnailRect = null;
			_lastThumbnailVisible = null;
		}

		private Window? FindWindow() => _window ??= Window.GetWindow(this);

		private Visual? FindHostVisual()
		{
			return PresentationSource.FromVisual(this)?.RootVisual as Visual ?? FindWindow();
		}

		private void UpdateThumbnailProperties()
		{
			if (_dwmThumbnail == IntPtr.Zero)
				return;

			var dpi = GetDpiScaleFactor();

			var host = FindHostVisual();
			if (host is null)
				return;

			Rect previewBounds;
			try
			{
				previewBounds = BoundsRelativeTo(this, host);
			}
			catch (InvalidOperationException)
			{
				ReleaseCapture();
				return;
			}

			var thumbnailRect = CreateDestinationRect(previewBounds, dpi);

			if (HasVisibleOwnedPopup())
			{
				UpdateThumbnailVisibility(false);
				_lastThumbnailRect = null;
				return;
			}

			if (_lastThumbnailRect is RECT last
				&& _lastThumbnailVisible == true
				&& last.top == thumbnailRect.top
				&& last.left == thumbnailRect.left
				&& last.bottom == thumbnailRect.bottom
				&& last.right == thumbnailRect.right)
			{
				return;
			}

			var props = new DWM_THUMBNAIL_PROPERTIES
			{
				fVisible = true,
				dwFlags = (int)(DWM_TNP.DWM_TNP_VISIBLE | DWM_TNP.DWM_TNP_OPACITY | DWM_TNP.DWM_TNP_RECTDESTINATION | DWM_TNP.DWM_TNP_SOURCECLIENTAREAONLY),
				opacity = 255,
				rcDestination = thumbnailRect,
				fSourceClientAreaOnly = true
			};
			NativeMethods.DwmUpdateThumbnailProperties(_dwmThumbnail, ref props);
			_lastThumbnailRect = thumbnailRect;
			_lastThumbnailVisible = true;
		}

		private bool HasVisibleOwnedPopup()
		{
			if (PreviewHandle == IntPtr.Zero)
				return false;

			var popup = Win32.GetLastActivePopup(PreviewHandle);
			return popup != IntPtr.Zero
				&& popup != PreviewHandle
				&& Win32.IsWindowVisible(popup);
		}

		private void UpdateThumbnailVisibility(bool isVisible)
		{
			if (_lastThumbnailVisible == isVisible)
				return;

			var props = new DWM_THUMBNAIL_PROPERTIES
			{
				dwFlags = (int)DWM_TNP.DWM_TNP_VISIBLE,
				fVisible = isVisible
			};

			NativeMethods.DwmUpdateThumbnailProperties(_dwmThumbnail, ref props);
			_lastThumbnailVisible = isVisible;
		}

		private static RECT CreateDestinationRect(Rect bounds, Point dpi)
		{
			var left = (int)Math.Round(bounds.Left * dpi.X);
			var top = (int)Math.Round(bounds.Top * dpi.Y);
			var width = Math.Max(0, (int)Math.Round(bounds.Width * dpi.X));
			var height = Math.Max(0, (int)Math.Round(bounds.Height * dpi.Y));

			return new RECT
			{
				left = left,
				top = top,
				right = left + width,
				bottom = top + height
			};
		}
	}
}
