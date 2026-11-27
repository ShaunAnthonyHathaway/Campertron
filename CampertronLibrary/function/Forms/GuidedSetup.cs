using CampertronLibrary.function.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // The guided setup (v0.3.0's PR 10, D80 point 4, D100; business-rules-settings §37): a short path for the
    // notification-only user (D79), who wants Campertron to check a few searches and say when a campsite opens up, by
    // email or ntfy. Its four steps are the order the monitoring's own checks impose (D71, D73): the notification file
    // first, then an enabled search, then outputTo in General.yaml, which the General form saves only once both pass.
    //
    // Nothing of the guided setup is kept anywhere: no file, no flag, no record of progress. Its state is read from the
    // saved settings files each time, by the very checks the monitoring's start makes (ConfigValidation.MonitoringStartErrors,
    // MonitoringSearchErrors and the secret files' metadata, D75), so it is never "complete" while the monitoring's start
    // would refuse the files, and a user who set things up elsewhere (the settings or searches pages, CampertronGUI, a text
    // editor) is where the files say. Nothing here creates, writes or deletes a file, reads a secret (the password and token
    // files are judged from their metadata only, D75; no environment variable is read), sends anything or searches.
    // No UI toolkit and no static state that changes (ER-37); the web UI shows it (the guided setup is web-only in
    // v0.3.0: CampertronGUI's is later work, D79 point 6). Since v0.4.0's phase 3 (D117) its steps' orchestration is the
    // layer's too (GuidedSetupFlow), which reads it again after each save.

    // how the user is notified: outputTo: Email (email, and ntfy too when Ntfy.yaml enables it) or outputTo: Ntfy
    public enum GuidedSetupMethod
    {
        Email,
        Ntfy,
    }

    // the guided setup's steps, in their order (Done is the result, not a step)
    public enum GuidedSetupStep
    {
        Method,
        Notifications,
        Search,
        TurnOn,
        Done,
    }

    // what the saved files say, from nothing to everything:
    //  - Unconfigured: nothing of the guided setup is saved: General.yaml's outputTo is not Email or Ntfy (or there is no
    //    readable General.yaml), there is no search file, and neither Email.yaml nor Ntfy.yaml would pass the monitoring's
    //    start for its method;
    //  - Partial: something is saved, but not yet everything the method needs;
    //  - Ready: the notification file and at least one enabled search pass every check the monitoring's start makes on the
    //    files for this method, but outputTo is not yet the method (the last step);
    //  - Complete: Ready, and General.yaml's outputTo is the method: web mode starts the monitoring by itself when it starts.
    public enum GuidedSetupState
    {
        Unconfigured,
        Partial,
        Ready,
        Complete,
    }

    // One reading of a settings folder for the guided setup. Immutable: a page reads it again for every request
    public sealed class GuidedSetupStatus
    {
        private readonly Dictionary<GuidedSetupMethod, List<ConfigDiagnostic>> _Start;
        private readonly Dictionary<GuidedSetupMethod, List<(string SettingsFile, string Text, bool IsProblem)>> _Secrets;

        private GuidedSetupStatus(OutputType? SavedOutput, bool GeneralExists, int SearchFileCount, List<string> SearchProblems,
            Dictionary<GuidedSetupMethod, List<ConfigDiagnostic>> Start, Dictionary<GuidedSetupMethod, List<(string, string, bool)>> Secrets)
        {
            this.SavedOutput = SavedOutput;
            this.GeneralExists = GeneralExists;
            this.SearchFileCount = SearchFileCount;
            this.SearchProblems = SearchProblems;
            _Start = Start;
            _Secrets = Secrets;
        }

        // Reads the settings folder as the monitoring's start would judge it, for both methods. Reads files only through the
        // bounded loader; creates nothing; never opens a secret file or reads an environment variable
        public static GuidedSetupStatus Read(string ConfigPath, DateTime Now) => Read(ConfigPath, Now, null);

        // the same, its search problems in the words of the host's place for the search files (the console's Searches menu, PT1)
        internal static GuidedSetupStatus Read(string ConfigPath, DateTime Now, SearchPlace? Place)
        {
            string GeneralPath = Path.Join(ConfigPath, ConfigFiles.GeneralFileName);
            bool GeneralExists = File.Exists(GeneralPath);
            OutputType? Saved = null;
            if (GeneralExists)
            {
                List<ConfigDiagnostic> Found = ConfigValidation.ValidateFile(GeneralPath, Now, null, out object? Settings);
                //the outputTo a start would use: a General.yaml the start refuses is not set up
                if (Settings is GeneralConfig General && Found.All(D => D.Severity != ConfigSeverity.Error))
                {
                    Saved = General.OutputTo;
                }
            }
            var Start = new Dictionary<GuidedSetupMethod, List<ConfigDiagnostic>>();
            var Secrets = new Dictionary<GuidedSetupMethod, List<(string, string, bool)>>();
            foreach (GuidedSetupMethod Method in new[] { GuidedSetupMethod.Email, GuidedSetupMethod.Ntfy })
            {
                OutputType Output = OutputOf(Method);
                Start[Method] = ConfigValidation.MonitoringStartErrors(ConfigPath, Output, Now);
                Secrets[Method] = ConfigValidation.MonitoringSecretFiles(ConfigPath, Output, Now)
                    .Select(Secret => (Secret, Said: SecretFileCheck.Describe(Secret.Key, Secret.FilePath, SecretFileCheck.Inspect(Secret.FilePath), Secret.Kind, true)))
                    .Where(One => One.Said != null)
                    .Select(One => (One.Secret.SettingsFile, One.Secret.SettingsFile + ": " + One.Said!.Value.Message, One.Said!.Value.IsProblem))
                    .ToList();
            }
            //the search files as the monitoring's start reads them (D64's gate, D73); the same for both methods
            List<string> SearchProblems = ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Email, null, Place).Select(D => D.Message).ToList();
            return new GuidedSetupStatus(Saved, GeneralExists, ConfigFiles.SearchFiles(ConfigPath).Count, SearchProblems, Start, Secrets);
        }

        public static OutputType OutputOf(GuidedSetupMethod Method) => Method == GuidedSetupMethod.Email ? OutputType.Email : OutputType.Ntfy;

        // the settings file of a method's notifications
        public static string FileOf(GuidedSetupMethod Method) => Method == GuidedSetupMethod.Email ? ConfigFiles.EmailFileName : ConfigFiles.NtfyFileName;

        // General.yaml's outputTo as saved (null without a General.yaml the start would use)
        public OutputType? SavedOutput { get; }
        public bool GeneralExists { get; }
        // how many search files the settings folder has (the console's own, as every UI lists them)
        public int SearchFileCount { get; }
        // why the monitoring's start would find no search to check, or a search file it stops on (D64, D73), in the
        // validation's words; empty when at least one enabled search can be checked
        public IReadOnlyList<string> SearchProblems { get; }

        // what the monitoring's start would refuse in Email.yaml and Ntfy.yaml for this method (D71), "File: message"
        public IReadOnlyList<string> StartProblems(GuidedSetupMethod Method) => _Start[Method].Select(D => D.FileName + ": " + D.Message).Distinct().ToList();

        // the secret files this method's providers would read that the monitoring's start would refuse (missing, empty, not
        // readable: D75's words), and those that only others can read too (a warning: the start uses them)
        public IReadOnlyList<string> SecretFileProblems(GuidedSetupMethod Method) => _Secrets[Method].Where(S => S.IsProblem).Select(S => S.Text).ToList();
        public IReadOnlyList<string> SecretFileWarnings(GuidedSetupMethod Method) => _Secrets[Method].Where(S => !S.IsProblem).Select(S => S.Text).ToList();

        // what keeps the notification step from going on: what the monitoring's start would refuse in the method's own
        // file, then its secret file's problems (v0.4.0's phase 3 moved this from the web UI's page, unchanged)
        public IReadOnlyList<string> NotificationProblems(GuidedSetupMethod Method)
        {
            string Own = FileOf(Method);
            return StartProblems(Method).Where(P => P.StartsWith(Own + ":", StringComparison.Ordinal))
                .Concat(SecretFileProblems(Method).Where(P => P.StartsWith(Own + ":", StringComparison.Ordinal))).ToList();
        }

        // everything the monitoring's start would refuse in the saved files for this method, as the review lists it: the
        // notification files, the secret files, then the search files (v0.4.0's phase 3 moved this from the web UI's page)
        public IReadOnlyList<string> Problems(GuidedSetupMethod Method) => StartProblems(Method).Concat(SecretFileProblems(Method)).Concat(SearchProblems).ToList();

        // the method's own notification file passes the monitoring's start: Email.yaml for email, Ntfy.yaml (enabled) for
        // ntfy, with its secret file usable when it names one. A missing file is the start's own error for its method
        // (MonitoringStartErrors: Email.yaml for outputTo: Email, Ntfy.yaml for outputTo: Ntfy)
        public bool NotificationsReady(GuidedSetupMethod Method)
        {
            string Own = FileOf(Method);
            return !_Start[Method].Any(D => string.Equals(D.FileName, Own, StringComparison.Ordinal))
                && !_Secrets[Method].Any(S => S.IsProblem && string.Equals(S.SettingsFile, Own, StringComparison.Ordinal));
        }

        // at least one enabled search the monitoring's start would check, and no search file it would stop on
        public bool SearchReady => SearchProblems.Count == 0;

        // every check of the monitoring's start on the files passes for this method (its secrets' sources are read only when
        // it starts: a file of them is judged here from its metadata)
        public bool FilesReady(GuidedSetupMethod Method) => _Start[Method].Count == 0 && SecretFileProblems(Method).Count == 0 && SearchReady;

        // General.yaml's saved outputTo is this method
        public bool IsOn(GuidedSetupMethod Method) => SavedOutput == OutputOf(Method);

        public bool IsUnconfigured => SavedOutput is not (OutputType.Email or OutputType.Ntfy) && SearchFileCount == 0
            && !NotificationsReady(GuidedSetupMethod.Email) && !NotificationsReady(GuidedSetupMethod.Ntfy);

        public GuidedSetupState State(GuidedSetupMethod Method) =>
            FilesReady(Method) ? (IsOn(Method) ? GuidedSetupState.Complete : GuidedSetupState.Ready)
            : IsUnconfigured ? GuidedSetupState.Unconfigured : GuidedSetupState.Partial;

        // the method the saved files point to: outputTo's, else the one whose notification file alone is ready; null when
        // they don't say (the user chooses)
        public GuidedSetupMethod? SuggestedMethod => SavedOutput switch
        {
            OutputType.Email => GuidedSetupMethod.Email,
            OutputType.Ntfy => GuidedSetupMethod.Ntfy,
            _ => NotificationsReady(GuidedSetupMethod.Ntfy) == NotificationsReady(GuidedSetupMethod.Email) ? null
                : NotificationsReady(GuidedSetupMethod.Ntfy) ? GuidedSetupMethod.Ntfy : GuidedSetupMethod.Email,
        };

        // the state the start page says: of the suggested method, else Unconfigured or Partial
        public GuidedSetupState Overall => SuggestedMethod is GuidedSetupMethod Method ? State(Method)
            : IsUnconfigured ? GuidedSetupState.Unconfigured : GuidedSetupState.Partial;

        // whether a step is done for this method, from the saved files only
        public bool IsDone(GuidedSetupMethod Method, GuidedSetupStep Step) => Step switch
        {
            GuidedSetupStep.Method => true,
            GuidedSetupStep.Notifications => NotificationsReady(Method),
            GuidedSetupStep.Search => SearchReady,
            GuidedSetupStep.TurnOn => State(Method) == GuidedSetupState.Complete,
            _ => State(Method) == GuidedSetupState.Complete,
        };

        // the first step that is not done (Done when every step is)
        public GuidedSetupStep NextStep(GuidedSetupMethod Method) =>
            !IsDone(Method, GuidedSetupStep.Notifications) ? GuidedSetupStep.Notifications
            : !IsDone(Method, GuidedSetupStep.Search) ? GuidedSetupStep.Search
            : !IsDone(Method, GuidedSetupStep.TurnOn) ? GuidedSetupStep.TurnOn
            : GuidedSetupStep.Done;
    }
}
