<p align="center">
  <img src="assets/slippery-taskbar.jpg" alt="Slippery Taskbar artwork" width="420">
</p>

# Slippery Taskbar

A lightweight Windows 11 taskbar hider inspired by [ButteryTaskbar2](https://github.com/LuisThiamNye/ButteryTaskbar2).

The goal is simple: keep the taskbar completely out of the way until you intentionally invoke Windows shell UI, especially with the **Windows key**.

## v0.3 behavior

Earlier builds used Windows' built-in taskbar auto-hide state. That turned out to be the wrong mechanism because Windows deliberately reveals an auto-hidden taskbar whenever the mouse touches the screen edge.

Starting with v0.3, Slippery Taskbar owns visibility itself:

- Windows native auto-hide is temporarily disabled while Slippery Taskbar hiding is enabled.
- Moving the mouse to the bottom edge **does not reveal a hidden taskbar**.
- Pressing either Windows key immediately reveals it.
- Once the taskbar has intentionally been shown, keeping the pointer over the taskbar keeps it visible long enough to interact with the notification area.
- Opening **Show hidden icons** keeps the taskbar visible while the overflow flyout is open.
- Start, Search, Task View, Quick Settings, Notification Center and other known Windows shell surfaces also keep it visible.
- Slippery Taskbar's own tray menu is protected from the hide loop while the menu is open.
- Your original Windows auto-hide setting is restored when Slippery Taskbar is disabled or exits normally.

## Preferences

Slippery Taskbar now creates a normal per-user preferences folder:

```text
%LOCALAPPDATA%\Slippery-Taskbar\
```

Settings are stored in:

```text
%LOCALAPPDATA%\Slippery-Taskbar\config.json
```

A typical file looks like:

```json
{
  "version": 1,
  "enabled": true,
  "start_with_windows": false,
  "keep_visible_while_interacting": true
}
```

The tray menu has **Open preferences folder** so you do not need to find the directory manually. Preferences are loaded on application startup; restart Slippery Taskbar after manually editing `config.json`.

Existing ButteryTaskbar11 / early Slippery Taskbar registry settings are migrated into the preferences file on first run.

## Features

- Windows 11-focused taskbar hide/show behavior.
- Windows key detected through Raw Input plus `GetAsyncKeyState` polling.
- Primary and secondary taskbar support.
- Multi-monitor support.
- Recovery after `explorer.exe` / taskbar restarts.
- Restores the notification icon after Explorer restarts.
- Hidden-icons overflow detection for multiple Windows 11 shell implementations, including:
  - `TopLevelWindowForOverflowXamlIsland`
  - `XamlExplorerHostIslandWindow`
  - `NotifyIconOverflowWindow`
- `Ctrl + Win + F11` toggles hiding on/off.
- Tray menu:
  - Enable taskbar hiding
  - Show taskbar for 5 seconds
  - Start with Windows
  - Open preferences folder
  - Copy diagnostics
  - Exit
- Explorer `.exe` icon and tray icon use the Slippery Taskbar artwork.
- No .NET runtime.
- Single executable.

## Run it

Download `SlipperyTaskbar.exe` from the latest GitHub Release.

1. Run the executable.
2. The taskbar stays visible briefly and then hides.
3. Moving the mouse to the bottom edge should **not** make it appear.
4. Press the Windows key. The taskbar should appear immediately.
5. Move the pointer onto the visible taskbar and click **Show hidden icons** (`^`).
6. The taskbar and overflow flyout should remain visible until you dismiss the flyout normally.
7. Right-click the Slippery Taskbar notification icon for settings.

The executable is unsigned, so Windows SmartScreen may warn about it.

## Build locally

Requirements:

- Go 1.23+

On Windows PowerShell:

```powershell
./build.ps1
```

The build script embeds Windows version/manifest metadata plus the project artwork as the Explorer file icon, builds the Windows x64 GUI executable, and writes `SHA256SUMS.txt`.

You can also build directly:

```powershell
go build -trimpath -ldflags "-H=windowsgui -s -w" -o SlipperyTaskbar.exe .
```

Direct Go builds still contain the in-app/tray artwork. `build.ps1` and GitHub Actions additionally stamp the Windows executable resources.

## Releases

`.github/workflows/build.yml` builds and vets the project on every push and pull request.

When `VERSION` is changed to a new semantic version on `main`, the workflow creates a GitHub Release containing:

- `SlipperyTaskbar.exe`
- `SHA256SUMS.txt`

Published versions are not overwritten by later commits with the same `VERSION` value.

## Diagnostics

If Windows changes one of its private shell window classes and a panel still causes the taskbar to disappear:

1. Get the taskbar/panel into the broken state.
2. Open the Slippery Taskbar tray menu.
3. Choose **Copy diagnostics**.
4. Paste the result into an issue.

Diagnostics include the Windows build, preferences path, native auto-hide state, detected foreground shell window, overflow state and taskbar handles. They do not copy documents or typed text.

## Compatibility notes

The implementation targets the stock Windows 11 taskbar and uses Win32 APIs. ExplorerPatcher, StartAllBack and some Windhawk taskbar mods can change taskbar window classes or geometry and may require extra compatibility handling.

## Project artwork

Artwork lives under `assets/` and is used for the project page, notification icon and packaged Windows executable.

## Credits

Inspired by Luis Thiam Nye's / CrypticButter's Buttery Taskbar projects. Slippery Taskbar is a new implementation rather than a port of the Jai source.
