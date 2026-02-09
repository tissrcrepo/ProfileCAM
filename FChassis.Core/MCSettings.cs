using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using MessagePack;
using System.Reflection;
using System.Windows;

namespace FChassis.Core;

/// <summary>All fields that can be set through the Options/Settings dialog</summary>
[MessagePackObject (AllowPrivate = true)]
public partial class MCSettings : INotifyPropertyChanged {
   #region Constructors
   // Singleton instance
   public static MCSettings It => sIt ??= new ();
   static MCSettings sIt;

   // Notify event, to bind the changes with SettingsDlg
   public event PropertyChangedEventHandler PropertyChanged;

   [JsonConstructor]
   public MCSettings () {
      mToolingPriority = [EKind.Hole, EKind.Notch, EKind.Cutout, EKind.Mark];
      mStandoff = 0.0;
      mFlexCuttingGap = 0.0;
      mMarkText = "Deluxe";
      mPartitionRatio = 0.5;
      mHeads = EHeads.Both;
      mApproachLength = 2;
      PartConfig = PartConfigType.LHComponent;
      MarkTextPosX = 700.4;
      MarkTextPosY = 10.0;
      NotchWireJointDistance = 2.0;
      NotchApproachLength = 5.0;
      mEnableMultipassCut = true;
      mMaxFrameLength = 4075;
      MaximizeFrameLengthInMultipass = true;
      mCutHoles = true;
      mCutNotches = true;
      mCutCutouts = true;
      mCutMarks = true;
      if (System.IO.Directory.Exists ("W:\\FChassis\\Sample"))
         NCFilePath = "W:\\FChassis\\Sample";
      else
         NCFilePath = "";

      MinThresholdForPartition = 585.0;
      MinNotchLengthThreshold = 210;
      MinCutOutLengthThreshold = 210;
      DINFilenameSuffix = "";
      NotchCutStartToken = "NCutStart";
      NotchCutEndToken = "NCutEnd";
      WorkpieceOptionsFilename = @"W:\FChassis\LCM2HWorkpieceOptions.json";
      DeadbandWidth = 980.0;
      LeastWJLength = 0.24999999999999999999999;
#if DEBUG
      Version = "Debug 91"; 
#elif TESTRELEASE
      Version = " Test Release 91";
#else
      Version = "1.0.25"; 
#endif
   }
   #endregion

   #region Delegates and Events
   public delegate void SettingValuesChangedEventHandler ();

   // Any changes to the properties here will also change 
   // elsewhere where the OnSettingValuesChangedEvent is subscribed with
   public event SettingValuesChangedEventHandler OnSettingValuesChangedEvent;
   #endregion

   #region Enums
   public enum EHeads {
      Left,
      Right,
      Both,
      None
   }

   public enum PartConfigType {
      LHComponent,
      RHComponent
   }

   public enum EOptimize {
      DP,
      Time
   }
   #endregion

   #region Helpers
   // Method to copy values from the deserialized object to the current singleton instance
   private void UpdateFields (MCSettings other) {
      Heads = other.Heads;
      Standoff = other.Standoff;
      LeastWJLength = other.LeastWJLength;
      FlexCuttingGap = other.FlexCuttingGap;
      ToolingPriority = other.ToolingPriority;
      MarkTextPosX = other.MarkTextPosX;
      MarkTextPosY = other.MarkTextPosY;
      MarkText = other.MarkText;
      MarkTextHeight = other.MarkTextHeight;
      MarkAngle = other.MarkAngle;
      OptimizeSequence = other.OptimizeSequence;
      ProgNo = other.ProgNo;
      NCFilePath = other.NCFilePath;
      SafetyZone = other.SafetyZone;
      SerialNumber = other.SerialNumber;
      SyncHead = other.SyncHead;
      UsePingPong = other.UsePingPong;
      PartConfig = other.PartConfig;
      OptimizePartition = other.OptimizePartition;
      SlotWithWJTOnly = other.SlotWithWJTOnly;
      DualFlangeCutoutNotchOnly = other.DualFlangeCutoutNotchOnly;
      //RotateX180 = other.RotateX180;
      IncludeFlange = other.IncludeFlange;
      IncludeCutout = other.IncludeCutout;
      IncludeWeb = other.IncludeWeb;
      PartitionRatio = other.PartitionRatio;
      ProbeMinDistance = other.ProbeMinDistance;
      NotchApproachLength = other.NotchApproachLength;
      ApproachLength = other.ApproachLength;
      NotchWireJointDistance = other.NotchWireJointDistance;
      FlexOffset = other.FlexOffset;
      StepLength = other.StepLength;
      EnableMultipassCut = other.EnableMultipassCut;
      MaxFrameLength = other.MaxFrameLength;
      MaximizeFrameLengthInMultipass = other.MaximizeFrameLengthInMultipass;
      CutHoles = other.CutHoles;
      CutNotches = other.CutNotches;
      CutCutouts = other.CutCutouts;
      CutMarks = other.CutMarks;
      CutWeb = other.CutWeb;
      CutFlange = other.CutFlange;
      MinThresholdForPartition = other.MinThresholdForPartition;
      MinNotchLengthThreshold = other.MinNotchLengthThreshold;
      MinCutOutLengthThreshold = other.MinCutOutLengthThreshold;
      DINFilenameSuffix = other.DINFilenameSuffix;
      NotchCutStartToken = other.NotchCutStartToken;
      NotchCutEndToken = other.NotchCutEndToken;
      Machine = other.Machine;
      WorkpieceOptionsFilename = other.WorkpieceOptionsFilename;
      ShowToolingNames = other.ShowToolingNames;
      ShowToolingExtents = other.ShowToolingExtents;
      DeadbandWidth = other.DeadbandWidth;
   }

   // Helper method to set a property and raise the event
   private void SetProperty<T> (ref T field, T value, [CallerMemberName] string? propertyName = null) {
      if (!Equals (field, value)) {
         field = value;
         OnSettingValuesChangedEvent?.Invoke ();
         PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
      }
   }

   // Method to raise the PropertyChanged event
   protected virtual void OnPropertyChanged ([CallerMemberName] string propertyName = null) {
      PropertyChanged?.Invoke (this, new PropertyChangedEventArgs (propertyName));
   }
   #endregion

   #region Properties with JSON Attributes
   [Key (0)]
   [JsonPropertyName ("heads")]
   public EHeads Heads { get => mHeads; set => SetProperty (ref mHeads, value); }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   EHeads mHeads = EHeads.Both;

   [Key (1)]
   [JsonPropertyName ("standoff")]
   public double Standoff { get => mStandoff; set => SetProperty (ref mStandoff, value); }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mStandoff;

   [Key (2)]
   [JsonPropertyName ("flexCuttingGap")]
   public double FlexCuttingGap { get => mFlexCuttingGap; set => SetProperty (ref mFlexCuttingGap, value); }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mFlexCuttingGap;

   [Key (3)]
   [JsonPropertyName ("toolingPriority")]
   public EKind[] ToolingPriority {
      get => mToolingPriority;
      set => SetProperty (ref mToolingPriority, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   EKind[] mToolingPriority;

   [JsonPropertyName ("markTextPosX")]
   [Key (8)]
   public double MarkTextPosX {
      get => mMarkTextPosX;
      set => SetProperty (ref mMarkTextPosX, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMarkTextPosX;

   [JsonPropertyName ("markTextPosY")]
   [Key (10)]
   public double MarkTextPosY {
      get => mMarkTextPosY;
      set => SetProperty (ref mMarkTextPosY, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMarkTextPosY;

   [JsonPropertyName ("markText")]
   [Key (12)]
   public string MarkText {
      get => mMarkText;
      set => SetProperty (ref mMarkText, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mMarkText;

   [JsonPropertyName ("markTextHeight")]
   [Key (14)]
   public int MarkTextHeight {
      get => mMarkTextHeight;
      set => SetProperty (ref mMarkTextHeight, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   int mMarkTextHeight;

   [JsonPropertyName ("markAngle")]
   [Key (16)]
   public ERotate MarkAngle {
      get => mMarkAngle;
      set => SetProperty (ref mMarkAngle, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   ERotate mMarkAngle = ERotate.Rotate0;

   [JsonPropertyName ("optimizeSequence")]
   [Key (18)]
   public bool OptimizeSequence {
      get => mOptimizeSequence;
      set => SetProperty (ref mOptimizeSequence, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mOptimizeSequence = false;

   [JsonPropertyName ("progNo")]
   [Key (20)]
   public int ProgNo {
      get => mProgNo;
      set => SetProperty (ref mProgNo, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   int mProgNo = 1;

   [JsonPropertyName ("ncFilePath")]
   [Key (22)]
   public string NCFilePath {
      get => mNCFilePath;
      set => SetProperty (ref mNCFilePath, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mNCFilePath;

   [JsonPropertyName ("safetyZone")]
   [Key (24)]
   public double SafetyZone {
      get => mSafetyZone;
      set => SetProperty (ref mSafetyZone, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mSafetyZone;

   [JsonPropertyName ("serialNumber")]
   [Key (26)]
   public uint SerialNumber {
      get => mSerialNumber;
      set => SetProperty (ref mSerialNumber, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   uint mSerialNumber;

   [JsonPropertyName ("syncHead")]
   [Key (28)]
   public bool SyncHead {
      get => mSyncHead;
      set => SetProperty (ref mSyncHead, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mSyncHead;

   [JsonPropertyName ("usePingPong")]
   [Key (30)]
   public bool UsePingPong {
      get => mUsePingPong;
      set => SetProperty (ref mUsePingPong, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mUsePingPong = true;

   [JsonPropertyName ("partConfig")]
   [Key (32)]
   public PartConfigType PartConfig {
      get => mPartConfig;
      set => SetProperty (ref mPartConfig, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   PartConfigType mPartConfig;

   [JsonPropertyName ("optimizePartition")]
   [Key (34)]
   public bool OptimizePartition {
      get => mOptimizePartition;
      set => SetProperty (ref mOptimizePartition, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mOptimizePartition;

   [JsonPropertyName ("includeFlange")]
   [Key (36)]
   public bool IncludeFlange {
      get => mIncludeFlange;
      set => SetProperty (ref mIncludeFlange, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mIncludeFlange;

   [JsonPropertyName ("includeCutout")]
   [Key (38)]
   public bool IncludeCutout {
      get => mIncludeCutout;
      set => SetProperty (ref mIncludeCutout, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mIncludeCutout;

   [JsonPropertyName ("includeWeb")]
   [Key (40)]
   public bool IncludeWeb {
      get => mIncludeWeb;
      set => SetProperty (ref mIncludeWeb, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mIncludeWeb;

   [JsonPropertyName ("partitionRatio")]
   [Key (42)]
   public double PartitionRatio {
      get => mPartitionRatio;
      set => SetProperty (ref mPartitionRatio, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mPartitionRatio;

   [JsonPropertyName ("probeMinDistance")]
   [Key (44)]
   public double ProbeMinDistance {
      get => mProbeMinDistance;
      set => SetProperty (ref mProbeMinDistance, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mProbeMinDistance;

   [JsonPropertyName ("notchApproachLength")]
   [Key (46)]
   public double NotchApproachLength {
      get => mNotchApproachLength;
      set => SetProperty (ref mNotchApproachLength, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mNotchApproachLength;

   [JsonPropertyName ("approachLength")]
   [Key (48)]
   public double ApproachLength {
      get => mApproachLength;
      set => SetProperty (ref mApproachLength, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mApproachLength;

   [JsonPropertyName ("notchWireJointDistance")]
   [Key (50)]
   public double NotchWireJointDistance {
      get => mNotchWireDistance;
      set => SetProperty (ref mNotchWireDistance, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mNotchWireDistance;

   [JsonPropertyName ("WMapLocation")]
   [Key (51)]
   public string WMapLocation {
      get => mWMapLocation;
      set => SetProperty (ref mWMapLocation, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mWMapLocation;

   [JsonPropertyName ("flexOffset")]
   [Key (52)]
   public double FlexOffset {
      get => mFlexOffset;
      set => SetProperty (ref mFlexOffset, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mFlexOffset;

   [JsonPropertyName ("stepLength")]
   [Key (54)]
   public double StepLength {
      get => mLengthPerStep;
      set => SetProperty (ref mLengthPerStep, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mLengthPerStep = 1.0;

   [JsonPropertyName ("enableMultipassCut")]
   [Key (56)]
   public bool EnableMultipassCut {
      get => mEnableMultipassCut;
      set => SetProperty (ref mEnableMultipassCut, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mEnableMultipassCut;

   [JsonPropertyName ("maxFrameLength")]
   [Key (58)]
   public double MaxFrameLength {
      get => mMaxFrameLength;
      set => SetProperty (ref mMaxFrameLength, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMaxFrameLength;

   [JsonPropertyName ("maximizeFrameLengthInMultipass")]
   [Key (60)]
   public bool MaximizeFrameLengthInMultipass {
      get => mMazimizeFrameLengthInMultipass;
      set => SetProperty (ref mMazimizeFrameLengthInMultipass, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mMazimizeFrameLengthInMultipass;

   [JsonPropertyName ("cutHoles")]
   [Key (62)]
   public bool CutHoles {
      get => mCutHoles;
      set => SetProperty (ref mCutHoles, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mCutHoles;

   [JsonPropertyName ("cutNotches")]
   [Key (64)]
   public bool CutNotches {
      get => mCutNotches;
      set => SetProperty (ref mCutNotches, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mCutNotches;

   [JsonPropertyName ("cutCutouts")]
   [Key (66)]
   public bool CutCutouts {
      get => mCutCutouts;
      set => SetProperty (ref mCutCutouts, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mCutCutouts;

   [JsonPropertyName ("cutMarks")]
   [Key (68)]
   public bool CutMarks {
      get => mCutMarks;
      set => SetProperty (ref mCutMarks, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mCutMarks;

   [JsonPropertyName ("minThresholdForPartition")]
   [Key (70)]
   public double MinThresholdForPartition {
      get => mMinThresholdForPartition;
      set => SetProperty (ref mMinThresholdForPartition, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMinThresholdForPartition;

   [JsonPropertyName ("minNotchLengthThreshold")]
   [Key (72)]
   public double MinNotchLengthThreshold {
      get => mMinNotchLengthThreshold;
      set => SetProperty (ref mMinNotchLengthThreshold, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMinNotchLengthThreshold;

   [JsonPropertyName ("minCutOutLengthThreshold")]
   [Key (74)]
   public double MinCutOutLengthThreshold {
      get => mMinCutOutLengthThreshold;
      set => SetProperty (ref mMinCutOutLengthThreshold, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMinCutOutLengthThreshold;

   [JsonPropertyName ("dinFilenameSuffix")]
   [Key (76)]
   public string DINFilenameSuffix {
      get => mDINFilenameSuffix;
      set => SetProperty (ref mDINFilenameSuffix, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mDINFilenameSuffix;

   [JsonPropertyName ("notchCutStartToken")]
   [Key (78)]
   public string NotchCutStartToken {
      get => mNotchCutStartToken;
      set => SetProperty (ref mNotchCutStartToken, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mNotchCutStartToken;

   [JsonPropertyName ("notchCutEndToken")]
   [Key (80)]
   public string NotchCutEndToken {
      get => mNotchCutEndToken;
      set => SetProperty (ref mNotchCutEndToken, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mNotchCutEndToken;

   [JsonPropertyName ("machine")]
   [Key (82)]
   public MachineType Machine {
      get => mMachine;
      set => SetProperty (ref mMachine, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   MachineType mMachine;

   [JsonPropertyName ("workpieceOptionsFilename")]
   [Key (84)]
   public string WorkpieceOptionsFilename {
      get => mWorkpieceOptionsFilename;
      set => SetProperty (ref mWorkpieceOptionsFilename, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   string mWorkpieceOptionsFilename;

   [JsonPropertyName ("showToolingNames")]
   [Key (86)]
   public bool ShowToolingNames {
      get => mShowToolingNames;
      set => SetProperty (ref mShowToolingNames, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mShowToolingNames;

   [JsonPropertyName ("showToolingExtents")]
   [Key (88)]
   public bool ShowToolingExtents {
      get => mShowToolingExtents;
      set => SetProperty (ref mShowToolingExtents, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mShowToolingExtents;

   [JsonPropertyName ("deadbandWidth")]
   [Key (90)]
   public double DeadbandWidth {
      get => mDeadbandWidth;
      set => SetProperty (ref mDeadbandWidth, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mDeadbandWidth;

   [JsonPropertyName ("movementSpeed")]
   [Key (92)]
   public double MovementSpeed {
      get => mMovementSpeed;
      set => SetProperty (ref mMovementSpeed, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMovementSpeed = 100;

   [JsonPropertyName ("machiningSpeed")]
   [Key (94)]
   public double MachiningSpeed {
      get => mMachiningSpeed;
      set => SetProperty (ref mMachiningSpeed, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mMachiningSpeed = 2.4;

   [JsonPropertyName ("optimizerType")]
   [Key (96)]
   public EOptimize OptimizerType {
      get {
         return mOptimizerType;
      }
      set {
         SetProperty (ref mOptimizerType, value);
      }
   }
   [IgnoreMember] // Ignore this field for MessagePack serialization
   EOptimize mOptimizerType = EOptimize.Time;

   [JsonPropertyName ("cutWeb")]
   [Key (97)]
   public bool CutWeb {
      get => mCutweb;
      set => SetProperty (ref mCutweb, value);
   }
   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mCutweb;

   [JsonPropertyName ("cutFlange")]
   [Key (98)]
   public bool CutFlange {
      get => mCutFlange;
      set => SetProperty (ref mCutFlange, value);
   }
   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mCutFlange;

   [JsonPropertyName ("version")]
   [Key (99)]
   public string Version { get; init; }

   [Key (100)]
   [JsonPropertyName ("leastWJLength")]
   public double LeastWJLength { get => mLeastWJLength; set => SetProperty (ref mLeastWJLength, value); }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   double mLeastWJLength;

   [JsonPropertyName ("slotWithWJTOnly")]
   [Key (101)]
   public bool SlotWithWJTOnly {
      get => mSlotWithWJTOnly;
      set => SetProperty (ref mSlotWithWJTOnly, value);
   }
   
   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mSlotWithWJTOnly;

   [JsonPropertyName ("dualFlangeCutoutNotchOnly")]
   [Key (102)]
   public bool DualFlangeCutoutNotchOnly {
      get => mDualFlangeCutoutNotchOnly;
      set => SetProperty (ref mDualFlangeCutoutNotchOnly, value);
   }

   [IgnoreMember] // Ignore this field for MessagePack serialization
   bool mDualFlangeCutoutNotchOnly;

   #endregion Properties with JSON Attributes

   #region Data Members
   [IgnoreMember] // Ignore these fields for MessagePack serialization
   JsonSerializerOptions mJSONWriteOptions, mJSONReadOptions;
   #endregion

   #region JSON Read/Write Methods
   // Method to serialize the singleton instance to a JSON file
   public void SaveSettingsToJson (string filePath) {
      // Serialize the object to binary JSON using MessagePack
      var binaryJson = MessagePackSerializer.Serialize (It);

      // Write the binary JSON to the file
      File.WriteAllBytes (filePath, binaryJson);
   }
   public void SaveSettingsToJsonASCII (string filePath) {
      // JSON serializer options
      var jsonOptions = new JsonSerializerOptions {
         Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Ensures ASCII encoding
         WriteIndented = true, // Optional: pretty-printed JSON for readability
      };

      // Get the properties that are marked with both [JsonPropertyName] and [Key]
      var properties = typeof (MCSettings)
          .GetProperties ()
          .Where (p => p.GetCustomAttribute<JsonPropertyNameAttribute> () != null
                   && p.GetCustomAttribute<KeyAttribute> () != null)
          .ToList ();

      // Create a dictionary to store the filtered properties and their values
      var jsonObject = new Dictionary<string, object> ();

      foreach (var prop in properties) {
         var value = prop.GetValue (It); // Get the value of the property
         var jsonPropertyName = prop.GetCustomAttribute<JsonPropertyNameAttribute> ().Name; // Get the JsonPropertyName
         jsonObject[jsonPropertyName] = value; // Add the value to the dictionary
      }

      // Serialize the filtered properties to JSON
      string jsonString = JsonSerializer.Serialize (jsonObject, jsonOptions);

      // Ensure ASCII encoding (non-ASCII chars replaced with '?')
      byte[] asciiBytes = Encoding.ASCII.GetBytes (jsonString);

      // Write the ASCII bytes to the specified file
      File.WriteAllBytes (filePath, asciiBytes);
   }

   public void LoadSettingsFromJson (string filePath) {
      if (File.Exists (filePath)) {
         mJSONReadOptions ??= new JsonSerializerOptions {
            Converters = { new JsonStringEnumConverter () } // Converts Enums from their string representation
         };
         byte[] fileBytes = File.ReadAllBytes (filePath);
         // Check if the file is binary JSON (e.g., MessagePack) or ASCII JSON
         bool isBinary = IsBinaryFile (fileBytes);
         if (isBinary) {
            // Deserialize from binary JSON (e.g., MessagePack)
            try {
               // Deserialize into a temporary object to avoid overwriting Version
               var tempSettings = MessagePackSerializer.Deserialize<MCSettings> (fileBytes);
               // Create a new instance with constructor-default Version
               sIt = new MCSettings (); // Assumes constructor sets Version = "1.0.17"
                                        // Copy relevant properties from temp, excluding Version
               CopySettingsExcludingVersion (tempSettings, sIt);
               // Optional: Log the file's original version for compatibility checks
               // Debug.WriteLine($"File written by version: {tempSettings.Version}");
            } catch (MessagePackSerializationException ex) {
               throw new InvalidOperationException ("Failed to deserialize binary JSON.", ex);
            }
         } else {
            // Deserialize from ASCII JSON
            try {
               var json = Encoding.UTF8.GetString (fileBytes);
               // Deserialize into a temporary object to avoid overwriting Version
               var tempSettings = JsonSerializer.Deserialize<MCSettings> (json, mJSONReadOptions);
               // Create a new instance with constructor-default Version
               sIt = new MCSettings (); // Assumes constructor sets Version = "1.0.17"
                                        // Copy relevant properties from temp, excluding Version
               CopySettingsExcludingVersion (tempSettings, sIt);
               // Optional: Log the file's original version for compatibility checks
               // var fileVersion = tempSettings.Version;
               // Debug.WriteLine($"File written by version: {fileVersion}");
            } catch (JsonException ex) {
               throw new InvalidOperationException ("Failed to deserialize ASCII JSON.", ex);
            }
         }
      } else {
         // If file doesn't exist, ensure sIt is initialized with constructor defaults
         sIt ??= new MCSettings (); // Version will be "1.0.17" from constructor
         MessageBox.Show ($"Settings file FChassis.User.Settings.JSON not found. Created default settings",
             "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
   }

   private void CopySettingsExcludingVersion (MCSettings source, MCSettings target) {
      var properties = typeof (MCSettings).GetProperties (BindingFlags.Public | BindingFlags.Instance);
      foreach (var prop in properties) {
         if (prop.Name != nameof (MCSettings.Version) && prop.CanRead && prop.CanWrite) {
            var value = prop.GetValue (source);
            prop.SetValue (target, value);
         }
      }
   }

   // Helper method to determine if a file is binary
   public static bool IsBinaryFile (byte[] fileBytes) {
      // Check for common binary file signatures or non-ASCII characters
      foreach (byte b in fileBytes) {
         if (b < 32 && b != 9 && b != 10 && b != 13) { // Non-printable ASCII characters (excluding tab, LF, CR)
            return true;
         }
         if (b > 126) { // Non-ASCII characters
            return true;
         }
      }
      return false;
   }
   #endregion
}