using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ProfileCAM.Presentation.ViewModel;
public partial class JoinResultVM {
   #region Enums
   public enum JoinResultOption {
      SaveAndOpen,
      Save,
      Cancel,
      None
   }
   #endregion

   #region Properties
   public JoinResultOption Result { get; set; } = JoinResultOption.None;
   #endregion

   #region Constructor(s)/Initializers
   public void Initialize () {
      // Perform any initialization logic here if required.
   }
   #endregion

   #region Commands
   [RelayCommand]
   private void SaveAndOpen (object obj) {
      Result = JoinResultOption.SaveAndOpen;
      CloseWindow (obj);
   }

   [RelayCommand]
   private void Save (object obj) {
      Result = JoinResultOption.Save;
      CloseWindow (obj);
   }

   [RelayCommand]
   public void Cancel (object obj) {
      Result = JoinResultOption.Cancel;
      //MessageBox.Show ("Operation cancelled.");
      CloseWindow (obj);
   }
   #endregion

   #region Actions with UI
   /// <summary>
   /// Displays a Save File Dialog and returns the selected file path.
   /// </summary>
   /// <returns>Selected file path or empty string if canceled.</returns>
   private string ShowSaveFileDialog () {
      SaveFileDialog saveFileDialog = new () {
         Title = "Save File",
         Filter = "STEP Files (*.stp;*.step)|*.stp;*.step|IGS Files (*.igs;*.iges)|*.igs;*.iges|All files (*.*)|*.*",
         DefaultExt = ".txt",
         FileName = "Result"
      };

      return (saveFileDialog.ShowDialog () == true) ? saveFileDialog.FileName : string.Empty;
   }

   void CloseWindow (object obj) {
      if (obj is Window window)
         window.Close ();
   }
   #endregion
}

