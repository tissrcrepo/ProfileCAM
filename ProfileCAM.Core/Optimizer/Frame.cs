#nullable enable
using ProfileCAM.Core.Optimizer;
using ProfileCAM.Core;
using ProfileCAM.Core.Geometries;
using ProfileCAM.Core.GCodeGen;
using System.Linq.Expressions;
using Flux.API;
using ProfileCAM.Core.GCodeGen.LCMMultipass2HLegacy;
using static ProfileCAM.Core.GCodeGen.IGCodeGenerator;
using System.Windows.Navigation;
using System.Security.Cryptography;

namespace ProfileCAM.Core.Optimizer {
   public struct Frame {
      public enum Bucket {
         H11,
         H12,
         H21,
         H22
      }
      //public enum HeadType {
      //   Master,
      //   Slave,
      //   Infer
      //}

      
      public ToolScopeList FrameToolScopesList { get; set; } = [];
      public ToolScopeList AllToolScopesList { get; set; } = [];
      public ToolScopeList FrameToolScopesBySX { get; set; } = [];
      public ToolScopeList FrameToolScopesByEX { get; set; } = [];

      public ToolScopeList FrameToolScopesH11 { get; set; } = [];
      public ToolScopeList FrameToolScopesH12 { get; set; } = [];
      public ToolScopeList FrameToolScopesH21 { get; set; } = [];
      public ToolScopeList FrameToolScopesH22 { get; set; } = [];
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
      IGCodeGenerator? mGcGen;
      double mRapidPosSpeed;
      double mMcSpeed;
      double mStandoffToEngageTime;
      double mStandOffDist;
      PointVec? mPrevFrameFinishPosH1;
      PointVec? mPrevFrameFinishPosH2;
      Bound3 mPartBound;
      public double Tol { get; set; } = 1e-6;
      double A, B, C;
      public bool IsLastPass { get; private set; }
      public bool IsSingleHeadJob { get; private set; } = false;
      public Frame () { }

      public Frame (
          IGCodeGenerator gcGen,
          ToolScopeList allToolScopes,
          ToolScopeList frameToolScopes,
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
          Bound3 partBound,
          bool isPossibleLastFrame,
          double tol = 1e-6) {
         // Find if the frame is the last one
         double partLastToolScopeEx = allToolScopes.Count == 0
                                      ? 0
                                      : allToolScopes.Max (ts => ts.EndX);
         double frameLastToolScopeEx = frameToolScopes.Count == 0
                                      ? 0
                                      : frameToolScopes.Max (ts => ts.EndX);
         bool isLastFrame = partLastToolScopeEx.EQ (frameLastToolScopeEx);

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
         mPartBound = partBound;
         var partLastSectionEndX = partBound.XMax;
         var partLastSectionStartX = partBound.XMax-MaxFL;
         // Heuristic for the last pass. Here the Sx - A - B - C - Ex 
         // will not be having a complete length. If the Ex-Sx <= MinFL + 100
         // then it is marked as single head
         if ((EndX - StartX).LTEQ (minFL + 100) && isPossibleLastFrame)
            IsSingleHeadJob = true;

         A = Head11EndX = StartX + delta;
         B = Head12EndX = Head11EndX + MinFL;
         C = Head21EndX = Head12EndX + MinFL;

         FrameToolScopesH11 = [];
         FrameToolScopesH12 = [];
         FrameToolScopesH21 = [];
         FrameToolScopesH22 = [];

         OffsetABCD ();
         CollectToolScopesInBuckets ();
         // Allocate Heads to tooling in toolscopes
         AllocateHeadsToToolScopes (IGCodeGenerator.ToolHeadType.Infer);
         CheckToolScopesIXNAtXPositions ();
         CheckCountConsistency ();
         CheckMinFLConsistency ();
         AllocateHeads2Toolings ();


         //if (FrameToolScopesH11.Count > 0 && FrameToolScopesH12.Count > 0 && FrameToolScopesH21.Count > 0 && FrameToolScopesH22.Count > 0) {
         //   int aa = 0;
         //   ++aa;
         //}
         FindMachinableStatus ();
         if (MachinableStatus != FrameMachinableStatus.Machinable)
            return;
         ComputeProcessingTimes (waitTimeSclaFactor: 10.0);
      }

      void ComputeProcessingTimes (double waitTimeSclaFactor, bool isLastFrame = false) {
         // Machine
         var (rapisPosTimeH11, mcTimeH11) = ComputeRapidPosAndMachiningTime (Bucket.H11, previousBucket: null);
         var (rapisPosTimeH12, mcTimeH12) = ComputeRapidPosAndMachiningTime (Bucket.H12, previousBucket: Bucket.H11);
         var (rapisPosTimeH21, mcTimeH21) = ComputeRapidPosAndMachiningTime (Bucket.H21, previousBucket: null);
         var (rapisPosTimeH22, mcTimeH22) = ComputeRapidPosAndMachiningTime (Bucket.H22, previousBucket: Bucket.H21);

         // Calculate times
         RapidPosTimeH1 = rapisPosTimeH11 + rapisPosTimeH12;
         RapidPosTimeH2 = rapisPosTimeH21 + rapisPosTimeH22;

         MachiningTimeH1 = mcTimeH11 + mcTimeH12;
         MachiningTimeH2 = mcTimeH21 + mcTimeH22;

         TotalRapidPosTime = RapidPosTimeH2 + RapidPosTimeH1;
         WaitTime = MachiningTimeH1 - MachiningTimeH2;
         if (isLastFrame) waitTimeSclaFactor = 1;
         var ScaledWaitTime = Math.Abs (MachiningTimeH1 - MachiningTimeH2) * waitTimeSclaFactor; // Scale factor for waiting time
         TotalMachiningTime = MachiningTimeH1 + MachiningTimeH2;
         TotalProcessTime = TotalMachiningTime + ScaledWaitTime + TotalRapidPosTime;
      }
      readonly ToolScopeList GetToolScopes (Bucket bucket) {
         ToolScopeList toolScopes = bucket switch {
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

         ToolScopeList toolScopes = GetToolScopes (bucket);
         if (toolScopes.Count == 0)
            return (0, 0);


         // If the mc is for H11 or H21 (Start) buckets ( so previous bucket is null )
         if (previousBucket == null) rapidPosDist += Utils.GetRapidPosDist (mPrevFrameFinishPosH1, Utils.GetStartPos (toolScopes));
         else if (previousBucket != null) {
            var prevBucketToolScopes = GetToolScopes (previousBucket.Value);
            var lastBuckerPos = Utils.GetEndPos (prevBucketToolScopes);
            if (lastBuckerPos != null)
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

      void AllocateHeads2Toolings () {
         // Sort tooling as per user settings
         if (mGcGen == null)
            throw new Exception ("G Code generator not set");
         FrameToolScopesH11 = Utils.GetToolingScopes4Head (FrameToolScopesH11, 0, mGcGen.GCodeGenSettings);
         FrameToolScopesH12 = Utils.GetToolingScopes4Head (FrameToolScopesH12, 0, mGcGen.GCodeGenSettings);
         FrameToolScopesH21 = Utils.GetToolingScopes4Head (FrameToolScopesH21, 1, mGcGen.GCodeGenSettings);
         FrameToolScopesH22 = Utils.GetToolingScopes4Head (FrameToolScopesH22, 1, mGcGen.GCodeGenSettings);

         ToolingsH11 = [.. FrameToolScopesH11.Select (ts => ts.Tooling)];
         ToolingsH12 = [.. FrameToolScopesH12.Select (ts => ts.Tooling)];
         ToolingsH21 = [.. FrameToolScopesH21.Select (ts => ts.Tooling)];
         ToolingsH22 = [.. FrameToolScopesH22.Select (ts => ts.Tooling)];
      }

      public ToolScopeList Bucket11ToolScopes { get => FrameToolScopesH11; }
      public ToolScopeList Bucket12ToolScopes { get => FrameToolScopesH12; }
      public ToolScopeList Bucket21ToolScopes { get => FrameToolScopesH21; }
      public ToolScopeList Bucket22ToolScopes { get => FrameToolScopesH22; }

      public List<Tooling> Bucket11Toolings { get => ToolingsH11; }
      public List<Tooling> Bucket12Toolings { get => ToolingsH12; }
      public List<Tooling> Bucket21Toolings { get => ToolingsH21; }
      public List<Tooling> Bucket22Toolings { get => ToolingsH22; }
      public ToolScopeList MasterHeadToolingScopes {
         get {
            ToolScopeList res = [.. Bucket11ToolScopes, .. Bucket12ToolScopes]; ;
            return res;
         }
      }
      public ToolScopeList SlaveHeadToolScopes {
         get {
            ToolScopeList res = [.. Bucket21ToolScopes, .. Bucket22ToolScopes]; ;
            return res;
         }
      }



      readonly void CheckCountConsistency () {
         if (IsSingleHeadJob) return;
         if (FrameToolScopesH11.Count + FrameToolScopesH12.Count + FrameToolScopesH21.Count + FrameToolScopesH22.Count != FrameToolScopesList.Count)
            throw new Exception ("Total tool scopes in the buckets NOT EQUAL to Count in FrameToolScopesList");
      }
      readonly void CheckMinFLConsistency () {
         if (IsSingleHeadJob) return;
         if ((C - B).SLT (MinFL))
            throw new Exception ($"The bucket for H2 => H21 {C - B} size lesser than {MinFL} mm");
         if ((B - A).SLT (MinFL))
            throw new Exception ($"The bucket for H2 => H21 {B - A} size lesser than {MinFL} mm");
      }
      public readonly void CheckToolScopesIXNAtXPositions (double tol = 1e-6) {
         if (IsSingleHeadJob)
            return;
         // there is no need to find the intersections at A and C. The intersecting tools scopes 
         // at A, are added to FrameToolScopesH12 since the left head can move in -X direction
         // at C, are added to FrameToolScopesH21 since the right head can always move in +X direction

         var atB = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, excludeProcessed: true, startIndex: 0, tol: tol);
         if (atB.Count > 0) throw new Exception ("ToolScopes intersect at B");
      }

      public readonly void AllocateHeadsToToolScopes (IGCodeGenerator.ToolHeadType hType) {
         int headForLeft = 0;   // H11 & H12
         int headForRight = 1;   // H21 & H22

         switch (hType) {
            case IGCodeGenerator.ToolHeadType.Master:
               headForLeft = 0;
               headForRight = 0;
               break;

            case IGCodeGenerator.ToolHeadType.Slave:
               headForLeft = 1;
               headForRight = 1;
               break;

            case IGCodeGenerator.ToolHeadType.Infer:
               headForLeft = 0;
               headForRight = 1;
               break;

            default:
               throw new ArgumentOutOfRangeException (nameof (hType), hType, "Invalid HeadType");
         }

         // Apply to all tool scopes in the four buckets
         SetHeadForList (FrameToolScopesH11, headForLeft);
         SetHeadForList (FrameToolScopesH12, headForLeft);
         SetHeadForList (FrameToolScopesH21, headForRight);
         SetHeadForList (FrameToolScopesH22, headForRight);
      }

      // Helper method to avoid code duplication
      private readonly void SetHeadForList (ToolScopeList? list, int headValue) {
         if (list == null)
            return;

         foreach (var ts in list) {
            if (ts?.Tooling != null) {
               ts.Tooling.Head = headValue;
            }
         }
      }

      void OffsetABCD () {
         if (IsSingleHeadJob)
            return;
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
            var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, b1, excludeProcessed: true,
               startIndex: 0, tol: Tol);

            if (ixnTSSAtFrameCenter.Count > 0) {
               needToLeftOffset = true;
               var ixnTSSAtFrameCenterBounds = Utils.GetScope (ixnTSSAtFrameCenter);
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
            var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, b2, excludeProcessed: true,
               startIndex: 0, tol: Tol);

            if (ixnTSSAtFrameCenter.Count > 0) {
               needToRightOffset = true;
               var ixnTSSAtFrameCenterBounds = Utils.GetScope (ixnTSSAtFrameCenter);
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
         if (PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, excludeProcessed: true, startIndex: 0, tol: Tol).Count > 0)
            throw new Exception ("Tool Scopes intersect at B line");
      }

      public void AllocateHeadsToToolScopes () {
         if (IsSingleHeadJob) {
            var partLastSectionMidX = (mPartBound.XMin + mPartBound.XMin) / 2.0;
            FrameToolScopesH11 = PartMultiFrames.GetToolScopesWithin (
             FrameToolScopesList, StartX, EndX, excludeProcessed: true);
            double avgCenter = FrameToolScopesH11.Average (ts => (ts.StartX + ts.EndX) / 2.0);
            if (avgCenter.LTEQ (partLastSectionMidX))
               AllocateHeadsToToolScopes (IGCodeGenerator.ToolHeadType.Master);
            else
               AllocateHeadsToToolScopes (IGCodeGenerator.ToolHeadType.Slave);
            return;
         }
         AllocateHeadsToToolScopes (ToolHeadType.Infer);
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
         var ixnTSSAtFrameEnd = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, EndX,
            excludeProcessed: true, startIndex: 0, tol: tol);

         //ToolScopeList ixnToAdd = [];

         // Check the length between Frame StartX and the itersecting toolscopes Endx 
         // is <= MaxFL. If so the intersecting tool scopes. Otherwise, it is not feasible 
         // as the frame length including the end of the ixn tool scope is more than MaxFL
         foreach (var tss in ixnTSSAtFrameEnd) {
            if (tss.IsProcessed == false && (tss.EndX - StartX).LTEQ (MaxFL))
               FrameToolScopesH22.Add (tss);
         }


         
      }

      public void FindMachinableStatus () {
         if (FrameToolScopesH11.Count == 0 && FrameToolScopesH12.Count == 0 && FrameToolScopesH21.Count == 0 && FrameToolScopesH22.Count == 0) {
            MachinableStatus = FrameMachinableStatus.Empty;
            return;
         }
         if (IsSingleHeadJob) {
            MachinableStatus = FrameMachinableStatus.Machinable;
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

      public static ToolScopeList GetUnprocessedToolScopes (ToolScopeList tss) {
         if (tss == null)
            return [];

         return [.. tss.Where (ts => ts != null && !ts.IsProcessed)];
      }

      public bool IsEmpty {
         get {
            if (Bucket11ToolScopes.Count == 0 && Bucket12ToolScopes.Count == 0 &&
               Bucket21ToolScopes.Count == 0 && Bucket22ToolScopes.Count == 0)
               return true;
            return false;
         }
      }

      public bool IsMasterToolScopesEmpty {
         get {
            if (Bucket11ToolScopes.Count == 0 && Bucket12ToolScopes.Count == 0)
               return true;
            return false;
         }
      }

      public bool IsSlaveToolScopesEmpty {
         get {
            if (Bucket21ToolScopes.Count == 0 && Bucket22ToolScopes.Count == 0)
               return true;
            return false;
         }
      }

      public readonly ToolScopeList FrameToolScopes(IGCodeGenerator.ToolHeadType headType) {
         ToolScopeList tss;
         if (headType == ToolHeadType.Master || headType == ToolHeadType.MasterB2)
            tss = [.. FrameToolScopesH11, .. FrameToolScopesH12];
         else
            tss = [.. FrameToolScopesH21, .. FrameToolScopesH22];
         return tss;
      }

      public readonly ToolScopeList FrameToolScopesPerBucket (IGCodeGenerator.ToolHeadType headType) {
         ToolScopeList tss = [];
         if (headType == ToolHeadType.Master)
            tss = FrameToolScopesH11;
         else if (headType == ToolHeadType.MasterB2)
            tss = FrameToolScopesH12;
         else if (headType == ToolHeadType.Slave)
            tss = FrameToolScopesH21;
         else if (headType == ToolHeadType.SlaveB2)
            tss = FrameToolScopesH22;
         return tss;
      }
      public readonly (double XStart, double XPartition, double XEnd) GetXMarkers () {
         double? b11XStart = null, b11XEnd = null, b12XStart = null, b12XEnd = null;
         double? b21XStart = null, b21XEnd = null, b22XStart = null, b22XEnd = null;
         if (ToolingsH11.Count > 0)
            (b11XStart, b11XEnd) = Utils.GetScopeXExtents (ToolingsH11);
         if (ToolingsH12.Count > 0)
            (b12XStart, b12XEnd) = Utils.GetScopeXExtents (ToolingsH12);
         if ( ToolingsH21.Count > 0)
            (b21XStart, b21XEnd) = Utils.GetScopeXExtents (ToolingsH21);
         if (ToolingsH22.Count > 0)
            (b22XStart, b22XEnd) = Utils.GetScopeXExtents (ToolingsH21);

         double xStart=0, xPartition=0, xEnd = 0;
         
         // Get XStart
         if (ToolingsH11.Count > 0 && b11XStart != null )
            xStart = b11XStart.Value;
         else if ( ToolingsH12.Count > 0 && b12XStart != null )
            xStart = b12XStart.Value;
         else if (ToolingsH21.Count > 0 && b21XStart != null)
            xStart = b21XStart.Value;
         else if (ToolingsH22.Count > 0 && b22XStart != null)
            xStart = b22XStart.Value;

         // Get XPartition
         if (ToolingsH12.Count > 0 && b12XEnd != null)
            xPartition = b12XEnd.Value;
         else if ( ToolingsH21.Count > 0 && b21XStart != null )
            xPartition = b21XStart.Value;
         else if ( ToolingsH11.Count > 0 && b11XEnd != null )
            xPartition = b11XEnd.Value;
         else if (ToolingsH22.Count > 0 && b22XStart != null)
            xPartition = b22XStart.Value;

         // Get XEnd
         if (ToolingsH22.Count > 0 && b22XEnd != null)
            xEnd = b22XEnd.Value;
         else if (ToolingsH21.Count > 0 && b21XEnd != null)
            xEnd = b21XEnd.Value;
         else if (ToolingsH12.Count > 0 && b12XEnd != null)
            xEnd = b12XEnd.Value;
         else if (ToolingsH11.Count > 0 && b11XStart != null)
            xEnd = b11XStart.Value;

         return (xStart, xPartition, xEnd);
      }
      public readonly int TotalToolScopes4Head(IGCodeGenerator.ToolHeadType headType) => FrameToolScopes(headType).Count;
      public readonly int TotalToolScopes4HeadPerBucket(IGCodeGenerator.ToolHeadType headType) => FrameToolScopesPerBucket(headType).Count;
      // Returns the horizontal extent of all the frames.
      public static double GetTotalScopesXExtents (List<Frame> frames) {
         double minOfAll = 0;
         double maxOfAll = 0;
         foreach (var frame in frames) {
            double maxh11 = frame.FrameToolScopesH11.Count == 0 ? 0 : frame.FrameToolScopesH11.Max (ts => ts.EndX);
            double maxh12 = frame.FrameToolScopesH12.Count == 0 ? 0 : frame.FrameToolScopesH12.Max (ts => ts.EndX);
            double maxh21 = frame.FrameToolScopesH21.Count == 0 ? 0 : frame.FrameToolScopesH21.Max (ts => ts.EndX);
            double maxh22 = frame.FrameToolScopesH22.Count == 0 ? 0 : frame.FrameToolScopesH22.Max (ts => ts.EndX);
            maxOfAll = new[] { maxh11, maxh12, maxh21, maxh22 }.Max ();

            double minh11 = frame.FrameToolScopesH11.Count == 0 ? 0 : frame.FrameToolScopesH11.Min (ts => ts.EndX);
            double minh12 = frame.FrameToolScopesH12.Count == 0 ? 0 : frame.FrameToolScopesH12.Min (ts => ts.EndX);
            double minh21 = frame.FrameToolScopesH21.Count == 0 ? 0 : frame.FrameToolScopesH21.Min (ts => ts.EndX);
            double minh22 = frame.FrameToolScopesH22.Count == 0 ? 0 : frame.FrameToolScopesH22.Min (ts => ts.EndX);
            minOfAll = new[] { minh11, minh12, minh21, minh22 }.Min ();
         }
         return maxOfAll - minOfAll;
      }

      public static (double minStartX, double maxEndX) GetScopeXExtents (ToolScopeList toolScopes) {
         if (toolScopes.Count == 0)
            return (0, 0);

         double minStartX = double.MaxValue;
         double maxEndX = double.MinValue;

         for (int ii = 0; ii < toolScopes.Count; ii++) {
            var toolScope = toolScopes[ii];
            if (toolScope == null) continue;

            var segs = toolScope.Tooling.Segs;
            for (int jj = 0; jj < toolScope.Tooling.Segs.Count; jj++) {
               var segment = segs[jj];
               if (segment.Curve == null)
                  throw new Exception ($"Curve for {ii} th toolscope and {jj} th tool segment is null");

               double startX = segment.Curve.Start.X;
               double endX = segment.Curve.End.X;

               if (startX < minStartX)
                  minStartX = startX;

               if (endX > maxEndX)
                  maxEndX = endX;
            }
         }
         return (minStartX, maxEndX);
      }

      public readonly (double minStartX, double maxEndX) GetScopeXExtentsPerBucket (ToolHeadType headType) {
         double maxEndX;
         double minStartX;
         switch (headType) {
            case ToolHeadType.Master:
               (minStartX, maxEndX) = GetScopeXExtents (FrameToolScopesH11);
               break;
            case ToolHeadType.MasterB2:
               (minStartX, maxEndX) = GetScopeXExtents (FrameToolScopesH12);
               break;
            case ToolHeadType.Slave:
               (minStartX, maxEndX) = GetScopeXExtents (FrameToolScopesH21);
               break;
            case ToolHeadType.SlaveB2:
               (minStartX, maxEndX) = GetScopeXExtents (FrameToolScopesH22);
               break;
            default:
               throw new Exception ("Unknown headtype encountered");
         }
         return (minStartX, maxEndX);
      }

      public static double GetCumuativeSumOfScopes(Frame? frame, ToolHeadType headType) {
         if (frame == null)
            throw new ArgumentNullException (nameof (frame));
         double cumScope=0;
         if (headType == ToolHeadType.Master || headType == ToolHeadType.MasterB2) {
            cumScope += frame.Value.FrameToolScopesH11.Sum (ts => ts.EndX - ts.StartX);
            cumScope += frame.Value.FrameToolScopesH12.Sum (ts => ts.EndX - ts.StartX);
         } else {
            cumScope += frame.Value.FrameToolScopesH21.Sum (ts => ts.EndX - ts.StartX);
            cumScope += frame.Value.FrameToolScopesH22.Sum (ts => ts.EndX - ts.StartX);
         }
         return cumScope;
      }

      public static double GetCumuativeSumOfScopes (List<Frame?>? frames, ToolHeadType headType) {
         if (frames == null)
            throw new ArgumentNullException (nameof (frames));

         double cumScope = 0;
         for (int i = 0; i < frames.Count; i++) {
            if (frames[i] == null)
               throw new ArgumentException ($"Frame at index {i} cannot be null", nameof (frames));
            cumScope += GetCumuativeSumOfScopes (frames[i], headType);
         }
         return cumScope;
      }
      public static double GetCumuativeSumOfScopesPerBucket (Frame? frame, ToolHeadType headType) {
         if (frame == null)
            throw new ArgumentNullException (nameof (frame));
         double cumScope = 0;
         if (headType == ToolHeadType.Master)
            cumScope += frame.Value.FrameToolScopesH11.Sum (ts => ts.EndX - ts.StartX);
         else if (headType == ToolHeadType.MasterB2)
            cumScope += frame.Value.FrameToolScopesH12.Sum (ts => ts.EndX - ts.StartX);
         else if (headType == ToolHeadType.Slave)
            cumScope += frame.Value.FrameToolScopesH21.Sum (ts => ts.EndX - ts.StartX);
         else if (headType == ToolHeadType.SlaveB2)
            cumScope += frame.Value.FrameToolScopesH22.Sum (ts => ts.EndX - ts.StartX);
         else
            throw new Exception ($"Undefined headtype {headType}. No action ");

            return cumScope;
      }

      public static (double xMinStart, double xMaxEnd) GetScopeXExtents (Frame? frame, ToolHeadType headType) {
         if ( frame == null )
         throw new ArgumentNullException(nameof (frame));
         double xStart, xEnd;
         if (headType == ToolHeadType.Master || headType == ToolHeadType.MasterB2) {
            var (xStart1, xEnd1) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH11);
            var (xStart2, xEnd2) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH12);
            xStart = xStart1 < xStart2 ? xStart1 : xStart2;
            xEnd = xEnd1 > xEnd2 ? xEnd1 : xEnd2;
         } else {
            var (xStart1, xEnd1) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH21);
            var (xStart2, xEnd2) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH22);
            xStart = xStart1 < xStart2 ? xStart1 : xStart2;
            xEnd = xEnd1 > xEnd2 ? xEnd1 : xEnd2;
         }
         return (xStart, xEnd);
      }

      public static (double xMinStart, double xMaxEnd) GetScopeXExtentsPerBucket (Frame? frame, ToolHeadType headType) {
         if (frame == null)
            throw new ArgumentNullException (nameof (frame));

         double xStart, xEnd;

         switch (headType) {
            case ToolHeadType.Master:
               (xStart, xEnd) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH11);
               break;

            case ToolHeadType.MasterB2:
               (xStart, xEnd) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH12);
               break;

            case ToolHeadType.Slave:
               (xStart, xEnd) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH21);
               break;

            case ToolHeadType.SlaveB2:
               (xStart, xEnd) = Frame.GetScopeXExtents (frame.Value.FrameToolScopesH22);
               break;

            default:
               throw new ArgumentException ($"Unsupported head type: {headType}", nameof (headType));
         }

         return (xStart, xEnd);
      }

      // This finds the bound of the master and masterB2  OR
      // Slave and SlaveB2.
      public readonly Bound3 GetBounds (ToolHeadType headType) {
         Bound3 bound = new ();
         if (headType == ToolHeadType.Master || headType == ToolHeadType.MasterB2) {
            foreach (var ts in FrameToolScopesH11) {
               foreach (var seg in ts.Tooling.Segs) {
                  var b1 = seg.Curve.Bounds;
                  bound += b1;
               }
            }
            foreach (var ts in FrameToolScopesH12) {
               foreach (var seg in ts.Tooling.Segs) {
                  var b1 = seg.Curve.Bounds;
                  bound += b1;
               }
            }
         } else {
            foreach (var ts in FrameToolScopesH21) {
               foreach (var seg in ts.Tooling.Segs) {
                  var b1 = seg.Curve.Bounds;
                  bound += b1;
               }
            }
            foreach (var ts in FrameToolScopesH22) {
               foreach (var seg in ts.Tooling.Segs) {
                  var b1 = seg.Curve.Bounds;
                  bound += b1;
               }
            }
         }
            return bound;
      }
   }

}