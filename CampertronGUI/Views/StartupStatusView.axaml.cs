using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Campertron.ViewModels;
using System;
using System.ComponentModel;

namespace Campertron.Views
{
    public partial class StartupStatusView : UserControl
    {
        private MainWindowViewModel? _ViewModel;

        public StartupStatusView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_ViewModel != null)
            {
                _ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            _ViewModel = DataContext as MainWindowViewModel;
            if (_ViewModel != null)
            {
                _ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        // When an attempt fails, Try again gets the focus, so the keyboard can retry at once.
        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.State) && _ViewModel?.IsFailed == true)
            {
                Dispatcher.UIThread.Post(() => RetryButton.Focus());
            }
        }

        private void RetryButton_Click(object? sender, RoutedEventArgs e)
        {
            _ViewModel?.Retry();
        }
    }
}
