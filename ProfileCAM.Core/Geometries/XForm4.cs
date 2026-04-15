using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Flux.API;
using static ProfileCAM.Core.Utils;
using ProfileCAM.Core.GCodeGen;
using MathNet.Numerics.LinearAlgebra.Factorization;

namespace ProfileCAM.Core.Geometries;

public static class IntExtensions {
   public static int Clamp (this int a, int min, int max) {
      if (a < min) return min;
      if (a > max) return max;
      return a;
   }
}
public static class DoubleExtensions {
   public static double Clamp (this double a, double min, double max) {
      if (a < min) return min;
      if (a > max) return max;
      return a;
   }
   public static double D2R (this double degrees) => degrees * (Math.PI / 180);
   public static double R2D (this double radians) => radians * (180.0 / Math.PI);
   public static double Round (this double input, int digits) => Math.Round (input, digits);
   public static bool GTEQ (this double a, double b, double tol) => a.EQ (b, tol) || a > b;
   public static bool GTEQ (this double a, double b) => (a - b).EQ (0) || a > b;
   public static bool LTEQ (this double a, double b, double tol) => a.EQ (b, tol) || a < b;
   public static bool LTEQ (this double a, double b) => (a - b).EQ (0) || a < b;
   public static bool SGT (this double a, double b, double tol) => !a.EQ (b, tol) && a > b;
   public static bool SGT (this double a, double b) => !(a - b).EQ (0) && a > b;
   public static bool SLT (this double a, double b, double tol) => !a.EQ (b, tol) && a < b;
   public static bool SLT (this double a, double b) => !(a - b).EQ (0) && a < b;
   public static bool GTEQ (this float a, float b, float tol) => a.EQ (b, tol) || a > b;
   public static bool GTEQ (this float a, float b) => (a - b).EQ (0) || a > b;
   public static bool LTEQ (this float a, float b, float tol) => a.EQ (b, tol) || a < b;
   public static bool LTEQ (this float a, float b) => (a - b).EQ (0) || a < b;
   public static bool SGT (this float a, float b, float tol) => !a.EQ (b, tol) && a > b;
   public static bool SGT (this float a, float b) => !(a - b).EQ (0) && a > b;
   public static bool SLT (this float a, float b, float tol) => !a.EQ (b, tol) && a < b;
   public static bool SLT (this float a, float b) => !(a - b).EQ (0) && a < b;

   /// <summary>
   /// Rounds the double to the specified number of decimal places.
   /// </summary>
   /// <param name="value">The double value to round.</param>
   /// <param name="decimalPlaces">The number of decimal places to round to.</param>
   /// <param name="rounding">Optional: The rounding mode to use. Defaults to MidpointRounding.ToEven.</param>
   /// <returns>The rounded double value.</returns>
   public static double Round (this double value, int decimalPlaces,
      MidpointRounding rounding = MidpointRounding.ToEven)
      => Math.Round (value, decimalPlaces, rounding);

}

public static class Vector3Extensions {
   /// <summary>
   /// Checks if two vectors are equal within a specified tolerance.
   /// </summary>
   /// <param name="v1">The first vector.</param>
   /// <param name="v2">The second vector to compare.</param>
   /// <param name="tolerance">The tolerance for comparison (default is 1e-6).</param>
   /// <returns>True if the vectors are equal within the tolerance; otherwise, false.</returns>
   public static bool EQ (this Vector3 v1, Vector3 v2, double tolerance = 1e-6) {

      // Example equality check logic (adjust based on your `Vector` implementation)
      v1 = v1.Normalized (); v2 = v2.Normalized ();
      return v1.X.EQ (v2.X, tolerance) && v1.Y.EQ (v2.Y, tolerance) && v1.Z.EQ (v2.Z, tolerance);
   }
   public static bool Aligned (this Vector3 l, Vector3 r) {
      l = l.Normalized ();
      r = r.Normalized ();
      if (l.Opposing (r)) return false;
      return true;
   }

   public static Vector3 Cross (this Vector3 v1, Vector3 v2) => Geom.Cross (v1, v2);

}

public static class Vector2Extensions {
   public static Vector2 Round (this Vector2 vec, int decimalPlaces,
      MidpointRounding rounding = MidpointRounding.ToEven) {
      return new Vector2 (Math.Round (vec.X, decimalPlaces, rounding), Math.Round (vec.Y, decimalPlaces, rounding));
   }
}

//public class FCArc {
//   Arc3 mArc;
//   Vector3 mNormal;
//   public FCArc (Arc3 arc, Vector3 normal) {
//      mArc = arc;
//      mNormal = normal;
//      Point3 st = arc.Start; Point3 end = arc.End;
//      double C = Math.Sqrt (Math.Pow (end.X - st.X, 2) + Math.Pow (end.Y - st.Y, 2) + Math.Pow (end.Z - st.Z, 2));
//      var (cen, rad) = Geom.EvaluateCenterAndRadius (arc);
//      double L = arc.Length;
//      double t, tNext;
//      t = L / rad;
//      double numer = 2 * L * Math.Sin (t / 2) - C * t;
//      double denom = L * Math.Cos (t / 2) - C;
//      tNext = t - numer / denom;
//      while (Math.Abs (tNext - t).LTEQ (1e-7)) {
//         tNext = t - numer / denom;
//      }
//   }
//   public Point3 Start { get; private set; }
//   public Point3 End { get; private set; }
//}
public class Geom {
   #region Enums
   public enum PairOfLineSegmentsType {
      Collinear,
      Skew,
      Parallel,
      SinglePointIntersection,
      SegmentsNotIntersectingWithinLimits
   }
   public enum PMC {
      Outside,
      Inside,
      On,
      CanNotEvaluate
   }
   public enum ToolingWinding {
      CW, CCW
   }
   #endregion

   #region DataTypes
   public struct Triangle3D (int a, int b, int c) {
      public int A { get; set; } = a;
      public int B { get; set; } = b;
      public int C { get; set; } = c;
   }
   public struct Line3D (int a, int b) {
      public int A { get; set; } = a;
      public int B { get; set; } = b;
   }
   #endregion

   #region Methods for 3D Arc
   /// <summary>
   /// This method is used to compute the local coordinate system of the 3d arc
   /// in such a way that the center to start direction becomes the local X Axis
   /// The normal to the plane of the arc, which MUST be obtained from Tooling plane
   /// normal becomes the Z axis and the Cross product of Z with X becomes Y axis.
   /// </summary>
   /// <param name="arc">The input arc</param>
   /// <param name="apn">The arc plane normal, which must be obtained from the tooling
   /// plane normal.</param>
   /// <returns>The Local coordinate system of the arc</returns>
   public static XForm4 GetArcCS (FCArc3 fcArc, Vector3 apn) {
      (var center, _) = EvaluateCenterAndRadius (fcArc);
      var normal = apn.Normalized ();
      Vector3 xVec = (fcArc.Start - center).Normalized ();
      Vector3 yVec = Geom.Cross (normal, xVec).Normalized ();
      XForm4 transform = new (xVec, yVec, normal.Normalized (), Geom.P2V (center));
      return transform;
   }

   /// <summary>
   /// This method evaluates the point on the arc for a given parameter. Point to note here
   /// is that this method is defined for a parameter range from 0 to 1.
   /// </summary>
   /// <param name="arc">The Input arc</param>
   /// <param name="param">Legit value is from 0 to 1</param>
   /// <param name="apn">The arcplane normal, that should be provided by the tooling</param>
   /// <returns>Point3 point at the parameter</returns>
   public static Point3 EvaluateArc (FCArc3 arc, double param, Vector3 apn) {
      var (angle, _) = GetArcAngleAndSense (arc, apn);
      return GetArcPointAtAngle (arc, angle * param, apn);
   }

   /// <summary>
   /// This method computes the tangent (vector3) and normal (vector3) at any point on the arc.
   /// </summary>
   /// <param name="arc">The input arcThe </param>
   /// <param name="pt">The point on the arc</param>
   /// <param name="apn">The arc plane normal that should be provided by the tooling</param>
   /// <param name="constrainedWithinArc">An optional boolean flag if the computation point be constrained
   /// to be strictly between start and end of the curve segment. This is used here to check if the point
   /// on the curve is strictly between the start and end points</param>
   /// <returns>A tuple of Tangent and Normal vector at the given point</returns>
   public static Tuple<Vector3, Vector3> EvaluateTangentAndNormalAtPoint (FCArc3 fcArc, Point3 pt, Vector3 apn,
      bool constrainedWithinArc = true, double tolerance = 1e-6) {
      if (fcArc == null || !IsPointOnCurve (fcArc as FCCurve3, pt, apn, hintSense: EArcSense.Infer, tolerance: 1e-3, constrainedWithinArc))
         throw new Exception ("Arc is null or point is not on the curve");
      var param = GetParamAtPoint (fcArc as FCCurve3, pt, apn, tolerance: tolerance);
      var (center, _) = EvaluateCenterAndRadius (fcArc);
      var pointAtParam1 = Geom.Evaluate (fcArc, param - 0.1, apn);
      var pointAtParam3 = Geom.Evaluate (fcArc, param + 0.1, apn);
      var pointAtParam2 = pt;
      var refVectorAlongTgt = pointAtParam2 - pointAtParam1;
      var normal = (pt - center).Normalized ();
      var planeNormal = Geom.Cross (pointAtParam3 - pointAtParam2, pointAtParam2 - pointAtParam1).Normalized ();
      var tangent = Geom.Cross (normal, planeNormal).Normalized ();
      if (tangent.Opposing (refVectorAlongTgt)) tangent *= -1;
      tangent = tangent.Normalized ();
      return new Tuple<Vector3, Vector3> (tangent, normal);
   }

   /// <summary>
   /// This method is used to get the angle between the start to any given point on the arc, considering 
   /// the Arc sense also, WRT the arc plane normal. TODO: Merge thsi method with GetArcAngle()
   /// </summary>
   /// <param name="arc">The input arc</param>
   /// <param name="pt">The given point on the arc</param>
   /// <param name="apn">The arc plane normal that should be provided by the tooling</param>
   /// <returns>A tuple of arc angle in radians together with EArcSense, which could be CW or CCW. 
   /// The angle returned is in a sense CW or CCW WRT the arc plane normal amanating towards
   /// the observer</returns>
   /// <exception cref="Exception">An exception is thrown if the given point is not on the arc</exception>
   public static Tuple<double, EArcSense> GetArcAngleAtPoint (FCArc3 fcArc, Point3 pt, Vector3 apn, EArcSense hintSense, double tolerance = 1e-6) {
      if (!fcArc.Start.EQ (pt, tolerance) && !fcArc.Start.EQ (pt, tolerance)) {
         (var center, var radius) = EvaluateCenterAndRadius (fcArc);

         // If the given pt is neither start and end of the fcArc..
         if (!fcArc.Start.EQ (pt, tolerance) && !fcArc.End.EQ (pt, tolerance)) {
            var dist = Math.Abs (pt.DistTo (center) - radius);
            var dotp = apn.Dot ((pt - center).Normalized ());
            if (!dist.EQ (0.0, tolerance) || !Math.Abs (dotp).EQ (0.0, tolerance))
               throw new Exception ("Given point is not on the 3d circle");
         }
         if (Utils.IsCircle (fcArc)) {
            if ((fcArc.Start - pt).Length.EQ (0.0, tolerance)) return new Tuple<double, EArcSense> (0, EArcSense.CCW);
            else if ((fcArc.End - pt).Length.EQ (0.0, tolerance)) return new Tuple<double, EArcSense> (2 * Math.PI, EArcSense.CCW);
         }
      } else {
         if (fcArc.Start.EQ (pt, tolerance)) {
            if (Utils.IsCircle (fcArc))
               return new Tuple<double, EArcSense> (Math.PI * 2, EArcSense.CCW);
            else
               return new Tuple<double, EArcSense> (0, EArcSense.CCW);
         }
      }
      return GetArcAngleAndSense (fcArc, fcArc.Start, pt, apn, hintSense);
   }

   public static Tuple<double, EArcSense> GetArcAngleAtPoint (FCArc3 fcArc, Vector3 apn, EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6) {
      return GetArcAngleAtPoint (fcArc, fcArc.End, apn, hintSense, tolerance);
   }

   public static bool IsMajor (FCArc3 arc) {
      (_, var rad) = EvaluateCenterAndRadius (arc);
      if ((arc.Length.SGT (Math.PI * rad))) return true;
      return false;
   }
   public static Tuple<double, EArcSense> GetArcAngleAndSense (FCArc3 fcArc, Vector3 normal, EArcSense hintSense = EArcSense.Infer) {
      return GetArcAngleAndSense (fcArc, fcArc.Start, fcArc.End, normal, hintSense);
   }

   public static Tuple<double, EArcSense> GetArcAngleAndSense (FCArc3 fcArc, Point3 start, Point3 end, Vector3 normal, EArcSense hintSense) {
      if (fcArc == null) throw new Exception ("Geom.GetArcAngleAndSense: fcArc is null");
      var (cen, _) = EvaluateCenterAndRadius (fcArc);

      if (Utils.IsCircle (fcArc)) {
         if (fcArc.Start.EQ (start) && fcArc.End.EQ (end)) {
            if (hintSense == EArcSense.Infer)
               return new Tuple<double, EArcSense> (Math.PI * 2, EArcSense.CCW);
            else
               return new Tuple<double, EArcSense> (Math.PI * 2, hintSense);
         }
         if (!IsPointOnCurve (fcArc as FCCurve3, start, normal))
            throw new Exception ("In GetArcAngleAndSense: For the circle, the start point is not on the circle");
         if (!IsPointOnCurve (fcArc as FCCurve3, end, normal))
            throw new Exception ("In GetArcAngleAndSense: For the circle, the end point is not on the circle");
         var cenToStart = start - cen; var cenToEnd = end - cen;
         var crossP = Geom.Cross (cenToStart, cenToEnd).Normalized ();
         var angBet = Math.Acos (cenToStart.Normalized ().Dot (cenToEnd.Normalized ()));
         if (crossP.Opposing (normal)) angBet = 2 * Math.PI - angBet;
         if (hintSense == EArcSense.Infer)
            return new Tuple<double, EArcSense> (angBet, EArcSense.CCW);
         else
            return new Tuple<double, EArcSense> (angBet, hintSense);
      } else if (fcArc.Start.EQ (start) && fcArc.Start.EQ (end) || fcArc.Start.EQ (end) && fcArc.Start.EQ (start)) {
         // If the start or end point provided is the start point of the fcArc itself, no need to compute
         // and return the 0 angle.
         if (hintSense == EArcSense.Infer)
            return new Tuple<double, EArcSense> (0, EArcSense.CCW);
         else
            return new Tuple<double, EArcSense> (0, hintSense);
      }

      // Compute the vectors from center to start and center to end
      normal = normal.Normalized ();
      var (center, radius) = EvaluateCenterAndRadius (fcArc);
      Vector3 vecStart = (start - center).Normalized ();
      Vector3 vecEnd = (end - center).Normalized ();

      // Calculate the angle between the two vectors using the dot product
      double dot = vecStart.Dot (vecEnd).Clamp (-0.9999999999999999999999, 0.999999999999999999999999999);
      double includedAngle = Math.Acos (dot);
      var angle = includedAngle;

      if (Math.Abs (Math.Abs (angle) - Math.PI) < 1e-5) {
         var arcDirFromStartPt = (fcArc.Arc.Evaluate (0.1) - fcArc.Start).Normalized ();
         var scVec = (center - start).Normalized ();

         // There is a finite difference in the fcArc length if the fcArc is semicircular. Instead of
         // the length being exactly PI*radius, in many cases, it is little lesser. In those cases,
         // we directly conclude that the fcArc is counter-clockwise if the apn is directed towards us
         //if (fcArc.Length < 2 * Math.PI*radius) return new (Math.Abs (angleUptoPt), EArcSense.CCW);
         if (Geom.Cross (arcDirFromStartPt, scVec).Normalized ().Aligned (normal)) {
            if (hintSense == EArcSense.Infer)
               return new (Math.Abs (angle), EArcSense.CCW);
            else
               return new (Math.Abs (angle), hintSense);
         } else {
            if (hintSense == EArcSense.Infer)
               return new (-Math.Abs (angle), EArcSense.CW);
            else
               return new (-Math.Abs (angle), hintSense);
         }
      }
      EArcSense sense;

      // To distinguish between CW and CCW, we need the cross product
      Vector3 sXe = Geom.Cross (vecStart, vecEnd).Normalized ();
      if (sXe.Length.EQ (0)) {
         angle = 0;
         if (fcArc.Length > Math.PI * radius) sense = EArcSense.CW;
         else sense = EArcSense.CCW;
         return new Tuple<double, EArcSense> (angle, sense);
      }

      // Determine the sense by comparing the cross product with the normal
      var L = fcArc.Length;
      EArcSense arcSense = EArcSense.CCW;
      bool fullArc = false;
      if (start.DistTo (fcArc.Start).EQ (0) && end.DistTo (fcArc.End).EQ (0)) fullArc = true;
      if (!fullArc) (_, arcSense) = GetArcAngleAndSense (fcArc, normal, hintSense);
      if (hintSense != EArcSense.Infer) {
         bool isMajorArc = L.SGT (Math.PI * radius);
         angle = includedAngle * (hintSense == EArcSense.CCW ? 1 : -1);

         if (isMajorArc)
            angle = (Math.Tau - includedAngle) * (hintSense == EArcSense.CCW ? 1 : -1);

         sense = hintSense;
      }
      //if (hintSense != EArcSense.Infer) {
      //   bool majorArc = L.SGT(Math.PI * radius);
      //   bool minorArc = !majorArc;
      //   if (minorArc && hintSense == EArcSense.CCW)
      //      angle = includedAngle;
      //   else if (minorArc && hintSense == EArcSense.CW)
      //      angle = -includedAngle;
      //   else if (majorArc && hintSense == EArcSense.CCW)
      //      angle = Math.Tau - includedAngle;
      //   else if (majorArc && hintSense == EArcSense.CW)
      //      angle = -(Math.Tau - includedAngle);
      //   else
      //      throw new Exception ("Arc sense and type not in valid cases");
      //   //if (L > Math.PI * radius) { // Major fcArc
      //   //   if (hintSense == EArcSense.CCW)
      //   //      angle = Math.Tau - includedAngle;
      //   //   else // CW
      //   //      angle = -includedAngle;
      //   //} else { // Minor fcArc
      //   //   if (hintSense == EArcSense.CCW)
      //   //      angle = includedAngle;
      //   //   else // CW
      //   //      angle = -(Math.Tau - includedAngle);
      //   //}
      //   sense = hintSense;
      //} 
      else {// if (hintSense == EArcSense.Infer) {
         if (sXe.Dot (normal) < 0.0 && L > Math.PI * radius) {
            angle = 2 * Math.PI - includedAngle;
            sense = EArcSense.CCW;
            if (sense != arcSense && !fullArc) {
               angle = -includedAngle;
               sense = EArcSense.CW;
            }
         } else if (sXe.Dot (normal) > 0.0 && L > Math.PI * radius) {
            angle = -(2 * Math.PI - includedAngle);
            sense = EArcSense.CW;
            if (sense != arcSense && !fullArc) {
               angle = includedAngle;
               sense = EArcSense.CCW;
            }
         } else if (sXe.Dot (normal) > 0 && L < Math.PI * radius) {
            sense = EArcSense.CCW;
            if (sense != arcSense && !fullArc) {
               angle = -(2 * Math.PI - includedAngle);
               sense = EArcSense.CW;
            }
         } else if (sXe.Dot (normal) < 0 && L < Math.PI * radius) {
            angle = -includedAngle;
            sense = EArcSense.CW;
            if (sense != arcSense && !fullArc) {
               angle = 2 * Math.PI - includedAngle;
               sense = EArcSense.CCW;
            }
         } else throw new Exception ("In GetArcAngleAndSense: Semicircular fcArc case not properly handled");
      }
      return new Tuple<double, EArcSense> (angle, sense);
   }

   //public static Tuple<double, EArcSense> GetArcAngleAndSense (Arc3 arc, Point3 start, Point3 end, Vector3 normal, EArcSense hintSense) {
   //   if (arc == null) throw new Exception ("Geom.GetArcAngleAndSense: arc is null");
   //   var (cen, _) = EvaluateCenterAndRadius (arc);

   //   if (Utils.IsCircle (arc)) {
   //      if (arc.Start.EQ (start) && arc.End.EQ (end)) {
   //         if (hintSense == EArcSense.Infer)
   //            return new Tuple<double, EArcSense> (Math.PI * 2, EArcSense.CCW);
   //         else
   //            return new Tuple<double, EArcSense> (Math.PI * 2, hintSense);
   //      }
   //      if (!IsPointOnCurve (arc as Curve3, start, normal))
   //         throw new Exception ("In GetArcAngleAndSense: For the circle, the start point is not on the circle");
   //      if (!IsPointOnCurve (arc as Curve3, end, normal))
   //         throw new Exception ("In GetArcAngleAndSense: For the circle, the end point is not on the circle");
   //      var cenToStart = start - cen; var cenToEnd = end - cen;
   //      var crossP = Geom.Cross (cenToStart, cenToEnd).Normalized ();
   //      var angBet = Math.Acos (cenToStart.Normalized ().Dot (cenToEnd.Normalized ()));
   //      if (crossP.Opposing (normal)) angBet = 2 * Math.PI - angBet;
   //      if (hintSense == EArcSense.Infer)
   //         return new Tuple<double, EArcSense> (angBet, EArcSense.CCW);
   //      else
   //         return new Tuple<double, EArcSense> (angBet, hintSense);
   //   } else if (arc.Start.EQ (start) && arc.Start.EQ (end) || arc.Start.EQ (end) && arc.Start.EQ (start)) {
   //      // If the start or end point provided is the start point of the arc itself, no need to compute
   //      // and return the 0 angle.
   //      if (hintSense == EArcSense.Infer)
   //         return new Tuple<double, EArcSense> (0, EArcSense.CCW);
   //      else
   //         return new Tuple<double, EArcSense> (0, hintSense);
   //   }

   //   // Compute the vectors from center to start and center to end
   //   normal = normal.Normalized ();
   //   var (center, radius) = EvaluateCenterAndRadius (arc);
   //   Vector3 vecStart = (start - center).Normalized ();
   //   Vector3 vecEnd = (end - center).Normalized ();

   //   // Calculate the angle between the two vectors using the dot product
   //   double dot = vecStart.Dot (vecEnd).Clamp (-0.9999999999999999999999, 0.999999999999999999999999999);
   //   double includedAngle = Math.Acos (dot);
   //   var angle = includedAngle;

   //   if (Math.Abs (Math.Abs (angle) - Math.PI) < 1e-5) {
   //      var arcDirFromStartPt = (arc.Evaluate (0.1) - arc.Start).Normalized ();
   //      var scVec = (center - start).Normalized ();

   //      // There is a finite difference in the arc length if the arc is semicircular. Instead of
   //      // the length being exactly PI*radius, in many cases, it is little lesser. In those cases,
   //      // we directly conclude that the arc is counter-clockwise if the apn is directed towards us
   //      //if (arc.Length < 2 * Math.PI*radius) return new (Math.Abs (angleUptoPt), EArcSense.CCW);
   //      if (Geom.Cross (arcDirFromStartPt, scVec).Normalized ().Aligned (normal)) {
   //         if (hintSense == EArcSense.Infer)
   //            return new (Math.Abs (angle), EArcSense.CCW);
   //         else
   //            return new (Math.Abs (angle), hintSense);
   //      } else {
   //         if (hintSense == EArcSense.Infer)
   //            return new (-Math.Abs (angle), EArcSense.CW);
   //         else
   //            return new (-Math.Abs (angle), hintSense);
   //      }
   //   }
   //   EArcSense sense;

   //   // To distinguish between CW and CCW, we need the cross product
   //   Vector3 sXe = Geom.Cross (vecStart, vecEnd).Normalized ();
   //   if (sXe.Length.EQ (0)) {
   //      angle = 0;
   //      if (arc.Length > Math.PI * radius) sense = EArcSense.CW;
   //      else sense = EArcSense.CCW;
   //      return new Tuple<double, EArcSense> (angle, sense);
   //   }

   //   // Determine the sense by comparing the cross product with the normal
   //   var L = arc.Length;
   //   EArcSense arcSense = EArcSense.CCW;
   //   bool fullArc = false;
   //   if (start.DistTo (arc.Start).EQ (0) && end.DistTo (arc.End).EQ (0)) fullArc = true;
   //   if (!fullArc) (_, arcSense) = GetArcAngleAndSense (arc, normal, hintSense);
   //   if (hintSense != EArcSense.Infer) {
   //      bool isMajorArc = L.SGT (Math.PI * radius);
   //      angle = includedAngle * (hintSense == EArcSense.CCW ? 1 : -1);

   //      if (isMajorArc)
   //         angle = (Math.Tau - includedAngle) * (hintSense == EArcSense.CCW ? 1 : -1);

   //      sense = hintSense;
   //   }
   //   //if (hintSense != EArcSense.Infer) {
   //   //   bool majorArc = L.SGT(Math.PI * radius);
   //   //   bool minorArc = !majorArc;
   //   //   if (minorArc && hintSense == EArcSense.CCW)
   //   //      angle = includedAngle;
   //   //   else if (minorArc && hintSense == EArcSense.CW)
   //   //      angle = -includedAngle;
   //   //   else if (majorArc && hintSense == EArcSense.CCW)
   //   //      angle = Math.Tau - includedAngle;
   //   //   else if (majorArc && hintSense == EArcSense.CW)
   //   //      angle = -(Math.Tau - includedAngle);
   //   //   else
   //   //      throw new Exception ("Arc sense and type not in valid cases");
   //   //   //if (L > Math.PI * radius) { // Major arc
   //   //   //   if (hintSense == EArcSense.CCW)
   //   //   //      angle = Math.Tau - includedAngle;
   //   //   //   else // CW
   //   //   //      angle = -includedAngle;
   //   //   //} else { // Minor arc
   //   //   //   if (hintSense == EArcSense.CCW)
   //   //   //      angle = includedAngle;
   //   //   //   else // CW
   //   //   //      angle = -(Math.Tau - includedAngle);
   //   //   //}
   //   //   sense = hintSense;
   //   //} 
   //   else {// if (hintSense == EArcSense.Infer) {
   //      if (sXe.Dot (normal) < 0.0 && L > Math.PI * radius) {
   //         angle = 2 * Math.PI - includedAngle;
   //         sense = EArcSense.CCW;
   //         if (sense != arcSense && !fullArc) {
   //            angle = -includedAngle;
   //            sense = EArcSense.CW;
   //         }
   //      } else if (sXe.Dot (normal) > 0.0 && L > Math.PI * radius) {
   //         angle = -(2 * Math.PI - includedAngle);
   //         sense = EArcSense.CW;
   //         if (sense != arcSense && !fullArc) {
   //            angle = includedAngle;
   //            sense = EArcSense.CCW;
   //         }
   //      } else if (sXe.Dot (normal) > 0 && L < Math.PI * radius) {
   //         sense = EArcSense.CCW;
   //         if (sense != arcSense && !fullArc) {
   //            angle = -(2 * Math.PI - includedAngle);
   //            sense = EArcSense.CW;
   //         }
   //      } else if (sXe.Dot (normal) < 0 && L < Math.PI * radius) {
   //         angle = -includedAngle;
   //         sense = EArcSense.CW;
   //         if (sense != arcSense && !fullArc) {
   //            angle = 2 * Math.PI - includedAngle;
   //            sense = EArcSense.CCW;
   //         }
   //      } else throw new Exception ("In GetArcAngleAndSense: Semicircular arc case not properly handled");
   //   }
   //   return new Tuple<double, EArcSense> (angle, sense);
   //}

   /// <summary>
   /// This method returns the evaluated point on the arc at an angle FROM the start point
   /// of the arc. 
   /// </summary>
   /// <param name="arc">The input arc</param>
   /// <param name="angleFromStPt">The angle from the start point of the arc</param>
   /// <param name="apn">The arc plane normal that should be obtained from the tooling</param>
   /// <returns>The point (type Point3) on the Arc that is "angleFromStPt" from the 
   /// start point of the arc</returns>
   public static Point3 GetArcPointAtAngle (FCArc3 fcArc, double angleFromStPt, Vector3 apn) {
      (_, var radius) = EvaluateCenterAndRadius (fcArc);
      XForm4 transform = GetArcCS (fcArc, apn);
      var ptAtAngle = new Point3 (radius * Math.Cos (angleFromStPt), radius * Math.Sin (angleFromStPt), 0.0);
      ptAtAngle = Geom.V2P (transform * ptAtAngle);
      return ptAtAngle;
   }

   /// <summary>
   /// This method returns two intermediate arc points on an "arc" 
   /// between fromPt to toPoint WRT the arc plane normal
   /// </summary>
   /// <param name="arc">The input arc</param>
   /// <param name="fromPt">The from point on the arc after which the intermediate
   /// points should be computed</param>
   /// <param name="toPoint">The to point before which the intermediate points should
   /// be computed</param>
   /// <param name="planeNormal">The plane normal which should be obtained from the tooling
   /// Important note: The plane normal is a very local phenomenon and shall only be obtained
   /// from the segment level and not from the tooling level.</param>
   /// <returns>Returns two intermediate points from "fromPt" and "toPoint" WRT 
   /// Arc Plane Normal in the direction of the sense of the arc</returns>
   public static List<Point3> GetTwoIntermediatePoints (FCArc3 fcArc, Point3 fromPt, Point3 toPoint, Vector3 planeNormal,
      EArcSense hintSense, double tolerance = 1e-6) {
      if (!IsPointOnCurve (fcArc, fromPt, planeNormal, hintSense: hintSense, tolerance) || !IsPointOnCurve (fcArc, toPoint, planeNormal, hintSense: hintSense, tolerance))
         throw new InvalidOperationException ("The point is not on the arc");
      var angDataFromPt = GetArcAngleAtPoint (fcArc, fromPt, planeNormal, hintSense, tolerance);
      var angDataToPt = GetArcAngleAtPoint (fcArc, toPoint, planeNormal, hintSense, tolerance);
      var deltaAngle = angDataToPt.Item1 - angDataFromPt.Item1;
      if (Utils.IsCircle (fcArc)) {
         //By default all circles are CCW in sense. If angleDataToPt is less than
         // PI radians, the major arc angle is compiuted
         if (deltaAngle.SLT (Math.PI))
            deltaAngle = Math.PI * 2.0 - deltaAngle;
      }
      List<Point3> points = [];
      points.Add (GetArcPointAtAngle (fcArc, angDataFromPt.Item1 + deltaAngle / 4.0, planeNormal));
      points.Add (GetArcPointAtAngle (fcArc, angDataFromPt.Item1 + deltaAngle * (3.0 / 4.0), planeNormal));
      return points;
   }

   /// <summary>
   /// This method computes the mid point of the Arc segment
   /// </summary>
   /// <param name="arc">The input arc segment</param>
   /// <param name="apn">The arc plane normal that should be obtained from tooling</param>
   /// <returns>The mid point of the arc segment</returns>
   /// <exception cref="Exception">If the arc or arc plane normal is null or if the arc is actually a circle</exception>
   public static Point3 GetMidPoint (FCArc3 fcArc, Vector3? apn) {
      if (apn == null) throw new Exception ("Arc plane normal is null");
      if (fcArc == null) throw new Exception ("Arc is null ");
      var arcAngData = GetArcAngleAndSense (fcArc, apn.Value);
      var mpAngle = arcAngData.Item1 / 2.0;
      return GetArcPointAtAngle (fcArc, mpAngle, apn.Value);
   }

   /// <summary>
   /// Computes a new point after the end point on the arc at a distance specified
   /// </summary>
   /// <param name="arc">The input arc</param>
   /// <param name="incrementDist">An incremental distance after the end point of the arc</param>
   /// <returns>Point3 which is at a distance "incrementDist" from the end point of the arc</returns>
   /// /// <exception cref="Exception">If the arc is null or if the arc is actually a circle</exception>
   public static Point3 GetNewEndPointOnArcAtIncrement (FCArc3 fcArc, double incrementDist, Vector3 apn) {
      if (fcArc == null) throw new Exception ("Arc plane normal is null");
      XForm4 transform = GetArcCS (fcArc, apn);
      var arcAngle = GetArcAngleAndSense (fcArc, apn);
      (_, var radius) = EvaluateCenterAndRadius (fcArc);
      var newAngle = (incrementDist + radius * arcAngle.Item1) / radius;
      var newEndPointOnArcAtIncrement = new Point3 (radius * Math.Cos (newAngle), radius * Math.Sin (newAngle), 0.0);
      newEndPointOnArcAtIncrement = Geom.V2P (transform * newEndPointOnArcAtIncrement);
      return newEndPointOnArcAtIncrement;
   }

   /// <summary>
   /// This method returns an arbitrary normal to the plane containing the 
   /// arc, considering at random two points other than start point. 
   /// </summary>
   /// <param name="arc"></param>
   /// <returns>A vector3 which is an arbitrary normal</returns>
   /// <caveat>
   /// This normal need not conform to the E3Flex and E3Plane
   /// normals' expectations. This normal is only to be used for computation. Once the Flux.API 
   /// issues are resolved, this method shall be used. Currently thsi method is used where the 
   /// direction of the normal does not matter</caveat>
   public static Vector3 GetArcPlaneNormal (FCArc3 arc) {
      Point3 P1 = (arc as FCCurve3).Start, P2 = (arc as FCCurve3).Curve.Evaluate (0.3), P3 = (arc as FCCurve3).Curve.Evaluate (0.7);
      Vector3 P1P2 = P2 - P1; Vector3 P2P3 = P3 - P2;
      Vector3 arcNormal = Geom.Cross (P1P2, P2P3).Normalized ();
      return arcNormal;
   }

   public static Vector3 GetArcPlaneNormal (Point3 P1, Point3 P2, Point3 P3) {
      Vector3 P1P2 = P2 - P1; Vector3 P2P3 = P3 - P2;
      Vector3 arcNormal = Geom.Cross (P1P2, P2P3).Normalized ();
      return arcNormal;
   }

   /// <summary>
   /// This method computes the center and radius of the arc in 3d.
   /// </summary>
   /// <param name="arc"></param>
   /// <returns>Tuple of Center (type Point3),Radius( type double)</returns>
   /// <exception cref="InvalidCastException"></exception>
   public static Tuple<Point3, double> EvaluateCenterAndRadius (FCArc3 arc) {
      Tuple<Point3, double> res;
      // It is assumed that the Arc is the only curve, only then a circle could be a 
      // feature
      if (arc == null)
         throw new InvalidCastException ("The curve is null");
      Point3 P1 = (arc as FCCurve3).Start, P2 = (arc as FCCurve3).Curve.Evaluate(0.3), P3 = (arc as FCCurve3).Curve.Evaluate (0.7);
      Vector3 P1P2 = P2 - P1; Vector3 P2P3 = P3 - P2;
      var apn = GetArcPlaneNormal (arc);
      Point3 M12 = (P1 + P2) * 0.5; Point3 M23 = (P2 + P3) * 0.5;
      Vector3 R12 = Geom.Cross (P1P2, apn).Normalized (); Vector3 R23 = Geom.Cross (P2P3, apn).Normalized ();
      Point3 M12Delta = M12 + R12 * 20.0; Point3 M23Delta = M23 + R23 * 20.0;
      (var center, _) = GetIntersectingPointBetLines (M12, M12Delta, M23, M23Delta, false);
      var radius = center.DistTo (P1);
      res = new Tuple<Point3, double> (center, radius);
      return res;
   }


   //public static Tuple<Point3, double> EvaluateCenterAndRadius (FCArc3 arc) {
   //   Tuple<Point3, double> res;
   //   // It is assumed that the Arc is the only curve, only then a circle could be a 
   //   // feature
   //   if (arc == null)
   //      throw new InvalidCastException ("The curve is null");
   //   res = new Tuple<Point3, double> (arc.Center, arc.Radius);
   //   return res;
   //}

   public static Tuple<Point3, double> EvaluateCenterAndRadius (Point3 sp, Point3 ip1, Point3 ip2) {
      Tuple<Point3, double> res;
      // It is assumed that the Arc is the only curve, only then a circle could be a
      // feature
      Vector3 P1P2 = ip1 - sp; Vector3 P2P3 = ip2 - ip1;
      var apn = GetArcPlaneNormal (sp, ip1, ip2);
      Point3 M12 = (sp + ip1) * 0.5; Point3 M23 = (ip1 + ip2) * 0.5;
      Vector3 R12 = Geom.Cross (P1P2, apn).Normalized (); Vector3 R23 = Geom.Cross (P2P3, apn).Normalized ();
      Point3 M12Delta = M12 + R12 * 20.0; Point3 M23Delta = M23 + R23 * 20.0;
      (var center, _) = GetIntersectingPointBetLines (M12, M12Delta, M23, M23Delta, false);
      var radius = center.DistTo (sp);
      res = new Tuple<Point3, double> (center, radius);
      return res;
   }

   public static bool AreCoplanar (
    Point3 p0,
    Point3 p1,
    Point3 p2,
    Point3 p3,
    Vector3 pNormal,
    double tol = 1e-6) {
      if (pNormal.Length < tol)
         throw new ArgumentException ("Plane normal is zero length");

      Vector3 n = pNormal.Normalized ();

      double d1 = (p1 - p0).Dot (n);
      double d2 = (p2 - p0).Dot (n);
      double d3 = (p3 - p0).Dot (n);

      return
          d1.EQ (0.0, tol) &&
          d2.EQ (0.0, tol) &&
          d3.EQ (0.0, tol);
   }
   /// <summary>
   /// This method splits the given arc in to N+1 arcs, given "N" points
   /// in between the start and end point of the arc.
   /// Caveat: The "N" intermediate points should be in the same order 
   /// from start point to the end point of the arc
   /// </summary>
   /// <param name="arc">Arc tos split</param>
   /// <param name="interPointsList"> N intermediate points on the given arc</param>
   /// <param name="deltaBetweenArcs">The distance along the arc from an arc's end point 
   /// to the next arc's start point of the split arcs</param>
   /// <returns>List of split Arcs (Arc3)</returns>
   //public static List<FCArc3> SplitArc (FCArc3 fcArc, List<Point3> interPointsList,
   //   double deltaBetweenArcs, Vector3 apn, EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6) {
   //   List<FCArc3> splitArcs = [];
   //   List<Point3> points = [];
   //   points.Add (fcArc.Start); points.AddRange (interPointsList);
   //   points.Add (fcArc.End);
   //   if (points.Count > 2) {
   //      Point3 newIncrStPt = points[0];
   //      for (int ii = 0; ii < points.Count - 1; ii++) {
   //         List<Point3> twoIntermediatePoints = GetTwoIntermediatePoints (fcArc, newIncrStPt, points[ii + 1], apn, hintSense, tolerance);
   //         if (twoIntermediatePoints.Count == 0)
   //            continue;
   //         // Nidge intermediate points
   //         var (cen, rad) = EvaluateCenterAndRadius (fcArc);
   //         for (int jj = 0; jj < 2; jj++) {
   //            var p = twoIntermediatePoints[jj];
   //            var np = NudgePointToArc (cen, rad, p, apn);
   //            if (!Geom.IsPointOnCurve (fcArc as FCCurve3, np, apn, hintSense))
   //               throw new Exception ("In SplitArc: nudged point is not on the arc with in 1e-6");
   //            twoIntermediatePoints[jj] = np;
   //         }
   //         var arc1 = new FCArc3 (newIncrStPt, twoIntermediatePoints[0], twoIntermediatePoints[1], points[ii + 1], apn);
   //         splitArcs.Add (arc1);
   //         newIncrStPt = GetNewEndPointOnArcAtIncrement (splitArcs[^1], deltaBetweenArcs, apn);
   //      }
   //   } else splitArcs.Add (fcArc);
   //   return splitArcs;
   //}

   public static List<FCArc3> SplitArc (FCArc3 fcArc, List<Point3> interPointsList,
      double deltaBetweenArcs, Vector3 apn, EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6) {
      return fcArc.SplitArc (interPointsList, deltaBetweenArcs);
   }


   /// <summary>
   /// This method creates a new 3d arc given the following parameters.
   /// The algorithm: A 2d arc with center mapped to origin with radius
   /// as dist from center to start point is created. A local coordinate
   /// system is computed with X direction as the center to start, Z direction
   /// as the arc plane normal, and Y axis as the Cross product between Z and X.
   /// A sequence of two points on the arc is created at random locations. 
   /// These sequence of random points between the arc angle are then transformed 
   /// to the current 3d coordinates. The Arc3 is subsequently created
   /// </summary>
   /// <param name="stPoint">The start point on the arc</param>
   /// <param name="endPoint">The end point on the arc</param>
   /// <param name="center">The center of the arc</param>
   /// <param name="arcPlaneNormal">The arc plane normal that should be obtained from
   /// the tooling</param>
   /// <param name="sense">The sense of the arc CW or CCW WRT the arc plane normal
   /// emanating from, towards the observer</param>
   /// <returns>The created arc of type Arc3</returns>
   public static FCArc3 CreateArc (Point3 stPoint, Point3 endPoint, Point3 center, Vector3 arcPlaneNormal,
      EArcSense sense) {
      var radius = (stPoint - center).Length;

      // Set the local coordinate system of the arc
      var xAxis = (stPoint - center).Normalized ();
      var zAxis = arcPlaneNormal.Normalized ();
      var yAxis = Geom.Cross (zAxis, xAxis).Normalized ();
      XForm4 arcTransform = new ();
      arcTransform.SetRotationComponents (xAxis, yAxis, zAxis);
      arcTransform.SetTranslationComponent (Geom.P2V (center));

      // Find the angle at which the end point of the arc exists
      var endPtLocalCS = arcTransform.InvertNew () * endPoint;
      double arcAngle = Math.Atan2 (endPtLocalCS.Y, endPtLocalCS.X);
      if (arcAngle > 0 && sense == EArcSense.CW) arcAngle = Math.PI * 2.0 - arcAngle;
      if (arcAngle < 0 && sense == EArcSense.CCW) arcAngle = Math.PI * 2.0 + arcAngle;

      // Generate two extra points in between the start and end point of the arc 
      // to be created, sequentially.
      var angle2ndRandom = RandomNumberWithin (0, arcAngle);
      Point3 some2ndPoint = new (radius * Math.Cos (angle2ndRandom), radius * Math.Sign (angle2ndRandom), 0.0);
      some2ndPoint = Geom.V2P (arcTransform * some2ndPoint);
      var angle3rdRandom = RandomNumberWithin (angle2ndRandom, arcAngle);
      Point3 some3rdPoint = new (radius * Math.Cos (angle3rdRandom), radius * Math.Sign (angle3rdRandom), 0.0);
      some3rdPoint = Geom.V2P (arcTransform * some3rdPoint);

      // Create the arc
      FCArc3 arc = new (stPoint, some2ndPoint, some3rdPoint, endPoint, arcPlaneNormal);
      return arc;
   }

   #endregion Methods for 3D Arc

   #region Methods for generic Curve ( Arc or Line in 3D )
   /// <summary>
   /// This method is a wrapper to the Evaluate() of Arc and line. 
   /// </summary>
   /// <param name="crv">The curve which shall be FCArc3 or FCLine3 type</param>
   /// <param name="param">A parameter from 0 to 1</param>
   /// <param name="apn">Arc plane normal that shall be provided by the tooling</param>
   /// <returns>The evaluated point of type Point3</returns>
   /// <exception cref="Exception">An exception is thrown if arc plane normal is not
   /// provided</exception>
   public static Point3 Evaluate (FCCurve3 crv, double param, Vector3? apn) {
      if (crv is FCArc3 arc) {
         if (apn == null) throw new Exception ("Arc plane normal cant be null");
         return EvaluateArc (arc, param, apn.Value);
      } else return EvaluateLine (crv as FCLine3, param);
   }

   /// <summary>
   /// This method returns the arc length parameter ( 0 to 1 ) at a point on the curve 
   /// between start and end.
   /// </summary>
   /// <param name="crv">The input curve, which shall be Line3 or Arc3</param>
   /// <param name="pt">The input point at which the parameter should be evaluated</param>
   /// <param name="apn">The arc plane normal which should be provided by the tooling</param>
   /// <returns>The parameter for the point pt [0,1]</returns>
   /// <exception cref="ArgumentNullException">This exception is thrown if input curve is null</exception>
   /// <exception cref="Exception">This exception occurs if either the given point is not on the curve OR
   /// if there is an inconsistency in the parameter computed for the line</exception>
   public static double GetParamAtPoint (FCCurve3 fcCrv, Point3 pt, Vector3? apn, double tolerance = 1e-6) {
      if (fcCrv == null || apn == null) throw new Exception ("Curve/arc plane normal is null");
      if (!IsPointOnCurve (fcCrv, pt, apn.Value, tolerance: tolerance))
         throw new Exception ("The Point is not on the curve");

      if (fcCrv is FCArc3) {
         if (apn == null) throw new Exception ("Arc Plane Normal needed");
         var (arcAngle, _) = GetArcAngleAndSense (fcCrv as FCArc3, apn.Value);
         var (arcAngleAtPt, _) = GetArcAngleAtPoint (fcCrv as FCArc3, pt, apn.Value, EArcSense.Infer, tolerance);
         return arcAngleAtPt / arcAngle;
      } else {
         double denomX = (fcCrv.End.X - fcCrv.Start.X);
         double denomY = (fcCrv.End.Y - fcCrv.Start.Y);
         double denomZ = (fcCrv.End.Z - fcCrv.Start.Z);
         double? t1 = null, t2 = null, t3 = null;
         if (Math.Abs (denomX) > 1e-6) t1 = (pt.X - fcCrv.Start.X) / denomX;
         if (Math.Abs (denomY) > 1e-6) t2 = (pt.Y - fcCrv.Start.Y) / denomY;
         if (Math.Abs (denomZ) > 1e-6) t3 = (pt.Z - fcCrv.Start.Z) / denomZ;
         // Handle all possible cases
         if (t1.HasValue && t2.HasValue && t3.HasValue) return (t1.Value + t2.Value + t3.Value) / 3.0;
         else if (t1.HasValue && t2.HasValue) return (t1.Value + t2.Value) / 2.0;
         else if (t1.HasValue && t3.HasValue) return (t1.Value + t3.Value) / 2.0;
         else if (t2.HasValue && t3.HasValue) return (t2.Value + t3.Value) / 2.0;
         else if (t1.HasValue) return t1.Value;
         else if (t2.HasValue) return t2.Value;
         else if (t3.HasValue) return t3.Value;
         else throw new InvalidOperationException ("The given point does not lie on the curve.");
      }
      throw new Exception ("Geometric inconsistency error in parameter computation");
   }

   /// <summary>
   /// This method returns the mid point of the curve segment. This method is a 
   /// wrapper to the specific midPoint method of Arc3 or Line3
   /// </summary>
   /// <param name="curve">The input curve segment, which shall be Arc3 or Line3</param>
   /// <param name="apn">ARc plane normal which shall be provided by the tooling</param>
   /// <returns>The mid point of the curve segment</returns>
   public static Point3 GetMidPoint (FCCurve3 fcCrv, Vector3? apn) {
      if (fcCrv is FCLine3 fcLine) return (fcLine.Start + fcLine.End) * 0.5;
      else return GetMidPoint (fcCrv as FCArc3, apn);
   }

   /// <summary>
   /// This method checks if any given 3D point lies on the parametric curve segment including 
   /// a tolerance. The supported curves are Line3 and Arc3</summary>
   /// <param name="curve">The input arc or line</param>
   /// <param name="pt">The given point</param>
   /// <param name="constrainedWithinSegment">Flag to check within the segment of the curve. True by default</param>
   /// <param name="apn">The arc plane normal that should be obtained from the tooling</param>
   /// <returns>Boolean if the given point lies on the curve</returns>
   /// <exception cref="Exception">If the input curve is null or if the arc plane normal
   /// is not provided</exception>
   public static bool IsPointOnCurve (FCCurve3 fcCurve, Point3 pt, Vector3? apn,
      EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6, bool constrainedWithinSegment = true) {
      if (fcCurve == null) throw new Exception ("The fcCurve passed is null");
      if (fcCurve.Start.EQ (pt, tolerance) || fcCurve.End.EQ (pt, tolerance)) return true;
      if (fcCurve is FCArc3) {
         if (apn == null) throw new Exception ("Arc plane normal is null");
         var arc = fcCurve as FCArc3;
         (var center, var radius) = Geom.EvaluateCenterAndRadius (arc);

         // The given point pt should be having radius distance from the center
         var ptToCenVec = center - pt;
         var ptToCenDir = ptToCenVec.Normalized ();
         var dotp = apn.Value.Dot (ptToCenDir);

         // Check for planarity
         if (!(ptToCenVec.Length - radius).EQ (0.0, tolerance) || !Math.Abs (dotp).EQ (0.0, tolerance)) return false;

         // Check for circle
         if (Utils.IsCircle (fcCurve as FCArc3)) {
            var x = pt.X; var y = pt.Y; var z = pt.Z;
            var xc = center.X; var yc = center.Y; var zc = center.Z;
            if (((x - xc) * (x - xc) + (y - yc) * (y - yc) + (z - zc) * (z - zc)).EQ (radius * radius, tolerance)) return true;
            else return false;
         }

         // Check for arc
         if (constrainedWithinSegment) {
            var arcAngle = GetArcAngleAndSense (arc, apn.Value, hintSense);
            var arcAngleFromStToPt = GetArcAngleAtPoint (arc, pt, apn.Value, hintSense, tolerance);
            var param = arcAngleFromStToPt.Item1 / arcAngle.Item1;
            if (param.LieWithin (0, 1, 1e-5)) return true;
            return false;
         }
         return true;
      } else {
         var line = fcCurve as FCLine3;
         var startPoint = line.Start; var endPoint = line.End;
         // Calculate direction vector of the line
         var direction = endPoint - startPoint;

         // Vector from startPoint to pt
         var toPoint = pt - startPoint;

         // Check for coplanarity (using the cross product)
         var crossProduct = Geom.Cross (direction, toPoint);

         // Magnitude squared of the cross product (to avoid expensive sqrt)
         double crossProductLengthSquared =
             crossProduct.X * crossProduct.X +
             crossProduct.Y * crossProduct.Y +
             crossProduct.Z * crossProduct.Z;

         var epsilon = 1e-6;
         // If cross product is close to zero, the points are collinear
         if (crossProductLengthSquared > epsilon * epsilon) {
            return false; // Not coplanar (or collinear in 3D)
         }

         // Check for degenerate line
         var lengthSquared = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
         if (lengthSquared < epsilon) {
            // Line segment is effectively a point; check if pt matches startPoint
            //return Math.Abs (pt.X - startPoint.X) < epsilon &&
            //       Math.Abs (pt.Y - startPoint.Y) < epsilon &&
            //       Math.Abs (pt.Z - startPoint.Z) < epsilon;
            throw new Exception ("Degenerate line");

         }

         // Calculate the parameter t
         double t = ((pt.X - startPoint.X) * direction.X +
                     (pt.Y - startPoint.Y) * direction.Y +
                     (pt.Z - startPoint.Z) * direction.Z) / lengthSquared;

         // Check if t is within the extended range [-epsilon, 1+epsilon]
         if (t < -epsilon || t > 1 + epsilon) {
            return false;
         }

         // Calculate the closest point on the line
         var closestPoint = new Point3 (
             startPoint.X + t * direction.X,
             startPoint.Y + t * direction.Y,
             startPoint.Z + t * direction.Z
         );

         // Check if pt is close enough to the closestPoint
         return Math.Abs (pt.X - closestPoint.X) < epsilon &&
                Math.Abs (pt.Y - closestPoint.Y) < epsilon &&
                Math.Abs (pt.Z - closestPoint.Z) < epsilon;
      }
      //var stToEndVec = line.End - line.Start; var stToPtVec = pt - line.Start;
      //var cp = Geom.Cross (stToPtVec.Normalized (), stToEndVec.Normalized ());
      //var cpv = cp.Normalized ();
      //if (!cp.Length.EQ (0.0, tolerance))
      //   return false;
      //var param = stToPtVec.Dot (stToEndVec) / (stToEndVec.Dot (stToEndVec));
      //if (constrainedWithinSegment) {
      //   if (param.LieWithin (0, 1)) return true;
      //} else return true;

      //return false;
   }

   //public static bool IsPointOnCurve (Curve3 curve, Point3 pt, Vector3? apn,
   //   EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6, bool constrainedWithinSegment = true) {
   //   if (curve == null) throw new Exception ("The curve passed is null");
   //   if (curve.Start.EQ (pt, tolerance) || curve.End.EQ (pt, tolerance)) return true;
   //   if (curve is Arc3) {
   //      if (apn == null) throw new Exception ("Arc plane normal is null");
   //      var arc = curve as FCArc3;
   //      (var center, var radius) = Geom.EvaluateCenterAndRadius (arc);

   //      // The given point pt should be having radius distance from the center
   //      var ptToCenVec = center - pt;
   //      var ptToCenDir = ptToCenVec.Normalized ();
   //      var dotp = apn.Value.Dot (ptToCenDir);

   //      // Check for planarity
   //      if (!(ptToCenVec.Length - radius).EQ (0.0, tolerance) || !Math.Abs (dotp).EQ (0.0, tolerance)) return false;

   //      // Check for circle
   //      if (Utils.IsCircle (curve as FCArc3)) {
   //         var x = pt.X; var y = pt.Y; var z = pt.Z;
   //         var xc = center.X; var yc = center.Y; var zc = center.Z;
   //         if (((x - xc) * (x - xc) + (y - yc) * (y - yc) + (z - zc) * (z - zc)).EQ (radius * radius, tolerance)) return true;
   //         else return false;
   //      }

   //      // Check for arc
   //      if (constrainedWithinSegment) {
   //         var arcAngle = GetArcAngleAndSense (arc, apn.Value, hintSense);
   //         var arcAngleFromStToPt = GetArcAngleAtPoint (arc, pt, apn.Value, hintSense, tolerance);
   //         var param = arcAngleFromStToPt.Item1 / arcAngle.Item1;
   //         if (param.LieWithin (0, 1, 1e-5)) return true;
   //         return false;
   //      }
   //      return true;
   //   } else {
   //      var line = curve as Line3;
   //      var startPoint = line.Start; var endPoint = line.End;
   //      // Calculate direction vector of the line
   //      var direction = endPoint - startPoint;

   //      // Vector from startPoint to pt
   //      var toPoint = pt - startPoint;

   //      // Check for coplanarity (using the cross product)
   //      var crossProduct = Geom.Cross (direction, toPoint);

   //      // Magnitude squared of the cross product (to avoid expensive sqrt)
   //      double crossProductLengthSquared =
   //          crossProduct.X * crossProduct.X +
   //          crossProduct.Y * crossProduct.Y +
   //          crossProduct.Z * crossProduct.Z;

   //      var epsilon = 1e-6;
   //      // If cross product is close to zero, the points are collinear
   //      if (crossProductLengthSquared > epsilon * epsilon) {
   //         return false; // Not coplanar (or collinear in 3D)
   //      }

   //      // Check for degenerate line
   //      var lengthSquared = direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z;
   //      if (lengthSquared < epsilon) {
   //         // Line segment is effectively a point; check if pt matches startPoint
   //         //return Math.Abs (pt.X - startPoint.X) < epsilon &&
   //         //       Math.Abs (pt.Y - startPoint.Y) < epsilon &&
   //         //       Math.Abs (pt.Z - startPoint.Z) < epsilon;
   //         throw new Exception ("Degenerate line");

   //      }

   //      // Calculate the parameter t
   //      double t = ((pt.X - startPoint.X) * direction.X +
   //                  (pt.Y - startPoint.Y) * direction.Y +
   //                  (pt.Z - startPoint.Z) * direction.Z) / lengthSquared;

   //      // Check if t is within the extended range [-epsilon, 1+epsilon]
   //      if (t < -epsilon || t > 1 + epsilon) {
   //         return false;
   //      }

   //      // Calculate the closest point on the line
   //      var closestPoint = new Point3 (
   //          startPoint.X + t * direction.X,
   //          startPoint.Y + t * direction.Y,
   //          startPoint.Z + t * direction.Z
   //      );

   //      // Check if pt is close enough to the closestPoint
   //      return Math.Abs (pt.X - closestPoint.X) < epsilon &&
   //             Math.Abs (pt.Y - closestPoint.Y) < epsilon &&
   //             Math.Abs (pt.Z - closestPoint.Z) < epsilon;
   //   }
   //   //var stToEndVec = line.End - line.Start; var stToPtVec = pt - line.Start;
   //   //var cp = Geom.Cross (stToPtVec.Normalized (), stToEndVec.Normalized ());
   //   //var cpv = cp.Normalized ();
   //   //if (!cp.Length.EQ (0.0, tolerance))
   //   //   return false;
   //   //var param = stToPtVec.Dot (stToEndVec) / (stToEndVec.Dot (stToEndVec));
   //   //if (constrainedWithinSegment) {
   //   //   if (param.LieWithin (0, 1)) return true;
   //   //} else return true;

   //   //return false;
   //}

   public static Point3 NudgePointToArc (Point3 center, double radius, Point3 point, Vector3 normal) {
      // Compute the vector from the center to the point
      var nudgedPt = point;
      Vector3 centerToPoint = (nudgedPt - center).Normalized ();
      normal = normal.Normalized ();
      int cnt = 0;
      double origCosTheta, costheta;
      origCosTheta = costheta = centerToPoint.Dot (normal);

      while (Math.Abs (costheta) > 1e-6) {
         // Compute the projection of the point onto the arc plane
         nudgedPt = Geom.V2P (nudgedPt - Geom.V2P (normal) * costheta);

         centerToPoint = (nudgedPt - center).Normalized ();
         costheta = centerToPoint.Dot (normal);
         cnt++;
         if (cnt > 10000) break;
      }
      nudgedPt = center + centerToPoint * radius;

      // Check
      var c2p = (nudgedPt - center).Normalized ();
      var newCTheta = c2p.Dot (normal);
      if (Math.Abs (newCTheta).SGT (Math.Abs (origCosTheta))) throw new Exception ("Nudging the point on the arc failed");

      return nudgedPt;
   }

   /// <summary>
   /// This method is a wrapper to SplitLine and SplitArc methods
   /// </summary>
   /// <param name="curve">Curve to be split</param>
   /// <param name="interPointsList">Intermediate points prescription in the same order 
   /// from start to end of the curve. Any duplicate point(s) or points that are equal to start 
   /// and end of the curve will be removed</param>
   /// <param name="deltaBetween">This is a optional distance between two curves after split. The curve
   /// is split between start to end (int point). Next curve starts from (int point + DELTA)</param>
   /// <param name="fpn">This is the abbreviation for "feature plane normal", which means, the normal
   /// at this segment's locality</param>
   /// <returns>Returns the list of Curve3. If there is no need to split the curves, this returns
   /// the original curve itself</returns>
   public static List<FCCurve3> SplitCurve (FCCurve3 fcCrv, List<Point3> interPointsList, Vector3 fpn,
      double deltaBetween = 0.0, EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6) {
      var distinctInterPoints = interPointsList
            .Where ((p, index) => interPointsList.Take (index).All (p2 => p2.EQ (p) != true))
            .ToList ();
      distinctInterPoints.RemoveAll (p => p.EQ (fcCrv.Start) == true || p.EQ (fcCrv.End) == true);
      if (fcCrv is FCArc3) return [.. SplitArc (fcCrv as FCArc3, distinctInterPoints, deltaBetween, fpn, hintSense, tolerance).Select (cr => (cr as FCCurve3))];
      else return [.. SplitLine (fcCrv as FCLine3, distinctInterPoints, deltaBetween).Select (cr => (cr as FCCurve3))];
   }

   /// <summary>
   /// This method is used to split the given input curves
   /// </summary>
   /// <param name="curve">The input curve</param>
   /// <param name="interPointsDistances">The intermediate segment distances at which 
   /// the input curve shall be split. The last delta distance should not be provided.</param>
   /// <param name="fpn">The Feature Plane nOrmal, a local normal on which the curve exists (in the case of Arc3)</param>
   /// <param name="deltaBetween">This delta is used to give a gap between a split curve's end
   /// and the next split curve's start</param>
   /// <returns></returns>
   public static List<FCCurve3> SplitCurve (FCCurve3 curve, List<double> interPointsDistances, Vector3 fpn,
      double deltaBetween = 0.0) {
      List<FCCurve3> crvs = [];
      double totalGivenLengths = 0;
      interPointsDistances.Sum (item => totalGivenLengths += (item + deltaBetween));
      if (curve.Length < totalGivenLengths)
         return crvs;
      List<Point3> interPointsList = [];
      double cumulativeDist = 0;
      foreach (var dist in interPointsDistances) {
         cumulativeDist += dist;
         var pt = GetPointAtLengthFromStart (curve, fpn, cumulativeDist);
         interPointsList.Add (pt);
      }
      var distinctInterPoints = interPointsList
            .Where ((p, index) => interPointsList.Take (index).All (p2 => p2.EQ (p) != true))
            .ToList ();
      distinctInterPoints.RemoveAll (p => p.EQ (curve.Start) == true || p.EQ (curve.End) == true);
      if (curve is FCArc3) return [.. SplitArc (curve as FCArc3, distinctInterPoints, deltaBetween, fpn).Select (cr => (cr as FCCurve3))];
      else return [.. SplitLine (curve as FCLine3, distinctInterPoints, deltaBetween).Select (cr => (cr as FCCurve3))];
   }

   /// <summary>
   /// This method is used to get the reversed curve Curve3 (Line3 or Arc3)
   /// </summary>
   /// <param name="curve">The input Curve3 (Line3 or Arc3)</param>
   /// <param name="planeNormal">The plane normal that contains the curve. This is for Arc3</param>
   /// <returns>The curve that is a reverse of the input curve</returns>
   public static FCCurve3 GetReversedCurve (FCCurve3 fcCurve, Vector3 planeNormal, EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6) {
      //if (curve is FCArc3) {
      //   var arc = curve as FCArc3;
      //   //var intPoints = GetTwoIntermediatePoints (arc, arc.Start, arc.End, planeNormal, hintSense, tolerance);
      //   //FCArc3 reversedArc = new (curve.End, intPoints[1], intPoints[0], curve.Start, planeNormal);
      //   FCCurve3 reversedArc = fcCurve.ReverseClone ();
      //   return reversedArc as FCCurve3;
      //} else if (fcCurve is FCLine3) {
      //   FCLine3 ln = new (curve.End, curve.Start);
      //   return ln as FCCurve3;
      //}
      //return null;
      FCCurve3 reversedArc = fcCurve.ReverseClone ();
      return reversedArc as FCCurve3;
   }

   /// <summary>
   /// This method is used to clone the curve.
   /// </summary>
   /// <param name="curve">The input curve</param>
   /// <param name="planeNormal">The normal to the plane that constains the arc. 
   /// if it is not an arc, the plane normal is immaterial</param>
   /// <returns></returns>
   public static FCCurve3 CloneCurve (FCCurve3 curve, Vector3 planeNormal) {
      if (curve is FCArc3 arc) {
         var p1 = EvaluateArc (arc, 0.3, planeNormal); var p2 = EvaluateArc (arc, 0.9, planeNormal);
         //var intPoints = GetTwoIntermediatePoints (arc, arc.Start, arc.End, planeNormal);
         var clonedArc = new FCArc3 (arc.Start, p1, p2, arc.End, planeNormal);
         return clonedArc as FCCurve3;
      } else {
         return new FCLine3 (curve.Start, curve.End) as FCCurve3;
      }
   }

   public static FCCurve3 CloneCurve (FCCurve3 curve) => curve.Clone ();


   /// <summary>
   /// This is a utility method that returns a point from the start of the curve
   /// at a specific length of the curve. 
   /// </summary>
   /// <param name="curve">The supported curves are Arc3 and Line3</param>
   /// <param name="planeNormal">The plane normal is needed in the case of Arc3</param>
   /// <param name="length">The input length of the curve from the start of the curve.</param>
   /// <returns>The Point3 if the point exists on the curve and within the start and end of the curve</returns>
   /// <exception cref="Exception">If the degeneracies happen such as Length is negative, or 
   /// if the length is more than the length of the curve itself, exception is thrown.
   /// </exception>
   public static Point3 GetPointAtLengthFromStart (FCCurve3 fcCrv, Vector3 planeNormal, double length) {
      Point3 pointAtLengthFromStart;
      if (length < -1e-6) throw new Exception ("GetPointAtLengthFromStart: Length can not be less than zero");
      if (length > fcCrv.Length + 1e-6) throw new Exception ("GetPointAtLengthFromStart: Length can not be more than fcCrv's length");
      if (length.LieWithin (-1e-6, 1e-6)) return fcCrv.Start;
      if (Math.Abs (fcCrv.Length - length).LieWithin (-1e-6, 1e-6)) return fcCrv.End;
      if (fcCrv is FCArc3 fcArc) {
         (var cen, var radius) = Geom.EvaluateCenterAndRadius (fcArc);
         double thetaAtPoint;
         double arcAngle;

         (arcAngle, _) = Geom.GetArcAngleAndSense (fcArc, planeNormal);
         var transform = Geom.GetArcCS (fcArc, planeNormal);

         double lengthRatio = (length) / fcArc.Length;
         thetaAtPoint = arcAngle * lengthRatio;
         pointAtLengthFromStart = Geom.V2P (transform * new Point3 (radius * Math.Cos (thetaAtPoint), radius * Math.Sin (thetaAtPoint), 0.0));
         if (!Geom.IsPointOnCurve (fcCrv, pointAtLengthFromStart, planeNormal))
            pointAtLengthFromStart = Geom.NudgePointToArc (cen, radius, pointAtLengthFromStart, planeNormal);
      } else {
         double t = length / fcCrv.Length;
         pointAtLengthFromStart = fcCrv.Start * (1 - t) + fcCrv.End * t;
      }
      return pointAtLengthFromStart;
   }

   public static double GetLengthAtPoint (FCCurve3 curve, Point3 pt, Vector3 planeNormal) {
      //GetLengthBetween (curve, curve.Start, pt, planeNormal);
      var t = Geom.GetParamAtPoint (curve, pt, planeNormal);
      return t * curve.Length;
   }

   /// <summary>
   /// This method returns the length of the curve between two points on the curve
   /// </summary>
   /// <param name="curve">The input curve</param>
   /// <param name="start">The start point after which the length shall be computed. This need not be
   /// the start point of the curve.</param>
   /// <param name="end">The end point upto which the length of the curve is computed. 
   /// This need not be the end point of the curve.</param>
   /// <param name="planeNormal">The locality plane normal used in the case of Arc3.</param>
   /// <returns>The length between the two given points AND on the arcs</returns>
   /// <exception cref="Exception">If the given points are not on the curve OR if they are 
   /// not in between the curve's start and end point</exception>
   public static double GetLengthBetween (FCCurve3 curve, Point3 start, Point3 end, Vector3 planeNormal) {
      if (((curve.Start - start).Length.EQ (0) && (curve.End - end).Length.EQ (0)) ||
            ((curve.Start - end).Length.EQ (0) && (curve.End - start).Length.EQ (0)))
         return curve.Length;
      if (!Geom.IsPointOnCurve (curve, start, planeNormal) || !Geom.IsPointOnCurve (curve, end, planeNormal))
         throw new Exception ("The point is not on the curve");
      //var t1 = Geom.GetParamAtPoint (curve, start, planeNormal); var t2 = Geom.GetParamAtPoint (curve, end, planeNormal);
      var l1 = Geom.GetLengthAtPoint (curve, start, planeNormal); var l2 = Geom.GetLengthAtPoint (curve, end, planeNormal);
      return Math.Abs (l1 - l2);
      //if (t1 > t2) (t1, t2) = (t2, t1);
      //return (t2 - t1) * curve.Length;
   }

   /// <summary>
   /// This method returns the length between two points ordered by its parameters
   /// </summary>
   /// <param name="curve">The input curve</param>
   /// <param name="t1">Start parameter</param>
   /// <param name="t2">End Parameter</param>
   /// <returns>The length of the curve in between the given parameters</returns>
   /// <exception cref="Exception"></exception>
   public static double GetLengthBetween (Curve3 curve, double t1, double t2) {
      if (!t1.LieWithin (0, 1) || !t2.LieWithin (0, 1)) throw new Exception ("Parameters are not within 0.0 and 1.0");
      if (t1 > t2) (t1, t2) = (t2, t1);
      return (t1 - t2) * curve.Length;
   }
   #endregion

   #region Methods for 3D Line
   /// <summary>
   /// This method evaluates the point on the line for a given parameter.
   /// </summary>
   /// <param name="ln">The given line segment</param>
   /// <param name="param">For a line segment, it is 0 to 1. 0 being the start,
   /// 1.0 being the end</param>
   /// <returns>Point3 point at the parameter</returns>
   public static Point3 EvaluateLine (FCLine3 ln, double param) => ln.Start * (1 - param) + ln.End * (param);

   /// <summary>
   /// Computes a new point after the end point on the line at a distance specified
   /// </summary>
   /// <param name="arc">The input line segment</param>
   /// <param name="incrementDist">The delta distance after the end of the line segment.</param>
   /// <returns>Point3 which is at a distance "incrementDist" from the end point of the line</returns>
   public static Point3 GetNewEndPointOnLineAtIncrement (FCLine3 fcLine, double incrementDist) {
      var newEndPointOnLineAtIncrement = fcLine.End + (fcLine.End - fcLine.Start).Normalized () * incrementDist;
      return newEndPointOnLineAtIncrement;
   }

   /// <summary>
   /// This method computes the shortest (perpendicular) distance between two 3d lines
   /// </summary>
   /// <param name="p11">Start point of Line 1</param>
   /// <param name="p12">End point of the line 1</param>
   /// <param name="p21">Start point of Line 2</param>
   /// <param name="p22">End point of the line 2</param>
   /// <param name="type">Out parameter: Gives additional info if the lines are either
   /// COLLINEAR or SKEW or PARALLEL or SINGLEINTERSECTION</param>
   /// <returns></returns>
   public static double GetShortestDistBetweenLines (Point3 p11, Point3 p12, Point3 p21, Point3 p22,
      out PairOfLineSegmentsType type) {
      var d1 = p12 - p11; var d2 = p22 - p21;
      double shortestDist;
      if (Geom.Cross (d1, d2).Length.EQ (0.0)) { // Parallel lines
         type = PairOfLineSegmentsType.Parallel;
         var r = p21 - p11;
         var perp2d1d2 = r - d1 * (r.Dot (d1) / (d1.Length * d1.Length));
         shortestDist = perp2d1d2.Length;
         if (shortestDist.EQ (0)) type = PairOfLineSegmentsType.Collinear;
      } else {
         shortestDist = Math.Abs ((p11 - p21).Dot (Geom.Cross (d1, d2))) / Geom.Cross (d1, d2).Length;
         type = PairOfLineSegmentsType.Skew;
         if (shortestDist.EQ (0)) type = PairOfLineSegmentsType.SinglePointIntersection;
      }
      return shortestDist;
   }

   /// <summary>
   /// This method returns the point of intersection between a pair of 3d coplanar line segments.
   /// </summary>
   /// <param name="p11">Start point of Line 1</param>
   /// <param name="p12">End point of Line 1</param>
   /// <param name="p21">Start point of Line 2</param>
   /// <param name="p22">End point of Line 2</param>
   /// <returns>A tuple of Point3 type with the status of intersection PairOfLineSegmentsType. 
   /// The intersection type should be checked before using the ixn point.</returns>
   /// <exception cref="InvalidOperationException"></exception>
   public static Tuple<Point3, Geom.PairOfLineSegmentsType> GetIntersectingPointBetLines (Point3 p11, Point3 p12,
      Point3 p21, Point3 p22, bool constrainedWithinSegment = true) {
      Point3 res = new ();
      GetShortestDistBetweenLines (p11, p12, p21, p22, out Geom.PairOfLineSegmentsType linesType);
      if (linesType != PairOfLineSegmentsType.SinglePointIntersection)
         return new Tuple<Point3, Geom.PairOfLineSegmentsType> (res, linesType);
      var u = p12 - p11; var v = p22 - p21; var w = p21 - p11;
      Matrix<double> A = DenseMatrix.OfArray (new double[,] { { u.X, -v.X }, { u.Y, -v.Y }, { u.Z, -v.Z } });
      Matrix<double> B = DenseMatrix.OfArray (new double[,] { { w.X }, { w.Y }, { w.Z } });

      // Least square solution for the overdetermined system.
      Matrix<double> X = (A.Transpose () * A).Inverse () * A.Transpose () * B;
      res = new Point3 (p11.X + X[0, 0] * (p12.X - p11.X), p11.Y + X[0, 0] * (p12.Y - p11.Y), p11.Z + X[0, 0] *
         (p12.Z - p11.Z));
      if (constrainedWithinSegment) {
         if (!X[0, 0].LieWithin (0, 1) || !X[1, 0].LieWithin (0, 1))
            return new Tuple<Point3, Geom.PairOfLineSegmentsType> (res,
               Geom.PairOfLineSegmentsType.SegmentsNotIntersectingWithinLimits);
      }
      return new Tuple<Point3, Geom.PairOfLineSegmentsType> (res, Geom.PairOfLineSegmentsType.SinglePointIntersection);
   }

   /// <summary>
   /// This method splits the Line3 into N+1 line segments, given "N" points in between 
   /// the line's start and segment.
   /// Caveat: The "N" intermediate points should be in the same order 
   /// from start point to the end point of the line
   /// </summary>
   /// <param name="line">The line to split</param>
   /// <param name="interPointsList">N intermediate points on the given line</param>
   /// <param name="deltaBetweenArcs">The distance along the arc from an arc's end point 
   /// to the next arc's start point of the split arcs</param>
   /// <returns>List of split Arcs (Arc3)</returns>
   public static List<FCLine3> SplitLine (FCLine3 fcLine, List<Point3> interPointsList, double deltaBetweenLines) {
      List<FCLine3> splitLines = [];
      List<Point3> points = [];
      points.Add (fcLine.Start); points.AddRange (interPointsList);
      points.Add (fcLine.End);
      if (points.Count > 2) {
         Point3 newIncrStPt = points[0];
         for (int ii = 0; ii < points.Count - 1; ii++) {
            splitLines.Add (new FCLine3 (newIncrStPt, points[ii + 1]));
            newIncrStPt = GetNewEndPointOnLineAtIncrement (splitLines[^1], deltaBetweenLines);
         }
      } else splitLines.Add (fcLine);
      return splitLines;
   }

   /// <summary>
   /// This method returns the parameter at the given point of the line. Even if the point 
   /// is not within the line segment, the parameter lying outside o to 1 is returned
   /// </summary>
   /// <param name="line">Input Line segment</param>
   /// <param name="somePt">Some point</param>
   /// <returns></returns>
   public static double GetParamAtPoint (FCLine3 line, Point3 somePt) {
      var AP = somePt - line.Start; var AB = line.End - line.Start;
      return (AP.Dot (AB) / AB.Dot (AB));
   }

   /// <summary>
   /// This method returns the linear interpolated normal for the given line at the specific inout point.
   /// </summary>
   /// <param name="line">Input line</param>
   /// <param name="stNormal">Normal at the start point of the line</param>
   /// <param name="endNormal">normal at the end point of the line</param>
   /// <param name="somePt">Point at which the interpolated normal has to be computed</param>
   /// <returns></returns>
   public static Vector3 GetLinearlyInterpolatedNormalAtPoint (FCLine3 line, Vector3 stNormal, Vector3 endNormal, Point3 somePt) {
      var param = GetParamAtPoint (line, somePt);
      return stNormal * (1 - param) + endNormal * param;
   }

   /// <summary>
   /// This method is used to linearly interpolate the normals for a sub-set child line
   /// of a parent. For toolings on flex, the normals at start and end of the lines are different,
   /// ( they are average of the normals between segments)
   /// </summary>
   /// <param name="parentLine">The reference line</param>
   /// <param name="stNormal">The start normal of the parent line or the reference line at start point</param>
   /// <param name="endNormal">The end normal of the parent line or the reference line at end point</param>
   /// <param name="childLine">The line for which the start and end normals at start and end points need
   /// to be evaluated</param>
   /// <returns></returns>
   public static Tuple<Vector3, Vector3> GetLinearlyInterpolatedNormalsForLine (FCLine3 parentLine, Vector3 stNormal, Vector3 endNormal, Line3 childLine) {
      var startNormalChild = GetLinearlyInterpolatedNormalAtPoint (parentLine, stNormal, endNormal, childLine.Start);
      var endNormalChild = GetLinearlyInterpolatedNormalAtPoint (parentLine, stNormal, endNormal, childLine.End);
      return Tuple.Create (startNormalChild, endNormalChild);
   }
   #endregion

   #region Other numeric methods
   public static Vector3 P2V (Point3 position) => new (position.X, position.Y, position.Z);
   public static Point3 V2P (Vector3 vec) => new (vec.X, vec.Y, vec.Z);
   public static Vector3 Cross (Vector3 left, Vector3 right) {
      return new Vector3 (
      left.Y * right.Z - left.Z * right.Y,
      left.Z * right.X - left.X * right.Z,
      left.X * right.Y - left.Y * right.X);
   }
   /// <summary>
   /// This method is used to compute a random number between lower and 
   /// upper limits double precision numbers
   /// </summary>
   /// <param name="min">Lower limit</param>
   /// <param name="max">Upper limit</param>
   /// <returns>The random number between the lower and upper limit double precision numbers</returns>
   public static double RandomNumberWithin (double min, double max) {
      var random = new Random ();
      if (max < min) {
         (max, min) = (min, max);
      }
      double minVal = min + 1e-4; double maxVal = max - 1e-4;
      var result = minVal + (random.NextDouble () * (maxVal - minVal));
      return result;
   }

   /// <summary>
   /// This method is to change a given vector a little in its direction with a magnitude 1e-3.
   /// This shall be used where a geometric degeneracy or a lock can be undone with perturbing 
   /// the vector a little so that it escapes the degeneracy
   /// </summary>
   /// <param name="vec">The Input vector to be perturbed a little</param>
   /// <returns>Returns a vector that is perturbed a little W.R.T the input vector</returns>
   public static Vector3 Perturb (Vector3 vec) {
      Vector3 res = new ((vec.X + RandomNumberWithin (0, 1.0)) * 1e-4, (vec.Y + RandomNumberWithin (0, 1.0)) * 1e-4,
         (vec.Z + RandomNumberWithin (0.0, 1.0)) * 1e-4);
      return res;
   }
   static Bound3 Clamp (double xMin, double yMin, double zMin,
      double xMax, double yMax, double zMax, Bound3 partBBox) {
      xMin.Clamp (partBBox.XMin, partBBox.XMax);
      yMin.Clamp (partBBox.YMin, partBBox.YMax);
      zMin.Clamp (partBBox.ZMin, partBBox.ZMax);
      xMax.Clamp (partBBox.XMin, partBBox.XMax);
      yMax.Clamp (partBBox.YMin, partBBox.YMax);
      zMax.Clamp (partBBox.ZMin, partBBox.ZMax);
      Bound3 bbox = new (xMin, yMin, zMin, xMax, yMax, zMax);
      return bbox;
   }
   static Bound3 Clamp (Point3 minPt, Point3 maxPt, Bound3 partBBox) {
      double xMin = minPt.X, yMin = minPt.Y, zMin = minPt.Z,
         xMax = maxPt.X, yMax = maxPt.Y, zMax = maxPt.Z;
      return Clamp (xMin, yMin, zMin, xMax, yMax, zMax, partBBox);
   }
   public static Bound3 ComputeBBox (FCCurve3 curve, Vector3? planeNormal, Bound3 partBBox) {
      if (curve == null) throw new ArgumentNullException (nameof (curve), "ComputeBBox: curve is null");
      if (curve is FCArc3 arc) {
         if (planeNormal == null) throw new ArgumentNullException (nameof (planeNormal), "ComputeBBox: Plane normal can not be null for arc input");
         var (cen, rad) = EvaluateCenterAndRadius (arc);
         if (IsCircle (curve)) {
            var xMin = cen.X - rad;
            var xMax = cen.X + rad;
            var yMin = cen.Y - rad;
            var yMax = cen.Y + rad;
            var zMin = cen.Z - rad;
            var zMax = cen.Z + rad;
            return Clamp (xMin, yMin, zMin, xMax, yMax, zMax, partBBox);
         } else {
            Point3 pz0 = cen + XForm4.mXAxis * rad; Point3 pz90 = cen + XForm4.mYAxis * rad;
            Point3 pz180 = cen + -XForm4.mXAxis * rad; Point3 pz270 = cen + -XForm4.mYAxis * rad;
            List<Point3> points = [];
            if (IsPointOnCurve (curve, pz0, planeNormal)) points.Add (pz0);
            if (IsPointOnCurve (curve, pz90, planeNormal)) points.Add (pz90);
            if (IsPointOnCurve (curve, pz180, planeNormal)) points.Add (pz180);
            if (IsPointOnCurve (curve, pz270, planeNormal)) points.Add (pz270);
            points.Add (curve.Start); points.Add (curve.End);
            Bound3 bbox = new (points);
            var minP = bbox.Min;
            var maxP = bbox.Max;
            return Clamp (minP, maxP, partBBox);
         }
      } else {
         List<Point3> points = [];
         points.Add (curve.Start); points.Add (curve.End);
         Bound3 bbox = new (points);
         var minP = bbox.Min;
         var maxP = bbox.Max;
         return Clamp (minP, maxP, partBBox);
      }
   }

   /// <summary>
   /// This method is used to determine the winding sense of segments, 
   /// whether they are Clockwise (CW) or Counter-Clockwise (CCW), 
   /// with respect to the plane's normal emanating toward the observer.
   /// </summary>
   /// <remarks>
   /// <para>
   /// <b>Algorithm:</b>
   /// <list type="number">
   /// <item>The start point of the segment is projected onto the plane defined by the plane normal and a reference point.</item>
   /// <item>A point (q) is chosen that is guaranteed to lie outside the polygon formed by the segments.</item>
   /// <item>
   /// The plane normal is selected based on the segment's orientation:
   /// <list type="bullet">
   /// <item>(0, sqrt(1/2), sqrt(1/2)) if one of the segment normals aligns with the positive Y direction (yPos).</item>
   /// <item>(0, -sqrt(1/2), sqrt(1/2)) if one of the segment normals aligns with the negative Y direction (yNeg).</item>
   /// </list>
   /// </item>
   /// <item>
   /// A farthest point from the start point of the segments is evaluated.
   /// This point must lie on the convex part of the polygon projected onto the plane.
   /// </item>
   /// <item>The cross product of two vectors is calculated:
   /// <list type="bullet">
   /// <item>(Start → One-Point-before-Farthest-Point)</item>
   /// <item>(Start → Farthest-Point)</item>
   /// </list>
   /// </item>
   /// <item>
   /// If the cross product aligns with the plane's normal, the windings are counter-clockwise (CCW).
   /// Otherwise, they are clockwise (CW).
   /// </item>
   /// </list>
   /// </para>
   /// </remarks>
   /// <param name="planeNormal">
   /// The normal vector of the plane on which the segments are projected.
   /// It is chosen to be either (0, sqrt(1/2), sqrt(1/2)) for yPos alignment or
   /// (0, -sqrt(1/2), sqrt(1/2)) for yNeg alignment.
   /// </param>
   /// <param name="pointOnPlane">A reference point on the plane that defines the projection plane.</param>
   /// <param name="cutSegs">
   /// Segments of lines or arcs represented as tuples: 
   /// <c>(Point3 StartPoint, Vector3 Direction, Vector3 Additional)</c>.
   /// </param>
   /// <returns>
   /// The winding direction of the segments relative to the plane's normal, 
   /// which is either Clockwise (CW) or Counter-Clockwise (CCW).
   /// </returns>

   public static ToolingWinding GetToolingWinding (Vector3 planeNormal, Point3 pointOnPlane,
      List<ToolingSegment> cutSegs) {
      List<Point3> pointsOnPLane = [];
      foreach (var cutSeg in cutSegs) {
         var p = cutSeg.Curve.Start;
         var pq = pointOnPlane - p;
         var dot = pq.Dot (planeNormal);
         var projP = p + planeNormal * dot;
         if (!pointsOnPLane.Any (p => p.DistTo (projP) < 1e-3)) pointsOnPLane.Add (projP);
      }
      var refPointOnPlane = pointsOnPLane[0];
      var pointsOnPLaneCopy = pointsOnPLane.ToList ();
      var farthestPointAndIndex = pointsOnPLaneCopy
         .Select ((p, index) => new { Point = p, Index = index })
         .Skip (1) // Skip the 0th point
         .OrderByDescending (p => refPointOnPlane.DistTo (p.Point))
         .First ();
      var farthestIndex = farthestPointAndIndex.Index;
      var previousIndex = farthestPointAndIndex.Index - 1;
      var vi_1 = pointsOnPLane[previousIndex] - refPointOnPlane;
      var vi = pointsOnPLane[farthestIndex] - refPointOnPlane;
      var cross = Geom.Cross (vi_1, vi);
      if (cross.Opposing (planeNormal)) return ToolingWinding.CW;
      return ToolingWinding.CCW;
   }
   #endregion

   #region Method for Tooling Segments
   /// <summary>
   /// This method is used to directionally reverse the given tooling segment,
   /// where the Start points and normals become the end points and normals and vice versa
   /// </summary>
   /// <param name="ts">The input Tooling Segment</param>
   /// <returns>This returns the reversed tooling segment</returns>
   public static ToolingSegment GetReversedToolingSegment (ToolingSegment ts, EArcSense hintSense = EArcSense.Infer, double tolerance = 1e-6) {
      var crv = GetReversedCurve (ts.Curve, ts.Vec0.Normalized (), hintSense, tolerance);
      var newTs = new ToolingSegment (crv, ts.Vec1, ts.Vec0) {
         NotchSectionType = ts.NotchSectionType
      };
      return newTs;
   }

   /// <summary>
   /// This method is a wrapper for GetReversedToolingSegment(), to reverse a list of 
   /// tooling segments</summary>
   /// <param name="toolSegs"></param>
   /// <returns>This returns the reversed tooling segments in the reverse order</returns>
   public static List<ToolingSegment> GetReversedToolingSegments (List<ToolingSegment> toolSegs) {
      List<ToolingSegment> resSegs = [];
      for (int ii = toolSegs.Count - 1; ii >= 0; ii--)
         resSegs.Add (GetReversedToolingSegment (toolSegs[ii]));
      return resSegs;
   }

   /// <summary>
   /// This method is used to linearly interpolate the normal at pointon the tooling segment
   /// </summary>
   /// <param name="parentSegment">The tooling segment that is used as a reference</param>
   /// <param name="atPoint">The point at which the the normal needs to be interpolated W.R.T 
   /// the reference tooling segment</param>
   /// <returns>This returns the normal vector at the given point</returns>
   public static Vector3 GetLinearlyInterpolatedNormalAtPoint (ToolingSegment parentSegment, Point3 atPoint) {
      if ((parentSegment.Curve.Start - atPoint).IsZero) return parentSegment.Vec0;
      else if ((parentSegment.Curve.End - atPoint).IsZero) return parentSegment.Vec1;
      if (parentSegment.Curve is FCArc3) return parentSegment.Vec0;
      else return GetLinearlyInterpolatedNormalAtPoint (parentSegment.Curve as FCLine3, parentSegment.Vec0, parentSegment.Vec1, atPoint);
   }

   /// <summary>
   /// This is a utility method that creates a tooling segment (ValueTuple (Curve3, Vector3,Vector3) )
   /// </summary>
   /// <param name="parentSegment">The reference segment to use its start and end normals.</param>
   /// <param name="crv">The curve which will be wrapped by the tooling segment</param>
   /// <returns>The newly created tooling segment</returns>
   public static ToolingSegment CreateToolingSegmentForCurve (ToolingSegment parentSegment, FCCurve3 crv, bool cloneCrv = false) {
      if (cloneCrv)
         return new ToolingSegment (crv, parentSegment.Vec0, parentSegment.Vec1, cloneCrv: cloneCrv);
      else
         return new ToolingSegment (crv, parentSegment.Vec0, parentSegment.Vec1);
   }

   public static ToolingSegment CreateToolingSegmentForCurve (ToolingSegment parentSegment, FCCurve3 crv, NotchSectionType nsType, bool clone = false) {
      return new ToolingSegment (crv, parentSegment.Vec0, parentSegment.Vec1, nsType);
   }

   public static ToolingSegment CreateToolingSegmentForCurve (FCCurve3 crv, Vector3 startNormal, Vector3 endNormal, bool clone = false) {
      return new ToolingSegment (crv, startNormal, endNormal);
   }

   public static ToolingSegment CreateToolingSegmentForCurve (FCCurve3 crv, Vector3 startNormal, Vector3 endNormal, NotchSectionType nsType, bool clone = false) {
      return new ToolingSegment (crv, startNormal, endNormal, nsType);
   }
   /// <summary>
   /// This method is a creates a list of tooling segments for a list of curves
   /// </summary>
   /// <param name="parentSegment">The reference tooling segment</param>
   /// <param name="curves">The input set of curves</param>
   /// <returns>List of newly created tooling segments</returns>
   public static List<ToolingSegment> CreateToolingSegmentForCurves (ToolingSegment parentSegment, List<FCCurve3> curves) {
      List<ToolingSegment> res = [];
      if (curves == null || curves.Count == 0) return res;
      foreach (var crv in curves) {
         var ts = new ToolingSegment (crv, parentSegment.Vec0, parentSegment.Vec1);
         res.Add (ts);
      }
      return res;
   }

   /// <summary>
   /// This method finds the cumulative lengths of segments upto the point
   /// on the segment from start
   /// </summary>
   /// <param name="segs">The input list of segments</param>
   /// <param name="pt">The input point, on a segment upto which the length has
   /// to be found</param>
   /// <returns>A tuple of Length and index of the segment at which the input point exists</returns>
   /// <exception cref="Exception">Throws an excption if the given input point is not on any of the 
   /// segments</exception>
   public static Tuple<double, int> GetLengthAtPoint (List<ToolingSegment> segs, Point3 pt) {
      double cumLength = 0;
      int idx = -1;
      bool ptOnOneSeg = false;
      for (int ii = 0; ii < segs.Count; ii++) {
         var segm = segs[ii];
         if (IsPointOnCurve (segm.Curve, pt, segm.Vec0)) {
            cumLength += GetLengthAtPoint (segm.Curve, pt, segm.Vec0);
            ptOnOneSeg = true;
            idx = ii;
            break;
         } else cumLength += segm.Curve.Length;
      }
      if (!ptOnOneSeg) throw new Exception ("Geom:GetLengthAtPoint: The given point pt does not exist on any segments");
      //foreach (var seg in segs) {
      //   idx++;
      //   if (IsPointOnCurve (seg.Curve, pt, seg.Vec0)) {
      //      cumLength += GetLengthAtPoint (seg.Curve, pt, seg.Vec0);
      //      break;
      //   } else cumLength += seg.Curve.Length;
      //}
      return new Tuple<double, int> (cumLength, idx);
   }

   /// <summary>
   /// This method computes a point on the segment out of a list of segments
   /// and the index of the segment, at which the given length matches.
   /// </summary>
   /// <param name="segs"></param>
   /// <param name="inLength"></param>
   /// <returns>A tuple of Point and the index in the segment</returns>
   public static Tuple<Point3, int> GetPointAtLength (List<ToolingSegment> segs, double inLength) {
      Point3 pt;
      (pt, int index) = EvaluatePointAndIndexAtLength (segs, -1, inLength);
      return new Tuple<Point3, int> (pt, index);
   }

   /// <summary>
   /// This method creates a list of tooling segments from the given input tooling segments, starting after the
   /// <paramref name="segStartIndex"/>-th item, and up to a length of <paramref name="uptoLength"/> from the 
   /// end point of the tooling segment at <paramref name="segStartIndex"/> th index
   /// </summary>
   /// <param name="segs">
   /// The segments of the tooling item. 
   /// Note: This <paramref name="segs"/> parameter may not represent the original segments of the tooling item,
   /// as it allows for considering modified segments.
   /// </param>
   /// <param name="reverseTrace">
   /// A boolean flag indicating whether the curves are sought in reverse order.
   /// </param>
   /// <returns>
   /// A list of <c>ToolingSegment</c> objects, which are the intermediate sequential tooling segments from the end 
   /// of the <paramref name="segStartIndex"/>-th tooling segment to (forward or reverse order) the split tooling segments. 
   /// <para>
   /// The split tooling segments can be either 1 or 2, depending on the point at which the split is made on the 
   /// the tooling segment at a distance of <paramref name="uptoLength"/>. 
   /// </para>
   /// </returns>

   public static Tuple<Point3, int> EvaluatePointAndIndexAtLength (List<ToolingSegment> segs, int segStartIndex,
      double uptoLength, /*Vector3 fpn,*/ bool reverseTrace = false, double minimumCurveLength = 0.5) {
      Tuple<Point3, int> res;// = new (new Point3 (), -1);
      double crvLengths = 0.0;
      var currIndex = segStartIndex;
      bool first = true;
      while (crvLengths < uptoLength && Math.Abs (crvLengths - uptoLength) > 1e-3) {
         if (reverseTrace ? currIndex - 1 >= 0 : currIndex + 1 < segs.Count) {
            if (reverseTrace) {
               if (!first) currIndex--;
            } else currIndex++;
            if (first) first = !first;
            crvLengths += segs[currIndex].Curve.Length;
         } else break;
      }

      // What if the crvLengths is almost the "uptoLength"?
      // In that case, no curve shall be split. The next seg item shall be added 
      // from segs to have an uniform output that the last segment shall be 
      // machinable.
      double prevCurveLengths = 0;
      if (reverseTrace) {
         for (int ii = currIndex + 1; ii < segStartIndex; ii++)
            prevCurveLengths += segs[ii].Curve.Length;
      } else {
         for (int ii = currIndex - 1; ii > segStartIndex; ii--)
            prevCurveLengths += segs[ii].Curve.Length;
      }
      double deltaDist = uptoLength - prevCurveLengths;

      if (deltaDist < minimumCurveLength) {
         if (!reverseTrace) --currIndex;
         res = new (segs[currIndex].Curve.End, currIndex);
      } else if (segs[currIndex].Curve.Length - deltaDist < minimumCurveLength) {
         if (reverseTrace) --currIndex;
         res = new (segs[currIndex].Curve.End, currIndex);
      } else {
         // Reverse the parameter if needed
         double t = deltaDist / segs[currIndex].Curve.Length;
         double segLen;
         if (segs[currIndex].Curve is FCArc3 arc) {
            var (_, rad) = EvaluateCenterAndRadius (arc);
            var (angle, _) = GetArcAngleAndSense (arc, segs[currIndex].Vec0.Normalized ());
            segLen = Math.Abs (rad * angle);
            t = deltaDist / segLen;
         }
         if (reverseTrace) t = 1 - t;
         var pt = Geom.Evaluate (segs[currIndex].Curve, t, segs[currIndex].Vec0.Normalized ());
         if (true) {
            var chk = Geom.IsPointOnCurve (segs[currIndex].Curve, pt, segs[currIndex].Vec0.Normalized ());
            if (!chk)
               throw new Exception ("What the heck!");
         }
         res = new (pt, currIndex);

      }
      return res;
   }

   public static Tuple<Point3, int> GetPointAtLengthFrom (Point3 iPoint, double iLength, List<ToolingSegment> segs) {
      Tuple<Point3, int> res;
      if (segs.Count == 2 || segs.Count == 1) {
         FCCurve3 crv;
         Vector3 normal;
         int index;
         if (segs.Count == 2) {
            crv = segs[1].Curve;
            normal = segs[1].Vec0.Normalized ();
            index = 1;
         } else {
            crv = segs[0].Curve;
            normal = segs[0].Vec0.Normalized ();
            index = 0;
         }
         if (Utils.IsCircle (crv)) {
            var evalPt = Geom.Evaluate (crv, iLength / crv.Length, normal);
            res = new Tuple<Point3, int> (evalPt, index);
            // DEBUG_DEBUG 
            if (!Geom.IsPointOnCurve (crv, evalPt, normal))
               throw new Exception ("In GetPointAtLengthFrom: evalPt is not on the curve!");
            return res;
         }
      }

      var (len, _) = GetLengthAtPoint (segs, iPoint);
      var tLen = len + iLength;
      res = EvaluatePointAndIndexAtLength (segs, -1, tLen);
      return res;
   }

   /// <summary>
   /// This method is used to find that segment on which a given tooling length occurs. 
   /// </summary>
   /// <param name="segments">The input tooling segments</param>
   /// <param name="toolingLength">The tooling length from start of the segment</param>
   /// <returns>A tuple of the index of the segment on which the input tooling length happens and the
   /// incremental length of the index-th segment from its own start</returns>
   public static Tuple<int, double> GetSegmentLengthAndIndexForToolingLength (List<ToolingSegment> segments, double toolingLength) {
      double segmentLengthAtNotch = 0;
      int jj = 0;
      while (segmentLengthAtNotch < toolingLength) {
         segmentLengthAtNotch += segments[jj].Curve.Length;
         jj++;
      }

      var lengthInLastSegment = toolingLength;
      int occuranceIndex = jj - 1;
      double previousCurveLengths = 0.0;
      for (int kk = occuranceIndex - 1; kk >= 0; kk--)
         previousCurveLengths += segments[kk].Curve.Length;

      lengthInLastSegment -= previousCurveLengths;
      return new Tuple<int, double> (occuranceIndex, lengthInLastSegment);
   }

   /// <summary>
   /// This method is used to find the length of the tooling segments from start index to end index 
   /// of the list of tooling segments, INCLUDING the start and the end segment
   /// </summary>
   /// <param name="segments">The input tooling segments</param>
   /// <param name="fromIdx">The index of the start segment</param>
   /// <param name="toIdx">The index of the end segment</param>
   /// <returns>The length of the tooling segments from start to end index including the start and 
   /// end the segments</returns>
   public static double GetLengthBetween (List<ToolingSegment> segments, int fromIdx, int toIdx) {
      if (segments.Count == 2) {
         if (Utils.IsCircle (segments[1].Curve))
            return segments[1].Curve.Length;
      }
      if (segments.Count == 1) {
         if (Utils.IsCircle (segments[0].Curve))
            return segments[0].Curve.Length;
      }
      // Ensure fromIdx <= toIdx
      if (fromIdx > toIdx)
         (fromIdx, toIdx) = (toIdx, fromIdx);

      // Sum lengths from 'fromIdx' up to 'toIdx' (inclusive)
      double lengthBetween = segments
          .Skip (fromIdx)
          .Take (toIdx - fromIdx + 1)
          .Sum (segment => segment.Curve.Length);

      return lengthBetween;
   }


   /// <summary>
   /// This method is used to find the length of the tooling segments from start point and the 
   /// end point on list of tooling segments. 
   /// </summary>
   /// <param name="segments">The input tooling segments</param>
   /// <param name="fromIdx">The from point on one of the segments</param>
   /// <param name="toIdx">The to point on one of the segments</param>
   /// <returns>The length of the tooling segments from start to end points 
   /// which is the sum of the start point to end of the start segment, 
   /// the lengths of all the tooling segments inbetween the start and end segments
   /// and the length of the last segment from start point of that segment To the given
   /// To Point</returns>
   public static double GetLengthBetween (List<ToolingSegment> segments, Point3 fromPt, Point3 toPt, bool inSameOrder = false) {
      if (segments.Count == 2 || segments.Count == 1) {
         FCCurve3 crv;
         Vector3 normal;
         //int index = -1;
         if (segments.Count == 2) {
            crv = segments[1].Curve;
            normal = segments[1].Vec0.Normalized ();
            //index = 1;
         } else {
            crv = segments[0].Curve;
            normal = segments[0].Vec0.Normalized ();
            //index = 0;
         }
         if (Utils.IsCircle (crv)) {
            //if (index == 1) {
            //   fromPt = crv.Start;
            //   toPt = crv.End;
            //}
            var t1 = Geom.GetParamAtPoint (crv, fromPt, normal);
            var t2 = Geom.GetParamAtPoint (crv, toPt, normal);
            if (t1 > t2) throw new Exception ("In Geom.GetLengthBetween() the param for fromPt is greater than param for to point");
            double distBetween = (t2 - t1) * crv.Length;
            return distBetween;
         }
      }

      //var fromPtOnSegment = segments.Select ((segment, index) => new { Segment = segment, Index = index })
      //                                    .FirstOrDefault (x => Geom.IsPointOnCurve (x.Segment.Curve, fromPt,
      //                                                                               x.Segment.Vec0.Normalized ()));
      var fromPtIdxOnSegment = GetIndexOfPointOnToolingSegments (segments, fromPt);
      if (fromPtIdxOnSegment == -1)
         throw new Exception ("GetLengthBetween: From pt is not on segment");


      //var toPtOnSegment = segments.Select ((segment, index) => new { Segment = segment, Index = index })
      //                                 .FirstOrDefault (x => Geom.IsPointOnCurve (x.Segment.Curve, toPt,
      //                                                                              x.Segment.Vec0.Normalized ()));
      var toPtIdxOnSegment = GetIndexOfPointOnToolingSegments (segments, toPt, inSameOrder ? fromPtIdxOnSegment : -1);
      if (toPtIdxOnSegment == -1)
         throw new Exception ("GetLengthBetween: To pt is not on segment");

      // Swap the objects if from index is > to Index
      if (fromPtIdxOnSegment > toPtIdxOnSegment) {
         (fromPtIdxOnSegment, toPtIdxOnSegment) = (toPtIdxOnSegment, fromPtIdxOnSegment);
         (fromPt, toPt) = (toPt, fromPt);
      }

      double fromPtSegLength;
      double toPtSegLength = 0;
      if (fromPtIdxOnSegment == toPtIdxOnSegment)
         fromPtSegLength = Geom.GetLengthBetween (segments[fromPtIdxOnSegment].Curve, fromPt, toPt, segments[fromPtIdxOnSegment].Vec0);
      else {
         fromPtSegLength = Geom.GetLengthBetween (segments[fromPtIdxOnSegment].Curve,
                                                         fromPt, segments[fromPtIdxOnSegment].Curve.End,
                                                         segments[fromPtIdxOnSegment].Vec0.Normalized ());
         toPtSegLength = Geom.GetLengthBetween (segments[toPtIdxOnSegment].Curve, toPt,
                                                       segments[toPtIdxOnSegment].Curve.Start,
                                                       segments[toPtIdxOnSegment].Vec0.Normalized ());
      }
      double intermediateLength = 0;
      //if (fromPtIdxOnSegment != toPtIdxOnSegment &&
      //   fromPtIdxOnSegment + 1 < segments.Count
      //    && toPtIdxOnSegment - 1 >= 0)
      //if (fromPtIdxOnSegment + 1 != toPtIdxOnSegment && toPtIdxOnSegment - 1 != fromPtIdxOnSegment)
      if (toPtIdxOnSegment - fromPtIdxOnSegment - 1 != 0 && toPtIdxOnSegment != fromPtIdxOnSegment)
         intermediateLength = GetLengthBetween (segments, fromPtIdxOnSegment + 1,
                                                       toPtIdxOnSegment - 1);

      double length = intermediateLength + (fromPtSegLength + toPtSegLength);
      return length;
   }

   /// <summary>
   /// This method is used to find the length of the segments between the given point
   /// occuring on a tooling segment AND the lengths of all segments upto the last segment
   /// </summary>
   /// <param name="segments">The input segments list</param>
   /// <param name="pt">The given point</param>
   /// <returns>the length of the segments between the given point occuring on a tooling segment 
   /// AND the lengths of all segments upto the last segment</returns>
   /// <exception cref="Exception">An exception is thrown if the given point is not on any of the 
   /// input list of tooling segments</exception>
   public static double GetLengthFromEndToolingToPosition (List<ToolingSegment> segments, Point3 pt) {
      //var result = segments.Select ((segment, index) => new { Segment = segment, Index = index })
      //                     .FirstOrDefault (x => Geom.IsPointOnCurve (x.Segment.Curve, pt,
      //                                                                x.Segment.Vec0.Normalized ()));
      int idx = GetIndexOfPointOnToolingSegments (segments, pt);
      if (idx == -1)
         throw new Exception ("GetLengthFromEndToolingToPosition: Given pt is not on any segment");

      double length = segments.Skip (idx + 1).Sum (segment => segment.Curve.Length);
      double idxthSegLengthFromPt = Geom.GetLengthBetween (segments[idx].Curve, pt,
                                                           segments[idx].Curve.End,
                                                           segments[idx].Vec0.Normalized ());
      length += idxthSegLengthFromPt;
      return length;
   }

   public static int GetIndexOfPointOnToolingSegments (List<ToolingSegment> segs, Point3 pt, int fromIndex = -1) {
      //{ ToolingSegment Segment, int Index} result;

      var result = segs
                  .Select ((segment, index) => new { Segment = segment, Index = index })
                  .FirstOrDefault (x =>
                  x.Index > fromIndex &&
                  Geom.IsPointOnCurve (x.Segment.Curve, pt, x.Segment.Vec0.Normalized ()));

      // DEBUG: Fallback logic using an anonymous type
      for (int ii = 0; ii < segs.Count; ii++) {
         if (Geom.IsPointOnCurve (segs[ii].Curve, pt, segs[ii].Vec0.Normalized ()) && ii >= fromIndex) {
            result = new { Segment = segs[ii], Index = ii }; // Anonymous type
            break; // Stop after finding the first match
         }
      }

      bool ptOnSegment = result != null;
      int idx = ptOnSegment ? result.Index : -1;
      return idx;
   }

   /// <summary>
   /// This method is used to find the length of the segments between the given point
   /// occuring on a tooling segment AND the lengths of all segments upto the first segment
   /// </summary>
   /// <param name="segments">The input segments list</param>
   /// <param name="pt">The given point</param>
   /// <returns>the length of the segments between the given point occuring on a tooling segment 
   /// AND the lengths of all segments upto the first segment</returns>
   /// <exception cref="Exception">An exception is thrown if the given point is not on any of the 
   /// input list of tooling segments</exception>
   public static double GetLengthFromStartToolingToPosition (List<ToolingSegment> segments, Point3 pt) {
      double length = 0.0;
      int idx = GetIndexOfPointOnToolingSegments (segments, pt);

      if (idx == -1)
         throw new Exception ("GetLengthFromStartToolingToPosition: Given pt is not on any segment");

      length = segments.Take (idx - 1).Sum (segment => segment.Curve.Length);
      length += Geom.GetLengthBetween (segments[idx].Curve, segments[idx].Curve.Start, pt,
                                       segments[idx].Vec0.Normalized ());
      return length;
   }
   #endregion

   //public static bool IsSameDir (Vector3 l, Vector3 r) {
   //   l = l.Normalized (); r = r.Normalized ();
   //   if (l.X.EQ (r.X) && l.Y.EQ (r.Y) && l.Z.EQ (r.Z)) return true;
   //   return false;
   //}

   public static Vector3 GetInterpolatedNormal (Vector3 start, Vector3 end, double t) {
      var vec = (end * t + start * (1 - t));
      return vec;
   }
   //public static bool IsEqual (Vector3 lhs, Vector3 rhs, double tol = 1e-6) {
   //   if (lhs.X.EQ (rhs.X, tol) && lhs.Y.EQ (rhs.Y, tol) && lhs.Z.EQ (rhs.Z, tol)) return true;
   //   return false;
   //}

   public static void MergeSegments (List<ToolingSegment> segs, int stIndex, int endIndex, NotchSectionType nType = NotchSectionType.None) {
      if (stIndex < 0 || stIndex >= segs.Count || endIndex < 0 || endIndex >= segs.Count)
         throw new Exception ("Array out of bounds");
      if (stIndex >= endIndex) return;
      var line = new FCLine3 (segs[stIndex].Curve.Start, segs[endIndex].Curve.End);
      var vec0 = segs[stIndex].Vec0; var vec1 = segs[stIndex].Vec1;
      var newTs = new ToolingSegment (line as FCCurve3, vec0, vec1, nType);

      for (int ii = stIndex; ii <= endIndex; ii++)
         segs.RemoveAt (stIndex);
      segs.Insert (stIndex, newTs);
   }

   public static bool AreCollinear (Point3 a, Point3 b, Point3 c, double tol = 1e-6) {
      // Compute vectors
      double abX = b.X - a.X;
      double abY = b.Y - a.Y;
      double abZ = b.Z - a.Z;

      double acX = c.X - a.X;
      double acY = c.Y - a.Y;
      double acZ = c.Z - a.Z;

      // Cross product AB x AC
      double crossX = abY * acZ - abZ * acY;
      double crossY = abZ * acX - abX * acZ;
      double crossZ = abX * acY - abY * acX;

      // Squared magnitude of cross product
      double crossMagSq = crossX * crossX +
                          crossY * crossY +
                          crossZ * crossZ;

      return crossMagSq < tol * tol;
   }

   public static double ShortestDistancePointToPlane (
    Point3 point,
    Point3 planePoint,
    Vector3 planeNormal,
    double tol = 1e-6) {
      if (planeNormal.Dot (planeNormal).EQ (0, tol * tol))
         throw new ArgumentException ("Plane normal is zero");

      var v = point - planePoint;

      double numerator = v.Dot (planeNormal);
      double denom = Math.Sqrt (planeNormal.Dot (planeNormal));

      return Math.Abs (numerator) / denom;
   }

   public static Point3 ProjectPointToPlane (
    Point3 point,
    Point3 planePoint,
    Vector3 planeNormal,
    double tol = 1e-6) {
      planeNormal = planeNormal.Normalized ();
      double n2 = planeNormal.Dot (planeNormal);

      if (n2.EQ (0, tol * tol))
         throw new ArgumentException ("Plane normal is zero");

      Vector3 v = point - planePoint;

      double factor = v.Dot (planeNormal) / n2;

      return point - planeNormal * factor;
   }
}

/// <summary>
/// The class XForm4 encapsulates the 3D homogenous transformation construction of 
/// size 4X4 matrix and transformation implementations for a rigid body. (There is no 
/// scaling or shear). This is an affine transformation and the determinant of this 
/// transform is always +1. The leadng 3X3 matrix represents an orthonormal unit vectors
/// The first 3 elments of the last column represents the translation component.
/// </summary>
public class XForm4 {
   #region Readonly Constants
   public static readonly Vector3 mZAxis = new (0, 0, 1);
   public static readonly Vector3 mYAxis = new (0, 1, 0);
   public static readonly Vector3 mXAxis = new (1, 0, 0);
   public static readonly Vector3 mNegZAxis = new (0, 0, -1);
   public static readonly Vector3 mNegYAxis = new (0, -1, 0);
   public static readonly Vector3 mNegXAxis = new (-1, 0, 0);
   public static readonly XForm4 IdentityXfm = new ();
   #endregion

   #region Predicates
   public static bool IsXAxis (Vector3 vec) {
      if ((vec.Normalized ().Dot (mXAxis) - 1.0).EQ (0)) return true;
      return false;
   }

   public static bool IsYAxis (Vector3 vec) {
      if ((vec.Normalized ().Dot (mYAxis) - 1.0).EQ (0)) return true;
      return false;
   }

   public static bool IsZAxis (Vector3 vec) {
      if ((vec.Normalized ().Dot (mZAxis) - 1.0).EQ (0)) return true;
      return false;
   }

   public static bool IsNegXAxis (Vector3 vec) {
      if ((vec.Normalized ().Dot (-mXAxis) - 1.0).EQ (0)) return true;
      return false;
   }

   public static bool IsNegYAxis (Vector3 vec) {
      if ((vec.Normalized ().Dot (-mYAxis) - 1.0).EQ (0)) return true;
      return false;
   }

   public static bool IsNegZAxis (Vector3 vec) {
      if ((vec.Normalized ().Dot (-mZAxis) - 1.0).EQ (0)) return true;
      return false;
   }
   #endregion

   #region Enums
   public enum EAxis { X, Y, Z, NegX, NegY, NegZ }

   public enum EXFormType { Identity, Zero }
   #endregion

   #region Data Members
   double[,] matrix = new double[4, 4];
   #endregion

   #region Constructors
   public XForm4 (EXFormType type = EXFormType.Identity) {
      if (type == EXFormType.Zero) Zero ();
      else Identify ();
   }
   public XForm4 (Vector3 col1, Vector3 col2, Vector3 col3, Vector3 col4) {
      Zero ();
      SetRotationComponents (col1, col2, col3);
      Translate (col4);
   }
   public XForm4 (XForm4 rhs) {
      for (int i = 0; i < 4; i++)
         for (int j = 0; j < 4; j++)
            matrix[i, j] = rhs.matrix[i, j];
   }

   public XForm4 (double[,] coords) {
      for (int i = 0; i < 4; i++)
         for (int j = 0; j < 4; j++)
            matrix[i, j] = coords[i, j];
   }
   public XForm4 (double a11, double a12, double a13, double a14,
      double a21, double a22, double a23, double a24,
      double a31, double a32, double a33, double a34,
      double a41, double a42, double a43, double a44) {
      matrix[0, 0] = a11; matrix[0, 1] = a12; matrix[0, 2] = a13; matrix[0, 2] = a14;
      matrix[1, 0] = a21; matrix[1, 1] = a22; matrix[1, 2] = a23; matrix[1, 3] = a24;
      matrix[2, 0] = a31; matrix[2, 1] = a32; matrix[2, 2] = a33; matrix[2, 3] = a34;
      matrix[3, 0] = a41; matrix[3, 1] = a42; matrix[3, 2] = a43; matrix[3, 3] = a44;
   }
   #endregion

   #region Explicit Setters
   public void SetRotationComponents (Vector3 col1, Vector3 col2, Vector3 col3) {
      col1 = col1.Normalized (); col2 = col2.Normalized (); col3 = col3.Normalized ();
      matrix[0, 0] = col1.X; matrix[1, 0] = col1.Y; matrix[2, 0] = col1.Z;
      matrix[0, 1] = col2.X; matrix[1, 1] = col2.Y; matrix[2, 1] = col2.Z;
      matrix[0, 2] = col3.X; matrix[1, 2] = col3.Y; matrix[2, 2] = col3.Z;
   }

   public void SetTranslationComponent (Vector3 col4) {
      matrix[0, 3] = col4.X; matrix[1, 3] = col4.Y; matrix[2, 3] = col4.Z;
   }
   #endregion

   #region Properties
   public double this[int i, int j] {
      get => matrix[i, j];
      set => matrix[i, j] = value;
   }

   public Vector3 XCompRot { get => new (matrix[0, 0], matrix[1, 0], matrix[2, 0]); }
   public Vector3 YCompRot { get => new (matrix[0, 1], matrix[1, 1], matrix[2, 1]); }
   public Vector3 ZCompRot { get => new (matrix[0, 2], matrix[1, 2], matrix[2, 2]); }
   #endregion

   #region Matrix manipulation methods ( The object is modified )
   public XForm4 Translate (Vector3 trans) {
      matrix[0, 3] = trans.X; matrix[1, 3] = trans.Y; matrix[2, 3] = trans.Z; matrix[3, 3] = 1.0;
      return this;
   }
   public XForm4 Multiply (XForm4 right) {
      double[,] result = new double[4, 4];
      for (int i = 0; i < 4; i++)
         for (int j = 0; j < 4; j++)
            for (int k = 0; k < 4; k++)
               result[i, j] += this[i, k] * right[k, j];
      matrix = result;
      return this;
   }

   public Vector3 Multiply (Vector3 vec) {
      double[,] result = new double[4, 1];
      double[,] rhs = new double[4, 1]; rhs[0, 0] = vec.X; rhs[1, 0] = vec.Y; rhs[2, 0] = vec.Z; rhs[3, 0] = 0.0;
      double mult;
      for (int i = 0; i < 4; i++) {
         for (int j = 0; j < 1; j++) {
            for (int k = 0; k < 4; k++) {
               mult = this[i, k] * rhs[k, j];
               result[i, j] += mult;
            }
         }
      }
      return new Vector3 (result[0, 0], result[1, 0], result[2, 0]);
   }

   public Vector3 Multiply (Point3 point) {
      var vec = Geom.P2V (point);
      double[,] result = new double[4, 1];
      double[,] rhs = new double[4, 1]; rhs[0, 0] = vec.X; rhs[1, 0] = vec.Y; rhs[2, 0] = vec.Z; rhs[3, 0] = 1.0;
      for (int i = 0; i < 4; i++) {
         for (int j = 0; j < 1; j++) {
            for (int k = 0; k < 4; k++)
               result[i, j] += this[i, k] * rhs[k, j];
         }
      }
      return new Vector3 (result[0, 0], result[1, 0], result[2, 0]);
   }

   void Identify () {
      for (int i = 0; i < 4; i++)
         for (int j = 0; j < 4; j++)
            matrix[i, j] = i == j ? 1.0 : 0.0;
   }

   void Zero () {
      for (int i = 0; i < 4; i++)
         for (int j = 0; j < 4; j++)
            matrix[i, j] = 0.0;
   }

   void RotationalTranspose () {
      for (int i = 0; i < 3; i++)
         for (int j = i + 1; j < 3; j++)
            matrix[j, i] = matrix[i, j];
   }

   public XForm4 Invert () {
      RotationalTranspose ();
      double[] p = [0, 0, 0];
      for (int i = 0; i < 3; i++)
         for (int j = 0; j < 3; j++)
            p[i] += matrix[i, j] * matrix[j, 3];
      for (int i = 0; i < 3; i++)
         matrix[i, 3] = -p[i];
      return this;
   }

   public XForm4 Rotate (EAxis ax, double angle /*Degrees*/) {
      XForm4 rotateXForm = GetRotationXForm (ax, angle);
      this.matrix = (this * rotateXForm).matrix;
      return this;
   }
   public XForm4 RotateNew (EAxis ax, double angle /*Degrees*/) {
      XForm4 rotMat;
      XForm4 rotateXForm = GetRotationXForm (ax, angle);
      rotMat = (this * rotateXForm);
      return rotMat;
   }

   /// <summary>
   /// Rotates point P by angle theta (radians) about an axis A that passes through point Center
   /// and is directed by the **unit** vector A.  
   /// Positive theta = counter-clockwise when looking **against** the ray A
   /// (i.e. thumb along A → fingers curl in the positive direction – right-hand rule).
   /// </summary>
   public static Point3 AxisRotation (Vector3 Axis, Point3 Center, Point3 P, double theta) {
      // ---- 1. Pre-compute trig values ---------------------------------
      Axis = Axis.Normalized ();
      double c = Math.Cos (theta);
      double s = Math.Sin (theta);
      double v = 1.0 - c;

      double ax = Axis.X, ay = Axis.Y, az = Axis.Z;   // Axis is already normalized

      // ---- 2. Build the 3×3 rotation matrix (Rodrigues) ----------------
      double R11 = c + ax * ax * v;
      double R12 = ax * ay * v - az * s;
      double R13 = ax * az * v + ay * s;

      double R21 = ax * ay * v + az * s;
      double R22 = c + ay * ay * v;
      double R23 = ay * az * v - ax * s;

      double R31 = ax * az * v - ay * s;
      double R32 = ay * az * v + ax * s;
      double R33 = c + az * az * v;

      // ---- 3. Vector from axis point Center to the point P ------------------
      double px = P.X - Center.X;
      double py = P.Y - Center.Y;
      double pz = P.Z - Center.Z;

      // ---- 4. Apply rotation to that vector ---------------------------
      double rx = R11 * px + R12 * py + R13 * pz;
      double ry = R21 * px + R22 * py + R23 * pz;
      double rz = R31 * px + R32 * py + R33 * pz;

      // ---- 5. Translate back by Center ------------------------------------
      double pxPrime = rx + Center.X;
      double pyPrime = ry + Center.Y;
      double pzPrime = rz + Center.Z;

      return new Point3 (pxPrime, pyPrime, pzPrime);
   }
   #endregion

   #region Matrix Copy Manipulation
   public XForm4 MultiplyNew (XForm4 right) {
      double[,] result = new double[4, 4];
      for (int i = 0; i < 4; i++)
         for (int j = 0; j < 4; j++)
            for (int k = 0; k < 4; k++)
               result[i, j] += this[i, k] * right[k, j];
      return new XForm4 (result);
   }
   public XForm4 InvertNew () {
      XForm4 resMat = new (this);
      resMat.Invert ();
      return resMat;
   }

   public static XForm4 GetRotationXForm (EAxis ax, double angle /*Degrees*/) {
      XForm4 rot = new ();
      switch (ax) {
         case EAxis.X:
            rot[1, 1] = Math.Cos (angle.D2R ());
            rot[1, 2] = -Math.Sin (angle.D2R ());
            rot[2, 1] = Math.Sin (angle.D2R ());
            rot[2, 2] = Math.Cos (angle.D2R ());
            break;
         case EAxis.Y:
            rot[0, 0] = Math.Cos (angle.D2R ());
            rot[0, 2] = Math.Sin (angle.D2R ());
            rot[2, 0] = -Math.Sin (angle.D2R ());
            rot[2, 2] = Math.Cos (angle.D2R ());
            break;
         case EAxis.Z:
            rot[0, 0] = Math.Cos (angle.D2R ());
            rot[0, 1] = -Math.Sin (angle.D2R ());
            rot[1, 0] = Math.Sin (angle.D2R ());
            rot[1, 1] = Math.Cos (angle.D2R ());
            break;
         default:
            break;
      }
      return rot;
   }
   #endregion

   #region Operator overloaders
   public static XForm4 operator * (XForm4 a, XForm4 b) => a.MultiplyNew (b);
   public static Vector3 operator * (XForm4 xf, Point3 pt) => xf.Multiply (pt);
   public static Vector3 operator * (XForm4 xf, Vector3 v3) => xf.Multiply (v3);
   #endregion
}

