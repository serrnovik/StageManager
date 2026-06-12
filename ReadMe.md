# Stage Manager for Windows

StageManager brings a macOS-style [Stage Manager](https://support.apple.com/en-us/HT213315) workflow to Windows. It keeps your active window in focus while showing live previews and app icons along the side so you can quickly switch between your current work contexts.

This is an alpha build, but it is now usable for daily testing: it supports **multiple virtual workspaces (desktops)**, **application window grouping**, tray controls, global shortcuts, and automated Windows installer builds.

![Stage Manager](media/StageManagerV0.1.png)

## Recommended Companion Setup

For the best day-to-day experience, use StageManager together with [PowerToys FancyZones](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones). FancyZones keeps your main work area predictable while StageManager keeps the current app/window set easy to switch from the side.

![Recommended FancyZones layout for StageManager](media/Fancy_zones_recomendation.png)

## Key Features

- **Stage-style window switching**: Keep one focused window visible while the rest of your current workspace stays available as side previews.
- **Live window previews**: Switch using DWM thumbnails with app icons, hover zoom, and grouped stack previews when several windows share a slot.
- **App and window grouping**: Group related windows, open a group picker when needed, and select individual windows without bringing the whole group forward.
- **Virtual desktop awareness**: Show windows from the current Windows virtual desktop and avoid mixing apps from other desktops into the stage.
- **Tray controls**: Toggle StageManager on/off, hide or show the stage icons, open settings, visit the project page, start with Windows, or exit from the tray menu.
- **Configurable shortcuts**: Configure global shortcuts for toggling StageManager and explicitly showing stage items when they are hidden.
- **Windows installer builds**: Install from the generated Inno Setup installer published by GitHub Actions releases.

## Usage

Download the latest installer or:
- Clone this repository
- `cd` into the repository directory
- Run `dotnet run --project StageManager`

To quit, find the app's tray icon (Windows might move it into the overflow menu) and use its context menu to close the app.
 
### Requirements
- Windows 10 version 2004 or newer
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download)


---

This project is based on [awaescher/StageManager](https://github.com/awaescher/StageManager), the original Windows Stage Manager feasibility prototype.

Stage Manager is using a few code files to handle window tracking from [workspacer](https://github.com/workspacer/workspacer), an amazing open source project by [Rick Button](https://github.com/rickbutton).
