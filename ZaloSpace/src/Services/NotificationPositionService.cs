using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ZaloSpaceManager.Services
{
    public static class NotificationPositionService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFPROCESS = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private static IntPtr _hookShow = IntPtr.Zero;
        private static IntPtr _hookLoc = IntPtr.Zero;
        private static WinEventDelegate _eventDelegate;
        private static DispatcherTimer _pollTimer;
        private static string _currentPosition = "BottomRight";

        public static string CurrentPosition
        {
            get { return _currentPosition; }
            set { _currentPosition = string.IsNullOrEmpty(value) ? "BottomRight" : value; }
        }

        public static void SetPosition(string pos)
        {
            CurrentPosition = pos;
            CheckAndRepositionOpenNotifications();
        }

        public static void Start(string initialPosition = "BottomRight")
        {
            CurrentPosition = initialPosition;
            if (_eventDelegate == null)
            {
                _eventDelegate = new WinEventDelegate(WinEventProc);
            }

            try
            {
                if (_hookShow == IntPtr.Zero)
                {
                    _hookShow = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _eventDelegate, 0, 0, WINEVENT_OUTOFPROCESS | WINEVENT_SKIPOWNPROCESS);
                }
                if (_hookLoc == IntPtr.Zero)
                {
                    _hookLoc = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _eventDelegate, 0, 0, WINEVENT_OUTOFPROCESS | WINEVENT_SKIPOWNPROCESS);
                }
            }
            catch { }

            if (_pollTimer == null)
            {
                _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _pollTimer.Tick += delegate
                {
                    CheckAndRepositionOpenNotifications();
                };
                _pollTimer.Start();
            }
        }

        public static void Stop()
        {
            if (_hookShow != IntPtr.Zero)
            {
                UnhookWinEvent(_hookShow);
                _hookShow = IntPtr.Zero;
            }
            if (_hookLoc != IntPtr.Zero)
            {
                UnhookWinEvent(_hookLoc);
                _hookLoc = IntPtr.Zero;
            }
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
        }

        private static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || hWnd == IntPtr.Zero) return;
            CheckAndRepositionOpenNotifications();
        }

        /// <summary>
        /// 100% Dynamic Self-Healing Stacking Engine:
        /// Scans all genuinely visible Zalo message notifications in real-time,
        /// sorts them, and stacks them without any stale state or phantom windows.
        /// Excludes ZaloCall, ZaloCap, ZaviMeet completely so calling is never disrupted.
        /// </summary>
        public static void CheckAndRepositionOpenNotifications()
        {
            List<IntPtr> activeToasts = new List<IntPtr>();

            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
                    return true;

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id)
                    return true;

                string procName = null;
                try
                {
                    using (Process proc = Process.GetProcessById((int)pid))
                    {
                        procName = proc.ProcessName;
                    }
                }
                catch { return true; }

                if (string.IsNullOrEmpty(procName)) return true;

                // 1. Exclude ZaloSpaceManager
                if (procName.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // 2. CRITICAL: Never touch ZaloCall (calls), ZaloCap (capture), or ZaviMeet (meetings)
                if (procName.IndexOf("ZaloCall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    procName.IndexOf("ZaloCap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    procName.IndexOf("ZaviMeet", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                // 3. Must be Zalo client (Zalo, ZaloExecutable)
                if (procName.IndexOf("zalo", StringComparison.OrdinalIgnoreCase) < 0)
                    return true;

                // 4. Exclude WPF windows
                StringBuilder sb = new StringBuilder(256);
                GetClassName(hWnd, sb, sb.Capacity);
                string className = sb.ToString();
                if (className.IndexOf("HwndWrapper", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                RECT rect;
                if (!GetWindowRect(hWnd, out rect)) return true;

                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;

                // 5. Message toast filter: width ~200-520px, height ~40-200px
                // Exclude main chat window (>= 600x400) and off-screen / ghost windows
                if (w < 200 || w > 520 || h < 40 || h > 200)
                    return true;

                if (rect.Left < -500 || rect.Top < -500)
                    return true;

                activeToasts.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            if (activeToasts.Count == 0) return;

            // Sort active toasts by their current vertical position or handle
            // so order is natural and stable
            activeToasts.Sort(delegate(IntPtr a, IntPtr b)
            {
                RECT ra, rb;
                GetWindowRect(a, out ra);
                GetWindowRect(b, out rb);
                return ra.Top.CompareTo(rb.Top);
            });

            Rect workArea = SystemParameters.WorkArea;
            int margin = 16;
            int gap = 8;

            int sLeft = (int)workArea.Left;
            int sTop = (int)workArea.Top;
            int sRight = (int)workArea.Right;
            int sBottom = (int)workArea.Bottom;
            int sWidth = (int)workArea.Width;

            bool isTop = _currentPosition.StartsWith("Top");

            if (isTop)
            {
                // Stack downwards from top
                int currentY = sTop + margin;
                for (int i = 0; i < activeToasts.Count; i++)
                {
                    IntPtr hWnd = activeToasts[i];
                    RECT rect;
                    if (!GetWindowRect(hWnd, out rect)) continue;
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;

                    int targetX = GetTargetX(w, _currentPosition, sLeft, sRight, sWidth, margin);
                    int targetY = currentY;

                    // Bounds check
                    if (targetY + h > sBottom - margin)
                    {
                        targetY = sBottom - h - margin;
                    }

                    if (Math.Abs(rect.Left - targetX) > 4 || Math.Abs(rect.Top - targetY) > 4)
                    {
                        SetWindowPos(hWnd, HWND_TOPMOST, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }

                    currentY = targetY + h + gap;
                }
            }
            else
            {
                // Stack upwards from bottom
                int runningBottom = sBottom - margin;
                for (int i = activeToasts.Count - 1; i >= 0; i--)
                {
                    IntPtr hWnd = activeToasts[i];
                    RECT rect;
                    if (!GetWindowRect(hWnd, out rect)) continue;
                    int w = rect.Right - rect.Left;
                    int h = rect.Bottom - rect.Top;

                    int targetX = GetTargetX(w, _currentPosition, sLeft, sRight, sWidth, margin);
                    int targetY = runningBottom - h;

                    // Bounds check
                    if (targetY < sTop + margin)
                    {
                        targetY = sTop + margin;
                    }

                    if (Math.Abs(rect.Left - targetX) > 4 || Math.Abs(rect.Top - targetY) > 4)
                    {
                        SetWindowPos(hWnd, HWND_TOPMOST, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }

                    runningBottom = targetY - gap;
                }
            }
        }

        private static int GetTargetX(int width, string position, int sLeft, int sRight, int sWidth, int margin)
        {
            switch (position)
            {
                case "TopLeft":
                case "BottomLeft":
                    return sLeft + margin;

                case "TopCenter":
                case "BottomCenter":
                    return sLeft + (sWidth - width) / 2;

                case "TopRight":
                case "BottomRight":
                default:
                    return sRight - width - margin;
            }
        }
    }
}
