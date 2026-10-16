//go:build windows

package main

import (
	"encoding/binary"
	"time"
	"unsafe"
)

func pollTaskbarState() {
	if !enabled { return }
	reconcileWinKeyStateFromPolling()
	now := time.Now()
	shellUI := isShellUIActive()
	if shellUI {
		candidate := now.Add(shellTransitionGrace)
		if candidate.After(visibleUntil) { visibleUntil = candidate }
	}

	// Mouse position must never reveal a hidden taskbar. It can only extend the
	// lifetime of a taskbar that was already shown by Win/shell UI. This gives
	// the user enough time to click the notification-area chevron; once the
	// overflow flyout exists, isTrayOverflowOpen() keeps it visible itself.
	interacting := prefs.KeepVisibleWhileInteracting && isCursorOverVisibleTaskbar()
	if interacting {
		candidate := now.Add(650 * time.Millisecond)
		if candidate.After(visibleUntil) { visibleUntil = candidate }
	}

	shouldShow := winDown || shellUI || interacting || now.Before(visibleUntil)
	if shouldShow != lastShouldShow || lastApply.IsZero() {
		applyTaskbarVisibility(shouldShow)
		lastShouldShow = shouldShow
		return
	}
	if !shouldShow && now.Sub(lastApply) >= hiddenStateWatchdog {
		applyTaskbarVisibility(false)
	}
}

func handleRawInput(handle uintptr) {
	var size uint32
	r, _, _ := pGetRawInputData.Call(handle, ridInput, 0, uintptr(unsafe.Pointer(&size)), unsafe.Sizeof(uintptr(0))*2+8)
	if r != 0 || size == 0 { return }
	buf := make([]byte, size)
	r, _, _ = pGetRawInputData.Call(handle, ridInput, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)), unsafe.Sizeof(uintptr(0))*2+8)
	if uint32(r) != size || len(buf) < 40 { return }
	if binary.LittleEndian.Uint32(buf[0:4]) != rimTypeKeyboard { return }
	offset := 24
	vkey := binary.LittleEndian.Uint16(buf[offset+6 : offset+8])
	message := binary.LittleEndian.Uint32(buf[offset+8 : offset+12])
	if vkey != vkLWin && vkey != vkRWin { return }
	down := message == wmKeyDown || message == wmSysKeyDown
	up := message == wmKeyUp || message == wmSysKeyUp
	if !down && !up { return }
	if vkey == vkLWin { rawLeftWin = down }
	if vkey == vkRWin { rawRightWin = down }
	lastRawWinEvent = time.Now()
	onWinKeyState(rawLeftWin || rawRightWin || asyncKeyDown(vkLWin) || asyncKeyDown(vkRWin))
}

func handleCommand(cmd uint32) {
	switch cmd {
	case cmdToggle:
		setEnabled(!enabled)
	case cmdShowFiveSeconds:
		visibleUntil = time.Now().Add(5 * time.Second)
		applyTaskbarVisibility(true)
		lastShouldShow = true
	case cmdStartup:
		setStartupEnabled(!startWithWindows)
	case cmdCopyDiagnostics:
		copyDiagnostics()
	case cmdExit:
		pDestroyWindow := user32.NewProc("DestroyWindow")
		pDestroyWindow.Call(hwnd)
	}
}

func windowProc(h uintptr, message uint32, wParam, lParam uintptr) uintptr {
	if message == taskbarCreatedMessage && taskbarCreatedMessage != 0 {
		addTrayIcon()
		ensureAutohideForEnabledState()
		visibleUntil = time.Now().Add(750 * time.Millisecond)
		return 0
	}
	switch message {
	case wmTimer:
		if wParam == pollTimerID { pollTaskbarState() }
		return 0
	case wmInput:
		handleRawInput(lParam)
		return 0
	case wmHotkey:
		if wParam == hotkeyID { setEnabled(!enabled) }
		return 0
	case wmDisplayChange, wmSettingChange:
		refreshTaskbars()
		ensureAutohideForEnabledState()
		return 0
	case wmPowerBroadcast:
		if wParam == pbtApmResumeAutomatic || wParam == pbtApmResumeSuspend {
			refreshTaskbars()
			ensureAutohideForEnabledState()
			visibleUntil = time.Now().Add(750 * time.Millisecond)
		}
		return 1
	case wmTray:
		if lParam == wmRButtonUp || lParam == wmContextMenu || lParam == wmLButtonDblClk { showTrayMenu() }
		return 0
	case wmCommand:
		handleCommand(uint32(wParam & 0xffff))
		return 0
	case wmQueryEndSession:
		return 1
	case wmDestroy:
		pKillTimer.Call(h, pollTimerID)
		pUnregisterHotKey.Call(h, hotkeyID)
		removeTrayIcon()
		restoreOriginalTaskbarState()
		pPostQuitMessage.Call(0)
		return 0
	}
	r, _, _ := pDefWindowProcW.Call(h, uintptr(message), wParam, lParam)
	return r
}

func registerInput(h uintptr) {
	d := rawInputDevice{UsagePage: 0x01, Usage: 0x06, Flags: ridevInputSink, HwndTarget: h}
	pRegisterRawInputDevices.Call(uintptr(unsafe.Pointer(&d)), 1, unsafe.Sizeof(d))
}

func createHiddenWindow(instance uintptr) bool {
	className := utf16Ptr("SlipperyTaskbarHiddenWindow")
	wc := wndClassEx{CbSize: uint32(unsafe.Sizeof(wndClassEx{})), LpfnWndProc: wndProcCallback,
		HInstance: instance, LpszClassName: className, HIcon: loadEmbeddedIcon(32), HIconSm: loadEmbeddedIcon(16)}
	pRegisterClassExW.Call(uintptr(unsafe.Pointer(&wc)))
	h, _, _ := pCreateWindowExW.Call(0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(utf16Ptr(appName))), 0,
		0, 0, 0, 0, 0, 0, instance, 0)
	hwnd = h
	return h != 0
}
