using Avalonia.Threading;
using CampertronLibrary.function.Forms;

namespace Campertron
{
    // The desktop GUI as the host of the library's forms (the shared form layer, D79's PR 3): their thread is Avalonia's UI
    // thread. CapturePost is called on the UI thread and gives a post to its dispatcher, so no worker thread ever asks for
    // Dispatcher.UIThread (D49, TD-56); the timers are the dispatcher's. Delegates only: no state.
    internal static class GuiHost
    {
        internal static readonly FormHost Avalonia = new FormHost(
            () =>
            {
                Dispatcher Ui = Dispatcher.UIThread;
                return Job => Ui.Post(Job, DispatcherPriority.Background);
            },
            (Due, Job) => DispatcherTimer.RunOnce(Job, Due, DispatcherPriority.Background),
            (Interval, Job) => DispatcherTimer.Run(() => { Job(); return true; }, Interval, DispatcherPriority.Background));
    }
}
