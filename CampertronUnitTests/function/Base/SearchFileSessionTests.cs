using System.Reflection;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The editing session on a search file (CM6, decision D44; business-rules §18): every setting of CampertronConfig
    // typed, the list edits with their comments, empty (null) lists and items and missing keys kept, the validation at its
    // fields and items, the campground check by exact FacilityID, the verified save, external changes and conflicts, and
    // nothing that searches. Each test has its own temporary settings folder (ConfigEditTestBase); nothing uses
    // LocalApplicationData, the network or the embedded database.
    public class SearchFileSessionTests : ConfigEditTestBase
    {
        internal const string SearchText =
            "# Yosemite search\n" +
            "displayName: Upper Pines   # shown in the results\n" +
            "autoRun: false\n" +
            "campgroundID: '232447'\n" +
            "totalHumans: 2\n" +
            "searchBy: StartEndDate\n" +
            "searchValue: 0\n" +
            "searchValueDates:\n" +
            "  # the trip\n" +
            "  - 2027-07-02 # start\n" +
            "  - 2027-07-05\n" +
            "showMonday: true\n" +
            "showTuesday: true\n" +
            "showWednesday: true\n" +
            "showThursday: true\n" +
            "showFriday: true\n" +
            "showSaturday: false\n" +
            "showSunday: false\n" +
            "includeEquipment:\n" +
            "  - Tent # small\n" +
            "  - RV\n" +
            "excludeEquipment: []\n" +
            "includeSites:\n" +
            "excludeSites: [A001, 'B 002']\n" +
            "includeAttributes:\n" +
            "  - Shade:Yes\n" +
            "excludeAttributes: []\n" +
            "includeCampsiteType: []\n" +
            "consecutiveDays: 1\n" +
            "# end\n";

        internal static readonly string[] SearchKeys =
        {
            "displayName", "autoRun", "campgroundID", "totalHumans", "searchBy", "searchValue", "searchValueDates",
            "showMonday", "showTuesday", "showWednesday", "showThursday", "showFriday", "showSaturday", "showSunday", "filterMatching",
            "includeEquipment", "excludeEquipment", "includeSites", "excludeSites", "includeAttributes", "excludeAttributes",
            "includeCampsiteType", "excludeCampsiteType", "consecutiveDays"
        };

        // the campground data the tests look campgrounds up in (exact FacilityID)
        internal static readonly Dictionary<string, CampgroundChoice> Campgrounds = new(StringComparer.Ordinal)
        {
            ["232447"] = new CampgroundChoice("232447", "UPPER PINES", "YOSEMITE VALLEY", "CA"),
            ["232445"] = new CampgroundChoice("232445", "WATCHMAN CAMPGROUND", "SPRINGDALE", "UT"),
            ["0990010"] = new CampgroundChoice("0990010", "LEADING ZERO CAMP", null, null),
        };

        private string Search => Path.Join(Folder, "Yosemite.yaml");

        private protected ConfigEditSession<CampertronConfig> OpenSearch(string Name = "Yosemite.yaml", bool Lookup = true) =>
            new ConfigEditSession<CampertronConfig>(Folder, Name, () => Now, Lookup ? Find : null);

        private static CampgroundChoice? Find(string Id) => Campgrounds.TryGetValue(Id, out CampgroundChoice? Choice) ? Choice : null;

        private static string Replace(string Text, string Old, string New)
        {
            Assert.That(Text, Does.Contain(Old));
            return Text.Replace(Old, New);
        }

        // 7: every setting of a search, typed, as the loader reads it
        [Test]
        public void ASearchFile_OpensWithEverySetting()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();

            Assert.That(Session.Kind, Is.EqualTo(ConfigFileKind.Search));
            Assert.That(Session.FileName, Is.EqualTo("Yosemite.yaml"));
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));
            Assert.That(Session.Keys, Is.EqualTo(SearchKeys));
            Assert.That(Session.Value("displayName"), Is.EqualTo("Upper Pines"));
            Assert.That(Session.Value("autoRun"), Is.EqualTo(false));
            Assert.That(Session.Value("campgroundID"), Is.EqualTo("232447"));
            Assert.That(Session.Value("totalHumans"), Is.EqualTo(2));
            Assert.That(Session.Value("searchBy"), Is.EqualTo(SearchTypes.StartEndDate));
            Assert.That(Session.Value("searchValue"), Is.EqualTo(0));
            Assert.That(Session.Value("searchValueDates"), Is.EqualTo(new[] { "2027-07-02", "2027-07-05" }));
            Assert.That(Session.Value("showFriday"), Is.EqualTo(true));
            Assert.That(Session.Value("showSunday"), Is.EqualTo(false));
            Assert.That(Session.Items("includeEquipment").Select(R => (R.OriginalIndex, R.Value)), Is.EqualTo(new (int?, string?)[] { (0, "Tent"), (1, "RV") }));
            Assert.That(Session.Value("excludeEquipment"), Is.EqualTo(Array.Empty<string>()));
            Assert.That(Session.Value("includeSites"), Is.Null, "an empty value is a null list");
            Assert.That(Session.Value("excludeSites"), Is.EqualTo(new[] { "A001", "B 002" }));
            Assert.That(Session.Value("excludeCampsiteType"), Is.Null, "a missing key");
            Assert.That(Session.Value("consecutiveDays"), Is.EqualTo(1));
            Assert.That(SearchKeys.Where(Session.IsList), Is.EqualTo(new[] { "searchValueDates", "includeEquipment", "excludeEquipment", "includeSites", "excludeSites",
                "includeAttributes", "excludeAttributes", "includeCampsiteType", "excludeCampsiteType" }));
            Assert.That(Session.Campground, Is.EqualTo(Campgrounds["232447"]));
            Assert.That(Session.SecretKeys, Is.Empty);
            Assert.That(Session.IsDirty || Session.CanSave, Is.False);
            Assert.That(SearchKeys.Select(Session.LockReason), Is.All.Null);
            ConfigEditNotice Disabled = Session.Notices.Single(N => N.Code == "SearchDisabled");
            Assert.That((Disabled.Key, Disabled.Severity), Is.EqualTo(("autoRun", ConfigSeverity.Info)));
            Assert.That(Session.Notices.Where(N => N.Severity != ConfigSeverity.Info), Is.Empty);
        }

        // 8: each kind of setting is an edit only while it differs from the file, and reverts per key and for the file
        [Test]
        public void DirtyState_IsTrackedForEveryKindOfSetting()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            var Edits = new (string Key, object? Value, object? Original)[]
            {
                ("displayName", "Upper Pines weekends", "Upper Pines"),
                ("autoRun", true, false),
                ("campgroundID", "232445", "232447"),
                ("totalHumans", (int?)4, (int?)2),
                ("searchBy", SearchTypes.DaysOut, SearchTypes.StartEndDate),
                ("searchValue", 14, 0),
                ("showSaturday", true, false),
                ("consecutiveDays", 2, 1),
            };
            foreach (var (Key, Value, Original) in Edits)
            {
                Session.Set(Key, Value);
                Assert.That(Session.IsChanged(Key), Is.True, Key);
                Session.Set(Key, Original);
                Assert.That(Session.IsChanged(Key), Is.False, Key + " set back");
            }
            Session.SetItems("includeEquipment", new[] { new ConfigListItem(1, "RV"), new ConfigListItem(0, "Tent") });
            Assert.That(Session.IsChanged("includeEquipment"), Is.True);
            Session.SetItems("includeEquipment", new[] { new ConfigListItem(0, "Tent"), new ConfigListItem(1, "RV") });
            Assert.That(Session.IsChanged("includeEquipment"), Is.False, "the same items in the same order");
            Assert.That(Session.IsDirty, Is.False);

            foreach (var (Key, Value, _) in Edits)
            {
                Session.Set(Key, Value);
            }
            Session.SetItems("searchValueDates", new[] { new ConfigListItem(0, "2027-07-03"), new ConfigListItem(1, "2027-07-05") });
            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "displayName", "autoRun", "campgroundID", "totalHumans", "searchBy", "searchValue",
                "searchValueDates", "showSaturday", "consecutiveDays" }));
            Session.Revert("searchBy");
            Assert.That(Session.IsChanged("searchBy") || Session.Value("searchBy") is not SearchTypes.StartEndDate, Is.False);
            Session.RevertAll();
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.Candidate, Is.Null);
        }

        // 9, 10, 11, 12, 28: scalar, Boolean, choice and number edits replace only their values; comments, spacing and
        // every other line stay; the save is verified and keeps a backup
        [Test]
        public void ScalarEdits_ReplaceOnlyTheirValues_AndTheSaveIsExact()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("displayName", "Upper Pines weekends");
            Session.Set("autoRun", true);
            Session.Set("searchBy", SearchTypes.DaysOut);
            Session.Set("searchValue", 14);
            Session.Set("totalHumans", (int?)4);
            Session.Set("consecutiveDays", 2);
            Session.Set("showSaturday", true);

            ConfigEditResult Saved = Session.Save();

            Assert.That(Saved.Succeeded, Is.True, Saved.Message);
            Assert.That(Saved.Message, Is.EqualTo("Saved Yosemite.yaml. The previous version is in Yosemite.yaml.bak."));
            string Expected = SearchText;
            Expected = Replace(Expected, "displayName: Upper Pines   # shown", "displayName: Upper Pines weekends   # shown");
            Expected = Replace(Expected, "autoRun: false\n", "autoRun: true\n");
            Expected = Replace(Expected, "totalHumans: 2\n", "totalHumans: 4\n");
            Expected = Replace(Expected, "searchBy: StartEndDate\n", "searchBy: DaysOut\n");
            Expected = Replace(Expected, "searchValue: 0\n", "searchValue: 14\n");
            Expected = Replace(Expected, "showSaturday: false\n", "showSaturday: true\n");
            Expected = Replace(Expected, "consecutiveDays: 1\n", "consecutiveDays: 2\n");
            Assert.That(Text(Search), Is.EqualTo(Expected));
            Assert.That(Text(Search + ".bak"), Is.EqualTo(SearchText));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml", "Yosemite.yaml.bak" }));
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text(Search)).SearchValue, Is.EqualTo(14));
        }

        // 12, 21: a number of people can be cleared (null) and a missing setting added; a missing one stays missing and a
        // null one null until edited
        [Test]
        public void MissingAndNullValues_StayAsTheyAre_UntilEdited()
        {
            string Text_ = Replace(SearchText, "totalHumans: 2\n", "");
            Write(Search, Text_);
            var Session = OpenSearch();
            Assert.That(Session.Value("totalHumans"), Is.Null);
            Session.Set("totalHumans", null);
            Assert.That(Session.IsDirty, Is.False, "null is what the missing key gives");
            Session.Set("displayName", "Pines");
            Assert.That(Session.Save().Succeeded, Is.True);
            string After = Replace(Text_, "displayName: Upper Pines   #", "displayName: Pines   #");
            Assert.That(Text(Search), Is.EqualTo(After), "includeSites (null), excludeCampsiteType and totalHumans (missing) untouched");

            Session.Set("totalHumans", (int?)3);
            Session.SetItems("excludeCampsiteType", new[] { new ConfigListItem(null, "GROUP") });
            Assert.That(Session.Save().Succeeded, Is.True);
            CampertronConfig Loaded = Yaml.LoadSettingsText<CampertronConfig>(Text(Search));
            Assert.That(Loaded.TotalHumans, Is.EqualTo(3));
            Assert.That(Loaded.ExcludeCampsiteType, Is.EqualTo(new[] { "GROUP" }));
            Assert.That(Text(Search), Does.StartWith(After.Substring(0, After.IndexOf("# end", StringComparison.Ordinal))), "added after the last setting");

            Session.Set("totalHumans", null);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text(Search)).TotalHumans, Is.Null);
            Assert.That(Session.Notices.Single(N => N.Code == "TotalHumansMissing").Key, Is.EqualTo("totalHumans"));
        }

        // 13, 14, 15, 16, 17, 18, 19: dates and filters are edited item by item: insertions, removals, reordering and
        // changed values, each item keeping its comments
        [Test]
        public void ListEdits_InsertRemoveAndReorderItems_KeepingTheirComments()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            // reordered: Tent keeps its comment; a new item at the end
            Session.SetItems("includeEquipment", new[] { new ConfigListItem(1, "RV"), new ConfigListItem(0, "Tent"), new ConfigListItem(null, "Trailer") });
            // the end date changed, the start kept with its comments
            Session.SetItems("searchValueDates", new[] { new ConfigListItem(0, "2027-07-02"), new ConfigListItem(1, "2027-07-06") });
            // a flow list: one removed, one added
            Session.SetItems("excludeSites", new[] { new ConfigListItem(1, "B 002"), new ConfigListItem(null, "C003") });
            // an empty flow list gets its first item
            Session.SetItems("excludeAttributes", new[] { new ConfigListItem(null, "Pets Allowed:No") });

            Assert.That(Session.Save().Succeeded, Is.True);

            string Expected = SearchText;
            Expected = Replace(Expected, "  - Tent # small\n  - RV\n", "  - RV\n  - Tent # small\n  - Trailer\n");
            Expected = Replace(Expected, "  - 2027-07-05\n", "  - 2027-07-06\n");
            Expected = Replace(Expected, "excludeSites: [A001, 'B 002']\n", "excludeSites: ['B 002', C003]\n");
            // in a flow list the editor quotes a value with a colon (it never relies on YAML's finer plain-scalar rules)
            Expected = Replace(Expected, "excludeAttributes: []\n", "excludeAttributes: ['Pets Allowed:No']\n");
            Assert.That(Text(Search), Is.EqualTo(Expected));
            CampertronConfig Loaded = Yaml.LoadSettingsText<CampertronConfig>(Text(Search));
            Assert.That(Loaded.IncludeEquipment, Is.EqualTo(new[] { "RV", "Tent", "Trailer" }));
            Assert.That(Loaded.ExcludeAttributes, Is.EqualTo(new[] { "Pets Allowed:No" }));

            // removing an item with its own comment announces the comment it takes
            var Again = OpenSearch();
            Again.SetItems("searchValueDates", new[] { new ConfigListItem(1, "2027-07-06") });
            ConfigEditNotice Removed = Again.Notices.Single(N => N.Code == "CommentRemoved");
            Assert.That((Removed.Key, Removed.Severity), Is.EqualTo(("searchValueDates", ConfigSeverity.Warning)));
        }

        // 20: a null list stays null (not []) until edited, and gets block items when it is
        [Test]
        public void ANullList_StaysNull_UntilItIsEdited()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("consecutiveDays", 3);
            Session.SetItems("includeSites", Array.Empty<ConfigListItem>());
            Assert.That(Session.IsChanged("includeSites"), Is.False, "no rows is no edit of a null list");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Search), Does.Contain("\nincludeSites:\nexcludeSites:"));
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text(Search)).IncludeSites, Is.Null);

            Session.SetItems("includeSites", new[] { new ConfigListItem(null, "A012"), new ConfigListItem(null, "A014") });
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text(Search)).IncludeSites, Is.EqualTo(new[] { "A012", "A014" }));
        }

        // An item that is empty (null) in the file, written "- ~", stays exactly as it is while other items are edited, and
        // can only be kept, changed or removed: a new row can't be empty. A list with a bare "-" item is one the editor
        // does not edit item by item: it is locked with its reason, and the other settings stay editable.
        [Test]
        public void AnEmptyItem_StaysAsItIs_WhileOtherItemsAreEdited()
        {
            string Text_ = Replace(SearchText, "  - Shade:Yes\n", "  - Shade:Yes\n  - ~\n");
            Write(Search, Text_);
            var Session = OpenSearch();
            Assert.That(Session.Items("includeAttributes").Select(R => R.Value), Is.EqualTo(new[] { "Shade:Yes", null }));
            Assert.That(Session.IsNullItem("includeAttributes", 1), Is.True);
            Assert.That(Session.LockReason("includeAttributes"), Is.Null);
            ConfigEditNotice Empty = Session.Notices.Single(N => N.Code == "ListItemNull");
            Assert.That((Empty.Key, Empty.Index, Empty.Severity), Is.EqualTo(("includeAttributes", (int?)1, ConfigSeverity.Warning)), "a disabled search: a warning");

            Assert.That(() => Session.SetItems("includeAttributes", new[] { new ConfigListItem(0, "Shade:Yes"), new ConfigListItem(null, null) }), Throws.ArgumentException);
            Assert.That(() => Session.SetItems("includeAttributes", new[] { new ConfigListItem(0, null), new ConfigListItem(1, null) }), Throws.ArgumentException);
            Session.SetItems("includeAttributes", new[] { new ConfigListItem(0, "Shade:No"), new ConfigListItem(1, null) });
            Assert.That(Session.CanSave, Is.True, Everything(Session));
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Search), Is.EqualTo(Replace(Text_, "  - Shade:Yes\n  - ~\n", "  - Shade:No\n  - ~\n")));

            // enabled, the empty item stops the search: an error that blocks the save until it is removed
            Session.Set("autoRun", true);
            Assert.That(Session.Notices.Single(N => N.Code == "ListItemNull").Severity, Is.EqualTo(ConfigSeverity.Error));
            Assert.That(Session.CanSave, Is.False);
            Session.SetItems("includeAttributes", new[] { new ConfigListItem(0, "Shade:No") });
            Assert.That(Session.CanSave, Is.True);

            // a bare "-": the list is locked, the rest editable, and the item kept as it is
            string Bare = Replace(SearchText, "  - Shade:Yes\n", "  - Shade:Yes\n  -\n");
            Write(Search, Bare);
            var Locked = OpenSearch();
            Assert.That(Locked.LockReason("includeAttributes"), Does.Contain("can't edit item by item"));
            Locked.Set("displayName", "Kept");
            Assert.That(Locked.Save().Succeeded, Is.True);
            Assert.That(Text(Search), Is.EqualTo(Replace(Bare, "displayName: Upper Pines   #", "displayName: Kept   #")));
        }

        // 22, 23, 24, 25: the validation's problems at their settings and list items; errors block, warnings don't
        [Test]
        public void Validation_MapsToSettingsAndItems_AndOnlyErrorsBlock()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("autoRun", true);
            Session.SetItems("searchValueDates", new[] { new ConfigListItem(0, "2027-07-02"), new ConfigListItem(null, "not a date") });

            ConfigEditNotice Invalid = Session.Notices.Single(N => N.Code == "DateInvalid");
            Assert.That((Invalid.Key, Invalid.Index, Invalid.Severity), Is.EqualTo(("searchValueDates", (int?)1, ConfigSeverity.Error)));
            Assert.That(Invalid.Message, Is.EqualTo("searchValueDates item 2 is not a date."));
            Assert.That(Session.HasBlockingErrors, Is.True);
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(Text(Search), Is.EqualTo(SearchText));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml" }));

            // a date in this computer's format: a warning at its item, and the save is allowed
            Session.SetItems("searchValueDates", new[] { new ConfigListItem(0, "2027-07-02"), new ConfigListItem(null, "2027/07/09") });
            ConfigEditNotice NotIso = Session.Notices.Single(N => N.Code == "DateNotIso");
            Assert.That((NotIso.Key, NotIso.Index, NotIso.Severity), Is.EqualTo(("searchValueDates", (int?)1, ConfigSeverity.Warning)));
            Assert.That(Session.CanSave, Is.True);

            // equal dates stop an enabled StartEndDate search (DateTime.MaxValue and reversed ranges are the validation's)
            Session.SetItems("searchValueDates", new[] { new ConfigListItem(0, "2027-07-02"), new ConfigListItem(1, "2027-07-02") });
            Assert.That(Session.Notices.Single(N => N.Code == "DatesEqual").Severity, Is.EqualTo(ConfigSeverity.Error));
            Session.Set("searchBy", SearchTypes.DaysOut);
            Session.Set("searchValue", int.MaxValue);
            ConfigEditNotice Range = Session.Notices.Single(N => N.Code == "SearchWindowOutOfRange");
            Assert.That((Range.Key, Range.Severity), Is.EqualTo(("searchValue", ConfigSeverity.Error)));
            Session.Set("searchValue", -1);
            Assert.That(Session.Notices.Single(N => N.Code == "SearchValueNegative").Severity, Is.EqualTo(ConfigSeverity.Error));
            Session.Set("searchValue", 7);
            Assert.That(Session.HasBlockingErrors, Is.False);

            // no day of the week: a warning about the file (no key)
            foreach (string Day in new[] { "showMonday", "showTuesday", "showWednesday", "showThursday", "showFriday" })
            {
                Session.Set(Day, false);
            }
            ConfigEditNotice Days = Session.Notices.Single(N => N.Code == "NoWeekdays");
            Assert.That((Days.Key, Days.Severity), Is.EqualTo(((string?)null, ConfigSeverity.Warning)));
            Assert.That(Session.Save().Succeeded, Is.True);
        }

        // 26: no edit, no write: not the file, not a backup, not the time
        [Test]
        public void NoEdit_WritesNothing()
        {
            Write(Search, SearchText);
            DateTime Written = File.GetLastWriteTimeUtc(Search);
            var Session = OpenSearch();
            Session.Set("autoRun", true);
            Session.Set("autoRun", false);

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Message, Is.EqualTo("Nothing to save: Yosemite.yaml has no changes."));
            Assert.That(Text(Search), Is.EqualTo(SearchText));
            Assert.That(File.GetLastWriteTimeUtc(Search), Is.EqualTo(Written));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml" }));
        }

        // 27, 52: the enabled toggle changes autoRun only: the preview shows that one line, and the save changes only it
        // (here in a file with a byte-order mark and CRLF line breaks)
        [Test]
        public void TheEnabledToggle_ChangesOnlyAutoRun()
        {
            string Crlf = SearchText.Replace("\n", "\r\n");
            Write(Search, Crlf, Bom: true);
            byte[] Before = File.ReadAllBytes(Search);
            var Session = OpenSearch();
            Session.Set("autoRun", true);

            Assert.That(Session.Preview.Select(L => (L.Kind, L.Text)), Is.EqualTo(new[]
            {
                (ConfigPreviewKind.Context, "displayName: Upper Pines   # shown in the results"),
                (ConfigPreviewKind.Removed, "autoRun: false"),
                (ConfigPreviewKind.Added, "autoRun: true"),
                (ConfigPreviewKind.Context, "campgroundID: '232447'"),
            }));
            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "autoRun" }));
            Assert.That(Session.Save().Succeeded, Is.True);

            byte[] After = File.ReadAllBytes(Search);
            byte[] Expected = Utf8(Crlf.Replace("autoRun: false\r\n", "autoRun: true\r\n"), Bom: true);
            Assert.That(After, Is.EqualTo(Expected));
            // exactly one changed range: "false" became "true"
            int First = Enumerable.Range(0, Before.Length).First(i => Before[i] != After[i]);
            Assert.That(Before.Skip(First + 5).ToArray(), Is.EqualTo(After.Skip(First + 4).ToArray()));
            Assert.That(File.ReadAllBytes(Search + ".bak"), Is.EqualTo(Before));

            Session.Set("autoRun", false);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(Search), Is.EqualTo(Before), "toggled back: the original bytes");
        }

        // 29: a write that does not verify leaves the file as it was and keeps the edits
        [Test]
        public void AFailedVerification_LeavesTheFile_AndKeepsTheEdits()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("displayName", "Changed");
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "temporary-written")
                {
                    File.WriteAllText(Path_, SearchText.Replace("Upper Pines", "Tampered"));
                }
            };

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Text(Search), Is.EqualTo(SearchText));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml" }));
            Assert.That(Session.IsDirty && Session.Value("displayName") is "Changed", Is.True);
        }

        // 30, 31: a change on disk while editing other settings: a conflict that the edits can be applied to; the save
        // then keeps both (an external autoRun edit and an external list edit included)
        [Test]
        public void AnExternalChangeOfOtherSettings_IsAConflict_ThatKeepsBothChanges()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("displayName", "Mine");
            Session.SetItems("includeEquipment", new[] { new ConfigListItem(0, "Tent"), new ConfigListItem(1, "RV"), new ConfigListItem(null, "Trailer") });
            string Theirs = Replace(Replace(SearchText, "autoRun: false", "autoRun: true"), "excludeSites: [A001, 'B 002']", "excludeSites: [A001]");
            Write(Search, Theirs);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.OverlappingKeys, Is.Empty);
            Assert.That(Session.Conflict.CanApplyEdits, Is.True);
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(Text(Search), Is.EqualTo(Theirs), "nothing written while the conflict is open");

            Assert.That(Session.ApplyEditsToExternalVersion(KeepMine: false).Succeeded, Is.True);
            Assert.That(Session.Save().Succeeded, Is.True);
            string Expected = Replace(Replace(Theirs, "displayName: Upper Pines   #", "displayName: Mine   #"), "  - RV\n", "  - RV\n  - Trailer\n");
            Assert.That(Text(Search), Is.EqualTo(Expected));
            Assert.That(Text(Search + ".bak"), Is.EqualTo(Theirs));
        }

        // 32: the same setting changed on disk and here: applying needs the explicit choice to keep the edited values; a
        // list both changed keeps the comments of the items both have
        [Test]
        public void AnExternalChangeOfTheSameSettings_NeedsTheChoiceToKeepMine()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("displayName", "Mine");
            Session.SetItems("includeEquipment", new[] { new ConfigListItem(0, "Tent"), new ConfigListItem(null, "Hammock") });
            string Theirs = Replace(Replace(SearchText, "displayName: Upper Pines", "displayName: Theirs"), "  - RV\n", "  - RV\n  - Van\n");
            Write(Search, Theirs);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.OverlappingKeys, Is.EqualTo(new[] { "displayName", "includeEquipment" }));
            Assert.That(Session.ApplyEditsToExternalVersion(KeepMine: false).Succeeded, Is.False);
            Assert.That(Session.Conflict, Is.Not.Null);
            Assert.That(Session.ApplyEditsToExternalVersion(KeepMine: true).Succeeded, Is.True);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Search), Is.EqualTo(Replace(Replace(SearchText, "displayName: Upper Pines", "displayName: Mine"), "  - RV\n", "  - Hammock\n")));
        }

        // conflict cancellation: discarding the edits shows the file on disk; nothing is written
        [Test]
        public void DiscardingTheEdits_InAConflict_KeepsTheFileOnDisk()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("autoRun", true);
            string Theirs = Replace(SearchText, "consecutiveDays: 1", "consecutiveDays: 2");
            Write(Search, Theirs);
            Session.CheckForExternalChange();

            Session.UseExternalVersion();

            Assert.That(Session.Conflict, Is.Null);
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.Value("consecutiveDays"), Is.EqualTo(2));
            Assert.That(Text(Search), Is.EqualTo(Theirs));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml" }));
        }

        // 33: deleted while edited: the edits stay; the file can be created again with them (create-only); deleted while
        // clean: the session is Missing
        [Test]
        public void ADeletedFile_KeepsTheEdits_AndCanBeCreatedAgain()
        {
            Write(Search, SearchText);
            var Clean = OpenSearch();
            var Session = OpenSearch();
            Session.Set("autoRun", true);
            File.Delete(Search);

            Assert.That(Clean.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Deleted));
            Assert.That(Clean.State, Is.EqualTo(ConfigEditState.Missing));
            Assert.That(Clean.Create().Succeeded, Is.False, "a search file is created with New search");
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.Kind, Is.EqualTo(ConfigConflictKind.Deleted));
            Assert.That(Session.RecreateWithEdits().Succeeded, Is.True);
            Assert.That(Text(Search), Is.EqualTo(Replace(SearchText, "autoRun: false", "autoRun: true")));

            // created meanwhile by another program: never written over
            Session.Set("consecutiveDays", 5);
            File.Delete(Search);
            Session.CheckForExternalChange();
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-create")
                {
                    Write(Search, "displayName: theirs\n");
                }
            };
            Assert.That(Session.RecreateWithEdits().Succeeded, Is.False);
            Assert.That(Text(Search), Is.EqualTo("displayName: theirs\n"));
        }

        // a permission change is no content change: the edits stay and the save keeps the new permissions
        [Test]
        public void APermissionChange_KeepsTheEdits()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("displayName", "Mine");
            if (!OnUnix("ConfigEditEmailTests.TheAccessRulesOnWindows_AreNeverBroadened"))
            {
                return;
            }
            File.SetUnixFileMode(Search, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.MetadataChanged));
            Assert.That(Session.Conflict, Is.Null);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.GetUnixFileMode(Search), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.GetUnixFileMode(Search + ".bak"), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }

        // a byte-order mark or line-break change on disk is a content change: a conflict whose resolution keeps it
        [Test]
        public void ALineBreakChangeOnDisk_IsKept_WhenTheEditsAreApplied()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("autoRun", true);
            string Theirs = SearchText.Replace("\n", "\r\n");
            Write(Search, Theirs, Bom: true);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.ApplyEditsToExternalVersion(KeepMine: false).Succeeded, Is.True);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(Search), Is.EqualTo(Utf8(Theirs.Replace("autoRun: false", "autoRun: true"), Bom: true)));
        }

        // no lost update: a change on disk between the check and the rename is never written over
        [Test]
        public void AChangeOnDisk_DuringTheSave_IsNeverWrittenOver()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("displayName", "Mine");
            string Theirs = Replace(SearchText, "consecutiveDays: 1", "consecutiveDays: 9");
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "temporary-written")
                {
                    Write(Search, Theirs);
                }
            };

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Text(Search), Is.EqualTo(Theirs));
            Assert.That(Session.Conflict, Is.Not.Null);
            Assert.That(Session.IsDirty, Is.True);
        }

        // 42, 46, 47, 48, 49: campgroundID is a string matched exactly against the campground data: found, the file's own
        // unknown ID (a warning), an entered unknown ID (an error that blocks), never parsed or trimmed
        [Test]
        public void TheCampgroundID_IsAnExactStringOfTheCampgroundData()
        {
            Write(Search, SearchText);
            var Session = OpenSearch();
            Session.Set("campgroundID", "0990010");
            Assert.That(Session.Campground!.FacilityID, Is.EqualTo("0990010"));
            Assert.That(Session.Notices.Where(N => N.Code == "CampgroundNotFound"), Is.Empty);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Search), Does.Contain("campgroundID: '0990010'\n"));
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text(Search)).CampgroundID, Is.EqualTo("0990010"), "the leading zero is kept");

            foreach (string Entered in new[] { "990010", " 0990010", "0990010 ", "23244", "232447x", "2324470" })
            {
                Session.Set("campgroundID", Entered);
                ConfigEditNotice NotFound = Session.Notices.Single(N => N.Code == "CampgroundNotFound");
                Assert.That((NotFound.Key, NotFound.Severity), Is.EqualTo(("campgroundID", ConfigSeverity.Error)), Entered);
                Assert.That(NotFound.Message, Does.Not.Contain(Entered.Trim()));
                Assert.That(Session.CanSave, Is.False, Entered);
                Assert.That(Session.Campground, Is.Null);
            }

            // the file's own ID that the campground data does not have: a warning; other settings can still be saved
            Write(Search, Replace(SearchText, "campgroundID: '232447'", "campgroundID: '999999'"));
            var Unknown = OpenSearch();
            Assert.That(Unknown.Notices.Single(N => N.Code == "CampgroundNotFound").Severity, Is.EqualTo(ConfigSeverity.Warning));
            Unknown.Set("autoRun", true);
            Assert.That(Unknown.CanSave, Is.True);

            // a lookup that answers with another campground is no answer
            var Loose = new ConfigEditSession<CampertronConfig>(Folder, "Yosemite.yaml", () => Now, Id => Campgrounds["232447"]);
            Assert.That(Loose.Campground, Is.Null);
            // without campground data nothing is claimed
            Assert.That(OpenSearch(Lookup: false).Notices.Where(N => N.Code == "CampgroundNotFound"), Is.Empty);
        }

        // undefined choices are kept as the file has them (a validation problem, never replaced silently)
        [Test]
        public void AnUndefinedSearchType_IsKept_UntilTheUserChoosesOne()
        {
            Write(Search, Replace(SearchText, "searchBy: StartEndDate", "searchBy: 7"));
            var Session = OpenSearch();
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));
            Assert.That((int)(SearchTypes)Session.Value("searchBy")!, Is.EqualTo(7));
            ConfigEditNotice Invalid = Session.Notices.Single(N => N.Code == "InvalidChoice");
            Assert.That((Invalid.Key, Invalid.Severity), Is.EqualTo(("searchBy", ConfigSeverity.Warning)), "disabled: a warning");

            Session.Set("displayName", "Kept");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Search), Does.Contain("searchBy: 7\n"));
            Session.Set("autoRun", true);
            Assert.That(Session.Notices.Single(N => N.Code == "InvalidChoice").Severity, Is.EqualTo(ConfigSeverity.Error));
            Session.Set("searchBy", SearchTypes.MonthsOut);
            Assert.That(Session.CanSave, Is.True);
        }

        // 5 (unknown and unsupported content): opened read-only with a reason that holds no value, never changed
        [TestCase("extraKey: hello\n", "ReadOnly", "UnknownKey")]
        [TestCase("searchBy: [DaysOut\n", "ReadOnly", "Malformed")]
        [TestCase("# only a comment\n", "ReadOnly", "Empty")]
        [TestCase("", "Unreadable", null)]
        public void FilesThatCantBeEdited_OpenWithAReason_AndAreNeverChanged(string Extra, string State, string? Code)
        {
            string Text_ = State == "Unreadable" ? "" : Extra.StartsWith("extraKey", StringComparison.Ordinal) ? SearchText + Extra
                : Extra.StartsWith("searchBy", StringComparison.Ordinal) ? Replace(SearchText, "searchBy: StartEndDate\n", Extra) : Extra;
            Write(Search, Text_);
            byte[] Before = File.ReadAllBytes(Search);
            var Session = OpenSearch();

            Assert.That(Session.State.ToString(), Is.EqualTo(State));
            Assert.That(Session.StateReason, Is.Not.Null.And.Not.Empty);
            if (Code != null)
            {
                Assert.That(Session.Notices.Select(N => N.Code), Does.Contain(Code));
            }
            Assert.That(() => Session.Set("autoRun", true), Throws.InvalidOperationException);
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(Session.SaveAs("Copy", out _).Succeeded, Is.False);
            Assert.That(Everything(Session), Does.Not.Contain("hello"));
            Assert.That(File.ReadAllBytes(Search), Is.EqualTo(Before));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml" }));
        }

        // the session's name rules: a search file by its name in the folder, never a path or a settings file
        [TestCase("General.yaml")]
        [TestCase("EMAIL.yaml")]
        [TestCase("../x.yaml")]
        [TestCase("sub/x.yaml")]
        [TestCase("")]
        public void ASearchSession_NeedsASearchFileName(string Name)
        {
            Assert.That(() => new ConfigEditSession<CampertronConfig>(Folder, Name, () => Now, null), Throws.ArgumentException);
        }

        [Test]
        public void GeneralAndEmailSessions_TakeNoFileNameOrCampgrounds()
        {
            Assert.That(() => new ConfigEditSession<GeneralConfig>(Folder, "Other.yaml", () => Now, null), Throws.ArgumentException);
            Assert.That(() => new ConfigEditSession<EmailConfig>(Folder, null, () => Now, Find), Throws.ArgumentException);
        }

        // 55: the search editing code refers to no search, download, email or refresh code
        [Test]
        public void TheSearchEditing_NeverSearches()
        {
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.", "CampertronLibrary.function.Base.Web", "System.Net.", "CampsiteConfig",
                "CampertronLibrary.function.Base.ConsoleUi", "System.Console", "System.Diagnostics.Process",
            };
            List<string> Called = ConfigEditSessionTests.Referenced(typeof(ConfigEditSession<CampertronConfig>))
                .Concat(ConfigEditSessionTests.Referenced(typeof(SearchFiles))).Concat(ConfigEditSessionTests.Referenced(typeof(SearchFileName))).ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.ConfigFileReplace::Create"));
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
        }
    }
}
