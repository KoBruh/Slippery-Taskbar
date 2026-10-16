//go:build windows

package main

import (
	"syscall"
	"time"
	"unsafe"
)

var (
	user32   = syscall.NewLazyDLL("user32.dll")
	shell32  = syscall.NewLazyDLL("shell32.dll")
	kernel32 = syscall.NewLazyDLL("kernel32.dll")
	advapi32 = syscall.NewLazyDLL("advapi32.dll")

	pRegisterClassExW              = user32.NewProc("RegisterClassExW")
	pCreateWindowExW               = user32.NewProc("CreateWindowExW")
	pDefWindowProcW                = user32.NewProc("DefWindowProcW")
	pGetMessageW                   = user32.NewProc("GetMessageW")
	pTranslateMessage              = user32.NewProc("TranslateMessage")
	pDispatchMessageW              = user32.NewProc("DispatchMessageW")
	pPostQuitMessage               = user32.NewProc("PostQuitMessage")
	pSetTimer                      = user32.NewProc("SetTimer")
	pKillTimer                     = user32.NewProc("KillTimer")
	pRegisterHotKey                = user32.NewProc("RegisterHotKey")
	pUnregisterHotKey              = user32.NewProc("UnregisterHotKey")
	pRegisterWindowMessageW        = user32.NewProc("RegisterWindowMessageW")
	pRegisterRawInputDevices       = user32.NewProc("RegisterRawInputDevices")
	pGetRawInputData               = user32.NewProc("GetRawInputData")
	pGetAsyncKeyState              = user32.NewProc("GetAsyncKeyState")
	pGetForegroundWindow           = user32.NewProc("GetForegroundWindow")
	pGetAncestor                   = user32.NewProc("GetAncestor")
	pGetClassNameW                 = user32.NewProc("GetClassNameW")
	pGetWindowTextLengthW          = user32.NewProc("GetWindowTextLengthW")
	pGetWindowTextW                = user32.NewProc("GetWindowTextW")
	pGetWindowThreadProcessId      = user32.NewProc("GetWindowThreadProcessId")
	pEnumWindows                   = user32.NewProc("EnumWindows")
	pFindWindowW                   = user32.NewProc("FindWindowW")
	pFindWindowExW                 = user32.NewProc("FindWindowExW")
	pIsWindow                      = user32.NewProc("IsWindow")
	pIsWindowVisible               = user32.NewProc("IsWindowVisible")
	pShowWindowAsync               = user32.NewProc("ShowWindowAsync")
	pSetWindowPos                  = user32.NewProc("SetWindowPos")
	pGetWindowRect                 = user32.NewProc("GetWindowRect")
	pMonitorFromWindow             = user32.NewProc("MonitorFromWindow")
	pGetMonitorInfoW               = user32.NewProc("GetMonitorInfoW")
	pGetCursorPos                  = user32.NewProc("GetCursorPos")
	pCreatePopupMenu               = user32.NewProc("CreatePopupMenu")
	pAppendMenuW                   = user32.NewProc("AppendMenuW")
	pSetForegroundWindow           = user32.NewProc("SetForegroundWindow")
	pTrackPopupMenu                = user32.NewProc("TrackPopupMenu")
	pDestroyMenu                   = user32.NewProc("DestroyMenu")
	pPostMessageW                  = user32.NewProc("PostMessageW")
	pOpenClipboard                 = user32.NewProc("OpenClipboard")
	pEmptyClipboard                = user32.NewProc("EmptyClipboard")
	pSetClipboardData              = user32.NewProc("SetClipboardData")
	pCloseClipboard                = user32.NewProc("CloseClipboard")
	pLoadIconW                     = user32.NewProc("LoadIconW")
	pSetProcessDpiAwarenessContext = user32.NewProc("SetProcessDpiAwarenessContext")

	pShellNotifyIconW = shell32.NewProc("Shell_NotifyIconW")
	pSHAppBarMessage  = shell32.NewProc("SHAppBarMessage")

	pGetModuleHandleW           = kernel32.NewProc("GetModuleHandleW")
	pGetModuleFileNameW         = kernel32.NewProc("GetModuleFileNameW")
	pOpenProcess                = kernel32.NewProc("OpenProcess")
	pCloseHandle                = kernel32.NewProc("CloseHandle")
	pQueryFullProcessImageNameW = kernel32.NewProc("QueryFullProcessImageNameW")
	pCreateMutexW               = kernel32.NewProc("CreateMutexW")
	pGlobalAlloc                = kernel32.NewProc("GlobalAlloc")
	pGlobalLock                 = kernel32.NewProc("GlobalLock")
	pGlobalUnlock               = kernel32.NewProc("GlobalUnlock")
	pGlobalFree                 = kernel32.NewProc("GlobalFree")
	pRtlMoveMemory              = kernel32.NewProc("RtlMoveMemory")

	pRegOpenKeyExW    = advapi32.NewProc("RegOpenKeyExW")
	pRegCreateKeyExW  = advapi32.NewProc("RegCreateKeyExW")
	pRegQueryValueExW = advapi32.NewProc("RegQueryValueExW")
	pRegSetValueExW   = advapi32.NewProc("RegSetValueExW")
	pRegDeleteValueW  = advapi32.NewProc("RegDeleteValueW")
	pRegCloseKey      = advapi32.NewProc("RegCloseKey")
)

var (
	hwnd                   uintptr
	singleInstance         uintptr
	tray                   notifyIconData
	taskbarCreatedMessage  uint32
	enabled                = true
	startWithWindows       bool
	winDown                bool
	rawLeftWin             bool
	rawRightWin            bool
	trayMenuOpen           bool
	lastRawWinEvent        time.Time
	visibleUntil           time.Time
	lastShouldShow         = true
	lastApply              time.Time
	originalAppbarState    uint32
	originalAppbarCaptured bool
	taskbars               []uintptr
	wndProcCallback        uintptr
	enumWindowsCallback    uintptr
)

func utf16Ptr(s string) *uint16 {
	p, _ := syscall.UTF16PtrFromString(s)
	return p
}

func fillUTF16(dst []uint16, s string) {
	encoded := syscall.StringToUTF16(s)
	if len(encoded) > len(dst) {
		encoded = encoded[:len(dst)]
	}
	copy(dst, encoded)
	if len(dst) > 0 {
		dst[len(dst)-1] = 0
	}
}

func getWindowClass(h uintptr) string {
	if h == 0 {
		return ""
	}
	buf := make([]uint16, 256)
	r, _, _ := pGetClassNameW.Call(h, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	if r == 0 {
		return ""
	}
	return syscall.UTF16ToString(buf[:r])
}
