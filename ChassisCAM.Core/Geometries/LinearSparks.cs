using ChassisCAM.Core;
using Flux.API;
using static ChassisCAM.Core.Geometries.Geom;

namespace ChassisCAM.Core.Geometries {
   /// <summary>
   /// The LinearSparks class is to represent the sparks that emanate 
   /// when laser cutting operation is performed.
   /// </summary>
   public class LinearSparks {
      public List<Point3> Points { get; set; } = [];
      public List<List<Line3D>> Sparks { get; set; } = [];

      /// <summary>
      /// The constructor to the LinearSparks takes the following
      /// </summary>
      /// <param name="summit">The Tip of the cone within which the sparks modeled as a set of lines shall exist.
      /// The summit is the tip of the tool</param>
      /// <param name="sparkDir">This is the direction of the sparks. Ideally, this shall be the vector from summit
      /// to the center of the circle, which is the base of the cone.</param>
      /// <param name="rc">This is the radius of the cone at its base.</param>
      /// <param name="lc">This is the length of the segment that connects the summit and the center of the base 
      /// circle of the cone.</param>
      /// <param name="nSparksPerSet">In order to give a lively spark, a set of sparks are created from summit
      /// towards the base of the circle, with length less than or equal to the length</param>
      /// <param name="nSets">No of such sets with each set having nSparksPerSet</param>
      public LinearSparks (Point3 summit, Vector3 sparkDir, double rc, double lc, int nSparksPerSet, int nSets) {
         Random random = new ();

         Points.Add (summit); // Add summit point
         int summitIndex = 0; // Index of the summit

         for (int i = 0; i < nSets; i++) { // Two sets of N lines
            List<Line3D> sparkSet = [];

            for (int j = 0; j < nSparksPerSet; j++) {
               double randomLength = random.NextDouble () * lc;
               Point3 randomPointOnBase = GenerateRandomPointOnBase (summit, sparkDir, rc, lc, random);

               Vector3 direction = new Vector3 (
                   randomPointOnBase.X - summit.X,
                   randomPointOnBase.Y - summit.Y,
                   randomPointOnBase.Z - summit.Z
               ).Normalized ();

               Point3 endPoint = new (
                   summit.X + direction.X * randomLength,
                   summit.Y + direction.Y * randomLength,
                   summit.Z + direction.Z * randomLength
               );

               Points.Add (endPoint);
               int endPointIndex = Points.Count - 1;
               sparkSet.Add (new Line3D (summitIndex, endPointIndex));
            }

            Sparks.Add (sparkSet);
         }
      }

      Point3 GenerateRandomPointOnBase (Point3 summit, Vector3 normal, double rc, double lc, Random random) {
         double theta = random.NextDouble () * 2 * Math.PI;
         double radius = Math.Sqrt (random.NextDouble ()) * rc; // Uniform distribution inside the circle

         Vector3 baseCenter = new (
             summit.X + normal.X * lc,
             summit.Y + normal.Y * lc,
             summit.Z + normal.Z * lc
         );

         Vector3 perpendicularVector = FindPerpendicularVector (normal);
         Vector3 anotherPerpendicular = normal.Cross (perpendicularVector);

         return new Point3 (
             baseCenter.X + radius * Math.Cos (theta) * perpendicularVector.X + radius * Math.Sin (theta) * anotherPerpendicular.X,
             baseCenter.Y + radius * Math.Cos (theta) * perpendicularVector.Y + radius * Math.Sin (theta) * anotherPerpendicular.Y,
             baseCenter.Z + radius * Math.Cos (theta) * perpendicularVector.Z + radius * Math.Sin (theta) * anotherPerpendicular.Z
         );
      }

      Vector3 FindPerpendicularVector (Vector3 normal) {
         if (Math.Abs (normal.X) < Math.Abs (normal.Y))
            return new Vector3 (0, -normal.Z, normal.Y).Normalized ();
         return new Vector3 (-normal.Z, 0, normal.X).Normalized ();
      }
   }
}