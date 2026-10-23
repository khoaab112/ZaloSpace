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
        private static readonly HashSet<IntPtr> _processedWindows = new HashSet<IntPtr>();

        public static string CurrentPosition
        {
            get { return _currentPosition; }
            set { _currentPosition = string.IsNullOrEmpty(value) ? "BottomRight" : value; }
        }

        public static void SetPosition(string pos)
        {
            CurrentPosition = pos;
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
                _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
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
            TryRepositionZaloNotification(hWnd);
        }

        private static void CheckAndRepositionOpenNotifications()
        {
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                TryRepositionZaloNotification(hWnd);
                return true;
            }, IntPtr.Zero);
        }

        private static void TryRepositionZaloNotification(IntPtr hWnd)
        {
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd)) return;

            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == 0) return;

            // CRITICAL: NEVER touch our own ZaloSpaceManager process or windows!
            if (pid == (uint)Process.GetCurrentProcess().Id) return;

            string procName = null;
            try
            {
                using (Process proc = Process.GetProcessById((int)pid))
                {
                    procName = proc.ProcessName;
                }
            }
            catch { return; }

            if (string.IsNullOrEmpty(procName)) return;

            // Never touch ZaloSpaceManager
            if (procName.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            // Never touch ZaloCall (call UI window), ZaloCap (screen capture), or ZaviMeet (video meeting)
            if (procName.IndexOf("ZaloCall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                procName.IndexOf("ZaloCap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                procName.IndexOf("ZaviMeet", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            // Must be genuine Zalo PC main process
            if (procName.IndexOf("zalo", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // Exclude WPF windows (popups, context menus, tooltips)
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            string className = sb.ToString();
            if (className.IndexOf("HwndWrapper", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            RECT rect;
            if (!GetWindowRect(hWnd, out rect)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            // Zalo Toast popup typically has width ~260-440px and height ~55-160px
            // and is distinct from the main Zalo window (which is >= 600x400)
            if (w < 250 || w > 460 || h < 50 || h > 170)
                return;

            // If user keeps default BottomRight and it's already near bottom-right, don't force move unless needed
            int targetX;
            int targetY;
            CalculateTargetCoordinates(w, h, _currentPosition, out targetX, out targetY);

            // Check if already positioned at target (within 6px tolerance)
            if (Math.Abs(rect.Left - targetX) <= 6 && Math.Abs(rect.Top - targetY) <= 6)
                return;

            SetWindowPos(hWnd, HWND_TOPMOST, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static void CalculateTargetCoordinates(int width, int height, string position, out int targetX, out int targetY)
        {
            Rect workArea = SystemParameters.WorkArea;
            int margin = 16;

            int sLeft = (int)workArea.Left;
            int sTop = (int)workArea.Top;
            int sRight = (int)workArea.Right;
            int sBottom = (int)workArea.Bottom;
            int sWidth = (int)workArea.Width;

            switch (position)
            {
                case "TopLeft":
                    targetX = sLeft + margin;
                    targetY = sTop + margin;
                    break;

                case "TopCenter":
                    targetX = sLeft + (sWidth - width) / 2;
                    targetY = sTop + margin;
                    break;

                case "TopRight":
                    targetX = sRight - width - margin;
                    targetY = sTop + margin;
                    break;

                case "BottomLeft":
                    targetX = sLeft + margin;
                    targetY = sBottom - height - margin;
                    break;

                case "BottomCenter":
                    targetX = sLeft + (sWidth - width) / 2;
                    targetY = sBottom - height - margin;
                    break;

                case "BottomRight":
                default:
                    targetX = sRight - width - margin;
                    targetY = sBottom - height - margin;
                    break;
            }
        }
    }
}
