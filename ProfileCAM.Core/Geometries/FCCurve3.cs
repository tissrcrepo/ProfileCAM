using Flux.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProfileCAM.Core.Geometries {
   public abstract class FCCurve3 {
      public abstract Point3 Start { get; set; }
      public abstract Point3 End { get; set; }
      public abstract FCCurve3 Clone ();
      public abstract double Length { get; set; }
      public abstract bool IsCircle { get; set; }
      public abstract Curve3 Curve { get; set; }
      public abstract FCCurve3 ReverseClone ();
   }
}
