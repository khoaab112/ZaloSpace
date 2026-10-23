using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using ZaloSpaceManager.Models;
using ZaloSpaceManager.Services;
using ZaloSpaceManager.UI;

namespace ZaloSpaceManager
{
    public class Program : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        public static void Main(string[] args)
        {
            // 1. Silent launch for Windows Startup / Auto-start (--launch <profileId>)
            if (args != null && args.Length >= 2 && args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string profileId = args[1];
                    AppSettings settings = ProfileService.LoadSettings();
                    ZaloInfo info = null;
                    if (!string.IsNullOrEmpty(settings.ZaloPath))
                    {
                        info = ZaloDetector.ValidatePath(settings.ZaloPath);
                    }
                    if (info == null || !info.IsValid)
                    {
                        info = ZaloDetector.AutoDetect();
                    }

                    if (info != null && info.IsValid)
                    {
                        var profiles = ProfileService.LoadProfiles();
                        foreach (var p in profiles)
                        {
                            if (p.Id == profileId)
                            {
                                LauncherService.LaunchProfile(p, info.ExePath);
                                break;
                            }
                        }
                    }
                }
                catch { }
                return;
            }

            // 2. Normal GUI launch: Bring existing window to front if already open
            try
            {
                Process current = Process.GetCurrentProcess();
                Process[] procs = Process.GetProcessesByName(current.ProcessName);
                foreach (Process p in procs)
                {
                    if (p.Id != current.Id)
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                                SetForegroundWindow(p.MainWindowHandle);
                                return; // Window already exists and was brought to front
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 3. Launch application safely
            try
            {
                Program app = new Program();
                MainWindow win = new MainWindow();
                app.Run(win);
            }
            catch (Exception ex)
            {
                try
                {
                    string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_error.log");
                    File.WriteAllText(log, ex.ToString());
                }
                catch { }
                MessageBox.Show("Lỗi khởi động Zalo Space Manager:\n" + ex.Message, "Zalo Space Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
