using Flux.API;

namespace ChassisCAM.Input;
public enum GroupCode {
   MainEntStartEnd = 0,
   Name = 2,
   LayerName = 8,
   SysVar = 9,
   XCoordStart = 10,
   YCoordStart = 20,
   ZCoordStart = 30,
   XCoordEnd = 11,
   YCoordEnd = 21,
   ZCoordEnd = 31,
   FloatValue = 40,
   DashElemLength = 49,
   ColorIndex = 62,
   LineTypeFlag = 70,
   LineTypeScaleFactor = 72,
   NElementsInLineType = 73,
   Thickness = 39,
   Angle = 50,
   Visibility = 60,
   TextValue = 1,
   TextStyle = 7,
   TextHeight = 40,
   TextRotation = 50,
   TextAlignment = 72,
   VerticalTextAlign = 73,
   ExtrusionX = 210,
   ExtrusionY = 220,
   ExtrusionZ = 230,
   BlockName = 2,
   CustomAnnoAttr = 1000,
   AppName = 1001,
   ControlString = 1002,
   LayerNameXD = 1003,
   BinaryData = 1004,
   Handle = 1005,
   PolylineFlag = 66,
   Bulge = 42,
   SubclassMarker = 100,
   LinetypeDescription = 3,
   LinetypeAlignment = 72
}

public enum SegVertexType {
   Start,
   End
}

public class DXFWriter (string filePath, PartData pData) {
   string mDXFFile = filePath;
   PartData mPartData = pData;
   public void WriteDXF () {
      try {
         using (var sw = new StreamWriter (mDXFFile)) {
            WriteHeader (sw);
            WriteTables (sw);
            WriteBlocks (sw);
            WriteEntities (sw);
            WriteFooter (sw);
         }
      } catch (Exception ex) {
         Console.WriteLine ($"DXF writing failed with {ex.Message}");
      }
   }

   void WriteLine (StreamWriter sw, GroupCode code, object value) {
      sw.WriteLine ($"\t{(int)code}");
      sw.WriteLine (value);
   }

   void WriteHeader (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "SECTION");
      WriteLine (sw, GroupCode.Name, "HEADER");

      WriteLine (sw, GroupCode.SysVar, "$EXTMAX");
      WriteLine (sw, GroupCode.XCoordStart, mPartData.RightTop.X);
      WriteLine (sw, GroupCode.YCoordStart, mPartData.RightTop.Y);

      WriteLine (sw, GroupCode.SysVar, "$EXTMIN");
      WriteLine (sw, GroupCode.XCoordStart, mPartData.LeftBottom.X);
      WriteLine (sw, GroupCode.YCoordStart, mPartData.LeftBottom.Y);

      WriteLine (sw, GroupCode.SysVar, "$LIMMAX");
      WriteLine (sw, GroupCode.XCoordStart, mPartData.RightTop.X);
      WriteLine (sw, GroupCode.YCoordStart, mPartData.RightTop.Y);

      WriteLine (sw, GroupCode.SysVar, "$LIMMIN");
      WriteLine (sw, GroupCode.XCoordStart, mPartData.LeftBottom.X);
      WriteLine (sw, GroupCode.YCoordStart, mPartData.LeftBottom.Y);

      WriteLine (sw, GroupCode.MainEntStartEnd, "ENDSEC");
   }

   void WriteTables (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "SECTION");
      WriteLine (sw, GroupCode.Name, "TABLES");

      WriteLineTypes (sw);
      WriteLayers (sw);

      WriteLine (sw, GroupCode.MainEntStartEnd, "ENDSEC");
   }

   void WriteLineTypes (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "TABLE");
      WriteLine (sw, GroupCode.Name, "LTYPE");
      WriteLine (sw, GroupCode.LineTypeFlag, 3);

      // CONTINUOUS
      WriteLine (sw, GroupCode.MainEntStartEnd, "LTYPE");
      WriteLine (sw, GroupCode.Name, "CONTINUOUS");
      WriteLine (sw, GroupCode.LineTypeFlag, 64);
      WriteLine (sw, GroupCode.LinetypeDescription, "CONTINUOUS");
      WriteLine (sw, GroupCode.LinetypeAlignment, 65);
      WriteLine (sw, GroupCode.NElementsInLineType, 0);
      WriteLine (sw, GroupCode.FloatValue, 1.0);

      // DOT
      WriteLine (sw, GroupCode.MainEntStartEnd, "LTYPE");
      WriteLine (sw, GroupCode.Name, "DOT");
      WriteLine (sw, GroupCode.LineTypeFlag, 64);
      WriteLine (sw, GroupCode.LinetypeDescription, "DOT");
      WriteLine (sw, GroupCode.LinetypeAlignment, 65);
      WriteLine (sw, GroupCode.NElementsInLineType, 2);
      WriteLine (sw, GroupCode.FloatValue, 1.0);
      WriteLine (sw, GroupCode.DashElemLength, 2);
      WriteLine (sw, GroupCode.DashElemLength, -2);

      // DASHDOTDOT
      WriteLine (sw, GroupCode.MainEntStartEnd, "LTYPE");
      WriteLine (sw, GroupCode.Name, "DASHDOTDOT");
      WriteLine (sw, GroupCode.LineTypeFlag, 64);
      WriteLine (sw, GroupCode.LinetypeDescription, "DASHDOTDOT");
      WriteLine (sw, GroupCode.LinetypeAlignment, 65);
      WriteLine (sw, GroupCode.NElementsInLineType, 6);
      WriteLine (sw, GroupCode.FloatValue, 1.0);
      WriteLine (sw, GroupCode.DashElemLength, 24);
      WriteLine (sw, GroupCode.DashElemLength, -6);
      WriteLine (sw, GroupCode.DashElemLength, 3);
      WriteLine (sw, GroupCode.DashElemLength, -6);
      WriteLine (sw, GroupCode.DashElemLength, 3);
      WriteLine (sw, GroupCode.DashElemLength, -6);
      WriteLine (sw, GroupCode.MainEntStartEnd, "ENDTAB");
   }

   void WriteLayers (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "TABLE");
      WriteLine (sw, GroupCode.Name, "LAYER");
      WriteLine (sw, GroupCode.LineTypeFlag, 3);

      // Layer 0
      WriteLine (sw, GroupCode.MainEntStartEnd, "LAYER");
      WriteLine (sw, GroupCode.LineTypeFlag, 0);
      WriteLine (sw, GroupCode.Name, "0");
      WriteLine (sw, GroupCode.ColorIndex, 0);
      WriteLine (sw, GroupCode.Name, "CONTINUOUS");

      // Bend layer
      WriteLine (sw, GroupCode.MainEntStartEnd, "LAYER");
      WriteLine (sw, GroupCode.LineTypeFlag, 0);
      WriteLine (sw, GroupCode.Name, "Bend");
      WriteLine (sw, GroupCode.ColorIndex, 94);
      WriteLine (sw, GroupCode.Name, "DOT");

      // MBend layer
      WriteLine (sw, GroupCode.MainEntStartEnd, "LAYER");
      WriteLine (sw, GroupCode.LineTypeFlag, 0);
      WriteLine (sw, GroupCode.Name, "MBend");
      WriteLine (sw, GroupCode.ColorIndex, 94);
      WriteLine (sw, GroupCode.Name, "DASHDOTDOT");

      WriteLine (sw, GroupCode.MainEntStartEnd, "ENDTAB");
   }

   void WriteBlocks (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "SECTION");
      WriteLine (sw, GroupCode.Name, "BLOCKS");
      WriteLine (sw, GroupCode.MainEntStartEnd, "ENDSEC");
   }

   void WriteEntities (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "SECTION");
      WriteLine (sw, GroupCode.Name, "ENTITIES");

      // First bend line
      WriteLine (sw, GroupCode.MainEntStartEnd, "LINE");
      WriteLine (sw, GroupCode.LayerName, "Bend");
      WriteLine (sw, GroupCode.XCoordStart, mPartData.LeftBottomBendLinePos.X);
      WriteLine (sw, GroupCode.YCoordStart, mPartData.LeftBottomBendLinePos.Y);
      WriteLine (sw, GroupCode.XCoordEnd, mPartData.LeftTopBendLinePos.X);
      WriteLine (sw, GroupCode.YCoordEnd, mPartData.LeftTopBendLinePos.Y);
      WriteLine (sw, GroupCode.ZCoordEnd, 0);
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"BEND_ANGLE:{mPartData.BendAngle * 180.0 / Math.PI}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, "MATERIAL:Steel");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"THICKNESS:{mPartData.Thickness}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"BEND_RADIUS:{mPartData.InnerRadius}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"K_FACTOR:{mPartData.KFactor}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"BEND_FACTOR:{mPartData.BendDeduction}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, "BUMP_COUNT:0");

      // Second bend line
      WriteLine (sw, GroupCode.MainEntStartEnd, "LINE");
      WriteLine (sw, GroupCode.LayerName, "Bend");
      WriteLine (sw, GroupCode.XCoordStart, mPartData.RightBottomBendLinePos.X);
      WriteLine (sw, GroupCode.YCoordStart, mPartData.RightBottomBendLinePos.Y);
      WriteLine (sw, GroupCode.XCoordEnd, mPartData.RightTopBendLinePos.X);
      WriteLine (sw, GroupCode.YCoordEnd, mPartData.RightTopBendLinePos.Y);
      WriteLine (sw, GroupCode.ZCoordEnd, 0);
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"BEND_ANGLE:{mPartData.BendAngle * 180.0 / Math.PI}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, "MATERIAL:Steel");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"THICKNESS:{mPartData.Thickness}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"BEND_RADIUS:{mPartData.InnerRadius}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"K_FACTOR:{mPartData.KFactor}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, $"BEND_FACTOR:{mPartData.BendDeduction}");
      WriteLine (sw, GroupCode.CustomAnnoAttr, "BUMP_COUNT:0");

      // First polyline (closed rectangle)
      WriteLine (sw, GroupCode.MainEntStartEnd, "POLYLINE");
      WriteLine (sw, GroupCode.LayerName, "0");
      WriteLine (sw, GroupCode.PolylineFlag, 1); // 1 = This is a closed polyline
      WriteLine (sw, GroupCode.Thickness, 0); // Optional: set thickness if needed
      WriteLine (sw, GroupCode.LineTypeFlag, 0); // 0 = no special flags

      // Vertices - write all four points in order
      WriteVertex (sw, mPartData.LeftBottom.X, mPartData.LeftBottom.Y);
      WriteVertex (sw, mPartData.LeftTop.X, mPartData.LeftTop.Y);
      WriteVertex (sw, mPartData.RightTop.X, mPartData.RightTop.Y);
      WriteVertex (sw, mPartData.RightBottom.X, mPartData.RightBottom.Y);
      WriteVertex (sw, mPartData.LeftBottom.X, mPartData.LeftBottom.Y);

      WriteLine (sw, GroupCode.MainEntStartEnd, "SEQEND");
      WriteLine (sw, GroupCode.LayerName, "0");

      // Holes (holes)
      foreach (var hole in mPartData.Holes) {
         var sheetCenter = hole.SheetCenter;
         if (hole.IsSlot ()) {
            if (hole.HoleWidth != null) {
               double holeRot = 0;
               if (hole.HoleRotation != null) holeRot = hole.HoleRotation.Value;
               WriteSlot (sw, sheetCenter, hole.Dia / 2, hole.HoleWidth.Value, holeRot);
            }
         } else
            WriteCircle (sw, sheetCenter.X, sheetCenter.Y, hole.Dia / 2);
      }
      WriteLine (sw, GroupCode.MainEntStartEnd, "ENDSEC");
   }

   void WriteVertex (StreamWriter sw, double x, double y, double? bulge = null) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "VERTEX");
      WriteLine (sw, GroupCode.LayerName, "0");
      WriteLine (sw, GroupCode.XCoordStart, x);
      WriteLine (sw, GroupCode.YCoordStart, y);
      if (bulge.HasValue)
         WriteLine (sw, GroupCode.Bulge, bulge.Value);
   }

   void WriteCircle (StreamWriter sw, double x, double y, double radius) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "CIRCLE");
      WriteLine (sw, GroupCode.LayerName, "0");
      WriteLine (sw, GroupCode.XCoordStart, x);
      WriteLine (sw, GroupCode.YCoordStart, y);
      WriteLine (sw, GroupCode.FloatValue, radius);
   }

   void WriteFooter (StreamWriter sw) {
      WriteLine (sw, GroupCode.MainEntStartEnd, "EOF");
   }
   public void WriteSlot (StreamWriter sw, Point2 sheetCenter, double radius, double hw, double phi, string layer = "0") {
      // Calculate centers of the two semicircular arcs
      Point2 arc1Center = new (sheetCenter.X, sheetCenter.Y + hw / 2);
      Point2 arc2Center = new (sheetCenter.X, sheetCenter.Y - hw / 2);

      // Calculate unit direction vectors (accounting for rotation angle phi)
      Vector2 unitVec1 = new (Math.Cos (phi), Math.Sin (phi));
      Vector2 unitVec2 = unitVec1 * -1;

      // Calculate the four key points
      Point2 P1 = arc1Center + unitVec1 * radius; // First arc start
      Point2 P2 = arc1Center + unitVec2 * radius; // First arc end
      Point2 P3 = arc2Center + unitVec2 * radius; // Second arc start
      Point2 P4 = arc2Center + unitVec1 * radius; // Second arc end

      // Start polyline
      WriteLine (sw, GroupCode.MainEntStartEnd, "POLYLINE");
      WriteLine (sw, GroupCode.LayerName, layer);
      WriteLine (sw, GroupCode.PolylineFlag, 1); // Closed polyline
      WriteLine (sw, GroupCode.Thickness, 0);

      // 1. First arc segment (P1 to P2) - CCW semicircle (bulge = 1)
      WriteVertex (sw, P1.X, P1.Y, 1.0);

      // 2. Straight line segment (P2 to P3) - bulge = 0
      WriteVertex (sw, P2.X, P2.Y, 0.0);

      // 3. Second arc segment (P3 to P4) - CCW semicircle (bulge = 1)
      WriteVertex (sw, P3.X, P3.Y, 1.0);

      // 4. Closing straight line (P4 to P1) - bulge = 0
      // Note: With PolylineFlag=1, this is technically optional
      WriteVertex (sw, P4.X, P4.Y, 0.0);

      WriteVertex (sw, P1.X, P1.Y, 0.0);

      // End polyline
      WriteLine (sw, GroupCode.MainEntStartEnd, "SEQEND");
   }
   //public void WriteSlot (StreamWriter sw, Point2 sheetCenter, double radius, double hw, double phi, string layer = "0") {
   //   // Calculate centers of the two semicircular arcs
   //   Point2 arc1Center = new (sheetCenter.X, sheetCenter.Y + hw / 2);
   //   Point2 arc2Center = new (sheetCenter.X, sheetCenter.Y - hw / 2);

   //   // Calculate unit direction vectors (accounting for rotation angle phi)
   //   Vector2 unitVec1 = new (Math.Cos (phi), Math.Sin (phi));
   //   Vector2 unitVec2 = unitVec1 * -1;

   //   // Calculate key points
   //   Point2 arc1Start = arc1Center + unitVec1 * radius;
   //   Point2 arc1End = arc1Center + unitVec2 * radius;
   //   Point2 arc2Start = arc2Center + unitVec2 * radius;
   //   Point2 arc2End = arc2Center + unitVec1 * radius;

   //   // Start polyline
   //   WriteLine (sw, GroupCode.MainEntStartEnd, "POLYLINE");
   //   WriteLine (sw, GroupCode.LayerName, layer);
   //   WriteLine (sw, GroupCode.PolylineFlag, 1); // Closed polyline
   //   WriteLine (sw, GroupCode.Thickness, 0);

   //   // Vertex sequence with correct bulge application:

   //   // 1. First arc segment arc1Start with bulge
   //   WriteVertex (sw, arc1Start.X, arc1Start.Y, 1.0); // Bulge creates arc to next point

   //   // 2. First straight segment, start point is the end point of the arc
   //   WriteVertex (sw, arc1End.X, arc1End.Y, 0.0); // 0 bulge = straight to next point

   //   // 3. Second arc segment with start point as arc2Start with 1.0 as bulge
   //   WriteVertex (sw, arc2Start.X, arc2Start.Y, 1.0); // Bulge creates arc to next point

   //   // 4. End point of the arc
   //   WriteVertex (sw, arc2End.X, arc2End.Y, 0.0); // Optional with PolylineFlag=1

   //   // 5. Closing line segment with arc1Start as the end point
   //   WriteVertex (sw, arc1Start.X, arc1Start.Y, 0.0);

   //   // End polyline
   //   WriteLine (sw, GroupCode.MainEntStartEnd, "SEQEND");
   //}
   private double CalculateBulge (double includedAngle) => Math.Tan (includedAngle / 4);
}
