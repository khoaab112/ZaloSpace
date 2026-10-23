using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ZaloSpaceManager.Models;

namespace ZaloSpaceManager.Services
{
    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        uint GetCount();
        PropertyKey GetAt(uint iProp);
        void GetValue([In] ref PropertyKey key, [Out] PropVariant pv);
        void SetValue([In] ref PropertyKey key, [In] PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid guid, uint id) { fmtid = guid; pid = id; }
    }

    [StructLayout(LayoutKind.Explicit)]
    public class PropVariant : IDisposable
    {
        [FieldOffset(0)] private ushort vt;
        [FieldOffset(8)] private IntPtr pointerValue;

        public PropVariant(string val)
        {
            vt = 31; // VT_LPWSTR
            pointerValue = Marshal.StringToCoTaskMemUni(val);
        }

        public void Dispose()
        {
            if (pointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointerValue);
                pointerValue = IntPtr.Zero;
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLinkClass { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }

    public static class TaskbarService
    {
        private const uint WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
        private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetClassLong")]
        private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return SetClassLong32(hWnd, nIndex, dwNewLong);
        }

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore propertyStore);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hWnd);
            void DeleteTab(IntPtr hWnd);
            void ActivateTab(IntPtr hWnd);
            void SetActiveAlt(IntPtr hWnd);
            void MarkFullscreenWindow(IntPtr hWnd, bool fFullscreen);
            void SetProgressValue(IntPtr hWnd, ulong ullCompleted, ulong ullTotal);
            void SetProgressState(IntPtr hWnd, int tbpFlags);
            void RegisterTab(IntPtr hWndTab, IntPtr hWndMDI);
            void UnregisterTab(IntPtr hWndTab);
            void SetTabOrder(IntPtr hWndTab, IntPtr hWndInsertBefore);
            void SetTabActive(IntPtr hWndTab, IntPtr hWndInsertBefore, uint dwReserved);
            void ThumbBarAddButtons(IntPtr hWnd, uint cButtons, IntPtr pButton);
            void ThumbBarUpdateButtons(IntPtr hWnd, uint cButtons, IntPtr pButton);
            void ThumbBarSetImageList(IntPtr hWnd, IntPtr himl);
            void SetOverlayIcon(IntPtr hWnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
            void SetThumbnailTooltip(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
            void SetThumbnailClip(IntPtr hWnd, IntPtr prcClip);
        }

        [ComImport]
        [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class TaskbarInstance
        {
        }

        public static readonly PropertyKey PKey_AppUserModel_ID = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        public static readonly PropertyKey PKey_AppUserModel_RelaunchCommand = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 2);
        public static readonly PropertyKey PKey_AppUserModel_RelaunchIconResource = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 3);
        public static readonly PropertyKey PKey_AppUserModel_RelaunchDisplayNameResource = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 4);

        private static ITaskbarList3 _taskbarList = null;
        private static readonly Dictionary<string, IntPtr> _cachedBigIcons = new Dictionary<string, IntPtr>();
        private static readonly Dictionary<string, IntPtr> _cachedSmallIcons = new Dictionary<string, IntPtr>();
        private static readonly HashSet<long> _styledWindows = new HashSet<long>();

        public static void RefreshDesktopShell()
        {
            // Do not broadcast SHCNE_ASSOCCHANGED as Windows Explorer will freeze and spin the wait cursor OS-wide!
        }

        public static void ClearAllIconCaches()
        {
            try
            {
                foreach (var h in _cachedBigIcons.Values)
                {
                    if (h != IntPtr.Zero) DestroyIcon(h);
                }
                _cachedBigIcons.Clear();

                foreach (var h in _cachedSmallIcons.Values)
                {
                    if (h != IntPtr.Zero) DestroyIcon(h);
                }
                _cachedSmallIcons.Clear();
            }
            catch { }
            _styledWindows.Clear();
        }

        public static void InvalidateIconCache(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return;
            try
            {
                if (_cachedBigIcons.ContainsKey(iconPath))
                {
                    IntPtr h = _cachedBigIcons[iconPath];
                    if (h != IntPtr.Zero) DestroyIcon(h);
                    _cachedBigIcons.Remove(iconPath);
                }
                if (_cachedSmallIcons.ContainsKey(iconPath))
                {
                    IntPtr h = _cachedSmallIcons[iconPath];
                    if (h != IntPtr.Zero) DestroyIcon(h);
                    _cachedSmallIcons.Remove(iconPath);
                }
            }
            catch { }
            _styledWindows.Clear();
        }

        private static void EnsureTaskbarList()
        {
            if (_taskbarList == null)
            {
                try
                {
                    _taskbarList = (ITaskbarList3)new TaskbarInstance();
                    _taskbarList.HrInit();
                }
                catch { }
            }
        }

        public static void SyncAllTaskbarIcons(List<Profile> profiles, string defaultZaloExe)
        {
            if (profiles == null || profiles.Count == 0) return;

            try
            {
                EnsureTaskbarList();

                // Map pids to profiles
                Process[] zProcs = Process.GetProcessesByName("Zalo");
                if (zProcs == null || zProcs.Length == 0) return;

                Dictionary<uint, Profile> pidToProfile = new Dictionary<uint, Profile>();

                foreach (Process p in zProcs)
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (string.IsNullOrEmpty(path)) continue;

                        foreach (Profile profile in profiles)
                        {
                            if (profile.Id == "1")
                            {
                                if (path.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    pidToProfile[(uint)p.Id] = profile;
                                    break;
                                }
                            }
                            else
                            {
                                if (path.IndexOf("Profile_" + profile.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    pidToProfile[(uint)p.Id] = profile;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (pidToProfile.Count == 0) return;

                // Enumerate top-level windows
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        uint pid;
                        GetWindowThreadProcessId(hWnd, out pid);
                        if (pidToProfile.ContainsKey(pid))
                        {
                            StringBuilder sbClass = new StringBuilder(256);
                            GetClassName(hWnd, sbClass, 256);
                            string cls = sbClass.ToString();

                            // Target Electron top-level application windows
                            if (cls.IndexOf("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                ApplyIconToWindow(hWnd, pidToProfile[pid]);
                            }
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);

                // Clean up dead window handles from _styledWindows cache
                if (_styledWindows.Count > 0)
                {
                    List<long> dead = null;
                    foreach (long k in _styledWindows)
                    {
                        if (!IsWindow(new IntPtr(k)))
                        {
                            if (dead == null) dead = new List<long>();
                            dead.Add(k);
                        }
                    }
                    if (dead != null)
                    {
                        foreach (long dk in dead) _styledWindows.Remove(dk);
                    }
                }
            }
            catch { }
        }

        public static void ApplyIconToWindow(IntPtr hWnd, Profile profile)
        {
            if (hWnd == IntPtr.Zero || profile == null) return;
            long wKey = hWnd.ToInt64();

            // Only style each window once per icon version to avoid interfering with Electron message loop
            if (_styledWindows.Contains(wKey)) return;

            string iconPath = profile.IconPath;
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath)) return;

            try
            {
                // 1. Taskbar Grouping and Icon linking via AppUserModelId & PropertyStore
                // Apply unique AppUserModelId matching the patched app.asar and Start Menu shortcut
                string appModelId = LauncherService.GetProfileAppId(profile.Id);
                Guid guidPropertyStore = new Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
                IPropertyStore propStore;
                if (SHGetPropertyStoreForWindow(hWnd, ref guidPropertyStore, out propStore) == 0 && propStore != null)
                {
                    using (PropVariant pvId = new PropVariant(appModelId))
                    {
                        PropertyKey keyId = PKey_AppUserModel_ID;
                        propStore.SetValue(ref keyId, pvId);
                    }

                    using (PropVariant pvIcon = new PropVariant(iconPath + ",0"))
                    {
                        PropertyKey keyIcon = PKey_AppUserModel_RelaunchIconResource;
                        propStore.SetValue(ref keyIcon, pvIcon);
                    }

                    using (PropVariant pvName = new PropVariant(profile.Name))
                    {
                        PropertyKey keyName = PKey_AppUserModel_RelaunchDisplayNameResource;
                        propStore.SetValue(ref keyName, pvName);
                    }

                    propStore.Commit();
                }

                // 2. Load or retrieve cached icon handles
                IntPtr hBig = IntPtr.Zero;
                IntPtr hSmall = IntPtr.Zero;

                if (!_cachedBigIcons.TryGetValue(iconPath, out hBig) || hBig == IntPtr.Zero)
                {
                    hBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 48, 48, LR_LOADFROMFILE);
                    if (hBig != IntPtr.Zero) _cachedBigIcons[iconPath] = hBig;
                }
                if (!_cachedSmallIcons.TryGetValue(iconPath, out hSmall) || hSmall == IntPtr.Zero)
                {
                    hSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (hSmall != IntPtr.Zero) _cachedSmallIcons[iconPath] = hSmall;
                }

                // 3. Send WM_SETICON & SetClassLongPtr to update Taskbar button, Alt+Tab & Titlebar
                if (hBig != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
                    SetClassLongPtr(hWnd, -14 /* GCLP_HICON */, hBig);
                }
                if (hSmall != IntPtr.Zero)
                {
                    SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
                    SetClassLongPtr(hWnd, -34 /* GCLP_HICONSM */, hSmall);
                }

                // 4. Force Taskbar button recreation to adopt new AppUserModelId & Icon immediately
                if (_taskbarList != null)
                {
                    try
                    {
                        _taskbarList.DeleteTab(hWnd);
                        _taskbarList.AddTab(hWnd);
                        _taskbarList.SetOverlayIcon(hWnd, IntPtr.Zero, null);
                    }
                    catch { }
                }

                _styledWindows.Add(wKey);
            }
            catch { }
        }
    }
}
