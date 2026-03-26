using System.Windows.Threading;
using ChassisCAM.Core;
using ChassisCAM.Core.Geometries;
using Flux.API;

namespace ChassisCAM.Presentation.Tools {
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
      public void Draw (
         (XForm4 XForm, Point3 WayPt, EMove MoveType)? trfTool0,
         (XForm4 XForm, Point3 WayPt, EMove MoveType)? trfTool1,
         Dispatcher dispatcher) {
         List<Point3> t0ToolCylTrfPts = [],
            t1ToolCylTrfPts = [], t0ToolToolTipTrfPts = [], t1ToolToolTipTrfPts = [],
         t0CompLSPts = [], t1CompLSPts = [];

         // Tool 0 
         if (trfTool0 != null) {
            var t0Trf = trfTool0.Value.XForm;
            var normalTool0 = trfTool0.Value.XForm.ZCompRot;
            var tipTool0 = trfTool0.Value.WayPt;

            // Tool 0 Linear Sparks
            if (trfTool0.Value.MoveType == EMove.Machining) {
               mLineSparks = new (tipTool0, normalTool0 * -1, rc: 5.0, lc: 200, nSparksPerSet: 50, nSets: 2);
               foreach (var sparks in mLineSparks.Sparks) {
                  foreach (var line in sparks) {
                     var p1 = mLineSparks.Points[line.A]; var p2 = mLineSparks.Points[line.B];
                     t0CompLSPts.AddRange ([p1, p2]);
                  }
               }
               if (t0CompLSPts.Count > 0)
                  t0CompLSPts.AddRange ([t0CompLSPts[^1], t0CompLSPts[^1]]);
            }

            // Tool 0 Tool Head
            foreach (var trg in mCylinder.Triangles) {
               var p1 = mCylinder.Points[trg.A]; var p2 = mCylinder.Points[trg.B];
               var p3 = mCylinder.Points[trg.C];
               Point3 xFormP1, xFormP2, xFormP3;
               if (t0Trf != null) {
                  xFormP1 = Geom.V2P (t0Trf * p1); xFormP2 = Geom.V2P (t0Trf * p2);
                  xFormP3 = Geom.V2P (t0Trf * p3);
                  t0ToolCylTrfPts.AddRange ([xFormP1, xFormP2, xFormP3]);
               }
            }

            // Tool 0 Tip
            foreach (var trg in mFCone.Triangles) {
               var p1 = mFCone.Points[trg.A]; var p2 = mFCone.Points[trg.B];
               var p3 = mFCone.Points[trg.C];
               Point3 xFormP1, xFormP2, xFormP3;
               if (t0Trf != null) {
                  xFormP1 = Geom.V2P (t0Trf * p1); xFormP2 = Geom.V2P (t0Trf * p2);
                  xFormP3 = Geom.V2P (t0Trf * p3);
                  t0ToolToolTipTrfPts.AddRange ([xFormP1, xFormP2, xFormP3]);
               }
            }
         }

         // Tool 1
         if (trfTool1 != null) {
            var t1Trf = trfTool1.Value.XForm;
            var normalTool1 = trfTool1.Value.XForm.ZCompRot;
            var tipTool1 = trfTool1.Value.WayPt;

            // Tool 1 Linear Sparks
            if (trfTool1.Value.MoveType == EMove.Machining) {
               mLineSparks = new (tipTool1, normalTool1 * -1, rc: 5.0, lc: 200, nSparksPerSet: 50, nSets: 2);
               foreach (var sparks in mLineSparks.Sparks) {
                  foreach (var line in sparks) {
                     var p1 = mLineSparks.Points[line.A]; var p2 = mLineSparks.Points[line.B];
                     t1CompLSPts.AddRange ([p1, p2]);
                  }
               }
               if (t1CompLSPts.Count > 0)
                  t1CompLSPts.AddRange ([t1CompLSPts[^1], t1CompLSPts[^1]]);
            }

            // Tool 1 Tool Head
            foreach (var trg in mCylinder.Triangles) {
               var p1 = mCylinder.Points[trg.A]; var p2 = mCylinder.Points[trg.B];
               var p3 = mCylinder.Points[trg.C];
               Point3 xFormP1, xFormP2, xFormP3;

               if (trfTool1 != null) {
                  xFormP1 = Geom.V2P (t1Trf * p1); xFormP2 = Geom.V2P (t1Trf * p2);
                  xFormP3 = Geom.V2P (t1Trf * p3);
                  t1ToolCylTrfPts.AddRange ([xFormP1, xFormP2, xFormP3]);
               }
            }

            // Tool 1 Tool Tip
            foreach (var trg in mFCone.Triangles) {
               var p1 = mFCone.Points[trg.A]; var p2 = mFCone.Points[trg.B];
               var p3 = mFCone.Points[trg.C];
               Point3 xFormP1, xFormP2, xFormP3;

               if (t1Trf != null) {
                  xFormP1 = Geom.V2P (t1Trf * p1); xFormP2 = Geom.V2P (t1Trf * p2);
                  xFormP3 = Geom.V2P (t1Trf * p3);
                  t1ToolToolTipTrfPts.AddRange ([xFormP1, xFormP2, xFormP3]);
               }
            }
         }

         if (t0CompLSPts.Count > 0) {
            dispatcher.Invoke (() => {
               Lux.HLR = true;
               Lux.Color = Utils.SteelCutingSparkColor2;
               Lux.Draw (EDraw.LineStrip, t0CompLSPts);
            });
         }

         if (t1CompLSPts.Count > 0) {
            dispatcher.Invoke (() => {
               Lux.HLR = true;
               Lux.Color = Utils.SteelCutingSparkColor2;
               Lux.Draw (EDraw.Lines, t1CompLSPts);
            });
         }


         if (t0ToolCylTrfPts.Count > 0) {
            dispatcher.Invoke (() => {
               Lux.HLR = true;
               Lux.Color = Utils.LHToolColor;
               Lux.Draw (EDraw.Triangle, t0ToolCylTrfPts);
            });
         }
         if (t1ToolCylTrfPts.Count > 0) {
            dispatcher.Invoke (() => {
               Lux.HLR = true;
               Lux.Color = Utils.RHToolColor;
               Lux.Draw (EDraw.Triangle, t1ToolCylTrfPts);
            });
         }
         if (t0ToolToolTipTrfPts.Count > 0) {
            dispatcher.Invoke (() => {
               Lux.HLR = true;
               Lux.Color = Utils.ToolTipColor2;
               Lux.Draw (EDraw.Triangle, t0ToolToolTipTrfPts);
            });
         }
         if (t1ToolToolTipTrfPts.Count > 0) {
            dispatcher.Invoke (() => {
               Lux.HLR = true;
               Lux.Color = Utils.ToolTipColor2;
               Lux.Draw (EDraw.Triangle, t1ToolToolTipTrfPts);
            });
         }

      }
      #endregion
   }
}