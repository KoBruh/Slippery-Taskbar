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
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _enabled && _lastShouldShow)
            {
                int message = wParam.ToInt32();
                if (message == NativeMethods.WM_MOUSEMOVE || message == NativeMethods.WM_LBUTTONDOWN)
                {
                    try
                    {
                        NativeMethods.MSLLHOOKSTRUCT mouse = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                            lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));

                        // Do not touch ABM_SETSTATE here. Changing ABS_AUTOHIDE causes
                        // Explorer to recalculate the desktop work area, which makes
                        // maximized windows resize when Win or a tray flyout is used.
                        // Instead, pre-authorize the notification-area transition and
                        // keep the actual taskbar window physically on-screen.
                        if (_taskbars.IsPointOverNotificationArea(mouse.pt))
                        {
                            TimeSpan grace = message == NativeMethods.WM_LBUTTONDOWN
                                ? TrayClickPinGrace
                                : TrayHoverPinGrace;
                            DateTime until = DateTime.UtcNow.Add(grace);
                            if (until > _trayInteractionPinUntilUtc) _trayInteractionPinUntilUtc = until;
                            ExtendVisibleUntil(grace);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write(ex);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void PollTaskbarState()
        {
            if (!_enabled) return;

            ReconcileWinKeyStateFromPolling();
            DateTime now = DateTime.UtcNow;

            bool trayOverflowOpen = _taskbars.IsTrayOverflowOpen();
            if (trayOverflowOpen)
            {
                ExtendVisibleUntil(ShellTransitionGrace);
                DateTime pinUntil = now.Add(TrayInteractionPinGrace);
                if (pinUntil > _trayInteractionPinUntilUtc) _trayInteractionPinUntilUtc = pinUntil;
            }

            bool trayVisibilityGuard = trayOverflowOpen || now < _trayInteractionPinUntilUtc;

            bool shellUi = _trayMenuOpen || _taskbars.IsShellUiForeground() || trayOverflowOpen;
            if (shellUi) ExtendVisibleUntil(ShellTransitionGrace);

            bool interacting = _preferencesStore.Current.KeepVisibleWhileInteracting &&
                               _taskbars.IsCursorOverVisibleTaskbar(_lastShouldShow);
            if (interacting) ExtendVisibleUntil(TimeSpan.FromMilliseconds(650));

            bool shouldShow = _winDown || shellUi || interacting || trayVisibilityGuard || now < _visibleUntilUtc;

            bool anyTaskbarVisible;
            bool allTaskbarsVisible;
            _taskbars.GetActualVisibilityState(out anyTaskbarVisible, out allTaskbarsVisible);

            // Native auto-hide is enabled only so Windows releases the taskbar's work
            // area. Explorer may still try to reveal the bar when the pointer touches
            // an edge, so immediately correct any visibility that Slippery did not
            // authorize. Conversely, if Explorer auto-hides while Start/shell UI is
            // active, put the taskbar back on-screen.
            // Only correct the dangerous direction here: Explorer revealing a
            // taskbar that Slippery intends to keep hidden. When the taskbar is
            // intentionally visible, Explorer is allowed to own the intermediate
            // frames of Start/tray/XAML animations. Re-showing or repositioning the
            // taskbar during those frames is what caused the visible close/reopen
            // stutter around the hidden-icons flyout.
            bool unauthorizedReveal = !shouldShow && anyTaskbarVisible;

            if (shouldShow != _lastShouldShow || unauthorizedReveal || _lastApplyUtc == DateTime.MinValue)
            {
                _taskbars.ApplyVisibility(shouldShow);
                _lastShouldShow = shouldShow;
                _lastApplyUtc = now;
                return;
            }

            if (!shouldShow && now - _lastApplyUtc >= HiddenStateWatchdog)
            {
                _taskbars.ApplyVisibility(false);
                _lastApplyUtc = now;
            }
        }

        private void ExtendVisibleUntil(TimeSpan duration)
        {
            DateTime candidate = DateTime.UtcNow.Add(duration);
            if (candidate > _visibleUntilUtc) _visibleUntilUtc = candidate;
        }

    }
}
