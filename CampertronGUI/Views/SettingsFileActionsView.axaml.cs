using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace Campertron.Views
{
    public partial class SettingsFileActionsView : UserControl
    {
        public SettingsFileActionsView()
        {
            InitializeComponent();
            ApplyGuided();
        }

        // Inside the guided setup (v0.4.0's phase 5, GuidedEditor): Save and continue, the guided setup's command, stands where
        // Save is, and Save is not shown; outside it, Save as before
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs Change)
        {
            base.OnPropertyChanged(Change);
            if (Change.Property == GuidedEditor.ContinueCommandProperty)
            {
                ApplyGuided();
            }
        }

        private void ApplyGuided()
        {
            ICommand? Continue = GuidedEditor.GetContinueCommand(this);
            SaveButton.IsVisible = Continue == null;
            ContinueButton.IsVisible = Continue != null;
            ContinueButton.Command = Continue;
        }
    }
}
