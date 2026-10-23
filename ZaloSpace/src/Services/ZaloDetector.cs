using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZaloSpaceManager.Services
{
    public class ZaloUserProfile
    {
        public bool Success { get; set; }
        public string UserId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public string DownloadedAvatarPath { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class ZaloInfo
    {
        public bool IsValid { get; set; }
        public string RootPath { get; set; }
        public string ExePath { get; set; }
        public string Version { get; set; }
        public string StatusMessage { get; set; }
        public string ExistingUserId { get; set; }

        public ZaloInfo()
        {
            IsValid = false;
            StatusMessage = "Chưa nhận diện được Zalo";
        }
    }

    public static class ZaloDetector
    {
        public static ZaloInfo AutoDetect()
        {
            // 1. Check running process
            try
            {
                Process[] procs = Process.GetProcessesByName("Zalo");
                if (procs != null && procs.Length > 0)
                {
                    foreach (Process p in procs)
                    {
                        try
                        {
                            string procPath = p.MainModule.FileName;
                            if (File.Exists(procPath))
                            {
                                ZaloInfo info = ValidatePath(procPath);
                                if (info.IsValid)
                                {
                                    info.StatusMessage = "Phát hiện từ tiến trình Zalo đang chạy";
                                    return info;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 2. Check standard LocalAppData
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string standardDir = Path.Combine(localApp, @"Programs\Zalo");
            if (Directory.Exists(standardDir))
            {
                ZaloInfo info = ValidatePath(standardDir);
                if (info.IsValid)
                {
                    info.StatusMessage = "Tìm thấy tại thư mục cài đặt tiêu chuẩn";
                    return info;
                }
            }

            // 3. Check Registry Uninstall
            string regPath = DetectFromRegistry();
            if (!string.IsNullOrEmpty(regPath))
            {
                ZaloInfo info = ValidatePath(regPath);
                if (info.IsValid)
                {
                    info.StatusMessage = "Tìm thấy qua Registry Windows";
                    return info;
                }
            }

            // 4. Check Program Files
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progDir = Path.Combine(progFiles, "Zalo");
            if (Directory.Exists(progDir))
            {
                ZaloInfo info = ValidatePath(progDir);
                if (info.IsValid) return info;
            }

            return new ZaloInfo { IsValid = false, StatusMessage = "Không tìm thấy Zalo trên máy tính" };
        }

        public static ZaloInfo ValidatePath(string inputPath)
        {
            ZaloInfo result = new ZaloInfo();
            if (string.IsNullOrEmpty(inputPath)) return result;

            try
            {
                string targetDir = inputPath;
                if (File.Exists(inputPath))
                {
                    targetDir = Path.GetDirectoryName(inputPath);
                }

                if (!Directory.Exists(targetDir)) return result;

                // Check if targetDir is a version folder like "Zalo-26.10.11"
                string dirName = Path.GetFileName(targetDir);
                string rootDir = targetDir;
                if (dirName != null && dirName.StartsWith("Zalo-"))
                {
                    rootDir = Directory.GetParent(targetDir).FullName;
                }

                // Find all subdirectories matching Zalo-*.*.*
                string[] versionDirs = Directory.GetDirectories(rootDir, "Zalo-*");
                string bestDir = null;
                Version maxVer = new Version(0, 0, 0);

                if (versionDirs != null && versionDirs.Length > 0)
                {
                    foreach (string vd in versionDirs)
                    {
                        string name = Path.GetFileName(vd);
                        string verStr = name.Replace("Zalo-", "");
                        try
                        {
                            Version v = new Version(verStr);
                            if (bestDir == null || v > maxVer)
                            {
                                maxVer = v;
                                bestDir = vd;
                            }
                        }
                        catch
                        {
                            if (bestDir == null) bestDir = vd;
                        }
                    }
                }

                // If no version folder, check if targetDir has Zalo.exe
                if (bestDir == null)
                {
                    if (File.Exists(Path.Combine(targetDir, "Zalo.exe")))
                    {
                        bestDir = targetDir;
                    }
                }

                if (bestDir != null && File.Exists(Path.Combine(bestDir, "Zalo.exe")))
                {
                    result.IsValid = true;
                    result.RootPath = rootDir;
                    result.ExePath = Path.Combine(bestDir, "Zalo.exe");
                    result.Version = maxVer.ToString();
                    if (result.Version == "0.0.0")
                    {
                        try
                        {
                            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(result.ExePath);
                            result.Version = fvi.ProductVersion;
                        }
                        catch { result.Version = "Đã xác thực"; }
                    }
                    result.StatusMessage = string.Format("Sẵn sàng (Phiên bản {0})", result.Version);
                    result.ExistingUserId = DetectExistingUser();
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.StatusMessage = "Lỗi kiểm tra đường dẫn: " + ex.Message;
            }

            return result;
        }

        public static string DetectExistingUser()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string zaloData = Path.Combine(appData, "ZaloData");
                if (!Directory.Exists(zaloData)) return null;

                // 1. Check for ftsm_<uid>.db
                string[] ftsmFiles = Directory.GetFiles(zaloData, "ftsm_*.db");
                if (ftsmFiles != null && ftsmFiles.Length > 0)
                {
                    string fn = Path.GetFileNameWithoutExtension(ftsmFiles[0]);
                    string uid = fn.Replace("ftsm_", "").Trim();
                    if (!string.IsNullOrEmpty(uid)) return uid;
                }

                // 2. Check Database\_production directory
                string prodDir = Path.Combine(zaloData, @"Database\_production");
                if (Directory.Exists(prodDir))
                {
                    string[] subDirs = Directory.GetDirectories(prodDir);
                    foreach (string d in subDirs)
                    {
                        string dirName = Path.GetFileName(d);
                        if (dirName != "Encrypt" && dirName != "Migrate" && dirName != "Shared")
                        {
                            return dirName;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string DetectFromRegistry()
        {
            string[] keys = new string[] {
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string k in keys)
            {
                try
                {
                    using (RegistryKey root = Registry.CurrentUser.OpenSubKey(k))
                    {
                        if (root != null)
                        {
                            string path = ScanSubKeys(root);
                            if (!string.IsNullOrEmpty(path)) return path;
                        }
                    }
                    using (RegistryKey root = Registry.LocalMachine.OpenSubKey(k))
                    {
                        if (root != null)
                        {
                            string path = ScanSubKeys(root);
                            if (!string.IsNullOrEmpty(path)) return path;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static string ScanSubKeys(RegistryKey root)
        {
            foreach (string subKeyName in root.GetSubKeyNames())
            {
                try
                {
                    using (RegistryKey sub = root.OpenSubKey(subKeyName))
                    {
                        if (sub != null)
                        {
                            object name = sub.GetValue("DisplayName");
                            if (name != null && name.ToString().IndexOf("Zalo", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                object loc = sub.GetValue("InstallLocation");
                                if (loc != null && !string.IsNullOrEmpty(loc.ToString()))
                                    return loc.ToString();

                                object icon = sub.GetValue("DisplayIcon");
                                if (icon != null && !string.IsNullOrEmpty(icon.ToString()))
                                {
                                    string iconPath = icon.ToString().Replace("\"", "").Trim();
                                    if (File.Exists(iconPath)) return Path.GetDirectoryName(iconPath);
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        public static ZaloUserProfile ExtractAndDownloadUserAvatar(string primaryDataDir, string profileId, string outputDir)
        {
            ZaloUserProfile result = new ZaloUserProfile { Success = false };

            try
            {
                // 1. Candidate data directories
                List<string> candidateDirs = new List<string>();
                if (!string.IsNullOrEmpty(primaryDataDir) && Directory.Exists(primaryDataDir))
                    candidateDirs.Add(primaryDataDir);

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (profileId == "1")
                {
                    string zData = Path.Combine(appData, "ZaloData");
                    if (Directory.Exists(zData) && !candidateDirs.Contains(zData))
                        candidateDirs.Add(zData);
                }
                else
                {
                    string pDir = Path.Combine(appData, "ZaloSpace", "Data", "Profile_" + profileId);
                    if (Directory.Exists(pDir) && !candidateDirs.Contains(pDir)) candidateDirs.Add(pDir);

                    string zaldat = Path.Combine(appData, "ZalDat" + profileId.PadRight(2, '0').Substring(0, 2));
                    if (Directory.Exists(zaldat) && !candidateDirs.Contains(zaldat)) candidateDirs.Add(zaldat);
                }

                string foundStorageDb = null;
                string foundUid = null;

                foreach (string d in candidateDirs)
                {
                    // Find UID from ftsm_*.db
                    string[] ftsm = Directory.GetFiles(d, "ftsm_*.db");
                    if (ftsm != null && ftsm.Length > 0)
                    {
                        string fn = Path.GetFileNameWithoutExtension(ftsm[0]);
                        foundUid = fn.Replace("ftsm_", "").Trim();
                    }

                    // Find UID from Database\_production folder
                    string prodDir = Path.Combine(d, @"Database\_production");
                    if (string.IsNullOrEmpty(foundUid) && Directory.Exists(prodDir))
                    {
                        string[] sub = Directory.GetDirectories(prodDir);
                        foreach (string sd in sub)
                        {
                            string dn = Path.GetFileName(sd);
                            if (dn != "Encrypt" && dn != "Migrate" && dn != "Shared")
                            {
                                foundUid = dn;
                                break;
                            }
                        }
                    }

                    string sDb = Path.Combine(d, @"Database\_production\Storage.db");
                    if (File.Exists(sDb))
                    {
                        foundStorageDb = sDb;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(foundStorageDb) || !File.Exists(foundStorageDb))
                {
                    result.ErrorMessage = "Chưa tìm thấy cơ sở dữ liệu của tài khoản này trên máy.\nHãy mở Zalo và đăng nhập trước khi lấy avatar.";
                    return result;
                }

                // Read Storage.db with FileShare.ReadWrite so it works even when Zalo is running!
                string content = null;
                using (FileStream fs = new FileStream(foundStorageDb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                {
                    content = sr.ReadToEnd();
                }

                string bestUrl = null;
                string bestName = null;

                if (!string.IsNullOrEmpty(foundUid))
                {
                    string searchStr = "0_" + foundUid;
                    int idx = 0;
                    while ((idx = content.IndexOf(searchStr, idx)) >= 0)
                    {
                        string chunk = content.Substring(idx, Math.Min(500, content.Length - idx));
                        idx += searchStr.Length;

                        int avaIdx = chunk.IndexOf("\"avatar\":\"");
                        if (avaIdx >= 0)
                        {
                            int urlStart = avaIdx + 10;
                            int urlEnd = chunk.IndexOf('"', urlStart);
                            if (urlEnd > urlStart)
                            {
                                string url = chunk.Substring(urlStart, urlEnd - urlStart);
                                if (url.StartsWith("http") && url.IndexOf(".jpg") > 0)
                                {
                                    bestUrl = url;
                                }
                            }
                        }

                        int nameIdx = chunk.IndexOf("\"zName\":\"");
                        if (nameIdx >= 0)
                        {
                            int nStart = nameIdx + 9;
                            int nEnd = chunk.IndexOf('"', nStart);
                            if (nEnd > nStart)
                            {
                                string name = chunk.Substring(nStart, nEnd - nStart);
                                if (!string.IsNullOrEmpty(name))
                                {
                                    bestName = name;
                                }
                            }
                        }
                    }
                }

                // Fallback: search for any avatar URL in Storage.db if UID match didn't yield an avatar
                if (string.IsNullOrEmpty(bestUrl))
                {
                    Match m = Regex.Match(content, @"""avatar"":\s*""(https?://s\d+-[^""]+)""");
                    if (m.Success)
                    {
                        bestUrl = m.Groups[1].Value;
                    }
                }

                if (string.IsNullOrEmpty(bestUrl))
                {
                    result.ErrorMessage = "Chưa tìm thấy ảnh đại diện của user trong dữ liệu Zalo.\nCó thể tài khoản chưa cài avatar hoặc chưa đồng bộ xong.";
                    return result;
                }

                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                byte[] avatarData = null;
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; // Tls12
                }
                catch { }

                using (System.Net.WebClient wc = new System.Net.WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36");
                    avatarData = wc.DownloadData(bestUrl);
                }

                if (avatarData == null || avatarData.Length == 0)
                {
                    result.ErrorMessage = "Không nhận được dữ liệu ảnh avatar từ máy chủ Zalo.";
                    return result;
                }

                string outFile = Path.Combine(outputDir, string.Format("zalo_avatar_{0}_{1}.jpg", profileId, DateTime.UtcNow.Ticks));
                File.WriteAllBytes(outFile, avatarData);

                // Clean up previous avatar files for this profile
                try
                {
                    string oldPrefix = string.Format("zalo_avatar_{0}", profileId);
                    foreach (string oldF in Directory.GetFiles(outputDir, oldPrefix + "*"))
                    {
                        if (!oldF.Equals(outFile, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(oldF); } catch { }
                        }
                    }
                }
                catch { }

                if (File.Exists(outFile))
                {
                    result.Success = true;
                    result.UserId = foundUid;
                    result.DisplayName = bestName;
                    result.AvatarUrl = bestUrl;
                    result.DownloadedAvatarPath = outFile;
                    return result;
                }
                else
                {
                    result.ErrorMessage = "Không thể ghi file ảnh đại diện vào thư mục.";
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Lỗi trích xuất avatar: " + ex.Message;
            }

            return result;
        }

        public static string ExtractUserName(string primaryDataDir, string profileId)
        {
            try
            {
                List<string> candidateDirs = new List<string>();
                if (!string.IsNullOrEmpty(primaryDataDir) && Directory.Exists(primaryDataDir))
                    candidateDirs.Add(primaryDataDir);

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (profileId == "1")
                {
                    string zData = Path.Combine(appData, "ZaloData");
                    if (Directory.Exists(zData) && !candidateDirs.Contains(zData))
                        candidateDirs.Add(zData);
                }
                else
                {
                    string pDir = Path.Combine(appData, "ZaloSpace", "Data", "Profile_" + profileId);
                    if (Directory.Exists(pDir) && !candidateDirs.Contains(pDir)) candidateDirs.Add(pDir);

                    string zaldat = Path.Combine(appData, "ZalDat" + profileId.PadRight(2, '0').Substring(0, 2));
                    if (Directory.Exists(zaldat) && !candidateDirs.Contains(zaldat)) candidateDirs.Add(zaldat);
                }

                string foundStorageDb = null;
                string foundUid = null;

                foreach (string d in candidateDirs)
                {
                    // Find UID from ftsm_*.db
                    string[] ftsm = Directory.GetFiles(d, "ftsm_*.db");
                    if (ftsm != null && ftsm.Length > 0)
                    {
                        string fn = Path.GetFileNameWithoutExtension(ftsm[0]);
                        foundUid = fn.Replace("ftsm_", "").Trim();
                    }

                    // Find UID from Database\_production folder
                    string prodDir = Path.Combine(d, @"Database\_production");
                    if (string.IsNullOrEmpty(foundUid) && Directory.Exists(prodDir))
                    {
                        string[] sub = Directory.GetDirectories(prodDir);
                        foreach (string sd in sub)
                        {
                            string dn = Path.GetFileName(sd);
                            if (dn != "Encrypt" && dn != "Migrate" && dn != "Shared")
                            {
                                foundUid = dn;
                                break;
                            }
                        }
                    }

                    string sDb = Path.Combine(d, @"Database\_production\Storage.db");
                    if (File.Exists(sDb))
                    {
                        foundStorageDb = sDb;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(foundStorageDb) && File.Exists(foundStorageDb))
                {
                    string content = null;
                    using (FileStream fs = new FileStream(foundStorageDb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        content = sr.ReadToEnd();
                    }

                    // 1. Target OWNER account profile key: "0_" + foundUid
                    if (!string.IsNullOrEmpty(foundUid))
                    {
                        string searchStr = "0_" + foundUid;
                        int idx = content.IndexOf(searchStr, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            string chunk = content.Substring(idx, Math.Min(600, content.Length - idx));
                            int nIdx = chunk.IndexOf("\"zName\":\"", StringComparison.OrdinalIgnoreCase);
                            if (nIdx >= 0)
                            {
                                int nStart = nIdx + 9;
                                int nEnd = chunk.IndexOf('\"', nStart);
                                if (nEnd > nStart)
                                {
                                    string ownerName = chunk.Substring(nStart, nEnd - nStart).Trim();
                                    if (!string.IsNullOrEmpty(ownerName))
                                    {
                                        return ownerName;
                                    }
                                }
                            }
                        }
                    }

                    // 2. If UID match didn't yield zName, check for profile_ key
                    Match mProfileName = Regex.Match(content, @"profile_[^\{]*\{[^\}]*""zName"":""([^""]+)""");
                    if (mProfileName.Success && !string.IsNullOrEmpty(mProfileName.Groups[1].Value))
                    {
                        return mProfileName.Groups[1].Value;
                    }
                }

                // Fallback to found UID if available
                if (!string.IsNullOrEmpty(foundUid))
                {
                    return foundUid;
                }
            }
            catch { }
            return null;
        }
    }
}
