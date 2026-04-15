using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ProfileCAM.Core;
using System.IO; // Required for File logging
using System.Text;
using System.Runtime.InteropServices; // Required for StringBuilder

namespace ProfileCAM.Presentation;

public partial class App : Application {
   // Add the DllImport declaration here (inside the class)
   [DllImport ("user32.dll")]
   static extern void SetProcessDPIAware ();

   bool AbnormalTermination { get; set; } = false;
   string? mLogFilePath;

   protected override void OnStartup (StartupEventArgs e) {
      // --- ADD THIS LINE RIGHT AT THE START ---
      SetProcessDPIAware (); // Force the app to be DPI aware before doing anything else

      base.OnStartup (e);

      // Set the current culture to "en-US"
      CultureInfo.CurrentCulture = new CultureInfo ("en-US");
      CultureInfo.CurrentUICulture = new CultureInfo ("en-US");

      // Initialize logging immediately
      InitializeLogging ();

      LogMessage ("Application Startup Initialized.");

      // Handle UI thread exceptions
      Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

      // Handle non-UI thread exceptions
      AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

      // Handle TaskScheduler exceptions
      TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

      try {
         LogMessage ("Calling OnAppStart...");
         OnAppStart ();
         LogMessage ("OnAppStart completed successfully.");
      } catch (Exception ex) {
         // This catches any exception thrown directly by OnAppStart itself
         LogMessage ($"CRITICAL: Exception in OnAppStart: {ex.Message}", ex);
         AbnormalTermination = true;
         // Even though we caught it, we should still shut down gracefully.
         MessageBox.Show ($"A critical error occurred during startup: {ex.Message}. Check the log file on your Desktop.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
         Shutdown ();
      }
   }

   void InitializeLogging () {
      try {
         // Get the Local AppData folder for the current user
         string localAppDataPath = Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData);

         // Create a subfolder for your application
         string appFolderPath = Path.Combine (localAppDataPath, "ProfileCAM");

         // Ensure the directory exists
         Directory.CreateDirectory (appFolderPath);

         // Create the full path for the log file
         mLogFilePath = Path.Combine (appFolderPath, "ProfileCAM_StartupLog.txt");

         // Write a header to a new log file each time the app starts
         File.WriteAllText (mLogFilePath, $"--- ProfileCAM Startup Log [{DateTime.Now}] ---\n");

         LogMessage ($"Log file initialized at: {mLogFilePath}");
      } catch (Exception ex) {
         // Fallback to Desktop if we can't use AppData
         string desktopPath = Environment.GetFolderPath (Environment.SpecialFolder.Desktop);
         mLogFilePath = Path.Combine (desktopPath, "FChassis_StartupLog_Fallback.txt");
         File.WriteAllText (mLogFilePath, $"Could not create log in AppData: {ex.Message}\n");
         File.AppendAllText (mLogFilePath, $"--- ProfileCAM Startup Log [{DateTime.Now}] ---\n");
      }
   }

   void LogMessage (string message, Exception? ex = null) {
      try {
         StringBuilder logEntry = new ();
         logEntry.AppendLine ($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

         if (ex != null) {
            logEntry.AppendLine ($"Exception Type: {ex.GetType ().FullName}");
            logEntry.AppendLine ($"Exception Message: {ex.Message}");
            logEntry.AppendLine ($"Stack Trace: {ex.StackTrace}");
            logEntry.AppendLine ("--");
         }
         if ( mLogFilePath != null )
            File.AppendAllText (mLogFilePath, logEntry.ToString ());
      } catch {
         // If logging fails, there's nowhere to log the error. We have to swallow it.
      }
   }

   void TaskScheduler_UnobservedTaskException (object? sender, UnobservedTaskExceptionEventArgs e) {
      AbnormalTermination = true;
      LogMessage ("UNHANDLED EXCEPTION in Async Task (UnobservedTaskException):", e.Exception);
      OnExitHandler ();

      // Mark the exception as handled to prevent process crash
      e.SetObserved ();
   }

   void CurrentDomain_UnhandledException (object sender, UnhandledExceptionEventArgs e) {
      AbnormalTermination = true;
      // The exception object is of type 'object', need to cast it
      Exception? ex = e.ExceptionObject as Exception;
      LogMessage ($"UNHANDLED EXCEPTION in Non-UI Thread (AppDomain). IsTerminating: {e.IsTerminating}", ex);
      OnExitHandler ();
   }

   void Current_DispatcherUnhandledException (object sender, DispatcherUnhandledExceptionEventArgs e) {
      AbnormalTermination = true;
      LogMessage ("UNHANDLED EXCEPTION on UI Thread (DispatcherUnhandledException):", e.Exception);
      OnExitHandler ();

      // Preventing the application from shutting down immediately.
      // Use with caution, as the application state may be unstable.
      e.Handled = true;

      // It's often kinder to show a message and shut down.
      MessageBox.Show ($"A unexpected error occurred and has been logged. The application may become unstable. Please check the log file on your Desktop.\n\nError: {e.Exception.Message}", "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Warning);
      // You could call Shutdown() here if the error is unrecoverable.
   }

   void OnExitHandler () => BeforeExit ();

   protected override void OnExit (ExitEventArgs e) {
      LogMessage ("Application Exiting Normally.");
      OnExitHandler ();
      base.OnExit (e);
   }

   public void BeforeExit () {
      LogMessage ($"BeforeExit called. Abnormal Termination: {AbnormalTermination}");
      if (AbnormalTermination) {
         LogMessage ("Performing emergency save due to abnormal termination...");
         try {
            SettingServices.It.SaveSettings (MCSettings.It, backupNew: true);
            LogMessage ("Emergency save completed.");
         } catch (Exception ex) {
            LogMessage ("FAILED during emergency save:", ex);
         }
      }
      LogMessage ("--- Application Shutdown Complete ---\n\n");
   }

   public void OnAppStart () {
      // Your startup logic here
      LogMessage ("OnAppStart logic begins.");
      // SettingServices.It.LoadSettings (); // Uncomment this and wrap in try-catch if it might fail
      LogMessage ("OnAppStart logic ends.");
   }
}