using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Campertron.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Campertron.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        // "Open settings folder" uses the platform's launcher through Avalonia (a directory to show, no command line, no
        // shell); a view model that already has one (tests) keeps it
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is SettingsViewModel ViewModel && ViewModel.FolderLauncher == null)
            {
                ViewModel.FolderLauncher = Launch;
            }
        }

        private async Task<bool> Launch(DirectoryInfo Folder)
        {
            TopLevel? Top = TopLevel.GetTopLevel(this);
            return Top != null && await Top.Launcher.LaunchDirectoryInfoAsync(Folder);
        }
    }
}
