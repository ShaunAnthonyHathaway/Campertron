using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // The monitoring page's automatic progress (D109; WebLive, business-rules-settings §35 and §39). While the web UI's
    // monitoring goes on (it starts, runs its checks, stops, restarts, pauses for the campground data's refresh, or its end
    // is being recorded), an open monitoring page shows it as it goes: the web UI's own script asks "?handler=progress"
    // (a GET that only looks) and puts the status line, the last check's sending and the five minutes' countdown in place;
    // when a part of the page changes (a check began or finished, a start, a stop, an end), it takes the page again and
    // puts only the changed parts in place, so a page whose Stop has the focus keeps it while the checks go on. Once the
    // monitoring is no longer running and does not start again by itself, the page shows how it ended and asks nothing
    // more. The monitoring is the web UI's, never a session's (D98): every signed-in session sees it, as every one sees the
    // page. Its states come round again (running, stopping, starting), so its answers have no rank: their number alone
    // keeps an older answer from replacing a newer one.
    internal static class WebMonitoringLive
    {
        // the parts of the page (their ids), each with a name of what it shows (data-part): the script replaces a part only
        // when its name changed (or when it holds #live)
        internal const string StatusPart = "status-part";
        internal const string ActionsPart = "actions-part";
        internal const string CheckPart = "check-part";

        // the monitoring goes on, or starts again by itself: an action or its end under way, a running monitoring, or a
        // pause for the refresh of a monitoring that ran. Not once web mode stops
        internal static bool Active(MonitoringControlView V) =>
            !V.ShuttingDown && (V.Busy != MonitoringControlBusy.None || V.Status.IsRunning || (V.Paused && V.Resumes));

        // its state as the answers name it: an announcement follows a change of it (a start that started, a stop asked for)
        internal static string State(MonitoringControlView V) =>
            !Active(V) ? WebLiveAnswer.EndedState
            : V.Busy != MonitoringControlBusy.None ? V.Busy.ToString().ToLowerInvariant()
            : V.Status.IsRunning ? (V.Status.StopRequested ? "stopping" : "running")
            : "paused";

        // the page's status part: everything it shows but the status line and the countdown (fields)
        internal static string StatusSignature(MonitoringControlView V)
        {
            MonitoringStatus S = V.Status;
            MonitoringControlStart? Start = V.ThisStart;
            MonitoringControlEnd? End = V.ThisEnd;
            return WebLive.Signature("status", State(V), V.Generation, StartedOnce(V), S.StartedAt, Start?.Generation, Start?.Started, Start?.WithHost, Start?.AfterRefresh,
                Start?.Output, Start?.At, S.Searches, S.CheckNumber, S.LastCheckAt, S.NextCheckAt, S.IsRunning && S.SearchFilesChangedSinceStart, Failed(V), End?.Generation,
                End?.At, NotStarted(V), V.Cooldown != null);
        }

        // the page's Start, Stop and Restart: the moment they post, which of them are available and why the others are not
        internal static string ActionsSignature(MonitoringControlView V) =>
            WebLive.Signature("actions", V.Token, V.CanStart, V.CanStop, V.CanRestart, WebMonitoringWording.Unavailable(V));

        // the page's last check: which check it is (its sending and the end's "so far" are fields)
        internal static string CheckSignature(MonitoringControlView V) => WebLive.Signature("check", V.LastCheck?.Generation, V.LastCheck?.Number, V.LastCheck?.Searches.Count);

        internal static string View(MonitoringControlView V) => WebLive.Signature("monitoring", StatusSignature(V), ActionsSignature(V), CheckSignature(V));

        // the monitoring's state in the page's words, by the page's element ids
        internal static Dictionary<string, string> Fields(MonitoringControlView V)
        {
            var Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = WebMonitoringWording.StateText(V) };
            if (V.LastCheck is MonitoringControlCheck Check)
            {
                Fields["check-summary"] = MonitoringWording.CheckSummary(Check, !V.Status.IsRunning && V.Busy == MonitoringControlBusy.None);
            }
            if (V.Cooldown is string Cooldown)
            {
                Fields["cooldown"] = Cooldown;
            }
            return Fields;
        }

        // What an open monitoring page asks while the monitoring goes on (Asked: the key it names, this start of the web
        // UI's). It only looks: one immutable view of the monitoring, taken as a GET takes it; nothing is started, stopped,
        // read or written. A page of an earlier start of the web UI, or a key that is none, is told so and shown nothing.
        // Seq: the session's answer number (its workspace's)
        internal static WebLiveAnswer Observe(MonitoringControlView V, string Instance, string? Asked, long Seq)
        {
            if (!string.Equals(Asked, Instance, StringComparison.Ordinal))
            {
                return WebLiveAnswer.Replaced(Seq);
            }
            if (!Active(V))
            {
                return WebLiveAnswer.Ended(Instance, Seq, 0);
            }
            return new WebLiveAnswer(WebLiveAnswer.FormatVersion, State(V), Instance, Seq, 0, View(V), Fields(V));
        }

        // what the page shows, as the page decides it (Monitoring.cshtml)
        // the shared form layer's (v0.4.0's phase 4: the desktop GUI's Monitoring tab shows the same parts)
        internal static bool StartedOnce(MonitoringControlView V) => V.ShowsFacts;
        internal static bool NotStarted(MonitoringControlView V) => V.ShowsStartRefused;
        internal static bool Failed(MonitoringControlView V) => V.ShowsFailure;
    }
}
