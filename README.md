# vatSys Drag-and-zoom

An open-source vatSys plugin that changes ASD navigation so the **right mouse
button drags the map**, while the mouse wheel zooms. The right-button messages
are translated to vatSys's native middle-button pan messages, so the plugin
continues to use vatSys's own map projection and rendering behavior.

Repository: <https://github.com/Jurinas-vatsys-plugins/drag-and-zoom>

## Features

- Hold the right mouse button and drag to pan the ASD map.
- Prevents the native right-click box/zoom action while dragging.
- Keeps the ASD at the position reached after releasing the drag.
- Adds mouse-wheel zooming using vatSys's configured range limits.
- Writes diagnostic input and zoom information to `DragAndZoom.log`.

## Installation

Download `vatSys.DragAndZoom.dll` from the [Releases](https://github.com/Jurinas-vatsys-plugins/drag-and-zoom/releases)
page and copy it into the active vatSys Profile's `Plugins` directory:

```text
C:\Users\<username>\Documents\vatSys Files\Profiles\<Profile Name>\Plugins\vatSys.DragAndZoom.dll
```

Restart vatSys after installing or replacing the DLL. The plugin must be in the
active Profile's `Plugins` directory, not the vatSys installation directory.

This repository also retains the original `PersistentPan` project and DLL.
That plugin is unchanged and uses the middle mouse button. The two plugins are
separate variants; normally install only the one whose mouse-button behavior
you want.

## Usage

1. Move the cursor over the ASD.
2. Press and hold the right mouse button.
3. Drag the map.
4. Release the right mouse button.

The map remains at the final position. Scroll up to zoom in and scroll down to
zoom out.

## Diagnostics

The plugin writes `DragAndZoom.log` beside the DLL:

```text
<Profile>\Plugins\DragAndZoom.log
```

If no log is created, confirm that the DLL is in the active Profile's
`Plugins` directory and restart every vatSys process.

## If vatSys cannot load the DLL

Windows may block DLL files downloaded from the Internet. Close vatSys, open
the DLL's **Properties**, enable **Unblock** on the **General** tab, and click
**Apply**. The same operation can be performed in PowerShell:

```powershell
Unblock-File "C:\Users\<username>\Documents\vatSys Files\Profiles\<Profile Name>\Plugins\vatSys.DragAndZoom.dll"
```

The release DLL must also be compiled against the same vatSys release installed
on the computer.

## Building

The project targets the 32-bit .NET Framework 4.7.2 environment used by vatSys.
Build against the exact vatSys executable that will load the plugin:

```powershell
.\Build-DragAndZoom.ps1 -VatSysExe "C:\Program Files (x86)\vatSys\bin\vatSys.exe"
```

Or run MSBuild directly:

```powershell
dotnet msbuild .\DragAndZoom\DragAndZoom.csproj /p:Configuration=Release /p:Platform=x86 /p:VatSysPath="I:\vatSys\bin\vatSys.exe"
```

The compiled plugin is written to:

```text
DragAndZoom\bin\Release\vatSys.DragAndZoom.dll
```

## License

This project is licensed under the GNU General Public License v3.0. See
[LICENSE](LICENSE).
