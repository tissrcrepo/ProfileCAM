using Flux.API;
using ProfileCAM.Core.Geometries;
using ProfileCAM.Core.Processes;
using static ProfileCAM.Core.MCSettings;
using static ProfileCAM.Core.Utils;

namespace ProfileCAM.Core.GCodeGen;
#nullable enable
public interface IGCodeGenerator {
   public enum ToolHeadType {
      Master,
      Slave
   }
   void InitializeToolingBlock (Tooling toolingItem, Tooling prevToolingItem, /*double frameFeed,*/
         double xStart, double xPartition, double xEnd, List<ToolingSegment> segs, bool isValidNotch, bool isFlexCut, bool isLast,
         int startIndex, int endIndex, ToolingSegment? nextTs);

   void RapidMoveToPiercingPosition (Point3 startPoint, Vector3 direction, EKind kind, bool usePingPongOption, string comment);

   void PrepareforToolApproach (Tooling toolingItem, List<ToolingSegment> toolingSegments,
       ToolingSegment? prevToolingSegment, Tooling? prevToolingItem,
       List<ToolingSegment> prevToolingSegs, bool isFirstTooling, bool isValidNotch, Tuple<Point3, Vector3>? notchEntry);

   void WriteToolCorrectionData (Tooling toolingItem);
   void WritePlaneForCircularMotionCommand (bool isFromWebFlange, bool isNotchCut);
   void WriteToolDiaCompensation (bool isFlexTooling);
   void EnableMachiningDirective ();
   void DisableMachiningDirective ();
   ToolingSegment? WriteTooling (List<ToolingSegment> segments, Tooling toolingItem, bool relativeCoords);
   void FinalizeToolingBlock (Tooling toolingItem, double prevCutToolingsLength, double totalToolingCutLength);
   public void WriteWireJointTrace (
      ToolingSegment wjtSeg,
      ToolingSegment? nextSeg,
      Vector3 scrapSideNormal,
      Point3 lastPosition,
      double notchApproachDistance,
      ref EPlane prevPlaneType,
      EFlange currFlangeType,
      Tooling toolingItem,
      ref double blockCutLength,
      double totalToolingsCutLength,
      double xStart,
      double xPartition,
      double xEnd,
      bool isFlexCut,
      bool isValidNotch,
      ToolingSegment? flexRefTS,
      out Point3? prevRapidPos,
      bool toCompleteToolingBlock = false,
      string comment = "Wire Joint Jump Trace",
      bool relativeCoords = false,
      bool firstWJTTrace = true
      );

   public void InitializeNotchToolingBlock (Tooling toolingItem, Tooling prevToolingItem,
         List<ToolingSegment> segs, Vector3 segmentNormal, /*double frameFeed,*/
         double xStart, double xPartition, double xEnd, bool isFlexCut, bool isLast,
         bool isValidNotch, int startIndex/*-1*/, int endIndex/*-1*/,
         int refSegIndex/*0*/, string comment/*""*/, bool isShortPerimeterNotch/*false*/,
         ToolingSegment? nextTs/* = null*/);
   public void InitializeNotchToolingBlock (Tooling toolingItem, Tooling prevToolingItem,
         List<Point3> points, Vector3 segmentNormal, /*double frameFeed,*/double xStart,
         double xPartition, double xEnd, bool isFlexCut, bool isLast, ToolingSegment? refSeg,
         ToolingSegment? nextTs,
         bool isValidNotch, string comment /*= ""*/);

   public void MoveToNextTooling (
       Vector3 prevToolingEndNormal,
       ToolingSegment? prevToolingEndSegment,
       Point3 nextToolingStartPoint,
       Vector3 nextToolingStartNormal,
       string prevToolingItemName,
       string nextToolingItemName,
       bool firstTime,
       EKind featType,
       bool usePingPongOption /*= true*/);

   public void WriteLineStatement (string st);
   public void MoveToMachiningStartPosition (Point3 toolingStartPosition, Vector3 toolingStartNormal, string toolingName);
   public void WriteCurve (ToolingSegment segment, string toolingName, bool relativeCoords/* = false*/,
         Point3? refStPt/* = null*/);

   public void FinalizeNotchToolingBlock (Tooling toolingItem,
         double cutLength, double totalCutLength);

   public Tuple<Point3, Vector3> GetLastToolHeadPosition ();

   public void WriteFlexLineSeg (
       ToolingSegment ts,
       bool isWJTStartCut,
       string toolingName,
       ToolingSegment? flexRefSeg/* = null*/,
       string lineSegmentComment/* = ""*/);

   public XForm4 GetXForm ();
   public void WriteLineSeg (Point3 stPoint, Point3 endPoint, Vector3 startNormal, Vector3 endNormal,
        string toolingName, bool relativeCoords/* = false*/, Point3? refStPt /*= null*/);

   public void WriteLineSeg (
       Point3 stPoint,
       Point3 endPoint,
       Vector3 startNormal,
       Vector3 endNormal,
       EPlane currPlaneType,
       EPlane previousPlaneType,
       EFlange currFlangeType,
       string toolingName,
       string lineSegmentComment /*= ""*/,
       bool relativeCoords /*= false*/,
       Point3? refStPoint /*= null*/);

   public void MoveToRetract (Point3 endPt, Vector3 endNormal, string toolingName);

   public void CreatePartition (List<Tooling> cuts, bool optimize, Bound3 bound);
   public void AllocateCutScopeTraces (int nCutScopes);
   public int GenerateGCode (ToolHeadType head, List<MachinableCutScope> mcCutScopes);
   public int GenerateGCode (ToolHeadType head);
   public void OnNewWorkpiece ();
   public void ClearZombies ();
   public void ResetForTesting (MCSettings mcs);
   public void SetFromMCSettings ();
   public void ResetBookKeepers ();

   // Add other methods used by Hole as needed...
   bool CreateDummyBlock4Master { get; }
   bool RapidMoveToPiercingPositionWithPingPong { get; set; }
   public double ApproachLength { get; set; }
   public double NotchWireJointDistance { get; set; }
   public IGenesysHub Process { get; set; }
   public double MinCutOutLengthThreshold { get; set; }
   public string NotchCutStartToken { get; set; }
   public string NotchCutEndToken { get; set; }
   public double JobInnerRadius { get; set; }
   public double JobThickness { get; set; }
   public PartConfigType PartConfigType { get; set; }
   public List<GCodeSeg>[] Traces { get; set; }
   public MCSettings.EHeads Heads { get; set; }
   public double DeadbandWidth { get; set; }
   public double MaxFrameLength { get; set; }
   public bool MaximizeFrameLengthInMultipass { get; set; }
   public double PartitionRatio { get; set; }
   public bool OptimizePartition { get; set; }
   public int BlockNumber { get; set; }
   public bool EnableMultipassCut { get; set; }
   public List<List<GCodeSeg>[]> CutScopeTraces { get; }
   public List<List<GCodeSeg>[]> FrameTraces { get; }
   public MCSettings GCodeGenSettings { get; set; }
   public bool Cutouts {  get; set; }
   public bool CutHoles {  get; set; }
   public bool CutMarks {  get; set; }
   public bool CutNotches {  get; set; }
   public bool CutWeb {  get; set; }
   public bool CutFlange {  get; set; }
   public string DINFileNameHead1 { get; set; }
   public string DINFileNameHead2 { get; set; }
   public List<MachinableCutScope> MachinableCutScopes { get; set; }

}