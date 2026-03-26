#nullable enable
using ChassisCAM.Core.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace ChassisCAM.Presentation.Windows;
public class OCCTHost : Win32Host {
   protected override HandleRef BuildWindowCore (HandleRef hwndParent) {
      var res = base.BuildWindowCore (hwndParent);
      if (res.Handle == IntPtr.Zero)
         return res;

      // Subclass the window procedure
      childWndProcDG = CustomWndProc;
      childoldWndProc = SetWindowLongPtr (childHwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate (childWndProcDG));
      return res;
   }

   IntPtr CustomWndProc (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
      if (DesignerProperties.GetIsInDesignMode (this))
         return CallWindowProc (childoldWndProc, hWnd, msg, wParam, lParam); // Call original window procedure for default handling
      switch (msg) {
         case WM_PAINT:
            OnPaint ();
            break;

         case WM_LBUTTONDOWN:
            OnMouseLeftButtonDown (wParam, lParam);
            break;

         case WM_LBUTTONUP:
            OnMouseLeftButtonUp (wParam, lParam);
            break;

         case WM_MOUSEMOVE:
            OnMouseMove (wParam, lParam);
            break;

         case WM_MOUSEWHEEL:
         OnMouseWheel (wParam, lParam);
         break;
      }

      Debug.Assert (childoldWndProc != IntPtr.Zero);
      return CallWindowProc (childoldWndProc, hWnd, msg, wParam, lParam); // Call original window procedure for default handling
   }

   void OnPaint () {
      ValidateRect (childHwnd, IntPtr.Zero);
      Redraw?.Invoke ();
   }

   void OnMouseLeftButtonDown (IntPtr wParam, IntPtr lParam) {
      var (x, y) = GetMousePos (lParam);
      SetFocus (childHwnd);
      panning = true;
      startMousePos.X = x;
      startMousePos.Y = y;
   }

   void OnMouseLeftButtonUp (IntPtr wParam, IntPtr lParam) 
      => panning = false;

   void OnMouseMove (IntPtr wParam, IntPtr lParam) {
      var (x, y) = GetMousePos (lParam);
      if (panning) {
         int dx = x - (int)startMousePos.X;
         int dy = y - (int)startMousePos.Y;
         Pan?.Invoke (dx, dy);

         startMousePos.X = x;
         startMousePos.Y = y;
      }
   }

   (int x, int y) GetMousePos (IntPtr lParam) {
      int lParamVal = lParam.ToInt32 ();
      int x = lParamVal & 0xFFFF;
      int y = (lParamVal >> 16) & 0xFFFF;
      return (x, y);
   }

   void OnMouseWheel (IntPtr wParam, IntPtr lParam) {
      var (x, y) = GetMousePos (lParam);
      
      POINT pt = new POINT { X = x, Y = y };
      ScreenToClient (childHwnd, ref pt); // Convert to client coordinates

      int delta = (short)((wParam.ToInt64 () >> 16) & 0xFFFF);
      Zoom?.Invoke (delta > 0, pt.X, pt.Y);
   }

   protected override void OnRenderSizeChanged (SizeChangedInfo sizeInfo) {
      base.OnRenderSizeChanged (sizeInfo);

      Redraw?.Invoke ();
   }

   protected override void DestroyWindowCore (HandleRef hwnd) {
      base.DestroyWindowCore (hwnd);
      childoldWndProc = IntPtr.Zero;
   }

   public Action? Redraw;
   public Action<int, int>? Pan;
   public Action<bool, int, int>? Zoom;

   IntPtr childoldWndProc = IntPtr.Zero;
   DGChildWndProc? childWndProcDG;

   bool panning = true;
   Point startMousePos;

   #region Delegate Declarations
   public delegate IntPtr DGChildWndProc (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
   #endregion Delegate Declarations
}