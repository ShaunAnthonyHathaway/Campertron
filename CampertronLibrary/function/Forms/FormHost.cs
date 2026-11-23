namespace CampertronLibrary.function.Forms
{
    // The thread a form lives on, as its host gives it (the shared form layer, D79's PR 3). A form is used by one thread at
    // a time: the desktop GUI's UI thread (its host is Avalonia's dispatcher and timers, CampertronGUI's GuiHost), or
    // whatever a web host uses to serialize a session's work. A search, a refresh and a campground's filter choices run on
    // worker threads and hand their progress and their end back through the host, so nothing of a form changes on another
    // thread. The host holds delegates only and no state.
    public sealed class FormHost
    {
        public FormHost(Func<Action<Action>> CapturePost, Func<TimeSpan, Action, IDisposable> Delay, Func<TimeSpan, Action, IDisposable> Ticker)
        {
            this.CapturePost = CapturePost ?? throw new ArgumentNullException(nameof(CapturePost));
            this.Delay = Delay ?? throw new ArgumentNullException(nameof(Delay));
            this.Ticker = Ticker ?? throw new ArgumentNullException(nameof(Ticker));
        }

        // Called on the form's thread; gives the way to queue a job for that thread from any thread, which never blocks
        // the caller. A form asks for it when it starts work, never on a worker thread (TD-56).
        public Func<Action<Action>> CapturePost { get; }

        // a one-shot timer whose job runs on the form's thread; disposing it stops it
        public Func<TimeSpan, Action, IDisposable> Delay { get; }

        // a repeating timer whose job runs on the form's thread; disposing it stops it
        public Func<TimeSpan, Action, IDisposable> Ticker { get; }

        // a form that was given no host and needs one says so (a host's mistake, never a user's)
        internal static FormHost Need(FormHost? Host)
        {
            return Host ?? throw new InvalidOperationException("This form needs its host's thread (FormHost), and none was given.");
        }
    }
}
