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

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);
        private const int DWMWA_CLOAKED = 14;

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

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOZORDER = 0x0004;

        private static IntPtr _hookShow = IntPtr.Zero;
        private static IntPtr _hookLoc = IntPtr.Zero;
        private static WinEventDelegate _eventDelegate;
        private static DispatcherTimer _pollTimer;
        private static string _currentPosition = "BottomRight";

        private class ToastItem
        {
            public IntPtr Hwnd;
            public int Width;
            public int Height;
            public bool IsCall;
            public int Top;
            public int Left;
        }

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
        /// Real-Time Self-Healing Notification & Call Alert Repositioning Engine:
        /// - Detects both regular chat toasts (~82px) and incoming call alerts (~180px).
        /// - Incoming calls receive priority #1: always placed at primary anchor corner, never subordinated.
        /// - Strips SWP_SHOWWINDOW so closing/fading notifications are never forced open as phantom slots.
        /// - Filters out DWM cloaked and off-screen ghost windows to eliminate mid-air floating bugs.
        /// </summary>
        public static void CheckAndRepositionOpenNotifications()
        {
            List<ToastItem> items = new List<ToastItem>();

            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
                    return true;

                // Ignore cloaked windows (hidden or animating out by DWM/Electron)
                int cloaked = 0;
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
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

                // 2. Never touch ZaloCall native binary, ZaloCap, or ZaviMeet
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

                // 5. Toast / Call alert filter: width ~200-520px, height ~40-300px
                // Main chat window is >= 600x400
                if (w < 200 || w > 520 || h < 40 || h > 300)
                    return true;

                // Ignore off-screen ghost windows
                if (rect.Right <= 0 || rect.Bottom <= 0 || rect.Left < -50 || rect.Top < -50)
                    return true;

                bool isCall = (h >= 150);

                items.Add(new ToastItem
                {
                    Hwnd = hWnd,
                    Width = w,
                    Height = h,
                    IsCall = isCall,
                    Top = rect.Top,
                    Left = rect.Left
                });

                return true;
            }, IntPtr.Zero);

            if (items.Count == 0) return;

            // Sorting Strategy:
            // 1. Active incoming calls (IsCall == true) MUST ALWAYS BE SLOT 0!
            //    A ringing call is urgent; it must occupy the prime anchor position.
            // 2. Regular message toasts follow in natural order.
            items.Sort(delegate(ToastItem a, ToastItem b)
            {
                if (a.IsCall && !b.IsCall) return -1;
                if (!a.IsCall && b.IsCall) return 1;
                return a.Top.CompareTo(b.Top);
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
                // Stack downwards from top:
                // Slot 0 (Incoming Call or newest toast) is placed at the top margin.
                // Subsequent toasts stack below it.
                int currentY = sTop + margin;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int targetX = GetTargetX(item.Width, _currentPosition, sLeft, sRight, sWidth, margin);
                    int targetY = currentY;

                    if (targetY + item.Height > sBottom - margin)
                    {
                        targetY = sBottom - item.Height - margin;
                    }

                    if (Math.Abs(item.Left - targetX) > 4 || Math.Abs(item.Top - targetY) > 4)
                    {
                        // Use SWP_NOZORDER, NO SWP_SHOWWINDOW
                        SetWindowPos(item.Hwnd, IntPtr.Zero, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
                    }

                    currentY = targetY + item.Height + gap;
                }
            }
            else
            {
                // Stack upwards from bottom:
                // Slot 0 (Incoming Call or primary toast) is placed at the bottom margin.
                // Subsequent toasts stack above it.
                int runningBottom = sBottom - margin;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int targetX = GetTargetX(item.Width, _currentPosition, sLeft, sRight, sWidth, margin);
                    int targetY = runningBottom - item.Height;

                    if (targetY < sTop + margin)
                    {
                        targetY = sTop + margin;
                    }

                    if (Math.Abs(item.Left - targetX) > 4 || Math.Abs(item.Top - targetY) > 4)
                    {
                        // Use SWP_NOZORDER, NO SWP_SHOWWINDOW
                        SetWindowPos(item.Hwnd, IntPtr.Zero, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
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
