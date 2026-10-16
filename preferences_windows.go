//go:build windows

package main

import (
	"encoding/json"
	"os"
	"os/exec"
	"path/filepath"
)

type preferences struct {
	Version                     int  `json:"version"`
	Enabled                     bool `json:"enabled"`
	StartWithWindows            bool `json:"start_with_windows"`
	KeepVisibleWhileInteracting bool `json:"keep_visible_while_interacting"`
}

var prefs = preferences{
	Version:                     1,
	Enabled:                     true,
	StartWithWindows:            false,
	KeepVisibleWhileInteracting: true,
}

func preferencesDirectory() string {
	base := os.Getenv("LOCALAPPDATA")
	if base == "" {
		if cache, err := os.UserCacheDir(); err == nil { base = cache }
	}
	if base == "" { base = "." }
	return filepath.Join(base, "Slippery-Taskbar")
}

func preferencesFilePath() string { return filepath.Join(preferencesDirectory(), "config.json") }

func loadPreferencesFile() bool {
	data, err := os.ReadFile(preferencesFilePath())
	if err != nil { return false }
	loaded := preferences{Version: 1, Enabled: true, KeepVisibleWhileInteracting: true}
	if err := json.Unmarshal(data, &loaded); err != nil { return false }
	if loaded.Version <= 0 { loaded.Version = 1 }
	prefs = loaded
	enabled = prefs.Enabled
	startWithWindows = prefs.StartWithWindows
	return true
}

func savePreferences() {
	prefs.Version = 1
	prefs.Enabled = enabled
	prefs.StartWithWindows = startWithWindows
	dir := preferencesDirectory()
	if err := os.MkdirAll(dir, 0o755); err != nil { return }
	data, err := json.MarshalIndent(prefs, "", "  ")
	if err != nil { return }
	data = append(data, '\n')
	tmp := preferencesFilePath() + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil { return }
	_ = os.Rename(tmp, preferencesFilePath())
}

func migratePreferencesFromRegistry() {
	const hkcu = uintptr(0x80000001)
	prefs = preferences{Version: 1, Enabled: true, KeepVisibleWhileInteracting: true}
	if v, ok := regReadDWORD(hkcu, regKey, "Enabled"); ok {
		prefs.Enabled = v != 0
	} else if v, ok := regReadDWORD(hkcu, legacyRegKey, "Enabled"); ok {
		prefs.Enabled = v != 0
	}
	if _, ok := regReadString(hkcu, runKey, runValue); ok {
		prefs.StartWithWindows = true
	} else if _, ok := regReadString(hkcu, runKey, legacyRunValue); ok {
		prefs.StartWithWindows = true
	}
	enabled = prefs.Enabled
	startWithWindows = prefs.StartWithWindows
	savePreferences()
}

func syncStartupRegistration() {
	const hkcu = uintptr(0x80000001)
	if startWithWindows {
		regWriteString(hkcu, runKey, runValue, `"`+executablePath()+`"`)
		regDeleteValue(hkcu, runKey, legacyRunValue)
	} else {
		regDeleteValue(hkcu, runKey, runValue)
		regDeleteValue(hkcu, runKey, legacyRunValue)
	}
}

func openPreferencesDirectory() {
	savePreferences()
	dir := preferencesDirectory()
	_ = os.MkdirAll(dir, 0o755)
	_ = exec.Command("explorer.exe", dir).Start()
}
