using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ZaloSpaceManager.Models;

namespace ZaloSpaceManager.Services
{
    public static class LauncherService
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
        private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        private static Dictionary<string, int> _runningPids = new Dictionary<string, int>();

        public static HashSet<string> GetRunningProfileIds(List<Profile> profiles)
        {
            HashSet<string> running = new HashSet<string>();
            if (profiles == null || profiles.Count == 0) return running;

            try
            {
                Process[] procs = Process.GetProcessesByName("Zalo");
                if (procs == null || procs.Length == 0) return running;

                foreach (Process p in procs)
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (string.IsNullOrEmpty(path)) continue;

                        if (path.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            running.Add("1");
                        }
                        else
                        {
                            foreach (Profile prof in profiles)
                            {
                                if (prof.Id != "1" && path.IndexOf("Profile_" + prof.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    running.Add(prof.Id);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return running;
        }

        public static bool IsProfileRunning(Profile profile, string defaultZaloExe)
        {
            if (profile == null) return false;

            // If we tracked PID, check if still alive
            if (_runningPids.ContainsKey(profile.Id))
            {
                int pid = _runningPids[profile.Id];
                try
                {
                    Process p = Process.GetProcessById(pid);
                    if (!p.HasExited) return true;
                }
                catch { _runningPids.Remove(profile.Id); }
            }

            // Check running processes
            try
            {
                Process[] procs = Process.GetProcessesByName("Zalo");
                if (procs == null || procs.Length == 0) return false;

                if (profile.Id == "1")
                {
                    // Profile 1 is running if original Zalo is running
                    foreach (Process p in procs)
                    {
                        try
                        {
                            string path = p.MainModule.FileName;
                            if (path != null && path.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                _runningPids[profile.Id] = p.Id;
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    string profileMarker = "Profile_" + profile.Id;
                    foreach (Process p in procs)
                    {
                        try
                        {
                            string path = p.MainModule.FileName;
                            if (path != null && path.IndexOf(profileMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                _runningPids[profile.Id] = p.Id;
                                return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool LaunchProfile(Profile profile, string zaloExePath)
        {
            if (profile == null || string.IsNullOrEmpty(zaloExePath) || !File.Exists(zaloExePath))
                return false;

            try
            {
                if (profile.Id == "1")
                {
                    // Update/Create Desktop Shortcut for Profile 1 with user avatar
                    CreateDesktopShortcut(profile, zaloExePath);

                    // Standard Zalo launch
                    Process proc = Process.Start(zaloExePath);
                    if (proc != null)
                    {
                        _runningPids[profile.Id] = proc.Id;
                    }
                    return true;
                }

                // Profile 2+: Setup isolated instance
                string baseDir = ProfileService.GetBaseDirectory();
                string instDir = Path.Combine(baseDir, "Instances", "Profile_" + profile.Id);
                string dataDir = profile.DataDir;

                if (!Directory.Exists(dataDir))
                    Directory.CreateDirectory(dataDir);

                // Prepare instance directory
                PrepareInstance(instDir, Path.GetDirectoryName(zaloExePath), profile.Id);

                string instExe = Path.Combine(instDir, "Zalo.exe");
                if (!File.Exists(instExe)) return false;

                // Create Desktop Shortcut with custom Avatar Icon!
                CreateDesktopShortcut(profile, instExe);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = instExe;
                psi.WorkingDirectory = instDir;
                psi.UseShellExecute = true;

                Process proc2 = Process.Start(psi);
                if (proc2 != null)
                {
                    _runningPids[profile.Id] = proc2.Id;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("LaunchProfile error: " + ex.Message);
            }

            return false;
        }

        public static List<Process> GetProfileProcesses(Profile profile)
        {
            List<Process> list = new List<Process>();
            if (profile == null) return list;

            try
            {
                Process[] procs = Process.GetProcessesByName("Zalo");
                if (procs == null || procs.Length == 0) return list;

                foreach (Process p in procs)
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (string.IsNullOrEmpty(path)) continue;

                        if (profile.Id == "1")
                        {
                            if (path.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                list.Add(p);
                            }
                        }
                        else
                        {
                            string marker = "Profile_" + profile.Id;
                            if (path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                list.Add(p);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return list;
        }

        public static void StopProfile(Profile profile, bool forceKill = false)
        {
            if (profile == null) return;
            try
            {
                var procs = GetProfileProcesses(profile);
                if (procs.Count == 0) return;

                foreach (Process p in procs)
                {
                    try { p.CloseMainWindow(); } catch { }
                }

                if (forceKill)
                {
                    System.Threading.Thread.Sleep(300);
                    foreach (Process p in procs)
                    {
                        try
                        {
                            if (!p.HasExited) p.Kill();
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public static void StopAllProfiles(List<Profile> profiles)
        {
            if (profiles == null) return;
            foreach (var p in profiles)
            {
                StopProfile(p, false);
            }
            System.Threading.Thread.Sleep(400);
            foreach (var p in profiles)
            {
                StopProfile(p, true);
            }
        }

        public static void SafeDeleteDirectory(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath)) return;
            try
            {
                foreach (string sub in Directory.GetDirectories(dirPath))
                {
                    try
                    {
                        var attr = File.GetAttributes(sub);
                        if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                        {
                            // It's an NTFS junction/reparse point!
                            // Only delete the junction link itself, NEVER recurse into it!
                            Directory.Delete(sub, false);
                        }
                        else
                        {
                            SafeDeleteDirectory(sub);
                        }
                    }
                    catch { }
                }

                foreach (string file in Directory.GetFiles(dirPath))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { }
                }

                Directory.Delete(dirPath, false);
            }
            catch { }
        }

        public static void CleanDeleteProfile(Profile profile)
        {
            if (profile == null || profile.Id == "1") return; // Never delete root profile

            try
            {
                // 1. Force kill all processes for this clone so no files are locked
                StopProfile(profile, true);
                System.Threading.Thread.Sleep(500);

                // 2. Remove AutoStart registry entry
                AutoStartService.SetProfileAutoStart(profile.Id, null, false);

                // 3. Delete Desktop Shortcuts
                List<string> desktopDirs = new List<string>();
                string d1 = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1)) desktopDirs.Add(d1);
                string d2 = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !desktopDirs.Contains(d2)) desktopDirs.Add(d2);

                foreach (string d in desktopDirs)
                {
                    try
                    {
                        string lnkExact = Path.Combine(d, profile.Name + ".lnk");
                        if (File.Exists(lnkExact)) { try { File.Delete(lnkExact); } catch { } }

                        // Scan for any shortcut containing Profile_{id} or starting with zalo_{id}
                        foreach (string lnk in Directory.GetFiles(d, "*.lnk"))
                        {
                            string fn = Path.GetFileName(lnk);
                            if (fn.StartsWith("zalo_" + profile.Id + "_", StringComparison.OrdinalIgnoreCase) ||
                                fn.Equals(profile.Name + ".lnk", StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(lnk); } catch { }
                            }
                        }
                    }
                    catch { }
                }

                // 4. Delete Instance directory (safely without touching real Zalo files!)
                string baseDir = ProfileService.GetBaseDirectory();
                string instDir = Path.Combine(baseDir, "Instances", "Profile_" + profile.Id);
                SafeDeleteDirectory(instDir);

                // 5. Delete Data directory
                if (!string.IsNullOrEmpty(profile.DataDir) && Directory.Exists(profile.DataDir))
                {
                    SafeDeleteDirectory(profile.DataDir);
                }

                // 6. Delete ZalDat folder in AppData if exists
                string zalDat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZalDat" + profile.Id.PadRight(2, '0').Substring(0, 2));
                if (Directory.Exists(zalDat))
                {
                    SafeDeleteDirectory(zalDat);
                }

                // 7. Delete Icon and Avatar files
                if (!string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath))
                {
                    try { File.Delete(profile.IconPath); } catch { }
                }

                string iconDir = ProfileService.GetIconsDirectory();
                if (Directory.Exists(iconDir))
                {
                    foreach (string f in Directory.GetFiles(iconDir, "*" + profile.Id + "*"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }

                // 8. Invalidate taskbar caches
                TaskbarService.ClearAllIconCaches();
                TaskbarService.RefreshDesktopShell();
            }
            catch (Exception ex)
            {
                Console.WriteLine("CleanDeleteProfile error: " + ex.Message);
            }
        }

        public static void PrepareInstance(string instDir, string srcDir, string profileId)
        {
            if (!Directory.Exists(instDir))
                Directory.CreateDirectory(instDir);

            // Copy Zalo.exe so it is a distinct, independent binary
            // Hardlink non-executable dependencies to save 100% disk space
            string[] topFiles = Directory.GetFiles(srcDir);
            foreach (string f in topFiles)
            {
                string fName = Path.GetFileName(f);
                string destFile = Path.Combine(instDir, fName);
                if (fName.Equals("Zalo.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(destFile) || File.GetLastWriteTime(f) > File.GetLastWriteTime(destFile))
                    {
                        try { File.Copy(f, destFile, true); } catch { }
                    }
                }
                else
                {
                    if (!File.Exists(destFile))
                    {
                        CreateHardLink(destFile, f);
                    }
                }
            }

            // Create junctions for directories except resources
            string[] dirs = Directory.GetDirectories(srcDir);
            foreach (string d in dirs)
            {
                string dirName = Path.GetFileName(d);
                if (dirName.Equals("resources", StringComparison.OrdinalIgnoreCase)) continue;

                string destSub = Path.Combine(instDir, dirName);
                if (!Directory.Exists(destSub))
                {
                    CreateJunction(destSub, d);
                }
            }

            // Resources folder
            string srcRes = Path.Combine(srcDir, "resources");
            string dstRes = Path.Combine(instDir, "resources");
            if (!Directory.Exists(dstRes))
                Directory.CreateDirectory(dstRes);

            // Hardlink non-asar files in resources
            if (Directory.Exists(srcRes))
            {
                foreach (string rf in Directory.GetFiles(srcRes))
                {
                    string fName = Path.GetFileName(rf);
                    if (fName.Equals("app.asar", StringComparison.OrdinalIgnoreCase)) continue;
                    string destRf = Path.Combine(dstRes, fName);
                    if (!File.Exists(destRf)) CreateHardLink(destRf, rf);
                }
                foreach (string rd in Directory.GetDirectories(srcRes))
                {
                    string destRd = Path.Combine(dstRes, Path.GetFileName(rd));
                    if (!Directory.Exists(destRd)) CreateJunction(destRd, rd);
                }
            }

            // Prepare patched app.asar for this profile
            string srcAsar = Path.Combine(srcRes, "app.asar");
            string dstAsar = Path.Combine(dstRes, "app.asar");

            if (File.Exists(srcAsar))
            {
                bool needPatch = !File.Exists(dstAsar) || File.GetLastWriteTime(srcAsar) > File.GetLastWriteTime(dstAsar);
                if (!needPatch && profileId != "1")
                {
                    try
                    {
                        // Check if dstAsar still contains unpatched com.vng.zalo, buggy loop, or unisolated call pipes
                        byte[] b = File.ReadAllBytes(dstAsar);
                        string s = Encoding.ASCII.GetString(b);
                        if (s.Contains("com.vng.zalo") || s.Contains("if (true)") || s.Contains("0===e?X():") || s.Contains("PipeZCallSend"))
                        {
                            needPatch = true;
                        }
                    }
                    catch { }
                }

                if (needPatch)
                {
                    PatchAsarForProfile(srcAsar, dstAsar, profileId);
                }
            }
        }

        public static string GetProfileAppId(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || profileId == "1") return "com.vng.zalo";
            string idStr = profileId.Trim();
            if (idStr.Length == 1) return "zalospace.p" + idStr; // 12 chars: zalospace.p2
            if (idStr.Length == 2) return "zalospace." + idStr;  // 12 chars: zalospace.10
            return ("zalo_" + idStr).PadRight(12, '_').Substring(0, 12);
        }

        private static void PatchAsarForProfile(string srcAsar, string dstAsar, string profileId)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(srcAsar);

                // 1. Target profile folder name (must be exact 8 chars to preserve asar offsets)
                string folderId = string.Format("ZalDat{0}", profileId.PadRight(2, '0').Substring(0, 2)); // e.g. ZalDat02 (8 chars)
                byte[] oldDataBytes = Encoding.ASCII.GetBytes("ZaloData");
                byte[] newDataBytes = Encoding.ASCII.GetBytes(folderId);

                // Replace all 8-byte ZaloData occurrences
                for (int i = 0; i <= bytes.Length - 8; i++)
                {
                    if (bytes[i] == 90 && bytes[i + 1] == 97 && bytes[i + 2] == 108 && bytes[i + 3] == 111 &&
                        bytes[i + 4] == 68 && bytes[i + 5] == 97 && bytes[i + 6] == 116 && bytes[i + 7] == 97)
                    {
                        for (int j = 0; j < 8; j++) bytes[i + j] = newDataBytes[j];
                        i += 7;
                    }
                }

                // 2. Patch com.vng.zalo so Electron sets unique AppUserModelId for clone
                if (profileId != "1")
                {
                    string newAppId = GetProfileAppId(profileId);
                    byte[] oldAppIdBytes = Encoding.ASCII.GetBytes("com.vng.zalo");
                    byte[] newAppIdBytes = Encoding.ASCII.GetBytes(newAppId);

                    for (int i = 0; i <= bytes.Length - 12; i++)
                    {
                        if (bytes[i] == oldAppIdBytes[0] && bytes[i + 1] == oldAppIdBytes[1] &&
                            bytes[i + 2] == oldAppIdBytes[2] && bytes[i + 3] == oldAppIdBytes[3] &&
                            bytes[i + 4] == oldAppIdBytes[4] && bytes[i + 5] == oldAppIdBytes[5] &&
                            bytes[i + 6] == oldAppIdBytes[6] && bytes[i + 7] == oldAppIdBytes[7] &&
                            bytes[i + 8] == oldAppIdBytes[8] && bytes[i + 9] == oldAppIdBytes[9] &&
                            bytes[i + 10] == oldAppIdBytes[10] && bytes[i + 11] == oldAppIdBytes[11])
                        {
                            for (int j = 0; j < 12; j++) bytes[i + j] = newAppIdBytes[j];
                            i += 11;
                        }
                    }

                    // 3. Patch ZaloCap infinite restart loop on exit code 0
                    // ZaloCap is a Qt single-instance app. Secondary instances exit with code 0.
                    // VNG code had "0===e?X():" which caused an infinite restart loop spawning processes and spinning the mouse cursor.
                    byte[] oldCapLoop = Encoding.ASCII.GetBytes("0===e?X():");
                    byte[] newCapLoop = Encoding.ASCII.GetBytes("0===e?0  :");
                    for (int i = 0; i <= bytes.Length - 10; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < 10; j++)
                        {
                            if (bytes[i + j] != oldCapLoop[j]) { match = false; break; }
                        }
                        if (match)
                        {
                            for (int j = 0; j < 10; j++) bytes[i + j] = newCapLoop[j];
                            i += 9;
                        }
                    }

                    // 4. Patch ZaloCall IPC named pipes so each clone has independent audio/video call communication
                    // Zalo default is "\\\\.\\pipe\\PipeZCallSend" (13 chars) and "\\\\.\\pipe\\PipeZCallRecv" (13 chars)
                    // If multiple instances share the same pipes, secondary instances fail with EADDRINUSE,
                    // causing incoming calls to never pop up on screen!
                    string pSuffix = profileId.PadLeft(2, '0').Substring(0, 2); // e.g. "02", "03"
                    string newPipeSend = "PipeCall_S_" + pSuffix; // exactly 13 chars
                    string newPipeRecv = "PipeCall_R_" + pSuffix; // exactly 13 chars

                    byte[] oldSendBytes = Encoding.ASCII.GetBytes("PipeZCallSend");
                    byte[] newSendBytes = Encoding.ASCII.GetBytes(newPipeSend);
                    for (int i = 0; i <= bytes.Length - 13; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < 13; j++)
                        {
                            if (bytes[i + j] != oldSendBytes[j]) { match = false; break; }
                        }
                        if (match)
                        {
                            for (int j = 0; j < 13; j++) bytes[i + j] = newSendBytes[j];
                            i += 12;
                        }
                    }

                    byte[] oldRecvBytes = Encoding.ASCII.GetBytes("PipeZCallRecv");
                    byte[] newRecvBytes = Encoding.ASCII.GetBytes(newPipeRecv);
                    for (int i = 0; i <= bytes.Length - 13; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < 13; j++)
                        {
                            if (bytes[i + j] != oldRecvBytes[j]) { match = false; break; }
                        }
                        if (match)
                        {
                            for (int j = 0; j < 13; j++) bytes[i + j] = newRecvBytes[j];
                            i += 12;
                        }
                    }
                }

                File.WriteAllBytes(dstAsar, bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("PatchAsar error: " + ex.Message);
            }
        }

        private static void CreateHardLink(string dest, string src)
        {
            try
            {
                if (File.Exists(dest)) File.Delete(dest);
                if (!CreateHardLinkNative(dest, src, IntPtr.Zero))
                {
                    File.Copy(src, dest, true);
                }
            }
            catch
            {
                try { File.Copy(src, dest, true); } catch { }
            }
        }

        private static void CreateJunction(string dest, string src)
        {
            if (Directory.Exists(dest)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", string.Format("/c mklink /J \"{0}\" \"{1}\"", dest, src));
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process p = Process.Start(psi);
                if (p != null) p.WaitForExit(1000);
            }
            catch { }
        }

        public static void CreateDesktopShortcut(Profile profile, string exePath)
        {
            try
            {
                if (profile == null || string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    return;

                List<string> desktopDirs = new List<string>();
                string d1 = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1) && !desktopDirs.Contains(d1))
                    desktopDirs.Add(d1);

                string d2 = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !desktopDirs.Contains(d2))
                    desktopDirs.Add(d2);

                string d3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
                if (Directory.Exists(d3) && !desktopDirs.Contains(d3))
                    desktopDirs.Add(d3);

                // Add Start Menu Programs folder so Windows Taskbar associates AppUserModelId with this shortcut!
                string progDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (!string.IsNullOrEmpty(progDir) && Directory.Exists(progDir))
                {
                    string appProgDir = Path.Combine(progDir, "ZaloSpace");
                    if (!Directory.Exists(appProgDir)) Directory.CreateDirectory(appProgDir);
                    if (!desktopDirs.Contains(appProgDir)) desktopDirs.Add(appProgDir);
                    if (!desktopDirs.Contains(progDir)) desktopDirs.Add(progDir);
                }

                string appModelId = GetProfileAppId(profile.Id);

                // Register AppUserModelId in Registry
                try
                {
                    using (var rk = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(string.Format(@"Software\Classes\AppUserModelId\{0}", appModelId)))
                    {
                        if (rk != null)
                        {
                            rk.SetValue("DisplayName", profile.Name ?? "Zalo");
                            if (!string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath))
                            {
                                rk.SetValue("IconUri", profile.IconPath);
                            }
                        }
                    }
                }
                catch { }

                foreach (string d in desktopDirs)
                {
                    try
                    {
                        string shortcutPath = Path.Combine(d, profile.Name + ".lnk");

                        // 1. Modern ShellLink with AppUserModelId for 100% Windows Taskbar Icon pairing
                        bool createdWithShellLink = false;
                        try
                        {
                            IShellLinkW shellLink = (IShellLinkW)new ShellLinkClass();
                            shellLink.SetPath(exePath);
                            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath));
                            if (!string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath))
                            {
                                shellLink.SetIconLocation(profile.IconPath, 0);
                            }

                            IPropertyStore store = (IPropertyStore)shellLink;
                            PropertyKey key = TaskbarService.PKey_AppUserModel_ID;
                            using (PropVariant pv = new PropVariant(appModelId))
                            {
                                store.SetValue(ref key, pv);
                                store.Commit();
                            }

                            IPersistFile persistFile = (IPersistFile)shellLink;
                            persistFile.Save(shortcutPath, true);
                            createdWithShellLink = true;
                        }
                        catch { }

                        // 2. Fallback to WScript.Shell
                        if (!createdWithShellLink)
                        {
                            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                            if (shellType != null)
                            {
                                dynamic shell = Activator.CreateInstance(shellType);
                                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                                shortcut.TargetPath = exePath;
                                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                                if (!string.IsNullOrEmpty(profile.IconPath) && File.Exists(profile.IconPath))
                                {
                                    shortcut.IconLocation = profile.IconPath + ",0";
                                }
                                shortcut.Save();
                            }
                        }
                    }
                    catch { }
                }

                // Notify shell to immediately refresh desktop icons
                TaskbarService.RefreshDesktopShell();
            }
            catch (Exception ex)
            {
                Console.WriteLine("CreateDesktopShortcut error: " + ex.Message);
            }
        }
    }
}
