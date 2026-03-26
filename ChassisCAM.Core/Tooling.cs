using System.Diagnostics;
using ChassisCAM.Core.GCodeGen;
using ChassisCAM.Core.Geometries;
using Flux.API;

namespace ChassisCAM.Core;
public readonly struct PointVec {
   public PointVec (Point3 pt, Vector3 vec) => (Pt, Vec) = (pt, vec);
   public readonly Point3 Pt;
   public readonly Vector3 Vec;
   public readonly Point3 Lift (double offset) => Pt + Vec * offset;
   public readonly double DistTo (PointVec rhs) => Pt.DistTo (rhs.Pt);
}

public enum EKind { Hole, Notch, Mark, Cutout, None };
public enum ECutKind {
   Top2YNeg, YNegToYPos, Top,
   YPos, YNeg, Top2YPos, YPosFlex, YNegFlex, None
};
public struct ToolingSegment {
   Curve3 mCurve;
   Vector3 mVec0;
   Vector3 mVec1;
   bool mIsValid = true;
   public ToolingSegment ((Curve3, Vector3, Vector3) vtSeg, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = Geom.CloneCurve (vtSeg.Item1, vtSeg.Item2);
      else mCurve = vtSeg.Item1;
      mVec0 = vtSeg.Item2;
      mVec1 = vtSeg.Item3;
      NotchSectionType = NotchSectionType.None;
   }
   public ToolingSegment ((Curve3, Vector3, Vector3, NotchSectionType) vtSeg, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = Geom.CloneCurve (vtSeg.Item1, vtSeg.Item2);
      else mCurve = vtSeg.Item1;
      mVec0 = vtSeg.Item2;
      mVec1 = vtSeg.Item3;
      NotchSectionType = vtSeg.Item4;
   }
   public ToolingSegment (Curve3 crv, Vector3 vec0, Vector3 vec1, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = Geom.CloneCurve (crv, vec0);
      else mCurve = crv;
      mVec0 = vec0;
      mVec1 = vec1;
      NotchSectionType = NotchSectionType.None;
   }
   public ToolingSegment (Curve3 crv, Vector3 vec0, Vector3 vec1, NotchSectionType nsectionType, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = Geom.CloneCurve (crv, vec0);
      else mCurve = crv;
      mVec0 = vec0;
      mVec1 = vec1;
      NotchSectionType = nsectionType;
   }
   public ToolingSegment (ToolingSegment rhs, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = Geom.CloneCurve (rhs.mCurve, rhs.mVec0);
      else mCurve = rhs.Curve;
      mVec0 = rhs.mVec0;
      mVec1 = rhs.mVec1;
      NotchSectionType = rhs.NotchSectionType;
   }
   public readonly void Deconstruct (out Curve3 curve, out Vector3 vec0, out Vector3 vec1) {
      curve = this.Curve;
      vec0 = this.Vec0;
      vec1 = this.Vec1;
   }
   public Curve3 Curve { readonly get => mCurve; set => mCurve = value; }
   public Vector3 Vec0 { readonly get => mVec0; set => mVec0 = value; }
   public Vector3 Vec1 { readonly get => mVec1; set => mVec1 = value; }
   public bool IsValid { readonly get => mIsValid; set => mIsValid = value; }
   public readonly double Length { get => mCurve.Length; }
   public NotchSectionType NotchSectionType { get; set; } = NotchSectionType.None;

   // Alternative implementation using object initializer with your existing constructor
   public readonly ToolingSegment Clone () {
      return new ToolingSegment (
          (Geom.CloneCurve (this.mCurve, this.mVec0), this.mVec0, this.mVec1, this.NotchSectionType
      )) {
         mIsValid = this.mIsValid
      };
   }
}
public class Tooling {
   public Tooling (Workpiece wp, E3Thick ent,
                   Pline shape, EKind kind) {
      Traces.Add ((ent, shape));
      Kind = kind; Work = wp;
   }

   public Tooling (Workpiece wp, EKind kind) {
      Kind = kind; Work = wp;
   }

   public Tooling Clone () {
      // Create a new Tooling object
      var clonedTooling = new Tooling (this.Work, this.Kind) {
         SeqNo = this.SeqNo,
         NotchKind = this.NotchKind,
         ProfileKind = this.ProfileKind,
         CutoutKind = this.CutoutKind,
         mPerimeter = this.mPerimeter,
         mSegs = [.. this.mSegs.Select (seg => new ToolingSegment (Geom.CloneCurve (seg.Curve, seg.Vec0), seg.Vec0, seg.Vec1))],
         mBound3 = this.mBound3,
         mName = this.mName,
         IsSingleHead1 = this.IsSingleHead1,
         IsSingleHead2 = this.IsSingleHead2,
         mHead = this.mHead,
         PostRoute = [.. this.PostRoute.Select (pv => new PointVec (pv.Pt, pv.Vec))],
         ShouldConsiderReverseRef = this.ShouldConsiderReverseRef
      };

      // Clone the Traces list
      clonedTooling.Traces.AddRange (this.Traces.Select (trace => (trace.Ent, trace.Trace.Clone ())));
      return clonedTooling;
   }

   public const double mJoinableLengthToClose = 5.0;
   public double JoinableLengthToClose { get { return mJoinableLengthToClose; } }
   public const double mNotchJoinableLengthToClose = 5.0;
   public readonly Workpiece Work;
   public readonly List<(E3Thick Ent, Pline Trace)> Traces = [];
   public int SeqNo = -1;
   public EKind Kind;
   public ECutKind NotchKind = ECutKind.None;
   public ECutKind CutoutKind = ECutKind.None;
   public ECutKind ProfileKind = ECutKind.None;
   double mPerimeter = -1.0;
   List<ToolingSegment> mSegs = [];

   public bool EdgeNotch { get; set; } = false;
   public List<ToolingSegment> Segs {
      get {
         if (mSegs.Count == 0)
            mSegs = [.. ExtractSegs];
         return mSegs;
      }

      set { mSegs = value; }
   }
   public Tooling RefTooling { get; set; } = null;
   Bound3? mBound3 = null;
   public double XMin { get => mBound3.Value.XMin; }
   public double XMax { get => mBound3.Value.XMax; }
   public Bound3 Bound3 {
      get => mBound3.Value;
      set => mBound3 = value;
   }
   string mName;
   public string Name {
      get => mName;
      set => mName = value;
   }
   public string FeatType { get; set; }
   public bool IsSingleHead1 { get; set; }
   public bool IsSingleHead2 { get; set; }
   int mHead = -1;
   public int Head {
      get => mHead;
      set => mHead = value;
   }

   public PointVec Start
      => Project (Traces[0].Ent, Traces[0].Trace.P1);

   public PointVec End
      => Project (Traces[^1].Ent, Traces[^1].Trace.P2);

   public List<PointVec> PostRoute = [];

   public bool ShouldConsiderReverseRef { get; set; }
   void OffsetStartingTraceToE3PlaneRef () {
      int i = Traces.FindIndex (item => item.Ent is E3Plane);
      if (i > 0)
         Traces.Skip (i).Concat (Traces.Take (i)).ToList ();
   }

   public Tooling JoinTo (Tooling b, double minJoinDist = mJoinableLengthToClose) {
      var t = new Tooling (Work, Kind);
      //var d1 = End.Pt.DistTo (b.Start.Pt);
      //var d2 = Start.Pt.DistTo (b.End.Pt);
      //var d3 = Start.Pt.DistTo (b.Start.Pt);
      //var d4 = End.Pt.DistTo (b.End.Pt);
      if (this.IsClosed () || b.IsClosed ())
         return null;

      if (End.Pt.DistTo (b.Start.Pt).LTEQ (minJoinDist)) {
         t.Traces.AddRange (Traces);
         t.Traces.AddRange (b.Traces);
      } else if (Start.Pt.DistTo (b.End.Pt).LTEQ (minJoinDist)) {
         t.Traces.AddRange (b.Traces);
         t.Traces.AddRange (Traces);
      } else if (Start.Pt.DistTo (b.Start.Pt).LTEQ (minJoinDist)
                 /*&& !(End.Pt.DistTo (b.End.Pt) < minJoinDist)*/) {
         b.Reverse ();
         t.Traces.AddRange (b.Traces); t.Traces.AddRange (Traces);
      } else if (End.Pt.DistTo (b.End.Pt).LTEQ (minJoinDist)
                 /*&& !(Start.Pt.DistTo (b.Start.Pt) < minJoinDist)*/) {
         b.Reverse ();
         t.Traces.AddRange (Traces); t.Traces.AddRange (b.Traces);
      } else
         t = null;

      return t;
   }

   public bool IsClosed (double tol = mJoinableLengthToClose) => End.Pt.DistTo (Start.Pt).LTEQ (0, tol);

   // If the tooling exists in more than one flange, then it returns true
   public bool IsDualFlangeTooling () {
      if (ProfileKind == ECutKind.Top2YNeg ||
         ProfileKind == ECutKind.Top2YPos ||
         ProfileKind == ECutKind.YNegToYPos ||
         Utils.IsDualFlangeSameSideNotch (this, Segs))
         return true;
      return false;
   }

   // If the tooling completely exists on one of the flange,
   // then it returns true
   public bool IsSingleFlangeTooling () {
      if (ProfileKind == ECutKind.Top ||
         ProfileKind == ECutKind.YPos ||
         ProfileKind == ECutKind.YNeg)
         return true;
      return false;
   }

   // If the tooling exists in more than one flange, but starting and ending of
   // the tooling is on same flange, then this returns true.
   public bool IsDualFlangeSameSideNotch () => Utils.IsDualFlangeSameSideNotch (this, Segs);
   public void IdentifyCutout () {
      // If closed tooling, it is marked as CUTOUT
      if (IsClosed (mJoinableLengthToClose)) {
         Kind = EKind.Cutout;
         OffsetStartingTraceToE3PlaneRef ();
      }
   }
   public bool IsDualFlangeCutoutNotch () {
      bool toTreatAsCutOut = CutOut.ToTreatAsCutOut (Segs, Bound3, MCSettings.It.MinCutOutLengthThreshold);
      if ((IsClosed () && Kind == EKind.Cutout) || (Kind == EKind.Hole && toTreatAsCutOut)) {
         if (Utils.IsDualFlangeSameSideCutout (Segs))
            return true;
      }
      return false;
   }

   public bool IsWebFlangeFeature () {
      for (int ii = 0; ii < Segs.Count; ii++) {
         var plType1 = Utils.GetFeatureNormalPlaneType (Segs[ii].Vec0, XForm4.IdentityXfm);
         var plType2 = Utils.GetFeatureNormalPlaneType (Segs[ii].Vec1, XForm4.IdentityXfm);
         if (plType1 != Utils.EPlane.Top || plType2 != Utils.EPlane.Top)
            return false;
      }
      return true;
   }

   public bool IsTopOrBottomFlangeFeature () {
      for (int ii = 0; ii < Segs.Count; ii++) {
         var plType1 = Utils.GetFeatureNormalPlaneType (Segs[ii].Vec0, XForm4.IdentityXfm);
         var plType2 = Utils.GetFeatureNormalPlaneType (Segs[ii].Vec1, XForm4.IdentityXfm);
         if ((plType1 != Utils.EPlane.YPos && plType1 != Utils.EPlane.YNeg) &&
            (plType2 != Utils.EPlane.YPos && plType2 != Utils.EPlane.YNeg))
            return false;
      }
      return true;
   }

   public void DrawWaypoints (Color32 color, double height) {
      Lux.HLR = true;
      Lux.Color = new Color32 (96, color.R, color.G, color.B);
      for (int i = 1; i < PostRoute.Count; i++) {
         PointVec pv0 = PostRoute[i - 1], pv1 = PostRoute[i];
         Lux.Draw (EDraw.Quad, [pv0.Pt, pv1.Pt,
                                   pv1.Pt + pv1.Vec * height,
                                   pv0.Pt + pv0.Vec * height]);
      }

      Lux.Color = color;
      for (int i = 1; i < PostRoute.Count; i++) {
         PointVec pv0 = PostRoute[i - 1], pv1 = PostRoute[i];
         Lux.Draw (EDraw.Lines, [pv0.Pt, pv1.Pt]);
      }
   }

   public double Perimeter {
      get {
         if (mPerimeter < 0.0)
            mPerimeter = mPerimeter = Segs.Sum (seg => seg.Curve.Length);

         return mPerimeter;
      }
   }

   public Utils.EFlange Flange {
      get {
         if (this.IsFlexFeature ())
            return Utils.EFlange.Flex;
         else {
            return Utils.GetArcPlaneFlangeType (this.ExtractSegs.First ().Vec0, GCodeGenerator.GetXForm (Work));
         }
      }
   }
   public void DrawSegs (Color32 color, double height) {
      bool first = true;
      Lux.HLR = true;
      List<(PointVec PV, bool Stencil)> pvs = [];
      foreach (var (Curve, Vec0, Vec1) in Segs) {
         if (first) {
            pvs.Add ((new (Curve.Start, Vec0), true));
            first = false;
         }

         if (Curve is Arc3 arc) {
            var pts = arc.Discretize (0.1).ToList ();
            for (int i = 1; i < pts.Count; i++)
               pvs.Add ((new (pts[i], Vec1), i == pts.Count - 1));
         } else
            pvs.Add ((new (Curve.End, Vec1), true));
      }

      Lux.Color = new Color32 (96, color.R, color.G, color.B);
      for (int i = 1; i < pvs.Count; i++) {
         PointVec pv0 = pvs[i - 1].PV, pv1 = pvs[i].PV;
         Lux.Draw (EDraw.Quad, [pv0.Pt, pv1.Pt, pv1.Pt + pv1.Vec * height, pv0.Pt + pv0.Vec * height]);
      }

      Lux.Color = color;
      for (int i = 1; i < pvs.Count; i++) {
         PointVec pv0 = pvs[i - 1].PV, pv1 = pvs[i].PV;
         Lux.Draw (EDraw.Lines, [pv0.Pt + pv0.Vec * height, pv1.Pt + pv1.Vec * height]);
      }

      foreach (var (pv, stencil) in pvs) {
         if (stencil)
            Lux.Draw (EDraw.Lines, [pv.Pt, pv.Pt + pv.Vec * height]);
      }
   }

   public void DrawSeqNo (double height) {
      if (SeqNo < 0)
         return;

      Lux.HLR = false;
      double length = Traces.Sum (a => a.Trace.Perimeter) / 2, start = 0;
      foreach (var (ent, trace) in Traces) {
         foreach (var seg in trace.Segs) {
            double end = start + seg.Length;
            if (end > length) {
               double lie = length.LieOn (start, end);
               var pv = Project (ent, seg.GetPointAt (lie));
               Point3 pt = pv.Pt + pv.Vec * height;
               Lux.Color = Color32.Black;
               Lux.DrawBillboardText ((SeqNo + 1).ToString (), pt, 16);
               return;
            }

            start = end;
         }
      }
   }

   public void DrawToolingName (double height) {
      Lux.HLR = false;
      double length = Traces.Sum (a => a.Trace.Perimeter) / 2, start = 0;
      var (ent, trace) = Traces[0];
      {
         var seg = trace.Segs.ToList ()[(int)((double)trace.Segs.ToList ().Count / 2.0)];
         {
            double end = start + seg.Length;
            double lie = length.LieOn (start, end);
            var pv = Project (ent, seg.GetPointAt (lie));
            Point3 pt = pv.Pt + pv.Vec * height;
            Lux.Color = Color32.Black;
            Lux.DrawBillboardText (Name.ToString (), pt, 16);
            return;
         }
      }
   }

   public static Point2 Unproject (Point3 pt, E3Plane ep) {
      var pt2 = pt * ep.Xfm.GetInverse ();
      return new (pt2.X, pt2.Y);
   }

   public static ECutKind GetCutKind (Tooling cut, XForm4 trf, bool profileKind = false) {
      var segs = cut.Segs;
      ECutKind cutKindAtFlex = ECutKind.None, cutKindAtFlange = ECutKind.None;
      bool YNegPlaneFeat = segs.Any (cutSeg => (((trf * cutSeg.Vec0.Normalized ()).Y).EQ (-1) && ((trf * cutSeg.Vec1.Normalized ()).Y).EQ (-1)));
      bool YPosPlaneFeat = segs.Any (cutSeg => (((trf * cutSeg.Vec0.Normalized ()).Y).EQ (1) && ((trf * cutSeg.Vec1.Normalized ()).Y).EQ (1)));
      bool TopPlaneFeat = segs.Any (cutSeg => (((trf * cutSeg.Vec0.Normalized ()).Z).EQ (1) && ((trf * cutSeg.Vec1.Normalized ()).Z).EQ (1)));

      // For LH Component
      foreach (var seg in segs) {
         var nn = (trf * seg.Vec0.Normalized ());
         if (nn.Y < -1e-6 && nn.Y.SGT (-1.0)) {
            cutKindAtFlex = ECutKind.YNegFlex;
            break;
         } else if (nn.Y > 1e-6 && nn.Y.SLT (1.0)) {
            cutKindAtFlex = ECutKind.YPosFlex;
            break;
         }
      }

      if (TopPlaneFeat && (YPosPlaneFeat || cutKindAtFlex == ECutKind.YPosFlex) && (YNegPlaneFeat || cutKindAtFlex == ECutKind.YNegFlex))
         cutKindAtFlange = ECutKind.YNegToYPos;
      else if (TopPlaneFeat && (YNegPlaneFeat || cutKindAtFlex == ECutKind.YNegFlex))
         cutKindAtFlange = ECutKind.Top2YNeg;
      else if (TopPlaneFeat && (YPosPlaneFeat || cutKindAtFlex == ECutKind.YPosFlex))
         cutKindAtFlange = ECutKind.Top2YPos;
      else if (TopPlaneFeat)
         cutKindAtFlange = ECutKind.Top;
      else if (YNegPlaneFeat)
         cutKindAtFlange = ECutKind.YNeg;
      else if (YPosPlaneFeat)
         cutKindAtFlange = ECutKind.YPos;
      if (cutKindAtFlex == ECutKind.None && cutKindAtFlange == ECutKind.None)
         throw new Exception ("Unsupported Notch Type");


      // Correction for RH Component
      if (/*!profileKind  && */MCSettings.It.PartConfig == MCSettings.PartConfigType.RHComponent) {
         switch (cutKindAtFlex) {
            case ECutKind.YNegFlex:
               cutKindAtFlex = ECutKind.YPosFlex;
               break;
            case ECutKind.YPosFlex:
               cutKindAtFlex = ECutKind.YNegFlex;
               break;
            default:
               break;
         }

         switch (cutKindAtFlange) {
            case ECutKind.YNeg:
               cutKindAtFlange = ECutKind.YPos;
               break;
            case ECutKind.YPos:
               cutKindAtFlange = ECutKind.YNeg;
               break;
            case ECutKind.Top2YNeg:
               cutKindAtFlange = ECutKind.Top2YPos;
               break;
            case ECutKind.Top2YPos:
               cutKindAtFlange = ECutKind.Top2YNeg;
               break;
            default:
               break;

         }
      }

      return cutKindAtFlange != ECutKind.None ? cutKindAtFlange : cutKindAtFlex;
   }

   public IEnumerable<ToolingSegment> ExtractSegs {
      get {
         PointVec? prevpvb = null;
         foreach (var (ent, pline0) in Traces) {
            var pline = pline0;
            if (ent is E3Flex)
               pline = pline.DiscretizeP (0.1);
            var seggs = pline.Segs.ToList ();
            foreach (var seg in pline.Segs) {

               PointVec pva = Project (ent, seg.A),
                        pvb = Project (ent, seg.B);
               if (prevpvb != null) {
                  if (pva.DistTo (prevpvb.Value) < mNotchJoinableLengthToClose
                      && pva.DistTo (prevpvb.Value) > 1.0) {
                     var line = new Line3 (prevpvb.Value.Pt, pva.Pt);
                     yield return new (line, prevpvb.Value.Vec, pva.Vec);
                  }
               }

               if (seg.IsCurved) {
                  // If this is a curved segment in 2D, then it lies on a plane and we can simply convert
                  // it to an Arc3 (all the normals along this curve are pointing in the same direction)
                  PointVec pvm1 = Project (ent, seg.GetPointAt (0.5)),
                           pvm2 = Project (ent, seg.GetPointAt (0.75));
                  var arc = new Arc3 (pva.Pt, pvm1.Pt, pvm2.Pt, pvb.Pt);
                  prevpvb = pva; prevpvb = pvb;
                  yield return new (arc, pva.Vec, pvb.Vec);
               } else {
                  // If this is a line in 2D space, it might be lofted into an arc in 3D (if it lies
                  // on a flex). So use the difference between start and end normals to figure out how many
                  // segments to divide this into
                  double angDiff = pva.Vec.AngleTo (pvb.Vec),
                         angStep = 5.D2R ();
                  int steps = 1 + (int)(angDiff / angStep);
                  for (int i = 0; i < steps; i++) {
                     double start = i / (double)steps, end = (i + 1) / (double)steps;
                     PointVec ps = Project (ent, seg.GetPointAt (start)),
                              pe = Project (ent, seg.GetPointAt (end));
                     var line = new Line3 (ps.Pt, pe.Pt);
                     prevpvb = pva; prevpvb = pvb;
                     yield return new (line, ps.Vec, pe.Vec);
                  }
               }
            }
         }
      }
   }

   public void Reverse () {
      Traces.ForEach (trace => trace.Trace.Reverse ());
      Traces.Reverse ();
      // Note: Segs is the most recent recomputed segments 
      // list. So, Segs will not be overwritten from Tooling.
      if (Segs.Count == 0)
         Segs = [.. ExtractSegs];
      else
         Segs = Geom.GetReversedToolingSegments (Segs);
   }

   public PointVec Project (E3Thick ent, Point2 pt) {
      Point3 pt3; Vector3 vec;
      if (ent is E3Plane ep) {
         vec = Workpiece.Classify (ep) switch {
            Workpiece.EType.YNeg => new Vector3 (0, -1, 0),
            Workpiece.EType.YPos => new Vector3 (0, 1, 0),
            _ => new Vector3 (0, 0, 1)
         };

         pt3 = pt * ep.Xfm;
      } else if (ent is E3Flex ef) {
         (pt3, vec) = ef.Project (pt);
         var (a, b) = ef.Axis; Point3 mid = pt3.SnapToLine (a, b);
         if (vec.Opposing (pt3 - mid))
            vec = -vec; // Get the 'outward facing' vector

         pt3 += (vec * ef.Thickness / 2);
      } else
         throw new NotImplementedException ();

      return new (pt3, vec);
   }

   public bool IsCircle () {
      var segsList = Segs.ToList ();

      // Assuming that circle wil be the only segment
      return segsList[0].Curve is Arc3 arc && arc.Start.EQ (arc.End);
   }

   // Any feature other than Mark, that has a closed contour, 
   // AND also passes through E3Flex is a Cutout
   public bool IsCutout () => (Kind == EKind.Cutout);
   public bool IsMark () => (Kind == EKind.Mark);

   // Any feature which is other than Mark, that has an open profile
   // is a Notch
   public bool IsNotch () => (Kind == EKind.Notch);
   public bool IsHole () => (Kind == EKind.Hole);

   // Any feature such as Notch or hole,
   // which features on the E3Flex is a
   // FlexFeature
   public bool IsFlexFeature () => Traces.Any (a => a.Ent is E3Flex);
   public bool IsFlexOnlyFeature () => Traces.All (a => a.Ent is E3Flex);
   // Features either Notch or hole,
   // which feature only on the E3Planes
   // and not on any E3Flex is PlaneFeature
   public bool IsPlaneFeature () => !IsFlexFeature ();
   public bool IsFlexHole () => IsHole () && IsFlexFeature ();
   public bool IsFlexCutout () => IsCutout () && IsFlexFeature ();
   public bool IsFlexNotch () => IsNotch () && IsFlexFeature ();

   /// <summary>
   /// This method judges a feature to be a narrow one if an across distance
   /// is lesser than the minimum. 
   /// How it works: For every two directional edges, which are opposing in direction,
   /// the distance between the start/end is compared with another edge's start and end.
   /// If the shortest length is less than minimum (default 2 mm), it is judged as 
   /// narrow.
   /// </summary>
   /// <caveat>
   /// If in a feature, even if two of the edges come very close to each other,
   /// that feature will be marked narrow. This method is to be used for features
   /// mostly composed of linear segments
   /// </caveat>
   /// <param name="min">Minimum value threshold</param>
   /// <returns>True if a narrow feature, False otherwise</returns>
   public bool IsNarrowFlexOnlyFeature (double min = 2.0) {
      if (!IsFlexOnlyFeature ()) return false;
      double minVal = double.MaxValue;
      for (int ii = 0; ii < Segs.Count - 1; ii++) {
         for (int jj = ii + 1; jj < Segs.Count; jj++) {
            if (Segs[ii].Curve.End.Subtract (Segs[ii].Curve.Start).ToVector ().Opposing (Segs[jj].Curve.End.Subtract (Segs[jj].Curve.Start).ToVector ())) {
               var v1 = Segs[ii].Curve.Start.DistTo (Segs[jj].Curve.Start);
               var v2 = Segs[ii].Curve.Start.DistTo (Segs[jj].Curve.End);
               var v3 = Segs[jj].Curve.Start.DistTo (Segs[ii].Curve.End);
               var v4 = Segs[jj].Curve.End.DistTo (Segs[ii].Curve.End);
               double[] mivals = [v1, v2, v3, v4];
               double miv = mivals.Min ();
               if (minVal > miv) minVal = miv;
            }
         }
      }
      return minVal.LTEQ (min);
   }

   public bool IsSlotWithWJT () {
      bool toTreatAsCutOut = false;
      if (IsHole () || IsCutout ())
         toTreatAsCutOut = CutOut.ToTreatAsCutOut (Segs, mBound3.Value, MCSettings.It.MinCutOutLengthThreshold);
      return toTreatAsCutOut;
   }

   public double Length {
      get {
         double len = 0;
         foreach (var seg in Segs)
            len += seg.Curve.Length;
         return len;
      }
   }
}