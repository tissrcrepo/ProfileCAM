using ProfileCAM.Core.Geometries;
using Flux.API;

namespace ProfileCAM.Input;
public enum RefCode {
   Web,
   Top,
   Bottom
}

public enum PartType {
   LH,
   RH
}
public struct HoleData {
   public HoleData (double x,
    double y,
    double dia,
    double width,
    double iRadius,
    double thickness,
    double bendAngle,
    PartType partType,
    RefCode pCode,
    RefCode mCode,
    double? holewidth = null,
    double? holerot = null) {
      bendAngle *= Math.PI / 180;
      X = x;
      Y = y; Dia = dia;
      PCode = pCode;
      MCode = mCode;
      HoleWidth = holewidth;
      HoleRotation = holerot;
      SheetCenter = HoleData.CenterOnSheet (X, Y, width, iRadius, thickness, bendAngle, partType, PCode, MCode);
   }
   public double X { get; set; }
   public double Y { get; set; }
   public Point2 SheetCenter { get; set; }
   RefCode PCode { get; set; }
   RefCode MCode { get; set; }
   public double Dia { get; set; }
   public double? HoleWidth { get; set; }
   public double? HoleRotation { get; set; }
   public bool IsSlot () {
      if (HoleWidth != null && HoleWidth.Value.SGT (0)) return true;
      return false;
   }
   public static double XSheetDist (double y, double ba, double rt, double w) => y - (2 * rt * Math.Tan (ba / 2) + ba * rt + w / 2);
   public static Point2 CenterOnSheet (double x, double y, double width, double iRad, double thickness,
      double bendAngle, PartType partType, RefCode pCode, RefCode mCode) {
      double rt = iRad + thickness; double xSheetDist;
      if (pCode == RefCode.Web && mCode == RefCode.Bottom) {
         if (partType == PartType.LH)
            return new Point2 (y - width / 2, x);
         else
            return new Point2 (width / 2 - y, x);
      } else if (pCode == RefCode.Web && mCode == RefCode.Bottom) {
         if (partType == PartType.LH)
            return new Point2 (width / 2 - y, x);
         else
            return new Point2 (y - width / 2, x);
      } else if (mCode == RefCode.Web) {
         xSheetDist = HoleData.XSheetDist (y, bendAngle, rt, width);
         if (pCode == RefCode.Top) return new Point2 (-xSheetDist, x);
         else if (pCode == RefCode.Bottom) return new Point2 (xSheetDist, x);
      }
      throw new Exception ("Circle data for unknown reference requested");
   }
}

public struct PartData (double thickness, double radius, double length, double height,
   double width, double kFactor, PartType partType, double bendAngle, List<HoleData> holes) {
   public double Thickness { get; init; } = thickness;
   public double InnerRadius { get; init; } = radius;
   public double Length { get; init; } = length;
   public double Height { get; init; } = height;
   public double Width { get; init; } = width;
   public double BendAngle { get; init; } = bendAngle * Math.PI / 180.0;
   public double KFactor { get; set; } = kFactor;
   public PartType PartType { get; set; } = partType;
   public List<HoleData> Holes { get; init; } = holes;
   public readonly Point2 LeftBottom => new (-(Height + Width / 2.0), 0);
   public readonly Point2 RightBottom => new ((Height + Width / 2.0), 0);
   public readonly Point2 RightTop => new ((Height + Width / 2.0), Length);
   public readonly Point2 LeftTop => new (-(Height + Width / 2.0), Length);
   readonly double Rt => InnerRadius + Thickness;
   public readonly double IRKT => InnerRadius + Thickness * KFactor; // Inner radius +( Bend factor * Mat thickness)
   public readonly double IRMT => InnerRadius + Thickness; // Inner radius +( Mat thickness)
   readonly double BendLineDist => (Width / 2);
   public readonly Point2 LeftBottomBendLinePos => new (-BendLineDist, 0);
   public readonly Point2 RightBottomBendLinePos => new (BendLineDist, 0);
   public readonly Point2 RightTopBendLinePos => new (BendLineDist, Length);
   public readonly Point2 LeftTopBendLinePos => new (-BendLineDist, Length);
   public readonly double OutsideSetback => Math.Tan (BendAngle / 2) * IRMT;
   public readonly double BendAllowance => (BendAngle * IRKT);
   public readonly double BendDeduction => 2 * OutsideSetback - BendAllowance; // Also known as Bend_Factor
}
