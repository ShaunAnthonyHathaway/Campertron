using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CampertronLibrary.function.RecDotOrg.data;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static ConsoleConfig;

namespace Campertron.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Size, minimum size and startup position are set in MainWindow.axaml.
            //CanResize = false;
            AddHandler(KeyDownEvent, RememberFocusBeforeTab, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, WrapTabAtContentEdge, RoutingStrategies.Bubble, handledEventsToo: true);

            CampertronInternalConfig config = Load.GetConfig();

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var configpath = Path.Join(path, "CampertronConfig");
            String DbFile = System.IO.Path.Join(configpath, "RecreationDotOrg.db");
            //if (File.Exists(DbFile) == false)
            //{
            //    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            //    {
            //        Process.Start("CampertronConsole.exe");
            //    }
            //    else
            //    {
            //        Process.Start("CampertronConsole");
            //    }
            //    System.Environment.Exit(0);
            //}
        }

        // Keyboard-trap workaround (TD-39, beta.8, build-and-test.md §P), tested in Campertron. With Avalonia 12.1.6,
        // Campertron's window and a minimal TabControl with two buttons both reproduce this: Tab and Shift+Tab do not
        // leave a TabControl's selected content, and at its last (or first) focusable element focus stays where it is
        // (a window without a TabControl wraps normally). Avalonia's own navigation runs first; only when a plain Tab or
        // Shift+Tab left focus unchanged inside the tab content does focus move to the selected tab header, where the
        // arrow keys switch tabs and Tab enters the content again. Reassess after any Avalonia upgrade or an upstream
        // fix for this exact behavior.
        private IInputElement? _FocusBeforeTab;

        private static bool IsPlainTab(KeyEventArgs e)
        {
            return e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None;
        }

        private void RememberFocusBeforeTab(object? Sender, KeyEventArgs e)
        {
            _FocusBeforeTab = IsPlainTab(e) ? FocusManager?.GetFocusedElement() : null;
        }

        private void WrapTabAtContentEdge(object? Sender, KeyEventArgs e)
        {
            IInputElement? Before = _FocusBeforeTab;
            _FocusBeforeTab = null;
            if (!IsPlainTab(e) || Before is not Visual Focused || Focused is TabItem
                || !ReferenceEquals(FocusManager?.GetFocusedElement(), Before)
                || MainTabControl.SelectedItem is not TabItem SelectedTab || !MainTabControl.IsVisualAncestorOf(Focused))
            {
                return;
            }
            e.Handled = SelectedTab.Focus(NavigationMethod.Tab) || e.Handled;
        }
    }
}