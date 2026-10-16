//go:build windows

package main

import (
	"time"
)

const (
	appName        = "Slippery Taskbar"
	regKey         = `Software\SlipperyTaskbar`
	legacyRegKey   = `Software\ButteryTaskbar11`
	runKey         = `Software\Microsoft\Windows\CurrentVersion\Run`
	runValue       = "SlipperyTaskbar"
	legacyRunValue = "ButteryTaskbar11"

	wmApp             = 0x8000
	wmTray            = wmApp + 1
	wmTimer           = 0x0113
	wmInput           = 0x00FF
	wmHotkey          = 0x0312
	wmDisplayChange   = 0x007E
	wmSettingChange   = 0x001A
	wmPowerBroadcast  = 0x0218
	wmCommand         = 0x0111
	wmQueryEndSession = 0x0011
	wmDestroy         = 0x0002
	wmNull            = 0x0000
	wmRButtonUp       = 0x0205
	wmContextMenu     = 0x007B
	wmLButtonDblClk   = 0x0203
	wmKeyDown         = 0x0100
	wmKeyUp           = 0x0101
	wmSysKeyDown      = 0x0104
	wmSysKeyUp        = 0x0105

	vkLWin = 0x5B
	vkRWin = 0x5C
	vkF11  = 0x7A

	modWin      = 0x0008
	modControl  = 0x0002
	modNoRepeat = 0x4000

	swHide           = 0
	swShowNoActivate = 4

	swpNoSize       = 0x0001
	swpNoMove       = 0x0002
	swpNoZOrder     = 0x0004
	swpNoActivate   = 0x0010
	swpShowWindow   = 0x0040
	swpHideWindow   = 0x0080
	swpNoSendChange = 0x0400

	monitorDefaultToNearest = 2
	gaRootOwner             = 3

	rimTypeKeyboard = 1
	ridInput        = 0x10000003
	ridevInputSink  = 0x00000100

	nifMessage = 0x00000001
	nifIcon    = 0x00000002
	nifTip     = 0x00000004
	nifInfo    = 0x00000010
	nimAdd     = 0x00000000
	nimModify  = 0x00000001
	nimDelete  = 0x00000002
	niifInfo   = 0x00000001

	idiApplication = 32512

	mfString    = 0x00000000
	mfChecked   = 0x00000008
	mfSeparator = 0x00000800

	tpmLeftAlign   = 0x0000
	tpmBottomAlign = 0x0020
	tpmRightButton = 0x0002

	abmGetState = 0x00000004
	abmSetState = 0x0000000A
	absAutoHide = 0x0000001

	processQueryLimitedInformation = 0x1000

	keyQueryValue = 0x0001
	keySetValue   = 0x0002
	regSZ         = 1
	regDword      = 4

	gmemMoveable  = 0x0002
	cfUnicodeText = 13

	pbtApmResumeSuspend   = 0x0007
	pbtApmResumeAutomatic = 0x0012

	errorAlreadyExists = 183

	cmdToggle          = 1001
	cmdShowFiveSeconds = 1002
	cmdStartup         = 1003
	cmdCopyDiagnostics = 1004
	cmdExit            = 1005

	pollTimerID = 1
	pollMS      = 30
	hotkeyID    = 1
)

const (
	// Keep the taskbar visible just long enough for Explorer shell surfaces to
	// finish opening/closing. Longer windows here make the taskbar feel sticky.
	winPressGrace        = 700 * time.Millisecond
	winReleaseGrace      = 500 * time.Millisecond
	shellTransitionGrace = 500 * time.Millisecond
	hiddenStateWatchdog  = 900 * time.Millisecond
)

type point struct{ X, Y int32 }

type rect struct{ Left, Top, Right, Bottom int32 }

type msg struct {
	Hwnd     uintptr
	Message  uint32
	WParam   uintptr
	LParam   uintptr
	Time     uint32
	Pt       point
	LPrivate uint32
}

type wndClassEx struct {
	CbSize        uint32
	Style         uint32
	LpfnWndProc   uintptr
	CbClsExtra    int32
	CbWndExtra    int32
	HInstance     uintptr
	HIcon         uintptr
	HCursor       uintptr
	HbrBackground uintptr
	LpszMenuName  *uint16
	LpszClassName *uint16
	HIconSm       uintptr
}

type notifyIconData struct {
	CbSize            uint32
	HWnd              uintptr
	UID               uint32
	UFlags            uint32
	UCallbackMessage  uint32
	HIcon             uintptr
	SzTip             [128]uint16
	DwState           uint32
	DwStateMask       uint32
	SzInfo            [256]uint16
	UTimeoutOrVersion uint32
	SzInfoTitle       [64]uint16
	DwInfoFlags       uint32
	GuidItem          [16]byte
	HBalloonIcon      uintptr
}

type appBarData struct {
	CbSize           uint32
	HWnd             uintptr
	UCallbackMessage uint32
	UEdge            uint32
	Rc               rect
	LParam           uintptr
}

type rawInputDevice struct {
	UsagePage  uint16
	Usage      uint16
	Flags      uint32
	HwndTarget uintptr
}

type monitorInfo struct {
	CbSize    uint32
	RcMonitor rect
	RcWork    rect
	DwFlags   uint32
}
