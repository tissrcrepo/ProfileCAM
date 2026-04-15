using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ProfileCAM.Core.Windows;
public class Win32Host : HwndHost {
   protected override HandleRef BuildWindowCore (HandleRef hwndParent) {
      #region For Designer
      if (DesignerProperties.GetIsInDesignMode (this)) {
         var placeholder = new System.Windows.Controls.Label { // Create a dummy control (label, etc.) for the Designer
            Content = "OCCTHost Placeholder",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
         };

         var hwndSource = (HwndSource)PresentationSource.FromVisual (placeholder);
         return hwndSource?.Handle != null ? new HandleRef (this, hwndSource.Handle) : new HandleRef (this, IntPtr.Zero);
      }
      #endregion For Designer

      childHwnd = CreateWindowEx (
          0, "STATIC", "", WS_CHILD | WS_VISIBLE | WS_TABSTOP | SS_NOTIFY,
          0, 0, (int)ActualWidth, (int)ActualHeight,
          hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

      return new HandleRef (this, childHwnd);
   }

   protected override void DestroyWindowCore (HandleRef hwnd) {
      if (hwnd.Handle != IntPtr.Zero) {
         DestroyWindow (hwnd.Handle);
         childHwnd = IntPtr.Zero;
      }
   }

   public void InvalidateChildWindow ()
      => InvalidateRect (childHwnd, IntPtr.Zero, false);

   public IntPtr childHwnd;

   #region PInvoke declarations
   [DllImport ("user32.dll", SetLastError = true)]
   public static extern IntPtr CallWindowProc (IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

   [DllImport ("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
   public static extern IntPtr CreateWindowEx (
       int dwExStyle, string lpszClassName, string lpszWindowName,
       int style, int x, int y, int width, int height,
       IntPtr hwndParent, IntPtr hMenu, IntPtr hInst, IntPtr pvParam);

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern bool DestroyWindow (IntPtr hwnd);

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern bool MoveWindow (IntPtr hwnd, int x, int y, int width, int height, bool repaint);

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern bool SetWindowPos (IntPtr hWnd, IntPtr hWndInsertAfter,
                                           int X, int Y, int cx, int cy, uint uFlags);

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern bool ValidateRect (IntPtr hWnd, IntPtr lpRect); // lpRect = IntPtr.Zero validates entire client area

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern bool InvalidateRect (IntPtr hWnd, IntPtr lpRect, bool bErase); // lpRect = IntPtr.Zero validates entire client area

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern IntPtr SetWindowLongPtr (IntPtr hWnd, int nIndex, IntPtr dwNewLong);

   [DllImport ("user32.dll", SetLastError = true)]
   public static extern IntPtr GetWindowLongPtr (IntPtr hWnd, int nIndex);

   [DllImport ("user32.dll")]
   public static extern IntPtr SetFocus (IntPtr hWnd);

   [DllImport ("user32.dll")]
   public static extern bool ScreenToClient (IntPtr hWnd, ref POINT lpPoint);
   #endregion PInvoke declarations

   #region PInvoke constant declarations
   [StructLayout (LayoutKind.Sequential)]
   public struct POINT {
      public int X;
      public int Y;
   }

   public const int WS_CHILD = 0x40000000;
   public const int WS_VISIBLE = 0x10000000;
   public const int WS_TABSTOP = 0x00010000;
   public const int SS_NOTIFY = 0x00000100;

   public const int GWLP_WNDPROC = -4;
   public const int WM_PAINT = 0x000F;
   public const int WM_MOUSEWHEEL = 0x020A;
   public const int WM_LBUTTONDOWN = 0x0201;
   public const int WM_LBUTTONUP = 0x0202;
   public const int WM_MOUSEMOVE = 0x0200;
   #endregion PInvoke constant declarations
}
