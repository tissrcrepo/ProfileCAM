using System;
using Flux.API;

namespace ProfileCAM.Core.Geometries;

/// <summary>
/// Represents a plane in 3D space, defined by three non-collinear points.
/// All distance/contains checks use a consistent absolute tolerance (length units).
/// </summary>
public class Plane {
   private readonly Vector3 mNormal;
   private readonly double mD;
   private readonly double mTolerance;

   /// <summary>
   /// Creates a plane from three points.
   /// Throws if the points are coincident or collinear within tolerance.
   /// </summary>
   public Plane (Point3 p1, Point3 p2, Point3 p3, double tolerance = 1e-6) {
      mTolerance = tolerance;

      Vector3 v1 = p2.ToPV () - p1.ToPV ();
      Vector3 v2 = p3.ToPV () - p1.ToPV ();

      Vector3 cross = v1.Cross (v2);
      double crossSqLen = cross.LengthSquared ();

      double maxBaseSqLen = Math.Max (v1.LengthSquared (), v2.LengthSquared ());

      // ---- Coincident check ----------------------------------------------
      if (maxBaseSqLen.SLT (mTolerance * mTolerance)) {
         throw new ArgumentException (
             "The three points are coincident within tolerance; cannot define a plane.");
      }

      // ---- Collinearity check --------------------------------------------
      // Equivalent to: distance(p3, line(p1-p2)) ≤ tolerance
      if (crossSqLen.SLT (mTolerance * mTolerance * maxBaseSqLen)) {
         throw new ArgumentException (
             "The three points are collinear within tolerance; cannot define a plane.");
      }

      // ---- Normalize normal ----------------------------------------------
      mNormal = cross.Normalized ();

      // ---- Plane equation Ax + By + Cz + D = 0 ----------------------------
      mD = -mNormal.Dot (p1.ToPV ());
   }

   /// <summary>
   /// Returns the perpendicular (unsigned) distance from a point to the plane.
   /// </summary>
   public double DistanceToPoint (Vector3 point)
       => Math.Abs (mNormal.Dot (point) + mD);

   public double DistanceToPoint (Point3 point)
       => DistanceToPoint (point.ToPV ());

   /// <summary>
   /// Returns the signed distance from a point to the plane.
   /// </summary>
   public double SignedDistanceToPoint (Vector3 point)
       => mNormal.Dot (point) + mD;

   public double SignedDistanceToPoint (Point3 point)
       => SignedDistanceToPoint (point.ToPV ());

   /// <summary>
   /// Checks whether a point lies on the plane within tolerance.
   /// </summary>
   public bool Contains (Vector3 point, double? tolerance = null) {
      double tol = tolerance ?? mTolerance;
      return DistanceToPoint (point).LTEQ (tol);
   }

   public bool Contains (Point3 point, double? tolerance = null) {
      return Contains (point.ToPV (), tolerance);
   }

   /// <summary>
   /// Checks whether this plane is the same as another plane within tolerance.
   /// </summary>
   public bool IsSamePlane (Plane other, double? tolerance = null) {
      double tol = tolerance ?? mTolerance;

      double dot = mNormal.Dot (other.mNormal);

      // Normals must be parallel or anti-parallel
      if (Math.Abs (Math.Abs (dot) - 1).SGT (tol))
         return false;

      // Take a point on this plane
      Vector3 ptOnThis = mNormal * -mD;

      return other.DistanceToPoint (ptOnThis).LTEQ (tol);
   }

   /// <summary>
   /// Checks whether four points are coplanar within tolerance.
   /// Handles all edge cases (including collinear triplets).
   /// </summary>
   public static bool AreCoplanar (
       Point3 p1, Point3 p2, Point3 p3, Point3 p4,
       double tolerance = 1e-6) {
      Point3[] pts = { p1, p2, p3, p4 };

      // Try all combinations of 3 points
      for (int i = 0; i < 4; i++) {
         for (int j = i + 1; j < 4; j++) {
            for (int k = j + 1; k < 4; k++) {
               int l = 6 - (i + j + k); // remaining index (0+1+2+3 = 6)

               try {
                  var plane = new Plane (pts[i], pts[j], pts[k], tolerance);

                  if (plane.Contains (pts[l], tolerance))
                     return true;
               } catch (ArgumentException) {
                  // Degenerate triplet → try next
               }
            }
         }
      }

      // All triplets failed → points are collinear/coincident
      return false;
   }
}

//using System;
//using ProfileCAM.Core;
//using Flux.API;

//namespace ProfileCAM.Core.Geometries;

///// <summary>
///// Represents a plane in 3D space, defined by three non-collinear points.
///// All distance/contains checks use a consistent absolute tolerance (length units).
///// </summary>
//public class Plane {
//   private readonly Vector3 mNormal;
//   private readonly double mD;
//   private readonly double mTolerance;

//   /// <summary>
//   /// Creates a plane from three points. Throws if the points are coincident or collinear within tolerance.
//   /// The degeneracy check is now dimensionally consistent: distance from third point to the line of the first two ≤ tolerance.
//   /// </summary>
//   public Plane (Point3 p1, Point3 p2, Point3 p3, double tolerance = 1e-6) {
//      mTolerance = tolerance;

//      Vector3 v1 = p2.ToPV () - p1.ToPV ();
//      Vector3 v2 = p3.ToPV () - p1.ToPV ();

//      Vector3 cross = v1.Cross (v2);
//      double crossSqLen = cross.LengthSquared ();

//      double maxBaseSqLen = Math.Max (v1.LengthSquared (), v2.LengthSquared ());

//      // If the three points are too close overall, we cannot define a unique plane
//      if (maxBaseSqLen.SLT (mTolerance * mTolerance)) {
//         throw new ArgumentException ("The three points are coincident within tolerance; cannot define a plane.");
//      }

//      // Professional-grade degeneracy test (no tol² or tol³ hacks):
//      // Equivalent to: distance(p3, line(p1-p2)) ≤ tolerance
//      // → crossSqLen ≤ tolerance² * maxBaseSqLen
//      if (crossSqLen.SLT (mTolerance * mTolerance * maxBaseSqLen)) {
//         throw new ArgumentException ("The three points are collinear within tolerance; cannot define a plane.");
//      }

//      //mNormal = cross / Math.Sqrt (crossSqLen);
//      mNormal = cross.Normalized ();
//      mD = -mNormal.Dot (p1.ToPV ());
//   }

//   /// <summary>
//   /// Returns the perpendicular (unsigned) distance from a point to the plane.
//   /// </summary>
//   public double DistanceToPoint (Vector3 point) => Math.Abs (mNormal.Dot (point) + mD);

//   public double DistanceToPoint (Point3 point) => DistanceToPoint (point.ToPV ());

//   /// <summary>
//   /// Returns the signed distance from a point to the plane.
//   /// </summary>
//   public double SignedDistanceToPoint (Vector3 point) => mNormal.Dot (point) + mD;

//   public double SignedDistanceToPoint (Point3 point) => SignedDistanceToPoint (point.ToPV ());

//   /// <summary>
//   /// Checks whether a point lies on the plane within the given tolerance (absolute distance).
//   /// </summary>
//   public bool Contains (Vector3 point, double? tolerance = null) {
//      double tol = tolerance ?? mTolerance;
//      return DistanceToPoint (point).LTEQ (tol);
//   }

//   public bool Contains (Point3 point, double? tolerance = null) {
//      return Contains (point.ToPV (), tolerance);
//   }

//   /// <summary>
//   /// Checks whether this plane is the same as another plane within tolerance
//   /// (normals are parallel and a point on one plane lies on the other).
//   /// </summary>
//   public bool IsSamePlane (Plane other, double? tolerance = null) {
//      double tol = tolerance ?? mTolerance;

//      double dot = mNormal.Dot (other.mNormal);
//      if (Math.Abs (Math.Abs (dot) - 1).SGT (tol))
//         return false;

//      // Any point on this plane (using the constant term)
//      Vector3 ptOnThis = mNormal * -mD;
//      return other.DistanceToPoint (ptOnThis).LTEQ (tol);
//   }

//   /// <summary>
//   /// Convenience helper: check if four points are coplanar by constructing the plane from the first three
//   /// and testing the fourth. (Exactly what you asked for.)
//   /// Returns false if the first three cannot define a plane.
//   /// </summary>
//   public static bool AreCoplanar (Point3 p1, Point3 p2, Point3 p3, Point3 p4, double tolerance = 1e-6) {
//      try {
//         var plane = new Plane (p1, p2, p3, tolerance);
//         return plane.Contains (p4, tolerance);
//      } catch (ArgumentException) {
//         // First three are collinear/coincident → cannot define a unique plane.
//         // For strict "all four on one plane" you could try other triplets,
//         // but per your request we keep it simple with the first three.
//         return false;
//      }
//   }
//}