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
    internal sealed class HiddenMessageWindow : NativeWindow
    {
        private readonly SlipperyContext _owner;
        private readonly Control _invoker;

        public HiddenMessageWindow(SlipperyContext owner)
        {
            _owner = owner;
            CreateParams cp = new CreateParams();
            cp.Caption = "SlipperyTaskbarHiddenWindow";
            cp.ClassName = null;
            CreateHandle(cp);

            _invoker = new Control();
            IntPtr invokerHandle = _invoker.Handle;
        }

        public void BeginInvoke(MethodInvoker action)
        {
            if (_invoker.IsHandleCreated)
                _invoker.BeginInvoke(action);
        }

        protected override void WndProc(ref Message m)
        {
            _owner.HandleWindowMessage(ref m);
            base.WndProc(ref m);
        }

        public void Destroy()
        {
            try { _invoker.Dispose(); } catch { }
            DestroyHandle();
        }
    }

    [DataContract]
    internal sealed class Preferences
    {
        [DataMember(Name = "version", Order = 1)]
        public int Version = 1;

        [DataMember(Name = "enabled", Order = 2)]
        public bool Enabled = true;

        [DataMember(Name = "start_with_windows", Order = 3)]
        public bool StartWithWindows = false;

        [DataMember(Name = "keep_visible_while_interacting", Order = 4)]
        public bool KeepVisibleWhileInteracting = true;
    }

    internal sealed class PreferencesStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "SlipperyTaskbar";
        private const string LegacyRunValue = "ButteryTaskbar11";
        private const string SettingsKey = @"Software\SlipperyTaskbar";
        private const string LegacySettingsKey = @"Software\ButteryTaskbar11";

        public Preferences Current { get; private set; }
        public string DirectoryPath { get; private set; }
        public string FilePath { get; private set; }

        public PreferencesStore()
        {
            DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slippery-Taskbar");
            FilePath = Path.Combine(DirectoryPath, "config.json");
            Current = new Preferences();
        }

        public Preferences Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (FileStream stream = File.OpenRead(FilePath))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Preferences));
                        Preferences loaded = serializer.ReadObject(stream) as Preferences;
                        if (loaded != null)
                        {
                            if (loaded.Version <= 0) loaded.Version = 1;
                            Current = loaded;
                            return Current;
                        }
                    }
                }
            }
            catch (Exception ex) { CrashLog.Write(ex); }

            Current = MigrateFromRegistry();
            Save(Current);
            return Current;
        }

        private Preferences MigrateFromRegistry()
        {
            Preferences prefs = new Preferences();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Enabled");
                        if (value != null) prefs.Enabled = Convert.ToInt32(value) != 0;
                    }
                }
                using (RegistryKey legacy = Registry.CurrentUser.OpenSubKey(LegacySettingsKey))
                {
                    if (legacy != null)
                    {
                        object value = legacy.GetValue("Enabled");
                        if (value != null) prefs.Enabled = Convert.ToInt32(value) != 0;
                    }
                }
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (run != null)
                        prefs.StartWithWindows = run.GetValue(RunValue) != null || run.GetValue(LegacyRunValue) != null;
                }
            }
            catch (Exception ex) { CrashLog.Write(ex); }
            return prefs;
        }

        public void Save(Preferences preferences)
        {
            Current = preferences;
            Current.Version = 1;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string temp = FilePath + ".tmp";
                using (FileStream stream = File.Create(temp))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Preferences));
                    serializer.WriteObject(stream, Current);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        }

        public void SyncStartupRegistration(bool enabled, string executablePath)
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (run == null) return;
                    if (enabled)
                        run.SetValue(RunValue, "\"" + executablePath + "\"", RegistryValueKind.String);
                    else
                        run.DeleteValue(RunValue, false);
                    run.DeleteValue(LegacyRunValue, false);
                }
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        }

        public void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                Process.Start("explorer.exe", DirectoryPath);
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        }
    }

}
