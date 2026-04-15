using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flux.API;

namespace ProfileCAM.Core.Geometries {
   public class FCLine3 : FCCurve3 {
      public FCLine3 (Point3 s, Point3 e) {
         Line = new Line3 (s, e);
         Length = Line.Length;
         Start = s;
         End = e;
         IsCircle = false;
         Curve = Line as Curve3;
      }
      public Line3 Arc { get; private set; }

      public Line3 Line { get; private set; }
      public override Point3 Start { get; set; }
      public override Point3 End { get; set; }
      public override double Length { get; set; }
      public override bool IsCircle { get; set; }
      public override Curve3 Curve { get; set; }
      public override FCCurve3 Clone () {
         FCLine3 arc = new (Start, End);
         return arc as FCCurve3;
      }
      public override FCCurve3 ReverseClone () {
         FCLine3 arc = new (End, Start);
         return arc as FCCurve3;
      }
   }
}
