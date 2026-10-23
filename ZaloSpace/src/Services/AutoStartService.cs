using System;
using Microsoft.Win32;

namespace ZaloSpaceManager.Services
{
    public static class AutoStartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

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
                        key.SetValue(keyName, launcherCmd);
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
                    return val != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
