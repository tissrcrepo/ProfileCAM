using Flux.API;
using ChassisCAM.Core.Geometries;

namespace ChassisCAM.Core.Optimizer;

public struct Frame {
   public List<ToolScope<Tooling>> ToolScopes { get; set; } = [];
   public Frame () { }
}
