using ProfileCAM.Core.Geometries;
using Flux.API;

namespace ProfileCAM.Core.Tools {
   /// <summary>
   /// The class Nozzle contains the logic of the laser cutting tool nozzle.
   /// </summary>
   /// <param name="diameter">The diameter of the laser cutting tool nozzle</param>
   /// <param name="height">The height of the laser cutting tool nozzle.</param>
   /// <param name="segments">No. of segments prescribed to simulate</param>
   public class Nozzle (double diameter, double height, int segments, Point3? pt = null, Vector3? normal = null) {
      #region Data Members
      static readonly double mCylOffset = 15;
      readonly Cylinder mCylinder = new (diameter, height, segments, mCylOffset);
      readonly FrustumCone mFCone = new (1.0, diameter, mCylOffset);
      LinearSparks mLineSparks = (pt != null || normal != null)
       ? new LinearSparks (pt.Value, normal.Value, 5.0, 100, 50, 2)
       : null;

      #endregion

      #region Draw methods
      /// <summary>
      /// The Draw method renders ( using Flux API) the tool head (cylinder), tool tip (cone)
      /// and the laser cutting sparks (LinearSparks) for 2 tools (which ever is available)
      /// </summary>
      /// <param name="trfTool0">The content for tool 0 (left side tool: Transformation, tool tip point and machining type</param>
      /// <param name="trfTool1">The content for tool 0 (right side tool: Transformation, tool tip point and machining type</param>
      /// <param name="dispatcher">The Dispatcher to inject the draw call to</param>
      public (List<Point3> CylinderPtsT1, List<Point3> ToolTipPtsT1, List<Point3> LinearSparkPtsT1,
           List<Point3> CylinderPtsT2, List<Point3> ToolTipPtsT2, List<Point3> LinearSparkPtsT2)
       GenerateDrawData ((XForm4 XForm, Point3 WayPt, EMove MoveType)? trfTool0,
                        (XForm4 XForm, Point3 WayPt, EMove MoveType)? trfTool1) {
         // Initialize lists for each tool
         var t0ToolCylTrfPts = new List<Point3> ();
         var t1ToolCylTrfPts = new List<Point3> ();
         var t0ToolToolTipTrfPts = new List<Point3> ();
         var t1ToolToolTipTrfPts = new List<Point3> ();
         var t0CompLSPts = new List<Point3> ();
         var t1CompLSPts = new List<Point3> ();

         // Parallel execution of Tool 0 and Tool 1 processing
         Parallel.Invoke (
             () => ProcessTool (trfTool0, t0ToolCylTrfPts, t0ToolToolTipTrfPts, t0CompLSPts),
             () => ProcessTool (trfTool1, t1ToolCylTrfPts, t1ToolToolTipTrfPts, t1CompLSPts)
         );

         return (t0ToolCylTrfPts, t0ToolToolTipTrfPts, t0CompLSPts,
                 t1ToolCylTrfPts, t1ToolToolTipTrfPts, t1CompLSPts);
      }

      // Helper method to process each tool separately
      void ProcessTool ((XForm4 XForm, Point3 WayPt, EMove MoveType)? trfTool,
                               List<Point3> toolCylTrfPts, List<Point3> toolToolTipTrfPts, List<Point3> compLSPts) {
         if (trfTool == null) return;

         var transform = trfTool.Value.XForm;
         var normal = transform.ZCompRot;
         var tip = trfTool.Value.WayPt;

         // Process Linear Sparks
         if (trfTool.Value.MoveType == EMove.Machining) {
            var sparks = new LinearSparks (tip, normal * -1, rc: 5.0, lc: 200, nSparksPerSet: 50, nSets: 2);
            Parallel.ForEach (sparks.Sparks, sparkSet => {
               foreach (var line in sparkSet) {
                  var p1 = sparks.Points[line.A];
                  var p2 = sparks.Points[line.B];

                  lock (compLSPts) {
                     compLSPts.AddRange ([p1, p2]);
                  }
               }
            });

            if (compLSPts.Count > 0) {
               lock (compLSPts) {
                  compLSPts.AddRange ([compLSPts[^1], compLSPts[^1]]);
               }
            }
         }

         // Process Tool Head (Cylinder)
         Parallel.ForEach (mCylinder.Triangles, trg => {
            var p1 = mCylinder.Points[trg.A];
            var p2 = mCylinder.Points[trg.B];
            var p3 = mCylinder.Points[trg.C];

            var xFormP1 = Geom.V2P (transform * p1);
            var xFormP2 = Geom.V2P (transform * p2);
            var xFormP3 = Geom.V2P (transform * p3);

            lock (toolCylTrfPts) {
               toolCylTrfPts.AddRange ([xFormP1, xFormP2, xFormP3]);
            }
         });

         // Process Tool Tip (Cone)
         Parallel.ForEach (mFCone.Triangles, trg => {
            var p1 = mFCone.Points[trg.A];
            var p2 = mFCone.Points[trg.B];
            var p3 = mFCone.Points[trg.C];

            var xFormP1 = Geom.V2P (transform * p1);
            var xFormP2 = Geom.V2P (transform * p2);
            var xFormP3 = Geom.V2P (transform * p3);

            lock (toolToolTipTrfPts) {
               toolToolTipTrfPts.AddRange ([xFormP1, xFormP2, xFormP3]);
            }
         });
      }
      #endregion
   }
}