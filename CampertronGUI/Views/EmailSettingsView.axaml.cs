using Avalonia.Controls;

namespace Campertron.Views
{
    public partial class EmailSettingsView : UserControl
    {
        public EmailSettingsView()
        {
            InitializeComponent();
            ListRowFocus.Attach(this.FindControl<ItemsControl>("RecipientsList")!, "RecipientBox");
        }
    }
}
