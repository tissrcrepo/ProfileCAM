using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProfileCAM.Core;
using ProfileCAM.Core.AssemblyUtils;
using ProfileCAM.Core.GCodeGen;
using ProfileCAM.Core.Processes;
using ProfileCAM.Presentation.Draw;
using ProfileCAM.Input;
using Flux.API;
using Microsoft.Win32;
using SPath = System.IO.Path;

namespace ProfileCAM.Presentation;
/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window, INotifyPropertyChanged {
   #region Fields
   Part? mPart = null;
   SimpleVM? mOverlay;
   Scene? mScene;
   Workpiece? mWork;
   List<Part> mSubParts = [];
   SettingsDlg? mSetDlg;
   GenesysHub? mGHub;
   ProcessSimulator? mProcessSimulator;
   ProcessSimulator.ESimulationStatus mSimulationStatus = ProcessSimulator.ESimulationStatus.NotRunning;
   string mSrcDir = "W:/ProfileCAM/Sample";

   [IgnoreDataMember]
   Dictionary<string, string>? mRecentFilesMap = [];
   public bool IsIgesAvailable { get; set; }
   public ProcessSimulator.ESimulationStatus SimulationStatus {
      get => mSimulationStatus;
      set {
         if (mSimulationStatus != value) {
            mSimulationStatus = value;
            OnPropertyChanged (nameof (SimulationStatus));
         }
      }
   }

   public event PropertyChangedEventHandler? PropertyChanged;
   #endregion

   #region Constructor   
   public MainWindow () {
      InitializeComponent ();
      // Initialize other components after successful drive mapping
      this.DataContext = this;

      SettingServices.It.LoadSettings (MCSettings.It);
      MCSettings.It.PropertyChanged += OnTextPosChanged;
      try {
         // Get the application directory
         // Get the default path from registry if ProfileCAM is installed
         string? localAppDir = "";
         //SettingServices.It.LoadSettings (MCSettings.It);
         //if (string.IsNullOrEmpty (MCSettings.It.WMapLocation)) {
         //   localAppDir = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);
         //   MCSettings.It.WMapLocation = localAppDir;
         //   SaveSettings ();
         //} else
         //   localAppDir = MCSettings.It.WMapLocation;

         localAppDir = GetAppSubstPath ();
         ValidateDirectory (localAppDir, createOnNoExist: true, terminateOnError: true);
         MCSettings.It.WMapLocation = localAppDir;
         SaveSettings ();
         if (localAppDir != null) {
            var fcDir = Path.Combine (localAppDir, "ProfileCAM");
            if (!Directory.Exists (fcDir))
               Directory.CreateDirectory (fcDir);

            var sampleDir = Path.Combine (fcDir, "Sample");
            if (!Directory.Exists (sampleDir))
               Directory.CreateDirectory (sampleDir);


            var dataDir = Path.Combine (fcDir, "Data");
            if (!Directory.Exists (dataDir))
               Directory.CreateDirectory (dataDir);

            bool wMapExists = false;
            try {
               if (!Directory.Exists (@"W:\")) {
                  Debug.WriteLine ("W: drive not found. Nothing to do.");
                  return;
               }

               string[] required = {
                @"W:\ProfileCAM",
                @"W:\ProfileCAM\Sample",
                @"W:\ProfileCAM\Data"
            };

               foreach (string path in required) {
                  if (!Directory.Exists (path)) {
                     Directory.CreateDirectory (path);
                     Debug.WriteLine ($"Created: {path}");
                  }
               }
               wMapExists = true;
               Debug.WriteLine ("ProfileCAM folder structure is ready on W:.");
            } catch (Exception ex) {
               Debug.WriteLine ($"ProfileCAM setup failed: {ex.Message}");
            }
            if (!wMapExists)
               MapWDrive (localAppDir);
         } else
            throw new Exception ("localAppDir is null");

         //var binInstallDir = Path.Combine (dataDir, "Bin");
         var binDir = SPath.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);
         //MessageBox.Show(binDir, "ProfileCAM Binary Directory", MessageBoxButton.OK );
         Library.Init ("W:/ProfileCAM/Data", binDir, this);
         // Uncomment if Flux.Base.dll is confirmed to be loaded
         // Flux.API.Settings.IGESviaHOOPS = false;

         Area.Child = (UIElement)Lux.CreatePanel ();
         PopulateFilesFromDir (PathUtils.ConvertToWindowsPath (mSrcDir));
      } catch (Exception ex) {
         Debug.WriteLine ($"Initialization failed: {ex.Message}");
         MessageBox.Show ($"Initialization failed: {ex.Message}",
             "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         return; // Exit constructor to prevent further initialization
      }

      Sys.SelectionChanged += OnSelectionChanged;
#if DEBUG
      IsIgesAvailable = AssemblyLoader.IsAssemblyLoadable ("igesd");
#else
      IsIgesAvailable = AssemblyLoader.IsAssemblyLoadable ("iges");
#endif

#if DEBUG
      IsSanityCheckVisible = true;
#else
      IsSanityCheckVisible = false;
#endif

#if DEBUG || TESTRELEASE
      IsTextMarkingOptionVisible = true;
#else
      IsTextMarkingOptionVisible = false;
#endif

      //// Set icon programmatically (alternative to XAML)
      //this.Icon = new BitmapImage(new Uri("pack://application:,,,/Resources/ProfileCAM.Splash.png"));
   }

   // Handler: Only reacts if MarkTextPosX changed
   void OnTextPosChanged (object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof (MCSettings.MarkTextPosX) || e.PropertyName == nameof (MCSettings.MarkTextPosY)) {
         // Your logic here, e.g., update UI with mcSettings.MarkTextPosX
         //Console.WriteLine ($"MarkTextPosX changed to: {mcSettings.MarkTextPosX}");
         _cutMarks = false;
         DrawTextMarking ();
      }
   }

   public static string? GetAppSubstPath (string driveLetter = "W:") {
      try {
         driveLetter = driveLetter.ToUpper ().TrimEnd ('\\', ':') + ":";

         var processStartInfo = new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = "/c subst",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
         };

         using (var process = Process.Start (processStartInfo)) {
            if (process == null) return null;

            string output = process.StandardOutput.ReadToEnd ();
            process.WaitForExit (5000); // 5 second timeout

            using (StringReader reader = new StringReader (output)) {
               string? line;
               while ((line = reader.ReadLine ()) != null) {
                  if (line.Trim ().StartsWith (driveLetter)) {
                     // Format: "W:\: => C:\Actual\Path"
                     var parts = line.Split (new[] { " => " }, StringSplitOptions.RemoveEmptyEntries);
                     if (parts.Length == 2) {
                        return parts[1].Trim ();
                     }
                  }
               }
            }
         }

         return null; // No mapping found
      } catch (Exception ex) {
         // You can show a MessageBox or log the error
         MessageBox.Show ($"Error getting subst path: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
         return null;
      }
   }

   public void ValidateDirectory (string? localAppDir, bool createOnNoExist = false, bool terminateOnError = true) {
      try {
         // Check if the path is null, empty, or whitespace
         if (string.IsNullOrWhiteSpace (localAppDir)) {
            throw new ArgumentException ("The directory path cannot be null or empty.", nameof (localAppDir));
         }

         // Check if the path contains invalid characters
         if (localAppDir.IndexOfAny (Path.GetInvalidPathChars ()) >= 0) {
            throw new ArgumentException ("The directory path contains invalid characters.", nameof (localAppDir));
         }

         // Optional: Check if the path is rooted (absolute path)
         if (!Path.IsPathRooted (localAppDir)) {
            throw new ArgumentException ("The directory must be an absolute path.", nameof (localAppDir));
         }

         // Check if the directory exists
         if (!Directory.Exists (localAppDir) && createOnNoExist) {
            //throw new DirectoryNotFoundException ($"The directory does not exist: {localAppDir}");
            MapWDrive (localAppDir);
         }

         // If all checks pass, the path is valid
         Console.WriteLine ($"Directory is valid: {localAppDir}");
      } catch (Exception ex) {
         // Log the error (optional)
         Console.WriteLine ($"Error: {ex.Message}");

         // Show message to user (WPF)
         MessageBox.Show ($"Application cannot start: {ex.Message}",
                        "Configuration Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

         // Exit the application
         if (terminateOnError)
            Application.Current.Shutdown (1);
         // Or: Environment.Exit(1);
      }
   }
   static void MapWDrive (string folderPath) {
      string batPath = Path.Combine (AppDomain.CurrentDomain.BaseDirectory, "MapW.bat");

      if (!Directory.Exists (folderPath)) {
         Console.WriteLine ($"Folder not found: {folderPath}");
         return;
      }

      var startInfo = new ProcessStartInfo {
         FileName = batPath,
         Arguments = $"\"{folderPath}\"",

         UseShellExecute = true,           // Required for elevation
         Verb = "runas",                   // Triggers UAC prompt
         CreateNoWindow = false,
         WindowStyle = ProcessWindowStyle.Normal
      };

      try {
         using (var process = Process.Start (startInfo)) {
            process?.WaitForExit ();
            if (process?.ExitCode == 0)
               Console.WriteLine ("W: drive mapped permanently!");
            else
               Console.WriteLine ("Failed or cancelled.");
         }
      } catch (System.ComponentModel.Win32Exception ex) {
         if (ex.NativeErrorCode == 1223)
            Console.WriteLine ("UAC was cancelled by user.");
         else
            Console.WriteLine ("Error: " + ex.Message);
      }
   }

   bool _isSanityCheckVisible;
   public bool IsSanityCheckVisible {
      get => _isSanityCheckVisible;
      set {
         if (_isSanityCheckVisible != value) {
            _isSanityCheckVisible = value;
            OnPropertyChanged ();
         }
      }
   }

   bool _isTextMarkingOptionVisible;
   public bool IsTextMarkingOptionVisible {
      get => _isTextMarkingOptionVisible;
      set {
         if (_isTextMarkingOptionVisible != value) {
            _isTextMarkingOptionVisible = value;
            OnPropertyChanged ();
         }
      }
   }

   protected void OnPropertyChanged ([CallerMemberName] string? propertyName = null)
       => PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
   void UpdateInputFilesList (List<string> files) => Dispatcher.Invoke (() => Files.ItemsSource = files);

   void PopulateFilesFromDir (string dir) {
      string? inputFileType = Environment.GetEnvironmentVariable ("FC_INPUT_FILE_TYPE");
      var fxFiles = new List<string> ();
      if (!string.IsNullOrEmpty (inputFileType) && inputFileType.ToUpper ().Equals ("FX")) {
         // Get FX files if the environment variable is set to "FX"
         fxFiles = [.. System.IO.Directory.GetFiles (dir, "*.fx").Select (System.IO.Path.GetFileName)];
      }

      // Get IGES and IGS files
      var allowedExtensions = new[] { ".iges", ".igs", ".step", ".stp", ".dxf", ".step", ".csv" };
      var igesFiles = System.IO.Directory.GetFiles (dir)
                                          .Where (file => allowedExtensions.Contains (System.IO.Path.GetExtension (file).ToLower ()))
                                          .Select (System.IO.Path.GetFileName)
                                          .ToList ();

      // Combine the two collections
      var allFiles = igesFiles.Concat (fxFiles).ToList ();

      // Assign the combined collection to ItemsSource
      UpdateInputFilesList (allFiles);
   }
   #endregion

   #region Event handlers
   void TriggerRedraw ()
      => Dispatcher.Invoke (() => mOverlay?.Redraw ());
   void ZoomWithExtents (Bound3 bound) {
      Dispatcher.Invoke (() =>
      {
         if (mScene is null)
            throw new InvalidOperationException ("mScene is not initialized.");

         mScene.Bound3 = bound;
      });
   }
   void OnSimulationFinished () {
      var simulator = mProcessSimulator
          ?? throw new InvalidOperationException ("mProcessSimulator is not initialized.");

      simulator.SimulationStatus = ProcessSimulator.ESimulationStatus.NotRunning;
   }

   void OnFileSelected (object sender, RoutedEventArgs e) {
      if (Files.SelectedItem != null)
         LoadPart (SPath.Combine (mSrcDir, (string)Files.SelectedItem));
   }

   void OnSelectionChanged (object obj) {
      Title = obj?.ToString () ?? "NONE";
      mOverlay?.Redraw ();
   }

   void OnProcessPropertyChanged (object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof (ProcessSimulator.SimulationStatus))
         OnPropertyChanged (nameof (SimulationStatus));
   }

   void OnMenuFileOpen (object sender, RoutedEventArgs e) {
      OpenFileDialog openFileDialog;
      if (string.IsNullOrEmpty (mSrcDir))
         mSrcDir = @"W:\ProfileCAM\Sample";
      string? inputFileType = Environment.GetEnvironmentVariable ("FC_INPUT_FILE_TYPE");
      if (!string.IsNullOrEmpty (inputFileType) && inputFileType.ToUpper ().Equals ("FX")) {
         openFileDialog = new () {
            Filter = "IGS Files (*.igs;*.iges)|*.igs;*.iges|STEP Files (*.stp;*.step)|*.stp;*.step|FX Files (*.fx)|*.fx|CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
            InitialDirectory = PathUtils.ConvertToWindowsPath (mSrcDir)
         };
      } else {
         if (string.IsNullOrEmpty (mSrcDir))
            mSrcDir = @"W:\ProfileCAM\Sample";
         openFileDialog = new () {
            Filter = "IGS Files (*.igs;*.iges)|*.igs;*.iges|STEP Files (*.stp;*.step)|*.stp;*.step|CSV Files (*.csv)|*.csv|All files (*.*)|*.*",
            InitialDirectory = PathUtils.ConvertToWindowsPath (mSrcDir)
         };
      }

      if (openFileDialog.ShowDialog () == true) {
         // Handle file opening, e.g., load the file into your application
         if (!string.IsNullOrEmpty (openFileDialog.FileName)) {
            LoadPart (openFileDialog.FileName);
            mSrcDir = PathUtils.ConvertToLinuxPath (Path.GetDirectoryName (openFileDialog.FileName));
         }
      }
   }


   void OnFilesHeaderItemClicked (object sender, RoutedEventArgs e) {
      //string userHomePath = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
      string fChassisFolderPath = System.IO.Path.Combine ($"W:\\", "ProfileCAM");
      string recentFilesJSONFilePath = System.IO.Path.Combine (fChassisFolderPath, "ProfileCAM.User.RecentFiles.JSON");

      mRecentFilesMap = LoadRecentFilesFromJSON (recentFilesJSONFilePath);

      // Rebuild the observable collection
      RecentFiles.Clear ();

      if (mRecentFilesMap is { Count: > 0 }) {
         static DateTimeOffset ParseTs (string ts) =>
             DateTimeOffset.TryParse (ts, out var dto) ? dto : DateTimeOffset.MinValue;

         foreach (var kv in mRecentFilesMap.OrderByDescending (kv => ParseTs (kv.Value))) {
            RecentFiles.Add ($"{kv.Key}\t\t{kv.Value}");
         }
      }
   }


   void OnRecentFileItemClick (object sender, RoutedEventArgs e) {
      if (sender is not MenuItem mi || mi.Header is not string line || string.IsNullOrWhiteSpace (line))
         return;

      const int TimestampLen = 19; // "yyyy-MM-dd HH:mm:ss"
      string path = line;

      if (line.Length >= TimestampLen + 1) // +1 for the separating space
      {
         // drop the timestamp and the preceding space
         path = line[..^(TimestampLen + 1)];
      }

      path = path.Trim ();

      if (!File.Exists (path)) {
         MessageBox.Show ($"File not found:\n{path}", "Open Recent", MessageBoxButton.OK, MessageBoxImage.Warning);
         return;
      }

      LoadPart (path);
   }

   void OnMenuDirOpen (object sender, RoutedEventArgs e) {
      var dlg = new FolderPicker {
         InputPath = PathUtils.ConvertToWindowsPath (mSrcDir),
      };
      if (dlg.ShowDialog () == true) {
         mSrcDir = dlg.ResultPath;
         PopulateFilesFromDir (mSrcDir);
      }
   }

   void OnMenuImportFile (object sender, RoutedEventArgs e) {
      if (string.IsNullOrEmpty (mSrcDir))
         mSrcDir = @"W:\ProfileCAM\Sample";
      OpenFileDialog openFileDialog = new () {
         Filter = "GCode Files (*.din)|*.din|All files (*.*)|*.*",
         InitialDirectory = PathUtils.ConvertToWindowsPath (mSrcDir)
      };
      if (openFileDialog.ShowDialog () == true) {

         // Handle file opening, e.g., load the file into your application
         if (!string.IsNullOrEmpty (openFileDialog.FileName)) {
            var extension = SPath.GetExtension (openFileDialog.FileName).ToLower ();
            if (extension == ".din")
               LoadGCode (openFileDialog.FileName);
         }
      }
   }

   void OnJoin (object sender, RoutedEventArgs e) {
      JoinWindow joinWindow = new ();

      // Subscribe to the FileSaved event
      joinWindow.joinWndVM.EvMirrorAndJoinedFileSaved += OnMirrorAndJoinedFileSaved;
      joinWindow.joinWndVM.EvLoadPart += LoadPart;

      joinWindow.ShowDialog ();
      joinWindow.Dispose ();
   }

   void OnMirrorAndJoinedFileSaved (string savedDirectory) {
      // Check if the saved file's directory matches MainWindow's mSrcDir
      if (string.Equals (System.IO.Path.GetFullPath (savedDirectory), System.IO.Path.GetFullPath (mSrcDir), StringComparison.OrdinalIgnoreCase)) {
         // Refresh file list
         PopulateFilesFromDir (mSrcDir);
      }
   }

   void OnMenuFileSave (object sender, RoutedEventArgs e) {
      SaveFileDialog saveFileDialog = new () {
         Filter = "FX files (*.fx)|*.fx|All files (*.*)|*.*",
         DefaultExt = "fx",
         FileName = System.IO.Path.GetFileName (mPart.Info.FileName),
      };

      bool? result = saveFileDialog.ShowDialog ();
      if (result == true) {
         string filePath = saveFileDialog.FileName;
         try {
            mPart.SaveFX (filePath);
         } catch (Exception ex) {
            MessageBox.Show ("Error: Could not write file to disk. Original error: " + ex.Message);
         }
      }
   }

   void OnFileClose (object sender, RoutedEventArgs e) {
      WriteRecentFiles ();
      if (Work != null) {
         if (mProcessSimulator != null) {
            if (mProcessSimulator.SimulationStatus == ProcessSimulator.ESimulationStatus.Running ||
            mProcessSimulator.SimulationStatus == ProcessSimulator.ESimulationStatus.Paused) mProcessSimulator.Stop ();
         }

         Work = null;
         Lux.UIScene = null;
         mOverlay = null;
      }

      Files.SelectedItem = null;
      CurrentFile = null;
   }

   void OnSettings (object sender, RoutedEventArgs e) {
      mSetDlg = new (MCSettings.It);
      mSetDlg.OnOkAction += () => { if (mSetDlg.IsModified) SaveSettings (); };
      mSetDlg.ShowDialog ();
   }

   void OnAboutClick (object sender, RoutedEventArgs e) {
      AboutWindow aboutWindow = new () {
         Owner = this
      };
      aboutWindow.InitializeComponent ();
      aboutWindow.ShowDialog ();
   }

   void OnWindowLoaded (object sender, RoutedEventArgs e) {
      GenesysHub = new ();
      mProcessSimulator = new (mGHub, this.Dispatcher);
      mProcessSimulator.TriggerRedraw += TriggerRedraw;
      mProcessSimulator.SetSimulationStatus += status => SimulationStatus = status;
      //mProcessSimulator.zoomExtentsWithBound3Delegate += bound => Dispatcher.Invoke (() => ZoomWithExtents (bound));

      //SettingServices.It.LoadSettings (MCSettings.It);
      if (String.IsNullOrEmpty (MCSettings.It.NCFilePath))
         MCSettings.It.NCFilePath = mGHub?.Workpiece?.NCFilePath ?? "";
   }

   void OnSanityCheck (object sender, RoutedEventArgs e) {
      mGHub.ResetGCodeGenForTesting ();
      SanityTestsDlg sanityTestsDlg = new (mGHub);
      sanityTestsDlg.ShowDialog ();
   }

   protected override void OnClosing (CancelEventArgs e) {
      try {
         // ~/ProfileCAM
         //string userHomePath = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
         string fChassisFolderPath = Path.Combine ($"W:\\", "ProfileCAM");
         Directory.CreateDirectory (fChassisFolderPath);

         // Settings JSON
         string settingsFilePath = Path.Combine (fChassisFolderPath, "ProfileCAM.User.Settings.JSON");

         string envVariable = Environment.GetEnvironmentVariable ("__FC_AUTH__");
         Guid expectedGuid = new ("e96e66ff-17e6-49ac-9fe1-28bb45a6c1b9");
#if DEBUG || TESTRELEASE
         MCSettings.It.SaveSettingsToJsonASCII (settingsFilePath);
#else
         if (!string.IsNullOrEmpty (envVariable) && Guid.TryParse (envVariable, out Guid currentGuid) && currentGuid == expectedGuid)
            MCSettings.It.SaveSettingsToJsonASCII (settingsFilePath);
         else
            MCSettings.It.SaveSettingsToJson (settingsFilePath);
#endif

         WriteRecentFiles ();

         System.Diagnostics.Debug.WriteLine ($"Settings file created at: {settingsFilePath}");
      } catch (Exception ex) {
         // Don’t block app shutdown on save errors
         System.Diagnostics.Debug.WriteLine ($"Error saving on close: {ex}");
      }

      base.OnClosing (e);
   }

   void WriteRecentFiles (string? file = null) {
      try {
         //string userHomePath = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
         string fChassisFolderPath = Path.Combine ($"W:\\", "ProfileCAM");
         Directory.CreateDirectory (fChassisFolderPath);
         string recentFilesJSONPath = System.IO.Path.Combine (fChassisFolderPath, "ProfileCAM.User.RecentFiles.JSON");
         string timeStamp = DateTime.Now.ToString ("yyyy-MM-dd HH:mm:ss");
         if (!string.IsNullOrEmpty (file)) {
            mRecentFilesMap[PathUtils.ConvertToWindowsPath (file, isFile: true)] = timeStamp;
         } else if (mPart != null && mPart.Info != null && !string.IsNullOrEmpty (mPart.Info.FileName))
            mRecentFilesMap[PathUtils.ConvertToWindowsPath (mPart.Info.FileName, isFile: true)] = timeStamp;

         // The old recent files ProfileCAM.User.RecentFiles.JSON
         // should be concatanated with new one mRecentFilesMap.
         // If no file was ever opened, the old recent files will be overwritten 
         // with nothing. This has to be avoided

         if (mRecentFilesMap != null) {
            var oldRecentFiles = LoadRecentFilesFromJSON (recentFilesJSONPath);
            mRecentFilesMap = mRecentFilesMap
             .Concat (oldRecentFiles)
             .GroupBy (kvp => kvp.Key)
             .ToDictionary (
                 g => g.Key,
                 g => g.Max (kvp => kvp.Value)  // Keeps the LATEST timestamp
             );

            TrimRecentFilesMap (mRecentFilesMap);

            // Recent files JSON (MCSettings manages mRecentFilesMap internally)
            SaveRecentFilesToJSON (mRecentFilesMap, recentFilesJSONPath);
         }
      } catch (Exception) {
      }
   }

   static void TrimRecentFilesMap (Dictionary<string, string> map) {
      const int MaxEntries = 30;

      if (map == null || map.Count <= MaxEntries)
         return;

      static DateTimeOffset ParseTs (string ts) =>
          DateTimeOffset.TryParse (ts, out var dto) ? dto : DateTimeOffset.MinValue;

      map = map
          .OrderByDescending (kv => ParseTs (kv.Value))   // newest first
          .Take (MaxEntries)                             // keep only top 30
          .ToDictionary (kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
   }
   #endregion

   #region Properties
   string _currentFile = "";
   public string CurrentFile {
      get => _currentFile;
      set {
         _currentFile = value;
         OnPropertyChanged ();
         OnPropertyChanged (nameof (WindowTitle)); // <-- This updates the title
      }
   }

   public string WindowTitle => string.IsNullOrEmpty (CurrentFile)
       ? "Profile CAM"
       : $"Profile CAM :: {CurrentFile}";


   public ObservableCollection<string> RecentFiles { get; set; } = [];

   public Workpiece? Work {
      get => mWork;
      set {
         // Prevent null from propagating to mGHub if it's non-nullable
         ArgumentNullException.ThrowIfNull (mGHub);
         if (mWork != value) {
            mWork = value;
            mGHub.Workpiece = value;        // Still requires mGHub.Workpiece to accept null
            OnPropertyChanged (nameof (Work));
         }
      }
   }

   public GenesysHub? GenesysHub {
      get => mGHub;
      set {
         if (mGHub != value)  // Check if value is different
         {
            if (mGHub != null) {
               mGHub.PropertyChanged -= OnProcessPropertyChanged;
            }

            mGHub = value;  // Ensure the new value is assigned

            if (mGHub != null) {
               mGHub.PropertyChanged += OnProcessPropertyChanged;
            }

            OnPropertyChanged (nameof (GenesysHub));
            OnPropertyChanged (nameof (SimulationStatus));
         }
      }
   }

   public ProcessSimulator? ProcessSimulator {
      get => mProcessSimulator;
      set {
         if (mProcessSimulator != value) {
            if (mProcessSimulator != null) {
               mProcessSimulator.PropertyChanged -= OnProcessPropertyChanged;
               mProcessSimulator = value;
               if (mProcessSimulator != null) {
                  mProcessSimulator.PropertyChanged += OnProcessPropertyChanged;
               }

               OnPropertyChanged (nameof (mProcessSimulator));
               OnPropertyChanged (nameof (SimulationStatus));
            }
         }
      }
   }
   #endregion Properties

   #region Draw Related Methods
   void DrawOverlay () {
      DrawTooling ();
      if (mProcessSimulator?.SimulationStatus == ProcessSimulator.ESimulationStatus.Running
         || mProcessSimulator?.SimulationStatus == ProcessSimulator.ESimulationStatus.Paused)
         mProcessSimulator.DrawGCodeForCutScope ();
      else
         mProcessSimulator?.DrawGCode ();
      mProcessSimulator?.DrawToolInstance ();
   }

   void DrawTooling () {
      if (mProcessSimulator?.SimulationStatus == ProcessSimulator.ESimulationStatus.NotRunning
         || mProcessSimulator?.SimulationStatus == ProcessSimulator.ESimulationStatus.Paused) {
         Lux.HLR = false;
         Lux.Color = new Color32 (255, 255, 0);
         switch (Sys.Selection) {
            case E3Plane ep:
               Lux.Draw (EMarker2D.CSMarker, ep.Xfm.ToCS (), 25);
               break;

            case E3Flex ef:
               Lux.Draw (EMarker2D.CSMarker, ef.Socket, 25);
               break;

            default:
               if (Work != null) {
                  if (Work.Cuts.Count == 0)
                     Lux.Draw (EMarker2D.CSMarker, CoordSystem.World, 25);
               }
               break;
         }

         // Draw LH and RH coordinate systems
         if (Work != null) {
            foreach (var cut in Work.Cuts) {
               if (cut.Head == 0)
                  cut.DrawSegs (Utils.LHToolColor, 10);
               else if (cut.Head == 1)
                  cut.DrawSegs (Utils.RHToolColor, 10);
               else
                  cut.DrawSegs (Color32.Yellow, 10);

               if (MCSettings.It.ShowToolingNames) {
                  // Draw the tool names
                  var tName = cut.Name;
                  var pt = cut.Segs[0].Curve.Start;
                  Lux.Color = new Color32 (128, 0, 128);
                  Lux.DrawBillboardText (tName, pt, (float)12);
               }
               if (MCSettings.It.ShowToolingExtents) {
                  // Draw the tool extents
                  var tXMin = $"{cut.XMin:F2}"; var tXMax = $"{cut.XMax:F2}";
                  var ptXMin = new Point3 (cut.XMin, cut.Segs[0].Curve.Start.Y, cut.Segs[0].Curve.Start.Z + 5);
                  var ptXMax = new Point3 (cut.XMax, cut.Segs[0].Curve.Start.Y, cut.Segs[0].Curve.Start.Z + 5);
                  Lux.Color = new Color32 (128, 0, 128);
                  Lux.DrawBillboardText (tXMin, ptXMin, (float)12);
                  Lux.DrawBillboardText (tXMax, ptXMax, (float)12);
               }
            }
         }
      }
   }
   #endregion

   #region Part Preparation Methods
   void LoadPart (string file) {
      // 1. Show busy cursor ONCE at start
      this.Cursor = Cursors.Wait;
      this.ForceCursor = true;
      // Force immediate UI update
      this.Dispatcher.Invoke (() => { }, DispatcherPriority.Background);

      try {
         WriteRecentFiles (file);
         var windowsFile = file;
         VerifyFluxAssemblies ();
         file = file.Replace ('\\', '/');

         // CSV → DXF conversion
         bool isCsv = Path.GetExtension (file).Equals (".csv", StringComparison.OrdinalIgnoreCase);
         if (isCsv) {
            var origCSVFile = file;
            var csvPartData = CsvReader.ReadPartData (file);
            file += ".dxf";
            new ProfileCAM.Input.DXFWriter (file, csvPartData).WriteDXF ();
            PopulateFilesFromDir (mSrcDir);
         }

         // Main heavy load
         mPart = Part.Load (file);
         mPart.Info.FileName = file;
         if (mPart.Info.MatlName == "NONE")
            mPart.Info.MatlName = "1.0038";

         if (mPart.Model == null) {
            if (mPart.Dwg != null)
               mPart.FoldTo3D ();
            else if (mPart.SurfaceModel != null)
               mPart.SheetMetalize ();
            else
               throw new Exception ("Invalid part");
         }

         if (mPart.Model == null)
            throw new InvalidOperationException ("Part model is null after loading");

         mOverlay = new SimpleVM (DrawOverlay);
         Lux.UIScene = mScene = new Scene (
             new GroupVModel (VModel.For (mPart.Model), mOverlay),
             mPart.Model.Bound);

         Work = new Workpiece (mPart.Model, mPart);
         CurrentFile = windowsFile;
         GenesysHub?.ClearZombies ();
      } catch (Exception ex) {
         string msg = ex is NullReferenceException || ex.Message.Contains ("invalid", StringComparison.OrdinalIgnoreCase)
             ? $"Part {Path.GetFileName (file)} is invalid"
             : $"Failed to load part: {ex.Message}";

         MessageBox.Show (msg, "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
      } finally {
         // 2. Restore cursor ONCE at the end — guaranteed
         this.Dispatcher.Invoke (() => {
            this.Cursor = Cursors.Arrow;
            this.ForceCursor = true;
         });
      }
   }
   bool _cutHoles = false, _cutNotches = false, _cutMarks = false;
   void DoAlign (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece ()) {
         if ( Work == null )
            throw new Exception ("Work is set null");

         Work.Align ();
         if (mScene != null)
            mScene.Bound3 = Work.Model.Bound;
         GenesysHub?.ClearZombies ();
         if (Work.Dirty) {
            Work.DeleteCuts ();
            _cutHoles = false; _cutNotches = false;
            _cutMarks = false;
         }
         mOverlay?.Redraw ();
         GCodeGenerator.EvaluateToolConfigXForms (Work);
      }
   }

   void DoAddHoles (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece () && !_cutHoles) {
         if (Work != null &&  Work.DoAddHoles ())
            GenesysHub?.ClearZombies ();
         _cutHoles = true;
         mOverlay?.Redraw ();
      }
   }

   void DoTextMarking (object sender, RoutedEventArgs e) => DrawTextMarking ();

   void DrawTextMarking () {
      if (!HandleNoWorkpiece () && !_cutMarks && Work != null) {
         if (Work.DoTextMarking (MCSettings.It))
            GenesysHub?.ClearZombies ();
         _cutMarks = true;
         mOverlay?.Redraw ();
      }
   }

   void DoCutNotches (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece() && !_cutNotches && Work != null) {
         if (Work.DoCutNotchesAndCutouts ())
            GenesysHub?.ClearZombies ();
         _cutNotches = true;
         mOverlay?.Redraw ();
      }
   }
   void DoRefresh (object sender, RoutedEventArgs e) => mOverlay?.Redraw ();
   void DoSorting (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece () && Work != null) {
         Work.DoSorting ();
         mOverlay?.Redraw ();
      }
   }

   void DoGenerateGCode (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece ()) {
         // Set busy cursor at start
         this.Cursor = Cursors.Wait;
         this.ForceCursor = true;

         // Force immediate UI update to show busy cursor
         this.Dispatcher.Invoke (() => { }, DispatcherPriority.Background);

#if DEBUG || TESTRELEASE
         try {
            if (GenesysHub == null)
               throw new Exception ("Genesyshub is null");
            GenesysHub.ComputeGCode ();
         } catch (Exception) {
            // 2. Restore cursor ONCE at the end — guaranteed
            this.Dispatcher.Invoke (() => {
               this.Cursor = Cursors.Arrow;
               this.ForceCursor = true;
            });
            throw;
         } finally {
            this.Dispatcher.Invoke (() => {
               this.Cursor = Cursors.Arrow;
               this.ForceCursor = true;
            });
         }
#else
         try {
            GenesysHub.ComputeGCode ();
         } catch (InfeasibleCutoutException ex) {
            MessageBox.Show (ex.Message, "Error",
                          MessageBoxButton.OK, MessageBoxImage.Error);
         } catch (Exception ex) {
            if (ex is NegZException)
               MessageBox.Show ("Part might not be aligned", "Error",
                          MessageBoxButton.OK, MessageBoxImage.Error);
            else if (ex is NotchCreationFailedException ex1)
               MessageBox.Show (ex1.Message, "Error",
                          MessageBoxButton.OK, MessageBoxImage.Error);
            else
               MessageBox.Show ($"G Code generation failed{ex.Message}", "Error",
                          MessageBoxButton.OK, MessageBoxImage.Error);
         } finally {
            Work.Dirty = false; // This will always execute

            // 2. Restore cursor ONCE at the end — guaranteed
            this.Dispatcher.Invoke (() => {
               this.Cursor = Cursors.Arrow;
               this.ForceCursor = true;
            });
         }
#endif
         mOverlay?.Redraw ();
      }
   }
   #endregion

   #region Simulation Related Methods
   void Simulate (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece () && ProcessSimulator != null ) {
         ProcessSimulator.SimulationFinished += OnSimulationFinished;
         Task.Run (ProcessSimulator.Run);
      }
   }

   void PauseSimulation (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece () && ProcessSimulator != null)
         ProcessSimulator.Pause ();
   }

   void StopSimulation (object sender, RoutedEventArgs e) {
      if (!HandleNoWorkpiece() && ProcessSimulator != null)
         ProcessSimulator.Stop ();
   }
   #endregion

   #region Actionable Methods
   bool HandleNoWorkpiece () {
      if (Work == null) {
         MessageBox.Show ("No Part is Loaded.", "Error",
                           MessageBoxButton.OK, MessageBoxImage.Error);
         return true;
      }
      return false;
   }

   void SaveSettings ()
     => SettingServices.It.SaveSettings (MCSettings.It);

   void LoadGCode (string filename) {
      try {
         if (GenesysHub == null)
            throw new Exception ("Genesyshub is null");
         GenesysHub.LoadGCode (filename);
      } catch (Exception ex) {
         MessageBox.Show (ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
   }

   void OpenDINsClick (object sender, RoutedEventArgs e) {
      if (Files.SelectedItem is string selectedFile)
         OpenDinsForFile (selectedFile);
   }

   void OpenDinsForFile (string selectedFile) {
      string dinFileNameH1 = "", dinFileNameH2 = "";
      try {
         string? editor = null;

         var pathsEnv = Environment.GetEnvironmentVariable ("PATH");
         if (!string.IsNullOrEmpty (pathsEnv)) {
            var paths = pathsEnv.Split (';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            string? npp = paths.Select (p => Path.Combine (p, "notepad++.exe"))
                               .FirstOrDefault (File.Exists);

            string? np = paths.Select (p => Path.Combine (p, "notepad.exe"))
                              .FirstOrDefault (File.Exists);

            editor = npp ?? np;
         }

         if (editor == null) {
            MessageBox.Show ("Neither Notepad++ nor Notepad was found in the system PATH.", "Error",
               MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }

         // Construct the DIN file paths using the selected file name
         string dinFileSuffix = string.IsNullOrEmpty (MCSettings.It.DINFilenameSuffix) ? "" : $"-{MCSettings.It.DINFilenameSuffix}-";
         dinFileNameH1 = $@"{Utils.RemoveLastExtension (selectedFile)}-{1}{dinFileSuffix}({(MCSettings.It.PartConfig == MCSettings.PartConfigType.LHComponent ? "LH" : "RH")}).din";
         dinFileNameH1 = System.IO.Path.Combine (MCSettings.It.NCFilePath, "Head1", dinFileNameH1);
         dinFileNameH2 = $@"{Utils.RemoveLastExtension (selectedFile)}-{2}{dinFileSuffix}({(MCSettings.It.PartConfig == MCSettings.PartConfigType.LHComponent ? "LH" : "RH")}).din";
         dinFileNameH2 = System.IO.Path.Combine (MCSettings.It.NCFilePath, "Head2", dinFileNameH2);

         if (!File.Exists (dinFileNameH1)) throw new Exception ($"\nFile: {dinFileNameH1} does not exist.\nGenerate G Code first");
         if (!File.Exists (dinFileNameH2)) throw new Exception ($"\nFile: {dinFileNameH2} does not exist.\nGenerate G Code first");

         // Open the files
         Process.Start (new ProcessStartInfo (editor, $"\"{dinFileNameH1}\"") { UseShellExecute = true });
         Process.Start (new ProcessStartInfo (editor, $"\"{dinFileNameH2}\"") { UseShellExecute = true });
      } catch (Exception ex) {
         MessageBox.Show ($"Error opening DIN files: {ex.Message}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
   }
   #endregion
   #region JSON readers/writers

   // --------------------------------------------------------------------
   // Saves mRecentFilesMap to JSON (keeps latest 30 by timestamp)
   // --------------------------------------------------------------------
   public static void SaveRecentFilesToJSON (Dictionary<string, string> map, string jsonFileName) {
      const int MaxEntries = 30;

      if (string.IsNullOrWhiteSpace (jsonFileName))
         throw new ArgumentException ("jsonFileName must be a non-empty path.", nameof (jsonFileName));

      // Ensure directory exists
      var dir = Path.GetDirectoryName (jsonFileName);
      if (!string.IsNullOrEmpty (dir) && !Directory.Exists (dir))
         Directory.CreateDirectory (dir);

      // Ensure map exists
      map ??= new Dictionary<string, string> (StringComparer.Ordinal);

      // Trim to newest 30 by timestamp
      static DateTimeOffset ParseTs (string ts) =>
         DateTimeOffset.TryParse (ts, out var dto) ? dto : DateTimeOffset.MinValue;

      if (map.Count > MaxEntries) {
         map = map
            .OrderByDescending (kv => ParseTs (kv.Value))
            .Take (MaxEntries)
            .ToDictionary (kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
      }

      var jsonOptions = new JsonSerializerOptions {
         Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
         WriteIndented = true
      };

      var jsonOut = JsonSerializer.Serialize (map, jsonOptions);

      // Persist as ASCII (non-ASCII -> '?') to match your existing convention
      var asciiBytes = Encoding.ASCII.GetBytes (jsonOut);
      File.WriteAllBytes (jsonFileName, asciiBytes);
   }

   static List<string> DescendingOrderMap (Dictionary<string, string> map) {
      List<string> recFiles = [];
      if (map != null) {
         // Keep newest first (by timestamp)
         static DateTimeOffset ParseTs (string ts)
             => DateTimeOffset.TryParse (ts, out var dto) ? dto : DateTimeOffset.MinValue;

         foreach (var kv in map.OrderByDescending (kv => ParseTs (kv.Value))) {
            recFiles.Add ($"{kv.Key} {kv.Value}");
         }
      }
      return recFiles;
   }

   public static Dictionary<string, string>? LoadRecentFilesFromJSON (string jsonFileName) {

      Dictionary<string, string>? map = [], recentFiles = [];
      if (string.IsNullOrWhiteSpace (jsonFileName))
         throw new ArgumentException ("jsonFileName must be a non-empty path.", nameof (jsonFileName));

      if (!File.Exists (jsonFileName))
         return map;

      try {
         var bytes = File.ReadAllBytes (jsonFileName);
         if (bytes.Length == 0)
            return map;

         var json = Encoding.UTF8.GetString (bytes);
         map = JsonSerializer.Deserialize<Dictionary<string, string>> (json);
         if (map != null) {
            static DateTimeOffset ParseTs (string ts)
                => DateTimeOffset.TryParse (ts, out var dto) ? dto : DateTimeOffset.MinValue;
            foreach (var kv in map.OrderByDescending (kv => ParseTs (kv.Value)))
               recentFiles[PathUtils.ConvertToWindowsPath (kv.Key, isFile: true)] = kv.Value;

         }
         //if (map != null) {
         //   // Keep newest first (by timestamp)
         //   static DateTimeOffset ParseTs (string ts)
         //       => DateTimeOffset.TryParse (ts, out var dto) ? dto : DateTimeOffset.MinValue;

         //   foreach (var kv in map.OrderByDescending (kv => ParseTs (kv.Value))) {
         //      RecentFiles.Add ($"{kv.Key} {kv.Value}");
         //   }
         //}
      } catch {
         // swallow exceptions -> return whatever was parsed so far (empty on error)
      }

      return recentFiles;
   }


   #endregion

   void VerifyFluxAssemblies () {
      try {
         Console.WriteLine ("=== Flux Assembly Dependency Analysis ===");
         Console.WriteLine ($"Current Domain: {AppDomain.CurrentDomain.FriendlyName}");

         // Get all loaded assemblies containing "Flux"
         var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies ()
             .Where (a => a.FullName?.Contains ("Flux") == true ||
                        a.GetName ().Name?.StartsWith ("Flux") == true)
             .ToList ();

         Console.WriteLine ($"\nFound {loadedAssemblies.Count} Flux-related assemblies:");
         foreach (var assembly in loadedAssemblies) {
            Console.WriteLine ($"  - {assembly.GetName ().Name} v{assembly.GetName ().Version}");
         }

         // Check Flux.API specifically
         var fluxApiAssembly = loadedAssemblies.FirstOrDefault (a => a.GetName ().Name == "Flux.API");

         if (fluxApiAssembly == null) {
            Console.WriteLine ("✗ Flux.API assembly not loaded");
            return;
         }

         Console.WriteLine ($"\n--- Flux.API Assembly Info ---");
         Console.WriteLine ($"Location: {fluxApiAssembly.Location}");
         Console.WriteLine ($"FullName: {fluxApiAssembly.FullName}");

         // Try to get referenced assemblies to see dependencies
         try {
            var referencedAssemblies = fluxApiAssembly.GetReferencedAssemblies ();
            Console.WriteLine ($"\nReferenced assemblies by Flux.API:");
            foreach (var refAssembly in referencedAssemblies) {
               var loadedRef = loadedAssemblies.FirstOrDefault (a => a.FullName == refAssembly.FullName);
               Console.WriteLine ($"  - {refAssembly.Name} v{refAssembly.Version} {(loadedRef != null ? "✓ LOADED" : "✗ MISSING")}");

               if (loadedRef == null) {
                  // Try to find this missing assembly in the Flux SDK directory
                  string? sdkPath = SPath.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);
                  if (sdkPath == null) throw new Exception ("One or more 3rd party libraries are missing");
                  var possiblePaths = new[]
                  {  
                        Path.Combine(sdkPath, $"{refAssembly.Name}.dll"),
                        Path.Combine(sdkPath, $"{refAssembly.Name}.exe"),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{refAssembly.Name}.dll")
                    };

                  foreach (var path in possiblePaths) {
                     if (File.Exists (path)) {
                        Console.WriteLine ($"    Found at: {path}");
                        try {
                           var loaded = Assembly.LoadFrom (path);
                           Console.WriteLine ($"    ✓ Successfully loaded from {path}");
                        } catch (Exception loadEx) {
                           Console.WriteLine ($"    ✗ Failed to load: {loadEx.Message}");
                        }
                        break;
                     }
                  }
               }
            }
         } catch (Exception ex) {
            Console.WriteLine ($"Error getting referenced assemblies: {ex.Message}");
         }

         // Safe way to check for Part type without triggering ExportedTypes exception
         Console.WriteLine ($"\n--- Checking for Part Type (Safe Method) ---");

         // Method 1: Try GetType without triggering full assembly load
         try {
            var partType = fluxApiAssembly.GetType ("Flux.API.Part");
            Console.WriteLine ($"fluxApiAssembly.GetType('Flux.API.Part'): {(partType != null ? "✓ FOUND" : "✗ NOT FOUND")}");

            if (partType != null) {
               // Check for Load method
               var loadMethod = partType.GetMethod ("Load", BindingFlags.Public | BindingFlags.Static, null, [typeof (string)], null);
               Console.WriteLine ($"Load method: {(loadMethod != null ? "✓ FOUND" : "✗ NOT FOUND")}");
            }
         } catch (Exception ex) {
            Console.WriteLine ($"Error getting Part type: {ex.GetType ().Name}: {ex.Message}");
         }

         // Method 2: Try Type.GetType
         try {
            var partType = Type.GetType ("Flux.API.Part, Flux.API");
            Console.WriteLine ($"Type.GetType('Flux.API.Part, Flux.API'): {(partType != null ? "✓ FOUND" : "✗ NOT FOUND")}");
         } catch (Exception ex) {
            Console.WriteLine ($"Error with Type.GetType: {ex.GetType ().Name}: {ex.Message}");
         }

         // Method 3: Search all types in all loaded Flux assemblies (safe approach)
         Console.WriteLine ($"\n--- Searching for 'Part' in all loaded Flux assemblies ---");
         foreach (var assembly in loadedAssemblies) {
            try {
               // Get types that don't trigger full assembly load
               var types = assembly.GetTypes ();
               var partTypes = types.Where (t => t.Name.Contains ("Part", StringComparison.OrdinalIgnoreCase)).ToList ();

               if (partTypes.Count > 0) {
                  Console.WriteLine ($"Found in {assembly.GetName ().Name}:");
                  foreach (var type in partTypes) {
                     Console.WriteLine ($"  - {type.FullName}");
                  }
               }
            } catch (ReflectionTypeLoadException rtle) {
               Console.WriteLine ($"✗ Could not load types from {assembly.GetName ().Name} due to missing dependencies:");
               foreach (var loaderEx in rtle.LoaderExceptions) {
                  if (loaderEx is FileNotFoundException fileEx) {
                     Console.WriteLine ($"    - Missing: {fileEx.FileName}");
                  } else {
                     Console.WriteLine ($"    - Error: {loaderEx?.Message}");
                  }
               }
            } catch (Exception ex) {
               Console.WriteLine ($"Error examining {assembly.GetName ().Name}: {ex.GetType ().Name}: {ex.Message}");
            }
         }

         // Check what's actually in the Flux SDK directory
         Console.WriteLine ($"\n--- Contents of Flux SDK Directory (C:\\FluxSDK\\Bin\\) ---");
         try {
            var sdkPath = @"C:\FluxSDK\Bin\";
            if (Directory.Exists (sdkPath)) {
               var dllFiles = Directory.GetFiles (sdkPath, "*.dll");
               var exeFiles = Directory.GetFiles (sdkPath, "*.exe");

               Console.WriteLine ($"DLL files: {dllFiles.Length}");
               foreach (var file in dllFiles.OrderBy (f => f)) {
                  Console.WriteLine ($"  - {Path.GetFileName (file)}");
               }

               Console.WriteLine ($"EXE files: {exeFiles.Length}");
               foreach (var file in exeFiles.OrderBy (f => f)) {
                  Console.WriteLine ($"  - {Path.GetFileName (file)}");
               }
            } else {
               Console.WriteLine ($"✗ Flux SDK directory not found: {sdkPath}");
            }
         } catch (Exception ex) {
            Console.WriteLine ($"Error reading SDK directory: {ex.Message}");
         }

      } catch (Exception ex) {
         Console.WriteLine ($"Diagnostic error: {ex.GetType ().Name}: {ex.Message}");
         Console.WriteLine ($"Stack trace: {ex.StackTrace}");
      }
   }

   //void InitializeDriveMapping () {
   //   try {
   //      // 1. Check if W: is already mapped with required structure
   //      if (DriveMapper.IsDriveMapped ("W:")) {
   //         string mappedPath = DriveMapper.GetMappedPath ("W:");
   //         if (!string.IsNullOrEmpty (mappedPath) && DriveMapper.HasRequiredFolderStructure (mappedPath)) {
   //            mSrcDir = "W:/ProfileCAM/Sample";
   //            PopulateFilesFromDir (PathUtils.ConvertToWindowsPath (mSrcDir));
   //            var dir = Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);
   //            Library.Init ("W:/ProfileCAM/Data", dir, this);
   //            return;
   //         } else {
   //            DriveMapper.UnmapDrive ("W:");
   //         }
   //      }

   //      // 2. Get default folder from registry
   //      string defaultInstallPath = GetInstallLocationFromRegistry ();
   //      string defaultMapPath = defaultInstallPath != null ? Path.Combine (defaultInstallPath, "Map") : null;

   //      System.Diagnostics.Debug.WriteLine ($"Registry InstallPath: {defaultInstallPath}");
   //      System.Diagnostics.Debug.WriteLine ($"Default MapPath: {defaultMapPath}");

   //      // 3. Create and configure FolderPicker
   //      var dlg = new FolderPicker {
   //         Title = "Select Folder to Map to W: Drive",
   //         OkButtonLabel = "Select Folder",
   //         ForceFileSystem = true
   //      };

   //      if (!string.IsNullOrEmpty (defaultMapPath) && Directory.Exists (defaultMapPath)) {
   //         dlg.InputPath = defaultMapPath;
   //         System.Diagnostics.Debug.WriteLine ($"Setting InputPath to: {defaultMapPath}");
   //      } else {
   //         string fallbackPath = Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments);
   //         if (Directory.Exists (fallbackPath)) {
   //            dlg.InputPath = fallbackPath;
   //            System.Diagnostics.Debug.WriteLine ($"Fallback InputPath to: {fallbackPath}");
   //         } else {
   //            System.Diagnostics.Debug.WriteLine ("No valid fallback path found");
   //         }
   //      }

   //      // 4. Show dialog with explicit owner window
   //      bool? dialogResult = null;
   //      Application.Current.Dispatcher.Invoke (() => {
   //         dialogResult = dlg.ShowDialog (this, throwOnError: true); // Enable throwOnError for debugging
   //      });

   //      System.Diagnostics.Debug.WriteLine ($"Dialog result: {dialogResult}");

   //      if (dialogResult == true) {
   //         string selectedPath = dlg.ResultPath;
   //         System.Diagnostics.Debug.WriteLine ($"Selected path: {selectedPath}");

   //         // 5. Map W: to user selected folder
   //         bool success = DriveMapper.MapDrive ("W:", selectedPath, true);

   //         if (success) {
   //            MessageBox.Show ($"Successfully mapped W: drive to {selectedPath}\n" +
   //                            $"ProfileCAM/Sample/Data folders created/verified.",
   //                "Drive Mapping", MessageBoxButton.OK, MessageBoxImage.Information);

   //            mSrcDir = "W:/ProfileCAM/Sample";
   //            PopulateFilesFromDir (PathUtils.ConvertToWindowsPath (mSrcDir));
   //            var dir = Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);
   //            Library.Init ("W:/ProfileCAM/Data", dir, this);
   //         } else {
   //            MessageBox.Show ("Failed to map W: drive. Please try again.",
   //                "Drive Mapping Failed", MessageBoxButton.OK, MessageBoxImage.Error);
   //         }
   //      } else {
   //         System.Diagnostics.Debug.WriteLine ("Dialog was cancelled or failed to show");
   //         MessageBox.Show ("Drive mapping is required. Please restart and select a folder.",
   //             "Drive Mapping Required", MessageBoxButton.OK, MessageBoxImage.Warning);
   //      }
   //   } catch (Exception ex) {
   //      System.Diagnostics.Debug.WriteLine ($"Drive mapping failed: {ex}");
   //      MessageBox.Show ($"Drive mapping failed: {ex.Message}",
   //          "Error", MessageBoxButton.OK, MessageBoxImage.Error);
   //   }
   //}


   string? GetInstallLocationFromRegistry () {
      try {
         string[] registryPathsToCheck =
         {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ProfileCAM",
            @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\ProfileCAM",
            // Add other possible registry paths if needed
        };

         foreach (string registryPath in registryPathsToCheck) {
            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey (registryPath)) {
               if (key != null) {
                  object? installLocation = key.GetValue ("InstallLocation");
                  if (installLocation != null && !string.IsNullOrEmpty (installLocation.ToString ())) {
                     string? path = installLocation.ToString ();

                     // Validate that the path exists and is accessible
                     if (Directory.Exists (path)) {
                        return path;
                     }
                  }
               }
            }
         }

         return null;
      } catch (Exception ex) {
         Debug.WriteLine ($"Error reading registry: {ex.Message}");
         return null;
      }
   }

   bool LoadFluxBaseAssembly () {
      try {
         // Option 1: Use AssemblyLoader if it supports loading
         // Replace "Flux.Base" with the correct assembly name if different
         //if (AssemblyLoader.IsAssemblyLoadable ("Flux.Base")) {
         //   // Assume AssemblyLoader loads the assembly internally or check if it provides a LoadAssembly method
         //   // If AssemblyLoader has a LoadAssembly method, use it:
         //   // var fluxAssembly = AssemblyLoader.LoadAssembly("Flux.Base");
         //   return true;
         //}

         // Option 2: Fallback to standard .NET assembly loading if AssemblyLoader doesn't load explicitly

         //string assemblyPath = Path.Combine (
         //    Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location),
         //    "Flux.Base.dll");

         var dir = Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);

         if (dir == null) {
            System.Diagnostics.Debug.WriteLine ("Assembly directory is null");
            return false;
         }

         string assemblyPath = Path.Combine (dir, "Flux.Base.dll");

         if (!File.Exists (assemblyPath)) {
            System.Diagnostics.Debug.WriteLine ($"Flux.Base.dll not found at: {assemblyPath}");
            return false;
         }

         // Load the assembly
         Assembly fluxAssembly = Assembly.LoadFrom (assemblyPath);
         System.Diagnostics.Debug.WriteLine ($"Successfully loaded Flux.Base.dll from: {assemblyPath}");
         return fluxAssembly != null;
      } catch (Exception ex) {
         System.Diagnostics.Debug.WriteLine ($"Failed to load Flux.Base.dll: {ex.Message}");
         return false;
      }
   }

   void OnSupportClick (object sender, RoutedEventArgs e) {
      OpenWebsite ("https://www.teckinsoft.in/support");
   }

   void OnTeckInSoftClick (object sender, RoutedEventArgs e) {
      OpenWebsite ("https://www.teckinsoft.in");
   }

   void OpenWebsite (string url) {
      try {
         // Use ProcessStartInfo to open Microsoft Edge with the specified URL
         ProcessStartInfo psi = new () {
            FileName = "msedge.exe",
            Arguments = url,
            UseShellExecute = true
         };
         Process.Start (psi);
      } catch (Exception) {
         // Fallback: if Edge fails, try using the default browser
         try {
            Process.Start (new ProcessStartInfo {
               FileName = url,
               UseShellExecute = true
            });
         } catch (Exception fallbackEx) {
            MessageBox.Show ($"Failed to open browser: {fallbackEx.Message}",
                           "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }
   }

   void OnRecentFileItemRMBClick (object sender, RoutedEventArgs e) {
      if (sender is MenuItem menuItem &&
          menuItem.DataContext is string recentFile) {
         string filePath = ExtractPathFromRecentFilesMenuItemEntry (recentFile);
         // recentFile is the clicked RecentFiles entry
         // Example: open DINs related to that file
         filePath = Path.GetFileName (filePath);
         OpenDinsForFile (filePath);
      }
   }

   // Method to extract Windows absolute path from a string 
   // that contains Path + \t\t + time stamp
   static string ExtractPathFromRecentFilesMenuItemEntry (string recentEntry) {
      return recentEntry.Split (
          ["\t\t"],
          StringSplitOptions.None)[0];
   }
}

