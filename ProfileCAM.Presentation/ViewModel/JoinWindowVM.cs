using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ProfileCAM.Presentation.ViewModel;
public partial class JoinWindowVM : ObservableObject, IDisposable {

   #region Commands
   [RelayCommand]
   void LoadPart1 () {
      var fileName = GetFilename (Part1FileName, "Select a Part File",
                                "CAD Files (*.iges;*.igs;*.stp;*.step)|*.iges;*.igs;*.stp;*.step|All Files (*.*)|*.*",
                                multiselect: false, initialDirectory);
      initialDirectory = Path.GetDirectoryName (fileName);
      if (fileName == null) return;

      Part1FileName = fileName;
      Action (() => LoadPart (0)).GetAwaiter ();
   }

   [RelayCommand]
   void LoadPart2 () {
      var fileName = GetFilename (Part2FileName, "Select a Part File",
                                "CAD Files (*.iges;*.igs;*.stp;*.step)|*.iges;*.igs;*.stp;*.step|All Files (*.*)|*.*",
                                multiselect: false, initialDirectory);
      initialDirectory = Path.GetDirectoryName (fileName);
      if (fileName == null) return;

      Part2FileName = fileName;
      Action (() => LoadPart (1)).GetAwaiter ();
   }

   [RelayCommand]
   void YawPart1By180 () => Action (() => YawBy180Internal (0)).GetAwaiter ();

   [RelayCommand]
   void YawPart2By180 () => Action (() => YawBy180Internal (1)).GetAwaiter ();

   [RelayCommand]
   void RollPart1By180 () => Action (() => RollBy180Internal (0)).GetAwaiter ();

   [RelayCommand]
   void RollPart2By180 () => Action (() => RollBy180Internal (1)).GetAwaiter ();

   [RelayCommand]
   async Task Join (object parameter) {
      await Action (Join); // Ensure Join() completes before checking the result

      if (parameter is Window currentWindow && (_joinResOpt == JoinResultVM.JoinResultOption.SaveAndOpen ||
         _joinResOpt == JoinResultVM.JoinResultOption.Cancel))
         currentWindow.Close ();
   }
   #endregion

   #region Initialization & Cleanup
   public bool Initialize () {
      Debug.Assert (Iges == null);
      Iges = new IGES.IGES ();
      Iges.Initialize ();
      return true;
   }

   public bool Uninitialize () {
      if (Iges != null) {
         Iges.Uninitialize ();
         Iges.Dispose ();
         Iges = null;
         GC.Collect ();  // Force garbage collection
         GC.WaitForPendingFinalizers ();
      }
      return true;
   }

   public void Dispose () {
      if (!_disposed) {
         Uninitialize ();
         _disposed = true;
         GC.SuppressFinalize (this);
      }
   }

   ~JoinWindowVM () => Dispose ();
   #endregion

   #region Methods
   int LoadPart (int pNo) {
      if (Iges == null) return -1; // Ensure _iges is initialized

      int errorNo = 1;
      do {
         //int shapeType = 0;

         if (pNo == 0) {
            if ((errorNo = Iges.LoadIGES (Part1FileName, pNo)) != 0)
               break;
            if ((errorNo = Iges.AlignToXYPlane (pNo)) != 0)
               break;
         } else if (pNo == 1) {
            if ((errorNo = Iges.LoadIGES (Part2FileName, pNo)) != 0)
               break;
            if ((errorNo = Iges.AlignToXYPlane (pNo)) != 0)
               break;
         } else break;

         Redraw?.Invoke ();
         //ConvertCadToImage (false);
      } while (false);
      if (errorNo != 0) HandleIGESError (errorNo);
      return errorNo;
   }

   int YawBy180Internal (int pno) {
      if (Iges == null) return -1;
      int errorNo;
      try {
         errorNo = Iges.YawPartBy180 (pno);
         Redraw?.Invoke ();
      } catch (Exception ex) {
         MessageBox.Show (ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         return 1;
      }
      //if (errorNo == 0)
      //   ConvertCadToImage (false);
      return errorNo;
   }

   int RollBy180Internal (int pno) {
      if (Iges == null) return -1;
      int errorNo;
      try {
         errorNo = Iges.RollPartBy180 (pno);
         Redraw?.Invoke ();
      } catch (Exception ex) {
         MessageBox.Show (ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         return 1;
      }
      //if (errorNo == 0)
      //   ConvertCadToImage (false);
      return errorNo;
   }

   int UndoJoin () {
      int errorNo = 0;
      if (Iges == null) return -1;
      Iges.UndoJoin ();
      Redraw?.Invoke ();
      return errorNo;
   }

   int Join () {
      if (Iges == null) return -1;
      int errorNo;
      try {
         errorNo = Iges.UnionShapes ();
      } catch (Exception ex) {
         MessageBox.Show (ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         return 1;
      }
      //if (errorNo == 0)
      //   ConvertCadToImage (true);

      // Ensure the dialog is opened on the UI thread
      int resVal = Application.Current.Dispatcher.Invoke (() => {
         JoinResult joinResultDialog = new ();
         joinResultDialog.InitializeComponent ();
         bool? dialogResult = joinResultDialog.ShowDialog ();

         int res = 1;
         if (dialogResult == true) {
            _joinResOpt = joinResultDialog.joinResVM.Result;

            switch (_joinResOpt) {
               case JoinResultVM.JoinResultOption.SaveAndOpen:
                  res = JoinSave ();
                  if (res == 0)
                     OpenSavedFile ();
                  else return res;
                  break;
               case JoinResultVM.JoinResultOption.Save:
                  res = JoinSave ();
                  break;
               case JoinResultVM.JoinResultOption.Cancel:
               case JoinResultVM.JoinResultOption.None:
                  UndoJoin ();
                  EvRequestCloseWindow?.Invoke ();
                  return 0;
            }
         }
         joinResultDialog.Close ();
         return res;
      });
      return resVal;
   }

   void OpenSavedFile () {
      if (!string.IsNullOrEmpty (JoinedFileName)) {
         try {
            EvLoadPart?.Invoke (JoinedFileName);
         } catch (Exception ex) {
            MessageBox.Show ($"Error opening file: {ex.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }
   }

   int JoinSave () {
      if (Iges == null) return -1;
      int errorNo = 0;

      JoinedFileName = SaveFilename (Part1FileName, "Select a Part File",
          "CAD Files (*.iges;*.igs;*.stp;*.step)|*.iges;*.igs;*.stp;*.step|All Files (*.*)|*.*",
          initialDirectory);

      if (!string.IsNullOrEmpty (JoinedFileName)) {
         errorNo = Iges.SaveIGES (JoinedFileName, 2);

         string? dirName = Path.GetDirectoryName (JoinedFileName);
         if (dirName != null)
            EvMirrorAndJoinedFileSaved?.Invoke (dirName);
         else
            throw new Exception ("Directory name is null ");
      }
      return errorNo;
   }
   #endregion

   #region Helper Methods
   async Task Action (Func<int> func) {
      Mouse.OverrideCursor = Cursors.Wait;
      int errorNo = 0;

      await Task.Run (() => errorNo = func ());

      HandleIGESError (errorNo);
      Mouse.OverrideCursor = null;
   }

   bool HandleIGESError (int errorNo) {
      if (errorNo == 0 || Iges == null) return false;
      return true;
   }

   /*void ConvertCadToImage (bool fused) {
      if (Iges == null) return;
      int width = 1000, height = 1000;
      byte[] imageData = null!;
      int errorNoFused = 0, errorNoP1 = 0, errorNoP2 = 0;
      if (fused)
         errorNoFused = Iges.GetShape (2, width, height, ref imageData);
      else
         errorNoP1 = Iges.GetShape (-1, width, height, ref imageData);

      if (errorNoP1 == 0 && errorNoP2 == 0 && errorNoFused != 0) {
         if (HandleIGESError (errorNoFused))
            return;
      }
      UpdateImage (width, height, imageData);
   }

   void UpdateImage (int width, int height, byte[] imageStream) {
      if (imageStream == null || imageStream.Length == 0) return;

      WriteableBitmap bitmap = new (width, height, 96, 96, PixelFormats.Rgb24, null);
      bitmap.Lock ();
      try {
         Marshal.Copy (imageStream, 0, bitmap.BackBuffer, imageStream.Length);
         bitmap.AddDirtyRect (new Int32Rect (0, 0, width, height));
      } finally {
         bitmap.Unlock ();
      }

      ThumbnailBitmap = ConvertWriteableBitmapToBitmapImage (bitmap);
   }

   BitmapImage ConvertWriteableBitmapToBitmapImage (WriteableBitmap wbm) {
      BitmapImage bmImage = new ();
      using (MemoryStream stream = new ()) {
         PngBitmapEncoder encoder = new ();
         encoder.Frames.Add (BitmapFrame.Create (wbm));
         encoder.Save (stream);
         bmImage.BeginInit ();
         bmImage.CacheOption = BitmapCacheOption.OnLoad;
         bmImage.StreamSource = stream;
         bmImage.EndInit ();
         bmImage.Freeze ();
      }
      return bmImage;
   }*/

   string GetFilename (string fileName, string title, string filter = "All files (*.*)|*.*",
                             bool multiselect = false, string? initialFolder = null) {
      OpenFileDialog openDlg = new () {
         Title = title,
         Filter = filter,
         Multiselect = multiselect,
         InitialDirectory = initialFolder ?? @"W:\ProfileCAM\Sample",
         FileName = fileName
      };
      return openDlg.ShowDialog () == true ? openDlg.FileName : string.Empty;
   }

   string SaveFilename (string fileName, string title, string filter = "All files (*.*)|*.*",
                              string? initialFolder = null) {
      SaveFileDialog saveDlg = new () {
         Title = title,
         Filter = filter,
         InitialDirectory = initialFolder ?? @"W:\ProfileCAM\Sample",
         FileName = fileName
      };
      return saveDlg.ShowDialog () == true ? saveDlg.FileName : string.Empty;
   }
   #endregion

   #region Events
   public event Action<string>? EvMirrorAndJoinedFileSaved; // Event to notify MainWindow when a file is saved
   public event Action<string>? EvLoadPart;
   public event Action? EvRequestCloseWindow;
   public Action? Redraw;
   #endregion

   #region Properties
   [ObservableProperty]
   private string _part1FileName = "";

   [ObservableProperty]
   private string _part2FileName = "";

   [ObservableProperty]
   private string _joinedFileName = "";

   [ObservableProperty]
   private BitmapImage _thumbnailBitmap;
   #endregion

   #region Fields
   public IGES.IGES? Iges;
   bool _disposed = false;
   JoinResultVM.JoinResultOption _joinResOpt = JoinResultVM.JoinResultOption.None;
   string? initialDirectory = "W:\\ProfileCAM\\TData";
   #endregion
}