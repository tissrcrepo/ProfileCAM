using System.Globalization;

namespace ProfileCAM.Input {
   public static class CsvReader {
      static bool TryGetCaseInsensitive<T> (this Dictionary<string, T> dictionary, string key, out T value) {
         ArgumentNullException.ThrowIfNull (dictionary);

         var comparisonKey = dictionary.Keys.FirstOrDefault (k =>
             string.Equals (k, key, StringComparison.OrdinalIgnoreCase));

         if (comparisonKey != null) {
            value = dictionary[comparisonKey];
            return true;
         }

         value = default!; // Using null-forgiving operator (we know default is safe for our use case)
         return false;
      }

      public static PartData ReadPartData (string filePath) {
         string[] lines;
         using (var fs = new FileStream (filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
         using (var reader = new StreamReader (fs)) {
            var lineList = new List<string> ();
            string line;
            while ((line = reader.ReadLine ()) != null)
               lineList.Add (line);
            lines = [.. lineList];
         }

         var parameters = new Dictionary<string, double> ();
         bool lh = false;
         var holes = new List<HoleData> ();

         // Read parameters
         foreach (var line in lines.Take (6)) // Assuming first 6 lines contain parameters
         {
            if (string.IsNullOrWhiteSpace (line))
               continue;

            var parts = line.Split (',');
            if (parts.Length < 2)
               continue;

            string paramName = parts[0].Trim ().ToLower ();
            string valueStr = parts[1].Trim ();

            if (paramName == "lh") {
               lh = valueStr == "1";
            } else if (double.TryParse (valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value)) {
               parameters[paramName] = value;
            }
         }

         if (!parameters.TryGetCaseInsensitive ("thickness", out double thickness))
            throw new InvalidDataException ("Missing thickness parameter");
         if (!parameters.TryGetCaseInsensitive ("radius", out double radius))
            throw new InvalidDataException ("Missing radius parameter");
         if (!parameters.TryGetCaseInsensitive ("length", out double length))
            throw new InvalidDataException ("Missing length parameter");
         if (!parameters.TryGetCaseInsensitive ("height", out double height))
            throw new InvalidDataException ("Missing height parameter");
         if (!parameters.TryGetCaseInsensitive ("width", out double width))
            throw new InvalidDataException ("Missing width parameter");

         double kFactor = parameters.TryGetCaseInsensitive ("k_factor", out double kf) ? kf : 0.43;
         double bendAngle = parameters.TryGetCaseInsensitive ("bend_angle", out double ba) ? ba : 90.0;

         // Read circles (starting after parameters and empty line)
         bool headerFound = false;
         for (int i = 6; i < lines.Length; i++) // Start after parameter section
         {
            if (string.IsNullOrWhiteSpace (lines[i]))
               continue;

            if (!headerFound) {
               headerFound = true;
               continue; // Skip header line
            }

            var parts = lines[i].Split (',');
            RefCode pcode = parts[3][0] switch {
               'w' or 'W' => RefCode.Web,
               'b' or 'B' => RefCode.Bottom,
               't' or 'T' => RefCode.Top,
               _ => throw new Exception ("Unknown flange")
            };
            RefCode mcode = parts[4][0] switch {
               'w' or 'W' => RefCode.Web,
               'b' or 'B' => RefCode.Bottom,
               't' or 'T' => RefCode.Top,
               _ => throw new Exception ("Unknown flange")
            };
            holes.Add (new HoleData (
                x: double.Parse (parts[0], CultureInfo.InvariantCulture),
                y: double.Parse (parts[1], CultureInfo.InvariantCulture),
                dia: double.Parse (parts[2], CultureInfo.InvariantCulture),
                width, radius, thickness, bendAngle, lh ? PartType.LH : PartType.RH,
                pCode: pcode,
                mCode: mcode,
                holewidth: string.IsNullOrWhiteSpace (parts[5]) ? null : double.Parse (parts[5], CultureInfo.InvariantCulture),
                holerot: string.IsNullOrWhiteSpace (parts[6]) ? null : double.Parse (parts[6], CultureInfo.InvariantCulture)
            ));
         }
         return new PartData (thickness, radius, length, height, width, kFactor, lh ? PartType.LH : PartType.RH, bendAngle, holes);
      }
   }
}