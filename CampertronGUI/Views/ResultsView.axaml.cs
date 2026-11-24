using Avalonia;
using Avalonia.Controls;
using Campertron.ViewModels;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Campertron.Views
{
    public partial class ResultsView : UserControl
    {
        // the least height of the results card (the choice of search, a summary line, the list's minimum and the actions)
        internal const double ResultsCardMinHeight = 260;

        public ResultsView()
        {
            InitializeComponent();
            ResultsScroll.PropertyChanged += (_, e) =>
            {
                if (e.Property == ScrollViewer.ViewportProperty)
                {
                    FitFrame();
                }
            };
            RunCard.SizeChanged += (_, _) => FitFrame();
            // D106: the two-column layout gives the run card its own column, so the heights are fitted again when it changes
            ResultsColumn.PropertyChanged += (_, e) =>
            {
                if (e.Property == ResponsiveColumns.IsWideProperty)
                {
                    Fit(Bounds.Height);
                }
            };
        }

        // The booking page opens through the platform's launcher (Avalonia's ILauncher: a checked https link to show, no
        // command line, no shell), and copies go to the clipboard, both only on the user's action; a view model that
        // already has them (tests) keeps its own.
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_Watched != null)
            {
                _Watched.PropertyChanged -= ViewModel_PropertyChanged;
                _Watched = null;
            }
            if (DataContext is ResultsViewModel ViewModel)
            {
                ViewModel.LinkLauncher ??= Launch;
                ViewModel.CopyText ??= Copy;
                _Watched = ViewModel;
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            Fit(Bounds.Height);
        }

        private ResultsViewModel? _Watched;

        private void ViewModel_PropertyChanged(object? Sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResultsViewModel.HasResults))
            {
                Fit(Bounds.Height);
            }
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

        // A low window keeps the result list usable: the list of search files and the summary get smaller, and the list of
        // search files gets smaller still once there are results (it scrolls; Start stays where it is). The controls above
        // the result list (Start, Cancel, the status) keep their size. In the two-column layout (D106) the run card has a
        // column of its own beside the results, so its list of search files may take more of the height.
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            Fit(e.NewSize.Height);
        }

        // The frame fills the viewport, so the result list's star row is bounded (virtualization needs a bounded height); with
        // results it is never lower than the run card and the results card's least height (side by side in the two-column
        // layout, D106: the higher of the two), so a low window (or a wide font) scrolls the tab instead of squeezing the list
        // under the actions.
        private void FitFrame()
        {
            double Viewport = ResultsScroll.Viewport.Height;
            if (Viewport <= 0)
            {
                return;
            }
            bool Results = (DataContext as ResultsViewModel)?.HasResults == true;
            double Least = !Results ? 0
                : ResultsColumn.IsWide ? Math.Max(RunCard.Bounds.Height, ResultsCardMinHeight) + 8
                : RunCard.Bounds.Height + 8 + ResultsCardMinHeight + 8;
            double Wanted = Math.Max(Viewport, Least);
            if (Math.Abs(ResultsFrame.Height - Wanted) > 0.5 || double.IsNaN(ResultsFrame.Height))
            {
                ResultsFrame.Height = Wanted;
            }
        }

        private void Fit(double Height)
        {
            FitFrame();
            if (Height <= 0)
            {
                return;
            }
            bool Results = (DataContext as ResultsViewModel)?.HasResults == true;
            SearchChoicesScroll.MaxHeight = ResultsColumn.IsWide ? Math.Clamp(Height * 0.5, 64, 640)
                : Results ? Math.Clamp(Height * 0.1, 44, 180) : Math.Clamp(Height * 0.3, 64, 320);
            ResultSummaryScroll.MaxHeight = Math.Clamp(Height * (Height < 600 ? 0.1 : 0.14), 40, 180);
        }
    }
}
