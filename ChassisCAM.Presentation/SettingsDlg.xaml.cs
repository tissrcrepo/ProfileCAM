using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

using ChassisCAM.Core;
using ChassisCAM.Core.Geometries;



namespace ChassisCAM.Presentation;
using static ChassisCAM.Core.Geometries.DoubleExtensions;
using static ChassisCAM.Core.Geometries.IntExtensions;
using static ChassisCAM.Core.MCSettings.EHeads;


/// <summary>Interaction logic for SettingsDlg.xaml</summary>
public partial class SettingsDlg : Window, INotifyPropertyChanged {
   public delegate void OnOkActionDelegate ();
   public event OnOkActionDelegate OnOkAction;
   bool mIsRestrictedSettingsVisible;

   public bool IsRestrictedSettingsVisible {
      get => mIsRestrictedSettingsVisible;
      set {
         if (mIsRestrictedSettingsVisible != value) {
            mIsRestrictedSettingsVisible = value;
            OnPropertyChanged ();
         }
      }
   }

   public event PropertyChangedEventHandler PropertyChanged;
   protected void OnPropertyChanged ([CallerMemberName] string propertyName = null) {
      PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
   }
   public MCSettings Settings { get; private set; }
   public bool IsModified { get; private set; }

   public SettingsDlg (MCSettings set) {
      InitializeComponent ();
      DataContext = this;

#if DEBUG || TESTRELEASE
      IsRestrictedSettingsVisible = true;
#else
      string envVariable = Environment.GetEnvironmentVariable ("__FC_AUTH__");
      Guid expectedGuid = new ("e96e66ff-17e6-49ac-9fe1-28bb45a6c1b9");
      if (!string.IsNullOrEmpty (envVariable) && Guid.TryParse (envVariable, out Guid currentGuid) && currentGuid == expectedGuid)
         IsRestrictedSettingsVisible = true;
      else
         IsRestrictedSettingsVisible = false;
#endif
      Bind (set);
      tbNotchWireJointDistance.TextChanged += TbNotchWireJointDistanceValueChanged;
   }

   /// <summary>
   /// This method binds the dialog controls with setters and getters
   /// </summary>
   /// <param name="set">MCSettings</param>
   void Bind (MCSettings set) {
      // Set the internal property to the passed settings object
      Settings = set;
      tbStandoff.Bind (() => Settings.Standoff, f => { Settings.Standoff = f.Clamp (0, 100); IsModified = true; });
      tbLeastWJLength.Bind (() => Settings.LeastWJLength, f => { Settings.LeastWJLength = f.Clamp (0.25, 100); IsModified = true; });
      tbFlexCuttingGap.Bind (() => Settings.FlexCuttingGap, f => { Settings.FlexCuttingGap = f.Clamp (0, 100); IsModified = true; });
      tbPartition.Bind (() => Settings.PartitionRatio, f => { Settings.PartitionRatio = f.Clamp (0, 1); IsModified = true; });
      tbStepLength.Bind (() => Settings.StepLength, f => { Settings.StepLength = f.Clamp (0.001, 50); IsModified = true; });
      cbPingPong.Bind (() => Settings.UsePingPong, b => { Settings.UsePingPong = b; IsModified = true; });
      cbOptimize.Bind (() => Settings.OptimizePartition, b => { Settings.OptimizePartition = b; IsModified = true; });
      cbOnlyWJTSlot.Bind (() => Settings.SlotWithWJTOnly, b => { Settings.SlotWithWJTOnly = b; IsModified = true; });
      cbDualFlangeCutoutNotch.Bind (() => Settings.DualFlangeCutoutNotchOnly, b => { Settings.DualFlangeCutoutNotchOnly = b; IsModified = true; });
      tbLeadInApproachArcAngle.Bind (() => Settings.LeadInApproachArcAngle, b => { Settings.LeadInApproachArcAngle = b; IsModified = true; });
      tbMarkText.Bind (() => Settings.MarkText, s => { Settings.MarkText = s; IsModified = true; });
      tbMarkTextHeight.Bind (() => Settings.MarkTextHeight, h => { Settings.MarkTextHeight = h.Clamp (8, 80); IsModified = true; });
      cbMarkTextAngle.ItemsSource = Enum.GetValues (typeof (ERotate)).Cast<ERotate> ().ToList ();
      cbMarkTextAngle.Bind (() => Settings.MarkAngle, s => { Settings.MarkAngle = s; IsModified = true; });
      tbMarkTextPositionX.Bind (() => Settings.MarkTextPosX, f => { Settings.MarkTextPosX = f.Clamp (0.05, 100000); IsModified = true; });
      tbMarkTextPositionY.Bind (() => Settings.MarkTextPosY, f => { Settings.MarkTextPosY = f.Clamp (-100000, 100000); IsModified = true; });

      //lbPriority.Bind (btnPrioUp, btnPrioDown, () => Settings.ToolingPriority, a => Settings.ToolingPriority = [.. a.OfType<EKind> ()]);
      rbBoth.Bind (() => Settings.Heads == Both, () => { Settings.Heads = Both; IsModified = true; });
      rbLeft.Bind (() => Settings.Heads == MCSettings.EHeads.Left,
         () => { Settings.Heads = MCSettings.EHeads.Left; IsModified = true; });
      rbRight.Bind (() => Settings.Heads == Right, () => { Settings.Heads = Right; IsModified = true; });
      rbLHComponent.Bind (() => Settings.PartConfig == MCSettings.PartConfigType.LHComponent,
         () => { Settings.PartConfig = MCSettings.PartConfigType.LHComponent; IsModified = true; });
      rbRHComponent.Bind (() => Settings.PartConfig == MCSettings.PartConfigType.RHComponent,
         () => { Settings.PartConfig = MCSettings.PartConfigType.RHComponent; IsModified = true; });
      tbApproachLength.Bind (() => Settings.ApproachLength, al => { Settings.ApproachLength = al.Clamp (0, 6); IsModified = true; });
      tbNotchApproachLength.Bind (() => Settings.NotchApproachLength,
         al => { Settings.NotchApproachLength = al.Clamp (0, 20); IsModified = true; });
      tbNotchWireJointDistance.Bind (() => Settings.NotchWireJointDistance,
         al => { Settings.NotchWireJointDistance = al.Clamp (0, 5); IsModified = true; });
      tbMinNotchLengthThreshold.Bind (() => Settings.MinNotchLengthThreshold,
         al => { Settings.MinNotchLengthThreshold = al.Clamp (10, 300.0); IsModified = true; });
      tbMinCutOutLengthThreshold.Bind (() => Settings.MinCutOutLengthThreshold,
         al => { Settings.MinCutOutLengthThreshold = al.Clamp (10, 300.0); IsModified = true; });
      cbCutHoles.Bind (() => Settings.CutHoles, b => { Settings.CutHoles = b; IsModified = true; });
      cbCutNotches.Bind (() => Settings.CutNotches, b => { Settings.CutNotches = b; IsModified = true; });
      cbCutCutouts.Bind (() => Settings.CutCutouts, b => { Settings.CutCutouts = b; IsModified = true; });
      cbCutMarks.Bind (() => Settings.CutMarks, b => { Settings.CutMarks = b; IsModified = true; });
      cbCutWeb.Bind (() => Settings.CutWeb, b => { Settings.CutWeb = b; IsModified = true; });
      cbCutFlange.Bind (() => Settings.CutFlange, b => { Settings.CutFlange = b; IsModified = true; });
      cbShowTlgNames.Bind (() => Settings.ShowToolingNames, b => { Settings.ShowToolingNames = b; IsModified = true; });
      cbShowTlgExtents.Bind (() => Settings.ShowToolingExtents, b => { Settings.ShowToolingExtents = b; IsModified = true; });
      tbMinThresholdPart.Bind (() => Settings.MinThresholdForPartition, b => { Settings.MinThresholdForPartition = b; IsModified = true; });
      tbDinFilenameSuffix.Bind (() => Settings.DINFilenameSuffix, b => { Settings.DINFilenameSuffix = b; IsModified = true; });

      chbMPC.Bind (() => {
         tbMaxFrameLength.IsEnabled = tbDeadBandWidth.IsEnabled = Settings.EnableMultipassCut;
         return Settings.EnableMultipassCut;
      },
       b => {
          Settings.EnableMultipassCut = b; // Update the value
          tbMaxFrameLength.IsEnabled = tbDeadBandWidth.IsEnabled = b; // Enable/disable based on the value
          IsModified = true;
       });

      tbMaxFrameLength.Bind (() => Settings.MaxFrameLength, b => { Settings.MaxFrameLength = b; IsModified = true; });
      tbDeadBandWidth.Bind (() => Settings.DeadbandWidth, b => { Settings.DeadbandWidth = b; IsModified = true; });
      tbDirectoryPath.Bind (() => {
         return Settings.NCFilePath;
      }, b => {
         Settings.NCFilePath = b;
         IsModified = true;
      });
      tbInputDirectoryWPath.Bind (() => {
         return Settings.WMapLocation;
      }, b => {
         Settings.WMapLocation = b;
         IsModified = true;
      });
      cbLCMMachine.ItemsSource = Enum.GetValues (typeof (MachineType)).Cast<MachineType> ();
      cbLCMMachine.Bind (() => {
         chbMPC.IsEnabled = tbMaxFrameLength.IsEnabled = tbDeadBandWidth.IsEnabled = (Settings.Machine == MachineType.LCMMultipass2H);
         return Settings.Machine;
      },
         (MachineType selectedType) => {
            chbMPC.IsEnabled = tbMaxFrameLength.IsEnabled = tbDeadBandWidth.IsEnabled = Settings.EnableMultipassCut = (selectedType == MachineType.LCMMultipass2H);
            Settings.Machine = selectedType;
            IsModified = true;
         });
      tbWPOptions.Bind (() => {
         return Settings.WorkpieceOptionsFilename;
      }, b => {
         if (Settings.WorkpieceOptionsFilename != b) {
            Settings.WorkpieceOptionsFilename = b;
            IsModified = true;
         }
      });
      rbDP.Bind (() => Settings.OptimizerType == MCSettings.EOptimize.DP,
         () => { Settings.OptimizerType = MCSettings.EOptimize.DP; IsModified = true; });
      rbTime.Bind (() => Settings.OptimizerType == MCSettings.EOptimize.Time,
         () => { Settings.OptimizerType = MCSettings.EOptimize.Time; IsModified = true; });

      btnOK.Bind (OnOk);
   }
   private void OnKeyDownHandler (object sender, KeyEventArgs e) {
      if (e.Key == Key.Enter) OnOk ();
      else if (e.Key == Key.Escape) Close ();
   }

   void OnOk () {
      OnOkAction?.Invoke ();
      Close ();
   }

   // Event handler for the Browse button click
   void OnOutputFolderSelect (object sender, RoutedEventArgs e) {
      // Create an OpenFileDialog to select a folder (we'll trick it for folder selection)
      var dialog = new OpenFileDialog {
         Title = "Select a Folder",
         Filter = "All files (*.*)|*.*",
         CheckFileExists = false,
         ValidateNames = false,
         FileName = "Select folder" // Trick to make it look like folder selection
      };

      // Show the dialog and get the result
      if (dialog.ShowDialog () == true) {
         // Extract the directory path from the selected file
         string selectedDirectory = System.IO.Path.GetDirectoryName (dialog.FileName);
         tbDirectoryPath.Text = selectedDirectory;
      }
   }

   void OnWorkpieceOptionsFileSelect (object sender, RoutedEventArgs e) {
      // Create an OpenFileDialog to select a JSON file
      var dialog = new OpenFileDialog {
         Title = "Select a JSON File",
         Filter = "JSON files (*.json)|*.json",
         CheckFileExists = true,  // This ensures the user selects an existing file
         ValidateNames = true     // Validate that a valid file name is selected
      };

      // Show the dialog and get the result
      if (dialog.ShowDialog () == true) {
         // Get the selected file path
         string selectedFile = dialog.FileName;
         tbWPOptions.Text = selectedFile;
      }
   }

   void OnInputFolderSelect( object sender, RoutedEventArgs e) {

   }
   void TbNotchWireJointDistanceValueChanged (object sender, TextChangedEventArgs e) {
      if (double.TryParse (tbNotchWireJointDistance.Text, out double value) && (value.SGT (5) || value.SLT (0))) {
         // Show an error message
         MessageBox.Show ("Notch Wire Joint Distance should lie betweem 0 and 5 mm", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);

         // Optionally, reset the value back to 5
         if (value.GTEQ (5)) {
            tbNotchWireJointDistance.Text = "5";
            Settings.NotchWireJointDistance = value.Clamp (0, 5);
         } else if (value.LTEQ (0)) {
            tbNotchWireJointDistance.Text = "0";
            Settings.NotchWireJointDistance = value.Clamp (0, 5);
         }
      }
   }
}
