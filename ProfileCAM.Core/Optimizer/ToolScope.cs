using Flux.API;
using ProfileCAM.Core.Geometries;

namespace ProfileCAM.Core.Optimizer;
public class ToolScope<T> where T : Tooling {
   public double StartX { get; set; }
   public double EndX { get; set; }
   public int Index { get; set; }
   public int IndexSx { get; set; }
   public int IndexEx { get; set; }
   public bool IsProcessed { get; set; } = false;
   public bool IsProcessCompleted { get; set; } = false;
   public T Tooling { get; set; }
   public List<ToolScope<T>> ToolScopeIxnsbyEndX { get; set; } = [];
   public PointVec StartPos () => new (this.Tooling.Segs[0].Curve.Start, this.Tooling.Segs[0].Vec0.Normalized ());
   public PointVec EndPos () => new  (this.Tooling.Segs[0].Curve.End, this.Tooling.Segs[0].Vec1.Normalized ());

   public ToolScope (Tooling Tlg, int idx, List<ToolingSegment> segs = null) {
      Tooling = (T)Tlg;
      Index = idx;
      IsProcessed = false;
      // Calculate StartX and EndX for this tooling
      segs ??= Tooling.Segs;
      StartX = double.MaxValue;
      EndX = double.MinValue;
      foreach (var seg in segs) {
         if (seg.Curve is FCLine3 line) {
            if (StartX > line.Start.X)
               StartX = line.Start.X;
            if (EndX < line.End.X)
               EndX = line.End.X;
         } else // FCArc3
            {
            FCArc3 arc = seg.Curve as FCArc3;
            var (cen, rad) = Geom.EvaluateCenterAndRadius (arc);
            var pt1X = cen.X - rad; var pt2X = cen.X + rad;
            var pt1 = new Point3 (pt1X, cen.Y, cen.Z);
            var pt2 = new Point3 (pt2X, cen.Y, cen.Z);

            if (Geom.IsPointOnCurve (seg.Curve, pt1, seg.Vec0)) {
               if (StartX > pt1.X)
                  StartX = pt1.X;
            }
            if (Geom.IsPointOnCurve (seg.Curve, pt2, seg.Vec0)) {
               if (EndX < pt2.X)
                  EndX = pt2.X;
            }
         }
      }
   }

   public ToolScope<T> Clone () {
      var clone = new ToolScope<T> (this.Tooling, this.Index) {
         StartX = this.StartX,
         EndX = this.EndX,
         IsProcessed = this.IsProcessed,
         IsProcessCompleted = this.IsProcessCompleted,
         ToolScopeIxnsbyEndX = this.ToolScopeIxnsbyEndX != null
        ? new List<ToolScope<T>> (this.ToolScopeIxnsbyEndX.Count)
        : []
      };

      if (this.ToolScopeIxnsbyEndX != null) {
         foreach (var ts in this.ToolScopeIxnsbyEndX) {
            clone.ToolScopeIxnsbyEndX.Add (ts.Clone ());
         }
      }

      return clone;
   }

   public double Length => Math.Abs (StartX - EndX);
}
