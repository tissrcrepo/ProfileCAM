using ChassisCAM.Core.GCodeGen;
using Flux.API;
using static ChassisCAM.Core.Utils;

namespace ChassisCAM.Core.Geometries {
   public struct SegmentedPositionType {
      public const string PreFlex1WJTStart = "PreFlex1WJTStart", Flex1WJTStart = "Flex1WJTStart",
            Flex1WJTEnd = "Flex1WJTEnd", PostFlex1WJTEnd = "PostFlex1WJTEnd";
      public const string PreFlex2WJTStart = "PreFlex2WJTStart", Flex2WJTStart = "Flex2WJTStart",
            Flex2WJTEnd = "Flex2WJTEnd", PostFlex2WJTEnd = "PostFlex2WJTEnd";
      public const string Approach = "Approach";
      public const string GambitPreApproach = "GambitPreApproach";
      public const string GambitPostApproach = "GambitPostApproach";
      public const string Start = "Start";
      public const string End = "End";
      public static string WJTStart (int index) => $"PlaneWJTStart{index}";
      public static string WJTEnd (int index) => $"PlaneWJTEnd{index}";
      public const string None = "None";
   }
   public struct NotchPosition (Point3 pt, int idx, double param, string segPosTYpe) : IComparable<NotchPosition> {
      public Point3 Position { get; set; } = pt;
      public int Index { get; set; } = idx;
      public string SegPositionType { get; set; } = segPosTYpe;
      public readonly bool IsPlaneFlexWJTStart () => SegPositionType.Contains ("PlaneWJTStart");
      public readonly bool IsPlaneFlexWJTEnd () => SegPositionType.Contains ("PlaneWJTEnd");
      public double Param { get; set; } = param;
      public static bool operator <= (NotchPosition a, NotchPosition b) => a.Param.LTEQ (b.Param);
      public static bool operator >= (NotchPosition a, NotchPosition b) => a.Param.GTEQ (b.Param);
      public static bool operator < (NotchPosition a, NotchPosition b) => a.Param.SLT (b.Param);
      public static bool operator > (NotchPosition a, NotchPosition b) => a.Param.SGT (b.Param);
      public readonly bool LieWithin (NotchPosition a, NotchPosition b, double tolerance = 1e-6)
          => Param.LieWithin (a.Param, b.Param, tolerance) || Param.LieWithin (b.Param, a.Param, tolerance);
      public static bool operator == (NotchPosition a, NotchPosition b) => a.Param.EQ (b.Param) && a.SegPositionType == b.SegPositionType;
      public static bool operator != (NotchPosition a, NotchPosition b) => !(a == b);
      public double DistTo (NotchPosition rhs, double totalPathLen) => (rhs.Param - Param) * totalPathLen;
      public override readonly bool Equals (object obj) {
         if (obj is not NotchPosition)
            return false;

         NotchPosition other = (NotchPosition)obj;
         return this == other; // Reuse the == operator
      }
      // Implement CompareTo using your overloaded operators
      public readonly int CompareTo (NotchPosition other) {
         // Compare based on Param first
         if (this.Param.LTEQ (other.Param))
            return -1; // This instance is less than the other
         if (this.Param.SGT (other.Param))
            return 1;  // This instance is greater than the other

         // If the Param values are equal, compare the SegPositionType
         return string.Compare (this.SegPositionType, other.SegPositionType, StringComparison.Ordinal);
      }
      public override readonly int GetHashCode () {
         unchecked // Overflow is fine
         {
            int hash = 17;
            hash = hash * 23 + Param.GetHashCode ();
            hash = hash * 23 + (SegPositionType?.GetHashCode () ?? 0);
            return hash;
         }
      }
   }
   public struct SegmentedPosition (Point3 pos, string segPostType = SegmentedPositionType.None) {
      Point3 mPos = pos;
      string mSegPosType = segPostType;
      public string SegPostType { get => mSegPosType; set { if (mSegPosType != value) mSegPosType = value; } }
   }
   public class NotchToolPath {
      #region Properties
      public List<NotchPosition> NotchPositions { get => mNotchPos; }
      public IEnumerable<ToolingSegment> Segs { get => mTs; }
      public double PathLength { get; set; } = 0;
      public double NotchMinThresholdLength { get; set; } = 0;
      public double NotchWJTLength { get; set; } = 0;
      public (int SegIndex, Point3 NPosition, double Param, Vector3 Normal, double SegParam)? ApproachParameters { get; set; }
      #endregion Properties
      #region Fields
      public List<ToolingSegment> mTs = [];
      public List<ToolingSegment> mRevTs = [];
      List<NotchPosition> mNotchPos = [];
      List<NotchSequenceSection> mNotchSequences = [];
      double mLeastWireJointLength;
      #endregion Fields
      #region Constructor
      public NotchToolPath (List<ToolingSegment> segments, double notchMinThresholdLen, double notchWJTLength, double leastWireJointLength) {
         mTs = [.. segments.Select (seg => /*Geom.CreateToolingSegmentForCurve (seg, seg.Curve, seg.NotchSectionType, clone: true)*/seg.Clone ())];
         PathLength = mTs.Sum (seg => seg.Length);
         mRevTs = Geom.GetReversedToolingSegments (segments);
         NotchWJTLength = notchWJTLength;
         NotchMinThresholdLength = notchMinThresholdLen;
         mLeastWireJointLength = leastWireJointLength;
      }
      #endregion Constructor

      #region Segmentation for notches
      void CheckApproachSegsSanity () {
         var segIndxPreApp = mNotchPos[mNotchPos.FindIndex (np => np.SegPositionType == SegmentedPositionType.GambitPreApproach)].Index;
         var segindxApp = mNotchPos[mNotchPos.FindIndex (np => np.SegPositionType == SegmentedPositionType.Approach)].Index;
         var segindxPostApp = mNotchPos[mNotchPos.FindIndex (np => np.SegPositionType == SegmentedPositionType.GambitPostApproach)].Index;
         if (!mTs[segindxApp].Length.EQ (NotchWJTLength, 1e-1))
            throw new Exception ($"Segment length is not NotchWJTLength {NotchWJTLength}");
         if (!mTs[segindxPostApp].Length.EQ (NotchWJTLength, 1e-1))
            throw new Exception ($"Segment length is not NotchWJTLength {NotchWJTLength}");
      }
      void MergeSegsAtApproach () {
         var segIndxPreApp = mNotchPos[mNotchPos.FindIndex (np => np.SegPositionType == SegmentedPositionType.GambitPreApproach)].Index;
         var segindxApp = mNotchPos[mNotchPos.FindIndex (np => np.SegPositionType == SegmentedPositionType.Approach)].Index;
         var segindxPostApp = mNotchPos[mNotchPos.FindIndex (np => np.SegPositionType == SegmentedPositionType.GambitPostApproach)].Index;
         int stSegIndex = -1, endSegIndex = -1;
         if (segindxApp - segIndxPreApp > 1) {
            stSegIndex = segIndxPreApp + 1; endSegIndex = segindxApp;
            if (endSegIndex > stSegIndex)
               Geom.MergeSegments (mTs, stSegIndex, endSegIndex);
         }
         if (segindxPostApp - segindxApp > 1) {
            stSegIndex = segindxApp + 1; endSegIndex = segindxPostApp;
            if (endSegIndex > stSegIndex)
               Geom.MergeSegments (mTs, stSegIndex, endSegIndex);
         }
         UpdateNotchSpecIndicesAndApproachData ();
      }
      void MergeSegsNearPlanarWireJoints () {
         // Merge multiple tooling segments happening within @planar wire joints' segments
         int wjtStPrevIdx = -1, wjtStIdx, wjtEndIx = -1;
         for (int ii = 0; ii < mNotchPos.Count; ii++) {
            // FlexWJTStart index is one before the actual WJT segment. 
            // Ideally, FlexWJTEnd should be the very next segment to FlexWJTStart
            // If the FlexWJTEnd is not very next to FlexWJTStart, then the segments 
            // needs to be merged
            if (mNotchPos[ii].IsPlaneFlexWJTStart ())
               wjtStPrevIdx = mNotchPos[ii].Index;
            if (wjtStPrevIdx != -1 && mNotchPos[ii].IsPlaneFlexWJTEnd ()) {
               wjtStIdx = wjtStPrevIdx + 1; // The segment of wjt starts from wjtStPrevIdx+1
               wjtEndIx = mNotchPos[ii].Index;
               if (wjtEndIx > wjtStIdx) {
                  Geom.MergeSegments (mTs, wjtStIdx, wjtEndIx);
                  UpdateNotchSpecIndicesAndApproachData ();
               }
            }
            if (wjtEndIx != -1) {
               wjtEndIx = -1;
               wjtStIdx = -1;
            }
         }
      }

      /// <summary>
      /// Merge multiple tooling segments that happen within @flex wire joints
      /// Remember:   FlexWJTEndIdx and flexWJTStartIdx should be the same. 
      /// If they are not, merge is required
      /// </summary>
      /// <param name="flexWJTType"></param>
      /// <exception cref="Exception"></exception>
      void MergeSegsNearFlexWireJoints (string flexWJTType) {
         string preOrPostFlexWJT = flexWJTType switch {
            SegmentedPositionType.Flex1WJTStart => SegmentedPositionType.PreFlex1WJTStart,
            SegmentedPositionType.Flex1WJTEnd => SegmentedPositionType.PostFlex1WJTEnd,
            SegmentedPositionType.Flex2WJTStart => SegmentedPositionType.PreFlex2WJTStart,
            SegmentedPositionType.Flex2WJTEnd => SegmentedPositionType.PostFlex2WJTEnd,
            _ => throw new Exception ($"NotchToolPath.MergeSegmentsNearFlexWireJoints: Unknow type {flexWJTType}")
         };

         // Flex1WJTStart and Flex2WJTStart are wire joints. They are the first segments to be encountered
         // if the machining is gonna happen on the flex. The segment with Flex1WJTStart and Flex2WJTStart
         // should have wire joint length
         if (flexWJTType == SegmentedPositionType.Flex1WJTStart || flexWJTType == SegmentedPositionType.Flex2WJTStart) {
            // In this  flex1/2 start index is the wire joint segment index
            int flexWJTEndIdx, flexWJTStartIdx = -1, preOrPostFlexWJTIdx = -1;
            for (int ii = 0; ii < mNotchPos.Count; ii++) {
               if (mNotchPos[ii].SegPositionType == preOrPostFlexWJT)
                  preOrPostFlexWJTIdx = mNotchPos[ii].Index;
               if (preOrPostFlexWJTIdx != -1 && mNotchPos[ii].SegPositionType == flexWJTType) {
                  // The seg with index (preOrPostFlexWJTIdx + 1) ideally be wire joint segment
                  // If the index is more than the above index, then are many tiny segments from 
                  // preOrPostFlexWJTIdx + 1 to mNotchPos[ii].Index then all the tiny segments
                  // needed to be merged.
                  flexWJTStartIdx = preOrPostFlexWJTIdx + 1;
                  flexWJTEndIdx = mNotchPos[ii].Index;
                  if (flexWJTEndIdx > flexWJTStartIdx) {
                     Geom.MergeSegments (mTs, flexWJTStartIdx, flexWJTStartIdx);
                     UpdateNotchSpecIndicesAndApproachData ();
                  }
               }
               if (flexWJTStartIdx != -1) {
                  flexWJTStartIdx = -1;
                  preOrPostFlexWJTIdx = -1;
               }
            }
         } else { // flexWJTType == SegmentedPositionType.Flex1WJTEnd || flexWJTType == SegmentedPositionType.Flex2WJTEnd
            // In this case, post flex1/2 end index is the wire joint segment index
            int postFlexWJTEndIdx = -1, flexWJTIdx = -1, postFlexWJTStIdx;
            for (int ii = 0; ii < mNotchPos.Count; ii++) {
               if (mNotchPos[ii].SegPositionType == flexWJTType)
                  flexWJTIdx = mNotchPos[ii].Index;
               if (flexWJTIdx != -1 && mNotchPos[ii].SegPositionType == preOrPostFlexWJT) {
                  // The seg with index (preOrPostFlexWJTIdx + 1) ideally be wire joint segment
                  // If the index is more than the above index, then are many tiny segments from 
                  // preOrPostFlexWJTIdx + 1 to mNotchPos[ii].Index then all the tiny segments
                  // needed to be merged.
                  postFlexWJTEndIdx = mNotchPos[ii].Index;
                  postFlexWJTStIdx = flexWJTIdx + 1;
                  if (postFlexWJTEndIdx > postFlexWJTStIdx) {
                     Geom.MergeSegments (mTs, postFlexWJTStIdx, postFlexWJTEndIdx);
                     UpdateNotchSpecIndicesAndApproachData ();
                  }
               }
               if (postFlexWJTEndIdx != -1) {
                  postFlexWJTEndIdx = -1;
                  flexWJTIdx = -1;
               }
            }
         }
      }
      public void SegmentPath () {
         // Checking
         EvalPositionsSanityCheck ();
         double angle = 0; Utils.EArcSense sense = Utils.EArcSense.Infer;
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            int segIndex = mNotchPos[ii].Index;

            // If the current segindex-th curve is arc when the previous segment is line
            if ((segIndex - 1 >= 0 && mTs[segIndex - 1].Curve is Line3 && mTs[segIndex].Curve is Arc3) ||
               (segIndex == 0 && mTs[segIndex].Curve is Arc3)
               ) {
               Arc3 firstArc = mTs[segIndex].Curve as Arc3;
               (angle, sense) = Geom.GetArcAngleAndSense (firstArc, mTs[segIndex].Vec0.Normalized (), hintSense: Utils.EArcSense.Infer);
            } else if (mTs[segIndex].Curve is Line3) {
               sense = EArcSense.Infer;
            }

            // FCH - 40 defect fix
            // targetPos.Value.segIndex should be equal to endSegmentIndex, if the length of 
            // Segment with endSegmentIndex is >= wjt distance (2). In cases when the Segment with endSegmentIndex
            // is < 2.0 the following if block becomes true. targetPos point happens on the index lesser than endSegmentIndex
            // and so the idea is to split the endSegmentIndex-1-th segment at the point where the split-2 segment_th_length + 
            // endSegmentIndex-th segment length = WJT distance.
            // Create a new segment uniting the last split segment with endSegmentIndex-segment. 
            // remove split-2 segment and endSegmentIndex-th segment and add the above newly created segment.
            // NOTE: Since the wjt length is very minimal, a line segment is always created even if the endSegmentIndex-1-th segment 
            // is an arc
            // Example: Arc 1, 2 and then line 2-3. The -lengthfromPt occurs at 2', on Arc 1-2, but between 1-2, near 2, so that the
            // length of arc from 2' to the line length of 2-3 is WJT. In this case, I split Arcs to form Arc 1-2' and Arc2' - 2, 
            // remove the arc 1-2 and line 2-3, and in the place add Arc1-2' and Line2'-3 . 2' is targetPos.Value.position. 
            // The index of Arc1-2 is targetPos.Value.segIndex and index of Line3 is endSegmentIndex
            if (mNotchPos[ii].SegPositionType == SegmentedPositionType.Flex1WJTStart) {

               // Find the index of the PreFlex1WJTStart notch point in the Segments List mTs. If the PreFlex1WJTStart's index 
               // is < flexWJTSegEndNotchPoint , then the following corrective measures have to be made.
               var preFlexWJTSegEndNotchPoint = mNotchPos[ii - 1].Position; // May not be the end of the PreFlex1WJTStart's segment.
               var preFlexWJTSegEndNotchPointSegIndex = mTs.FindIndex (ts => ts.Curve.End.DistTo (preFlexWJTSegEndNotchPoint).EQ (0));
               if (preFlexWJTSegEndNotchPointSegIndex == -1)
                  preFlexWJTSegEndNotchPointSegIndex = mTs.FindIndex (ts => Geom.IsPointOnCurve (ts.Curve, preFlexWJTSegEndNotchPoint, ts.Vec0));

               var flexWJTSegEndNotchPoint = mNotchPos[ii].Position;
               var flexWJTSegEndNotchPointIdx = mTs.FindIndex (ts => ts.Curve.End.DistTo (flexWJTSegEndNotchPoint).EQ (0));

               // By assumption/design notch point with type Flex1WJTStart's end point should actually match with mTs[ii].Flex1WJTStart's end point
               if (flexWJTSegEndNotchPointIdx == -1)
                  throw new Exception ("Flex1WJTStart segment's end position is not matching with notch point of type Flex1WJTStart ");

               var flex1WJTEndSeg = mTs[flexWJTSegEndNotchPointIdx];
               if (preFlexWJTSegEndNotchPointSegIndex < flexWJTSegEndNotchPointIdx) {

                  var newSegEndPoint = flex1WJTEndSeg.Curve.End;
                  var splitCurves = Geom.SplitCurve (mTs[preFlexWJTSegEndNotchPointSegIndex].Curve, [preFlexWJTSegEndNotchPoint], mTs[preFlexWJTSegEndNotchPointSegIndex].Vec0);
                  var ts1 = new ToolingSegment (splitCurves[0], mTs[preFlexWJTSegEndNotchPointSegIndex].Vec0, mTs[preFlexWJTSegEndNotchPointSegIndex].Vec1);

                  var line = new Line3 (preFlexWJTSegEndNotchPoint, newSegEndPoint);
                  var ts2 = new ToolingSegment (line, mTs[preFlexWJTSegEndNotchPointSegIndex].Vec1, mTs[preFlexWJTSegEndNotchPointSegIndex].Vec1);
                  List<ToolingSegment> newTSS = [ts1, ts2];

                  // Remove the higher index first (j), then the lower one (i)
                  int i = preFlexWJTSegEndNotchPointSegIndex; int j = flexWJTSegEndNotchPointIdx;

                  for (int kk = j; kk >= i; kk--)
                     mTs.RemoveAt (kk);
                  int segsRemoved = j - i + 1;

                  // Now insert the new segments at position i
                  mTs.InsertRange (i, newTSS);
                  int segsAdded = 2;

                  // for the elements added/deleted, update the mNotchPos's indiex values.
                  // These Index values point to the indices of segments list mTs
                  for (int kk = ii + 1; kk < mNotchPos.Count; kk++)
                     mNotchPos[kk] = mNotchPos[kk] with { Index = mNotchPos[kk].Index + (segsAdded - segsRemoved) };
               } else {


                  //if (mTs[segIndex].Curve is Arc3 arc)
                  //   (angle, sense) = Geom.GetArcAngleAtPoint (arc, new Vector3 (0, 0, 1));
                  var crvs = Geom.SplitCurve (mTs[segIndex].Curve,
                                                       [mNotchPos[ii].Position],
                                                       mTs[segIndex].Vec0.Normalized (), hintSense: sense, tolerance: (mTs[segIndex].Curve is Arc3) ? 1e-3 : 1e-6);
                  if (crvs.Count > 1) {
                     var toolSegsForCrvs = Geom.CreateToolingSegmentForCurves (mTs[segIndex], crvs);
                     mTs.RemoveAt (segIndex);
                     mTs.InsertRange (segIndex, toolSegsForCrvs);

                     // Reindex the mNotchPos
                     for (int jj = ii + 1; jj < mNotchPos.Count; jj++) {
                        var npos = mNotchPos[jj];
                        npos.Index += 1;
                        mNotchPos[jj] = npos;
                     }
                  }
               }
            } else {
               var crvs = Geom.SplitCurve (mTs[segIndex].Curve,
                                                    [mNotchPos[ii].Position],
                                                    mTs[segIndex].Vec0.Normalized (), hintSense: sense, tolerance: (mTs[segIndex].Curve is Arc3) ? 1e-3 : 1e-6);
               if (crvs.Count > 1) {
                  var toolSegsForCrvs = Geom.CreateToolingSegmentForCurves (mTs[segIndex], crvs);
                  mTs.RemoveAt (segIndex);
                  mTs.InsertRange (segIndex, toolSegsForCrvs);

                  // Reindex the mNotchPos
                  for (int jj = ii + 1; jj < mNotchPos.Count; jj++) {
                     var npos = mNotchPos[jj];
                     npos.Index += 1;
                     mNotchPos[jj] = npos;
                  }
               }
            }
         }

         // Checking
         UpdateNotchSpecIndicesAndApproachData ();

         MergeSegsAtApproach ();
         MergeSegsNearPlanarWireJoints ();
         MergeSegsNearFlexWireJoints (SegmentedPositionType.Flex1WJTStart);
         MergeSegsNearFlexWireJoints (SegmentedPositionType.Flex1WJTEnd);
         MergeSegsNearFlexWireJoints (SegmentedPositionType.Flex2WJTStart);
         MergeSegsNearFlexWireJoints (SegmentedPositionType.Flex2WJTEnd);

         // Testing and asserting
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            double tol = 1e-6;
            if (mTs[mNotchPos[ii].Index].Curve is Arc3) tol = 1e-4;
            if (!mNotchPos[ii].Position.DistTo (mTs[mNotchPos[ii].Index].Curve.End).EQ (0, tol))
               throw new Exception ("Segmentation fault");
         }

         ToolingSegment ts;
         // Set the SegmentedPositionType string on Tooling Segment
         // -------------------------------------------------------
         int fromSegIndex = mNotchPos[0].Index;
         int npIx;
         for (npIx = 1; npIx < mNotchPos.Count - 1; npIx++) {
            switch (mNotchPos[npIx].SegPositionType) {
               case SegmentedPositionType.Start:
               case SegmentedPositionType.PreFlex1WJTStart:
               case SegmentedPositionType.PreFlex2WJTStart:
               case SegmentedPositionType.GambitPreApproach:
               case SegmentedPositionType.Approach:
               case SegmentedPositionType.GambitPostApproach:
                  for (int jj = fromSegIndex; jj < mNotchPos[npIx].Index; jj++) {
                     ts = mTs[jj];
                     ts.NotchSectionType = NotchSectionType.MachineToolingForward;
                     mTs[jj] = ts;
                     fromSegIndex = jj + 1;
                  }

                  // mNotchPos[npIx].Index-th index in mNotchPositions list is significant.
                  // The previous elements are NotchSectionType.MachineToolingForward only
                  ts = mTs[fromSegIndex];
                  if (mNotchPos[npIx].SegPositionType == SegmentedPositionType.GambitPreApproach)
                     ts.NotchSectionType = NotchSectionType.GambitPreApproachMachining;
                  else if (mNotchPos[npIx].SegPositionType == SegmentedPositionType.Approach)
                     ts.NotchSectionType = NotchSectionType.ApproachMachining;
                  else if (mNotchPos[npIx].SegPositionType == SegmentedPositionType.GambitPostApproach)
                     ts.NotchSectionType = NotchSectionType.GambitPostApproachMachining;
                  else if (mNotchPos[npIx].SegPositionType == SegmentedPositionType.PreFlex1WJTStart ||
                     mNotchPos[npIx].SegPositionType == SegmentedPositionType.PreFlex2WJTStart)
                     ts.NotchSectionType = NotchSectionType.MachineToolingForward;
                  mTs[fromSegIndex] = ts;
                  fromSegIndex++;
                  break;
               case SegmentedPositionType.Flex1WJTStart:
               case SegmentedPositionType.Flex2WJTStart:
                  if (mNotchPos[npIx].Index != fromSegIndex)
                     throw new Exception ("Wire joint tool segments index is not only one in number");
                  ts = mTs[fromSegIndex];
                  ts.NotchSectionType = NotchSectionType.WireJointTraceJumpForwardOnFlex;
                  mTs[fromSegIndex] = ts;
                  fromSegIndex = fromSegIndex + 1;
                  break;

               case SegmentedPositionType.Flex1WJTEnd:
               case SegmentedPositionType.Flex2WJTEnd:
                  for (int jj = fromSegIndex; jj <= mNotchPos[npIx].Index; jj++) {
                     ts = mTs[jj];
                     ts.NotchSectionType = NotchSectionType.MachineFlexToolingForward;
                     mTs[jj] = ts;
                     fromSegIndex = jj + 1;
                  }
                  break;

               case SegmentedPositionType.PostFlex1WJTEnd:
               case SegmentedPositionType.PostFlex2WJTEnd:
                  if (mNotchPos[npIx].Index != fromSegIndex)
                     throw new Exception ("Wire joint tool segments index is not only one in number");
                  ts = mTs[fromSegIndex];
                  ts.NotchSectionType = NotchSectionType.WireJointTraceJumpForwardOnFlex;
                  mTs[fromSegIndex] = ts;
                  fromSegIndex = fromSegIndex + 1;
                  break;
               default:
                  break;
            }
            if (mNotchPos[npIx].IsPlaneFlexWJTStart ()) {
               for (int jj = fromSegIndex; jj <= mNotchPos[npIx].Index; jj++) {
                  ts = mTs[jj];
                  ts.NotchSectionType = NotchSectionType.MachineToolingForward;
                  mTs[jj] = ts;
                  fromSegIndex = jj + 1;
               }
            } else if (mNotchPos[npIx].IsPlaneFlexWJTEnd ()) {
               if (mNotchPos[npIx].Index != fromSegIndex)
                  throw new Exception ("Wire joint tool segments index is not only one in number");
               ts = mTs[fromSegIndex];
               ts.NotchSectionType = NotchSectionType.WireJointTraceJumpForward;
               mTs[fromSegIndex] = ts;
               fromSegIndex = fromSegIndex + 1;
            }
         }

         // Testing and asserting
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            double tol = 1e-6;
            if (mTs[mNotchPos[ii].Index].Curve is Arc3) tol = 1e-4;
            if (!mNotchPos[ii].Position.DistTo (mTs[mNotchPos[ii].Index].Curve.End).EQ (0, tol))
               throw new Exception ("Segmentation fault");
         }

         // Fill the last left out tooling segments with Machining forward
         for (int ii = fromSegIndex; ii < mTs.Count; ii++) {
            if (mTs[ii].NotchSectionType != NotchSectionType.None)
               throw new Exception ($"NotchToolPath.SegmentPath: {ii}-th segment's NotchSectionType is valid and being overwritten");
            ts = mTs[ii];
            ts.NotchSectionType = NotchSectionType.MachineToolingForward;
            mTs[ii] = ts;
         }

         for (int ii = 0; ii < mTs.Count; ii++) {
            if (mTs[ii].NotchSectionType == NotchSectionType.None)
               throw new Exception ($"NotchToolPath.SegmentPath: {ii}-th segment's NotchSectionType is NULL. Setting missed for {ii}-th segment");
         }

         // Testing and asserting
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            double tol = 1e-6;
            if (mTs[mNotchPos[ii].Index].Curve is Arc3) tol = 1e-4;
            if (!mNotchPos[ii].Position.DistTo (mTs[mNotchPos[ii].Index].Curve.End).EQ (0, tol))
               throw new Exception ("Segmentation fault");
         }

         //// Testing and asserting
         //for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
         //   if (!mNotchPos[ii].Position.DistTo (mTs[mNotchPos[ii].Index].Curve.End).EQ (0))
         //      throw new Exception ("Segmentation fault");
         //}

         ts = mTs[0];
         if (ts.NotchSectionType == NotchSectionType.None) {
            ts.NotchSectionType = NotchSectionType.Start;
            mTs[0] = ts;
         }

         // Testing and asserting
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            double tol = 1e-6;
            if (mTs[mNotchPos[ii].Index].Curve is Arc3) tol = 1e-4;
            if (!mNotchPos[ii].Position.DistTo (mTs[mNotchPos[ii].Index].Curve.End).EQ (0, tol))
               throw new Exception ("Segmentation fault");
         }

         ts = mTs[^1];
         if (ts.NotchSectionType == NotchSectionType.None) {
            ts.NotchSectionType = NotchSectionType.End;
            mTs[^1] = ts;
         }

         // Testing and asserting
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            double tol = 1e-6;
            if (mTs[mNotchPos[ii].Index].Curve is Arc3) tol = 1e-4;
            if (!mNotchPos[ii].Position.DistTo (mTs[mNotchPos[ii].Index].Curve.End).EQ (0, tol))
               throw new Exception ("Segmentation fault");
         }
         for (int ii = 0; ii < mTs.Count; ii++) {
            if (mTs[ii].NotchSectionType == NotchSectionType.None)
               throw new Exception ($"{ii}-th segment is not set with NotchSectionType");
         }

         UpdateNotchSpecIndicesAndApproachData ();
      }


      public void UpdateNotchSpecIndicesAndApproachData () {
         for (int ii = 1; ii < mNotchPos.Count - 1; ii++) {
            //var segIndex = mTs.FindIndex (ts => ts.Curve.End.DistTo (mNotchPos[ii].Position).EQ (0));
            int segIndex = -1;
            for (int kk = 0; kk < mTs.Count; kk++) {
               var dist = mTs[kk].Curve.End.DistTo (mNotchPos[ii].Position);
               double tol = 1e-6;
               if (mTs[kk].Curve is Arc3) tol = 1e-3;
               if (dist.EQ (0, tol)) {
                  segIndex = kk;
                  break;
               }
            }
            if (segIndex == -1)
               throw new Exception ("NotchToolPath.UpdateNotchSpecIndicesAndApproachData: Notch position not found in Segs list");
            var npos = mNotchPos[ii];
            npos.Index = segIndex;
            mNotchPos[ii] = npos;
         }
         // Update ApproachParameters property
         (int segIndex, double param, double segParam, Point3 position)? approachPosData = null;
         if (ApproachParameters == null)
            approachPosData = ParamAtPositionFromPointAtLength (mTs[0].Curve.Start, 0.5 * PathLength);
         else
            approachPosData = ParamAtPositionFromPointAtLength (mTs[0].Curve.Start, ApproachParameters.Value.Param * PathLength);
         var approachParamData = ParamAtPosition (approachPosData.Value.position);
         ApproachParameters = (approachPosData.Value.segIndex, approachPosData.Value.position, approachPosData.Value.param,
            approachParamData.Value.normal, approachPosData.Value.segParam);
      }

      List<NotchSequenceSection> CreateNotchForwardSequencesFromApproach () {
         List<NotchSequenceSection> forwardSeqs = [];

         // Get the index of ApproachMachining from tss
         var fromIdx = mTs.FindIndex (ts => ts.NotchSectionType == NotchSectionType.ApproachMachining);
         if (fromIdx == -1) throw new Exception ($"Section type {NotchSectionType.ApproachMachining} does not feature in the List of tooling segments");
         int N = mTs.Count;
         int startIndex = fromIdx;
         for (int ii = fromIdx; ii < N; ii++) {
            // The sequences of forward notch sections are continuous machining except when 
            // obstructed by a wire joint on plane or on flex
            switch (mTs[ii].NotchSectionType) {
               case NotchSectionType.WireJointTraceJumpForward:
               case NotchSectionType.WireJointTraceJumpForwardOnFlex:
                  // Create machining notch sequence forward
                  if (mTs[ii - 1].NotchSectionType == NotchSectionType.MachineFlexToolingForward)
                     forwardSeqs.Add (Notch.CreateNotchSequence (startIndex, (ii - 1), NotchSectionType.MachineFlexToolingForward));
                  else if (mTs[ii - 1].NotchSectionType == NotchSectionType.MachineToolingForward ||
                     mTs[ii - 1].NotchSectionType == NotchSectionType.GambitPostApproachMachining)
                     forwardSeqs.Add (Notch.CreateNotchSequence (startIndex, (ii - 1), NotchSectionType.MachineToolingForward));

                  if (mTs[ii].NotchSectionType == NotchSectionType.WireJointTraceJumpForward)
                     // Create wire joint jump trace forward
                     forwardSeqs.Add (Notch.CreateNotchSequence (ii, ii, NotchSectionType.WireJointTraceJumpForward));
                  else
                     // Create wire joint jump trace forward on flex
                     forwardSeqs.Add (Notch.CreateNotchSequence (ii, ii, NotchSectionType.WireJointTraceJumpForwardOnFlex));
                  startIndex = ii + 1;
                  break;
               default:
                  break;
            }
         }

         // Complete the notch sections
         forwardSeqs.Add (Notch.CreateNotchSequence (startIndex, N - 1, NotchSectionType.MachineToolingForward));
         return forwardSeqs;
      }

      List<NotchSequenceSection> CreateNotchReverseSequencesFromApproach () {
         List<NotchSequenceSection> reverseSeqs = [];
         var tss = Geom.GetReversedToolingSegments (mTs);

         // Need to swap GambitPre and GambitPost
         var gambitPreIndex = tss.FindIndex (ts => ts.NotchSectionType == NotchSectionType.GambitPreApproachMachining);
         var gambitPostIndex = tss.FindIndex (ts => ts.NotchSectionType == NotchSectionType.GambitPostApproachMachining);
         if (gambitPreIndex == -1 || gambitPostIndex == -1)
            throw new Exception ("Either GambitPreApproachMachining or GambitPreApproachMachining or both  not found in tooling segments");

         // Swap Gambit pre and Post
         var tsGPre = tss[gambitPreIndex];
         tsGPre.NotchSectionType = NotchSectionType.GambitPostApproachMachining;
         tss[gambitPreIndex] = tsGPre;

         var tsGPost = tss[gambitPostIndex];
         tsGPost.NotchSectionType = NotchSectionType.GambitPreApproachMachining;
         tss[gambitPostIndex] = tsGPost;

         // Get the index of ApproachMachining from tss
         var fromIdx = tss.FindIndex (ts => ts.NotchSectionType == NotchSectionType.ApproachMachining);
         if (fromIdx == -1) throw new Exception ($"Section type {NotchSectionType.ApproachMachining} does not feature in the List of tooling segments");
         int N = tss.Count;
         int startIndex = fromIdx;
         for (int ii = fromIdx; ii < N; ii++) {
            // The sequences of reverse notch sections are continuous machining except when 
            // obstructed by a wire joint on plane or on flex
            switch (tss[ii].NotchSectionType) {
               case NotchSectionType.WireJointTraceJumpForward:
               case NotchSectionType.WireJointTraceJumpForwardOnFlex:
                  // Create machining notch sequence reverse
                  if (tss[ii - 1].NotchSectionType == NotchSectionType.MachineFlexToolingForward)
                     reverseSeqs.Add (Notch.CreateNotchSequence (N - 1 - startIndex, N - 1 - (ii - 1), NotchSectionType.MachineFlexToolingReverse));
                  else if (tss[ii - 1].NotchSectionType == NotchSectionType.MachineToolingForward ||
                     tss[ii - 1].NotchSectionType == NotchSectionType.GambitPostApproachMachining)
                     reverseSeqs.Add (Notch.CreateNotchSequence (N - 1 - startIndex, N - 1 - (ii - 1), NotchSectionType.MachineToolingReverse));

                  if (tss[ii].NotchSectionType == NotchSectionType.WireJointTraceJumpForward)
                     // Create wire joint jump trace reverse
                     reverseSeqs.Add (Notch.CreateNotchSequence (N - 1 - ii, N - 1 - ii, NotchSectionType.WireJointTraceJumpReverse));
                  else
                     // Create wire joint jump trace reverse on flex
                     reverseSeqs.Add (Notch.CreateNotchSequence (N - 1 - ii, N - 1 - ii, NotchSectionType.WireJointTraceJumpReverseOnFlex));
                  startIndex = ii + 1;
                  break;
               default:
                  break;
            }
         }
         // Complete the notch sections
         reverseSeqs.Add (Notch.CreateNotchSequence (N - 1 - startIndex, 0, NotchSectionType.MachineToolingReverse));
         return reverseSeqs;
      }
      public List<NotchSequenceSection> GetNotchSequences () {
         mNotchSequences.Clear ();
         mNotchSequences.Add (Notch.CreateApproachToNotchSequence ());

         // Assemble the tooling sequence sections
         var approachIndex = mTs.FindIndex (ts => ts.NotchSectionType == NotchSectionType.ApproachMachining);
         var postApproachIndex = mTs.FindIndex (ts => ts.NotchSectionType == NotchSectionType.GambitPostApproachMachining);
         if (Notch.IsForwardFirstNotchTooling (mTs)) {
            mNotchSequences.Add (Notch.CreateNotchSequence (approachIndex, approachIndex, NotchSectionType.GambitPreApproachMachining));
            // Collect all the sections till the last tooling segment in mTs
            mNotchSequences.AddRange (CreateNotchForwardSequencesFromApproach ());
            mNotchSequences.Add (Notch.CreateNotchSequence (mTs.Count - 1, -1, NotchSectionType.MoveToMidApproach));
            mNotchSequences.Add (Notch.CreateApproachToNotchSequence (reEntry: true));
            mNotchSequences.Add (Notch.CreateNotchSequence (postApproachIndex, postApproachIndex,
               NotchSectionType.GambitPostApproachMachining));
            mNotchSequences.AddRange (CreateNotchReverseSequencesFromApproach ());

         } else {
            // First move forward
            mNotchSequences.Add (Notch.CreateNotchSequence (postApproachIndex, postApproachIndex,
               NotchSectionType.GambitPostApproachMachining));
            // Collect all the sections till the 0th tooling segment in mTs
            mNotchSequences.AddRange (CreateNotchReverseSequencesFromApproach ());
            mNotchSequences.Add (Notch.CreateNotchSequence (0, -1, NotchSectionType.MoveToMidApproach));
            mNotchSequences.Add (Notch.CreateApproachToNotchSequence (reEntry: true));
            mNotchSequences.Add (Notch.CreateNotchSequence (approachIndex, approachIndex,
               NotchSectionType.GambitPreApproachMachining));
            mNotchSequences.AddRange (CreateNotchForwardSequencesFromApproach ());
         }
         return mNotchSequences;
      }

      public void ValidateNotchPositions () {
         for (int ii = 0; ii < mNotchPos.Count; ii++) {
            for (int jj = ii + 1; jj < mNotchPos.Count; jj++) {
               if (mNotchPos[ii].IsPlaneFlexWJTStart () && mNotchPos[jj].IsPlaneFlexWJTEnd () && !NotchWJTLength.EQ (0) && mNotchPos[ii].Position.EQ (mNotchPos[jj].Position))
                  throw new InvalidOperationException (
                      $"Duplicate positions at Planar Wire joint positions found at {ii} and {jj} which are ({mNotchPos[ii].Position.X}, {mNotchPos[ii].Position.Y}, {mNotchPos[ii].Position.Z})");
               else if (mNotchPos[ii].Position.EQ (mNotchPos[jj].Position)) {
                  throw new InvalidOperationException (
                      $"Duplicate positions at {ii} and {jj} which are ({mNotchPos[ii].Position.X}, {mNotchPos[ii].Position.Y}, {mNotchPos[ii].Position.Z})");
               }
            }
            if (mNotchPos[ii].SegPositionType == SegmentedPositionType.Flex1WJTStart ||
               mNotchPos[ii].SegPositionType == SegmentedPositionType.PostFlex1WJTEnd ||
               mNotchPos[ii].SegPositionType == SegmentedPositionType.Flex2WJTStart ||
               mNotchPos[ii].SegPositionType == SegmentedPositionType.PostFlex2WJTEnd) {
               var wjtActualLength = mNotchPos[ii - 1].Position.DistTo (mNotchPos[ii].Position);
               if (wjtActualLength.EQ (0))
                  throw new Exception ($"Actual Length of WJT of Flex1WJTStart, {ii - 1} to {ii} {wjtActualLength} is ZERO");
            }
         }
      }

      void EvalPositionsSanityCheck () {
         var gambitPreApproachData = ParamAtPositionFromPointAtLength (ApproachParameters.Value.NPosition, -NotchWJTLength);
         var gambitPreApproachNPos = new NotchPosition (gambitPreApproachData.Value.position, gambitPreApproachData.Value.segIndex, gambitPreApproachData.Value.param,
            SegmentedPositionType.GambitPreApproach);

         var gambitPostApproachData = ParamAtPositionFromPointAtLength (ApproachParameters.Value.NPosition, NotchWJTLength);
         var gambitPostApproachNPos = new NotchPosition (gambitPostApproachData.Value.position, gambitPostApproachData.Value.segIndex, gambitPostApproachData.Value.param,
            SegmentedPositionType.GambitPostApproach);

         // Checking
         var approachT = ApproachParameters.Value.Param;
         var preAppT = gambitPreApproachData.Value.param;
         var postAppT = gambitPostApproachData.Value.param;
         double distbet1 = DistanceBetween (this, preAppT, approachT);
         double distbet2 = DistanceBetween (this, approachT, postAppT);
         if (!distbet1.EQ (NotchWJTLength, 0.1)) throw new Exception ($"NotchToolPath.EvalPositionsSanityCheck: preApp -> App length not {NotchWJTLength}. It is {distbet1}");
         if (!distbet2.EQ (NotchWJTLength, 0.1)) throw new Exception ($"NotchToolPath.EvalPositionsSanityCheck: preApp -> App length not {NotchWJTLength}. It is {distbet2}");
      }

      public NotchPosition?
         ComputeApproachParameters () {
         NotchPosition? approachNPos = null;

         // Add positions for Approach and Gambits
         // --------------------------------------
         // Add Approach at 0.5 param
         // TODO : Find the approach parameter based on the validity
         (int SegIndex, double Param, double SegParam, Point3 Position)? approachPosData = null;
         approachPosData = ParamAtPositionFromPointAtLength (mTs[0].Curve.Start, 0.5 * PathLength);
         var approachSegIndex = approachPosData.Value.SegIndex;
         var approachPos = approachPosData.Value.Position;
         var approachParamData = ParamAtPosition (approachPos);

         int mm = 0;
         bool approachIndexChanged = false;
         int changedIndex = approachSegIndex;
         while (changedIndex < mTs.Count && changedIndex >= 0 && !mTs[changedIndex].IsValid) {
            if (mm > 0) mm *= -1;
            else mm = Math.Abs (mm) + 1;
            changedIndex = approachSegIndex + mm;
            approachIndexChanged = true;
         }
         if (changedIndex >= mTs.Count && changedIndex < 0)
            throw new Exception ("No tooling segment is valid for approach");
         if (approachIndexChanged) {
            // Keeping the new position of approach at changedIndex-th segment's mid point
            approachPos = Geom.Evaluate (mTs[changedIndex].Curve, 0.5, mTs[changedIndex].Vec0);
            var changedApproachPosData = ParamAtPosition (approachPos);
            approachNPos = new NotchPosition (approachPos, changedApproachPosData.Value.segIndex, changedApproachPosData.Value.param,
               SegmentedPositionType.Approach);
            ApproachParameters = (changedApproachPosData.Value.segIndex, approachPos, changedApproachPosData.Value.param, changedApproachPosData.Value.normal, changedApproachPosData.Value.segParam);
         } else {
            approachNPos = new NotchPosition (approachPos, approachSegIndex, 0.5, SegmentedPositionType.Approach);
            ApproachParameters = (approachPosData.Value.SegIndex, approachPosData.Value.Position, approachPosData.Value.Param, approachParamData.Value.normal, approachPosData.Value.SegParam);
         }
         return approachNPos;
      }

      public void EvalNotchSpecPositions () {
         var flexIndices = Notch.GetFlexSegmentIndices (mTs);

         int flex1StartIdx = -1;
         int flex1EndIdx = -1;
         int flex2StartIdx = -1;
         int flex2EndIdx = -1;
         if (flexIndices.Count >= 1) {
            if (flexIndices[0].Item1 < 0)
               throw new Exception ("Toolpath.EvalNotchSpecPositions: SplitExtremeSegmentsOnFlangeToFlex is buggy. Flex index is 0");
            // Get the indices of the flex positions
            flex1StartIdx = flexIndices[0].Item1;
            flex1EndIdx = flexIndices[0].Item2;

            if (flexIndices.Count == 2) {
               if (flexIndices[0].Item2 >= mTs.Count)
                  throw new Exception ("Toolpath.EvalNotchSpecPositions: SplitExtremeSegmentsOnFlangeToFlex is buggy. Flex index is count");
               flex2StartIdx = flexIndices[1].Item1;
               flex2EndIdx = flexIndices[1].Item2;
            }
         }


         // Add the starting position
         mNotchPos.Add (new NotchPosition (mTs[0].Curve.Start, 0, 0, SegmentedPositionType.Start));
         // Add the ending position
         mNotchPos.Add (new NotchPosition (mTs[^1].Curve.End, mTs.Count - 1, 1.0, SegmentedPositionType.End));

         // Decl
         NotchPosition? preFlex1StPos = null, flex1StPos = null;
         NotchPosition? postFlex1EndPos = null, flex1EndPos = null;

         // Add positions corresponding to flex-1 
         // -------------------------------------
         double wjtLen = 2.0;
         if (flex1StartIdx != -1) {
            // Create WJT before beginning of Flex machining only if the WJ length >= 1.0
            wjtLen = NotchWJTLength.SLT (1.0) ? 2.0 : NotchWJTLength;
            // Find the parameter of the pre-flexWJT position
            var preFlex1WJTParamData = ParamAtPositionFromPointAtLength (mTs[flex1StartIdx].Curve.Start, -wjtLen);
            // Add the pre Flex1 Start position
            preFlex1StPos = new NotchPosition (preFlex1WJTParamData.Value.position, preFlex1WJTParamData.Value.segIndex,
               preFlex1WJTParamData.Value.param, SegmentedPositionType.PreFlex1WJTStart);
            mNotchPos.Add (preFlex1StPos.Value);

            // Add the Flex1 Start position
            var flex1StartParamData = ParamAtPositionFromPointAtLength (mTs[flex1StartIdx].Curve.Start, 0);
            if (flex1StartParamData != null) {
               flex1StPos = new NotchPosition (flex1StartParamData.Value.position, flex1StartParamData.Value.segIndex,
                  flex1StartParamData.Value.param, SegmentedPositionType.Flex1WJTStart);
               mNotchPos.Add (flex1StPos.Value);
            }

            // Add the Flex1 End position
            var flex1EndParamData = ParamAtPositionFromPointAtLength (mTs[flex1EndIdx].Curve.End, 0);
            if (flex1EndParamData != null) {
               flex1EndPos = new NotchPosition (flex1EndParamData.Value.position, flex1EndParamData.Value.segIndex,
                  flex1EndParamData.Value.param, SegmentedPositionType.Flex1WJTEnd);
               mNotchPos.Add (flex1EndPos.Value);
            }

            // Find the parameter of the pre-flexWJT position
            var postFlex1WJTParamData = ParamAtPositionFromPointAtLength (mTs[flex1EndIdx].Curve.End, wjtLen);
            // Add the pre Flex1 Start position
            postFlex1EndPos = new NotchPosition (postFlex1WJTParamData.Value.position, postFlex1WJTParamData.Value.segIndex,
               postFlex1WJTParamData.Value.param, SegmentedPositionType.PostFlex1WJTEnd);
            mNotchPos.Add (postFlex1EndPos.Value);
         }

         NotchPosition? preFlex2StPos = null, flex2StPos = null;
         NotchPosition? postFlex2EndPos = null, flex2EndPos = null;

         // Add positions corresponding to flex-2 
         // -------------------------------------
         if (flex2StartIdx != -1) {
            // Create WJT after end of Flex machining only if the WJ length >= 1.0
            wjtLen = NotchWJTLength.SLT (1.0) ? 2.0 : NotchWJTLength;
            // Find the parameter of the pre-flexWJT position
            var preFlex2WJTParamData = ParamAtPositionFromPointAtLength (mTs[flex2StartIdx].Curve.Start, -wjtLen);
            // Add the pre Flex2 Start position
            preFlex2StPos = new NotchPosition (preFlex2WJTParamData.Value.position, preFlex2WJTParamData.Value.segIndex,
               preFlex2WJTParamData.Value.param, SegmentedPositionType.PreFlex2WJTStart);
            mNotchPos.Add (preFlex2StPos.Value);

            // Add the Flex2 Start position
            var flex2StartParamData = ParamAtPositionFromPointAtLength (mTs[flex2StartIdx].Curve.Start, 0);
            if (flex2StartParamData != null) {
               flex2StPos = new NotchPosition (flex2StartParamData.Value.position, flex2StartParamData.Value.segIndex,
                  flex2StartParamData.Value.param, SegmentedPositionType.Flex2WJTStart);
               mNotchPos.Add (flex2StPos.Value);
            }

            // Add the Flex2 End position
            var flex2EndParamData = ParamAtPositionFromPointAtLength (mTs[flex2EndIdx].Curve.End, 0);
            if (flex2EndParamData != null) {
               flex2EndPos = new NotchPosition (flex2EndParamData.Value.position, flex2EndParamData.Value.segIndex,
                  flex2EndParamData.Value.param, SegmentedPositionType.Flex2WJTEnd);
               mNotchPos.Add (flex2EndPos.Value);
            }

            // Find the parameter of the pre-flexWJT position
            var postFlex2WJTParamData = ParamAtPositionFromPointAtLength (mTs[flex2EndIdx].Curve.End, wjtLen);
            // Add the pre Flex2 Start position
            postFlex2EndPos = new NotchPosition (postFlex2WJTParamData.Value.position, postFlex2WJTParamData.Value.segIndex,
               postFlex2WJTParamData.Value.param, SegmentedPositionType.PostFlex2WJTEnd);
            mNotchPos.Add (postFlex2EndPos.Value);
         }

         mNotchPos.Sort ();
         ValidateNotchPositions ();

         NotchPosition? approachNPos = null, gambitPreApproachNPos = null, gambitPostApproachNPos = null;
         approachNPos = ComputeApproachParameters ();

         mNotchPos.Add (approachNPos.Value);

         // Add GambitPreApproach
         wjtLen = NotchWJTLength.SLT (mLeastWireJointLength) ? 2.0 : NotchWJTLength;

         var gambitPreApproachData = ParamAtPositionFromPointAtLength (ApproachParameters.Value.NPosition, -wjtLen);
         gambitPreApproachNPos = new NotchPosition (gambitPreApproachData.Value.position, gambitPreApproachData.Value.segIndex, gambitPreApproachData.Value.param,
            SegmentedPositionType.GambitPreApproach);
         mNotchPos.Add (gambitPreApproachNPos.Value);

         // Add GambitPostApproach

         var gambitPostApproachData = ParamAtPositionFromPointAtLength (ApproachParameters.Value.NPosition, wjtLen);
         gambitPostApproachNPos = new NotchPosition (gambitPostApproachData.Value.position, gambitPostApproachData.Value.segIndex, gambitPostApproachData.Value.param,
            SegmentedPositionType.GambitPostApproach);
         mNotchPos.Add (gambitPostApproachNPos.Value);


         // Check
         EvalPositionsSanityCheck ();

         // Sort the positions list
         mNotchPos.Sort ();

         // Create Wire joint traces on planar machining only if the NotchWJTLength specified 
         // is >= Least Wire Joint Length ( Restricted settings).
         if (NotchWJTLength.GTEQ (mLeastWireJointLength)) {
            // Adding Planar Wire joint positions
            // ---------------------------
            // Start with the start position as the prev (ref) Position
            NotchPosition? prevPos = mNotchPos[0];

            // Evaluate the start and end position data for the wirejoint
            int wjtCount = 1;
            do {
               // Compute new/current WJT positions
               var wjtStartPosData = ParamAtPositionFromPointAtLength (prevPos.Value.Position, NotchMinThresholdLength);
               var wjtEndPosData = ParamAtPositionFromPointAtLength (prevPos.Value.Position, NotchMinThresholdLength + wjtLen);

               if (wjtStartPosData == null || wjtEndPosData == null)
                  break;

               // Create new/current WJT positions
               var currWJTStartPos = new NotchPosition (wjtStartPosData.Value.position, wjtStartPosData.Value.segIndex, wjtStartPosData.Value.param,
                        SegmentedPositionType.WJTStart (wjtCount));
               var currWJTEndPos = new NotchPosition (wjtEndPosData.Value.position, wjtEndPosData.Value.segIndex, wjtEndPosData.Value.param,
                  SegmentedPositionType.WJTEnd (wjtCount));

               // Check if the positions wjtStartPosData and wjtEndPosData are within flexes
               if (flex1StartIdx != -1) {
                  if (preFlex1StPos == null || postFlex1EndPos == null) throw new Exception ("Pre and Post flex1 position object is null");
               }
               if (flex2StartIdx != -1) {
                  if (preFlex2StPos == null || postFlex2EndPos == null) throw new Exception ("Pre and Post flex2 position object is null");
               }

               // Find the bounds of the wjt start and end position
               var (JustLesserThanWJTSt, JustGreaterThanWJTSt) = FindBoundingNotches (mNotchPos, currWJTStartPos);
               var (JustLesserThanWJTEnd, JustGreaterThanWJTEnd) = FindBoundingNotches (mNotchPos, currWJTEndPos);

               if (JustLesserThanWJTSt == null || JustGreaterThanWJTSt == null)
                  break;

               bool feasibleWJTPos = false;
               if ((JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.Start &&
                  JustLesserThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Start) ||
                  (JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd &&
                  JustLesserThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd) ||
                  (JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.PostFlex2WJTEnd &&
                  JustLesserThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PostFlex2WJTEnd) ||
                  (JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.GambitPostApproach &&
                  JustLesserThanWJTEnd.Value.SegPositionType == SegmentedPositionType.GambitPostApproach) ||
                  (wjtCount > 0 && JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.WJTEnd (wjtCount - 1) &&
                  JustLesserThanWJTEnd.Value.SegPositionType == SegmentedPositionType.WJTEnd (wjtCount - 1)))
                  feasibleWJTPos = true;

               // If the new computed WJT position is infeasible ( if it lies within the Flex Pre to post ), set
               // the prev position to the PostFlex1/2WJTEnd
               if (!feasibleWJTPos) {
                  if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PreFlex1WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex1WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex1WJTEnd ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd);
                  else if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PreFlex2WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex2WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex2WJTEnd ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PostFlex2WJTEnd)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.PostFlex2WJTEnd);
                  else if (JustGreaterThanWJTSt.Value.SegPositionType == SegmentedPositionType.Flex1WJTStart ||
                     JustGreaterThanWJTSt.Value.SegPositionType == SegmentedPositionType.Flex1WJTEnd ||
                     JustGreaterThanWJTSt.Value.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd);
                  else if (JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.PreFlex2WJTStart ||
                     JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.Flex2WJTStart ||
                     JustLesserThanWJTSt.Value.SegPositionType == SegmentedPositionType.Flex2WJTEnd)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.PostFlex2WJTEnd);
                  else if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.GambitPreApproach ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Approach)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.GambitPostApproach);
                  else if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.End &&
                     JustLesserThanWJTEnd.Value.SegPositionType == SegmentedPositionType.GambitPostApproach)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.GambitPostApproach);
                  else if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.GambitPreApproach ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Approach ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.GambitPostApproach)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.GambitPostApproach);
                  continue;
               }

               // The current WJTs can be added only if the WJT positions are not within flexes

               // Valid cases are
               // Pos at start of wjt is just lesser than prevPos ( Start, )
               // Add the wire joints only when the JustGreaterWJTEnd position's distance from
               // nextWJTEndPos position is 0.75 * NotchMinThresholdLength
               var currPosToGTWJTPos = currWJTEndPos.DistTo (JustGreaterThanWJTEnd.Value, PathLength);
               if (currPosToGTWJTPos.GTEQ (0.75 * NotchMinThresholdLength)) {
                  // Add wjt start
                  mNotchPos.Add (new NotchPosition (wjtStartPosData.Value.position, wjtStartPosData.Value.segIndex, wjtStartPosData.Value.param,
                     SegmentedPositionType.WJTStart (wjtCount)));

                  // Add wjt end
                  mNotchPos.Add (new NotchPosition (wjtEndPosData.Value.position, wjtEndPosData.Value.segIndex, wjtEndPosData.Value.param,
                     SegmentedPositionType.WJTEnd (wjtCount)));

                  // Sort the positions list
                  mNotchPos.Sort ();

                  // Set the prev pos again
                  prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.WJTEnd (wjtCount));
               } else {
                  if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PreFlex1WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex1WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex1WJTEnd)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.PostFlex1WJTEnd);
                  else if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.PreFlex2WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex2WJTStart ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Flex2WJTEnd)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.PostFlex2WJTEnd);
                  else if (JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.GambitPreApproach ||
                     JustGreaterThanWJTEnd.Value.SegPositionType == SegmentedPositionType.Approach)
                     prevPos = mNotchPos.FirstOrDefault (n => n.SegPositionType == SegmentedPositionType.GambitPostApproach);
                  else
                     break;
                  continue;
               }

               wjtCount++;
            } while (true);
         }
      }
      #endregion
      #region computations
      public double DistanceBetween (double param1, double param2) {
         if (param1.SLT (0) || param1.SGT (1.0) || param2.SLT (0) || param2.SGT (1.0))
            throw new Exception ("Toolpath.DistanceBetween: param1 and param2 should be between 0 and 1");
         return PathLength * (param2 - param1);
      }
      public (int segIndex, double param, Vector3 normal, double segParam)? ParamAtPosition (Point3 pt, double tolerance = 1e-6) {
         double accumulatedLength = 0;

         for (int ii = 0; ii < mTs.Count; ii++) {
            var segment = mTs[ii];
            var curve = segment.Curve;
            var normal = mTs[ii].Vec0.Normalized ();
            if (Geom.IsPointOnCurve (curve, pt, normal, hintSense: EArcSense.Infer, tolerance, true)) {
               // Check if point is at segment start (within tolerance)
               bool atStart = pt.DistTo (curve.Start).EQ (0);
               bool atEnd = pt.DistTo (curve.End).EQ (0);

               // Case 1: First segment's start point
               if (ii == 0 && atStart) {
                  return (0, 0.0, normal, 0.0);
               }

               // Case 2: Any other segment's start point → return previous segment
               if (ii > 0 && atStart) {
                  double prevSegmentLength = mTs[ii - 1].Length;
                  double np = (accumulatedLength - prevSegmentLength) / PathLength;
                  return (ii - 1, np, mTs[ii - 1].Vec1.Normalized (), 1.0);
               }

               // Case 3: Any segment's end point
               if (atEnd) {
                  double np = (accumulatedLength + segment.Length) / PathLength;
                  return (ii, np, mTs[ii].Vec1.Normalized (), 1.0);
               }

               // Case 4: Point is somewhere in the middle of the segment
               double segLengthUptoPt = Geom.GetLengthAtPoint (curve, pt, segment.Vec0);
               double normalizedParam = (accumulatedLength + segLengthUptoPt) / PathLength;
               var segParam = segLengthUptoPt / curve.Length;
               normal = mTs[ii].Vec0 * (1 - segParam) + mTs[ii].Vec1 * segParam;
               return (ii, normalizedParam, normal, segParam);
            }

            accumulatedLength += segment.Length;
         }

         throw new Exception ("ToolPath.ParamAtPoint: Given point not on the tool path");
      }

      public (int segIndex, Point3 position, Vector3 normalAtPosition, double segParam)? PositionAtParam (double param, double tolerance = 1e-6) {
         if (mTs.Count == 0)
            throw new InvalidOperationException ("ToolPath contains no segments");

         // Handle special case: param = 0
         if (param.EQ (0, tolerance))
            return (0, mTs[0].Curve.Start, mTs[0].Vec0.Normalized (), 0.0);

         // Handle case where param ≈ 1.0 (end of last segment)
         if (param.EQ (1.0, tolerance)) {
            int lastIdx = mTs.Count - 1;
            return (lastIdx, mTs[lastIdx].Curve.End, mTs[0].Vec1.Normalized (), 1.0);
         }

         double targetLength = param * PathLength;
         double accumulatedLength = 0;

         for (int ii = 0; ii < mTs.Count; ii++) {
            var segment = mTs[ii];
            if (segment.Curve is Arc3) {
               int aa = 0;
               ++aa;
            }
            double segmentLength = segment.Length;

            // Check if target is within this segment
            if (targetLength.LTEQ (accumulatedLength + segmentLength, tolerance)) {
               double remainingLength = targetLength - accumulatedLength;

               // Case 1: At start of segment (only possible for ii > 0)
               if (ii > 0 && remainingLength.EQ (0, tolerance))
                  return (ii - 1, mTs[ii].Curve.Start, mTs[ii].Vec0.Normalized (), 0.0);


               // Case 2: At end of segment
               if (remainingLength.EQ (segmentLength, tolerance))
                  return (ii, mTs[ii].Curve.End, mTs[ii].Vec1.Normalized (), 1.0);


               // Case 3: Strict length validation
               if (segment.Length.SLT (remainingLength))
                  throw new Exception ($"ToolPath.PositionAtParam: remaining length {remainingLength} out of segment bounds including tolerance [0,{segmentLength}]");

               // Case 4: Somewhere in the middle of segment
               Point3 position = Geom.GetPointAtLengthFromStart (segment.Curve, segment.Vec0, remainingLength);

               // Interpolate normal
               var t = remainingLength / segment.Length;
               var normal = mTs[ii].Vec0.Normalized () * (1 - t) + mTs[ii].Vec1.Normalized () * t;
               return (ii, position, normal, t);
            }

            accumulatedLength += segmentLength;
         }

         throw new ArgumentOutOfRangeException (nameof (param), "Parameter value exceeds path length");
      }

      public static (int segIndex, Point3 position, Vector3 normalAtPosition, double segParam)? PositionAtParam
         (NotchToolPath npt, double param, double tolerance = 1e-6) {
         var segs = npt.Segs.ToList ();
         if (segs.Count == 0)
            throw new InvalidOperationException ("ToolPath contains no segments");

         // Handle special case: param = 0
         if (param.EQ (0, tolerance))
            return (0, segs[0].Curve.Start, segs[0].Vec0.Normalized (), 0.0);

         // Handle case where param ≈ 1.0 (end of last segment)
         if (param.EQ (1.0, tolerance)) {
            int lastIdx = segs.Count - 1;
            return (lastIdx, segs[lastIdx].Curve.End, segs[0].Vec1.Normalized (), 1.0);
         }

         double targetLength = param * npt.PathLength;
         double accumulatedLength = 0;

         for (int ii = 0; ii < segs.Count; ii++) {
            var segment = segs[ii];
            double segmentLength = segment.Length;

            // Check if target is within this segment
            if (targetLength.LTEQ (accumulatedLength + segmentLength, tolerance)) {
               double remainingLength = targetLength - accumulatedLength;

               // Case 1: At start of segment (only possible for ii > 0)
               if (ii > 0 && remainingLength.EQ (0, tolerance))
                  return (ii - 1, segs[ii].Curve.Start, segs[ii].Vec0.Normalized (), 0.0);


               // Case 2: At end of segment
               if (remainingLength.EQ (segmentLength, tolerance))
                  return (ii, segs[ii].Curve.End, segs[ii].Vec1.Normalized (), 1.0);


               // Case 3: Strict length validation
               if (segment.Length.SLT (remainingLength))
                  throw new Exception ($"ToolPath.PositionAtParam: remaining length {remainingLength} out of segment bounds including tolerance [0,{segmentLength}]");

               // Case 4: Somewhere in the middle of segment
               Point3 position = Geom.GetPointAtLengthFromStart (segment.Curve, segment.Vec0, remainingLength);

               // Interpolate normal
               var t = remainingLength / segment.Length;
               var normal = segs[ii].Vec0.Normalized () * (1 - t) + segs[ii].Vec1.Normalized () * t;
               return (ii, position, normal, t);
            }

            accumulatedLength += segmentLength;
         }

         throw new ArgumentOutOfRangeException (nameof (param), "Parameter value exceeds path length");
      }

      public (int segIndex, Point3 position, Vector3 normalAtPosition, double segParam)? PositionFromPointAtLength (
          Point3 fromPt,
          double lengthFromFromPt,
          double tolerance = 1e-6) {

         // Check if the from point is on any of the tooling segment
         var fromPtOnAnySeg = mTs.Any (x => Geom.IsPointOnCurve (x.Curve, fromPt, x.Vec0.Normalized (), hintSense: EArcSense.Infer, tolerance));
         if (!fromPtOnAnySeg)
            throw new ArgumentException ("Starting point not found on tool path", nameof (fromPt));

         // 1. First check if fromPt matches any segment's end point and lengthFromFromPt is 0
         var endSegmentIndex = mTs.FindIndex (ts => ts.Curve.End.DistTo (fromPt).EQ (0, tolerance));
         if (endSegmentIndex != -1 && lengthFromFromPt.EQ (0, tolerance)) {
            if (mTs[endSegmentIndex].Curve is Arc3) {
               int aa = 0;
               ++aa;
            }
            // Return the end point of the matching segment
            return (endSegmentIndex, mTs[endSegmentIndex].Curve.End, mTs[endSegmentIndex].Vec1.Normalized (), 1.0);
         }

         // 2. Find the starting position on the path
         var startPos = ParamAtPosition (fromPt, tolerance);
         if (!startPos.HasValue)
            throw new ArgumentException ("Starting point not found on tool path", nameof (fromPt));

         int startSegIndex = startPos.Value.segIndex;
         double startSegParam = startPos.Value.segParam;
         Vector3 normalAtParamPos = startPos.Value.normal;
         double accumulatedLength = 0;

         // 3. Calculate the exact starting length along the path
         for (int ii = 0; ii < startSegIndex; ii++)
            accumulatedLength += mTs[ii].Length;

         var startSegment = mTs[startSegIndex];
         if (mTs[startSegIndex].Curve is Arc3) {
            int aa = 0;
            ++aa;
         }
         double lengthInStartSegment = startSegParam * startSegment.Length;
         accumulatedLength += lengthInStartSegment;

         // 4. Calculate target absolute length along path
         double targetLength = accumulatedLength + lengthFromFromPt;
         if (targetLength.SLT (0))
            throw new Exception ("ToolPath.PositionFromPointAtLength: Target length is < 0");

         // 5. Handle special cases
         // If the length given is zero, the from point is to be returned
         if (lengthFromFromPt.EQ (0, tolerance))
            return (startSegIndex, fromPt, normalAtParamPos, startSegParam);

         // If the target lenfth is 0, it means, it is at the global start
         // The start segment's start point of thr tooling segments has to be returned
         if (targetLength.EQ (0, tolerance))
            return (0, mTs[0].Curve.Start, mTs[0].Vec0.Normalized (), 0.0);

         // Modified boundary check - now throws exception if beyond path length
         if (targetLength.SGT (PathLength, tolerance)) return null;
         //throw new ArgumentOutOfRangeException (nameof (lengthFromFromPt),
         //    $"Length {lengthFromFromPt} extends beyond tool path length {PathLength}");

         // If the total length equals path length, the end point
         // of the curve of the last segment is to be returned
         if (targetLength.EQ (PathLength, tolerance)) {
            int lastIdx = mTs.Count - 1;
            if (mTs[lastIdx].Curve is Arc3) {
               int aa = 0;
               ++aa;
            }
            return (lastIdx, mTs[lastIdx].Curve.End, mTs[lastIdx].Vec1.Normalized (), 1.0);
         }

         // 6. Find the target position using existing method
         var targetPos = PositionAtParam (targetLength / PathLength, tolerance);
         if (!targetPos.HasValue)
            throw new InvalidOperationException ($"Failed to find position at length {lengthFromFromPt} from starting point");

         return targetPos.Value;
      }

      public (int segIndex, double param, double segParam, Point3 position)? ParamAtPositionFromPointAtLength (Point3 fromPt,
          double lengthFromFromPt,
          double tolerance = 1e-6) {
         var res = PositionFromPointAtLength (fromPt, lengthFromFromPt, tolerance);
         if (!res.HasValue) return null;
         var res2 = ParamAtPosition (res.Value.position, tolerance);
         if (!res2.HasValue) return null;
         return (res2.Value.segIndex, res2.Value.param, res2.Value.segParam, res.Value.position);
      }
      #endregion
      #region Static utility methods
      public static bool IsPositionWithinFlex (Point3 pt, List<ToolingSegment> segs) {
         var flexIndices = Notch.GetFlexSegmentIndices (segs);
         for (int ii = 0; ii < segs.Count; ii++) {
            if (!Geom.IsPointOnCurve (segs[ii].Curve, pt, segs[ii].Vec0)) continue;
            if (flexIndices.Count == 1) {
               if (flexIndices[0].Item1 <= ii && ii <= flexIndices[0].Item2) return true;
            } else if (flexIndices.Count == 2) {
               if (flexIndices[1].Item1 <= ii && ii <= flexIndices[1].Item2) return true;
            }
         }
         return false;
      }

      public static double DistanceBetween (NotchToolPath npt, double t1, double t2, double tolerance = 1e-6) {
         if (t1.SGT (1.0) || t1.SLT (0) || t2.SGT (1.0) || t2.SLT (0))
            throw new Exception ("NotchToolPath.DistanceBetween: Parameter is out of bounds [0,1]");
         var targetPosT1 = PositionAtParam (npt, t1, tolerance);
         var targetPosT2 = PositionAtParam (npt, t2, tolerance);
         var p1 = targetPosT1.Value.position;
         var p2 = targetPosT2.Value.position;
         var distBet = DistanceBetween (npt, p1, p2, tolerance);
         return distBet;
      }

      public static double DistanceBetween (NotchToolPath npt, Point3 fromPt, Point3 toPt, double tolerance = 1e-6) {
         // 1. Find both points on the path
         var fromPos = FindPointOnSegments (npt.Segs.ToList (), fromPt, tolerance);
         var toPos = FindPointOnSegments (npt.Segs.ToList (), toPt, tolerance);

         if (!fromPos.HasValue || !toPos.HasValue)
            throw new ArgumentException ("One or both points not found on tool path");

         // 2. Calculate accumulated lengths
         double fromLength = CalculateAccumulatedLength (npt.Segs.ToList (), fromPos.Value.segIndex, fromPos.Value.segParam);
         double toLength = CalculateAccumulatedLength (npt.Segs.ToList (), toPos.Value.segIndex, toPos.Value.segParam);

         // 3. Return signed distance
         return toLength - fromLength;
      }

      public static (int segIndex, double segParam)? FindPointOnSegments (List<ToolingSegment> segs, Point3 pt, double tolerance) {
         for (int i = 0; i < segs.Count; i++) {
            var seg = segs[i];
            if (Geom.IsPointOnCurve (seg.Curve, pt, seg.Vec0.Normalized (), hintSense: EArcSense.Infer, tolerance, true)) {
               double segParam;
               if (pt.DistTo (seg.Curve.Start).LTEQ (tolerance))
                  segParam = 0.0;
               else if (pt.DistTo (seg.Curve.End).LTEQ (tolerance))
                  segParam = 1.0;
               else
                  segParam = Geom.GetLengthAtPoint (seg.Curve, pt, seg.Vec0) / seg.Length;

               return (i, segParam);
            }
         }
         return null;
      }

      public static double CalculateAccumulatedLength (List<ToolingSegment> segs, int segIndex, double segParam) {
         double length = 0;

         // Sum lengths of all segments before the current one
         for (int i = 0; i < segIndex; i++)
            length += segs[i].Length;

         // Add partial length within current segment
         length += segs[segIndex].Length * segParam;

         return length;
      }
      public static (NotchPosition? JustLesser, NotchPosition? JustGreater)
   FindBoundingNotches (List<NotchPosition> notches, NotchPosition myNotchPos) {
         if (notches == null || notches.Count == 0)
            return (null, null);

         // Ensure list is sorted
         notches.Sort ();

         int index = notches.BinarySearch (myNotchPos);

         if (index < 0) {
            index = ~index; // Get insertion point
         }

         NotchPosition? justLesser = null;

         // Get largest notch ≤ myNotchPos
         if (index > 0) {
            justLesser = notches[index - 1];
         } else if (index == 0 && myNotchPos.CompareTo (notches[0]) >= 0) {
            // Special case: position equals first notch
            justLesser = notches[0];
         }

         NotchPosition? justGreater;
         // Get smallest notch > myNotchPos
         if (index < notches.Count) {
            justGreater = notches[index];
         } else {
            // Special case: position is after last notch but before path end
            // Return last notch if position is before end (param < 1.0)
            justGreater = notches[^1];
         }

         return (justLesser, justGreater);
      }
      #endregion
   }
}
