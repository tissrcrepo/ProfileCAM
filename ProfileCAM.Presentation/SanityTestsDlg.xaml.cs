using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using ProfileCAM.Core.Processes;
using ProfileCAM.Core;

namespace ProfileCAM.Presentation;
public partial class SanityTestsDlg : Window, INotifyPropertyChanged {
   #region Constructor
   public SanityTestsDlg (IGenesysHub genHub) {
      InitializeComponent ();
      GenesysHub = genHub;
      this.DataContext = this;
   }
   #endregion

   #region Data Members
   // Cached list to store the UI controls created for each row
   List<(CheckBox checkBox, TextBox textBox, Button browseButton, Button settingsButton, Button gCodeButton, Button diffButton, Ellipse statEllipse)> cachedControls = [];
   bool isDirty = false; // Dirty flag to track changes
   const string mDefaultTestSuiteDir = @"W:\ProfileCAM\SanityTests";
   const string mDefaultFxFileDir = @"W:\ProfileCAM\Sample";
   const string mDefaultBaselineDir = @"W:\ProfileCAM\TData";
   JsonSerializerOptions? mJSONWriteOptions, mJSONReadOptions;
   string? mTestSuiteDir;
   string? mTestFileName;
   int mNFixedTopRows = 2;
   List<int> mSelectedTestIndices = [];
   public event PropertyChangedEventHandler? PropertyChanged;
   #endregion

   #region Properties
   public String? TestFileName {
      get => mTestFileName;
      set {
         if (mTestFileName != value) {
            mTestFileName = value;
            this.Title = "Sanity Check " + value;
         }
      }
   }

   public string? FilePath { get; set; } = mDefaultFxFileDir;
   public string BaselineDir { get; set; } = mDefaultBaselineDir;
   public IGenesysHub GenesysHub { get; set; }
   public SanityCheck? SanityCheck { get; set; }

   public List<SanityTestData> SanityTests { get; set; } = [];
   public List<bool> SanityTestResult { get; set; } = [];
   public string? TestSuiteDir {
      get => mTestSuiteDir;
      set {
         if (mTestSuiteDir != value) {
            mTestSuiteDir = value;
            OnPropertyChanged ();
         }
      }
   }
   #endregion

   #region Event Handlers
   public void OnSanityTestsDlgLoaded (object sender, RoutedEventArgs e) {
      TestSuiteDir = mDefaultTestSuiteDir;
      SanityCheck = new (GenesysHub);
   }

   void OnPropertyChanged ([CallerMemberName] string? propertyName = null)
      => PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));

   void OnAddTestButtonClick (object sender, RoutedEventArgs e) {
      // Open file dialog to select a JSON file
      OpenFileDialog openFileDialog = new () {
         Filter = "STEP Files (*.stp;*.step)|*.stp;*.step|IGS Files (*.igs;*.iges)|*.igs;*.iges|All files (*.*)|*.*",
         InitialDirectory = FilePath
      };

      if (openFileDialog.ShowDialog () == true) {
         // Create Sanity test data object 
         var sanityTestData = new SanityTestData {
            FxFileName = openFileDialog.FileName,
            ToRun = true
         };
         FilePath = System.IO.Path.GetDirectoryName (openFileDialog.FileName);
         SanityTests.Add (sanityTestData);
         AddSanityTestRow (sanityTestData);
         // Mark as dirty since changes were made
         isDirty = true;
      }
   }

   void OnOkButtonClick (object sender, RoutedEventArgs e) {
      SaveIfDirty ();

      // Close without any prompts if not dirty
      this.Close ();
   }

   void OnDeleteButtonClick (object sender, RoutedEventArgs e)
      => RemoveSanityTestRows ();

   void OnBrowseFileButtonClick (int rowIndex) {
      // Handle Browse button click for the given row index
      OpenFileDialog openFileDialog = new () {
         Filter = "STEP Files (*.stp;*.step)|*.stp;*.step|FX Files (*.fx)|*.fx|IGS Files (*.igs;*.iges)|*.igs;*.iges|All files (*.*)|*.*",
         InitialDirectory = FilePath
      };

      if (openFileDialog.ShowDialog () == true) {
         FilePath = openFileDialog.FileName;
         cachedControls[rowIndex - mNFixedTopRows].textBox.Text = FilePath;
         var sanityTst = SanityTests[rowIndex - mNFixedTopRows];
         sanityTst.FxFileName = FilePath;
         SanityTests[rowIndex - mNFixedTopRows] = sanityTst;
      }
   }

   void OnRunCheckBoxChangeStatus (int controlIndex) {
      var testData = SanityTests[controlIndex];
      var isChecked = cachedControls[controlIndex].checkBox.IsChecked;
      if (isChecked.HasValue) {
         testData.ToRun = isChecked.Value;
         if (isChecked.Value == false && SelectAllCheckBox.IsChecked == true)
            SelectAllCheckBox.IsChecked = false;
      }

      SanityTests[controlIndex] = testData;
      bool anyNoRuns = cachedControls.Any (cc => cc.checkBox.IsChecked == false);
      if (!anyNoRuns)
         SelectAllCheckBox.IsChecked = true;

      isDirty = true;
   }

   void OnBrowseSuiteButtonClick (object sender, RoutedEventArgs e)
      => MessageBox.Show ($"Select Sanity Tests Directory");

   void OnClickSettingsButton (int rowIndex, SanityTestData sData) {
      // Create a new SettingsDlg instance with the settings
      var settingsDialog = new SettingsDlg (sData.MCSettings);

      // Subscribe to the OnOkAction event to handle the callback
      settingsDialog.OnOkAction += () => {
         // Handle the OK action and update the SanityTestData with the updated settings
         sData.MCSettings = settingsDialog.Settings;

         if (settingsDialog.IsModified) {
            isDirty = true;
            SanityTests[rowIndex - mNFixedTopRows] = sData;
         }
      };

      // Show the settings dialog
      settingsDialog.ShowDialog ();
   }

   void OnCloseButtonClick (object sender, RoutedEventArgs e) {
      SaveIfDirty ();
      RemoveSanityTestRows (onlySelected: false);
      this.Close ();
   }

   void OnRunSelectedTestsButtonClick (object sender, RoutedEventArgs e) {
      Mouse.OverrideCursor = Cursors.Wait;
      mSelectedTestIndices.Clear ();
      List<SanityTestData> testDataList = [];
      for (int ii = 0; ii < SanityTests.Count; ii++) {
         if (cachedControls[ii].checkBox.IsChecked == false)
            continue;

         testDataList.Add (SanityTests[ii]);
         mSelectedTestIndices.Add (ii);
      }
      if (testDataList.Count == 0) {
         MessageBox.Show ("Please select tests to run", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         Mouse.OverrideCursor = null;
         return;
      }
      var stats = RunTests (testDataList);
      UpdateRunStatus (mSelectedTestIndices, stats);
      Mouse.OverrideCursor = null;
   }

   void OnRunSingleTestClick (int rowIndex) {
      Mouse.OverrideCursor = Cursors.Wait;
      var testIndex = rowIndex - mNFixedTopRows;
      List<SanityTestData> testData = [SanityTests[testIndex]];
      mSelectedTestIndices = [testIndex];
      var stats = RunTests (testData, true);
      UpdateRunStatus (mSelectedTestIndices, stats);
      Mouse.OverrideCursor = null;
   }

   void OnDiffButtonClick (int rowIndex) {
      var dataIndex = mSelectedTestIndices.FindIndex (t => t == rowIndex - mNFixedTopRows);
      if (dataIndex != -1) {
         if (SanityCheck != null) {
            bool anyDiff = SanityCheck.Diff (BaselineDir, dataIndex, launchWinmerge: true);
            if (!anyDiff)
               MessageBox.Show ("There are no differences!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
         }
      } else {
         MessageBox.Show ("Please run the test before diff", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
   }

   void OnNewTSuiteButtonClick (object sender, RoutedEventArgs e) {
      SaveIfDirty ();
      RemoveSanityTestRows (onlySelected: false); // Proceed without saving
      ClearZombies ();
      OpenFileDialog fileDialog = new OpenFileDialog () {
         Filter = "Step Files (*.stp;*.step)|*.stp;*.step|IGS Files(*.igs;*.iges)|*.igs;*.iges|All files (*.*)|*.*",
         Multiselect = true,
         InitialDirectory = TestSuiteDir
      };
      if (fileDialog.ShowDialog () == true) {
         foreach (var file in fileDialog.FileNames) {
            var sanityTestData = new SanityTestData {
               FxFileName = file,
               ToRun = true
            };
            /*FilePath = System.IO.Path.GetDirectoryName (file);*/
            SanityTests.Add (sanityTestData);
            AddSanityTestRow (sanityTestData);
         }
         this.Title = "Sanity Tests - New Test Suite";
      }
      isDirty = true;
   }
   void OnLoadTSuiteButtonClick (object sender, RoutedEventArgs e) {
      RemoveSanityTestRows ();
      OpenFileDialog openFileDialog = new () {
         Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
         InitialDirectory = TestSuiteDir
      };

      if (openFileDialog.ShowDialog () == true)
         LoadSettingsFromJson (openFileDialog.FileName);

      TestFileName = openFileDialog.FileName;
      this.Title = "Sanity Tests Suite - " + TestFileName;
   }

   void OnSelectAllCheckBoxClick (object sender, RoutedEventArgs e) {
      if (SelectAllCheckBox.IsChecked == true) {
         // Check all checkboxes
         foreach (var (checkbox, _, _, _, _, _, _) in cachedControls)
            checkbox.IsChecked = true;
      } else {
         // Uncheck all checkboxes
         foreach (var (checkbox, _, _, _, _, _, _) in cachedControls)
            checkbox.IsChecked = false;
      }
   }

   void OnSaveButtonClick (object sender, RoutedEventArgs e) {
      if (!string.IsNullOrEmpty (TestFileName)) {
         SaveSettingsToJson (TestFileName);
         isDirty = false; // Reset the dirty flag after saving
      } else if (SanityTests.Count > 0) {
         SaveFileDialog saveFileDialog = new () {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = TestSuiteDir
         };

         if (saveFileDialog.ShowDialog () == true) {
            SaveSettingsToJson (saveFileDialog.FileName);
            TestFileName = saveFileDialog.FileName;
            this.Title = "Sanity Tests - " + TestFileName;
            isDirty = false; // Reset the dirty flag after saving
         }
      }

      if (SanityTests.Count == 0) {
         MessageBox.Show ("No tests to save", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
   }
   #endregion

   #region Utilities
   TextBox CreateTextBox (string initialText, int width) {
      TextBox filePathTextBox = new () {
         Margin = new Thickness (5),
         Text = initialText,
         Width = width
      };

      return filePathTextBox;
   }

   Ellipse CreateEllipseWidget (int newRowIdx, int colIndex) {
      // Add an Ellipse to the new row to indicate status
      Ellipse statusEllipse = new () {
         Width = 40,
         Height = 20,
         Fill = new SolidColorBrush (Colors.LightBlue),
         Margin = new Thickness (5),
         Stroke = new SolidColorBrush (Colors.Black)
      };

      Grid.SetRow (statusEllipse, newRowIdx);
      Grid.SetColumn (statusEllipse, colIndex);
      return statusEllipse;
   }

   void RemoveSanityTestRows (bool onlySelected = true) {
      // Collect all selected rows
      List<int> rowsToDelete = [];

      for (int i = 0; i < cachedControls.Count; i++) {
         if ((cachedControls[i].checkBox.IsChecked == true && onlySelected) || onlySelected == false)
            rowsToDelete.Add (i);
      }

      // Delete rows in reverse order to avoid indexing issues
      rowsToDelete.Sort ((a, b) => b.CompareTo (a));
      foreach (int rowIndex in rowsToDelete)
         RemoveRow (rowIndex + mNFixedTopRows);

      // Iterate through the indices and remove from cachedControls
      foreach (int index in rowsToDelete) {
         if (index >= 0 && index < SanityTests.Count)
            SanityTests.RemoveAt (index);
      }
   }

   void SaveIfDirty () {
      // If the test pres is modified and if the test was loaded (not cewly created)
      if (isDirty) {
         MessageBoxResult result = MessageBox.Show ("Do you want to save ?", "Save Confirmation",
                                                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
         if (result == MessageBoxResult.Yes) {
            if (!String.IsNullOrEmpty (TestFileName)) SaveSettingsToJson (TestFileName);
            else {
               // Show Save File Dialog
               SaveFileDialog saveFileDialog = new () {
                  Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                  InitialDirectory = TestSuiteDir
               };

               if (saveFileDialog.ShowDialog () == true) {
                  SaveSettingsToJson (saveFileDialog.FileName);
               }
            }
         }
      }
      isDirty = false;
   }

   List<bool>? RunTests (List<SanityTestData> testDataLst, bool forceRun = false) {
      var stats = SanityCheck?.Run (testDataLst, BaselineDir, SanityCheck?.GetArgumentNullException (), forceRun);
      return stats;
   }

   void UpdateRunStatus (List<int> testIndices, List<bool>? stats) {
      if (stats == null)
         throw new ArgumentNullException ("Statistics is null");
      for (int ii = 0; ii < stats.Count; ii++) {
         if (stats[ii]) cachedControls[testIndices[ii]].statEllipse.Fill = new SolidColorBrush (Colors.Green);
         else cachedControls[testIndices[ii]].statEllipse.Fill = new SolidColorBrush (Colors.Red);
      }
   }

   void RemoveRow (int rowIndex) {
      // Create a list of elements to remove to avoid modifying the collection while iterating
      var elementsToRemove = new List<UIElement> ();
      foreach (UIElement element in MainGrid.Children)
         if (Grid.GetRow (element) == rowIndex)
            elementsToRemove.Add (element);

      foreach (UIElement element in elementsToRemove)
         MainGrid.Children.Remove (element);

      // Remove the row definition
      MainGrid.RowDefinitions.RemoveAt (rowIndex);

      // Remove from cached controls
      cachedControls.RemoveAt (rowIndex - mNFixedTopRows);

      // Update the row index for the remaining rows
      foreach (UIElement element in MainGrid.Children) {
         int currentRow = Grid.GetRow (element);
         if (currentRow > rowIndex) {
            Grid.SetRow (element, currentRow - 1);
         }
      }

      // Mark as dirty since changes were made
      isDirty = true;
   }

   void ClearZombies () {
      TestSuiteDir = mDefaultTestSuiteDir;
      isDirty = false;
      cachedControls.Clear ();
      SanityTests.Clear ();
      TestFileName = string.Empty;
      mSelectedTestIndices.Clear ();
   }

   void SaveSettingsToJson (string filePath) {
      mJSONWriteOptions ??= new JsonSerializerOptions {
         WriteIndented = true, // For pretty-printing the JSON
         Converters = { new JsonStringEnumConverter () } // Converts Enums to their string representation
      };

      var jsonObject = new {
         Tests = SanityTests
      };

      var json = JsonSerializer.Serialize (jsonObject, mJSONWriteOptions);
      File.WriteAllText (filePath, json);
   }

   void LoadSettingsFromJson (string filePath, bool isTestBatch = false) {
      mJSONReadOptions ??= new JsonSerializerOptions {
         Converters = { new JsonStringEnumConverter () } // Converts Enums from their string representation
      };

      if (File.Exists (filePath)) {
         var json = File.ReadAllText (filePath);
         var jsonObject = JsonSerializer.Deserialize<JsonElement> (json, mJSONReadOptions);

         if (jsonObject.TryGetProperty ("Tests", out JsonElement testsElement)
             && testsElement.ValueKind == JsonValueKind.Array) {
            if (!isTestBatch) {
               SanityTests.Clear ();
               cachedControls.Clear ();
            }
            foreach (var element in testsElement.EnumerateArray ()) {
               var sanityTestData = new SanityTestData ().LoadFromJsonElement (element);
               SanityTests.Add (sanityTestData);

               // Create UI elements to match the loaded data, starting from row index 2 onwards
               AddSanityTestRow (sanityTestData);
            }
            isDirty = isTestBatch;
         }
      }
   }

   void OnLoadTestBatchButtonClick (object sender, RoutedEventArgs e) {
      SaveIfDirty ();
      FileDialog fileDialog = new OpenFileDialog {
         Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
         InitialDirectory = TestSuiteDir
      };
      if (fileDialog.ShowDialog () == true) {
         RemoveSanityTestRows (onlySelected: false);
         LoadSettingsFromJson (fileDialog.FileName);
         TestFileName = fileDialog.FileName;
         this.Title = "Sanity Tests Batch - " + TestFileName;
      }
   }

   void OnCreateTestBatchButtonClick (object sender, RoutedEventArgs e) {
      SaveIfDirty ();
      FileDialog fileDialog = new OpenFileDialog {
         Multiselect = true,
         Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
         InitialDirectory = TestSuiteDir
      };
      if (fileDialog.ShowDialog () == true) {
         RemoveSanityTestRows (onlySelected: false);
         foreach (var file in fileDialog.FileNames) {
            LoadSettingsFromJson (fileDialog.FileName, true);
         }
         TestFileName = "";
         this.Title = "Sanity Tests - New Test Batch";
      }

      if (fileDialog.FileNames.Length == 0) {
         MessageBox.Show ("No files selected", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
   }

   private void OnSaveAsButtonClick (object sender, RoutedEventArgs e) {

      if (!String.IsNullOrEmpty (TestFileName) || SanityTests.Count == 0) return;  // return if no tests to save or existing file
      SaveFileDialog saveFileDialog = new SaveFileDialog {
         Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
         InitialDirectory = TestSuiteDir
      };

      if (saveFileDialog.ShowDialog () == true) {
         TestFileName = saveFileDialog.FileName;
         SaveSettingsToJson (TestFileName);
         this.Title = "Sanity Tests - " + TestFileName;
         isDirty = false; // Reset the dirty flag after saving
      }
   }

   void AddSanityTestRow (SanityTestData sanityTestData) {
      // Create a new row in the grid
      int newRowIdx = MainGrid.RowDefinitions.Count;
      MainGrid.RowDefinitions.Add (new RowDefinition { Height = GridLength.Auto });

      // Add a CheckBox to the new row
      CheckBox newCheckBox = new () {
         Margin = new Thickness (5), IsChecked = sanityTestData.ToRun,
         HorizontalAlignment = HorizontalAlignment.Center
      };

      if (!sanityTestData.ToRun && SelectAllCheckBox.IsChecked == true)
         SelectAllCheckBox.IsChecked = false;

      Grid.SetRow (newCheckBox, newRowIdx);
      newCheckBox.Checked += (s, args) => OnRunCheckBoxChangeStatus (Grid.GetRow (newCheckBox) - mNFixedTopRows);
      newCheckBox.Unchecked += (s, args) => OnRunCheckBoxChangeStatus (Grid.GetRow (newCheckBox) - mNFixedTopRows);
      Grid.SetColumn (newCheckBox, 0);
      MainGrid.Children.Add (newCheckBox);

      // Add a TextBox to the new row to display the JSON file path
      TextBox filePathTextBox = CreateTextBox (sanityTestData.FxFileName, 220);

      Grid.SetRow (filePathTextBox, newRowIdx);
      Grid.SetColumn (filePathTextBox, 1);
      MainGrid.Children.Add (filePathTextBox);

      // Add a "Browse" Button to the new row
      Button browseButton = new () { Content = "Browse", Style = (Style)FindResource ("CustomButtonStyle"), Margin = new Thickness (5) };
      browseButton.Click += (s, args) => OnBrowseFileButtonClick (Grid.GetRow (browseButton));
      Grid.SetRow (browseButton, newRowIdx);
      Grid.SetColumn (browseButton, 2);
      MainGrid.Children.Add (browseButton);

      // Add a "Settings" Button to the new row
      Button settingsButton = new () { Content = "Settings", Style = (Style)FindResource ("CustomButtonStyle"), Margin = new Thickness (5) };
      settingsButton.Click += (s, args) => OnClickSettingsButton (Grid.GetRow (settingsButton), sanityTestData);
      Grid.SetRow (settingsButton, newRowIdx);
      Grid.SetColumn (settingsButton, 3);
      MainGrid.Children.Add (settingsButton);

      // Add a "GCode" Button to the new row
      Button gCodeButton = new () { Content = "Run", Style = (Style)FindResource ("CustomButtonStyle"), Margin = new Thickness (5) };
      gCodeButton.Click += (s, args) => OnRunSingleTestClick (Grid.GetRow (gCodeButton));
      Grid.SetRow (gCodeButton, newRowIdx);
      Grid.SetColumn (gCodeButton, 4);
      MainGrid.Children.Add (gCodeButton);

      // Add a "Diff" Button to the new row
      Button diffButton = new () { Content = "Diff", Style = (Style)FindResource ("CustomButtonStyle"), Margin = new Thickness (5) };
      diffButton.Click += (s, args) => OnDiffButtonClick (Grid.GetRow (diffButton));
      Grid.SetRow (diffButton, newRowIdx);
      Grid.SetColumn (diffButton, 5);
      MainGrid.Children.Add (diffButton);

      var statusEllipse = CreateEllipseWidget (newRowIdx, 6);
      MainGrid.Children.Add (statusEllipse);

      // Cache the created controls
      cachedControls.Add ((newCheckBox, filePathTextBox, browseButton,
                           settingsButton, gCodeButton, diffButton,
                           statusEllipse));
   }
   #endregion
}
