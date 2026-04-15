using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace ProfileCAM.Presentation;
public static class DriveMapper {
   public static bool MapDrive (string driveLetter, string targetPath, bool createSubfolders = true) {
      try {
         // Ensure target path exists
         if (!Directory.Exists (targetPath)) {
            Directory.CreateDirectory (targetPath);
         }

         // Create required subfolders if they don't exist
         CreateRequiredSubfolders (targetPath, createSubfolders);

         // First map with subst (non-elevated) for immediate availability
         MapWithSubst (driveLetter, targetPath);

         // Then map with registry for persistence (user level)
         //MapWithRegistry (driveLetter, targetPath);

         return true;
      } catch (Exception ex) {
         MessageBox.Show ($"Failed to map drive: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
         return false;
      }
   }

   public static void CreateRequiredSubfolders (string basePath, bool createIfMissing = true) {
      string fchassisPath = Path.Combine (basePath, "ProfileCAM");
      string samplesPath = Path.Combine (fchassisPath, "Sample");
      string dataPath = Path.Combine (fchassisPath, "Data");

      if (!Directory.Exists (fchassisPath)) {
         if (!createIfMissing) {
            throw new InvalidOperationException (
                $"Target folder must contain 'ProfileCAM' subfolder. " +
                $"Expected path: {fchassisPath}");
         }
         Directory.CreateDirectory (fchassisPath);
      }

      if (!Directory.Exists (samplesPath)) {
         if (!createIfMissing) {
            throw new InvalidOperationException (
                $"Target folder must contain 'ProfileCAM/Sample' subfolder. " +
                $"Expected path: {samplesPath}");
         }
         Directory.CreateDirectory (samplesPath);
      }

      if (!Directory.Exists (dataPath)) {
         if (!createIfMissing) {
            throw new InvalidOperationException (
                $"Target folder must contain 'ProfileCAM/Data' subfolder. " +
                $"Expected path: {dataPath}");
         }
         Directory.CreateDirectory (dataPath);
      }
   }

   public static bool HasRequiredFolderStructure (string path) {
      try {
         string fchassisPath = Path.Combine (path, "ProfileCAM");
         string samplesPath = Path.Combine (fchassisPath, "Sample");
         string dataPath = Path.Combine (fchassisPath, "Data");

         return Directory.Exists (fchassisPath) &&
                Directory.Exists (samplesPath) &&
                Directory.Exists (dataPath);
      } catch {
         return false;
      }
   }

   public static void ValidateFolderStructure (string basePath) {
      string fchassisPath = Path.Combine (basePath, "ProfileCAM");
      string samplesPath = Path.Combine (fchassisPath, "Sample");
      string dataPath = Path.Combine (fchassisPath, "Data");

      if (!Directory.Exists (fchassisPath)) {
         throw new InvalidOperationException (
             $"Target folder must contain 'ProfileCAM' subfolder. " +
             $"Expected path: {fchassisPath}");
      }

      if (!Directory.Exists (samplesPath)) {
         throw new InvalidOperationException (
             $"Target folder must contain 'ProfileCAM/Sample' subfolder. " +
             $"Expected path: {samplesPath}");
      }

      if (!Directory.Exists (dataPath)) {
         throw new InvalidOperationException (
             $"Target folder must contain 'ProfileCAM/Data' subfolder. " +
             $"Expected path: {dataPath}");
      }
   }

   public static void MapWithSubst (string driveLetter, string targetPath) {
      // Validate folder structure before mapping
      ValidateFolderStructure (targetPath);

      // Remove existing mapping if any
      UnmapDriveSubst (driveLetter);

      var process = Process.Start (new ProcessStartInfo {
         FileName = "subst",
         Arguments = $"{driveLetter} \"{targetPath}\"",
         CreateNoWindow = true,
         UseShellExecute = false
      });

      process?.WaitForExit (5000);

      if (process?.ExitCode != 0) {
         throw new Exception ($"subst command failed with exit code: {process?.ExitCode}");
      }
   }

   public static void MapWithRegistry (string driveLetter, string targetPath) {
      // Validate folder structure before registry operations
      ValidateFolderStructure (targetPath);

      string driveName = driveLetter.Replace (":", "");

      // Create registry mapping in MountPoints2 for user-level persistence
      using (RegistryKey? key = Registry.CurrentUser.OpenSubKey (
          @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2", true)) {
         if (key != null) {
            // Remove existing mapping
            try { key.DeleteSubKeyTree (driveName); } catch { }

            // Create new mapping
            using (RegistryKey driveKey = key.CreateSubKey (driveName)) {
               driveKey.SetValue ("_LabelFromReg", $"Mapped {driveLetter}", RegistryValueKind.String);
            }
         }
      }

      // Add to startup for persistence (user level - runs when user logs in)
      using (RegistryKey? key = Registry.CurrentUser.OpenSubKey (
          @"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
         if (key != null) {
            string substCommand = $"subst {driveLetter} \"{targetPath}\"";
            key.SetValue ($"MapDrive_{driveName}", $"cmd.exe /c {substCommand}", RegistryValueKind.String);
         }
      }
   }

   public static void UnmapDrive (string driveLetter) {
      try {
         // Remove subst mapping
         UnmapDriveSubst (driveLetter);

         // Remove registry mapping
         string driveName = driveLetter.Replace (":", "");
         using (RegistryKey? key = Registry.CurrentUser.OpenSubKey (
    @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2", true)) {

            if (key != null) {
               try { key.DeleteSubKeyTree (driveName); } catch { }
            }
         }

         // Remove from startup
         using (RegistryKey? key = Registry.CurrentUser.OpenSubKey (
    @"Software\Microsoft\Windows\CurrentVersion\Run", true)) {

            if (key != null) {
               try { key.DeleteValue ($"MapDrive_{driveName}"); } catch { }
            }
         }
      } catch {
         // Ignore errors during unmapping
      }
   }

   private static void UnmapDriveSubst (string driveLetter) {
      try {
         Process.Start (new ProcessStartInfo {
            FileName = "subst",
            Arguments = $"{driveLetter} /D",
            CreateNoWindow = true,
            UseShellExecute = false
         })?.WaitForExit (5000);
      } catch {
         // Ignore errors when deleting non-existent mappings
      }
   }

   public static bool IsDriveMapped (string driveLetter) {
      try {
         // Check if drive letter exists and is accessible
         return Directory.Exists (driveLetter + Path.DirectorySeparatorChar);
      } catch {
         return false;
      }
   }

   public static string? GetMappedPath (string driveLetter) {
      try {
         // Use subst command to get the mapped path
         var process = new Process {
            StartInfo = new ProcessStartInfo {
               FileName = "subst",
               Arguments = driveLetter,
               RedirectStandardOutput = true,
               UseShellExecute = false,
               CreateNoWindow = true
            }
         };

         process.Start ();
         string output = process.StandardOutput.ReadToEnd ();
         process.WaitForExit (5000);

         if (process.ExitCode == 0 && !string.IsNullOrEmpty (output)) {
            // Parse output: "W:\: => C:\Some\Path"
            var parts = output.Split (new[] { " => " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) {
               return parts[1].Trim ();
            }
         }

         return null;
      } catch {
         return null;
      }
   }
}