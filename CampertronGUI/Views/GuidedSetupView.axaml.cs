using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using System;
using System.Linq;

namespace Campertron.Views
{
    // The Monitoring tab's Guided setup (v0.4.0's phase 5). After each action the focus goes to what the shared flow's outcome
    // says needs the user's attention (GuidedSetupAttention), as the web UI's guided setup gives it to its own element: the
    // step's heading when a step opens, the form's problems, the form's result after a save that did not go through, the
    // conflict with the file on disk, what the monitoring's start would refuse, what the step said, New search's message or the
    // new search's own page. A reading of the files never moves the focus; nothing moves it but the user's action.
    public partial class GuidedSetupView : UserControl
    {
        private GuidedSetupViewModel? _Observed;

        public GuidedSetupView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_Observed != null)
            {
                _Observed.AttentionRequested -= FocusAttention;
            }
            _Observed = DataContext as GuidedSetupViewModel;
            if (_Observed != null)
            {
                _Observed.AttentionRequested += FocusAttention;
            }
        }

        // the element that has the focus for what an outcome needs the user to see (laid out first: a control that is not
        // visible yet can't take it); the step's heading when that element is not shown
        private void FocusAttention(GuidedSetupAttention Attention)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Control? Target = Attention switch
                {
                    GuidedSetupAttention.Problems => _Observed?.IsMethodStep == true ? GuidedChoiceBlock : GuidedProblemsBlock,
                    GuidedSetupAttention.Status => Shown("StatusBlock"),
                    GuidedSetupAttention.Conflict => Shown("ConflictBlock"),
                    GuidedSetupAttention.Needs => GuidedNeedsBlock,
                    GuidedSetupAttention.Notice => _Observed?.HasMonitoringNotice == true ? GuidedMonitoringNoticeBlock : GuidedNoticeBlock,
                    GuidedSetupAttention.NewSearch => _Observed?.HasCreated == true ? GuidedCreatedBlock : GuidedNewSearchBlock,
                    _ => GuidedStepHeading,
                };
                if (Target == null || !Target.IsEffectivelyVisible || !Target.Focus(NavigationMethod.Unspecified))
                {
                    GuidedStepHeading.Focus(NavigationMethod.Unspecified);
                }
            }, DispatcherPriority.Loaded);
        }

        // the step's editor's own element of that name, while it is shown (the editors are the Settings and Searches tabs' views)
        private Control? Shown(string Name) =>
            this.GetVisualDescendants().OfType<Control>().FirstOrDefault(C => C.Name == Name && C.IsEffectivelyVisible);

        // the step's heading takes the focus (the window's lead to the guided setup, and a step opened from elsewhere)
        internal void FocusHeading()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (GuidedStepHeading.IsEffectivelyVisible)
                {
                    GuidedStepHeading.Focus(NavigationMethod.Unspecified);
                }
            }, DispatcherPriority.Loaded);
        }
    }
}
