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
        internal void HandleWindowMessage(ref Message m)
        {
            if (_taskbarCreatedMessage != 0 && m.Msg == _taskbarCreatedMessage)
            {
                HandleExplorerRestart();
                return;
            }

            switch (m.Msg)
            {
                case NativeMethods.WM_INPUT:
                    HandleRawInput(m.LParam);
                    break;
                case NativeMethods.WM_HOTKEY:
                    if (m.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
                        SetEnabled(!_enabled);
                    break;
                case NativeMethods.WM_DISPLAYCHANGE:
                case NativeMethods.WM_SETTINGCHANGE:
                    _taskbars.RefreshTaskbars();
                    RepairAppbarPolicyIfNeeded();
                    break;
                case NativeMethods.WM_POWERBROADCAST:
                    int power = m.WParam.ToInt32();
                    if (power == NativeMethods.PBT_APMRESUMEAUTOMATIC || power == NativeMethods.PBT_APMRESUMESUSPEND)
                    {
                        _taskbars.RefreshTaskbars();
                        _taskbars.EnsureAppbarPolicy(_enabled);
                        _taskbars.EnsureWorkAreaPolicy(_enabled);
                        _lastAppbarPolicyCheckUtc = DateTime.UtcNow;
                        _visibleUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
                    }
                    break;
            }
        }

        private void HandleExplorerRestart()
        {
            _taskbars.RefreshTaskbars();
            _taskbars.EnsureAppbarPolicy(_enabled);
            _taskbars.EnsureWorkAreaPolicy(_enabled);
            _lastAppbarPolicyCheckUtc = DateTime.UtcNow;
            _visibleUntilUtc = DateTime.UtcNow.AddMilliseconds(750);

            // NotifyIcon normally recreates itself after TaskbarCreated, but
            // toggling visibility makes recovery explicit across Explorer builds.
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;
            }
        }

        private void RepairAppbarPolicyIfNeeded()
        {
            if (!_enabled) return;

            _lastAppbarPolicyCheckUtc = DateTime.UtcNow;
            uint state = _taskbars.GetAppbarState();
            if ((state & NativeMethods.ABS_AUTOHIDE) != 0) return;

            _appbarPolicyRepairAttempts++;
            _taskbars.RefreshTaskbars();
            _taskbars.EnsureAppbarPolicy(true);
            _lastAppbarPolicyRepairUtc = DateTime.UtcNow;

            uint after = _taskbars.GetAppbarState();
            if ((after & NativeMethods.ABS_AUTOHIDE) != 0)
                _appbarPolicyRepairSuccesses++;
        }

        private void HandleRawInput(IntPtr rawInputHandle)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER));
            uint first = NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (first != 0 || size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint read = NativeMethods.GetRawInputData(rawInputHandle, NativeMethods.RID_INPUT, buffer, ref size, headerSize);
                if (read == UInt32.MaxValue || read != size) return;

                byte[] bytes = new byte[(int)size];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                if (bytes.Length < (int)headerSize + 12) return;
                uint type = BitConverter.ToUInt32(bytes, 0);
                if (type != NativeMethods.RIM_TYPEKEYBOARD) return;

                int offset = (int)headerSize;
                ushort vkey = BitConverter.ToUInt16(bytes, offset + 6);
                uint message = BitConverter.ToUInt32(bytes, offset + 8);
                if (vkey != NativeMethods.VK_LWIN && vkey != NativeMethods.VK_RWIN) return;

                bool down = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                bool up = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
                if (!down && !up) return;

                if (vkey == NativeMethods.VK_LWIN) _rawLeftWin = down;
                if (vkey == NativeMethods.VK_RWIN) _rawRightWin = down;
                _lastRawWinEventUtc = DateTime.UtcNow;
                OnWinKeyState(_rawLeftWin || _rawRightWin || IsAsyncKeyDown(NativeMethods.VK_LWIN) || IsAsyncKeyDown(NativeMethods.VK_RWIN));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void ReconcileWinKeyStateFromPolling()
        {
            DateTime now = DateTime.UtcNow;
            bool left = IsAsyncKeyDown(NativeMethods.VK_LWIN);
            bool right = IsAsyncKeyDown(NativeMethods.VK_RWIN);
            if (!left && !right && (_rawLeftWin || _rawRightWin) &&
                _lastRawWinEventUtc != DateTime.MinValue && now - _lastRawWinEventUtc > TimeSpan.FromMilliseconds(120))
            {
                _rawLeftWin = false;
                _rawRightWin = false;
            }
            OnWinKeyState(left || right || _rawLeftWin || _rawRightWin);
        }

        private static bool IsAsyncKeyDown(int vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & unchecked((short)0x8000)) != 0;
        }

        private void OnWinKeyState(bool down)
        {
            if (down == _winDown) return;
            _winDown = down;
            if (down)
            {
                ExtendVisibleUntil(WinPressGrace);
                if (!_lastShouldShow)
                {
                    _taskbars.ApplyVisibility(true);
                    _lastShouldShow = true;
                    _lastApplyUtc = DateTime.UtcNow;
                }
            }
            else
            {
                ExtendVisibleUntil(WinReleaseGrace);
            }
        }

    }
}
