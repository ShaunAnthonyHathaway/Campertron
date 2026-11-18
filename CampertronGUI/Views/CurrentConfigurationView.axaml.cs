using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Campertron.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Campertron.Views
{
    // Settings > System > Current configuration (V011-2, decision D60).
    public partial class CurrentConfigurationView : UserControl
    {
        public CurrentConfigurationView()
        {
            InitializeComponent();
        }

        // Copy goes to the clipboard and Open settings folder to the platform's launcher (Avalonia's: a directory to show,
        // no command line, no shell), both only when the user presses them; a view model that already has them (tests)
        // keeps its own.
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is CurrentConfigurationViewModel ViewModel)
            {
                ViewModel.CopyText ??= Copy;
                ViewModel.FolderLauncher ??= Launch;
            }
        }

        private Task<bool> Copy(string Text)
        {
            return CampgroundDetailsView.CopyTextAsync(TopLevel.GetTopLevel(this)?.Clipboard, Text);
        }

        private async Task<bool> Launch(DirectoryInfo Folder)
        {
            TopLevel? Top = TopLevel.GetTopLevel(this);
            return Top != null && await Top.Launcher.LaunchDirectoryInfoAsync(Folder);
        }
    }
}
