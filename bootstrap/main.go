//go:build windows

package main

import (
	"crypto/sha256"
	"embed"
	"encoding/hex"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

//go:embed payload/*.cs payload/slippery-taskbar.ico
var payload embed.FS

var (
	user32                   = syscall.NewLazyDLL("user32.dll")
	kernel32                 = syscall.NewLazyDLL("kernel32.dll")
	messageBoxW              = user32.NewProc("MessageBoxW")
	findWindowW              = user32.NewProc("FindWindowW")
	getWindowThreadProcessID = user32.NewProc("GetWindowThreadProcessId")
	postThreadMessageW       = user32.NewProc("PostThreadMessageW")
	openProcess              = kernel32.NewProc("OpenProcess")
	waitForSingleObject      = kernel32.NewProc("WaitForSingleObject")
	closeHandle              = kernel32.NewProc("CloseHandle")
)

const (
	mbIconError                    = 0x00000010
	mbOK                           = 0x00000000
	wmQuit                         = 0x0012
	synchronize                    = 0x00100000
	processQueryLimitedInformation = 0x1000
	waitObject0                    = 0x00000000
	waitTimeout                    = 0x00000102
)

func main() {
	local := os.Getenv("LOCALAPPDATA")
	if local == "" {
		showError("Slippery Taskbar", "LOCALAPPDATA is unavailable, so Slippery Taskbar cannot install its managed build.")
		return
	}

	appDir := filepath.Join(local, "Slippery-Taskbar")
	sourceDir := filepath.Join(appDir, "managed-source")
	if err := os.MkdirAll(sourceDir, 0755); err != nil {
		showError("Slippery Taskbar", "Could not create the application folder:\n"+err.Error())
		return
	}

	entries, err := payload.ReadDir("payload")
	if err != nil {
		showError("Slippery Taskbar", "Embedded C# source is missing.")
		return
	}
	var sourceNames []string
	var sourceData [][]byte
	for _, entry := range entries {
		if entry.IsDir() || !strings.HasSuffix(strings.ToLower(entry.Name()), ".cs") {
			continue
		}
		data, readErr := payload.ReadFile("payload/" + entry.Name())
		if readErr != nil {
			showError("Slippery Taskbar", "Embedded C# source is incomplete.")
			return
		}
		sourceNames = append(sourceNames, entry.Name())
		sourceData = append(sourceData, data)
	}
	if len(sourceNames) == 0 {
		showError("Slippery Taskbar", "Embedded C# source is missing.")
		return
	}
	icon, err := payload.ReadFile("payload/slippery-taskbar.ico")
	if err != nil {
		showError("Slippery Taskbar", "Embedded application icon is missing.")
		return
	}

	iconPath := filepath.Join(sourceDir, "slippery-taskbar.ico")
	managedExe := filepath.Join(appDir, "SlipperyTaskbar.exe")
	stampPath := filepath.Join(appDir, ".managed-build-id")
	compileLog := filepath.Join(appDir, "compile.log")

	for i, name := range sourceNames {
		if err := os.WriteFile(filepath.Join(sourceDir, name), sourceData[i], 0644); err != nil {
			showError("Slippery Taskbar", "Could not write the managed source:\n"+err.Error())
			return
		}
	}
	if err := os.WriteFile(iconPath, icon, 0644); err != nil {
		showError("Slippery Taskbar", "Could not write the managed icon:\n"+err.Error())
		return
	}

	hasher := sha256.New()
	for i, name := range sourceNames {
		hasher.Write([]byte(name))
		hasher.Write(sourceData[i])
	}
	hasher.Write(icon)
	buildID := hex.EncodeToString(hasher.Sum(nil))
	oldStamp, _ := os.ReadFile(stampPath)

	if string(oldStamp) != buildID || !fileExists(managedExe) {
		csc := findCSC()
		if csc == "" {
			showError("Slippery Taskbar",
				"The Windows .NET Framework C# compiler could not be found.\n\n"+
					"Expected it under %WINDIR%\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe.\n\n"+
					"The full C# source has still been written to:\n"+sourceDir)
			return
		}

		nextExe := filepath.Join(appDir, "SlipperyTaskbar.next.exe")
		_ = os.Remove(nextExe)
		args := []string{
			"/nologo",
			"/target:winexe",
			"/optimize+",
			"/platform:x64",
			"/out:" + nextExe,
			"/reference:System.dll",
			"/reference:System.Core.dll",
			"/reference:System.Drawing.dll",
			"/reference:System.Windows.Forms.dll",
			"/reference:System.Runtime.Serialization.dll",
		}
		for _, name := range sourceNames {
			args = append(args, filepath.Join(sourceDir, name))
		}

		cmd := exec.Command(csc, args...)
		cmd.Dir = sourceDir
		cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
		output, compileErr := cmd.CombinedOutput()
		_ = os.WriteFile(compileLog, output, 0644)
		if compileErr != nil || !fileExists(nextExe) {
			_ = os.Remove(nextExe)
			showError("Slippery Taskbar",
				"The C# build failed. The compiler output was saved to:\n"+compileLog+"\n\n"+
					"You can send that file back to me and I can correct the build.")
			return
		}

		if fileExists(managedExe) {
			if err := stopRunningManagedApp(6 * time.Second); err != nil {
				_ = os.Remove(nextExe)
				showError("Slippery Taskbar",
					"The new build is ready, but the currently running Slippery Taskbar could not be closed safely.\n\n"+
						"Use Exit from the Slippery Taskbar tray menu, then run this setup again.\n\n"+err.Error())
				return
			}
		}

		if err := replaceFile(nextExe, managedExe); err != nil {
			_ = os.Remove(nextExe)
			showError("Slippery Taskbar", "Could not install the rebuilt managed executable:\n"+err.Error())
			return
		}
		_ = os.WriteFile(stampPath, []byte(buildID), 0644)
	}

	cmd := exec.Command(managedExe)
	cmd.Dir = appDir
	if err := cmd.Start(); err != nil {
		showError("Slippery Taskbar", "The managed Slippery Taskbar build could not start:\n"+err.Error())
		return
	}
}

func stopRunningManagedApp(timeout time.Duration) error {
	caption, _ := syscall.UTF16PtrFromString("SlipperyTaskbarHiddenWindow")
	hWnd, _, _ := findWindowW.Call(0, uintptr(unsafe.Pointer(caption)))
	if hWnd == 0 {
		return nil
	}

	var pid uint32
	threadID, _, _ := getWindowThreadProcessID.Call(hWnd, uintptr(unsafe.Pointer(&pid)))
	if threadID == 0 {
		return fmt.Errorf("could not identify the running Slippery Taskbar UI thread")
	}

	posted, _, _ := postThreadMessageW.Call(threadID, wmQuit, 0, 0)
	if posted == 0 {
		return fmt.Errorf("could not request the running Slippery Taskbar to exit")
	}

	if pid != 0 {
		process, _, _ := openProcess.Call(synchronize|processQueryLimitedInformation, 0, uintptr(pid))
		if process != 0 {
			defer closeHandle.Call(process)
			millis := uint32(timeout / time.Millisecond)
			result, _, _ := waitForSingleObject.Call(process, uintptr(millis))
			if result == waitObject0 {
				return nil
			}
			if result == waitTimeout {
				return fmt.Errorf("the running Slippery Taskbar did not exit within %s", timeout)
			}
			return fmt.Errorf("waiting for the running Slippery Taskbar failed")
		}
	}

	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		window, _, _ := findWindowW.Call(0, uintptr(unsafe.Pointer(caption)))
		if window == 0 {
			time.Sleep(200 * time.Millisecond)
			return nil
		}
		time.Sleep(100 * time.Millisecond)
	}
	return fmt.Errorf("the running Slippery Taskbar did not exit within %s", timeout)
}

func replaceFile(source, destination string) error {
	if fileExists(destination) {
		if err := os.Remove(destination); err != nil {
			return err
		}
	}
	if err := os.Rename(source, destination); err == nil {
		return nil
	}
	data, err := os.ReadFile(source)
	if err != nil {
		return err
	}
	if err := os.WriteFile(destination, data, 0755); err != nil {
		return err
	}
	return os.Remove(source)
}

func findCSC() string {
	windows := os.Getenv("WINDIR")
	if windows == "" {
		windows = os.Getenv("SystemRoot")
	}
	if windows != "" {
		candidates := []string{
			filepath.Join(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe"),
			filepath.Join(windows, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe"),
		}
		for _, path := range candidates {
			if fileExists(path) {
				return path
			}
		}
	}
	if path, err := exec.LookPath("csc.exe"); err == nil {
		return path
	}
	return ""
}

func fileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && !info.IsDir()
}

func showError(title, text string) {
	t, _ := syscall.UTF16PtrFromString(title)
	m, _ := syscall.UTF16PtrFromString(text)
	_, _, _ = messageBoxW.Call(0, uintptr(unsafe.Pointer(m)), uintptr(unsafe.Pointer(t)), mbOK|mbIconError)
}

func init() {
	_ = fmt.Sprintf
}
