using CampertronLibrary.function.Base;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CampertronLibrary.function.Forms
{
    // The test notification of the Email.yaml and Ntfy.yaml editors (decisions D86 and D88, v0.3.0's PR 5;
    // business-rules-gui §17, business-rules-settings §32): its words, whether it can be sent now and why not, that one is
    // being sent, and its result. The send itself is the library's TestNotification (the saved file, the monitoring's
    // secret source and transport), never this form's: the form holds no setting, no password and no token, only fixed
    // words and the result's fixed words.
    //  - It is offered only for a file that exists and has no unsaved change: the test uses the file as it is saved (D86).
    //  - One at a time: while one is being sent, the button is not offered and TryBegin refuses another.
    //  - The desktop GUI runs it with Start: the send on a worker thread, its end posted back through the host's thread
    //    (FormHost), so the window never waits. The web UI calls TryBegin, Send (on its request, without the form's lock)
    //    and End itself.
    // Nothing here reads a secret, searches, writes a file or touches the monitoring.
    public sealed class TestNotificationForm : FormObject
    {
        private readonly SettingsFileViewModel _File;
        private readonly bool _Email;
        private readonly string _ConfigPath;
        private bool _IsSending;
        private string? _ResultText;
        private string _ResultSeverity = string.Empty;

        internal TestNotificationForm(SettingsFileViewModel File, bool Email, string ConfigPath)
        {
            _File = File;
            _Email = Email;
            _ConfigPath = ConfigPath;
            SendCommand = new RelayCommand(Start, () => CanSend);
        }

        // the desktop GUI's thread (GuiHost.Avalonia); the web UI gives none and runs the send itself
        internal FormHost? Host { get; set; }

        public string Heading => _Email ? "Test email" : "Test notification";
        public string ButtonLabel => _Email ? "Send a test email" : "Send a test notification";
        public string BusyText => _Email ? "Sending a test email…" : "Sending a test notification…";

        public string Explanation => _Email
            ? "Sends one test email with Email.yaml as it is saved: its SMTP server, port, user name, From address and recipients, and the SMTP password from the source email mode would use, in its order (the password file, then CAMPERTRON_EMAIL_PASSWORD as this program sees it, then the plain-text password). The password is read only when you press the button, for this one email; it is never shown, kept or written. The test works whatever outputTo is. It searches nothing, writes no file, records nothing in Camp.History and does not start, stop or change the monitoring."
            : "Publishes one test notification with Ntfy.yaml as it is saved: its server, topic, priority and tags, and the access token from the source the monitoring would use, in its order (the token file, then CAMPERTRON_NTFY_TOKEN as this program sees it, then none). The token is read only when you press the button, for this one notification; it is never shown, kept or written. The test works whether Enabled is on or off. It searches nothing, writes no file, records nothing in Camp.History and does not start, stop or change the monitoring.";

        public bool IsSending
        {
            get => _IsSending;
            private set => this.RaiseAndSetIfChanged(ref _IsSending, value);
        }

        // why the test can't be sent now, or null when it can (also while one is being sent: the busy text says it)
        public string? UnavailableReason
        {
            get
            {
                string Name = _File.FileName;
                if (_File.IsMissing || (_File is NtfySettingsViewModel Ntfy && Ntfy.IsNew))
                {
                    return _Email
                        ? Name + " does not exist yet: create and save it first."
                        : Name + " does not exist yet: enter the server and the topic, and save it first.";
                }
                if (_File.IsDirty || _File.HasConflict)
                {
                    return "The test uses " + Name + " as it is saved: save or revert your changes first.";
                }
                return null;
            }
        }
        public bool HasUnavailableReason => UnavailableReason != null && !IsSending;
        public bool CanSend => !IsSending && UnavailableReason == null;

        // the last test's result in fixed words, its severity in words first ("Info: …" sent, "Error: …" not sent)
        public string? ResultText
        {
            get => _ResultText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ResultText, value);
                this.RaisePropertyChanged(nameof(HasResult));
                this.RaisePropertyChanged(nameof(ResultIsError));
            }
        }
        public string ResultSeverity
        {
            get => _ResultSeverity;
            private set => this.RaiseAndSetIfChanged(ref _ResultSeverity, value);
        }
        public bool HasResult => ResultText != null;
        public bool ResultIsError => ResultSeverity == MessageSeverity.Error;

        public RelayCommand SendCommand { get; }

        // the editor's state changed (a change, a save, a reload): whether the test is offered, and why not
        internal void Refresh()
        {
            this.RaisePropertyChanged(nameof(UnavailableReason));
            this.RaisePropertyChanged(nameof(HasUnavailableReason));
            this.RaisePropertyChanged(nameof(CanSend));
            SendCommand.Refresh();
        }

        // starts a test if one can be sent now: true, and the form says it is sending; false when it can't (one is being
        // sent, the file is missing or has unsaved changes)
        public bool TryBegin()
        {
            if (!CanSend)
            {
                return false;
            }
            IsSending = true;
            ResultText = null;
            ResultSeverity = string.Empty;
            Refresh();
            return true;
        }

        // the test itself, with the saved file (any thread; no state of the form is used or changed)
        internal TestNotificationResult Send(Func<DateTime> Now, CancellationToken Cancel)
        {
            try
            {
                return _Email ? TestNotification.SendEmail(_ConfigPath, Now) : TestNotification.SendNtfy(_ConfigPath, Now, Cancel);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                //a defect, never a secret: the exception's type only
                return new TestNotificationResult(false, "Error: the test could not be sent because of an unexpected problem (" + ex.GetType().Name + ").");
            }
        }

        // the test ended: its result, and the button is offered again
        internal void End(TestNotificationResult Result)
        {
            IsSending = false;
            ResultSeverity = Result.Severity;
            ResultText = Result.Text;
            Refresh();
        }

        // the desktop GUI's button: the send on a worker thread, its end on the form's thread through the host
        public void Start()
        {
            FormHost Thread = FormHost.Need(Host);
            if (!TryBegin())
            {
                return;
            }
            Action<Action> Post = Thread.CapturePost();
            Task.Run(() =>
            {
                TestNotificationResult Result = Send(() => DateTime.Now, CancellationToken.None);
                Post(() => End(Result));
            });
        }
    }
}
