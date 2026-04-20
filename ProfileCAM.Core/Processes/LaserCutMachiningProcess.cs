using System.ComponentModel;
using ProfileCAM.Core.Tools;
using Flux.API;
using ProfileCAM.Core.GCodeGen.LCMMultipass2HLegacy;

namespace ProfileCAM.Core.Processes {
   /// <summary>GenesysHub is used to generate G-Code, and the Traces for simulation</summary>
   #nullable enable
   public class GenesysHub : INotifyPropertyChanged {
      #region G Code Drawables and Utilities
      List<List<GCodeSeg>> mTraces = [[], []];
      public List<List<GCodeSeg>> Traces { get => mTraces; }
      public List<List<GCodeSeg>[]> CutScopeTraces { get => mGCodeGenerator.CutScopeTraces; }

      public void ClearTraces () {
         mTraces[0]?.Clear ();
         mTraces[1]?.Clear ();
         CutScopeTraces?.Clear ();
      }
      #endregion

      #region Digital Twins - Resources and Workpiece
      Workpiece? mWorkpiece;
      public Workpiece? Workpiece {
         get => mWorkpiece;
         set {
            if (mWorkpiece != value) {
               mWorkpiece = value;
               mGCodeGenerator.OnNewWorkpiece ();
            }
         }
      }
      Nozzle? mMachiningTool;
      public Nozzle? MachiningTool { get => mMachiningTool; set => mMachiningTool = value; }
      #endregion

      #region Property changed event handlers
      public event PropertyChangedEventHandler? PropertyChanged;
      protected void OnPropertyChanged (string propertyName) {
         PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
      }
      #endregion

      #region Constructor
      public GenesysHub () {
         MachiningTool = new Nozzle (9.0, 100.0, 100);
         mGCodeGenerator = new GCodeGenerator (this, true/* Left to right machining*/);
         mGCodeParser = new GCodeParser ();
         CutHoles = true;
         CutMark = true;
         CutNotches = true;
         Cutouts = true;
         CutWeb = true;
         CutFlange = true;
      }
      #endregion

      #region GCode generator properties
      public MCSettings.PartConfigType PartConfigType {
         get => mGCodeGenerator.PartConfigType;
         set => mGCodeGenerator.PartConfigType = value;
      }
      public bool Cutouts { get => mGCodeGenerator.Cutouts; set => mGCodeGenerator.Cutouts = value; }
      public bool CutHoles { get => mGCodeGenerator.CutHoles; set => mGCodeGenerator.CutHoles = value; }
      public bool CutMark { get => mGCodeGenerator.CutMarks; set => mGCodeGenerator.CutMarks = value; }
      public bool CutNotches { get => mGCodeGenerator.CutNotches; set => mGCodeGenerator.CutNotches = value; }
      public bool CutWeb { get => mGCodeGenerator.CutWeb; set => mGCodeGenerator.CutWeb = value; }
      public bool CutFlange { get => mGCodeGenerator.CutFlange; set => mGCodeGenerator.CutFlange = value; }
      public MCSettings.EHeads Heads { get => mGCodeGenerator.Heads; set => mGCodeGenerator.Heads = value; }
      public double PartitionRatio { get => mGCodeGenerator.PartitionRatio; set => mGCodeGenerator.PartitionRatio = value; }
      public double NotchWireJointDistance { get => mGCodeGenerator.NotchWireJointDistance; set => mGCodeGenerator.NotchWireJointDistance = value; }
      #endregion

      #region GCode Generator and Utilities
      GCodeParser mGCodeParser;
      readonly GCodeGenerator mGCodeGenerator;
      public GCodeGenerator GCodeGen { get => mGCodeGenerator; }
      public void ClearZombies () {
         ClearTraces ();
         mGCodeGenerator.ClearZombies ();
      }
      public void LoadGCode (string filename) {
         try {
            mGCodeParser.Parse (filename);
         } catch (Exception e) {
            string formattedString = String.Format ("Parsing GCode file {0} failed. Error: {1}", filename, e.Message);
            throw new Exception (formattedString);
         }
         mTraces[0] = mGCodeParser.Traces[0];
         mTraces[1] = mGCodeParser.Traces[1];
      }

      public void ResetGCodeGenForTesting () => mGCodeGenerator?.ResetForTesting (MCSettings.It);

      /// <summary>Uses GenesysHub to generate code, and to generate the simulation traces</summary>
      /// If 'testing' is set to true, we reset the settings to a known stable value
      /// used for testing, and create always a partition at 0.5. Otherwise, we use a 
      /// dynamically computed optimal partitioning
      //public void ComputeGCode (bool testing = false, double ratio = 0.5) {
      public void ComputeGCode (bool testing = false) {
         ClearZombies ();
         mTraces = Utils.ComputeGCode (mGCodeGenerator, testing);
      }
      #endregion
   }
}