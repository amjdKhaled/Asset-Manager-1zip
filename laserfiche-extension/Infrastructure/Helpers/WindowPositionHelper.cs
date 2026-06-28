using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace LaserficheAIExtension.Infrastructure.Helpers
{
    /// <summary>
    /// Helper for saving and restoring window position while ensuring it stays on screen.
    /// </summary>
    public static class WindowPositionHelper
    {
        public static void EnsureOnScreen(ref double left, ref double top, double width, double height)
        {
            var screen = Screen.FromPoint(new System.Drawing.Point((int)left, (int)top));
            var workingArea = screen.WorkingArea;

            // Ensure window is not off-screen
            if (left + width > workingArea.Right)
                left = workingArea.Right - width;
            if (top + height > workingArea.Bottom)
                top = workingArea.Bottom - height;
            if (left < workingArea.Left)
                left = workingArea.Left;
            if (top < workingArea.Top)
                top = workingArea.Top;
        }

        public static void ApplyToWindow(Window window, double left, double top, double width, double height, bool isMaximized)
        {
            if (isMaximized)
            {
                window.WindowState = WindowState.Maximized;
                return;
            }

            EnsureOnScreen(ref left, ref top, width, height);

            window.Left = left;
            window.Top = top;
            window.Width = width;
            window.Height = height;
        }

        public static void CaptureFromWindow(Window window, out double left, out double top, out double width, out double height, out bool isMaximized)
        {
            isMaximized = window.WindowState == WindowState.Maximized;

            if (isMaximized)
            {
                // When maximized, capture restore bounds
                var hwnd = new WindowInteropHelper(window).Handle;
                NativeMethods.GetWindowPlacement(hwnd, out var placement);
                left = placement.rcNormalPosition.Left;
                top = placement.rcNormalPosition.Top;
                width = placement.rcNormalPosition.Right - placement.rcNormalPosition.Left;
                height = placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top;
            }
            else
            {
                left = window.Left;
                top = window.Top;
                width = window.Width;
                height = window.Height;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool GetWindowPlacement(IntPtr hWnd, out WINDOWPLACEMENT lpwndpl);

            public struct WINDOWPLACEMENT
            {
                public int length;
                public int flags;
                public int showCmd;
                public System.Drawing.Point ptMinPosition;
                public System.Drawing.Point ptMaxPosition;
                public RECT rcNormalPosition;
            }

            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }
        }
    }
}
