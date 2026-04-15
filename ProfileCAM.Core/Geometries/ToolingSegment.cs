using ProfileCAM.Core.GCodeGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flux.API;

namespace ProfileCAM.Core.Geometries;
public struct ToolingSegment {
   FCCurve3 mCurve;
   Vector3 mVec0;
   Vector3 mVec1;
   bool mIsValid = true;

   public ToolingSegment ((FCCurve3, Vector3, Vector3) vtSeg, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = vtSeg.Item1.Clone ();
      else mCurve = vtSeg.Item1;
      mVec0 = vtSeg.Item2;
      mVec1 = vtSeg.Item3;
      NotchSectionType = NotchSectionType.None;
   }

   public ToolingSegment ((FCCurve3, Vector3, Vector3, NotchSectionType) vtSeg, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = vtSeg.Item1.Clone ();
      else mCurve = vtSeg.Item1;
      mVec0 = vtSeg.Item2;
      mVec1 = vtSeg.Item3;
      NotchSectionType = vtSeg.Item4;
   }

   public ToolingSegment (FCCurve3 crv, Vector3 vec0, Vector3 vec1, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = crv.Clone ();
      else mCurve = crv;
      mVec0 = vec0;
      mVec1 = vec1;
      NotchSectionType = NotchSectionType.None;
   }

   public ToolingSegment (FCCurve3 crv, Vector3 vec0, Vector3 vec1, NotchSectionType nsectionType, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = crv.Clone ();
      else mCurve = crv;
      mVec0 = vec0;
      mVec1 = vec1;
      NotchSectionType = nsectionType;
   }

   public ToolingSegment (ToolingSegment rhs, bool cloneCrv = false) {
      if (cloneCrv)
         mCurve = rhs.Curve.Clone ();
      else mCurve = rhs.Curve;
      mVec0 = rhs.mVec0;
      mVec1 = rhs.mVec1;
      NotchSectionType = rhs.NotchSectionType;
   }

   public readonly void Deconstruct (out FCCurve3 curve, out Vector3 vec0, out Vector3 vec1) {
      curve = this.Curve;
      vec0 = this.Vec0;
      vec1 = this.Vec1;
   }

   public FCCurve3 Curve { readonly get => mCurve; set => mCurve = value; }
   public Vector3 Vec0 { readonly get => mVec0; set => mVec0 = value; }
   public Vector3 Vec1 { readonly get => mVec1; set => mVec1 = value; }
   public bool IsValid { readonly get => mIsValid; set => mIsValid = value; }
   public readonly double Length { get => mCurve.Length; }
   public NotchSectionType NotchSectionType { get; set; } = NotchSectionType.None;

   // Deep clone of the current instance
   public readonly ToolingSegment Clone () {
      return new ToolingSegment (
          mCurve.Clone (),    // Deep clone the curve
          mVec0,             // Vector3 is a struct, copies by value
          mVec1,             // Vector3 is a struct, copies by value
          NotchSectionType,  // Enum copies by value
          cloneCrv: false    // We already cloned the curve above, so no need to clone again
      ) {
         mIsValid = this.mIsValid,
         NotchSectionType = this.NotchSectionType
      };
   }

   // Deep clone an entire list of ToolingSegment
   public static List<ToolingSegment> Clone (List<ToolingSegment> rhs) {
      if (rhs == null)
         return null;

      var clonedList = new List<ToolingSegment> (rhs.Count);

      foreach (var segment in rhs) {
         // Deep clone each segment in the list
         clonedList.Add (segment.Clone ());
      }

      return clonedList;
   }

   // Alternative: Clone with control over curve cloning
   public readonly ToolingSegment Clone (bool cloneCurve) {
      if (cloneCurve) {
         return new ToolingSegment (
             mCurve.Clone (),
             mVec0,
             mVec1,
             NotchSectionType,
             cloneCrv: false
         ) {
            mIsValid = this.mIsValid
         };
      } else {
         return new ToolingSegment (
             mCurve,          // Reference to same curve (shallow)
             mVec0,
             mVec1,
             NotchSectionType,
             cloneCrv: false
         ) {
            mIsValid = this.mIsValid
         };
      }
   }

   // Static method to clone with specific curve cloning behavior for each segment
   public static List<ToolingSegment> Clone (List<ToolingSegment> rhs, bool cloneCurves) {
      if (rhs == null)
         return null;

      var clonedList = new List<ToolingSegment> (rhs.Count);

      foreach (var segment in rhs) {
         clonedList.Add (segment.Clone (cloneCurves));
      }

      return clonedList;
   }

   // For ICloneable interface support (optional)
   public readonly object CloneObject () {
      return Clone ();
   }
}

