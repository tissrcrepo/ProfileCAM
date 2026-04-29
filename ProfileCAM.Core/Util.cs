using System.Text;
using static System.Math;
using Flux.API;
using ProfileCAM.Core.Geometries;
using System.Text.Json;
using static ProfileCAM.Core.Geometries.Geom;
using static ProfileCAM.Core.MCSettings;
using ProfileCAM.Core.Optimizer;
using System.Diagnostics;
using ProfileCAM.Core;
using ProfileCAM.Core.GCodeGen.LCMMultipass2HLegacy;
using ProfileCAM.Core.GCodeGen.GCodeFeatures;
using ProfileCAM.Core.GCodeGen;

namespace ProfileCAM.Core;

public struct NotchAttribute (FCCurve3 crv, Vector3 stNormal, Vector3 endNormal, Vector3 oFlgNormal,
   Vector3 nrBdyVec, XForm4.EAxis proxBdyVec, Vector3 srapSideDir, bool flag = true) {
   public FCCurve3 Curve { get; set; } = crv;//Item1
   public Vector3 StNormal { get; set; } = stNormal;//Item2
   public Vector3 EndNormal { get; set; } = endNormal;//Item3
   public Vector3 OFlangeNormal { get; set; } = oFlgNormal;//Item4
   public Vector3 NearestBdyVec { get; set; } = nrBdyVec;//Item5
   public XForm4.EAxis ProxBdyDir { get; set; } = proxBdyVec;//Item6
   public Vector3 ScrapSideDir { get; set; } = srapSideDir;//Item7
   public bool Flag { get; set; } = flag;//Item8
}

public class MachinableCutScope {
   public MachinableCutScope (CutScope cs, IGCodeGenerator gCGen) {
      ArgumentNullException.ThrowIfNull (cs);
      CutScope = cs;
      ToolingScopes = cs.MachinableToolingScopes;
      mScopeWidth = ToolingScopes.Sum (t => (t.EndX - t.StartX));
      StartX = cs.StartX;
      EndX = cs.EndX;
      GCGen = gCGen;
      if (!(cs.TSInScope1?.Count > 0 || cs.TSInScope2?.Count > 0))
         Utils.CreatePartition (GCGen, ToolingScopes, MCSettings.It.OptimizePartition, cs.Bound);
      SetData ();
      Toolings = [.. ToolingScopes.Select (ts => ts.Tooling)];
      Bound = cs.Bound;
   }
   public MachinableCutScope (List<Tooling> toolings, IGCodeGenerator gCGen) {
      CutScope = null;
      ToolingScopes = ToolingScope.CreateToolingScopes (toolings);
      mScopeWidth = ToolingScopes.Sum (t => (t.EndX - t.StartX));
      StartX = ToolingScopes.Min (ts => ts.StartX);
      EndX = ToolingScopes.Min (ts => ts.EndX);
      GCGen = gCGen;
      Toolings = [.. ToolingScopes.Select (ts => ts.Tooling)];
      Bound = Utils.CalculateBound3 (Toolings);
      Utils.CreatePartition (GCGen, ToolingScopes, MCSettings.It.OptimizePartition, Bound);
      SetData ();
   }

   void SetData () {
      ToolingScopesH1 = [.. ToolingScopes.Where (t => t.Tooling.Head == 0)];
      ToolingScopesH2 = [.. ToolingScopes.Where (t => t.Tooling.Head == 1)];
      ToolingScopesWidth = ToolingScopes.Sum (t => (t.EndX - t.StartX));
      ToolingScopesWidthH1 = ToolingScopesH1.Sum (t => (t.EndX - t.StartX));
      ToolingScopesWidthH2 = ToolingScopesH2.Sum (t => (t.EndX - t.StartX));
      ToolingsHead1 = [.. ToolingScopes.Select (ts => ts.Tooling).Where (ts => ts.Head == 0)];
      ToolingsHead2 = [.. ToolingScopes.Select (ts => ts.Tooling).Where (ts => ts.Head == 1)];
   }
   public CutScope CutScope { get; set; }
   public IGCodeGenerator GCGen { get; set; }
   public List<ToolingScope> ToolingScopes { get; set; }
   public double StartX { get; private set; }
   public double EndX { get; private set; }
   double mScopeWidth;
   public double ScopeWidth { get => mScopeWidth; }
   public List<Tooling> Toolings { get; private set; }
   public List<Tooling> ToolingsHead1 { get; private set; }
   public List<Tooling> ToolingsHead2 { get; private set; }
   public double ToolingScopesWidth { get; set; }
   public double ToolingScopesWidthH1 { get; set; }
   public double ToolingScopesWidthH2 { get; set; }
   public List<ToolingScope> ToolingScopesH1 { get; private set; }
   public List<ToolingScope> ToolingScopesH2 { get; private set; }
   public static List<MachinableCutScope> CreateMachinableCutScopes (List<CutScope> css, IGCodeGenerator gcGen) =>
      [.. css.Select (cs => new MachinableCutScope (cs, gcGen))];
   public Bound3 Bound { get; private set; }
}

internal static class Extensions {
   public static double ToRadians (this double deg) => deg * Math.PI / 180.0;
   public static double ToDegrees (this double rad) => rad * 180.0 / Math.PI;
   public static double LieOn (this double f, double a, double b) => (f - a) / (b - a);
   public static bool EQ (this double a, double b) => Abs (a - b) < 1e-6;
   public static bool EQ (this double a, double b, double err) => Abs (a - b) < err;
   public static bool EQ (this float a, float b) => Abs (a - b) < 1e-6;
   public static bool EQ (this float a, float b, double err) => Abs (a - b) < err;
   public static bool EQ (this Vector3 a, Vector3 b, double err) {
      if (a.X.EQ (b.X, err) && a.Y.EQ (b.Y, err) && a.Z.EQ (b.Z, err)) return true;
      return false;
   }
   public static double D2R (this int a) => a * PI / 180;
   public static bool LieWithin (this double val, double leftLimit,
                                 double rightLimit, double epsilon = 1e-6)
      => (leftLimit - epsilon <= val && val <= rightLimit + epsilon);
   public static Point3 Subtract (this Point3 val, Point3 sub) {
      Point3 pt = new (val.X - sub.X, val.Y - sub.Y, val.Z - sub.Z);
      return pt;
   }
   public static Vector3 ToVector (this Point3 val) => new (val.X, val.Y, val.Z);
   public static Point3 ToPoint (this Vector3 val) => new (val.X, val.Y, val.Z);
   public static Vector3 ToPV (this Point3 val) => new (val.X, val.Y, val.Z);
   public static double LengthSquared (this Vector3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;
   public static Point2 Subtract (this Point2 val, Point2 sub) {
      Point2 pt = new (val.X - sub.X, val.Y - sub.Y);
      return pt;
   }
   public static Vector3 Cross (this Vector3 a, Vector3 b) => Geom.Cross (a, b);

   public static string GetGCodeComment (this Point3 val, string token) {
      var s = $"{token} {{ {val.X.Round (3)}, {val.Y.Round (3)}, {val.Z.Round (3)} }}";
      return Utils.GetGCodeComment (s);
   }
   public static string GetGCodeComment (this Point2 val, string token) {
      var s = $"{token} {{ {val.X.Round (3)}, {val.Y.Round (3)} }}";
      return Utils.GetGCodeComment (s);
   }
   public static bool IsSameSense (this Vector3 vec1, Vector3 vec2) {
      if (vec1.Opposing (vec2)) return false;
      return true;
   }
   public static bool IsWebFlange (this Vector3 normal, double tol = 1e-6) => normal.Normalized ().EQ (XForm4.mZAxis, tol);
   public static bool IsTopOrBottomFlange (this Vector3 normal, double tol = 1e-6) =>
      normal.Normalized ().EQ (XForm4.mYAxis, tol) || normal.Normalized ().EQ (XForm4.mNegYAxis, tol);
}

#nullable enable
//public static class CloningExtensions {
//   public static Arc3? Clone (this Arc3 arc) {
//      bool exists = Arc3Factory.TryGetPoints (arc, out var s, out var ip1, out var ip2, out var e);
//      if ( exists )
//         return new FCLine3 (s, ip1, ip2, e);
//      throw new Exception ("Arc is not cached");
//   }

//   public static Line3? Clone (this Line3 line) {
//      // For Line3 (assuming no cache), just create new
//      return new FCLine3 (line.Start, line.End);
//   }

//   public static Curve3? Clone (this Curve3 curve) {
//      if (curve is Arc3 arc)
//         return arc.Clone ();  // Uses cached Arc3 clone
//      else if (curve is Line3 line)
//         return line.Clone ();  // Creates new FCLine3
//      else
//         return null;
//   }
//}

public static class GeomExtensions {
   public static bool IsCircle (this FCArc3 arc, double tol = 1e-6) {
      if (arc.Start.DistTo (arc.End).LTEQ (tol)) return true;
      return false;
   }
   public static Point3 Center (this FCArc3 arc) {
      var (cen, _) = Geom.EvaluateCenterAndRadius (arc);
      return cen;
   }
   public static double Radius (this FCArc3 arc) {
      var (_, rad) = Geom.EvaluateCenterAndRadius (arc);
      return rad;
   }
   public static Point3 EvaluatePointAtParam (
    this FCCurve3 crv,
    double param,
    Vector3? apn = null,
    double tol = 1e-6) {
      if (crv is FCArc3 arc)
         return arc.EvaluatePointAtParam (param, apn, tol);
      else if (crv is FCLine3 line)
         return line.EvaluatePointAtParam (param, apn: null, tol);
      else
         throw new NotImplementedException ("Entity is not implemented");
   }

   public static Point3 EvaluatePointAtParam (
    this FCArc3 arc,
    double param,
    Vector3 apn,
    double tol = 1e-6) {
      // ---- Normalize axis -------------------------------------------------
      apn = apn.Normalized ();

      var center = arc.Center ();
      var start = arc.Start;
      double radius = arc.Radius ();

      // ---- Optional safety: ensure axis ⟂ radius --------------------------
      var rvec = (start - center).Normalized ();
      if (!rvec.Dot (apn).EQ (0, tol))
         throw new Exception ("Axis is not perpendicular to arc plane");

      double theta;

      // ---- Circle case -----------------------------------------------------
      if (arc.IsCircle (tol)) {
         // Allow any param (negative, >1, multi-turn)
         double s = param * arc.Length;

         theta = s / radius;

         // Normalize angle to avoid overflow (optional)
         theta %= 2 * Math.PI;
      } else {
         // Arc case: strictly bounded
         if (!param.LieWithin (0, 1))
            throw new Exception ("For arc, param must be between 0 and 1");

         double s = param * arc.Length;
         theta = s / radius;
      }

      // ---- Rotate start point about axis ----------------------------------
      return XForm4.AxisRotation (apn, center, start, theta);
   }

   public static Point3 EvaluatePointAtParam (
    this FCLine3 line,
    double param,
    Vector3? apn = null,
    bool constrainedWithInLine = true,
    double tol = 1e-6) {
      var start = line.Start;
      var end = line.End;

      // ---- Direction vector ----------------------------------------------
      var dir = end - start;

      // ---- Degenerate case: zero-length line ------------------------------
      if (dir.Length.EQ (0, tol))
         return start;

      // ---- Parameter handling --------------------------------------------
      double t = param;

      if (constrainedWithInLine) {
         if (!t.LieWithin (0, 1))
            throw new Exception ("Parameter must be between 0 and 1 for a line segment");
      }

      // ---- Linear interpolation ------------------------------------------
      return start + dir * t;
   }
}



public enum MachiningSense {
   Downward,
   Upward,
   Level,
   None
}

public enum OrdinateAxis {
   Y, Z
}

public enum ERotate {
   Rotate0, Rotate90, Rotate180, Rotate270
}

public enum MachineType {
   LCMLegacy,
   LCMMultipass2H,
   LCMMultipass2HNoDB
}

public enum EGCode {
   G0, G1, G2, G3, None
}

public enum EMove {
   Retract,
   Retract2SafeZ,
   SafeZ2SafeZ,
   SafeZ2Retract,
   Retract2Machining,
   Machining,
   RapidPosition,
   None
}

public enum FrameMachinableStatus {
   Empty,
   Machinable,
   Impossible
}
/// <summary>
/// Represents the drawable information of a G-Code segment, 
/// which is used for simulation purposes.
/// </summary>
/// <remarks>
/// The <see cref="GCodeGenerator"/> class populates a 
/// <see cref="List{GCodeDrawableSegment}"/> for each tool head.
/// This list can also be populated by parsing the G-Code directly.
/// </remarks>
public class GCodeSeg {
   EGCode mGCode;
   public EGCode GCode => mGCode;

   public GCodeSeg (Point3 stPoint, Point3 endPoint, Vector3 StNormal, Vector3 EndNormal,
                    EGCode gcmd, EMove moveType, string toolingName) {
      // Initialize all fields
      mStartPoint = default;
      mEndPoint = default;
      mCenter = default;
      mStartNormal = default;
      mEndNormal = default;
      mRadius = 0;
      mMoveType = default;
      mToolingName = string.Empty;
      SetGCLine (stPoint, endPoint, StNormal, EndNormal, gcmd, moveType, toolingName);
   }

   public GCodeSeg (FCArc3 arc, Point3 stPoint, Point3 endPoint, Point3 center, double radius,
                    Vector3 StNormal, EGCode gcmd, EMove moveType, string toolingName) {
      // Initialize all fields
      mStartPoint = default;
      mEndPoint = default;
      mCenter = default;
      mStartNormal = default;
      mEndNormal = default;
      mRadius = 0;
      mMoveType = default;
      mToolingName = string.Empty;
      SetGCArc (arc, stPoint, endPoint, center, radius, StNormal, gcmd, moveType, toolingName);
   }

   public GCodeSeg (GCodeSeg rhs) {
      mStartPoint = rhs.mStartPoint;
      mEndPoint = rhs.mEndPoint;
      mRadius = rhs.mRadius;
      mCenter = rhs.mCenter;
      mStartNormal = rhs.mStartNormal;
      mEndNormal = rhs.mEndNormal;
      mMoveType = rhs.mMoveType;
      mArc = rhs.mArc;
      mToolingName = rhs.mToolingName;
      mGCode = rhs.mGCode;
   }

   Point3 mStartPoint, mEndPoint, mCenter;
   Vector3 mStartNormal, mEndNormal;
   double mRadius;
   FCArc3? mArc;
   public FCArc3? Arc => mArc;
   EMove mMoveType;
   public EMove MoveType => mMoveType;
   string mToolingName;
   public string ToolingName { get => mToolingName; }
   public Point3 StartPoint => mStartPoint;
   public Point3 EndPoint => mEndPoint;
   public Point3 ArcCenter => mCenter;
   public double Radius => mRadius;
   public Vector3 StartNormal => mStartNormal;
   public Vector3 EndNormal => mEndNormal;

   public double Length {
      get {
         if (mGCode is EGCode.G0 or EGCode.G1)
            return StartPoint.DistTo (EndPoint);
         else if (mGCode is EGCode.G2 or EGCode.G3) {
            if (mArc == null)
               throw new Exception ("For G2 or G3, Arc is null");
            double length = mArc.Length;
            return length;
         }

         throw new NotSupportedException ("Unknown G Entity while computing length");
      }
   }

   public void SetGCLine (Point3 stPoint, Point3 endPoint,
                          Vector3 stNormal, Vector3 endNormal,
                          EGCode gcmd, EMove moveType, string toolingName) {
      if (gcmd is not EGCode.G0 and not EGCode.G1)
         throw new InvalidDataException ("The GCode cmd for line is wrong");
      mArc = null;
      mStartPoint = stPoint; mEndPoint = endPoint; mGCode = gcmd;
      mStartNormal = stNormal; mEndNormal = endNormal;
      mMoveType = moveType;
      mToolingName = toolingName;
   }

   public void SetGCArc (FCArc3 arc, Point3 stPoint, Point3 endPoint,
                         Point3 center, double radius, Vector3 stNormal,
                         EGCode gcmd, EMove moveType, string toolingName) {
      if (gcmd is not EGCode.G2 and not EGCode.G3)
         throw new InvalidDataException ("The GCode cmd for Arc is wrong");

      mStartPoint = stPoint;
      mEndPoint = endPoint;
      mCenter = center;
      mRadius = radius;
      mGCode = gcmd;
      mStartNormal = stNormal;
      mArc = arc;
      mMoveType = moveType;
      mToolingName = toolingName;
   }

   public void XfmToMachine (IGCodeGenerator codeGen) { // TODO remove argument
      mStartPoint = Utils.XfmToMachine (codeGen, mStartPoint);
      mEndPoint = Utils.XfmToMachine (codeGen, mEndPoint);
      mStartNormal = Utils.XfmToMachineVec (codeGen, mStartNormal);
      mEndNormal = Utils.XfmToMachineVec (codeGen, mEndNormal);
      if (IsArc ())
         mCenter = Utils.XfmToMachine (codeGen, mCenter);
   }

   public GCodeSeg XfmToMachineNew (IGCodeGenerator codeGGen) {
      GCodeSeg seg = new (this);
      seg.XfmToMachine (codeGGen);
      return seg;
   }

   public bool IsLine ()
      => mGCode is EGCode.G0 or EGCode.G1;

   public bool IsArc ()
      => mGCode is EGCode.G2 or EGCode.G3;
}

public static class Utils {
   public enum EPlane {
      YNeg,
      YPos,
      Top,
      Flex,
      None,
   }

   public enum EFlange {
      Bottom,
      Top,
      Web,
      Flex,
      TopFlex,
      BottomFlex,
      None
   }

   public enum EArcSense {
      CW, CCW, Infer, Unknown
   }
   public enum ArcType {
      Major,
      Minor,
      Semicircular
   }

   public enum FlexMachiningFlangeDirection {
      Web2Flange,
      Flange2Web,
      WebWJT,
      FlangeWJT
   }

   public const double EpsilonVal = 1e-6;
   public static Color32 LHToolColor = new (57, 255, 20); // neon green
   public static Color32 RHToolColor = new (255, 87, 51); // Neon Red
   public static Color32 ToolTipColor1 = new (70, 130, 180); // Steel Blue 
   public static Color32 ToolTipColor2 = new (44, 90, 140); // Steel Blue Dark
   public static Color32 SteelCutingSparkColor = new (255, 230, 80); // Bright Yellow Color
   public static Color32 SteelCutingSparkColor2 = new (255, 255, 60); // Bright Yellow Color
   public static Color32 G0SegColor = Color32.White;
   public static Color32 G1SegColor = Color32.Blue;
   public static Color32 G2SegColor = Color32.Magenta;
   public static Color32 G3SegColor = Color32.Cyan;

   /// <summary>
   /// This method computes the angle between two vectors about X axis.
   /// The angle is made negative if the cross product of the input vectors
   /// oppose the global X axis.
   /// </summary>
   /// <param name="fromPointPV">The from vector</param>
   /// <param name="toPointPV">The to vector</param>
   /// <returns>The angle between the two vectors specified</returns>
   public static double GetAngleAboutXAxis (Vector3 fromPointPV,
                                            Vector3 toPointPV, XForm4 xfm) {
      var stNormal = xfm * fromPointPV;
      var endNormal = xfm * toPointPV;
      var theta = stNormal.AngleTo (endNormal);
      theta *= GetAngleSignAbtX (stNormal, endNormal, XForm4.IdentityXfm);
      return theta;
   }

   /// <summary>
   /// This method returns the angle between the global Z axis and the normal 
   /// to either of the planes Top, or YPos, or YNeg. 
   /// </summary>
   /// <param name="planeType">One of the EPlane types.</param>
   /// <returns>EPlane Top returns 0.0, EPlane YPos returns -pi/2
   /// and EPlane YNeg returns pi/2 radians</returns>
   /// <exception cref="NotSupportedException">If EPlane is other than YPos, or YNeg or
   /// Top, a NotSupportedException is thrown</exception>
   public static double GetAngle4PlaneTypeAboutXAxis (EPlane planeType) {
      double angleBetweenStartAndEndPoints;
      if (planeType == Utils.EPlane.Top)
         angleBetweenStartAndEndPoints = 0.0;
      else if (planeType == Utils.EPlane.YPos)
         angleBetweenStartAndEndPoints = -Math.PI / 2.0;
      else if (planeType == Utils.EPlane.YNeg)
         angleBetweenStartAndEndPoints = Math.PI / 2.0;
      else
         throw new NotSupportedException ("Unsupported plane type encountered");

      return angleBetweenStartAndEndPoints;
   }

   public static int GetAngleSignAbtX (Vector3 stNormal, Vector3 endNormal, XForm4 xfm) {
      var stN = xfm * stNormal.Normalized ();
      var endN = xfm * endNormal.Normalized ();
      var cross = Geom.Cross (stN, endN).Normalized ();
      if (!cross.IsZero && cross.Opposing (XForm4.mXAxis)) return -1;
      return 1;
   }

   public static int GetAngleSignAbtX (Vector3 stNormal, Vector3 endNormal, Workpiece wp, IGCodeGenerator gcGen) {
      var stN = Utils.GetXForm (wp, gcGen) * stNormal.Normalized ();
      var endN = Utils.GetXForm (wp, gcGen) * endNormal.Normalized ();
      var cross = Geom.Cross (stN, endN).Normalized ();
      if (!cross.IsZero && cross.Opposing (XForm4.mXAxis)) return -1;
      return 1;
   }

   public static int GetAngleSignAbtX (Point3 stPoint, Point3 endPoint, Workpiece wp, IGCodeGenerator gcGen) {
      var stN = Utils.GetXForm (wp, gcGen) * Geom.P2V (stPoint).Normalized ();
      var endN = Utils.GetXForm (wp, gcGen) * Geom.P2V (endPoint).Normalized ();
      var cross = Geom.Cross (stN, endN).Normalized ();
      if (!cross.IsZero && cross.Opposing (XForm4.mXAxis)) return -1;
      return 1;
   }

   /// <summary>
   /// This method returns the plane type on which the arc is described.
   /// </summary>
   /// <param name="vec">The vector normal to the arc from the tooling.</param>
   /// <caveat>The vector normal should not be any vector other than the one
   /// obtained from the tooling</caveat>
   /// <returns>Returns one of Eplane.Top, Eplane.YPos, Eplane.YNeg or
   /// Eplane.Top.None</returns>
   public static EPlane GetArcPlaneType (Vector3 vec, XForm4 xfm) {
      xfm ??= XForm4.IdentityXfm;
      var trVec = xfm * vec.Normalized ();
      if (Workpiece.Classify (trVec) == Workpiece.EType.Top)
         return Utils.EPlane.Top;

      if (Workpiece.Classify (trVec) == Workpiece.EType.YPos)
         return Utils.EPlane.YPos;

      if (Workpiece.Classify (trVec) == Workpiece.EType.YNeg)
         return Utils.EPlane.YNeg;

      return EPlane.None;
   }

   /// <summary>
   /// Given a normal vector, this method finds the flange type.
   /// </summary>
   /// <param name="vec">The normal vector to the flange</param>
   /// <param name="xfm">Any additional transformation to the above normal vector</param>
   /// <remarks>The flange type is machine specific. If the xfm is identity, the flange type 
   /// is assumed to be in the local coordinate system of the part</remarks>
   /// <returns>Flange type</returns>
   /// <exception cref="NotSupportedException"></exception>
   public static Utils.EFlange GetArcPlaneFlangeType (Vector3 vec, XForm4 xfm) {
      xfm ??= XForm4.IdentityXfm;
      var trVec = xfm * vec.Normalized ();
      if (Workpiece.Classify (trVec) == Workpiece.EType.Top)
         return EFlange.Web;

      if (Workpiece.Classify (trVec) == Workpiece.EType.Top)
         return EFlange.Web;

      if (trVec.Normalized ().Y.SGT (-1.0) && trVec.Normalized ().Y.EQ (0) && trVec.Normalized ().Y.SLT (1.0))
         return EFlange.Flex;

      if (Workpiece.Classify (trVec) == Workpiece.EType.YPos) {
         if (MCSettings.It.PartConfig == MCSettings.PartConfigType.LHComponent)
            return EFlange.Top;
         else if (MCSettings.It.PartConfig == MCSettings.PartConfigType.RHComponent)
            return EFlange.Bottom;
      } else if (Workpiece.Classify (trVec) == Workpiece.EType.YNeg) {
         if (MCSettings.It.PartConfig == MCSettings.PartConfigType.LHComponent)
            return EFlange.Bottom;
         else if (MCSettings.It.PartConfig == MCSettings.PartConfigType.RHComponent)
            return EFlange.Top;
      }
      throw new NotSupportedException (" Flange type could not be assessed");
   }

   public static Utils.EFlange GetFlangeType (Vector3 vec, XForm4 xfm) {
      xfm ??= XForm4.IdentityXfm;
      var trVec = xfm * vec.Normalized ();
      if (trVec.EQ (XForm4.mZAxis))
         return EFlange.Web;
      else if (trVec.EQ (XForm4.mYAxis)) {
         if (MCSettings.It.PartConfig == PartConfigType.LHComponent)
            return EFlange.Top;
         else
            return EFlange.Bottom;
      } else if (trVec.EQ (XForm4.mNegYAxis)) {
         if (MCSettings.It.PartConfig == PartConfigType.LHComponent)
            return EFlange.Bottom;
         else
            return EFlange.Top;
      } else if (trVec.Y.SGT (0)) {
         if (MCSettings.It.PartConfig == PartConfigType.LHComponent)
            return EFlange.TopFlex;
         else
            return EFlange.BottomFlex;
      } else if (trVec.Y.SLT (0)) {
         if (MCSettings.It.PartConfig == PartConfigType.LHComponent)
            return EFlange.BottomFlex;
         else
            return EFlange.TopFlex;
      } else
         throw new Exception ("GetFlangeType: Flange type could not be assessed");
   }

   /// <summary>
   /// This method is a handy one to project a 3d point onto XY or XZ plane
   /// </summary>
   /// <param name="pt">The 3d point</param>
   /// <param name="ep">One of the E3Planes, YNeg, YPos or Top planes</param>
   /// <returns>The 2d point, which is the projection of 3d point on the plane</returns>
   public static Point2 ToPlane (Point3 pt, EPlane ep) => ep switch {
      EPlane.YNeg => new Point2 (pt.X, pt.Z),
      EPlane.YPos => new Point2 (pt.X, pt.Z),
      _ => new Point2 (pt.X, pt.Y),
   };

   public static Vector2 ToPlane (Vector3 pt, EPlane ep) => ep switch {
      EPlane.YNeg => new Vector2 (pt.X, pt.Z),
      EPlane.YPos => new Vector2 (pt.X, pt.Z),
      _ => new Vector2 (pt.X, pt.Y),
   };

   /// <summary>
   /// This method returns the EFlange type for the tooling. 
   /// </summary>
   /// <param name="toolingItem">The tooling</param>
   /// <param name="xfm">Any additional transformation to the above normal vector</param>
   /// <remarks>The flange type is machine specific. If the xfm is identity, the flange type 
   /// is assumed to be in the local coordinate system of the part</remarks>
   /// <returns>The EFlange type for the tooling, considering if the workpiece
   /// is LH or RH component, set.</returns>
   /// <exception cref="NotSupportedException">This exception is thrown if the plane type 
   /// could not be deciphered</exception>
   public static EFlange GetFlangeType (Tooling toolingItem, XForm4? xfm) {
      xfm ??= XForm4.IdentityXfm;
      //var trVec = xfm * toolingItem.Start.Vec.Normalized ();

      if (toolingItem.IsPlaneFeature ())
         return GetArcPlaneFlangeType (toolingItem.Start.Vec.Normalized (), xfm);

      if (toolingItem.IsFlexFeature ())
         return Utils.EFlange.Flex;

      throw new NotSupportedException (" Flange type could not be assessed");
   }

   /// <summary>
   /// THis methos returns the plane type given the tooling
   /// </summary>
   /// <param name="toolingItem">The input tooling item.</param>
   /// <param name="xfm">Any additional transformation to the above normal vector</param>
   /// <returns>Returns one of the EPlane types such as EPlane.Top, YPos or YNeg</returns>
   /// <exception cref="InvalidOperationException">If the plane type could not be deciphered.</exception>
   public static EPlane GetPlaneType (Tooling toolingItem, XForm4 xfm) {
      xfm ??= XForm4.IdentityXfm;
      var trVec = xfm * toolingItem.Start.Vec.Normalized ();
      if (toolingItem.IsPlaneFeature ()) {
         if (Workpiece.Classify (trVec) == Workpiece.EType.Top)
            return EPlane.Top;

         if (Workpiece.Classify (trVec) == Workpiece.EType.YPos)
            return EPlane.YPos;

         if (Workpiece.Classify (trVec) == Workpiece.EType.YNeg)
            return EPlane.YNeg;
      }

      if (toolingItem.IsFlexFeature ())
         return Utils.EPlane.Flex;

      throw new InvalidOperationException (" The feature is neither plane nor flex");
   }

   public static EPlane GetPlaneType (Vector3 normal, XForm4 xfm)
      => GetArcPlaneType (normal, xfm);

   /// <summary>
   /// This method returns the Vector3 normal emanating from the E3Plane
   /// given the tooling.
   /// </summary>
   /// <param name="toolingItem">The input tooling</param>
   /// <param name="xfm">Any additional transformation to the above normal vector</param>
   /// <returns>The normal to the plane in the direction emanating 
   /// from the plane</returns>
   /// <exception cref="InvalidOperationException"></exception>
   public static Vector3 GetEPlaneNormal (Tooling toolingItem, XForm4 xfm) {
      xfm ??= XForm4.IdentityXfm;
      var trVec = xfm * toolingItem.Start.Vec.Normalized ();
      if (toolingItem.IsPlaneFeature ()) {
         if (Workpiece.Classify (trVec) == Workpiece.EType.Top)
            return XForm4.mZAxis;

         if (Workpiece.Classify (trVec) == Workpiece.EType.YPos)
            return XForm4.mYAxis;

         if (Workpiece.Classify (trVec) == Workpiece.EType.YNeg)
            return -XForm4.mYAxis;
      }

      if (toolingItem.IsFlexFeature ()) {
         var segs = toolingItem.Segs;
         Vector3 n = new (0.0, Math.Sqrt (2.0), Math.Sqrt (2.0));
         var yNegFlex = segs.Any (cutSeg => cutSeg.Vec0.Normalized ().Y < -0.1);
         if (yNegFlex)
            n = new (0.0, -Math.Sqrt (2.0), Math.Sqrt (2.0));

         return n;
      }

      throw new InvalidOperationException (" The feature is neither plane nor flex");
   }

   /// <summary>This method finds the ordinate direction of the given vector featureNormal.</summary>
   /// <param name="featureNormal">The normal to the feature ( line,arc or circle)</param>
   /// <param name="trf">Any additional transformation to the above normal vector</param>
   /// <returns>
   /// One of the ordinate direction (FChassisUtils.EPlane.Top or FChassisUtils.EPlane.YNeg or FChassisUtils.EPlane.YPos )
   /// which strictly aligns [ Abs(dotproduct) = 1.0 ] with the featureNormal. 
   /// Returns FChassisUtils.EPlane.Flex for other cases
   /// </returns>
   /// <exception cref="NegZException">This exception is thrown if a negative Z normal is encountered</exception>
   public static EPlane GetFeatureNormalPlaneType (Vector3 featureNormal, XForm4 trf) {
      featureNormal = featureNormal.Normalized ();
      trf ??= XForm4.IdentityXfm;
      featureNormal = trf * featureNormal;
      var zAxis = new Vector3 (0, 0, 1);
      var yAxis = new Vector3 (0, 1, 0);
      var featureNormalDotZAxis = featureNormal.Dot (zAxis);
      var featureNormalDotYAxis = featureNormal.Dot (yAxis);

      if (Math.Abs (featureNormalDotZAxis - 1.0) < EpsilonVal)
         return EPlane.Top;

      if (Math.Abs (featureNormalDotZAxis + 1.0) < EpsilonVal)
         throw new NegZException ();

      if (Math.Abs (featureNormalDotYAxis - 1.0) < EpsilonVal)
         return EPlane.YPos;

      if (Math.Abs (featureNormalDotYAxis + 1.0) < EpsilonVal)
         return EPlane.YNeg;

      return EPlane.Flex;
   }

   /// <summary>
   /// This method finds the cross product between two vectors (a cross b)
   /// </summary>
   /// <param name="a">First vector</param>
   /// <param name="b">Seond vector</param>
   /// <returns></returns>
   public static Vector3 CrossProduct (Vector3 a, Vector3 b) {
      return new Vector3 (
          a.Y * b.Z - a.Z * b.Y,
          a.Z * b.X - a.X * b.Z,
          a.X * b.Y - a.Y * b.X);
   }

   public static bool IsCircle (FCCurve3 curve)
      => curve != null && curve is FCArc3 && curve.Start.EQ (curve.End);

   //public static bool IsCircle (FCCurve3 curve)
   //   => curve != null && curve is FCArc3 && curve.IsCircle;

   public static bool IsArc (FCCurve3 curve)
      => curve != null && curve is FCArc3 && !curve.Start.EQ (curve.End);

   /// <summary>
   /// This method creates and returns a point, which is moved from the 
   /// input point, alone the direction specified upto a specific distance
   /// along that vector.
   /// </summary>
   /// <param name="pt">The input ref point</param>
   /// <param name="dir">The direction along which the new point has to be computed</param>
   /// <param name="moveLength">The distance by which the new point has to be moved 
   /// along the direction</param>
   /// <returns>The new point from a ref point, along a direction, by a specific distance.</returns>
   public static Point3 MovePoint (Point3 pt, Vector3 dir, double moveLength) {
      var normDir = dir.Normalized ();
      return (pt + normDir * moveLength);
   }

   /// <summary>
   /// This method discretizes a given arc with no of steps input
   /// </summary>
   /// <param name="seg">The given arc segment</param>
   /// <param name="steps">No of steps</param>
   /// <returns>List of Tuples of the intermediate points with a linearly interpolated normals</returns>
   public static List<Tuple<Point3, Vector3>>? DiscretizeArc (GCodeSeg seg, int steps) {
      List<Tuple<Point3, Vector3>>? res = null;
      if (!seg.IsArc () || (seg.GCode != EGCode.G2 && seg.GCode != EGCode.G3))
         return res;

      res = [];
      double theta = 2 * Math.PI;
      Vector3 center2Start = seg.StartPoint - seg.ArcCenter;
      Vector3 center2End = seg.EndPoint - seg.ArcCenter;
      Vector3 crossVec = Geom.Cross (center2Start, center2End);
      if (crossVec.Length.EQ (0)) {
         var otherPt = Geom.P2V (Geom.Evaluate (seg.Arc, 0.5, seg.StartNormal));
         crossVec = Geom.Cross (center2Start, otherPt);
      }

      if (!seg.StartPoint.DistTo (seg.EndPoint).EQ (0)) {
         var val = center2Start.Dot (center2End) / (center2Start.Length * center2End.Length);
         if (val < -1)
            val += 1e-6;
         else if (val > 1)
            val -= 1e-6;

         val = val.Clamp (-1.0, 1.0);
         theta = Math.Acos (val);

         if (seg.GCode == EGCode.G3 && crossVec.Dot (seg.StartNormal) < 0.0)
            theta = 2 * Math.PI - theta;
         else if (seg.GCode == EGCode.G2 && crossVec.Dot (seg.StartNormal) > 0.0)
            theta = 2 * Math.PI - theta;

         if (seg.GCode == EGCode.G2 && theta > 0.0)
            theta = -theta;
      }

      double delAlpha = theta / steps;
      for (int k = 0; k <= steps; k++) {
         double alphaK = k * delAlpha;
         Vector3 comp1 = center2Start * Math.Cos (alphaK);
         Vector3 comp2 = Geom.Cross (seg.StartNormal, center2Start) * Math.Sin (alphaK);
         Vector3 comp3 = seg.StartNormal * seg.StartNormal.Dot (center2Start) * (1.0 - Math.Cos (alphaK));
         Vector3 vRot = comp1 + comp2 + comp3;
         res.Add (new Tuple<Point3, Vector3> (seg.ArcCenter + Geom.V2P (vRot), seg.StartNormal));
      }

      return res;
   }

   /// <summary>
   /// This method discretizes a given line segment
   /// </summary>
   /// <param name="seg">Segment to be discretized</param>
   /// <param name="steps">No of each discretized line segments</param>
   /// <returns>A list of tuples of intermediate points, interpolated with normals</returns>
   public static List<Tuple<Point3, Vector3>>? DiscretizeLine (GCodeSeg seg, int steps) {
      List<Tuple<Point3, Vector3>>? res = null;
      if (seg.IsArc () || (seg.GCode != EGCode.G0 && seg.GCode != EGCode.G1))
         return res;

      res = [];
      double stepLength = seg.StartPoint.DistTo (seg.EndPoint) / steps;
      var prevNormal = seg.StartNormal.Normalized ();

      // For smooth transitioning of the tool, the normal's change from previous seg's last point to the 
      // current seg's last point should be gradual. This requires linear interpolation.
      res.Add (new Tuple<Point3, Vector3> (seg.StartPoint, seg.StartNormal.Normalized ()));
      for (int k = 1; k < steps - 1; k++) {
         var pt1 = Utils.MovePoint (seg.StartPoint,
                                    Geom.P2V (seg.EndPoint) - Geom.P2V (seg.StartPoint), k * stepLength);
         var angleBetweenStNormalEndNormal = Utils.GetAngleAboutXAxis (seg.StartNormal,
                                                                       seg.EndNormal,
                                                                       XForm4.IdentityXfm);
         if (angleBetweenStNormalEndNormal.EQ (0.0))
            res.Add (new Tuple<Point3, Vector3> (pt1, seg.StartNormal.Normalized ()));
         else if ((Math.Abs ((seg.EndPoint - seg.StartPoint).Normalized ().Dot (XForm4.mZAxis)) - 1.0).EQ (0.0)) {
            var t = (double)k / (double)(steps - 1);
            var newNormal = seg.StartNormal.Normalized () * (1 - t) + seg.EndNormal.Normalized () * t;
            newNormal = newNormal.Normalized ();
            res.Add (new Tuple<Point3, Vector3> (pt1, newNormal));
         } else {
            var pt0 = Utils.MovePoint (seg.StartPoint,
                                       Geom.P2V (seg.EndPoint) - Geom.P2V (seg.StartPoint),
                                       (k - 1) * stepLength);
            var pt2 = Utils.MovePoint (seg.StartPoint,
                                       Geom.P2V (seg.EndPoint) - Geom.P2V (seg.StartPoint),
                                       (k + 1) * stepLength);
            var norm1 = Geom.Cross ((pt1 - pt0), XForm4.mXAxis);
            if (norm1.Opposing (prevNormal))
               norm1 = -norm1;

            prevNormal = norm1;

            var norm2 = Geom.Cross ((pt2 - pt1), XForm4.mXAxis);
            if (norm2.Opposing (prevNormal))
               norm2 = -norm2;

            var norm = ((norm2 + norm1) * 0.5).Normalized ();
            prevNormal = norm;
            res.Add (new Tuple<Point3, Vector3> (pt1, norm));
         }
      }
      res.Add (new Tuple<Point3, Vector3> (seg.EndPoint, seg.EndNormal.Normalized ()));
      return res;
   }

   /// <summary>
   /// This is an utility method to return the ordinate vector from
   /// input aaxis
   /// </summary>
   /// <param name="axis">The axis</param>
   /// <returns>The vector that the input axis' points to</returns>
   /// <exception cref="NotSupportedException">If an axis is non-ordinate, an exception is
   /// thrown</exception>
   public static Vector3 GetUnitVector (XForm4.EAxis axis) {
      Vector3 res = axis switch {
         XForm4.EAxis.NegZ => -XForm4.mZAxis,
         XForm4.EAxis.Z => XForm4.mZAxis,
         XForm4.EAxis.NegX => -XForm4.mXAxis,
         XForm4.EAxis.X => XForm4.mXAxis,
         XForm4.EAxis.NegY => -XForm4.mYAxis,
         XForm4.EAxis.Y => XForm4.mYAxis,
         _ => throw new NotSupportedException ("Unsupported XForm.EAxis type")
      };

      return res;
   }

   /// <summary>
   /// This method returns the scrap side or material removal side direction w.r.t the tooling start segment
   /// w.r.t to the mid point of the first segment
   /// </summary>
   /// <param name="tooling"></param>
   /// <returns>If Line or Arc Thsi returns the mid point of the tooling segment along with the material 
   /// removal side direction evaluated at the mid point.
   /// If Circle, this returns the start point of the circle and the material removal direction 
   /// evaluated at the starting point.</returns>
   public static Tuple<Point3, Vector3> GetMaterialRemovalSideDirection (Tooling tooling) {
      var segmentsList = tooling.Segs.ToList ();
      var toolingPlaneNormal = segmentsList[0].Vec0;

      // Tooling direction as the direction of the st to end point in the case of line OR
      // tangent int he direction of start to end of the arc in the case of an arc
      Vector3 toolingDir;
      Point3 newToolingEntryPoint;
      if (Utils.IsCircle (segmentsList[0].Curve))
         newToolingEntryPoint = segmentsList[0].Curve.Start;
      else
         newToolingEntryPoint = Geom.GetMidPoint (segmentsList[0].Curve, toolingPlaneNormal);

      if (segmentsList[0].Curve is FCArc3 arc)
         (toolingDir, _) = Geom.EvaluateTangentAndNormalAtPoint (arc, newToolingEntryPoint, toolingPlaneNormal, tolerance: 1e-3);
      else
         toolingDir = (segmentsList[0].Curve.End - segmentsList[0].Curve.Start).Normalized ();

      // Ref points along the direction of the binormal, which is along or opposing the direction
      // in which the material removal side exists.
      var biNormal = Geom.Cross (toolingDir, toolingPlaneNormal).Normalized ();
      Vector3 scrapSideDirection = biNormal.Normalized ();
      if (Geom.Cross (toolingDir, biNormal).Opposing (toolingPlaneNormal))
         scrapSideDirection = -biNormal;

      return new (newToolingEntryPoint, scrapSideDirection);
   }

   public static Tuple<Point3, Vector3> GetMaterialRemovalSideDirection (List<ToolingSegment> segmentsList) {
      var toolingPlaneNormal = segmentsList[0].Vec0;

      // Tooling direction as the direction of the st to end point in the case of line OR
      // tangent int he direction of start to end of the arc in the case of an arc
      Vector3 toolingDir;
      Point3 newToolingEntryPoint;
      if (Utils.IsCircle (segmentsList[0].Curve))
         newToolingEntryPoint = segmentsList[0].Curve.Start;
      else
         newToolingEntryPoint = Geom.GetMidPoint (segmentsList[0].Curve, toolingPlaneNormal);

      if (segmentsList[0].Curve is FCArc3 arc)
         (toolingDir, _) = Geom.EvaluateTangentAndNormalAtPoint (arc, newToolingEntryPoint, toolingPlaneNormal, tolerance: 1e-3);
      else
         toolingDir = (segmentsList[0].Curve.End - segmentsList[0].Curve.Start).Normalized ();

      // Ref points along the direction of the binormal, which is along or opposing the direction
      // in which the material removal side exists.
      var biNormal = Geom.Cross (toolingDir, toolingPlaneNormal).Normalized ();
      Vector3 scrapSideDirection = biNormal.Normalized ();
      if (Geom.Cross (toolingDir, biNormal).Opposing (toolingPlaneNormal))
         scrapSideDirection = -biNormal;

      return new (newToolingEntryPoint, scrapSideDirection);
   }

   public static Vector3 GetMaterialRemovalSideDirection (ToolingSegment ts, Point3 pt, EKind featType, ECutKind profileKind = ECutKind.None) {
      var toolingPlaneNormal = ts.Vec0;
      if (!Geom.IsPointOnCurve (ts.Curve, pt, toolingPlaneNormal, tolerance: (ts.Curve is FCArc3) ? 1e-3 : 1e-6))
         throw new Exception ("In GetMaterialRemovalSideDirection: The given point is not on the Tool Segment's Curve");

      // Tooling direction as the direction of the st to end point in the case of line OR
      // tangent int he direction of start to end of the arc in the case of an arc
      Vector3 toolingDir;
      Point3 newToolingEntryPoint;
      if (Utils.IsCircle (ts.Curve))
         newToolingEntryPoint = ts.Curve.Start;
      else
         newToolingEntryPoint = Geom.GetMidPoint (ts.Curve, toolingPlaneNormal);

      if (ts.Curve is FCArc3 arc)
         (toolingDir, _) = Geom.EvaluateTangentAndNormalAtPoint (arc, newToolingEntryPoint, toolingPlaneNormal, tolerance: 1e-3);
      else
         toolingDir = (ts.Curve.End - ts.Curve.Start).Normalized ();

      // Ref points along the direction of the binormal, which is along or opposing the direction
      // in which the material removal side exists.
      var biNormal = Geom.Cross (toolingDir, toolingPlaneNormal).Normalized ();
      Vector3 scrapSideDirection = biNormal.Normalized ();

      if (featType == EKind.Notch) {
         // The profile is clockwise
         if (profileKind == ECutKind.Top2YNeg && Geom.Cross (toolingDir, biNormal).IsSameSense (toolingPlaneNormal))
            scrapSideDirection = -biNormal;
         // The profile is counter-clockwise
         else if ((profileKind == ECutKind.Top2YPos || profileKind == ECutKind.Top) && Geom.Cross (toolingDir, biNormal).Opposing (toolingPlaneNormal))
            scrapSideDirection = -biNormal;
      } else if (featType == EKind.Cutout) {
         // The profile is always counter-clockwise
         scrapSideDirection = -biNormal;
      }

      return scrapSideDirection;
   }



   public static List<ToolingSegment> MoveStartSegToPriorityFlange (List<ToolingSegment> toolingSegmentsList, EFlange priorityFlange) {
      int newStartIndex = -1;
      List<ToolingSegment> outList = [];
      for (int ii = 0; ii < toolingSegmentsList.Count; ii++) {
         var plType1 = Utils.GetFeatureNormalPlaneType (toolingSegmentsList[ii].Vec0, XForm4.IdentityXfm);
         var plType2 = Utils.GetFeatureNormalPlaneType (toolingSegmentsList[ii].Vec1, XForm4.IdentityXfm);
         if (plType1 == plType2 && plType1 != EPlane.Flex) {
            if (priorityFlange == EFlange.Web && plType1 == EPlane.Top) {
               newStartIndex = ii;
               break;
            } else if ((priorityFlange == EFlange.Top || priorityFlange == EFlange.Bottom) &&
               (plType1 == EPlane.YPos || plType1 == EPlane.YNeg)) {
               newStartIndex = ii;
               break;
            }
         }
      }
      if (newStartIndex != -1)
         outList = [.. toolingSegmentsList.Skip (newStartIndex), .. toolingSegmentsList.Take (newStartIndex)];
      if (outList.Count == 0)
         return toolingSegmentsList;
      else
         return outList;
   }

   /// <summary>
   /// 
   /// </summary>
   /// <param name="toolingItem"></param>
   /// <param name="gcgen"></param>
   /// <param name="leastCurveLength"></param>
   /// <returns></returns>

   public static List<ToolingSegment> AddLeadinToTooling (Tooling toolingItem, List<ToolingSegment> segs,
      IGCodeGenerator? gcgen = null, double leastCurveLength = 0.5) {
      // If the tooling item is Mark, no need of creating the G Code
      if (toolingItem.IsMark ()) return [.. toolingItem.Segs];

      List<ToolingSegment> modifiedSegmentsList = [];
      List<ToolingSegment> toolingSegmentsList;
      if (segs == null || segs.Count == 0)
         toolingSegmentsList = [.. toolingItem.Segs];
      else
         toolingSegmentsList = segs;

      // Do not make the flex tool segment as the first in the segment list
      if (toolingItem.IsDualFlangeCutoutNotch ()) {
         //EFlange priorityFlange = EFlange.Web;
         //int newStartIndex = -1;

         //for (int ii = 0; ii < toolingSegmentsList.Count; ii++) {
         //   var plType1 = Utils.GetFeatureNormalPlaneType (toolingSegmentsList[ii].Vec0, XForm4.IdentityXfm);
         //   var plType2 = Utils.GetFeatureNormalPlaneType (toolingSegmentsList[ii].Vec1, XForm4.IdentityXfm);
         //   if (plType1 == plType2 && plType1 != EPlane.Flex) {
         //      if (priorityFlange == EFlange.Web && plType1 == EPlane.Top) {
         //         newStartIndex = ii;
         //         break;
         //      } else if ((priorityFlange == EFlange.Top || priorityFlange == EFlange.Bottom) &&
         //         (plType1 == EPlane.YPos || plType1 == EPlane.YPos)) {
         //         newStartIndex = ii;
         //         break;
         //      }
         //   }
         //}

         //if (newStartIndex != -1)
         //   toolingSegmentsList = [.. toolingSegmentsList.Skip (newStartIndex), .. toolingSegmentsList.Take (newStartIndex)];
         toolingSegmentsList = Utils.MoveStartSegToPriorityFlange (toolingSegmentsList, EFlange.Web);
      }

      Vector3 materialRemovalDirection; Point3 firstToolingEntryPt;
      if (!toolingItem.IsNotch () && !toolingItem.IsMark ()) {
         // E3Plane normal 
         //var apn = Utils.GetEPlaneNormal (toolingItem, XForm4.IdentityXfm);
         var apn = toolingSegmentsList[0].Vec0.Normalized ();

         // Compute an appropriate approach length. From the engg team, 
         // it was asked to have a dia = approach length * 4, which is 
         // stored in approachDistOfArc. 
         // if the circle's dia is smaller than the above approachDistOfArc
         // then assign approach length from settings. 
         // Recursively find the approachDistOfArc by halving the previous value
         // until its not lesser than 0.5. 0.5 is the lower limit.
         double approachLen = gcgen?.ApproachLength ?? MCSettings.It.ApproachLength;
         var approachDistOfArc = approachLen * 4.0;
         double circleRad;
         if (toolingSegmentsList[0].Curve is FCArc3 circle && Utils.IsCircle (circle)) {
            (_, circleRad) = Geom.EvaluateCenterAndRadius (circle);
            if (circleRad < approachDistOfArc) approachDistOfArc = approachLen;
            while (circleRad < approachDistOfArc) {
               if (approachDistOfArc < leastCurveLength) break;
               approachDistOfArc *= leastCurveLength;
            }
         }

         // Compute the scrap side direction
         (firstToolingEntryPt, materialRemovalDirection) = Utils.GetMaterialRemovalSideDirection (toolingSegmentsList);

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
         //var p1 = firstToolingEntryPt - toolingDir * 4.0; var p2 = firstToolingEntryPt - toolingDir * 2.0;
         FCCurve3 chord = new FCLine3 (newToolingStPt, firstToolingEntryPt);
         var p1 = chord.Curve.Evaluate (0.4);
         var p2 = chord.Curve.Evaluate (0.7);

         // Compute the vectors from center of the arc to the above points
         var cp1 = (p1 - arcCenter).Normalized (); var cp2 = (p2 - arcCenter).Normalized ();

         // Compute the intersection of the vector cenetr to p1/p2 on the circle of the arc. These are 
         // intermediate points along the actual direction of the arc
         var ip1 = arcCenter + cp1 * approachArcRad; var ip2 = arcCenter + cp2 * approachArcRad;

         //var (cirCenter, cirRad) = EvaluateCenterAndRadius (toolingSegmentsList[0].Curve as FCArc3);
         // Create arc, the fourth point being the midpoint of the arc or starting point
         // if its a circle.
         FCArc3 arc = new (newToolingStPt, ip1, ip2, firstToolingEntryPt, toolingSegmentsList[0].Vec0);
         if (Utils.IsCircle (toolingSegmentsList[0].Curve)) {
            modifiedSegmentsList.Add (new (arc, toolingSegmentsList[0].Vec0, toolingSegmentsList[0].Vec0));
            modifiedSegmentsList.Add (toolingSegmentsList[0]);
            return modifiedSegmentsList;
         } else {
            List<Point3> internalPoints = [];
            internalPoints.Add (Geom.GetMidPoint (toolingSegmentsList[0].Curve, apn));
            var splitCurves = Geom.SplitCurve (toolingSegmentsList[0].Curve, internalPoints, apn, deltaBetween: 0.0);
            var meanNormal = (toolingSegmentsList[0].Vec0 + toolingSegmentsList[0].Vec1) / 2.0;
            modifiedSegmentsList.Add (new (arc, meanNormal, meanNormal));
            modifiedSegmentsList.Add (new (splitCurves[1], meanNormal, toolingSegmentsList[0].Vec1));
            for (int ii = 1; ii < toolingSegmentsList.Count; ii++) modifiedSegmentsList.Add (toolingSegmentsList[ii]);
            modifiedSegmentsList.Add (new (splitCurves[0], toolingSegmentsList[0].Vec0, meanNormal));

            return modifiedSegmentsList;
         }
      } else return toolingSegmentsList;
   }

   /// <summary>
   /// This is the primary method to evaluate the notch point on a tooling item. The tooling item contains
   /// the segments, which are a list of Larcs (Line and Arcs), Line3 in 2d and 3d and planar arcs.
   /// </summary>
   /// <param name="segments">The list of tooling segments on which the notch points need to be evaluated</param>
   /// <param name="percentage">This is the ratio of the length from start of the tooling segments' total length.</param>
   /// <param name="leastCurveLength">This least possible length of the curve, 
   /// below which it is assumed a curve of zero length</param>
   /// <returns> A tuple of the index of the occurance of the point and the point itself, at the percentage of 
   /// the total length of the entire tooling</returns>
   /// <exception cref="Exception">An exception is thrown if the percentage is less than 0 or more than 100</exception>
   public static Tuple<int, Point3> GetNotchPointsOccuranceParams (List<ToolingSegment> segments,
                                                                   double percentage, double leastCurveLength = 0.5) {
      if (percentage < 1e-6 || percentage > 1.0 - 1e-6)
         throw new Exception ("Notch entry points can not be lesser 0% or more than 100%");

      var totalSegsLength = segments.Sum (seg => seg.Curve.Length);
      double percentLength = percentage * totalSegsLength;
      double segmentLengthAtNotch = 0;
      int jj = 0;
      while (segmentLengthAtNotch < percentLength) {
         segmentLengthAtNotch += segments[jj].Curve.Length;
         jj++;
      }

      var segmentLength = percentLength;
      int occuranceIndex = jj - 1;
      double previousCurveLengths = 0.0;
      for (int kk = occuranceIndex - 1; kk >= 0; kk--)
         previousCurveLengths += segments[kk].Curve.Length;

      segmentLength -= previousCurveLengths;

      // 25% of length can not happen to be almost close to the first segment's start point
      // but shall happen for the second segment onwards
      Point3 notchPoint;
      Tuple<int, Point3> notchPointOccuranceParams;
      var distToPrevSegsEndPoint = leastCurveLength;
      if (segmentLength < distToPrevSegsEndPoint)
         // in case of segmentLength is less than threshold, the notch attr is set as the 
         // index of the previous segments index and the point to be the end point of the 
         // previous segment's index.
         notchPointOccuranceParams = new (occuranceIndex - 1, segments[occuranceIndex - 1].Curve.End);
      else if (segments[occuranceIndex].Curve.Length - segmentLength < distToPrevSegsEndPoint)
         notchPointOccuranceParams = new (occuranceIndex, segments[occuranceIndex].Curve.End);
      else {
         notchPoint = Geom.GetPointAtLengthFromStart (segments[occuranceIndex].Curve,
                                                      segments[occuranceIndex].Vec0.Normalized (),
                                                      segmentLength);
         notchPointOccuranceParams = new (occuranceIndex, notchPoint);
      }

      return notchPointOccuranceParams;
   }


   /// <summary>
   /// This method is used to check the sanity of the tooling segments by checking the 
   /// G0 continuity
   /// </summary>
   /// <param name="segs">The input list of tooling segments</param>
   /// <exception cref="Exception">An exception is thrown if any segmnt misses 
   /// G0 continuity with its neighbor (with in a general tolerance of 1e-6)</exception>
   public static void CheckSanityOfToolingSegments (List<ToolingSegment> segs) {
      for (int ii = 1; ii < segs.Count; ii++) {
         //var dist = segs[ii - 1].Curve.End.DistTo (segs[ii].Curve.Start);
         if (!segs[ii - 1].Curve.End.DistTo (segs[ii].Curve.Start).EQ (0, 1e-3))
            throw new Exception ("There is a discontinuity in tooling segments");
      }
   }

   /// <summary>
   /// This method shall be invoked if the tooling segments exhibit a C-0 discontinuity 
   /// This is addressed by the following strategy.
   /// if (i-1)th line seg ends at discontinuity with i-th segment, a new line seg is created
   /// in the place of i-1th seg, whose start position is old i-1-th pos and end pos is i-th seg's start.
   /// if (i-1)th arc seg ends at discontinuity with i-th segment, a new line seg is created
   /// a new line is created from i-1-th arc seg's end to i-th seg's end and replaced with i-th seg
   /// if (i-1)th line seg ends at discontinuity with i-th arc segment, a new line seg is created
   /// from i-1-th seg's start to i-th seg's start and replaced with i-1th line segment
   /// if (i-1)th arc seg ends at a discontinuity with i-th arc seg, then a line segment is 
   /// inserted in between the two arc segments
   /// </summary>
   /// <param name="segs"></param>
   /// <returns></returns>
   /// <exception cref="Exception">Throws an exception if the types of the segments are neither arc
   /// nor line</exception>
   public static List<ToolingSegment> FixSanityOfToolingSegments (ref List<ToolingSegment> segs) {
      var fixedSegs = segs;
      for (int ii = 1; ii < fixedSegs.Count; ii++) {
         if (!fixedSegs[ii - 1].Curve.End.DistTo (fixedSegs[ii].Curve.Start).EQ (0)) {
            if (fixedSegs[ii - 1].Curve is FCLine3 && fixedSegs[ii].Curve is FCLine3) {
               var newLine = new FCLine3 (fixedSegs[ii - 1].Curve.Start, fixedSegs[ii].Curve.Start);
               var newTS = Geom.CreateToolingSegmentForCurve (newLine as FCCurve3, fixedSegs[ii - 1].Vec0, fixedSegs[ii].Vec0);
               fixedSegs[ii - 1] = newTS;
            } else if (fixedSegs[ii - 1].Curve is FCArc3 && fixedSegs[ii].Curve is FCLine3) {
               var newLine = new FCLine3 (fixedSegs[ii - 1].Curve.End, fixedSegs[ii].Curve.End);
               var newTS = Geom.CreateToolingSegmentForCurve (newLine as FCCurve3, fixedSegs[ii - 1].Vec1, fixedSegs[ii].Vec1);
               fixedSegs[ii] = newTS;
            } else if (fixedSegs[ii - 1].Curve is FCLine3 && fixedSegs[ii].Curve is FCArc3) {
               var newLine = new FCLine3 (fixedSegs[ii - 1].Curve.Start, fixedSegs[ii].Curve.Start);
               var newTS = Geom.CreateToolingSegmentForCurve (newLine as FCCurve3, fixedSegs[ii - 1].Vec0, fixedSegs[ii].Vec0);
               fixedSegs[ii - 1] = newTS;
            } else if (fixedSegs[ii - 1].Curve is FCArc3 && fixedSegs[ii].Curve is FCArc3) {
               // Create a link line between arcs
               var newLine = new FCLine3 (fixedSegs[ii - 1].Curve.End, fixedSegs[ii].Curve.Start);
               var newTS = Geom.CreateToolingSegmentForCurve (newLine as FCCurve3, fixedSegs[ii - 1].Vec1, fixedSegs[ii].Vec0);
               fixedSegs.Insert (ii, newTS); ii--;
            } else
               throw new Exception ("Utils.FixSanityOfToolingSegments: Unknown segment type encountered");
         }
      }

      CheckSanityOfToolingSegments (fixedSegs);
      return fixedSegs;
   }

   /// <summary>
   /// This method is used to split the given tooling segments as defined by the points
   /// prescribed in the notchPointsInfo list
   /// </summary>
   /// <param name="segments">The input segments and also the output</param>
   /// <param name="notchPtsInfo">The input notchPointsInfo list and also the output</param>
   /// <param name="tolerance">The epsilon tolerance, which is by default 1e-6</param>
   public static void SplitToolingSegmentsAtPoints (ref List<ToolingSegment> segments,
                                                    ref List<NotchPointInfo> notchPtsInfo,
                                                    double[] percentPos,
                                                    int[] segIndices,
                                                    double tolerance = 1e-6) {
      List<Point3> nptInterestPts = [], nptPts = [];
      int[] newSegIndices = [.. segIndices];
      Point3[] notchPoints = new Point3[percentPos.Length];
      int idx = 0;
      for (int ii = 0; ii < notchPtsInfo.Count; ii++) {
         nptInterestPts = [];
         for (int jj = 0; jj < notchPtsInfo[ii].mPoints.Count && segIndices.Contains (notchPtsInfo[ii].mSegIndex); jj++)
            nptInterestPts.Add (notchPtsInfo[ii].mPoints[jj]);
         for (int pp = 0; pp < nptInterestPts.Count; pp++) {
            var npt = nptInterestPts[pp];
            int segIndex = segments.FindIndex (s => s.Curve.End.DistTo (npt).EQ (0, tolerance));
            if (segIndex == -1) {
               for (int kk = 0; kk < segments.Count; kk++) {
                  if (Geom.IsPointOnCurve (segments[kk].Curve, npt, segments[kk].Vec0, hintSense: EArcSense.Infer, tolerance, true)) {
                     segIndex = kk; break;
                  }
               }
            }
            var crvs = Geom.SplitCurve (segments[segIndex].Curve,
                                        [npt],
                                        segments[segIndex].Vec0.Normalized (),
                                        deltaBetween: 0, hintSense: EArcSense.Infer, tolerance);
            if (crvs.Count > 1) {
               var toolSegsForCrvs = Geom.CreateToolingSegmentForCurves (segments[segIndex], crvs);
               segments.RemoveAt (segIndex);
               segments.InsertRange (segIndex, toolSegsForCrvs);
            }
            newSegIndices[idx] = segIndex;
            notchPoints[idx] = segments[segIndex].Curve.End;
            idx++;
         }
      }

      notchPtsInfo = Notch.GetNotchPointsInfo (newSegIndices, notchPoints, percentPos);
      for (int ii = 0; ii < notchPtsInfo.Count; ii++) {
         nptInterestPts = [];
         for (int jj = 0; jj < notchPtsInfo[ii].mPoints.Count; jj++)
            nptPts.Add (notchPtsInfo[ii].mPoints[jj]);
      }

      notchPtsInfo = [];
      string[] atPos = ["@25", "@50", "@75"];
      for (int ii = 0; ii < nptPts.Count; ii++) {
         var npt = nptPts[ii];
         idx = -1;
         idx = segments.FindIndex (s => s.Curve.End.DistTo (npt).EQ (0, tolerance));
         if (segIndices[ii] == -1) idx = -1;
         NotchPointInfo nptInfo = new ();
         double atpc = 0;
         if (percentPos.Length == 1) {
            atpc = percentPos[0];
            nptInfo = new (idx, npt, atpc, atPos[1]);
         } else if (percentPos.Length == 3) {
            atpc = percentPos[ii];
            nptInfo = new (idx, npt, atpc, atPos[ii]);
         }
         notchPtsInfo.Add (nptInfo);
      }
   }

   /// <summary>
   /// This mwthod is a wrapper to Geom.SplitCurve, which splits the list of toolingSegments input 
   /// at the given point.
   /// </summary>
   /// <param name="segments">The list of tooling segments</param>
   /// <param name="segIndex">The segment's index in the list of tooling segment</param>
   /// <param name="point">The point at which the split is needed</param>
   /// <param name="fpn">The Feature Plane Normal, which should be the local normal to the segment</param>
   /// <param name="tolerance">The epsilon tolerance, which is by default 1e-6</param>
   /// <returns>The list of tooling segments that got created. If no curves were split, it returns an 
   /// empty list</returns>
   public static List<ToolingSegment> SplitToolingSegmentsAtPoint (
                                          List<ToolingSegment> segments,
                                          int segIndex, Point3 point, Vector3 fpn,
                                          double tolerance = 1e-6) {
      List<Point3> intPoints = [point];
      // Consistency check
      if (segments.Count == 0 || segIndex < 0 || segIndex >= segments.Count ||
         !Geom.IsPointOnCurve (segments[segIndex].Curve, point, fpn, hintSense: EArcSense.Infer, tolerance))
         throw new Exception ("SplitToolingSegmentsAtPoint: Point not on the curve");

      var crvs = Geom.SplitCurve (segments[segIndex].Curve, intPoints, fpn,
                                  deltaBetween: 0, EArcSense.Infer, tolerance);
      var toolSegsForCrvs = Geom.CreateToolingSegmentForCurves (segments[segIndex], crvs);

      return toolSegsForCrvs;
   }

   /// <summary>
   /// This method is a predicate that tells if a segment is on the E3Flex. 
   /// TODO: In the subsequent iterations, an elegant way will be found to check 
   /// if the segment is on the E3Flex using projection/unprojection
   /// </summary>
   /// <param name="stNormal">The start normal of the segment</param>
   /// <param name="endNormal">The end normal of the segment.</param>
   /// <returns></returns>
   /// <exception cref="Exception"></exception>
   public static bool IsToolingOnFlex (Vector3 stNormal, Vector3 endNormal) {
      stNormal = stNormal.Normalized (); endNormal = endNormal.Normalized ();
      if ((stNormal.Dot (XForm4.mZAxis).EQ (1) && endNormal.Dot (XForm4.mZAxis).EQ (1))
           || (stNormal.Dot (XForm4.mYAxis).EQ (1) && endNormal.Dot (XForm4.mYAxis).EQ (1))
           || (stNormal.Dot (-XForm4.mYAxis).EQ (1) && endNormal.Dot (-XForm4.mYAxis).EQ (1)))
         return false;
      else if (stNormal.Dot (-XForm4.mZAxis).EQ (1)
               || endNormal.Dot (-XForm4.mZAxis).EQ (1))
         throw new Exception ("Negative Z axis normal encountered");

      return true;
   }

   public static bool IsToolingOnFlex (ToolingSegment ts) {
      if ((ts.Vec0.Normalized ().Dot (XForm4.mZAxis).EQ (1) && ts.Vec1.Normalized ().Dot (XForm4.mZAxis).EQ (1))
           || (ts.Vec0.Normalized ().Dot (XForm4.mYAxis).EQ (1) && ts.Vec1.Normalized ().Dot (XForm4.mYAxis).EQ (1))
           || (ts.Vec0.Normalized ().Dot (XForm4.mNegYAxis).EQ (1) && ts.Vec1.Normalized ().Dot (XForm4.mNegYAxis).EQ (1)))
         return false;
      if (ts.Vec0.Normalized ().Dot (-XForm4.mZAxis).EQ (1)
               || ts.Vec1.Normalized ().Dot (-XForm4.mZAxis).EQ (1))
         throw new Exception ("Negative Z axis normal encountered");

      return true;
   }

   public static List<ToolingSegment> GetToolingsWithNormal (List<ToolingSegment> segs, Vector3 normalDir) {
      List<ToolingSegment> res = [];
      normalDir = normalDir.Normalized ();
      foreach (var s in segs) {
         if (s.Vec0.EQ (normalDir) || s.Vec1.EQ (normalDir))
            res.Add (s);
      }
      return res;
   }

   /// <summary>
   /// This is a utility method that computes the distance between the the segments, including the 
   /// start and end segment.
   /// </summary>
   /// <param name="segs">The list of tooling segments</param>
   /// <param name="stIndex">The start index of the tooling segment</param>
   /// <param name="endIndex">The end index of the tooling segment</param>
   /// <returns>The length of all the curves from start segment to end segment, including the start 
   /// and the end segment</returns>
   public static double GetDistanceBetween (List<ToolingSegment> segs, int stIndex, int endIndex) {
      double length = 0.0;
      for (int ii = stIndex; ii < endIndex; ii++)
         length += segs[ii].Curve.Length;
      return length;
   }

   /// <summary>
   /// This method computes the bounds in 3D of a set of points
   /// </summary>
   /// <param name="points">The input set of 3d points</param>
   /// <returns>The bounds in 3d</returns>
   public static Bound3 GetPointsBounds (List<Point3> points) {
      // Calculate max and min values for X, Y, Z
      var (maxX, minX, maxY, minY, maxZ, minZ) = (
          points.Max (p => p.X), points.Min (p => p.X),
          points.Max (p => p.Y), points.Min (p => p.Y),
          points.Max (p => p.Z), points.Min (p => p.Z)
      );

      Bound3 bounds = new (minX, minY, minZ, maxX, maxY, maxZ);
      return bounds;
   }

   /// <summary>
   /// This method returns the bounds 3d of a list of tooling segments from
   /// the starting index to the end index. If startIndex is -1, all the items 
   /// in the list are considered
   /// </summary>
   /// <param name="toolingSegs">The list of tooling segments</param>
   /// <param name="startIndex">The start index </param>
   /// <param name="endIndex">The end index</param>
   /// <returns></returns>
   public static Bound3 GetToolingSegmentsBounds (List<ToolingSegment> toolingSegs, Bound3 extentBox,
                                                  int startIndex = -1, int endIndex = -1) {
      List<ToolingSegment> toolingSegsSub = [];
      if (startIndex != -1) {
         int increment = startIndex <= endIndex ? 1 : -1;
         for (int ii = startIndex; (startIndex <= endIndex ? ii <= endIndex : ii >= endIndex);
            ii += increment)

            toolingSegsSub.Add (toolingSegs[ii]);
      } else
         toolingSegsSub = toolingSegs;

      // Extract all Point3 instances from Start and End properties of Curve3
      return Utils.CalculateBound3 (toolingSegsSub, extentBox);
   }

   /// <summary>
   /// This method calculates the non-associated (ordinate) bounding box of the set of 
   /// tooling segments
   /// </summary>
   /// <param name="toolingSegments">The input tooling segments</param>
   /// <param name="partBBox">The overall bounding box containing the tooling segments. This 
   /// is used for clamping the limits</param>
   /// <returns>An non-associated bounding box  type Bound3
   /// </returns>
   public static Bound3 CalculateBound3 (List<ToolingSegment> toolingSegments, Bound3 partBBox) {
      if (toolingSegments == null || toolingSegments.Count == 0)
         throw new ArgumentException ("Tooling segments list cannot be null or empty.");

      Bound3? cumBBox = null;
      foreach (var seg in toolingSegments) {
         Bound3 bbox = Geom.ComputeBBox (seg.Curve, seg.Vec0, partBBox);
         cumBBox = cumBBox == null ? bbox : cumBBox + bbox;
      }
      if (cumBBox.HasValue)
         return cumBBox.Value;
      throw new Exception ("Cumulative bounding box not defined");
   }

   /// <summary>
   /// Calculates the bounding box of the list of toolings.
   /// </summary>
   /// <param name="cuts">List of toolings</param>
   /// <returns></returns>
   public static Bound3 CalculateBound3 (List<Tooling> cuts) {
      var bounds = cuts.SelectMany (cut => new[] { cut.Bound3 });
      return new Bound3 (bounds.Min (b => b.XMin),
                         bounds.Min (b => b.YMin),
                         bounds.Min (b => b.ZMin),
                         bounds.Max (b => b.XMax),
                         bounds.Max (b => b.YMax),
                         bounds.Max (b => b.ZMax));
   }

   public static Bound3 CalculateBound3 (ToolScopeList tssList) {
      if (tssList.Count == 0) return Bound3.Empty;
      var cuts = tssList.Select (ts => ts.Tooling).ToList ();
      return CalculateBound3 (cuts);
      //var bounds = cuts.SelectMany (cut => new[] { cut.Bound3 });
      //return new Bound3 (bounds.Min (b => b.XMin),
      //                   bounds.Min (b => b.YMin),
      //                   bounds.Min (b => b.ZMin),
      //                   bounds.Max (b => b.XMax),
      //                   bounds.Max (b => b.YMax),
      //                   bounds.Max (b => b.ZMax));
   }

   public static Bound3 CalculateBound3 (Frame frame, IGCodeGenerator.ToolHeadType headType) {
      Bound3 bounds;
      if (headType == IGCodeGenerator.ToolHeadType.Master || headType == IGCodeGenerator.ToolHeadType.MasterB2) {
         bounds = CalculateBound3 (frame.FrameToolScopesH11);
         bounds += CalculateBound3 (frame.FrameToolScopesH12);
      } else {
         bounds = CalculateBound3 (frame.FrameToolScopesH21);
         bounds += CalculateBound3 (frame.FrameToolScopesH22);
      }
      return bounds;
   }

   public static Bound3 CalculateBound3PerBucket (Frame frame, IGCodeGenerator.ToolHeadType headType) {
      Bound3 bounds = new ();
      if (headType == IGCodeGenerator.ToolHeadType.Master)
         bounds += CalculateBound3 (frame.FrameToolScopesH11);
      else if (headType == IGCodeGenerator.ToolHeadType.MasterB2)
         bounds += CalculateBound3 (frame.FrameToolScopesH12);
      else if (headType == IGCodeGenerator.ToolHeadType.Slave)
         bounds += CalculateBound3 (frame.FrameToolScopesH21);
      else if (headType == IGCodeGenerator.ToolHeadType.SlaveB2)
         bounds += CalculateBound3 (frame.FrameToolScopesH22);
      return bounds;
   }




   /// <summary>
   /// This method is used to compute 3d point intersecting the 
   /// segment in list of segments, whose X value alone is specified. 
   /// </summary>
   /// <param name="segs">The input tooling segments</param>
   /// <param name="xVal">The X Value at which the intersection parameters are to be 
   /// calculated</param>
   /// <returns>A tuple of 3d Point, parameter with in the index-th segment,
   /// index of the segment and flag true or false, if the intersection happens 
   /// between 0 - 1 parameter</returns>
   public static Tuple<Point3, double, int, bool> GetPointParamsAtXVal (List<ToolingSegment> segs, double xVal) {
      double t = -1.0;
      Point3 p = new ();
      int kk;
      bool ixn = false;
      for (kk = 0; kk < segs.Count; kk++) {
         if ((segs[kk].Curve.Start.X - xVal).EQ (0))
            return new Tuple<Point3, double, int, bool> (segs[kk].Curve.Start, 0, kk, true);

         if ((segs[kk].Curve.End.X - xVal).EQ (0))
            return new Tuple<Point3, double, int, bool> (segs[kk].Curve.End, 1, kk, true);

         if (segs[kk].Curve is FCArc3 arc) {
            var (c, r) = Geom.EvaluateCenterAndRadius (arc);
            var p1 = new Point3 (xVal, segs[kk].Curve.Start.Y,
                                 c.Z + Math.Sqrt (r * r - (xVal - c.X) * (xVal - c.X)));
            var p2 = new Point3 (xVal, segs[kk].Curve.Start.Y,
                                 c.Z - Math.Sqrt (r * r - (xVal - c.X) * (xVal - c.X)));

            // Find out which of the above points exists with in the segment
            if (Geom.IsPointOnCurve (segs[kk].Curve, p1, segs[kk].Vec0))
               p = p1;
            else if (Geom.IsPointOnCurve (segs[kk].Curve, p2, segs[kk].Vec0))
               p = p2;
            else
               continue;

            t = Geom.GetParamAtPoint (arc, p, segs[kk].Vec0);
         } else {
            var x1 = segs[kk].Curve.Start.X; var x2 = segs[kk].Curve.End.X;
            var z1 = segs[kk].Curve.Start.Z; var z2 = segs[kk].Curve.End.Z;
            var y1 = segs[kk].Curve.Start.Y; var y2 = segs[kk].Curve.End.Y;
            t = (xVal - x1) / (x2 - x1);
            var z = z1 + t * (z2 - z1);
            var y = y1 + t * (y2 - y1);
            p = new Point3 (xVal, y, z);
         }
         if (t.LieWithin (0, 1)) {
            ixn = true;
            break;
         }
      }
      if (kk == segs.Count)
         return new Tuple<Point3, double, int, bool> (new Point3 (double.MinValue, double.MinValue, double.MinValue), -1, -1, ixn);
      return new Tuple<Point3, double, int, bool> (p, t, kk, ixn);
   }

   /// <summary>
   /// This method is to split a tooling scope. Please note that the tooling scope is
   /// a wrapper over tooling. The tooling has a list of tooling segments. The split will happen
   /// based on the X values stored in the tooling scope object.
   /// </summary>
   /// <param name="ts">The input tooling scope.</param>
   /// <param name="isLeftToRight">A provision (flag) if the part is machined in forward or
   /// reverse to the legacy direction</param>
   /// <returns>List of tooling segments, split.</returns>
   /// <exception cref="Exception">This exception is thrown if the tooling does not intersect
   /// between the X values stored in the tooling scope.</exception>
   public static List<ToolingSegment> SplitNotchToScope (ToolingScope ts, bool isLeftToRight, double tolerance = 1e-6) {
      var segs = ts.Tooling.Segs; var toolingItem = ts.Tooling;
      List<ToolingSegment> resSegs = [];
      if (segs[^1].Curve.End.X < segs[0].Curve.Start.X && (ts.Tooling.ProfileKind == ECutKind.YPos || ts.Tooling.ProfileKind == ECutKind.YNeg))
         throw new Exception ("The notch direction in X is opposite to the direction of the part");

      var startX = ts.StartX; var endX = ts.EndX;
      double xPartition;
      bool maxSideToPartition = false;
      if (isLeftToRight) {
         if ((toolingItem.Bound3.XMax - ts.EndX).EQ (0)) {
            xPartition = startX;
            maxSideToPartition = true;
         } else if ((toolingItem.Bound3.XMin - ts.StartX).EQ (0))
            xPartition = endX;
         else
            throw new Exception ("ToolingScope does not match with Tooling: In left to right");
      } else {
         if ((toolingItem.Bound3.XMax - ts.StartX).EQ (0)) {
            xPartition = endX;
            maxSideToPartition = true;
         } else if ((toolingItem.Bound3.XMin - ts.EndX).EQ (0))
            xPartition = startX;
         else
            throw new Exception ("ToolingScope does not match with Tooling: In Right to left");
      }

      var (notchXPt, _, index, doesIntersect) = GetPointParamsAtXVal (segs, xPartition);
      List<ToolingSegment> splitSegs;

      if (doesIntersect) {
         splitSegs = SplitToolingSegmentsAtPoint (segs, index, notchXPt, segs[index].Vec0.Normalized (), tolerance);

         // Create a new line tooling segment.
         if (splitSegs.Count == 2) {
            if (maxSideToPartition) {
               // Take all toolingSegments from Last toolingSegmen to index-1, add the 0th index of splitSegs, add it to the lastTSG.
               resSegs.Add (splitSegs[1]);
               for (int ii = index + 1; ii < segs.Count; ii++)
                  resSegs.Add (segs[ii]);
            } else {
               for (int ii = 0; ii < index; ii++)
                  resSegs.Add (segs[ii]);
               resSegs.Add (splitSegs[0]);
            }
         }
      }
      return resSegs;
   }

   /// <summary>
   /// This method is to check, at any instance, if the notch points info data structure
   /// is valid with regards to the input tooling segments.
   /// <remarks> Notch Point Info structure stores, the index of the element in the tooling segments list,
   /// and an array of points. This serves as follows.
   /// If the notch occurances are not created at 25/50/75 percent of the lengths, the method that computes
   /// them, needs a data structure that gives index (of the segment in segs) and the list of points that
   /// occur in that index.
   /// Once the occurances are computed and the segments are split at those points, to make the segment's end point
   /// to be the point of interest, the notch pointS info, the list contains, each element, which has an unique
   /// index, ONLY ONE POINT (instead of an array) which the end point of that index-th segment.
   /// This end point coordinates are verified to be identical with the index-th points in the segments
   /// This filters any error while preparing the segments for notch
   /// </remarks>
   /// </summary>
   /// <param name="segs">The input list of segments</param>
   /// <param name="npsInfo">The data structure that holds the notch points specs</param>
   /// <exception cref="Exception">Exception is thrown if an error is found</exception>
   public static void CheckSanityNotchPointsInfo (List<ToolingSegment> segs,
                                                  List<NotchPointInfo> npsInfo,
                                                  double tolerance = 1e-6) {
      for (int ii = 0; ii < npsInfo.Count; ii++) {
         if (npsInfo[ii].mSegIndex == -1) continue;
         var npInfoPt = npsInfo[ii].mPoints[0];
         var segEndPt = segs[npsInfo[ii].mSegIndex].Curve.End;
         if (!npInfoPt.DistTo (segEndPt).EQ (0, tolerance))
            throw new Exception ("NOtchpoint and segment's point do not match");
      }
   }

   /// <summary>
   /// This method is used to find if a segment in the segments list is CONCAVE,
   /// and so the segment can not be used for the notch spec point creation
   /// <remarks> The algorithm checks for any two points on the segment (after discretizing) if the
   /// sign of the Y unless Z, unless X values difference is the same as the difference between
   /// (in the same order) end to start of the segment. If this is violated, then the outward vector
   /// to the nearest boundary from a point in the invalid segment will intersect one of the other 
   /// tooling segments before reaching the boundary thus making the notch speific approach or reentry
   /// completely wrong</remarks>
   /// </summary>
   /// <param name="segments">The input list of segments</param>
   /// <exception cref="Exception">If the sign can not evaluated</exception>
   public static void MarkfeasibleSegments (ref List<ToolingSegment> segments) {
      int sgn;
      string about = "";
      double diffY = segments[^1].Curve.End.Y - segments[0].Curve.Start.Y;
      sgn = Math.Sign (diffY);
      if (sgn != 0)
         about = "Y";

      double diffZ = segments[^1].Curve.End.Z - segments.First ().Curve.Start.Z;
      if (sgn == 0 || Math.Abs (diffZ) > Math.Abs (diffY)) {
         sgn = Math.Sign (diffZ);
         about = "Z";
      }
      double diffX = segments[^1].Curve.End.X - segments.First ().Curve.Start.X;
      if (sgn == 0 || (Math.Abs (diffX) > Math.Abs (diffZ) && Math.Abs (diffX) > Math.Abs (diffY))) {
         sgn = Math.Sign (diffX);
         about = "X";
      }
      if (sgn == 0)
         throw new Exception ("Sign of the tooling segment (end-start) computation ambiguous");

      for (int ii = 0; ii < segments.Count; ii++) {
         if (segments[ii].Curve is FCLine3 fcLine) {
            double diff = -1;
            if (about == "Y")
               diff = segments[ii].Curve.End.Y - segments[ii].Curve.Start.Y;
            else if (about == "Z")
               diff = segments[ii].Curve.End.Z - segments[ii].Curve.Start.Z;
            else if (about == "X")
               diff = segments[ii].Curve.End.X - segments[ii].Curve.Start.X;

            if (diff.EQ (0))
               continue;
            var segSign = Math.Sign (diff);
            if (segSign != 0 && segSign != sgn) {
               var seg = segments[ii];
               seg.IsValid = false;
               segments[ii] = seg;
            }
         } else if (segments[ii].Curve is FCArc3 fcArc) {
            var arcPts = fcArc.Discretize (0.1).ToList ();
            bool broken = false;
            for (int jj = 1; jj < arcPts.Count; jj++) {
               var diff = arcPts[jj].Y - arcPts[jj - 1].Y;
               if (diff.EQ (0))
                  continue;

               var segSign = Math.Sign (diff);
               if (segSign != 0 && segSign != sgn) {
                  var seg = segments[ii];
                  seg.IsValid = false;
                  segments[ii] = seg;
                  broken = true;
                  break;
               }
            }
            if (broken)
               continue;
         } else
            throw new Exception ("Unknown type of entity other than FCArc3 or FCLine3 encountered");
      }
   }

   /// <summary>
   /// This method finds the index of each notch spec point in the input notch points list
   /// in the input tooling segments.
   /// </summary>
   /// <param name="segs">The input tooling segments</param>
   /// <param name="notchPointsInfo">The input notch points info, also used to mark the indices</param>
   public static void ReIndexNotchPointsInfo (List<ToolingSegment> segs, ref List<NotchPointInfo> notchPointsInfo,
      bool isWireJointsNeeded, double tolerance = 1e-6) {
      // Update the ordinate notch points ( 25,50, and 75)
      string[] atPos = ["@25", "@50", "@75"];
      if (!isWireJointsNeeded) atPos = ["@50"];
      int posCnt = 0;
      for (int ii = 0; ii < notchPointsInfo.Count; ii++) {
         var npinfo = notchPointsInfo[ii];
         if (npinfo.mSegIndex != -1) {
            int index = segs.FindIndex (s => s.Curve.End.DistTo (npinfo.mPoints[0]).EQ (0, tolerance));
            npinfo.mSegIndex = index;
         }
         if (npinfo.mPosition == "@25" || npinfo.mPosition == "@50" || npinfo.mPosition == "@75") {
            if (isWireJointsNeeded) npinfo.mPosition = atPos[posCnt++];
            else npinfo.mPosition = atPos[0];
         }
         notchPointsInfo[ii] = npinfo;
      }
      notchPointsInfo = [.. notchPointsInfo.OrderBy (n => n.mPercentage)];
   }

   /// <summary>
   /// This method updates the notch points list for the given position, percentage and point
   /// </summary>
   /// <param name="segs">The input segments</param>
   /// <param name="notchPointsInfo">The input/output notch points info</param>
   /// <param name="position">a string token as symbol</param>
   /// <param name="percent">The parameter of the point in the tooling segments</param>
   /// <param name="pt">The actual point</param>
   /// <exception cref="Exception">If the given point is not participating in the tooling segments list</exception>
   public static void UpdateNotchPointsInfo (List<ToolingSegment> segs,
                                             ref List<NotchPointInfo> notchPointsInfo,
      string position, double percent, Point3 pt, bool isWireJointsNeeded, double tolerance = 1e-6) {
      var npinfo = new NotchPointInfo () {
         mPercentage = percent,
         mPoints = [],
         mPosition = position
      };
      npinfo.mPoints.Add (pt);
      int index = segs.FindIndex (s => s.Curve.End.DistTo (pt).EQ (0));
      if (index == -1)
         throw new Exception ("Index = -1 for notch spec point in mSegments");

      npinfo.mSegIndex = index;
      notchPointsInfo.Add (npinfo);
      ReIndexNotchPointsInfo (segs, ref notchPointsInfo, isWireJointsNeeded, tolerance);
   }

   /// <summary>
   /// This method writes Rapid position G Code statement as [G0  X Y Z Angle]
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="y">Y Coordinate</param>
   /// <param name="z">Z Coordinate</param>
   /// <param name="a">Angle in degrees</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string RapidPosition (StreamWriter sw, double x, double y, double z,
                                     double a, MachineType machine = MachineType.LCMMultipass2H,
                                     bool createDummyBlock4Master = false) {
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master) return "";
      string gcodeStatement = $"G0 X{x:F3} Y{y:F3} Z{z:F3} A{a:F3}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes Rapid position G Code statement as [G0  X Y Angle] OR 
   /// [G0  X Z Angle] depending on the ordinate axis
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="oaxis">The ordinate Axis (Y or Z based on XZ or YZ plane)</param>
   /// <param name="val">Y or Z Coordinate</param>
   /// <param name="a">Angle about X axis</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   /// <param name="extraToken">This is a string that contains extra parameters to be 
   /// passed to the G Code</param>
   /// <param name="comment">G Code comment statement</param>
   /// <returns>The G Code string itself</returns>
   public static string RapidPosition (StreamWriter sw, double x, OrdinateAxis oaxis, double val,
                                     double a, MachineType machine = MachineType.LCMMultipass2H,
                                     bool createDummyBlock4Master = false,
                                     string extraToken = "", string comment = "") {
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master) return "";
      string gcodeStatement = "";
      if (oaxis == OrdinateAxis.Y)
         gcodeStatement = $"G0 X{x:F3} Y{val:F3} A{a:F3} {extraToken} {(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      else if (oaxis == OrdinateAxis.Z)
         gcodeStatement = $"G0 X{x:F3} Z{val:F3} A{a:F3} {extraToken} {(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes Rapid position G Code statement as [G0  X Y/Z  Comment]
   /// Y/Z means either of one.
   /// </summary>
   /// <param name="sw">The streamwriter</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="oaxis">Either Y oor Z ordinate axis</param>
   /// <param name="val">the coordinate value aling the above ordinate axis</param>
   /// <param name="comment">G Code comment</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   /// <returns>The G Code string itself</returns>
   public static string RapidPosition (StreamWriter sw, double x, OrdinateAxis oaxis,
                                     double val, string comment, string extraToken = "",
                                     MachineType machine = MachineType.LCMMultipass2H,
                                     bool createDummyBlock4Master = false) {
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master) return "";
      string gcodeStatement = "";
      if (oaxis == OrdinateAxis.Y)
         gcodeStatement = $"G0 X{x:F3} Y{val:F3} {extraToken} {(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      else if (oaxis == OrdinateAxis.Z)
         gcodeStatement = $"G0 X{x:F3} Z{val:F3} {extraToken} {(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes the ready to machining G Code statement as [G1  X Y Z Angle Comment]
   /// Though this G1 machining statement, in the context, this is more of a ready to machining
   /// statement.
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="y">Y coordinate</param>
   /// <param name="z">Z Coordinate</param>
   /// <param name="a">Angle aboit X axis in degrees</param>
   /// <param name="f">Feed rate. This differentiates this statement from machining statement</param>
   /// <param name="comment">G Code comment</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string LinearMachining (StreamWriter sw, double x, double y, double z, double a, double f, string comment = "",
      MachineType machine = MachineType.LCMMultipass2H, bool createDummyBlock4Master = false) {
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return "";
      string gcodeStatement = $"G1 X{x:F3} Y{y:F3} Z{z:F3} A{a:F3} F{f:F0} {(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes the linear machining statement as [G1  X Y Z A Comment ]
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="y">Y Coordinate</param>
   /// <param name="z">Z Coordinate</param>
   /// <param name="a">Angle about X axis in degrees</param>
   /// <param name="comment">G Code comment</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string LinearMachining (StreamWriter sw, double x, double y, double z, double a, string comment = "",
      MachineType machine = MachineType.LCMMultipass2H, bool createDummyBlock4Master = false) {
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return "";
      string gcodeStatement = $"G1 X{x:F3} Y{y:F3} Z{z:F3} A{a:F3} {(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes the linear machining statement as [G1  X Y Z Comment ]
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="y">Y Coordinate</param>
   /// <param name="z">Z Coordinate</param>
   /// <param name="comment">G Code comment</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string LinearMachining (StreamWriter sw, double x, double y, double z,
                                       string comment = "", MachineType machine = MachineType.LCMMultipass2H,
                                       bool createDummyBlock4Master = false) {
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return "";
      string gcodeStatement = $"G1 X{x:F3} Y{y:F3} Z{z:F3}{(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes the linear machining statement as [G1  X  Y/Z A Comment ]
   /// Y/Z means either Y or Z.
   /// </summary>
   /// <param name="sw">The streamwriter</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="oaxis">Either Y oor Z ordinate axis</param>
   /// <param name="val">the coordinate value aling the above ordinate axis</param>
   /// <param name="a">Angle about X axis in degrees</param>
   /// <param name="comment">G Code comment</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string LinearMachining (StreamWriter sw, double x, OrdinateAxis oaxis,
                                       double val, double a, string comment = "",
                                       MachineType machine = MachineType.LCMMultipass2H,
                                       bool createDummyBlock4Master = false) {
      string gcodeStatement = "";
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return gcodeStatement;
      if (oaxis == OrdinateAxis.Y)
         gcodeStatement = $"G1 X{x:F3} Y{val:F3} A{a:F3}{(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      else if (oaxis == OrdinateAxis.Z)
         gcodeStatement = $"G1 X{x:F3} Z{val:F3} A{a:F3}{(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method writes the linear machining statement as [G1  X  Y/Z Comment ]
   /// Y/Z means either Y or Z.
   /// </summary>
   /// <param name="sw">The streamwriter</param>
   /// <param name="x">X Coordinate</param>
   /// <param name="oaxis">Either Y oor Z ordinate axis</param>
   /// <param name="val">the coordinate value aling the above ordinate axis</param>
   /// <param name="comment">G Code comment</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string LinearMachining (StreamWriter sw, double x, OrdinateAxis oaxis,
                                       double val, string comment = "",
                                       MachineType machine = MachineType.LCMMultipass2H,
                                       bool createDummyBlock4Master = false) {
      string gcodeStatement = "";
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return gcodeStatement;
      if (oaxis == OrdinateAxis.Y)
         gcodeStatement = $"G1 X{x:F3} Y{val:F3}{(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      else if (oaxis == OrdinateAxis.Z)
         gcodeStatement = $"G1 X{x:F3} Z{val:F3}{(string.IsNullOrEmpty (comment) ? "" : $" ({comment})")}";
      gcodeStatement = NormalizeNegativeZero (gcodeStatement);
      sw.WriteLine (gcodeStatement);
      return gcodeStatement;
   }

   /// <summary>
   /// This method is used to write circular machining statement as G{2}{3} I val J val
   /// where {2}{3} means either 2 ( clockwise) or 3 (counter-clockwise)
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="arcSense">clockwise or counter clockwise</param>
   /// <param name="i">The X-axis offset from the start point of the arc to the center of the arc.</param>
   /// <param name="oaxis">The ordinate axis, Y, which means val is J and if ordinate axis is Z, val is K</param>
   /// <param name="val">The Y/Z-axis offset from the start point of the arc to the center of the arc.</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is a slave. In the case of slave for 
   /// machine type LCMMultipass2H, no g code statement is written</param>
   public static string CircularMachining (StreamWriter sw, Utils.EArcSense arcSense,
                                         double i, OrdinateAxis oaxis, double val,
                                         EFlange flange, MCSettings.PartConfigType partConfig,
                                         XForm4 compCoorsys,
                                         MachineType machine = MachineType.LCMMultipass2H,
                                         Point3? cen = null, double? rad = null, Point3? stpt = null,
                                         bool createDummyBlock4Master = false) {
      string gCodeStatement = "";
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return gCodeStatement;

      // To find if the arcs or circles are G2 or G3 for the machine type MachineType.LCMMultipass2H
      // The second Rotational Component ( Local Y Axis) of -90 rotated compCoorsys about X Axis
      // is Dot Product'ed with XForm.mYAxis.
      // Conventionally, we mean CCW arc if X and Y are along the positive sides of global X and Ys.
      // In the case of Bottom flange, the second Rot component or local Y Axis is along global -Z axis, and
      // so the value is {0,0,-1}. Removing Y component ( const for Bottom and Top flanges), we have
      // {1,0} and {0,-1}. If the product of Y Component is -ve, revert Clockwise with counter-clockwise
      // arcs or circles.
      // Similarly, for RH component, instead of bottom, flange, it os top flange, where the local Y axis of 
      // of -90 rotated compCoorsys about X Axis is Dot Product'ed with XForm.mYAxis.
      Vector2 localY, localX;
      bool reverseArcSense = false;
      double rotAngle = 90;
      if ((flange == EFlange.Bottom && partConfig == MCSettings.PartConfigType.LHComponent) ||
         (flange == EFlange.Top && partConfig == MCSettings.PartConfigType.RHComponent))
         rotAngle = -90;
      if (flange != EFlange.Web) {
         localY = Utils.ToPlane (compCoorsys.RotateNew (XForm4.EAxis.X, rotAngle).YCompRot, EPlane.YNeg);
         localX = Utils.ToPlane (compCoorsys.RotateNew (XForm4.EAxis.X, rotAngle).XCompRot, EPlane.YNeg);
         if (localX.X * localY.Y < 0) reverseArcSense = true;
      }

      if (machine == MachineType.LCMMultipass2H &&
         ((flange == EFlange.Bottom && partConfig == MCSettings.PartConfigType.LHComponent) ||
         (flange == EFlange.Top && partConfig == MCSettings.PartConfigType.RHComponent))) {
         if (!reverseArcSense) throw new Exception ("CCW error");
      }

      // LCMMultipass2H machine's G2 and G3 functions are reversed in sense for LH Component with BOTTOM flange or
      // RH component Top Flange. So CW is G3 and counter-clockwise is G2
      if (oaxis == OrdinateAxis.Y) {
         if (reverseArcSense)
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 3 : 2)} I{i:F3} J{val:F3} ";
         else
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 2 : 3)} I{i:F3} J{val:F3} ";
      } else if (oaxis == OrdinateAxis.Z) {
         if (reverseArcSense)
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 3 : 2)} I{i:F3} K{val:F3} ";
         else
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 2 : 3)} I{i:F3} K{val:F3} ";
      }
      gCodeStatement = NormalizeNegativeZero (gCodeStatement);
      sw.Write (gCodeStatement);

      string gCodeComment = "";
      if (stpt != null && cen != null) {
         gCodeComment = $"  Circle St Pt X: {stpt.Value.X:F3} Y: {stpt.Value.Y:F3} Z: {stpt.Value.Z:F3} ";
         gCodeComment += $"  Center X: {cen.Value.X:F3} Y: {cen.Value.Y:F3} Z: {cen.Value.Z:F3} ";
         gCodeComment += $"  Radius: {rad:F3} ";
      }

      gCodeComment = NormalizeNegativeZero (gCodeComment);
      sw.WriteLine (Utils.GetGCodeComment (gCodeComment));
      return gCodeStatement + gCodeComment;
   }

   /// <summary>
   /// This method is used to write partially-circular (Arc) machining statement as 
   /// G{2}{3} I val J val X Y where {2}{3} means either 2 ( clockwise) or 3 (counter-clockwise)
   /// </summary>
   /// <param name="sw">The stream writer</param>
   /// <param name="arcSense">clockwise or counter clockwise</param>
   /// <param name="i">The X-axis offset from the start point of the arc to the center of the arc.</param>
   /// <param name="oaxis">The ordinate axis, Y, which means val is J and if ordinate axis is Z, val is K</param>
   /// <param name="val">The Y/Z-axis offset from the start point of the arc to the center of the arc.</param>
   /// <param name="x">This represents the end point of the arc on the X </param>
   /// <param name="y">This represents the end point of the arc on the Y/Z axis</param>
   /// <param name="machine">Machine type</param>
   /// <param name="createDummyBlock4Master">The flag specifies if the head is the slave. In the case of slave  
   /// of the machine LCMMultipass2H, no g code statement is to be written</param>
   public static string ArcMachining (StreamWriter sw, Utils.EArcSense arcSense,
                                    double i, OrdinateAxis oaxis, double val,
                                    double x, double y, EFlange flange,
                                    MCSettings.PartConfigType partConfig,
                                    XForm4 compCoorsys,
                                    MachineType machine = MachineType.LCMMultipass2H,
                                    bool createDummyBlock4Master = false) {
      string gCodeStatement = "";
      if (machine == MachineType.LCMMultipass2H && createDummyBlock4Master)
         return gCodeStatement;

      // To find if the arcs or circles are G2 or G3 for the machine type MachineType.LCMMultipass2H
      // The second Rotational Component ( Local Y Axis) of -90 rotated compCoorsys about X Axis
      // is Dot Product'ed with XForm.mYAxis.
      // Conventionally, we mean CCW arc if X and Y are along the positive sides of global X and Ys.
      // In the case of Bottom flange, the second Rot component or local Y Axis is along global -Z axis, and
      // so the value is {0,0,-1}. Removing Y component ( const for Bottom and Top flanges), we have
      // {1,0} and {0,-1}. If the product of Y Component is -ve, revert Clockwise with counter-clockwise
      // arcs or circles.
      // Similarly, for RH component, instead of bottom, flange, it os top flange, where the local Y axis of 
      // of -90 rotated compCoorsys about X Axis is Dot Product'ed with XForm.mYAxis.
      Vector2 localY, localX;
      bool reverseArcSense = false;
      double rotAngle = 90;
      if ((flange == EFlange.Bottom && partConfig == MCSettings.PartConfigType.LHComponent) ||
         (flange == EFlange.Top && partConfig == MCSettings.PartConfigType.RHComponent))
         rotAngle = -90;
      if (flange != EFlange.Web) {
         localY = Utils.ToPlane (compCoorsys.RotateNew (XForm4.EAxis.X, rotAngle).YCompRot, EPlane.YNeg);
         localX = Utils.ToPlane (compCoorsys.RotateNew (XForm4.EAxis.X, rotAngle).XCompRot, EPlane.YNeg);
         if (localX.X * localY.Y < 0) reverseArcSense = true;
      }

      if (machine == MachineType.LCMMultipass2H &&
         ((flange == EFlange.Bottom && partConfig == MCSettings.PartConfigType.LHComponent) ||
         (flange == EFlange.Top && partConfig == MCSettings.PartConfigType.RHComponent))) {
         if (!reverseArcSense) throw new Exception ("CCW error");
      }

      // LCMMultipass2H machine's G2 and G3 functions are reversed in sense for BOTTOM flange. So
      // CW is G3 and counter-clockwise is G2
      if (oaxis == OrdinateAxis.Y) {
         if (reverseArcSense)
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 3 : 2)} I{i:F3} J{val:F3}";
         else
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 2 : 3)} I{i:F3} J{val:F3}";
         gCodeStatement += $" X{x:F3} Y{y:F3}";
      } else if (oaxis == OrdinateAxis.Z) {
         if (reverseArcSense)
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 3 : 2)} I{i:F3} K{val:F3}";
         else
            gCodeStatement = $"G{(arcSense == Utils.EArcSense.CW ? 2 : 3)} I{i:F3} K{val:F3}";
         gCodeStatement += $" X{x:F3} Z{y:F3}";
      }
      gCodeStatement = NormalizeNegativeZero (gCodeStatement);
      sw.WriteLine (gCodeStatement);
      return gCodeStatement;
   }

   /// <summary>
   /// This method is the calling point for G Code Generation. Currently, there are two types of 
   /// machines, <c>Legacy</c> and <c>LCMMultipass2H</c>. The latter is the Laser Cutting Machine
   /// supporting multipass cuts. This method calls the G Code generation for the respective machines
   /// </summary>
   /// <param name="gcodeGen">G Code Generator</param>
   /// <param name="testing">Boolean flag if G Code is run for testing sanity</param>
   /// <returns>Returns the List of G Codes, for Head 1 and Head 2, generated for each cut scope. 
   /// For a single pass legacy, the wrapper list holds only one Cut Scope's G Codes for Head 1 and Head 2</returns>
#nullable enable
   public static List<List<GCodeSeg>> ComputeGCode (IGCodeGenerator gcodeGen, bool testing = false, double tol = 1e-6) {
      List<List<GCodeSeg>> traces = [[], [], [], []];

      if (gcodeGen.Process.Workpiece == null)
         throw new Exception ("Workpiece is not set at gcodeGen.Process.Workpiece");

      // Check if the workpiece needs a multipass cutting
      if (gcodeGen.EnableMultipassCut && gcodeGen.Process.Workpiece.Model.Bound.XMax - gcodeGen.Process.Workpiece.Model.Bound.XMin >= gcodeGen.MaxFrameLength)
         gcodeGen.EnableMultipassCut = true;

      if (testing)
         gcodeGen.CreatePartition (gcodeGen.Process.Workpiece.Cuts, /*optimize*/false,
                                   gcodeGen.Process.Workpiece.Model.Bound);
      else {
         // Sanity test might have changed the instance of setting properties
         gcodeGen.SetFromMCSettings ();
         gcodeGen.ResetBookKeepers ();
      }
      if (gcodeGen.EnableMultipassCut && MultiPassCuts.IsMultipassCutTask (gcodeGen.Process.Workpiece.Model)) {
#if true
         double MinFL = 800;
         double MaxFL = gcodeGen.MaxFrameLength;

         // Get all part multi frams.
         PartMultiFrames partMultiFrames = new (gcodeGen, MinFL, tol);
         partMultiFrames.Optimize ();
         var optimalFrames = partMultiFrames.OptimalFrames;
         var penaltyTime = optimalFrames.Sum (off => off.Value.TotalProcessTime);
         var waitTime = optimalFrames.Sum (off => off.Value.WaitTime);
         if (optimalFrames != null)
            foreach (var optFRame in optimalFrames)
               if (optFRame.HasValue)
                  optFRame.Value.AllocateHeadsToToolScopes ();


         // DEBUG
         var cuts = partMultiFrames.Work.Cuts;
         int nHoles = 0;
         
         var tssSum = 0;
         foreach (var oframe in optimalFrames) {
            tssSum += oframe.Value.FrameToolScopesH11.Count;
            tssSum += oframe.Value.FrameToolScopesH12.Count;
            tssSum += oframe.Value.FrameToolScopesH21.Count;
            tssSum += oframe.Value.FrameToolScopesH22.Count;
         }

         for (int ii = 0; ii < cuts.Count; ii++) {
            var toolingItem = cuts[ii];
            bool toTreatAsCutOut = CutOut.ToTreatAsCutOut (toolingItem.Segs, partMultiFrames.Work.Bound, MCSettings.It.MinCutOutLengthThreshold);
            if ((toolingItem.IsHole () && !toTreatAsCutOut) || toolingItem.IsMark ()) {
               nHoles++;
            }
         }
         if (nHoles != tssSum)
            throw new Exception ("Holes in Part IS NOT EQUAL TO the tool scopes in partMultiFrames");


         partMultiFrames.GenerateGCode ();

         traces[0] = gcodeGen.CutScopeTraces[0][0];
         traces[1] = gcodeGen.CutScopeTraces[0][1];
         traces[2] = gcodeGen.CutScopeTraces[0][2];
         traces[3] = gcodeGen.CutScopeTraces[0][3];
      }

      return traces;

#else


         // -----------------------------------------------------------------------------------------

         MultiPassCuts mpc = new (gcodeGen);

         // Compute using Branch and Bound only if Toolings are below max features.
         if (MCSettings.It.OptimizerType == MCSettings.EOptimize.Time) {
            if (mpc.ToolingScopes.Count < MultiPassCuts.MaxFeatures)
               mpc.ComputeBranchAndBoundCutscopes ();
            else mpc.ComputeSpatialOptimizationCutscopes ();
         } else if (MCSettings.It.OptimizerType == MCSettings.EOptimize.DP)
            mpc.ComputeDPOptimizationCutscopes ();

         mpc.GenerateGCode ();
         traces[0] = mpc.CutScopeTraces[0][0];
         traces[1] = mpc.CutScopeTraces[0][1];
      } else {
         var prevVal = gcodeGen.EnableMultipassCut;
         gcodeGen.EnableMultipassCut = false;
         gcodeGen.CreatePartition (gcodeGen.Process.Workpiece.Cuts, gcodeGen.OptimizePartition, gcodeGen.Process.Workpiece.Model.Bound);
         gcodeGen.GenerateGCode (IGCodeGenerator.ToolHeadType.Master);
         gcodeGen.GenerateGCode (IGCodeGenerator.ToolHeadType.Slave);
         traces[0] = gcodeGen.CutScopeTraces[0][0];
         traces[1] = gcodeGen.CutScopeTraces[0][1];
         gcodeGen.EnableMultipassCut = prevVal;
      }
      return traces;
#endif

   }

   /// <summary>
   /// This method is to be used to read Json file and return the object
   /// </summary>
   /// <typeparam name="T"></typeparam>
   /// <param name="filePath"></param>
   /// <returns></returns>
   /// <exception cref="FileNotFoundException"></exception>
   public static T? ReadJsonFile<T> (string filePath) {
      try {
         if (!File.Exists (filePath)) {
            throw new FileNotFoundException ($"File not found: {filePath}");
         }
         var json = File.ReadAllText (filePath);
         return JsonSerializer.Deserialize<T> (json);
      } catch { return default; }
   }

   /// <summary>
   /// This method is to write a given object to a json file
   /// </summary>
   /// <typeparam name="T">The type of the object to write</typeparam>
   /// <param name="filePath">Json file path</param>
   /// <param name="obj">Object to write</param>
   /// <returns>A boolean representing the write operation status</returns>
   public static bool WriteJsonFile<T> (string filePath, T obj, JsonSerializerOptions? options = default) {
      try {
         var json = JsonSerializer.Serialize (obj, options);
         File.WriteAllText (filePath, json);
         return true;
      } catch {
         return false;
      }
   }

   /// <summary>
   /// This method is to be used to find the segment whose normals are same as the
   /// prescribed input normal
   /// </summary>
   /// <param name="segments">List of Tooling Segments</param>
   /// <param name="normal">The input normal</param>
   /// <returns>A tuple of start index and end index of segments list</returns>
   public static Tuple<int, int> GetSegIndicesWithNormal (List<ToolingSegment> segments, Vector3 normal) {
      int stIx = -1, endIx = -1;
      normal = normal.Normalized ();
      for (int ii = 0; ii < segments.Count; ii++) {
         if (segments[ii].Vec0.Normalized ().EQ (normal) && segments[ii].Vec1.Normalized ().EQ (normal)) {
            if (stIx < 0) stIx = ii;
            endIx = ii;
         }
      }
      return new Tuple<int, int> (stIx, endIx);
   }

   /// <summary>
   /// This method is to be used as a predicate: if a given normal is on the
   /// Flex section of the part
   /// </summary>
   /// <param name="normal"></param>
   /// <returns></returns>
   public static bool IsNormalAtFlex (Vector3 normal) {
      normal = normal.Normalized ();
      if (!Math.Abs (normal.Y).EQ (0) && !Math.Abs (normal.Z).EQ (0)) return true;
      return false;
   }

   /// <summary>
   /// This method is used to generate a random file name
   /// </summary>
   /// <param name="length">The length of the filename string</param>
   /// <returns></returns>
   /// <exception cref="ArgumentException"></exception>
   public static string GenerateRandomString (int length) {
      if (length < 1)
         throw new ArgumentException ("Length must be at least 1.");

      const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
      const string digits = "0123456789";

      Random random = new ();

      // Start with a random letter
      char firstChar = letters[random.Next (letters.Length)];

      StringBuilder sb = new ();
      sb.Append (firstChar);

      // Append the remaining characters as digits
      for (int i = 1; i < length; i++) {
         char digit = digits[random.Next (digits.Length)];
         sb.Append (digit);
      }
      return sb.ToString ();
   }

   /// <summary>
   /// This method is to determine if the currIndex-th segment machining is succeeding 
   /// a past segment, machined in the web flange
   /// </summary>
   /// <param name="segs">The input segments list</param>
   /// <param name="currIndex">The index of the segment whose one of the previous segment 
   /// existed on the web flange</param>
   /// <returns>True if the curr index-th segment succeeded a most recent web flange,
   /// False otherwise</returns>
   /// <exception cref="Exception"></exception>
   public static bool IsMachiningFromWebFlange (List<ToolingSegment> segs, int currIndex) {
      if (Math.Abs (segs[0].Vec0.Normalized ().Z).EQ (1))
         return true;
      if (Math.Abs (segs[0].Vec0.Normalized ().Y).EQ (1) ||
         Math.Abs (segs[0].Vec0.Normalized ().Y).EQ (-1))
         return false;
      for (int ii = currIndex; ii < segs.Count; ii++) {
         if (Math.Abs (segs[ii].Vec0.Normalized ().Z).EQ (1))
            return false;
         if (Math.Abs (segs[ii].Vec0.Normalized ().Y).EQ (1) ||
            Math.Abs (segs[ii].Vec0.Normalized ().Y).EQ (-1))
            return true;
      }
      for (int ii = currIndex; ii < segs.Count; ii++) {
         if (Math.Abs (segs[ii].Vec0.Normalized ().Z).SGT (1e-6) &&
            Math.Abs (segs[ii].Vec1.Normalized ().Y).SGT (1e-6)) {
            if (Math.Abs (segs[ii].Vec1.Normalized ().Z).SGT (Math.Abs (segs[ii].Vec1.Normalized ().Z)))
               return true;
            return false;
         }
      }
      throw new Exception ("Can not figure the next ordinate flange");
   }

   public static ToolingSegment GetMachiningSegmentPostWJT (
      ToolingSegment wjtSeg,
      Vector3 scrapSideNormal,
      Bound3 partBound,
      double approachDistance) {
      var nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * approachDistance;

      // Adjust the machining start point based on boundary constraints
      if (scrapSideNormal.Dot (XForm4.mNegZAxis).SGT (0)) {
         if (nextMachiningStart.Z.SLT (partBound.ZMin))
            nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * approachDistance * 0.5;
      } else if (scrapSideNormal.Dot (XForm4.mXAxis).SGT (0)) {
         if (nextMachiningStart.X.SGT (partBound.XMax))
            nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * approachDistance * 0.5;
      } else if (scrapSideNormal.Dot (XForm4.mNegXAxis).SGT (0)) {
         if (nextMachiningStart.X.SLT (partBound.XMin))
            nextMachiningStart = wjtSeg.Curve.End + scrapSideNormal.Normalized () * approachDistance * 0.5;
      }

      return new ToolingSegment (new FCLine3 (nextMachiningStart, wjtSeg.Curve.End) as FCCurve3, wjtSeg.Vec1, wjtSeg.Vec1);
   }

   public static string BuildDINFileName (string filename, int head, MCSettings.PartConfigType partCfgType, string dinFilenameSuffix) {
      string dinFileSuffix = string.IsNullOrEmpty (dinFilenameSuffix) ? "" : $"-{dinFilenameSuffix}-";
      string dinFileName = $@"{filename}-{head + 1}{dinFileSuffix}({(partCfgType == MCSettings.PartConfigType.LHComponent ? "LH" : "RH")}).din";
      return dinFileName;
   }

   public static string RemoveLastExtension (string filePath) {
      return Path.Combine (Path.GetDirectoryName (filePath) ?? "",
                          Path.GetFileNameWithoutExtension (filePath));
   }

   /// <summary>
   /// This method is a helper one to figure out if the notch happens at the center of the 
   /// part and if it is split. This may happen the LH and RH components are merged into a single component.
   /// As part of the initial implementation strategy, a notch shall have "Split" key word 
   /// int its name and shall have the parent unsplit notch
   /// </summary>
   /// <param name="toolingItem">Input tooling Item</param>
   /// <param name="segs">Tooling Segments</param>
   /// <returns></returns>
   public static bool IsDualFlangeSameSideNotch (Tooling toolingItem, List<ToolingSegment> segs) {
      if (toolingItem.NotchKind == ECutKind.Top2YNeg || toolingItem.NotchKind == ECutKind.Top2YPos) {
         if (segs[0].Vec0.Normalized ().EQ (segs[^1].Vec1.Normalized ()) &&
            (segs[0].Vec0.Normalized ().EQ (XForm4.mYAxis) || segs[0].Vec0.Normalized ().EQ (XForm4.mNegYAxis)))
            return true;
      }
      if (toolingItem.FeatType.Contains ("Split")) {
         if (toolingItem.RefTooling != null && (toolingItem.RefTooling.NotchKind == ECutKind.Top2YNeg || toolingItem.RefTooling.NotchKind == ECutKind.Top2YPos)) {
            if (toolingItem.RefTooling.Segs[0].Vec0.Normalized ().EQ (toolingItem.RefTooling.Segs[^1].Vec1.Normalized ()) &&
               (toolingItem.RefTooling.Segs[0].Vec0.Normalized ().EQ (XForm4.mYAxis) || toolingItem.RefTooling.Segs[0].Vec0.Normalized ().EQ (XForm4.mNegYAxis)))
               return true;
         }
      }
      return false;
   }

   public static bool IsDualFlangeSameSideCutout (List<ToolingSegment> segs) {
      int planes = 0;
      EPlane plane = EPlane.None;
      foreach (var seg in segs) {
         var plType1 = Utils.GetFeatureNormalPlaneType (seg.Vec0, XForm4.IdentityXfm);
         if (plType1 == EPlane.Top || plType1 == EPlane.YPos || plType1 == EPlane.YNeg) {
            if (plane != plType1) {
               plane = plType1;
               planes++;
            }
         }
         if (planes == 2)
            return true;
      }
      // CONSIDER_CONSIDER what if the hole/citout is only on the flex
      return false;
   }
   /// <summary>
   /// This method returns the length of the tooling segments specified from
   /// start and end index. If they are specified as -1, this returns the 
   /// length of all the segments
   /// </summary>
   /// <param name="toolingItem">The Tooling item whose length is to be measured</param>
   /// <param name="startIndex"></param>
   /// <param name="endIndex"></param>
   /// <returns></returns>
   public static double GetToolingLength (Tooling toolingItem, int startIndex = -1, int endIndex = -1) {
      if (startIndex == -1 && endIndex == -1) return toolingItem.Perimeter;
      if (startIndex == -1 || endIndex == -1) throw new Exception ("Start and End indices should be valid index");
      double len = 0;
      for (int ii = startIndex; ii <= endIndex; ii++)
         len += toolingItem.Segs[startIndex].Curve.Length;
      return len;
   }
   public static double GetToolingLength (List<ToolingSegment> segs) {
      if (segs == null) throw new ArgumentException ("argument List<ToolingSegment> segs is null,", nameof (segs));
      double cumLen = 0;
      foreach (var seg in segs)
         cumLen += seg.Curve.Length;
      return cumLen;
   }

   public static double GetToolingLengthWithLeadIn (List<ToolingSegment> segs) {
      if (segs == null) throw new ArgumentException ("argument List<ToolingSegment> segs is null,", nameof (segs));
      double cumLen = 0;
      for (int ii = 0; ii < segs.Count; ii++) {
         if (ii == 0) {
            FCArc3? arc = segs[0].Curve as FCArc3;
            // In lead ins, the length of the arc is 2*Pi*r/4 / specified in LeadInApproachArcAngle degrees (90 def)
            var (_, rad) = EvaluateCenterAndRadius (arc);
            //cumLen += Math.PI * rad / 2;
            cumLen += MCSettings.It.LeadInApproachArcAngle.ToRadians () * rad;
         } else
            cumLen += segs[ii].Curve.Length;
      }
      return cumLen;
   }

   public static double GetToolingLength (List<ToolingSegment> segs, int startIndex = -1, int endIndex = -1, bool leadIn = false) {
      if (segs == null) throw new ArgumentException ("argument List<ToolingSegment> segs is null,", nameof (segs));
      if (startIndex == -1 && endIndex == -1) {
         if (leadIn) return GetToolingLengthWithLeadIn (segs);
         return GetToolingLength (segs);
      }
      if (startIndex == -1 || endIndex == -1) throw new Exception ("Start and End indices should be valid index");
      double cumLen = 0;
      for (int ii = startIndex; ii <= endIndex; ii++) {
         if (ii == 0 && leadIn) {
            FCArc3? arc = segs[0].Curve as FCArc3;
            // In lead ins, the length of the arc is 2*Pi*r/4 = Pi*r/2;
            var (_, rad) = EvaluateCenterAndRadius (arc);
            //cumLen += Math.PI * rad / 2;
            cumLen += MCSettings.It.LeadInApproachArcAngle.ToRadians () * rad;

         } else
            cumLen += segs[startIndex].Curve.Length;
      }
      return cumLen;
   }

   public static bool IsSameSideExitNotch (Tooling ti) {
      if (ti.ProfileKind == ECutKind.Top2YPos || ti.ProfileKind == ECutKind.Top2YNeg) {
         if (ti.Segs[0].Vec0.Normalized ().EQ (ti.Segs[^1].Vec0.Normalized ()))
            return true;
      }
      return false;
   }

   public static List<ToolingSegment> ModifyToolingForToolDiaCompensation (Tooling toolingItem, List<ToolingSegment> toolingSegs) {
      List<ToolingSegment> resSegs;
      if (toolingSegs.Count == 0)
         throw new ArgumentException ("List of tooling segments is empty", nameof (toolingSegs));
      else if (toolingSegs.Count == 1) {
         var seg = toolingSegs[0].Curve;
         if (seg is FCLine3)
            throw new Exception ("The single segment of the tooling item is not an Arc");
         var arc3 = Utils.ModifyCircleForToolDiaCompensation (seg as FCArc3, toolingSegs[0].Vec0);
         resSegs = [Geom.CreateToolingSegmentForCurve (toolingSegs[0], arc3 as FCCurve3)];
      } else {
         var ssegs = toolingSegs;

         if (toolingItem.IsDualFlangeCutoutNotch ())
            ssegs = MoveStartSegToPriorityFlange (ssegs, EFlange.Web);

         bool isWebFlangeFeature = toolingItem.IsWebFlangeFeature ();
         bool isTopOrBottomFlangeFeature = toolingItem.IsTopOrBottomFlangeFeature ();
         int maxXIndex = 0;
         double maxX = double.MinValue;
         //double minY = double.MaxValue;
         //double minZ = double.MaxValue;
         double minYZ = double.MaxValue;

         for (int ii = 0; ii < ssegs.Count; ii++) {
            var arcPlaneType = GetArcPlaneType (ssegs[ii].Vec0, XForm4.IdentityXfm);
            if (maxX.SLT (ssegs[ii].Curve.Start.X)) {
               maxX = ssegs[ii].Curve.Start.X;
               double y = ssegs[ii].Curve.Start.Y;
               double z = ssegs[ii].Curve.Start.Z;
               maxXIndex = ii;
               if (isWebFlangeFeature) {
                  if (minYZ.SGT (y)) {
                     minYZ = ssegs[ii].Curve.Start.Y;
                     maxXIndex = ii;
                  }
               } else if (isTopOrBottomFlangeFeature) {
                  if (minYZ.SGT (z)) {
                     minYZ = ssegs[ii].Curve.Start.Z;
                     maxXIndex = ii;
                  }
               }
            }
         }
         resSegs = [.. ssegs.Skip (maxXIndex), .. ssegs.Take (maxXIndex)];
      }
      return resSegs;
   }

   public static FCArc3 ModifyCircleForToolDiaCompensation (FCArc3? arc, Vector3 apn) {
      if (arc == null) throw new Exception ("Arc is null");
      if (!Utils.IsCircle (arc as FCCurve3))
         return arc;
      var (cen, rad) = Geom.EvaluateCenterAndRadius (arc);
      Point3 newStPt = new (rad + cen.X, cen.Y, cen.Z);
      Point3 intPoint1;
      Point3 intPoint2;

      intPoint1 = XForm4.AxisRotation (apn, cen, newStPt, MCSettings.It.LeadInApproachArcAngle.ToRadians ());
      intPoint2 = XForm4.AxisRotation (apn, cen, newStPt, -MCSettings.It.LeadInApproachArcAngle.ToRadians ());

      (cen, rad) = Geom.EvaluateCenterAndRadius (arc);

      return new FCArc3 (newStPt, intPoint1, intPoint2, newStPt, apn);
   }

   public static bool IsFlexCutSegment (ToolingSegment ts) {
      if (Utils.IsToolingOnFlex (ts.Vec0, ts.Vec1))
         return true;
      return false;
   }

   public static bool IsGCodeComment (string line) {
      if (string.IsNullOrWhiteSpace (line))
         return false;

      int start = 0;
      int end = line.Length - 1;

      // Skip leading spaces/tabs
      while (start < line.Length && char.IsWhiteSpace (line[start]))
         start++;

      // Skip trailing spaces/tabs
      while (end >= 0 && char.IsWhiteSpace (line[end]))
         end--;

      if (start >= end)
         return false;

      return line[start] == '(' && line[end] == ')';
   }

   public static string NormalizeNegativeZero (string input) {
      if (string.IsNullOrEmpty (input))
         return input;

      return input.Replace ("-0.000", "0.000");
   }
   public static Utils.EFlange[] sFlangeCutPriority = [Utils.EFlange.Bottom, Utils.EFlange.Top, Utils.EFlange.Web, Utils.EFlange.Flex];
   public static XForm4? sXformLHInv = null;
   public static XForm4? sXformRHInv = null;
   public static List<Tooling> GetToolings4Head (List<Tooling> cuts, int headNo, MCSettings mcs) {
      // New priorities are set as per task FCH-35
      List<Tooling> res, holes = [];

      holes = [.. cuts.Where (cut => cut.Head == headNo && cut.Kind == EKind.Hole)];

      // Set priority by flange on which the features are present in flangeCutPriority
      holes = [..holes.OrderBy (cut => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (cut,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      //.ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      .ThenBy (cut => cut.Start.Pt.X)];

      // Collect CutOuts, then order by by flange priority ( flangeCutPriority ),  then by ascending order of X
      var cutouts = (cuts.Where (cut => cut.Kind == EKind.Cutout && cut.Head == headNo));
      cutouts = [..cutouts.OrderBy (cut => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (cut,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      //.ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      .ThenBy (cut => cut.Start.Pt.X)];

      // Collect single plane notches, then order by flange priority ( flangeCutPriority ),  then by ascending order of X
      var singlePlaneNotches = cuts.Where (cut => cut.Kind == EKind.Notch && cut.Head == headNo && cut.IsSingleFlangeTooling ());
      singlePlaneNotches = [..singlePlaneNotches.OrderBy (cut => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (cut,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      .ThenBy (cut => cut.Start.Pt.X)];

      // Collect dual plane notches , then order by flange priority ( flangeCutPriority ),  then by ascending order of X
      var dualPlaneNotches = cuts.Where (cut => cut.Kind == EKind.Notch && cut.Head == headNo && cut.IsDualFlangeTooling ());
      dualPlaneNotches = [..dualPlaneNotches.OrderBy (cut => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (cut,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      .ThenBy (cut => cut.Start.Pt.X)];

      // Concat all
      res = [.. holes, .. cutouts, .. singlePlaneNotches, .. dualPlaneNotches];

      return res;
   }

   public static ToolScopeList GetToolingScopes4Head (ToolScopeList tss, int headNo, MCSettings mcs) {
      // New priorities are set as per task FCH-35
      ToolScopeList res, holes = [];
      
      holes = [.. tss.Where (ts => ts.Tooling.Head == headNo && ts.Tooling.Kind == EKind.Hole)];

      // Set priority by flange on which the features are present in flangeCutPriority
      holes = [..holes.OrderBy (ts => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (ts.Tooling,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      //.ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      .ThenBy (ts => ts.Tooling.Start.Pt.X)];

      // Collect CutOuts, then order by by flange priority ( flangeCutPriority ),  then by ascending order of X
      var cutouts = (tss.Where (ts => ts.Tooling.Kind == EKind.Cutout && ts.Tooling.Head == headNo));
      cutouts = [..cutouts.OrderBy (ts => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (ts.Tooling,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      //.ThenBy (cut => MCSettings.It.ToolingPriority.ToList().IndexOf (cut.Kind))
      .ThenBy (ts => ts.Tooling.Start.Pt.X)];

      // Collect single plane notches, then order by flange priority ( flangeCutPriority ),  then by ascending order of X
      var singlePlaneNotches = tss.Where (ts => ts.Tooling.Kind == EKind.Notch && ts.Tooling.Head == headNo && ts.Tooling.IsSingleFlangeTooling ());
      singlePlaneNotches = [..singlePlaneNotches.OrderBy (ts => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (ts.Tooling,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      .ThenBy (ts => ts.Tooling.Start.Pt.X)];

      // Collect dual plane notches , then order by flange priority ( flangeCutPriority ),  then by ascending order of X
      var dualPlaneNotches = tss.Where (ts => ts.Tooling.Kind == EKind.Notch && ts.Tooling.Head == headNo && ts.Tooling.IsDualFlangeTooling ());
      dualPlaneNotches = [..dualPlaneNotches.OrderBy (ts => Array.IndexOf (sFlangeCutPriority, Utils.GetFlangeType (ts.Tooling,mcs.PartConfig==PartConfigType.LHComponent?sXformLHInv:sXformRHInv)))
      .ThenBy (ts => ts.Tooling.Start.Pt.X)];

      // Concat all
      res = [.. holes, .. cutouts, .. singlePlaneNotches, .. dualPlaneNotches];

      return res;
   }

   public static bool IsHoleFeature (Tooling toolingItem, Bound3 workpieceBound, double minCutoutLengthThreshold) {
      bool toTreatAsCutOut = CutOut.ToTreatAsCutOut (toolingItem.Segs, workpieceBound, minCutoutLengthThreshold);
      if ((toolingItem.IsHole () && !toTreatAsCutOut) || toolingItem.IsMark ())
         return true;
      return false;
   }

   public static (double MinStartX, double MaxEndX)? GetScopeXExtents (
        ToolScopeList toolScopes) {
      ArgumentNullException.ThrowIfNull (toolScopes);

      if (toolScopes.Count == 0)
         return null;

      double minStartX = double.MaxValue;
      double maxEndX = double.MinValue;

      for (int ii = 0; ii < toolScopes.Count; ii++) {
         var ts = toolScopes[ii];

         if (ts.StartX < minStartX)
            minStartX = ts.StartX;

         if (ts.EndX > maxEndX)
            maxEndX = ts.EndX;
      }

      return (minStartX, maxEndX);
   }

   /// <summary>
   /// Returns the rapid position distance from pointVec of (flange/web) to
   /// pointVec ( flange/Web)
   /// </summary>
   /// <param name="from"></param>
   /// <param name="to"></param>
   /// <returns></returns>
   public static double GetRapidPosDist (PointVec? from, PointVec? to) {
      if (from is null)
         throw new ArgumentNullException (nameof (from));
      if (to is null)
         throw new ArgumentNullException (nameof (to));

      var fromVec = from.Value.Vec.Normalized (); var toVec = to.Value.Vec.Normalized ();
      var fromPt = from.Value.Pt; var toPt = to.Value.Pt;
      if (fromPt.EQ (toPt)) return 0.0;
      // Same side flange
      if (fromVec.EQ (toVec))
         return fromPt.DistTo (toPt);

      else if (!fromVec.EQ (XForm4.mZAxis) && !toVec.EQ (XForm4.mZAxis)) {
         //top to bottom or bottom to top
         double zDist = Math.Abs (fromPt.Z) + Math.Abs (toPt.Z);
         var fromPtAtZ0 = new Point2 (from.Value.Pt.X, fromPt.Y);
         var toPtAtZ0 = new Point2 (to.Value.Pt.X, to.Value.Pt.Y);
         var rapidPosDist = zDist + fromPtAtZ0.DistTo (toPtAtZ0);
         return rapidPosDist;
      } else if (fromVec.EQ (XForm4.mZAxis) || toVec.EQ (XForm4.mZAxis)) {
         // Web to flange or flange to web
         double zDist = Math.Abs (fromPt.Z) + Math.Abs (toPt.Z);
         var fromPtAtZ0 = new Point2 (from.Value.Pt.X, fromPt.Y);
         var toPtAtZ0 = new Point2 (to.Value.Pt.X, to.Value.Pt.Y);
         var rapidPosDist = zDist + fromPtAtZ0.DistTo (toPtAtZ0);
         return rapidPosDist;
      }
      return 0;
   }

   public static PointVec? GetStartPos (ToolScopeList tss) {
      ArgumentNullException.ThrowIfNull (tss);
      if (tss.Count == 0)
         return null;
      return new PointVec (tss[0].Tooling.Segs[0].Curve.Start, tss[0].Tooling.Segs[0].Vec0.Normalized ());
   }

   public static PointVec? GetEndPos (ToolScopeList tss) {
      ArgumentNullException.ThrowIfNull (tss);
      if (tss.Count == 0)
         return null;
      return new PointVec (tss[0].Tooling.Segs[^1].Curve.Start, tss[0].Tooling.Segs[^1].Vec0.Normalized ());
   }

   public static PointVec? GetPosAt (ToolScopeList tss, int index) {
      ArgumentNullException.ThrowIfNull (tss);
      if (tss.Count == 0)
         return null;
      return new PointVec (tss[index].Tooling.Segs[^1].Curve.Start, tss[index].Tooling.Segs[^1].Vec0.Normalized ());
   }

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

   public static XForm4 XfmToMachine (IGCodeGenerator codeGen, XForm4 xFormWCS) {
      XForm4 mcXForm;
      if (codeGen.PartConfigType == MCSettings.PartConfigType.LHComponent) mcXForm = Utils.sXformLHInv * xFormWCS;
      else mcXForm = Utils.sXformRHInv * xFormWCS;
      return mcXForm;
   }

   public static Vector3 XfmToMachineVec (IGCodeGenerator codeGen, Vector3 vecWRTWCS) {
      Vector3 resVec;
      if (codeGen.PartConfigType == MCSettings.PartConfigType.LHComponent) resVec = Utils.sXformLHInv * vecWRTWCS;
      else resVec = Utils.sXformRHInv * vecWRTWCS;
      return resVec;
   }

   public static void CreatePartition (IGCodeGenerator gcGen, List<ToolingScope> tss, bool optimize, Bound3 bound) {
      var toolings = tss.Select (ts => ts.Tooling).ToList ();
      gcGen.CreatePartition (toolings, optimize, bound);
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
   public static Point3 XfmToMachine (IGCodeGenerator codeGen, Point3 ptWRTWCS) {
      Vector3 resVec;
      if (codeGen.PartConfigType == MCSettings.PartConfigType.LHComponent) resVec = Utils.sXformLHInv * ptWRTWCS;
      else resVec = Utils.sXformRHInv * ptWRTWCS;
      return Geom.V2P (resVec);
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
   public static XForm4? GetXForm (Workpiece wp, IGCodeGenerator? gcGen = null) {
      ArgumentNullException.ThrowIfNull (wp);
      if (Utils.sXformLHInv == null || Utils.sXformRHInv == null)
         Utils.EvaluateToolConfigXForms (wp);
      if (gcGen == null)
         return MCSettings.It.PartConfig == PartConfigType.LHComponent ? Utils.sXformLHInv : Utils.sXformRHInv;
      return gcGen.PartConfigType == PartConfigType.LHComponent ? Utils.sXformLHInv : Utils.sXformRHInv;
   }

   public static int GetLastNonEmptyIndex (
    List<Frame?>? frames,
    IGCodeGenerator.ToolHeadType head) {
      if (frames == null || frames.Count == 0)
         return -1;

      for (int ii = frames.Count - 1; ii >= 0; ii--) {
         var frame = frames[ii];
         if (!frame.HasValue)
            continue;

         var f = frame.Value;

         if (head == IGCodeGenerator.ToolHeadType.Master ||
             head == IGCodeGenerator.ToolHeadType.MasterB2) {
            if (f.FrameToolScopesH11.Count > 0 ||
                f.FrameToolScopesH12.Count > 0)
               return ii;
         } else {
            if (f.FrameToolScopesH21.Count > 0 ||
                f.FrameToolScopesH22.Count > 0)
               return ii;
         }
      }

      return -1;
   }

   public static int GetLastNonEmptyIndexPerBucket (
    List<Frame?>? frames,
    IGCodeGenerator.ToolHeadType head) {

      if (frames == null || frames.Count == 0)
         return -1;

      for (int ii = frames.Count - 1; ii >= 0; ii--) {
         var frame = frames[ii];
         if (!frame.HasValue)
            continue;

         var f = frame.Value;

         switch (head) {
            case IGCodeGenerator.ToolHeadType.Master:
               if (f.FrameToolScopesH11.Count > 0)
                  return ii;
               break;

            case IGCodeGenerator.ToolHeadType.MasterB2:
               if (f.FrameToolScopesH12.Count > 0)
                  return ii;
               break;

            case IGCodeGenerator.ToolHeadType.Slave:
               if (f.FrameToolScopesH21.Count > 0)
                  return ii;
               break;

            case IGCodeGenerator.ToolHeadType.SlaveB2:
               if (f.FrameToolScopesH22.Count > 0)
                  return ii;
               break;
         }
      }

      return -1;
   }

   // Helper method to create bounds from multiple points
   public static Bound3 CreateBounds (params Point3[] points) {
      if (points == null || points.Length == 0)
         throw new ArgumentException ("At least one point is required");

      double xmin = points[0].X, ymin = points[0].Y, zmin = points[0].Z;
      double xmax = points[0].X, ymax = points[0].Y, zmax = points[0].Z;

      for (int ii = 1; ii < points.Length; ii++) {
         xmin = Math.Min (xmin, points[ii].X);
         ymin = Math.Min (ymin, points[ii].Y);
         zmin = Math.Min (zmin, points[ii].Z);
         xmax = Math.Max (xmax, points[ii].X);
         ymax = Math.Max (ymax, points[ii].Y);
         zmax = Math.Max (zmax, points[ii].Z);
      }

      return new Bound3 (xmin, ymin, zmin, xmax, ymax, zmax);
   }

   public static (double? minStartX, double? maxEndX) GetScopeXExtents (ToolingList toolings) {
      if (toolings.Count == 0)
         return (0, 0);

      double minStartX = double.MaxValue;
      double maxEndX = double.MinValue;

      bool valChanged = false;
      for (int ii = 0; ii < toolings.Count; ii++) {
         var tooling = toolings[ii];
         if (tooling == null) continue;

         var segs = tooling.Segs;
         for (int jj = 0; jj < tooling.Segs.Count; jj++) {
            var segment = segs[jj];
            if (segment.Curve == null)
               throw new Exception ($"Curve for {ii} th toolscope and {jj} th tool segment is null");

            double startX = segment.Curve.Start.X;
            double endX = segment.Curve.End.X;

            if (startX < minStartX) {
               minStartX = startX;
               valChanged = true;
            }

            if (endX > maxEndX) {
               maxEndX = endX;
               valChanged = true;
            }
         }
      }
      if (valChanged)
         return (minStartX, maxEndX);
      else return (null, null);
   }

   public static Bound3 GetBound(ToolScopeList tsList) {
      Bound3 bound = Bound3.Empty;
      foreach( var ts in tsList) {
         foreach( var seg in ts.Tooling.Segs)
            bound += seg.GetBound ();
      }
      return bound;
   }
}