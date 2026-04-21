using ProfileCAM.Core.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static ProfileCAM.Core.Utils;
using Flux.API;

namespace ProfileCAM.Core.GCodeGen.LCMMultipass2HLegacy {
   /// <summary>
   /// The following class parses any G Code and caches the G0 and G1 segments. Work is 
   /// still in progress to read G2 and G3 segments. The processor has to be set with 
   /// the Traces to simulate. Currently, only one G Code (file) can be used for simulation.
   /// </summary>
   public class GCodeParser4LCMMultipass2HLegacy {
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
      public GCodeParser4LCMMultipass2HLegacy () { }
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
                  if (eGCodeVal == EGCode.G2) arc = Geom.CreateArc (arcStartPoint, arcEndPoint, arcCenter, normal, EArcSense.CW);
                  else arc = Geom.CreateArc (arcStartPoint, arcEndPoint, arcCenter, normal, EArcSense.CCW);

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
}
