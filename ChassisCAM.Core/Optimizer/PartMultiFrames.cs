using Flux.API;
using ChassisCAM.Core.Optimizer;
using ChassisCAM.Core.Geometries;
using Flux;
using ChassisCAM.Core.GCodeGen;
using System.Collections.Generic;

namespace ChassisCAM.Core.Optimizer;
#nullable enable
public class PartMultiFrames {
   public Workpiece Work { get; }
   public double MaxFrameLength { get; }
   public double MinFrameLength { get; }
   public LinkedList<ToolScope<Tooling>> ToolScopes { get; } = [];
   public List<ToolScope<Tooling>> ToolScopesList { get; } = [];
   //public LinkedList<ToolScope<Tooling>> ToolScopesBySx { get; private set; } = [];
   public List<ToolScope<Tooling>> ToolScopesBySxList { get; private set; } = [];
   //public LinkedList<ToolScope<Tooling>> ToolScopesByEx { get; private set; } = [];
   public List<ToolScope<Tooling>> ToolScopesByExList { get; private set; } = [];
   public SortedDictionary<(int ii, int jj), CandidateFrame> CandidateFrames1 { get; private set; } = [];
   public SortedDictionary<int, List<(int jj, CandidateFrame cf)>> CandidateFrames2 = [];
   public List<(int, int)> FrameHeaders { get; private set; } = [];
   public List<ToolScope<Tooling>> ToolScopesExListBetween (int st, int end) {
      if (end < st)
         throw new Exception ("Start index should be lessert than end index");
      List<ToolScope<Tooling>> res = [];
      for (int ii = st; ii <= end; ii++)
         res.Add (ToolScopesByExList[ii]);
      return res;
   }
   public int Count { get { return ToolScopes.Count; } }
   public PartMultiFrames (GCodeGenerator gcGen, double minFL, double tol = 1e-6) {
      ArgumentNullException.ThrowIfNull (gcGen);
      Work = gcGen.Process.Workpiece;
      MaxFrameLength = gcGen.MaxFrameLength;
      MinFrameLength = minFL;

      // Pre-allocate (important for performance)
      //ToolScopes = new LinkedList<ToolScope<Tooling>> ();

      for (int ii = 0; ii < Work.Cuts.Count; ii++) {
         // Temporary to work only for holes. TEMP_TEMP
         if (Utils.IsHoleFeature (Work.Cuts[ii], Work.Bound, gcGen.MinCutOutLengthThreshold)) {
            ToolScopes.AddLast (new ToolScope<Tooling> (Work.Cuts[ii], ii));
            if (ToolScopes.Last == null) throw new Exception ("ToolScope is null");
            ToolScopesList.Add (ToolScopes.Last.Value);
         }
      }


      //for (int ii = 0; ii < ToolScopesList.Count; ii++)
      //   ToolScopesList[ii].Index = ii;

      // Sort by StartX
      ToolScopesBySxList = [.. ToolScopes.OrderBy (ts => ts.StartX)];

      for (int ii = 0; ii < ToolScopesBySxList.Count; ii++)
         ToolScopesBySxList[ii].IndexSx = ii;


      //ToolScopesBySx = new (ToolScopesBySxList);

      // Sort by EndX
      ToolScopesByExList = [.. ToolScopes.OrderBy (ts => ts.EndX)];

      for (int ii = 0; ii < ToolScopesByExList.Count; ii++)
         ToolScopesByExList[ii].IndexEx = ii;


      //ToolScopesByEx = new (ToolScopesByExList);


      for (int ii = 0; ii < ToolScopesBySxList.Count; ii++) {
         int lastJj = -1;

         double ii_thSx = ToolScopesBySxList[ii].StartX;
         double ii_thEndX = ToolScopesBySxList[ii].EndX;

         for (int jj = 0; jj < ToolScopesBySxList.Count; jj++) {
            // If the toolscopes pointed to by both ii and jj are the same, continue
            if (ToolScopesBySxList[ii] == ToolScopesBySxList[jj])
               continue;

            double jj_thSx = ToolScopesBySxList[jj].StartX;
            double jj_thEndX = ToolScopesBySxList[jj].EndX;



            // Ignore the toolscopes of previous frames
            if (jj_thEndX.LTEQ (ii_thSx) || jj_thSx.SLT (ii_thSx))
               continue;

            var scope = jj_thEndX - ii_thSx;
            // The frame scope should at least be MinFrameLength wide
            if (scope.SLT (MinFrameLength))
               continue;

            // If the scope is more than MaxFL break this loop
            if (scope.SGT (MaxFrameLength))
               break;

            // Terminating criterion:
            if ((jj_thSx - ii_thSx).SGT (MaxFrameLength))
               break;

            var cf = new CandidateFrame (ToolScopesBySxList, ii, jj, tol);

            CandidateFrames1[(ii, jj)] = cf;
            CandidateFrames2[ii] = [(jj, cf)];

            lastJj = jj; // track last valid jj for THIS ii
         }

         if (lastJj != -1)
            FrameHeaders.Add ((ii, lastJj));

         // Debug test assertion
         for (int kk = 0; kk < FrameHeaders.Count; kk++) {
            int sindex = FrameHeaders[kk].Item1;
            int eIndex = FrameHeaders[kk].Item2;
            var ex = ToolScopesBySxList[eIndex].EndX;
            var sx = ToolScopesBySxList[sindex].StartX;
            if ((ex - sx).SGT (MaxFrameLength))
               throw new Exception ($"In ToolScopesBySxList, the scope of tool scopes from {sx} to {ex}  = {ex - sx} for indices start {sindex} and end {eIndex} is more than Max Frame Length {MaxFrameLength}");
         }
      }

      List<Frame> candFRames = [];
      foreach (var kvp in CandidateFrames2) {
         int ii = kvp.Key;
         var cfList = kvp.Value;

         for (int jj = 0; jj < cfList.Count; jj++) {
            var val = cfList[jj];

            var frame = new Frame (
                ToolScopesList,
                val.cf.ToolScopesList,
                MinFrameLength,
                MaxFrameLength,
                ii,
                val.jj);
            candFRames.Add (frame);

            int aa = 0;
            ++aa;
         }
      }
   }

   public static (LinkedListNode<ToolScope<Tooling>> ToolScopes, int Index)? GetFirstUnprocessedNode (
    LinkedList<ToolScope<Tooling>> toolScopes,
    LinkedListNode<ToolScope<Tooling>>? startNode = null) {
      var node = startNode ?? toolScopes.First;

      while (node != null) {
         if (!node.Value.IsProcessed)
            return (node, node.Value.IndexSx);

         node = node.Next;
      }

      return null; // none found
   }

   public static (ToolScope<Tooling> ToolScope, int Index)? GetFirstUnprocessedNode (
    List<ToolScope<Tooling>> toolScopes,
    int startIndex = 0) {
      if (toolScopes == null)
         throw new ArgumentNullException (nameof (toolScopes));

      for (int ii = startIndex; ii < toolScopes.Count; ii++) {
         if (!toolScopes[ii].IsProcessed)
            return (toolScopes[ii], toolScopes[ii].IndexSx); // or toolScopes[ii].IndexSx if you prefer
      }

      return null;
   }
   // Finds and adds the features that intersect with every feature's EndX line
   // starting from sNode of toolScopes. if sNode = null, it starts from First node
   //public static void FindFeaturesIxnEx (LinkedList<ToolScope<Tooling>> toolScopes, LinkedListNode<ToolScope<Tooling>>? sNode = null) {
   //   LinkedListNode<ToolScope<Tooling>>? node1 = sNode;
   //   node1 ??= toolScopes.First;
   //   while (node1 != null) {

   //      var next1 = node1.Next; // IMPORTANT (save before delete)

   //      var toolscope = node1.Value;
   //      toolscope.ToolScopeIxnsbyEndX = [];
   //      node1.Value = toolscope;


   //      var node2 = node1.Next;
   //      while (node2 != null) {
   //         var next2 = node1.Next;

   //         // If the StartX of node2 > EndX of node1 further checks are not necessary
   //         // Terminal condition
   //         // Case 1
   //         //  Node1         Sx--------Ex
   //         //  Node2                        Sx-----------------------Ex
   //         if (node2.Value.StartX.SGT (node1.Value.EndX))
   //            break;

   //         // Case 1
   //         //  Node1         Sx--------Ex
   //         //  Node2   Sx-----------------------Ex
   //         // Case 2
   //         // Node1    Sx--------Ex
   //         // Node2    Sx-----------------------Ex
   //         // If the node2's StartX is < Node1's EndX and 
   //         if (node2.Value.StartX.SLT (node1.Value.EndX) && node2.Value.EndX.SGT (node1.Value.EndX) ||
   //            node2.Value.EndX.SGT (node1.Value.EndX))
   //            node1.Value.ToolScopeIxnsbyEndX.Add (node2.Value);
   //         node2 = next2;
   //      }

   //      node1 = next1;
   //   }
   //}

   public static void FindToolScopesIxnEx (
    LinkedList<ToolScope<Tooling>> toolScopes,
    LinkedListNode<ToolScope<Tooling>>? sNode = null) {
      var node1 = sNode ?? toolScopes.First;

      while (node1 != null) {
         var next1 = node1.Next;

         // Get intersections at EndX of current node
         var ixns = GetToolScopesIxnAt (toolScopes, node1.Value.EndX, node1.Next);

         // Since ToolScope is struct → copy-modify-assign back
         var toolscope = node1.Value;
         if (ixns != null)
            toolscope.ToolScopeIxnsbyEndX = [.. ixns]; // convert LinkedList → List

         node1.Value = toolscope;

         node1 = next1;
      }
   }


   //  public static LinkedList<ToolScope<Tooling>>? GetToolScopesIxnAt (
   //LinkedList<ToolScope<Tooling>> toolScopes,
   //double xVal,
   //LinkedListNode<ToolScope<Tooling>>? sNode = null) {
   //     var res = new LinkedList<ToolScope<Tooling>> ();

   //     var node1 = sNode ?? toolScopes.First;

   //     while (node1 != null) {
   //        var next1 = node1.Next;

   //        var node2 = node1.Next;

   //        while (node2 != null) {
   //           var next2 = node2.Next; // ✔️ FIXED

   //           // Terminal condition
   //           if (node2.Value.StartX.SGT (xVal))
   //              break;

   //           // Overlap / intersection condition
   //           if (
   //               (node2.Value.StartX.SLT (xVal) && node2.Value.EndX.SGT (xVal)) ||
   //               node2.Value.EndX.SGT (xVal)
   //           ) {
   //              res.AddLast (node2.Value); // ✔️ add to result
   //           }

   //           node2 = next2;
   //        }

   //        node1 = next1;
   //     }

   //     return res;
   //  }

   public static LinkedList<ToolScope<Tooling>> GetToolScopesIxnAt (
    LinkedList<ToolScope<Tooling>> toolScopes,
    double xVal,
    LinkedListNode<ToolScope<Tooling>>? sNode = null) {
      var res = new LinkedList<ToolScope<Tooling>> ();

      var node = sNode ?? toolScopes.First;

      int ii = 0; // optional, if you want index tracking

      while (node != null) {
         var ts = node.Value;

         if (ts.StartX.SGT (xVal))
            break;

         if (ts.StartX.SLT (xVal) && xVal.SLT (ts.EndX)) {
            res.AddLast (ts);
         }

         node = node.Next;
         ii++; // optional
      }

      return res;
   }


   public static List<ToolScope<Tooling>> GetToolScopesIxnAt (
    List<ToolScope<Tooling>> toolScopes,
    double xVal,
    int startIndex = 0,
    double tol = 1e-6) {
      var res = new List<ToolScope<Tooling>> ();

      for (int ii = startIndex; ii < toolScopes.Count; ii++) {
         var ts = toolScopes[ii];

         // Early exit if sorted by StartX
         if (ts.StartX.SGT (xVal, tol))
            break;

         if (ts.StartX.SLT (xVal, tol) && xVal.SLT (ts.EndX, tol)) {
            res.Add (ts);
         }
      }

      return res;
   }

   public static LinkedList<ToolScope<Tooling>>? GetToolScopesWithin (LinkedList<ToolScope<Tooling>> toolScopes, double startX, double endX, LinkedListNode<ToolScope<Tooling>>? sNode = null, double tol = 1e-6) {
      var res = new LinkedList<ToolScope<Tooling>> ();

      var node1 = sNode ?? toolScopes.First;
      while (node1 != null) {
         var next1 = node1.Next;

         // Terminal condition
         if (startX.LTEQ (node1.Value.StartX, tol) && endX.LTEQ (node1.Value.EndX, tol))
            res.AddLast (node1.Value);
         node1 = next1;
      }
      return res;
   }

   public static List<ToolScope<Tooling>> GetToolScopesWithin (
    List<ToolScope<Tooling>> toolScopes,
    double startX,
    double endX,
    int startIndex = 0,
    double tol = 1e-6) {
      var res = new List<ToolScope<Tooling>> ();

      for (int ii = startIndex; ii < toolScopes.Count; ii++) {
         var ts = toolScopes[ii];

         // Terminal condition
         if (startX.LTEQ (ts.StartX, tol) && ts.EndX.LTEQ (endX, tol)) {
            res.Add (ts);
         }
      }

      return res;
   }

   //public List<ToolScope<Tooling>> GetToolScopesWithin (
   // int startIndex, // By StartX sorted list
   // int endIndex, // By EndX sorted list
   // double tol = 1e-6) {
   //   List<ToolScope<Tooling>> res = [];
   //   res.Add (ToolScopesBySxList[startIndex]);

   //   for (int ii = startIndex; ii < endIndex; ii++) {
   //      //if (ii == startIndex) {
   //      //   var tsBounds = Utils.GetBounds ([ToolScopesByExList[ii]]);
   //      //   if (ToolScopesByExList[ii].S.LTEQ(tsBounds.Value.MinStartX) && tsBounds.Value.MaxEndX.LTEQ(endIndex))
   //      //}
   //      var firstTSSxBound = Utils.GetBounds ([ToolScopesBySxList[startIndex]]);

   //      var (StartXOfExListTSS, _) = Utils.GetBounds ([ToolScopesByExList[ii]]) ?? throw new Exception ($"Bounds is null for tsExBound[{ii}]");
   //      if (firstTSSxBound == null ) throw new Exception ($"Bounds is null for firstTSSxBound[{ii}]");
   //      if (StartXOfExListTSS.SLT (firstTSSxBound.Value.MinStartX))
   //         throw new Exception ("Start Index and end Index ordered does not lie with in the scope");

   //      res.Add (ToolScopesByExList[ii]);
   //   }
   //   return res;
   //}

   public List<ToolScope<Tooling>> GetToolScopesWithin (
    int startIndex, // By StartX sorted list
    int endIndex, // By EndX sorted list
    double tol = 1e-6) {
      List<ToolScope<Tooling>> res = [];
      for (int ii = startIndex; ii <= endIndex; ii++)
         res.Add (ToolScopesList[ii]);

      return res;
   }

   public static List<ToolScope<Tooling>> GetToolScopesWithin (
      List<ToolScope<Tooling>> toolScopes,
    int startIndex, // By StartX sorted list
    int endIndex, // By EndX sorted list
    double tol = 1e-6) {
      List<ToolScope<Tooling>> res = [];
      for (int ii = startIndex; ii <= endIndex; ii++)
         res.Add (toolScopes[ii]);

      return res;
   }

   void Optimize () {
      while (true) {
         while (true) {
         }
      }
   }
   //public static Frame? GetFrame (PartMultiFrames partMultiFrames, LinkedListNode<ToolScope<Tooling>>? sNode = null, LinkedListNode<ToolScope<Tooling>>? eNode = null) {

   //   //// Remove already processed features from partMultiFrames.ToolScopesBySx
   //   //var node = partMultiFrames.ToolScopesBySx.First;
   //   //while (node != null) {
   //   //   var next = node.Next; // IMPORTANT (save before delete)

   //   //   var toolscope = node.Value;

   //   //   // If already processed, remove from linked list
   //   //   if (toolscope.IsProcessed) {
   //   //      partMultiFrames.ToolScopesBySx.Remove (node); // O(1)
   //   //   }

   //   //   node = next;
   //   //}

   //   //// Remove already processed features from partMultiFrames.ToolScopesByEx
   //   //node = partMultiFrames.ToolScopesByEx.First;
   //   //while (node != null) {
   //   //   var next = node.Next; // IMPORTANT (save before delete)

   //   //   var toolscope = node.Value;

   //   //   // If already processed, remove from linked list
   //   //   if (toolscope.IsProcessed) {
   //   //      partMultiFrames.ToolScopesByEx.Remove (node); // O(1)
   //   //   }

   //   //   node = next;
   //   //}

   //   // Find out the toolings that are FSx <= TsSx && TsEx <= FEx ToolScope completely inside the frame
   //   // Remove already processed features from partMultiFrames.ToolScopesByEx
   //   var node = partMultiFrames.ToolScopesByEx.First;
   //   while (node != null) {
   //      var next = node.Next; // IMPORTANT (save before delete)

   //      var toolscope = node.Value;
   //      if (!toolscope.IsProcessed) {

   //      }
   //      // If already processed, remove from linked list
   //      //if (toolscope.IsProcessed) {
   //      //   partMultiFrames.ToolScopesByEx.Remove (node); // O(1)
   //      //}

   //      node = next;
   //   }
   //}
}