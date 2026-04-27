using System.Xml.Linq;
using ProfileCAM.Core.Geometries;
using Flux.API;
using MathNet.Numerics.Distributions;
using static ProfileCAM.Core.Utils;
using ProfileCAM.Core.GCodeGen.LCMMultipass2HLegacy;

namespace ProfileCAM.Core.GCodeGen.GCodeFeatures {
   /// <summary>
   /// This class represents the feature which is a tooling, whose curve is 
   /// closed and the curve exists on more than one plane through flex section.
   /// Note: If the curve is open, it is Notch
   /// </summary>
   public class CutOut : ToolingFeature {
      #region Constructor
      public CutOut (IGCodeGenerator gcgen, Tooling toolingItem,
         Tooling prevToolingItem, List<ToolingSegment> prevSegs, ToolingSegment? prevToolingSegment,
         EPlane prevPlaneType, double xStart, double xPartition, double xEnd, double wireJointDistance,
          double notchApproachLength, double prevCutToolingsLength, double prevMarkToolingsLength,
               double totalMarkLength, double totalToolingCutLength, bool isFirstTooling, bool featureToBeTreatedAsCutout) {
         mFeatureToBeTreatedAsCutout = featureToBeTreatedAsCutout;
         GCGen = gcgen;
         ToolingItem = toolingItem; // Initialize the Tooling property here
         mPrevToolingItem = prevToolingItem;
         mPrevToolingSegs = prevSegs;
         mXStart = xStart; mXPartition = xPartition; mXEnd = xEnd;
         mIsFirstTooling = isFirstTooling;
         PreviousToolingSegment = prevToolingSegment;
         mPrevCutToolingsLength = prevCutToolingsLength;
         mPrevMarkToolingsLength = prevMarkToolingsLength;
         mTotalMarkLength = totalMarkLength;
         mTotalToolingCutLength = totalToolingCutLength;
         NotchApproachLength = notchApproachLength;
         mPrevPlane = prevPlaneType;
         mWireJointDistance = wireJointDistance;

         ToolingSegments = ModifyToolingForToolDiaCompensation (toolingItem, toolingItem.Segs);

         ToolingSegments = AddLeadinToTooling (ToolingItem, ToolingSegments, GCGen, 0.5);

         if (ToolingSegments == null || ToolingSegments.Count == 0)
            throw new Exception ("Segments accounted for entry is null");
         PerformToolingSegmentation ();
      }
      #endregion

      #region Properties
      public IGCodeGenerator GCGen { get; set; }
      public override List<ToolingSegment> ToolingSegments { get; set; }
      public override ToolingSegment? GetMostRecentPreviousToolingSegment () => PreviousToolingSegment;
      public Tooling ToolingItem {
         get => mT;
         set {
            // Null check to avoid NullReferenceException
            if (value == null)                throw new ArgumentNullException (nameof (value), "Tooling cannot be null.");

            // Check if the new value is a valid Cutout
            if (!value.IsCutout () && !mFeatureToBeTreatedAsCutout)
               throw new Exception ("Not a Cutout object");

            // Assign value only if it's different from the current value
            if (mT != value)                mT = value;
         }
      }
      public ToolingSegment Exit { get => mExitTooling; set => mExitTooling = value; }
      public ToolingSegment? PreviousToolingSegment { get; private set; }
      public double NotchApproachLength { get; set; }
      #endregion

      #region Data members
      Tooling mPrevToolingItem;
      List<ToolingSegment> mPrevToolingSegs;
      double mXStart, mXPartition, mXEnd;
      bool mIsFirstTooling;
      double mPrevCutToolingsLength, mPrevMarkToolingsLength,
               mTotalMarkLength, mTotalToolingCutLength;
      List<NotchSequenceSection> mCutOutBlocks = [];
      List<(List<ToolingSegment> Segs, bool FlexSegs)> mSegValLists = [];
      Tooling mT;
      Point3? mMostRecentPrevToolPosition;
      EPlane mPrevPlane;
      ToolingSegment mExitTooling;
      double mBlockCutLength = 0;
      double mTotalToolingsCutLength = 0;
      List<Point3> mPreWJTPts = [];
      bool mFeatureToBeTreatedAsCutout = false;
      ToolingSegment? mFlexStartRef = null;
      double mWireJointDistance;
      #endregion

      #region Preprocessors
      /// <summary>
      /// Sanity checker for the cutting blocks. It verifies the following conditions:
      /// <list type="bullet">
      ///   <item>
      ///      <description>
      ///         The wire joint segment referred to by the cut block has 
      ///         approximately 2.0 units in the actual Tooling Segments.
      ///      </description>
      ///   </item>
      ///   <item>
      ///      <description>
      ///         The start index of the nth cutting block shall be less than or 
      ///         equal to the end index of the nth cutting block.
      ///      </description>
      ///   </item>
      ///   <item>
      ///      <description>
      ///         The end index of the nth cutting block shall always be less than 
      ///         the end index of the (n+1)th cutting block.
      ///      </description>
      ///   </item>
      /// </list>
      /// </summary>
      /// <exception cref="Exception">Throws an exception if any of the conditions fail.</exception>
      void CheckSanityOfCutOutBlocks () {
         for (int ii = 0; ii < mCutOutBlocks.Count; ii++) {
            // Check if the wire joint segment from index of cutout block 
            // is indeed approximately 2.0 units
            if (mCutOutBlocks[ii].SectionType == NotchSectionType.WireJointTraceJumpForward ||
               mCutOutBlocks[ii].SectionType == NotchSectionType.WireJointTraceJumpForwardOnFlex) {
               if (mCutOutBlocks[ii].StartIndex != mCutOutBlocks[ii].EndIndex)
                  throw new Exception ("WJT start index is not equal to end");
               if (ToolingSegments[mCutOutBlocks[ii].StartIndex].Curve.Length.SLT (mWireJointDistance, 0.1)) {
                  int stIndex = mCutOutBlocks[ii].StartIndex;
                  var llen = ToolingSegments[mCutOutBlocks[ii].StartIndex].Curve.Length;
                  double cumLen = llen;

                  while (stIndex - 1 >= 0 && cumLen.SLT (mWireJointDistance)) {
                     cumLen += ToolingSegments[stIndex - 1].Curve.Length;
                     if (Math.Abs (cumLen - mWireJointDistance).EQ (0, 0.1))
                        break;
                     if (Math.Abs (cumLen - mWireJointDistance).SGT (0.1))
                        throw new Exception ("Cumulative WJT != Wire Joint Distance");
                     stIndex--;
                  }
               } else                   if (!mWireJointDistance.EQ (0) && Math.Abs (ToolingSegments[mCutOutBlocks[ii].StartIndex].Curve.Length - mWireJointDistance).GTEQ (0.4))                      throw new Exception ("Cumulative WJT != Wire Joint Distance");
            }

            // The start index of ii-th cutting block shall be lesser than or equal to ii-th end index
            // The end index of ii-th cutting block shall always be < the ii+1-th cutting block's end index
            if (ii + 1 < mCutOutBlocks.Count) {
               if (mCutOutBlocks[ii].StartIndex > mCutOutBlocks[ii].EndIndex)
                  throw new Exception ("ii-th Start Index > ii - th End Index");
               if (mCutOutBlocks[ii].EndIndex >= mCutOutBlocks[ii + 1].StartIndex)
                  throw new Exception ("ii-th End Index >= ii+1 - th Start Index");
            }

            if (mCutOutBlocks[ii].StartIndex >= ToolingSegments.Count)
               throw new Exception ("start index is greater than ToolingSegments count");
         }
      }

      void DoMachiningSegmentationForFlangesAndFlex () {
         mSegValLists = [];

         // ** Move to the Start point of the tooling whose normal is + Z axis **
         // The first tooling segment has to be on the web flange. UNless, rotate the list
         // by one element until the first element is on the web flange
         while (!ToolingSegments[0].Vec0.Normalized ().EQ (XForm4.mZAxis) ||
            !ToolingSegments[0].Vec1.Normalized ().EQ (XForm4.mZAxis)) { // CHANGE_CHANGE
            var ts = ToolingSegments[^1];
            ToolingSegments.RemoveAt (ToolingSegments.Count - 1);
            ToolingSegments.Insert (0, ts);
         }

         // ** Create cutout blocks for machining on plane and on flex **
         mCutOutBlocks = [];
         var flexSegIndices = Notch.GetFlexSegmentIndices (ToolingSegments);
         int startIdx = -1, endIdx = -1;
         int flexCount = 0;
         NotchSequenceSection cb;
         bool isFlexToolingFinal = false;
         for (int ii = 0; ii < ToolingSegments.Count; ii++)             if (flexSegIndices.Count > flexCount) {
               isFlexToolingFinal = true;
               if (ii < flexSegIndices[flexCount].Item1)                   if (startIdx == -1)
                     startIdx = ii;
               if (ii == flexSegIndices[flexCount].Item1) {
                  if (startIdx != -1)
                     endIdx = ii - 1;
                  if (startIdx != -1 && endIdx != -1) {
                     cb = new NotchSequenceSection (startIdx, endIdx, NotchSectionType.MachineToolingForward) {
                        Flange = GetArcPlaneFlangeType (ToolingSegments[ii].Vec0, GCGen.GetXForm ())
                     };
                     mCutOutBlocks.Add (cb);
                     startIdx = flexSegIndices[flexCount].Item1;
                     endIdx = -1;
                  }
               }
               if (ii == flexSegIndices[flexCount].Item2) {
                  if (startIdx != -1)
                     endIdx = ii;
                  if (startIdx != -1 && endIdx != -1) {
                     cb = new NotchSequenceSection (startIdx, endIdx, NotchSectionType.MachineFlexToolingForward) {
                        Flange = GetArcPlaneFlangeType (ToolingSegments[ii].Vec0, GCGen.GetXForm ())
                     };
                     mCutOutBlocks.Add (cb);
                     endIdx = -1;
                     startIdx = -1;
                  }
                  flexCount++;
               }
            } else                if (startIdx == -1) {
                  startIdx = ii;
                  isFlexToolingFinal = false;
               }
         endIdx = ToolingSegments.Count - 1;
         if (isFlexToolingFinal)
            cb = new NotchSequenceSection (startIdx, endIdx, NotchSectionType.MachineFlexToolingForward);
         else
            cb = new NotchSequenceSection (startIdx, endIdx, NotchSectionType.MachineToolingForward);

         cb.Flange = GetArcPlaneFlangeType (ToolingSegments[^1].Vec0, GCGen.GetXForm ());
         mCutOutBlocks.Add (cb);
         CheckSanityOfCutOutBlocks ();
      }

      void DoWireJointJumpTraceSegmentationForFlex () {
         // ** Add Wire joint sections in the cutout blocks at the start and end of the flex machining **
         double wjtLenAtFlex = GCGen.NotchWireJointDistance;
         if (wjtLenAtFlex < 0.5) wjtLenAtFlex = 2.0;
         for (int ii = 0; ii < mCutOutBlocks.Count; ii++) {
            bool nextBlockStartFlexMc = false;
            if (ii + 1 < mCutOutBlocks.Count)
               nextBlockStartFlexMc = mCutOutBlocks[ii].SectionType == NotchSectionType.MachineToolingForward &&
                   mCutOutBlocks[ii + 1].SectionType == NotchSectionType.MachineFlexToolingForward;

            bool nextBlockStartMc = false;
            if (ii + 1 < mCutOutBlocks.Count)
               nextBlockStartMc = mCutOutBlocks[ii].SectionType == NotchSectionType.MachineFlexToolingForward &&
                   mCutOutBlocks[ii + 1].SectionType == NotchSectionType.MachineToolingForward;

            bool needWJTForLastSection = false;
            if (ii - 1 >= 0 && mCutOutBlocks[ii].SectionType == NotchSectionType.MachineToolingForward &&
               mCutOutBlocks[ii - 1].SectionType == NotchSectionType.MachineFlexToolingForward)
               needWJTForLastSection = true;

            if (ii + 1 < mCutOutBlocks.Count && (nextBlockStartFlexMc || nextBlockStartMc || needWJTForLastSection)) {
               int mcBlockEndIndex = -1;
               if (nextBlockStartFlexMc)
                  mcBlockEndIndex = mCutOutBlocks[ii].EndIndex;
               else if (nextBlockStartMc)
                  mcBlockEndIndex = mCutOutBlocks[ii].EndIndex;
               bool revTrace = false;
               if (nextBlockStartFlexMc)                   revTrace = true;
               var (wjtPtAtFlex, segIndexToSplit) = Geom.EvaluatePointAndIndexAtLength (ToolingSegments, mcBlockEndIndex,
                  wjtLenAtFlex, reverseTrace: revTrace);

               var splitToolSegs = SplitToolingSegmentsAtPoint (ToolingSegments, segIndexToSplit, wjtPtAtFlex,
               ToolingSegments[segIndexToSplit].Vec0.Normalized ());
               // TEST_CODE CHANGE_CHANGE
               var wjtDist = ToolingSegments[mCutOutBlocks[ii].EndIndex].Curve.End.DistTo (wjtPtAtFlex);
               bool multiSegsWithinWJT = false;
               if (splitToolSegs.Count == 2) {
                  var cutoutSegs = ToolingSegments;

                  Notch.MergeSegments (ref splitToolSegs, ref cutoutSegs, segIndexToSplit);

                  wjtDist = ToolingSegments[mCutOutBlocks[ii].EndIndex].Curve.End.DistTo (wjtPtAtFlex);
                  ToolingSegments = cutoutSegs;
                  NotchSequenceSection cb;
                  if (nextBlockStartFlexMc) {
                     cb = new NotchSequenceSection (segIndexToSplit + 1, segIndexToSplit + 1, NotchSectionType.WireJointTraceJumpForwardOnFlex) {
                        Flange = GetArcPlaneFlangeType (ToolingSegments[mcBlockEndIndex].Vec0.Normalized (), XForm4.IdentityXfm)
                     };
                     mCutOutBlocks.Insert (ii + 1, cb);
                  } else if (nextBlockStartMc)                      if (mCutOutBlocks[ii].EndIndex != segIndexToSplit - 1) {
                        multiSegsWithinWJT = true;
                        var startPt = ToolingSegments[mCutOutBlocks[ii].EndIndex].Curve.End;
                        var startNormal = ToolingSegments[mCutOutBlocks[ii].EndIndex].Vec1;
                        var endPt = splitToolSegs[0].Curve.End;
                        Vector3 endNormal;

                        wjtDist = startPt.DistTo (endPt);

                        // Compute the normal at the wjtPtAtFlex
                        if (splitToolSegs[0].Curve is FCArc3)
                           endNormal = ToolingSegments[segIndexToSplit].Vec0;
                        else {
                           var t = splitToolSegs[0].Curve.Length / ToolingSegments[segIndexToSplit].Curve.Length;
                           endNormal = ToolingSegments[segIndexToSplit].Vec0 * (1 - t) + ToolingSegments[segIndexToSplit].Vec1 * t;
                        }

                        // Create new tooling segment from end of the curve at ToolingSegments[mCutOutBlocks[ii].EndIndex].Curve to
                        // the end of splitToolSegs[0].Curve
                        var ts = Geom.CreateToolingSegmentForCurve (new FCLine3 (startPt, endPt) as FCCurve3, startNormal, endNormal);
                        wjtDist = ts.Curve.Length;

                        // The wire joint segment in ToolingSegments with "mCutOutBlocks[ii].EndIndex + 1" -th seg 
                        // is the wire joint segment.
                        cb = new NotchSequenceSection (mCutOutBlocks[ii].EndIndex + 1, mCutOutBlocks[ii].EndIndex + 1, NotchSectionType.WireJointTraceJumpForwardOnFlex) {
                           Flange = GetArcPlaneFlangeType (endNormal, XForm4.IdentityXfm)
                        };
                        mCutOutBlocks.Insert (ii + 1, cb);

                        // No of segments to be removed
                        var nsegsToRemove = segIndexToSplit - mCutOutBlocks[ii].EndIndex;

                        // Remove the segments
                        int toolSegsRemovedCount = 0;
                        while (toolSegsRemovedCount < nsegsToRemove) {
                           ToolingSegments.RemoveAt (mCutOutBlocks[ii].EndIndex + 1);
                           toolSegsRemovedCount++;
                        }

                        // Insert the new tooling segment at index mCutOutBlocks[ii].EndIndex+1
                        ToolingSegments.Insert (mCutOutBlocks[ii].EndIndex + 1, ts);
                        toolSegsRemovedCount--;

                        for (int jj = ii + 2; jj < mCutOutBlocks.Count; jj++) {
                           var cutoutblk = mCutOutBlocks[jj];
                           cutoutblk.StartIndex += toolSegsRemovedCount;
                           cutoutblk.EndIndex += toolSegsRemovedCount;
                           mCutOutBlocks[jj] = cutoutblk;
                        }

                        // Test
                        wjtDist = ToolingSegments[mCutOutBlocks[ii].EndIndex + 1].Curve.Length;

                        // Modify the toolingsegments indices in the mCutOutBlocks
                     } else {
                        cb = new NotchSequenceSection (segIndexToSplit, segIndexToSplit, NotchSectionType.WireJointTraceJumpForwardOnFlex) {
                           Flange = GetArcPlaneFlangeType (ToolingSegments[mcBlockEndIndex].Vec0.Normalized (), XForm4.IdentityXfm)
                        };
                        mCutOutBlocks.Insert (ii + 1, cb);
                     }

                  if (multiSegsWithinWJT == false)                      for (int jj = ii + 2; jj < mCutOutBlocks.Count; jj++) {
                        var cutoutblk = mCutOutBlocks[jj];
                        cutoutblk.StartIndex += 1;
                        cutoutblk.EndIndex += 1;
                        mCutOutBlocks[jj] = cutoutblk;
                     }
               }
            }
         }
         CheckSanityOfCutOutBlocks ();
      }

      /// <summary>
      /// This predicate method finds if the hole is to be treated as a CutOut. Technically
      /// in this entire software, a CutOut is a closed contour curve that happens on one or 
      /// more flanges. In the case of Web flange closed contours, if the Y bounds is more
      /// than twice the minimum Cutout Length Threshold, it is also treated as CutOut.
      /// The commonality between the above is the necessity to create a Wire Joint Jump Trace
      /// </summary>
      /// <param name="segs">The input list of tooling segments</param>
      /// <param name="fullPartBound">The full bound of the part</param>
      /// <param name="minCutOutLengthThreshold">The minimum CutOut length threshold that
      /// is set in the settings.</param>
      /// <returns></returns>
      public static bool ToTreatAsCutOut (List<ToolingSegment> segs, Bound3 fullPartBound, double minCutOutLengthThreshold) {

         // condition to introduce wire joints on web flange
         bool toIntroduceWJT = false;
         var segBounds = GetToolingSegmentsBounds (segs, fullPartBound);
         var yMax = (double)segBounds.YMax; var yMin = (double)segBounds.YMin;
         var ySpan = yMax - yMin;
         if (Math.Abs (ySpan).GTEQ (2 * minCutOutLengthThreshold)) {
            foreach (var seg in segs)                if (GetArcPlaneFlangeType (seg.Vec0, XForm4.IdentityXfm) != EFlange.Web)
                  return false;
            toIntroduceWJT = true;
         }
         return toIntroduceWJT;
      }

      /// <summary>
      /// This method computes the PRE Wirejoint points. These points
      /// are the last points of the tooling block, from which a wire joint
      /// distance is left out. The condition for inserting these PRE Wire Joint
      /// Jump Traces is when the Y Max or Y Min is greater than or equal to 
      /// MinCutOutLengthThreshold (200 mm by default)
      /// </summary>
      /// <exception cref="Exception"></exception>
      void ComputePreWireJointPoints () {
         mPreWJTPts = [];

         // condition to introduce wire joints on web flange
         bool toIntroduceWJT = ToTreatAsCutOut (ToolingSegments, GCGen.Process.Workpiece.Bound, GCGen.MinCutOutLengthThreshold);

         // When will a web-flange notch be created with Wire joints other than at flex beginnings and at ends?
         // Create ePoints by filtering and projecting mCutOutBlocks
         // if (YMax - YMin) => YSpan >= 2 * MinCutoutLengthThreshold and if one of the flanges happens tobe bottom or top, NO WJT is created on flanges.
         // Wire joints are created at the beginning and at the ends of the Flex machining nevertheless.
         var ePoints = mCutOutBlocks
             .Where (block => block.SectionType == NotchSectionType.MachineToolingForward &&
             block.Flange == EFlange.Web && toIntroduceWJT)
             .Select (block => (
                 StPoint: ToolingSegments[block.StartIndex].Curve.Start,
                 EndPoint: ToolingSegments[block.EndIndex].Curve.End))
             .ToList ();
         if (ePoints.Count == 0)
            return;

         // Validate and populate preWJTPts
         var preWJTPts = new List<Point3> ();
         List<double> distances = [];
         foreach (var (StPoint, EndPoint) in ePoints) {
            // Find the start index
            int eStIdx = ToolingSegments.FindIndex (t => t.Curve.Start.EQ (StPoint!));

            // Validate start index
            if (eStIdx == -1)
               throw new Exception ("Start point not found in ToolingSegments");

            // Find the end index
            int eEndIdx = ToolingSegments.FindIndex (t =>
                t.Curve.End.EQ (EndPoint!) && ToolingSegments.IndexOf (t) > eStIdx);

            // Validate end index
            if (eEndIdx == -1) {
               eEndIdx = ToolingSegments.FindIndex (t =>
                t.Curve.End.EQ (EndPoint!));
               if (eEndIdx == -1)
                  throw new Exception ("End point not found in ToolingSegments");
               else if (eEndIdx == 0)
                  eEndIdx = eStIdx;
            }

            // Calculate distance
            Point3 modStartPoint;
            double dist;
            if (ToolingSegments.Count == 2 && IsCircle (ToolingSegments[eEndIdx].Curve)) {
               modStartPoint = ToolingSegments[eEndIdx].Curve.Start;
               dist = ToolingSegments[eEndIdx].Curve.Length;
               dist += ToolingSegments[eStIdx].Curve.Length;
            } else                dist = Geom.GetLengthBetween (ToolingSegments, StPoint, EndPoint, inSameOrder: true);
            distances.Add (dist);

            // Populate points
            PopulatePreWJTPts (StPoint, eStIdx, eEndIdx, preWJTPts);
         }

         // ** Get the distance between the st and end points **
         CheckSanityOfCutOutBlocks ();

         var dd = mDistances;
         // ** Segment the ToolingSegments at Pre Wire Joint Points **
         for (int ii = 0; ii < preWJTPts.Count; ii++) {
            var segIndexToSplit = ToolingSegments.FindIndex (t => Geom.IsPointOnCurve (t.Curve, preWJTPts[ii], t.Vec0));
            var cbIdx = mCutOutBlocks.FindIndex (cb => cb.StartIndex <= segIndexToSplit && segIndexToSplit <= cb.EndIndex);

            Point3 stPt;
            if (ii == 0) stPt = ToolingSegments[segIndexToSplit].Curve.Start;
            else stPt = preWJTPts[ii - 1];
            var d = Geom.GetLengthBetween (ToolingSegments, stPt, preWJTPts[ii], inSameOrder: true);

            var splitToolSegs = SplitToolingSegmentsAtPoint (ToolingSegments, segIndexToSplit, preWJTPts[ii],
                                       ToolingSegments[segIndexToSplit].Vec0.Normalized ());
            if (splitToolSegs.Count == 2) {
               var cutoutSegs = ToolingSegments;
               Notch.MergeSegments (ref splitToolSegs, ref cutoutSegs, segIndexToSplit);
               ToolingSegments = cutoutSegs;

               var cbBlock = mCutOutBlocks[cbIdx];
               var prevEndIndex = cbBlock.EndIndex;
               cbBlock.EndIndex = segIndexToSplit;
               mCutOutBlocks[cbIdx] = cbBlock;

               NotchSequenceSection cb = new (segIndexToSplit + 1, prevEndIndex + 1, NotchSectionType.MachineToolingForward) {
                  // Set the flange to wweb. 
                  // Note: The wire joint points are computed
                  // only for web flange machining segments
                  Flange = EFlange.Web
               };
               mCutOutBlocks.Insert (cbIdx + 1, cb);

               int incr = 1; // one for WJT and the other for actual index of the next start
               for (int kk = cbIdx + 2; kk < mCutOutBlocks.Count; kk++) {
                  var cutoutblk = mCutOutBlocks[kk];
                  cutoutblk.StartIndex += incr;
                  cutoutblk.EndIndex += incr;
                  mCutOutBlocks[kk] = cutoutblk;
               }
            }
            mPreWJTPts.Add (preWJTPts[ii]);
            CheckSanityOfCutOutBlocks ();
         }
         CheckSanityOfCutOutBlocks ();
      }

      void DoWireJointJumpTraceSegmentationForFlanges () {
         // ** Compute Pre Wire Joint Points on the tooling segments
         //var tLen = ToolingItem.Length;
         ComputePreWireJointPoints ();
         for (int ii = 0; ii < mPreWJTPts.Count; ii++)             for (int jj = 0; jj < mCutOutBlocks.Count; jj++) {
               var preWJTPtIndex = ToolingSegments.FindIndex (t => t.Curve.End.EQ (mPreWJTPts[ii]));
               if (preWJTPtIndex == -1) throw new Exception ("The index of pre wire joint point in tooling segments is -1");

               if (mCutOutBlocks[jj].StartIndex <= preWJTPtIndex && preWJTPtIndex <= mCutOutBlocks[jj].EndIndex &&
                  mCutOutBlocks[jj].SectionType == NotchSectionType.MachineToolingForward) {
                  var (wjtPt, segIndexToSplit) = Geom.GetPointAtLengthFrom (mPreWJTPts[ii], GCGen.NotchWireJointDistance, ToolingSegments);
                  var splitToolSegs = SplitToolingSegmentsAtPoint (ToolingSegments, segIndexToSplit, wjtPt,
                                       ToolingSegments[segIndexToSplit].Vec0.Normalized ());
                  if (splitToolSegs.Count == 2) {

                     var cutoutSegs = ToolingSegments;
                     Notch.MergeSegments (ref splitToolSegs, ref cutoutSegs, segIndexToSplit);
                     ToolingSegments = cutoutSegs;

                     var stIndex = mCutOutBlocks[jj].StartIndex; var endIndex = mCutOutBlocks[jj].EndIndex;
                     int cbIncr = jj;
                     NotchSequenceSection cb;
                     if (stIndex < segIndexToSplit) {
                        cb = mCutOutBlocks[jj];
                        cb.EndIndex = segIndexToSplit - 1;
                        mCutOutBlocks[jj] = cb;
                        cbIncr++;
                     }

                     cb = new (segIndexToSplit, segIndexToSplit, NotchSectionType.WireJointTraceJumpForward) {
                        Flange = EFlange.Web
                     };
                     mCutOutBlocks.Insert (cbIncr, cb);
                     cbIncr++;
                     int incr = 1; // one for WJT and the other for actual index of the next start
                     incr = segIndexToSplit - mCutOutBlocks[cbIncr].StartIndex + 1;
                     for (int kk = cbIncr; kk < mCutOutBlocks.Count; kk++) {
                        var cutoutblk = mCutOutBlocks[kk];
                        cutoutblk.StartIndex += incr;
                        cutoutblk.EndIndex += incr;
                        mCutOutBlocks[kk] = cutoutblk;
                     }
                     CheckSanityOfCutOutBlocks ();
                  } else if (splitToolSegs.Count == 1) {
                     // This means, the segment with index "segIndexToSplit" is the
                     // resulting segment after split. This means, the end point is the
                     // interested point.

                     // So the cut block ending with segIndexToSplit-1 is intact. 
                     // A new cut block with index segIndexToSplit is to be made WireJointTraceJumpForward
                     // and inserted at jj+1

                     // The previous cut block which has index from segIndexToSplit to prevEndIndex
                     // needs to be changed its start index alone to segIndexToSplit+1
                     CheckSanityOfCutOutBlocks ();
                     var nextStartIndex = mCutOutBlocks[jj + 1].StartIndex;
                     var nextEndIndex = mCutOutBlocks[jj + 1].EndIndex;

                     // Reassign the end index of the current cutout block
                     NotchSequenceSection cb;
                     if (nextStartIndex == segIndexToSplit) {
                        cb = new (segIndexToSplit, segIndexToSplit, NotchSectionType.WireJointTraceJumpForward) {
                           Flange = EFlange.Web
                        };
                        mCutOutBlocks.Insert (jj + 1, cb);
                        cb = mCutOutBlocks[jj + 2];
                        cb.StartIndex = segIndexToSplit + 1;
                        mCutOutBlocks[jj + 2] = cb;
                     }
                     if (mCutOutBlocks[jj].StartIndex == segIndexToSplit) {
                        var cbb = mCutOutBlocks[jj];
                        cbb.SectionType = NotchSectionType.WireJointTraceJumpForward;
                        mCutOutBlocks[jj] = cbb;
                     }
                     CheckSanityOfCutOutBlocks ();
                  }
                  break;
               }
            }
      }

      List<double> mDistances = [];
      // Local function for populating preWJTPts
      void PopulatePreWJTPts (Point3 startPoint, int startIndex, int endIndex, List<Point3> points) {
         var iPoint = startPoint;
         if (startIndex > endIndex) throw new Exception ("Start index > End Index. Wrong!");

         // Compute 25%, 50% and 75% lengths for segments from startIndex to endIndex
         double[] percentages = [0.05, 0.25, 0.5, 0.75];
         var toolingLengthBetween = Geom.GetLengthBetween (ToolingSegments, startIndex, endIndex);
         for (int ii = 0; ii < percentages.Length; ii++) {
            var percentLength = toolingLengthBetween * percentages[ii];
            var (preWJTPt, segIndexToSplit) = Geom.GetPointAtLengthFrom (iPoint, percentLength, ToolingSegments);
            if (segIndexToSplit == -1 || segIndexToSplit < startIndex) break;
            double dist;
            try {
               var isCircle = IsCircle (ToolingSegments[segIndexToSplit].Curve);
               dist = Geom.GetLengthBetween (ToolingSegments, iPoint, preWJTPt, inSameOrder: true);
               mDistances.Add (dist);
            } catch (Exception) { break; }
            if (segIndexToSplit == -1 || segIndexToSplit > endIndex)                if (segIndexToSplit == -1)
                  throw new Exception ("In PopulatePreWJTPts: Segment Index to split is -1");
               else
                  throw new Exception ("In PopulatePreWJTPts: segIndexToSplit > endIndex");
            points.Add (preWJTPt);
         }
      }

      /// <summary>
      /// This is the method that segments ( splits and merges) Tooling Segments 
      /// </summary>
      /// <exception cref="Exception"></exception>
      void PerformToolingSegmentation () {
         if (GCGen.CreateDummyBlock4Master)
            return;
         CheckToolingSegs (ToolingSegments);
         DoMachiningSegmentationForFlangesAndFlex ();
         CheckToolingSegs (ToolingSegments);
         DoWireJointJumpTraceSegmentationForFlex ();
         DoWireJointJumpTraceSegmentationForFlanges ();
      }
      #endregion

      static void CheckToolingSegs (List<ToolingSegment> tss) {
         for (int ii = 1; ii < tss.Count; ii++)             if (!tss[ii].Curve.Start.EQ (tss[ii - 1].Curve.End)) {
               var ts1 = Geom.CreateToolingSegmentForCurve (new FCLine3 (tss[ii - 1].Curve.End, tss[ii].Curve.Start), tss[ii - 1].Vec1, tss[ii].Vec0);
               tss.Insert (ii, ts1);
               ii--;
            }
      }
      #region G Code Writers
      public override void WriteTooling () {

         if (ToolingItem.IsDualFlangeCutoutNotch ())
            WriteDualFlangeCutout ();
         else
            WriteCutoutSlot ();
      }
      void WriteCutoutSlot () {
         bool continueMachining = false;
         for (int ii = 0; ii < mCutOutBlocks.Count; ii++) {
            var cutoutSequence = mCutOutBlocks[ii];
            switch (cutoutSequence.SectionType) {
               case NotchSectionType.WireJointTraceJumpForwardOnFlex:
                  throw new Exception ("There can not be any WJT on Flex, as the slot does not have any flex machining");

               case NotchSectionType.WireJointTraceJumpForward:
                  if (ii == 0) throw new Exception ("CutOut writing starts from Wire Joint Jump Trace, which is wrong");
                  Vector3 scrapSideNormal = GetMaterialRemovalSideDirection (ToolingSegments[cutoutSequence.StartIndex],
                     ToolingSegments[cutoutSequence.StartIndex].Curve.End, EKind.Cutout, ToolingItem.ProfileKind);
                  string comment;
                  if (cutoutSequence.SectionType == NotchSectionType.WireJointTraceJumpForward)
                     comment = "(( ** CutOut: Wire Joint Jump Trace Forward Direction ** ))";
                  else
                     comment = "(( ** CutOut: Wire Joint Jump Trace Forward Direction on Flex** ))";
                  var wjtTS = ToolingSegments[cutoutSequence.StartIndex];
                  if (cutoutSequence.SectionType == NotchSectionType.WireJointTraceJumpReverse) {
                     wjtTS = Geom.GetReversedToolingSegment (wjtTS);
                     comment = "((** CutOut: Wire Joint Jump Trace Reverse Direction ** ))";
                  }

                  //bool isNextSeqFlexMc = (ii + 1 < mCutOutBlocks.Count && mCutOutBlocks[ii + 1].SectionType == NotchSectionType.MachineFlexToolingForward);
                  //bool isPrevSeqFlexMc = (ii - 1 >= 0 && mCutOutBlocks[ii - 1].SectionType == NotchSectionType.MachineFlexToolingForward);
                  EFlange flangeType = GetArcPlaneFlangeType (wjtTS.Vec1, GCGen.GetXForm ());

                  //if (isNextSeqFlexMc && cutoutSequence.SectionType != NotchSectionType.WireJointTraceJumpForwardOnFlex)
                  //   throw new Exception ("next seq is flex cut but current seq type is not NotchSectionType.WireJointTraceJumpForwardOnFlex");

                  mFlexStartRef = GetMachiningSegmentPostWJT (wjtTS, scrapSideNormal, GCGen.Process.Workpiece.Bound, NotchApproachLength);
                  // Here it is assumed that if the next sequence block is machining on flex, in which case,
                  // the current sequence type is WireJointTraceJumpForwardOnFlex, the tooling block 
                  // shall be completed and so toCompleteToolingBlock is true, false otherwise
                  //if (cutoutSequence.SectionType == NotchSectionType.WireJointTraceJumpForwardOnFlex) {
                  //   GCGen.WriteWireJointTrace (wjtTS, nextSeg: null, scrapSideNormal,
                  //      mMostRecentPrevToolPosition.Value, NotchApproachLength, ref mPrevPlane, flangeType, ToolingItem,
                  //      ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                  //         isFlexCut: false, isValidNotch: false, flexRefTS: mFlexStartRef, out _,
                  //         toCompleteToolingBlock: isNextSeqFlexMc|| isPrevSeqFlexMc, comment);
                  //   PreviousToolingSegment = new (mFlexStartRef.Value.Curve, PreviousToolingSegment.Value.Vec1, mFlexStartRef.Value.Vec0);
                  //   mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                  //}

                  // Here it is assumed that if the next sequence block is machining on flex, in which case,
                  // the current sequence type is WireJointTraceJumpForwardOnFlex, the tooling block 
                  // shall be completed and so toCompleteToolingBlock is true, false otherwise
                  //if (isNextSeqFlexMc || isPrevSeqFlexMc) {
                  //   string comment1;
                  //   if (isNextSeqFlexMc)
                  //      comment1 = comment + " Cutout : Seperate second tooling block for WJT as first part of upcoming flex cut";
                  //   else
                  //      comment1 = comment + " Cutout : Seperate second tooling block for WJT as first part of previous flex cut";

                  //   GCGen.WriteWireJointTrace (wjtTS, nextSeg: ToolingSegments[mCutOutBlocks[ii + 1].StartIndex], scrapSideNormal,
                  //         mMostRecentPrevToolPosition.Value, NotchApproachLength, ref mPrevPlane, flangeType, ToolingItem,
                  //         ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                  //            isFlexCut: true, isValidNotch: false, flexRefTS: mFlexStartRef, out _, toCompleteToolingBlock: false, comment);

                  //} else 
                  {
                     GCGen.WriteWireJointTrace (wjtTS, nextSeg: null, scrapSideNormal,
                           mMostRecentPrevToolPosition.Value, NotchApproachLength, ref mPrevPlane, flangeType, ToolingItem,
                           ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                              isFlexCut: false, isValidNotch: false, flexRefTS: mFlexStartRef, out _, toCompleteToolingBlock: false, comment,
                              relativeCoords: false, firstWJTTrace: true);
                  }
                  PreviousToolingSegment = new (mFlexStartRef.Value.Curve, PreviousToolingSegment.Value.Vec1, mFlexStartRef.Value.Vec0);
                  mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                  continueMachining = true;

                  break;
               case NotchSectionType.MachineToolingForward: {
                     Tuple<Point3, Vector3> cutoutEntry;
                     if (cutoutSequence.StartIndex > cutoutSequence.EndIndex)
                        throw new Exception ("In CutOut.WriteTooling: MachineToolingForward : startIndex > endIndex");
                     if (!continueMachining) {
                        GCGen.RapidMoveToPiercingPositionWithPingPong = false;
                        GCGen.InitializeNotchToolingBlock (ToolingItem, prevToolingItem: null, ToolingSegments,
                           ToolingSegments[cutoutSequence.StartIndex].Vec0, mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mCutOutBlocks.Count - 1,
                           /*isToBeTreatedAsCutOut: mFeatureToBeTreatedAsCutout,*/ isValidNotch: false, cutoutSequence.StartIndex, cutoutSequence.EndIndex,
                           refSegIndex: 0, comment: "CutOutSequence: Machining Forward Direction", isShortPerimeterNotch: false, nextTs: null);
                     } else {
                        string titleComment = Utils.GetGCodeComment ("CutOutSequence: Machining Forward Direction");
                        GCGen.WriteLineStatement (titleComment);
                     }
                     if (ii == 0) {
                        cutoutEntry = Tuple.Create (ToolingSegments[0].Curve.Start, ToolingSegments[0].Vec0);
                        GCGen.PrepareforToolApproach (ToolingItem, ToolingSegments, PreviousToolingSegment, mPrevToolingItem,
                           mPrevToolingSegs, mIsFirstTooling, isValidNotch: false, notchEntry:cutoutEntry);
                        if (!GCGen.RapidMoveToPiercingPositionWithPingPong)
                           GCGen.RapidMoveToPiercingPosition (ToolingSegments[0].Curve.Start, ToolingSegments[0].Vec0, 
                              EKind.Cutout, usePingPongOption: true, comment:"");
                     }
                     if (!continueMachining) {
                        GCGen.RapidMoveToPiercingPositionWithPingPong = false;
                        if (ii == 0) {
                           cutoutEntry = Tuple.Create (ToolingSegments[0].Curve.Start, ToolingSegments[0].Vec0);
                           GCGen.MoveToMachiningStartPosition (cutoutEntry.Item1, cutoutEntry.Item2, ToolingItem.Name);
                        }
                        var isFromWebFlange = IsMachiningFromWebFlange (ToolingSegments, cutoutSequence.StartIndex);
                        GCGen.WriteToolCorrectionData (ToolingItem);
                        GCGen.RapidMoveToPiercingPosition (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
                              ToolingSegments[cutoutSequence.StartIndex].Vec0, EKind.Cutout, usePingPongOption: false, comment:"");

                        GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        GCGen.WriteToolDiaCompensation (isFlexTooling: false);
                        GCGen.EnableMachiningDirective ();
                     }
                     for (int jj = cutoutSequence.StartIndex; jj <= cutoutSequence.EndIndex; jj++) {
                        mExitTooling = ToolingSegments[jj];
                        GCGen.WriteCurve (ToolingSegments[jj], ToolingItem.Name, relativeCoords: false, refStPt: null);
                        mBlockCutLength += ToolingSegments[jj].Curve.Length;
                     }
                     PreviousToolingSegment = ToolingSegments[cutoutSequence.EndIndex];
                     GCGen.DisableMachiningDirective ();
                     mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                     GCGen.FinalizeNotchToolingBlock (ToolingItem, mBlockCutLength, mTotalToolingsCutLength);
                     continueMachining = false;
                  }
                  break;
               //case NotchSectionType.MachineFlexToolingForward: {
               //      if (ii == 0) throw new Exception ("CutOut writing starts from Flex side machining, which is wrong");
               //      if (cutoutSequence.StartIndex > cutoutSequence.EndIndex)
               //         throw new Exception ("In CutOut.WriteTooling: MachineFlexToolingForward : startIndex > endIndex");
               //      if (!continueMachining) {
               //         GCGen.RapidMoveToPiercingPositionWithPingPong = false;
               //         GCGen.InitializeNotchToolingBlock (ToolingItem, prevToolingItem: null, ToolingSegments, ToolingSegments[cutoutSequence.StartIndex].Vec0,
               //            mXStart, mXPartition, mXEnd, isFlexCut: true, ii == mCutOutBlocks.Count - 1,
               //            //isToBeTreatedAsCutOut:mFeatureToBeTreatedAsCutout,
               //            isValidNotch: false,
               //            cutoutSequence.StartIndex,
               //            cutoutSequence.EndIndex, refSegIndex: cutoutSequence.StartIndex,
               //            "CutOutSequence: Flex machining Forward Direction");
               //      }
               //      {
               //         if (!continueMachining) {
               //            GCGen.RapidMoveToPiercingPositionWithPingPong = false;
               //            var isFromWebFlange = Utils.IsMachiningFromWebFlange (ToolingSegments, cutoutSequence.StartIndex);
               //            GCGen.RapidMoveToPiercingPosition (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
               //               ToolingSegments[cutoutSequence.StartIndex].Vec0, EKind.Cutout, usePingPongOption: true);
               //            GCGen.WriteToolCorrectionData (ToolingItem);
               //            GCGen.RapidMoveToPiercingPosition (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
               //               ToolingSegments[cutoutSequence.StartIndex].Vec0, EKind.Cutout, usePingPongOption: false);
               //            GCGen.EnableMachiningDirective ();
               //         }
               //         GCGen.WriteLineStatement (GCodeGenerator.GetGCodeComment ("CutOutSequence: Machining in Flex in Forward Direction"));
               //         for (int jj = cutoutSequence.StartIndex; jj <= cutoutSequence.EndIndex; jj++) {
               //            GCGen.WriteFlexLineSeg (ToolingSegments[jj],
               //            isWJTStartCut: false, ToolingItem.Name, flexRefSeg: mFlexStartRef);
               //            mExitTooling = ToolingSegments[jj];
               //            mBlockCutLength += ToolingSegments[jj].Curve.Length;
               //            PreviousToolingSegment = ToolingSegments[jj];
               //         }
               //         mFlexStartRef = null;

               //         // The next in sequence has to be wire joint jump trace and so
               //         // continueMachining is made to false
               //         GCGen.DisableMachiningDirective ();
               //         mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
               //      }
               //      GCGen.WriteLineStatement (GCGen.NotchCutEndToken);
               //      GCGen.FinalizeNotchToolingBlock (ToolingItem, mBlockCutLength, mTotalToolingsCutLength);
               //   }
               //   continueMachining = false;
               //   break;

               default:
                  throw new Exception ("Undefined CutOut sequence");
            }
         }
      }
      void WriteDualFlangeCutout () {
         // The tooling machining is done using multiple tooling blocks. Continue machining for each sequence 
         // means, continue machining wothout creating a new tooling block, continuing from previous sequence section type.
         bool continueMachining = false;
         Tuple<Point3, Vector3> cutoutEntry;
         for (int ii = 0; ii < mCutOutBlocks.Count; ii++) {
            var cutoutSequence = mCutOutBlocks[ii];
            switch (cutoutSequence.SectionType) {
               case NotchSectionType.WireJointTraceJumpForward:
               case NotchSectionType.WireJointTraceJumpForwardOnFlex:
                  if (ii == 0) throw new Exception ("CutOut writing starts from Wire Joint Jump Trace, which is wrong");
                  Vector3 scrapSideNormal = GetMaterialRemovalSideDirection (ToolingSegments[cutoutSequence.StartIndex],
                     ToolingSegments[cutoutSequence.StartIndex].Curve.End, EKind.Cutout, ToolingItem.ProfileKind);
                  string comment;
                  if (cutoutSequence.SectionType == NotchSectionType.WireJointTraceJumpForward)
                     comment = " ** Dual Flange Notch: WJT Forward Direction ** ";
                  else
                     comment = comment = " ** Dual Flange Notch: WJT Reverse Direction ** ";
                  var wjtTS = ToolingSegments[cutoutSequence.StartIndex];
                  if (cutoutSequence.SectionType == NotchSectionType.WireJointTraceJumpReverse) {
                     wjtTS = Geom.GetReversedToolingSegment (wjtTS);
                     comment = "** Dual Flange Notch: WJT Reverse Direction ** ))";
                  }

                  bool isNextSeqFlexMc = ii + 1 < mCutOutBlocks.Count && mCutOutBlocks[ii + 1].SectionType == NotchSectionType.MachineFlexToolingForward;
                  bool isPrevSeqFlexMc = ii - 1 >= 0 && mCutOutBlocks[ii - 1].SectionType == NotchSectionType.MachineFlexToolingForward;
                  EFlange flangeType = GetArcPlaneFlangeType (wjtTS.Vec1, GCGen.GetXForm ());
                  if (isNextSeqFlexMc && cutoutSequence.SectionType != NotchSectionType.WireJointTraceJumpForwardOnFlex)
                     throw new Exception ("next seq is flex cut but current seq type is not NotchSectionType.WireJointTraceJumpForwardOnFlex");

                  mFlexStartRef = GetMachiningSegmentPostWJT (wjtTS, scrapSideNormal, GCGen.Process.Workpiece.Bound, NotchApproachLength);
                  if (mMostRecentPrevToolPosition == null)
                     throw new Exception ($"Cutout.WriteTooling: {cutoutSequence.SectionType}, index-{ii} Most recent prev tool absolute position is null");

                  // DEBUG_DEBUG
                  //Point3? recentOutPos = null;
                  // Here it is assumed that if the next sequence block is machining on flex, in which case,
                  // the current sequence type is WireJointTraceJumpForwardOnFlex, the tooling block 
                  // shall be completed and so toCompleteToolingBlock is true, false otherwise
                  var comment1 = comment + " First WJT Tool Block ";
                  if (cutoutSequence.SectionType == NotchSectionType.WireJointTraceJumpForwardOnFlex) {
                     GCGen.WriteWireJointTrace (wjtTS, nextSeg: null, scrapSideNormal,
                        mMostRecentPrevToolPosition.Value, NotchApproachLength, ref mPrevPlane, flangeType, ToolingItem,
                        ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                           isFlexCut: false, isValidNotch: true, flexRefTS: mFlexStartRef, out mMostRecentPrevToolPosition,
                           toCompleteToolingBlock: true, comment1, relativeCoords: true, firstWJTTrace: true);
                     PreviousToolingSegment = new (mFlexStartRef.Value.Curve, PreviousToolingSegment.Value.Vec1, mFlexStartRef.Value.Vec0);
                     mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                  }

                  // Here it is assumed that if the next sequence block is machining on flex, in which case,
                  // the current sequence type is WireJointTraceJumpForwardOnFlex, the tooling block 
                  // shall be completed and so toCompleteToolingBlock is true, false otherwise
                  if (isNextSeqFlexMc || isPrevSeqFlexMc) {
                     ToolingSegment? nextTS = null;

                     // Next tooling segment is used for writing BlockType for
                     // Web to Flange starting flex. It can be upwards or downwards
                     if (ii + 1 < mCutOutBlocks.Count)
                        nextTS = ToolingSegments[mCutOutBlocks[ii + 1].StartIndex];
                     comment1 = comment + " Second WJT Tool Block ";
                     GCGen.WriteWireJointTrace (wjtTS, nextSeg: nextTS, scrapSideNormal,
                           mMostRecentPrevToolPosition.Value, NotchApproachLength, ref mPrevPlane, flangeType, ToolingItem,
                           ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                              isFlexCut: true, isValidNotch: true, flexRefTS: mFlexStartRef, out mMostRecentPrevToolPosition,
                              toCompleteToolingBlock: false, comment1, relativeCoords: true, firstWJTTrace: false);

                     PreviousToolingSegment = new (mFlexStartRef.Value.Curve, PreviousToolingSegment.Value.Vec1, mFlexStartRef.Value.Vec0);
                     mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                  }
                  continueMachining = true;
                  //else {
                  //   GCGen.WriteWireJointTrace (wjtTS, nextSeg: null, scrapSideNormal,
                  //         mMostRecentPrevToolPosition.Value, NotchApproachLength, ref mPrevPlane, flangeType, ToolingItem,
                  //         ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                  //            isFlexCut: false, isValidNotch: false, flexRefTS: mFlexStartRef, out mMostRecentPrevToolPosition,
                  //            toCompleteToolingBlock: false, comment, relativeCoords: true, firstWJTTrace: false);
                  //}


                  break;
               case NotchSectionType.MachineToolingForward: {
                     if (cutoutSequence.StartIndex > cutoutSequence.EndIndex)
                        throw new Exception ("In CutOut.WriteTooling: MachineToolingForward : startIndex > endIndex");

                     if (!continueMachining) {
                        GCGen.RapidMoveToPiercingPositionWithPingPong = false;
                        GCGen.InitializeNotchToolingBlock (ToolingItem, prevToolingItem: null, ToolingSegments,
                           ToolingSegments[cutoutSequence.StartIndex].Vec0, mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mCutOutBlocks.Count - 1,
                           /*isToBeTreatedAsCutOut: mFeatureToBeTreatedAsCutout,*/ isValidNotch: false, cutoutSequence.StartIndex, cutoutSequence.EndIndex,
                           refSegIndex: 0, comment: "CutOutSequence: Machining Forward Direction", isShortPerimeterNotch: false, nextTs: null);

                        cutoutEntry = Tuple.Create (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
                           ToolingSegments[cutoutSequence.StartIndex].Vec0);
                        if (ii == 0) {
                           GCGen.PrepareforToolApproach (ToolingItem, ToolingSegments, PreviousToolingSegment, mPrevToolingItem,
                              mPrevToolingSegs, mIsFirstTooling, isValidNotch: false, notchEntry:cutoutEntry);
                           GCGen.MoveToMachiningStartPosition (cutoutEntry.Item1, cutoutEntry.Item2, ToolingItem.Name);
                           GCGen.RapidMoveToPiercingPosition (ToolingSegments[0].Curve.Start, ToolingSegments[0].Vec0, EKind.Cutout, usePingPongOption: true, comment: "");
                        }

                        //GCGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");

                        var isFromWebFlange = IsMachiningFromWebFlange (ToolingSegments, cutoutSequence.StartIndex);
                        ////GCGen.PrepareforToolApproach (ToolingItem, ToolingSegments, PreviousToolingSegment, mPrevToolingItem,
                        ////   mPrevToolingSegs, mIsFirstTooling, isValidNotch: false, cutoutEntry);
                        //GCGen.RapidMoveToPiercingPosition (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
                        //      ToolingSegments[cutoutSequence.StartIndex].Vec0, EKind.Cutout, usePingPongOption: false);

                        //GCGen.MoveToMachiningStartPosition (cutoutEntry.Item1, cutoutEntry.Item2, ToolingItem.Name);
                        mMostRecentPrevToolPosition = cutoutEntry.Item1;


                        //GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: false);
                        //GCGen.WriteLineStatement (GCGen.NotchCutStartToken);
                        //GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        //GCGen.EnableMachiningDirective ();

                        GCGen.WriteToolCorrectionData (ToolingItem);
                        GCGen.WriteLineStatement (GCGen.NotchCutStartToken);
                        GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        GCGen.WriteToolDiaCompensation (isFlexTooling: false);
                        GCGen.EnableMachiningDirective ();

                     } else {
                        string titleComment = Utils.GetGCodeComment ("CutOutSequence: Machining Forward Direction");
                        GCGen.WriteLineStatement (titleComment);
                     }

                     for (int jj = cutoutSequence.StartIndex; jj <= cutoutSequence.EndIndex; jj++) {
                        mExitTooling = ToolingSegments[jj];
                        GCGen.WriteCurve (ToolingSegments[jj], ToolingItem.Name, relativeCoords: true, refStPt: mMostRecentPrevToolPosition);
                        mBlockCutLength += ToolingSegments[jj].Curve.Length;
                     }

                     PreviousToolingSegment = ToolingSegments[cutoutSequence.EndIndex];
                     GCGen.DisableMachiningDirective ();
                     GCGen.WriteLineStatement (GCGen.NotchCutEndToken);
                     mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                     GCGen.FinalizeNotchToolingBlock (ToolingItem, mBlockCutLength, mTotalToolingsCutLength);
                     continueMachining = false;
                  }
                  break;
               case NotchSectionType.MachineFlexToolingForward: {
                     if (ii == 0) throw new Exception ("CutOut writing starts from Flex side machining, which is wrong");
                     if (cutoutSequence.StartIndex > cutoutSequence.EndIndex)
                        throw new Exception ("In CutOut.WriteTooling: MachineFlexToolingForward : startIndex > endIndex");

                     if (!continueMachining) {
                        GCGen.RapidMoveToPiercingPositionWithPingPong = false;
                        GCGen.InitializeNotchToolingBlock (ToolingItem, prevToolingItem: null, ToolingSegments, ToolingSegments[cutoutSequence.StartIndex].Vec0,
                           mXStart, mXPartition, mXEnd, isFlexCut: true, ii == mCutOutBlocks.Count - 1,
                           //isToBeTreatedAsCutOut:mFeatureToBeTreatedAsCutout,
                           isValidNotch: false,
                           cutoutSequence.StartIndex,
                           cutoutSequence.EndIndex, refSegIndex: cutoutSequence.StartIndex,
                           "CutOutSequence: Flex machining Forward Direction", isShortPerimeterNotch: false, nextTs: null);
                        GCGen.WriteLineStatement (Utils.GetGCodeComment ("CutOutSequence: Machining in Flex in Forward Direction"));
                        var isFromWebFlange = IsMachiningFromWebFlange (ToolingSegments, cutoutSequence.StartIndex);
                        GCGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");

                        cutoutEntry = Tuple.Create (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
                           ToolingSegments[cutoutSequence.StartIndex].Vec0);
                        GCGen.RapidMoveToPiercingPosition (ToolingSegments[cutoutSequence.StartIndex].Curve.Start,
                           ToolingSegments[cutoutSequence.StartIndex].Vec0, EKind.Cutout, usePingPongOption: true, comment: "");
                        //GCGen.MoveToMachiningStartPosition (cutoutEntry.Item1, cutoutEntry.Item2, ToolingItem.Name);
                        mMostRecentPrevToolPosition = ToolingSegments[cutoutSequence.StartIndex].Curve.Start;

                        //GCGen.WriteToolDiaCompensation (isFlexTooling: true);
                        //GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: false);

                        //GCGen.WriteLineStatement (GCGen.NotchCutStartToken);
                        //GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        //GCGen.EnableMachiningDirective ();

                        GCGen.WriteToolCorrectionData (ToolingItem);
                        GCGen.WriteLineStatement (GCGen.NotchCutStartToken);
                        GCGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        GCGen.WriteToolDiaCompensation (isFlexTooling: true);
                        GCGen.EnableMachiningDirective ();
                     }
                     {
                        if (mFlexStartRef == null)
                           throw new Exception ("Error mFlexStartRef = null");
                        for (int jj = cutoutSequence.StartIndex; jj <= cutoutSequence.EndIndex; jj++) {
                           GCGen.WriteFlexLineSeg (ToolingSegments[jj],
                           isWJTStartCut: false, ToolingItem.Name, flexRefSeg: mFlexStartRef, lineSegmentComment:"");
                           mExitTooling = ToolingSegments[jj];
                           mBlockCutLength += ToolingSegments[jj].Curve.Length;
                           PreviousToolingSegment = ToolingSegments[jj];
                        }
                        mFlexStartRef = null;

                        // The next in sequence has to be wire joint jump trace and so
                        // continueMachining is made to false
                        GCGen.DisableMachiningDirective ();
                        mMostRecentPrevToolPosition = GCGen.GetLastToolHeadPosition ().Item1;
                     }

                     GCGen.WriteLineStatement (GCGen.NotchCutEndToken);
                     GCGen.FinalizeNotchToolingBlock (ToolingItem, mBlockCutLength, mTotalToolingsCutLength);
                  }
                  continueMachining = false;
                  break;

               default:
                  throw new Exception ("Undefined CutOut sequence");
            }
         }
      }
      #endregion
   }
}