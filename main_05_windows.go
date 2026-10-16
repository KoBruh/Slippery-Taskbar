//go:build windows

package main

import (
	"fmt"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

func setStartupEnabled(v bool) {
	const hkcu = uintptr(0x80000001)
	if v {
		regWriteString(hkcu, runKey, runValue, `"`+executablePath()+`"`)
		regDeleteValue(hkcu, runKey, legacyRunValue)
	} else {
		regDeleteValue(hkcu, runKey, runValue)
		regDeleteValue(hkcu, runKey, legacyRunValue)
	}
	startWithWindows = v
}

func addTrayIcon() {
	tray = notifyIconData{CbSize: uint32(unsafe.Sizeof(notifyIconData{})), HWnd: hwnd, UID: 1,
		UFlags: nifMessage | nifIcon | nifTip, UCallbackMessage: wmTray}
	icon := loadEmbeddedIcon(20)
	if icon == 0 {
		icon, _, _ = pLoadIconW.Call(0, idiApplication)
	}
	tray.HIcon = icon
	fillUTF16(tray.SzTip[:], appName)
	pShellNotifyIconW.Call(nimAdd, uintptr(unsafe.Pointer(&tray)))
}

func removeTrayIcon() { pShellNotifyIconW.Call(nimDelete, uintptr(unsafe.Pointer(&tray))) }

func showBalloon(title, text string) {
	oldFlags := tray.UFlags
	tray.UFlags = nifInfo
	fillUTF16(tray.SzInfoTitle[:], title)
	fillUTF16(tray.SzInfo[:], text)
	tray.DwInfoFlags = niifInfo
	pShellNotifyIconW.Call(nimModify, uintptr(unsafe.Pointer(&tray)))
	tray.UFlags = oldFlags
}

func appendMenu(menu uintptr, flags uint32, id uint32, text string) {
	var p uintptr
	if text != "" {
		p = uintptr(unsafe.Pointer(utf16Ptr(text)))
	}
	pAppendMenuW.Call(menu, uintptr(flags), uintptr(id), p)
}

func showTrayMenu() {
	var pt point
	pGetCursorPos.Call(uintptr(unsafe.Pointer(&pt)))
	menu, _, _ := pCreatePopupMenu.Call()
	if menu == 0 {
		return
	}
	defer pDestroyMenu.Call(menu)

	flags := uint32(mfString)
	if enabled {
		flags |= mfChecked
	}
	appendMenu(menu, flags, cmdToggle, "Enable taskbar hiding\tCtrl+Win+F11")
	appendMenu(menu, mfString, cmdShowFiveSeconds, "Show taskbar for 5 seconds")
	flags = mfString
	if startWithWindows {
		flags |= mfChecked
	}
	appendMenu(menu, flags, cmdStartup, "Start with Windows")
	appendMenu(menu, mfSeparator, 0, "")
	appendMenu(menu, mfString, cmdCopyDiagnostics, "Copy diagnostics")
	appendMenu(menu, mfSeparator, 0, "")
	appendMenu(menu, mfString, cmdExit, "Exit")

	pSetForegroundWindow.Call(hwnd)
	pTrackPopupMenu.Call(menu, tpmRightButton|tpmBottomAlign|tpmLeftAlign, uintptr(pt.X), uintptr(pt.Y), 0, hwnd, 0)
	pPostMessageW.Call(hwnd, wmNull, 0, 0)
}

func copyTextToClipboard(text string) bool {
	r, _, _ := pOpenClipboard.Call(hwnd)
	if r == 0 {
		return false
	}
	defer pCloseClipboard.Call()
	pEmptyClipboard.Call()
	data := syscall.StringToUTF16(text)
	bytes := uintptr(len(data) * 2)
	mem, _, _ := pGlobalAlloc.Call(gmemMoveable, bytes)
	if mem == 0 {
		return false
	}
	ptr, _, _ := pGlobalLock.Call(mem)
	if ptr == 0 {
		pGlobalFree.Call(mem)
		return false
	}
	pRtlMoveMemory.Call(ptr, uintptr(unsafe.Pointer(&data[0])), bytes)
	pGlobalUnlock.Call(mem)
	set, _, _ := pSetClipboardData.Call(cfUnicodeText, mem)
	if set == 0 {
		pGlobalFree.Call(mem)
		return false
	}
	return true
}

func copyDiagnostics() {
	const hklm = uintptr(0x80000002)
	const ntKey = `SOFTWARE\Microsoft\Windows NT\CurrentVersion`
	product, _ := regReadString(hklm, ntKey, "ProductName")
	display, _ := regReadString(hklm, ntKey, "DisplayVersion")
	build, _ := regReadString(hklm, ntKey, "CurrentBuildNumber")
	ubr, _ := regReadDWORD(hklm, ntKey, "UBR")

	fg, _, _ := pGetForegroundWindow.Call()
	root, _, _ := pGetAncestor.Call(fg, gaRootOwner)
	refreshTaskbars()

	var b strings.Builder
	fmt.Fprintf(&b, "Slippery Taskbar diagnostics\r\n")
	fmt.Fprintf(&b, "Windows: %s %s build %s.%d\r\n", product, display, build, ubr)
	fmt.Fprintf(&b, "Enabled: %t\r\nWin key down: %t\r\nShell UI foreground: %t\r\nTray overflow open: %t\r\nTaskbars found: %d\r\n", enabled, winDown, isShellUIForeground(), isTrayOverflowOpen(), len(taskbars))
	appendWindowDiag := func(label string, h uintptr) {
		fmt.Fprintf(&b, "%s: hwnd=0x%x", label, h)
		if h != 0 {
			fmt.Fprintf(&b, ", process=%s, class=%q, title=%q", getProcessName(h), getWindowClass(h), getWindowTitle(h))
		}
		b.WriteString("\r\n")
	}
	appendWindowDiag("Foreground", fg)
	if root != fg {
		appendWindowDiag("Root owner", root)
	}
	for i, h := range taskbars {
		appendWindowDiag(fmt.Sprintf("Taskbar %d", i), h)
	}

	if copyTextToClipboard(b.String()) {
		showBalloon(appName, "Diagnostics copied to the clipboard.")
	}
}

func onWinKeyState(down bool) {
	if down == winDown {
		return
	}
	winDown = down
	now := time.Now()
	if down {
		candidate := now.Add(winPressGrace)
		if candidate.After(visibleUntil) {
			visibleUntil = candidate
		}
		if !lastShouldShow {
			applyTaskbarVisibility(true)
			lastShouldShow = true
		}
	} else {
		candidate := now.Add(winReleaseGrace)
		if candidate.After(visibleUntil) {
			visibleUntil = candidate
		}
	}
}

func asyncKeyDown(vk uint32) bool {
	r, _, _ := pGetAsyncKeyState.Call(uintptr(vk))
	return int16(r&0xffff) < 0
}

func reconcileWinKeyStateFromPolling() {
	now := time.Now()
	left, right := asyncKeyDown(vkLWin), asyncKeyDown(vkRWin)
	if !left && !right && (rawLeftWin || rawRightWin) && !lastRawWinEvent.IsZero() && now.Sub(lastRawWinEvent) > 120*time.Millisecond {
		rawLeftWin, rawRightWin = false, false
	}
	onWinKeyState(left || right || rawLeftWin || rawRightWin)
}
