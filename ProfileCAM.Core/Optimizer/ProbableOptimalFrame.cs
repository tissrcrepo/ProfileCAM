using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProfileCAM.Core.Optimizer {
   public struct ProbableOptimalFrame {
      public LinkedList<ToolScope<Tooling>> ToolScopes = [];

      public ProbableOptimalFrame (IEnumerable<ToolScope<Tooling>> scopes) {
         foreach (var ts in scopes)
            ToolScopes.AddLast (ts.Clone ());
      }
   }
}
