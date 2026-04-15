using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProfileCAM.Core;
using ProfileCAM.Core.GCodeGen;
using ProfileCAM.Core.Processes;
using Flux.API;
using Microsoft.Win32;
using SanityHub.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;

namespace SanityHub {
   public partial class MainViewModel : ObservableObject {
      [ObservableProperty] string selectedSetting = string.Empty;
      public ObservableCollection<FileItem> Files { get; } = [];
      GenesysHub genesysHub = new ();
      public MainViewModel () { }

      [RelayCommand]
      private void BrowseFiles () {
         var dlg = new OpenFileDialog () {
            Multiselect = true,
            Title = "Select files to run"
         };

         if (dlg.ShowDialog () == true) {
            foreach (var f in dlg.FileNames) {
               if (!Files.Any (x => x.FullPath.Equals (f, StringComparison.OrdinalIgnoreCase)))
                  Files.Add (new FileItem { FullPath = f, Status = RunStatus.None });
            }
         }
      }

      [RelayCommand]
      private void DeleteFile (FileItem item) {
         if (item == null) return;
         Files.Remove (item);
      }

      [RelayCommand]
      private void ShowDetails (FileItem item) {
         if (item == null) return;
         var wnd = new DetailsWindow (item);
         wnd.Owner = Application.Current.Windows.OfType<Window> ().FirstOrDefault (w => w.IsActive);
         wnd.ShowDialog ();
      }

      [RelayCommand]
      public void RunAll () {
         // Add apppropriate folder path 
         string folderPath = "C:\\D drive\\Projects\\ProfileCAM\\Main ProfileCAM\\ProfileCAM\\TestData\\SettingJSONs";
         foreach (string filePath in Directory.GetFiles (folderPath, "*.json")) {
            if (!File.Exists (filePath)) continue;
            SelectedSetting = Path.GetFileNameWithoutExtension (filePath);
            MCSettings.It.LoadSettingsFromJson (filePath);
            foreach (var file in Files.ToList ()) {
               RunFile (file);
            }
         }
      }

      private void RunFile (FileItem file) {
         file.Status = RunStatus.Running;
         file.Details = $"Started at {DateTime.Now:HH:mm:ss}\n";

         try {
            var part = Part.Load (file.FullPath);
            genesysHub.Workpiece = new Workpiece (part.Model, part);
            genesysHub.Workpiece.Align ();
            if (MCSettings.It.CutHoles) genesysHub.Workpiece.DoAddHoles ();
            if (MCSettings.It.CutMarks) genesysHub.Workpiece.DoTextMarking (MCSettings.It);
            if (MCSettings.It.CutNotches || MCSettings.It.CutCutouts) genesysHub.Workpiece.DoCutNotchesAndCutouts ();

            // Check results of Branch and Bound
            MultiPassCuts mpc = new (genesysHub.GCodeGen);
            if (mpc.ToolingScopes.Count < MultiPassCuts.MaxFeatures) {
               file.Details += $"Branch and Bound Optimization started at {DateTime.Now:HH:mm:ss.fff}\n";
               mpc.ComputeBranchAndBoundCutscopes ();
               file.Details += $"Completed Branch and Bound Optimization at {DateTime.Now:HH:mm:ss.fff}\n";

               var fieldInfo = typeof (MultiPassCuts).GetField ("mMachinableCutScopes", BindingFlags.NonPublic | BindingFlags.Instance);
               var machinableCutScopes = fieldInfo?.GetValue (mpc) as List<CutScope> ?? [];

               file.Details += $"BRANCH AND BOUND results---------------\n";
               file.Details += $"Cutscopes count -- {machinableCutScopes.Count}\n";
               file.Details += $"BRANCH AND BOUND results---------------\n";
            }

            // Generate GCode
            file.Details += $"Started GCode generation at {DateTime.Now:HH:mm:ss}\n";
            var traces = Utils.ComputeGCode (genesysHub.GCodeGen, false);
            file.Details += $"Completed GCode generation at {DateTime.Now:HH:mm:ss}\n";

            // If no error comes up, set the status to passed.
            file.Status = RunStatus.Passed;
         } catch (Exception ex) {
            file.Status = RunStatus.Failed;
            file.Details += $"Exception: {ex.Message}\n";
         }
      }
   }
}