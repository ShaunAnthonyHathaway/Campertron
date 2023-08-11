using Avalonia.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Campertron.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Width = 1024;
            Height = 768;
            //CanResize = false;
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            String DbFile = System.IO.Path.Join(path, "RecreationDotOrg.db");
            if (File.Exists(DbFile) == false)
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("CampertronCLI.exe");
                }
                else
                {
                    Process.Start("CampertronCLI");
                }
                System.Environment.Exit(0);
            }
        }
    }
}