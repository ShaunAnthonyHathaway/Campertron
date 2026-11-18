using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using System;
using System.ComponentModel;

namespace Campertron.Views
{
    // Settings > System > Campground data (V011-1, decision D61). The view moves the focus where the next step is: to
    // Start refresh when the confirmation opens, to Cancel when the refresh starts, and back to Refresh campground data…
    // when the confirmation closes or the refresh ends while the focus was in this card; Escape in the confirmation is Back.
    public partial class CampgroundDataView : UserControl
    {
        private CampgroundDataViewModel? _Observed;
        private bool _WasConfirming;
        private bool _WasRunning;

        public CampgroundDataView()
        {
            InitializeComponent();
            RefreshConfirmation.AddHandler(KeyDownEvent, ConfirmationKeyDown, RoutingStrategies.Tunnel);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_Observed != null)
            {
                _Observed.PropertyChanged -= ViewModel_PropertyChanged;
            }
            _Observed = DataContext as CampgroundDataViewModel;
            _WasConfirming = _Observed?.IsConfirming ?? false;
            _WasRunning = _Observed?.IsRunning ?? false;
            if (_Observed != null)
            {
                _Observed.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ConfirmationKeyDown(object? Sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _Observed?.IsConfirming == true)
            {
                _Observed.Back();
                e.Handled = true;
            }
        }

        private void ViewModel_PropertyChanged(object? Sender, PropertyChangedEventArgs e)
        {
            if (_Observed == null)
            {
                return;
            }
            if (e.PropertyName == nameof(CampgroundDataViewModel.IsConfirming))
            {
                bool Now = _Observed.IsConfirming;
                if (Now && !_WasConfirming)
                {
                    FocusLater(StartRefreshButton);
                }
                else if (!Now && _WasConfirming && !_Observed.IsRunning && HasFocusWithin())
                {
                    FocusLater(RefreshDatabaseButton);
                }
                _WasConfirming = Now;
            }
            else if (e.PropertyName == nameof(CampgroundDataViewModel.IsRunning))
            {
                bool Now = _Observed.IsRunning;
                if (Now && !_WasRunning)
                {
                    FocusLater(CancelRefreshButton);
                }
                else if (!Now && _WasRunning && HasFocusWithin())
                {
                    FocusLater(RefreshDatabaseButton);
                }
                _WasRunning = Now;
            }
            // D62: an automatic restart that didn't happen gives Refresh campground data… back, and the focus with it
            else if (e.PropertyName == nameof(CampgroundDataViewModel.IsRestarting) && !_Observed.IsRestarting && HasFocusWithin())
            {
                FocusLater(RefreshDatabaseButton);
            }
        }

        // whether the keyboard focus is in this card (or nowhere: a focused button that was hidden leaves it nowhere)
        private bool HasFocusWithin()
        {
            object? Focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            return Focused == null || (Focused is Avalonia.Visual Element && (ReferenceEquals(Element, this) || this.IsVisualAncestorOf(Element)));
        }

        // after the layout has shown the target (a control that is not visible yet can't take the focus)
        private static void FocusLater(Control Target)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Target.IsEffectivelyVisible && Target.IsEffectivelyEnabled)
                {
                    Target.Focus(NavigationMethod.Tab);
                }
            }, DispatcherPriority.Loaded);
        }
    }
}
