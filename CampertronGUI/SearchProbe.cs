using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;

namespace Campertron
{
    // The search editor's part of `CampertronGUI --smoke-test` (CM6, decision D44): on the platform the release runs on
    // (natively on Windows and macOS in the release dry runs), in a new temporary folder (never the settings folder), it
    // creates a search with New search, edits a search file with a byte-order mark, CRLF line breaks and comments through
    // the editing session (the enabled toggle alone, then list items and the name), saves it under another name, and
    // chooses its campground by exact FacilityID from the installed campground data. It never searches availability. The
    // result line names only what was checked.
    internal static class SearchProbe
    {
        private const string SearchText =
            "# Campertron search probe\r\n" +
            "displayName: Probe   # shown in the results\r\n" +
            "autoRun: false\r\n" +
            "campgroundID: '232447'\r\n" +
            "totalHumans: 2\r\n" +
            "searchBy: DaysOut\r\n" +
            "searchValue: 7\r\n" +
            "searchValueDates: []\r\n" +
            "showMonday: true\r\nshowTuesday: true\r\nshowWednesday: true\r\nshowThursday: true\r\nshowFriday: true\r\nshowSaturday: true\r\nshowSunday: true\r\n" +
            "includeEquipment:\r\n" +
            "  # tents first\r\n" +
            "  - Tent # small\r\n" +
            "  - RV\r\n" +
            "excludeEquipment: []\r\nincludeSites: []\r\nexcludeSites: []\r\nincludeAttributes: []\r\nexcludeAttributes: []\r\n" +
            "includeCampsiteType: []\r\nexcludeCampsiteType:\r\n  - GROUP\r\n" +
            "consecutiveDays: 1\r\n";
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
        private static readonly UnixFileMode GroupReadable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

        internal static (bool Passed, string Detail) Run(string ConfigPath)
        {
            string Folder = Path.Join(Path.GetTempPath(), "campertron-search-probe-" + Guid.NewGuid().ToString("N"));
            string Step = "creating the probe folder";
            try
            {
                Directory.CreateDirectory(Folder);

                Step = "New search";
                ConfigEditResult Created = SearchFiles.Create(Folder, "Probe new", out string? NewName);
                if (!Created.Succeeded || NewName != "Probe new.yaml" || Yaml.LoadSettingsText<CampertronConfig>(File.ReadAllText(Path.Join(Folder, NewName))).AutoRun)
                {
                    return Fail("New search did not create a disabled search");
                }

                // a campground of the installed data, chosen by its exact ID: one whose ID without its last character is no
                // campground, so a partial ID must not find it
                Step = "the campground data";
                Dictionary<string, CampgroundChoice> Campgrounds = Read.CampgroundPickerEntries(ConfigPath).ToDictionary(E => E.FacilityID, E => E.Choice, StringComparer.Ordinal);
                string? ProbeCampground = Campgrounds.Keys.Where(Id => Id.Length > 1 && Id != "232447" && !Campgrounds.ContainsKey(Id.Substring(0, Id.Length - 1)))
                    .OrderBy(Id => Id, StringComparer.Ordinal).FirstOrDefault();
                if (ProbeCampground == null)
                {
                    return Fail("the campground data lists no campground");
                }
                CampgroundChoice? Find(string Id) => Campgrounds.TryGetValue(Id, out CampgroundChoice? Choice) ? Choice : null;
                var Partial = new ConfigEditSession<CampertronConfig>(Folder, "Probe new.yaml", null, Find);
                Partial.Set("campgroundID", ProbeCampground.Substring(0, ProbeCampground.Length - 1));
                if (!Partial.Notices.Any(N => N.Code == "CampgroundNotFound" && N.Severity == ConfigSeverity.Error) || Partial.CanSave)
                {
                    return Fail("a partial campground ID was accepted");
                }

                Step = "the search file";
                string Search = Path.Join(Folder, "Probe.yaml");
                byte[] Original = Bom.Concat(Encoding.UTF8.GetBytes(SearchText)).ToArray();
                File.WriteAllBytes(Search, Original);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(Search, GroupReadable);
                }

                // the enabled toggle alone: only "false" becomes "true"
                var Toggle = new ConfigEditSession<CampertronConfig>(Folder, "Probe.yaml", null, Find);
                Toggle.Set("autoRun", true);
                if (!Toggle.Save().Succeeded || !File.ReadAllBytes(Search).AsSpan().SequenceEqual(Expected(SearchText.Replace("autoRun: false", "autoRun: true"))))
                {
                    return Fail("the enabled toggle did not change autoRun alone");
                }
                if (!File.ReadAllBytes(Search + ".bak").AsSpan().SequenceEqual(Original))
                {
                    return Fail("Probe.yaml.bak is not the exact previous file");
                }

                // list items (reordered, one added; the comments travel with their items), the name and the campground
                var Edit = new ConfigEditSession<CampertronConfig>(Folder, "Probe.yaml", null, Find);
                Edit.SetItems("includeEquipment", new[] { new ConfigListItem(1, "RV"), new ConfigListItem(0, "Tent"), new ConfigListItem(null, "Trailer") });
                Edit.Set("displayName", "Probe edited");
                Edit.Set("campgroundID", ProbeCampground);
                if (Edit.Notices.Any(N => N.Code == "CampgroundNotFound") || Edit.Campground?.FacilityID != ProbeCampground)
                {
                    return Fail("the campground was not found by its exact ID");
                }
                string Edited = SearchText.Replace("autoRun: false", "autoRun: true").Replace("displayName: Probe   #", "displayName: Probe edited   #")
                    .Replace("campgroundID: '232447'", "campgroundID: '" + ProbeCampground + "'")
                    .Replace("  # tents first\r\n  - Tent # small\r\n  - RV\r\n", "  - RV\r\n  # tents first\r\n  - Tent # small\r\n  - Trailer\r\n");
                if (Edit.HasBlockingErrors || !Edit.Save().Succeeded || !File.ReadAllBytes(Search).AsSpan().SequenceEqual(Expected(Edited)))
                {
                    return Fail("the list, name and campground edits were not saved exactly (byte-order mark, CRLF, comments)");
                }
                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(Search) != GroupReadable || File.GetUnixFileMode(Search + ".bak") != GroupReadable))
                {
                    return Fail("Probe.yaml or its backup did not keep the file's permissions");
                }
                if (OperatingSystem.IsWindows() && AccessRules(Search) != AccessRules(Search + ".bak"))
                {
                    return Fail("Probe.yaml's backup has other access rules than the file");
                }

                // Save As: the edited search to a new file; the source stays as it is
                var Copy = new ConfigEditSession<CampertronConfig>(Folder, "Probe.yaml", null, Find);
                Copy.Set("consecutiveDays", 2);
                byte[] Source = File.ReadAllBytes(Search);
                if (!Copy.SaveAs("Probe copy", out string? CopyName).Succeeded || CopyName != "Probe copy.yaml"
                    || !File.ReadAllBytes(Path.Join(Folder, CopyName)).AsSpan().SequenceEqual(Expected(Edited.Replace("consecutiveDays: 1", "consecutiveDays: 2")))
                    || !File.ReadAllBytes(Search).AsSpan().SequenceEqual(Source) || Copy.SaveAs("probe COPY", out _).Succeeded)
                {
                    return Fail("Save As did not write the new file alone, or wrote over a file");
                }
                if (Directory.GetFiles(Folder).Any(F => F.EndsWith(ConfigFileReplace.TemporarySuffix, StringComparison.Ordinal)))
                {
                    return Fail("a temporary file was left");
                }
                return (true, "pass created=disabled auto-run=toggled-alone lists=kept comments=kept line-endings=crlf bom=kept save-as=created,no-overwrite campground-id=exact permissions="
                    + (OperatingSystem.IsWindows() ? "access-rules-kept" : "640"));
            }
            catch (Exception ex)
            {
                // the type only: a message could name a path
                return Fail(Step + " failed with " + ex.GetType().Name);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(Folder))
                    {
                        Directory.Delete(Folder, true);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // a leftover temporary folder is harmless; the result above stands
                }
            }
        }

        private static byte[] Expected(string Text) => Bom.Concat(Encoding.UTF8.GetBytes(Text)).ToArray();

        private static (bool, string) Fail(string Why) => (false, "fail " + Why);

        private static string? AccessRules(string Path_) =>
            OperatingSystem.IsWindows()
                ? new FileInfo(Path_).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                : null;
    }
}
