# Drag and Zoom

An open-source vatSys plugin for persistent ASD panning and mouse-wheel zoom.

Repository: <https://github.com/Jurinas-vatsys-plugins/drag-and-zoom>

## Features

- Keeps the ASD at the position reached after panning.
- Supports a configurable pan mouse button:
  - Middle Button (default)
  - Left Button
  - Right Button
  - XButton 1
  - XButton 2
- Keeps the pan button's normal click: a click goes to vatSys, a drag pans.
- Restores vatSys native middle-button behaviour when another pan button is selected.
- Adds mouse-wheel zoom using vatSys's configured range limits.
- Optionally disables the native ASD right-button zoom box.
- Provides an optional, user-recorded keyboard shortcut for the settings window.
- Uses a vatSys-style settings window and adds an enable/disable control to the vatSys top menu.
- Writes diagnostic logs beside the plugin DLL.

## Installation

Download `vatSys.DragAndZoom.dll` from the [Releases](https://github.com/Jurinas-vatsys-plugins/drag-and-zoom/releases) page.

The DLL can be installed for one Profile:

```text
C:\Users\<username>\Documents\vatSys Files\Profiles\<Profile Name>\Plugins\vatSys.DragAndZoom.dll
```

Or globally for the vatSys installation:

```text
<vatSys installation>\bin\Plugins\vatSys.DragAndZoom.dll
```

The vatSys plugin loader scans both locations. Install the DLL in only one location. Use the Profile directory for Profile-specific behaviour, or `bin\Plugins` to make the plugin available to all Profiles.

Close vatSys before installing or replacing the DLL; a running vatSys keeps the file locked. Start vatSys again afterwards.

The DLL must be built against the exact `vatSys.exe` that loads it. A DLL built against a different vatSys release may not load.

### Upgrading from Persistent Pan

Up to v1.4.0 this plugin was called Persistent Pan and shipped as `vatSys.PersistentPan.dll`. When upgrading:

1. Close vatSys.
2. Delete every `vatSys.PersistentPan.dll` (in `bin\Plugins` and in each Profile's `Plugins` directory). Otherwise both plugins load and handle the mouse twice.
3. Install `vatSys.DragAndZoom.dll` as described above.

Existing settings in `PersistentPan.settings` are read automatically on the first start and saved to `DragAndZoom.settings` from then on.

The earlier separate right-button-only "Drag-and-zoom" plugin is replaced by this one. To get its behaviour, select Right Button as the pan button.

## Configuration

Open the top menu and select:

```text
Settings -> Drag and Zoom -> Settings...
```

### Plugin state

The top menu contains one mutually exclusive state selector:

```text
Settings -> Drag and Zoom -> Plugin state -> Active / Inactive
```

`Inactive` makes the plugin release all ASD input handling. vatSys native behaviour is then restored without restarting vatSys.

### Pan Button

Click the mouse-button capture button and press the desired mouse button on it. The available choices are:

- Middle Button
- Left Button
- Right Button
- XButton 1
- XButton 2

The default is Middle Button. When another button is selected, the plugin no longer handles the middle button and vatSys receives its native middle-button input.

### Keep Normal Click

Applies when a pan button other than Middle Button is selected. Enabled by default.

- Enabled: a press that is released without moving is passed to vatSys as a normal click with that button (for example, left-click still selects tracks). The map pans only once the cursor moves beyond the Windows drag threshold (a few pixels) while the button is held.
- Disabled: every press of the selected button pans and never reaches vatSys.

Starting a pan with a non-middle button never triggers vatSys's middle-click functions (such as the label colour change). While a two-click vatSys function is in progress (moving a label, zoom box, BRL, reroute), the next press of the pan button goes to vatSys to complete it instead of panning.

### Disable Zoom Box

vatSys's zoom box is a two-click function: the first right-click on an empty part of the ASD shows a box that follows the mouse, and the next right-click zooms to it.

- Off: vatSys keeps its native right-button zoom box.
- On: the zoom box is suppressed. Other right-click functions, such as moving a track label, keep working.

If Right Button is selected as the pan button, this option is forced on and locked.

### Settings shortcut

No shortcut is assigned by default. To assign one:

1. Click the keyboard capture button.
2. Press the desired key combination.
3. Release the key combination.
4. Save the settings.

`Escape`, `Backspace`, `Delete`, or middle-click clears the shortcut. The shortcut is optional and can be left as `None` to avoid conflicts with other plugins.

`Save` stores the settings; `Cancel` or the close button discards unsaved changes. Settings are stored beside the DLL in:

```text
DragAndZoom.settings
```

## Usage

1. Move the cursor over the ASD.
2. Press and hold the configured pan mouse button.
3. Drag the map.
4. Release the button.

The ASD remains at the final map position.

Use the mouse wheel to zoom in and out. The plugin uses vatSys's native range limits and rendering APIs.

## Diagnostics

The plugin writes:

```text
DragAndZoom.log
```

beside the DLL. The log records plugin loading, menu registration, settings changes, pan input, passed-through clicks, wheel input, and zoom range changes.

If no log is created:

1. Confirm that the DLL is in the active Profile's `Plugins` directory or in the vatSys `bin\Plugins` directory.
2. Close every vatSys process.
3. Check that Windows has not blocked the DLL.
4. Confirm that the DLL was built against the same `vatSys.exe` release.

## Troubleshooting DLL loading

Windows may block DLL files downloaded from the Internet.

1. Close vatSys completely.
2. Right-click `vatSys.DragAndZoom.dll`.
3. Select **Properties**.
4. On the **General** tab, enable **Unblock** if it is shown.
5. Click **Apply**, then **OK**.
6. Start vatSys again.

PowerShell alternative:

```powershell
Unblock-File "C:\Users\<username>\Documents\vatSys Files\Profiles\<Profile Name>\Plugins\vatSys.DragAndZoom.dll"
```

## Building

The project targets the 32-bit .NET Framework 4.7.2 environment used by vatSys.

Requirements:

- Windows
- .NET Framework 4.7.2 Developer Pack
- MSBuild
- The vatSys installation that will load the plugin

Build against the exact vatSys executable:

```powershell
.\Build-DragAndZoom.ps1 -VatSysExe "C:\Program Files (x86)\vatSys\bin\vatSys.exe"
```

Or run MSBuild directly:

```powershell
dotnet msbuild DragAndZoom\DragAndZoom.csproj -p:Configuration=Release -p:Platform=x86 "-p:VatSysPath=C:\Program Files (x86)\vatSys\bin\vatSys.exe"
```

The output is written to:

```text
DragAndZoom\bin\Release\vatSys.DragAndZoom.dll
```

## Compatibility

The plugin uses vatSys's plugin API, its window and control classes, and the ASD methods exposed by the installed vatSys executable. Changes to vatSys's assembly version or internal ASD implementation may require a rebuild or compatibility update.

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE).

## Acknowledgements

Developed for the vatSys community.
