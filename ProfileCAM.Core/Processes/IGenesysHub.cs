using ProfileCAM.Core.GCodeGen;
using ProfileCAM.Core.Tools;
using ProfileCAM.Core;
using System.ComponentModel;

namespace ProfileCAM.Core.Processes;
public interface IGenesysHub : INotifyPropertyChanged {
   // Traces
   List<List<GCodeSeg>> Traces { get; }
   List<List<GCodeSeg>[]> CutScopeTraces { get; }
   void ClearTraces ();

   // Workpiece & Tool
   Workpiece? Workpiece { get; set; }
   Nozzle? MachiningTool { get; set; }

   // GCode Generator properties
   MCSettings.PartConfigType PartConfigType { get; set; }
   bool Cutouts { get; set; }
   bool CutHoles { get; set; }
   bool CutMark { get; set; }
   bool CutNotches { get; set; }
   bool CutWeb { get; set; }
   bool CutFlange { get; set; }
   MCSettings.EHeads Heads { get; set; }
   double PartitionRatio { get; set; }
   double NotchWireJointDistance { get; set; }

   // GCode Generator access
   IGCodeGenerator GCodeGen { get; }

   // Methods
   void ClearZombies ();
   void LoadGCode (string filename);
   void ResetGCodeGenForTesting ();
   void ComputeGCode (bool testing /*= false*/);
}