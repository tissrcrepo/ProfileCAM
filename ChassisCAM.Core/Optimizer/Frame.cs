#nullable enable
using ChassisCAM.Core.Optimizer;
using ChassisCAM.Core;
using Flux;
using ChassisCAM.Core.Geometries;
using System.Linq.Expressions;

public struct Frame {
   public List<ToolScope<Tooling>> FrameToolScopesList { get; set; } = [];
   public List<ToolScope<Tooling>> AllToolScopesList { get; set; } = [];
   public List<ToolScope<Tooling>> FrameToolScopesBySX { get; set; } = [];
   public List<ToolScope<Tooling>> FrameToolScopesByEX { get; set; } = [];

   public List<ToolScope<Tooling>> FrameToolScopesH11 { get; set; } = [];
   public List<ToolScope<Tooling>> FrameToolScopesH12 { get; set; } = [];
   public List<ToolScope<Tooling>> FrameToolScopesH21 { get; set; } = [];
   public List<ToolScope<Tooling>> FrameToolScopesH22 { get; set; } = [];

   public double MinFL { get; set; } = 0;
   public double MaxFL { get; set; } = 0;

   public double StartX { get; set; } = 0;
   public double EndX { get; set; } = 0;

   public double Head11EndX { get; set; } = 0;
   public double Head12EndX { get; set; } = 0;
   public double Head21EndX { get; set; } = 0;

   public double Head22EndX { readonly get => EndX; set => EndX = value; }
   public double Head11StartX { readonly get => StartX; set => StartX = value; }

   // Replace node references with indices
   public int StartIndex { get; set; }
   public int EndIndex { get; set; }
   public double Tol { get; set; } = 1e-6;
   double A, B, C;

   public Frame () { }

   public Frame (
       List<ToolScope<Tooling>> allToolScopes,
       List<ToolScope<Tooling>> frameToolScopes,
       double minFL,
       double maxFL,
       int startIndex,
       int endIndex,
       double tol = 1 - 6) {
      Tol = tol;
      AllToolScopesList = allToolScopes ?? throw new FrameNotProcessableException ("Empty frame");
      FrameToolScopesList = frameToolScopes ?? throw new FrameNotProcessableException ("Empty frame");

      StartIndex = startIndex;
      EndIndex = endIndex;

      MinFL = minFL;
      MaxFL = maxFL;

      // Sort once → reuse
      FrameToolScopesBySX = [.. FrameToolScopesList.OrderBy (ts => ts.StartX)];

      FrameToolScopesByEX = [.. FrameToolScopesList.OrderBy (ts => ts.EndX)];

      if (FrameToolScopesBySX.Count == 0)
         throw new InvalidOperationException ("FrameToolScopesBySX empty");

      StartX = FrameToolScopesBySX[0].StartX;

      if (FrameToolScopesByEX.Count == 0)
         throw new InvalidOperationException ("FrameToolScopesByEX empty");

      EndX = FrameToolScopesByEX[^1].EndX;

      var delta = ((EndX - StartX) / 2) - MinFL;

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
         var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, b1, startIndex: 0, tol: tol);

         if (ixnTSSAtFrameCenter.Count > 0) {
            needToLeftOffset = true;
            var ixnTSSAtFrameCenterBounds = Utils.GetBounds (ixnTSSAtFrameCenter);
            if (ixnTSSAtFrameCenterBounds != null) {

               leftOffset = -(b1 - ixnTSSAtFrameCenterBounds.Value.MinStartX);
               //rightSideGap = ixnTSSAtFrameCenterBounds.Value.MaxEndX - B;
               //if ((leftSideGap).SLT (rightSideGap)) // ixn StartX is closer to B
               //leftOffset = -leftSideGap;
               //else offset = rightSideGap;
            }
            if (leftOffset > 30.0)
               leftOffsetStrategy = false;

         } else
            break;
         b1 += leftOffset;
         cumLeftOffset += leftOffset;
         //// Checking
         //ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, startIndex: 0, tol: tol);
         //if (ixnTSSAtFrameCenter != null && ixnTSSAtFrameCenter.Count > 0)
         //   throw new Exception ("IXN toolscopes at B found even after offset");
      }

      while (true) {
         var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, b2, startIndex: 0, tol: tol);

         if (ixnTSSAtFrameCenter.Count > 0) {
            needToRightOffset = true;
            var ixnTSSAtFrameCenterBounds = Utils.GetBounds (ixnTSSAtFrameCenter);
            if (ixnTSSAtFrameCenterBounds != null) {

               //leftSideGap = B - ixnTSSAtFrameCenterBounds.Value.MinStartX;
               rightOffset = ixnTSSAtFrameCenterBounds.Value.MaxEndX - b2;
               //if ((leftSideGap).SLT (rightSideGap)) // ixn StartX is closer to B
               //rightOffset = rightSideGap;
               //else offset = rightSideGap;
            } else
               break;
            if (rightOffset > 30.0)
               rightOffsetStrategy = false;

            b2 += rightOffset;
            cumRightOffset += rightOffset;

         } else
            break;
         //// Checking
         //ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, startIndex: 0, tol: tol);
         //if (ixnTSSAtFrameCenter != null && ixnTSSAtFrameCenter.Count > 0)
         //   throw new Exception ("IXN toolscopes at B found even after offset");
      }
      if (needToLeftOffset || needToRightOffset) {
         if (!leftOffsetStrategy && !rightOffsetStrategy) {
            var offset = (leftOffset > rightOffset) ? leftOffset : rightOffset;
            throw new Exception ($"The arrangement of toolscopes occuring at the middle intersects with middle line ( bucker boundary between H12 and H21) and the offset middle line is computed to be more than {offset}");
         }

         if (leftOffsetStrategy && rightOffsetStrategy) {
            var offset = (cumLeftOffset < cumRightOffset) ? cumLeftOffset : cumRightOffset;
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
      CollectToolScopesInBuckets ();
      CheckToolScopesIXNAtXPositions ();
      CheckCountConsistency ();
      CheckMinFLConsistency ();
   }

   void CheckCountConsistency () {
      if (FrameToolScopesH11.Count + FrameToolScopesH12.Count + FrameToolScopesH21.Count + FrameToolScopesH22.Count != FrameToolScopesList.Count)
         throw new Exception ("Total tool scopes in the buckets NOT EQUAL to Count in FrameToolScopesList");
   }
   void CheckMinFLConsistency () {
      if ((C - B).SLT (MinFL))
         throw new Exception ($"The bucket for H2 => H21 {C - B} size lesser than {MinFL} mm");
      if ((B - A).SLT (MinFL))
         throw new Exception ($"The bucket for H2 => H21 {B - A} size lesser than {MinFL} mm");
   }
   public readonly void CheckToolScopesIXNAtXPositions (double tol = 1e-6) {
      // there is no need to find the intersections at A and C. The intersecting tools scopes 
      // at A, are added to FrameToolScopesH12 since the left head can move in -X direction
      // at C, are added to FrameToolScopesH21 since the right head can always move in +X direction
      //var atA = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, A, startIndex: 0, tol: tol);
      //if (atA.Count > 0) 
      //   throw new Exception ("ToolScopes intersect at A");
      var atB = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, startIndex: 0, tol: tol);
      if (atB.Count > 0) throw new Exception ("ToolScopes intersect at B");
      //var atC = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, C, startIndex: 0, tol: tol);
      //if (atC.Count > 0) throw new Exception ("ToolScopes intersect at C");

   }
   void CollectToolScopesInBuckets (double tol = 1e-6) {
      double A = Head11EndX;
      double B = Head12EndX;
      double C = Head21EndX;



      var tssAllWithin = PartMultiFrames.GetToolScopesWithin (
          FrameToolScopesList, StartX, EndX);

      if (tssAllWithin.Count == 0)
         return;




      // Collect tool scopes within H11
      FrameToolScopesH11 = PartMultiFrames.GetToolScopesWithin (
          FrameToolScopesList, StartX, A);
      // Intersecting tss at A is collected by FrameToolScopesH12





      // Collect tool scopes within H12
      FrameToolScopesH12 = PartMultiFrames.GetToolScopesWithin (
          FrameToolScopesList, A, B);
      // cross the "A" line and are safe w.r.t head2 as it only increases the distance between
      var ixnTSSAtA = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, A, startIndex: 0, tol: tol);
      FrameToolScopesH12.AddRange (ixnTSSAtA);




      // Collect tool scopes within H21
      FrameToolScopesH21 = PartMultiFrames.GetToolScopesWithin (
          FrameToolScopesList, B, C);

      // Intersecting Tss at C added to H21: Reason : The endx of the ixn tss
      // cross the "C" line and are safe w.r.t head1 as it only increases the distance between
      var ixnTSSAtC = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, C, startIndex: 0, tol: tol);
      FrameToolScopesH21.AddRange (ixnTSSAtC);




      // Collect tool scopes within H22
      FrameToolScopesH22 = PartMultiFrames.GetToolScopesWithin (
          FrameToolScopesList, C, EndX);


      // Find the intersecting tool scopes at EndX
      var ixnTSSAtFrameEnd = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, EndX, startIndex: 0, tol: tol);

      //List<ToolScope<Tooling>> ixnToAdd = [];

      // Check the length between Frame StartX and the itersecting toolscopes Endx 
      // is <= MaxFL. If so the intersecting tool scopes. Otherwise, it is not feasible 
      // as the frame length including the end of the ixn tool scope is more than MaxFL
      foreach (var tss in ixnTSSAtFrameEnd) {
         if ((tss.EndX - StartX).LTEQ (MaxFL))
            FrameToolScopesH22.Add (tss);
      }
      //FrameToolScopesH22.AddRange (ixnToAdd);


      //// Handle intersection at B
      //var ixnTSSAtFrameCenter = PartMultiFrames.GetToolScopesIxnAt (FrameToolScopesList, B, startIndex: 0, tol: tol);
      //var h11Bounds = Utils.GetBounds (FrameToolScopesH11);
      //var h21Bounds = Utils.GetBounds (FrameToolScopesH21);
      //var h22Bounds = Utils.GetBounds (FrameToolScopesH22);
      //var h12Bounds = Utils.GetBounds (FrameToolScopesH12);

      //List<ToolScope<Tooling>> unProcessedTSSAtCen = [];
      //foreach(var cenIxnTSS in ixnTSSAtFrameCenter) {
      //   if ( h22Bounds != null && h11Bounds != null ) {
      //      if ( (h22Bounds.Value.MinStartX- cenIxnTSS.EndX).GTEQ(MinFL, tol) )
      //         FrameToolScopesH12.Add (cenIxnTSS);
      //      else if ( (h11Bounds.Value.MaxEndX-cenIxnTSS.StartX).GTEQ(MinFL, tol ))
      //         FrameToolScopesH21.Add (cenIxnTSS);
      //   }
      //}


   }
   public bool IsPossibleToMachine (double tol = 1e-6) {
      bool possibleToMachine = true;
      if (FrameToolScopesH11.Count == 0 && FrameToolScopesH22.Count == 0)
         possibleToMachine = Frame.TryBuildIndependentToolScopesWithinMinFrameLength (FrameToolScopesH12, FrameToolScopesH21, MinFL, tol);
      return possibleToMachine;
   }
   public static bool TryBuildIndependentToolScopesWithinMinFrameLength (
    List<ToolScope<Tooling>> h12,
    List<ToolScope<Tooling>> h21,
    double minFL,
    double tol = 1e-6) {
      //h1Schedule = [];
      //h2Schedule = [];

      // Waste case: both empty
      if (h12.Count == 0 && h21.Count == 0)
         return false;

      // Only H21 exists
      if (h12.Count == 0) {
         //h2Schedule = [.. h21.OrderBy (ts => ts.StartX)];
         return true;
      }

      // Only H12 exists
      if (h21.Count == 0) {
         //h1Schedule = [.. h12.OrderBy (ts => ts.StartX)];
         return true;
      }

      // Both exist → check global safety
      double maxH12 = h12.Max (ts => ts.EndX);
      double minH21 = h21.Min (ts => ts.StartX);

      if ((minH21 - maxH12).SLT (minFL, tol))
         return false;

      //// Safe → independent execution
      //h1Schedule = [.. h12.OrderBy (ts => ts.StartX)];
      //h2Schedule = [.. h21.OrderBy (ts => ts.StartX)];

      return true;
   }

   public void GetOptimumSubFrame () {

   }
}