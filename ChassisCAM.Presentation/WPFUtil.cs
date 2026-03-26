using System.Collections;
using System.Windows.Controls;
using static ChassisCAM.Core.Geometries.DoubleExtensions;
using static ChassisCAM.Core.Geometries.IntExtensions;
namespace ChassisCAM.Presentation;

static class WPFUtil {
   /// <summary>Binds an action to the 'Click' event of a button</summary>
   /// <param name="button">The button to bind</param>
   /// <param name="action">The action to execute when the button is clicked</param>
   public static void Bind (this Button button, Action action)
      => button.Click += (s, e) => action ();

   /// <summary>Binds a check-box to a boolean getter / setter</summary>
   /// <param name="cb">The checkbox to bind</param>
   /// <param name="getter">Getter is called to initialize the check-box</param>
   /// <param name="setter">Setter is called each time the user checks / unchecks</param>
   public static void Bind (this CheckBox cb, Func<bool> getter, Action<bool> setter) {
      cb.IsChecked = getter ();
      cb.Checked += (s, e) => setter (true);
      cb.Unchecked += (s, e) => setter (false);
   }

   /// <summary>Binds a ListBox that is used only to 'shuffle' the order of some items</summary>
   /// The listbox should be accompanied by UP and DOWN arrow buttons (actual Button controls),
   /// and when either of these are clicked, the currently selected item is shuffled up or down,
   /// and the setter is called to update the original source collection
   /// <param name="lb">List-box to work with</param>
   /// <param name="getter">Getter should return the set of items to display in the list</param>
   /// <param name="setter">Setter is called with an enumerable of newly ordered items, whenever the list-box is shuffled</param>
   /// <param name="up">The button that is used to shuffle the selected item UP</param>
   /// <param name="down">The button that is used to shuffle the selected item DOWN</param>
   public static void Bind (this ListBox lb, Button up, Button down,
                            Func<IEnumerable> getter, Action<IEnumerable> setter) {
      lb.Items.Clear ();
      foreach (var obj in getter ())
         lb.Items.Add (obj);

      lb.SelectedIndex = 0;
      up.Bind (() => _shuffle (lb, -1, setter));
      down.Bind (() => _shuffle (lb, 1, setter));

      static void _shuffle (ListBox lb, int delta, Action<IEnumerable> setter) {
         int n = lb.SelectedIndex;
         if (n == -1)
            return;

         int n2 = n + delta;
         if (n2 < 0 || n2 >= lb.Items.Count)
            return;

         var item = lb.Items[n];
         lb.Items.RemoveAt (n);
         lb.Items.Insert (n2, item);
         lb.SelectedIndex = n2;
         setter (lb.Items);
      }
   }

   /// <summary>Binds a radio-button : getter tells us it it should start off checked, setter is called when the user clicks it</summary>
   /// <param name="rb">The radio-button to bind</param>
   /// <param name="getter">Getter called to figure if the radio-button should start off 'checked'</param>
   /// <param name="setter">Setter is called when the user 'checks' this radio button (not called when this gets unchecked!)</param>
   /// Note that the setter is not called when the radio-button gets 'unchecked' - if you have
   /// designed it correctly, some other radio button has gotten checked at this point, and that
   /// setter should take the appropriate action. (For this to work correctly, ensure that you have
   /// set the GroupName consistently for the entire group of radio-buttons).
   public static void Bind (this RadioButton rb, Func<bool> getter, Action setter) {
      rb.IsChecked = getter ();
      rb.Checked += (s, e) => setter ();
   }

   /// <summary>Binds a TextBox to a string</summary>
   /// <param name="tb">The text-box</param>
   /// <param name="getter">Getter used to fetch the initial value to display</param>
   /// <param name="setter">Setter that is called each time the text is updated (called on LostFocus, not for each keystroke)</param>
   public static void Bind (this TextBox tb, Func<string> getter, Action<string> setter) {
      tb.Text = getter ().ToString ();
      tb.GotFocus += (s, e) => tb.SelectAll ();
      tb.LostFocus += (s, e) => setter (tb.Text);
      tb.TextChanged += (s, e) => setter (tb.Text);
   }

   /// <summary>Binds a text-box to a double</summary>
   /// <param name="tb">The text-box</param>
   /// <param name="getter">Getter used to fetch the intial value of the double to display</param>
   /// <param name="setter">Setter called whenever we have a new double (only if parsed correctly)</param>
   public static void Bind (this TextBox tb, Func<double> getter, Action<double> setter) {
      tb.Text = getter ().ToString ();
      tb.GotFocus += (s, e) => tb.SelectAll ();
      tb.LostFocus += (s, e) => {
         if (double.TryParse (tb.Text, out var f))
            setter (f);

         tb.Text = getter ().ToString ();
      };
   }

   /// <summary>Binds a text-box to a int</summary>
   /// <param name="tb">The text-box</param>
   /// <param name="getter">Getter used to fetch the intial value of the double to display</param>
   /// <param name="setter">Setter called whenever we have a new double (only if parsed correctly)</param>
   public static void Bind (this TextBox tb, Func<int> getter, Action<int> setter) {
      tb.Text = getter ().ToString ();
      tb.GotFocus += (s, e) => tb.SelectAll ();
      tb.LostFocus += (s, e) => {
         if (int.TryParse (tb.Text, out var f))
            setter (f);

         tb.Text = getter ().ToString ();
      };
   }

   /// <summary>
   /// Binds a ComboBox to an enum or a list of options.
   /// </summary>
   /// <typeparam name="T">The type of the item in the ComboBox (e.g., enum or string)</typeparam>
   /// <param name="cb">The ComboBox to bind</param>
   /// <param name="getter">Getter used to fetch the initial selected value</param>
   /// <param name="setter">Setter called whenever the selected item changes</param>
   public static void Bind<T> (this ComboBox cb, Func<T> getter, Action<T> setter) {
      // Initialize the ComboBox with the current value from the getter
      cb.SelectedItem = getter ();

      // When the selection changes, update the bound property
      cb.SelectionChanged += (s, e) => {
         if (cb.SelectedItem is T selectedItem)
            setter (selectedItem);
      };
   }

   public static int IndexOf<T> (this T[] array, T item) => Array.IndexOf (array, item);
}
