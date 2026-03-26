using System.Windows;
using ChassisCAM.Core;

namespace ChassisCAM.Presentation {
   /// <summary>
   /// Interaction logic for About.xaml
   /// </summary>
   public partial class AboutWindow : Window {
      
      public AboutWindow () {
         InitializeComponent ();
         DataContext = this;
         
      }

      public string Version { get; } = MCSettings.It.Version; 

      void OnAboutCloseClick (object sender, RoutedEventArgs e) => this.Close ();
   }
}
