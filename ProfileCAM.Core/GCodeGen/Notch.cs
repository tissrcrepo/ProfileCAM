using System.Collections.Generic;
using ProfileCAM.Core.Geometries;
using Flux.API;
using static ProfileCAM.Core.Utils;

namespace ProfileCAM.Core.GCodeGen;
#region Data structures and Enums used Notch Computation
/// <summary>
/// The NotchPointInfo structure holds a list of notch specific points list
/// against the index of the occuring in the List of Tooling Segments and
/// the percentage of the length (by prescription).
/// </summary>
public struct NotchPointInfo (int sgIndx, Point3 pt, double percent, string position) {
   public string mPosition = position;
   public int mSegIndex = sgIndx;
   public List<Point3> mPoints = [pt];
   public double mPercentage = percent;

   // Method for deep copy
   public NotchPointInfo DeepCopy () {
      // Create a new instance with the same values
      var copy = new NotchPointInfo {
         mSegIndex = this.mSegIndex,
         mPercentage = this.mPercentage,
         mPoints = [.. this.mPoints] // Deep copy the list
      };
      return copy;
   }
}


/// <summary>
/// The following enums signify the various cutting or rapid positioning strokes used during notch cutting.
/// <list type="number">
/// <item>
///     <description>ApproachMachining: This approach is mandatory to start the non-edge notch tooling. 
///     The tool describes a set of cutting strokes as follows:</description>
///     <list type="bullet">
///         <item>Approach to the midpoint of the line segment from the notch point at 50% of the length to the 
///         part boundary (in the scrap side direction).</item>
///         <item>Cutting stroke from the above midpoint to the part boundary in the scrap side direction.</item>
///         <item>Rapid positioning to the midpoint.</item>
///         <item>Cutting stroke from the midpoint to the 50% point on the tooling segment.</item>
///     </list>
/// </item>
/// <item>
///     <description>ApproachOnReEntry: This approach involves moving from one end of the non-edge notch tooling
///     to the midpoint of the ApproachMachining midpoint and making a cutting stroke from that midpoint
///     to the 50% notch point on the tooling.</description>
/// </item>
/// <item>
///     <description>GambitPreApproachMachining: This operation machines a distance of wire joint length 
///     from the 50% notch point in the reverse direction of the notch tooling.</description>
/// </item>
/// <item>
///     <description>GambitPostApproachMachining: This operation machines a distance of wire joint length 
///     from the 50% notch point in the forward direction of the notch tooling.</description>
/// </item>
/// <item>
///     <description>MachineToolingForward: This operation machines the notch tooling profile that occurs
///     on the Web, Bottom, or Top Flanges in the forward direction of the notch tooling.</description>
/// </item>
/// <item>
///     <description>MachineToolingReverse: This operation machines the notch tooling profile that occurs
///     on the Web, Bottom, or Top Flanges in the reverse direction of the notch tooling.</description>
/// </item>
/// <item>
///     <description>MachineFlexToolingReverse: This operation machines the notch tooling profile that occurs
///     on the Flex section in the reverse direction of the notch tooling.</description>
/// </item>
/// <item>
///     <description>MachineFlexToolingForward: This operation machines the notch tooling profile that occurs
///     on the Flex section in the forward direction of the notch tooling.</description>
/// </item>
/// </list>
/// </summary>
public enum NotchSectionType {
   /// <summary>
   /// This is mandatory to start the non-edge notch tooling. 
   /// The tool describes a set of the following cutting strokes
   /// -> Approach to the mid point of the line segment from notch point at 50% of the length to the 
   /// part boundary (in scrap side direction)
   /// -> Cutting stroke from the above mid point to the part boundary in the scrap side direction
   /// -> Rapid position to the mid point
   /// -> Cutting stroke from mid point to the 50% point on the tooling segment
   /// </summary>
   ApproachMachining,

   /// <summary>
   /// This is the approach from one end of the non-edge notch tooling
   /// to the mid point of the ApproachMachining mid point and a cutting stroke from that mid point
   /// to the 50% notch point on the tooling
   /// </summary>
   ApproachOnReEntry,

   /// <summary>
   /// This is to machine a distance of wire joint distance 
   /// from 50% notch point in the reverse order of the notch tooling
   /// </summary>
   GambitPreApproachMachining,

   /// <summary>
   /// This is to machine a distance of wire joint distance 
   /// from 50% notch point in the forward order of the notch tooling
   /// </summary>
   GambitPostApproachMachining,

   /// <summary>
   /// This is to machine the notch tooling profile that occurs
   /// on Web or Bottom or Top Flanges in the forward direction of the notch tooling
   /// </summary>
   MachineToolingForward,

   /// <summary>
   /// This is to machine the notch tooling profile that occurs
   /// on Web or Bottom or Top Flanges in the reverse direction of the notch tooling
   /// </summary>
   MachineToolingReverse,

   /// <summary>
   /// This is to machine the notch tooling profile that occurs
   /// on the Flex section in the reverse direction of the notch tooling
   /// </summary>
   MachineFlexToolingReverse,

   /// <summary>
   /// This is to machine the notch tooling profile that occurs
   /// on the Flex section in the forward direction of the notch tooling
   /// </summary>
   MachineFlexToolingForward,

   /// <summary>
   /// This is to introduce a partly joined arrangement with a distance of 
   /// wire joint distance specified in the settings in the reverse direction 
   /// of the notch tooling
   /// </summary>
   WireJointTraceJumpReverse,
   WireJointTraceJumpReverseOnFlex,

   /// <summary>
   /// This is to introduce a partly joined arrangement with a distance of 
   /// wire joint distance specified in the settings in the forward direction 
   /// of the notch tooling
   /// </summary>
   WireJointTraceJumpForward,
   WireJointTraceJumpForwardOnFlex,

   /// <summary>
   /// This is a directive to move to the mid point of the intitial segment defined 
   /// in "ApproachMachining", to reposition the tooling head
   /// </summary>
   MoveToMidApproach,
   Start,
   End,
   None
}

/// <summary>
/// The following structure holds notch specific ( prescribed by process team) points
/// along the notch. The integers are the indices of the list of tooling segments, whose 
/// end points are the notch specific points at which either the entry to the cutting 
/// profile happens or a wire joint is introduced, or an initial gambit action to move
/// in the forward or reverse direction of wire joint distance happens.
/// </summary>
public struct NotchSegmentIndices {
   public NotchSegmentIndices () { }
   public int segIndexAt25pc = -1, segIndexAt50pc = -1, segIndexAt75pc = -1,
      segIndexAtWJTPost25pc = -1, segIndexAtWJTPost50pc = -1, /*segIndexAtWJTPre50pc = -1,*/
      segIndexAtWJTPost75pc = -1, segIndexAtWJTPreApproach = -1, segIndexAtWJTApproach = -1, segIndexAtWJTPostApproach = -1;
   public List<Tuple<int, int, int, int>> flexIndices = [];
}

/// <summary>
/// The entire notch tooling is subdivided into multiple tooling blocks.
/// The following structure holds the sub section of the notch tooling
/// </summary>
public struct NotchSequenceSection {
   public NotchSequenceSection () { }
   public NotchSequenceSection (int stIndex, int endIndex, NotchSectionType type) {
      StartIndex = stIndex; EndIndex = endIndex; SectionType = type;
   }
   public int StartIndex { get; set; } = -1;
   public int EndIndex { get; set; } = -1;
   public NotchSectionType SectionType { get; set; }
   public EFlange Flange { get; set; }
}
#endregion

/// <summary>
/// The class Notch holds all the sequences of actions from creating notch specific points
/// through writing the notch. The sequence is modularized as much as it is needed. Once the 
/// the final prescription is made from the process team, more optimizations will be done
/// </summary>
public class Notch : ToolingFeature {
   #region Enums
   enum IndexType {
      Max,
      Zero,
      PreApproach,
      PostApproach,
      Approach,
      At75,
      Post75,
      Post50,
      At50,
      Post25,
      At25,
      Flex2End,
      Flex2Start,
      Flex2AfterEnd,
      Flex2BeforeStart,
      Flex1AfterEnd,
      Flex1End,
      Flex1Start,
      Flex1BeforeStart,
      None
   }
   #endregion

   #region Constructor(s)
   public Notch (
     Tooling toolingItem,
     Bound3 bound,
     Bound3 fullPartBound,
     GCodeGenerator gcodeGen,
     Tooling prevToolingItem,
     ToolingSegment? prevToolingSegment,
     List<ToolingSegment> prevToolingSegs,
     bool firstTooling,
     EPlane prevPlaneType,
     double xStart,
     double xPartition,
     double xEnd,
     double notchWireJointDistance,
     double notchApproachLength,
     double minNotchThresholdLength,
     double[] percentlength,
     double totalPrevCutToolingsLength,
     double totalToolingsCutLength,
     bool isWireJointsNeeded,
     double leastWJLength,
     double curveLeastLength = 0.5) {
      if (!toolingItem.IsNotch ())
         throw new Exception ("Cannot create a notch from a non-notch feature");

      mToolingItem = toolingItem;
      mBound = bound;
      mFullPartBound = fullPartBound;
      mNotchApproachLength = notchApproachLength;
      mNotchWireJointDistance = notchWireJointDistance;
      mCurveLeastLength = curveLeastLength;
      mPercentLength = percentlength;
      mGCodeGen = gcodeGen;
      mPrevPlane = prevPlaneType;

      mSegments = [.. mToolingItem.Segs];
      mTotalToolingsCutLength = totalToolingsCutLength;
      mCutLengthTillPrevTooling = totalPrevCutToolingsLength;

      mXStart = xStart;
      mXPartition = xPartition;
      mXEnd = xEnd;

      MinNotchLengthThreshold = minNotchThresholdLength;
      EdgeNotch = false;

      PreviousTooling = prevToolingItem;
      PreviousToolingSegment = prevToolingSegment;
      mFirstTooling = firstTooling;
      mPrevToolingSegments = prevToolingSegs;

      mLeastWJLength = leastWJLength;

      // Check if the notch starts and ends on the same flange while it is
      // dual flange notch
      mTwoFlangeNotchStartAndEndOnSameSideFlange = Utils.IsDualFlangeSameSideNotch (mToolingItem, mSegments);

      // Split tooling segments if the notch ends with a single segment after flex
      SplitExtremeSegmentsOnFlangeToFlex (ref mSegments);

      if (mToolingItem.FeatType.Contains ("Split"))
         mSplit = true;

      mToolingPerimeter = mSegments.Sum (segment => segment.Curve.Length);

      TotalToolingLength = Notch.GetTotalNotchToolingLength (
          mBound,
          toolingItem,
          mPercentLength,
          notchWireJointDistance,
          notchApproachLength,
          mCurveLeastLength,
          !notchWireJointDistance.EQ (0),
          mGCodeGen.JobInnerRadius,
       mGCodeGen.JobThickness,
       gcodeGen.PartConfigType
      );

      if (!mTwoFlangeNotchStartAndEndOnSameSideFlange &&
         toolingItem.EdgeNotch)
         EdgeNotch = true;
      else {
         mShortPerimeterNotch = false;
         if (mShortPerimeterNotch || !isWireJointsNeeded)
            mIsWireJointsNeeded = false;

         Utils.FixSanityOfToolingSegments (ref mSegments);
         Utils.MarkfeasibleSegments (ref mSegments);
         ComputeNotchParameters ();
      }
   }

   #endregion

   #region Base Class Overriders
   public override List<ToolingSegment> ToolingSegments { get => mSegments; set => mSegments = value; }
   public override ToolingSegment? GetMostRecentPreviousToolingSegment () => Exit;
   #endregion

   #region Caching tool position
   ToolingSegment? mExitTooling;
   Point3 mRecentToolPosition;
   double mXStart, mXPartition, mXEnd;
   public ToolingSegment? Exit { get => mExitTooling; set => mExitTooling = value; }
   #endregion

   #region External references
   GCodeGenerator mGCodeGen;
   static Bound3 mBound;
   static Bound3 mFullPartBound;
   Tooling mToolingItem;
   EPlane mPrevPlane = EPlane.None;
   bool mFirstTooling = false;
   List<ToolingSegment> mPrevToolingSegments;
   double mLeastWJLength;
   public EPlane PrevPlane { get => mPrevPlane; set => mPrevPlane = value; }
   public Tooling PreviousTooling { get; set; }
   public ToolingSegment? PreviousToolingSegment { get; set; }
   #endregion

   #region Tunable Parameters / Setting Prescriptions
   double mCurveLeastLength;
   // As desired by the machine team
   double[] mPercentLength = [0.25, 0.5, 0.75];
   double mNotchWireJointDistance = 2.0;
   public double NotchWireJointDistance { get => mNotchWireJointDistance; set => mNotchWireJointDistance = value; }
   double mNotchApproachLength = 5.0;
   public double NotchApproachLength { get => mNotchApproachLength; set => mNotchApproachLength = value; }
   bool mSplit = false;
   bool relCoords = true;
   #endregion

   #region Data members
   NotchSegmentIndices mNotchIndices;
   List<NotchAttribute> mNotchAttrs = [];
   List<NotchSequenceSection> mNotchSequences = [];
   Point3?[] mWireJointPts = [null, null, null, null];
   List<Point3> mFlexWireJointPts = [];
   double mBlockCutLength = 0;
   double mTotalToolingsCutLength = 0;
   double mCutLengthTillPrevTooling = 0;
   bool mIsWireJointsNeeded = true;
   bool mTwoFlangeNotchStartAndEndOnSameSideFlange = false;
   ToolingSegment? mFlexStartRef = null;

   // Find the flex segment indices
   List<Tuple<int, int>> mFlexIndices = [];

   // The indices of segs on whose segment the 25%, 50% and 75% of the length occurs
   int?[] mSegIndices = [null, null, null]; int mSegsCount = 0;

   // The point on the segment which shall participate in notch tooling
   Point3?[] mNotchPoints = new Point3?[3];
   List<NotchPointInfo> mNotchPointsInfo = [];
   int mApproachIndex = 1;
   double minThresholdSegLen = 15.0;
   bool mShortPerimeterNotch = false;
   List<int> mInvalidIndices = [];
   double mToolingPerimeter = 0;
   NotchToolPath mNToolPath;
   #endregion

   #region Public Properties
   public List<NotchAttribute> NotchAttributes { get => mNotchAttrs; }
   List<ToolingSegment> mSegments = [];
   public bool EdgeNotch { get; set; }
   public double MinNotchLengthThreshold { get; set; }
   public double TotalToolingLength { get; set; }
   #endregion

   #region Notch parameters computing methods
   /// <summary>
   /// This method recomputes the 25%, 50%, and 75% notch points if the previous 
   /// computation finds the locations existing within the flexes. A heuristic is used, where
   /// 25% and 75% notch points are recomputed only if the distance from the notch point to the start (for the 25%-th
   /// notch point) or to the end (for the 75%-th notch point) is more than 200 units (mm).
   /// If the length is less than 200 units, the corresponding notch point is excluded by setting its index to -1.
   /// </summary>
   /// <param name="segs">The list of tooling segments.</param>
   public static void SplitExtremeSegmentsOnFlangeToFlex (ref List<ToolingSegment> segs) {
      int idx = 0;
      int count = 1;
      List<FCCurve3> fcCrvs;
      while (count < 2) {
         if ((Utils.IsToolingOnFlex (segs[idx]) || (segs.Count > 1 && Utils.IsToolingOnFlex (segs[1]))) && segs[idx].Curve.Length.SGT (10.0)) {
            var firstSegEndPt = Geom.GetPointAtLengthFromStart (segs[idx].Curve, segs[idx].Vec0, segs[idx].Curve.Length - 2.0);
            List<Point3> intPts = [firstSegEndPt];
            fcCrvs = Geom.SplitCurve (segs[idx].Curve, intPts, segs[idx].Vec0);
            if (fcCrvs.Count == 2) {
               var ts1 = Geom.CreateToolingSegmentForCurve (segs[idx], fcCrvs[0]);
               var ts2 = Geom.CreateToolingSegmentForCurve (segs[idx], fcCrvs[1]);
               List<ToolingSegment> tss = [ts1, ts2];
               segs.RemoveAt (idx);
               segs.InsertRange (idx, tss);
            }
         }
         if ((Utils.IsToolingOnFlex (segs[idx]) || (segs.Count >= 2 && Utils.IsToolingOnFlex (segs[^2]))) && segs[idx].Curve.Length.SGT (10.0)) {
            var lastSegStartPoint = Geom.GetPointAtLengthFromStart (segs[idx].Curve, segs[idx].Vec0, 2.0);
            List<Point3> intPts = [lastSegStartPoint];
            fcCrvs = Geom.SplitCurve (segs[idx].Curve, intPts, segs[idx].Vec0);
            if (fcCrvs.Count == 2) {
               var ts1 = Geom.CreateToolingSegmentForCurve (segs[idx], fcCrvs[0]);
               var ts2 = Geom.CreateToolingSegmentForCurve (segs[idx], fcCrvs[1]);
               List<ToolingSegment> tss = [ts1, ts2];
               segs.RemoveAt (idx);
               segs.InsertRange (idx, tss);
            }
         }
         count++;
         idx = segs.Count - 1;
      }
   }

   /// <summary>
   /// This method finds if one or many of the ordinate notch points at
   /// 25%, 50% and 75% of the tooling length lies within the flex segments
   /// and if so, removes that notch point
   /// </summary>
   /// <param name="segs">The tooling segments</param>
   /// <param name="notchPtCountIndex">0,1, or 2, for 25%, 50% or 75%</param>
   /// <param name="notchPt">The notch point</param>
   /// <param name="thresholdNotchLenForNotchApproach">This is the minimum length
   /// that is kept to remove a notch point, if the length of this notch point to 
   /// nearest flex start is lesser than this value from outside</param>
   /// <param name="segIndices">The index in the segments list, where this notch point
   /// falls</param>
   /// <param name="notchPoints">The array of notch points to modify if the 50% point
   /// lies on the flex. A point at 40% is then calculated and added as if it were at 50%
   /// </param>
   void RecomputeNotchPointsWithinFlex (List<ToolingSegment> segs, int notchPtCountIndex, Point3 notchPt,
      double thresholdNotchLenForNotchApproach, ref int?[] segIndices, ref Point3?[] notchPoints) {
      double? lenToToolingEnd = null;

      // Notch point at 75% of the tooling length is within a flex section
      if (notchPtCountIndex == 2)
         lenToToolingEnd = Geom.GetLengthFromEndToolingToPosition (segs, notchPt);
      else if (notchPtCountIndex == 0) // Notch point at 25% of the tooling length is within a flex section
         lenToToolingEnd = Geom.GetLengthFromStartToolingToPosition (segs, notchPt);
      if (lenToToolingEnd != null) {
         if (lenToToolingEnd.Value > thresholdNotchLenForNotchApproach) {
            // Add new notch point at approx mid
            double percent;
            if (notchPtCountIndex == 2) percent = mPercentLength[2] = 0.75;
            else percent = mPercentLength[0] = 0.25;
            var (sgIdx, npt) = Utils.GetNotchPointsOccuranceParams (segs, percent, mCurveLeastLength);
            segIndices[notchPtCountIndex] = sgIdx; notchPoints[notchPtCountIndex] = npt;
         } else
            // Mark this notch as false or delete
            segIndices[notchPtCountIndex] = null; notchPoints[notchPtCountIndex] = null;
      } else {
         // Handle 50% pc case here
         var (sgIdx, npt) = Utils.GetNotchPointsOccuranceParams (segs, 0.4, mCurveLeastLength);
         mPercentLength[1] = 0.4;
         segIndices[notchPtCountIndex] = sgIdx; notchPoints[notchPtCountIndex] = npt;
      }
   }

   /// <summary>
   /// This method recomputes the notch points for length ratios of 25% and 75% if
   /// these points exist within the flex section of the tooling. The following actions will be taken:
   /// <list type="number">
   /// <item>
   ///     <description>If the notch points are within "minThresholdLenFromNPToFlexPt" units 
   ///     from the nearest flex and outside the flex, these notch points are removed.</description>
   /// </item>
   /// <item>
   ///     <description>If the notch points occur outside the flex section and if the distance from
   ///     the extreme section (start for 25% and end for 75%) is more than "thresholdNotchLenForNotchApproach",
   ///     the 25%-th notch point is recomputed at 0.125-th length and the 75%-th notch point is recomputed
   ///     at 0.875-th length of the notch tooling.</description>
   /// </item>
   /// <item>
   ///     <description>If the notch point at 50%-th length lies within the flex, another notch point
   ///     is computed at 40% of the total tooling length from the start.</description>
   /// </item>
   /// </list>
   /// </summary>
   /// <param name="segs">The input tooling segments list, which should be further segmented.</param>
   /// <param name="flexIndices">The list of tuples containing the start and end indices of the flex.</param>
   /// <param name="notchPoints">The existing notch points at 25%, 50%, and 75% of the total tooling length.</param>
   /// <param name="segIndices">The array of indices of the list of tooling segments where the 
   /// notch points occur.</param>
   /// <param name="mPercentLength">The input specification of the percentage length (25%, 50%, 75%).</param>
   /// <param name="minThresholdLenFromNPToFlexPt">The minimum length of the tooling segment below which
   /// a notch point is considered invalid and removed if it occurs.</param>
   /// <param name="thresholdNotchLenForNotchApproach">The threshold length of the tooling segments that
   /// allows for recomputing notch points at 25% and 75%. If the length from this notch point to the nearest
   /// end is less than this threshold, it is not required to create a new notch point as removing the scrap
   /// part is manageable.</param>
   void RecomputeNotchPointsAgainstFlexNotch (List<ToolingSegment> segs, List<Tuple<int, int>> flexIndices,
      ref Point3?[] notchPoints, ref int?[] segIndices, double[] mPercentLength, double minThresholdLenFromNPToFlexPt,
      double thresholdNotchLenForNotchApproach) {
      int index = 0;
      while (index < mPercentLength.Length) {
         if (notchPoints[index] == null) { index++; continue; }
         var (IsWithinAnyFlex, StartIndex, EndIndex) = IsPointWithinFlex (flexIndices, segs, notchPoints[index].Value,
            segIndices[index].Value, minThresholdLenFromNPToFlexPt);

         if (IsWithinAnyFlex)
            RecomputeNotchPointsWithinFlex (segs, index, notchPoints[index].Value, thresholdNotchLenForNotchApproach, ref segIndices, ref notchPoints);
         else if (segIndices[index] != -1) {
            if (StartIndex != -1) {
               var fromNPTToFlexStart = Geom.GetLengthBetween (segs, notchPoints[index].Value, segs[StartIndex].Curve.Start);
               if (fromNPTToFlexStart < 10.0) {
                  var (newNPTAtIndex, idx) = Geom.EvaluatePointAndIndexAtLength (segs, segIndices[index].Value, 11.0/*length offset for approach pt*/,
                        reverseTrace: true);
                  segIndices[index] = idx; notchPoints[index] = newNPTAtIndex;
               }
            }
            if (EndIndex != -1) {
               var fromNPTToFlexEnd = Geom.GetLengthBetween (segs, notchPoints[index].Value, segs[EndIndex].Curve.End);
               if (fromNPTToFlexEnd < 10.0) {
                  var (newNPTAtIndex, idx) = Geom.EvaluatePointAndIndexAtLength (segs, segIndices[index].Value, 11.0/*length offset for approach pt*/,
                        reverseTrace: false);
                  segIndices[index] = idx; notchPoints[index] = newNPTAtIndex;
               }
            }
         }
         index++;
      }
   }

   /// <summary>
   /// The following method computes the wire joint positions
   /// at 25%, 50% and 75% of the lengths and splits the tooling segments in such a way that 
   /// the end point of the segment is the notch or wire joint point
   /// </summary>
   /// <param name="segs">The input tooling segments list</param>
   /// <param name="notchPoints">The input prescribed notch points</param>
   /// <param name="notchPointsInfo">A data structure that stores one notch or wire joint
   /// point per unique index of the list of tooling items after splitting. The end point is
   /// the notch or wire joint distance point</param>
   /// <param name="atLength">A variable that holds the wire joint length</param>
   public void ComputeWireJointPositionsOnFlanges (List<ToolingSegment> segs, Point3?[] notchPoints,
      ref List<NotchPointInfo> notchPointsInfo, double atLength, int approachSegmentIndex) {

      // Split the tooling segments at wire joint length from notch points 
      mWireJointPts = [null, null, null, null];
      if (!mIsWireJointsNeeded) mWireJointPts = [null, null];
      int ptCount = 0;
      double percent = 0;
      for (int ii = 0; ii < notchPoints.Length; ii++) {
         string pos = ii switch {
            0 => "@25",
            1 => "@50",
            2 => "@75",
            _ => ""
         };
         if (!mIsWireJointsNeeded) {
            if (notchPointsInfo.Count > 1) throw new Exception ("Notchpoints info size > 1 for no WJT case");

            if (notchPointsInfo[0].mPercentage.EQ (0.25) ||
               notchPointsInfo[0].mPercentage.EQ (0.125)) {
               pos = "@25"; percent = 0.25;
            } else if (notchPointsInfo[0].mPercentage.EQ (0.50)) {
               pos = "@50"; percent = 0.50;
            } else if (notchPointsInfo[0].mPercentage.EQ (0.75) ||
                notchPointsInfo[0].mPercentage.EQ (0.875)) {
               pos = "@75"; percent = 0.75;
            }
         }

         var segIndex = notchPointsInfo
            .Where (n => n.mPosition == pos)
            .Select (n => n.mSegIndex)
            .FirstOrDefault (-1);

         if (notchPoints[ii] == null || segIndex == -1) { ptCount++; continue; }

         // Find the index of the occurrence of the point where Curve3.End matches the given point
         var notchPointIndex = segs.FindIndex (s => s.Curve.End.DistTo (notchPoints[ii].Value).EQ (0, mSplit ? 1e-4 : 1e-6));

         // If the wire Joint Distance is close to 0.0, this should not affect
         // the parameters of the notch at 50% of the length (pre, @50 and post)
         if (atLength < 0.5 && ii == approachSegmentIndex) atLength = 2.0;
         (mWireJointPts[ptCount], var segIndexToSplit) = Geom.EvaluatePointAndIndexAtLength (segs, notchPointIndex,
            atLength/*, segs[notchPointIndex].Item2.Normalized ()*/);
         var splitToolSegs = Utils.SplitToolingSegmentsAtPoint (segs, segIndexToSplit, mWireJointPts[ptCount].Value,
            segs[notchPointIndex].Vec0.Normalized (), tolerance: mSplit ? 1e-4 : 1e-6);

         // Make the NotchPointsInfo to contain unique entries by having unique index of the
         // tooling segments list per point (notch or wire joint)
         MergeSegments (ref splitToolSegs, ref segs, segIndexToSplit);

         // Update the notchPointsINfo
         if (mIsWireJointsNeeded) {
            if (ii == approachSegmentIndex) {
               pos = "";
               switch (ii) {
                  case 0: pos = "@2501"; percent = 0.2501; break;
                  case 1: pos = "@5001"; percent = 0.5001; break;
                  case 2: pos = "@7501"; percent = 0.7501; break;
                  default: break;
               }
            } else {
               switch (ii) {
                  case 0: pos = "@2501"; percent = 0.2501; break;
                  case 1: pos = "@5001"; percent = 0.5001; break;
                  case 2: pos = "@7501"; percent = 0.7501; break;
                  default: break;
               }
            }
         } else {
            if (pos == "@50") {
               pos = "@5001"; percent = 0.5001;
            } else if (pos == "@25") {
               pos = "@2501"; percent = 0.2501;
            } else if (pos == "@75") {
               pos = "@7501"; percent = 0.7501;
            }

         }
         Utils.UpdateNotchPointsInfo (segs, ref notchPointsInfo, pos, percent, splitToolSegs[0].Curve.End,
            mIsWireJointsNeeded, tolerance: mSplit ? 1e-4 : 1e-6);
         Utils.CheckSanityNotchPointsInfo (segs, notchPointsInfo, tolerance: mSplit ? 1e-4 : 1e-6);

         // Atapproach index...
         if (ii == approachSegmentIndex) {
            ptCount++;
            notchPointIndex = segs.FindIndex (s => s.Curve.End.DistTo (notchPoints[ii].Value).EQ (0, mSplit ? 1e-4 : 1e-6));
            (mWireJointPts[ptCount], segIndexToSplit) = Geom.EvaluatePointAndIndexAtLength (segs, notchPointIndex, atLength,
               reverseTrace: true);
            splitToolSegs = Utils.SplitToolingSegmentsAtPoint (segs, segIndexToSplit, mWireJointPts[ptCount].Value,
               segs[notchPointIndex].Vec0.Normalized (), tolerance: mSplit == true ? 1e-4 : 1e-6);
            MergeSegments (ref splitToolSegs, ref segs, segIndexToSplit);
            if (mIsWireJointsNeeded) {
               switch (ii) {
                  case 0: pos = "@2499"; percent = 0.2499; break;
                  case 1: pos = "@4999"; percent = 0.4999; break;
                  case 2: pos = "@7499"; percent = 0.7499; break;
                  default: break;
               }
            } else {
               if (pos == "@5001") {
                  pos = "@4999"; percent = 0.4999;
               } else if (pos == "@2501") {
                  pos = "@2499"; percent = 0.2499;
               } else if (pos == "@7501") {
                  pos = "@7499"; percent = 0.7499;
               }
            }
            Utils.UpdateNotchPointsInfo (segs, ref notchPointsInfo, pos, percent, splitToolSegs[0].Curve.End,
               mIsWireJointsNeeded, tolerance: mSplit ? 1e-4 : 1e-6);
            Utils.CheckSanityNotchPointsInfo (segs, notchPointsInfo, tolerance: mSplit ? 1e-4 : 1e-6);
            (mWireJointPts[ptCount], mWireJointPts[ptCount - 1]) = (mWireJointPts[ptCount - 1], mWireJointPts[ptCount]);
         }
         ptCount++;
      }
   }

   /// <summary>
   /// The following method computes the indices of all the notch points and wire joint points that are occurring
   /// on the list of segmented tooling segments for each of the above points. 
   /// Note: The notch point or the wire joint lengthed points occur as the end point of the tooling segment
   /// </summary>
   /// <param name="segs">The segmented tooling segments list</param>
   /// <param name="notchPoints">The points at the prescribed lengths of tooling (25%, 50% and 75%)</param>
   /// <param name="wjtPoints">The points on the list of tooling segments where wire joint jump trace is desired</param>
   /// <param name="flexWjtPoints">The start and the end points of the flex tooling which is also treated 
   /// as wire joint jump trace</param>
   public void ComputeNotchToolingIndices (List<ToolingSegment> segs, Point3?[] notchPoints,
      Point3?[] wjtPoints, List<Point3> flexWjtPoints) {
      mNotchIndices = new NotchSegmentIndices ();
      int ptCount = 0;
      for (int ii = 0; ii < notchPoints.Length; ii++) {
         if (notchPoints[ii] == null) {
            ptCount++;
            continue;
         }
         int notchPointIndexPostSplit = -1;
         notchPointIndexPostSplit = segs
             .Select ((segment, idx) => new { segment, idx })
             .Where (x => x.segment.Curve.End.DistTo (notchPoints[ii].Value).EQ (0, mSplit ? 1e-4 : 1e-6))
             .Select (x => x.idx)
             .FirstOrDefault ();
         int wjtPointIndexPostSplit = -1;
         if (wjtPoints[ptCount] != null)
            wjtPointIndexPostSplit = segs
                .Select ((segment, idx) => new { segment, idx })
                .Where (x => x.segment.Curve.End.DistTo (wjtPoints[ptCount].Value).EQ (0, mSplit ? 1e-4 : 1e-6))
                .Select (x => x.idx)
                .FirstOrDefault ();
         if (ii == mApproachIndex) {
            mNotchIndices.segIndexAtWJTApproach = notchPointIndexPostSplit;
            mNotchIndices.segIndexAtWJTPreApproach = wjtPointIndexPostSplit;
            if (ii == 0 && ii != mApproachIndex && mNotchPointsInfo.Where (np => np.mPosition == "@25").FirstOrDefault ().mSegIndex != -1) {
               mNotchIndices.segIndexAt25pc = notchPointIndexPostSplit;
               mNotchIndices.segIndexAtWJTPost25pc = wjtPointIndexPostSplit;
            } else if (ii == 1 && ii != mApproachIndex && mNotchPointsInfo.Where (np => np.mPosition == "@50").FirstOrDefault ().mSegIndex != -1) {
               mNotchIndices.segIndexAt50pc = notchPointIndexPostSplit;
               //mNotchIndices.segIndexAtWJTPre50pc = wjtPointIndexPostSplit;
               mNotchIndices.segIndexAtWJTPost50pc = wjtPointIndexPostSplit;
            } else if (ii == 2 && ii != mApproachIndex && mNotchPointsInfo.Where (np => np.mPosition == "@75").FirstOrDefault ().mSegIndex != -1) {
               mNotchIndices.segIndexAt75pc = notchPointIndexPostSplit;
               mNotchIndices.segIndexAtWJTPost75pc = wjtPointIndexPostSplit;
            }
         } else {
            if (ii == 0 && ii != mApproachIndex && mNotchPointsInfo.Where (np => np.mPosition == "@25").FirstOrDefault ().mSegIndex != -1) {
               mNotchIndices.segIndexAt25pc = notchPointIndexPostSplit;
               mNotchIndices.segIndexAtWJTPost25pc = wjtPointIndexPostSplit;
            } else if (ii == 1 && ii != mApproachIndex && mNotchPointsInfo.Where (np => np.mPosition == "@50").FirstOrDefault ().mSegIndex != -1) {
               mNotchIndices.segIndexAt50pc = notchPointIndexPostSplit;
               //mNotchIndices.segIndexAtWJTPre50pc = wjtPointIndexPostSplit;
               mNotchIndices.segIndexAtWJTPost50pc = wjtPointIndexPostSplit;
            } else if (ii == 2 && ii != mApproachIndex && mNotchPointsInfo.Where (np => np.mPosition == "@75").FirstOrDefault ().mSegIndex != -1) {
               mNotchIndices.segIndexAt75pc = notchPointIndexPostSplit;
               mNotchIndices.segIndexAtWJTPost75pc = wjtPointIndexPostSplit;
            }
         }
         if (ii == mApproachIndex) {
            ptCount++;
            if (wjtPoints[ptCount] == null) continue;
            wjtPointIndexPostSplit = segs
                .Select ((segment, idx) => new { segment, idx })
                .Where (x => x.segment.Curve.End.DistTo (wjtPoints[ptCount].Value).EQ (0, mSplit ? 1e-4 : 1e-6))
                .Select (x => x.idx)
                .FirstOrDefault ();
            mNotchIndices.segIndexAtWJTPostApproach = wjtPointIndexPostSplit;
         }
         ptCount++;
      }
      for (int ii = 0; ii < flexWjtPoints.Count; ii += 4) {
         var preSegFlexStPointIndex = segs
             .Select ((segment, idx) => new { segment, idx })
             .Where (x => x.segment.Curve.End.DistTo (flexWjtPoints[ii]).EQ (0, mSplit ? 1e-4 : 1e-6))
             .Select (x => x.idx)
             .FirstOrDefault ();
         var flexStPointIndex = segs
             .Select ((segment, idx) => new { segment, idx })
             .Where (x => x.segment.Curve.End.DistTo (flexWjtPoints[ii + 1]).EQ (0, mSplit ? 1e-4 : 1e-6))
             .Select (x => x.idx)
             .FirstOrDefault ();
         var flexEndPointIndex = segs
             .Select ((segment, idx) => new { segment, idx })
             .Where (x => x.segment.Curve.End.DistTo (flexWjtPoints[ii + 2]).EQ (0, mSplit ? 1e-4 : 1e-6))
             .Select (x => x.idx)
             .FirstOrDefault ();
         var postFlexEndPointIndex = segs
             .Select ((segment, idx) => new { segment, idx })
             .Where (x => x.segment.Curve.End.DistTo (flexWjtPoints[ii + 3]).EQ (0, mSplit ? 1e-4 : 1e-6))
             .Select (x => x.idx)
             .FirstOrDefault ();
         Tuple<int, int, int, int> flexIndices = new (preSegFlexStPointIndex, flexStPointIndex, flexEndPointIndex, postFlexEndPointIndex);
         mNotchIndices.flexIndices.Add (flexIndices);
      }

      // Set wirejoint indices to -1 if they exist in between the flex indices
      for (int ii = 0; ii < mNotchIndices.flexIndices.Count; ii++) {
         List<int> flexIndices = [mNotchIndices.flexIndices[ii].Item1, mNotchIndices.flexIndices[ii].Item2, mNotchIndices.flexIndices[ii].Item3, mNotchIndices.flexIndices[ii].Item4];
         flexIndices.Sort ();
         if (mNotchIndices.segIndexAt25pc >= flexIndices[0] && mNotchIndices.segIndexAt25pc <= flexIndices[3]) {
            mNotchIndices.segIndexAt25pc = -1;
            mNotchIndices.segIndexAtWJTPost25pc = -1;
         }
         if (mNotchIndices.segIndexAt75pc >= flexIndices[0] && mNotchIndices.segIndexAt75pc <= flexIndices[3]) {
            mNotchIndices.segIndexAt75pc = -1;
            mNotchIndices.segIndexAtWJTPost75pc = -1;
         }
      }
   }

   /// <summary>
   /// The following method creates a Notch Sequence Section with user inputs
   /// </summary>
   /// <param name="startIndex">The start index of the list of tooling segments</param>
   /// <param name="endIndex">The end index of the list of tooling segments</param>
   /// <param name="notchSectionType">The type of tooling block that is desired</param>
   /// <returns></returns>
   /// <exception cref="Exception">The method expects that the start and end end indices are in non 
   /// decreasing order. Unless, this throws an exception. In the case of reversed tooling segment
   /// too, the start and end index should be prescribed in the same non-decreasing order</exception>
   public static NotchSequenceSection CreateNotchSequence (int startIndex, int endIndex, NotchSectionType notchSectionType) {
      if (notchSectionType == NotchSectionType.MachineToolingForward) {
         if (startIndex > endIndex) throw new Exception ("StartIndex < endIndex for forward machiniing");
      }
      var nsq = new NotchSequenceSection () {
         StartIndex = startIndex,
         EndIndex = endIndex,
         SectionType = notchSectionType
      };
      return nsq;
   }

   /// <summary>
   /// Creates notch sequences in the reverse direction of the list of tooling,
   /// taking into account the notch and wirejoint points and flex sections 
   /// for various possible occurrences of notch points and flex sections.
   /// </summary>
   /// <param name="mNotchIndices">The indices of the notch and wirejoint points.</param>
   /// <returns>A list of assembled notch sequence sections to be used for generating G Code.</returns>
   /// <exception cref="Exception">Thrown when the notch sequences do not follow the correct directional
   /// or sequential order. If the order is incorrect, an exception will be thrown.</exception>
   List<NotchSequenceSection> CreateNotchReverseSequences () {
      bool appAt25 = false, appAt50 = false, appAt75 = false;
      if (mNotchIndices.segIndexAt25pc == mNotchIndices.segIndexAtWJTApproach) appAt25 = true;
      else if (mNotchIndices.segIndexAt50pc == mNotchIndices.segIndexAtWJTApproach) appAt50 = true;
      else if (mNotchIndices.segIndexAt75pc == mNotchIndices.segIndexAtWJTApproach) appAt75 = true;
      List<(int Index, IndexType Type)> notchIndexSequence = [
    (0, IndexType.Zero),
       (mNotchIndices.segIndexAtWJTPreApproach, IndexType.PreApproach),
       (mNotchIndices.segIndexAtWJTApproach, IndexType.Approach),
       (mNotchIndices.segIndexAtWJTPostApproach, IndexType.PostApproach),
       (mSegments.Count-1, IndexType.Max)
      ];
      if (!appAt25) {
         if (mNotchIndices.segIndexAt25pc != -1) notchIndexSequence.Add ((mNotchIndices.segIndexAt25pc, IndexType.At25));
         if (mNotchIndices.segIndexAtWJTPost25pc != -1) notchIndexSequence.Add ((mNotchIndices.segIndexAtWJTPost25pc, IndexType.Post25));
      }
      if (!appAt50) {
         if (mNotchIndices.segIndexAt50pc != -1) notchIndexSequence.Add ((mNotchIndices.segIndexAt50pc, IndexType.At50));
         if (mNotchIndices.segIndexAtWJTPost50pc != -1) notchIndexSequence.Add ((mNotchIndices.segIndexAtWJTPost50pc, IndexType.Post50));
      }
      if (!appAt75) {
         if (mNotchIndices.segIndexAt75pc != -1) notchIndexSequence.Add ((mNotchIndices.segIndexAt75pc, IndexType.At75));
         if (mNotchIndices.segIndexAtWJTPost75pc != -1) notchIndexSequence.Add ((mNotchIndices.segIndexAtWJTPost75pc, IndexType.Post75));
      }
      if (mNotchIndices.flexIndices.Count > 0) {
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item1, IndexType.Flex1BeforeStart));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item2, IndexType.Flex1Start));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item3, IndexType.Flex1End));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item4, IndexType.Flex1AfterEnd));
      }
      if (mNotchIndices.flexIndices.Count > 1) {
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item1, IndexType.Flex2BeforeStart));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item2, IndexType.Flex2Start));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item3, IndexType.Flex2End));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item4, IndexType.Flex2AfterEnd));
      }

      // Sort the collection in descending order of Index
      notchIndexSequence = [.. notchIndexSequence.OrderByDescending (item => item.Index)];
      IndexType prevIdxType = IndexType.None;
      int prevIdx = -1;
      bool started = false;
      List<NotchSequenceSection> reverseNotchSequences = [];
      bool at25Handled = false; bool atZeroHandled = false;
      for (int ii = 0; ii < notchIndexSequence.Count; ii++) {
         if (notchIndexSequence[ii].Index == -1) break;
         if (prevIdxType == IndexType.Zero && at25Handled == true) continue;
         if (prevIdxType == IndexType.At25 && atZeroHandled == true) continue;
         if (prevIdx == notchIndexSequence[ii].Index && prevIdx != 0)
            throw new Exception ("Two notch sequence indices are the same. Wrong");
         int startIndex = -1;
         switch (notchIndexSequence[ii].Type) {
            case IndexType.PostApproach:
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               started = true;
               break;
            case IndexType.Flex2AfterEnd:
               if (!started) continue;
               if (prevIdxType == IndexType.PostApproach) startIndex = prevIdx;
               else startIndex = prevIdx - 1;
               if (startIndex >= notchIndexSequence[ii].Index + 1)
                  reverseNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index + 1, NotchSectionType.MachineToolingReverse));
               else throw new Exception ("Start < end Index");
               reverseNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpReverseOnFlex));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex2End:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex2AfterEnd) throw new Exception ("Prev and curr idx types are not compatible");
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex2Start:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex2End) throw new Exception ("Prev and curr idx types are not compatible");
               reverseNotchSequences.Add (CreateNotchSequence (prevIdx, notchIndexSequence[ii].Index, NotchSectionType.MachineFlexToolingReverse));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex2BeforeStart:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex2Start) throw new Exception ("Prev and curr idx types are not compatible");
               reverseNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpReverseOnFlex));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1AfterEnd:
               if (!started) continue;
               if (prevIdx - 1 >= notchIndexSequence[ii].Index + 1) {
                  if (prevIdxType == IndexType.PostApproach || prevIdxType == IndexType.At25) startIndex = prevIdx;
                  else startIndex = prevIdx - 1;
                  if (startIndex >= notchIndexSequence[ii].Index + 1)
                     reverseNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index + 1, NotchSectionType.MachineToolingReverse));
                  else throw new Exception ("Start < end Index");
               }
               reverseNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpReverseOnFlex));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1End:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex1AfterEnd) throw new Exception ("Prev and curr idx types are not compatible");
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1Start:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex1End) throw new Exception ("Prev and curr idx types are not compatible");
               reverseNotchSequences.Add (CreateNotchSequence (prevIdx, notchIndexSequence[ii].Index, NotchSectionType.MachineFlexToolingReverse));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1BeforeStart:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex1Start && prevIdxType != IndexType.Zero) throw new Exception ("Prev and curr idx types are not compatible");
               reverseNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpReverseOnFlex));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Post50:
            case IndexType.Post25:
               if (!started) continue;
               if (prevIdxType == IndexType.Flex1BeforeStart || prevIdxType == IndexType.Flex2BeforeStart ||
                  notchIndexSequence[ii].Index != mNotchIndices.segIndexAtWJTPostApproach) {
                  if (prevIdxType == IndexType.PostApproach) startIndex = prevIdx;
                  else startIndex = prevIdx - 1;
                  if (startIndex >= notchIndexSequence[ii].Index + 1) {
                     reverseNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index + 1, NotchSectionType.MachineToolingReverse));
                     prevIdxType = notchIndexSequence[ii].Type;
                     prevIdx = notchIndexSequence[ii].Index;
                  } else throw new Exception ("prevIdx - 1 > notchIndexSequence[ii].Index + 1 iS FALSE");
                  if (notchIndexSequence[ii].Index != mNotchIndices.segIndexAtWJTPostApproach)
                     reverseNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpReverse));
               }
               break;
            case IndexType.At25:
            case IndexType.At50:
               if (!started) continue;
               if (notchIndexSequence[ii].Type == IndexType.At25 && prevIdxType == IndexType.Post25) {
                  prevIdxType = IndexType.At25;
                  prevIdx = notchIndexSequence[ii].Index;
               }
               break;
            case IndexType.Zero:
               if (!started) continue;
               if (prevIdx != notchIndexSequence[ii].Index && notchIndexSequence[ii - 1].Index != mNotchIndices.segIndexAtWJTPost25pc &&
                  notchIndexSequence[ii - 1].Index != mNotchIndices.segIndexAtWJTApproach &&
                  notchIndexSequence[ii - 1].Type != IndexType.Flex1BeforeStart &&
                  notchIndexSequence[ii - 1].Type != IndexType.Flex2BeforeStart) startIndex = prevIdx;
               else startIndex = prevIdx - 1;
               reverseNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index, NotchSectionType.MachineToolingReverse));
               prevIdx = notchIndexSequence[ii].Index;
               prevIdxType = IndexType.Zero;
               atZeroHandled = true;
               break;
            default:
               break;
         }
         ;
      }
      if (prevIdx > 0)
         reverseNotchSequences.Add (CreateNotchSequence (prevIdx, 0, NotchSectionType.MachineFlexToolingReverse));
      return reverseNotchSequences;
   }

   /// <summary>
   /// The following method creates Notch sequences in the forward direction of the 
   /// list of tooling, considering the notch and wirejoint points and flex sections 
   /// for various possible occurances of notch points and flex sections.
   /// </summary>
   /// <param name="mNotchIndices">The indices of the notch, wire joint points</param>
   /// <returns>A list of assembled notch sequence sections to be used for writing G Code</returns>
   /// <exception cref="Exception">The notch sequences happen in a strong directional
   /// order. If this directional/sequencial order is wrong, exception will be thrown</exception>
   List<NotchSequenceSection> CreateNotchForwardSequences () {
      bool appAt25 = false, appAt50 = false, appAt75 = false;
      if (mNotchIndices.segIndexAt25pc == mNotchIndices.segIndexAtWJTApproach) appAt25 = true;
      else if (mNotchIndices.segIndexAt50pc == mNotchIndices.segIndexAtWJTApproach) appAt50 = true;
      else if (mNotchIndices.segIndexAt75pc == mNotchIndices.segIndexAtWJTApproach) appAt75 = true;
      List<(int Index, IndexType Type)> notchIndexSequence = [
    (0, IndexType.Zero),
       (mNotchIndices.segIndexAtWJTPreApproach, IndexType.PreApproach),
       (mNotchIndices.segIndexAtWJTApproach, IndexType.Approach),
       (mNotchIndices.segIndexAtWJTPostApproach, IndexType.PostApproach),
       (mSegments.Count-1, IndexType.Max)
      ];
      if (!appAt25) {
         if (mIsWireJointsNeeded) {
            notchIndexSequence.Add ((mNotchIndices.segIndexAt25pc, IndexType.At25));
            notchIndexSequence.Add ((mNotchIndices.segIndexAtWJTPost25pc, IndexType.Post25));
         }
      }
      if (!appAt50) {
         if (mIsWireJointsNeeded) {
            notchIndexSequence.Add ((mNotchIndices.segIndexAt50pc, IndexType.At50));
            notchIndexSequence.Add ((mNotchIndices.segIndexAtWJTPost50pc, IndexType.Post50));
         }
      }
      if (!appAt75) {
         if (mIsWireJointsNeeded) {
            notchIndexSequence.Add ((mNotchIndices.segIndexAt75pc, IndexType.At75));
            notchIndexSequence.Add ((mNotchIndices.segIndexAtWJTPost75pc, IndexType.Post75));
         }
      }
      if (mNotchIndices.flexIndices.Count > 0) {
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item1, IndexType.Flex1BeforeStart));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item2, IndexType.Flex1Start));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item3, IndexType.Flex1End));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[0].Item4, IndexType.Flex1AfterEnd));
      }
      if (mNotchIndices.flexIndices.Count > 1) {
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item1, IndexType.Flex2BeforeStart));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item2, IndexType.Flex2Start));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item3, IndexType.Flex2End));
         notchIndexSequence.Add ((mNotchIndices.flexIndices[1].Item4, IndexType.Flex2AfterEnd));
      }

      // Sort the collection in ascending order of Index
      notchIndexSequence = [.. notchIndexSequence.OrderBy (item => item.Index)];
      IndexType prevIdxType = IndexType.None;
      int prevIdx = -1;
      bool started = false;
      List<NotchSequenceSection> forwardNotchSequences = [];
      for (int ii = 0; ii < notchIndexSequence.Count; ii++) {
         if (notchIndexSequence[ii].Index == -1) continue;
         if (prevIdxType == IndexType.Max && prevIdxType != IndexType.Post75) throw new Exception ("IndexType.Max is referred to by two entries");
         if (prevIdx == notchIndexSequence[ii].Index && prevIdx != notchIndexSequence[^1].Index)
            throw new Exception ("Two notch sequence indices are the same. Wrong");
         int startIndex = -1;
         switch (notchIndexSequence[ii].Type) {
            case IndexType.PreApproach:
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               started = true;
               break;
            case IndexType.Flex2BeforeStart:
               if (!started) continue;
               startIndex = prevIdx + 1;
               if (startIndex < notchIndexSequence[ii].Index - 1) {
                  forwardNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index - 1, NotchSectionType.MachineToolingForward));
                  forwardNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpForwardOnFlex));
                  prevIdxType = notchIndexSequence[ii].Type;
                  prevIdx = notchIndexSequence[ii].Index;
               }
               break;
            case IndexType.Flex2Start:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex2BeforeStart) throw new Exception ("Prev and curr idx types are not compatible");
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex2End:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex2Start) throw new Exception ("Prev and curr idx types are not compatible");
               forwardNotchSequences.Add (CreateNotchSequence (prevIdx, notchIndexSequence[ii].Index, NotchSectionType.MachineFlexToolingForward));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex2AfterEnd:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex2End) throw new Exception ("Prev and curr idx types are not compatible");
               forwardNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpForwardOnFlex));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1BeforeStart:
               if (!started) continue;
               startIndex = prevIdx + 1;
               if (startIndex <= notchIndexSequence[ii].Index) {
                  if (startIndex < notchIndexSequence[ii].Index - 1)
                     forwardNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index - 1, NotchSectionType.MachineToolingForward));
                  else
                     forwardNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index, NotchSectionType.MachineToolingForward));
                  forwardNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpForwardOnFlex));
                  prevIdxType = notchIndexSequence[ii].Type;
                  prevIdx = notchIndexSequence[ii].Index;
               }
               break;
            case IndexType.Flex1Start:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex1BeforeStart) throw new Exception ("Prev and curr idx types are not compatible");
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1End:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex1Start) throw new Exception ("Prev and curr idx types are not compatible");
               forwardNotchSequences.Add (CreateNotchSequence (prevIdx, notchIndexSequence[ii].Index, NotchSectionType.MachineFlexToolingForward));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.Flex1AfterEnd:
               if (!started) continue;
               if (prevIdxType != IndexType.Flex1End) throw new Exception ("Prev and curr idx types are not compatible");
               forwardNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpForwardOnFlex));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;
            case IndexType.At75:
            case IndexType.At50:
            case IndexType.At25:
               if (!started) continue;
               startIndex = prevIdx + 1;
               if (prevIdxType == IndexType.Flex1AfterEnd || prevIdxType == IndexType.Flex2AfterEnd || notchIndexSequence[ii].Index != mNotchIndices.segIndexAtWJTPreApproach) {
                  if (startIndex < notchIndexSequence[ii].Index)
                     forwardNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index, NotchSectionType.MachineToolingForward));
                  prevIdxType = notchIndexSequence[ii].Type;
                  prevIdx = notchIndexSequence[ii].Index;
               }
               break;
            case IndexType.Post25:
            case IndexType.Post50:
            case IndexType.Post75:
               if (!started) continue;
               forwardNotchSequences.Add (CreateNotchSequence (notchIndexSequence[ii].Index, notchIndexSequence[ii].Index, NotchSectionType.WireJointTraceJumpForward));
               prevIdxType = notchIndexSequence[ii].Type;
               prevIdx = notchIndexSequence[ii].Index;
               break;

            case IndexType.Max:
               if (!started) continue;
               if (notchIndexSequence[^1].Index != prevIdx)
                  startIndex = prevIdx + 1;
               else startIndex = prevIdx;
               if (startIndex <= notchIndexSequence[ii].Index) {
                  forwardNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index, NotchSectionType.MachineToolingForward));
                  prevIdxType = notchIndexSequence[ii].Type;
               } else {
                  if (notchIndexSequence[^2].Type != IndexType.Post75 && notchIndexSequence[^2].Type != IndexType.Flex1AfterEnd &&
                     notchIndexSequence[^2].Type != IndexType.Flex2AfterEnd)
                     forwardNotchSequences.Add (CreateNotchSequence (startIndex, notchIndexSequence[ii].Index, NotchSectionType.MachineToolingForward));
               }
               prevIdx = notchIndexSequence[ii].Index;
               break;
            default:
               break;
         }
         ;
      }
      return forwardNotchSequences;
   }

   /// <summary>
   /// Creates a sequence section to enter or re-enter the notch tooling 
   /// before or after machining the first part. The following steps are involved:
   /// <list type="number">
   /// <item>
   ///     <description>The notch tooling is machined, allowing the scrap-side material 
   ///     to connect with the required side at smaller lengths (wire joint distances). 
   ///     A direct approach to the tooling segment is completely avoided.</description>
   /// </item>
   /// <item>
   ///     <description>A cutting stroke is made from the approximate midpoint of the 
   ///     scrap-side material to the nearest edge.</description>
   /// </item>
   /// <item>
   ///     <description>Another cutting stroke is made from the midpoint to the 50%-th notch point.</description>
   /// </item>
   /// <item>
   ///     <description>Machining of the notch starts in the direction that reaches the 
   ///     0 of X the fastest.</description>
   /// </item>
   /// <item>
   ///     <description>Further cutting occurs from the midpoint to the 50%-th notch point, 
   ///     with machining in the direction opposite to the previously traced path.</description>
   /// </item>
   /// </list>
   /// </summary>
   /// <param name="reEntry">Indicates whether this is a re-entry (True) or a first-time entry (False).</param>
   /// <returns>A structure that holds the type of the sequence section.</returns>
   public static NotchSequenceSection CreateApproachToNotchSequence (bool reEntry = false) {
      // Create first notch sequence
      NotchSequenceSection nsq = new () { SectionType = NotchSectionType.ApproachMachining };
      if (reEntry) nsq.SectionType = NotchSectionType.ApproachOnReEntry;
      return nsq;
   }

   /// <summary>
   /// This method is an internal utility to store unique set of 
   /// the index of the tooling segment against the notch point (the end
   /// point of that index-th segment). 
   /// Reason: There are cases where more than 1 notch points occur in the 
   /// same index-th tooling segment. This case is split so that one 
   /// index-th tooling segment does not contain more than 1 notch point
   /// </summary>
   /// <param name="splitToolSegs">The tooling segments that are already split at
   /// the points of interest</param>
   /// <param name="segs">The parent segments list of the tooling, which needs to be 
   /// corrected with the split segments</param>
   /// <param name="notchPointsInfo">The structure that holds the index and the single 
   /// notch point</param>
   /// <param name="index">The index at which the split has happened</param>
   public static void MergeSegments (ref List<ToolingSegment> splitToolSegs, ref List<ToolingSegment> segs, int segIndexToSplit) {
      if (splitToolSegs.Count > 1) {
         segs.RemoveAt (segIndexToSplit);
         segs.InsertRange (segIndexToSplit, splitToolSegs);
      }
   }

   /// <summary>
   /// This method creates and populates various notch parameters and data structures required 
   /// for tooling operations. It performs the following actions:
   /// <list type="number">
   /// <item>
   ///     <description>Splits the list of tooling segments at all notch points, wire joint points, 
   ///     and at the start and end of flex sections.</description>
   /// </item>
   /// <item>
   ///     <description>Records indices and coordinates of the notch points where occurrences happen.</description>
   /// </item>
   /// <item>
   ///     <description>Identifies wire joint points, their lengths, and their respective indices.</description>
   /// </item>
   /// <item>
   ///     <description>Determines the indices of points at the start and end of flex sections.
   ///     <list type="bullet">
   ///         <item>
   ///             <description>The initial segment whose end marks the starting point of the flex section.
   ///             This segment's length is equivalent to the wire joint distance.</description>
   ///         </item>
   ///         <item>
   ///             <description>The segment that represents the first segment of the flex section.</description>
   ///         </item>
   ///         <item>
   ///             <description>The index of the segment that marks the end of the flex section.</description>
   ///         </item>
   ///         <item>
   ///             <description>The segment whose starting point is the ending point of the flex section.
   ///             Its length is also the wire joint distance.</description>
   ///         </item>
   ///     </list>
   ///     </description>
   /// </item>
   /// <item>
   ///     <description>Stores sequentially ordered indices for the segments and points.</description>
   /// </item>
   /// </list>
   /// </summary>
   void ComputeNotchParameters () {
      Utils.CheckSanityOfToolingSegments (mSegments);
      if (!mToolingItem.IsNotch ()) return;

      // Find the flex segment indices
      mFlexIndices = GetFlexSegmentIndices (mSegments);
      var tname = mToolingItem.Name;

      // Toolpath computations
      mNToolPath = new (mSegments, MinNotchLengthThreshold, NotchWireJointDistance, mLeastWJLength);
      mNToolPath.EvalNotchSpecPositions ();
      var positions = mNToolPath.NotchPositions;
      mNToolPath.SegmentPath ();

      // Check the path length at approach
      double len = 0;
      foreach (var seg in mNToolPath.Segs) {
         if (seg.NotchSectionType == NotchSectionType.ApproachMachining) break;
         len += seg.Length;
      }

      mSegments = [.. mNToolPath.Segs.Select (seg => seg.Clone ())];
      mNotchIndices.segIndexAtWJTApproach = mSegments.FindIndex (ts => ts.NotchSectionType == NotchSectionType.ApproachMachining);
      if (mNotchIndices.segIndexAtWJTApproach == -1)
         throw new Exception ("Notch.ComputeNotchParameters: Segment corresponding to NotchSectionType.ApproachMachining not found");

      mNotchIndices.segIndexAtWJTPostApproach = mSegments.FindIndex (ts => ts.NotchSectionType == NotchSectionType.GambitPostApproachMachining);
      if (mNotchIndices.segIndexAtWJTPostApproach == -1)
         throw new Exception ("Notch.ComputeNotchParameters: Segment corresponding to NotchSectionType.GambitPostApproachMachining not found");

      mNotchIndices.segIndexAtWJTPreApproach = mSegments.FindIndex (ts => ts.NotchSectionType == NotchSectionType.GambitPreApproachMachining);
      if (mNotchIndices.segIndexAtWJTPreApproach == -1)
         throw new Exception ("Notch.ComputeNotchParameters: Segment corresponding to NotchSectionType.GambitPreApproachMachining not found");
      mNotchSequences = mNToolPath.GetNotchSequences ();


      // Compute the notch attributes
      mNotchAttrs = GetNotchAttributes (mSegments, [(mNToolPath.ApproachParameters.Value.SegIndex, mNToolPath.ApproachParameters.Value.NPosition)],
      mFullPartBound, mToolingItem, mGCodeGen.JobInnerRadius, mGCodeGen.JobThickness, mGCodeGen.PartConfigType);
      mApproachIndex = 0;
      return;
   }

   /// <summary>
   /// This following method is used to quickly compute notch data to decide if the notch is 
   /// valid.
   /// </summary>
   /// <param name="segs">The input tooling segments</param>
   /// <param name="percentPos">The positions of the points occuring in the interested order</param>
   /// <param name="curveLeastLength">The least count length of the curve</param>
   /// <returns></returns>
   public static Tuple<int[], Point3[]> ComputeNotchPointOccuranceParams (List<ToolingSegment> segs, double[] percentPos, double curveLeastLength,
      double tolerance = 1e-6) {
      int count = 0;
      Point3[] notchPoints = new Point3[percentPos.Length];

      int[] segIndices = [-1, -1, -1];
      if (percentPos.Length == 1) segIndices = [-1];
      else if (percentPos.Length == 2) segIndices = [-1, -1];

      List<NotchPointInfo> notchPointsInfo = [];
      while (count < percentPos.Length) {
         List<ToolingSegment> splitCurves = [];
         (segIndices[count], notchPoints[count]) = Utils.GetNotchPointsOccuranceParams (segs, percentPos[count], curveLeastLength);
         var check = Geom.IsPointOnCurve (segs[segIndices[count]].Curve, notchPoints[count], segs[segIndices[count]].Vec0, hintSense: EArcSense.Infer, tolerance);
         notchPointsInfo.FindIndex (np => np.mSegIndex == segIndices[count]);

         // Find the notch point with the specified segIndex
         var index = notchPointsInfo.FindIndex (np => np.mSegIndex == segIndices[count]);
         if (index != -1) notchPointsInfo[index].mPoints.Add (notchPoints[count]);
         else {
            var atpc = 0.0;
            if (percentPos[count].EQ (0.25) || percentPos[count].EQ (0.125)) atpc = 0.25;
            else if (percentPos[count].EQ (0.5)) atpc = 0.5;
            else if (percentPos[count].EQ (0.75) || percentPos[count].EQ (0.875)) atpc = 0.75;
            NotchPointInfo np = new (segIndices[count], notchPoints[count], atpc,
               atpc.EQ (0.25) ? "@25" : (atpc.EQ (0.5) ? "@50" : "@75"));
            notchPointsInfo.Add (np);
            //NotchPointInfo np = new (segIndices[count], notchPoints[count], count == 0 ? 25 : (count == 1 ? 50 : 75),
            //   count == 0 ? "@25" : (count == 1 ? "@50" : "@75"));
            //notchPointsInfo.Add (np);
         }
         count++;
      }
      return new Tuple<int[], Point3[]> (segIndices, notchPoints);
   }

   /// <summary>
   /// This method computes the notch positions fo the entry machining to the 
   /// notch profile. The tool first reaches the position namely, n1, which is 
   /// offset at right angles to the line joining 50% lengthed point and the nearest
   /// boundary along the flange. The tool starts machining from n1 through nMid1 and 
   /// to the end of the flange. It again rapid positions at n2, starts machining from
   /// n2 through nMid2 and to the 50$ point.
   /// </summary>
   /// <param name="toolingItem">The input tooling item</param>
   /// <param name="segs">Preprocessed segments</param>
   /// <param name="notchAttrs">The notch attributes</param>
   /// <param name="bound">The total bound of the part</param>
   /// <param name="approachIndex">The segment index in the segs</param>
   /// <param name="wireJointDistance">The input prescription for allowing a small
   /// joint to prevent the iron sheet from falling after machining</param>
   /// <returns></returns>
   public static (Point3 FirstEntryPt, Point3 FirstMidPt, Point3 FlangeEndPt,
                Point3 SecondEntryPt, Point3 SecondMidPt, Point3 ToolingApproachPt)
   GetNotchApproachPositions (Tooling toolingItem,
                           List<ToolingSegment> segs,
                           List<NotchAttribute> notchAttrs,
                           Bound3 bound,
                           double radius,
                           double thickness,
                           int approachIndex,
                           double wireJointDistance,
                        bool sameSidedExitNotch,
                        MCSettings.PartConfigType partConfigType) {
      var planeNormal = notchAttrs[approachIndex].EndNormal.Normalized ();
      Point3 flangeBoundaryEnd;

      // In order to find the best flange end point somewhere mid between the start and
      // end of notch tooling, to be far away from the starting and end points of the
      // segnments' start and end, a measure of MIN (| (p->Sp) - (p->Ep) | ) is found.
      // This is a generalization of taking the mid point of between the start and
      // end points of the segments. If the notch is only on one of the flanges,
      // a mid point would suffice. But if the notch is on flex or on multiple flanges,
      // the above idea is the best. For any point to be equi distant and on the part,
      // a MIN (| (p->Sp) - (p->Ep) | ) holds good.
      Point3 bestApproachPtOnProfile = new ();
      int bestSegIndex = -1;
      bool bestPointFound = false;
      double thresholdLengthFromStartToApproachPt = 15.0;
      (var segIndex, _) = Utils.GetNotchPointsOccuranceParams (segs, 0.5, 0.5);

      var flangeAt50Pc = Utils.GetArcPlaneFlangeType (segs[segIndex].Vec1, XForm4.IdentityXfm);
      double distToEndAlongX, distToStartAlongX;
      double distToEndAlongY, distToStartAlongY;
      var indices = Utils.GetSegIndicesWithNormal (segs, XForm4.mYAxis);
      if (indices.Item1 == -1 || indices.Item2 == -1)
         indices = Utils.GetSegIndicesWithNormal (segs, XForm4.mNegYAxis);
      if (indices.Item1 == -1 || indices.Item2 == -1)
         indices = Utils.GetSegIndicesWithNormal (segs, XForm4.mZAxis);
      if (indices.Item1 == -1 || indices.Item2 == -1)
         throw new Exception ("Indices with Bottom or Top flanges not found");

      switch (flangeAt50Pc) {
         case EFlange.Bottom:
         case EFlange.Top:
            distToEndAlongX = Math.Abs (segs[indices.Item2].Curve.End.X - segs[segIndex].Curve.End.X);
            distToStartAlongX = Math.Abs (segs[indices.Item1].Curve.Start.X - segs[segIndex].Curve.End.X);
            if (distToStartAlongX.SGT (thresholdLengthFromStartToApproachPt) && distToEndAlongX.SGT (thresholdLengthFromStartToApproachPt)) {
               bestApproachPtOnProfile = segs[segIndex].Curve.End;
               bestSegIndex = segIndex; bestPointFound = true;
            }
            break;
         case EFlange.Web:
            distToEndAlongY = Math.Abs (segs[indices.Item2].Curve.End.Y - segs[segIndex].Curve.End.Y);
            distToStartAlongY = Math.Abs (segs[indices.Item1].Curve.Start.Y - segs[segIndex].Curve.End.Y);
            if (distToStartAlongY.SGT (thresholdLengthFromStartToApproachPt) && distToEndAlongY.SGT (thresholdLengthFromStartToApproachPt)) {
               bestApproachPtOnProfile = segs[segIndex].Curve.End;
               bestSegIndex = segIndex; bestPointFound = true;
            }
            break;
         case EFlange.Flex:
            bestApproachPtOnProfile = segs[segIndex].Curve.End;
            bestSegIndex = segIndex; bestPointFound = true;
            break;
      }
      if (!bestPointFound) {
         Point3[] paramPts = new Point3[51];
         double[] percentPos = new double[51];
         double stPercent = 0.25; double incr = 0.01;
         for (int ii = 0; ii < 51; ii++) percentPos[ii] = stPercent + ii * incr;

         int[] segIndices = new int[51];
         for (int ii = 0; ii < 51; ii++)
            (segIndices[ii], paramPts[ii]) = Utils.GetNotchPointsOccuranceParams (segs, percentPos[ii], 0.5);
         var Sp = segs.First ().Curve.Start; var Ep = segs[^1].Curve.End;

         // By default, 1-th index is assumed to be approach index.
         double minDifference = double.MaxValue;

         // Loop through paramPts[] to find the point that minimizes the distance difference
         for (int i = 0; i < paramPts.Length; i++) {
            var p = paramPts[i];
            double distToStart = p.DistTo (Sp);  // Distance to the start point
            double distToEnd = p.DistTo (Ep);    // Distance to the end point
            double diff = Math.Abs (distToStart - distToEnd);

            if (diff < minDifference) {
               minDifference = diff;
               bestApproachPtOnProfile = p;
               bestSegIndex = segIndices[i];  // Get the corresponding segIndices[]
               bestPointFound = true;
            }
         }
      }
      if (!bestPointFound) throw new Exception ("Best mid point can not be found");

      // For the best point find the notch attribute info. We are interested in finding the 
      // flange end point, which is given by item5 of NotchAttribute
      var notchAttr = Notch.ComputeNotchAttribute (bound, toolingItem, segs, bestSegIndex, bestApproachPtOnProfile, radius, thickness, partConfigType);
      flangeBoundaryEnd = bestApproachPtOnProfile + notchAttr.NearestBdyVec;

      if (sameSidedExitNotch) {
         if ((toolingItem.RefTooling != null && toolingItem.FeatType.Contains ("Split") &&
            (toolingItem.RefTooling.ProfileKind == ECutKind.Top2YNeg || toolingItem.RefTooling.ProfileKind == ECutKind.Top2YPos)) ||
            Utils.IsSameSideExitNotch (toolingItem)) {

            var tooling = Utils.IsSameSideExitNotch (toolingItem) ? toolingItem : toolingItem.RefTooling;

            if (segs[bestSegIndex].Vec0.IsWebFlange ()) {
               // Get the segments that has {0,0,1} as the normal
               indices = Utils.GetSegIndicesWithNormal (segs, XForm4.mZAxis);

               // The following finds the Y coordinate of segments, which is 
               // the least and also belongs to the segment whose nornmal is Z+
               if (indices.Item1 != -1 || indices.Item2 != -1) {
                  flangeBoundaryEnd = segs[indices.Item1].Curve.Start;
                  if (tooling.ProfileKind == ECutKind.Top2YNeg) {
                     if (flangeBoundaryEnd.Y > segs[indices.Item1].Curve.End.Y)
                        flangeBoundaryEnd = segs[indices.Item1].Curve.End;
                     if (flangeBoundaryEnd.Y > segs[indices.Item2].Curve.Start.Y)
                        flangeBoundaryEnd = segs[indices.Item2].Curve.Start;
                     if (flangeBoundaryEnd.Y > segs[indices.Item2].Curve.End.Y)
                        flangeBoundaryEnd = segs[indices.Item2].Curve.End;
                  } else {
                     if (flangeBoundaryEnd.Y < segs[indices.Item1].Curve.End.Y)
                        flangeBoundaryEnd = segs[indices.Item1].Curve.End;
                     if (flangeBoundaryEnd.Y < segs[indices.Item2].Curve.Start.Y)
                        flangeBoundaryEnd = segs[indices.Item2].Curve.Start;
                     if (flangeBoundaryEnd.Y < segs[indices.Item2].Curve.End.Y)
                        flangeBoundaryEnd = segs[indices.Item2].Curve.End;
                  }
                  if (toolingItem.FeatType.Contains ("Split-1")) {
                     var stX = tooling.Segs[0].Curve.Start.X;
                     var endX = tooling.Segs[^1].Curve.End.X;
                     flangeBoundaryEnd = new ((0.7 * stX + 0.3 * endX), flangeBoundaryEnd.Y, flangeBoundaryEnd.Z);
                  } else if (toolingItem.FeatType.Contains ("Split-2")) {
                     var stX = tooling.Segs[0].Curve.Start.X;
                     var endX = tooling.Segs[^1].Curve.End.X;
                     flangeBoundaryEnd = new ((0.4 * stX + 0.6 * endX), flangeBoundaryEnd.Y, flangeBoundaryEnd.Z);
                  } else {
                     var stX = tooling.Segs[0].Curve.Start.X;
                     var endX = tooling.Segs[^1].Curve.End.X;
                     flangeBoundaryEnd = new ((stX + endX) / 2.0, flangeBoundaryEnd.Y, flangeBoundaryEnd.Z);
                  }
               }
            }
         }
      }

      // The point on the segment at the end of the approachIndex-th segment
      Point3 notchPointAtApproachpc = notchAttrs[approachIndex].Curve.End;

      // Vector from approachIndex-th segment end point TO flangeBoundaryEnd
      var outwardVec = flangeBoundaryEnd - notchPointAtApproachpc;
      var outwardVecDir = outwardVec.Normalized ();

      // Notch spec Mid point
      Point3 nMid1 = notchPointAtApproachpc + outwardVec * 0.5;
      double gap = wireJointDistance < 0.5 ? 0.5 : wireJointDistance;

      // Notch Spec second Mid point
      Point3 nMid2 = nMid1 - outwardVecDir * gap;

      // Notch spec wire joint points for mid1 and mid2
      Point3 n1, n2;
      Vector3 n12Axis;
      if (Utils.GetPlaneType (planeNormal, XForm4.IdentityXfm) == EPlane.Top) {

         // Two flange notch ending on the same side is a notch that is not at
         // the ends of the part. n1 and n2 have to be computed as the binormal direction
         // to the cross product of approach tooling direction and the plane normal
         // which is in same sense to the scrap side normal.
         if (sameSidedExitNotch) {
            var scrapSideDir = notchAttrs[approachIndex].ScrapSideDir;
            var pNormal = notchAttrs[approachIndex].EndNormal.Normalized ();
            var inwardDir = -outwardVecDir;
            var biNormal = inwardDir.Cross (pNormal).Normalized ();
            if (biNormal.IsSameSense (scrapSideDir)) n12Axis = biNormal;
            else n12Axis = -biNormal;
         } else n12Axis = XForm4.mYAxis;

         n1 = nMid1 + n12Axis * gap;
         if ((n1 - nMid1).Opposing (outwardVecDir)) n1 = nMid1 - n12Axis * gap;
         n2 = nMid2 + n12Axis * gap;
         if ((n2 - nMid2).Opposing (outwardVecDir)) n2 = nMid2 - n12Axis * gap;
      } else {
         n1 = nMid1 + XForm4.mXAxis * gap;
         if ((n1 - nMid1).Opposing (outwardVecDir)) n1 = nMid1 - XForm4.mXAxis * gap;
         n2 = nMid2 + XForm4.mXAxis * gap;
         if ((n2 - nMid2).Opposing (outwardVecDir)) n2 = nMid2 - XForm4.mXAxis * gap;
      }
      return (n1, nMid1, flangeBoundaryEnd, n2, nMid2, notchPointAtApproachpc);
   }
   #endregion

   #region G Code writer
   /// <summary>
   /// This method writes the G Code Edge Notch.
   /// Prerequisite: The method ComputeNotchParameters() should be
   /// called before calling this method. 
   /// An edge notch is one which is cut along an edge of the part and does 
   /// not describe any area from the edge. It is kind of degenerate
   /// </summary>
   /// <exception cref="Exception">Exception will be thrown if the indices do not conform to
   /// the order.</exception>
   public void WriteEdgeNotch () {
      foreach (var seg in mSegments) {
         mGCodeGen.EnableMachiningDirective ();
         mGCodeGen.WriteCurve (seg, mToolingItem.Name);
         mGCodeGen.DisableMachiningDirective ();
      }
      Exit = mSegments[^1];
   }

   /// <summary>
   /// This methos writes the G Code for notches whose tooling lengths are shorter
   /// than the limit prescribed in settings "Min Notch Length Threshold". 
   /// Any value that is too long or too short might not behave expectedly.
   /// <remarks>
   /// Edge Notch Vs Short Perimeter Notch: The edge notch is a kind of degenerate one
   /// where the tooling happens on the edge only, while the short perimeter notch is one
   /// which is not edge notch but the length of the total tooling is lesser than the prescribed
   /// one, in settings.
   /// </remarks>
   /// </summary>
   public void WriteShortPerimeterNotch () {
      mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, mSegments, mSegments[0].Vec0,
                     mXStart, mXPartition, mXEnd, isFlexCut: false, isLast: true,
                        //isToBeTreatedAsCutOut: false,
                        isValidNotch: true,
                     startIndex: 0,
                     endIndex: mSegments.Count - 1, refSegIndex: 0,
                     "NotchSequence: Short Edge Notch", isShortPerimeterNotch: true);
      {
         var notchEntry = new Tuple<Point3, Vector3> (mSegments[0].Curve.Start, mSegments[0].Vec0);
         mGCodeGen.PrepareforToolApproach (mToolingItem, mSegments, PreviousToolingSegment, PreviousTooling,
            mPrevToolingSegments, mFirstTooling, isValidNotch: true, notchEntry);
         if (!mGCodeGen.RapidMoveToPiercingPositionWithPingPong)
            mGCodeGen.RapidMoveToPiercingPosition (notchEntry.Item1, notchEntry.Item2, EKind.Notch, usePingPongOption: true);

         mGCodeGen.MoveToMachiningStartPosition (notchEntry.Item1, notchEntry.Item2, mToolingItem.Name);
         var isFromWebNotch = Utils.IsMachiningFromWebFlange (mSegments, 0);

         mGCodeGen.WriteToolCorrectionData (mToolingItem);
         mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
         mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebNotch, isNotchCut: false);
         mGCodeGen.WriteToolDiaCompensation (isFlexTooling: false);

         mGCodeGen.EnableMachiningDirective ();
         {
            for (int jj = 0; jj < mSegments.Count; jj++) {
               mGCodeGen.WriteCurve (mSegments[jj], mToolingItem.Name);
               mBlockCutLength += mSegments[jj].Curve.Length;
               PreviousToolingSegment = mSegments[jj];
            }
         }
         mGCodeGen.DisableMachiningDirective ();
         mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
      }
      mGCodeGen.FinalizeNotchToolingBlock (mToolingItem, mBlockCutLength, mTotalToolingsCutLength);
      Exit = mSegments[^1];
   }

   /// <summary>
   /// This method writes the G Code for the feature, comprehensively.
   /// </summary>
   public override void WriteTooling () {
      if (EdgeNotch) {
         mExitTooling = null;
         return;
      }
      if (mShortPerimeterNotch) {
         WriteShortPerimeterNotch ();
         return;
      }
      var segs = mSegments;
      var (n1, nMid1, flangeEnd, n2, nMid2, notchPointAtApproachpc) = GetNotchApproachPositions (mToolingItem, segs, mNotchAttrs,
      mFullPartBound, mGCodeGen.JobInnerRadius, mGCodeGen.JobThickness, mApproachIndex, mNotchWireJointDistance,
      sameSidedExitNotch: mTwoFlangeNotchStartAndEndOnSameSideFlange, mGCodeGen.PartConfigType);
      var notchAttr = mNotchAttrs[mApproachIndex];
      var notchApproachEndNormal = notchAttr.EndNormal;
      var notchApproachStNormal = notchAttr.StNormal;
      mBlockCutLength = mCutLengthTillPrevTooling;
      //Point3 prevTSPt = flangeEnd;
      bool continueMachining = false;
      Point3? prevAbsToolPosition = null;
      mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
      for (int ii = 0; ii < mNotchSequences.Count; ii++) {
         var notchSequence = mNotchSequences[ii];
         switch (notchSequence.SectionType) {
            case NotchSectionType.ApproachMachining: {
                  continueMachining = false;
                  Utils.EPlane currPlaneType = Utils.GetFeatureNormalPlaneType (notchApproachEndNormal, new ());
                  List<Point3> pts = [];
                  pts.Add (nMid2); pts.Add (n2);
                  pts.Add (flangeEnd);
                  pts.Add (notchPointAtApproachpc);
                  var mTrace = mGCodeGen.mTraces[0];
                  bool isFromWebFlange = true;
                  if (Math.Abs (notchApproachEndNormal.Y) > Math.Abs (notchApproachEndNormal.Z))
                     isFromWebFlange = false;

                  // ** Reference Tooling Segment is the first one to machine from n1 to nMid1
                  var startTS = new ToolingSegment (new FCLine3 (n1, nMid1), notchApproachEndNormal, notchApproachEndNormal);
                  mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, pts, notchApproachStNormal,
                     mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mNotchSequences.Count - 1, startTS, nextTs: null,
                     isValidNotch: true,
                     "NotchSequence: Approach to the Tooling - First Sequence");

                  if (mGCodeGen.CreateDummyBlock4Master) {
                     mGCodeGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");
                     return;
                  }
                  {
                     var notchEntry = Tuple.Create (n1, notchAttr.StNormal);
                     mGCodeGen.PrepareforToolApproach (mToolingItem, mSegments, PreviousToolingSegment, PreviousTooling,
                        mPrevToolingSegments, mFirstTooling, isValidNotch: true, notchEntry);

                     mGCodeGen.RapidMoveToPiercingPosition (notchEntry.Item1, notchEntry.Item2, EKind.Notch, usePingPongOption: true);
                     prevAbsToolPosition = notchEntry.Item1;

                     mGCodeGen.WriteToolCorrectionData (mToolingItem);
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                     mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                     mGCodeGen.WriteToolDiaCompensation (isFlexTooling: false);


                     mGCodeGen.MoveToMachiningStartPosition (notchEntry.Item1, notchEntry.Item2, mToolingItem.Name);

                     mGCodeGen.EnableMachiningDirective ();
                     {
                        // *** Moving to the mid point wire joint distance ***
                        mGCodeGen.WriteLineSeg (n1, nMid1, notchApproachStNormal, notchApproachEndNormal, currPlaneType,
                           mPrevPlane, Utils.GetArcPlaneFlangeType (notchApproachEndNormal.Normalized (),
                           mGCodeGen.GetXForm ()),
                           mToolingItem.Name, relativeCoords: relCoords, refStPoint: prevAbsToolPosition);

                        mGCodeGen.WriteLineSeg (n2, flangeEnd, notchApproachStNormal,
                           notchApproachEndNormal, currPlaneType, mPrevPlane,
                           Utils.GetArcPlaneFlangeType (notchApproachEndNormal.Normalized (),
                           mGCodeGen.GetXForm ()), mToolingItem.Name, relativeCoords: relCoords, refStPoint: prevAbsToolPosition);

                        PreviousToolingSegment = new ((new FCLine3 (notchEntry.Item1, flangeEnd), notchApproachStNormal, notchApproachEndNormal));
                     }
                     mGCodeGen.DisableMachiningDirective ();
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutEndToken);

                     mBlockCutLength += n1.DistTo (nMid1);
                     mBlockCutLength += nMid1.DistTo (flangeEnd);
                  }
                  mGCodeGen.FinalizeNotchToolingBlock (mToolingItem, mBlockCutLength, mTotalToolingsCutLength);

                  // *** Retract and move to next machining start point n2
                  mGCodeGen.MoveToRetract (n1, notchApproachEndNormal, mToolingItem.Name);
                  mGCodeGen.MoveToMachiningStartPosition (n2, notchApproachStNormal, mToolingItem.Name);

                  pts.Clear ();
                  pts.Add (nMid1); pts.Add (n1); pts.Add (notchPointAtApproachpc);

                  pts.Add (n2);
                  pts.Add (nMid2);

                  // Next is approach on re-entry
                  notchPointAtApproachpc = mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End;
                  pts.Add (notchPointAtApproachpc);
                  pts.Add (n1); pts.Add (nMid1);

                  // Forward or reverse machining
                  if (mNotchSequences[ii + 1].SectionType == NotchSectionType.GambitPreApproachMachining) {
                     // forward
                     for (int jj = mNotchSequences[ii + 1].StartIndex; jj <= mNotchSequences[ii + 1].EndIndex; jj++) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                     for (int jj = mNotchSequences[ii + 2].StartIndex; jj <= mNotchSequences[ii + 2].EndIndex; jj++) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                  } else if (mNotchSequences[ii + 1].SectionType == NotchSectionType.GambitPostApproachMachining) {
                     // Reverse
                     for (int jj = mNotchSequences[ii + 1].StartIndex; jj >= mNotchSequences[ii + 1].EndIndex; jj--) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                     for (int jj = mNotchSequences[ii + 2].StartIndex; jj >= mNotchSequences[ii + 2].EndIndex; jj--) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                  }
                  isFromWebFlange = true;
                  if (Math.Abs (notchApproachEndNormal.Y) > Math.Abs (notchApproachEndNormal.Z))
                     isFromWebFlange = false;

                  // Reference Tooling Segment is the first one to machine from n2 to nMid2
                  startTS = new ToolingSegment (new FCLine3 (n2, nMid2), notchApproachEndNormal, notchApproachEndNormal);
                  mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, pts, notchApproachStNormal,
                     mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mNotchSequences.Count - 1, startTS, nextTs: null,
                     isValidNotch: true,
                     "NotchSequence: Approach to the Tooling : 2nd Sequence");
                  mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
                  {
                     mGCodeGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");
                     if (mGCodeGen.CreateDummyBlock4Master) return;
                     mGCodeGen.RapidMoveToPiercingPosition (n2, notchApproachStNormal, EKind.Notch, usePingPongOption: true);
                     prevAbsToolPosition = n2;

                     mGCodeGen.WriteToolCorrectionData (mToolingItem);
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                     mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                     mGCodeGen.WriteToolDiaCompensation (isFlexTooling: false);

                     mGCodeGen.EnableMachiningDirective ();
                     {
                        // *** Start machining from n2 -> nMid2 -> 50% dist end point ***
                        mGCodeGen.WriteLineSeg (n2, nMid2, notchApproachStNormal, notchApproachEndNormal, currPlaneType,
                           mPrevPlane, Utils.GetArcPlaneFlangeType (notchApproachEndNormal.Normalized (),
                           mGCodeGen.GetXForm ()),
                           mToolingItem.Name, relativeCoords: relCoords, refStPoint: prevAbsToolPosition);

                        // @Notchpoint 50
                        mGCodeGen.WriteLineSeg (mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.Start,
                           mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End, notchApproachStNormal,
                           notchApproachEndNormal, currPlaneType, mPrevPlane,
                           Utils.GetArcPlaneFlangeType (notchApproachEndNormal.Normalized (),
                           mGCodeGen.GetXForm ()), mToolingItem.Name, relativeCoords: relCoords, refStPoint: prevAbsToolPosition);

                        PreviousToolingSegment = new ((new FCLine3 (nMid2, mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End),
                           notchApproachStNormal, notchApproachStNormal));
                     }

                     mBlockCutLength += n2.DistTo (nMid2);
                     mBlockCutLength += nMid2.DistTo (mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End);
                  }

                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
                  continueMachining = true;
                  mFirstTooling = false;
               }
               break;
            case NotchSectionType.ApproachOnReEntry: {
                  bool isFromWebFlange = true;
                  if (Math.Abs (notchApproachEndNormal.Y) > Math.Abs (notchApproachEndNormal.Z))
                     isFromWebFlange = false;
                  mGCodeGen.WriteLineStatement (GCodeGenerator.GetGCodeComment ("NotchSequence: Approaching notch profile after Re-Entry"));
                  Utils.EPlane currPlaneType = Utils.GetFeatureNormalPlaneType (notchApproachEndNormal, new ());

                  // @Notchpoint at approach
                  notchPointAtApproachpc = mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End;

                  List<Point3> pts = [];
                  pts.Add (notchPointAtApproachpc); pts.Add (mRecentToolPosition);
                  pts.Add (n1); pts.Add (nMid1);

                  {
                     if (mNotchSequences[ii - 1].SectionType == NotchSectionType.MoveToMidApproach) {
                        mGCodeGen.WriteToolCorrectionData (mToolingItem);
                        mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                        mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        mGCodeGen.WriteToolDiaCompensation (isFlexTooling: false);
                        mGCodeGen.EnableMachiningDirective ();
                     }
                     {
                        mGCodeGen.WriteLineSeg (mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.Start,
                           mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End, notchApproachStNormal,
                           notchApproachEndNormal, currPlaneType, mPrevPlane,
                           Utils.GetArcPlaneFlangeType (notchApproachEndNormal.Normalized (),
                           mGCodeGen.GetXForm ()), mToolingItem.Name, relativeCoords: true, refStPoint: prevAbsToolPosition);

                        PreviousToolingSegment = new ((new FCLine3 (PreviousToolingSegment.Value.Curve.End,
                           mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End), notchApproachStNormal, notchApproachEndNormal));
                     }

                     mBlockCutLength += mRecentToolPosition.DistTo (mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End);
                  }

                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
               }
               break;
            case NotchSectionType.GambitPostApproachMachining:
            case NotchSectionType.GambitPreApproachMachining: {
                  string titleComment;
                  if (notchSequence.SectionType == NotchSectionType.GambitPostApproachMachining)
                     titleComment = GCodeGenerator.GetGCodeComment ("NotchSequence: Machining Gambit Forward");
                  else
                     titleComment = GCodeGenerator.GetGCodeComment ("NotchSequence: Machining Gambit Reverse");
                  mGCodeGen.WriteLineStatement (titleComment);
                  {
                     ToolingSegment segment = mSegments[notchSequence.StartIndex];
                     ToolingSegment revSegment = Geom.GetReversedToolingSegment (mSegments[notchSequence.StartIndex], tolerance: mSplit ? 1e-4 : 1e-6);
                     if (notchSequence.SectionType == NotchSectionType.GambitPostApproachMachining) {
                        mGCodeGen.WriteCurve (segment, mToolingItem.Name, relativeCoords: relCoords, refStPt: prevAbsToolPosition);
                        mGCodeGen.WriteCurve (revSegment, mToolingItem.Name, relativeCoords: relCoords, refStPt: prevAbsToolPosition);
                        PreviousToolingSegment = revSegment;
                     } else {
                        mGCodeGen.WriteCurve (revSegment, mToolingItem.Name, relativeCoords: relCoords, refStPt: prevAbsToolPosition);
                        PreviousToolingSegment = revSegment;
                     }

                     mBlockCutLength += 2 * segment.Curve.Length;
                  }
                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
               }
               break;
            case NotchSectionType.WireJointTraceJumpForward:
            case NotchSectionType.WireJointTraceJumpReverse:
            case NotchSectionType.WireJointTraceJumpReverseOnFlex:
            case NotchSectionType.WireJointTraceJumpForwardOnFlex:

               //var blockNoMark = mGCodeGen.BlockNumberMark;
               if (notchSequence.SectionType == NotchSectionType.WireJointTraceJumpForwardOnFlex || notchSequence.SectionType == NotchSectionType.WireJointTraceJumpReverseOnFlex)
                  notchAttr = ComputeNotchAttribute (mFullPartBound, mToolingItem, mSegments, notchSequence.StartIndex,
                  mSegments[notchSequence.StartIndex].Curve.End, mGCodeGen.JobInnerRadius, mGCodeGen.JobThickness, mGCodeGen.PartConfigType, isFlexMachining: true);
               else
                  notchAttr = ComputeNotchAttribute (mFullPartBound, mToolingItem, mSegments, notchSequence.StartIndex,
                  mSegments[notchSequence.StartIndex].Curve.Start, mGCodeGen.JobInnerRadius, mGCodeGen.JobThickness, mGCodeGen.PartConfigType, isFlexMachining: false);

               Vector3 scrapSideNormal;
               if (Math.Abs (mSegments[notchSequence.StartIndex].Vec0.Normalized ().Z - 1.0).EQ (0, mSplit ? 1e-4 : 1e-6) ||
                  Math.Abs (-mSegments[notchSequence.StartIndex].Vec0.Normalized ().Y + 1.0).EQ (0, mSplit ? 1e-4 : 1e-6) ||
                  Math.Abs (mSegments[notchSequence.StartIndex].Vec0.Normalized ().Y - 1.0).EQ (0, mSplit ? 1e-4 : 1e-6))
                  scrapSideNormal = notchAttr.OFlangeNormal;
               else
                  scrapSideNormal = notchAttr.NearestBdyVec;

               if (mTwoFlangeNotchStartAndEndOnSameSideFlange)
                  scrapSideNormal = notchAttr.ScrapSideDir;

               string comment = GCodeGenerator.GetGCodeComment ("** Notch: Wire Joint Jump Trace Forward Direction ** ");
               var wjtTS = mSegments[notchSequence.StartIndex];
               if (notchSequence.SectionType == NotchSectionType.WireJointTraceJumpReverse ||
                  notchSequence.SectionType == NotchSectionType.WireJointTraceJumpReverseOnFlex) {
                  wjtTS = Geom.GetReversedToolingSegment (wjtTS);
                  comment = GCodeGenerator.GetGCodeComment ("** Notch: Wire Joint Jump Trace Reverse Direction ** ");
               }
               bool isNextSeqFlexMc = (mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineFlexToolingReverse ||
                  mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineFlexToolingForward);
               bool isPrevSeqFlexMc = (mNotchSequences[ii - 1].SectionType == NotchSectionType.MachineFlexToolingReverse ||
                  mNotchSequences[ii - 1].SectionType == NotchSectionType.MachineFlexToolingForward);

               EFlange flangeType = Utils.GetArcPlaneFlangeType (wjtTS.Vec1, mGCodeGen.GetXForm ());

               //bool nextBeginFlexMachining = false;
               //if (ii + 1 < mNotchSequences.Count) {
               //   if (mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineFlexToolingReverse ||
               //      mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineFlexToolingForward)
               //      nextBeginFlexMachining = true;
               //}

               // As per the requirement, the wire joint trace has to be written two times for 2 head laser
               // cutting machine. 
               // Once as a separate tool block, the second time as a continuous tool block with (next) flex
               // machining
               mFlexStartRef = Utils.GetMachiningSegmentPostWJT (wjtTS, scrapSideNormal, mGCodeGen.Process.Workpiece.Bound, NotchApproachLength);

               // FCH-43, Always create seperate tool blocks, once for wirejoint, and the other for the wirejoint that
               // continues with the machining, ( flex or forward/reverse)
               //nextBeginFlexMachining = true;
               /*if (nextBeginFlexMachining) */
               string comment1 = comment;
               if (isNextSeqFlexMc)
                  comment1 = comment + " : WJT trace before Flex Cut";
               else if (isPrevSeqFlexMc)
                  comment1 = comment + " : WJT trace after Flex Cut";
               //else {
               //   if (mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineToolingForward ||
               //   mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineToolingReverse)
               //      comment1 = comment + " : Seperate tooling block for WJT trace after Flex Cut";
               //}

               mGCodeGen.WriteWireJointTrace (wjtTS, nextSeg: null, scrapSideNormal,
                        mRecentToolPosition, NotchApproachLength, ref mPrevPlane, flangeType, mToolingItem,
                        ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                         isFlexCut: false,
                         isValidNotch: true,
                         flexRefTS: mFlexStartRef,
                         out prevAbsToolPosition,
                         toCompleteToolingBlock: isNextSeqFlexMc || isPrevSeqFlexMc,
                         comment1, relativeCoords: relCoords,
                         firstWJTTrace: true);
               PreviousToolingSegment = new (mFlexStartRef.Value.Curve, PreviousToolingSegment.Value.Vec1, mFlexStartRef.Value.Vec0);
               mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;

               //comment1 = comment + " : WJT trace as first part of Flex Cut";

               //else {
               //   if (mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineToolingForward ||
               //   mNotchSequences[ii + 1].SectionType == NotchSectionType.MachineToolingReverse)
               //      comment1 = comment + " : Seperate tooling block for WJT trace as first part of upcoming machining";
               //}

               if (isNextSeqFlexMc || isPrevSeqFlexMc) {
                  if (isNextSeqFlexMc)
                     comment1 = comment + " : Seperate second tooling block for WJT as first part of upcoming flex cut";
                  else
                     comment1 = comment + " : Seperate second tooling block for WJT as first part of previous flex cut";

                  mGCodeGen.WriteWireJointTrace (wjtTS, nextSeg: ToolingSegments[mNotchSequences[ii + 1].StartIndex], scrapSideNormal,
                          mRecentToolPosition, NotchApproachLength, ref mPrevPlane, flangeType, mToolingItem,
                          ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                          isFlexCut: true,
                          isValidNotch: true,
                          flexRefTS: mFlexStartRef,
                          out prevAbsToolPosition,
                          toCompleteToolingBlock: false,
                          comment1,
                          relativeCoords: relCoords,
                          firstWJTTrace: false);

                  PreviousToolingSegment = new (mFlexStartRef.Value.Curve, PreviousToolingSegment.Value.Vec1, mFlexStartRef.Value.Vec0);
                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
                  //comment1 = comment + " : Seperate second tooling block for WJT as first part of upcoming planar cut";
                  //mGCodeGen.WriteWireJointTrace (wjtTS, nextSeg: null, scrapSideNormal,
                  //  mRecentToolPosition, NotchApproachLength, ref mPrevPlane, flangeType, mToolingItem,
                  //  ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
                  //  isFlexCut: false,
                  //  isValidNotch: true,
                  //  flexRefTS: mFlexStartRef,
                  //  out prevAbsToolPosition,
                  //  toCompleteToolingBlock: false,
                  //  comment1,
                  //  relativeCoords: relCoords,
                  //  firstWJTTrace: false);
               }

               //else
               //   mGCodeGen.WriteWireJointTrace (wjtTS, scrapSideNormal,
               //         mRecentToolPosition, NotchApproachLength, ref mPrevPlane, flangeType, mToolingItem,
               //         ref mBlockCutLength, mTotalToolingsCutLength, mXStart, mXPartition, mXEnd,
               //         isFlexCut: false,
               //         isValidNotch: true,
               //         flexRefTS: null,
               //         out prevAbsToolPosition,
               //         toCompleteToolingBlock: false,
               //         comment, relativeCoords: relCoords);

               //if (isNextSeqFlexMc || isPrevSeqFlexMc)
               //   continueMachining = true;
               //else
               //   continueMachining = false;
               continueMachining = true;

               break;
            case NotchSectionType.MachineToolingForward: {
                  if (notchSequence.StartIndex > notchSequence.EndIndex)
                     throw new Exception ("In WriteNotch: MachineToolingForward : startIndex > endIndex");
                  if (!continueMachining) {
                     mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, mSegments,
                        mSegments[notchSequence.StartIndex].Vec0, mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mNotchSequences.Count - 1,
                        isValidNotch: true, notchSequence.StartIndex, notchSequence.EndIndex,
                        comment: "NotchSequence: Machining Forward Direction");
                  } else {
                     string titleComment = GCodeGenerator.GetGCodeComment ("NotchSequence: Machining Forward Direction");
                     mGCodeGen.WriteLineStatement (titleComment);
                  }
                  {
                     if (!continueMachining) {
                        mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
                        mGCodeGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");
                        mGCodeGen.RapidMoveToPiercingPosition (mSegments[notchSequence.StartIndex].Curve.Start,
                           mSegments[notchSequence.StartIndex].Vec0, EKind.Notch, usePingPongOption: true);
                        prevAbsToolPosition = mSegments[notchSequence.StartIndex].Curve.Start;

                        var isFromWebFlange = Utils.IsMachiningFromWebFlange (mSegments, notchSequence.StartIndex);

                        mGCodeGen.WriteToolCorrectionData (mToolingItem);
                        mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                        mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                        mGCodeGen.WriteToolDiaCompensation (isFlexTooling: false);
                        mGCodeGen.EnableMachiningDirective ();
                     }

                     for (int jj = notchSequence.StartIndex; jj <= notchSequence.EndIndex; jj++) {
                        mExitTooling = mSegments[jj];
                        //if (jj == notchSequence.StartIndex)
                        mGCodeGen.WriteCurve (mSegments[jj], mToolingItem.Name, relativeCoords: relCoords,
                           refStPt: prevAbsToolPosition);

                        //prevTSPt = mSegments[jj].Curve.End;
                        mBlockCutLength += mSegments[jj].Curve.Length;
                     }
                     PreviousToolingSegment = mSegments[notchSequence.EndIndex];

                     mGCodeGen.DisableMachiningDirective ();
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutEndToken);
                     mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
                  }
                  mGCodeGen.FinalizeNotchToolingBlock (mToolingItem, mBlockCutLength, mTotalToolingsCutLength);
                  continueMachining = false;
               }
               break;
            case NotchSectionType.MachineToolingReverse: {
                  if (notchSequence.StartIndex < notchSequence.EndIndex)
                     throw new Exception ("In WriteNotch: MachineToolingReverse : startIndex < endIndex");
                  if (!continueMachining) {
                     mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, mSegments, mSegments[notchSequence.StartIndex].Vec0,
                        mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mNotchSequences.Count - 1, /*isToBeTreatedAsCutOut:false,*/
                        isValidNotch: true, notchSequence.StartIndex, notchSequence.EndIndex, comment: "NotchSequence: Machining Reverse Direction");
                  } else {
                     string titleComment = GCodeGenerator.GetGCodeComment ("NotchSequence: Machining Reverse Direction");
                     mGCodeGen.WriteLineStatement (titleComment);
                  }

                  if (!continueMachining) {
                     mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
                     mGCodeGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");
                     mGCodeGen.RapidMoveToPiercingPosition (mSegments[notchSequence.StartIndex].Curve.End,
                        mSegments[notchSequence.StartIndex].Vec1, EKind.Notch, usePingPongOption: true);
                     prevAbsToolPosition = mSegments[notchSequence.StartIndex].Curve.End;

                     var isFromWebFlange = Utils.IsMachiningFromWebFlange (mSegments, notchSequence.StartIndex);

                     mGCodeGen.WriteToolCorrectionData (mToolingItem);
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                     mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                     mGCodeGen.WriteToolDiaCompensation (isFlexTooling: false);

                     mGCodeGen.EnableMachiningDirective ();
                  }

                  for (int jj = notchSequence.StartIndex; jj >= notchSequence.EndIndex; jj--) {
                     mExitTooling = Geom.GetReversedToolingSegment (mSegments[jj], tolerance: mSplit ? 1e-4 : 1e-6);
                     mGCodeGen.WriteCurve (mExitTooling.Value, mToolingItem.Name, relativeCoords: relCoords,
                        refStPt: prevAbsToolPosition);

                     //prevTSPt = mExitTooling.Value.Curve.End;
                     mBlockCutLength += mExitTooling.Value.Curve.Length;
                  }
                  PreviousToolingSegment = mSegments[notchSequence.EndIndex];

                  mGCodeGen.DisableMachiningDirective ();
                  mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutEndToken);
                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;

                  mGCodeGen.FinalizeNotchToolingBlock (mToolingItem, mBlockCutLength, mTotalToolingsCutLength);
               }
               continueMachining = false;
               break;
            case NotchSectionType.MachineFlexToolingReverse: {
                  if (notchSequence.StartIndex < notchSequence.EndIndex)
                     throw new Exception ("In WriteNotchGCode: MachineFlexToolingReverse : startIndex < endIndex");

                  if (!continueMachining) {
                     mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
                     mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, mSegments,
                        mSegments[notchSequence.StartIndex].Vec0, mXStart, mXPartition, mXEnd, isFlexCut: true,
                        ii == mNotchSequences.Count - 1, isValidNotch: true, notchSequence.StartIndex, notchSequence.EndIndex,
                        refSegIndex: notchSequence.StartIndex,
                        "NotchSequence: Flex machining Reverse Direction");
                     var isFromWebFlange = Utils.IsMachiningFromWebFlange (mSegments, notchSequence.StartIndex);
                     mGCodeGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");
                     mGCodeGen.RapidMoveToPiercingPosition (mSegments[notchSequence.StartIndex].Curve.End,
                        mSegments[notchSequence.StartIndex].Vec1, EKind.Notch, usePingPongOption: true);
                     prevAbsToolPosition = mSegments[notchSequence.StartIndex].Curve.End;

                     mGCodeGen.WriteToolCorrectionData (mToolingItem);
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                     mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                     mGCodeGen.WriteToolDiaCompensation (isFlexTooling: true);

                     mGCodeGen.EnableMachiningDirective ();
                  }

                  mGCodeGen.WriteLineStatement (GCodeGenerator.GetGCodeComment ("NotchSequence: Machining in Flex in Reverse Direction"));
                  for (int jj = notchSequence.StartIndex; jj >= notchSequence.EndIndex; jj--) {
                     var segment = Geom.GetReversedToolingSegment (mSegments[jj], tolerance: mSplit ? 1e-4 : 1e-6);
                     mGCodeGen.WriteFlexLineSeg (segment,
                       isWJTStartCut: false, mToolingItem.Name, flexRefSeg: mFlexStartRef);
                     //prevTSPt = segment.Curve.End;
                     mBlockCutLength += segment.Curve.Length;
                     PreviousToolingSegment = segment;
                  }
                  mFlexStartRef = null;
                  mGCodeGen.DisableMachiningDirective ();
                  mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutEndToken);
                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
                  mGCodeGen.FinalizeNotchToolingBlock (mToolingItem, mBlockCutLength, mTotalToolingsCutLength);
               }
               continueMachining = false;
               break;
            case NotchSectionType.MachineFlexToolingForward: {
                  if (notchSequence.StartIndex > notchSequence.EndIndex)
                     throw new Exception ("In WriteNotch: MachineFlexToolingForward : startIndex > endIndex");

                  if (!continueMachining) {
                     mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
                     mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, mSegments,
                        mSegments[notchSequence.StartIndex].Vec0, mXStart, mXPartition, mXEnd, isFlexCut: true,
                        ii == mNotchSequences.Count - 1, isValidNotch: true, notchSequence.StartIndex, notchSequence.EndIndex,
                        refSegIndex: notchSequence.StartIndex, "NotchSequence: Flex machining Forward Direction");

                     var isFromWebFlange = Utils.IsMachiningFromWebFlange (mSegments, notchSequence.StartIndex);
                     mGCodeGen.WriteLineStatement ("ToolPlane\t( Confirm Cutting Plane )");
                     mGCodeGen.RapidMoveToPiercingPosition (mSegments[notchSequence.StartIndex].Curve.End,
                        mSegments[notchSequence.StartIndex].Vec1, EKind.Notch, usePingPongOption: true);
                     prevAbsToolPosition = mSegments[notchSequence.StartIndex].Curve.End;

                     mGCodeGen.WriteToolCorrectionData (mToolingItem);
                     mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutStartToken);
                     mGCodeGen.WritePlaneForCircularMotionCommand (isFromWebFlange, isNotchCut: true);
                     mGCodeGen.WriteToolDiaCompensation (isFlexTooling: true);
                     mGCodeGen.EnableMachiningDirective ();
                  }

                  mGCodeGen.WriteLineStatement (GCodeGenerator.GetGCodeComment ("NotchSequence: Machining in Flex in Forward Direction"));
                  for (int jj = notchSequence.StartIndex; jj <= notchSequence.EndIndex; jj++) {

                     mGCodeGen.WriteFlexLineSeg (mSegments[jj],
                        isWJTStartCut: false, mToolingItem.Name, flexRefSeg: mFlexStartRef);
                     //prevTSPt = mSegments[jj].Curve.End;

                     mBlockCutLength += mSegments[jj].Curve.Length;
                     PreviousToolingSegment = mSegments[jj];
                  }
                  mFlexStartRef = null;

                  // The next in sequence has to be wire joint jump trace and so
                  // continueMachining is made to false
                  mGCodeGen.DisableMachiningDirective ();
                  mGCodeGen.WriteLineStatement (mGCodeGen.NotchCutEndToken);
                  mRecentToolPosition = mGCodeGen.GetLastToolHeadPosition ().Item1;
                  mGCodeGen.FinalizeNotchToolingBlock (mToolingItem, mBlockCutLength, mTotalToolingsCutLength);
               }
               continueMachining = false;
               break;
            case NotchSectionType.MoveToMidApproach: {
                  string titleComment = GCodeGenerator.GetGCodeComment ("NotchSequence: Rapid Move from one end of the notch tooling to the mid approach");

                  Point3 prevEndPoint = mExitTooling.Value.Curve.End;
                  Vector3 PrevEndNormal = mExitTooling.Value.Vec1.Normalized ();
                  continueMachining = true;
                  List<Point3> pts = []; pts.Add (prevEndPoint);
                  pts.Add (n2);
                  pts.Add (nMid2);

                  // Next is approach on re-entry
                  notchPointAtApproachpc = mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End;
                  pts.Add (notchPointAtApproachpc);
                  pts.Add (n1); pts.Add (nMid1);

                  // Forward or backward machining
                  if (mNotchSequences[ii + 2].SectionType == NotchSectionType.GambitPreApproachMachining) {
                     // forward
                     for (int jj = mNotchSequences[ii + 2].StartIndex; jj <= mNotchSequences[ii + 2].EndIndex; jj++) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                     for (int jj = mNotchSequences[ii + 3].StartIndex; jj <= mNotchSequences[ii + 3].EndIndex; jj++) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                  } else if (mNotchSequences[ii + 2].SectionType == NotchSectionType.GambitPostApproachMachining) {
                     // Reverse
                     for (int jj = mNotchSequences[ii + 2].StartIndex; jj >= mNotchSequences[ii + 2].EndIndex; jj--) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                     for (int jj = mNotchSequences[ii + 3].StartIndex; jj >= mNotchSequences[ii + 3].EndIndex; jj--) {
                        pts.Add (mSegments[jj].Curve.Start);
                        pts.Add (mSegments[jj].Curve.End);
                     }
                  }

                  var refTS = new ToolingSegment (new FCLine3 (nMid2, mSegments[mNotchIndices.segIndexAtWJTApproach].Curve.End), notchApproachEndNormal, notchApproachEndNormal);
                  titleComment = titleComment + "\n" + GCodeGenerator.GetGCodeComment ("Notch: Move to Mid2 towards machining again the rest of the tooling segment");
                  mGCodeGen.InitializeNotchToolingBlock (mToolingItem, prevToolingItem: null, pts, notchApproachStNormal,
                     mXStart, mXPartition, mXEnd, isFlexCut: false, ii == mNotchSequences.Count - 1, refTS, nextTs: null,
                     isValidNotch: true,
                     titleComment);

                  mGCodeGen.MoveToRetract (prevEndPoint, PrevEndNormal, mToolingItem.Name);
                  mGCodeGen.RapidMoveToPiercingPositionWithPingPong = false;
                  mGCodeGen.MoveToNextTooling (PrevEndNormal, mExitTooling, nMid2, notchApproachStNormal,
                     "Moving from one end of tooling to mid of tooling",
                     "", false, EKind.Notch, usePingPongOption: true);

                  if (!mGCodeGen.RapidMoveToPiercingPositionWithPingPong)
                     mGCodeGen.RapidMoveToPiercingPosition (nMid2, notchApproachStNormal, EKind.Notch, usePingPongOption: true);
                  prevAbsToolPosition = nMid2;
                  mGCodeGen.MoveToMachiningStartPosition (nMid2, notchApproachStNormal, mToolingItem.Name);
                  mRecentToolPosition = nMid2;
                  continueMachining = true;
               }
               break;
            default:
               throw new Exception ("Undefined notch sequence");
         }
      }
   }
   #endregion

   #region Getters / Predicates
   /// <summary>
   /// This method computes a list of tuples representing the start and end indices of the tooling
   /// segments that occur on the Flex.
   /// </summary>
   /// <param name="segs">The input list of tooling segments.</param>
   /// <returns>A list of tuples, where each tuple contains the start and end indices of the tooling
   /// segments that occur on the Flex. The method assumes that there are two flex toolings on the notch tooling.</returns>
   public static List<Tuple<int, int>> GetFlexSegmentIndices (List<ToolingSegment> segs) {
      // Find the flex segment indices
      List<Tuple<int, int>> flexIndices = [];
      int flexStartIndex = -1, flexEndIndex;
      for (int ii = 0; ii < segs.Count; ii++) {
         var (_, stNormal, endNormal) = segs[ii];
         if (Utils.IsToolingOnFlex (stNormal, endNormal)) {
            if (flexStartIndex == -1) flexStartIndex = ii;
         } else if (flexStartIndex != -1) {
            flexEndIndex = ii - 1;
            var indxes = new Tuple<int, int> (flexStartIndex, flexEndIndex);
            flexIndices.Add (indxes);
            flexStartIndex = -1;
         }
      }
      if (flexStartIndex != -1) {
         flexEndIndex = segs.Count - 1;
         var indxes = new Tuple<int, int> (flexStartIndex, flexEndIndex);
         flexIndices.Add (indxes);
      }
      return flexIndices;
   }

   /// <summary>
   /// This method is used to find of the notch occurs only on the endge of the 
   /// part. This case is mostly for testing purpose.
   /// </summary>
   /// <param name="bound">The bound3d of the entire part or the toling of the notch 
   /// based on the need</param>
   /// <param name="toolingItem">The tooling item</param>
   /// <param name="percentPos">The positions of the points occuring in the interested order</param>
   /// <param name="notchApproachLength">The notch approach length</param>
   /// <param name="leastCurveLength">The practical least length of the curve that can 
   /// <returns>True if the notch happens on one of the boundary edges, false otherwise</returns>
   /// </param>
   public static bool IsEdgeNotch (Bound3 bound, Tooling toolingItem,
      double[] percentPos,
   double leastCurveLength,
   double radius,
   double thickness,
   double notchApproachDist,
   MCSettings.PartConfigType partConfigType) {
      var ti = toolingItem.Clone ();
      var attrs = GetNotchApproachParams (bound, toolingItem, percentPos,
         leastCurveLength, radius, thickness, partConfigType);
      if (toolingItem.IsNotch () && attrs.Count == 0) return true;

      // If a notch should have start and endx at XMin, or XMax or ZMin
      // it is not an edge notch
      if ((ti.Segs[0].Curve.Start.X.EQ (bound.XMax, 2 * notchApproachDist) || ti.Segs[0].Curve.Start.X.EQ (bound.XMin, 2 * notchApproachDist) ||
         ti.Segs[0].Curve.Start.Z.EQ (bound.ZMin, 2 * notchApproachDist)) &&
         (ti.Segs[^1].Curve.End.X.EQ (bound.XMax, 2 * notchApproachDist) || ti.Segs[^1].Curve.End.X.EQ (bound.XMin, 2 * notchApproachDist) ||
         ti.Segs[^1].Curve.End.Z.EQ (bound.ZMin, 2 * notchApproachDist)))
         return false;
      else
         return true;
   }

   /// <summary>
   /// A predicate method that returns if the given "notchPoint" is within the 
   /// flex section of tooling, considering a minimum thershold "minThresholdLenFromNPToFlexPt"
   /// outside of flex also as inside
   /// </summary>
   /// <param name="flexIndices">The list of flexe indices where each item is a tuple 
   /// of start and end index in the tooling segments</param>
   /// <param name="segs">The input tooling segments</param>
   /// <param name="notchPoint">The input notch point</param>
   /// <param name="minThresholdLenFromNPToFlexPt">The minimum threshold distance of the 
   /// notch point from the nearest flex start/end point, even if outside, is considered
   /// to be inside.</param>
   /// <returns>A tuple of bool: if the notch point is within the flex, 
   /// Start Index and End Index</returns>
   (bool IsWithinAnyFlex, int StartIndex, int EndIndex) IsPointWithinFlex (List<Tuple<int, int>> flexIndices,
      List<ToolingSegment> segs, Point3 notchPoint, int segIndex, double minThresholdLenFromNPToFlexPt) {
      bool isWithinAnyFlex = false;
      int stIndex = -1, endIndex = -1;
      foreach (var flexIdx in flexIndices) {
         if (segIndex == flexIdx.Item1)
            throw new Exception ("In Notch.IsPointWithinFlex () , the notch point index can not be equal to flex point indices");
         if ((flexIdx.Item1 < segIndex && segIndex < flexIdx.Item2) ||
            (flexIdx.Item2 < segIndex && segIndex < flexIdx.Item1)) {
            isWithinAnyFlex = true;
            stIndex = flexIdx.Item1; endIndex = flexIdx.Item2;
            return new (isWithinAnyFlex, stIndex, endIndex);
         }
      }
      foreach (var flexIdx in flexIndices) {
         //var flexToolingLen = Geom.GetLengthBetween (segs, flexIdx.Item1, flexIdx.Item2);
         var lenNPToFlexStPt = Geom.GetLengthBetween (segs, notchPoint, segs[flexIdx.Item1].Curve.Start);
         var lenNPToFlexEndPt = Geom.GetLengthBetween (segs, notchPoint, segs[flexIdx.Item2].Curve.End);
         //var residue = lenNPToFlexStPt + lenNPToFlexEndPt - flexToolingLen;
         if (lenNPToFlexStPt < minThresholdLenFromNPToFlexPt || lenNPToFlexEndPt < minThresholdLenFromNPToFlexPt /*|| Math.Abs (residue).EQ (0, 1e-2)*/
            ) {
            isWithinAnyFlex = true;
            //stIndex = flexIdx.Item1; endIndex = flexIdx.Item2;
            return new (isWithinAnyFlex, stIndex, endIndex);
         }
      }
      return new (isWithinAnyFlex, stIndex, endIndex);
   }

   /// <summary>
   /// This method computes the total machinable length of a notch with approach.
   /// Note: A notch with approach is that notch that does not occur on the part's 
   /// edge.
   /// </summary>
   /// <param name="bound">The bound3d of the entire part or the toling of the notch 
   /// based on the need</param>
   /// <param name="toolingItem">The notch tooling item</param>
   /// <param name="percentPos">The array of percentages at which notch points are desired</param>
   /// <param name="notchWireJointDistance">The gap that is intended to make the sheet metal hold up
   /// even after the cut, which shall require a little physical force to break away the scrap side</param>
   /// <param name="notchApproachLength">The length of the laser cutting line length that is desired to
   /// tread before the tooling segment is reached to cut</param>
   /// <param name="leastCurveLength">The least length of the curve (0.5 ideally) below which it is 
   /// assumed that there is no curve</param>
   /// <returns>The overall length of the cut (this includes tooling and other cutting strokes for approach etc.)</returns>
   public static double GetTotalNotchToolingLength (Bound3 bound, Tooling toolingItem,
      double[] percentPos, double notchWireJointDistance, double notchApproachLength, double leastCurveLength, bool isWireJointCutsNeeded,
double radius, double thickness, MCSettings.PartConfigType partConfigType) {
      var attrs = GetNotchApproachParams (bound, toolingItem, percentPos,
      leastCurveLength, radius, thickness, partConfigType);

      double totalMachiningLength = 0;
      if (attrs.Count == 0) {
         totalMachiningLength = toolingItem.Segs.Sum (t => t.Curve.Length);
         return totalMachiningLength;
      }

      // Computation of total machining length
      int appIndex = 1;
      if (!isWireJointCutsNeeded) appIndex = 0;
      var outwardVec = attrs[appIndex].Item3;
      var outwardVecDir = outwardVec.Normalized ();

      // For gambit move from @50
      totalMachiningLength += 2 * 2; // Two times 2.0 length

      // For notch approach dist 
      int notchApproachDistCount = 0;

      // For notch approach cut ( entry)
      notchApproachDistCount += 2;
      int wireJointDistCount = 0;

      // For flexes: Subtract wirejointDist count one for each flex if wireJointDist > 0.5
      // Each wire joint trace at flex has one notchApproachDistCount added
      var segs = toolingItem.Segs.ToList ();
      var flexIndices = GetFlexSegmentIndices (segs);
      if (flexIndices.Count > 0) {
         if (notchWireJointDistance > 0.5) wireJointDistCount -= 2;
         notchApproachDistCount += 2;
         if (flexIndices.Count > 1) {
            notchApproachDistCount += 2;
            if (notchWireJointDistance > 0.5) wireJointDistCount -= 2;
         }
      }

      // Assuming that 25% and 75% cut length wire joint traces
      // exist ( wireJointDistance is not zero) and are outside 
      // the flexes.
      wireJointDistCount -= 2;
      notchApproachDistCount += 2;

      // Account for totalCutLength from above counts
      totalMachiningLength += (notchApproachDistCount * notchApproachLength);
      totalMachiningLength += (wireJointDistCount * notchWireJointDistance);

      // To account for notch approach
      Point3 notchPointAt50pc = attrs[appIndex].Item1;
      Point3 nMid1 = notchPointAt50pc + outwardVec * 0.5;
      Point3 nMid2 = notchWireJointDistance > 0.5 ? nMid1 - outwardVecDir * notchWireJointDistance : nMid1 - outwardVecDir * 2.0;

      // nMid1 to end of the part along the outward vector
      totalMachiningLength += nMid1.DistTo (notchPointAt50pc + outwardVec);

      // Two times tracing from nMid2 (inside) to the 50% lengthed segment's end, one for 
      // initial approach and the other for re-entry
      totalMachiningLength += 2 * nMid2.DistTo (attrs[appIndex].Item1);

      // Add the length of all the tooling segment of the notch
      foreach (var (crv, _, _) in segs) totalMachiningLength += crv.Length;
      return totalMachiningLength;
   }

   /// <summary>
   /// This is an utility method that creates from the indices of segments and notch points array to 
   /// NotchPointsInfo data strcuture to ascertain the uniqueness of the segment's index against a (only one)
   /// point after the input tooling segment is split.
   /// </summary>
   /// <param name="segIndices">An array of indices at which the notch points occur</param>
   /// <param name="notchPoints">The array of notch points</param>
   /// <param name="percentPos">The percentage as param of the total length array[0.25 0.5 0.75]</param>
   /// <returns>A list of the NotchPointsInfo that contains the unique set of index against
   /// the notch point</returns>
   public static List<NotchPointInfo> GetNotchPointsInfo (int[] segIndices, Point3[] notchPoints, double[] percentPos) {
      List<NotchPointInfo> notchPointsInfo = [];
      int count = percentPos.Length;
      for (int ii = 0; ii < count; ii++) {
         notchPointsInfo.FindIndex (np => np.mSegIndex == segIndices[ii]);

         // Find the notch point with the specified segIndex
         var index = notchPointsInfo.FindIndex (np => np.mSegIndex == segIndices[ii]);
         if (index != -1) notchPointsInfo[index].mPoints.Add (notchPoints[ii]);
         else {
            var atpc = 0.0;
            if (percentPos[ii].EQ (0.25) || percentPos[ii].EQ (0.125)) atpc = 0.25;
            else if (percentPos[ii].EQ (0.5)) atpc = 0.5;
            else if (percentPos[ii].EQ (0.75) || percentPos[ii].EQ (0.875)) atpc = 0.75;
            NotchPointInfo np = new (segIndices[ii], notchPoints[ii], atpc,
               atpc.EQ (0.25) ? "@25" : (atpc.EQ (0.5) ? "@50" : "@75"));
            notchPointsInfo.Add (np);
         }
      }
      return notchPointsInfo;
   }

   /// <summary>
   /// This method computes the vital notch parameters such as the points, normals and the
   /// direction to the nearest boundary at all the interested positions in the notch
   /// </summary>
   /// <param name="model">Model is used to get the bounds of the tooling</param>
   /// <param name="toolingItem">The input tooling item</param>
   /// <param name="percentPos">The positions of the interested points at lengths</param>
   /// <param name="notchApproachDistance">The approach distance for the notch</param>
   /// <param name="curveLeastLength">The least length of the curve, below which the curve data is 
   /// removed</param>
   /// <returns>A list of tuples that contain the notch point, normal at the point
   /// and the direction to the nearest boundary</returns>
   public static List<Tuple<Point3, Vector3, Vector3>> GetNotchApproachParams (Bound3 bound, Tooling toolingItem,
   double[] percentPos, double curveLeastLength, double radius, double thickness, MCSettings.PartConfigType partConfigType) {
      List<Tuple<Point3, Vector3, Vector3>> attrs = [];
      var segs = toolingItem.Segs.ToList ();
      if (!toolingItem.IsNotch ()) return attrs;
      Point3[] notchPoints;
      int[] segIndices;
      (segIndices, notchPoints) = ComputeNotchPointOccuranceParams (segs, percentPos, curveLeastLength);

      var notchPointsInfo = GetNotchPointsInfo (segIndices, notchPoints, percentPos);

      // Split the curves and modify the indices and segments in segments and
      // in notchPointsInfo
      SplitToolingSegmentsAtPoints (ref segs, ref notchPointsInfo, percentPos, segIndices,
         toolingItem.FeatType.Contains ("split", StringComparison.CurrentCultureIgnoreCase) ? 1e-4 : 1e-6);
      var notchAttrs = GetNotchAttributes (ref segs, ref notchPointsInfo, bound, toolingItem, radius, thickness, partConfigType);
      foreach (var notchAttr in notchAttrs) {
         var approachEndPoint = notchAttr.Curve.End;
         if (notchAttr.NearestBdyVec.Length > /*notchApproachDistance*/1.0 - Utils.EpsilonVal) { // TODO Revisit for notchApproachDistance
            var res = new Tuple<Point3, Vector3, Vector3> (approachEndPoint + notchAttr.NearestBdyVec,
               notchAttr.EndNormal, notchAttr.NearestBdyVec);
            attrs.Add (res);
         } else {
            attrs.Clear ();
            break;
         }
      }
      return attrs;
   }

   /// <summary>
   /// This method is used to obtain the direction in which the notch
   /// shall be machined upon the first entry. 
   /// </summary>
   /// <param name="segs">The input segments of the tooling</param>
   /// <returns>True if machining be in the forward direction. False otherwise.</returns>
   public static bool IsForwardFirstNotchTooling (List<ToolingSegment> segs) {
      bool forwardNotchTooling;
      if (segs[0].Curve.Start.X - mBound.XMin < mBound.XMax - segs[0].Curve.Start.X) {
         if (segs[^1].Curve.End.X < segs[0].Curve.Start.X) forwardNotchTooling = true;
         else forwardNotchTooling = false;
      } else {
         if (segs[^1].Curve.End.X > segs[0].Curve.Start.X) forwardNotchTooling = true;
         else forwardNotchTooling = false;
      }
      return forwardNotchTooling;
   }

   /// <summary>
   /// This method is used to compute all the notch attributes given the 
   /// the input List of NotchPointInfo. 
   /// Important note: Before calling this method, the input tooling segments are to be split
   /// at the occurance of the notch points. The expected output of this pre-step is to have a data strcuture 
   /// (NotchPOintInfo) where each one has the segmet index and only one point at that index. 
   /// The tooling segments are split in such a way that the notch points or any other characteristic points
   /// are at the end of the index-th segment.
   /// </summary>
   /// <param name="segments">The input list of tooling segments</param>
   /// <param name="notchPointsInfo">The List of NotchPointInfo where each item as exactly one
   /// index of the segment in the list and only one point, which should be the end point of 
   /// the index-th segment in the tooling segments list</param>
   /// <param name="bound">The bound of the tooling item</param>
   /// <param name="toolingItem">The tooling item.</param>
   /// <returns></returns>
   /// <exception cref="Exception">An exception is thrown if the pre-step to split the tooling segments is not 
   /// made. This is checked if each of the NotchPointInfo has only one point for the index (of the segment)</exception>
   public static List<NotchAttribute> GetNotchAttributes (ref List<ToolingSegment> segments,
   ref List<NotchPointInfo> notchPointsInfo, Bound3 bound, Tooling toolingItem, double radius, double thickness,
   MCSettings.PartConfigType partConfigType) {
      List<NotchAttribute> notchAttrs = [];

      // Assertion that each notch point info should have only one point after split
      // The notch point of the segIndex-th segment is the end point of the segment
      for (int ii = 0; ii < notchPointsInfo.Count; ii++) {
         if (notchPointsInfo[ii].mSegIndex == -1) continue;
         var pts = notchPointsInfo[ii].mPoints;
         if (pts.Count != 1) throw new Exception ($"GetNotchAttributes: List<NotchPointInfo> notchPointsInfo {ii}th indexth points size != 1");
      }

      // Compute the notch attributes
      for (int ii = 0; ii < notchPointsInfo.Count; ii++) {
         var newNotchAttr = ComputeNotchAttribute (bound, toolingItem, segments, notchPointsInfo[ii].mSegIndex, notchPointsInfo[ii].mPoints[0],
         radius, thickness, partConfigType);
         notchAttrs.Add (newNotchAttr);
      }
      return notchAttrs;
   }

   public static List<NotchAttribute> GetNotchAttributes (
      List<ToolingSegment> segments,
      List<(int SegIndex, Point3 NPosition)> refVals,
      Bound3 bound,
   Tooling toolingItem, double radius, double thickness,
   MCSettings.PartConfigType partConfigType) {
      List<NotchAttribute> notchAttrs = [];

      // Compute the notch attributes
      for (int ii = 0; ii < refVals.Count; ii++) {
         var newNotchAttr = ComputeNotchAttribute (bound, toolingItem, segments, refVals[ii].SegIndex, refVals[ii].NPosition, radius, thickness, partConfigType);
         notchAttrs.Add (newNotchAttr);
      }
      return notchAttrs;
   }

   /// <summary>
   /// This method computes the notch attribute at a given notch point and the index of the tooling segments.
   /// This notch attribute contains the curve segment. The end point of the curve segment will be the notch point
   /// 
   /// TODO: The notch attribute is yet to be optimized. It will be optimized in the subsequent iterations.
   /// </summary>
   /// <param name="bound">The bounds of the tooling</param>
   /// <param name="toolingItem">The tooling item, which has the list of Larcs.</param>
   /// <param name="segments">The list of Larcs</param>
   /// <param name="segIndex">The index of the list of Larcs at which the notch point exists</param>
   /// <param name="notchPoint">The notch point, in the given context, is an unique point on the curve at which 
   /// it is desired to approach the cutting tool to start cutting the segments or leave gap with a small cut 
   /// so as to make it easy to remove the possibly heavy scrap material after cut,</param>
   /// <returns>The notch atrribute, which is a tuple of the following. The Curve, Start normal, The end Normal
   /// Normal along the flange of the curve segment, The outward vector to the nearest proximal boundary 
   /// The EAxis to the proximal boundary ( in the case of the previous vector being 0) and a flag</returns>
   /// <exception cref="NotSupportedException">An exception is thrown if the outward vector does not point 
   /// to the NegX, or X or Neg Z</exception>
   public static NotchAttribute ComputeNotchAttribute (
    Bound3 bound,
    Tooling toolingItem,
    List<ToolingSegment> segments,
    int segIndex,
    Point3 notchPoint,
    double radius,
    double thickness,
    MCSettings.PartConfigType partConfigType,
    bool isFlexMachining = false) {
      // Determine if the notch is on two flanges and if the start and end of the notch
      // are on the same Y flange.
      List<ToolingSegment> webSegs;
      double yReach = 0;

      bool twoFlangeNotchStartAndEndOnSameSideFlange = IsDualFlangeSameSideNotch (toolingItem, segments);
      if (twoFlangeNotchStartAndEndOnSameSideFlange) {
         webSegs = Utils.GetToolingsWithNormal (segments, XForm4.mZAxis);
         yReach = webSegs[0].Curve.End.Y;
      }

      // If segIndex is invalid, return a default NotchAttribute.
      if (segIndex == -1) {
         return new NotchAttribute (
             null,
             new Vector3 (),
             new Vector3 (),
             new Vector3 (),
             new Vector3 (),
             XForm4.EAxis.Z,
             new Vector3 (),
             false
         );
      }

      // Initialize variables for processing
      XForm4.EAxis proxBdyStart;
      Vector3 outwardNormalAlongFlange;
      Vector3 vectorOutwardAtStart, vectorOutwardAtEnd, vectorOutwardAtSpecPoint, scrapsideMaterialDir;

      if (segments[segIndex].Curve is FCArc3 arc) {
         // Handle Arc3 curves
         (var center, _) = Geom.EvaluateCenterAndRadius (arc);

         vectorOutwardAtSpecPoint = GetVectorToProximalBoundary (
             notchPoint,
             bound,
             segments[segIndex],
             toolingItem.ProfileKind,
             out proxBdyStart,
             twoFlangeNotchStartAndEndOnSameSideFlange,
             segments[0], segments[^1],
             radius,
             thickness,
             partConfigType,
             isFlexMachining,
             yReach
         );

         if (twoFlangeNotchStartAndEndOnSameSideFlange)
            scrapsideMaterialDir = GetMaterialRemovalSideDirection (segments[segIndex], notchPoint, EKind.Notch, toolingItem.ProfileKind);
         else
            scrapsideMaterialDir = vectorOutwardAtSpecPoint;

         return new NotchAttribute (
             segments[segIndex].Curve,
             segments[segIndex].Vec0.Normalized (),
             segments[segIndex].Vec1.Normalized (),
             scrapsideMaterialDir.Normalized (),
             vectorOutwardAtSpecPoint,
             proxBdyStart,
             scrapsideMaterialDir,
             true
         );
      } else {
         // Handle Line3 curves
         var line = segments[segIndex].Curve as FCLine3;
         var p1p2 = line.End - line.Start;

         vectorOutwardAtStart = GetVectorToProximalBoundary (
             line.Start,
             bound,
             segments[segIndex],
             toolingItem.ProfileKind,
             out proxBdyStart,
             twoFlangeNotchStartAndEndOnSameSideFlange,
             segments[0], segments[^1],
             radius,
             thickness,
             partConfigType,
             isFlexMachining,
             yReach
         );

         vectorOutwardAtEnd = GetVectorToProximalBoundary (
             line.End,
             bound,
             segments[segIndex],
             toolingItem.ProfileKind,
             out _,
             twoFlangeNotchStartAndEndOnSameSideFlange,
             segments[0], segments[^1],
             radius,
             thickness,
             partConfigType,
             isFlexMachining,
             yReach
         );

         vectorOutwardAtSpecPoint = GetVectorToProximalBoundary (
             notchPoint,
             bound,
             segments[segIndex],
             toolingItem.ProfileKind,
             out _,
             twoFlangeNotchStartAndEndOnSameSideFlange,
             segments[0], segments[^1],
             radius,
             thickness,
             partConfigType,
             isFlexMachining,
             yReach
         );

         if (twoFlangeNotchStartAndEndOnSameSideFlange)
            scrapsideMaterialDir = GetMaterialRemovalSideDirection (segments[segIndex], notchPoint, EKind.Notch, toolingItem.ProfileKind);
         else
            scrapsideMaterialDir = vectorOutwardAtSpecPoint;

         Vector3 bdyVec = proxBdyStart switch {
            XForm4.EAxis.NegX => XForm4.mNegXAxis,
            XForm4.EAxis.X => XForm4.mXAxis,
            XForm4.EAxis.NegZ => XForm4.mNegZAxis,
            XForm4.EAxis.NegY => XForm4.mNegYAxis,
            XForm4.EAxis.Y => XForm4.mYAxis,
            _ => throw new NotSupportedException ("Outward vector cannot be other than NegX, X, and NegZ")
         };

         // outwardNormalAlongFlange is the bi normal along the flange, 
         // which is bi normal to the segment normal at notch point and the 
         // segment vector. 
         outwardNormalAlongFlange = new Vector3 ();

         if (vectorOutwardAtStart.Length.EQ (0) || vectorOutwardAtEnd.Length.EQ (0)) {
            outwardNormalAlongFlange = bdyVec;
         } else {
            int nc = 0;
            // For the web flange..
            if (segments[segIndex].Vec0.Normalized ().EQ (XForm4.mZAxis)) {
               do {
                  if (!Geom.Cross (p1p2, bdyVec).Length.EQ (0)) {
                     outwardNormalAlongFlange = Geom.Cross (
                         segments[segIndex].Vec0.Normalized (),
                         p1p2
                     ).Normalized ();

                     if (outwardNormalAlongFlange.Opposing (bdyVec))
                        outwardNormalAlongFlange *= -1.0;

                     break;
                  } else
                     p1p2 = Geom.Perturb (p1p2);

                  ++nc;
                  if (nc > 10)
                     break;
               } while (true);
            } else { // If the flange is not web flange...
               if (segments[segIndex].Vec0.Normalized ().EQ (XForm4.mYAxis) ||
                  segments[segIndex].Vec0.Normalized ().EQ (XForm4.mNegYAxis) || Utils.IsToolingOnFlex (segments[segIndex])) {
                  // Find the three vectors, 1. from notch point to the bound Xmin
                  // 2. from notch point to XMax,
                  // 3. vectorOutwardAtSpecPoint
                  var v1 = new Vector3 (bound.XMin - notchPoint.X, 0, 0);
                  var v2 = new Vector3 (bound.XMax - notchPoint.X, 0, 0);
                  var v3 = vectorOutwardAtSpecPoint;
                  // Choose the vector with least length as outwardNormalAlongFlange
                  if (v1.Length <= v2.Length && v1.Length <= v3.Length)
                     outwardNormalAlongFlange = v1;
                  else if (v2.Length <= v1.Length && v2.Length <= v3.Length)
                     outwardNormalAlongFlange = v2;
                  else
                     outwardNormalAlongFlange = v3;
               } else if (segments[segIndex].Vec0.Normalized ().EQ (XForm4.mNegZAxis))
                  throw new Exception ("Utils.ComputeNotchAttribute: Neg Z axis not supported");
               else
                  throw new Exception ("Utils.ComputeNotchAttribute: Undefined normal not supported");
            }
         }
      }

      return new NotchAttribute (
          segments[segIndex].Curve,
          segments[segIndex].Vec0.Normalized (),
          segments[segIndex].Vec1.Normalized (),
          outwardNormalAlongFlange.Normalized (),
          vectorOutwardAtSpecPoint,
          proxBdyStart,
          scrapsideMaterialDir,
          true
      );
   }


   /// <summary>
   /// This method returns the vector from a point on a contour towards nearest 
   /// proximal boundary that is happening on -X or -Z axis. The magnitude of this 
   /// vector is the distance to boundary from the given point
   /// </summary>
   /// <param name="pt">The input point</param>
   /// <param name="bound">The bounds of the current tooling</param>
   /// <param name="toolingItem">The tooling</param>
   /// <param name="proxBdy">The ordinate Axis of the proximal boundary vector. This is significant 
   /// if the given point is itself is at X=0.</param>
   /// <returns>The vector from the given point to the point on the nearest boundary along -X or X or -Z</returns>
   /// <exception cref="Exception">If the notch type is of type unknown, an exception is thrown</exception>
   static Vector3 GetVectorToProximalBoundary (Point3 pt, Bound3 bound, ToolingSegment seg,
                                            ECutKind profileKind, out XForm4.EAxis proxBdy,
                                            bool doubleFlangeNotchWithSameSideStartAndEnd,
                                            ToolingSegment startSeg, ToolingSegment endSeg,
                                            double radius, double thickness, MCSettings.PartConfigType partConfigType,
                                            bool isFlexMachining = false, double bdyYExtreme = 0) {
      Vector3 res;
      Point3 bdyPtXMin, bdyPtXMax, bdyPtZMin;
      Vector3 normalAtNotchPt;
      double t;
      Arc3 arc;
      bdyPtXMin = new Point3 (bound.XMin, pt.Y, pt.Z);
      bdyPtXMax = new Point3 (bound.XMax, pt.Y, pt.Z);
      switch (profileKind) {
         case ECutKind.Top:

            if (doubleFlangeNotchWithSameSideStartAndEnd) {
               res = new Vector3 (0, bdyYExtreme - pt.Y, 0);
               if (bdyYExtreme - pt.Y < 0) proxBdy = XForm4.EAxis.NegY;
               else proxBdy = XForm4.EAxis.Y;
            } else {
               if (seg.Curve is Arc3) {
                  //arc = seg.Curve as Arc3;
                  //var (tgt, _) = Geom.EvaluateTangentAndNormalAtPoint (arc, pt, seg.Vec0.Normalized ());
                  // To find the scrapside normal for arcs
                  // Find the cross of tangent vec with seg normal
                  //var segNormal = seg.Vec0.Normalized ();
                  //var biNormal = tgt.Cross (segNormal).Normalized (); 
                  //var negBiNormal = biNormal * -1;
                  // Scrapside direction is one among biNormal or negBiNormal.
                  // it is found by checking the cross of tgt and binormal sense with segment's normal.
                  // Here if the cross is same sense with binormal then it is the scrapside. Otherwise, 
                  // negBinormal is the scrapside.
                  // How it works: IN Top plane(flange), The profile starts from neg-X to Pos-X. So
                  // the scrapside is always to the left 90 deg to the tangent. A left turn is CCW and so
                  // if the computed normal is CW, it is negated.
                  //var computedNormal = tgt.Cross (biNormal);
                  //if (computedNormal.Opposing (seg.Vec0))
                  //   res = bdyPtXMin - pt;
                  //else
                  //   res = bdyPtXMax - pt;

                  if (pt.DistTo (bdyPtXMin)
                     < pt.DistTo (bdyPtXMax)) {
                     proxBdy = XForm4.EAxis.NegX;
                     res = bdyPtXMin - pt;
                  } else {
                     proxBdy = XForm4.EAxis.X;
                     res = bdyPtXMax - pt;
                  }
               } else {
                  if (pt.DistTo (bdyPtXMin)
                        < pt.DistTo (bdyPtXMax)) {
                     res = bdyPtXMin - pt;
                     proxBdy = XForm4.EAxis.NegX;
                  } else {
                     res = bdyPtXMax - pt;
                     proxBdy = XForm4.EAxis.X;
                  }
               }
            }
            break;
         case ECutKind.Top2YPos: // Also for doubleFlangeNotchWithSameSideStartAndEnd
         case ECutKind.Top2YNeg: // also for doubleFlangeNotchWithSameSideStartAndEnd
         case ECutKind.YNegToYPos: /* TRIPLE_FLANGE_NOTCH */
         case ECutKind.YPos:
         case ECutKind.YNeg:
            double OrdX;
            double OrdY = bound.Min.Y;
            double OrdZ = bound.Max.Z;
            if (seg.Curve is Arc3) {
               if (pt.DistTo (bdyPtXMin)
                        < pt.DistTo (bdyPtXMax))
                  proxBdy = XForm4.EAxis.NegX;
               else
                  proxBdy = XForm4.EAxis.X;

               if (doubleFlangeNotchWithSameSideStartAndEnd) {
                  OrdX = (startSeg.Curve.Start.X + endSeg.Curve.End.X) / 2;
                  OrdZ = bound.Max.Z;
                  // Clockwise case
                  if (profileKind == ECutKind.YPos || profileKind == ECutKind.Top2YPos) {
                     OrdY = -bound.Min.Y + (radius + thickness);
                  } else {
                     OrdY = bound.Min.Y - (radius + thickness);
                  }
               } else {
                  if (pt.DistTo (bdyPtXMin)
                        < pt.DistTo (bdyPtXMax))
                     OrdX = bound.Min.X;
                  else
                     OrdX = bound.Max.X;

                  var maxYCondition = (profileKind == ECutKind.YPos && partConfigType == MCSettings.PartConfigType.LHComponent) ||
                     (profileKind == ECutKind.YNeg && partConfigType == MCSettings.PartConfigType.RHComponent);
                  var minYCondition = (profileKind == ECutKind.YNeg && partConfigType == MCSettings.PartConfigType.LHComponent) ||
                     (profileKind == ECutKind.YPos && partConfigType == MCSettings.PartConfigType.RHComponent);

                  if (maxYCondition) {
                     OrdX = pt.X;
                     OrdY = bound.Max.Y;
                     OrdZ = bound.Min.Z;
                     proxBdy = XForm4.EAxis.NegZ;
                  } else if (minYCondition) {
                     OrdX = pt.X;
                     OrdY = bound.Min.Y;
                     OrdZ = bound.Min.Z;
                     proxBdy = XForm4.EAxis.NegZ;
                  } else if (profileKind == ECutKind.Top2YPos || profileKind == ECutKind.Top2YNeg) {
                     OrdY = pt.Y;
                     OrdZ = pt.Z;
                  }
               }
               res = new Point3 (OrdX, OrdY, OrdZ) - pt;
            } else {
               if (profileKind == ECutKind.Top2YPos || profileKind == ECutKind.Top2YNeg || profileKind == ECutKind.YNegToYPos) {
                  t = pt.DistTo (seg.Curve.Start) / seg.Curve.Length;
                  normalAtNotchPt = Geom.GetInterpolatedNormal (seg.Vec0, seg.Vec1, t);

                  if (isFlexMachining) goto case ECutKind.YPosFlex;
                  if (normalAtNotchPt.EQ (XForm4.mZAxis)) goto case ECutKind.Top;
                  if (Utils.IsNormalAtFlex (normalAtNotchPt)) goto case ECutKind.YPosFlex;
               }
               bdyPtZMin = new Point3 (pt.X, pt.Y, bound.ZMin);
               res = bdyPtZMin - pt;
               proxBdy = XForm4.EAxis.NegZ;
            }
            break;
         case ECutKind.YPosFlex:
         case ECutKind.YNegFlex:
            if (pt.DistTo (bdyPtXMin = new Point3 (bound.XMin, pt.Y, pt.Z))
                  < pt.DistTo (bdyPtXMax = new Point3 (bound.XMax, pt.Y, pt.Z))) {
               res = bdyPtXMin - pt;
               proxBdy = XForm4.EAxis.NegX;
            } else {
               res = bdyPtXMax - pt;
               proxBdy = XForm4.EAxis.X;
            }
            if (!isFlexMachining) {
               bdyPtZMin = new Point3 (pt.X, pt.Y, bound.ZMin);
               if (res.Length > (bdyPtZMin - pt).Length) {
                  res = bdyPtZMin - pt;
                  proxBdy = XForm4.EAxis.NegZ;
               }
            }
            break;
         default:
            throw new Exception ("Unknown notch type encountered");
      }
      return res;
   }
}

#endregion