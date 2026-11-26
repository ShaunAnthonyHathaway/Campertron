using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Campertron.Views
{
    public partial class NtfySettingsView : UserControl
    {
        public NtfySettingsView()
        {
            InitializeComponent();
            ListRowFocus.Attach(this.FindControl<ItemsControl>("NtfyTagsList")!, "TagBox");
        }

        // Inside the guided setup (v0.4.0's phase 5, GuidedEditor) the way to General.yaml's outputTo is not shown: the guided
        // setup's last step saves outputTo, and the button would only change the Settings tab's chosen file
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs Change)
        {
            base.OnPropertyChanged(Change);
            if (Change.Property == GuidedEditor.ContinueCommandProperty && GuidedEditor.IsGuided(this))
            {
                ShowGeneralButton.SetValue(IsVisibleProperty, false, BindingPriority.Animation);
            }
        }
    }
}
