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
        private readonly List<IntPtr> _taskbars = new List<IntPtr>();
        private readonly string _workAreaStatePath;
        private uint _originalAppbarState;
        private bool _originalAppbarCaptured;
        private readonly List<WorkAreaEntry> _originalWorkAreas = new List<WorkAreaEntry>();
        private bool _workAreasCaptured;
        private DateTime _lastForcedMaximizedPassUtc = DateTime.MinValue;
        private int _lastForcedMaximizedCount;
        private int _totalForcedMaximizedCount;

        private sealed class WorkAreaEntry
        {
            public NativeMethods.RECT Monitor;
            public NativeMethods.RECT Work;
        }

        public TaskbarController(string stateDirectory)
        {
            _workAreaStatePath = Path.Combine(stateDirectory, "workarea.state");
        }

        private static readonly HashSet<string> ShellProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe", "ShellExperienceHost.exe",
            "ShellHost.exe", "Widgets.exe", "WidgetBoard.exe"
        };

        private static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "ControlCenterWindow",
            "Windows.UI.Core.CoreWindow", "TaskListThumbnailWnd", "NotifyIconOverflowWindow",
            "Shell_InputSwitchTopLevelWindow", "MultitaskingViewFrame"
        };

        public IList<IntPtr> RefreshTaskbars()
        {
            _taskbars.Clear();
            IntPtr primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero) _taskbars.Add(primary);

            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (GetWindowClass(hWnd) == "Shell_SecondaryTrayWnd")
                    _taskbars.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            _taskbars.Sort(delegate(IntPtr a, IntPtr b) { return a.ToInt64().CompareTo(b.ToInt64()); });
            for (int i = _taskbars.Count - 1; i > 0; i--)
                if (_taskbars[i] == _taskbars[i - 1]) _taskbars.RemoveAt(i);
            return _taskbars;
        }

        public void ApplyVisibility(bool show)
        {
            RefreshTaskbars();
            foreach (IntPtr h in _taskbars)
            {
                if (!NativeMethods.IsWindow(h)) continue;

                NativeMethods.ShowWindow(h, show ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
            }
        }

        public void ForceAllMaximizedWindowsIntoTopAndSideGaps()
        {
            DateTime now = DateTime.UtcNow;
            if (_lastForcedMaximizedPassUtc != DateTime.MinValue &&
                now - _lastForcedMaximizedPassUtc < TimeSpan.FromMilliseconds(90))
                return;
            _lastForcedMaximizedPassUtc = now;
            _lastForcedMaximizedCount = 0;

            NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd)) return true;
                if (!NativeMethods.IsZoomed(hWnd)) return true;

                string cls = GetWindowClass(hWnd);
                if (IsTaskbarClass(cls) || IsShellUiWindow(hWnd)) return true;

                IntPtr monitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return true;

                NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                if (!NativeMethods.GetMonitorInfo(monitor, ref mi)) return true;

                int topReserve = mi.rcWork.Top - mi.rcMonitor.Top;
                int leftReserve = mi.rcWork.Left - mi.rcMonitor.Left;
                int rightReserve = mi.rcMonitor.Right - mi.rcWork.Right;

                if (topReserve < 8 && leftReserve < 8 && rightReserve < 8) return true;

                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(hWnd, out r)) return true;

                int tolerance = 40;
                bool looksLikeWorkAreaMaximized =
                    r.Left <= mi.rcWork.Left + tolerance &&
                    r.Top <= mi.rcWork.Top + tolerance &&
                    r.Right >= mi.rcWork.Right - tolerance &&
                    r.Bottom >= mi.rcWork.Bottom - tolerance;
                if (!looksLikeWorkAreaMaximized) return true;

                IntPtr stylePtr = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE);
                long style = stylePtr.ToInt64();
                if ((style & NativeMethods.WS_MAXIMIZE) == 0) return true;

                int extraLeft = Math.Max(0, mi.rcWork.Left - r.Left);
                int extraTop = Math.Max(0, mi.rcWork.Top - r.Top);
                int extraRight = Math.Max(0, r.Right - mi.rcWork.Right);
                int extraBottom = Math.Max(0, r.Bottom - mi.rcWork.Bottom);

                int x = mi.rcMonitor.Left - extraLeft;
                int y = mi.rcMonitor.Top - extraTop;
                int right = mi.rcMonitor.Right + extraRight;
                int bottom = mi.rcMonitor.Bottom + extraBottom;
                int cx = right - x;
                int cy = bottom - y;
                if (cx <= 0 || cy <= 0) return true;

                IntPtr restoredStyle = new IntPtr(style & ~NativeMethods.WS_MAXIMIZE);
                IntPtr previousStyle = NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, restoredStyle);
                bool clearedMaximizeStyle = previousStyle != IntPtr.Zero;

                bool moved = NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, x, y, cx, cy,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSENDCHANGING);

                if (clearedMaximizeStyle)
                    NativeMethods.SetWindowLongPtr(hWnd, NativeMethods.GWL_STYLE, stylePtr);

                if (moved)
                {
                    _lastForcedMaximizedCount++;
                    _totalForcedMaximizedCount++;
                }

                return true;
            }, IntPtr.Zero);
        }

        public int LastForcedMaximizedCount
        {
            get { return _lastForcedMaximizedCount; }
        }

        public int TotalForcedMaximizedCount
        {
            get { return _totalForcedMaximizedCount; }
        }

        private static void ForceTaskbarOnScreen(IntPtr h)
        {
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(h, out r)) return;
            int width = r.Right - r.Left;
            int height = r.Bottom - r.Top;
            if (width <= 0 || height <= 0) return;

            IntPtr monitor = NativeMethods.MonitorFromWindow(h, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;
            NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref mi)) return;

            int x = r.Left;
            int y = r.Top;
            if (width >= height)
            {
                bool top = Math.Abs(r.Top - mi.rcMonitor.Top) < Math.Abs(r.Bottom - mi.rcMonitor.Bottom);
                y = top ? mi.rcMonitor.Top : mi.rcMonitor.Bottom - height;
                x = Clamp(r.Left, mi.rcMonitor.Left, Math.Max(mi.rcMonitor.Left, mi.rcMonitor.Right - width));
            }
            else
            {
                bool left = Math.Abs(r.Left - mi.rcMonitor.Left) < Math.Abs(r.Right - mi.rcMonitor.Right);
                x = left ? mi.rcMonitor.Left : mi.rcMonitor.Right - width;
                y = Clamp(r.Top, mi.rcMonitor.Top, Math.Max(mi.rcMonitor.Top, mi.rcMonitor.Bottom - height));
            }

            NativeMethods.SetWindowPos(h, IntPtr.Zero, x, y, width, height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOSENDCHANGING);
        }

        private static int Clamp(int value, int low, int high)
        {
            if (value < low) return low;
            if (value > high) return high;
            return value;
        }

        public uint GetAppbarState()
        {
            NativeMethods.APPBARDATA data = new NativeMethods.APPBARDATA();
            data.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.APPBARDATA));
            return (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data).ToUInt64();
        }

        private void SetAppbarState(uint state)
        {
            IntPtr primaryTaskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (primaryTaskbar == IntPtr.Zero) return;

            NativeMethods.APPBARDATA data = new NativeMethods.APPBARDATA();
            data.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.APPBARDATA));
            data.hWnd = primaryTaskbar;
            data.lParam = new IntPtr((long)state);
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
        }

        public void EnsureAppbarPolicy(bool enabled)
        {
            if (!_originalAppbarCaptured)
            {
                _originalAppbarState = GetAppbarState();
                _originalAppbarCaptured = true;
            }

            if (enabled)
                SetAppbarState(_originalAppbarState | NativeMethods.ABS_AUTOHIDE);
            else
                SetAppbarState(_originalAppbarState);
        }

        public bool IsPointOverNotificationArea(NativeMethods.POINT point)
        {
            RefreshTaskbars();
            foreach (IntPtr taskbar in _taskbars)
            {
                if (!IsTaskbarActuallyOnScreen(taskbar)) continue;

                IntPtr notify = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                NativeMethods.RECT r;
                if (notify != IntPtr.Zero && NativeMethods.GetWindowRect(notify, out r) && PointInRect(point, r))
                    return true;

                if (!NativeMethods.GetWindowRect(taskbar, out r)) continue;
                int width = r.Right - r.Left;
                int height = r.Bottom - r.Top;
                if (width >= height)
                {
                    int region = Math.Min(420, Math.Max(180, width / 4));
                    NativeMethods.RECT trayRegion = r;
                    trayRegion.Left = r.Right - region;
                    if (PointInRect(point, trayRegion)) return true;
                }
                else
                {
                    int region = Math.Min(420, Math.Max(180, height / 4));
                    NativeMethods.RECT trayRegion = r;
                    trayRegion.Top = r.Bottom - region;
                    if (PointInRect(point, trayRegion)) return true;
                }
            }
            return false;
        }

        private static bool PointInRect(NativeMethods.POINT point, NativeMethods.RECT rect)
        {
            return point.X >= rect.Left && point.X < rect.Right &&
                   point.Y >= rect.Top && point.Y < rect.Bottom;
        }

    }
}
