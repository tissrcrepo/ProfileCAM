using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ChassisCAM.Presentation.Draw;

namespace ChassisCAM.Presentation.VisibilityConverters;
public class SimulationStatusToVisibilityConverter : IValueConverter {
   public object Convert (object value, Type targetType, object parameter, CultureInfo culture) {
      if (value is ProcessSimulator.ESimulationStatus status) switch (status) {
            case ProcessSimulator.ESimulationStatus.Running:
               if ((string)parameter == "Pause" || (string)parameter == "Stop")
                  return Visibility.Visible;
               break;
            case ProcessSimulator.ESimulationStatus.Paused:
               if ((string)parameter == "Stop" || (string)parameter == "Simulate")
                  return Visibility.Visible;
               break;
            case ProcessSimulator.ESimulationStatus.NotRunning:
               if ((string)parameter == "Simulate")
                  return Visibility.Visible;
               break;
         }

      return Visibility.Collapsed;
   }

   public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture) {
      throw new NotImplementedException ();
   }
}