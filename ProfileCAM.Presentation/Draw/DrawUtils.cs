using System.ComponentModel;
using System.Windows.Threading;
using ProfileCAM.Core;
using ProfileCAM.Core.Geometries;
using ProfileCAM.Core.Tools;
using Flux.API;
using ProfileCAM.Core.Processes;


namespace ProfileCAM.Presentation.Draw {
   using ToolConfigSpec = (XForm4 XForm, Point3 WayPt, EMove MoveType);
   public class ProcessSimulator (IGenesysHub gHub, Dispatcher dsp) : INotifyPropertyChanged {
      #region Enums
      public enum RefCSys {
         WCS,
         MCS
      }
      public enum ESimulationStatus {
         Running,
         Paused,
         NotRunning
      }
      #endregion

      #region Data members
      private struct GCodeSegmentIndices {
         public GCodeSegmentIndices () {
            gCodeSegIndex = 0;
            wayPointIndex = 0;
         }
         public int gCodeSegIndex, wayPointIndex;
         //RefCSys mReferenceCS = RefCSys.WCS;
      }
      GCodeSegmentIndices[] mNextXFormIndex = [new (), new ()];
      double mPrevStepLen;
      int mCutScopeIndex = 0;
      int mLeftHeadBucketNumber = 0;
      int mRightHeadBucketNumber = 0;
      private readonly object mCutScopeLockObject = new ();
      private readonly object mLeftHeadBucketNumberLockObject = new ();
      private readonly object mRightHeadBucketNumberLockObject = new ();
      bool mIsZoomedToCutScope = false;
      List<Tuple<Point3, Vector3>>?[] mWayPoints = new List<Tuple<Point3, Vector3>>[2];
      #endregion

      #region Delegates and Events
      public delegate void TriggerRedrawDelegate ();
      public delegate void SetSimulationStatusDelegate (ESimulationStatus status);
      public delegate void ZoomExtentsWithBound3Delegate (Bound3 bound);
      public event TriggerRedrawDelegate? TriggerRedraw;
      public event Action? SimulationFinished;
      public event SetSimulationStatusDelegate? SetSimulationStatus;
      public event ZoomExtentsWithBound3Delegate? zoomExtentsWithBound3Delegate = null;
      public event PropertyChangedEventHandler? PropertyChanged;
      protected void OnPropertyChanged (string propertyName) {
         PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
      }
      #endregion

      #region External Refs
      public IGenesysHub GenesysHub { get; set; } = gHub;
      public Dispatcher Dispatch { get; set; } = dsp;
      //public List<List<GCodeSeg>[]> CutScopeTraces { get => GenesysHub.GCodeGen.CutScopeTraces; }
      #endregion

      #region Properties
      public (XForm4 XForm, Point3 WayPt, EMove MoveType) ToolConfigSpec { get; set; }
      ESimulationStatus mSimulationStatus = ESimulationStatus.NotRunning;
      public ESimulationStatus SimulationStatus {
         get => mSimulationStatus;
         set {
            if (mSimulationStatus != value) {
               mSimulationStatus = value;
               SetSimulationStatus?.Invoke (value);
            }
         }
      }

      readonly List<XForm4>[] mXForms = [[], []];
      public void ClearXForms () { mXForms[0].Clear (); mXForms[1].Clear (); }
      RefCSys mReferenceCS = RefCSys.WCS;
      public RefCSys ReferenceCS { get => mReferenceCS; set => mReferenceCS = value; }
      #endregion

      #region Resetters
      public void ClearZombies () {
         ClearTraces ();
         ClearXForms ();
         RewindEnumerator (0);
         RewindEnumerator (1);
         TriggerRedraw?.Invoke ();
         //mMultipassCuts?.ClearZombies ();
         GenesysHub.GCodeGen.ClearZombies ();
      }
      #endregion

      #region Simulation methods
      public void ClearTraces () {
         GenesysHub.Traces[0]?.Clear ();
         GenesysHub.Traces[1]?.Clear ();
         GenesysHub.CutScopeTraces?.Clear ();
      }

      ToolConfigSpec? GetNextToolXForm (MCSettings.EHeads head) {
         XForm4 xFormRes;
         int buckNo = -1;
         if (head == MCSettings.EHeads.Left)
            buckNo = GetLeftHeadBucketNumber ();
         else if (head == MCSettings.EHeads.Right)
            buckNo = GetRightHeadBucketNumber ();

         if (GenesysHub.Traces[buckNo] == null || GenesysHub.MachiningTool == null) return null;

         if (mNextXFormIndex[(int)head].gCodeSegIndex >= GenesysHub.Traces[buckNo].Count) return null;

         int steps = (int)(GenesysHub.Traces[buckNo][mNextXFormIndex[(int)head].gCodeSegIndex].Length / MCSettings.It.StepLength);

         if (mNextXFormIndex[(int)head].wayPointIndex == 0) {
            if (GenesysHub.Traces[buckNo][mNextXFormIndex[(int)head].gCodeSegIndex].GCode is EGCode.G0 or EGCode.G1) 
               mWayPoints[(int)head] = Utils.DiscretizeLine (GenesysHub.Traces[buckNo][mNextXFormIndex[(int)head].gCodeSegIndex], steps);
            else if (GenesysHub.Traces[buckNo][mNextXFormIndex[(int)head].gCodeSegIndex].GCode is EGCode.G2 or EGCode.G3) 
               mWayPoints[(int)head] = Utils.DiscretizeArc (GenesysHub.Traces[buckNo][mNextXFormIndex[(int)head].gCodeSegIndex], steps);
         }

         var wayPoints = mWayPoints[(int)head];
         if (wayPoints == null || wayPoints.Count == 0)
            throw new Exception ("Unable to compute treadingPoints");

         var waypointVec = wayPoints[mNextXFormIndex[(int)head].wayPointIndex];
         mNextXFormIndex[(int)head].wayPointIndex++;
         if (mNextXFormIndex[(int)head].wayPointIndex >= wayPoints.Count) {
            mNextXFormIndex[(int)head].gCodeSegIndex += 1;
            mNextXFormIndex[(int)head].wayPointIndex = 0;
         }

         var (wayPt, wayVecAtPt) = waypointVec;
         var yComp = Geom.Cross (wayVecAtPt, XForm4.mXAxis).Normalized ();
         xFormRes = new XForm4 (XForm4.mXAxis, yComp, wayVecAtPt.Normalized (), Geom.P2V (wayPt));
         if (ReferenceCS == RefCSys.MCS) xFormRes = Utils.XfmToMachine (GenesysHub.GCodeGen, xFormRes);
         if (mNextXFormIndex[(int)head].gCodeSegIndex == GenesysHub.Traces[buckNo].Count)
            return null;
         return (xFormRes, wayPt, GenesysHub.Traces[buckNo][mNextXFormIndex[(int)head].gCodeSegIndex].MoveType);
      }

      void RewindEnumerator (int head) {
         mNextXFormIndex[head].wayPointIndex = 0;
         mNextXFormIndex[head].gCodeSegIndex = 0;
         mWayPoints = new List<Tuple<Point3, Vector3>>[2];
         SetCutScopeIndex (0);
         SetLeftHeadBucketNumber (0);
         SetRightHeadBucketNumber (2);
         if (GenesysHub.CutScopeTraces.Count > 0) {
            GenesysHub.Traces[0] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][0]; // Head 1 Bucket 11
            GenesysHub.Traces[1] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][1]; // Head 1 Bucket 12
            GenesysHub.Traces[2] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][2]; // Head 1 Bucket 21
            GenesysHub.Traces[3] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][3]; // Head 1 Bucket 22
         }
      }

      void DrawToolSim (ProfileCAM.Core.MCSettings.EHeads head) {
         
         var mcCss = GenesysHub.GCodeGen.CutScopeTraces;
         //Bound3 bound = new ();

         //if (mcCss.Count > 0)
         //   bound = mcCss[0].Bound;

         //if (!mIsZoomedToCutScope) {
         //   zoomExtentsWithBound3Delegate?.Invoke (bound);
         //   mIsZoomedToCutScope = true;
         //}
         ToolConfigSpec? trfObject0 = null, trfObject1 = null;
         while (true) {
            if (head == MCSettings.EHeads.Both) {
               trfObject0 = GetNextToolXForm (MCSettings.EHeads.Left);
               trfObject1 = GetNextToolXForm (MCSettings.EHeads.Right);
            } else if (head == MCSettings.EHeads.Left)
               trfObject0 = GetNextToolXForm (MCSettings.EHeads.Left);
            else if (head == MCSettings.EHeads.Right)
               trfObject1 = GetNextToolXForm (MCSettings.EHeads.Right);


            if (trfObject0 == null && trfObject1 == null && SimulationStatus != ESimulationStatus.NotRunning) {
               // If Multipass
               if (GenesysHub.CutScopeTraces.Count > 1 && GetCutScopeIndex () + 1 < GenesysHub.CutScopeTraces.Count) {
                  // Safe incrementor
                  IncrementLeftHeadBucketNumber ();
                  IncrementRightHeadBucketNumber ();
                  if ( GetLeftHeadBucketNumber () >= 2)
                     SetLeftHeadBucketNumber (0);
                  if (GetRightHeadBucketNumber () >= 4)
                     SetRightHeadBucketNumber (2);


                  IncrementCutScopeIndex ();
                  int csIdx = GetCutScopeIndex ();
                  int head1bno = GetLeftHeadBucketNumber ();
                  int head2bno = GetRightHeadBucketNumber ();
                  //if (csIdx >= 0 && csIdx < mcCss.Count)
                  //   zoomExtentsWithBound3Delegate?.Invoke (mcCss[csIdx].Bound);

                  // Reset enumerator
                  RewindEnumerator (0);
                  RewindEnumerator (1);

                  // Rewind will reset everything, so the cutscope index needs to be restored
                  SetCutScopeIndex (csIdx);
                  SetLeftHeadBucketNumber (head1bno);
                  SetLeftHeadBucketNumber (head2bno);

                  if (head == MCSettings.EHeads.Both) {
                     GenesysHub.Traces[0] = GenesysHub.CutScopeTraces[csIdx][0];
                     GenesysHub.Traces[1] = GenesysHub.CutScopeTraces[csIdx][1];
                     GenesysHub.Traces[2] = GenesysHub.CutScopeTraces[csIdx][2];
                     GenesysHub.Traces[3] = GenesysHub.CutScopeTraces[csIdx][3];
                  } else {
                     if (head == MCSettings.EHeads.Left) {
                        GenesysHub.Traces[0] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][0];
                        GenesysHub.Traces[1] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][1];
                     } else if (head == MCSettings.EHeads.Right) {
                        GenesysHub.Traces[2] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][2];
                        GenesysHub.Traces[3] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][3];
                     }
                  }

                  //mMachiningTool.Draw (trfObject0, trfObject1, mDispatcher);
                  if (GenesysHub.MachiningTool == null)
                     throw new Exception ("Machining tool is not set for GenesysHub");

                  DrawUtils.DrawLaserCuttingTools (GenesysHub.MachiningTool, Dispatch, trfObject0, trfObject1);

                  return; // Exit the loop after drawing
               } else {
                  // Draw the tool again at the beginning of the process
                  RewindEnumerator (0);
                  RewindEnumerator (1);

                  if (GenesysHub.CutScopeTraces.Count > 0) {
                     if (head == MCSettings.EHeads.Both) {
                        GenesysHub.Traces[0] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][0];
                        GenesysHub.Traces[1] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][1];
                        GenesysHub.Traces[2] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][2];
                        GenesysHub.Traces[3] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][3];
                     } else {
                        if (head == MCSettings.EHeads.Left) {
                           GenesysHub.Traces[0] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][0];
                           GenesysHub.Traces[1] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][1];
                        } else if (head == MCSettings.EHeads.Right) {
                           GenesysHub.Traces[2] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][2];
                           GenesysHub.Traces[3] = GenesysHub.CutScopeTraces[GetCutScopeIndex ()][3];
                        }
                     }
                  }

                  //mMachiningTool.Draw (trfObject0, trfObject1, mDispatcher);
                  if (GenesysHub.MachiningTool == null)
                     throw new Exception ("Machining tool is not set for GenesysHub");

                  DrawUtils.DrawLaserCuttingTools (GenesysHub.MachiningTool, Dispatch, trfObject0, trfObject1);

                  // Finish the simulation trigger
                  SimulationFinished?.Invoke ();
                  SimulationStatus = ESimulationStatus.NotRunning;

                  if (ProfileCAM.Core.MCSettings.It.EnableMultipassCut)
                     ProfileCAM.Core.MCSettings.It.StepLength = mPrevStepLen;

                  Lux.StopContinuousRender (GFXCallback);
                  TriggerRedraw?.Invoke ();

                  // Restore the zoom to cover the entire part
                  if (GenesysHub.Workpiece == null)
                     throw new Exception ("Genesyshub's Workpiece is null");

                  zoomExtentsWithBound3Delegate?.Invoke (GenesysHub.Workpiece.Bound);
                  return;
               }
            } else {
               //mMachiningTool.Draw (trfObject0, trfObject1, mDispatcher);
               if (GenesysHub.MachiningTool == null)
                  throw new Exception ("Machining tool is not set for GenesysHub");
               DrawUtils.DrawLaserCuttingTools (GenesysHub.MachiningTool, Dispatch, trfObject0, trfObject1);
               return;
            }
         }
      }

      void GFXCallback (double elapsed) {
         // TODO : Based on the elapsed time, the speed of the tool(s)
         // should be calculated.
         TriggerRedraw?.Invoke ();
      }

      public void DrawToolInstance () {
         if (SimulationStatus == ESimulationStatus.Running) {
            var head = ProfileCAM.Core.MCSettings.It.Heads;
            DrawToolSim (head);
         }
      }

      public void Run () {
         if (SimulationStatus == ESimulationStatus.Running) return;

         var prevSimulationStatus = SimulationStatus;
         if (GenesysHub.Traces[0] == null && GenesysHub.Traces[1] == null) return;
         if (GenesysHub.Traces[0] != null && GenesysHub.Traces[0].Count > 0) {
            SimulationStatus = ESimulationStatus.Running;
            mXForms[0].Clear ();
         }
         if (GenesysHub.Traces[1] != null && GenesysHub.Traces[1].Count > 0) {
            SimulationStatus = ESimulationStatus.Running;
            mXForms[1].Clear ();
         }
         if (SimulationStatus == ESimulationStatus.Running) {
            if (MCSettings.It.Heads is MCSettings.EHeads.Left or MCSettings.EHeads.Both &&
               prevSimulationStatus == ESimulationStatus.NotRunning) RewindEnumerator (0);
            if (MCSettings.It.Heads is MCSettings.EHeads.Right or MCSettings.EHeads.Both &&
               prevSimulationStatus == ESimulationStatus.NotRunning) RewindEnumerator (1);

            mPrevStepLen = MCSettings.It.StepLength;
            SetCutScopeIndex (0);
            SetLeftHeadBucketNumber (0);
            SetRightHeadBucketNumber (2);
            Lux.StartContinuousRender (GFXCallback);
         }
      }

      public void Stop () {
         Lux.StopContinuousRender (GFXCallback);
         if (MCSettings.It.EnableMultipassCut) MCSettings.It.StepLength = mPrevStepLen;
         SimulationStatus = ESimulationStatus.NotRunning;
         if (MCSettings.It.Heads is MCSettings.EHeads.Left or MCSettings.EHeads.Both) RewindEnumerator (0);
         if (MCSettings.It.Heads is MCSettings.EHeads.Right or MCSettings.EHeads.Both) RewindEnumerator (1);
         SimulationFinished?.Invoke ();
         GFXCallback (0.01);
      }

      public void Pause () {
         SimulationStatus = ESimulationStatus.Paused;
         if (MCSettings.It.EnableMultipassCut) MCSettings.It.StepLength = mPrevStepLen;
         Lux.StopContinuousRender (GFXCallback);
      }
      #endregion

      #region GCode Draw Implementation
      public void DrawGCode () {
         if (GenesysHub.CutScopeTraces == null) return;
         foreach (var cutScopeTooling in GenesysHub.CutScopeTraces)
            DrawGCode (cutScopeTooling);
      }
      public void DrawGCodeForCutScope () {
         // If simulation runs and when a new part is loaded, this 
         // check is necessary
         if (GenesysHub.CutScopeTraces != null && GenesysHub.CutScopeTraces.Count > 0)
            DrawGCode (GenesysHub.CutScopeTraces[GetCutScopeIndex ()]);
      }

      // Method to set the index
      public void SetCutScopeIndex (int value) {
         lock (mCutScopeLockObject) {
            mCutScopeIndex = value;
         }
      }

      // Method to get the index
      public int GetCutScopeIndex () {
         lock (mCutScopeLockObject) {
            return mCutScopeIndex;
         }
      }

      //// Method to increment the index safely
      public void IncrementCutScopeIndex () {
         lock (mCutScopeLockObject) {
            mCutScopeIndex++;
         }
      }

      //

      public void SetLeftHeadBucketNumber (int value) {
         lock (mLeftHeadBucketNumberLockObject) {
            mLeftHeadBucketNumber = value;
         }
      }

      // Method to get the index
      public int GetLeftHeadBucketNumber () {
         lock (mLeftHeadBucketNumberLockObject) {
            return mLeftHeadBucketNumber;
         }
      }

      //// Method to increment the index safely
      public void IncrementLeftHeadBucketNumber () {
         lock (mLeftHeadBucketNumberLockObject) {
            mLeftHeadBucketNumber++;
         }
      }

      public void SetRightHeadBucketNumber (int value) {
         lock (mRightHeadBucketNumberLockObject) {
            mRightHeadBucketNumber = value;
         }
      }

      // Method to get the index
      public int GetRightHeadBucketNumber () {
         lock (mRightHeadBucketNumberLockObject) {
            return mRightHeadBucketNumber;
         }
      }

      //// Method to increment the index safely
      public void IncrementRightHeadBucketNumber () {
         lock (mRightHeadBucketNumberLockObject) {
            mRightHeadBucketNumber++;
         }
      }

      public void DrawGCode (List<GCodeSeg>[] cutScopeTooling) {
         List<List<GCodeSeg>> listOfListOfDrawables = [];
         if (cutScopeTooling[0].Count > 0) listOfListOfDrawables.Add (cutScopeTooling[0]);
         if (cutScopeTooling[1].Count > 0) listOfListOfDrawables.Add (cutScopeTooling[1]);
         if (cutScopeTooling[2].Count > 0) listOfListOfDrawables.Add (cutScopeTooling[2]);
         if (cutScopeTooling[3].Count > 0) listOfListOfDrawables.Add (cutScopeTooling[3]);
         List<Action> drawActions = [];
         List<Point3> G0DrawPoints = [], G1DrawPoints = [];
         List<List<Point3>> G2DrawPoints = [], G3DrawPoints = [];
         foreach (var drawables in listOfListOfDrawables) {
            foreach (var gcseg in drawables) {
               var seg = gcseg;
               if (ReferenceCS == RefCSys.MCS)
                  seg = seg.XfmToMachineNew (GenesysHub.GCodeGen);
               Color32 segColor = Color32.Nil;

               if (seg.IsLine ()) {
                  if (seg.GCode == EGCode.G0 || seg.MoveType == EMove.Retract2Machining) {
                     segColor = new Color32 (255, 255, 255);
                     G0DrawPoints.Add (seg.StartPoint);
                     G0DrawPoints.Add (seg.EndPoint);
                  } else {
                     segColor = Color32.Blue;
                     G1DrawPoints.Add (seg.StartPoint);
                     G1DrawPoints.Add (seg.EndPoint);
                  }
               } else if (seg.IsArc ()) {
                  var arcPointVecs = Utils.DiscretizeArc (seg, 50);
                  if (arcPointVecs != null) {
                     List<Point3> arcPts = [];
                     if (seg.GCode == EGCode.G3) {
                        segColor = Color32.Cyan;
                        foreach (var ptVec in arcPointVecs) arcPts.Add (ptVec.Item1);
                        G3DrawPoints.Add (arcPts);
                     } else {
                        segColor = Color32.Magenta;
                        foreach (var ptVec in arcPointVecs) arcPts.Add (ptVec.Item1);
                        G2DrawPoints.Add (arcPts);
                     }
                  } else
                     throw new Exception ("Arc discretization failed");
               }
            }
         }
         Dispatch.Invoke (() => {
            Lux.HLR = true;
            Lux.Color = Utils.G3SegColor;
            foreach (var arcPoints in G3DrawPoints) {
               Lux.Draw (EDraw.Lines, arcPoints);

               // The following draw call is to terminate drawing of the 
               // above arc points. Else, the arcs are connected continuously
               // There has to be a better/elegant solution: TODO
               Lux.Draw (EDraw.LineStrip, [arcPoints[^1], arcPoints[^1]]);
            }
         });
         Dispatch.Invoke (() => {
            Lux.HLR = true;
            Lux.Color = Utils.G2SegColor;
            foreach (var arcPoints in G2DrawPoints) {
               Lux.Draw (EDraw.Lines, arcPoints);

               // The following draw call is to terminate drawing of the 
               // above arc points. Else, the arcs are connected continuously
               // There has to be a better/elegant solution: TODO
               Lux.Draw (EDraw.LineStrip, [arcPoints[^1], arcPoints[^1]]);
            }
         });
         Dispatch.Invoke (() => {
            Lux.HLR = true;
            Lux.Color = Utils.G0SegColor;
            Lux.Draw (EDraw.Lines, G0DrawPoints);
         });
         Dispatch.Invoke (() => {
            Lux.HLR = true;
            Lux.Color = Utils.G1SegColor;
            Lux.Draw (EDraw.Lines, G1DrawPoints);
         });
      }
      #endregion
   }

   public static class DrawUtils {
      public static void DrawLaserCuttingTools (Nozzle nozzle, Dispatcher dsp,
      ToolConfigSpec? trfTool0, ToolConfigSpec? trfTool1) {
         // Run GenerateDrawData in Parallel
         var drawDataTask = Task.Run (() => nozzle.GenerateDrawData (trfTool0, trfTool1));

         // Await the result while keeping UI responsive
         var (cylPtsT1, tooltipPtsT1, lsPtsT1,
              cylPtsT2, tooltipPtsT2, lsPtsT2) = drawDataTask.Result;

         // Batch draw calls and use Dispatcher.InvokeAsync to prevent blocking UI thread
         dsp.InvokeAsync (() => {
            Lux.HLR = true;

            // Draw linear sparks
            if (lsPtsT1.Count > 0) {
               Lux.Color = Utils.SteelCutingSparkColor2;
               Lux.Draw (EDraw.LineStrip, lsPtsT1);
            }

            if (lsPtsT2.Count > 0) {
               Lux.Color = Utils.SteelCutingSparkColor2;
               Lux.Draw (EDraw.Lines, lsPtsT2);
            }

            // Draw cylinder points
            if (cylPtsT1.Count > 0) {
               Lux.Color = Utils.LHToolColor;
               Lux.Draw (EDraw.Triangle, cylPtsT1);
            }
            if (cylPtsT2.Count > 0) {
               Lux.Color = Utils.RHToolColor;
               Lux.Draw (EDraw.Triangle, cylPtsT2);
            }

            // Draw tool tip points
            if (tooltipPtsT1.Count > 0) {
               Lux.Color = Utils.ToolTipColor2;
               Lux.Draw (EDraw.Triangle, tooltipPtsT1);
            }
            if (tooltipPtsT2.Count > 0) {
               Lux.Color = Utils.ToolTipColor2;
               Lux.Draw (EDraw.Triangle, tooltipPtsT2);
            }
         });
      }
   }
}