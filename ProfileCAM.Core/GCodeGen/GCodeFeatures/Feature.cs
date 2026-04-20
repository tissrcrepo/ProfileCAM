using ProfileCAM.Core.Geometries;

namespace ProfileCAM.Core.GCodeGen.GCodeFeatures {
   /// <summary>
   /// This abstract class captures the abstract methods that a ToolingFeature to be tooled
   /// shall implement
   /// </summary>
   public abstract class ToolingFeature {
      /// <summary>
      /// Property to set or get the Tooling Segments that this ToolingFeature holds
      /// </summary>
      public abstract List<ToolingSegment> ToolingSegments { get; set; }

      /// <summary>
      /// This method writes the G Code for the ToolingFeature, comprehensively.
      /// </summary>
      public abstract void WriteTooling ();

      /// <summary>
      /// This method returns the most recent previously tooled segment. This is very 
      /// significant when trying to associate and plan the rapid position from previously
      /// tooled segment of previous tooling to the first segment of the current tooling, more so
      /// in valid notches, when the tooling segments shall be edited to the requirements
      /// </summary>
      /// <returns>The most recent previously tooled segment</returns>
      public abstract ToolingSegment? GetMostRecentPreviousToolingSegment ();
   }
}