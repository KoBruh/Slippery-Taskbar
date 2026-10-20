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
        private DateTime _lastForegroundGapCheckUtc = DateTime.MinValue;
        private int _foregroundGapCorrections;

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        public void CorrectForegroundMaximizedGap()
        {
            DateTime now = DateTime.UtcNow;
            if (_lastForegroundGapCheckUtc != DateTime.MinValue &&
                now - _lastForegroundGapCheckUtc < TimeSpan.FromMilliseconds(75))
                return;
            _lastForegroundGapCheckUtc = now;

            IntPtr hWnd = NativeMethods.GetForegroundWindow();
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hWnd) || !IsZoomed(hWnd)) return;

            string cls = GetWindowClass(hWnd);
            if (IsTaskbarClass(cls) || IsShellUiWindow(hWnd)) return;

            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(hWnd, out r)) return;

            IntPtr monitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            NativeMethods.MONITORINFO mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(monitor, ref mi)) return;

            NativeMethods.RECT m = mi.rcMonitor;
            int monitorWidth = m.Right - m.Left;
            int monitorHeight = m.Bottom - m.Top;
            if (monitorWidth <= 0 || monitorHeight <= 0) return;

            int tolerance = Math.Max(12, Math.Min(monitorWidth, monitorHeight) / 100);
            int maxGap = 192;

            int topGap = r.Top - m.Top;
            int leftGap = r.Left - m.Left;
            int rightGap = m.Right - r.Right;
            int bottomGap = m.Bottom - r.Bottom;

            bool changed = false;
            int x = r.Left;
            int y = r.Top;
            int right = r.Right;
            int bottom = r.Bottom;

            if (topGap >= 12 && topGap <= maxGap &&
                Math.Abs(leftGap) <= tolerance && Math.Abs(rightGap) <= tolerance && Math.Abs(bottomGap) <= tolerance)
            {
                y = m.Top;
                changed = true;
            }
            else if (leftGap >= 12 && leftGap <= maxGap &&
                     Math.Abs(topGap) <= tolerance && Math.Abs(rightGap) <= tolerance && Math.Abs(bottomGap) <= tolerance)
            {
                x = m.Left;
                changed = true;
            }
            else if (rightGap >= 12 && rightGap <= maxGap &&
                     Math.Abs(topGap) <= tolerance && Math.Abs(leftGap) <= tolerance && Math.Abs(bottomGap) <= tolerance)
            {
                right = m.Right;
                changed = true;
            }

            if (!changed) return;

            int cx = right - x;
            int cy = bottom - y;
            if (cx <= 0 || cy <= 0) return;

            if (NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, x, y, cx, cy,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSENDCHANGING))
            {
                _lastGapCorrections++;
                _foregroundGapCorrections++;
            }
        }

        public int ForegroundGapCorrectionCount
        {
            get { return _foregroundGapCorrections; }
        }
    }
}
