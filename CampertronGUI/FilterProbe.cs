using Campertron.ViewModels;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronLibrary.function.RecDotOrg.sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Campertron
{
    // The filter selectors' part of `CampertronGUI --smoke-test` (decision D51): on the platform the release runs on, the
    // installed campground data gives a campground's equipment, campsite types, campsite names and attributes through the
    // selectors' own source, read on a worker thread and handed back on the UI thread; the start built Campertron's
    // attribute index; and a selector checks a value into its list. It reads the database only (never a settings file),
    // writes nothing and never searches availability. The result line names only what was checked.
    internal static class FilterProbe
    {
        private const int MaxCampgrounds = 50;

        internal static async Task<(bool Passed, string Detail)> RunAsync(string ConfigPath, IReadOnlyList<string> CampgroundIds)
        {
            int UiThread = Environment.CurrentManagedThreadId;
            int LoadThread = UiThread;
            var Source = new FilterChoiceSource(Id =>
            {
                LoadThread = Environment.CurrentManagedThreadId;
                return Read.FilterChoicesByCampground(Id, ConfigPath);
            }, () => FilterChoiceSource.DatabaseFingerprint(ConfigPath), CapturedPost(), Job => Task.Run(Job));
            try
            {
                // the first campground of the picker's list that has campsites in the data
                foreach (string Id in CampgroundIds.Take(MaxCampgrounds))
                {
                    var Answer = new TaskCompletionSource<(CampgroundFilterChoices?, string?)>();
                    Source.Request(Id, (Choices, Problem) => Answer.TrySetResult((Choices, Problem)));
                    (CampgroundFilterChoices? Found, string? Problem) = await Answer.Task.WaitAsync(TimeSpan.FromSeconds(60));
                    if (Found == null)
                    {
                        return (false, "fail the campground data could not be read: " + Problem);
                    }
                    if (Found.Campsites == 0)
                    {
                        continue;
                    }
                    bool Indexed = RidbIndexes.Exists(Path.Join(ConfigPath, "RecreationDotOrg.db"));
                    if (Found.Equipment.Count == 0 || Found.CampsiteTypes.Count == 0 || Found.CampsiteNames.Count == 0)
                    {
                        return (false, "fail campground " + Id + " has campsites but no equipment, types or names");
                    }
                    // a selector of the search editor checks a value into its list (one item, exactly the value)
                    var Selector = new FilterSelectorField("includeEquipment", "Include equipment", "", FilterField.Equipment, true, "equipment type", "equipment types");
                    Selector.FillRows(new List<ConfigListItem>());
                    Selector.SetLoaded(Found.Equipment);
                    Selector.Toggle(Selector.Options.First(O => O.IsChoice));
                    List<(int? OriginalIndex, string? Value)> Rows = Selector.Values().ToList();
                    bool Toggled = Rows.Count == 1 && Rows[0].OriginalIndex == null && Rows[0].Value == Found.Equipment[0].Value;
                    string Detail = "campground=" + Id
                        + " campsites=" + Found.Campsites.ToString(CultureInfo.InvariantCulture)
                        + " equipment=" + Found.Equipment.Count.ToString(CultureInfo.InvariantCulture)
                        + " types=" + Found.CampsiteTypes.Count.ToString(CultureInfo.InvariantCulture)
                        + " names=" + Found.CampsiteNames.Count.ToString(CultureInfo.InvariantCulture)
                        + " attributes=" + Found.Attributes.Count.ToString(CultureInfo.InvariantCulture)
                        + " thread=" + (LoadThread != UiThread ? "worker" : "ui")
                        + " index=" + (Indexed ? "yes" : "no")
                        + " selector=" + (Toggled ? "toggled" : "unchanged");
                    bool Passed = LoadThread != UiThread && Indexed && Toggled;
                    return (Passed, (Passed ? "pass " : "fail ") + Detail);
                }
                return (false, "fail no campground of the first " + MaxCampgrounds.ToString(CultureInfo.InvariantCulture) + " has campsites");
            }
            catch (Exception ex) when (ex is TimeoutException || ex is InvalidOperationException || ex is IOException || ex is System.Data.Common.DbException)
            {
                return (false, "fail " + ex.GetType().Name);
            }
        }

        private static Action<Action> CapturedPost()
        {
            Avalonia.Threading.Dispatcher Ui = Avalonia.Threading.Dispatcher.UIThread;
            return Job => Ui.Post(Job, Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
