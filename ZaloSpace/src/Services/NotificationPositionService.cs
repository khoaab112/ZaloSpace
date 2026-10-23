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

        // Smart Stacking Manager for avoiding notification overlap / stacking multiple notifications & calls
        private class ActiveWindowSlot
        {
            public IntPtr HWnd;
            public int Width;
            public int Height;
            public int TargetX;
            public int TargetY;
            public DateTime RegisteredTime;
        }

        private static readonly List<ActiveWindowSlot> _activeSlots = new List<ActiveWindowSlot>();
        private static readonly object _lock = new object();

        public static string CurrentPosition
        {
            get { return _currentPosition; }
            set { _currentPosition = string.IsNullOrEmpty(value) ? "BottomRight" : value; }
        }

        public static void SetPosition(string pos)
        {
            CurrentPosition = pos;
            lock (_lock)
            {
                _activeSlots.Clear();
            }
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
                _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
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
            lock (_lock)
            {
                _activeSlots.Clear();
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

        private static void CleanUpActiveSlots()
        {
            for (int i = _activeSlots.Count - 1; i >= 0; i--)
            {
                IntPtr h = _activeSlots[i].HWnd;
                if (!IsWindow(h) || !IsWindowVisible(h))
                {
                    _activeSlots.RemoveAt(i);
                }
            }
        }

        private static void TryRepositionZaloNotification(IntPtr hWnd)
        {
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd)) return;

            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == 0) return;

            // 1. NEVER touch our own ZaloSpaceManager process or windows!
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

            // 2. Never touch ZaloSpaceManager or non-Zalo applications
            if (procName.IndexOf("ZaloSpace", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            // 3. Skip screen capture (ZaloCap) and video meeting rooms (ZaviMeet)
            if (procName.IndexOf("ZaloCap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                procName.IndexOf("ZaviMeet", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            // 4. Must be a genuine Zalo process (Zalo, ZaloCall, ZaloExecutable)
            if (procName.IndexOf("zalo", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // 5. Exclude WPF windows (popups, context menus, tooltips)
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            string className = sb.ToString();
            if (className.IndexOf("HwndWrapper", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            RECT rect;
            if (!GetWindowRect(hWnd, out rect)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            // 6. Comprehensive filter for Zalo notifications & Call popups:
            // - Toast message: width ~200-540px, height ~40-240px
            // - Incoming Call card (ZaloCall): width ~240-480px, height ~180-360px
            // Exclude main chat window which is >= 600x400
            if (w < 200 || w > 540 || h < 40 || h > 380)
                return;

            if (w >= 600 || h >= 400)
                return;

            int targetX;
            int targetY;
            CalculateStackedCoordinates(hWnd, w, h, _currentPosition, out targetX, out targetY);

            // Check if already positioned at target (within 6px tolerance)
            if (Math.Abs(rect.Left - targetX) <= 6 && Math.Abs(rect.Top - targetY) <= 6)
                return;

            SetWindowPos(hWnd, HWND_TOPMOST, targetX, targetY, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static void CalculateStackedCoordinates(IntPtr hWnd, int width, int height, string position, out int targetX, out int targetY)
        {
            lock (_lock)
            {
                CleanUpActiveSlots();

                // If this window already has an assigned slot and hasn't changed position mode:
                ActiveWindowSlot existing = null;
                for (int i = 0; i < _activeSlots.Count; i++)
                {
                    if (_activeSlots[i].HWnd == hWnd)
                    {
                        existing = _activeSlots[i];
                        break;
                    }
                }

                Rect workArea = SystemParameters.WorkArea;
                int margin = 16;
                int gap = 10; // spacing between stacked notifications

                int sLeft = (int)workArea.Left;
                int sTop = (int)workArea.Top;
                int sRight = (int)workArea.Right;
                int sBottom = (int)workArea.Bottom;
                int sWidth = (int)workArea.Width;

                // Determine base horizontal X
                int baseX;
                switch (position)
                {
                    case "TopLeft":
                    case "BottomLeft":
                        baseX = sLeft + margin;
                        break;

                    case "TopCenter":
                    case "BottomCenter":
                        baseX = sLeft + (sWidth - width) / 2;
                        break;

                    case "TopRight":
                    case "BottomRight":
                    default:
                        baseX = sRight - width - margin;
                        break;
                }

                // If already assigned, keep its position so it doesn't jump
                if (existing != null)
                {
                    targetX = baseX;
                    targetY = existing.TargetY;
                    existing.Width = width;
                    existing.Height = height;
                    return;
                }

                // Calculate stacked Y for new window
                bool isTop = position.StartsWith("Top");
                int calculatedY;

                if (isTop)
                {
                    // Stack from top down: window 1 at top, window 2 below window 1, etc.
                    calculatedY = sTop + margin;
                    foreach (ActiveWindowSlot slot in _activeSlots)
                    {
                        int nextSlotY = slot.TargetY + slot.Height + gap;
                        if (nextSlotY > calculatedY)
                        {
                            calculatedY = nextSlotY;
                        }
                    }

                    // Bounds check
                    if (calculatedY + height > sBottom - margin)
                    {
                        calculatedY = sBottom - height - margin;
                    }
                }
                else
                {
                    // Stack from bottom up: window 1 at bottom, window 2 above window 1, etc.
                    calculatedY = sBottom - height - margin;
                    foreach (ActiveWindowSlot slot in _activeSlots)
                    {
                        int nextSlotY = slot.TargetY - height - gap;
                        if (nextSlotY < calculatedY)
                        {
                            calculatedY = nextSlotY;
                        }
                    }

                    // Bounds check
                    if (calculatedY < sTop + margin)
                    {
                        calculatedY = sTop + margin;
                    }
                }

                targetX = baseX;
                targetY = calculatedY;

                _activeSlots.Add(new ActiveWindowSlot
                {
                    HWnd = hWnd,
                    Width = width,
                    Height = height,
                    TargetX = targetX,
                    TargetY = targetY,
                    RegisteredTime = DateTime.Now
                });
            }
        }
    }
}
