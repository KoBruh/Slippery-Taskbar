//go:build windows

package main

import (
	_ "embed"
	"encoding/binary"
	"math"
	"unsafe"
)

// appIconICO is embedded so the tray/window icon still works for direct
// `go build` and cross-compiled builds, even when no PE resource compiler is
// available. GitHub Actions additionally embeds the same ICO as a Windows PE
// resource so Explorer shows the artwork on SlipperyTaskbar.exe itself.
//
//go:embed assets/slippery-taskbar.ico
var appIconICO []byte

var pCreateIconFromResourceEx = user32.NewProc("CreateIconFromResourceEx")

func loadEmbeddedIcon(desiredSize int) uintptr {
	if len(appIconICO) < 6 || binary.LittleEndian.Uint16(appIconICO[0:2]) != 0 || binary.LittleEndian.Uint16(appIconICO[2:4]) != 1 {
		return 0
	}
	count := int(binary.LittleEndian.Uint16(appIconICO[4:6]))
	if count <= 0 || len(appIconICO) < 6+count*16 {
		return 0
	}

	bestOff := 0
	bestLen := 0
	bestScore := math.MaxInt
	for i := 0; i < count; i++ {
		e := appIconICO[6+i*16 : 6+(i+1)*16]
		w := int(e[0])
		h := int(e[1])
		if w == 0 {
			w = 256
		}
		if h == 0 {
			h = 256
		}
		length := int(binary.LittleEndian.Uint32(e[8:12]))
		off := int(binary.LittleEndian.Uint32(e[12:16]))
		if length <= 0 || off < 0 || off+length > len(appIconICO) {
			continue
		}
		score := absInt(w-desiredSize) + absInt(h-desiredSize)
		if w < desiredSize || h < desiredSize {
			score++
		}
		if score < bestScore {
			bestScore = score
			bestOff = off
			bestLen = length
		}
	}
	if bestLen == 0 {
		return 0
	}

	r, _, _ := pCreateIconFromResourceEx.Call(
		uintptr(unsafe.Pointer(&appIconICO[bestOff])),
		uintptr(bestLen),
		1,
		0x00030000,
		uintptr(desiredSize),
		uintptr(desiredSize),
		0,
	)
	return r
}

func absInt(v int) int {
	if v < 0 {
		return -v
	}
	return v
}
