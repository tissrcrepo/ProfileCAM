using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfileCAM.Core {
   /// <summary>
   /// This structure holds the information for each sanity test.
   /// </summary>
   public struct SanityTestData {
      public SanityTestData () { }
      public string FxFileName { get; set; }
      public bool ToRun { get; set; }
      public MCSettings MCSettings { get; set; } = new MCSettings ();

      #region Data Members
      JsonSerializerOptions /*mJSONWriteOptions, */mJSONReadOptions;
      #endregion

      #region JSON read/write utilities
      /// <summary>
      /// This method deserializes the sanity test suite and creates SanityTestData instance
      /// </summary>
      /// <param name="element">The complete path to the sanity test suite (JSON file)</param>
      /// <returns>Sanity Data Instance</returns>
      /// <exception cref="FileNotFoundException">Throws this exception if the file is not found</exception>
      public SanityTestData LoadFromJsonElement (JsonElement element) {
         mJSONReadOptions = new JsonSerializerOptions {
            Converters = { new JsonStringEnumConverter () } // Converts Enums from their string representation
         };

         return new SanityTestData {
            FxFileName = element.GetProperty (nameof (FxFileName)).GetString (),
            ToRun = element.GetProperty (nameof (ToRun)).GetBoolean (),
            MCSettings = JsonSerializer.Deserialize<MCSettings> (element.GetProperty (nameof (MCSettings)).GetRawText (), mJSONReadOptions)
         };
      }
      #endregion
   }
}
