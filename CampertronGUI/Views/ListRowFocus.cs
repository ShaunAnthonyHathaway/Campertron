using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using System.Linq;

namespace Campertron.Views
{
    // D71: a row added with Add (recipient, tag, date) gets the keyboard focus in its box as soon as it is shown, so the
    // keyboard goes on with typing the value
    internal static class ListRowFocus
    {
        internal static void Attach(ItemsControl List, string BoxName)
        {
            List.ContainerPrepared += (_, e) =>
            {
                Control Container = e.Container;
                if (Container.DataContext is not ListRow Row || !Row.FocusWhenShown)
                {
                    return;
                }
                Row.FocusWhenShown = false;
                Dispatcher.UIThread.Post(() =>
                {
                    Container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(Box => Box.Name == BoxName)?.Focus();
                }, DispatcherPriority.Loaded);
            };
        }
    }
}
