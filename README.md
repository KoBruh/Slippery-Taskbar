<p align="center">
  <img src="assets/slippery-taskbar.jpg" alt="Slippery Taskbar artwork" width="420">
</p>

# Slippery Taskbar

A lightweight Windows 11 taskbar hider inspired by [ButteryTaskbar2](https://github.com/LuisThiamNye/ButteryTaskbar2).

This repository is the home of **Slippery Taskbar**, a new implementation focused on modern Windows 11 shell behaviour and smooth taskbar transitions.

The goal is the same: keep the taskbar completely out of the way until you intentionally invoke Windows shell UI. This version is built around current Windows 11 shell behaviour instead of assuming the Start menu always uses one old window class.

## What this fixes

The older app can hide the taskbar again immediately after you press the Windows key because newer Windows 11 builds moved Start, Search, Quick Settings, Notification Center and other shell surfaces between different XAML hosts/processes.

This remake changes the state model:

- The **Windows key itself is authoritative**: pressing either Win key immediately shows the taskbar.
- Win-key input is detected with **Raw Input plus `GetAsyncKeyState` polling**, avoiding reliance on the old low-level keyboard-hook timing.
- The taskbar stays visible while known shell UI is foreground, including Start, Search, Task View, Quick Settings, Notification Center, tray overflow and Win+X menus.
- It recognizes both older Windows 11 hosts (`ShellExperienceHost.exe` / `Windows.UI.Core.CoreWindow`) and newer hosts such as `ShellHost.exe` / `ControlCenterWindow`.
- Taskbar show/hide changes are **edge-triggered** instead of being forced every few milliseconds, so Explorer's own XAML animations are not constantly interrupted.
- The **Show hidden icons** flyout is detected by its actual top-level window (`TopLevelWindowForOverflowXamlIsland`, with legacy `NotifyIconOverflowWindow` fallback), so it stays open even if focus moves elsewhere.
- A short grace period covers transitions between the Windows key, Start and other shell windows without making the taskbar feel sticky.

## October 2026 smoothness update

- Removed the 30 ms repeated taskbar show/reposition loop that could fight Explorer animations.
- Added persistent detection for the Windows 11 hidden-icons overflow flyout.
- Reduced Win-key transition hold time so closing Start feels more immediate.
- Kept a slow hidden-state watchdog for Explorer/taskbar recreation without touching an already-visible taskbar.

## Features

- Primary and secondary taskbar support.
- Multi-monitor support.
- Recovery after `explorer.exe` / taskbar restarts.
- Restores the tray icon after Explorer restarts.
- `Ctrl + Win + F11` toggles hiding on/off.
- Tray menu:
  - Enable taskbar hiding
  - Show taskbar for 5 seconds
  - Start with Windows
  - Copy diagnostics
  - Exit
- Restores your original Windows taskbar auto-hide state on a normal exit.
- No .NET runtime and no third-party libraries.
- Single executable.

## Run it

Use `SlipperyTaskbar.exe` from the release/package root.

1. Run the executable.
2. The taskbar stays visible briefly, then hides.
3. Press the Windows key. The taskbar should appear immediately and remain visible while Start is open.
4. Close Start or switch back to a normal application. The taskbar hides after a short grace period.
5. Open **Show hidden icons** (`^`). The taskbar should remain visible and the overflow flyout should stay open until you dismiss it normally.
6. Right-click the tray icon for settings.

The executable is unsigned, so Windows SmartScreen may warn about it. You can build it yourself from this source if you prefer.

## Build locally

Requirements:

- Go 1.23+
- Windows 11, Windows 10, macOS or Linux can build the source; the output target is Windows x64.

On Windows PowerShell:

```powershell
./build.ps1
```

`build.ps1` automatically uses the Windows SDK/MSVC resource tools when they are installed. That embeds the project artwork as the Explorer `.exe` icon and adds Windows version/manifest metadata. The ICO is also embedded directly in the Go program, so the tray/window icon still works even when the resource tools are not available.

You can also build directly:

```powershell
go build -trimpath -ldflags "-H=windowsgui -s -w" -o SlipperyTaskbar.exe .
```

For cross-compiling from Linux/macOS:

```bash
GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -trimpath -ldflags '-H=windowsgui -s -w' -o SlipperyTaskbar.exe .
```

Direct/cross-compiled builds still contain the in-app/tray artwork; the GitHub Actions and Windows `build.ps1` path produce the fully resource-stamped `.exe`.

## Project artwork

The project artwork lives at `assets/slippery-taskbar.jpg`. A multi-resolution Windows icon is generated/stored at `assets/slippery-taskbar.ico` and is used by both the tray UI and packaged Windows executable.

## GitHub Actions

The included `.github/workflows/build.yml` builds a Windows x64 executable, embeds the icon/Windows metadata, generates `SHA256SUMS.txt`, and uploads both as an Actions artifact on pushes, pull requests and manual workflow runs.

## Diagnostics for future Windows builds

Microsoft can change private shell process/window names in future Windows releases. If one specific shell panel makes the taskbar disappear:

1. Open that panel.
2. Right-click the Slippery Taskbar tray icon.
3. Choose **Copy diagnostics**.
4. Paste the copied text into a bug report.

It includes the Windows build plus the foreground process/class/title and detected taskbar handles, without collecting documents, keystrokes or other personal content.

## Compatibility notes

The implementation targets the stock Windows 11 taskbar and uses public Win32 APIs. ExplorerPatcher, StartAllBack and some Windhawk taskbar mods can change taskbar window classes or geometry and may require extra compatibility handling.

## Credits

Inspired by Luis Thiam Nye's / CrypticButter's Buttery Taskbar projects. Slippery Taskbar is a new implementation rather than a port of the Jai source.
