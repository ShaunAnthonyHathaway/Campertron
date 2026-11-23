using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.WebAccess
{
    // what a save of Web.yaml did
    internal enum WebAccessSaveOutcome
    {
        // the file was saved
        Saved,
        // nothing was written: the change leaves the web UI open to the network without a password, and the user has not
        // confirmed that yet. The same save with the confirmation goes through
        NeedsConfirmation,
        // nothing was written: the editing session's own reason (nothing to save, an input error, a conflict)
        NotSaved
    }

    internal sealed class WebAccessSaveResult
    {
        internal WebAccessSaveResult(WebAccessSaveOutcome Outcome, string Message)
        {
            this.Outcome = Outcome;
            this.Message = Message;
        }
        internal WebAccessSaveOutcome Outcome { get; }
        // the warning to confirm, or the editing session's message
        internal string Message { get; }
    }

    // The one warning of the web UI's access settings (decision D81 point 4, D92; business-rules-settings §31): a save
    // that leaves the password off while the web UI listens on the network asks once, at that save, for a confirmation.
    //  - "Open" is the password off and the network on. The network is on when Web.yaml's networkListening is true, or,
    //    whatever the file says, when this process was started with a --listen address that is not this computer's own
    //    (ListensOnNetwork): the file's setting is not what decides for that process.
    //  - The confirmation is asked only for a save that makes the settings open: the saved file is not open and the
    //    changed one is. Once such a save went through, the saved file is open, so no later save asks again, and nothing
    //    else reminds: no lasting notice, no refusal to start (D81).
    //  - Nothing else is refused here. The state of the password file and of the keys folder never keeps a save from
    //    going through (they are reported, never a condition); only the editing session's own errors do, as for every
    //    settings file.
    internal static class WebAccessRule
    {
        internal const string Warning = "Without a password, everyone who can reach this computer on the network can open Campertron's web UI and change its settings, searches and notifications. Turn the password off only on a network you trust, or let the web UI listen on this computer only.";
        internal const string Confirmation = "Save without a password, reachable from the network";

        // whether settings leave the web UI open: no password, and reachable from the network
        internal static bool IsOpen(bool PasswordProtection, bool NetworkListening, bool? ListensOnNetwork = null) =>
            !PasswordProtection && (ListensOnNetwork ?? NetworkListening);

        internal static bool IsOpen(WebConfig? Settings, bool? ListensOnNetwork = null)
        {
            WebConfig Used = Settings ?? new WebConfig();
            return IsOpen(Used.PasswordProtection, Used.NetworkListening, ListensOnNetwork);
        }

        // whether a save from Saved (what the file holds; null: no file, the defaults) to Changed must be confirmed first
        internal static bool NeedsConfirmation(WebConfig? Saved, WebConfig Changed, bool? ListensOnNetwork = null) =>
            IsOpen(Changed, ListensOnNetwork) && !IsOpen(Saved, ListensOnNetwork);

        // the same for an editing session of Web.yaml: the file as it was read against the settings as edited
        internal static bool NeedsConfirmation(ConfigEditSession<WebConfig> Session, bool? ListensOnNetwork = null)
        {
            bool Was = IsOpen(Session.OriginalValue(PasswordKey) as bool? ?? true, Session.OriginalValue(NetworkKey) as bool? ?? true, ListensOnNetwork);
            bool Becomes = IsOpen(Session.Value(PasswordKey) as bool? ?? true, Session.Value(NetworkKey) as bool? ?? true, ListensOnNetwork);
            return Becomes && !Was;
        }

        internal const string PasswordKey = "passwordProtection";
        internal const string NetworkKey = "networkListening";

        // Saves an editing session of Web.yaml. A save that needs the confirmation and does not have it writes nothing and
        // returns the warning; with it, or when none is needed, the session saves as every settings file is saved
        // (ConfigEditSession.Save: the validation, the safe writer, a new file created owner-only)
        internal static WebAccessSaveResult Save(ConfigEditSession<WebConfig> Session, bool Confirmed, bool? ListensOnNetwork = null)
        {
            if (Session.CanSave && !Confirmed && NeedsConfirmation(Session, ListensOnNetwork))
            {
                return new WebAccessSaveResult(WebAccessSaveOutcome.NeedsConfirmation, Warning);
            }
            ConfigEditResult Result = Session.Save();
            return new WebAccessSaveResult(Result.Succeeded ? WebAccessSaveOutcome.Saved : WebAccessSaveOutcome.NotSaved, Result.Message);
        }
    }
}
