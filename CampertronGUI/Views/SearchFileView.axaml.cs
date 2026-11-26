using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Campertron.Views
{
    public partial class SearchFileView : UserControl
    {
        public SearchFileView()
        {
            InitializeComponent();
        }

        // Inside the guided setup (v0.4.0's phase 5, GuidedEditor) the search's main settings only, as the web UI's guided
        // setup shows them: the days of the week and the filters keep their values and stay in the Searches tab. Hidden above
        // their own binding (a file that is missing hides them anyway), so the Searches tab's editor is unchanged
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs Change)
        {
            base.OnPropertyChanged(Change);
            if (Change.Property == GuidedEditor.ContinueCommandProperty && GuidedEditor.IsGuided(this))
            {
                WeekdaysCard.SetValue(IsVisibleProperty, false, BindingPriority.Animation);
                FiltersCard.SetValue(IsVisibleProperty, false, BindingPriority.Animation);
            }
        }
    }
}
