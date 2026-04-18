#nullable enable
using ProfileCAM.Core.Optimizer;
using ProfileCAM.Core;
using ProfileCAM.Core.Geometries;
using System.Linq.Expressions;
using ProfileCAM.Core.GCodeGen;
using Flux.API;

namespace ProfileCAM.Core.Optimizer {
   public struct Frame {
      public enum Bucket {
         H11,
         H12,
         H21,
         H22
      }
      public List<ToolScope<Tooling>> FrameToolScopesList { get; set; } = [];
      public List<ToolScope<Tooling>> AllToolScopesList { get; set; } = [];
      public List<ToolScope<Tooling>> FrameToolScopesBySX { get; set; } = [];
      public List<ToolScope<Tooling>> FrameToolScopesByEX { get; set; } = [];

      public List<ToolScope<Tooling>> FrameToolScopesH11 { get; set; } = [];
      public List<ToolScope<Tooling>> FrameToolScopesH12 { get; set; } = [];
      public List<ToolScope<Tooling>> FrameToolScopesH21 { get; set; } = [];
      public List<ToolScope<Tooling>> FrameToolScopesH22 { get; set; } = [];
      public List<Tooling> ToolingsH11 = [];
      public List<Tooling> ToolingsH12 = [];
      public List<Tooling> ToolingsH21 = [];
      public List<Tooling> ToolingsH22 = [];
      public readonly PointVec? FinishPositionHead1 => Utils.GetEndPos (FrameToolScopesH12);
      public readonly PointVec? FinishPositionHead2 => Utils.GetEndPos (FrameToolScopesH22);

      public double MinFL { get; set; } = 0;
      public double MaxFL { get; set; } = 0;

      public double StartX { get; set; } = 0;
      public double EndX { get; set; } = 0;

      public double Head11EndX { get; set; } = 0;
      public double Head12EndX { get; set; } = 0;
      public double Head21EndX { get; set; } = 0;

      public double Head22EndX { readonly get => EndX; set => EndX = value; }
      public double Head11StartX { readonly get => StartX; set => StartX = value; }
      public double MachiningTimeH1 { get; private set; } = 0;
      public double MachiningTimeH2 { get; private set; } = 0;
      public double RapidPosTimeH1 { get; private set; } = 0;
      public double RapidPosTimeH2 { get; private set; } = 0;
      public double TotalRapidPosTime { get; private set; } = 0;
      public double WaitTime { get; private set; } = 0;
      public double TotalMachiningTime { get; private set; } = 0;
      public double TotalProcessTime { get; private set; } = 0;
      public FrameMachinableStatus MachinableStatus { get; private set; } = FrameMachinableStatus.Impossible;

      // Replace node references with indices
      public int StartIndex { get; set; }
      public int EndIndex { get; set; }
      GCodeGenerator? mGcGen;
      double mRapidPosSpeed;
      double mMcSpeed;
      double mStandoffToEngageTime;
      double mStandOffDist;
      PointVec? mPrevFrameFinishPosH1;
      PointVec? mPrevFrameFinishPosH2;

      public double Tol { get; set; } = 1e-6;
      double A, B, C;

      public Frame () { }

      public Frame (
          GCodeGenerator gcGen,
          List<ToolScope<Tooling>> allToolScopes,
          List<ToolScope<Tooling>> frameToolScopes,
          double minFL,
          double maxFL,
          int startIndex, // in allToolScopes
          int endIndex, // in frameToolScopes
          PointVec? prevFrameFinishPosH1,
          PointVec? prevFrameFinishPosH2,
          double rapidPosSpeed,
          double mcSpeed,
          double sOff2EngageTime,
          double sOffDist,
          double tol = 1e-6) {
         mGcGen = gcGen;
         Tol = tol;
         mPrevFrameFinishPosH1 = prevFrameFinishPosH1;
         mPrevFrameFinishPosH2 = prevFrameFinishPosH2;
         AllToolScopesList = allToolScopes ?? throw new FrameNotProcessableException ("Empty frame");
         FrameToolScopesList = frameToolScopes ?? throw new FrameNotProcessableException ("Empty frame");

         StartIndex = startIndex;
         EndIndex = endIndex;

         MinFL = minFL;
         MaxFL = maxFL;

         mRapidPosSpeed = rapidPosSpeed;
         mMcSpeed = mcSpeed;
         mStandoffToEngageTime = sOff2EngageTime;
         mStandOffDist = sOffDist;

         // Sort once → reuse
         FrameToolScopesBySX = [.. FrameToolScopesList.OrderBy (ts => ts.StartX)];

         FrameToolScopesByEX = [.. FrameToolScopesList.OrderBy (ts => ts.EndX)];

         if (FrameToolScopesBySX.Count == 0)
            throw new InvalidOperationException ("FrameToolScopesBySX empty");

         StartX = FrameToolScopesBySX[0].StartX;

         if (FrameToolScopesByEX.Count == 0)
            throw new InvalidOperationException ("FrameToolScopesByEX empty");

         EndX = FrameToolScopesByEX[^1].EndX;

         var delta = (EndX - StartX) / 2 - MinFL;

         A = Head11EndX = StartX + delta;
         B = Head12EndX = Head11EndX + MinFL;
         C = Head21EndX = Head12EndX + MinFL;

         FrameToolScopesH11 = [];
         FrameToolScopesH12 = [];
         FrameToolScopesH21 = [];
         FrameToolScopesH22 = [];

         // No intersection of toolscopes at "B" or the center is desired. Dilemma arised
         // as to which of the buckets the ixn tss be part of., FrameToolScopesH12 or FrameToolScopesH21
         // This dilemma is solved using moving B line either to right or left depending on 
         // which side the defference is lesser
         // The intention is to avoid any tool scopes intersecting at "B"
         double leftOffset = 0, rightOffset = 0;
         double cumLeftOffset = 0, cumRightOffset = 0;
         //double leftSideGap = 0;
         double a = A, b1 = B, b2 = B, c = C;
         bool leftOffsetStrategy = true, rightOffsetStrategy = true;
         bool needToLeftOffset = false, needToRightOffset = false;
         while (true) {
            var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, b1, excludeProcessed: true, startIndex: 0, tol: tol);

            if (ixnTSSAtFrameCenter.Count > 0) {
               needToLeftOffset = true;
               var ixnTSSAtFrameCenterBounds = Utils.GetBounds (ixnTSSAtFrameCenter);
               if (ixnTSSAtFrameCenterBounds != null)
                  leftOffset = -(b1 - ixnTSSAtFrameCenterBounds.Value.MinStartX);
               if (leftOffset > 30.0)
                  leftOffsetStrategy = false;

            } else
               break;
            b1 += leftOffset;
            cumLeftOffset += leftOffset;
         }

         while (true) {
            var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, b2, excludeProcessed: true, startIndex: 0, tol: tol);

            if (ixnTSSAtFrameCenter.Count > 0) {
               needToRightOffset = true;
               var ixnTSSAtFrameCenterBounds = Utils.GetBounds (ixnTSSAtFrameCenter);
               if (ixnTSSAtFrameCenterBounds != null)
                  //leftSideGap = B - ixnTSSAtFrameCenterBounds.Value.MinStartX;
                  rightOffset = ixnTSSAtFrameCenterBounds.Value.MaxEndX - b2;
               else
                  break;
               if (rightOffset > 30.0)
                  rightOffsetStrategy = false;

               b2 += rightOffset;
               cumRightOffset += rightOffset;

            } else
               break;
         }

         if (needToLeftOffset || needToRightOffset) {
            if (!leftOffsetStrategy && !rightOffsetStrategy) {
               var offset = leftOffset > rightOffset ? leftOffset : rightOffset;
               throw new Exception ($"The arrangement of toolscopes occuring at the middle intersects with middle line ( bucker boundary between H12 and H21) and the offset middle line is computed to be more than {offset}");
            }

            if (leftOffsetStrategy && rightOffsetStrategy) {
               var offset = cumLeftOffset < cumRightOffset ? cumLeftOffset : cumRightOffset;
               A += offset;
               Head11EndX = A;
               B += offset;
               Head12EndX = B;
               C += offset;
               Head21EndX = C;
            } else if (leftOffsetStrategy) {
               A += cumLeftOffset;
               Head11EndX = A;
               B += cumLeftOffset;
               Head12EndX = B;
               C += cumLeftOffset;
               Head21EndX = C;
            } else {
               A += cumRightOffset;
               Head11EndX = A;
               B += cumRightOffset;
               Head12EndX = B;
               C += cumRightOffset;
               Head21EndX = C;
            }
         }
         if (PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, excludeProcessed: true, startIndex: 0, tol: tol).Count > 0)
            throw new Exception ("Tool Scopes intersect at B line");


         CollectToolScopesInBuckets ();
         CheckToolScopesIXNAtXPositions ();
         CheckCountConsistency ();
         CheckMinFLConsistency ();

         // Sort tooling as per user settings
         ToolingsH11 = Utils.GetToolings4Head (FrameToolScopesH11, 0, mGcGen.GCodeGenSettings);
         ToolingsH12 = Utils.GetToolings4Head (FrameToolScopesH12, 0, mGcGen.GCodeGenSettings);
         ToolingsH21 = Utils.GetToolings4Head (FrameToolScopesH21, 1, mGcGen.GCodeGenSettings);
         ToolingsH22 = Utils.GetToolings4Head (FrameToolScopesH22, 1, mGcGen.GCodeGenSettings);
         if (FrameToolScopesH11.Count > 0 && FrameToolScopesH12.Count > 0 && FrameToolScopesH21.Count > 0 && FrameToolScopesH22.Count > 0) {
            int aa = 0;
            ++aa;
         }
         FindMachinableStatus ();
         if (MachinableStatus != FrameMachinableStatus.Machinable)
            return;

         // Machine
         var (rapisPosTimeH11, mcTimeH11) = ComputeRapidPosAndMachiningTime (Bucket.H11, null);
         var (rapisPosTimeH12, mcTimeH12) = ComputeRapidPosAndMachiningTime (Bucket.H12, Bucket.H11);
         var (rapisPosTimeH21, mcTimeH21) = ComputeRapidPosAndMachiningTime (Bucket.H21, null);
         var (rapisPosTimeH22, mcTimeH22) = ComputeRapidPosAndMachiningTime (Bucket.H22, Bucket.H21);

         // Calculate times
         RapidPosTimeH1 = rapisPosTimeH11 + rapisPosTimeH12;
         RapidPosTimeH2 = rapisPosTimeH21 + rapisPosTimeH22;

         MachiningTimeH1 = mcTimeH11 + mcTimeH12;
         MachiningTimeH2 = mcTimeH21 + mcTimeH22;

         TotalRapidPosTime = RapidPosTimeH2 + RapidPosTimeH1;
         WaitTime = Math.Abs (MachiningTimeH1 - MachiningTimeH2);
         TotalMachiningTime = MachiningTimeH1 + MachiningTimeH2 ;
         TotalProcessTime = TotalMachiningTime + WaitTime + TotalRapidPosTime;
      }
      readonly List<ToolScope<Tooling>> GetToolScopes (Bucket bucket) {
         List<ToolScope<Tooling>> toolScopes = bucket switch {
            Bucket.H11 => FrameToolScopesH11,
            Bucket.H12 => FrameToolScopesH12,
            Bucket.H21 => FrameToolScopesH21,
            Bucket.H22 => FrameToolScopesH22,
            _ => []  // default case
         };
         return toolScopes;
      }
      readonly (double RapidPosTime, double McTime) ComputeRapidPosAndMachiningTime (Bucket bucket, Bucket? previousBucket = null) {
         double rapidPosDist = 0;
         double rapidPosTime = 0;
         double mcTime = 0;

         if (previousBucket == null && mPrevFrameFinishPosH1 == null)
            throw new ArgumentException ("Either previous bucket of the frame OR previous frame's eEnd Position should exist");

         List<ToolScope<Tooling>> toolScopes = GetToolScopes (bucket);
         if (toolScopes.Count == 0)
            return (0, 0);


         // If the mc is for H11 or H21 (Start) buckets ( so previous bucket is null )
         if (previousBucket == null) rapidPosDist += Utils.GetRapidPosDist (mPrevFrameFinishPosH1, Utils.GetStartPos (toolScopes));
         else if (previousBucket != null) {
            var prevBucketToolScopes = GetToolScopes (previousBucket.Value);
            var lastBuckerPos = Utils.GetEndPos (prevBucketToolScopes);
            if ( lastBuckerPos != null )
               rapidPosDist += Utils.GetRapidPosDist (lastBuckerPos, Utils.GetStartPos (toolScopes));
         }

         // Get the rapid pos dist from previous frame
         for (int ii = 0; ii < toolScopes.Count; ii++) {
            // for ii =0 rapid position distance is already calculated
            if (ii > 0) {
               // Compute rapid position distance and time from previous to current tooling
               var prevPos = Utils.GetPosAt (toolScopes, ii - 1);
               var currPos = Utils.GetPosAt (toolScopes, ii);
               rapidPosDist += Utils.GetRapidPosDist (prevPos, currPos);
               rapidPosTime += rapidPosDist / mRapidPosSpeed;

               // Standoff to engage in and out
               rapidPosTime += 2 * mStandoffToEngageTime;
            }

            // Compute machining distance and time.
            var toolingLength = Utils.GetToolingLength (toolScopes[ii].Tooling);
            mcTime += toolingLength / mMcSpeed;
            //toolScopes[ii].IsProcessed = true;
         }

         return (rapidPosTime, mcTime);
      }
      
      readonly void CheckCountConsistency () {
         if (FrameToolScopesH11.Count + FrameToolScopesH12.Count + FrameToolScopesH21.Count + FrameToolScopesH22.Count != FrameToolScopesList.Count)
            throw new Exception ("Total tool scopes in the buckets NOT EQUAL to Count in FrameToolScopesList");
      }
      readonly void CheckMinFLConsistency () {
         if ((C - B).SLT (MinFL))
            throw new Exception ($"The bucket for H2 => H21 {C - B} size lesser than {MinFL} mm");
         if ((B - A).SLT (MinFL))
            throw new Exception ($"The bucket for H2 => H21 {B - A} size lesser than {MinFL} mm");
      }
      public readonly void CheckToolScopesIXNAtXPositions (double tol = 1e-6) {
         // there is no need to find the intersections at A and C. The intersecting tools scopes 
         // at A, are added to FrameToolScopesH12 since the left head can move in -X direction
         // at C, are added to FrameToolScopesH21 since the right head can always move in +X direction
         
         var atB = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, excludeProcessed: true, startIndex: 0, tol: tol);
         if (atB.Count > 0) throw new Exception ("ToolScopes intersect at B");
      }

      readonly void AllocateHeadsToToolScopes () {
         for (int ii = 0; ii < FrameToolScopesH11.Count; ii++)
            FrameToolScopesH11[ii].Tooling.Head = 0;
         for (int ii = 0; ii < FrameToolScopesH12.Count; ii++)
            FrameToolScopesH12[ii].Tooling.Head = 0;
         for (int ii = 0; ii < FrameToolScopesH21.Count; ii++)
            FrameToolScopesH21[ii].Tooling.Head = 1;
         for (int ii = 0; ii < FrameToolScopesH22.Count; ii++)
            FrameToolScopesH22[ii].Tooling.Head = 1;
      }
      void CollectToolScopesInBuckets (double tol = 1e-6) {
         double A = Head11EndX;
         double B = Head12EndX;
         double C = Head21EndX;



         var tssAllWithin = PartMultiFrames.GetToolScopesWithin (
             FrameToolScopesList, StartX, EndX, excludeProcessed: true);

         if (tssAllWithin.Count == 0)
            return;




         // Collect tool scopes within H11
         FrameToolScopesH11 = PartMultiFrames.GetToolScopesWithin (
             FrameToolScopesList, StartX, A, excludeProcessed: true);
         // Intersecting tss at A is collected by FrameToolScopesH12





         // Collect tool scopes within H12
         FrameToolScopesH12 = PartMultiFrames.GetToolScopesWithin (
             FrameToolScopesList, A, B, excludeProcessed: true);
         // cross the "A" line and are safe w.r.t head2 as it only increases the distance between
         var ixnTSSAtA = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, A, excludeProcessed: true, startIndex: 0, tol: tol);
         FrameToolScopesH12.AddRange (ixnTSSAtA);




         // Collect tool scopes within H21
         FrameToolScopesH21 = PartMultiFrames.GetToolScopesWithin (
             FrameToolScopesList, B, C, excludeProcessed: true);

         // Intersecting Tss at C added to H21: Reason : The endx of the ixn tss
         // cross the "C" line and are safe w.r.t head1 as it only increases the distance between
         var ixnTSSAtC = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, C, excludeProcessed: true, startIndex: 0, tol: tol);
         FrameToolScopesH21.AddRange (ixnTSSAtC);




         // Collect tool scopes within H22
         FrameToolScopesH22 = PartMultiFrames.GetToolScopesWithin (
             FrameToolScopesList, C, EndX, excludeProcessed: true);


         // Find the intersecting tool scopes at EndX
         var ixnTSSAtFrameEnd = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, EndX, excludeProcessed: true, startIndex: 0, tol: tol);

         //List<ToolScope<Tooling>> ixnToAdd = [];

         // Check the length between Frame StartX and the itersecting toolscopes Endx 
         // is <= MaxFL. If so the intersecting tool scopes. Otherwise, it is not feasible 
         // as the frame length including the end of the ixn tool scope is more than MaxFL
         foreach (var tss in ixnTSSAtFrameEnd) {
            if (tss.IsProcessed == false && (tss.EndX - StartX).LTEQ (MaxFL))
               FrameToolScopesH22.Add (tss);
         }


         // Allocate Heads to tooling in toolscopes
         AllocateHeadsToToolScopes ();
      }

      public void FindMachinableStatus () {
         if (FrameToolScopesH11.Count == 0 && FrameToolScopesH12.Count == 0 && FrameToolScopesH21.Count == 0 && FrameToolScopesH22.Count == 0) {
            MachinableStatus = FrameMachinableStatus.Empty;
            return;
         }

         // Both exist → check global safety
         double maxH12 = FrameToolScopesH12.Max (ts => ts.EndX);
         double MinH12 = FrameToolScopesH12.Min (ts => ts.StartX);
         double maxH21 = FrameToolScopesH21.Max (ts => ts.EndX);
         double minH21 = FrameToolScopesH21.Min (ts => ts.StartX);

         //if ((maxH12 - MinH12).SLT (MinFL, Tol) || (maxH21 - minH21).SLT (MinFL, Tol))
         var thresholdDist = minH21 - maxH12;

         // If the H11 and H22 buckets are empty and the tool scopes 
         if (FrameToolScopesH11.Count == 0 && FrameToolScopesH22.Count == 0 && FrameToolScopesH12.Count >= 0 && FrameToolScopesH21.Count >= 0 &&
            thresholdDist.SLT (MinFL, Tol)) {
            MachinableStatus = FrameMachinableStatus.Impossible;
            return;
         }

         if (FrameToolScopesH11.Count > 0 || FrameToolScopesH12.Count > 0 || FrameToolScopesH21.Count > 0 || FrameToolScopesH22.Count > 0) {
            MachinableStatus = FrameMachinableStatus.Machinable;
            return;
         }
         MachinableStatus = FrameMachinableStatus.Machinable;
      }
   }
}