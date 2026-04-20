using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ProfileCAM.Core.Geometries;
using ProfileCAM.Core.Processes;
using ProfileCAM.Core.Optimizer;
using Flux.API;
using static ProfileCAM.Core.MCSettings;
using static ProfileCAM.Core.Utils;
using ProfileCAM.Core.GCodeGen.GCodeFeatures;

namespace ProfileCAM.Core.GCodeGen.LCMMultipass2HNoDB {
   /// <summary>
   /// The following class parses any G Code and caches the G0 and G1 segments. Work is 
   /// still in progress to read G2 and G3 segments. The processor has to be set with 
   /// the Traces to simulate. Currently, only one G Code (file) can be used for simulation.
   /// </summary>
   public class GCodeParser {
      #region Data Members
      XForm4 mXformLH, mXformRH;
      Point3? mLHOrigin, mRHOrigin;
      int? mHead;
      #endregion

      #region Properties
      readonly List<GCodeSeg>[] mTraces = [[], []];
      public List<GCodeSeg>[] Traces { get => mTraces; }
      double? mJobLength;
      public double? JobLength { get => mJobLength; set => mJobLength = value; }
      double? mJobWidth;
      public double? JobWidth { get => mJobWidth; set => mJobWidth = value; }
      double? mJobThickness;
      public double? JobThickness { get => mJobThickness; set => mJobThickness = value; }
      bool? mLHComponent = true;
      public bool? LHComponent { get => mLHComponent; set { mLHComponent = value; } }
      #endregion

      #region Constructor(s)
      public GCodeParser () { }
      #endregion

      #region Data Processor(s)
      void EvaluateMachineXFm () {
         // For LH component
         mXformLH = new XForm4 ();
         mXformLH.Translate (new Vector3 (0.0, -JobWidth.Value / 2.0, 0.0));

         // For RH component
         mXformRH = new XForm4 ();
         mXformRH.Translate (new Vector3 (0.0, JobWidth.Value / 2.0, 0.0));
      }
      public static string GetGCodeComment (string comment) => " ( " + comment + " ) ";
      #endregion

      #region Lifecyclers
      public void ClearZombies () {
         mTraces[0].Clear ();
         mTraces[1].Clear ();
      }
      #endregion

      #region Parser(s)
      public void Parse (string filePath) {
         var fileLines = File.ReadAllLines (filePath);
         double lastX = 0, lastY = 0, lastZ = 0, lastAngle = 0;
         double iic = 0, jjc = 0, kkc = 0;
         Vector3 lastNormal = XForm4.mZAxis;
         bool firstEntry = true;
         string arcPlane = "XY";
         bool initTime = true;
         foreach (var line in fileLines) {
            if (line.StartsWith ("G18")) arcPlane = "XZ";
            if (line.StartsWith ("G17")) arcPlane = "XY";

            // Variables init
            double x = lastX, y = lastY, z = lastZ, angle = lastAngle;
            Vector3 normal = lastNormal;

            string cncIdPattern = @"CNC_ID\s*=\s*(\d+)";
            Match cncIdMatch = Regex.Match (line, cncIdPattern, RegexOptions.IgnoreCase);
            if (cncIdMatch.Success) {
               mHead = int.Parse (cncIdMatch.Groups[1].Value) - 1;
               if (mHead != 0 && mHead != 1)
                  throw new Exception ("Undefined head (tool)");
               mTraces[mHead.Value].Clear ();
            }
            {
               string jobLengthPattern = @"Job_Length\s*=\s*(\d+)";
               Match jobLengthMatch = Regex.Match (line, jobLengthPattern, RegexOptions.IgnoreCase);
               if (jobLengthMatch.Success) {
                  JobLength = int.Parse (jobLengthMatch.Groups[1].Value);
                  if (!JobLength.HasValue)
                     throw new Exception ("Job length can not be inferred from Din file");
               }
            }
            {
               string jobWidthPattern = @"Job_Width\s*=\s*(\d+)";
               Match jobWidthMatch = Regex.Match (line, jobWidthPattern, RegexOptions.IgnoreCase);
               if (jobWidthMatch.Success) {
                  JobWidth = int.Parse (jobWidthMatch.Groups[1].Value);
                  if (!JobWidth.HasValue)
                     throw new Exception ("Job Width can not be inferred from Din file");
               }
            }
            {
               string jobThicknessPattern = @"Job_Thickness\s*=\s*(\d+)";
               Match jobThicknessMatch = Regex.Match (line, jobThicknessPattern, RegexOptions.IgnoreCase);
               if (jobThicknessMatch.Success) {
                  JobThickness = int.Parse (jobThicknessMatch.Groups[1].Value);
                  if (!JobThickness.HasValue)
                     throw new Exception ("Job Thickness can not be inferred from Din file");
               }
            }
            if (initTime && JobLength.HasValue && JobWidth.HasValue && JobThickness.HasValue) {
               mLHOrigin = new Point3 (0.0, -JobWidth.Value / 2, JobThickness.Value);
               mRHOrigin = new Point3 (JobLength.Value, JobWidth.Value / 2, JobThickness.Value);
               EvaluateMachineXFm ();
               initTime = false;
            }

            string jobTypePattern = @"Job_Type\s*=\s*(\d+)";
            Match jobTypeMatch = Regex.Match (line, jobTypePattern, RegexOptions.IgnoreCase);
            if (jobTypeMatch.Success) {
               var job_type = int.Parse (jobTypeMatch.Groups[1].Value);
               if (job_type == 1) LHComponent = true;
               else if (job_type == 2) LHComponent = false;
               else throw new Exception
                     ("Undefined Part Configuration [ Job_Type should either be 1 (LHComponent) or 2 (RHComponent)]");
            }

            // Regular expression to match G followed by 0, 1, 2, or 3 with optional whitespace (spaces, tabs)
            string gPattern = @"G\s*([0-9]+)";
            Match gMatch = Regex.Match (line, gPattern, RegexOptions.IgnoreCase);
            EGCode eGCodeVal;
            if (gMatch.Success) {
               //axisValues["G"] = double.Parse (gMatch.Groups[1].Value);
               var gval = int.Parse (gMatch.Groups[1].Value);
               eGCodeVal = gval switch {
                  0 => EGCode.G0,
                  1 => EGCode.G1,
                  2 => EGCode.G2,
                  3 => EGCode.G3,
                  _ => EGCode.None
               };

               // Regular expression to match X, Y, Z, A, B, C, I, J, K, F followed by optional
               // whitespace (spaces, tabs) and then a number
               string axisPattern = @"([XYZABCIJKxyzabcijkfF])\s*([-+]?\d+(\.\d+)?)";
               MatchCollection axisMatches = Regex.Matches (line, axisPattern);

               // Loop through all matches and add them to the dictionary
               foreach (Match match in axisMatches) {
                  string axis = match.Groups[1].Value.ToUpper ();
                  double value = double.Parse (match.Groups[2].Value);
                  //axisValues[axis] = value;
                  switch (axis[0]) {
                     case 'X': x = value; continue;
                     case 'Y': y = value; continue;
                     case 'Z': z = value; continue;
                     case 'I': iic = value; continue;
                     case 'J': jjc = value; continue;
                     case 'K': kkc = value; continue;
                     case 'A':
                        normal = new Vector3 (0, -Math.Sin (value.D2R ()), Math.Cos (value.D2R ()));
                        normal = new Vector3 (0, -Math.Sin (value.D2R ()), Math.Cos (value.D2R ()));
                        continue;
                     default:
                        continue;
                  }
               }
               string comment = string.Format ($"Din file {0}", filePath);
               comment = GetGCodeComment (comment);
               if (!LHComponent.HasValue) throw new Exception ("Unable to find the part configuration. LHComponent or RHComponent");
               if (!mHead.HasValue) throw new Exception ("Unable to find the Tool '0' or '1'");
               if (eGCodeVal == EGCode.G0 || eGCodeVal == EGCode.G1) {
                  var point = new Point3 (x, y, z);
                  if (LHComponent.Value) point = Geom.V2P (mXformLH * point);
                  else point = Geom.V2P (mXformRH * point);
                  Point3 prevPoint;
                  if (firstEntry) {
                     prevPoint = mLHComponent.Value ? mLHOrigin.Value : mRHOrigin.Value;
                     firstEntry = false;
                  } else prevPoint = mTraces[mHead.Value][^1].EndPoint;

                  mTraces[mHead.Value].Add (new GCodeSeg (prevPoint, point,
                              lastNormal, normal, eGCodeVal, EMove.Machining, comment));
               } else if (eGCodeVal == EGCode.G2 || eGCodeVal == EGCode.G3) {
                  Point3 arcStartPoint = new (lastX, lastY, lastZ),
                     arcEndPoint = new (x, y, z), arcCenter;
                  if (arcPlane == "XY") arcCenter = new Point3 (arcStartPoint.X + iic, arcStartPoint.Y + jjc, z);
                  else arcCenter = new Point3 (arcStartPoint.X + iic, y, arcStartPoint.Z + kkc);

                  if (mLHComponent.Value) {
                     arcStartPoint = Geom.V2P (mXformLH * arcStartPoint);
                     arcEndPoint = Geom.V2P (mXformLH * arcEndPoint);
                     arcCenter = Geom.V2P (mXformLH * arcCenter);
                  } else {
                     arcStartPoint = Geom.V2P (mXformRH * arcStartPoint);
                     arcEndPoint = Geom.V2P (mXformRH * arcEndPoint);
                     arcCenter = Geom.V2P (mXformRH * arcCenter);
                  }

                  FCArc3 arc = null;
                  if (eGCodeVal == EGCode.G2) arc = Geom.CreateArc (arcStartPoint, arcEndPoint, arcCenter, normal, Utils.EArcSense.CW);
                  else arc = Geom.CreateArc (arcStartPoint, arcEndPoint, arcCenter, normal, Utils.EArcSense.CCW);

                  var radius = (arcStartPoint - arcCenter).Length;
                  mTraces[mHead.Value].Add (new GCodeSeg (arc, arcStartPoint, arcEndPoint, arcCenter, radius, normal, eGCodeVal,
                        EMove.Machining, comment));
               }
            }
            lastX = x;
            lastY = y;
            lastZ = z;
            lastNormal = normal;
         }
      }
      #endregion
   }

   public class GCodeGenerator : IGCodeGenerator{
      #region Data Members
      List<E3Flex> mFlexes;      // Flexes in the workpiece
      List<E3Plane> mPlanes;     // Planes in this workpiece
      double mThickness;         // Workpiece thickness
      readonly Point3[] mToolPos = new Point3[2];     // Tool position (for each head)
      readonly Vector3[] mToolVec = new Vector3[2];   // Tool orientaton (for each head)
      readonly Point3[] mSafePoint = new Point3[2];
      //readonly bool mDebug = false;
      StreamWriter sw;
      bool mMachiningDirectiveSet = false;
      double mCurveLeastLength = 0.5;
      double[] mPercentLengths = [0.25, 0.5, 0.75];
      int mProgramNumber;
      int mCutScopeNo = 0;
      string NCName;
      bool mLastCutScope = false;
      ToolingSegment? mPrevToolingSegment = null;
      List<double> mCutscopeToolingLengths = [];
      public bool RapidMoveToPiercingPositionWithPingPong { get; set; }
      #endregion Data Members

      #region GCode Generator Properties
      public List<GCodeSeg>[] mTraces = [[], []];
      //public List<GCodeSeg>[] Traces => mTraces;
      GenesysHub mGHub;
      List<List<GCodeSeg>[]> mCutScopeTraces = [];
      public List<List<GCodeSeg>[]> CutScopeTraces => mCutScopeTraces;
      public GenesysHub Process { get => mGHub; set => mGHub = value; }
      List<NotchAttribute> mNotchAttributes = [];
      public List<NotchAttribute> NotchAttributes { get { return mNotchAttributes; } }
      public List<MachinableCutScope> MachinableCutScopes { get; private set; }
      //public List<List<Tooling>> Frames {  get; private set; }
      public int BlockNumber { get; set; } = 0;
      public bool CreateDummyBlock4Master { get; set; } = false;
      public string DINFileNameHead1 { get; set; }
      public string DINFileNameHead2 { get; set; }
      public double JobInnerRadius { get; set; }
      public double JobThickness { get; set; }
      public bool LeftToRightMachining { get; set; }
      public Dictionary<string, WorkpieceOptions> WPOptions { get; set; }

      #region Settings Properties
      public double ApproachLength { get { return GCodeGenSettings.ApproachLength; } set { GCodeGenSettings.ApproachLength = value; } }
      public bool CutFlange { get { return GCodeGenSettings.CutFlange; } set { GCodeGenSettings.CutFlange = value; } }
      public bool CutHoles { get { return GCodeGenSettings.CutHoles; } set { GCodeGenSettings.CutHoles = value; } }
      public bool CutMarks { get { return GCodeGenSettings.CutMarks; } set { GCodeGenSettings.CutMarks = value; } }
      public bool CutNotches { get { return GCodeGenSettings.CutNotches; } set { GCodeGenSettings.CutNotches = value; } }
      public bool Cutouts { get { return GCodeGenSettings.CutCutouts; } set { GCodeGenSettings.CutCutouts = value; } }
      public bool CutWeb { get { return GCodeGenSettings.CutWeb; } set { GCodeGenSettings.CutWeb = value; } }
      public double DeadbandWidth { get { return GCodeGenSettings.DeadbandWidth; } set { GCodeGenSettings.DeadbandWidth = value; } }
      public string DinFilenameSuffix { get { return GCodeGenSettings.DINFilenameSuffix; } set { GCodeGenSettings.DINFilenameSuffix = value; } }
      public bool DualFlangeCutoutNotchOnly { get { return GCodeGenSettings.DualFlangeCutoutNotchOnly; } set { GCodeGenSettings.DualFlangeCutoutNotchOnly = value; } }
      public bool EnableMultipassCut { get { return GCodeGenSettings.EnableMultipassCut; } set { GCodeGenSettings.EnableMultipassCut = value; } }
      public double FlexCuttingGap { get { return GCodeGenSettings.FlexCuttingGap; } set { GCodeGenSettings.FlexCuttingGap = value; } }
      public MCSettings.EHeads Heads { get { return GCodeGenSettings.Heads; } set { GCodeGenSettings.Heads = value; } }
      public double LeadInApproachArcAngle { get { return GCodeGenSettings.LeadInApproachArcAngle; } set { GCodeGenSettings.LeadInApproachArcAngle = value; } }
      public double LeastWJLength { get { return GCodeGenSettings.LeastWJLength; } set { GCodeGenSettings.LeastWJLength = value; } }
      public MachineType Machine { get { return GCodeGenSettings.Machine; } set { GCodeGenSettings.Machine = value; } }
      public ERotate MarkAngle { get { return GCodeGenSettings.MarkAngle; } set { GCodeGenSettings.MarkAngle = value; } }
      public string MarkText { get { return GCodeGenSettings.MarkText; } set { GCodeGenSettings.MarkText = value; } }
      public int MarkTextHeight { get { return GCodeGenSettings.MarkTextHeight; } set { GCodeGenSettings.MarkTextHeight = value; } }
      public double MarkTextPosX { get { return GCodeGenSettings.MarkTextPosX; } set { GCodeGenSettings.MarkTextPosX = value; } }
      public double MarkTextPosY { get { return GCodeGenSettings.MarkTextPosY; } set { GCodeGenSettings.MarkTextPosY = value; } }
      public double MaxFrameLength { get { return GCodeGenSettings.MaxFrameLength; } set { GCodeGenSettings.MaxFrameLength = value; } }
      public bool MaximizeFrameLengthInMultipass { get { return GCodeGenSettings.MaximizeFrameLengthInMultipass; } set { GCodeGenSettings.MaximizeFrameLengthInMultipass = value; } }
      public double MinCutOutLengthThreshold { get { return GCodeGenSettings.MinCutOutLengthThreshold; } set { GCodeGenSettings.MinCutOutLengthThreshold = value; } }
      public double MinNotchLengthThreshold { get { return GCodeGenSettings.MinNotchLengthThreshold; } set { GCodeGenSettings.MinNotchLengthThreshold = value; } }
      public double MinThresholdForPartition { get { return GCodeGenSettings.MinThresholdForPartition; } set { GCodeGenSettings.MinThresholdForPartition = value; } }
      public string NCFilePath { get { return GCodeGenSettings.NCFilePath; } set { GCodeGenSettings.NCFilePath = value; } }
      public double NotchApproachLength { get { return GCodeGenSettings.NotchApproachLength; } set { GCodeGenSettings.NotchApproachLength = value; } }
      public string NotchCutEndToken { get { return GCodeGenSettings.NotchCutEndToken; } set { GCodeGenSettings.NotchCutEndToken = value; } }
      public string NotchCutStartToken { get { return GCodeGenSettings.NotchCutStartToken; } set { GCodeGenSettings.NotchCutStartToken = value; } }
      public double NotchWireJointDistance { get { return GCodeGenSettings.NotchWireJointDistance; } set { GCodeGenSettings.NotchWireJointDistance = value; } }
      public bool OptimizePartition { get { return GCodeGenSettings.OptimizePartition; } set { GCodeGenSettings.OptimizePartition = value; } }
      public bool OptimizeSequence { get { return GCodeGenSettings.OptimizeSequence; } set { GCodeGenSettings.OptimizeSequence = value; } }
      public MCSettings.PartConfigType PartConfigType { get { return GCodeGenSettings.PartConfig; } set { GCodeGenSettings.PartConfig = value; } }
      public double PartitionRatio { get { return GCodeGenSettings.PartitionRatio; } set { GCodeGenSettings.PartitionRatio = value; } }
      public int ProgNo { get { return GCodeGenSettings.ProgNo; } set { GCodeGenSettings.ProgNo = value; } }
      public double SafetyZone { get { return GCodeGenSettings.SafetyZone; } set { GCodeGenSettings.SafetyZone = value; } }
      public uint SerialNumber { get { return GCodeGenSettings.SerialNumber; } set { GCodeGenSettings.SerialNumber = value; } }
      public bool SlotWithWJTOnly { get { return GCodeGenSettings.SlotWithWJTOnly; } set { GCodeGenSettings.SlotWithWJTOnly = value; } }
      public double Standoff { get { return GCodeGenSettings.Standoff; } set { GCodeGenSettings.Standoff = value; } }
      public EKind[] ToolingPriority { get { return GCodeGenSettings.ToolingPriority; } set { GCodeGenSettings.ToolingPriority = value; } }
      public bool UsePingPong { get { return GCodeGenSettings.UsePingPong; } set { GCodeGenSettings.UsePingPong = value; } }
      public string WorkpieceOptionsFilename { get { return GCodeGenSettings.WorkpieceOptionsFilename; } set { GCodeGenSettings.WorkpieceOptionsFilename = value; } }
      #endregion Settings Properties
      //public double LeastWJLength { get; set; }
      //public uint SerialNumber { get; set; }
      //public double PartitionRatio { get; set; }
      //public MCSettings.EHeads Heads { get; set; }
      //public int ProgNo { get; set; }
      //public ERotate MarkAngle { get; set; }
      //public bool OptimizeSequence { get; set; }
      //public bool OptimizePartition { get; set; }
      //public bool SlotWithWJTOnly { get; set; }
      //public bool DualFlangeCutoutNotchOnly { get; set; }
      //public double LeadInApproachArcAngle { get; set; }
      //public double SafetyZone { get; set; }
      //public EKind[] ToolingPriority { get; set; }
      //public double NotchWireJointDistance { get; set; } = 2.0;
      //public double NotchApproachLength { get; set; } = 5.0;
      //public bool Cutouts { get; set; } = true;
      //public bool CutNotches { get; set; } = true;
      //public bool CutMarks { get; set; } = true;
      //public bool CutWeb { get; set; } = true;
      //public bool CutFlange { get; set; } = true;
      //public bool CutHoles { get; set; } = true;
      //public bool EnableMultipassCut { get; set; }
      //public double MaxFrameLength { get; set; }
      //public bool MaximizeFrameLengthInMultipass { get; set; }
      //public bool LeftToRightMachining { get; set; }
      //public double MinThresholdForPartition { get; set; }
      //public double MinNotchLengthThreshold { get; set; }
      //public double MinCutOutLengthThreshold { get; set; }
      //public string DinFilenameSuffix { get; set; }
      //public string NotchCutStartToken { get; set; }
      //public string NotchCutEndToken { get; set; }
      //public MachineType Machine { get; set; }
      //public string NCFilePath { get; set; }
      //public string DINFileNameHead1 { get; set; }
      //public string DINFileNameHead2 { get; set; }
      //public string WorkpieceOptionsFilename { get; set; }
      //public Dictionary<string, WorkpieceOptions> WPOptions { get; set; }
      //public double DeadbandWidth { get; set; }
      //public int BlockNumber { get; set; } = 0;
      //public bool CreateDummyBlock4Master { get; set; } = false;
      //public double JobInnerRadius { get; set; }
      //public double JobThickness { get; set; }
      #endregion Generator Properties

      #region Lifecyclers
      public void ClearZombies () {
         mTraces[0].Clear ();
         mTraces[1].Clear ();
         CutScopeTraces?.Clear ();
         ResetBookKeepers ();
      }

      void WriteBlockType (Tooling toolingItem, ToolingSegment? ts, ToolingSegment? nextTs,
         bool isValidNotch, bool isFlexCut,
         //bool isToBeTreatedAsCutOut,
         bool edgeNotch = false) {
         if (isFlexCut && nextTs == null)
            throw new Exception ("Writing blocktype for flex / WJT's second block needs the first next segment of flex. Its null");
         if (edgeNotch) return;
         string comment = "";
         double blockType;
         var notchCutKind = toolingItem.NotchKind;
         var cutoutKind = toolingItem.CutoutKind;
         var isCutout = toolingItem.IsCutout (); /*|| isToBeTreatedAsCutOut;*/
         if (isCutout && cutoutKind == ECutKind.None)
            throw new Exception ("Feature to be treated as CutOut but CutOutKind is NONE");
         string gcodeSt = "";
         EFlange stNormalFlange, endNormalFlange, nextSegEndNormalFlange = EFlange.None;
         if (ts == null) throw new Exception ("Reference Tooling Segment NULL");
         stNormalFlange = Utils.GetFlangeType (ts.Value.Vec0, GetXForm ());
         endNormalFlange = Utils.GetFlangeType (ts.Value.Vec1, GetXForm ());

         var isFlangeCutout = isCutout && (cutoutKind == ECutKind.Top || cutoutKind == ECutKind.YPos || cutoutKind == ECutKind.YNeg);

         // for writing BlockType +/- 4.5 => Downwards ( from Web to flange )
         // for writing BlockType +/- 4.6 => Upwards ( from flange to web )
         MachiningSense machiningSense = MachiningSense.None;
         if (nextTs != null) {
            var isFlexCutSeg = Utils.IsFlexCutSegment (nextTs.Value);
            if (isFlexCutSeg) {
               nextSegEndNormalFlange = Utils.GetFlangeType (nextTs.Value.Vec1, GetXForm ());
               Vector3 refSegDir = (nextTs.Value.Curve.End - nextTs.Value.Curve.Start).Normalized ();
               if (refSegDir.IsSameSense (XForm4.mNegZAxis))
                  machiningSense = MachiningSense.Downward;
               else if (refSegDir.IsSameSense (XForm4.mZAxis))
                  machiningSense = MachiningSense.Upward;
               else
                  throw new Exception ("Error 1101");
            }
         }

         if (CreateDummyBlock4Master)
            gcodeSt = $"BlockType={10} ({comment})";
         else {
            if ((!isValidNotch && !isCutout) || isFlangeCutout) {
               if (Utils.GetFlangeType (toolingItem, GetXForm ()) == Utils.EFlange.Web) { // If any feature confined to only one flange
                  if (IsOppositeReference (toolingItem.Name)) {
                     blockType = -3;
                     comment += " Web Flange Hole - Opposite reference ";
                  } else {
                     blockType = 3;
                     comment += " Web Flange Hole";
                  }
               } else if (Utils.GetFlangeType (toolingItem, GetXForm ()) == Utils.EFlange.Bottom) {
                  blockType = 1;
                  comment += " Bottom Flange Cut";
               } else if (Utils.GetFlangeType (toolingItem, GetXForm ()) == Utils.EFlange.Top) {
                  blockType = 2;
                  comment += " Top Flange Cut";
               } else if (toolingItem.IsFlexHole () || !isValidNotch) {
                  blockType = 0;
               } else throw new Exception ("GCodeGenerator.WriteBlockType() Unrecognized feature type in gcode generation");
               gcodeSt = $"BlockType={blockType:F0} ({comment})";

            } else if (isValidNotch || isCutout) {
               var cutKind = ECutKind.None;
               if (isValidNotch)
                  cutKind = notchCutKind;
               if (isCutout) // THis is a cutout treated as notch
                  cutKind = cutoutKind;

               switch (cutKind) {
                  case ECutKind.Top:
                     comment = "Web Flange Notch";
                     if (isFlexCut)
                        throw new Exception ("There is no flex cut for web flange cuts");
                     gcodeSt = $"BlockType={4.0} ({comment})";
                     break;

                  case ECutKind.YPos: // Top Flange
                  case ECutKind.YPosFlex:
                     comment = "Top Flange Notch";
                     if (isFlexCut)
                        throw new Exception ("Error -100");
                     else
                        blockType = -4.9;
                     gcodeSt = $"BlockType={blockType:F1} ({comment})";
                     break;

                  case ECutKind.YNeg:
                  case ECutKind.YNegFlex: // Bottom flange
                     comment = "Bottom Flange Notch";
                     if (isFlexCut)
                        throw new Exception ("Error -101");

                     else
                        blockType = 4.9;
                     gcodeSt = $"BlockType={blockType:F1} ({comment})";
                     break;

                  case ECutKind.Top2YPos: // Web -> Top Flange Notch/Cutout
                  case ECutKind.Top2YNeg:
                     if (cutKind == ECutKind.Top2YPos)
                        comment = "Web -> Top Flange Notch";
                     else
                        comment = "Web -> Bottom Flange Notch";

                     // nextSegEndNormalFlange is that of next tooling segment. If none is given
                     // same segment's end normal is considered, else , next seg's end normal is considered
                     // This is meant to infer if the machining is gonna start machining on the flex ( upwards or downwards)
                     var endNormal = nextSegEndNormalFlange == EFlange.None ? endNormalFlange : nextSegEndNormalFlange;

                     if (stNormalFlange == EFlange.Top && endNormal == EFlange.Top)
                        blockType = -4.9;
                     else if (stNormalFlange == EFlange.Bottom && endNormal == EFlange.Bottom)
                        blockType = 4.9;
                     else if (stNormalFlange == EFlange.Web && endNormal == EFlange.Web) {
                        if (cutKind == ECutKind.Top2YPos)
                           blockType = -4.0;
                        else
                           blockType = 4.0;
                     } else if (stNormalFlange == EFlange.Web && endNormal == EFlange.TopFlex)
                        blockType = -4.5;
                     else if (stNormalFlange == EFlange.Web && endNormal == EFlange.BottomFlex)
                        blockType = 4.5;
                     else if (stNormalFlange == EFlange.TopFlex && endNormal == EFlange.TopFlex) {
                        if (machiningSense == MachiningSense.Downward)
                           blockType = -4.5;
                        else
                           blockType = -4.6;
                     } else if (stNormalFlange == EFlange.Top && endNormal == EFlange.TopFlex)
                        blockType = -4.6;
                     else if (stNormalFlange == EFlange.Bottom && endNormal == EFlange.BottomFlex)
                        blockType = 4.6;
                     else if (stNormalFlange == EFlange.BottomFlex && endNormal == EFlange.BottomFlex) {
                        if (machiningSense == MachiningSense.Downward)
                           blockType = 4.5;
                        else
                           blockType = 4.6;
                     } else
                        throw new Exception ("Unknown Flange type for toolsegment of the notch");
                     gcodeSt = $"BlockType={blockType:F1} ({comment})";
                     break;

                  case ECutKind.YNegToYPos:
                     //throw new Exception ("Bottom to Top flange notches/cutouts not yet supported");
                     /* TRIPLE_FLANGE_NOTCH */
                     stNormalFlange = Utils.GetArcPlaneFlangeType (toolingItem.Segs[0].Vec0, GetXForm ());
                     endNormalFlange = Utils.GetArcPlaneFlangeType (toolingItem.Segs[^1].Vec0, GetXForm ());
                     comment = "Bottom -> Web -> Top Flange Notch LH Comp";
                     if (PartConfigType == PartConfigType.LHComponent) {
                        if (stNormalFlange == EFlange.Flex || endNormalFlange == EFlange.Flex)
                           blockType = 7.5;
                        else if ((stNormalFlange == EFlange.Bottom || stNormalFlange == EFlange.Flex) && (endNormalFlange == EFlange.Top || endNormalFlange == EFlange.Flex))
                           blockType = 7.9;
                        else throw new Exception ("Unknown Flange type for toolsegment of the notch");
                        gcodeSt = $"BlockType={blockType:F1} ({comment})";
                     } else {
                        if (stNormalFlange == EFlange.Flex || endNormalFlange == EFlange.Flex)
                           blockType = -7.5;
                        else if ((stNormalFlange == EFlange.Bottom || stNormalFlange == EFlange.Flex) && (endNormalFlange == EFlange.Top || endNormalFlange == EFlange.Flex))
                           blockType = -7.9;
                        else throw new Exception ("Unknown Flange type for toolsegment of the notch");
                        gcodeSt = $"BlockType={blockType:F1} ({comment})";
                     }
                     break;
                  default:
                     break;
               }
            }
         }
         sw.WriteLine (gcodeSt);
      }
      public MCSettings GCodeGenSettings { get; set; }
      /// <summary>Resets the GCodeGenerator state to a known default, for testing</summary>
      /// There is a lot of state in the GCodeGenerator, like program numbers that
      /// keep incrementing forward. We need to reset all this state to some known defaults
      /// so that tests can be run. Otherwise, the tests become sequence dependent and if we 
      /// add or remove additional tests in the sequence, the program numbers for all subsequent
      /// parts will be incorrect, leading to spurious test failures
      public void ResetForTesting (MCSettings mcs) {
         ResetBookKeepers ();
         //GCodeGenSettings.Standoff = mcs.Standoff;
         //GCodeGenSettings.LeastWJLength = mcs.LeastWJLength;
         //FlexCuttingGap = mcs.FlexCuttingGap;
         //ApproachLength = mcs.ApproachLength;
         //UsePingPong = mcs.UsePingPong;
         //NotchApproachLength = mcs.NotchApproachLength;
         //NotchWireJointDistance = mcs.NotchWireJointDistance;
         //MarkText = mcs.MarkText;
         //MarkTextHeight = mcs.MarkTextHeight;
         //MarkTextPosX = mcs.MarkTextPosX;
         //MarkTextPosY = mcs.MarkTextPosY;
         //PartConfigType = mcs.PartConfig;
         //SerialNumber = mcs.SerialNumber;
         //PartitionRatio = mcs.PartitionRatio;
         //Heads = mcs.Heads;
         //ProgNo = mcs.ProgNo;
         //MarkAngle = mcs.MarkAngle;
         //ToolingPriority = mcs.ToolingPriority;
         //OptimizePartition = mcs.OptimizePartition;
         //SlotWithWJTOnly = mcs.SlotWithWJTOnly;
         //DualFlangeCutoutNotchOnly = mcs.DualFlangeCutoutNotchOnly;
         //LeadInApproachArcAngle = mcs.LeadInApproachArcAngle;
         //OptimizeSequence = mcs.OptimizeSequence;
         //SafetyZone = mcs.SafetyZone;
         //EnableMultipassCut = mcs.EnableMultipassCut;
         //MaxFrameLength = mcs.MaxFrameLength;
         //MaximizeFrameLengthInMultipass = mcs.MaximizeFrameLengthInMultipass;
         //CutHoles = mcs.CutHoles;
         //CutNotches = mcs.CutNotches;
         //Cutouts = mcs.CutCutouts;
         //CutMarks = mcs.CutMarks;
         //CutWeb = mcs.CutWeb;
         //CutFlange = mcs.CutFlange;
         //Machine = mcs.Machine;
         //MinThresholdForPartition = mcs.MinThresholdForPartition;
         //MinNotchLengthThreshold = mcs.MinNotchLengthThreshold;
         //MinCutOutLengthThreshold = mcs.MinCutOutLengthThreshold;
         //DinFilenameSuffix = mcs.DINFilenameSuffix;
         //NotchCutStartToken = mcs.NotchCutStartToken;
         //NotchCutEndToken = mcs.NotchCutEndToken;
         //NCFilePath = mcs.NCFilePath;
         //DINFileNameHead1 = "";
         //DINFileNameHead2 = "";
         //WorkpieceOptionsFilename = mcs.WorkpieceOptionsFilename;
         //DeadbandWidth = mcs.DeadbandWidth;
         GCodeGenSettings = mcs;

         // Following ovverriders
         if (Heads == EHeads.Left || Heads == EHeads.Right) PartitionRatio = 1.0;
         LeftToRightMachining = true;

         GCodeGenSettings = mcs;
      }
      public void ResetBookKeepers () {
         mPgmNo[Utils.EFlange.Web] = 3000;
         mPgmNo[Utils.EFlange.Top] = 2000;
         mPgmNo[Utils.EFlange.Bottom] = 1000;
         mContourProgNo = ContourProgNo;
         mNotchProgNo = NotchProgNo;
         mMarkProgNo = MarkProgNo;
         mHashProgNo.Clear ();
         mLastCutScope = false;
      }
      #endregion

      #region GCode BookKeepers
      readonly HashSet<int> mHashProgNo = [];
      bool mWebFlangeOnly = false;
      const int WebCCNo = 3, FlangeCCNo = 2;
      int mContourProgNo = ContourProgNo;
      int mNotchProgNo = NotchProgNo;
      int mMarkProgNo = MarkProgNo;
      const int ContourProgNo = 5000;
      const int NotchProgNo = 4000;
      const int MarkProgNo = 8000;
      const int DigitProg = 6000, DigitConst = 1000, DigitPitch = 7;

      // As we are outputting two head and we need to maintain program number
      // save program number in a dictionary so that it can be used while writing
      // cutting head 2 program number
      readonly Dictionary<Utils.EFlange, int> mPgmNo = new () {
         [Utils.EFlange.Web] = 3000,
         [Utils.EFlange.Top] = 2000,
         [Utils.EFlange.Bottom] = 1000
      };
      int mBaseBlockNo = 1000;
      int mNo = 0;
      int GetNotchProgNo () => mNotchProgNo;
      int GetStartMarkProgNo () => mMarkProgNo;
      int GetProgNo (Tooling item) {
         if (OptimizeSequence) return mNo++;
         if (item.IsNotch ()) return mNotchProgNo++;
         else if (item.IsCutout () || item.IsFlexHole ()) return mContourProgNo++;
         else if (item.IsMark ()) return mMarkProgNo++;
         else return mPgmNo[Utils.GetFlangeType (item, GetXForm ())];
      }

      public string BlockNumberMark { get; set; } = "";
      void OutN (StreamWriter sw, string comment = "") {
         int nNo;
         if (EnableMultipassCut) nNo = mCutScopeNo * mBaseBlockNo + BlockNumber;
         else nNo = BlockNumber;
         string line = $"N{nNo}" +
                 (string.IsNullOrEmpty (comment)
                     ? ""
                     : $"\t( {comment} )");
         BlockNumberMark = $"N{nNo}";
         sw.WriteLine (line);
         sw.WriteLine ($"BlockID={nNo}");
         BlockNumber++;
      }
      #endregion

      #region GCode Options
      const double Rapid = 8000;
      bool IsSingleHead => !OptimizePartition && (PartitionRatio.EQ (0.0) ||
                             PartitionRatio.EQ (1.0));
      bool IsSingleHead1 => IsSingleHead && PartitionRatio.EQ (1.0);

      readonly double mSafeClearance = 28.0;
      readonly double mRetractClearance = 20.0;
      //readonly double[] mControlDiameter = [14.7];
      #endregion

      #region Partition Data members
      /// <summary>The X-partition location</summary>
      double mXSplit;
      #endregion

      #region Tool Configuration data
      ToolHeadType mHead = ToolHeadType.Master;
      public ToolHeadType Head { get => mHead; set => mHead = value; }
      //static XForm4 Utils.sXformLHInv = null;
      //static XForm4 Utils.sXformRHInv = null;
      //public static XForm4 LHCSys {
      //   get {
      //      Utils.sXformLHInv ??= new ();
      //      var csys = Utils.sXformLHInv.InvertNew ();
      //      //Matrix3 coorsysM3 = new Matrix3 (csys[0, 0], csys[0, 1], csys[0, 2], csys[0, 3],
      //      //   csys[1, 0], csys[1, 1], csys[1, 2], csys[1, 3],
      //      //   csys[2, 0], csys[2, 1], csys[2, 2], csys[2, 3],
      //      //   csys[3, 0], csys[3, 1], csys[3, 2], csys[3, 3]);
      //      //return coorsysM3;
      //      return csys;
      //   }
      //}
      //public static XForm4 RHCSys {
      //   get {
      //      Utils.sXformLHInv ??= new ();
      //      var csys = Utils.sXformRHInv.InvertNew ();
      //      //Matrix3 coorsysM3 = new Matrix3 (csys[0, 0], csys[0, 1], csys[0, 2], csys[0, 3],
      //      //   csys[1, 0], csys[1, 1], csys[1, 2], csys[1, 3],
      //      //   csys[2, 0], csys[2, 1], csys[2, 2], csys[2, 3],
      //      //   csys[3, 0], csys[3, 1], csys[3, 2], csys[3, 3]);
      //      //return coorsysM3;
      //      return csys;
      //   }
      //}
      #endregion

      #region Enums and Types
      enum EToolingShape {
         /// <summary>Circle hole</summary>
         Circle,
         HoleShape,
         NotchStart,
         NotchGiveWay,
         Notch,
         Cutout,
         /// <summary>Left segment of notch</summary>
         NotchL,
         NotchL2,
         /// <summary>Right segment of notch</summary>
         NotchR,
         NotchR2,
         Text,
         // If there is no hole in a flange, add this dummy
         HoleSubstituteLine,
         CutOutStart,
         CutOutStart2,
         CutOutYNeg,
         CutOutYPos,
         CutOutEnd,
         // Used to bring cutting head to top
         SerialStartNo,
         Arc,
         Others,
      }

      public enum ToolHeadType {
         Master,
         Slave
      }
      #endregion

      #region Constructors and constructing utilities
      public GCodeGenerator (GenesysHub gHub, bool isLeftToRight) {
         mGHub = gHub;
         SetFromMCSettings ();
         MCSettings.It.OnSettingValuesChangedEvent += SetFromMCSettings;
         mTraces = [[], []];
         LeftToRightMachining = isLeftToRight;
         DinFilenameSuffix = "";
         NotchCutStartToken = "";
         NotchCutEndToken = "";
      }

      /// <summary>
      /// The following method sets the properties that are local to the 
      /// GCodeGenerator. Note: Any changes to the global MCSettings properties
      /// will trigger the following method, as MCSettings.It.OnSettingValuesChangedEvent 
      /// is subscribed with SetFromMCSettings;
      /// </summary>
      public void SetFromMCSettings () {
         GCodeGenSettings = MCSettings.It;

         DINFileNameHead1 = "";
         DINFileNameHead2 = "";

         // Following ovverriders
         if (Heads == EHeads.Left || Heads == EHeads.Right) PartitionRatio = 1.0;

         //Standoff = MCSettings.It.Standoff;
         //LeastWJLength = MCSettings.It.LeastWJLength;
         //FlexCuttingGap = MCSettings.It.FlexCuttingGap;
         //ApproachLength = MCSettings.It.ApproachLength;
         //UsePingPong = MCSettings.It.UsePingPong;
         //NotchApproachLength = MCSettings.It.NotchApproachLength;
         //NotchWireJointDistance = MCSettings.It.NotchWireJointDistance;
         //MarkText = MCSettings.It.MarkText;
         //MarkTextHeight = MCSettings.It.MarkTextHeight;
         //MarkTextPosX = MCSettings.It.MarkTextPosX;
         //MarkTextPosY = MCSettings.It.MarkTextPosY;
         //PartConfigType = MCSettings.It.PartConfig;
         //SerialNumber = MCSettings.It.SerialNumber;
         //PartitionRatio = MCSettings.It.PartitionRatio;
         //Heads = MCSettings.It.Heads;
         //ProgNo = MCSettings.It.ProgNo;
         //MarkAngle = MCSettings.It.MarkAngle;
         //ToolingPriority = MCSettings.It.ToolingPriority;
         //OptimizePartition = MCSettings.It.OptimizePartition;
         //SlotWithWJTOnly = MCSettings.It.SlotWithWJTOnly;
         //DualFlangeCutoutNotchOnly = MCSettings.It.DualFlangeCutoutNotchOnly;
         //LeadInApproachArcAngle = MCSettings.It.LeadInApproachArcAngle;
         //SafetyZone = MCSettings.It.SafetyZone;
         //EnableMultipassCut = MCSettings.It.EnableMultipassCut;
         //MaxFrameLength = MCSettings.It.MaxFrameLength;
         //MaximizeFrameLengthInMultipass = MCSettings.It.MaximizeFrameLengthInMultipass;
         //CutHoles = MCSettings.It.CutHoles;
         //CutNotches = MCSettings.It.CutNotches;
         //Cutouts = MCSettings.It.CutCutouts;
         //CutMarks = MCSettings.It.CutMarks;
         //CutWeb = MCSettings.It.CutWeb;
         //CutFlange = MCSettings.It.CutFlange;
         //Machine = MCSettings.It.Machine;
         //MinThresholdForPartition = MCSettings.It.MinThresholdForPartition;
         //MinNotchLengthThreshold = MCSettings.It.MinNotchLengthThreshold;
         //MinCutOutLengthThreshold = MCSettings.It.MinCutOutLengthThreshold;
         //DinFilenameSuffix = MCSettings.It.DINFilenameSuffix;
         //NotchCutStartToken = MCSettings.It.NotchCutStartToken;
         //NotchCutEndToken = MCSettings.It.NotchCutEndToken;
         //NCFilePath = MCSettings.It.NCFilePath;
         DINFileNameHead1 = "";
         DINFileNameHead2 = "";
         //WorkpieceOptionsFilename = MCSettings.It.WorkpieceOptionsFilename;
         //DeadbandWidth = MCSettings.It.DeadbandWidth;


      }
      GCodeGenerator () { }
      public void OnNewWorkpiece () {
         if (Process != null && Process.Workpiece != null && Process.Workpiece.Model != null) {
            mFlexes = [.. Process.Workpiece.Model.Flexes];
            mPlanes = [.. Process.Workpiece.Model.Entities.OfType<E3Plane> ()];
            mThickness = mPlanes[0].ThickVector.Length;
            mWebFlangeOnly = mFlexes.Count == 0;
            NCName = Process.Workpiece.NCFileName;
            JobInnerRadius = Process.Workpiece.Model.Flexes.First ().Radius;
            JobThickness = Process.Workpiece.Model.Flexes.First ().Thickness;
         }
      }
      #endregion

      #region Utilies for Tool Transformations
      public static void EvaluateToolConfigXForms (Workpiece work) {
         // For LH Component
         if (Utils.sXformLHInv == null || Utils.sXformRHInv == null) {
            Utils.sXformLHInv = new XForm4 ();
            Utils.sXformRHInv = new XForm4 ();
            var flangeThickness = work.Model.Entities.OfType<E3Plane> ().ToList ().First ().ThickVector.Length;
            Utils.sXformLHInv.Translate (new Vector3 (0.0, work.Bound.YMin, flangeThickness));
            //if (mcName == "LMMultipass2H")
            //Utils.sXformLHInv.SetRotationComponents (new Vector3 (-1, 0, 0), new Vector3 (0, -1, 0), new Vector3 (0, 0, 1));
            Utils.sXformLHInv.Invert ();
            // For RH component
            Utils.sXformRHInv.Translate (new Vector3 (0.0, work.Bound.YMax, flangeThickness));
            //if (mcName == "LMMultipass2H")
            //Utils.sXformRHInv.SetRotationComponents (new Vector3 (-1, 0, 0), new Vector3 (0, -1, 0), new Vector3 (0, 0, 1));
            Utils.sXformRHInv.Invert ();
         }
      }

      public static Point3 XfmToMachine (GCodeGenerator codeGen, Point3 ptWRTWCS) {
         Vector3 resVec;
         if (codeGen.PartConfigType == MCSettings.PartConfigType.LHComponent) resVec = Utils.sXformLHInv * ptWRTWCS;
         else resVec = Utils.sXformRHInv * ptWRTWCS;
         return Geom.V2P (resVec);
      }

      public Point3 XfmToMachine (Point3 ptWRTWCS) {
         Vector3 resVec;
         if (PartConfigType == MCSettings.PartConfigType.LHComponent) resVec = Utils.sXformLHInv * ptWRTWCS;
         else resVec = Utils.sXformRHInv * ptWRTWCS;
         return Geom.V2P (resVec);
      }

      public static XForm4 XfmToMachine (GCodeGenerator codeGen, XForm4 xFormWCS) {
         XForm4 mcXForm;
         if (codeGen.PartConfigType == MCSettings.PartConfigType.LHComponent) mcXForm = Utils.sXformLHInv * xFormWCS;
         else mcXForm = Utils.sXformRHInv * xFormWCS;
         return mcXForm;
      }

      public XForm4 XfmToMachine (XForm4 xFormWCS) {
         XForm4 mcXForm;
         if (PartConfigType == MCSettings.PartConfigType.LHComponent) mcXForm = Utils.sXformLHInv * xFormWCS;
         else mcXForm = Utils.sXformRHInv * xFormWCS;
         return mcXForm;
      }

      public static Vector3 XfmToMachineVec (GCodeGenerator codeGen, Vector3 vecWRTWCS) {
         Vector3 resVec;
         if (codeGen.PartConfigType == MCSettings.PartConfigType.LHComponent) resVec = Utils.sXformLHInv * vecWRTWCS;
         else resVec = Utils.sXformRHInv * vecWRTWCS;
         return resVec;
      }

      public Vector3 XfmToMachineVec (Vector3 vecWRTWCS) {
         Vector3 resVec;
         if (PartConfigType == MCSettings.PartConfigType.LHComponent) resVec = Utils.sXformLHInv * vecWRTWCS;
         else resVec = Utils.sXformRHInv * vecWRTWCS;
         return resVec;
      }
      #endregion

      #region Tooling filters (FChassisMachineSettings.Settings)
      //Utils.EFlange[] Utils.sFlangeCutPriority = [Utils.EFlange.Bottom, Utils.EFlange.Top, Utils.EFlange.Web, Utils.EFlange.Flex];

      public double GetXPartition (List<Tooling> cuts) {
         List<Tooling> resHead0 = [], resHead1 = [];
         if (!LeftToRightMachining) {
            resHead0 = [..cuts.Where (cut => cut.Head == 0)
         .OrderByDescending (cut => cut.Start.Pt.X)];
            resHead1 = [..cuts.Where (cut => cut.Head == 1)
         .OrderByDescending (cut => cut.Start.Pt.X)];
         } else {
            resHead0 = [..cuts.Where (cut => cut.Head == 0)
         .OrderBy (cut => cut.Start.Pt.X)];
            resHead1 = [..cuts.Where (cut => cut.Head == 1)
         .OrderBy (cut => cut.Start.Pt.X)];
         }
         double midX = -10;
         if (resHead0.Count > 0 && resHead1.Count > 0) {
            if (LeftToRightMachining) midX = (resHead0[^1].XMax + resHead1.First ().XMin) / 2.0;
            else midX = (resHead0[^1].XMin + resHead1.First ().XMax) / 2.0;
         } else if (resHead0.Count > 0) {
            if (LeftToRightMachining) midX = resHead0[^1].XMax;
            else midX = resHead0[^1].XMin;
         } else if (resHead1.Count > 0) {
            if (LeftToRightMachining) midX = resHead1[^1].XMax;
            else midX = resHead1[^1].XMin;
         }
         return midX;
      }

      /// <summary>
      /// This method orders the features according to the prescribed priorities.
      /// The priorities cannot be set in the UI since they are hard-coded for 
      /// a double-headed laser cutting machine. The priority is as follows:
      /// <list type="number">
      ///   <item>
      ///      <description><c>Bottom flange holes and single plane notches</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Top flange holes and single plane notches</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Web flange holes and single plane notches</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Bottom flange cutouts</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Top flange cutouts</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Web flange cutouts</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Bottom flange dual flange notches</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Top flange dual flange notches</c></description>
      ///   </item>
      ///   <item>
      ///      <description><c>Web flange dual flange notches</c></description>
      ///   </item>
      /// </list>
      /// </summary>
      /// <param name="cuts">The list of toolings to be ordered.</param>
      /// <param name="headNo">The head number specifying the laser head to use
      ///   <remarks>
      ///   <list type="bullet">
      ///      <item><description><c>0:</c> Master head</description></item>
      ///      <item><description><c>1:</c> Slave head</description></item>
      ///   </list>
      ///   </remarks>
      /// </param>
      /// <returns>The list of toolings ordered by the specified priority.</returns>
      //public List<Tooling> GetToolings4Head (List<Tooling> cuts, int headNo) {
      //   List<Tooling> res;
      //   if (!LeftToRightMachining)
      //      res = [..cuts.Where (cut => cut.Head == headNo)
      //   .OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenByDescending (cut => cut.Start.Pt.X)];
      //   else
      //      res = [..cuts.Where (cut => cut.Head == headNo && ( cut.Kind==EKind.Hole ||
      //      (cut.Kind==EKind.Notch && ( cut.ProfileKind == ECutKind.YPosFlex || cut.ProfileKind == ECutKind.YNegFlex ||
      //      cut.ProfileKind == ECutKind.Top || cut.ProfileKind == ECutKind.YPos || cut.ProfileKind == ECutKind.YNeg || /* TRIPLE_FLANGE_NOTCH */cut.ProfileKind == ECutKind.YNegToYPos))) )];
      //   res = [..res.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Order CutOuts
      //   var cutouts = (cuts.Where (cut => cut.Kind == EKind.Cutout));
      //   cutouts = [..cutouts.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Order dual flange notches
      //   var notches = cuts.Where (cut => cut.Kind == EKind.Notch && (cut.ProfileKind == ECutKind.Top2YNeg || cut.ProfileKind == ECutKind.Top2YPos));
      //   notches = [..notches.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Concat all
      //   res = [.. res, .. cutouts, .. notches];

      //   return res;
      //}
      //public List<Tooling> GetToolings4Head (List<Tooling> cuts, int headNo) {
      //   // New priorities are set as per task FCH-35
      //   List<Tooling> res, holes = [];
      //   if (!LeftToRightMachining) {
      //      res = [..cuts.Where (cut => cut.Head == headNo)
      //   .OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenByDescending (cut => cut.Start.Pt.X)];
      //      throw new Exception ("RightToLeftMachining requested. This means some options are not set correct");
      //   } else
      //      holes = [.. cuts.Where (cut => cut.Head == headNo && cut.Kind == EKind.Hole)];

      //   // Set priority by flange on which the features are present in Utils.sFlangeCutPriority
      //   holes = [..holes.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   //.ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Collect CutOuts, then order by by flange priority ( Utils.sFlangeCutPriority ),  then by ascending order of X
      //   var cutouts = (cuts.Where (cut => cut.Kind == EKind.Cutout && cut.Head == headNo));
      //   cutouts = [..cutouts.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   //.ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Collect single plane notches, then order by flange priority ( Utils.sFlangeCutPriority ),  then by ascending order of X
      //   var singlePlaneNotches = cuts.Where (cut => cut.Kind == EKind.Notch && cut.Head == headNo && cut.IsSingleFlangeTooling ());
      //   singlePlaneNotches = [..singlePlaneNotches.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Collect dual plane notches , then order by flange priority ( Utils.sFlangeCutPriority ),  then by ascending order of X
      //   var dualPlaneNotches = cuts.Where (cut => cut.Kind == EKind.Notch && cut.Head == headNo && cut.IsDualFlangeTooling ());
      //   dualPlaneNotches = [..dualPlaneNotches.OrderBy (cut => Array.IndexOf (Utils.sFlangeCutPriority, Utils.GetFlangeType (cut,PartConfigType==PartConfigType.LHComponent?Utils.sXformLHInv:Utils.sXformRHInv)))
      //   .ThenBy (cut => cut.Start.Pt.X)];

      //   // Concat all
      //   res = [.. holes, .. cutouts, .. singlePlaneNotches, .. dualPlaneNotches];

      //   return res;
      //}

      #endregion

      #region Partition Implementation 

      public static void CreatePartition (GCodeGenerator gcGen, List<ToolingScope> tss, bool optimize, Bound3 bound) {
         var toolings = tss.Select (ts => ts.Tooling).ToList ();
         gcGen.CreatePartition (toolings, optimize, bound);
      }

      /// <summary>This creates the optimal partition of holes so both heads are equally busy</summary>
      public void CreatePartition (List<Tooling> cuts, bool optimize, Bound3 bound) {
         if (Heads == EHeads.Left || Heads == EHeads.Right) {
            for (int ii = 0; ii < cuts.Count; ii++) {
               var cut = cuts[ii];
               if (Heads == EHeads.Left)
                  cut.Head = 0;
               else if (Heads == EHeads.Right)
                  cut.Head = 1;
               cuts[ii] = cut;
            }
            return;
         }
         double min = 0.1, max = 0.9, mid = 0;
         int count = 15;
         if (!optimize) {
            count = 1;
            min = max = PartitionRatio;
         }
         for (int i = 0; i < count; i++) {
            mid = (min + max) / 2;
            Partition (cuts, mid, bound);
            GetTimes (cuts, out double t0, out double t1);
            if (t0 > t1) max = mid; else min = mid;
         }
         mXSplit = bound.XMin + ((bound.XMax - bound.XMin) * mid);
         mXSplit = Math.Round (mXSplit, 0);

         if (Machine == MachineType.LCMMultipass2H && bound.XMax - mXSplit < MinThresholdForPartition) {
            for (int ii = 0; ii < cuts.Count; ii++) {
               var cut = cuts[ii];
               cut.Head = 0;
               cuts[ii] = cut;
            }
         }
      }

      /// <summary>This partitions the cuts with a given ratio, and sorts them</summary>
      void Partition (List<Tooling> cuts, double ratio, Bound3 bound) {
         //double xf = Math.Round (bound.XMax * ratio, 1);
         double xPartVal = bound.XMin + ((bound.XMax - bound.XMin) * ratio);
         double xf = Math.Round (xPartVal, 1);
         foreach (Tooling tooling in cuts) {
            if (IsSingleHead) {
               if (IsSingleHead1) {
                  if (Heads is MCSettings.EHeads.Left or MCSettings.EHeads.Both)
                     tooling.Head = 0;
                  else if (Heads is MCSettings.EHeads.Right or MCSettings.EHeads.Both)
                     tooling.Head = 1;
                  else throw new InvalidOperationException
                        ("Only single head detected while the tool configuration with Both tools prescribed!");
               }
            } else {
               if (tooling.End.Pt.X < xf)
                  tooling.Head = 0;
               else
                  tooling.Head = 1;
            }
         }
      }

      void GetTimes (List<Tooling> cuts, out double t0, out double t1) {
         t0 = t1 = 0;
         double tpierce = mThickness < 7 ? 0.1 : 0.3;

         for (int i = 0; i <= 1; i++) {
            double t = 0, fr = 1.8;
            if (mThickness < 7) fr = i == 0 ? 1 : 1.3;
            else fr = i == 0 ? 1.3 : 1;
            fr *= 1000 / 60.0;    // Convert to mm/sec

            cuts = [.. cuts.Where (a => a.Head == i)];
            if (cuts.Count == 0) {
               if (i == 0) t0 = 0;
               else t1 = 0;
               continue;
            }
            var pt = cuts[0].Start.Pt;
            double rrx = 1000, rry = 500;
            foreach (var cut in cuts) {
               // First, find the traverse path to this location, and add the
               // traverse time (simplified)
               var vec = cut.Start.Pt - pt;
               double tx = Math.Abs (vec.X) / rrx, ty = Math.Abs (vec.Y) / rry;
               t += Math.Max (tx, ty);

               // Then, add the pierce time
               t += 0.5;

               // Then, add the cutting time (accurate)
               t += cut.Perimeter / fr;
               pt = cut.End.Pt;
            }
            if (i == 0) t0 = t;
            else t1 = t;
         }
      }
      #endregion

      #region GCode Generation Methods
      /// <summary>
      /// This method writes M14 directive in G Code if a previous
      /// immediate M14 is not found and/or M15 is found
      /// Note: This M14 directive intimates the machine controller
      /// to start the machining process
      /// </summary>
      public void EnableMachiningDirective () {
         if (!mMachiningDirectiveSet && !CreateDummyBlock4Master)
            sw.WriteLine ("M14\t( ** Start of Cut **)");
         mMachiningDirectiveSet = true;
      }

      /// <summary>
      /// This method writes M15 directive in G Code if a 
      /// previous M14 is found.
      /// Note: The M15 directive intimates the machine controller
      /// to stop the machining process.
      /// </summary>
      public void DisableMachiningDirective () {
         if (mMachiningDirectiveSet && !CreateDummyBlock4Master)
            sw.WriteLine ("M15\t( ** End Of Cut ** )");
         mMachiningDirectiveSet = false;
      }

      /// <summary>
      /// This method is the entry point to write g code IF there is only one
      /// pass (feed) to the machine. This is legacy one.
      /// </summary>
      /// <param name="headNo">The head number specifying the laser head to use
      ///   <remarks>
      ///   <list type="bullet">
      ///      <item><description><c>0:</c> Master head</description></item>
      ///      <item><description><c>1:</c> Slave head</description></item>
      ///   </list>
      ///   </remarks>
      /// </param>
      /// <returns>Total tooling items (features) processed</returns>
      //public int GenerateGCode (ToolHeadType head) {
      //   MachinableCutScope mccss = new (Process.Workpiece.Cuts, this);
      //   if (CutScopeTraces.Count == 0) AllocateCutScopeTraces (1);
      //   BlockNumber = 0;
      //   return GenerateGCode (head, [mccss]);
      //}

      /// <summary>
      /// This method is an utility which allocates data structure to store
      /// the array of array of g codes.
      /// </summary>
      /// <param name="nCutScopes">Total number of Cut Scopes</param>
      public void AllocateCutScopeTraces (int nCutScopes) {
         mCutScopeTraces = [];
         for (int i = 0; i < nCutScopes; i++) {
            // Create a new List<GCodeSeg>[] to hold the GCodeSeg lists
            List<GCodeSeg>[] newCutScope = [[], []]; // Adjust the size based on your needs

            // Add the new array to mCutScopeTraces
            mCutScopeTraces.Add (newCutScope);
         }
      }

      /// <summary>
      /// This method writes the sequence header marking the token numbers
      /// start and end for each type of feature
      /// </summary>
      /// <param name="p">The flange</param>
      /// <param name="np">The number of pass (in multipass), 1 in the case of single pass</param>
      /// <param name="cnt">Incremented value for each feature</param>
      public void WriteNSequenceHeader (Utils.EFlange p, int np, int cnt) {
         int startValue;
         int endValue;
         if (!OptimizeSequence)
            startValue = mPgmNo[p] + (Machine == MachineType.LCMMultipass2H ? mBaseBlockNo : 0);
         else
            startValue = np + (Machine == MachineType.LCMMultipass2H ? mBaseBlockNo : 0);

         if (!OptimizeSequence)
            endValue = mPgmNo[p] + (Machine == MachineType.LCMMultipass2H ? mBaseBlockNo : 0);
         else
            endValue = np + (Machine == MachineType.LCMMultipass2H ? mBaseBlockNo : 0);
         string line = $"(N{startValue + 1} to N{cnt + endValue} in {p} flange)";
         sw.WriteLine (line);
      }
      Point3 firstTlgStartPoint;
      /// <summary>
      /// This method is the entry point for writing G Code both for LEGACY and 
      /// LCMMultipass2H machines. 
      /// <list type="bullet">
      ///   <item>
      ///     <description>0: Head - 1</description>
      ///   </item>
      ///   <item>
      ///     <description>1: Head - 2</description>
      ///   </item>
      /// </list>
      /// </summary>
      /// <param name="head">Cutting head identified by</param>
      /// <param name="mcCutScopes">The cut scopes to be processed</param>
      /// <returns>The generated G Code</returns>
      /// <exception cref="Exception">Throws an exception if an error occurs during G Code generation</exception>
      public int GenerateGCode (ToolHeadType head, List<MachinableCutScope> mcCutScopes) {
         var xPartition = mcCutScopes.Sum (cs => cs.ToolingScopesWidthH1);
         List<List<Tooling>> frames = [];
         foreach (var csc in mcCutScopes)
            frames.Add (csc.Toolings);
         var nToolingsWritten = GenerateGCode (head, frames, xPartition);
         if (nToolingsWritten > 0)
            MachinableCutScopes = mcCutScopes;
         return nToolingsWritten;
      }

      public int GenerateGCode (ToolHeadType head, List<List<Tooling>> frames, double xPartition) { // List<Tooling> is One Frame
         mCutscopeToolingLengths = [];
         Head = head;
         CreateDummyBlock4Master = false;

         string ncName = Utils.BuildDINFileName (Process.Workpiece.NCFileName, (int)Head, PartConfigType, DinFilenameSuffix);
         string ncFolder;
         // Output file name builder for G Code for both the heads
         if (head == ToolHeadType.Master) {
            ncFolder = Path.Combine (NCFilePath, "Head1");
            Directory.CreateDirectory (ncFolder);
            DINFileNameHead1 = Path.Combine (ncFolder, ncName);
         } else {
            ncFolder = Path.Combine (NCFilePath, "Head2");
            Directory.CreateDirectory (ncFolder);
            DINFileNameHead2 = Path.Combine (ncFolder, ncName);
         }
         // Initial preperatory header
         using (sw = new StreamWriter (head == 0 ? DINFileNameHead1 : DINFileNameHead2)) {
            sw.WriteLine ("%{1}({0})", ncName, ProgNo);
            sw.WriteLine ("N1");
            sw.WriteLine ($"CNC_ID={(int)Head + 1}");
            sw.WriteLine ($"Job_Length = {Math.Round (Process.Workpiece.Model.Bound.XMax, 1)}");
            sw.WriteLine ($"Job_Width = {Math.Round (Process.Workpiece.Model.Bound.YMax - Process.Workpiece.Model.Bound.YMin, 1)}");
            sw.WriteLine ("Job_Height = {0}\r\nJob_Thickness = {1}", Math.Round (Process.Workpiece.Model.Bound.ZMax - Process.Workpiece.Model.Bound.ZMin, 1), Math.Round (mThickness, 1));
            sw.WriteLine ($"X_Partition = {xPartition:F3}");

            // Output the outer radius
            if (JobInnerRadius.EQ (0))
               throw new Exception ("Job inner radius is ZERO.");
            if (JobThickness.EQ (0))
               throw new Exception ("Job thickness is ZERO.");
            double jobOuterRadius = JobInnerRadius + JobThickness;

            if (!mWebFlangeOnly) sw.WriteLine ($"Job_O_Radius = {Math.Round (jobOuterRadius, 1)}");

            // Output the lh or rh component
            sw.WriteLine ($"Job_Type  = {(PartConfigType == MCSettings.PartConfigType.LHComponent ? 1 : 2)}");
            if (!string.IsNullOrEmpty (MarkText)) {
               sw.WriteLine ($"Marking_X_Pos = {Math.Round (MarkTextPosX, 1)}");
               sw.WriteLine ($"Marking_Y_Pos = {Math.Round (MarkTextPosY, 1)}");
               int textRotAngle = MarkAngle switch {
                  ERotate.Rotate0 => 0,
                  ERotate.Rotate90 => 90,
                  ERotate.Rotate180 => 180,
                  ERotate.Rotate270 => 270,
                  _ => 0
               };
               sw.WriteLine ($"Marking_Angle = {textRotAngle}");
               sw.WriteLine ($"Marking_Height = {MarkTextHeight}");
               sw.WriteLine ($"G253 F=\"ModelTag:{MarkText}\" E0");
            }
            sw.WriteLine ("(BF-Soffset:S1, TF-Soffset:S2, WEB_BF-Soffset:S3, WEB_TF-Soffset:S4, Marking:S3)\r\n(Block No - BF:N1001~N1999, TF:N2001~N2999, WEB:N3001~N3999, Notch:N4001~N4999, " +
               "CutOut:N5001~N5999)" +
               "\r\n(BlockType - 0:Flange Holes, 1:Web Block with BF reference, -1:Web Block with TF reference, 2:Notch, 3:Cutout, 4:Marking)" +
               "\r\n(PM:Pierce Method, CM:Cutting Method, EM:End Method, ZRH: Z/Y Retract Height)\r\n(M50 - Sync On command, only in Tandem job Programs)\r\n(Job_TYPE - 1:LH JOB, 2:RH_JOB)\r\n" +
               "(X_Correction & YZ_Correction Limit +/-5mm)");
            sw.WriteLine (GetGCodeComment ($"Job Inner Radius = {JobInnerRadius:F3}"));
            sw.WriteLine (GetGCodeComment ($"Flex Cutting Gap = {FlexCuttingGap:F3}"));
            sw.WriteLine (GetGCodeComment ($"Multipass = {EnableMultipassCut && MultiPassCuts.IsMultipassCutTask (Process.Workpiece.Model)}"));
            //sw.WriteLine (GetGCodeComment ($"Least Wire Joint Length = {LeastWJLength}"));
            sw.WriteLine (GetGCodeComment ($"Version = {MCSettings.It.Version}"));
            sw.WriteLine ("(---Don't alter above Parameters---)");
            sw.WriteLine ();

            if (Heads == EHeads.Both) sw.WriteLine ("M50");
            sw.WriteLine ("M15");
            sw.WriteLine ("H=LaserTableID");
            sw.WriteLine ("G61\t( Stop Block Preparation )");
            sw.WriteLine (GetGCodeApplyToolDiaCompensation ());
            sw.WriteLine (GetGCodeComment ($" Cutting with {Head} head "));
            foreach (var toolings in frames)
               mCutscopeToolingLengths.Add (GetTotalToolingsLength (toolings));
            var totalToolingsLen = mCutscopeToolingLengths.Sum ();
            string ncname = NCName;
            if (ncname.Length > 20) ncname = ncname[..20];
            double totalMarkLength = 0;
            sw.WriteLine ($"G253 E0 F=\"0=1:{ncname}:{Math.Round (totalToolingsLen, 2)}," +
                   $"{Math.Round (totalMarkLength, 2)}\"");
            sw.WriteLine ($"G20 X=BlockID");

            // ****************************************************************************
            // Logic to change scope goes here.
            // 0. Create partition for multipass tooling ( sets head 0 or 1 )
            // 1. Get all the toolings within a scope
            // 2. Pass them into the following methods
            //*****************************************************************************

            // Instead of getting one cuts for head0 and head1, in the case of MULTIPASS we should get
            // List<cuts> for head0 and another list<cuts> for head1
            List<Tooling> totalCuts = [];
            double xStart = 0;
            double xEnd = 0;
            mCutScopeNo = 0; int cnnt = 0;
            mLastCutScope = false;
            BlockNumber = 1;

            for (int mm = 0; mm < frames.Count; mm++) {
               // CreateDummyBlock4Master Variable to signal the g code writer
               // if no G-statements is to be output, if the Slave head is
               // machining and master head is waiting from the start
               CreateDummyBlock4Master = false;
               mPrevToolingSegment = null;
               var frame = frames[mm];
               cnnt++;

               if (Head == ToolHeadType.Master && mm == frames.Count - 2 && frames[mm + 1].Count == 0) mLastCutScope = true;
               else if (Head == ToolHeadType.Slave && mm == frames.Count - 2 && frames[mm + 1].Count == 0) mLastCutScope = true;
               else if (cnnt == frames.Count) mLastCutScope = true;

               Bound3 cutScopeBound = Utils.CalculateBound3 (frame);
               (xStart, xEnd) = Tooling.GetScope (frame);
               //xStart = mcCutScope.StartX; xEnd = mcCutScope.EndX;
               if ((xEnd - xStart).SGT (MaxFrameLength)) throw new Exception ($"The Cut scope length is greater than Max Frame Length:{MaxFrameLength}, " +
                  $"for the Cut Scope index {cnnt} starting from Tooling {frame.First ().Name}");
               mCutScopeNo++;
               mToolPos[0] = new Point3 (cutScopeBound.XMin, cutScopeBound.YMin, mSafeClearance);
               mToolPos[1] = new Point3 (cutScopeBound.XMax, cutScopeBound.YMax, mSafeClearance);
               mSafePoint[0] = new Point3 (cutScopeBound.XMin, cutScopeBound.YMin, 50);
               mSafePoint[1] = new Point3 (cutScopeBound.XMax, cutScopeBound.YMax, 50);

               // The machine (inverse) transforms are computed. This transformation matrix the 
               // key to perform all the computations
               EvaluateToolConfigXForms (Process.Workpiece);

               // Allocate toolings for each head. It is assumed that partitioning is 
               // already made.
               List<Tooling> cuts = [];
               var cutsHead1 = Utils.GetToolings4Head (frame, (int)ToolHeadType.Master, GCodeGenSettings);
               var cutsHead2 = Utils.GetToolings4Head (frame, (int)ToolHeadType.Slave, GCodeGenSettings);
               if (head == ToolHeadType.Master) cuts = cutsHead1;
               else if (head == ToolHeadType.Slave) cuts = cutsHead2;

               if ((cutsHead1.Count == 0 && cutsHead2.Count > 0 && Head == ToolHeadType.Master) ||
                  (Head == ToolHeadType.Master && Heads == EHeads.Right)) {
                  CreateDummyBlock4Master = true;
                  if (cutsHead2.Count > 0)
                     cuts = cutsHead2;
                  WriteLineStatement (GetGCodeComment (" Writing dummy block for master head 1 "));
               }
               if (cuts.Count == 0) continue;

               WriteCuts (cuts, cutScopeBound, xStart, ref xEnd, frame, totalCuts, mCutscopeToolingLengths[mm]);
            }
            // Re init Traces with first entry of CutScopeTraces
            sw.WriteLine ("\r\nN65535");
            sw.WriteLine ("EndOfJob");
            sw.WriteLine ("G99");
            //string headInfo = $"for Head{(int)Head + 1}";
            //MachinableCutScopes = mcCutScopes;
            //Frames = frames;
            return totalCuts.Count;
         }
      }
      /// <summary>
      /// This method prepares to write toolings and then writes the toolings
      /// </summary>
      /// <param name="cuts">List of toolings</param>
      /// <param name="cutScopeBound">The bounding box of the entire cutscope</param>
      /// <param name="xStart">The X Start of the cutscope bound</param>
      /// <param name="xEnd">The X End of the cutscope bounding</param>
      /// <param name="mcCutScope">The machinable cut scope, which contains the tooling scopes
      /// which inturn contains the tooling</param>
      /// <param name="totalCuts">Total toolings written</param>
      /// <exception cref="Exception">If the tooling X max of the is more than the 
      /// cutscope bound, an exception is thrown</exception>
      void WriteCuts (List<Tooling> cuts, Bound3 cutScopeBound, double xStart,
         ref double xEnd, MachinableCutScope mcCutScope, List<Tooling> totalCuts,
         double cutscopeToolingLength) {
         // GCode generation for all the eligible tooling starts here
         xEnd = cutScopeBound.XMax;
         foreach (var cut in cuts) {
            var cutBound = cut.Bound3;
            if (((double)cutBound.XMax).SGT (xEnd))
               throw new Exception ("Tooling's XMax is more than frame feed");

            // Recomputation of tool cut kind is needed to evaluate once again
            // for LH or RH component, if only LH and RH component was changed in settings
            // and gcode generation is intended
            if (cut.Kind == EKind.Cutout)
               cut.CutoutKind = Tooling.GetCutKind (cut, GetXForm ());
            else if (cut.Kind == EKind.Notch)
               cut.NotchKind = Tooling.GetCutKind (cut, GetXForm ());
            cut.ProfileKind = Tooling.GetCutKind (cut, XForm4.IdentityXfm, profileKind: true);
         }
         // Compute the splitPartition
         var xPartition = GetXPartition (mcCutScope.Toolings);

         // Actually write toolings to g code
         DoWriteCuts (cuts, cutScopeBound, xStart, xPartition, xEnd, shouldOutputDigit: false, cutscopeToolingLength);
         totalCuts.AddRange (cuts);

         // Update Traces for this cutscope
         if (CutScopeTraces[mCutScopeNo - 1][0].Count == 0)
            CutScopeTraces[mCutScopeNo - 1][0] = mTraces[0];
         if (CutScopeTraces[mCutScopeNo - 1][1].Count == 0)
            CutScopeTraces[mCutScopeNo - 1][1] = mTraces[1];
         mTraces = [[], []];
      }

      void WriteCuts (List<Tooling> cuts, Bound3 cutScopeBound, double xStart,
         ref double xEnd, List<Tooling> frame, List<Tooling> totalCuts,
         double cutscopeToolingLength) {
         // GCode generation for all the eligible tooling starts here
         xEnd = cutScopeBound.XMax;
         foreach (var cut in cuts) {
            var cutBound = cut.Bound3;
            if (((double)cutBound.XMax).SGT (xEnd))
               throw new Exception ("Tooling's XMax is more than frame feed");

            // Recomputation of tool cut kind is needed to evaluate once again
            // for LH or RH component, if only LH and RH component was changed in settings
            // and gcode generation is intended
            if (cut.Kind == EKind.Cutout)
               cut.CutoutKind = Tooling.GetCutKind (cut, GetXForm ());
            else if (cut.Kind == EKind.Notch)
               cut.NotchKind = Tooling.GetCutKind (cut, GetXForm ());
            cut.ProfileKind = Tooling.GetCutKind (cut, XForm4.IdentityXfm, profileKind: true);
         }
         // Compute the splitPartition
         var xPartition = GetXPartition (frame);

         // Actually write toolings to g code
         DoWriteCuts (cuts, cutScopeBound, xStart, xPartition, xEnd, shouldOutputDigit: false, cutscopeToolingLength);
         totalCuts.AddRange (cuts);

         // Update Traces for this cutscope
         if (CutScopeTraces[mCutScopeNo - 1][0].Count == 0)
            CutScopeTraces[mCutScopeNo - 1][0] = mTraces[0];
         if (CutScopeTraces[mCutScopeNo - 1][1].Count == 0)
            CutScopeTraces[mCutScopeNo - 1][1] = mTraces[1];
         mTraces = [[], []];
      }

      /// <summary>
      /// The following method writes the program number to intimate the 
      /// GCode controller to make appropriate actions before starting to
      /// machine the upcoming we, top or bottom flanges
      /// </summary>
      /// <param name="toolingItem">The input tooling item</param>
      /// <param name="number">Ther block number. </param>
      public void SetProgNo (Tooling toolingItem, int number) {
         if (toolingItem.IsHole ()) mPgmNo[Utils.GetFlangeType (toolingItem, GetXForm ())] = number;
         else if (toolingItem.IsNotch ()) mNotchProgNo = number;
         else if (toolingItem.IsCutout () || toolingItem.IsFlexHole ()) mContourProgNo = number;
         else if (toolingItem.IsMark ()) mMarkProgNo = number;
      }

      /// <summary>
      /// The following method writes the program number to intimate the 
      /// GCode controller to make appropriate actions before starting to
      /// machine the upcoming we, top or bottom flanges</summary>
      /// <param name="toolingItem">The input tooling item</param>
      public void SetProgNo (Tooling toolingItem) {
         if (toolingItem.IsHole ()) mPgmNo[Utils.GetFlangeType (toolingItem, GetXForm ())] = mProgramNumber;
         else if (toolingItem.IsNotch ()) mNotchProgNo = mProgramNumber;
         else if (toolingItem.IsCutout () || toolingItem.IsFlexHole ()) mContourProgNo = mProgramNumber;
         else if (toolingItem.IsMark ()) mMarkProgNo = mProgramNumber;
      }

      /// <summary>
      /// This is a handy method to get the machine (inv) transform.
      /// This should be used when judging if a flange/plane is top or bottom
      /// and also while computing vector directions
      /// </summary>
      /// <returns>The machine (inverse) transform</returns>
      public XForm4 GetXForm () {
         if (Utils.sXformLHInv == null || Utils.sXformRHInv == null)
            GCodeGenerator.EvaluateToolConfigXForms (Process.Workpiece);
         return PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv : Utils.sXformRHInv;
      }

      /// <summary>
      /// This is a handy static method to get the machine (inv) transform.
      /// This should be used when judging if a flange/plane is top or bottom
      /// and also while computing vector directions. This mathod is to be
      /// called for vector direction computations, if the G Code generator is
      /// not yet been initialized</summary>
      /// <param name="wp">Workpiece object</param>
      /// <param name="gcGen">G Code generator object. Can be null also</param>
      /// <returns></returns>
      public static XForm4 GetXForm (Workpiece wp, GCodeGenerator gcGen = null) {
         ArgumentNullException.ThrowIfNull (wp);
         if (Utils.sXformLHInv == null || Utils.sXformRHInv == null)
            GCodeGenerator.EvaluateToolConfigXForms (wp);
         if (gcGen == null)
            return MCSettings.It.PartConfig == PartConfigType.LHComponent ? Utils.sXformLHInv : Utils.sXformRHInv;
         return gcGen.PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv : Utils.sXformRHInv;
      }

      /// <summary>
      /// This method is used to write a curve, which can either be a line or 
      /// an arc segment
      /// </summary>
      /// <param name="segment">The input tooling segment</param>
      /// <param name="toolingName">The input tooling name</param>
      /// <param name="isFlexSection">Flag, true if the tooling is on flex, 
      /// false otherwise</param>
      public void WriteCurve (ToolingSegment segment, string toolingName, bool relativeCoords = false,
         Point3? refStPt = null) {
         var stNormal = segment.Vec0.Normalized ();
         var endNormal = segment.Vec1.Normalized ();
         WriteCurve (segment.Curve, stNormal, endNormal, toolingName, relativeCoords: relativeCoords, refStPt: refStPt);
      }

      /// <summary>
      /// This method generates G Code for machining a curve, which can either be a line or
      /// an arc.
      /// </summary>
      /// <param name="curve">Curve to machine</param>
      /// <param name="stNormal">Normal at the start of the curve</param>
      /// <param name="endNormal">Normal at the end of the curve</param>
      /// <param name="toolingName">Tooling name (for simulation data)</param>
      /// <param name="isFlexSection">Flag, true if the tooling is on flex, 
      /// false otherwise</param>
      public void WriteCurve (FCCurve3 fcCurve, Vector3 stNormal, Vector3 endNormal,
         string toolingName, bool relativeCoords = false,
         Point3? refStPt = null) {
         /* current normal plane type (at the end) */
         var currPlaneType = Utils.GetArcPlaneType (endNormal, XForm4.IdentityXfm);
         if (fcCurve is FCLine3)
            WriteLineSeg (fcCurve.Start, fcCurve.End, stNormal, endNormal, toolingName, relativeCoords: relativeCoords, refStPt: refStPt);
         else if (fcCurve is FCArc3 fcArc) {
            var (cen, _) = Geom.EvaluateCenterAndRadius (fcArc);
            WriteArc (fcArc, currPlaneType,
               cen, stNormal, toolingName, relativeCoords, refStPt: refStPt);
         }
      }

      /// <summary>
      /// This method writes G Code machining for Arcs/Circles.
      /// </summary>
      /// <param name="arc"> The arc in 3d, to be machined thrrough</param>
      /// <param name="arcPlaneType">Arc plane type [Utils.EPlane] to derive the if the arc is G2 or 
      /// G3</param>
      /// <param name="arcFlangeType">Arc flange type [Utils.EFlange] to omit/include the appropriate 
      /// coordinates</param>
      /// <param name="arcCenter">Center of the arc</param>
      /// <param name="arcStartPoint">Arc start point</param>
      /// <param name="arcEndPoint">End point of the arc</param>
      /// <param name="startNormal">Start normal at the beginning of the arc. The arc is considered 
      /// planar and so the end normal
      /// is same as start normal</param>
      /// <param name="toolingName">Name of the tooling for simulation and debug</param>
      /// <exception cref="ArgumentException"></exception>
      public void WriteArc (FCArc3 fcArc, Utils.EPlane arcPlaneType, Utils.EFlange arcFlangeType,
         Point3 arcCenter, Point3 arcStartPoint, Point3 arcEndPoint, Vector3 startNormal,
         string toolingName, PointVec? flexRef = null, bool relativeCoords = false) {
         Utils.EArcSense arcType;
         var apn = arcPlaneType switch {
            Utils.EPlane.Top => XForm4.mZAxis,
            Utils.EPlane.YPos => XForm4.mYAxis,
            Utils.EPlane.YNeg => -XForm4.mYAxis,
            _ => throw new Exception ("Arc can not be written onflex plane")
         };
         var (_, arcSense) = Geom.GetArcAngleAndSense (fcArc, apn);

         // Both in YNeg and YPos plane, PLC is taking a different reference
         // Z axis is decreasing while moving from top according to Eckelmann controller
         // So need to reverse clockwise and counter clockwise option
         /*^ (arcPlaneType == FChassisUtils.EPlane.Top && !Options.ReverseY)*/
         if (arcSense == Utils.EArcSense.CCW) arcType = Utils.EArcSense.CCW;
         else arcType = Utils.EArcSense.CW;
         arcStartPoint = Utils.MovePoint (arcStartPoint, startNormal, Standoff);
         arcEndPoint = Utils.MovePoint (arcEndPoint, startNormal, Standoff);
         arcCenter = Utils.MovePoint (arcCenter, startNormal, Standoff);

         // Transform the arc end point to machine coordinate system
         var mcCoordArcCenter = XfmToMachine (arcCenter);
         var mcCoordArcStPoint = XfmToMachine (arcStartPoint);
         var mcCoordArcEndPoint = XfmToMachine (arcEndPoint);
         var mcCoordArcCenter2D = Utils.ToPlane (mcCoordArcCenter, arcPlaneType);
         var mcCoordArcStPoint2D = Utils.ToPlane (mcCoordArcStPoint, arcPlaneType);
         var mcCoordArcEndPoint2D = Utils.ToPlane (mcCoordArcEndPoint, arcPlaneType);
         var arcSt2CenVec = mcCoordArcCenter2D - mcCoordArcStPoint2D; // This gives I and J
         var radius = arcSt2CenVec.Length;

#if DEBUG_ROUND3
         arcSt2CenVec = arcSt2CenVec.Round (3);
#endif
         EGCode gCmd;
         if (arcType == Utils.EArcSense.CW) gCmd = EGCode.G2; else gCmd = EGCode.G3;
         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new GCodeSeg (fcArc, arcStartPoint, arcEndPoint, arcCenter, radius, startNormal,
               gCmd, EMove.Machining, toolingName));
            mToolPos[(int)Head] = arcEndPoint;
            mToolVec[(int)Head] = startNormal;
         }
         switch (arcFlangeType) {
            case Utils.EFlange.Web:
               if (Utils.IsArc (fcArc))
                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Y, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew ());
               else
                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Y, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew (),
                     machine: Machine,
                     mcCoordArcCenter, radius, mcCoordArcStPoint);
               break;
            case Utils.EFlange.Bottom:
               if (Utils.IsArc (fcArc))
                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew ());
               else
                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew (),
                     machine: Machine,
                     mcCoordArcCenter, radius, mcCoordArcStPoint);
               break;
            case Utils.EFlange.Top:
               if (Utils.IsArc (fcArc))
                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew ());
               else
                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew (),
                     machine: Machine,
                     mcCoordArcCenter, radius, mcCoordArcStPoint);
               break;
            default:
               throw new ArgumentException ("Arc is ill-defined perhaps on the flex");
         }
      }

      //      public void WriteArc (FCArc3 fcArc, Utils.EPlane arcPlaneType, Utils.EFlange arcFlangeType,
      //         Point3 arcCenter, Point3 arcStartPoint, Point3 arcEndPoint, Vector3 startNormal,
      //         string toolingName, PointVec? flexRef = null, bool relativeCoords = false) {
      //         Utils.EArcSense arcType;
      //         var apn = arcPlaneType switch {
      //            Utils.EPlane.Top => XForm4.mZAxis,
      //            Utils.EPlane.YPos => XForm4.mYAxis,
      //            Utils.EPlane.YNeg => -XForm4.mYAxis,
      //            _ => throw new Exception ("Arc can not be written onflex plane")
      //         };
      //         var (_, arcSense) = Geom.GetArcAngleAndSense (fcArc, apn);

      //         // Both in YNeg and YPos plane, PLC is taking a different reference
      //         // Z axis is decreasing while moving from top according to Eckelmann controller
      //         // So need to reverse clockwise and counter clockwise option
      //         /*^ (arcPlaneType == FChassisUtils.EPlane.Top && !Options.ReverseY)*/
      //         if (arcSense == Utils.EArcSense.CCW) arcType = Utils.EArcSense.CCW;
      //         else arcType = Utils.EArcSense.CW;
      //         arcStartPoint = Utils.MovePoint (arcStartPoint, startNormal, Standoff);
      //         arcEndPoint = Utils.MovePoint (arcEndPoint, startNormal, Standoff);
      //         arcCenter = Utils.MovePoint (arcCenter, startNormal, Standoff);

      //         // Transform the arc end point to machine coordinate system
      //         var mcCoordArcCenter = XfmToMachine (arcCenter);
      //         var mcCoordArcStPoint = XfmToMachine (arcStartPoint);
      //         var mcCoordArcEndPoint = XfmToMachine (arcEndPoint);
      //         var mcCoordArcCenter2D = Utils.ToPlane (mcCoordArcCenter, arcPlaneType);
      //         var mcCoordArcStPoint2D = Utils.ToPlane (mcCoordArcStPoint, arcPlaneType);
      //         var mcCoordArcEndPoint2D = Utils.ToPlane (mcCoordArcEndPoint, arcPlaneType);
      //         var arcSt2CenVec = mcCoordArcCenter2D - mcCoordArcStPoint2D; // This gives I and J
      //         var radius = arcSt2CenVec.Length;

      //#if DEBUG_ROUND3
      //         arcSt2CenVec = arcSt2CenVec.Round (3);
      //#endif
      //         EGCode gCmd;
      //         if (arcType == Utils.EArcSense.CW) gCmd = EGCode.G2; else gCmd = EGCode.G3;
      //         if (!CreateDummyBlock4Master) {
      //            mTraces[(int)Head].Add (new GCodeSeg (fcArc.Arc, arcStartPoint, arcEndPoint, arcCenter, radius, startNormal,
      //               gCmd, EMove.Machining, toolingName));
      //            mToolPos[(int)Head] = arcEndPoint;
      //            mToolVec[(int)Head] = startNormal;
      //         }
      //         switch (arcFlangeType) {
      //            case Utils.EFlange.Web:
      //               if (Utils.IsArc (fcArc.Arc))
      //                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Y, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
      //                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
      //                     PartConfigType == PartConfigType.LHComponent ? mXformLHInv.InvertNew () : mXformRHInv.InvertNew ());
      //               else
      //                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Y, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
      //                     PartConfigType == PartConfigType.LHComponent ? mXformLHInv.InvertNew () : mXformRHInv.InvertNew (),
      //                     machine: Machine,
      //                     mcCoordArcCenter, radius, mcCoordArcStPoint);
      //               break;
      //            case Utils.EFlange.Bottom:
      //               if (Utils.IsArc (fcArc.Arc))
      //                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
      //                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
      //                     PartConfigType == PartConfigType.LHComponent ? mXformLHInv.InvertNew () : mXformRHInv.InvertNew ());
      //               else
      //                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
      //                     PartConfigType == PartConfigType.LHComponent ? mXformLHInv.InvertNew () : mXformRHInv.InvertNew (),
      //                     machine: Machine,
      //                     mcCoordArcCenter, radius, mcCoordArcStPoint);
      //               break;
      //            case Utils.EFlange.Top:
      //               if (Utils.IsArc (fcArc.Arc))
      //                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
      //                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
      //                     PartConfigType == PartConfigType.LHComponent ? mXformLHInv.InvertNew () : mXformRHInv.InvertNew ());
      //               else
      //                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
      //                     PartConfigType == PartConfigType.LHComponent ? mXformLHInv.InvertNew () : mXformRHInv.InvertNew (),
      //                     machine: Machine,
      //                     mcCoordArcCenter, radius, mcCoordArcStPoint);
      //               break;
      //            default:
      //               throw new ArgumentException ("Arc is ill-defined perhaps on the flex");
      //         }
      //      }

      /// <summary>
      /// This method is used to write G Code for an arc segment
      /// </summary>
      /// <param name="arc">The input arc</param>
      /// <param name="arcPlaneType">The type of the plane, ( Top, YPos or YNeg)</param>
      /// <param name="arcCenter">Center of the arc</param>
      /// <param name="arcStartPoint">Start point of the Arc</param>
      /// <param name="arcEndPoint"></param>
      /// <param name="startNormal"></param>
      /// <param name="toolingName"></param>
      /// <exception cref="Exception">If arc is to be written on Flex section</exception>
      /// <exception cref="ArgumentException">If the arc is ill-defined</exception>
      void WriteArc (FCArc3 fcArc, Utils.EPlane arcPlaneType,
         Point3 arcCenter, Vector3 startNormal, string toolingName, bool relativeCoords = false,
         Point3? refStPt = null) {
         var arcStartPoint = fcArc.Start;
         var arcEndPoint = fcArc.End;
         Utils.EArcSense arcType;
         var apn = arcPlaneType switch {
            Utils.EPlane.Top => XForm4.mZAxis,
            Utils.EPlane.YPos => XForm4.mYAxis,
            Utils.EPlane.YNeg => -XForm4.mYAxis,
            _ => throw new Exception ("Arc can not be written onflex plane")
         };
         var (_, arcSense) = Geom.GetArcAngleAndSense (fcArc, apn);

         // Both in YNeg and YPos plane, PLC is taking a different reference
         // Z axis is decreasing while moving from top according to Eckelmann controller
         // So need to reverse clockwise and counter clockwise option
         /*^ (arcPlaneType == FChassisUtils.EPlane.Top && !Options.ReverseY)*/
         if (arcSense == Utils.EArcSense.CCW) arcType = Utils.EArcSense.CCW;
         else arcType = Utils.EArcSense.CW;
         arcStartPoint = Utils.MovePoint (arcStartPoint, startNormal, GCodeGenSettings.Standoff);
         arcEndPoint = Utils.MovePoint (arcEndPoint, startNormal, GCodeGenSettings.Standoff);
         arcCenter = Utils.MovePoint (arcCenter, startNormal, GCodeGenSettings.Standoff);

         // Transform the arc end point to machine coordinate system
         var mcCoordArcCenter = XfmToMachine (arcCenter);
         var mcCoordArcStPoint = XfmToMachine (arcStartPoint);
         var mcCoordArcEndPoint = XfmToMachine (arcEndPoint);
         var mcCoordArcCenter2D = Utils.ToPlane (mcCoordArcCenter, arcPlaneType);
         var mcCoordArcStPoint2D = Utils.ToPlane (mcCoordArcStPoint, arcPlaneType);
         var mcCoordArcEndPoint2D = Utils.ToPlane (mcCoordArcEndPoint, arcPlaneType);
         var actualMcCoordArcEndPoint2D = mcCoordArcEndPoint2D;
         if (relativeCoords) {
            if (refStPt != null) {
               var refStPt2D = Utils.ToPlane (XfmToMachine (refStPt.Value), arcPlaneType);
               mcCoordArcEndPoint2D = mcCoordArcEndPoint2D.Subtract (refStPt2D);
            } else
               mcCoordArcEndPoint2D = mcCoordArcEndPoint2D.Subtract (mcCoordArcStPoint2D);
         }

         // Vector from Start Point to the center of the arc
         var arcSt2CenVec = mcCoordArcCenter2D - mcCoordArcStPoint2D; // This gives I and J
         var radius = arcSt2CenVec.Length;

#if DEBUG_ROUND3
         arcSt2CenVec = arcSt2CenVec.Round (3);
#endif
         EGCode gCmd;
         if (arcType == Utils.EArcSense.CW) gCmd = EGCode.G2; else gCmd = EGCode.G3;
         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new GCodeSeg (fcArc, arcStartPoint, arcEndPoint, arcCenter, radius, startNormal,
            gCmd, EMove.Machining, toolingName));
            mToolPos[(int)Head] = arcEndPoint;
            mToolVec[(int)Head] = startNormal;
         }

         Utils.EFlange arcFlangeType = Utils.GetArcPlaneFlangeType (startNormal, GetXForm ());
         switch (arcFlangeType) {
            case Utils.EFlange.Web:
               if (Utils.IsArc (fcArc))
                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Y, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew ());
               else
                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Y, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew (),
                     machine: Machine,
                     mcCoordArcCenter, radius, mcCoordArcStPoint);
               break;
            case Utils.EFlange.Bottom:
               if (Utils.IsArc (fcArc))
                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew ());
               else
                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew (),
                     machine: Machine,
                     mcCoordArcCenter, radius, mcCoordArcStPoint);
               break;
            case Utils.EFlange.Top:
               if (Utils.IsArc (fcArc))
                  Utils.ArcMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, mcCoordArcEndPoint2D.X,
                     mcCoordArcEndPoint2D.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew ());
               else
                  Utils.CircularMachining (sw, arcType, arcSt2CenVec.X, OrdinateAxis.Z, arcSt2CenVec.Y, arcFlangeType, PartConfigType,
                  PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv.InvertNew () : Utils.sXformRHInv.InvertNew (),
                     machine: Machine,
                     mcCoordArcCenter, radius, mcCoordArcStPoint);
               break;
            default:
               throw new ArgumentException ("Arc is ill-defined perhaps on the flex");
         }
      }

      /// <summary>
      /// This method writes GCode for the segment of motion from Retracted tool position 
      /// to the machining start tool position.</summary>
      /// <param name="toolingStartPosition">The new tooling start position</param>
      /// <param name="toolingStartNormal">The normal at the tooling start normal</param>
      /// <param name="toolingName">The tooling name</param>
      public void MoveToMachiningStartPosition (Point3 toolingStartPosition, Vector3 toolingStartNormal, string toolingName) {
         // Linear Move to start machining tooling
         Point3 toolingStartPointWithMachineClearance = toolingStartPosition + toolingStartNormal * GCodeGenSettings.Standoff;
         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new (mToolPos[(int)Head], toolingStartPointWithMachineClearance,
            mToolVec[(int)Head], toolingStartNormal, EGCode.G1, EMove.Retract2Machining, toolingName));
            mToolPos[(int)Head] = toolingStartPointWithMachineClearance;
            mToolVec[(int)Head] = toolingStartNormal;
         }
      }

      /// <summary>
      /// A utility method to create a G Code comment for a string input
      /// </summary>
      /// <param name="comment">The input string</param>
      /// <returns>The G Code comment preceding "(" and succeeded by ")"</returns>
      public static string GetGCodeComment (string comment) {
         if (!String.IsNullOrEmpty (comment))
            return " ( " + comment + " ) ";
         else
            return "";
      }

      /// <summary>
      /// Write a G Code comment for Point3 type
      /// </summary>
      /// <param name="pt">The input point</param>
      /// <param name="comment">The g code comment that containd the input point within</param>
      public void WritePointComment (Point3 pt, string comment) =>
         WriteLineStatement (GetGCodeComment ($" {comment} Point  X:{pt.X:F3} Y:{pt.Y:F3} Z:{pt.Z:F3}"));

      /// <summary>
      /// This is a utility method which finds if the the cross product between 
      /// start and end normals ( after applying machine transform) 
      /// bears the same direction w.r.t the positive X Axis
      /// </summary>
      /// <param name="stNormal">The Start Normal</param>
      /// <param name="endNormal">The End Normal</param>
      /// <returns>1 if the X axis is in alignment with cross product, -1 otherwise</returns>
      public int GetAngleSignWRTPartAboutXAxis (Vector3 stNormal, Vector3 endNormal) {
         var stN = GetXForm () * stNormal.Normalized (); var endN = GetXForm () * endNormal.Normalized ();
         var cross = Geom.Cross (stN, endN).Normalized ();
         if (cross.Opposing (XForm4.mXAxis)) return -1;
         return 1;
      }

      /// <summary>
      /// This is a utility method which finds if the the cross product between 
      /// the given start and end normals bears the same direction w.r.t the positive X Axis
      /// </summary>
      /// <param name="stNormal">The Start Normal</param>
      /// <param name="endNormal">The End Normal</param>
      /// <returns>1 if the X axis is in alignment with cross product, -1 otherwise</returns>
      public int GetAngleSignWRTMachineAboutXAxis (Vector3 stNormal, Vector3 endNormal) {
         var stN = stNormal.Normalized (); var endN = endNormal.Normalized ();
         var cross = Geom.Cross (stN, endN).Normalized ();
         if (cross.Opposing (XForm4.mXAxis)) return -1;
         return 1;
      }

      public void WriteFlexLineSeg (
       ToolingSegment ts,
       bool isWJTStartCut,
       string toolingName,
       ToolingSegment? flexRefSeg = null,
       string lineSegmentComment = "") {
         if (flexRefSeg == null)
            throw new ArgumentNullException (nameof (flexRefSeg), "The Wire joint machining reference for flex cut can not be null. The Flex Cut is relative positions");

         lineSegmentComment = GetGCodeComment (lineSegmentComment);
         var tsStartPoint = ts.Curve.Start; var tsEndPoint = ts.Curve.End;
         var mcCoordTSStPoint = XfmToMachine (tsStartPoint); var mcCoordTSEndPoint = XfmToMachine (tsEndPoint);
         var tsStartNormalDir = ts.Vec0.Normalized (); var tsEndNormalDir = ts.Vec1.Normalized ();

         var endPointWithMCClearance = tsEndPoint + tsEndNormalDir * GCodeGenSettings.Standoff;
         var mcCoordTSEndNormalDir = GetXForm () * tsEndNormalDir;

         // Incremental angles are computed from the previous absolute angle ( for web , or top/bottom flanges).
         // flexRefSeg is used as the ref to find the previous normal.
         double mcCoordTSAngleWithFlexRefStart = (GetXForm () * flexRefSeg.Value.Vec0.Normalized ()).AngleTo (mcCoordTSEndNormalDir).R2D ();
         var flexRefTSStartPoint = flexRefSeg.Value.Curve.Start;
         var mcCoordflexRefTSStartPoint = XfmToMachine (flexRefTSStartPoint);
         var flexRefTSEndPoint = flexRefSeg.Value.Curve.End;
         var mcCoordflexRefTSEndPoint = XfmToMachine (flexRefTSEndPoint);
         var flexRefTSStartNormalDir = flexRefSeg.Value.Vec0.Normalized ();
         var mcCoordFlexRefTSStartNormalDir = GetXForm () * flexRefTSStartNormalDir;
         mcCoordTSAngleWithFlexRefStart *= GetAngleSignWRTMachineAboutXAxis (mcCoordFlexRefTSStartNormalDir, mcCoordTSEndNormalDir);

         // This following check does not set angle every time for the same plane type.
         if (isWJTStartCut) {
#if DEBUG_ROUND3
            var ptDiff = (mcCoordflexRefTSEndPoint - mcCoordflexRefTSStartPoint).Round (3);
#else
            var ptDiff = (mcCoordflexRefTSEndPoint - mcCoordflexRefTSStartPoint);
#endif
            Utils.LinearMachining (sw, ptDiff.X, ptDiff.Y, ptDiff.Z, mcCoordTSAngleWithFlexRefStart,
                lineSegmentComment, machine: MachineType.LCMMultipass2H, createDummyBlock4Master: CreateDummyBlock4Master);
         } else {
            var totalOuterRad = JobInnerRadius + JobThickness + FlexCuttingGap;
            var sign = Math.Sign (mcCoordTSEndNormalDir.Y);
            var theta = mcCoordTSAngleWithFlexRefStart.D2R ();
            var yComp = sign * totalOuterRad * Math.Sin (Math.Abs (theta));
            var zComp = totalOuterRad * (Math.Cos (Math.Abs (theta)) - 1.0);
#if DEBUG_ROUND3
            var mcOuterFlexPt = new Point3 (mcCoordTSEndPoint.X - mcCoordflexRefTSStartPoint.X, yComp, zComp).Round (3);
#else
            var mcOuterFlexPt = new Point3 (mcCoordTSEndPoint.X - mcCoordflexRefTSStartPoint.X, yComp, zComp);
#endif
            Utils.LinearMachining (sw, mcOuterFlexPt.X, mcOuterFlexPt.Y, mcOuterFlexPt.Z,
                  mcCoordTSAngleWithFlexRefStart, lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
         }

         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new GCodeSeg (
                mToolPos[(int)Head],
                endPointWithMCClearance,
                tsStartNormalDir,
                tsEndNormalDir,
                EGCode.G1,
                EMove.Machining,
                toolingName));
            mToolPos[(int)Head] = endPointWithMCClearance;
            mToolVec[(int)Head] = tsEndNormalDir;
         }
      }

      /// <summary>
      /// This method specifically writes machining G Code (G1) for linear machinable
      /// moves.
      /// </summary>
      /// <param name="endPoint">End point of the current segment.</param>
      /// <param name="startNormal">Start normal of the current linear segment, needed for simulation data</param>
      /// <param name="endNormal">End normal of the current linear segment, needed for simulation data</param>
      /// <param name="currPlaneType">Current plane type, needed if angle to be included in the G Code statement</param>
      /// <param name="previousPlaneType">Previous plane type, needed if angle to be included in the 
      /// G Code statement</param>
      /// <param name="currFlangeType">Current flange type, needed to include Y/Z coordinates in the G Code</param>
      /// <param name="toolingName">Name of the tooling for simulation purposes</param>
      public void WriteLineSeg (
       Point3 stPoint,
       Point3 endPoint,
       Vector3 startNormal,
       Vector3 endNormal,
       Utils.EPlane currPlaneType,
       Utils.EPlane previousPlaneType,
       Utils.EFlange currFlangeType,
       string toolingName,
       string lineSegmentComment = "",
       bool relativeCoords = false,
       Point3? refStPoint = null) {
         lineSegmentComment = GetGCodeComment (lineSegmentComment);
         var startPointWithMCClearance = stPoint + startNormal * GCodeGenSettings.Standoff;
         var endPointWithMCClearance = endPoint + endNormal * GCodeGenSettings.Standoff;

         double angleBetweenZAxisAndCurrToolingEndPoint;
         Point3 mcCoordStartPointWithMCClearance, mcCoordEndPointWithMCClearance;
         mcCoordStartPointWithMCClearance = XfmToMachine (startPointWithMCClearance);
         mcCoordEndPointWithMCClearance = XfmToMachine (endPointWithMCClearance);
         var actualMcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance;

         if (relativeCoords) {
            if (refStPoint != null)
               mcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance.Subtract (XfmToMachine (refStPoint.Value));
            else
               mcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance.Subtract (mcCoordStartPointWithMCClearance);
         }
#if DEBUG_ROUND3
         mcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance.Round (3);
         mcCoordStartPointWithMCClearance = mcCoordStartPointWithMCClearance.Round (3);
#endif
         // This following check does not set angle every time for the same plane type.
         if (currPlaneType == Utils.EPlane.Flex || currPlaneType != previousPlaneType) {
            if (currPlaneType == Utils.EPlane.Flex)
               angleBetweenZAxisAndCurrToolingEndPoint = Utils.GetAngleAboutXAxis (
                   XForm4.mZAxis, endNormal, GetXForm ()).R2D ();
            else
               angleBetweenZAxisAndCurrToolingEndPoint = Utils.GetAngle4PlaneTypeAboutXAxis (currPlaneType).R2D ();


            if (currFlangeType == Utils.EFlange.Bottom || currFlangeType == Utils.EFlange.Top)
               Utils.LinearMachining (
                   sw,
                   mcCoordEndPointWithMCClearance.X,
                   OrdinateAxis.Z,
                   mcCoordEndPointWithMCClearance.Z,
                   angleBetweenZAxisAndCurrToolingEndPoint,
                   lineSegmentComment,
                   createDummyBlock4Master: CreateDummyBlock4Master);
            else if (currFlangeType == Utils.EFlange.Web)
               Utils.LinearMachining (
                   sw,
                   mcCoordEndPointWithMCClearance.X,
                   OrdinateAxis.Y,
                   mcCoordEndPointWithMCClearance.Y,
                   angleBetweenZAxisAndCurrToolingEndPoint,
                   lineSegmentComment,
                   createDummyBlock4Master: CreateDummyBlock4Master);
            else
               Utils.LinearMachining (
                   sw,
                   mcCoordEndPointWithMCClearance.X,
                   mcCoordEndPointWithMCClearance.Y,
                   mcCoordEndPointWithMCClearance.Z,
                   angleBetweenZAxisAndCurrToolingEndPoint,
                   lineSegmentComment,
                   createDummyBlock4Master: CreateDummyBlock4Master);

            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new GCodeSeg (
                   mToolPos[(int)Head],
                   endPointWithMCClearance,
                   startNormal,
                   endNormal,
                   EGCode.G1,
                   EMove.Machining,
                   toolingName));
               mToolPos[(int)Head] = endPointWithMCClearance;
               mToolVec[(int)Head] = endNormal;
            }
         } else {
            if (currFlangeType == Utils.EFlange.Top || currFlangeType == Utils.EFlange.Bottom)
               Utils.LinearMachining (
                   sw,
                   mcCoordEndPointWithMCClearance.X,
                   OrdinateAxis.Z,
                   mcCoordEndPointWithMCClearance.Z,
                   lineSegmentComment,
                   createDummyBlock4Master: CreateDummyBlock4Master);
            else if (currFlangeType == Utils.EFlange.Web)
               Utils.LinearMachining (
                   sw,
                   mcCoordEndPointWithMCClearance.X,
                   OrdinateAxis.Y,
                   mcCoordEndPointWithMCClearance.Y,
                   lineSegmentComment,
                   createDummyBlock4Master: CreateDummyBlock4Master);
            else
               Utils.LinearMachining (
                   sw,
                   mcCoordEndPointWithMCClearance.X,
                   mcCoordEndPointWithMCClearance.Y,
                   mcCoordEndPointWithMCClearance.Z,
                   lineSegmentComment,
                   createDummyBlock4Master: CreateDummyBlock4Master);

            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new GCodeSeg (
                   mToolPos[(int)Head],
                   endPointWithMCClearance,
                   startNormal,
                   endNormal,
                   EGCode.G1,
                   EMove.Machining,
                   toolingName));
               mToolPos[(int)Head] = endPointWithMCClearance;
               mToolVec[(int)Head] = endNormal;
            }
         }
      }



      /// <summary>
      /// This method specifically writes machining G Code (G1) for linear machinable moves
      /// </summary>
      /// <param name="endPoint">End point of the current segment.</param>
      /// <param name="startNormal">Start normal of the current linear segment, needed for simulation data</param>
      /// <param name="endNormal">End normal of the current linear segment, needed for simulation data</param>
      /// <param name="toolingName">Name of the tooling</param>
      public void WriteLineSeg (Point3 stPoint, Point3 endPoint, Vector3 startNormal, Vector3 endNormal,
         string toolingName, bool relativeCoords = false, Point3? refStPt = null) {
         var startPointWithMCClearance = stPoint + endNormal * GCodeGenSettings.Standoff;
         var endPointWithMCClearance = endPoint + endNormal * GCodeGenSettings.Standoff;

         string lineSegmentComment = "";
         double angleBetweenZAxisAndCurrToolingEndPoint;

         bool planeChange = false;
         var angleBetweenPrevAndCurrNormal = endNormal.AngleTo (mToolVec[(int)Head]).R2D ();
         if (!angleBetweenPrevAndCurrNormal.EQ (0)) planeChange = true;

         angleBetweenZAxisAndCurrToolingEndPoint = endNormal.AngleTo (XForm4.mZAxis).R2D ();
         var mcCoordStartPointWithMCClearance = XfmToMachine (startPointWithMCClearance);
         var mcCoordEndPointWithMCClearance = XfmToMachine (endPointWithMCClearance);
         var actualMcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance;
         if (relativeCoords) {
            if (refStPt != null) {
               var mcRefStPt = XfmToMachine (refStPt.Value);
               mcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance.Subtract (mcRefStPt);
            } else
               mcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance.Subtract (mcCoordStartPointWithMCClearance);
         }

#if DEBUG_ROUND3
         mcCoordEndPointWithMCClearance = mcCoordEndPointWithMCClearance.Round (3);
#endif

         Vector3 stN, endN;
         if (PartConfigType == PartConfigType.LHComponent) {
            stN = Utils.sXformLHInv * startNormal;
            endN = Utils.sXformLHInv * endNormal;
         } else {
            stN = Utils.sXformRHInv * startNormal;
            endN = Utils.sXformRHInv * endNormal;
         }
         var cross = Geom.Cross (stN, endN).Normalized ();
         if (cross.Opposing (XForm4.mXAxis)) angleBetweenZAxisAndCurrToolingEndPoint *= -1;

         Utils.EFlange currFlangeType = Utils.GetArcPlaneFlangeType (startNormal, GetXForm ());
         if (planeChange) {
            if (currFlangeType == Utils.EFlange.Bottom || currFlangeType == Utils.EFlange.Top)
               Utils.LinearMachining (sw, mcCoordEndPointWithMCClearance.X, OrdinateAxis.Z, mcCoordEndPointWithMCClearance.Z,
                  angleBetweenZAxisAndCurrToolingEndPoint, lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
            else if (currFlangeType == Utils.EFlange.Web)
               Utils.LinearMachining (sw, mcCoordEndPointWithMCClearance.X, OrdinateAxis.Y, mcCoordEndPointWithMCClearance.Y,
                  angleBetweenZAxisAndCurrToolingEndPoint, lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
            else
               Utils.LinearMachining (sw, mcCoordEndPointWithMCClearance.X, mcCoordEndPointWithMCClearance.Y, mcCoordEndPointWithMCClearance.Z,
                  angleBetweenZAxisAndCurrToolingEndPoint, lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
         } else {
            if (currFlangeType == Utils.EFlange.Top || currFlangeType == Utils.EFlange.Bottom)
               Utils.LinearMachining (sw, mcCoordEndPointWithMCClearance.X, OrdinateAxis.Z, mcCoordEndPointWithMCClearance.Z,
                  lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
            else if (currFlangeType == Utils.EFlange.Web)
               Utils.LinearMachining (sw, mcCoordEndPointWithMCClearance.X, OrdinateAxis.Y, mcCoordEndPointWithMCClearance.Y,
                  lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
            else
               Utils.LinearMachining (sw, mcCoordEndPointWithMCClearance.X, mcCoordEndPointWithMCClearance.Y, mcCoordEndPointWithMCClearance.Z,
                  lineSegmentComment, createDummyBlock4Master: CreateDummyBlock4Master);
         }
         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new GCodeSeg (mToolPos[(int)Head], endPointWithMCClearance,
               startNormal, endNormal, EGCode.G1, EMove.Machining, toolingName));
            mToolPos[(int)Head] = endPointWithMCClearance;
            mToolVec[(int)Head] = endNormal;
         }
      }

      /// <summary>
      /// This method writes G Code for the plane of the arc to be machined,
      /// G17 or G18
      /// </summary>
      /// <param name="isFromWebFlange">If true, then it is G17, else writes G18</param>
      /// <param name="isNotchCut">If true, its a notch cut. 
      /// specific to the 2 laser headed cutting machine with multipass.
      /// </param>
      public void WritePlaneForCircularMotionCommand (bool isFromWebFlange, bool isNotchCut = false) {
         string gcode;
         if (isFromWebFlange)
            gcode = $"G17";
         else
            gcode = $"G18";
         if (isNotchCut)
            gcode += $" D=BlockAngle";
         gcode += "\t( Plane Selection : G17-XY Plane, G18-XZ Plane )";
         sw.WriteLine (gcode);
      }

      /// <summary>
      /// This method accounts for the notch approach length by adding a quartercircular arc
      /// lying on the material removal side or scrap side of the hole or cutout. 
      ///
      /// The start point of the arc will be on the material removal side, and the ending 
      /// point of the arc will be the mid point of the segment (arc or line). The ending point 
      /// of the arc is the start point of the arc if it is a circle. 
      /// 
      /// The tooling shall have to start from the approach arc start point and end at the 
      /// mid point of the original start segment if it is an arc or line segment, the end point 
      /// 
      /// If the segment is not a circle, in order to make this seamless, the start segment is 
      /// split into two. The modified first segment becomes the arc, second segment becomes 
      /// the second split segment of the original first curve, and finally,
      /// the last segment is the first split segment of the original first segment.
      /// 
      /// If the input segment is a circle, the modified segments list shall now have the approach arc
      /// as the first tooling segment, and the circle itself as the next tooling segment.
      /// 
      /// Algorithm: 
      /// The approach distance from the starting point in the case of circle, or the mid point
      /// in the case of line or arc, is set to 4 times the approach length set in the settings. 
      /// If this approach length is greater than the radius of the circle/arc itself, then the approach 
      /// distance is set to 2.0 mm. If it is still greater each halfed value is checked until the value is 
      /// more than 0.5mm. Otherwise, 0.5 mm is set.
      /// 
      /// The center of the approach arc is computed to be at radius distance ( approach distance of arc / 2)
      /// The first point on the arc is computed to be the above radius distance along the direction of 
      /// negative tooling
      /// 
      /// The last point of the arc is the new tooling entry point, which is the mid point of the tooling
      /// if the segment is not circle and is either arc or line OR the start point of the circle.
      /// Next, 2 points are found at the distances of 4 and then 2 mm from the new tooling entry point,
      /// (which is the end point of the arc), in the direction of negative tooling, say d1 and d2.
      ///
      /// The intermediate points are found by taking a point along the direction of  d1 and d2 from the 
      /// center of the arcs.
      /// </summary>
      /// <param name="toolingItem"></param>
      /// <returns>The modified list of the tooling segments</returns>
      public List<ToolingSegment> AddLeadinToTooling (Tooling toolingItem) {
         // If the tooling item is Mark, no need of creating the G Code
         if (toolingItem.IsMark ()) return [.. toolingItem.Segs];

         List<ToolingSegment> modifiedSegmentsList = [];
         var toolingSegmentsList = toolingItem.Segs.ToList ();
         Vector3 materialRemovalDirection; Point3 firstToolingEntryPt;
         if (!toolingItem.IsNotch () && !toolingItem.IsMark ()) {
            // E3Plane normal 
            var apn = Utils.GetEPlaneNormal (toolingItem, XForm4.IdentityXfm);

            // Compute an appropriate approach length. From the engg team, 
            // it was asked to have a dia = approach length * 4, which is 
            // stored in approachDistOfArc. 
            // if the circle's dia is smaller than the above approachDistOfArc
            // then assign approach length from settings. 
            // Recursively find the approachDistOfArc by halving the previous value
            // until its not lesser than 0.5. 0.5 is the lower limit.
            var approachDistOfArc = ApproachLength * 4.0;
            double circleRad;
            if (toolingItem.Segs.ToList ()[0].Curve is FCArc3 fcArc && Utils.IsCircle (fcArc)) {
               (_, circleRad) = Geom.EvaluateCenterAndRadius (fcArc);
               if (circleRad < approachDistOfArc) approachDistOfArc = ApproachLength;
               while (circleRad < approachDistOfArc) {
                  if (approachDistOfArc < mCurveLeastLength) break;
                  approachDistOfArc *= mCurveLeastLength;
               }
            }

            // Compute the scrap side direction
            (firstToolingEntryPt, materialRemovalDirection) = Utils.GetMaterialRemovalSideDirection (toolingItem);

            // Compute the tooling direction.
            Vector3 toolingDir;
            if (toolingSegmentsList[0].Curve is FCLine3)
               toolingDir = toolingSegmentsList[0].Curve.End - toolingSegmentsList[0].Curve.Start;
            else
               (toolingDir, _) = Geom.EvaluateTangentAndNormalAtPoint (toolingSegmentsList[0].Curve as FCArc3,
                  firstToolingEntryPt, apn, tolerance: 1e-3);
            toolingDir = toolingDir.Normalized ();

            // Compute new start point of the tooling, which is the start point of the quarter arc point on the 
            // scrap side of the material
            //var newToolingStPt = firstToolingEntryPt + materialRemovalDirection * approachDistOfArc;
            var approachArcRad = approachDistOfArc * 0.5;
            var arcCenter = firstToolingEntryPt + materialRemovalDirection * approachArcRad;
            var newToolingStPt = arcCenter - toolingDir * approachArcRad;

            // Find 2 points on the ray from newToolingStPt in the direction of -toolingDir, from the center of the arc
            var p1 = firstToolingEntryPt - toolingDir * 4.0; var p2 = firstToolingEntryPt - toolingDir * 2.0;

            // Compute the vectors from center of the arc to the above points
            var cp1 = (p1 - arcCenter).Normalized (); var cp2 = (p2 - arcCenter).Normalized ();

            // Compute the intersection of the vector cenetr to p1/p2 on the circle of the arc. These are 
            // intermediate points along the actual direction of the arc
            var ip1 = arcCenter + cp1 * approachArcRad; var ip2 = arcCenter + cp2 * approachArcRad;

            // Create arc, the fourth point being the midpoint of the arc or starting point
            // if its a circle.
            FCArc3 fcArc2 = new (newToolingStPt, ip1, ip2, firstToolingEntryPt, toolingSegmentsList[0].Vec0);
            if (Utils.IsCircle (toolingSegmentsList[0].Curve)) {
               modifiedSegmentsList.Add (new (fcArc2, toolingSegmentsList[0].Vec0, toolingSegmentsList[0].Vec0));
               modifiedSegmentsList.Add (toolingSegmentsList[0]);
               return modifiedSegmentsList;
            } else {
               List<Point3> internalPoints = [];
               internalPoints.Add (Geom.GetMidPoint (toolingSegmentsList[0].Curve, apn));
               var splitCurves = Geom.SplitCurve (toolingSegmentsList[0].Curve, internalPoints, apn, deltaBetween: 0.0);
               modifiedSegmentsList.Add (new (fcArc2, toolingSegmentsList[0].Vec0, toolingSegmentsList[0].Vec0));
               modifiedSegmentsList.Add (new (splitCurves[1], toolingSegmentsList[0].Vec0, toolingSegmentsList[0].Vec1));
               for (int ii = 1; ii < toolingSegmentsList.Count; ii++) modifiedSegmentsList.Add (toolingSegmentsList[ii]);
               modifiedSegmentsList.Add (new (splitCurves[0], toolingSegmentsList[0].Vec0, toolingSegmentsList[0].Vec1));
               return modifiedSegmentsList;
            }
         } else return toolingSegmentsList;
      }

      /// <summary>
      /// This is the method to be called for actual machining. This takes care of 
      /// linear and circular machining.
      /// </summary>
      /// <param name="toolingSegmentsList">List of Tooling segments (of lines and arcs)</param>
      /// <param name="toolingItem">The actual tooling item. The tooling segments list might vary 
      /// from the tooling segments of the tooling item, when the segments are modified for approach 
      /// distance by adding a quarter circular arc</param>
      /// <param name="bound">The bounding box of the tooling item</param>
      public ToolingSegment? WriteTooling (List<ToolingSegment> toolingSegmentsList, Tooling toolingItem,
         bool relativeCoords) {
         ToolingSegment ts;
         (var curve, var CurveStartNormal, _) = toolingSegmentsList[0];
         Utils.EPlane previousPlaneType = Utils.EPlane.None;
         Utils.EPlane currPlaneType;
         if (toolingItem.IsFlexFeature ()) currPlaneType = Utils.EPlane.Flex;
         else currPlaneType = Utils.GetFeatureNormalPlaneType (CurveStartNormal, new ());

         // Write any feature other than notch
         MoveToMachiningStartPosition (curve.Start, CurveStartNormal, toolingItem.Name);
         EnableMachiningDirective ();
         {
            // Write all other features such as Holes, Cutouts and edge notches
            for (int ii = 0; ii < toolingSegmentsList.Count; ii++) {
               var (FCCurve, startNormal, endNormal) = toolingSegmentsList[ii];
               startNormal = startNormal.Normalized ();
               endNormal = endNormal.Normalized ();
               var startPoint = FCCurve.Start;
               var endPoint = FCCurve.End;

               if (ii > 0) currPlaneType = Utils.GetFeatureNormalPlaneType (endNormal, GetXForm ());

               if (FCCurve is FCArc3) { // This is a 2d arc. 
                  var arcPlaneType = Utils.GetArcPlaneType (startNormal, GetXForm ());
                  var arcFlangeType = Utils.GetArcPlaneFlangeType (startNormal, GetXForm ());
                  (var center, _) = Geom.EvaluateCenterAndRadius (FCCurve as FCArc3);
                  WriteArc (FCCurve as FCArc3, arcPlaneType, arcFlangeType, center, startPoint, endPoint, startNormal,
                     toolingItem.Name);
               } else WriteLineSeg (startPoint, endPoint, startNormal, endNormal, currPlaneType, previousPlaneType,
                  Utils.GetFlangeType (toolingItem, new ()), toolingItem.Name, relativeCoords: relativeCoords);
               previousPlaneType = currPlaneType;
            }
            DisableMachiningDirective ();
            ts = toolingSegmentsList[^1];
         }
         return ts;
      }

      /// <summary>
      /// This method gets the tooling shape kind of the tooling
      /// </summary>
      /// <param name="toolingItem">The input tooling</param>
      /// <returns>Returns one of Notch, HoleShape, Text, or Cutout</returns>
      /// <exception cref="NotSupportedException">This exception is thrown if 
      /// any other kind is encountered</exception>
      static EToolingShape GetToolingShapeKind (Tooling toolingItem) {
         EToolingShape shape = EToolingShape.HoleShape;
         FCCurve3 firstCurve = toolingItem.Segs.First ().Curve;
         if (firstCurve as FCArc3 != null) {
            if (Utils.IsCircle (firstCurve))
               shape = EToolingShape.Circle;
         } else if (toolingItem.Kind == EKind.Notch) shape = EToolingShape.Notch;
         else if (toolingItem.Kind == EKind.Hole) shape = EToolingShape.HoleShape;
         else if (toolingItem.Kind == EKind.Mark) shape = EToolingShape.Text;
         else if (toolingItem.Kind == EKind.Cutout) shape = EToolingShape.Cutout;
         else throw new NotSupportedException ("Invalid tooling item kind encountered");
         return shape;
      }

      /// <summary>
      /// THis method moves the tool in rapid position to the safety position. 
      /// This the X and Y coordinate of the tool origin with Z value as 28 mm.
      /// This method registers this data only for the simulation and has no 
      /// bearing on the G Code that is being written. 
      /// </summary>
      void MoveToSafety () {
         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new (mSafePoint[(int)Head], mToolPos[(int)Head], XForm4.mZAxis, XForm4.mZAxis,
            EGCode.G0, EMove.Retract2SafeZ, "No tooling"));

            mToolVec[(int)Head] = XForm4.mZAxis;
         }
      }

      /// <summary>
      /// This method moves the tool from current machining position, end of tooling to 
      /// the retracted position only for the simulation. This has no bearing on the G Code 
      /// that is being written.
      /// </summary>
      /// <param name="endPt">Current tooling end point</param>
      /// <param name="endNormal">End normal at the tooling end point</param>
      /// <param name="toolingName">Tooling name</param>
      public void MoveToRetract (Point3 endPt, Vector3 endNormal, string toolingName) {
         var toolingEPRetracted =
                Utils.MovePoint (endPt, endNormal, mRetractClearance);
         if (!CreateDummyBlock4Master) {
            mTraces[(int)Head].Add (new (mToolPos[(int)Head], toolingEPRetracted, endNormal, endNormal,
            EGCode.G0, EMove.Retract, toolingName));
            mToolPos[(int)Head] = toolingEPRetracted;
            mToolVec[(int)Head] = endNormal.Normalized ();
         }
      }

      /// <summary>
      /// This method makes the tool move from previous tooling retract position, which is 
      /// previous tooling end position away from the position by end normal of the previous tooling
      /// TO the position, whose coordinates are X of the next tooling, Y of the next tooling and Z as safety
      /// value (28 mm).
      /// </summary>
      /// <param name="prevToolingSegs">Segments of the previous tooling</param>
      /// <param name="prevToolingName">Name of the previous tooling</param>
      /// <param name="currToolingSegs">Segments of the current tooling.</param>
      /// <param name="currentToolingName">Name of the current tooling</param>
      void MoveFromRetractToSafety (List<ToolingSegment> prevToolingSegs, string prevToolingName,
         List<ToolingSegment> currToolingSegs,
         string currentToolingName, EKind featType) {
         if (prevToolingSegs != null && prevToolingSegs.Count > 0) {
            (var prevSegEndCurve, _, var prevSegEndCurveEndNormal) = prevToolingSegs[^1];
            var prevToolingEPRetracted =
                   Utils.MovePoint (prevSegEndCurve.End, prevSegEndCurveEndNormal, mRetractClearance);
            Point3 prevToolingEPRetractedSafeZ = new (prevToolingEPRetracted.X, prevToolingEPRetracted.Y,
               mSafeClearance);
            var mcCoordsPrevToolingEPRetractedSafeZ = XfmToMachine (prevToolingEPRetractedSafeZ);
            Utils.LinearMachining (sw, mcCoordsPrevToolingEPRetractedSafeZ.X, mcCoordsPrevToolingEPRetractedSafeZ.Y,
               mcCoordsPrevToolingEPRetractedSafeZ.Z, 0, Rapid, createDummyBlock4Master: CreateDummyBlock4Master);
            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new GCodeSeg (mToolPos[(int)Head], prevToolingEPRetractedSafeZ, mToolVec[(int)Head],
               XForm4.mZAxis, EGCode.G0, EMove.Retract2SafeZ, prevToolingName));
               mToolPos[(int)Head] = prevToolingEPRetractedSafeZ;
               mToolVec[(int)Head] = XForm4.mZAxis;
            }
         }
         (var currSegStCurve, var currSegStCurveStNormal, _) = currToolingSegs[0];

         // Move to the current tooling item start posotion safeZ
         var currToolingSPRetracted =
                Utils.MovePoint (currSegStCurve.Start, currSegStCurveStNormal, mRetractClearance);
         Point3 currToolingSPRetractedSafeZ = new (currToolingSPRetracted.X, currToolingSPRetracted.Y,
            mSafeClearance);
         var mcCoordsCurrToolingSPRetractedSafeZ = XfmToMachine (currToolingSPRetractedSafeZ);
         if (featType != EKind.Mark) {
            Utils.RapidPosition (sw, mcCoordsCurrToolingSPRetractedSafeZ.X, mcCoordsCurrToolingSPRetractedSafeZ.Y,
               mcCoordsCurrToolingSPRetractedSafeZ.Z, 0, createDummyBlock4Master: CreateDummyBlock4Master);
            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new (mToolPos[(int)Head], currToolingSPRetractedSafeZ, mToolVec[(int)Head],
               XForm4.mZAxis, EGCode.G0,
               EMove.SafeZ2SafeZ, currentToolingName));
               mToolPos[(int)Head] = currToolingSPRetractedSafeZ;
               mToolVec[(int)Head] = XForm4.mZAxis;
            }
         }
      }

      /// <summary>
      /// This method moves the tool using G1, from safety Z position (28mm) to
      /// the retract position (for the next tooling). The retract position is the
      /// position from the next tooling start point, offset by retract clearance
      /// along the start normal vector
      /// </summary>
      /// <param name="toolingStartPt">Tooling start point of the next tooling</param>
      /// <param name="toolingStartNormalVec">Normal vector (outward) at the next tooling start point</param>
      /// <param name="toolingName">Name of the tooling : Can be used in simulation for debug purpose</param>
      public void MoveFromSafetyToRetract (Point3 toolingStartPt, Vector3 toolingStartNormalVec, string toolingName,
         bool planeChangeNeeded, EKind featType, bool usePingPongOption = true, string comment = "") {
         var currToolingStPtRetracted =
               Utils.MovePoint (toolingStartPt, toolingStartNormalVec, mRetractClearance);
         var angleBetweenZAxisNcurrToolingStPt =
       Utils.GetAngleAboutXAxis (XForm4.mZAxis, toolingStartNormalVec, GetXForm ()).R2D ();
         var mcCoordsCurrToolingStPtRetracted = XfmToMachine (currToolingStPtRetracted);

         if (featType != EKind.Mark) {
            if (planeChangeNeeded)
               Utils.LinearMachining (sw, mcCoordsCurrToolingStPtRetracted.X, mcCoordsCurrToolingStPtRetracted.Y,
                  mcCoordsCurrToolingStPtRetracted.Z, angleBetweenZAxisNcurrToolingStPt, Rapid, "Move to Piercing Position",
                  createDummyBlock4Master: CreateDummyBlock4Master);
            else
               RapidMoveToPiercingPosition (toolingStartPt, toolingStartNormalVec, featType, usePingPongOption, comment);

            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new (mToolPos[(int)Head], currToolingStPtRetracted,
               mToolVec[(int)Head], toolingStartNormalVec, EGCode.G0, EMove.SafeZ2Retract, toolingName));
               mToolPos[(int)Head] = currToolingStPtRetracted;
               mToolVec[(int)Head] = toolingStartNormalVec.Normalized ();
            }
         }
      }

      /// <summary>
      /// This method writes the X bounds for the feature described by the list of 
      /// tooling items, to write START_X and END_X. 
      /// </summary>
      /// <param name="toolingItem">The inout tooling item</param>
      /// <param name="segments">The list of tooling segments</param>
      /// <param name="startIndex">The start index in the list of tooling items. If it is -1, all the segments in the tooling
      /// segments will be considered</param>
      /// <param name="endIndex">The end index of the list of tooling items.</param>
      public void WriteBounds (Tooling toolingItem, List<ToolingSegment> segments, int startIndex = -1, int endIndex = -1) {
         var toolingSegsBounds = Utils.GetToolingSegmentsBounds (segments, Process.Workpiece.Model.Bound, startIndex, endIndex);
         var xMin = toolingSegsBounds.XMin; var xMax = toolingSegsBounds.XMax;
         var minPt = new Point3 (xMin, 0, 0); var maxPt = new Point3 (xMax, 0, 0);
         var mcMinPt = GCodeGenerator.XfmToMachine (this, minPt);
         var mcMaxPt = GCodeGenerator.XfmToMachine (this, maxPt);
         double toolingLen;
         if (segments.Count == 2 && segments[1].Curve is FCArc3) {
            FCArc3 fcArc = segments[1].Curve as FCArc3;
            if (IsCircle (fcArc))
               toolingLen = Utils.GetToolingLength (segments, startIndex, endIndex, leadIn: true);
            else
               toolingLen = Utils.GetToolingLength (segments, startIndex, endIndex, leadIn: false);
         } else
            toolingLen = Utils.GetToolingLength (segments, startIndex, endIndex, leadIn: false);

         if (LeftToRightMachining) {
            var statement = $"START_X={mcMinPt.X:F3} END_X={mcMaxPt.X:F3} PathLength={toolingLen:F2}";
            sw.WriteLine (statement);
         } else {
            var statement = $"START_X={mcMaxPt.X:F3} END_X={mcMinPt.X:F3} PathLength={toolingLen:F2}";
            sw.WriteLine (statement);
         }
      }

      /// <summary>
      /// This method is specific to the 2 laser headed multipass cutting machine,
      /// to output statement for calibrating the circle.
      /// </summary>
      /// <param name="toolingItem">The current tooling item</param>
      /// <param name="prevToolingItem">The previous tooling item</param>
      void CalibrateForCircle (Tooling toolingItem, Tooling prevToolingItem) {
         if (toolingItem.IsCircle ()) {
            //var bnd = Process.Workpiece.Bound;
            var evalValue = Geom.EvaluateCenterAndRadius (toolingItem.Segs.ToList ()[0].Curve as FCArc3);
            Point3 arcMcCoordsCenter;
            if (prevToolingItem != null) arcMcCoordsCenter = XfmToMachine (evalValue.Item1);
            else arcMcCoordsCenter = XfmToMachine (evalValue.Item1);
            var point2 = Utils.ToPlane (arcMcCoordsCenter, Utils.GetFeatureNormalPlaneType (toolingItem.Start.Vec, XForm4.IdentityXfm));
            sw.WriteLine ($"X_Coordinate={point2.X:F3} YZ_Coordinate={point2.Y:F3}");
         }
      }

      /// <summary>
      /// This method writes the program header before starting of the tooling. This forms the
      /// title section of a tooling block.
      /// </summary>
      /// <param name="toolingItem">The current tooling item</param>
      /// <param name="segs">Tooling segments. This need not be the same as that of toolingIte.Segs</param>
      /// <param name="xStart">Start position along X, MinX of the tooling item scope.</param>
      /// <param name="xPartition">The X position where the cut scope is partitioned</param>
      /// <param name="xEnd">End position along X, MaxX of the tooling item scope.</param>
      /// <param name="isLastToolingSeg">Flag if the tooling item's segment is the last element in the list</param>
      /// <param name="prevToolingItem">Previous tooling item</param>
      /// <param name="isValidNotch">Flag to pass if the Notch is valid</param>
      /// <param name="isFlexCut">Flag to pass of the cut section is on the flex section</param>
      /// <param name="startIndex">Start index of the segs</param>
      /// <param name="endIndex">End index of the segs</param>
      /// <param name="refSegIndex">Index of the reference tooling item, whose normals are used
      /// in ascertaining the plane normal if an arc is involved</param>
      public void WriteProgramHeader (Tooling toolingItem, List<ToolingSegment> segs, /*double frameFeed, */
         double xStart, double xPartition, double xEnd, bool isLastToolingSeg, /*bool isToBeTreatedAsCutOut,*/
         Tooling prevToolingItem = null, bool isValidNotch = false, bool isFlexCut = false,
         int startIndex = -1, int endIndex = -1, int refSegIndex = 0, ToolingSegment? nextTs = null) {
         string comment = $"** Tooling Name : {toolingItem.Name} - {toolingItem.FeatType} **";

         // Write N Token
         OutN (sw, comment);
         sw.WriteLine ("CutScopeNo={0}", mCutScopeNo);
         if (isValidNotch || toolingItem.IsCutout ()) mProgramNumber++;

         // Write block type
         WriteBlockType (toolingItem, segs[refSegIndex], nextTs: nextTs, isValidNotch, isFlexCut/*, isToBeTreatedAsCutOut*/);
         double SplitEndX = xEnd;
         if (mLastCutScope && isLastToolingSeg) SplitEndX = Process.Workpiece.Bound.XMax;

         // Statements specific to dual headed laser multi pass machine
         sw.WriteLine ("SplitStartX={0} SplitPartitionX={1} SplitEndX={2} ( Cut Scope Length:{3} )",
            xStart.ToString ("F3"), xPartition.ToString ("F3"), SplitEndX.ToString ("F3"), (xEnd - xStart).ToString ("F3"));
         if (CreateDummyBlock4Master) return;
         WriteBounds (toolingItem, segs, startIndex, endIndex);
         if (!isValidNotch) CalibrateForCircle (toolingItem, prevToolingItem);
         sw.WriteLine ("X_Correction=0 YZ_Correction=0");
      }

      /// <summary>
      /// This method writes the program header before starting of the tooling. This forms the
      /// title section of a tooling block.
      /// </summary>
      /// <param name="toolingItem">The current tooling item</param>
      /// <param name="pts">List of points, to be used for computing bounds</param>
      /// <param name="xStart">Start position along X, MinX of the tooling item scope.</param>
      /// <param name="xPartition">The X position where the cut scope is partitioned</param>
      /// <param name="xEnd">End position along X, MaxX of the tooling item scope.</param>
      /// <param name="isFlexCut">Flag to pass of the cut section is on the flex section</param>
      /// <param name="isLastToolingSeg">Flag if the tooling item's segment is the last element in the list</param>
      /// <param name="prevToolingItem">Previous tooling item</param>
      /// <param name="isValidNotch">Flag to pass if the Notch is valid</param>
      /// <param name="refSeg">The reference tooling item, whose normals are used
      /// in ascertaining the plane normal if an arc is involved</param>
      public void WriteProgramHeader (Tooling toolingItem, List<Point3> pts,
         double xStart, double xPartition, double xEnd, bool isFlexCut, bool isLastToolingSeg,
         /*bool isToBeTreatedAsCutOut,*/ bool isValidNotch,
         Tooling prevToolingItem = null, ToolingSegment? refSeg = null, ToolingSegment? nextSeg = null) {
         string comment = $"** Tooling Name : {toolingItem.Name} - {toolingItem.FeatType} **";
         OutN (sw, comment);
         sw.WriteLine ("CutScopeNo={0}", mCutScopeNo);
         if (isValidNotch || toolingItem.IsCutout () /*|| (toolingItem.IsHole () && isToBeTreatedAsCutOut)*/) mProgramNumber++;
         WriteBlockType (toolingItem, refSeg, nextTs: nextSeg, isValidNotch, isFlexCut/*, isToBeTreatedAsCutOut:false*/);
         double SplitEndX = xEnd;
         if (mLastCutScope && isLastToolingSeg) SplitEndX = Process.Workpiece.Bound.XMax;

         sw.WriteLine ("SplitStartX={0} SplitPartitionX={1} SplitEndX={2} ( Cut Scope Length:{3} )",
            xStart.ToString ("F3"), xPartition.ToString ("F3"), SplitEndX.ToString ("F3"), (xEnd - xStart).ToString ("F3"));
         WriteBounds (pts);
         if (CreateDummyBlock4Master) return;
         if (!isValidNotch) CalibrateForCircle (toolingItem, prevToolingItem);
         sw.WriteLine ("X_Correction=0 YZ_Correction=0");
      }

      /// <summary>
      /// This method writes START_X and END_X values, that signify the X bounds of the feature.
      /// </summary>
      /// <param name="toolingItem">The input tooling item</param>
      /// <param name="pts">The input set of points for which the bounds need to be written</param>
      public void WriteBounds (List<Point3> pts) {
         var toolingSegsBounds = Utils.GetPointsBounds (pts);
         var xMin = toolingSegsBounds.XMin; var xMax = toolingSegsBounds.XMax;
         double tl = 0;
         for (int ii = 1; ii < pts.Count; ii++)
            tl += pts[ii - 1].DistTo (pts[ii]);
         if (LeftToRightMachining)
            sw.WriteLine ($"START_X={xMin:F3} END_X={xMax:F3} PathLength={tl:F2}");
         else
            sw.WriteLine ($"START_X={xMax:F3} END_X={xMin:F3} PathLength={tl:F2}");
      }

      /// <summary>
      /// This method is used to initialize the tooling block for non-edge notches, holes,
      /// cutouts and marks.
      /// </summary>
      /// <param name="toolingItem">The input tooling item</param>
      /// <param name="segs">The list of tooling segments</param>
      /// <param name="startIndex">The start index in the list of tooling items</param>
      /// <param name="endIndex">The end endex in the list of tooling items.</param>
      public void InitializeToolingBlock (Tooling toolingItem, Tooling prevToolingItem, /*double frameFeed,*/
         double xStart, double xPartition, double xEnd, List<ToolingSegment> segs, bool isValidNotch, bool isFlexCut, bool isLast,
         int startIndex = -1, int endIndex = -1, ToolingSegment? nextTs = null) {
         sw.WriteLine ();
         // ** Tool block initialization **
         // Now compute the offset based on X
         int offset = 0;
         switch (Utils.GetFlangeType (toolingItem, GetXForm ())) {
            case Utils.EFlange.Top:
               offset = 2;
               sw.WriteLine (GetGCodeComment ("-----CUTTING ON TOP FLANGE--------"));
               break;
            case Utils.EFlange.Bottom:
               offset = 1;
               sw.WriteLine (GetGCodeComment ("-----CUTTING ON BOTTOM FLANGE--------"));
               break;
            case Utils.EFlange.Web:
               offset = 3;// toolingItem.ShouldConsiderReverseRef ? 4 : 3; break;
               sw.WriteLine (GetGCodeComment ("-----CUTTING ON WEB FLANGE--------"));
               break;
         }
         sw.WriteLine (GetGCodeComment (" ** Tool Block Initialization ** "));
         WriteProgramHeader (toolingItem, segs, /*frameFeed,*/xStart, xPartition, xEnd, isLast, /*isToBeTreatedAsCutOut: isToBeTreatedAsCutOut,*/
            prevToolingItem, isValidNotch: isValidNotch, /*isFlexCut:*/isFlexCut, startIndex, endIndex, nextTs: nextTs);
         if (CreateDummyBlock4Master) {
            sw.WriteLine (GetGCodeComment (" ** End - Tool Block Initialization ** "));
            return;
         }
         string sComment = offset switch {
            1 => string.Format (GetGCodeComment ("Machining on the Bottom Flange")),
            2 => string.Format (GetGCodeComment ("Machining on the Top Flange")),
            3 => string.Format (GetGCodeComment ("Machining on the Web Flange")),
            _ => ""
         };
         sw.WriteLine ($"S{offset}\t{sComment}");

         // Output X tool compensation
         if (Utils.GetPlaneType (toolingItem, GetXForm ()) == Utils.EPlane.Top) sw.WriteLine ($"G93 Z0 T1");
         else sw.WriteLine ($"G93 Z=-Head_Height T1");

         // Statements specific to the Dual Laser multipass cutting machine
         sw.WriteLine ("G61\t( Stop Block Preparation )");
         if (toolingItem.IsNotch () || toolingItem.IsCutout () && !toolingItem.IsFlexCutout ())
            sw.WriteLine ("PM=Notch_PM CM=Notch_CM EM=Notch_EM ZRH=Notch_YRH");
         else if (toolingItem.IsFlexCutout ())
            sw.WriteLine ("PM=Profile_PM CM=Profile_CM EM=Profile_EM ZRH=Profile_YRH");
         else if (Utils.GetFlangeType (toolingItem, GetXForm ()) == Utils.EFlange.Web)
            sw.WriteLine ("PM=Web_PM CM=Web_CM EM=Web_EM ZRH=Web_ZRH");
         else sw.WriteLine ("PM=Flange_PM CM=Flange_CM EM=Flange_EM ZRH=Flange_YRH\t( Block Process Specific Parametes )");
         sw.WriteLine ("Update_Param\t( Update Cutting Parameters )");
         sw.WriteLine ($"Lead_In={(toolingItem.IsNotch () ? NotchApproachLength :
            ApproachLength):F3}\t( Approach Length )");
         sw.WriteLine (GetGCodeComment (" ** End - Tool Block Initialization ** "));
      }

      /// <summary>
      /// This method initializes the tooling block of an non-edge notch by specifying
      /// flange type, plane type for arcs, exact position mode, time fed rate
      /// and other parameters. This also writes X bounds for the specific section
      /// </summary>
      /// <param name="toolingItem">The tooling item input</param>
      /// <param name="segs">The segments participating in the specific notch section.
      /// Please refer to the parameters startIndex and endIndex</param>
      /// <param name="segmentNormal">The normal to the set tooling items.</param>
      /// /// <param name="startIndex">The start index of the tooling segments. If startIndex is -1,
      /// then the entire tooling segments will be considered</param>
      /// /// <param name="endIndex">The end index of the tooling segments</param>
      /// /// <param name="circularMotionCmd">If it is true, an appropriate G Code directive 
      /// between G17 or G18 will be written</param>
      /// <param name="comment">User's comment</param>
      public void InitializeNotchToolingBlock (Tooling toolingItem, Tooling prevToolingItem,
         List<ToolingSegment> segs, Vector3 segmentNormal, /*double frameFeed,*/
         double xStart, double xPartition, double xEnd, bool isFlexCut, bool isLast,
         bool isValidNotch, int startIndex = -1, int endIndex = -1,
         int refSegIndex = 0, string comment = "", bool isShortPerimeterNotch = false,
         ToolingSegment? nextTs = null) {
         sw.WriteLine ();
         int offset;
         switch (Utils.GetArcPlaneFlangeType (segmentNormal, GetXForm ())) {
            case Utils.EFlange.Top:
               offset = 2;
               sw.WriteLine ("(-----CUTTING ON TOP FLANGE--------)");
               break;
            case Utils.EFlange.Bottom:
               offset = 1;
               sw.WriteLine ("(-----CUTTING ON BOTTOM FLANGE-----)");
               break;
            case Utils.EFlange.Web:
               offset = 3;// toolingItem.ShouldConsiderReverseRef ? 4 : 3; break;
               sw.WriteLine ("(-----CUTTING ON WEB FLANGE--------)");
               break;
            default: offset = -10; break;
         }
         if (isValidNotch)
            sw.WriteLine (GetGCodeComment (" ** Notch: Tool Block Initialization ** "));
         else
            sw.WriteLine (GetGCodeComment (" ** Cutout: Tool Block Initialization ** "));
         sw.WriteLine (GetGCodeComment ($"{comment}"));
         WriteProgramHeader (toolingItem, segs, xStart, xPartition, xEnd, isLast, /*isToBeTreatedAsCutOut: isToBeTreatedAsCutOut,*/
            prevToolingItem, isValidNotch: isValidNotch, /*isFlexCut:*/ isFlexCut, startIndex, endIndex, refSegIndex: refSegIndex,
            nextTs: nextTs);
         if (CreateDummyBlock4Master) {
            sw.WriteLine (GetGCodeComment (" ** End - Tool Block Initialization ** "));
            return;
         }
         if (offset > 0) {
            string sComment = offset switch {
               1 => string.Format (GetGCodeComment ("Machining on the Bottom Flange")),
               2 => string.Format (GetGCodeComment ("Machining on the Top Flange")),
               3 => string.Format (GetGCodeComment ("Machining on the Web Flange")),
               _ => ""
            };
            sw.WriteLine ($"S{offset}\t{sComment}");
         }
         // Output X tool compensation
         if (Utils.GetArcPlaneType (segmentNormal, GetXForm ()) == Utils.EPlane.Top) sw.WriteLine ($"G93 Z0 T1");
         else sw.WriteLine ($"G93 Z=-Head_Height T1");

         // Statements specific to the Dual Laser multipass cutting machine
         sw.WriteLine ("G61\t( Stop Block Preparation )");
         if (isShortPerimeterNotch)
            sw.WriteLine ("PM=ENotch_PM CM=ENotch_CM EM=ENotch_EM ZRH=ENotch_YRH\t( Block Process Specific Parameters )");
         else if (isFlexCut)
            sw.WriteLine ("PM=Flex_PM CM=Flex_CM EM=Flex_EM ZRH=Flex_YRH\t( Block Process Specific Parameters )");
         else
            sw.WriteLine ("PM=Notch_PM CM=Notch_CM EM=Notch_EM ZRH=Notch_YRH\t( Block Process Specific Parameters )");
         sw.WriteLine ("Update_Param\t( Update Cutting Parameters )");
         sw.WriteLine ($"Lead_In={NotchApproachLength:F3}\t( Approach Length )");
         sw.WriteLine ();
      }

      /// <summary>
      /// This method initializes the tooling block by specifying
      /// flange type, exact position mode, time fed rate
      /// and other parameters. This also writes X bounds for the specific section
      /// </summary>
      /// <param name="toolingItem">The tooling item input</param>
      /// <param name="points">THe set of points that participate in the tooling section. 
      /// This is so in the case of approach to the tooling considering approach length 
      /// and wire joint distance in the case of non-edge notches</param>
      /// <param name="segmentNormal">The normal to the set of points</param>
      /// <param name="comment">User's comment</param>
      public void InitializeNotchToolingBlock (Tooling toolingItem, Tooling prevToolingItem,
         List<Point3> points, Vector3 segmentNormal, /*double frameFeed,*/double xStart,
         double xPartition, double xEnd, bool isFlexCut, bool isLast, ToolingSegment? refSeg,
         ToolingSegment? nextTs,
         bool isValidNotch, string comment = "") {
         sw.WriteLine ();
         int offset;
         switch (Utils.GetArcPlaneFlangeType (segmentNormal, GetXForm ())) {
            case Utils.EFlange.Top:
               offset = 2;
               sw.WriteLine ("(-----CUTTING ON TOP FLANGE--------)");
               break;
            case Utils.EFlange.Bottom:
               offset = 1;
               sw.WriteLine ("(-----CUTTING ON BOTTOM FLANGE-----)");
               break;
            case Utils.EFlange.Web:
               offset = 3;
               sw.WriteLine ("(-----CUTTING ON WEB FLANGE--------)");
               break;
            default: offset = -10; break;
         }
         if (isValidNotch)
            sw.WriteLine (GetGCodeComment (" ** Notch: Tool Block Initialization ** "));
         else
            sw.WriteLine (GetGCodeComment (" ** Cutout: Tool Block Initialization ** "));
         if (Utils.IsGCodeComment (comment))
            sw.WriteLine (comment);
         else
            sw.WriteLine (GetGCodeComment (comment));
         WriteProgramHeader (toolingItem, points, xStart, xPartition, xEnd, isFlexCut, isLast,
            isValidNotch: isValidNotch, prevToolingItem, refSeg: refSeg, nextSeg: nextTs);
         if (CreateDummyBlock4Master) {
            sw.WriteLine (GetGCodeComment ("** End - Tool Block Initialization ** "));
            return;
         }
         if (offset > 0) {
            string sComment = offset switch {
               1 => string.Format (GetGCodeComment ("Machining on the Bottom Flange")),
               2 => string.Format (GetGCodeComment ("Machining on the Top Flange")),
               3 => string.Format (GetGCodeComment ("Machining on the Web Flange")),
               _ => ""
            };
            sw.WriteLine ($"S{offset}\t{sComment}");
         }
         // Output X tool compensation
         if (Utils.GetArcPlaneType (segmentNormal, GetXForm ()) == Utils.EPlane.Top) sw.WriteLine ($"G93 Z0 T1");
         else sw.WriteLine ($"G93 Z=-Head_Height T1");
         //WritePlaneForCircularMotionCommand (Utils.GetFlangeType (toolingItem, GetXForm ()), angleCorrection: false);
         sw.WriteLine ("G61\t( Stop Block Preparation )");
         if (isFlexCut)
            sw.WriteLine ("PM=Flex_PM CM=Flex_CM EM=Flex_EM ZRH=Flex_YRH\t( Block Process Specific Parameters )");
         else
            sw.WriteLine ("PM=Notch_PM CM=Notch_CM EM=Notch_EM ZRH=Notch_YRH\t( Block Process Specific Parameters )");
         sw.WriteLine ("Update_Param\t( Update Cutting Parameters )");
         sw.WriteLine ($"Lead_In={NotchApproachLength:F3}\t( Approach Length )");
      }

      /// <summary>
      /// FinalizeToolingBlock is to be called at the end of the tooling of types
      /// other than non-edge notches
      /// </summary>
      /// <param name="toolingItem">The input tooling item</param>
      /// <param name="markLength">Mark length (text)</param>
      /// <param name="totalMarkLength">Total Mark Length</param>
      /// <param name="cutLength">cut length of tooling other than </param>
      /// <param name="totalCutLength">Total cut length of the toolings</param>
      public void FinalizeToolingBlock (Tooling toolingItem, double prevCutToolingsLength,
         double totalCutLength) {
         double percentage = 0;
         double cutLength;
         if (!toolingItem.IsMark ()) {
            cutLength = toolingItem.Perimeter + prevCutToolingsLength;
            percentage = cutLength / totalCutLength * 100;
         }

         if (!CreateDummyBlock4Master)
            sw.WriteLine ($"G253 E0 F=\"{(toolingItem.IsMark () ? 1 : 2)}=1:1:{percentage.Round (0)}\"");
         sw.WriteLine (GetGCodeCancelToolDiaCompensation ()); // Cancel tool diameter compensation
      }

      /// <summary>
      /// FinalizeNotchToolingBlock is exclusive to non-edge Notches. This writes G code 
      /// directive to write overall completion percentage
      /// </summary>
      /// <param name="toolingItem">The tooling Item</param>
      /// <param name="cutLength">The cut length of the notch tooling block</param>
      /// <param name="totalCutLength">Total cut length of the notch (including wire joint length
      /// and notch approach)</param>
      public void FinalizeNotchToolingBlock (Tooling toolingItem,
         double cutLength, double totalCutLength) {
         sw.WriteLine (GetGCodeComment ("** Tooling Block Finalization ** "));
         double percentage = (cutLength / totalCutLength) * 100;
         string gcodest;
         if (!CreateDummyBlock4Master) {
            gcodest = $"G253 E0 F=\"{(toolingItem.IsMark () ? 1 : 2)}=1:1:{percentage:F0}\"";
            sw.WriteLine (gcodest);
         }

         // Cancel tool diameter compensation
         sw.WriteLine (GetGCodeCancelToolDiaCompensation ());
      }

      public static string GetGCodeApplyToolDiaCompensation () => "G40 E0\t( Apply Tool Dia Compensation )";
      public static string GetGCodeCancelToolDiaCompensation () => "G40 E1\t( Cancel Tool Dia Compensation )";

      /// <summary>
      /// This method the first one to be called to move the laser cutting tool
      /// to the start position of the subsequent tooling.
      /// </summary>
      /// <param name="toolingItem">The next tooling item</param>
      /// <param name="modifiedToolingSegs">The tooling segments, which might have been 
      /// modified as per the requirement, like adding a quarter circular arc in the case
      /// of holes and cutouts, or creating more segments for the notch</param>
      /// <param name="prevToolingSegment">This is the last tooling segment of the previous tooling item</param>
      /// <param name="prevToolingItem">Previous tooling item</param>
      /// <param name="prevToolingSegs">Tooling segments of the previous tooling item</param>
      /// <param name="firstTooling">Flag if the about to be tooled tooling item is first in the list.
      /// This makes the tool move from the initial start position. This is not written to the G Code
      /// but used in Simulation</param>
      /// <param name="isValidNotch">Flag if the feature is a valid notch</param>
      /// <param name="notchEntry">IN the case of notches, the entry position and normal</param>
#nullable enable
      public void PrepareforToolApproach (Tooling toolingItem, List<ToolingSegment> modifiedToolingSegs,
         ToolingSegment? prevToolingSegment, Tooling prevToolingItem,
         List<ToolingSegment> prevToolingSegs, bool firstTooling, bool isValidNotch,
         Tuple<Point3, Vector3>? notchEntry = null) {

         if (firstTooling) MoveToSafety ();
         else if (prevToolingSegment != null)
            MoveToRetract (prevToolingSegment.Value.Curve.End, prevToolingSegment.Value.Vec0, prevToolingItem?.Name);
         if (isValidNotch) {
            ArgumentNullException.ThrowIfNull (notchEntry);
            if (prevToolingSegment != null)
               MoveToNextTooling (prevToolingSegment.Value.Vec0, prevToolingSegment,
               notchEntry.Item1, notchEntry.Item2.Normalized (), prevToolingItem != null ? prevToolingItem.Name : "",
               toolingItem.Name, firstTooling, toolingItem.Kind);
            else
               MoveToNextTooling (prevToolingItem != null ? prevToolingItem.End.Vec : new Vector3 (),
                  (prevToolingSegs != null && prevToolingSegs.Count > 0) ? prevToolingSegs[^1] : null,
               notchEntry.Item1, notchEntry.Item2.Normalized (), prevToolingItem != null ? prevToolingItem.Name : "",
               toolingItem.Name, firstTooling, toolingItem.Kind);
         } else {
            if (prevToolingSegment != null)
               MoveToNextTooling (prevToolingSegment.Value.Vec0, prevToolingSegment,
               modifiedToolingSegs[0].Curve.Start, modifiedToolingSegs[0].Vec0,
               prevToolingItem != null ? prevToolingItem.Name : "",
               toolingItem.Name, firstTooling, toolingItem.Kind);
            else
               MoveToNextTooling (prevToolingItem != null ? prevToolingItem.End.Vec : new Vector3 (),
                  (prevToolingSegs != null && prevToolingSegs.Count > 0) ? prevToolingSegs[^1] : null,
                  modifiedToolingSegs[0].Curve.Start, modifiedToolingSegs[0].Vec0,
                  prevToolingItem != null ? prevToolingItem.Name : "",
                  toolingItem.Name, firstTooling, toolingItem.Kind);
         }
      }
#nullable restore

      /// <summary>
      /// This method is specific to dual laser headed multipass cutting machine
      /// </summary>
      /// <param name="toolingItem">The input tooling item</param>
      /// <param name="fromWebFlange">Flag to intimate if the tooling happens from
      /// web flange</param>
      public void WriteToolCorrectionData (Tooling toolingItem) {
         if (CreateDummyBlock4Master) return;

         if (!toolingItem.IsMark ())
            sw.WriteLine ("ToolCorrection\t( Correct Tool Position based on Job )");
      }

      public void WriteToolDiaCompensation (bool isFlexTooling) {
         // For Flex tooling alone, G40 or G41 statement shall not be written
         if (!isFlexTooling)
            sw.WriteLine ($"G{(Utils.sXformRHInv[1, 3] < 0.0 ? 41 : 42)} D1 R=KERF E0\t( Tool Dia Compensation)");
      }

      public double GetTotalToolingsLength (List<Tooling> toolingItems) {
         // For notches, compute the length
         // Compute the total tooling lengths of Hole, Cutouts, and Notches
         double totalToolingCutLength = toolingItems
             .Where (a => (a.IsCutout () || a.IsHole ()))
             .Sum (a => a.Perimeter);

         foreach (var ti in toolingItems) {
            if (ti.EdgeNotch)
               continue;
            else
               totalToolingCutLength += Notch.GetTotalNotchToolingLength (
                   Process.Workpiece.Bound, ti, mPercentLengths, NotchWireJointDistance,
                   NotchApproachLength, mCurveLeastLength, !NotchWireJointDistance.EQ (0),
                JobInnerRadius, JobThickness, PartConfigType);
         }
         return totalToolingCutLength;
      }
      /// <summary>
      /// This is the main method which prepares the machine with calling various pre-machining
      /// settings/macros, and then calls WriteTooling, which actually calls machining G Codes.
      /// This also adds post processing macros to complete
      /// </summary>
      /// <param name="toolingItems"></param>
      /// <param name="shouldOutputDigit"></param>
      void DoWriteCuts (
       List<Tooling> toolingItems, Bound3 bound, /*double frameFeed,*/ double xStart, double xPartition,
         double xEnd, bool shouldOutputDigit, double cutscopeToolingLength) {
         Tooling prevToolingItem = null;
         List<ToolingSegment> prevToolingSegs = null;
         bool first = true;
         string traverseM = UsePingPong ? "M1014" : "";
         mProgramNumber = mPgmNo[Utils.EFlange.Web];

         double totalMarkLength = Process.Workpiece.Cuts
             .Where (a => a.IsMark ())
             .Sum (a => a.Perimeter);

         double prevCutToolingsLength = 0, prevMarkToolingsLength = 0;
         for (int i = 0; i < toolingItems.Count; i++) {
            Tooling toolingItem = toolingItems[i];

            // The following switches are for tests.
            if (!Cutouts && toolingItem.IsCutout ()) continue;
            if (!CutNotches && toolingItem.IsNotch ()) continue;
            if (!CutMarks && toolingItem.IsMark ()) continue;
            if (!CutHoles && toolingItem.IsHole ()) continue;

            // Check if the web and flanges are included to be machined.
            if (toolingItem.Flange == EFlange.Web && !CutWeb) continue;
            if ((toolingItem.Flange == EFlange.Top || toolingItem.Flange == EFlange.Bottom) && !CutFlange) continue;
            if (SlotWithWJTOnly && !toolingItem.IsSlotWithWJT ()) continue;
            if (DualFlangeCutoutNotchOnly && !toolingItem.IsDualFlangeCutoutNotch ()) continue;

            // Debug_Debug
            firstTlgStartPoint = toolingItem.Segs.ToList ()[0].Curve.Start;

            // ** Create the feature for which G Code needs to be created
            ToolingFeature feature = null;
            bool toTreatAsCutOut = CutOut.ToTreatAsCutOut (toolingItem.Segs, Process.Workpiece.Bound, MinCutOutLengthThreshold);
            if ((toolingItem.IsHole () && !toTreatAsCutOut) || toolingItem.IsMark ()) {
               feature = new Hole (
                   toolingItem, this as IGCodeGenerator, xStart, xEnd, xPartition, prevToolingSegs, mPrevToolingSegment, bound,
                   prevCutToolingsLength, prevMarkToolingsLength, totalMarkLength, cutscopeToolingLength,
                   first, prevToolingItem, i == toolingItems.Count - 1);
            } else if (toolingItem.IsNotch () && !toolingItem.EdgeNotch) {
               Utils.EPlane previousPlaneType = Utils.EPlane.None;

               // Write the Notch first
               bool isWireJointsNeeded = !NotchWireJointDistance.EQ (0);
               if (!isWireJointsNeeded)
                  mPercentLengths = [0.5];

               feature = new Notch (
                   toolingItem, bound, Process.Workpiece.Bound, this as IGCodeGenerator, prevToolingItem, mPrevToolingSegment,
                   prevToolingSegs, first, previousPlaneType, xStart, xPartition, xEnd,
                   NotchWireJointDistance, NotchApproachLength, MinNotchLengthThreshold, mPercentLengths,
                   prevCutToolingsLength, cutscopeToolingLength, isWireJointsNeeded: isWireJointsNeeded,
                   LeastWJLength, curveLeastLength: mCurveLeastLength);
            } else if (toolingItem.IsCutout () || toTreatAsCutOut) {
               Utils.EPlane previousPlaneType = Utils.EPlane.None;
               feature = new CutOut (
                   this, toolingItem, prevToolingItem, prevToolingSegs, mPrevToolingSegment, previousPlaneType,
                   xStart, xPartition, xEnd, NotchWireJointDistance, NotchApproachLength, prevCutToolingsLength, prevMarkToolingsLength,
                   totalMarkLength, cutscopeToolingLength, first, toTreatAsCutOut);
            }

            if (feature == null) continue;

            if (first) prevToolingItem = null;
            mProgramNumber = GetProgNo (toolingItem);
            mProgramNumber++;

            // Open shutter and go to program number
            // Output the first program as probing function always
            if (first) {
               string ncname = NCName;
               if (ncname.Length > 20) ncname = ncname[..20];
               if (!CreateDummyBlock4Master) {
                  if (shouldOutputDigit)
                     sw.WriteLine ("G253 E0 F=\"3=THL RF\"");
               }
            }

            // ** Write tooling **
            feature.WriteTooling ();

            // ** Get the most recent tooling segment **
            mPrevToolingSegment = feature.GetMostRecentPreviousToolingSegment ();

            // Get the modified tooling segments of the feature
            var toolingSegs = feature.ToolingSegments;

            //// ** Tooling block finalization - Start**
            // Compute the cut tooling length
            if (!toolingItem.IsMark ()) {
               if (toolingItem.IsNotch () && !toolingItem.EdgeNotch)
                  prevCutToolingsLength += Notch.GetTotalNotchToolingLength (
                      Process.Workpiece.Bound, toolingItem, [0.25, 0.5, 0.75],
                      NotchWireJointDistance, NotchApproachLength, mCurveLeastLength,
                   !NotchWireJointDistance.EQ (0), JobInnerRadius, JobThickness, PartConfigType);
               else
                  prevCutToolingsLength += toolingItem.Perimeter;
            } else
               prevMarkToolingsLength += toolingItem.Perimeter;

            first = false;
            prevToolingItem = toolingItem;
            prevToolingSegs = toolingSegs;
            SetProgNo (toolingItem);
         }

         // Digit will be made 0 if it doesn't belong to this head
         if (shouldOutputDigit) {
            double x = MarkTextPosX, y = MarkTextPosY;
            var range = GetSerialDigitToOutput ();
            for (int i = range.Item1; i < range.Item2; i++) {
               int progNo = GetDigitProgNo (i) + 1;
               OutN (sw);
               sw.WriteLine ($"P1763={progNo}");
               if (i == 0) sw.WriteLine ("M58\r\nG61\t( Stop Block Preparation )");
               sw.WriteLine ($":P1707={i}");
               sw.WriteLine ($":P1708={DigitConst}+P{1860 + i}");
               sw.WriteLine ($":P1838=P1661+(P1707*{DigitPitch})+{x:F3} " +
                   $"(X-Axis Actual Distance from Flux)");
               Point3 markTextPoint = new (x, y, 0);
               markTextPoint = XfmToMachine (markTextPoint);
               double yVal = markTextPoint.Y;
               sw.WriteLine ($":P1839=P2005{(Math.Sign (yVal) == 1 ? "+" : "-")}{Math.Abs (yVal)} " +
                   $"(Y-Axis Actual Distance from Flux)");
               string mark = PartConfigType == MCSettings.PartConfigType.LHComponent ? "L160" : "L161";
               sw.WriteLine ($"S100\r\nG93 X=P1838 Y=P1839\r\nG22 {mark} J=P1708\r\nG61\t( Stop Block Preparation )\r\n");
            }
         }
      }

      /// <summary>
      /// This method positions the tool head exactly at the starting position 
      /// of the next tooling segment, but with a distance "clearance" along the 
      /// starting normal.
      /// </summary>
      /// <param name="toPoint">The next point of the tooling segment</param>
      /// <param name="endNormal">The normal at the next tooling starting point</param>
      /// <param name="clearance">A distance along the normal at the point</param>
      /// <param name="toolingName">Tooling name</param>
      public void RapidPositionWithClearance (
       Point3 toPoint, Vector3 endNormal, double clearance, string toolingName, bool isMark,
       bool usePingPongOption = true, bool firstWJTTrace = true) {
         var toPointOffset = Utils.MovePoint (toPoint, endNormal, clearance);
         var angle = Utils.GetAngleAboutXAxis (XForm4.mZAxis, endNormal, GetXForm ()).R2D ();
         var mcCoordsToPointOffset = XfmToMachine (toPointOffset);

         if (!isMark) {
            Utils.EPlane currPlaneType = Utils.GetArcPlaneType (endNormal, GetXForm ());

            if (currPlaneType == EPlane.YPos || currPlaneType == EPlane.YNeg) {
               WritePointComment (mcCoordsToPointOffset, " WJT ## Machining from ");
               Utils.RapidPosition (
                   sw, mcCoordsToPointOffset.X, OrdinateAxis.Z, mcCoordsToPointOffset.Z, angle,
                   machine: Machine,
                   createDummyBlock4Master: CreateDummyBlock4Master,
                   usePingPongOption && UsePingPong && firstWJTTrace ? "M1014" : "",
                   "Rapid Position with Clearance");
            } else if (currPlaneType == EPlane.Top) {
               WritePointComment (mcCoordsToPointOffset, " WJT ## Machining from ");
               Utils.RapidPosition (
                   sw, mcCoordsToPointOffset.X, OrdinateAxis.Y, mcCoordsToPointOffset.Y, angle,
                   machine: Machine,
                   createDummyBlock4Master: CreateDummyBlock4Master,
                   usePingPongOption && UsePingPong && firstWJTTrace ? "M1014" : "",
                   "Rapid Position with Clearance");
            }

            if (!CreateDummyBlock4Master && usePingPongOption) {
               mTraces[(int)Head].Add (new (
                   mToolPos[(int)Head],
                   toPointOffset,
                   endNormal,
                   endNormal,
                   EGCode.G0,
                   EMove.RapidPosition,
                   toolingName));

               mToolPos[(int)Head] = toPointOffset;
               mToolVec[(int)Head] = endNormal.Normalized ();
            }
         }
      }

      /// <summary>
      /// This method writes the G Code segment for wire joint trace jump(skip). 
      /// The wire joint trace is a set of segments that start from a tooling segment
      /// end point with a rapid move, (G0), reach the position along the outward normal 
      /// on the flange, at a distance of notch approach distance, from the next tooling 
      /// segment's start point and machine (G1) from this point to the point on the tooling segment
      /// </summary>
      /// <param name="wjtSeg">The wire joint segment</param>
      /// <param name="scrapSideNormal">The direction in which the scrappable material exists</param>
      /// <param name="lastPosition">The last position of the tool head (from previous stroke)</param>
      /// <param name="notchApproachDistance">The notch approach distance</param>
      /// <param name="prevPlaneType">The previous plane type YPos, YNeg, or Top (for angle computation 
      /// about X axis)</param>
      /// <param name="currFlangeType">Web, Top or Bottom(for angle computation about X Axis)</param>
      /// <param name="toolingItem">The current tooling item</param>
      /// <param name="blockCutLength">The machining distance of the current wire joint trace</param>
      /// <param name="totalToolingsCutLength">The total machining length (of the notch)</param>
      /// <param name="comment">Comment to be written in G Code</param>
      public void WriteWireJointTrace (
      ToolingSegment wjtSeg,
      ToolingSegment? nextSeg,
      Vector3 scrapSideNormal,
      Point3 lastPosition,
      double notchApproachDistance,
      ref Utils.EPlane prevPlaneType,
      Utils.EFlange currFlangeType,
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
      ) {
         prevRapidPos = null;
         // Determine the current plane type based on the wire joint segment's vector
         Utils.EPlane currPlaneType = Utils.GetArcPlaneType (wjtSeg.Vec1, GetXForm ());
         var nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * notchApproachDistance;

         // Adjust the machining start point based on boundary constraints
         if (scrapSideNormal.Dot (XForm4.mNegZAxis).SGT (0)) {
            if (nextMachiningStart.Z.SLT (Process.Workpiece.Bound.ZMin))
               nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * notchApproachDistance * 0.5;
         } else if (scrapSideNormal.Dot (XForm4.mXAxis).SGT (0)) {
            if (nextMachiningStart.X.SGT (Process.Workpiece.Bound.XMax))
               nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * notchApproachDistance * 0.5;
         } else if (scrapSideNormal.Dot (XForm4.mNegXAxis).SGT (0)) {
            if (nextMachiningStart.X.SLT (Process.Workpiece.Bound.XMin))
               nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * notchApproachDistance * 0.5;
         }

         // Create the participating points list
         var fromPt = GetLastToolHeadPosition ().Item1;
         List<Point3> pts =
         [
           nextMachiningStart,
           wjtSeg.Curve.End,
           lastPosition
         ];

         // Determine if machining is from the web flange
         // The bool flag is used to generate if it is G17 or G18 for 
         // arcs/circles. However, the wire joint jump trace before
         // the flex machining has no meaning in specifying.
         bool isFromWebFlange = false;
         if (wjtSeg.Vec0.Normalized ().EQ (XForm4.mZAxis)) isFromWebFlange = true;
         else if (wjtSeg.Vec1.Normalized ().EQ (XForm4.mZAxis)) isFromWebFlange = true;

         // Initialize tooling block for valid notches or cutouts
         if (toolingItem.IsDualFlangeCutoutNotch ()) comment = "Dual Flange Cutout Notch: " + comment;
         if (toolingItem.IsCutout ()) comment = "CutOut: " + comment;
         else if (toolingItem.IsNotch () && isValidNotch) comment = "Notch: " + comment;

         InitializeNotchToolingBlock (
             toolingItem,
             prevToolingItem: null,
             pts,
             wjtSeg.Vec1.Normalized (),
             xStart,
             xPartition,
             xEnd,
             isFlexCut: isFlexCut,
             isLast: false,
             wjtSeg,
             nextTs: nextSeg,
             isValidNotch: isValidNotch,
             comment
         );

         // Write toolplane confirmation
         WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");

         // Exit if dummy block creation is enabled
         if (CreateDummyBlock4Master) return;

         // Rapid positioning again without the ping-pong option true 
         // with M1014 token printed if ping pong option is used
         RapidPositionWithClearance (
             nextMachiningStart,
             wjtSeg.Vec0,
             mRetractClearance,
             toolingItem.Name,
             isMark: false,
             usePingPongOption: true,
             firstWJTTrace
         );

         MoveToMachiningStartPosition (nextMachiningStart, wjtSeg.Vec0, toolingItem.Name);

         WriteToolCorrectionData (toolingItem);

         if (isValidNotch)
            WriteLineStatement (NotchCutStartToken);
         WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: isValidNotch);
         WriteToolDiaCompensation (isFlexTooling: isFlexCut);

         EnableMachiningDirective ();

         prevRapidPos = Utils.MovePoint (nextMachiningStart, wjtSeg.Vec0.Normalized (), mRetractClearance);
         // Write the machining trace
         if (isFlexCut) {
            if (flexRefTS == null)
               throw new Exception ("For flex cut, the reference tooling segment can't be null");
            WriteFlexLineSeg (wjtSeg,
             isWJTStartCut: true,
             toolingItem.Name,
             flexRefSeg: flexRefTS,
             lineSegmentComment: "WJT approach machining up to the tooling profile");
         } else
            WriteLineSeg (
               wjtSeg.Curve.Start,
                wjtSeg.Curve.End,
                wjtSeg.Vec1,
                wjtSeg.Vec1,
                currPlaneType,
                prevPlaneType,
                currFlangeType,
                toolingItem.Name,
                lineSegmentComment: "WJT approach machining up to the tooling profile",
                relativeCoords: relativeCoords,
                refStPoint: prevRapidPos);

         if (toCompleteToolingBlock) {
            // Diable Machining only if the next tooling block will start on flex
            DisableMachiningDirective ();
            if (isValidNotch)
               WriteLineStatement (NotchCutEndToken);

            // Finalize (end) the tooling block with one stroke of approach machining
            FinalizeNotchToolingBlock (toolingItem, blockCutLength, totalToolingsCutLength);
         }

         // Update the block cut length
         blockCutLength += mToolPos[(int)Head].DistTo (fromPt);

         // Update the previous plane type
         prevPlaneType = currPlaneType;

         // Tooling block finalization happens after machining or another jump trace
      }


      /// <summary>
      /// This method is used to write G Code that moves the 
      /// tool head from current end of the tooling to the next tooling segment
      /// </summary>
      /// <param name="prevToolingEndNormal">The normal at the previous end</param>
      /// <param name="prevToolingEndSegment">The previous end segment</param>
      /// <param name="nextToolingStartPoint">The start point on the next tooling segment</param>
      /// <param name="nextToolingStartNormal">The start normal of the next tooling</param>
      /// <param name="prevToolingItemName">The name of the previous tooling stroke</param>
      /// <param name="nextToolingItemName">The name of the current tooling stroke.</param>
      /// <param name="firstTime">A boolean flag that tells if the tooling item is the first one to start with.
      /// This is used for angle computation</param>
      public void MoveToNextTooling (
       Vector3 prevToolingEndNormal,
       ToolingSegment? prevToolingEndSegment,
       Point3 nextToolingStartPoint,
       Vector3 nextToolingStartNormal,
       string prevToolingItemName,
       string nextToolingItemName,
       bool firstTime,
       EKind featType,
       bool usePingPongOption = true) {
         double changeInAngle;

         if (firstTime)
            changeInAngle = Utils.GetAngleAboutXAxis (
                XForm4.mZAxis,
                nextToolingStartNormal,
                GetXForm ()
            ).R2D ();
         else
            changeInAngle = Utils.GetAngleAboutXAxis (
                prevToolingEndNormal,
                nextToolingStartNormal,
                GetXForm ()
            ).R2D ();


         bool movedToCurrToolingRetractedPos = false;
         bool planeChangeNeeded = false;

         if (!changeInAngle.LieWithin (-10.0, 10.0) && !CreateDummyBlock4Master) {
            planeChangeNeeded = true;

            if (featType != EKind.Mark)
               sw.WriteLine ("PlaneTransfer\t( Enable Plane Transformation for Tool TurnOver )");

            MoveFromRetractToSafety (
                prevToolingEndSegment,
                prevToolingItemName,
                nextToolingStartPoint,
                nextToolingStartNormal,
                nextToolingItemName,
                featType
            );

            MoveFromSafetyToRetract (
                nextToolingStartPoint,
                nextToolingStartNormal,
                nextToolingItemName,
                planeChangeNeeded,
                featType,
                usePingPongOption
            );

            movedToCurrToolingRetractedPos = true;
            sw.WriteLine ("EndPlaneTransfer\t( Disable Plane Transformation after Tool TurnOver)");
         } else {
            if (featType != EKind.Mark) {
               sw.WriteLine ("ToolPlane\t( Confirm Cutting Plane )");

               if (CreateDummyBlock4Master) {
                  return;
               }
            }
         }

         if (!movedToCurrToolingRetractedPos) {
            MoveFromSafetyToRetract (
                nextToolingStartPoint,
                nextToolingStartNormal,
                nextToolingItemName,
                planeChangeNeeded,
                featType,
                usePingPongOption
            );
         }
      }


      /// <summary>
      /// This method makes the tool move from previous tooling retract position, which is 
      /// previous tooling end position away from the position by end normal of the previous tooling</summary>
      /// TO the position, whose coordinates are X of the next tooling, Y of the next tooling and Z as safety<param name="prevToolingLastSegment"></param>
      /// value (28 mm).
      /// <param name="prevToolingName">Name of the previous tooling</param>
      /// <param name="currToolingStPoint"></param>
      /// <param name="currToolingStNormal"></param>
      /// <param name="currentToolingName"></param>
      /// <param name="isMark"></param>
      public void MoveFromRetractToSafety (ToolingSegment? prevToolingLastSegment, string prevToolingName,
         Point3 currToolingStPoint, Vector3 currToolingStNormal, string currentToolingName, EKind featType) {
         if (prevToolingLastSegment != null) {
            (var prevSegEndCurve, _, var prevSegEndCurveEndNormal) = prevToolingLastSegment.Value;
            var prevToolingEPRetracted =
                   Utils.MovePoint (prevSegEndCurve.End, prevSegEndCurveEndNormal, mRetractClearance);
            Point3 prevToolingEPRetractedSafeZ = new (prevToolingEPRetracted.X, prevToolingEPRetracted.Y,
               mSafeClearance);
            var mcCoordsPrevToolingEPRetractedSafeZ = XfmToMachine (prevToolingEPRetractedSafeZ);
            if (featType != EKind.Mark)
               Utils.LinearMachining (sw, mcCoordsPrevToolingEPRetractedSafeZ.X, mcCoordsPrevToolingEPRetractedSafeZ.Y,
                  mcCoordsPrevToolingEPRetractedSafeZ.Z, 0, Rapid, comment: "", machine: Machine, createDummyBlock4Master: CreateDummyBlock4Master);

            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new GCodeSeg (mToolPos[(int)Head], prevToolingEPRetractedSafeZ, mToolVec[(int)Head],
               XForm4.mZAxis, EGCode.G0, EMove.Retract2SafeZ, prevToolingName));
               mToolPos[(int)Head] = prevToolingEPRetractedSafeZ;
               mToolVec[(int)Head] = XForm4.mZAxis;
            }
         }

         // Move to the current tooling item start posotion safeZ
         var currToolingSPRetracted =
                Utils.MovePoint (currToolingStPoint, currToolingStNormal, mRetractClearance);
         Point3 currToolingSPRetractedSafeZ = new (currToolingSPRetracted.X, currToolingSPRetracted.Y,
            mSafeClearance);
         var mcCoordsCurrToolingSPRetractedSafeZ = XfmToMachine (currToolingSPRetractedSafeZ);
         if (featType != EKind.Mark) {
            Utils.RapidPosition (sw, mcCoordsCurrToolingSPRetractedSafeZ.X, mcCoordsCurrToolingSPRetractedSafeZ.Y,
               mcCoordsCurrToolingSPRetractedSafeZ.Z, 0, machine: Machine, createDummyBlock4Master: CreateDummyBlock4Master);

            if (!CreateDummyBlock4Master) {
               mTraces[(int)Head].Add (new (mToolPos[(int)Head], currToolingSPRetractedSafeZ, mToolVec[(int)Head].Length.EQ (0) ? XForm4.mZAxis : mToolVec[(int)Head], XForm4.mZAxis, EGCode.G0,
               EMove.SafeZ2SafeZ, currentToolingName));
               mToolPos[(int)Head] = currToolingSPRetractedSafeZ;
               mToolVec[(int)Head] = XForm4.mZAxis;
            }
         }
      }

      /// <summary>
      /// This method reads from JSON, if any tooling needs to be referenced 
      /// in an opposite order.
      /// </summary>
      /// <param name="toolingName"></param>
      /// <returns>Flag true if yes, false otherwise</returns>
      public bool IsOppositeReference (string toolingName) {
         if (WPOptions == null) {
            if (string.IsNullOrEmpty (WorkpieceOptionsFilename)) return false;
            try {
               string json = File.ReadAllText (WorkpieceOptionsFilename);
               var data = JsonSerializer.Deserialize<List<WorkpieceOptions>> (json);
               WPOptions = [];
               foreach (var item in data) {
                  WPOptions[item.FileName] = item;
               }
            } catch (Exception) {
               return false;
            }
         }
         var WPOptionsForTooling = GetWorkpieceOptions ();
         return WPOptionsForTooling?.IsOppositeReference (toolingName) ?? false;
      }

      WorkpieceOptions? GetWorkpieceOptions () {
         if (WPOptions.TryGetValue (NCName, out var workpieceOptions)) return workpieceOptions;
         return null;
      }

      /// <summary>
      /// A wrapper method to write a "WriteLine" statement in G Code
      /// </summary>
      /// <param name="st">String to write</param>
      public void WriteLineStatement (string st) => sw.WriteLine (st);

      /// <summary>
      /// This method rapid positions the tool (G0) to the start position of next tooling
      /// with option to include PingPong option.
      /// </summary>
      /// <param name="stPoint"></param>
      /// <param name="stNormal"></param>
      /// <param name="usePingPongOption"></param>
      /// <param name="comment"></param>
      public void RapidMoveToPiercingPosition (
       Point3 stPoint, Vector3 stNormal, EKind featType, bool usePingPongOption = true, string comment = "") {
         // Debug
         if (RapidMoveToPiercingPositionWithPingPong && usePingPongOption)
            return;
         if (CreateDummyBlock4Master) return;
         var stPointWithStandoff =
               Utils.MovePoint (stPoint, stNormal, mRetractClearance);
         var mcCoordsStPoint = XfmToMachine (stPointWithStandoff);
         var planeType = Utils.GetPlaneType (stNormal, GetXForm ());
         comment = "Move to Piercing Position " + comment;
         if (planeType == EPlane.YNeg || planeType == EPlane.YPos) {
            Utils.RapidPosition (
                sw, mcCoordsStPoint.X, OrdinateAxis.Z, mcCoordsStPoint.Z,
                comment,
                (UsePingPong && usePingPongOption) ? "M1014" : "",
                createDummyBlock4Master: CreateDummyBlock4Master);
         } else if (planeType == EPlane.Top) {
            if (featType != EKind.Mark)
               Utils.RapidPosition (
                   sw, mcCoordsStPoint.X, OrdinateAxis.Y, mcCoordsStPoint.Y,
                   comment,
                   (UsePingPong && usePingPongOption) ? "M1014" : "",
                   createDummyBlock4Master: CreateDummyBlock4Master);
         }
         if (usePingPongOption)
            RapidMoveToPiercingPositionWithPingPong = true;
         else
            RapidMoveToPiercingPositionWithPingPong = false;
      }

      /// <summary>
      /// Thius method gets the last position of the head
      /// </summary>
      /// <returns>The last position of the tool head</returns>
      public Tuple<Point3, Vector3> GetLastToolHeadPosition () {
         return new Tuple<Point3, Vector3> (mToolPos[(int)Head], mToolVec[(int)Head]);
      }

      // Tuple<Start, End> Start inclusive and End exclusive
      // That is in index format
      Tuple<int, int> GetSerialDigitToOutput () => Tuple.Create (0, (int)SerialNumber);
      static int GetDigitProgNo (int digitNo) => DigitProg + digitNo;
      #endregion
   }

   /// <summary>
   /// The WorkpieceOptions holds the data for Opposite reference for specific 
   /// Tooling referred to by name.
   /// </summary>
   public struct WorkpieceOptions {
      [JsonPropertyName ("FileName")]
      public string FileName { get; set; }

      [JsonPropertyName ("OppositeReference")]
      public string[] OppositeReference { get; set; }
      public readonly bool IsOppositeReference (string input) {
         if (Array.Exists (OppositeReference, element => element == input)) return true;
         return false;
      }
   }
}