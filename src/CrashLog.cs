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
    internal static class CrashLog
    {
        public static void Write(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Slippery-Taskbar");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "slippery.log"), DateTime.Now.ToString("s") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }

}
