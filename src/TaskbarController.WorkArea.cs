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
        private bool _lastWorkAreaCorrectionSucceeded;

        public void EnsureWorkAreaPolicy(bool enabled)
        {
            _originalWorkAreas.Clear();
            _workAreasCaptured = false;
            DeleteWorkAreaState();
            _lastWorkAreaCorrectionSucceeded = false;

            if (!enabled) return;

            // ABM_SETSTATE can make Windows report the full monitor as its work area
            // without forcing already-maximized windows to recalculate their maximize
            // rectangle. Re-applying each monitor's full bounds through SPI_SETWORKAREA
            // with SPIF_SENDCHANGE both makes the desired work area explicit and sends
            // the normal system-wide work-area notification. This happens only when the
            // policy is applied (startup/re-enable/Explorer recovery), never on Win-key
            // taskbar show/hide transitions.
            Screen[] screens = Screen.AllScreens;
            if (screens.Length == 0) return;

            bool success = true;
            foreach (Screen screen in screens)
            {
                NativeMethods.RECT full = RectFromRectangle(screen.Bounds);
                if (!NativeMethods.SystemParametersInfoSetWorkArea(ref full, NativeMethods.SPIF_SENDCHANGE))
                    success = false;
            }
            _lastWorkAreaCorrectionSucceeded = success;
        }

        private void CaptureOriginalWorkAreas()
        {
            if (_workAreasCaptured) return;

            List<WorkAreaEntry> saved;
            if (TryLoadWorkAreaState(out saved) && SnapshotMatchesCurrentMonitors(saved))
            {
                _originalWorkAreas.Clear();
                _originalWorkAreas.AddRange(saved);
                _workAreasCaptured = true;
                return;
            }

            DeleteWorkAreaState();
            _originalWorkAreas.Clear();
            foreach (Screen screen in Screen.AllScreens)
            {
                WorkAreaEntry entry = new WorkAreaEntry();
                entry.Monitor = RectFromRectangle(screen.Bounds);
                entry.Work = RectFromRectangle(screen.WorkingArea);
                _originalWorkAreas.Add(entry);
            }
            if (_originalWorkAreas.Count == 0) return;

            _workAreasCaptured = true;
            SaveWorkAreaState();
        }

        private void RestoreOriginalWorkAreas()
        {
            if (!_workAreasCaptured)
            {
                List<WorkAreaEntry> saved;
                if (TryLoadWorkAreaState(out saved) && SnapshotMatchesCurrentMonitors(saved))
                {
                    _originalWorkAreas.Clear();
                    _originalWorkAreas.AddRange(saved);
                    _workAreasCaptured = true;
                }
            }

            if (_workAreasCaptured)
            {
                foreach (WorkAreaEntry entry in _originalWorkAreas)
                {
                    NativeMethods.RECT work = entry.Work;
                    NativeMethods.SystemParametersInfoSetWorkArea(ref work, NativeMethods.SPIF_SENDCHANGE);
                }
            }

            _originalWorkAreas.Clear();
            _workAreasCaptured = false;
            DeleteWorkAreaState();
        }

        private static NativeMethods.RECT RectFromRectangle(Rectangle rectangle)
        {
            NativeMethods.RECT r = new NativeMethods.RECT();
            r.Left = rectangle.Left; r.Top = rectangle.Top;
            r.Right = rectangle.Right; r.Bottom = rectangle.Bottom;
            return r;
        }

        private static bool SnapshotMatchesCurrentMonitors(List<WorkAreaEntry> saved)
        {
            Screen[] screens = Screen.AllScreens;
            if (saved == null || saved.Count != screens.Length) return false;

            for (int i = 0; i < screens.Length; i++)
            {
                NativeMethods.RECT current = RectFromRectangle(screens[i].Bounds);
                bool found = false;
                foreach (WorkAreaEntry entry in saved)
                {
                    if (RectEquals(entry.Monitor, current))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        private static bool RectEquals(NativeMethods.RECT a, NativeMethods.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        private void SaveWorkAreaState()
        {
            try
            {
                string directory = Path.GetDirectoryName(_workAreaStatePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (FileStream stream = File.Create(_workAreaStatePath))
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(0x53545741);
                    writer.Write(2);
                    writer.Write(_originalWorkAreas.Count);
                    foreach (WorkAreaEntry entry in _originalWorkAreas)
                    {
                        WriteRect(writer, entry.Monitor);
                        WriteRect(writer, entry.Work);
                    }
                    writer.Flush();
                    stream.Flush(true);
                }
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        }

        private bool TryLoadWorkAreaState(out List<WorkAreaEntry> entries)
        {
            entries = new List<WorkAreaEntry>();
            try
            {
                if (!File.Exists(_workAreaStatePath)) return false;
                using (FileStream stream = File.OpenRead(_workAreaStatePath))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != 0x53545741 || reader.ReadInt32() != 2) return false;
                    int count = reader.ReadInt32();
                    if (count < 1 || count > 32) return false;
                    for (int i = 0; i < count; i++)
                    {
                        WorkAreaEntry entry = new WorkAreaEntry();
                        entry.Monitor = ReadRect(reader);
                        entry.Work = ReadRect(reader);
                        entries.Add(entry);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                entries.Clear();
                return false;
            }
        }

        private static void WriteRect(BinaryWriter writer, NativeMethods.RECT r)
        {
            writer.Write(r.Left); writer.Write(r.Top); writer.Write(r.Right); writer.Write(r.Bottom);
        }

        private static NativeMethods.RECT ReadRect(BinaryReader reader)
        {
            NativeMethods.RECT r = new NativeMethods.RECT();
            r.Left = reader.ReadInt32(); r.Top = reader.ReadInt32();
            r.Right = reader.ReadInt32(); r.Bottom = reader.ReadInt32();
            return r;
        }

        private void DeleteWorkAreaState()
        {
            try { if (File.Exists(_workAreaStatePath)) File.Delete(_workAreaStatePath); }
            catch (Exception ex) { CrashLog.Write(ex); }
        }

        public string DescribeWorkArea()
        {
            StringBuilder b = new StringBuilder();
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (i > 0) b.Append("; ");
                b.Append("monitor ").Append(i).Append(" bounds=").Append(RectangleText(screens[i].Bounds));
                b.Append(" work=").Append(RectangleText(screens[i].WorkingArea));
            }
            b.Append(", reservation=native-auto-hide");
            b.Append(", correction=").Append(_lastWorkAreaCorrectionSucceeded);
            return b.ToString();
        }

        private static string RectangleText(Rectangle r)
        {
            return string.Format("[{0},{1},{2},{3}]", r.Left, r.Top, r.Right, r.Bottom);
        }
    }
}
