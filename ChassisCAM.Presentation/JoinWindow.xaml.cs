using System.Windows;
using ChassisCAM.Core.Windows;
namespace ChassisCAM.Presentation;
public partial class JoinWindow : Window, IDisposable {
   public ViewModel.JoinWindowVM joinWndVM = new ();
   public JoinWindow () {
      InitializeComponent ();

      joinWndVM.Initialize ();
      this.DataContext = joinWndVM;
      joinWndVM.EvRequestCloseWindow += () => this.Close ();

      this.OCCTHostContent.Content = this.OCCTHostWnd;
      this.joinWndVM.Redraw = this.OCCTHostWnd.InvalidateChildWindow;

      var iges = this.joinWndVM.Iges;
      this.Loaded += (sender, e) => iges.InitView (this.OCCTHostWnd.childHwnd);
      this.OCCTHostWnd.Pan = iges.Pan;
      this.OCCTHostWnd.Zoom = iges.Zoom;
      this.OCCTHostWnd.Redraw = iges.Redraw;

      this.Closing += JoinWindow_Closing;
   }

   Windows.OCCTHost OCCTHostWnd = new ();

   void JoinWindow_Closing (object sender, System.ComponentModel.CancelEventArgs e) {
      this.DialogResult = true;
      Dispose (); // Ensure cleanup on window close
   }

   // Implement IDisposable pattern
   public void Dispose () {
      if (joinWndVM != null) {
         joinWndVM.Uninitialize ();
         joinWndVM = null;
      }

      GC.SuppressFinalize (this);
   }

   ~JoinWindow () {
      Dispose ();
   }
}