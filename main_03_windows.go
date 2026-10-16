//go:build windows

package main

import (
	"path/filepath"
	"sort"
	"strings"
	"syscall"
	"unsafe"
)

func getWindowTitle(h uintptr) string {
	if h == 0 {
		return ""
	}
	n, _, _ := pGetWindowTextLengthW.Call(h)
	if n == 0 {
		return ""
	}
	buf := make([]uint16, n+1)
	r, _, _ := pGetWindowTextW.Call(h, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	if r == 0 {
		return ""
	}
	return syscall.UTF16ToString(buf[:r])
}

func getProcessName(h uintptr) string {
	if h == 0 {
		return ""
	}
	var pid uint32
	pGetWindowThreadProcessId.Call(h, uintptr(unsafe.Pointer(&pid)))
	if pid == 0 {
		return ""
	}
	ph, _, _ := pOpenProcess.Call(processQueryLimitedInformation, 0, uintptr(pid))
	if ph == 0 {
		return ""
	}
	defer pCloseHandle.Call(ph)

	buf := make([]uint16, 32768)
	size := uint32(len(buf))
	r, _, _ := pQueryFullProcessImageNameW.Call(ph, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)))
	if r == 0 {
		return ""
	}
	full := syscall.UTF16ToString(buf[:size])
	return filepath.Base(full)
}

func isTaskbarClass(cls string) bool {
	return cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd"
}

func isKnownShellClass(cls string) bool {
	switch cls {
	case "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "ControlCenterWindow",
		"Windows.UI.Core.CoreWindow", "TaskListThumbnailWnd", "NotifyIconOverflowWindow",
		"Shell_InputSwitchTopLevelWindow", "MultitaskingViewFrame":
		return true
	}
	return false
}

func isShellUIProcess(proc string) bool {
	switch strings.ToLower(proc) {
	case "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "shellexperiencehost.exe",
		"shellhost.exe", "widgets.exe", "widgetboard.exe":
		return true
	}
	return false
}

func isExplorerShellSurface(h uintptr, cls, title string) bool {
	if strings.ToLower(getProcessName(h)) != "explorer.exe" {
		return false
	}
	if isTaskbarClass(cls) || cls == "#32768" {
		return true
	}
	switch cls {
	case "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "TaskListThumbnailWnd",
		"NotifyIconOverflowWindow", "Shell_InputSwitchTopLevelWindow":
		return true
	}
	switch strings.ToLower(title) {
	case "task view", "notification center", "control center":
		return true
	}
	return false
}

func isShellUIWindow(h uintptr) bool {
	if h == 0 {
		return false
	}
	cls := getWindowClass(h)
	title := getWindowTitle(h)
	proc := strings.ToLower(getProcessName(h))
	if isTaskbarClass(cls) || isShellUIProcess(proc) || isExplorerShellSurface(h, cls, title) {
		return true
	}
	if (proc == "explorer.exe" || proc == "shellhost.exe" || proc == "shellexperiencehost.exe" ||
		proc == "startmenuexperiencehost.exe" || proc == "searchhost.exe") &&
		(strings.Contains(cls, "Xaml") || isKnownShellClass(cls)) {
		return true
	}
	return false
}

func isShellUIForeground() bool {
	fg, _, _ := pGetForegroundWindow.Call()
	if fg == 0 {
		return false
	}
	if isShellUIWindow(fg) {
		return true
	}
	root, _, _ := pGetAncestor.Call(fg, gaRootOwner)
	return root != 0 && root != fg && isShellUIWindow(root)
}

func isVisibleTopLevelClass(className string) bool {
	h, _, _ := pFindWindowW.Call(uintptr(unsafe.Pointer(utf16Ptr(className))), 0)
	if h == 0 {
		return false
	}
	visible, _, _ := pIsWindowVisible.Call(h)
	return visible != 0
}

func isTrayOverflowOpen() bool {
	// Windows 11 has used all three of these for the notification-area overflow
	// across different releases. XamlExplorerHostIslandWindow is important on
	// newer builds where the old TopLevelWindowForOverflowXamlIsland check alone
	// misses the flyout.
	return isVisibleTopLevelClass("TopLevelWindowForOverflowXamlIsland") ||
		isVisibleTopLevelClass("XamlExplorerHostIslandWindow") ||
		isVisibleTopLevelClass("NotifyIconOverflowWindow")
}

func isShellUIActive() bool {
	return trayMenuOpen || isShellUIForeground() || isTrayOverflowOpen()
}

func refreshTaskbars() {
	taskbars = taskbars[:0]
	primary, _, _ := pFindWindowW.Call(uintptr(unsafe.Pointer(utf16Ptr("Shell_TrayWnd"))), 0)
	if primary != 0 {
		taskbars = append(taskbars, primary)
	}
	pEnumWindows.Call(enumWindowsCallback, 0)
	sort.Slice(taskbars, func(i, j int) bool { return taskbars[i] < taskbars[j] })
	out := taskbars[:0]
	var prev uintptr
	for i, h := range taskbars {
		if i == 0 || h != prev {
			out = append(out, h)
			prev = h
		}
	}
	taskbars = out
}

func enumWindowsProc(h, _ uintptr) uintptr {
	if getWindowClass(h) == "Shell_SecondaryTrayWnd" {
		taskbars = append(taskbars, h)
	}
	return 1
}

func getAppbarState() uint32 {
	d := appBarData{CbSize: uint32(unsafe.Sizeof(appBarData{}))}
	r, _, _ := pSHAppBarMessage.Call(abmGetState, uintptr(unsafe.Pointer(&d)))
	return uint32(r)
}

func setAppbarState(state uint32) {
	d := appBarData{CbSize: uint32(unsafe.Sizeof(appBarData{})), LParam: uintptr(state)}
	pSHAppBarMessage.Call(abmSetState, uintptr(unsafe.Pointer(&d)))
}

func ensureAutohideForEnabledState() {
	if !originalAppbarCaptured {
		originalAppbarState = getAppbarState()
		originalAppbarCaptured = true
	}
	if enabled {
		setAppbarState(originalAppbarState | absAutoHide)
	} else {
		setAppbarState(originalAppbarState)
	}
}

func restoreOriginalTaskbarState() {
	if originalAppbarCaptured {
		setAppbarState(originalAppbarState)
	}
	refreshTaskbars()
	for _, h := range taskbars {
		pShowWindowAsync.Call(h, swShowNoActivate)
	}
}

func isWindow(h uintptr) bool {
	r, _, _ := pIsWindow.Call(h)
	return r != 0
}

func abs32(v int32) int32 {
	if v < 0 {
		return -v
	}
	return v
}

func max32(a, b int32) int32 {
	if a > b {
		return a
	}
	return b
}
