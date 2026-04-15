using Flux.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProfileCAM.Core;
using System.CodeDom;


namespace ProfileCAM.Core.Geometries {
   public struct FArc3 {
      public Point3 Cen { get; set; }
      public double Rad { get; set; }
      public Vector3 PNormal { get; set; }
      public Vector3 ArcNormal { get; set; }
      public Utils.EArcSense ArcSense { get; set; }
      public double ArcAngle { get; set; }
      public Point3 Start { get; set; }
      public Point3 End { get; set; }
      public bool IsSemicircular { get; set; } = false;
      public Utils.ArcType ArcType { get; set; }
      public bool IsCircle { get; set; } = false;

      public FArc3 (
    Point3 sp,
    Point3 ip1,
    Point3 ip2,
    Point3 ep,
    Vector3 planeNormal,
    double tol = 1e-6) {
         // -----------------------------
         // Basic validation
         // -----------------------------

         if (sp.EQ (ip1, tol) || ip1.EQ (ip2, tol) || ip2.EQ (ep, tol) ||
             sp.EQ (ip2, tol) || ip1.EQ (ep, tol))
            throw new ArgumentException ("Points are not unique");

         if (planeNormal.Dot (planeNormal).EQ (0, tol * tol))
            throw new ArgumentException ("Plane normal is zero");

         if (!Geom.AreCoplanar (sp, ip1, ip2, ep, planeNormal, tol))
            throw new ArgumentException ("Points are not coplanar");

         if (Geom.AreCollinear (sp, ip1, ip2, tol) ||
             Geom.AreCollinear (ip1, ip2, ep, tol))
            throw new ArgumentException ("Points are collinear");

         // -----------------------------
         // Circle computation
         // -----------------------------

         (Cen, Rad) = Geom.EvaluateCenterAndRadius (sp, ip1, ip2);
         var (cen2, rad2) = Geom.EvaluateCenterAndRadius (ip1, ip2, ep);

         if (!Cen.EQ (cen2, tol) || !Rad.EQ (rad2, tol))
            throw new ArgumentException ("Points do not lie on same circle");

         // -----------------------------
         // Canonical start / end
         // -----------------------------

         Start = Cen + (sp - Cen).Normalized () * Rad;
         End = Cen + (ep - Cen).Normalized () * Rad;

         PNormal = planeNormal.Normalized ();

         // -----------------------------
         // Determine arc orientation
         // -----------------------------

         var c2ip1 = ip1 - Cen;
         var c2ip2 = ip2 - Cen;

         double orient = PNormal.Dot (c2ip1.Cross (c2ip2));

         if (Math.Abs (orient) < tol)
            throw new ArgumentException ("Arc orientation undefined");

         ArcSense = orient > 0
             ? Utils.EArcSense.CCW
             : Utils.EArcSense.CW;

         ArcNormal = PNormal;

         // -----------------------------
         // Circle case
         // -----------------------------

         if (sp.EQ (ep, tol)) {
            IsCircle = true;

            ArcAngle = ArcSense == Utils.EArcSense.CCW
                ? Math.Tau
                : -Math.Tau;

            ArcType = Utils.ArcType.Major;
            IsSemicircular = false;

            return;
         }

         // -----------------------------
         // Arc angle
         // -----------------------------

         ArcAngle = AngleAtPoint (End, tol);

         double absAngle = Math.Abs (ArcAngle);

         IsSemicircular = absAngle.EQ (Math.PI, tol);

         ArcType =
             absAngle.SLT (Math.PI, tol)
             ? Utils.ArcType.Minor
             : (IsSemicircular
                 ? Utils.ArcType.Semicircular
                 : Utils.ArcType.Major);
      }

      public readonly double AngleAtPoint (Point3 pt, double tol = 1e-6) {
         if (!pt.DistTo (Cen).EQ (Rad, tol))
            throw new ArgumentException ("The point is not at Radius distance from center", nameof (pt));

         if (IsCircle && Start.EQ (pt, tol))
            return ArcSense == Utils.EArcSense.CCW ? Math.Tau : -Math.Tau;

         var c2s = Start - Cen;
         var c2p = pt - Cen;

         // --- Compute cosθ safely ---
         double cosTheta = c2s.Dot (c2p) / (Rad * Rad);
         cosTheta = Math.Max (-1.0, Math.Min (1.0, cosTheta));

         double theta = Math.Acos (cosTheta); // θ ∈ [0, π]

         // Start point is end point and arc is not circle.
         if (theta.EQ (0, tol))
            return 0;

         // if theta is PI.. ( Semicircular case )
         if (theta.EQ (Math.PI, tol)) {
            theta = ArcSense == Utils.EArcSense.CCW ? Math.PI : -Math.PI;
            return theta;
         }



         // For semicircular arcs
         //double signedAngle;
         //if (IsSemicircular) {
         //   signedAngle = theta;
         //   if (ArcSense == Utils.EArcSense.CCW) {
         //      if (signedAngle < 0)
         //         signedAngle += Math.Tau;
         //   } else{ // CW

         //      if (signedAngle > 0)
         //         signedAngle -= Math.Tau;
         //   }
         //   return signedAngle;
         //}

         // For all other arcs

         // For major or minor arc
         double signedAngle = Math.Atan2 (
             PNormal.Dot (c2s.Cross (c2p)),
             c2s.Dot (c2p)
         );

         // signedAngle is now in (-π, π]

         // We must match traversal direction:
         if (ArcSense == Utils.EArcSense.CCW) {
            if (signedAngle < 0)
               signedAngle += Math.Tau;
         } else // CW
           {
            if (signedAngle > 0)
               signedAngle -= Math.Tau;
         }

         return signedAngle;

      }
      //public FArc3 (Point3 sp, Point3 ip1, Point3 ip2, Point3 ep, Vector3 pNormal, double tol = 1e-6) {
      //   if (sp.EQ (ip1, tol) || ip1.EQ (ip2, tol) || ip2.EQ (ep, tol) || sp.EQ (ep, tol) ||
      //      sp.EQ (ip2, tol) || ip1.EQ (ep, tol))
      //      throw new ArgumentException ("Start point, end point or intermediate points are not unique");

      //   if (pNormal.Length.EQ (0, tol))
      //      throw new ArgumentException ("Plane normal is zero");

      //   if (!Geom.AreCoplanar (sp, ip1, ip2, ep, pNormal, tol))
      //      throw new Exception ("The input points to create the arc/circle are not coplanar");

      //   // Collinearity check
      //   if (Geom.AreCollinear (sp, ip1, ip2, tol) || Geom.AreCollinear (ip1, ip2, ep, tol) ||
      //      Geom.AreCollinear (sp, ip1, ep, tol) || Geom.AreCollinear (sp, ip2, ep, tol))
      //      throw new ArgumentException ("Points are collinear");

      //   // Unique arc test
      //   (Cen, Rad) = Geom.EvaluateCenterAndRadius (sp, ip1, ip2);
      //   var (cen, rad) = Geom.EvaluateCenterAndRadius (ip1, ip2, ep);
      //   if (!Cen.EQ (cen, tol) || !Rad.EQ (rad, tol))
      //      throw new Exception ("There is no arc possible through the given points");


      //   var c2s = sp - Cen; var c2e = ep - Cen;
      //   var c2Ip1 = ip1 - Cen; var c2Ip2 = ip2 - Cen;

      //   // Self correction for radius and start point with c2s as ref
      //   Start = Cen + c2s.Normalized () * Rad;
      //   End = Cen + c2e.Normalized () * Rad;
      //   ip1 = Cen + c2Ip1.Normalized () * Rad;
      //   ip2 = Cen + c2Ip2.Normalized () * Rad;
      //   c2s = Start - Cen;
      //   c2e = End - Cen;

      //   // Check if its a circle
      //   if (Start.EQ (End, tol)) {
      //      IsCircle = true;
      //      ArcSense = Utils.EArcSense.CCW;
      //      ArcAngle = 0;
      //      ArcType = Utils.ArcType.Major;
      //      return;
      //   }

      //   // Set Plane Normal
      //   PNormal = pNormal;

      //   // Calculate Arc sense
      //   var rawCross = Geom.Cross ((ip1 - sp), (ip2 - ip1));
      //   if (rawCross.Dot (rawCross).EQ (0, tol * tol))
      //      throw new Exception ("Arc orientation undefined");
      //   Vector3 segCross = rawCross.Normalized ();

      //   //// Calculate arc angle ( ACos)
      //   //var cosTheta = (c2s.Dot (c2e)) / (Rad * Rad);
      //   //cosTheta = Math.Max (-1.0, Math.Min (1.0, cosTheta));  // clamp
      //   //ArcAngle = Math.Acos (cosTheta);
      //   //if (ArcAngle.EQ (Math.PI, tol)) {
      //   //   IsSemicircular = true;
      //   //   ArcAngle = Math.PI;
      //   //}

      //   // Compute Arc Sense
      //   if (segCross.IsSameSense (pNormal)) {
      //      ArcSense = Utils.EArcSense.CCW;
      //   } else {
      //      ArcSense = Utils.EArcSense.CW;
      //   }

      //   // Assign Plane Normal
      //   ArcNormal = segCross;

      //   // Compute Arc Angle
      //   ArcAngle = AngleAtPoint (End, tol);

      //   // Compute Arc Type
      //   ArcType = Utils.ArcType.Minor;
      //   if ( ArcAngle.SGT(Math.PI) ||  ArcAngle.SLT(-Math.PI))
      //      ArcType = Utils.ArcType.Major;

      //   //// Compute Arc Angle, Arc Type
      //   //var s2eCross = c2s.Cross (c2e);
      //   //bool opposing = s2eCross.Dot (ArcNormal) < 0;
      //   //ArcType = Utils.ArcType.Minor;
      //   //if (ArcSense == Utils.EArcSense.CCW && opposing) {
      //   //   if (cosTheta.EQ (0, tol))
      //   //      ArcAngle = 3 * Math.PI / 2.0;
      //   //   else
      //   //      ArcAngle = Math.Tau - ArcAngle;
      //   //   ArcType = Utils.ArcType.Major;
      //   //} else if (ArcSense == Utils.EArcSense.CW && opposing) {
      //   //   if (cosTheta.EQ (0, tol))
      //   //      ArcAngle = -3 * Math.PI / 2.0;
      //   //   else
      //   //      ArcAngle = ArcAngle - Math.Tau;
      //   //   ArcType = Utils.ArcType.Major;
      //   //} else if (ArcSense == Utils.EArcSense.CW)
      //   //   ArcAngle = -ArcAngle;
      //}

      public readonly Point3 EvaluateAtParam (double t, double tol = 1e-6) {
         if (!t.LieWithin (0, 1, tol))
            throw new ArgumentException ("Arc parameter must lie in [0,1]");

         if (t.EQ (0, tol))
            return Start;

         if (t.EQ (1, tol))
            return End;

         double angle = t * ArcAngle;

         return XForm4.AxisRotation (
             PNormal,
             Cen,
             Start,
             angle
         );
      }

      public readonly List<FArc3> Split (double t, double tol = 1e-6) {
         if (!t.LieWithin (0, 1, tol))
            throw new ArgumentException ("Arc parameter must lie in [0,1]");

         if (t.EQ (0, tol) || t.EQ (1, tol))
            return [this];

         Point3 splitPt = EvaluateAtParam (t, tol);

         // First arc interior points
         Point3 ip11 = EvaluateAtParam (t / 3.0, tol);
         Point3 ip12 = EvaluateAtParam (2 * t / 3.0, tol);

         // Second arc interior points
         Point3 ip21 = EvaluateAtParam (t + (1 - t) / 3.0, tol);
         Point3 ip22 = EvaluateAtParam (t + 2 * (1 - t) / 3.0, tol);

         var arc1 = new FArc3 (Start, ip11, ip12, splitPt, PNormal, tol);
         var arc2 = new FArc3 (splitPt, ip21, ip22, End, PNormal, tol);

         return [arc1, arc2];
      }

      public (double, double) Domain () => (0, ArcAngle);
      //public double AngleAtPoint(Point3 pt, double tol = 1e-6) {
      //   var c2s = Start - Cen; var c2e = pt - Cen;

      //   // Calculate arc angle ( ACos)
      //   var cosTheta = (c2s.Dot (c2e)) / (Rad * Rad);
      //   cosTheta = Math.Max (-1.0, Math.Min (1.0, cosTheta));  // clamp
      //   var arcAngle = Math.Acos (cosTheta);
      //   if (arcAngle.EQ (Math.PI, tol))
      //      return Math.PI;

      //   // Compute Arc Angle, Arc Type
      //   var s2eCross = c2s.Cross (c2e);
      //   bool opposing = s2eCross.Dot (ArcNormal) < 0;
      //   ArcType = Utils.ArcType.Minor;
      //   if (ArcSense == Utils.EArcSense.CCW && opposing) {
      //      if (cosTheta.EQ (0, tol))
      //         arcAngle = 3 * Math.PI / 2.0;
      //      else
      //         arcAngle = Math.Tau - arcAngle;
      //      ArcType = Utils.ArcType.Major;
      //   } else if (ArcSense == Utils.EArcSense.CW && opposing) {
      //      if (cosTheta.EQ (0, tol))
      //         arcAngle = -3 * Math.PI / 2.0;
      //      else
      //         arcAngle = arcAngle - Math.Tau;
      //      ArcType = Utils.ArcType.Major;
      //   } else if (ArcSense == Utils.EArcSense.CW)
      //      arcAngle = -arcAngle;
      //   return arcAngle;

      //}
      public readonly bool IsPointOnArc (Point3 pt, double tol = 1e-6) {
         // Plane check
         if (!Geom.AreCoplanar (Cen, Start, End, pt, PNormal, tol))
            return false;

         // Radius check
         var v = pt - Cen;
         if (!v.Length.EQ (Rad, tol))
            return false;

         // Full circle
         if (IsCircle)
            return true;

         double angleAtPt = AngleAtPoint (pt, tol);

         // Orientation mismatch
         if (!angleAtPt.EQ (0, tol) &&
             Math.Sign (angleAtPt) != Math.Sign (ArcAngle))
            return false;

         double param = angleAtPt / ArcAngle;

         return param.LieWithin (0, 1, tol);
      }

      public readonly double ParamAtPoint (Point3 pt, double tol = 1e-6) {
         if (!IsPointOnArc (pt, tol))
            throw new ArgumentException ("Point is not on arc");

         double angle = AngleAtPoint (pt, tol);

         double t = angle / ArcAngle;

         if (t.EQ (0, tol))
            return 0;

         if (t.EQ (1, tol))
            return 1;

         return t;
      }

      public readonly List<FArc3> Split (Point3 pt, double tol = 1e-6) {
         if (!IsPointOnArc (pt, tol))
            throw new ArgumentException ("Point is not on arc", nameof (pt));

         if (pt.EQ (Start, tol) || pt.EQ (End, tol))
            return [this];

         double t = ParamAtPoint (pt, tol);

         if (!t.LieWithin (0, 1, tol))
            throw new ArgumentException (
                $"Point {pt} does not lie within the arc {this}");

         return Split (t, tol);
      }

      /// <summary>
      /// Finds the intersection points (0, 1, or 2) of the line formed by the intersection of two planes
      /// with the circle that contains the given circular arc.
      ///
      /// The circle lies in plane 1 (defined by arcPlaneNormal and circleCenter).
      /// The second plane is defined by intersectPlaneNormal and a point on it (intersectPlanePoint).
      ///
      /// - If the two planes are coincident (coplanar), throws ArgumentException as specified.
      /// - If the two planes are parallel but distinct, returns empty list (no intersection line).
      /// - Otherwise returns the 0, 1 (tangent), or 2 intersection points with the circle.
      ///
      /// NOTE: These points are intersections with the full circle.
      /// To restrict to the actual arc segment you must perform additional checks
      /// (e.g. using start/end angles, start/end points, or angular sweep of the arc).
      /// </summary>
      public static List<Vector3> FindPlaneIntersectionWithCircle (
    Vector3 circleCenter,
    double circleRadius,
    Vector3 arcPlaneNormal,
    Vector3 intersectPlaneNormal,
    Vector3 intersectPlanePoint,
    double EPSILON = 1e-6) {
         // Check radius - must be > 0
         if (circleRadius.LTEQ (0.0, EPSILON))
            throw new ArgumentException ("Circle radius must be positive.");

         Vector3 n1 = arcPlaneNormal.Normalized ();
         Vector3 n2 = intersectPlaneNormal.Normalized ();
         Vector3 C = circleCenter;
         double R = circleRadius;

         // Plane equations:
         // n1 · x = d1 (circle's plane)
         // n2 · x = d2 (intersecting plane)
         double d1 = n1.Dot (C);
         double d2 = n2.Dot (intersectPlanePoint);

         Vector3 D = n1.Cross (n2);           // direction of intersection line
         double D_len2 = D.LengthSquared ();

         // --- Check for parallel / coplanar planes ---
         if (D_len2.LTEQ (0.0, EPSILON * EPSILON)) {
            // Normals are (nearly) parallel
            if (Math.Abs (n2.Dot (C) - d2).LTEQ (EPSILON, EPSILON)) {
               // Planes are coincident
               throw new ArgumentException ("The given plane is coplanar with the plane containing the circle.");
            }
            // Parallel but distinct -> no intersection line
            return [];
         }

         // --- Compute a point P on the intersection line ---
         // Formula: P = [ (d1 * n2 - d2 * n1) × D ] / |D|^2
         Vector3 v = n2 * d1 - n1 * d2;
         Vector3 P = v.Cross (D) / D_len2;

         // --- Intersect the line P + t * D with the circle |X - C| = R ---
         Vector3 V = P - C;
         double a = D_len2;                          // D·D
         double b = 2.0 * V.Dot (D);
         double c = V.LengthSquared () - R * R;

         double discriminant = b * b - 4.0 * a * c;
         List<Vector3> points = [];

         // Check if discriminant is significantly negative (no real intersection)
         if (discriminant.SLT (0.0, EPSILON))
            return points; // no real intersection


         // Clamp discriminant to zero if it's within tolerance (to avoid Math.Sqrt of negative number)
         // This handles cases where discriminant is slightly negative due to floating-point error
         double safeDiscriminant = discriminant < 0 ? 0 : discriminant;
         double sqrtDiscriminant = Math.Sqrt (safeDiscriminant);

         // Check if discriminant is effectively zero (tangent case)
         if (sqrtDiscriminant.LTEQ (0.0, EPSILON)) {
            double t0 = (-b) / (2.0 * a);
            points.Add (P + D * t0);
         } else {
            // Two intersection points
            double t1 = (-b - sqrtDiscriminant) / (2.0 * a);
            double t2 = (-b + sqrtDiscriminant) / (2.0 * a);
            points.Add (P + D * t1);
            points.Add (P + D * t2);
         }

         return points;
      }
      //public readonly List<FArc3> Split (Point3 pt, double tol = 1e-6) {
      //   if (!IsPointOnArc (pt, tol))
      //      throw new Exception ($"{pt} is not on the arc");

      //   if (pt.EQ (Start, tol) || pt.EQ (Start, tol))
      //      return [this];

      //   var t = ParamAtPoint (pt, tol);
      //   return Split (t, tol);
      //   //var ip1 = EvaluateAtParam ((1 / 3) * angleAtPt, tol);
      //   //var ip2 = EvaluateAtParam ((2 / 3) * angleAtPt, tol);
      //   //var iEnd1 = EvaluateAtParam (angleAtPt, tol);
      //   //var arc1 = new FArc3 (Start, ip1, ip2, iEnd1, PNormal, tol);

      //   //var ip3Ang = angleAtPt + (1 / 3) * (ArcAngle - angleAtPt);
      //   //var ip4Ang = angleAtPt + (2 / 3) * (ArcAngle - angleAtPt);
      //   //if (ip3Ang.GTEQ (ArcAngle, tol))
      //   //   throw new Exception ($"INtermediate angle 3 {ip3Ang} is greater than ArcAngle {ArcAngle}");
      //   //if (ip4Ang.GTEQ (ArcAngle, tol))
      //   //   throw new Exception ($"INtermediate angle 4 {ip4Ang} is greater than ArcAngle {ArcAngle}");
      //   //var ip3 = EvaluateAtParam (ip3Ang, tol);
      //   //var ip4 = EvaluateAtParam (ip4Ang, tol);
      //   //var arc2 = new FArc3 (iEnd1, ip3, ip4, End, PNormal, tol);

      //   //return [arc1, arc2];
      //}
   }
}
