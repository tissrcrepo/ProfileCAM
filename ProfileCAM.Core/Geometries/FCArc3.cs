using Flux.API;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Windows.Shapes;
using static ProfileCAM.Core.Utils;

namespace ProfileCAM.Core.Geometries;

/// <summary>
/// Wrapper over Flux Arc3.
/// Circles are considered CCW by default (unless user explicitly changes ArcSense).
/// </summary>
public class FCArc3 : FCCurve3 {
   public Arc3 Arc { get; private set; }

   private bool mIsCircle;

   public double Tol { get; set; } = 1e-6;

   public Point3 Center { get; private set; }
   public double Radius { get; private set; }

   // Correct overrides (read-only)
   public override Point3 Start { get; set; }
   public override Point3 End { get; set; }
   public override double Length { get; set; }
   public override bool IsCircle { get; set; }
   public override Curve3 Curve { get; set; }

   public Vector3 PlaneNormal { get; private set; }
   public Vector3 ArcNormal { get; private set; }
   public Point3 IP1 { get; private set; }
   public Point3 IP2 { get; private set; }

   private Utils.EArcSense _arcSense;
   Utils.ArcType _arcType;

   public Utils.EArcSense ArcSense {
      get => _arcSense;
      set => _arcSense = value;   // User can override even for circles
   }

   // SignedAngle
   public double SignedAngle { get; private set; }   // Signed sweep angle from Start to End



   /// <summary>
   /// Main constructor. If Start == End (within tolerance), it is treated as a full circle (CCW by default).
   /// </summary>
   public FCArc3 (Point3 s, Point3 i1, Point3 i2, Point3 e, Vector3 planeNormal, double tol = 1e-6) {
      Tol = tol;

      ////----Coplanarity check------------------------------------------------
      var plane = new Plane (s, i1, i2, tol);
      if (!plane.Contains (e, tol))
         throw new Exception ($"Points {s}, {i1}, {i2}, {e} are not coplanar.");
      try {
         Arc = new Arc3 (s, i1, i2, e);
      } catch (Exception ex) {
         throw new Exception (ex.Message);
      }

      // ---- Normalize and validate PlaneNormal -------------------------------
      PlaneNormal = planeNormal.Normalized ();
      var computedNormal = (i1 - s).Cross (i2 - s).Normalized ();

      if (!computedNormal.Dot (PlaneNormal).EQ (1, tol) &&
          !computedNormal.Dot (PlaneNormal).EQ (-1, tol))
         throw new Exception ("Provided PlaneNormal is inconsistent with input points.");


      if ((s - i1).Length.EQ (0, tol) || (i2 - i1).Length.EQ (0, tol) || (i2 - e).Length.EQ (0, tol))
         throw new Exception ($"Arc definining points are not unique {s}, {i1}, {i2}, {e}");

      // ---- Construct underlying Arc3 ----------------------------------------
      
      Curve = Arc as Curve3;
      Length = Arc.Length;
      // ---- Assign remaining properties --------------------------------------
      Start = s;
      End = e;
      IP1 = i1;
      IP2 = i2;


      Length = Arc.Length;

      // ---- IsCircle is strictly based on Start == End as per your requirement ----
      IsCircle = s.DistTo (e).EQ (0, tol);

      // Compute Center and Radius
      (Center, Radius) = Geom.EvaluateCenterAndRadius (this);

      if (Radius.LTEQ (0, tol))
         throw new InvalidOperationException ("Invalid arc: radius is zero or negative.");

      // Infer arc type
      if (Length.SGT (Math.PI * Radius, tol))
         _arcType = Utils.ArcType.Major;
      else if (Length.SLT (Math.PI * Radius, tol))
         _arcType = Utils.ArcType.Minor;
      else if (Length.EQ (Math.PI * Radius, tol))
         _arcType = Utils.ArcType.Semicircular;
      else
         throw new Exception ("Unable to infer arc type");



      // ---- Compute robust ArcNormal -----------------------------------------
      var vsToi1 = (i1 - s).Normalized ();
      var vi1Toi22 = (i2 - i1).Normalized ();
      var cross = vsToi1.Cross (vi1Toi22);

      if (cross.LengthSquared ().SLT (tol * tol))
         throw new Exception ("Intermediate points are degenerate (too close).");

      ArcNormal = cross.Normalized ();

      // ---- Validate ArcNormal vs PlaneNormal --------------------------------
      if (!ArcNormal.Dot (PlaneNormal).EQ (1, tol) &&
          !ArcNormal.Dot (PlaneNormal).EQ (-1, tol))
         throw new Exception ("ArcNormal are not parallel or reverse parallel.");

      // ---- Determine ArcSense -----------------------------------------------
      if (IsCircle) {
         _arcSense = Utils.EArcSense.CCW;   // Circles are CCW by default
      } else {
         _arcSense = ArcNormal.IsSameSense (PlaneNormal)
             ? Utils.EArcSense.CCW
             : Utils.EArcSense.CW;
      }



      SignedAngle = SignedAngleToPt (End, tol);

      // No safety fallback - IsCircle is decided purely by Start == End
   }

   public IEnumerable<Point3> Discretize (double error) => Arc.Discretize (error);

   public override FCCurve3 Clone () {
      FCArc3 arc = new (Start, IP1, IP2, End, PlaneNormal, Tol);
      return arc as FCCurve3;
   }

   public override FCCurve3 ReverseClone () {
      FCArc3 fArc = new (End, IP2, IP1, Start, PlaneNormal);
      return fArc as FCCurve3;
   }

   // ====================== Evaluators ======================

   /// <summary>
   /// Evaluates point at normalized parameter t ∈ [0,1] 
   /// (for circles: param can be any real number if normalizeAngle=false)
   /// </summary>
   public Point3 EvaluatePointAtParam (double t, double tol = 1e-6) {
      if (Radius.LTEQ (0, tol))
         throw new InvalidOperationException ("Invalid arc: radius ≤ 0.");

      double theta = t * SignedAngle;
      theta = theta % Math.Tau;
      // ====================== Normalize angle for circles ======================

      // ====================== Perform rotation ======================
      // XForm4.AxisRotation rotates around PlaneNormal using right-hand rule.
      // Because we already set the sign of theta correctly based on ArcSense,
      // this produces movement in the correct direction along the arc.
      return XForm4.AxisRotation (PlaneNormal, Center, Start, theta);
   }

   // Signed SignedAngle
   public double SignedAngleToPt (Point3 toPt, double tol = 1e-6) {
      // ---- Validation ---------------------------------------------------
      Plane pl = new (Start, IP1, IP2, tol);
      if (!pl.Contains (toPt, tol))
         throw new ArgumentException ($"Point {toPt} is not on the arc plane {pl}.");

      if (!Center.DistTo (toPt).EQ (Radius, tol))
         throw new ArgumentException ($"Point {toPt} is not at radius {Radius} from center.");

      // ---- Special case: Start point always returns 0 -------------------
      if (Start.EQ (toPt, tol))
         return 0.0;


      // ====================== CIRCLE CASE ======================
      if (IsCircle) {
         if (ArcSense == Utils.EArcSense.CCW) {
            // For CCW circles: return angle in [0, 2π)
            return 2 * Math.PI;                     // positive, up to +2π
         } else // CW
           {
            // For CW circles: return angle in (-2π, 0]

            return -2 * Math.PI;                     // negative, down to -2π
         }
      }


      // ---- Compute signed angle using right-hand rule w.r.t. PlaneNormal ----
      var vC2Start = (Start - Center).Normalized ();
      var vC2Pt = (toPt - Center).Normalized ();

      double angleS2Pt = Math.Acos (
          vC2Start.Dot (vC2Pt)  // Always positive
      );

      if (_arcType == Utils.ArcType.Minor) {
         if (_arcSense == Utils.EArcSense.CCW)
            return angleS2Pt;
         else if (_arcSense == Utils.EArcSense.CW)
            return -angleS2Pt;
      } else { // Major arc
         if (_arcSense == Utils.EArcSense.CCW)
            return 2 * Math.PI - angleS2Pt;
         else if (_arcSense == Utils.EArcSense.CW)
            return -(2 * Math.PI - angleS2Pt);
      }
      throw new Exception ("Undefined case of Arc in computing angle");
   }


   public double EvaluateParamAtPoint (Point3 pt, double tol = 1e-6) {
      Plane pl = new (Center, Start, End);
      if (!pl.Contains (pt, tol))
         throw new ArgumentException ($"Point {pt} is not on the arc plane.");

      if (!Center.DistTo (pt).EQ (Radius, tol))
         throw new ArgumentException ($"Point {pt} is not at radius {Radius} from center.");

      if (Start.EQ (pt, tol)) return 0.0;
      if (End.EQ (pt, tol)) return 1.0;

      double angle2Pt = SignedAngleToPt (pt, tol);

      double t = angle2Pt / SignedAngle;
      return t;
   }

   public bool IsPointOnCurve (Point3 pt) {
      Plane pl = new (Center, Start, End);
      if (!pl.Contains (pt, Tol))
         return false;

      if (!Center.DistTo (pt).EQ (Radius, Tol))
         return false;

      if (IsCircle)
         return true;

      var t = EvaluateParamAtPoint (pt, Tol);

      if (t.LieWithin (0.000, 1.00000)) return true;
      return false;
   }

   //public Point3 GetNewEndPointOnArcAtIncrement (double incrementDist) {
   //   var newT = 1 + (incrementDist / Length);
   //   var pt = EvaluatePointAtParam (newT);
   //   return pt;
   //}

   public List<Point3> GetTwoIntermediatePoints () {
      var p1 = EvaluatePointAtParam (0.25);
      if (!IsPointOnCurve (p1))
         throw new Exception ($"Point {p1} is not on the Arc {Arc}");
      var p2 = EvaluatePointAtParam (0.75);
      if (!IsPointOnCurve (p2))
         throw new Exception ($"Point {p2} is not on the Arc {Arc}");
      return [p1, p2];
   }

   //public List<Point3> GetTwoIntermediatePoints (Point3 p1, Point3 p2) {
   //   if (!IsPointOnCurve (p1, Tol))
   //      throw new Exception ($"Point {p1} is not on the Arc {Arc}");
   //   if (!IsPointOnCurve (p2, Tol))
   //      throw new Exception ($"Point {p2} is not on the Arc {Arc}");
   //   var t1 = EvaluateParamAtPoint (p1);
   //   var t2 = EvaluateParamAtPoint (p2);

   //   var diff = t2 - t1;
   //   var nt1 = t1 + diff / 3;
   //   var nt2 = t1 + 2 * diff / 3;
   //   var nP1 = EvaluatePointAtParam (nt1);
   //   var nP2 = EvaluatePointAtParam (nt2);

   //   return [nP1, nP2];
   //}

   public List<FCArc3> SplitArc (List<Point3> interPointsList,
      double deltaBetweenArcs) {

      for (int ii = 1; ii < interPointsList.Count; ii++) {
         if (interPointsList[ii].DistTo (interPointsList[ii - 1]).EQ (0, Tol))
            throw new Exception ($"interPointsList has duplicate points {interPointsList[ii]} at {ii - 1} and at {ii - 1}");
      }
      List<FCArc3> res = [];
      for (int ii = 0; ii < interPointsList.Count; ii++) {
         if (!IsPointOnCurve (interPointsList[ii]))
            throw new Exception ($"The inter point {interPointsList[ii]} is not on the arc {this}");
         if (interPointsList[ii].EQ (Start, Tol) || interPointsList[ii].EQ (End, Tol))
            continue;
      }

      List<FCArc3> splitArcs = [];
      List<Point3> points = [];
      points.Add (Start); points.AddRange (interPointsList);
      points.Add (End);
      if (points.Count > 2) {
         Point3 newIncrStPt = points[0];
         for (int ii = 0; ii < points.Count - 1; ii++) {
            List<Point3> twoIntermediatePoints = GetTwoIntermediatePoints (newIncrStPt, points[ii + 1], End);
            if (twoIntermediatePoints.Count == 0)
               continue;
            if (twoIntermediatePoints.Count != 2)
               throw new Exception ($"twoIntermediatePoints does not contain 2 points");
            // Nidge intermediate points
            for (int jj = 0; jj < 2; jj++) {
               var p = twoIntermediatePoints[jj];
               //var np = NudgePointToArc (cen, rad, p, apn);
               if (!IsPointOnCurve (p))
                  throw new Exception ($"In SplitArc: one of the twoIntermediatePoints {p} is not on the arc with in 1e-6");
               twoIntermediatePoints[jj] = p;
            }
            var arc1 = new FCArc3 (newIncrStPt, twoIntermediatePoints[0], twoIntermediatePoints[1], points[ii + 1], PlaneNormal);
            if (this.ArcSense != arc1.ArcSense)
               throw new Exception ("Arcsense not same after split");
            if (this._arcType != arc1._arcType)
               throw new Exception ("ArcType not same after split");
            if (arc1.Length.SGT (this.Length))
               throw new Exception ("Split small arc's length is GTEQ parent arc length");
            splitArcs.Add (arc1);
            newIncrStPt = splitArcs[^1].GetNewEndPointOnArcAtIncrement (deltaBetweenArcs);
         }
      } else splitArcs.Add (Clone () as FCArc3);
      return splitArcs;
   }

   List<Point3> GetTwoIntermediatePoints(Point3 p1, Point3 p2, Point3? end = null) {
      if (!IsPointOnCurve (p1))
         throw new Exception ($"Point {p1} is not on the Arc {Arc}");
      if (!IsPointOnCurve (p2))
         throw new Exception ($"Point {p2} is not on the Arc {Arc}");

      double? t1 = null, t2 = null;
      if (p2.EQ (Start, Tol)) {
         var pp = p1;
         p1 = p2;
         p2 = pp;
         t1 = 0.0;
      }
      if (p1.EQ (End, Tol)) {
         var pp = p1;
         p1 = p2;
         p2 = pp;
         t2 = 1.0;
      }
      double te = -1;
      if (end != null)
         te = EvaluateParamAtPoint (end.Value);
      if (p1.EQ (Start, Tol)) t1 = 0.0000000000000000;
      if (p2.EQ (End, Tol)) t2 = 1.000000000000000000;

      if (t1 == null)
         t1 = EvaluateParamAtPoint (p1);
      if (t2 == null)
         t2 = EvaluateParamAtPoint (p2);

      if (!t1.HasValue || !t2.HasValue)
         throw new Exception ($"Parameter for {p1} and/or {p2} can not be found");
      var p3 = EvaluatePointAtParam (t1.Value + (t2.Value - t1.Value) / 3);
      var p4 = EvaluatePointAtParam (t1.Value + 2 * (t2.Value - t1.Value) / 3);
      return [p3, p4];
   }

   public List<FCArc3> SplitArc (FCArc3 fcArc, List<Point3> interPointsList,
      double deltaBetweenArcs, Vector3 apn) {
      List<FCArc3> splitArcs = [];
      List<Point3> points = [];
      points.Add (fcArc.Start); points.AddRange (interPointsList);
      points.Add (fcArc.End);
      if (points.Count > 2) {
         Point3 newIncrStPt = points[0];
         for (int ii = 0; ii < points.Count - 1; ii++) {
            List<Point3> twoIntermediatePoints = GetTwoIntermediatePoints (newIncrStPt, points[ii + 1], End);
            if (twoIntermediatePoints.Count == 0)
               continue;
            // Nidge intermediate points
            
            for (int jj = 0; jj < 2; jj++) {
               var p = twoIntermediatePoints[jj];
               //var np = NudgePointToArc (cen, rad, p, apn);
               if (!IsPointOnCurve (p))
                  throw new Exception ("In SplitArc: nudged point is not on the arc with in 1e-6");
               twoIntermediatePoints[jj] = p;
            }
            var arc1 = new FCArc3 (newIncrStPt, twoIntermediatePoints[0], twoIntermediatePoints[1], points[ii + 1], apn);
            splitArcs.Add (arc1);
            newIncrStPt = splitArcs[^1].GetNewEndPointOnArcAtIncrement (deltaBetweenArcs);
         }
      } else splitArcs.Add (fcArc);
      return splitArcs;
   }

   Point3 GetNewEndPointOnArcAtIncrement(double delta) {
      
      var newSignedAngle = ((Length + delta) / Length) * SignedAngle;
      var newT = newSignedAngle / SignedAngle;
      var p = EvaluatePointAtParam (newT);
      return p;
   }
}