using Flux.API;
using ChassisCAM.Core.Geometries;

namespace ChassisCAM.Core.Optimizer;
public struct ToolScope<T> where T : Tooling {
   public double StartX { get; set; }
   public double EndX { get; set; }
   public int Index { get; set; }
   public bool IsProcessed { get; set; }

   public T Tooling { get; set; }

   public ToolScope (Tooling Tlg, int idx, List<ToolingSegment> segs = null) {
      Tooling = (T)Tlg;
      Index = idx;
      IsProcessed = false;
      // Calculate StartX and EndX for this tooling
      segs ??= Tooling.Segs;
      StartX = double.MaxValue;
      EndX = double.MinValue;
      foreach (var seg in segs) {
         if (seg.Curve is Line3 line) {
            if (StartX > line.Start.X)
               StartX = line.Start.X;
            if (EndX < line.End.X)
               EndX = line.End.X;
         } else // Arc3
            {
            Arc3 arc = seg.Curve as Arc3;
            var (cen, rad) = Geom.EvaluateCenterAndRadius (arc);
            var pt1X = cen.X - rad; var pt2X = cen.X + rad;
            var pt1 = new Point3 (pt1X, cen.Y, cen.Z);
            var pt2 = new Point3 (pt2X, cen.Y, cen.Z);
            if (Geom.IsPointOnCurve (seg.Curve, pt1, seg.Vec0)) {
               if (StartX > pt1.X)
                  StartX = pt1.X;
               if (EndX < pt1.X)
                  EndX = pt1.X;
            }else if ( Geom.IsPointOnCurve(seg.Curve, pt2, seg.Vec0)) {
               if (StartX > pt2.X)
                  StartX = pt2.X;
               if (EndX < pt2.X)
                  EndX = pt2.X;
            }
         }
      }
   }

   public readonly double Length => Math.Abs (StartX - EndX);
}
