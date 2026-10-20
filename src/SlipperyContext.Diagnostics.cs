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
    internal sealed partial class SlipperyContext : ApplicationContext, IDisposable
    {
        private void CopyDiagnostics()
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Slippery Taskbar C# diagnostics");
            b.AppendLine("Build: 1.0.4-policy");
            b.AppendLine("Runtime: " + Environment.Version);
            b.AppendLine("OS: " + Environment.OSVersion);
            b.AppendLine("Executable: " + Application.ExecutablePath);
            b.AppendLine("Preferences: " + _preferencesStore.FilePath);
            b.AppendLine("Enabled: " + _enabled);
            b.AppendLine("Start with Windows: " + _startWithWindows);
            b.AppendLine("Keep visible while interacting: " + _preferencesStore.Current.KeepVisibleWhileInteracting);
            b.AppendLine("Native auto-hide active: " + ((_taskbars.GetAppbarState() & NativeMethods.ABS_AUTOHIDE) != 0));
            b.AppendLine("Appbar policy repair attempts: " + _appbarPolicyRepairAttempts);
            b.AppendLine("Appbar policy repair successes: " + _appbarPolicyRepairSuccesses);
            b.AppendLine("Last appbar policy repair UTC: " + (_lastAppbarPolicyRepairUtc == DateTime.MinValue ? "never" : _lastAppbarPolicyRepairUtc.ToString("O")));
            b.AppendLine("Tray visibility guard active: " + (DateTime.UtcNow < _trayInteractionPinUntilUtc || _taskbars.IsTrayOverflowOpen()));
            b.AppendLine("Notification-area mouse guard installed: " + (_mouseHook != IntPtr.Zero));
            b.AppendLine("Work area: " + _taskbars.DescribeWorkArea());
            b.AppendLine("Win key down: " + _winDown);
            b.AppendLine("Shell UI foreground: " + _taskbars.IsShellUiForeground());
            b.AppendLine("Tray overflow open: " + _taskbars.IsTrayOverflowOpen());

            IList<IntPtr> taskbars = _taskbars.RefreshTaskbars();
            b.AppendLine("Taskbars found: " + taskbars.Count);
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            b.AppendLine("Foreground: " + _taskbars.DescribeWindow(foreground));
            for (int i = 0; i < taskbars.Count; i++)
                b.AppendLine("Taskbar " + i + ": " + _taskbars.DescribeWindow(taskbars[i]));

            try
            {
                Clipboard.SetText(b.ToString());
                _notifyIcon.ShowBalloonTip(2500, "Slippery Taskbar", "Diagnostics copied to the clipboard.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        }

        protected override void ExitThreadCore()
        {
            Dispose();
            base.ExitThreadCore();
        }

        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { if (_timer != null) { _timer.Stop(); _timer.Dispose(); } } catch { }
            try { NativeMethods.UnregisterHotKey(_window.Handle, NativeMethods.HOTKEY_ID); } catch { }
            try { if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; } } catch { }
            try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _taskbars.RestoreOriginalTaskbarState(); } catch { }
            try { _window.Destroy(); } catch { }
        }
    }
}
