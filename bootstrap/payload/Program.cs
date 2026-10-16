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


[assembly: AssemblyTitle("Slippery Taskbar")]
[assembly: AssemblyProduct("Slippery Taskbar")]
[assembly: AssemblyDescription("Hide the Windows 11 taskbar until the Windows key or shell UI intentionally reveals it.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace SlipperyTaskbar
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\SlipperyTaskbar.CSharp.Singleton", out createdNew))
            {
                if (!createdNew)
                    return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    CrashLog.Write(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex != null) CrashLog.Write(ex);
                };

                SlipperyContext context = new SlipperyContext();
                try
                {
                    Application.Run(context);
                }
                finally
                {
                    context.Dispose();
                }
            }
        }
    }

    internal enum TrayCommand
    {
        None,
        ToggleEnabled,
        ShowFiveSeconds,
        ToggleStartup,
        OpenPreferences,
        CopyDiagnostics,
        Exit
    }

}
