using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("FFmpeg Forge")]
[assembly: System.Reflection.AssemblyDescription("Universal Video / Audio / Image converter powered by FFmpeg")]
[assembly: System.Reflection.AssemblyProduct("FFmpeg Forge")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace FFmpegForge
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var f = new MainForm();
            if (args.Length > 0) f.Shown += (s, e) => f.AddFromCommandLine(args);
            Application.Run(f);
        }
    }
}
