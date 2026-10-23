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

        private static Mutex _singleInstanceMutex = null;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length >= 2 && args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase))
            {
                // Silent launch for auto-start
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
                return;
            }

            // Normal launch: Enforce single instance to prevent duplicate background manager processes!
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "ZaloSpaceManager_SingleInstance_Mutex_Khoa", out createdNew);
            if (!createdNew)
            {
                // Bring existing manager window to front if running
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcesses())
                {
                    if (p.Id != current.Id && p.MainWindowTitle.IndexOf("Zalo Space Manager", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
                        SetForegroundWindow(p.MainWindowHandle);
                        return;
                    }
                }
                return;
            }

            Program app = new Program();
            MainWindow win = new MainWindow();
            win.ShowDialog();

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            }
        }
    }
}
