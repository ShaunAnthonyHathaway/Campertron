using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Campertron.ViewModels;
using System;
using System.Threading.Tasks;

namespace Campertron.Views
{
    // The Monitoring tab (v0.4.0's phase 4). After the user's Start, Stop or Restart the focus goes to the state line (the
    // pressed button is unavailable while the monitoring starts or stops), so a screen reader reads what happens, as the web
    // UI's page focuses its status after an action; a reading of the monitoring never moves the focus. The booking page opens
    // through the platform's launcher and copies go to the clipboard, only on the user's action, as in the Results tab.
    public partial class MonitoringView : UserControl
    {
        private MonitoringViewModel? _Observed;

        public MonitoringView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_Observed != null)
            {
                _Observed.Acted -= FocusState;
            }
            _Observed = DataContext as MonitoringViewModel;
            if (_Observed != null)
            {
                _Observed.LinkLauncher ??= Launch;
                _Observed.CopyText ??= Copy;
                _Observed.Acted += FocusState;
            }
        }

        private void FocusState()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (MonitoringStateText.IsEffectivelyVisible)
                {
                    MonitoringStateText.Focus(NavigationMethod.Unspecified);
                }
            }, DispatcherPriority.Loaded);
        }

        private async Task<bool> Launch(Uri Link)
        {
            TopLevel? Top = TopLevel.GetTopLevel(this);
            return Top != null && await Top.Launcher.LaunchUriAsync(Link);
        }

        private Task<bool> Copy(string Text)
        {
            return CampgroundDetailsView.CopyTextAsync(TopLevel.GetTopLevel(this)?.Clipboard, Text);
        }
    }
}
