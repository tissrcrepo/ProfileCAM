using System.ComponentModel;
using System.Diagnostics;
using ChassisCAM.Core.GCodeGen;
using ChassisCAM.Core.Geometries;
using Flux.API;
using static System.Math;
using static ChassisCAM.Core.MCSettings;

namespace ChassisCAM.Core {
   public class Workpiece : INotifyPropertyChanged {
      #region Interface ---------------------------------------------------------------------
      public Model3 Model => mModel;
      readonly Model3 mModel;

      public List<Tooling> Cuts => mCuts;
      List<Tooling> mCuts = [];
      public bool Dirty { get; set; } = false;
      public Workpiece (Model3 model, Part part) {
         mBound = (mModel = model).Bound;
         mNCFileName = Path.GetFileNameWithoutExtension (part.Info.FileName);
         mNCFilePath = Path.GetDirectoryName (part.Info.FileName);
      }

      Bound3 mBound;
      readonly string mNCFileName;

      public string NCFileName => mNCFileName;

      readonly string mNCFilePath;
      public string NCFilePath => mNCFilePath;
      public Bound3 Bound => mBound;

      public event PropertyChangedEventHandler PropertyChanged;
      protected virtual void OnPropertyChanged (string propertyName)
         => PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));

      public bool SortingComplete => sortingComplete;
      bool sortingComplete = false;
      int mRotToggle = 1;
      /// <summary>Align the model for processing</summary>
      public void Align () {
         // First, align the baseplane so that it is aligned with the XY plane
         var modelCS = mModel.Baseplane.Xfm.ToCS ();
         if (!modelCS.Equals (CoordSystem.World)) {
            var xfm = Matrix3.Between (mModel.Baseplane.Xfm.ToCS (), CoordSystem.World);
            Apply (xfm);
            Dirty = true;
         }

         // The baseplane should have the maximum extent in X - if that is in Y instead,
         // rotate it 90 degrees about the Z axis. Model extrusion is now in the X direction
         var size = mModel.Baseplane.Bound.Size;
         if (size.Y > size.X) {
            Apply (Matrix3.Rotation (EAxis.Z, Geo.HalfPI));
            Dirty = true;
         }

         // If the flanges are protruding 'downward', then rotate the model by 180 
         // degrees about the X axis
         if (-mModel.Bound.ZMin < mModel.Bound.ZMax) {
            Apply (Matrix3.Rotation (EAxis.X, Geo.PI));
            Dirty = true;
         }

         Apply (Matrix3.Rotation (EAxis.Z, Geo.PI * mRotToggle));
         if (mRotToggle == 1) mRotToggle = 0;
         else mRotToggle = 1;

         // Now shift the origin:
         var (mbound, pbound) = (mModel.Bound, mModel.Baseplane.Bound);
         double dx = -mbound.XMin;        // Model stretches from X=0 to X=Len
         double dy = -mbound.Midpoint.Y;  // Model is centered in Y (Y=0 is the centerline)
         double dz = -pbound.ZMin;        // Bottom of the baseplane is at Z=0,
                                          // and two flanges are down in -Z territory
         Apply (Matrix3.Translation (dx, dy, dz));
         mBound = mModel.Bound;

         // Helper ...........................
         void Apply (Matrix3 xfm) {
            foreach (var ent in mModel.Entities)
               ent.Xform (xfm);
         }

         GCodeGenerator.EvaluateToolConfigXForms (this);
      }
      public void DeleteCuts () => Cuts.Clear ();
      public bool DoAddHoles () {
         int cutIndex = Cuts.Count + 1;
         foreach (var ep in mModel.Entities.OfType<E3Plane> ()) foreach (var con in ep.Contours.Skip (1)) {
               var shape = con.Clone ().Cleanup (threshold: 1e-3);
               EType type = Classify (ep);

               // If ...
               // 1. the contour appears to be CCW w.r.t a negative X
               //    with positive Y OR
               // 2. Vice versa after projection for YNeg plane OR
               // 3. Converse of the above condition
               // THEN shape has to be reversed
               if (type == EType.Top && ep.Xfm.M11 * ep.Xfm.M22 < 0.0
                  || type == EType.YNeg && ep.Xfm.M11 * ep.Xfm.M23 < 0.0
                  || type == EType.YPos && ep.Xfm.M11 * ep.Xfm.M23 > 0.0) if (shape.Winding == EWinding.CCW)
                     shape.Reverse ();
                  else if (shape.Winding == EWinding.CW)
                     shape.Reverse ();

               Tooling cut = new (this, ep, shape, EKind.Hole);
               var name = $"Tooling-{cutIndex++}";
               var featType = $"{Utils.GetFlangeType (cut,
                  GCodeGenerator.GetXForm (this))} - {cut.Kind}";
               cut.Name = name;
               cut.FeatType = featType;
               bool toTreatAsCutOut = CutOut.ToTreatAsCutOut (cut.Segs, Bound, It.MinCutOutLengthThreshold);
               if (toTreatAsCutOut) {
                  cut.CutoutKind = Tooling.GetCutKind (cut, GCodeGenerator.GetXForm (this));
                  cut.ProfileKind = Tooling.GetCutKind (cut, XForm4.IdentityXfm, profileKind: true);
                  cut.Kind = EKind.Cutout;
               }
               Cuts.Add (cut);
            }
         foreach (var ef in mModel.Entities.OfType<E3Flex> ()) {
            var bound = new Bound2 (ef.Trims.Select (a => a.Bound));
            foreach (var con in ef.Trims) {
               var b2 = con.Bound;
               // [Alag:Review] can use OR and combinue "continue"
               if (b2.XMin.EQ (bound.XMin, 0.01) || b2.XMax.EQ (bound.XMax, 0.01))
                  continue;

               if (b2.YMin.EQ (bound.YMin, 0.01) || b2.YMax.EQ (bound.YMax, 0.01))
                  continue;

               var shape = con.Clone ().Cleanup (threshold: 1e-3);
               if (shape.Winding == EWinding.CW) shape.Reverse ();
               Cuts.Add (new Tooling (this, ef, shape, EKind.Hole));
               Cuts[^1].Name = $"Tooling-{cutIndex++}";
               Cuts[^1].FeatType = $"{Utils.GetFlangeType (Cuts[^1],
                  GCodeGenerator.GetXForm (this))} - {Cuts[^1].Kind}";
            }
         }

         // In the case of FlexHoles, since the segments happen on E3Plane and E3Flex and the resultant
         // list of segments are not owned by any one E3Entity, the segments' start points are projected
         // onto the plane away from E3Flex either by 45 deg or by -45 deg. This is decided by the mid normal
         // to the E3Flex. The windiwng of the polygon on the projected plane is used to check if the
         // Traces of the tooling has to be reversed.
         Stopwatch swRevTool = Stopwatch.StartNew ();
         Stopwatch swCalcBound = Stopwatch.StartNew ();
         TimeSpan tsRevTool = new (); TimeSpan tsCalcBound = new ();
         foreach (var cut in Cuts) {
            var cutSegs = cut.Segs.ToList ();
            bool yNegFlex = cutSegs.Any (cutSeg => cutSeg.Vec0.Normalized ().Y < -0.1);
            if (cut.Kind == EKind.Hole && Utils.GetFlangeType (cut,
               GCodeGenerator.GetXForm (this)) == Utils.EFlange.Flex) {
               Vector3 n = new (0.0, Sqrt (2.0), Sqrt (2.0));
               Point3 q = new (0.0, mBound.YMax - 10.0, mBound.ZMax + 10.0);
               if (yNegFlex) {
                  n = new Vector3 (0.0, -Sqrt (2.0), Sqrt (2.0));
                  q = new Point3 (0.0, mBound.YMin - 10.0, mBound.ZMax + 10.0);
               }

               if (Geom.GetToolingWinding (n, q, cutSegs) == Geom.ToolingWinding.CW) {
                  swRevTool.Start ();
                  cut.Reverse ();
                  swRevTool.Stop ();
                  tsRevTool += swRevTool.Elapsed;
               }
            }

            // Calculate the bound3 for each cut
            swCalcBound.Start ();
            cut.Bound3 = Utils.CalculateBound3 (cutSegs, Model.Bound);
            swCalcBound.Stop ();
            tsCalcBound += swCalcBound.Elapsed;
         }
         Dirty = true;
         return true;
      }

      public bool DoTextMarking (MCSettings mcs) {
         // Remove any previous Mark
         for (int ii = 0; ii < Cuts.Count; ii++) if (Cuts[ii].IsMark ()) {
               Cuts.RemoveAt (ii);
               ii--;
            }
         ERotate eTextAng = mcs.MarkAngle;
         double textAng = eTextAng switch {
            ERotate.Rotate0 => 0,
            ERotate.Rotate90 => 90 * Math.PI / 180,
            ERotate.Rotate180 => Math.PI,
            ERotate.Rotate270 => 270 * Math.PI / 180,
            _ => 0
         };
         int cutIndex = Cuts.Count + 1;
         var bp = mModel.Baseplane;
         var xfm = bp.Xfm.GetInverse () * Matrix3.Translation (0, 0, Offset);
         var textPt = new Point2 (400.0, -12.5);
         if (mcs.MarkTextPosX.LieWithin (mModel.Bound.XMin, mModel.Bound.XMax))
            textPt = new Point2 (mcs.MarkTextPosX, mcs.MarkTextPosY);

         // Additional rotational angle
         Point3 ax1 = new (textPt.X, textPt.Y, 0); Point3 ax2 = new (textPt.X, textPt.Y, 10);
         var rotXfm = Matrix3.Rotation (ax1, ax2, textAng);

         var e2t = new E2Text (mcs.MarkText, textPt, mcs.MarkTextHeight, "SIMPLEX", 0);
         foreach (var pline in e2t.Plines) {
            Pline p2 = pline.Xformed (rotXfm * xfm);

            Cuts.Add (new Tooling (this, mModel.Baseplane, p2, EKind.Mark));
            Cuts[^1].Name = $"Tooling-{cutIndex++}";
            Cuts[^1].FeatType = $"{Utils.GetFlangeType (Cuts[^1],
                                   GCodeGenerator.GetXForm (this))} - {Cuts[^1].Kind}";

            // Calculate the bound3 for each cut
            Cuts[^1].Bound3 = Utils.CalculateBound3 ([.. Cuts[^1].Segs], Model.Bound);
         }

         Dirty = true;
         return true;
      }

      public void DoSorting () {
         double clearance = 25;
         mCuts = [.. mCuts.OrderBy (a => a.Start.Pt.X)];
         for (int i = 0; i < mCuts.Count; i++)
            mCuts[i].SeqNo = i;

         var box = mBound;
         for (int i = 1; i < mCuts.Count; i++) {
            Tooling prevTooling = mCuts[i - 1], currTooling = mCuts[i];
            var pts = prevTooling.PostRoute; pts.Clear ();
            Point3 prevToolingEndPlusClearance = prevTooling.End.Lift (clearance),
                   currToolingStartPlusClearance = currTooling.Start.Lift (clearance);
            double xmid = (prevToolingEndPlusClearance.X + currToolingStartPlusClearance.X) / 2,
                   ymin = box.YMin - clearance,
                   ymax = box.YMax + clearance,
                   zmax = box.ZMax + clearance;
            pts.Add (prevTooling.End); pts.Add (new (prevToolingEndPlusClearance, prevTooling.End.Vec));

            Vector3 vecN = new Vector3 (0, -1, 1).Normalized (),
                   vecP = new Vector3 (0, 1, 1).Normalized ();
            switch ((Classify (prevTooling.End.Vec), Classify (currTooling.End.Vec))) {
               case (EType.Top, EType.YNeg):
               case (EType.YNeg, EType.Top):
                  pts.Add (new (new (xmid, ymin, zmax), vecN));
                  break;

               case (EType.Top, EType.YPos):
               case (EType.YPos, EType.Top):
                  pts.Add (new (new (xmid, ymax, zmax), vecP));
                  break;

               case (EType.YNeg, EType.YPos):
                  pts.Add (new (new (xmid, ymin, zmax), vecN));
                  pts.Add (new (new (xmid, ymax, zmax), vecP));
                  break;

               case (EType.YPos, EType.YNeg):
                  pts.Add (new (new (xmid, ymax, zmax), vecP));
                  pts.Add (new (new (xmid, ymin, zmax), vecN));
                  break;
            }

            pts.Add (new (currToolingStartPlusClearance, currTooling.Start.Vec)); pts.Add (currTooling.Start);
         }

         sortingComplete = true;
      }

      public bool DoCutNotchesAndCutouts () {
         int cutIndex = Cuts.Count + 1;
         var mb = mBound;
         List<Tooling> cuts = [];

         // The notches in the planes
         foreach (var ep in mModel.Entities.OfType<E3Plane> ()) {
            // First, compute the 'full rectangle bound' of this plane, and any segments
            // of this plane contour not lying on that bound need to be cut out
            var pb = ep.Bound;
            (Point3 p1, Point3 p2) = Classify (ep) switch {
               EType.Top => (new Point3 (mb.XMin, pb.YMin, mb.YMax),
                             new Point3 (mb.XMax, pb.YMax, mb.YMax)),
               _ => (new Point3 (mb.XMin, pb.YMin, pb.ZMin),
                     new Point3 (mb.XMax, pb.YMin, pb.ZMax)),
            };

            (Point2 p3, Point2 p4) = (Tooling.Unproject (p1, ep), Tooling.Unproject (p2, ep));
            Bound2 rect = new (p3, p4);
            foreach (var notch in GetNotches (rect, ep.Contours[0])) cuts.Add (new Tooling (this, ep, notch, EKind.Notch));
         }

         foreach (var ef in mModel.Entities.OfType<E3Flex> ()) {
            // First compute the 'full rectangle bound' of this flex
            Point2 pt2 = ef.Trims.First ().P1;
            Point3 pt3 = ef.Project (pt2).Pt;
            double dx = pt2.X - pt3.X;
            var rect = new Bound2 (ef.Trims.Select (a => a.Bound));
            rect = new Bound2 (mBound.XMin + dx, rect.YMin, mBound.XMax + dx, rect.YMax);
            foreach (var trim in ef.Trims)
               foreach (var notch in GetNotches (rect, trim))
                  cuts.Add (new Tooling (this, ef, notch, EKind.Notch));
         }

         // Connect the notch toolings that are close to each other
         bool done = false;
         Tooling t0 = null, t1 = null;
         while (!done) {
            done = true;
            for (int i = 0; i < cuts.Count - 1; i++) {
               for (int j = i + 1; j < cuts.Count; j++) {
                  t0 = cuts[i]; t1 = cuts[j];
                  if (t0 == null)
                     break;

                  if (t1 == null)
                     continue;

                  Tooling tm = t0.JoinTo (t1, Tooling.mNotchJoinableLengthToClose)
                                 ?? t1.JoinTo (t0, Tooling.mNotchJoinableLengthToClose);
                  if (tm != null) {
                     cuts.Add (tm);
                     cuts[i] = cuts[j] = null;
                     done = false;
                  }
               }

               if (t0 == null)
                  continue;
            }
         }

         // Remove all invalid toolings
         cuts.RemoveAll (c => c == null);

         for (int zz = 0; zz < cuts.Count; zz++) {
            Tooling cut = cuts[zz];
            cut.IdentifyCutout ();

            // If the cutout is fully on the flex and if it has shortest distance between (across)
            // is lesser than 2.0, the cutout is discarded
            // This is included to exclude such cutouts that are generated from JOIN functionality
            if (cut.IsFlexCutout ()) {
               if (cut.IsNarrowFlexOnlyFeature ()) continue;
            }

            // If the hole 
            if (cut.IsFlexOnlyFeature ()) continue;

            // For some reason, a closed PLine in Flux after projection
            // to the E3Plane/E3Flex in the Tooling constructor, does not
            // produce a closed tooling. This issue has to be investigated.
            // The following fix is temporary, to close the tooling
            var modifidSegs = cut.ExtractSegs.ToList ();
            cut.Name = $"Tooling-{cutIndex++}";
            if (cut.Kind == EKind.Cutout) {
               if (cut.Segs[0].Curve.Start.DistTo (cut.Segs[^1].Curve.End).SGT (0)) {
                  var refToolingSegment = new ToolingSegment (cut.Segs[0].Curve, cut.Segs[^1].Vec1, cut.Segs[0].Vec0);
                  var missingTs = Geom.CreateToolingSegmentForCurve (refToolingSegment, new FCLine3 (cut.Segs[^1].Curve.End, cut.Segs[0].Curve.Start));
                  modifidSegs.Add (missingTs);
                  cut.Segs = modifidSegs;
               }
            }
            cut.FeatType = $"{Utils.GetFlangeType (cut,
                                                   GCodeGenerator.GetXForm (this))} - {cut.Kind}";
            var cutSegs = cut.Segs.ToList ();
            if (cut.Kind == EKind.Cutout || cut.Kind == EKind.Hole) {
               if (!It.CutCutouts)
                  continue;

               // In the case of Cutouts, ( closed notches ) since the segments happen on 
               // E3Plane and E3Flex and the resultant list of segments are not owned by any one
               // E3Entity, the segments' start point are projected onto the plane away from E3Flex
               // in 45 deg or -45 deg. The windiwng of the polygon on the projected plane is used
               // to check if the Traces of the tooling has to be reversed.
               Vector3 n = Utils.GetEPlaneNormal (cut,
                                                  GCodeGenerator.GetXForm (this));
               Point3 q = new (0.0, mBound.YMax + 10.0, mBound.ZMax + 10.0);
               bool yNegFlexFeat = cutSegs.Any (cutSeg => cutSeg.Vec0.Normalized ().Y < -0.1);
               if (yNegFlexFeat)
                  q = new Point3 (0.0, mBound.YMin - 10.0, mBound.ZMax + 10.0);

               if (Geom.GetToolingWinding (n, q, cutSegs) == Geom.ToolingWinding.CW)
                  cut.Reverse ();
               cutSegs = [.. cut.Segs];
               cut.CutoutKind = Tooling.GetCutKind (cut, GCodeGenerator.GetXForm (this));
               cut.ProfileKind = Tooling.GetCutKind (cut, XForm4.IdentityXfm, profileKind: true);
            } else {
               if (!It.CutNotches)
                  continue;
               cut.NotchKind = Tooling.GetCutKind (cut, GCodeGenerator.GetXForm (this));
               cut.ProfileKind = Tooling.GetCutKind (cut, XForm4.IdentityXfm, profileKind: true);
               var NotchStFlType = Utils.GetArcPlaneFlangeType (cutSegs.First ().Vec0,
                                                                GCodeGenerator.GetXForm (this));
               var NotchEndFlType = Utils.GetArcPlaneFlangeType (cutSegs[^1].Vec1,
                                                                 GCodeGenerator.GetXForm (this));
               // If the notch starts on a flange and ends on the same flange
               if (cut.ProfileKind == ECutKind.Top2YPos || cut.ProfileKind == ECutKind.Top2YNeg) {
                  if (cutSegs[0].Vec0.Normalized ().EQ (cutSegs[^1].Vec1.Normalized ())) {
                     if (cutSegs[^1].Curve.End.X < cutSegs[0].Curve.Start.X)
                        cut.Reverse ();
                  } else { // If the notch starts on web and ends on a flange
                     var endX = cutSegs[^1].Curve.End.X;
                     if (endX - mBound.XMin < mBound.XMax - endX && cutSegs.First ().Curve.Start.X > endX)
                        cut.Reverse ();
                     else if (mBound.XMax - endX < endX - mBound.XMin && cutSegs.First ().Curve.Start.X < endX)
                        cut.Reverse ();
                  }
               } else if (cut.ProfileKind == ECutKind.YPos || cut.ProfileKind == ECutKind.YNeg) {
                  if (cutSegs.First ().Curve.Start.X > cutSegs[^1].Curve.End.X)
                     cut.Reverse ();
               } else if (cut.ProfileKind == ECutKind.Top || cut.ProfileKind == ECutKind.YNegToYPos) {
                  if (cutSegs.First ().Curve.Start.Y > cutSegs[^1].Curve.End.Y)
                     cut.Reverse ();
               }

               // Set if the notch is a EdgeNotch
               double[] percentLengths = [0.25, 0.5, 0.75];
               double mCurveLeastLength = 0.5;
               if (Notch.IsEdgeNotch (mBound, cut, percentLengths,
                   mCurveLeastLength, Model.Flexes.First ().Radius, Model.Flexes.First ().Thickness, MCSettings.It.NotchApproachLength))
                  cut.EdgeNotch = true;

               RemoveEdgeNotchSegments (cut, out bool segmentsRemoved);
               if (segmentsRemoved) {
                  // Collect the discrete toolings (should always return at least one)
                  List<Tooling> subCuts = CollectDiscreteToolings (cut);

                  //// Validate: subCuts cannot be empty if we're removing the original
                  //if (subCuts == null || subCuts.Count == 0) {
                  //   throw new InvalidOperationException (
                  //       $"CollectDiscreteToolings returned empty list for cut '{cut.Name}' " +
                  //       "after segments were removed. This is a logic error.");
                  //}
                  if (subCuts.Count > 0 && zz < cuts.Count) {
                     // Remove the original cut from the list
                     cuts.RemoveAt (zz);

                     // Insert the sub-cuts at the same position
                     cuts.InsertRange (zz, subCuts);

                     // Adjust the index: -1 because loop will increment zz
                     //zz = zz + subCuts.Count - 1;
                     zz--;

                     continue; // Skip further processing of this iteration
                  }
               }
            }
            cutSegs = [.. cut.Segs];
            // Calculate the bound3 for each cut
            cut.Bound3 = Utils.CalculateBound3 (cutSegs, Model.Bound);
            mCuts.Add (cut);
         }

         Dirty = true;

         return true;
      }

      void RemoveEdgeNotchSegments (Tooling cut, out bool segmentsRemoved) {
         var xMin = Model.Bound.XMin;
         var xMax = Model.Bound.XMax;
         var notchApproachLength = MCSettings.It.NotchApproachLength;
         segmentsRemoved = false;
         var flexIndices = Notch.GetFlexSegmentIndices (cut.Segs);
         for (int ii = 0; ii < flexIndices.Count; ii++) {
            // Iterate backwards within each flex segment range
            for (int jj = flexIndices[ii].Item2; jj >= flexIndices[ii].Item1; jj--) {
               if (cut.Segs[jj].Curve.Start.X.EQ (xMin, 2 * notchApproachLength) ||
                   cut.Segs[jj].Curve.Start.X.EQ (xMax, 2 * notchApproachLength)) {
                  cut.Segs.RemoveAt (jj);
                  segmentsRemoved = true;
               }
            }
            // Recompute flexIndices after processing each flex segment
            flexIndices = Notch.GetFlexSegmentIndices (cut.Segs);
         }
      }

      //List<Tooling> CollectDiscreteToolings (Tooling cut) {
      //   List<Tooling> resSubCuts = [];
      //   List<ToolingSegment> resSegs = [];
      //   int toolSegsCount = cut.Segs.Count;
      //   for (int ii = 0; ii < toolSegsCount - 1; ii++) {
      //      if (cut.Segs[ii].Curve.End.DistTo (cut.Segs[ii + 1].Curve.Start).EQ (0, cut.JoinableLengthToClose))
      //         resSegs.Add (cut.Segs[ii]);
      //      else {
      //         var clonedCut = cut.Clone ();
      //         resSubCuts.Add (clonedCut);
      //         clonedCut.Segs = resSegs;
      //         if (resSegs.Count == 0)
      //            throw new Exception ("In CollectDiscreteToolings, Segs = 0 ");
      //         resSegs = [];
      //      }
      //   }
      //   if (resSubCuts.Count == 0)
      //      resSubCuts = [cut];
      //   return resSubCuts;
      //}
      List<Tooling> CollectDiscreteToolings (Tooling cut) {
         List<Tooling> resSubCuts = [];
         List<ToolingSegment> currentSegs = [];
         int toolSegsCount = cut.Segs.Count;

         if (toolSegsCount == 0) {
            return resSubCuts;
         }

         for (int ii = 0; ii < toolSegsCount; ii++) {
            // Add current segment to the current group
            currentSegs.Add (cut.Segs[ii]);

            // Check if there's a discontinuity with the next segment
            bool isLast = (ii == toolSegsCount - 1);
            bool hasGap = false;

            if (!isLast) {
               // Only check next segment if we're not at the last one
               hasGap = !cut.Segs[ii].Curve.End.DistTo (cut.Segs[ii + 1].Curve.Start)
                           .EQ (0, cut.JoinableLengthToClose);
            }

            // If this is the last segment OR there's a gap with the next one,
            // finalize the current group
            if (isLast || hasGap) {
               // Create a new tooling for the current continuous group
               var clonedCut = cut.Clone ();
               clonedCut.Segs = new List<ToolingSegment> (currentSegs);

               if (clonedCut.Segs.Count == 0) {
                  throw new Exception ("Created tooling with 0 segments");
               }

               resSubCuts.Add (clonedCut);
               currentSegs.Clear ();
            }
         }

         return resSubCuts;
      }
      #endregion

      #region Implementation ----------------------------------------------------------------
      //void Dirty () {
      //   foreach (var cut in mCuts) {
      //      cut.PostRoute.Clear ();
      //      cut.SeqNo = -1;
      //   }
      //}

      public static EType Classify (Vector3 vec) {
         if (Abs (vec.Z) > Abs (vec.Y))
            return EType.Top;

         return vec.Y < 0 ? EType.YNeg : EType.YPos;
      }

      public static EType Classify (E3Plane ep) {
         var vec = ep.ThickVector.Normalized ();
         if (Abs (vec.Z).EQ (1))
            return EType.Top;

         if (Abs (vec.Y).EQ (1)) {
            Point3 pt = Point2.Zero * ep.Xfm;
            return pt.Y < -10 ? EType.YNeg : EType.YPos;
         }

         throw new Exception ("Unsupported plane");
      }

      public enum EType { Top, YNeg, YPos };
      const double Offset = 5;

      static List<Pline> GetNotches (Bound2 b, Pline p) {
         List<Pline> output = [];
         const double E = 0.01;
         foreach (var seg in p.Segs) {
            if (seg.IsCurved) {
               output.Add (seg.ToPline ());
               continue;
            }

            //[Alag:Review] CAN use OR and combine "continue"
            if (seg.A.Y.EQ (b.YMin, E) && seg.B.Y.EQ (b.YMin, E))    // Bottom edge
               continue;
            if (seg.A.Y.EQ (b.YMax, E) && seg.B.Y.EQ (b.YMax, E))    // Top edge
               continue;
            if (seg.A.X.EQ (b.XMin, E) && seg.B.X.EQ (b.XMin, E))    // Left edge
               continue;
            if (seg.A.X.EQ (b.XMax, E) && seg.B.X.EQ (b.XMax, E))    // Right edge
               continue;

            output.Add (seg.ToPline ());
         }

         for (int i = output.Count - 1; i >= 0; i--) {
            Pline one = output[(i + output.Count - 1) % output.Count],
                  two = output[i];

            if (one.P2.EQ (two.P1)) {
               one.Append (two);
               output.RemoveAt (i);
            }
         }

         return output;
      }
      #endregion
   }
}