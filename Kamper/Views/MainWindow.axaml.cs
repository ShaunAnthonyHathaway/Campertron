using Avalonia.Controls;

namespace Kamper.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Width = 800;
            Height = 600;
            CanResize = false;
        }
    }
}