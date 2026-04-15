using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProfileCAM.Core.Optimizer {
   public struct CandidateFrame {
      public CandidateFrame (List<ToolScope<Tooling>> toolScopesList, int sIndex, int eIndex, double tol = 1e-6) {
         if (toolScopesList == null)
            throw new ArgumentException ("CandidateFrame requires non-null toolScopesList");

         ToolScopesList = PartMultiFrames.GetToolScopesWithin (toolScopesList,
                sIndex,
                eIndex,
                tol: tol);

         var bound = Utils.GetBounds (ToolScopesList);
         if (bound != null) {
            StartX = bound.Value.MinStartX;
            EndX = bound.Value.MaxEndX;
         }
         StartIndex = sIndex;
         EndIndex = eIndex;
      }
      public int StartIndex { get; set; } = -1;
      public int EndIndex { get; set; } = -1;
      public double StartX { get; set; }
      public double EndX { get; set; }
      public List<ToolScope<Tooling>> ToolScopesList { get; set; } = [];

      public double ScopeLength => EndX - StartX;
   }
}
