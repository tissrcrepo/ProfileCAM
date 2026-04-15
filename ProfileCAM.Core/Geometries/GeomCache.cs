//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using Flux.API;

//using System;
//using System.Runtime.CompilerServices;
//using System.Collections.Generic;


//namespace ProfileCAM.Core.Geometries;
//public sealed class ArcData {
//   public readonly Point3 Start;
//   public readonly Point3 IP1;
//   public readonly Point3 IP2;
//   public readonly Point3 End;

//   public ArcData (Point3 s, Point3 ip1, Point3 ip2, Point3 e) {
//      Start = s;
//      IP1 = ip1;
//      IP2 = ip2;
//      End = e;
//   }
//}




//public readonly struct ArcKey : IEquatable<ArcKey> {
//   private readonly Point3 _s;
//   private readonly Point3 _e;
//   private readonly Point3 _c;
//   private readonly double _r;
//   private readonly double _len;

//   private const double Tol = 1e-6;

//   public ArcKey (Point3 s, Point3 e, Point3 c, double r, double len) {
//      _s = s;
//      _e = e;
//      _c = c;
//      _r = r;
//      _len = len;
//   }

//   public bool Equals (ArcKey other) {
//      return PointEquals (_s, other._s)
//          && PointEquals (_e, other._e)
//          && PointEquals (_c, other._c)
//          && _r.EQ (other._r, Tol)
//          && _len.EQ (other._len, Tol);
//   }

//   public override bool Equals (object obj)
//       => obj is ArcKey other && Equals (other);

//   public override int GetHashCode () {
//      var hc = new HashCode ();

//      hc.Add (_s.X); hc.Add (_s.Y); hc.Add (_s.Z);
//      hc.Add (_e.X); hc.Add (_e.Y); hc.Add (_e.Z);
//      hc.Add (_c.X); hc.Add (_c.Y); hc.Add (_c.Z);
//      hc.Add (_r);
//      hc.Add (_len);

//      return hc.ToHashCode ();
//   }

//   private static bool PointEquals (Point3 a, Point3 b) {
//      return a.X.EQ (b.X, Tol)
//          && a.Y.EQ (b.Y, Tol)
//          && a.Z.EQ (b.Z, Tol);
//   }
//}

//public sealed class GeomCache {
//   public static GeomCache It { get; } = new GeomCache ();

//   private readonly Dictionary<ArcKey, ArcData> _arcCache = new ();

//   private GeomCache () { }

//   public Arc3 CreateArc (Point3 start, Point3 ip1, Point3 ip2, Point3 end) {
//      var arc = new FArc3 (start, ip1, ip2, end);

//      var key = new ArcKey (
//          arc.Start,
//          arc.End,
//          arc.Center (),
//          arc.Radius (),
//          arc.Length
//      );

//      _arcCache[key] = new ArcData (start, ip1, ip2, end);

//      return arc;
//   }

//   public bool TryGetPoints (
//       Arc3 arc,
//       out Point3 start,
//       out Point3 ip1,
//       out Point3 ip2,
//       out Point3 end) {
//      var key = new ArcKey (
//          arc.Start,
//          arc.End,
//          arc.Center (),
//          arc.Radius (),
//          arc.Length
//      );

//      if (_arcCache.TryGetValue (key, out var data)) {
//         start = data.Start;
//         ip1 = data.IP1;
//         ip2 = data.IP2;
//         end = data.End;
//         return true;
//      }

//      start = ip1 = ip2 = end = default;
//      return false;
//   }
//}

//#nullable enable
////public static class Arc3Factory {
////   public static Arc3? Create (Point3 start, Point3 ip1, Point3 ip2, Point3 end) {
////      Point3 p1 = new (56.323005800970677,
////                        57.223022239345205,
////                        5.00000000000000);
////      Point3 p2 = new (69.999999999999972,
////                        101.95845451799758,
////                        5.00000000000000);
////      if (start.EQ (p1) && end.EQ (p2)) {
////         int aa = 0;
////         aa++;
////      }
////      var arc =  GeomCache.It.CreateArc (start, ip1, ip2, end);
////      bool success = Arc3Factory.TryGetPoints (arc, out var s, out var iip1, out var iip2, out var e);
////      if ( !success) {
////         int aa = 0;
////         ++aa;
////         return null;
////      } else {
////         return arc;
////      }
////   }

////   public static bool TryGetPoints (
////       Arc3 arc,
////       out Point3 start,
////       out Point3 ip1,
////       out Point3 ip2,
////       out Point3 end) {
////      return GeomCache.It.TryGetPoints (arc, out start, out ip1, out ip2, out end);
////   }
////}
//public static class Arc3Factory {
//   public static Arc3 Create (Point3 s, Point3 ip1, Point3 ip2, Point3 e) {
//      return GeomCache.It.CreateArc (s, ip1, ip2, e);
//   }

//   public static bool TryGetPoints (
//       Arc3 arc,
//       out Point3 s,
//       out Point3 ip1,
//       out Point3 ip2,
//       out Point3 e) {
//      return GeomCache.It.TryGetPoints (arc, out s, out ip1, out ip2, out e);
//   }
//}