//go:build windows

package main

import (
	"syscall"
	"time"
	"unsafe"
)

func main() {
	pSetProcessDpiAwarenessContext.Call(^uintptr(3))

	mutexName := utf16Ptr(`Local\SlipperyTaskbar.Singleton`)
	h, _, err := pCreateMutexW.Call(0, 0, uintptr(unsafe.Pointer(mutexName)))
	if h == 0 {
		return
	}
	singleInstance = h
	defer pCloseHandle.Call(singleInstance)
	if errno, ok := err.(syscall.Errno); ok && errno == errorAlreadyExists {
		return
	}

	wndProcCallback = syscall.NewCallback(windowProc)
	enumWindowsCallback = syscall.NewCallback(enumWindowsProc)
	loadSettings()

	instance, _, _ := pGetModuleHandleW.Call(0)
	if !createHiddenWindow(instance) {
		return
	}

	taskbarCreatedMessageR, _, _ := pRegisterWindowMessageW.Call(uintptr(unsafe.Pointer(utf16Ptr("TaskbarCreated"))))
	taskbarCreatedMessage = uint32(taskbarCreatedMessageR)
	registerInput(hwnd)
	pRegisterHotKey.Call(hwnd, hotkeyID, modControl|modWin|modNoRepeat, vkF11)
	addTrayIcon()

	originalAppbarState = getAppbarState()
	originalAppbarCaptured = true
	ensureAutohideForEnabledState()
	refreshTaskbars()
	visibleUntil = time.Now().Add(time.Second)
	lastShouldShow = true
	applyTaskbarVisibility(true)
	pSetTimer.Call(hwnd, pollTimerID, pollMS, 0)

	var m msg
	for {
		r, _, _ := pGetMessageW.Call(uintptr(unsafe.Pointer(&m)), 0, 0, 0)
		if int32(r) <= 0 {
			break
		}
		pTranslateMessage.Call(uintptr(unsafe.Pointer(&m)))
		pDispatchMessageW.Call(uintptr(unsafe.Pointer(&m)))
	}
}
