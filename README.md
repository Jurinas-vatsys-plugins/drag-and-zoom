# vatSys Persistent Pan

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
- Restores vatSys native middle-button behaviour when another pan button is selected.
- Adds mouse-wheel zoom using vatSys's configured range limits.
- Optionally disables the native ASD right-button zoom box.
- Provides an optional, user-recorded keyboard shortcut for the settings window.
- Adds an enable/disable control to the vatSys top menu.
- Writes diagnostic logs beside the plugin DLL.

## Installation

Download `vatSys.PersistentPan.dll` from the [Releases](https://github.com/Jurinas-vatsys-plugins/drag-and-zoom/releases) page.

The DLL can be installed for one Profile:

```text
C:\Users\<username>\Documents\vatSys Files\Profiles\<Profile Name>\Plugins\vatSys.PersistentPan.dll
```

Or globally for the vatSys installation:

```text
<vatSys installation>\bin\Plugins\vatSys.PersistentPan.dll
```

The vatSys plugin loader scans both locations. Install the DLL in only one location. Use the Profile directory for Profile-specific behaviour, or `bin\Plugins` to make the plugin available to all Profiles.

Restart vatSys after installing or replacing the DLL.

The DLL must be built against the exact `vatSys.exe` that loads it. A DLL built against a different vatSys release may not load.

## Configuration

Open the top menu and select:

```text
Settings -> Controls -> Settings...
```

The settings window contains:

### Plugin state

The top menu contains one mutually exclusive state selector:

```text
Settings -> Controls -> Plugin state -> Active / Inactive
```

`Inactive` makes the plugin release all ASD input handling. vatSys native behaviour is then restored without restarting vatSys.

### Pan drag mouse button

Click the mouse-button capture button and press the desired mouse button. The available choices are:

- Middle Button
- Left Button
- Right Button
- XButton 1
- XButton 2

The default is Middle Button. When another button is selected, the plugin no longer handles the middle button and vatSys receives its native middle-button input.

### Native right-button zoom box

This option is available only in the settings window. It is intentionally not duplicated in the top menu.

- Enabled: vatSys keeps its native right-button drag zoom box.
- Disabled: the native right-button drag zoom box is suppressed.

If Right Button is selected as the pan button, the option is automatically forced to Disabled and locked. Right-button drag is then reserved for persistent panning, so it cannot also be used for the native zoom box.

### Settings shortcut

No shortcut is assigned by default. To assign one:

1. Click the keyboard capture button.
2. Press the desired key combination.
3. Release the key combination.
4. Save the settings.

`Escape`, `Backspace`, `Delete`, or middle-click clears the shortcut. The shortcut is optional and can be left as `None` to avoid conflicts with other plugins.

Settings are stored beside the DLL in:

```text
PersistentPan.settings
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
PersistentPan.log
```

beside the DLL. The log records plugin loading, menu registration, settings changes, pan input, wheel input, and zoom range changes.

If no log is created:

1. Confirm that the DLL is in the active Profile's `Plugins` directory or in the vatSys `bin\Plugins` directory.
2. Close every vatSys process.
3. Check that Windows has not blocked the DLL.
4. Confirm that the DLL was built against the same `vatSys.exe` release.

## Troubleshooting DLL loading

Windows may block DLL files downloaded from the Internet.

1. Close vatSys completely.
2. Right-click `vatSys.PersistentPan.dll`.
3. Select **Properties**.
4. On the **General** tab, enable **Unblock** if it is shown.
5. Click **Apply**, then **OK**.
6. Start vatSys again.

PowerShell alternative:

```powershell
Unblock-File "C:\Users\<username>\Documents\vatSys Files\Profiles\<Profile Name>\Plugins\vatSys.PersistentPan.dll"
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
.\Build-ForVatSys.ps1 -VatSysExe "C:\Program Files (x86)\vatSys\bin\vatSys.exe"
```

Or run MSBuild directly:

```powershell
```

The output is written to:

```text
PersistentPan\bin\Release\vatSys.PersistentPan.dll
```

## Compatibility

The plugin uses vatSys's plugin API and the ASD methods exposed by the installed vatSys executable. Changes to vatSys's assembly version or internal ASD implementation may require a rebuild or compatibility update.

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE).

## Acknowledgements

Developed for the vatSys community.
