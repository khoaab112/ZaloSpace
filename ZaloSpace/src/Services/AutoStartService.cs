using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ZaloSpaceManager.Services
{
    public static class AutoStartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string GetManagerExecutablePath()
        {
            try
            {
                string path = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch { }

            try
            {
                string baseExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZaloSpaceManager.exe");
                if (File.Exists(baseExe)) return baseExe;

                string parentExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ZaloSpaceManager.exe");
                string full = Path.GetFullPath(parentExe);
                if (File.Exists(full)) return full;
            }
            catch { }

            return @"c:\Users\khoaz\Downloads\tesst\ZaloSpaceManager.exe";
        }

        public static void SetProfileAutoStart(string profileId, string launcherCmd, bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    string keyName = "ZaloSpace_" + profileId;
                    if (enable)
                    {
                        if (!string.IsNullOrEmpty(launcherCmd) && !launcherCmd.StartsWith("\"\""))
                        {
                            key.SetValue(keyName, launcherCmd);
                        }
                    }
                    else
                    {
                        key.DeleteValue(keyName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AutoStart error: " + ex.Message);
            }
        }

        public static bool IsProfileAutoStart(string profileId)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    string keyName = "ZaloSpace_" + profileId;
                    object val = key.GetValue(keyName);
                    if (val == null) return false;
                    string s = val.ToString().Trim();
                    return !string.IsNullOrEmpty(s) && !s.StartsWith("\"\"") && !s.StartsWith("--launch");
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
