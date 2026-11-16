using Avalonia.Controls;
using Avalonia.Threading;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.search;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Campertron
{
    // The Results tab's part of `CampertronGUI --smoke-test` (GS2, decision D46). On the platform the release runs on, it
    // shows the Results tab with the startup's folders (read only), then runs GS1's search service through the tab's own
    // view model and view in new temporary folders: a fixture search of a fixture campground, whose campsite details are
    // written there and whose months are answered by a fixture transport, so nothing reaches recreation.gov and no
    // database is needed. It checks a completed run whose typed results bind to the list, a run canceled during a stalled
    // request, and a blocked run (HTTP 429) that ends without ending the application and starts the five-minute wait. The
    // real tab gets its own view model back afterwards and the folders are removed. The result line names only what was
    // checked.
    internal static class RunProbe
    {
        internal const string CampgroundId = "9900001";
        internal const string FileName = "Probe run.yaml";
        internal static readonly string[] Campsites = { "990000101", "990000102" };

        internal const string SearchText =
            "# Campertron run probe\n" +
            "displayName: Probe run\n" +
            "autoRun: true\n" +
            "campgroundID: '" + CampgroundId + "'\n" +
            "totalHumans: 2\n" +
            "searchBy: DaysOut\n" +
            "searchValue: 10\n" +
            "searchValueDates: []\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n" +
            "includeEquipment: []\nexcludeEquipment: []\nincludeSites: []\nexcludeSites: []\nincludeAttributes: []\nexcludeAttributes: []\n" +
            "includeCampsiteType: []\nexcludeCampsiteType: []\n" +
            "consecutiveDays: 1\n";

        // How the fixture answers a month: every night available, a request that stalls until it is canceled, or HTTP 429.
        internal enum Answer { Available, Stall, RateLimited }

        // a fixture recreation.gov: answers in memory, never a network request
        internal sealed class FixtureGov
        {
            private int _Mode;
            internal readonly ManualResetEventSlim Stalled = new ManualResetEventSlim(false);
            private int _Requests;

            internal Answer Mode
            {
                get => (Answer)Volatile.Read(ref _Mode);
                set => Volatile.Write(ref _Mode, (int)value);
            }
            internal int Requests => Volatile.Read(ref _Requests);

            internal SearchResponse Send(SearchRequest Request, CancellationToken Token)
            {
                Interlocked.Increment(ref _Requests);
                switch (Mode)
                {
                    case Answer.Stall:
                        Stalled.Set();
                        Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                        Token.ThrowIfCancellationRequested();
                        return new SearchResponse(503, "");
                    case Answer.RateLimited:
                        return new SearchResponse(429, "Too Many Requests");
                    default:
                        return new SearchResponse(200, MonthJson(Request.Month, Campsites));
                }
            }
        }

        // the search file and the campground's campsite details (the cache's own files and format; the completion marker
        // last), in the given folders
        internal static void WriteFixture(string ConfigPath, string CachePath, string SearchFile = FileName, string Search = SearchText)
        {
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            File.WriteAllText(Path.Join(ConfigPath, SearchFile), Search, new UTF8Encoding(false));
            WriteCampground(CachePath, CampgroundId, "PROBE CAMPGROUND", "Campertron Probe Park", Campsites);
        }

        internal static void WriteCampground(string CachePath, string Id, string Name, string Park, IReadOnlyList<string> Sites, Func<string, string>? SiteName = null)
        {
            Write(CachePath, Id + "-CampInfo.json", new ReturnParkCampground { CampsiteName = Name, ParkName = Park });
            Write(CachePath, Id + "-Campsites.json", Sites.Select((Site, Index) => new CampsitesRecdata
            {
                CampsiteID = Site,
                FacilityID = Id,
                CampsiteName = SiteName?.Invoke(Site) ?? (Index + 1).ToString("000", CultureInfo.InvariantCulture),
                CampsiteType = "STANDARD NONELECTRIC",
                Loop = "A",
                TypeOfUse = "Overnight",
            }).ToList());
            Write(CachePath, Id + "-CampsitesByPark.json", Sites.ToList());
            foreach (string Site in Sites)
            {
                Write(CachePath, Site + "-Equipment.json", new List<string> { "Tent" });
                Write(CachePath, Site + "-Attributes.json", new List<AttributeValuePair>
                {
                    new AttributeValuePair { AttributeName = "Checkin Time", AttributeValue = "2:00 PM" },
                    new AttributeValuePair { AttributeName = "Checkout Time", AttributeValue = "12:00 PM" },
                });
            }
            Write(CachePath, Id + "-Cached.json", DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
        }

        private static void Write(string Folder, string Name, object Value)
        {
            File.WriteAllText(Path.Join(Folder, Name), JsonSerializer.Serialize(Value), new UTF8Encoding(false));
        }

        // a month as recreation.gov answers it: each campsite's status for every day of the month
        internal static string MonthJson(DateTime Month, IEnumerable<string> Sites, Func<string, int, bool>? Available = null)
        {
            int Days = DateTime.DaysInMonth(Month.Year, Month.Month);
            var Json = new StringBuilder("{\"campsites\":{");
            int Count = 0;
            foreach (string Site in Sites)
            {
                Json.Append(Count++ > 0 ? "," : "").Append('"').Append(Site).Append("\":{\"availabilities\":{");
                for (int Day = 1; Day <= Days; Day++)
                {
                    bool Open = Available?.Invoke(Site, Day) ?? true;
                    Json.Append(Day > 1 ? "," : "").Append('"').Append(new DateTime(Month.Year, Month.Month, Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                        .Append("T00:00:00Z\":\"").Append(Open ? "Available" : "Reserved").Append('"');
                }
                Json.Append("},\"campsite_id\":\"").Append(Site).Append("\",\"campsite_type\":\"STANDARD NONELECTRIC\",\"loop\":\"A\",\"max_num_people\":6,\"min_num_people\":1,\"site\":\"")
                    .Append(Site).Append("\"}");
            }
            return Json.Append("},\"count\":").Append(Count).Append('}').ToString();
        }

        // On the UI thread, after the Searches tab step of the smoke test. Never throws: a failure is the result.
        internal static async Task<(bool Passed, string Tab, string Detail)> RunAsync(MainWindow Window, MainWindowViewModel ViewModel)
        {
            string Tab = "no";
            string Step = "the Results tab";
            string Root = Path.Join(Path.GetTempPath(), "campertron-run-probe-" + Guid.NewGuid().ToString("N"));
            ResultsView? View = Window.FindControl<ResultsView>("ResultsContent");
            ResultsViewModel? Real = ViewModel.Results;
            ResultsViewModel? Probe = null;
            try
            {
                // the real tab, with the folders the startup prepared: its view, its bindings and its list (read only)
                TabControl? Tabs = Window.FindControl<TabControl>("MainTabControl");
                if (Tabs == null || View == null || Real == null)
                {
                    return (false, Tab, "fail the Results tab is missing");
                }
                Tabs.SelectedIndex = MainWindow.ResultsTabIndex;
                Window.UpdateLayout();
                Button? Start = View.FindControl<Button>("StartButton");
                Button? Cancel = View.FindControl<Button>("CancelButton");
                bool Shown = View.IsEffectivelyVisible && Real.IsLoaded;
                bool Paths = Real.ConfigPath == ViewModel.ConfigPath && Real.CachePath != null && Real.CachePath == ViewModel.CachePath && Real.PathsAvailable;
                // Cancel is bound and disabled while nothing runs (a command that can't run disables its button)
                bool Bound = ReferenceEquals(Start?.Command, Real.StartCommand) && ReferenceEquals(Cancel?.Command, Real.CancelCommand) && Cancel?.IsEffectivelyEnabled == false;
                if (!Shown || !Paths || !Bound)
                {
                    return (false, Tab, "fail the Results tab was not shown" + (!Shown ? "" : !Paths ? " with the startup's folders" : " with its Start and Cancel buttons bound"));
                }
                Tab = "yes files=" + Real.Searches.Count.ToString(CultureInfo.InvariantCulture) + " paths=startup start=bound cancel=bound";

                // the run, through the tab's own view model and view, in temporary folders
                Step = "the fixture";
                string Config = Path.Join(Root, "CampertronConfig");
                string CachePath = Path.Join(Root, "CampertronCache");
                WriteFixture(Config, CachePath);
                var Gov = new FixtureGov();
                Probe = new ResultsViewModel(Config, CachePath, new CampgroundCatalog(() => new List<CampgroundPickerEntry>()), _ => false,
                    new ResultsSeams { Options = new SearchHostOptions { Transport = Gov.Send, RetryDelay = TimeSpan.FromMilliseconds(50), HeavyTrafficDelay = TimeSpan.FromMilliseconds(10) } });
                View.DataContext = Probe;
                Probe.Show();
                Window.UpdateLayout();
                if (Probe.Searches.Count != 1 || !Probe.Searches[0].IsSelected || !Probe.CanStart)
                {
                    return (false, Tab, "fail the fixture search was not listed and chosen");
                }

                Step = "a completed run";
                Probe.StartCommand.Execute(null);
                if (!await Idle(Probe, TimeSpan.FromSeconds(20)) || Probe.LastResult is not SearchRunResult Done || Done.Status != SearchRunStatus.Completed)
                {
                    return (false, Tab, "fail the fixture run did not complete");
                }
                SearchResult Search = Done.Searches.Single();
                int Openings = Search.Openings.Count;
                int Nights = Search.Openings.Select(O => O.Night).Distinct().Count();
                Window.UpdateLayout();
                ListBox? Rows = View.FindControl<ListBox>("ResultRowsList");
                if (Openings == 0 || Openings != Nights * Campsites.Length || Search.Sites.Count != Campsites.Length || Rows == null
                    || Rows.ItemCount != Openings + Nights || Rows.ContainerFromIndex(1)?.DataContext is not ResultOpeningRow)
                {
                    return (false, Tab, "fail the fixture run's typed results were not complete or not bound");
                }

                Step = "a canceled run";
                Gov.Mode = Answer.Stall;
                Probe.StartCommand.Execute(null);
                if (!await Task.Run(() => Gov.Stalled.Wait(TimeSpan.FromSeconds(20))))
                {
                    return (false, Tab, "fail the stalled request never started");
                }
                var Clock = Stopwatch.StartNew();
                Probe.CancelCommand.Execute(null);
                Probe.CancelCommand.Execute(null);
                if (!await Idle(Probe, TimeSpan.FromSeconds(10)) || Probe.RunStatus != SearchRunStatus.Canceled || Clock.Elapsed > TimeSpan.FromSeconds(5))
                {
                    return (false, Tab, "fail the canceled run did not end promptly");
                }

                Step = "a blocked run";
                Gov.Mode = Answer.RateLimited;
                Probe.StartCommand.Execute(null);
                if (!await Idle(Probe, TimeSpan.FromSeconds(20)) || Probe.RunStatus != SearchRunStatus.Blocked)
                {
                    return (false, Tab, "fail the rate-limited run did not end blocked");
                }
                if (!Window.IsVisible || Probe.CanStart || !Probe.IsCoolingDown || !Probe.HasCooldown)
                {
                    return (false, Tab, "fail the blocked run did not keep the window open with Start waiting");
                }
                return (true, Tab, "pass planned=1 completed=openings:" + Openings.ToString(CultureInfo.InvariantCulture)
                    + " results=bound canceled=prompt blocked=no-exit cooldown=on network=none");
            }
            catch (Exception ex)
            {
                return (false, Tab, "fail " + Step + " (" + ex.GetType().Name + ")");
            }
            finally
            {
                if (View != null)
                {
                    View.DataContext = Real;
                }
                Probe?.Detach();
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // a temporary folder; the system removes it later
                }
            }
        }

        // completes when the view model's run has ended (its result shown), or false after the timeout
        private static async Task<bool> Idle(ResultsViewModel ViewModel, TimeSpan Timeout)
        {
            if (!ViewModel.IsRunning)
            {
                return true;
            }
            var Ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? Sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ResultsViewModel.IsRunning) && !ViewModel.IsRunning)
                {
                    Ended.TrySetResult(true);
                }
            }
            ViewModel.PropertyChanged += Changed;
            try
            {
                Task Finished = await Task.WhenAny(Ended.Task, Task.Delay(Timeout));
                return Finished == Ended.Task;
            }
            finally
            {
                ViewModel.PropertyChanged -= Changed;
            }
        }
    }
}
