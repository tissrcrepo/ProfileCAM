using System.Windows;

namespace ChassisCAM.Presentation;
public partial class JoinResult : Window {
   public ViewModel.JoinResultVM joinResVM = new ();

   public JoinResult () {
      InitializeComponent ();
      joinResVM.Initialize ();
      this.Closing += OnJoinResultWndClosing;
      this.DataContext = joinResVM;
      void OnJoinResultWndClosing (object? sender, System.ComponentModel.CancelEventArgs e) => this.DialogResult = true; // Added ? to object
   }

   void OnJoinResultWndClosing (object sender, System.ComponentModel.CancelEventArgs e) => this.DialogResult = true;
}
