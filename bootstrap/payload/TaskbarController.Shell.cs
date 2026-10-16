using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SlipperyTaskbar
{
    internal sealed partial class TaskbarController
    {
        public void RestoreOriginalTaskbarState()
        {
            if (_originalAppbarCaptured) SetAppbarState(_originalAppbarState);
            _originalWorkAreas.Clear();
            _workAreasCaptured = false;
            DeleteWorkAreaState();
            RefreshTaskbars();
            foreach (IntPtr h in _taskbars)
                NativeMethods.ShowWindow(h, NativeMethods.SW_SHOWNOACTIVATE);
        }

        public bool IsCursorOverVisibleTaskbar(bool lastShouldShow)
        {
            if (!lastShouldShow) return false;
            RefreshTaskbars();
            NativeMethods.POINT pt;
            if (!NativeMethods.GetCursorPos(out pt)) return false;

            foreach (IntPtr h in _taskbars)
            {
                if (!IsTaskbarActuallyOnScreen(h)) continue;
                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(h, out r)) continue;
                if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom)
                    return true;
            }
            return false;
        }

        public void GetActualVisibilityState(out bool anyVisible, out bool allVisible)
        {
            RefreshTaskbars();
            anyVisible = false;
            allVisible = _taskbars.Count > 0;

            foreach (IntPtr h in _taskbars)
            {
                bool visible = IsTaskbarActuallyOnScreen(h);
                if (visible) anyVisible = true;
                else allVisible = false;
            }
        }

        private static bool IsTaskbarActuallyOnScreen(IntPtr h)
        {
            if (!NativeMethods.IsWindow(h) || !NativeMethods.IsWindowVisible(h))
                return false;

            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(h, out r))
                return false;

            IntPtr monitor = NativeMethods.MonitorFromWindow(h, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;

            NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref mi))
                return false;

            long centerX = (long)r.Left + ((long)r.Right - r.Left) / 2;
            long centerY = (long)r.Top + ((long)r.Bottom - r.Top) / 2;
            return centerX >= mi.rcMonitor.Left && centerX < mi.rcMonitor.Right &&
                   centerY >= mi.rcMonitor.Top && centerY < mi.rcMonitor.Bottom;
        }

        public bool IsShellUiActive(bool trayMenuOpen)
        {
            return trayMenuOpen || IsShellUiForeground() || IsTrayOverflowOpen();
        }

        public bool IsShellUiForeground()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            if (IsShellUiWindow(fg)) return true;
            IntPtr root = NativeMethods.GetAncestor(fg, NativeMethods.GA_ROOTOWNER);
            return root != IntPtr.Zero && root != fg && IsShellUiWindow(root);
        }

        public bool IsTrayOverflowOpen()
        {
            return IsVisibleTopLevelClass("TopLevelWindowForOverflowXamlIsland") ||
                   IsVisibleTopLevelClass("XamlExplorerHostIslandWindow") ||
                   IsVisibleTopLevelClass("NotifyIconOverflowWindow");
        }

        private static bool IsVisibleTopLevelClass(string className)
        {
            IntPtr after = IntPtr.Zero;
            while (true)
            {
                IntPtr h = NativeMethods.FindWindowEx(IntPtr.Zero, after, className, null);
                if (h == IntPtr.Zero) return false;
                if (NativeMethods.IsWindowVisible(h)) return true;
                after = h;
            }
        }

        private bool IsShellUiWindow(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            string cls = GetWindowClass(h);
            string title = GetWindowTitle(h);
            string process = GetProcessName(h);
            if (IsTaskbarClass(cls) || ShellProcesses.Contains(process) || IsExplorerShellSurface(h, cls, title))
                return true;

            bool shellProcess = string.Equals(process, "explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(process, "shellhost.exe", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(process, "shellexperiencehost.exe", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(process, "startmenuexperiencehost.exe", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(process, "searchhost.exe", StringComparison.OrdinalIgnoreCase);
            return shellProcess && (cls.IndexOf("Xaml", StringComparison.OrdinalIgnoreCase) >= 0 || ShellClasses.Contains(cls));
        }

        private bool IsExplorerShellSurface(IntPtr h, string cls, string title)
        {
            if (!string.Equals(GetProcessName(h), "explorer.exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (IsTaskbarClass(cls) || cls == "#32768") return true;
            if (cls == "TopLevelWindowForOverflowXamlIsland" || cls == "XamlExplorerHostIslandWindow" ||
                cls == "TaskListThumbnailWnd" || cls == "NotifyIconOverflowWindow" || cls == "Shell_InputSwitchTopLevelWindow")
                return true;

            string lower = title.ToLowerInvariant();
            return lower == "task view" || lower == "notification center" || lower == "control center";
        }

        private static bool IsTaskbarClass(string cls)
        {
            return cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
        }

        public string DescribeWindow(IntPtr h)
        {
            if (h == IntPtr.Zero) return "hwnd=0";
            return string.Format("hwnd=0x{0:x}, process={1}, class=\"{2}\", title=\"{3}\"",
                h.ToInt64(), GetProcessName(h), GetWindowClass(h), GetWindowTitle(h));
        }

        private static string GetProcessName(IntPtr h)
        {
            try
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return string.Empty;
                using (Process process = Process.GetProcessById((int)pid))
                {
                    string name = process.ProcessName;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
                    return name;
                }
            }
            catch { return string.Empty; }
        }

        private static string GetWindowClass(IntPtr h)
        {
            StringBuilder b = new StringBuilder(256);
            return NativeMethods.GetClassName(h, b, b.Capacity) > 0 ? b.ToString() : string.Empty;
        }

        private static string GetWindowTitle(IntPtr h)
        {
            int length = NativeMethods.GetWindowTextLength(h);
            if (length <= 0) return string.Empty;
            StringBuilder b = new StringBuilder(length + 1);
            return NativeMethods.GetWindowText(h, b, b.Capacity) > 0 ? b.ToString() : string.Empty;
        }
    }
}
