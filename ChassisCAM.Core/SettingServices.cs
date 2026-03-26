using System.Windows;

namespace ChassisCAM.Core {
   public class SettingServices {
      readonly string settingsFilePath;
      public static SettingServices It => sIt ??= new ();
      static SettingServices sIt;

      public bool LeftToRightMachining { get; set; }
      SettingServices () {
         string fChassisDrive = "W:\\";
         string fChassisFolderPath;
         if (!Directory.Exists (fChassisDrive)) {
            string userHomePath = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
            fChassisFolderPath = Path.Combine (userHomePath, "ChassisCAM");
         } else
            fChassisFolderPath = Path.Combine (fChassisDrive, "ChassisCAM");

         // Define the full path to the settings file
         settingsFilePath = Path.Combine (fChassisFolderPath, "ChassisCAM.User.Settings.JSON");

         // Ensure the directory exists
         if (!Directory.Exists (fChassisFolderPath)) {
            Directory.CreateDirectory (fChassisFolderPath);
         }
         LeftToRightMachining = true;
      }

      public void SaveSettings (MCSettings settings, bool backupNew = false) {
         string envVariable = Environment.GetEnvironmentVariable ("__FC_AUTH__");
         Guid expectedGuid = new ("e96e66ff-17e6-49ac-9fe1-28bb45a6c1b9");
         if (backupNew) {
#if DEBUG || TESTRELEASE
            settings.SaveSettingsToJsonASCII (settingsFilePath + ".bckup");
#else
            settings.SaveSettingsToJson (settingsFilePath + ".bckup");
#endif
         } else {
#if DEBUG || TESTRELEASE
            settings.SaveSettingsToJsonASCII (settingsFilePath);
#else
            if (!string.IsNullOrEmpty (envVariable) && Guid.TryParse (envVariable, out Guid currentGuid) && currentGuid == expectedGuid) {
               MCSettings.It.SaveSettingsToJsonASCII (settingsFilePath);
            } else
               MCSettings.It.SaveSettingsToJson (settingsFilePath);
#endif
         }
      }

      public void LoadSettings (MCSettings settings) {
         if (File.Exists (settingsFilePath)) {
            try {
               settings.LoadSettingsFromJson (settingsFilePath);
            } catch (Exception ex) {
#if DEBUG
               MessageBox.Show ($"Error encountered when loading settings: {ex.Message}",
                "Settings Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
               throw;
            }
            // Write to ASCII json if TESTRELEASE or DEBUG config
            string envVariable = Environment.GetEnvironmentVariable ("__FC_AUTH__");
            Guid expectedGuid = new ("e96e66ff-17e6-49ac-9fe1-28bb45a6c1b9");
#if DEBUG || TESTRELEASE
            try {
               MCSettings.It.SaveSettingsToJsonASCII (settingsFilePath);
            } catch (Exception ex) {
               MessageBox.Show ($"Error encountered when loading settings: {ex.Message}",
                "Settings Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
#else
            if (!string.IsNullOrEmpty (envVariable) && Guid.TryParse (envVariable, out Guid currentGuid) && currentGuid == expectedGuid) {
               MCSettings.It.SaveSettingsToJsonASCII (settingsFilePath);
            } else
               MCSettings.It.SaveSettingsToJson (settingsFilePath);
#endif
         }
      }
   }
}
