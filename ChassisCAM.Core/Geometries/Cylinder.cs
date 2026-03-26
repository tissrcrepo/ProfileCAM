using Flux.API;
using static ChassisCAM.Core.Geometries.Geom;

namespace ChassisCAM.Core.Geometries {
   /// <summary>
   /// The Cylinder class represents the laser cutting tool ( excluding tip)
   /// </summary>
   public class Cylinder {
      readonly double mDiameter, mHeight, mOffset;
      readonly int mSegments;
      public List<Point3> Points { get; set; } = [];
      public List<Triangle3D> Triangles { get; set; } = [];

      public Cylinder (double diameter, double height, int segments, double offset) {
         mDiameter = diameter;
         mHeight = height;
         mSegments = segments;
         mOffset = offset;

         double radius = mDiameter / 2.0;
         double angleStep = 2 * Math.PI / mSegments;
         double startZ = mOffset; // Starting Z coordinate based on the offset

         // Add points for the bottom base
         for (int i = 0; i <= mSegments; i++) {
            double angle = i * angleStep;
            Points.Add (new Point3 (radius * Math.Cos (angle), radius * Math.Sin (angle), startZ));
         }

         // Add points for the top base
         for (int i = 0; i < mSegments; i++) {
            double angle = i * angleStep;
            Points.Add (new Point3 (radius * Math.Cos (angle), radius * Math.Sin (angle), startZ + mHeight));
         }

         // Create triangles for the bottom base
         int bottomCenterIndex = Points.Count;
         Points.Add (new Point3 (0, 0, startZ)); // Center of the bottom base
         for (int i = 0; i < mSegments; i++) Triangles.Add (new Triangle3D (bottomCenterIndex, i, (i + 1) % mSegments));

         // Create triangles for the top base
         int topCenterIndex = Points.Count;
         Points.Add (new Point3 (0, 0, startZ + mHeight)); // Center of the top base
         for (int i = 0; i < mSegments; i++) Triangles.Add (new Triangle3D (topCenterIndex, mSegments + (i + 1) % mSegments, mSegments + i));

         // Create triangles for the lateral surface
         for (int i = 0; i < mSegments; i++) {
            int nextIndex = (i + 1) % mSegments;
            Triangles.Add (new Triangle3D (i, mSegments + i, nextIndex));
            Triangles.Add (new Triangle3D (nextIndex, mSegments + i, mSegments + nextIndex));
         }
      }
   }
}