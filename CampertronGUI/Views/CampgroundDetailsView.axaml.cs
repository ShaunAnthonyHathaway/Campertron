using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Campertron.ViewModels;
using System;
using System.Threading.Tasks;

namespace Campertron.Views
{
    public partial class CampgroundDetailsView : UserControl
    {
        public const string CopiedMessage = "Copied to the clipboard.";
        public const string CopyFailedMessage = "Couldn't copy. Select the ID and copy it yourself.";

        // Shown before a campground is selected; each tab explains its own steps.
        public static readonly StyledProperty<string?> EmptyHintProperty =
            AvaloniaProperty.Register<CampgroundDetailsView, string?>(nameof(EmptyHint));

        public string? EmptyHint
        {
            get => GetValue(EmptyHintProperty);
            set => SetValue(EmptyHintProperty, value);
        }

        public CampgroundDetailsView()
        {
            InitializeComponent();
        }

        // A copy message belongs to the campground it was shown for.
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            ShowCopyStatus(null);
        }

        private async void CopyIdButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not CampgroundDetails Details || !Details.HasCampground)
            {
                return;
            }
            bool Copied = await CopyTextAsync(TopLevel.GetTopLevel(this)?.Clipboard, Details.CampgroundId);
            // The selection may have changed while the clipboard was busy.
            if (ReferenceEquals(DataContext, Details))
            {
                ShowCopyStatus(Copied ? CopiedMessage : CopyFailedMessage);
            }
        }

        // Never throws: a missing or failing clipboard is reported as false, never as a crash.
        public static async Task<bool> CopyTextAsync(IClipboard? Clipboard, string Text)
        {
            if (Clipboard == null || string.IsNullOrEmpty(Text))
            {
                return false;
            }
            try
            {
                await Clipboard.SetTextAsync(Text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ShowCopyStatus(string? Message)
        {
            CopyStatusText.Text = Message;
            CopyStatusText.IsVisible = Message != null;
        }
    }
}
