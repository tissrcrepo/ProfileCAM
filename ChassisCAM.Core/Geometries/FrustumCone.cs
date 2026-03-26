using Flux.API;
using static ChassisCAM.Core.Geometries.Geom;

namespace ChassisCAM.Core.Geometries {
   public class FrustumCone {
      readonly double mBottomRadius, mTopRadius, mHeight;
      readonly int mSegments = 50; // Fixed discretization
      public List<Point3> Points { get; set; } = [];
      public List<Triangle3D> Triangles { get; set; } = [];

      public FrustumCone (double bottomDiamater, double topDiameter, double height) {
         mBottomRadius = bottomDiamater / 2;
         mTopRadius = topDiameter / 2;
         mHeight = height;

         double angleStep = 2 * Math.PI / mSegments;
         double startZ = 0.0; // Bottom base Z-coordinate

         // Add points for the bottom base
         for (int i = 0; i < mSegments; i++) {
            double angle = i * angleStep;
            Points.Add (new Point3 (mBottomRadius * Math.Cos (angle), mBottomRadius * Math.Sin (angle), startZ));
         }

         // Add points for the top base
         for (int i = 0; i < mSegments; i++) {
            double angle = i * angleStep;
            Points.Add (new Point3 (mTopRadius * Math.Cos (angle), mTopRadius * Math.Sin (angle), startZ + mHeight));
         }

         // Create triangles for the bottom base
         int bottomCenterIndex = Points.Count;
         Points.Add (new Point3 (0, 0, startZ)); // Center of the bottom base
         for (int i = 0; i < mSegments; i++) {
            Triangles.Add (new Triangle3D (bottomCenterIndex, i, (i + 1) % mSegments));
         }

         // Create triangles for the top base
         int topCenterIndex = Points.Count;
         Points.Add (new Point3 (0, 0, startZ + mHeight)); // Center of the top base
         for (int i = 0; i < mSegments; i++) {
            Triangles.Add (new Triangle3D (topCenterIndex, mSegments + ((i + 1) % mSegments), mSegments + i));
         }

         // Create triangles for the lateral surface
         for (int i = 0; i < mSegments; i++) {
            int nextIndex = (i + 1) % mSegments;
            Triangles.Add (new Triangle3D (i, mSegments + i, nextIndex));
            Triangles.Add (new Triangle3D (nextIndex, mSegments + i, mSegments + nextIndex));
         }
      }
   }
}