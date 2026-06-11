using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace StageManager.Model
{
	[System.Diagnostics.DebuggerDisplay("{Title}")]
	public class SceneModel : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler? PropertyChanged;
		private bool _isVisible;
		private bool _isWindowPickerOpen;
		private bool _isOverflowGroup;
		private string _overflowTitle = "More windows";
		private Scene? _scene;

		public static SceneModel FromScene(Scene scene)
		{
			var model = new SceneModel();
			model.Id = scene.Id;
			model.Windows = new ObservableCollection<WindowModel>(scene.Windows.Select(w => new WindowModel(w)));
			model.UpdateDisplayWindows(_ => true);
			model.Scene = scene;
			return model;
		}

		public static SceneModel CreateOverflowGroup()
		{
			return new SceneModel
			{
				Id = Guid.Empty,
				_isOverflowGroup = true,
				IsVisible = true
			};
		}

		public SceneModel()
		{
			Updated = DateTime.UtcNow;
			RaisePropertyChanged(nameof(HasMultipleWindows));
		}

		public void UpdateDisplayWindows(Func<WindowModel, bool> predicate)
		{
			var windows = Windows.ToArray();
			var displayWindows = windows.Where(predicate).ToArray();

			for (int i = 0; i < displayWindows.Length; i++)
			{
				if (DisplayWindows.Count > i && DisplayWindows[i].Handle == displayWindows[i].Handle)
					continue;

				var windowToMove = DisplayWindows.FirstOrDefault(w => w.Handle == displayWindows[i].Handle);
				if (windowToMove is object)
					DisplayWindows.Move(DisplayWindows.IndexOf(windowToMove), i);
				else
					DisplayWindows.Insert(i, displayWindows[i]);
			}

			for (int i = DisplayWindows.Count - 1; i >= 0; i--)
			{
				if (!displayWindows.Any(w => w.Handle == DisplayWindows[i].Handle))
					DisplayWindows.RemoveAt(i);
			}

			RaisePropertyChanged(nameof(HasMultipleWindows));
			RaisePropertyChanged(nameof(HasDisplayWindows));
			RaisePropertyChanged(nameof(PrimaryDisplayWindow));
		}

		public void UpdateFromScene(Scene updatedScene)
		{
			if (Id != updatedScene.Id)
				throw new NotSupportedException();

			Scene = updatedScene;

			var updatedWindows = updatedScene.Windows.ToArray();
			for (int i = 0; i < updatedWindows.Length; i++)
			{
				if (Windows.Count > i && Windows[i].Window.Handle == updatedWindows[i].Handle)
				{
					// same position - just update
					Windows[i].Window = updatedWindows[i];
				}
				else
				{
					var windowToUpdate = Windows.FirstOrDefault(w => w.Window.Handle == updatedWindows[i].Handle);
					if (windowToUpdate is object)
					{
						// has the window but other position -> update and move
						windowToUpdate.Window = updatedWindows[i];
						Windows.Move(Windows.IndexOf(windowToUpdate), i);
					}
					else
					{
						// no window tp update --> add/insert
						Windows.Insert(i, new WindowModel(updatedWindows[i]));
					}
				}
			}

			// remove windows that have been gone
			if (Windows.Count > updatedScene.Windows.Count())
			{
				for (int i = Windows.Count - 1; i >= 0; i--)
				{
					if (!updatedScene.Windows.Any(w => w.Handle == Windows[i].Window.Handle))
						Windows.RemoveAt(i);
				}
			}

			Updated = DateTime.UtcNow;
			UpdateDisplayWindows(_ => true);
			RaisePropertyChanged(nameof(HasMultipleWindows));
		}

		public void UpdateOverflowWindows(IEnumerable<SceneModel> scenes, string title)
		{
			if (!IsOverflowGroup)
				throw new NotSupportedException();

			Windows.Clear();
			foreach (var window in scenes.SelectMany(s => s.DisplayWindows))
				Windows.Add(window);

			_overflowTitle = title;
			UpdateDisplayWindows(_ => true);
			Updated = DateTime.UtcNow;
			RaisePropertyChanged(nameof(Title));
			RaisePropertyChanged(nameof(HasMultipleWindows));
		}

		private void Scene_SelectedChanged(object? sender, EventArgs e)
		{
			Updated = DateTime.UtcNow;
		}

		public Guid Id { get; set; }

		public Scene? Scene
		{
			get => _scene;
			private set
			{
				if (_scene is object)
					_scene.SelectedChanged -= Scene_SelectedChanged;

				_scene = value;

				if (_scene is object)
					_scene.SelectedChanged += Scene_SelectedChanged;
			}
		}

		public string Title => IsOverflowGroup ? _overflowTitle : Scene?.Title ?? "";

		public bool HasMultipleWindows => IsOverflowGroup || DisplayWindows.Count > 1;

		public bool HasDisplayWindows => DisplayWindows.Count > 0;

		public WindowModel? PrimaryDisplayWindow => DisplayWindows.FirstOrDefault();

		public bool IsOverflowGroup => _isOverflowGroup;

		public bool IsVisible
		{
			get => _isVisible;
			set
			{
				if (_isVisible != value)
				{
					_isVisible = value;
					RaisePropertyChanged();
					RaisePropertyChanged(nameof(Visibility));
				}
			}
		}

		public bool IsWindowPickerOpen
		{
			get => _isWindowPickerOpen;
			set
			{
				if (_isWindowPickerOpen != value)
				{
					_isWindowPickerOpen = value;
					RaisePropertyChanged();
				}
			}
		}

		public DateTime Updated { get; private set; }

		private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}

		public System.Windows.Visibility Visibility => IsVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

		public ObservableCollection<WindowModel> Windows { get; set; } = new ObservableCollection<WindowModel>();

		public ObservableCollection<WindowModel> DisplayWindows { get; } = new ObservableCollection<WindowModel>();
	}
}
