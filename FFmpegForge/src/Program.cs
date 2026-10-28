using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("FFmpeg Forge")]
[assembly: System.Reflection.AssemblyDescription("Universal Video / Audio / Image converter powered by FFmpeg")]
[assembly: System.Reflection.AssemblyProduct("FFmpeg Forge")]
[assembly: System.Reflection.AssemblyVersion("1.1.3.1")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.3.1")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.1.3a")]

namespace FFmpegForge
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            // never close silently: any unexpected error is shown and written to FFmpegForge-error.log
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception, true);
            try
            {
                try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                // started from inside an archive (WinRAR / 7-Zip / Windows "compressed folder"): only the .exe was unpacked
                string low = dir.ToLowerInvariant();
                if (low.Contains("\\rar$ex") || low.Contains("\\7zo") || low.Contains("\\temp1_") || low.Contains(".zip\\") || low.Contains(".rar\\"))
                    MessageBox.Show("FFmpeg Forge is running from inside the archive, so its files (FFmpeg in bin\\, settings…) are not available.\n\n" +
                                    "Extract the whole archive first (right-click → Extract here), then start FFmpegForge.exe from the extracted folder.",
                                    "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                var f = new MainForm();
                if (args.Length > 0) f.Shown += (s, e) => f.AddFromCommandLine(args);
                Application.Run(f);
            }
            catch (Exception ex) { Report(ex, true); }
        }

        static void Report(Exception ex, bool fatal)
        {
            string text = "FFmpeg Forge 1.1.3a — " + (fatal ? "fatal error" : "error") + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                          "Folder: " + AppDomain.CurrentDomain.BaseDirectory + "\r\n" +
                          "Windows: " + Environment.OSVersion + "  .NET: " + Environment.Version + "\r\n\r\n" + (ex != null ? ex.ToString() : "(unknown)") + "\r\n\r\n";
            string log = null;
            foreach (var d in new[] { AppDomain.CurrentDomain.BaseDirectory, System.IO.Path.GetTempPath() })
            {
                try { log = System.IO.Path.Combine(d, "FFmpegForge-error.log"); System.IO.File.AppendAllText(log, text); break; }
                catch { log = null; }
            }
            try
            {
                MessageBox.Show((ex != null ? ex.GetType().Name + ": " + ex.Message : "Unknown error") +
                                (log != null ? "\n\nDetails were saved in:\n" + log : ""),
                                "FFmpeg Forge — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
