using System.Runtime.ExceptionServices;
using static ConsoleConfig;

namespace CampertronLibrary.function.Base
{
    // the notification providers of the unattended monitoring (decision D67)
    internal enum NotificationProvider
    {
        Email,
        Ntfy
    }

    // both providers failed in one check: each one's failure, in provider order. Its message is fixed text; the failures'
    // own words come from ConsoleUi.DescribeFailure
    internal sealed class NotificationException : Exception
    {
        internal NotificationException(IReadOnlyList<(NotificationProvider Provider, Exception Error)> Failures) : base("the notifications could not be sent.")
        {
            this.Failures = Failures;
        }
        internal IReadOnlyList<(NotificationProvider Provider, Exception Error)> Failures { get; }
    }

    // The unattended monitoring and its providers (decision D67, business-rules §28): one definition for the start, the
    // checks and the history.
    // - The monitoring is outputTo: Email (email, plus ntfy when Ntfy.yaml enables it) or outputTo: Ntfy (ntfy only).
    // - Email is enabled by outputTo: Email; ntfy by Ntfy.yaml's enabled: true in either monitoring mode. An enabled
    //   provider must be usable: its settings and secret are checked at the start, which stops with one error and exit
    //   code 1 otherwise; with outputTo: Ntfy and ntfy not enabled the start stops too (no provider). A provider is never
    //   silently ignored, and nothing is sent to test it.
    // - One check, one result set: each search's stream (the same search, the same openings) goes to every enabled
    //   provider: email as before (one email per search, D57), ntfy for each search with an opening. A provider that fails
    //   is not tried again for the rest of that check; the other goes on. Openings are recorded in Camp.History once any
    //   enabled provider delivered them (the maintainer's decision of 2026-11-22): no duplicates and no resend storm; a
    //   provider that failed misses those openings; when every provider fails nothing is recorded and everything comes
    //   again (D36). After the check a failure ends the run with exit code 1, as a failed email always did.
    internal static class Notifications
    {
        internal static bool IsMonitoring(OutputType? OutputTo) => OutputTo == OutputType.Email || OutputTo == OutputType.Ntfy;

        internal static bool EmailEnabled(CampertronInternalConfig? Config) => Config?.GeneralConfig?.OutputTo == OutputType.Email;

        internal static bool NtfyEnabled(CampertronInternalConfig? Config) => IsMonitoring(Config?.GeneralConfig?.OutputTo) && Config?.NtfyConfig?.Enabled == true;

        // outputTo: Ntfy needs ntfy (Ntfy.yaml with enabled: true); outputTo: Email has email
        internal static bool NtfyRequired(CampertronInternalConfig? Config) => Config?.GeneralConfig?.OutputTo == OutputType.Ntfy;

        // the mode's name in its messages: "email mode" (outputTo: Email, with or without ntfy) or "ntfy mode"
        internal static string ModeName(CampertronInternalConfig? Config) => NtfyRequired(Config) ? "ntfy mode" : "email mode";

        // the start line's output: "Email", "Email and ntfy" or "Ntfy"
        internal static string OutputText(CampertronInternalConfig? Config) =>
            EmailEnabled(Config) ? (NtfyEnabled(Config) ? "Email and ntfy" : "Email") : "Ntfy";

        // publishes one ntfy message, or throws; replaceable so tests can record the messages and make a publish fail
        // without a network (the production publish is NtfyPublisher's)
        internal static Action<NtfyConfig, NtfyMessage> PublishNtfy { get; set; } = (Ntfy, Message) => NtfyPublisher.Publish(Ntfy, Message);

        // how many messages a check that has news sends: one email per search, one ntfy message per search with an opening
        internal static (int Emails, int NtfyMessages) Planned(CampertronInternalConfig Config, IEnumerable<ConsoleConfigItem> Searches)
        {
            List<ConsoleConfigItem> All = Searches.ToList();
            return (EmailEnabled(Config) ? All.Count : 0, NtfyEnabled(Config) ? All.Count(S => ConsoleResults.Openings(S) > 0) : 0);
        }

        // one check's notifications, in the searches' order. Record is called with a search's stream once any provider
        // delivered it. Throws after the check: one provider's failure as it is (a failed email as before), both as a
        // NotificationException
        internal static void Deliver(CampertronInternalConfig Config, GeneralConfig GenConfig, IEnumerable<ConsoleConfigItem> Ordered, ref ConfigType LastConfigType,
            Action<ConsoleConfigItem> Record)
        {
            bool Email = EmailEnabled(Config);
            bool Ntfy = NtfyEnabled(Config);
            Exception? EmailFailure = null;
            Exception? NtfyFailure = null;
            foreach (ConsoleConfigItem Search in Ordered)
            {
                bool Delivered = false;
                if (Email && EmailFailure == null)
                {
                    try
                    {
                        //sends this search's email and writes its "Email sent" line, or throws
                        CampsiteConfig.ProcessConsoleConfig(Search.Values ?? new List<ConsoleConfigValue>(), ref LastConfigType, GenConfig, Config.EmailConfig!, Config.CachePath);
                        Delivered = true;
                    }
                    catch (Exception ex)
                    {
                        EmailFailure = ex;
                    }
                }
                if (Ntfy && NtfyFailure == null)
                {
                    NtfyMessage? Message = NtfyMessages.Build(Search);
                    if (Message != null)
                    {
                        try
                        {
                            PublishNtfy(Config.NtfyConfig!, Message);
                            Delivered = true;
                            ConsoleUi.LoopLine("ntfy notification sent: " + ConsoleResults.SearchName(Search) + " (" + ConsoleUi.Count(Message.Openings, "opening", "openings") + ")");
                        }
                        catch (Exception ex)
                        {
                            NtfyFailure = ex;
                        }
                    }
                }
                if (Delivered)
                {
                    Record(Search);
                }
            }
            if (EmailFailure != null && NtfyFailure != null)
            {
                throw new NotificationException(new[] { (NotificationProvider.Email, EmailFailure), (NotificationProvider.Ntfy, NtfyFailure) });
            }
            if (EmailFailure != null)
            {
                ExceptionDispatchInfo.Capture(EmailFailure).Throw();
            }
            if (NtfyFailure != null)
            {
                ExceptionDispatchInfo.Capture(NtfyFailure).Throw();
            }
        }
    }
}
