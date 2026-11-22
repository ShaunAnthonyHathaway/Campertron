using Avalonia.Controls;

namespace Campertron.Views
{
    public partial class ListFieldView : UserControl
    {
        public ListFieldView()
        {
            InitializeComponent();
            ListRowFocus.Attach(this.FindControl<ItemsControl>("Rows")!, "RowBox");
        }
    }
}
