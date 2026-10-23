using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ZaloSpaceManager.Services
{
    public class StorageInfo
    {
        public long AppBytes { get; set; }
        public long DataBytes { get; set; }
        public long TotalBytes { get { return AppBytes + DataBytes; } }

        public string AppFormatted { get { return FormatBytes(AppBytes); } }
        public string DataFormatted { get { return FormatBytes(DataBytes); } }
        public string TotalFormatted { get { return FormatBytes(TotalBytes); } }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824) return string.Format("{0:0.##} GB", bytes / 1073741824.0);
            if (bytes >= 1048576) return string.Format("{0:0.#} MB", bytes / 1048576.0);
            if (bytes >= 1024) return string.Format("{0:0.#} KB", bytes / 1024.0);
            return bytes + " B";
        }
    }

    public static class StorageService
    {
        public static Task<StorageInfo> CalculateStorageAsync(string zaloExePath)
        {
            return Task.Factory.StartNew(() =>
            {
                StorageInfo info = new StorageInfo();

                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    // 1. Zalo Core App Directory
                    string zaloAppDir = null;
                    if (!string.IsNullOrEmpty(zaloExePath) && File.Exists(zaloExePath))
                    {
                        zaloAppDir = Path.GetDirectoryName(zaloExePath);
                        // If under a version folder (e.g. Zalo-26.10.11), take the parent Programs\Zalo folder
                        string parent = Path.GetDirectoryName(zaloAppDir);
                        if (parent != null && Path.GetFileName(parent).Equals("Zalo", StringComparison.OrdinalIgnoreCase))
                        {
                            zaloAppDir = parent;
                        }
                    }
                    else
                    {
                        string defaultApp = Path.Combine(localAppData, @"Programs\Zalo");
                        if (Directory.Exists(defaultApp)) zaloAppDir = defaultApp;
                    }

                    if (!string.IsNullOrEmpty(zaloAppDir) && Directory.Exists(zaloAppDir))
                    {
                        info.AppBytes += GetDirectorySizeSafe(zaloAppDir);
                    }

                    // 2. Clone Instances binaries (only actual files, junctions not counted twice)
                    string baseDir = ProfileService.GetBaseDirectory();
                    string instRoot = Path.Combine(baseDir, "Instances");
                    if (Directory.Exists(instRoot))
                    {
                        info.AppBytes += GetDirectorySizeSafe(instRoot);
                    }

                    // 3. Main Zalo Data Directory (Chat, Media, DBs, Cache)
                    string mainData = Path.Combine(appData, "ZaloData");
                    if (Directory.Exists(mainData))
                    {
                        info.DataBytes += GetDirectorySizeSafe(mainData);
                    }

                    // 4. Clone Data Directory
                    string cloneDataRoot = Path.Combine(baseDir, "Data");
                    if (Directory.Exists(cloneDataRoot))
                    {
                        info.DataBytes += GetDirectorySizeSafe(cloneDataRoot);
                    }

                    // 5. Any ZalDat* directories in AppData
                    if (Directory.Exists(appData))
                    {
                        foreach (string sub in Directory.GetDirectories(appData, "ZalDat*"))
                        {
                            info.DataBytes += GetDirectorySizeSafe(sub);
                        }
                    }
                }
                catch { }

                return info;
            });
        }

        public static long GetDirectorySizeSafe(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath)) return 0;
            long total = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(dirPath);
                // Never traverse into reparse points / junctions
                if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    return 0;

                foreach (FileInfo fi in di.GetFiles("*", SearchOption.TopDirectoryOnly))
                {
                    try { total += fi.Length; } catch { }
                }
                foreach (DirectoryInfo sub in di.GetDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint)
                        {
                            total += GetDirectorySizeSafe(sub.FullName);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return total;
        }
    }
}
