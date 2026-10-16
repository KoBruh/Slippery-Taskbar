//go:build windows

package main

import (
	"syscall"
	"time"
	"unsafe"
)

func clamp32(v, lo, hi int32) int32 {
	if v < lo { return lo }
	if v > hi { return hi }
	return v
}

func forceTaskbarOnScreen(h uintptr) {
	if !isWindow(h) { return }
	var r rect
	ok, _, _ := pGetWindowRect.Call(h, uintptr(unsafe.Pointer(&r)))
	if ok == 0 { return }
	width, height := r.Right-r.Left, r.Bottom-r.Top
	if width <= 0 || height <= 0 { return }

	mon, _, _ := pMonitorFromWindow.Call(h, monitorDefaultToNearest)
	if mon == 0 { return }
	mi := monitorInfo{CbSize: uint32(unsafe.Sizeof(monitorInfo{}))}
	ok, _, _ = pGetMonitorInfoW.Call(mon, uintptr(unsafe.Pointer(&mi)))
	if ok == 0 { return }
	m := mi.RcMonitor

	x, y := r.Left, r.Top
	if width >= height {
		topEdge := abs32(r.Top-m.Top) < abs32(r.Bottom-m.Bottom)
		if topEdge { y = m.Top } else { y = m.Bottom - height }
		x = clamp32(r.Left, m.Left, max32(m.Left, m.Right-width))
	} else {
		leftEdge := abs32(r.Left-m.Left) < abs32(r.Right-m.Right)
		if leftEdge { x = m.Left } else { x = m.Right - width }
		y = clamp32(r.Top, m.Top, max32(m.Top, m.Bottom-height))
	}

	pSetWindowPos.Call(h, 0, uintptr(x), uintptr(y), uintptr(width), uintptr(height),
		swpNoActivate|swpNoZOrder|swpShowWindow|swpNoSendChange)
}

func applyTaskbarVisibility(show bool) {
	refreshTaskbars()
	for _, h := range taskbars {
		if !isWindow(h) { continue }
		if show {
			pShowWindowAsync.Call(h, swShowNoActivate)
			forceTaskbarOnScreen(h)
		} else {
			pShowWindowAsync.Call(h, swHide)
			pSetWindowPos.Call(h, 0, 0, 0, 0, 0, swpNoMove|swpNoSize|swpNoZOrder|swpNoActivate|swpHideWindow)
		}
	}
	lastApply = time.Now()
}

func regOpen(root uintptr, path string, access uint32) (uintptr, bool) {
	var key uintptr
	r, _, _ := pRegOpenKeyExW.Call(root, uintptr(unsafe.Pointer(utf16Ptr(path))), 0, uintptr(access), uintptr(unsafe.Pointer(&key)))
	return key, r == 0
}

func regCreate(root uintptr, path string, access uint32) (uintptr, bool) {
	var key uintptr
	r, _, _ := pRegCreateKeyExW.Call(root, uintptr(unsafe.Pointer(utf16Ptr(path))), 0, 0, 0, uintptr(access), 0, uintptr(unsafe.Pointer(&key)), 0)
	return key, r == 0
}

func regClose(key uintptr) {
	if key != 0 { pRegCloseKey.Call(key) }
}

func regReadDWORD(root uintptr, path, name string) (uint32, bool) {
	key, ok := regOpen(root, path, keyQueryValue)
	if !ok { return 0, false }
	defer regClose(key)
	var typ uint32
	var value uint32
	size := uint32(4)
	r, _, _ := pRegQueryValueExW.Call(key, uintptr(unsafe.Pointer(utf16Ptr(name))), 0, uintptr(unsafe.Pointer(&typ)), uintptr(unsafe.Pointer(&value)), uintptr(unsafe.Pointer(&size)))
	return value, r == 0 && typ == regDword
}

func regReadString(root uintptr, path, name string) (string, bool) {
	key, ok := regOpen(root, path, keyQueryValue)
	if !ok { return "", false }
	defer regClose(key)
	var typ uint32
	size := uint32(0)
	r, _, _ := pRegQueryValueExW.Call(key, uintptr(unsafe.Pointer(utf16Ptr(name))), 0, uintptr(unsafe.Pointer(&typ)), 0, uintptr(unsafe.Pointer(&size)))
	if r != 0 || size == 0 || (typ != regSZ && typ != 2) { return "", false }
	buf := make([]uint16, size/2+1)
	r, _, _ = pRegQueryValueExW.Call(key, uintptr(unsafe.Pointer(utf16Ptr(name))), 0, uintptr(unsafe.Pointer(&typ)), uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&size)))
	if r != 0 { return "", false }
	return syscall.UTF16ToString(buf), true
}

func regWriteDWORD(root uintptr, path, name string, value uint32) {
	key, ok := regCreate(root, path, keySetValue)
	if !ok { return }
	defer regClose(key)
	pRegSetValueExW.Call(key, uintptr(unsafe.Pointer(utf16Ptr(name))), 0, regDword, uintptr(unsafe.Pointer(&value)), 4)
}

func regWriteString(root uintptr, path, name, value string) {
	key, ok := regCreate(root, path, keySetValue)
	if !ok { return }
	defer regClose(key)
	data := syscall.StringToUTF16(value)
	pRegSetValueExW.Call(key, uintptr(unsafe.Pointer(utf16Ptr(name))), 0, regSZ, uintptr(unsafe.Pointer(&data[0])), uintptr(len(data)*2))
}

func regDeleteValue(root uintptr, path, name string) {
	key, ok := regCreate(root, path, keySetValue)
	if !ok { return }
	defer regClose(key)
	pRegDeleteValueW.Call(key, uintptr(unsafe.Pointer(utf16Ptr(name))))
}

func loadSettings() {
	if !loadPreferencesFile() {
		migratePreferencesFromRegistry()
	}
	syncStartupRegistration()
}

func setEnabled(v bool) {
	if enabled == v { return }
	enabled = v
	prefs.Enabled = v
	savePreferences()
	ensureAutohideForEnabledState()
	if !v {
		applyTaskbarVisibility(true)
		lastShouldShow = true
	} else {
		visibleUntil = time.Now().Add(500 * time.Millisecond)
		applyTaskbarVisibility(true)
		lastShouldShow = true
	}
}

func executablePath() string {
	buf := make([]uint16, 32768)
	r, _, _ := pGetModuleFileNameW.Call(0, uintptr(unsafe.Pointer(&buf[0])), uintptr(len(buf)))
	if r == 0 { return "" }
	return syscall.UTF16ToString(buf[:r])
}
