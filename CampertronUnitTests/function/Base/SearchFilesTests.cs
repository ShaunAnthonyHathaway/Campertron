using System.Globalization;
using CampertronLibrary.function.Base;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CampertronUnitTests.function.Base
{
    // The search files of the settings folder in the GUI (CM6, decision D44; business-rules §18): which files are search
    // files and in which order, the names New search and Save As accept, creating a search (the example, not enabled) and
    // Save As (create-only, never over a file), the folder's "no usable enabled search" state, and that the console reads
    // the folder as before. Each test has its own temporary settings folder (ConfigEditTestBase).
    public class SearchFilesTests : ConfigEditTestBase
    {
        private string In(string Name) => Path.Join(Folder, Name);

        private protected ConfigEditSession<CampertronConfig> OpenSearch(string Name) =>
            new ConfigEditSession<CampertronConfig>(Folder, Name, () => Now, null);

        // 1, 2, 3, 4: the console's search files (no other discovery): General.yaml and Email.yaml in any case, backups,
        // temporary files, .yml files and subfolders are not search files; sorted ignoring case, then ordinally, in any
        // culture (in Turkish, EMAIL lower-cased with the culture is "emaıl")
        [Test]
        public void TheSearchFiles_AreTheConsolesOnes_InAFixedOrder()
        {
            // names that differ only in case are one file on Windows and (by default) macOS: the twins only where they exist
            Write(In("Case.probe"), "");
            bool CaseSensitive = !File.Exists(In("case.probe"));
            File.Delete(In("Case.probe"));
            var Names_ = new List<string> { "b.yaml", "A.yaml", "a2.yaml", "gEnErAl.yaml", "EMAIL.yaml", "x.yaml.bak", "x.yaml.0123456789abcdef0123456789abcdef.tmp", "y.yml", "notes.txt" };
            var Expected = new List<string> { "A.yaml", "a2.yaml", "b.yaml" };
            if (CaseSensitive)
            {
                Names_.AddRange(new[] { "a.yaml", "General.yaml", "Email.yaml" });
                Expected.Insert(1, "a.yaml");
            }
            foreach (string Name in Names_)
            {
                Write(In(Name), "displayName: " + Name + "\n");
            }
            Directory.CreateDirectory(In("sub"));
            Write(In(Path.Join("sub", "z.yaml")), "displayName: z\n");
            CultureInfo Before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                List<string> Names = ConfigFiles.SortedSearchFileNames(Folder);
                Assert.That(Names, Is.EqualTo(Expected), "ignoring case, then ordinally (A before a)");
                Assert.That(Names, Is.EquivalentTo(ConfigFiles.SearchFiles(Folder).Select(Path.GetFileName)), "exactly the console's files");
            }
            finally
            {
                CultureInfo.CurrentCulture = Before;
            }
        }

        // 35, 36, 37, 38: the names New search and Save As accept
        [TestCase("Yosemite weekends", "Yosemite weekends.yaml")]
        [TestCase("Zion.yaml", "Zion.yaml")]
        [TestCase("Zion.YAML", "Zion.yaml")]
        [TestCase("Upper_Pines-2027 (July)", "Upper_Pines-2027 (July).yaml")]
        [TestCase("v1.2", "v1.2.yaml")]
        [TestCase("Caf\u00E9", "Caf\u00E9.yaml")]
        public void ANewSearchFileName_IsAccepted(string Input, string FileName)
        {
            Assert.That(SearchFileName.Check(Input, out string Name), Is.Null);
            Assert.That(Name, Is.EqualTo(FileName));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(".yaml")]
        [TestCase("../outside")]
        [TestCase("..")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("/etc/passwd")]
        [TestCase("C:\\Windows\\x")]
        [TestCase("x.yml")]
        [TestCase(".hidden")]
        [TestCase("trailing.")]
        [TestCase(" leading")]
        [TestCase("trailing ")]
        [TestCase("CON")]
        [TestCase("con.yaml")]
        [TestCase("NUL.txt")]
        [TestCase("LPT1")]
        [TestCase("com\u00B9")]
        [TestCase("General")]
        [TestCase("general.yaml")]
        [TestCase("EMAIL")]
        [TestCase("Email.YAML")]
        [TestCase("a:b")]
        [TestCase("a*b")]
        [TestCase("a?b")]
        [TestCase("a\"b")]
        [TestCase("a<b>")]
        [TestCase("a|b")]
        [TestCase("tab\there")]
        [TestCase("new\nline")]
        [TestCase("\u202Eevil")]
        [TestCase("emoji\uD83D\uDE00")]
        public void AnUnsafeOrReservedName_IsRefused(string? Input)
        {
            string? Problem = SearchFileName.Check(Input, out string Name);
            Assert.That(Problem, Is.Not.Null.And.Not.Empty);
            Assert.That(Name, Is.Empty);
        }

        [Test]
        public void ATooLongName_IsRefused()
        {
            Assert.That(SearchFileName.Check(new string('a', SearchFileName.MaxNameLength), out _), Is.Null);
            Assert.That(SearchFileName.Check(new string('a', SearchFileName.MaxNameLength + 1), out _), Does.Contain("too long"));
        }

        // 41: an existing file, folder or link of the name, also in another case, is refused
        [Test]
        public void ANameInUse_IsRefused_InAnyCase()
        {
            Write(In("zion.yaml"), "displayName: z\n");
            Directory.CreateDirectory(In("Folder.yaml"));
            Assert.That(SearchFileName.CheckFree(Folder, "zion.yaml"), Is.EqualTo("zion.yaml already exists; choose another name."));
            Assert.That(SearchFileName.CheckFree(Folder, "Zion.yaml"), Does.Contain("differs from it only in upper and lower case"));
            Assert.That(SearchFileName.CheckFree(Folder, "folder.yaml"), Is.Not.Null);
            Assert.That(SearchFileName.CheckFree(Folder, "Other.yaml"), Is.Null);
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(In("dangling.yaml"), In("nowhere.yaml"));
                Assert.That(SearchFileName.CheckFree(Folder, "Dangling.yaml"), Is.Not.Null, "a dangling link");
            }
        }

        // 34: New search writes the example search (the console's serializer text), not enabled, create-only, no backup;
        // its message says what that means (V011-3)
        [Test]
        public void NewSearch_CreatesTheExampleSearch_NotEnabled()
        {
            ConfigEditResult Result = SearchFiles.Create(Folder, "Yosemite weekends", out string? Created);

            Assert.That(Result.Succeeded, Is.True, Result.Message);
            Assert.That(Result.Message, Is.EqualTo("Created Yosemite weekends.yaml with the example search's settings. It is not enabled (autoRun: false): "
                + "the Results tab runs it when you select it, and the console's Search and email mode run it once you enable it and save."));
            Assert.That(Created, Is.EqualTo("Yosemite weekends.yaml"));
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite weekends.yaml" }), "no backup, no temporary file");
            CampertronConfig Sample = Yaml.SearchSampleConfig();
            Sample.AutoRun = false;
            string Expected = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build().Serialize(Sample);
            Assert.That(Text(In("Yosemite weekends.yaml")), Is.EqualTo(Expected));
            CampertronConfig Loaded = Yaml.LoadSettingsText<CampertronConfig>(Text(In("Yosemite weekends.yaml")));
            Assert.That(Loaded.AutoRun, Is.False);
            Assert.That(Loaded.CampgroundID, Is.EqualTo("232445"));
            Assert.That(Yaml.SearchSampleConfig().AutoRun, Is.True, "the console's example is still enabled");
            Assert.That(OpenSearch("Yosemite weekends.yaml").State, Is.EqualTo(ConfigEditState.Editable));
        }

        [Test]
        public void NewSearch_RefusesUnsafeAndUsedNames_WritingNothing()
        {
            Write(In("Zion.yaml"), "displayName: z\n");
            foreach (string Input in new[] { "zion", "Zion.yaml", "../x", "General", "x.yml", "" })
            {
                Assert.That(SearchFiles.Create(Folder, Input, out string? Created).Succeeded, Is.False, Input);
                Assert.That(Created, Is.Null);
            }
            Assert.That(Files(), Is.EqualTo(new[] { "Zion.yaml" }));
            Assert.That(Text(In("Zion.yaml")), Is.EqualTo("displayName: z\n"));
            Assert.That(Directory.GetDirectories(Folder), Is.Empty, "no subfolder");
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(Folder)!, "x.yaml"), Is.Empty, "nothing outside the folder");
        }

        // 39: a file created meanwhile by another program is never written over
        [Test]
        public void NewSearch_NeverWritesOverAConcurrentWinner()
        {
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-create")
                {
                    Write(In("Race.yaml"), "displayName: theirs\n");
                }
            };

            ConfigEditResult Result = SearchFiles.Create(Folder, "Race", out string? Created);

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Created, Is.Null);
            Assert.That(Text(In("Race.yaml")), Is.EqualTo("displayName: theirs\n"));
            Assert.That(Files(), Is.EqualTo(new[] { "Race.yaml" }));
        }

        // 40: Save As writes the edited search to a new file (the reviewed text), with the source's permissions, and leaves
        // the source as it is
        [Test]
        public void SaveAs_WritesTheEditedSearchToANewFile_AndLeavesTheSource()
        {
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(In("Yosemite.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
            var Session = OpenSearch("Yosemite.yaml");
            Session.Set("displayName", "Upper Pines in July");
            Session.Set("autoRun", true);
            string Candidate = Session.Candidate!;

            ConfigEditResult Result = Session.SaveAs("Upper Pines July", out string? Created);

            Assert.That(Result.Succeeded, Is.True, Result.Message);
            Assert.That(Result.Message, Is.EqualTo("Saved as Upper Pines July.yaml. Yosemite.yaml was not changed."));
            Assert.That(Created, Is.EqualTo("Upper Pines July.yaml"));
            Assert.That(Text(In("Upper Pines July.yaml")), Is.EqualTo(Candidate));
            Assert.That(Candidate, Does.Contain("# the trip\n  - 2027-07-02 # start\n"), "comments travel with the search");
            Assert.That(Text(In("Yosemite.yaml")), Is.EqualTo(SearchFileSessionTests.SearchText));
            Assert.That(Files(), Is.EqualTo(new[] { "Upper Pines July.yaml", "Yosemite.yaml" }), "no backup for a new file");
            Assert.That(Session.IsDirty, Is.True, "the source keeps the edits until the caller discards them");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(In("Upper Pines July.yaml")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
            }

            // without edits: an exact copy
            var Clean = OpenSearch("Yosemite.yaml");
            Assert.That(Clean.SaveAs("Copy", out _).Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(In("Copy.yaml")), Is.EqualTo(File.ReadAllBytes(In("Yosemite.yaml"))));
        }

        // 41, 39: Save As never writes over a file: an existing name (also in another case), a concurrent winner
        [Test]
        public void SaveAs_NeverWritesOverAFile()
        {
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            Write(In("Taken.yaml"), "displayName: taken\n");
            var Session = OpenSearch("Yosemite.yaml");
            Session.Set("autoRun", true);

            Assert.That(Session.SaveAs("Taken", out string? Created).Succeeded, Is.False);
            Assert.That(Session.SaveAs("TAKEN.yaml", out Created).Succeeded, Is.False);
            Assert.That(Session.SaveAs("Yosemite", out Created).Succeeded, Is.False, "not over itself");
            Assert.That(Session.SaveAs("../Escape", out Created).Succeeded, Is.False);
            Assert.That(Session.SaveAs("Email", out Created).Succeeded, Is.False);
            Assert.That(Created, Is.Null);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-create")
                {
                    Write(In("Race.yaml"), "displayName: theirs\n");
                }
            };
            Assert.That(Session.SaveAs("Race", out Created).Succeeded, Is.False);
            Assert.That(Text(In("Taken.yaml")), Is.EqualTo("displayName: taken\n"));
            Assert.That(Text(In("Race.yaml")), Is.EqualTo("displayName: theirs\n"));
            Assert.That(Text(In("Yosemite.yaml")), Is.EqualTo(SearchFileSessionTests.SearchText));
            Assert.That(Files(), Is.EqualTo(new[] { "Race.yaml", "Taken.yaml", "Yosemite.yaml" }));
        }

        // a search with blocking problems is not saved under another name either
        [Test]
        public void SaveAs_IsRefused_WithBlockingProblems()
        {
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            var Session = OpenSearch("Yosemite.yaml");
            Session.Set("autoRun", true);
            Session.Set("searchValue", -1);
            Session.Set("searchBy", SearchTypes.DaysOut);
            Assert.That(Session.HasBlockingErrors, Is.True);
            Assert.That(Session.SaveAs("Copy", out _).Succeeded, Is.False);
            Assert.That(Files(), Is.EqualTo(new[] { "Yosemite.yaml" }));
        }

        // 53, 54: whether the console's Search and email mode have a search to run, as the settings folder's validation
        // counts it (enabled and usable); the GUI shows the same. The words follow V011-3: the console's Search creates its
        // example when it is next chosen (email mode stops with an error instead, D65), and a search that is not enabled still runs when it is chosen in the Results tab
        [Test]
        public void TheFolderNotice_FollowsWhatTheConsoleRuns()
        {
            ConfigDiagnostic None = ConfigValidation.SearchFolderDiagnostic(0, false, null)!;
            Assert.That((None.Code, None.Severity), Is.EqualTo((ConfigCode.NoSearchFiles, ConfigSeverity.Info)));
            Assert.That(None.Message, Is.EqualTo("There is no search file; the console's Search creates and runs its example search the next time it is chosen, and email mode stops with an error until there is an enabled search."));
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            var Session = OpenSearch("Yosemite.yaml");
            Assert.That(Session.IsEnabledAndUsable, Is.False, "autoRun: false");
            ConfigDiagnostic Off = ConfigValidation.SearchFolderDiagnostic(1, Session.IsEnabledAndUsable, OutputType.Email)!;
            Assert.That((Off.Code, Off.Severity), Is.EqualTo((ConfigCode.NoEnabledSearch, ConfigSeverity.Warning)));
            Assert.That(Off.Message, Is.EqualTo("No usable search file is enabled (autoRun: true), so email mode stops with an error instead of checking, until a search "
                + "is enabled and email mode is started again. The Results tab still runs a search that is not enabled when you select it."));
            Assert.That(ConfigValidation.ValidateFolder(Folder, Now).Single(D => D.Code == ConfigCode.NoEnabledSearch).Message,
                Is.EqualTo("No usable search file is enabled (autoRun: true), so the console's Search runs nothing. The Results tab still runs a search that is not enabled when you select it."));

            Session.Set("autoRun", true);
            Assert.That(Session.IsEnabledAndUsable, Is.False, "not saved yet: the file on disk counts");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Session.IsEnabledAndUsable, Is.True);
            Assert.That(ConfigValidation.SearchFolderDiagnostic(1, true, OutputType.Email), Is.Null);
            Assert.That(ConfigValidation.ValidateFolder(Folder, Now).Where(D => D.Code == ConfigCode.NoEnabledSearch), Is.Empty);

            // enabled, but with an error: it does not count (as ValidateFolder counts it)
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText.Replace("autoRun: false", "autoRun: true").Replace("  - 2027-07-05\n", "  - 2027-07-02\n"));
            Assert.That(ConfigValidation.IsEnabledAndUsable(In("Yosemite.yaml"), Now, null), Is.False);
        }

        // 54: the console reads the folder as before: a GUI-saved search runs; with none enabled nothing is written or
        // created and nothing runs (the console's Search; email mode then stops at its start, D64, EmailStartTests)
        [Test]
        public void TheConsole_ReadsTheFolderAsBefore()
        {
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            string Before = string.Join("|", Files().Select(N => N + "=" + Text(In(N))));
            List<CampertronConfig> None = Yaml.CampertronConfigGetConfigs(Folder);
            Assert.That(None, Is.Empty);
            Assert.That(string.Join("|", Files().Select(N => N + "=" + Text(In(N)))), Is.EqualTo(Before), "no file written or created");
            Assert.That(Err.ToString(), Does.Contain("No search file has autoRun: true"));

            var Session = OpenSearch("Yosemite.yaml");
            Session.Set("autoRun", true);
            Session.SetItems("searchValueDates", new[] { new ConfigListItem(0, "2027-07-02"), new ConfigListItem(1, "2027-07-04") });
            Assert.That(Session.Save().Succeeded, Is.True);
            CampertronConfig Run = Yaml.CampertronConfigGetConfigs(Folder).Single();
            Assert.That(Run.DisplayName, Is.EqualTo("Upper Pines"));
            Assert.That(Run.GetSearchDates(), Is.EqualTo(new[] { new DateTime(2027, 7, 2), new DateTime(2027, 7, 3), new DateTime(2027, 7, 4) }));
        }

        // V011-3, behaviors 2 and 9 (D63): the console's Search reads the search files each time it searches (ConsoleSearch,
        // before phase 14 Load.Preload, calls CampertronConfigGetConfigs for every Search; email mode calls it once, SearchDatesTests) and runs only the
        // enabled ones, so a change between two searches is used by the next; the example search is created and run only
        // when a search finds no search file at all, never by reading the folder for anything else
        [Test]
        public void TheConsolesSearch_ReadsTheFilesEachTime_AndRunsOnlyTheEnabledOnes()
        {
            Assert.That(ConfigValidation.ValidateFolder(Folder, Now).Select(D => D.Code), Does.Contain(ConfigCode.NoSearchFiles));
            Assert.That(Files(), Is.Empty, "validating a folder without a search file creates none");
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            Write(In("Trip.yaml"), SearchFileSessionTests.SearchText.Replace("Upper Pines", "Trip"));
            Assert.That(Yaml.CampertronConfigGetConfigs(Folder), Is.Empty, "neither is enabled");
            Write(In("Trip.yaml"), SearchFileSessionTests.SearchText.Replace("Upper Pines", "Trip").Replace("autoRun: false", "autoRun: true"));
            Assert.That(Yaml.CampertronConfigGetConfigs(Folder).Select(C => C.DisplayName), Is.EqualTo(new[] { "Trip" }), "the next search reads the change");
            File.Delete(In("Trip.yaml"));
            File.Delete(In("Yosemite.yaml"));
            Assert.That(Yaml.CampertronConfigGetConfigs(Folder).Select(C => C.DisplayName), Is.EqualTo(new[] { "Zion non-group tent friendly sites" }),
                "with no search file, the search creates and runs the example");
            Assert.That(Files(), Is.EqualTo(new[] { "ZionConfig.yaml" }));
        }

        // V011-3: the library's words about autoRun say what the setting does for the console and the Results tab, and
        // none says that a search runs at a start
        [Test]
        public void TheLibrarysAutoRunWords_NeverSayASearchRunsAtAStart()
        {
            Write(In("Yosemite.yaml"), SearchFileSessionTests.SearchText);
            var Words = new List<string>
            {
                ConfigValidation.SearchFolderDiagnostic(0, false, null)!.Message,
                ConfigValidation.SearchFolderDiagnostic(1, false, null)!.Message,
                ConfigValidation.SearchFolderDiagnostic(1, false, OutputType.Email)!.Message,
                ConfigValidation.ValidateSearch(SearchFileSessionTests.SearchText, "Yosemite.yaml", Now).Single(D => D.Code == ConfigCode.SearchDisabled).Message,
                SearchFiles.Create(Folder, "Weekend", out _).Message,
            };
            // the search files' diagnostics (a missing General.yaml or Email.yaml is created at the next start, which is true and
            // not about autoRun)
            Words.AddRange(ConfigValidation.ValidateFolder(Folder, Now).Where(D => D.FileKind == ConfigFileKind.Search).Select(D => D.Message));
            string[] Retired = { "startup", "at start", "next start", "when campertron starts", "does not run", "this search does not run", "until you enable it (autorun)" };
            Assert.That(Words.Where(W => Retired.Any(R => W.Contains(R, StringComparison.OrdinalIgnoreCase))), Is.Empty);
            Assert.That(Words[3], Does.StartWith("Not enabled (autoRun: false): "));
            Assert.That(Words.Skip(1).Take(2), Has.All.StartWith("No usable search file is enabled (autoRun: true), so ")
                .And.All.EndWith(" The Results tab still runs a search that is not enabled when you select it."));
        }
    }
}
