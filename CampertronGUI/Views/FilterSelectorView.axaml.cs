using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Campertron.ViewModels;

namespace Campertron.Views
{
    // A filter selector (decision D51). The list of values is one tab stop: arrow keys move between values (the ListBox's
    // own navigation) and Space checks or unchecks the focused value, so a long list never becomes hundreds of tab stops.
    public partial class FilterSelectorView : UserControl
    {
        public FilterSelectorView()
        {
            InitializeComponent();
            ChoiceList.AddHandler(KeyDownEvent, OnChoiceKeyDown, RoutingStrategies.Tunnel);
        }

        private void OnChoiceKeyDown(object? Sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || DataContext is not FilterSelectorField Field)
            {
                return;
            }
            FilterOptionRow? Row = (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as FilterOptionRow
                                   ?? ChoiceList.SelectedItem as FilterOptionRow;
            if (Row != null)
            {
                Field.Toggle(Row);
                e.Handled = true;
            }
        }
    }
}
