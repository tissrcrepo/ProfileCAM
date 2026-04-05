using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#nullable enable
namespace ChassisCAM.Core.Optimizer {
   public class ProcessedFrameResult {
      LinkedListNode<ToolScope<Tooling>>? mSNode = null;
      LinkedListNode<ToolScope<Tooling>>? mENode = null;
      double mMcTime = double.MaxValue;
      public ProcessedFrameResult (LinkedListNode<ToolScope<Tooling>>? sNode, LinkedListNode<ToolScope<Tooling>>? eNode,
         double time) {
         mSNode = sNode;
         mENode = eNode;
         mMcTime = time;
      }
   }
}
