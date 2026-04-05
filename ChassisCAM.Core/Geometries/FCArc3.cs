using Flux.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;

namespace ChassisCAM.Core.Geometries;
/// <summary>
/// Wrapper over Flux Arc3
/// </summary>
public class FCArc3 {
   public Arc3 Arc { get; set; }
   Point3 mStart, mEnd, mIp1, mIp2;
   (Point3 Start, Point3 IntP1, Point3 IntP2, Point3 End) GetGenPoints () => (mStart, mIp1, mIp2, mEnd);
   public Point3 Center { get; private set; }
   public double Radius { get; private set; }
   public Point3 Start { get; private set; }
   public Point3 End { get; private set; }
   public double Length { get; private set; }
   public FCArc3 (Point3 s, Point3 i1, Point3 i2, Point3 e) {
      Arc = new Arc3 (s, i1, i2, e);
      (Center, Radius) = Geom.EvaluateCenterAndRadius (Arc);
      Start = s;
      End = e;
      Length = Arc.Length;
      mIp1 = i1;
      mIp2 = i2;
   }
   public IEnumerable<Point3> Discretize (double error) => Arc.Discretize (error);
   public FCArc3 Clone () => new (Start, mIp1, mIp2, End);
}

