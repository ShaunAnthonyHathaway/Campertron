using Avalonia.Controls;

namespace Campertron.Views
{
    public partial class NtfySettingsView : UserControl
    {
        public NtfySettingsView()
        {
            InitializeComponent();
            ListRowFocus.Attach(this.FindControl<ItemsControl>("NtfyTagsList")!, "TagBox");
        }
    }
}
