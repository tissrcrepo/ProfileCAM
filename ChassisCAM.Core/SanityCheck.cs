using System.Diagnostics;
using ChassisCAM.Core.GCodeGen;
using ChassisCAM.Core.Processes;
using Flux.API;

namespace ChassisCAM.Core {
   /// <summary>Implements a very basic sanity check</summary>
   /// This is just a placeholder for a more elaborate test system. For now, we just
   /// want to ensure that as we make changes to the code generator, the generated code does
   /// not start varying
   public class SanityCheck {
      #region Constructor(s)
      // Constructor
      public SanityCheck (GenesysHub genHub) {
         GenesysHub = genHub ?? throw new ArgumentNullException (nameof (genHub));
         GCodeGen = GenesysHub.GCodeGen;
      }
      #endregion
      #region Contained Entities
      public GenesysHub GenesysHub { get; private set; }
      public Part Part { get; private set; }
      public GCodeGenerator GCodeGen { get; private set; }
      #endregion

      #region Properties
      public List<SanityTestData> SanityTests = [];
      public List<(string DINFileHead1, string DINFileHead2)> DINFiles = [];
      #endregion

      #region Action Methods
      /// <summary>
      /// This method loads the Fx file into the DB.
      /// </summary>
      /// <param name="partName">Complete part name with address</param>
      /// <exception cref="Exception">An exception is thrown if it is invalid</exception>
      public void LoadPart (string partName) {
         Part = Part.Load (partName);
         if (Part.Info.MatlName == "NONE")
            Part.Info.MatlName = "1.0038";

         if (Part.Model == null) {
            if (Part.Dwg != null) Part.FoldTo3D ();
            else if (Part.SurfaceModel != null)
               Part.SheetMetalize ();
            else
               throw new Exception ("Invalid part");
         }

         GenesysHub.Workpiece = new Workpiece (Part.Model, Part);
      }

      public ArgumentNullException GetArgumentNullException () {
         return new (nameof (GCodeGen), "SanityCheck.Run: GCodeGen is null");
      }

      /// <summary>
      /// This method Runs the G Code generator for the tests specified in testList
      /// </summary>
      /// <param name="testList">The input list of tests to be run to generate g code</param>
      /// <param name="baselineDir">The path to the base line directory (for file comparison)</param>
      /// <param name="forceRun">Sanity Test structure has an option <c>ToRun</c>. ForceRun optional
      /// variable overrules it.</param>
      /// <returns>List of Bool values, to say if the tests are successfully run. <c>True</c> if successful,
      /// <c>False</c> otherwise</returns>
      /// <exception cref="ArgumentNullException"></exception>
      /// <exception cref="Exception"></exception>
      public List<bool> Run (List<SanityTestData> testList, string baselineDir,
                             ArgumentNullException argumentNullException,
                             bool forceRun = false) {
         if (GCodeGen == null)
            throw argumentNullException;

         if (testList.Count == 0)
            throw new Exception ("SanityCheck.Run: testList is empty");

         int idx = 0;
         DINFiles = [.. Enumerable.Repeat ((string.Empty, string.Empty), testList.Count)];
         List<bool> runStats = [.. Enumerable.Repeat (false, testList.Count)];
         foreach (SanityTestData test in testList) {
            if (!test.ToRun && !forceRun) continue;
            try {
               // GCode generator reset options
               GCodeGen.ResetForTesting (test.MCSettings);

               // Part loading, aligning, and cutting
               LoadPart (test.FxFileName);
               GenesysHub.Workpiece.Align ();
               if (test.MCSettings.CutHoles)
                  GenesysHub.Workpiece.DoAddHoles ();

               if (test.MCSettings.CutMarks)
                  GenesysHub.Workpiece.DoTextMarking (test.MCSettings);

               if (test.MCSettings.CutNotches || test.MCSettings.CutCutouts)
                  GenesysHub.Workpiece.DoCutNotchesAndCutouts ();

               GenesysHub.Workpiece.DoSorting ();

               // Compute G Code
               Utils.ComputeGCode (GCodeGen, testing: true);
               var headData = ((GCodeGen.DINFileNameHead1, GCodeGen.DINFileNameHead2));
               DINFiles[idx] = headData;
               var diff = Diff (baselineDir, idx, launchWinmerge: false);
               if (!diff)
                  runStats[idx] = true;
            } catch (Exception) { }

            idx++;
         }
         return runStats;
      }

      /// <summary>
      /// This file is the entry point to perform the file compare
      /// </summary>
      /// <param name="baselineDir">The path to the baseline files.</param>
      /// <param name="index">The index of the DINFiles[]</param>
      /// <param name="launchWinmerge">Optional parameter to launch WinMerge. This is <c>False</c> by default</param>
      /// <returns>Returns <c>false</c> if there are no changes between the files and the baseline; otherwise, returns <c>true</c>. </returns>
      public bool Diff (string baselineDir, int index, bool launchWinmerge = false) {
         // Further more, compare the DIN with baseline and populate the runStats.
         string DINFilenameHead1 = "", DINFilenameHead2 = "";
         if (GCodeGen.Heads == MCSettings.EHeads.Both) {
            DINFilenameHead1 = Path.GetFileName (DINFiles[index].DINFileHead1);
            DINFilenameHead2 = Path.GetFileName (DINFiles[index].DINFileHead2);
         } else if (GCodeGen.Heads == MCSettings.EHeads.Left)
            DINFilenameHead1 = Path.GetFileName (DINFiles[index].DINFileHead1);
         else if (GCodeGen.Heads == MCSettings.EHeads.Right)
            DINFilenameHead2 = Path.GetFileName (DINFiles[index].DINFileHead2);

         string head1DINBaselineAbsFile = "", head2DINBaselineAbsFile = "";
         if (!string.IsNullOrEmpty (DINFiles[index].DINFileHead1))
            head1DINBaselineAbsFile = Path.Combine (baselineDir, "Head1", DINFilenameHead1);

         if (!string.IsNullOrEmpty (DINFiles[index].DINFileHead2))
            head2DINBaselineAbsFile = Path.Combine (baselineDir, "Head2", DINFilenameHead2);

         var res = CheckDINs (head1DINBaselineAbsFile, DINFiles[index].DINFileHead1,
                              head2DINBaselineAbsFile, DINFiles[index].DINFileHead2,
                              launchWinmerge);
         return res;
      }

      /// <summary>
      /// This is a wrapoper method to perform the file compare between the baseline
      /// and the test files. First, the files texts are compared. If they differ
      /// </summary>
      /// <param name="baselineDINFileHead1">baseline file Head1</param>
      /// <param name="testDINFileHead1">Test file Head1</param>
      /// <param name="baselineDINFileHead2">Baseline file Head2</param>
      /// <param name="testDINFileHead2">Test file Head2</param>
      /// <param name="launchWinmerge">Optional parameter to launch WinMerge. This is <c>False</c> by default</param>
      /// <returns>Returns <c>false</c> if there are no changes between the files and the baseline; otherwise, returns <c>true</c>. </returns>
      bool CheckDINs (string baselineDINFileHead1, string testDINFileHead1,
                      string baselineDINFileHead2, string testDINFileHead2,
                      bool launchWinmerge = false) {
         if (!System.IO.File.Exists (baselineDINFileHead1)
               && System.IO.File.Exists (testDINFileHead1)) {
            string directoryPath = Path.GetDirectoryName (baselineDINFileHead1);
            if (!System.IO.Directory.Exists (directoryPath)) {
               System.IO.Directory.CreateDirectory (directoryPath);
            }
            System.IO.File.Copy (testDINFileHead1, baselineDINFileHead1);
         }

         if (!System.IO.File.Exists (baselineDINFileHead2)
               && System.IO.File.Exists (testDINFileHead2)) {
            string directoryPath = Path.GetDirectoryName (baselineDINFileHead2);
            if (!System.IO.Directory.Exists (directoryPath)) {
               System.IO.Directory.CreateDirectory (directoryPath);
            }
            System.IO.File.Copy (testDINFileHead2, baselineDINFileHead2);
         }
         string reftextH1 = System.IO.File.ReadAllText (baselineDINFileHead1),
                testtextH1 = System.IO.File.ReadAllText (testDINFileHead1),
                reftextH2 = System.IO.File.ReadAllText (baselineDINFileHead2),
                testtextH2 = System.IO.File.ReadAllText (testDINFileHead2);
         bool res = false;

         if (reftextH1 != testtextH1 || reftextH2 != testtextH2) {
            res = true;
            if (launchWinmerge)
               DoDINCompare (baselineDINFileHead1, testDINFileHead1, reftextH1 != testtextH1,
                             baselineDINFileHead2, testDINFileHead2, reftextH2 != testtextH2);
         }
         return res;
      }

      /// <summary>
      /// This method performs a comparison between the G Code files and the baseline under the
      /// following condition
      /// </summary>
      /// <param name="reference1">The path to the reference G Code file (baseline) for Head1</param>
      /// <param name="testfile1">The path to the test G Code file to be compared for Head2</param>
      /// <param name="reference2">The path to the reference G Code file (baseline) for Head1</param>
      /// <param name="testfile2">The path to the test G Code file to be compared for Head2</param>
      /// <returns>Returns <c>false</c> if there are no changes between the G Code files and the baseline; otherwise, returns <c>true</c>.</returns>
      bool DoDINCompare (string reference1, string testfile1, bool isHead1Changed, string reference2, string testfile2, bool isHead2Changed) {
         bool res = false;
         string winmergePath = IsFileComparerInstalled ();

         if (!string.IsNullOrEmpty (winmergePath)) {

            if (!System.IO.File.Exists (winmergePath))
               throw new Exception ("WINMERGE_NOT_INSTALLED");
            if (isHead1Changed) {
               res |= LaunchWinMerge (winmergePath, reference1, testfile1);
            }
            if (isHead2Changed) {
               res |= LaunchWinMerge (winmergePath, reference2, testfile2);
            }
         }
         return res;
      }

      /// <summary>
      /// This method launches the WinMerge application to compare the files.
      /// </summary>
      /// <param name="path">winmerge exe path</param>
      /// <param name="referenceFile">Base file to compare against</param>
      /// <param name="testFile">File to compare</param>
      /// <returns>A status boolean</returns>
      /// <exception cref="Exception">throws exception When launching fails</exception>
      static bool LaunchWinMerge (string path, string referenceFile, string testFile) {
         ProcessStartInfo startInfo = new () {
            FileName = path,
            Arguments = $"/e /u /dl \"Reference\" /dr \"Test\" \"{referenceFile}\" \"{testFile}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
         };
         try {
            using (Process process = Process.Start (startInfo)) {
               process.WaitForExit ();
               return process.ExitCode == 1 || process.ExitCode == 2 || process.ExitCode == -1;
            }
         } catch (Exception) {
            throw new Exception ("WINMERGE_LAUNCH_FAILED");
         }
      }

      /// <summary>
      /// This is an utility method which checks if the FC_REG_DIFF_COMPARE env variable
      /// is set to TRUE and if the winmergeu.exe is available.
      /// </summary>
      /// <returns>The path to the WinMergeU.exe</returns>
      public static string IsFileComparerInstalled () {
         string pathEnv = Environment.GetEnvironmentVariable ("PATH") ?? throw new Exception ("PATH_ENV_DOESNT_EXIST");

         // Split the PATH environment variable into individual directories
         string[] paths = pathEnv.Split (Path.PathSeparator);

         // Try to find WinMergeU.exe in each directory
         string winMergePath = null;
         foreach (string path in paths) {
            string potentialPath = Path.Combine (path, "WinMergeU.exe");
            if (System.IO.File.Exists (potentialPath)) {
               winMergePath = potentialPath;
               break;
            }
         }

         if (winMergePath == null)
            throw new Exception ("WINMERGE_NOT_FOUND");

         return winMergePath;
      }
      #endregion
   }
}