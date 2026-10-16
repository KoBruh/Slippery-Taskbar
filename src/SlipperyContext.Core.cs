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
        private readonly PreferencesStore _preferencesStore;
        private readonly TaskbarController _taskbars;
        private readonly HiddenMessageWindow _window;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _enabledItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly uint _taskbarCreatedMessage;

        private TrayCommand _pendingCommand;
        private bool _trayMenuOpen;
        private bool _enabled;
        private bool _startWithWindows;
        private bool _winDown;
        private bool _rawLeftWin;
        private bool _rawRightWin;
        private DateTime _lastRawWinEventUtc;
        private DateTime _visibleUntilUtc;
        private bool _lastShouldShow = true;
        private DateTime _lastApplyUtc = DateTime.MinValue;
        private DateTime _trayInteractionPinUntilUtc = DateTime.MinValue;
        private IntPtr _mouseHook = IntPtr.Zero;
        private NativeMethods.LowLevelMouseProc _mouseHookProc;
        private bool _disposed;

        private static readonly TimeSpan WinPressGrace = TimeSpan.FromMilliseconds(700);
        private static readonly TimeSpan WinReleaseGrace = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan ShellTransitionGrace = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan HiddenStateWatchdog = TimeSpan.FromMilliseconds(900);
        private static readonly TimeSpan TrayInteractionPinGrace = TimeSpan.FromMilliseconds(900);
        private static readonly TimeSpan TrayClickPinGrace = TimeSpan.FromMilliseconds(1800);
        private static readonly TimeSpan TrayHoverPinGrace = TimeSpan.FromMilliseconds(500);

        public SlipperyContext()
        {
            _preferencesStore = new PreferencesStore();
            Preferences prefs = _preferencesStore.Load();
            _enabled = prefs.Enabled;
            _startWithWindows = prefs.StartWithWindows;

            _taskbars = new TaskbarController(_preferencesStore.DirectoryPath);
            _window = new HiddenMessageWindow(this);
            IntPtr ignored = _window.Handle;

            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            NativeMethods.RegisterRawKeyboard(_window.Handle);
            NativeMethods.RegisterHotKey(_window.Handle, NativeMethods.HOTKEY_ID,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_WIN | NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_F11);

            // Observe notification-area pointer activity before Explorer processes it.
            // Native auto-hide remains enabled and stable for the entire enabled
            // session; this hook only starts a short visibility guard so Explorer
            // cannot slide the bar away underneath the hidden-icons flyout.
            _mouseHookProc = MouseHookCallback;
            _mouseHook = NativeMethods.InstallLowLevelMouseHook(_mouseHookProc);

            _menu = new ContextMenuStrip();
            _enabledItem = AddMenuItem("Enable taskbar hiding    Ctrl+Win+F11", TrayCommand.ToggleEnabled);
            AddMenuItem("Show taskbar for 5 seconds", TrayCommand.ShowFiveSeconds);
            _startupItem = AddMenuItem("Start with Windows", TrayCommand.ToggleStartup);
            _menu.Items.Add(new ToolStripSeparator());
            AddMenuItem("Open preferences folder", TrayCommand.OpenPreferences);
            AddMenuItem("Copy diagnostics", TrayCommand.CopyDiagnostics);
            _menu.Items.Add(new ToolStripSeparator());
            AddMenuItem("Exit", TrayCommand.Exit);

            _menu.Opening += OnMenuOpening;
            _menu.ItemClicked += OnMenuItemClicked;
            _menu.Closed += OnMenuClosed;

            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (appIcon == null) appIcon = SystemIcons.Application;

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = appIcon;
            _notifyIcon.Text = "Slippery Taskbar";
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;

            _preferencesStore.SyncStartupRegistration(_startWithWindows, Application.ExecutablePath);
            _taskbars.EnsureAppbarPolicy(_enabled);
            _taskbars.EnsureWorkAreaPolicy(_enabled);
            _taskbars.ApplyVisibility(true);
            _lastShouldShow = true;
            _lastApplyUtc = DateTime.UtcNow;
            _visibleUntilUtc = DateTime.UtcNow.AddMilliseconds(700);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate { PollTaskbarState(); };
            _timer.Start();
        }

        private ToolStripMenuItem AddMenuItem(string text, TrayCommand command)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Tag = command;
            _menu.Items.Add(item);
            return item;
        }

        private void OnMenuOpening(object sender, CancelEventArgs e)
        {
            _pendingCommand = TrayCommand.None;
            _trayMenuOpen = true;
            _enabledItem.Checked = _enabled;
            _startupItem.Checked = _startWithWindows;

            if (!_lastShouldShow)
            {
                _taskbars.ApplyVisibility(true);
                _lastShouldShow = true;
                _lastApplyUtc = DateTime.UtcNow;
            }
        }

        private void OnMenuItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem != null && e.ClickedItem.Tag is TrayCommand)
                _pendingCommand = (TrayCommand)e.ClickedItem.Tag;
        }

        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            _trayMenuOpen = false;
            ExtendVisibleUntil(ShellTransitionGrace);

            TrayCommand command = _pendingCommand;
            _pendingCommand = TrayCommand.None;
            if (command != TrayCommand.None)
            {
                // Run after WinForms has completely unwound ContextMenuStrip's
                // nested popup loop. This avoids the stale-focus/menu bug from
                // the earlier raw Shell_NotifyIcon implementation.
                _window.BeginInvoke((MethodInvoker)delegate { ExecuteCommand(command); });
            }
        }

        private void ExecuteCommand(TrayCommand command)
        {
            switch (command)
            {
                case TrayCommand.ToggleEnabled:
                    SetEnabled(!_enabled);
                    break;
                case TrayCommand.ShowFiveSeconds:
                    _visibleUntilUtc = DateTime.UtcNow.AddSeconds(5);
                    _taskbars.ApplyVisibility(true);
                    _lastShouldShow = true;
                    _lastApplyUtc = DateTime.UtcNow;
                    break;
                case TrayCommand.ToggleStartup:
                    _startWithWindows = !_startWithWindows;
                    SavePreferences();
                    _preferencesStore.SyncStartupRegistration(_startWithWindows, Application.ExecutablePath);
                    break;
                case TrayCommand.OpenPreferences:
                    _preferencesStore.OpenFolder();
                    break;
                case TrayCommand.CopyDiagnostics:
                    CopyDiagnostics();
                    break;
                case TrayCommand.Exit:
                    ExitThread();
                    break;
            }
        }

        private void SetEnabled(bool value)
        {
            if (_enabled == value) return;
            _enabled = value;
            SavePreferences();
            _trayInteractionPinUntilUtc = DateTime.MinValue;
            _taskbars.EnsureAppbarPolicy(_enabled);
            _taskbars.EnsureWorkAreaPolicy(_enabled);

            if (!_enabled)
            {
                _taskbars.ApplyVisibility(true);
                _lastShouldShow = true;
                _lastApplyUtc = DateTime.UtcNow;
            }
            else
            {
                _visibleUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
                _taskbars.ApplyVisibility(true);
                _lastShouldShow = true;
                _lastApplyUtc = DateTime.UtcNow;
            }
        }

        private void SavePreferences()
        {
            Preferences prefs = _preferencesStore.Current;
            prefs.Enabled = _enabled;
            prefs.StartWithWindows = _startWithWindows;
            _preferencesStore.Save(prefs);
        }

    }
}
