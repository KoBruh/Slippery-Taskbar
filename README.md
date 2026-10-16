# Slippery Taskbar

A lightweight C#/.NET app for Windows 11 that hides the taskbar until you intentionally reveal it with the Windows key or shell UI.

## Download

Grab the latest `SlipperyTaskbar-Setup` from **Releases**.

Features: no bottom-edge reveal, Win-key reveal, tray controls, multi-monitor support, startup option, and full-height maximized windows.

## Build from source

- Windows built-in .NET Framework compiler: run `src\build-framework.cmd`
- .NET 8 SDK: run `src\publish-net8.ps1`

Source is in `src/`. Preferences are stored in `%LOCALAPPDATA%\Slippery-Taskbar\config.json`.
