using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // A search's filterMatching (decision D51, business-rules §1, §3 and §15): written Exact or Contains, missing in every
    // file written before D51 (Contains), Exact in a new search; anything else is refused instead of guessed (YamlDotNet
    // would read an empty value as none, a number as a choice, " Exact " as Exact and "Exact, Contains" as their
    // combination). Editing adds or changes it only when the user sets it. Temporary folders only (ConfigEditTestBase).
    public class FilterMatchingTests : ConfigEditTestBase
    {
        private static string SearchWith(string? Line) => SearchFileSessionTests.SearchText.Replace("showSunday: false\n", "showSunday: false\n" + (Line == null ? "" : Line + "\n"));

        private static List<ConfigDiagnostic> Matching(string Text) =>
            ConfigValidation.ValidateSearch(Text, "Trip.yaml", Now).Where(D => D.Key == "filterMatching").ToList();

        [TestCase("Exact", FilterMatchMode.Exact)]
        [TestCase("Contains", FilterMatchMode.Contains)]
        [TestCase("exact", FilterMatchMode.Exact)]
        [TestCase("CONTAINS", FilterMatchMode.Contains)]
        [TestCase("'Exact'", FilterMatchMode.Exact)]
        [TestCase("\"Contains\"", FilterMatchMode.Contains)]
        [TestCase("Exact   # the whole value", FilterMatchMode.Exact)]
        public void TheNames_InAnyCase_AreAccepted(string Written, FilterMatchMode Expected)
        {
            string Text = SearchWith("filterMatching: " + Written);
            Assert.That(Matching(Text), Is.Empty);
            CampertronConfig Search = Yaml.LoadSettingsText<CampertronConfig>(Text);
            Assert.That(Search.FilterMatching, Is.EqualTo(Expected));
            Assert.That(Search.EffectiveFilterMatching, Is.EqualTo(Expected));
            Assert.That(ConfigValidation.CheckSearchText(Text, "Trip.yaml"), Is.Empty);
        }

        [Test]
        public void AFileWithoutIt_UsesContains_WithoutADiagnostic()
        {
            string Text = SearchWith(null);
            Assert.That(Text, Does.Not.Contain("filterMatching"));
            Assert.That(Matching(Text), Is.Empty);
            CampertronConfig Search = Yaml.LoadSettingsText<CampertronConfig>(Text);
            Assert.That(Search.FilterMatching, Is.Null);
            Assert.That(Search.EffectiveFilterMatching, Is.EqualTo(FilterMatchMode.Contains));
        }

        // what YamlDotNet would read as a choice anyway, and what it refuses itself
        [TestCase("", null, "FilterMatchingInvalid")]
        [TestCase("~", null, "FilterMatchingInvalid")]
        [TestCase("null", null, "FilterMatchingInvalid")]
        [TestCase("0", FilterMatchMode.Exact, "FilterMatchingInvalid")]
        [TestCase("1", FilterMatchMode.Contains, "FilterMatchingInvalid")]
        [TestCase("7", (FilterMatchMode)7, "FilterMatchingInvalid")]
        [TestCase("-1", (FilterMatchMode)(-1), "FilterMatchingInvalid")]
        [TestCase("' Exact'", FilterMatchMode.Exact, "FilterMatchingInvalid")]
        [TestCase("'Contains '", FilterMatchMode.Contains, "FilterMatchingInvalid")]
        [TestCase("'Exact, Contains'", FilterMatchMode.Contains, "FilterMatchingInvalid")]
        public void OtherSpellings_AreAnError_NeverAGuess(string Written, FilterMatchMode? LoaderReads, string CodeName)
        {
            ConfigCode Code = Enum.Parse<ConfigCode>(CodeName);
            string Text = SearchWith("filterMatching: " + Written);
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text).FilterMatching, Is.EqualTo(LoaderReads), "what YamlDotNet alone would use");
            ConfigDiagnostic D = Matching(Text).Single();
            Assert.That((D.Code, D.Severity, D.Line), Is.EqualTo((Code, ConfigSeverity.Error, Text.Split('\n').ToList().FindIndex(L => L.StartsWith("filterMatching")) + 1)));
            Assert.That(D.Message, Is.EqualTo("filterMatching must be Exact or Contains; a search file without filterMatching uses Contains."));
            Assert.That(ConfigValidation.CheckSearchText(Text, "Trip.yaml").Select(X => X.Code), Is.EqualTo(new[] { ConfigCode.FilterMatchingInvalid }));
        }

        [TestCase("''", "InvalidChoice")]
        [TestCase("Fuzzy", "InvalidChoice")]
        [TestCase("[Exact]", "NotAScalar")]
        public void WhatTheLoaderRefuses_IsItsOwnError(string Written, string CodeName)
        {
            ConfigCode Code = Enum.Parse<ConfigCode>(CodeName);
            string Text = SearchWith("filterMatching: " + Written);
            Assert.That(() => Yaml.LoadSettingsText<CampertronConfig>(Text), Throws.InstanceOf<YamlDotNet.Core.YamlException>());
            ConfigDiagnostic D = Matching(Text).Single();
            Assert.That((D.Code, D.Severity), Is.EqualTo((Code, ConfigSeverity.Error)));
        }

        [Test]
        public void TwiceIsAnError_EvenWithTheSameValue()
        {
            foreach (string Second in new[] { "Exact", "Contains" })
            {
                string Text = SearchWith("filterMatching: Exact\nfilterMatching: " + Second);
                List<ConfigDiagnostic> Found = ConfigValidation.ValidateSearch(Text, "Trip.yaml", Now).Where(D => D.Key == "filterMatching").ToList();
                Assert.That(Found.Select(D => (D.Code, D.Severity)), Is.EquivalentTo(new[]
                {
                    (ConfigCode.DuplicateKey, ConfigSeverity.Warning),
                    (ConfigCode.FilterMatchingInvalid, ConfigSeverity.Error)
                }), Second);
                Assert.That(Found.Single(D => D.Code == ConfigCode.FilterMatchingInvalid).Message, Does.Contain("more than once"));
            }
        }

        [Test]
        public void ItIsAnError_AlsoInADisabledSearch()
        {
            // a value that is not a choice is an error like the loader's own, whatever autoRun says
            string Text = SearchWith("filterMatching: 1");
            Assert.That(Text, Does.Contain("autoRun: false"));
            Assert.That(Matching(Text).Single().Severity, Is.EqualTo(ConfigSeverity.Error));
        }

        [Test]
        public void AStart_RefusesItInAnEnabledSearch_AndIgnoresADisabledOne()
        {
            File.WriteAllText(Path.Join(Folder, "Off.yaml"), SearchWith("filterMatching:"));
            Assert.That(Yaml.CampertronConfigGetConfigs(Folder), Is.Empty, "a disabled search is not loaded, as before");

            File.WriteAllText(Path.Join(Folder, "On.yaml"), SearchWith("filterMatching: 'Exact, Contains'").Replace("autoRun: false", "autoRun: true"));
            var Refused = Assert.Throws<ConfigValidationException>(() => Yaml.CampertronConfigGetConfigs(Folder));
            Assert.That(Refused!.Message, Is.EqualTo("filterMatching must be Exact or Contains; a search file without filterMatching uses Contains."));
            Assert.That(Refused.Diagnostics.Single().FileName, Is.EqualTo("On.yaml"));

            File.WriteAllText(Path.Join(Folder, "On.yaml"), SearchWith("filterMatching: Exact").Replace("autoRun: false", "autoRun: true"));
            CampertronConfig Loaded = Yaml.CampertronConfigGetConfigs(Folder).Single();
            Assert.That(Loaded.EffectiveFilterMatching, Is.EqualTo(FilterMatchMode.Exact));
            File.WriteAllText(Path.Join(Folder, "On.yaml"), SearchWith(null).Replace("autoRun: false", "autoRun: true"));
            Assert.That(Yaml.CampertronConfigGetConfigs(Folder).Single().EffectiveFilterMatching, Is.EqualTo(FilterMatchMode.Contains));
        }

        [Test]
        public void SettingsWithoutText_WithANumberThatIsNoChoice_AreRefused()
        {
            var Search = new CampertronConfig { AutoRun = true, FilterMatching = (FilterMatchMode)5 };
            Assert.That(ConfigValidation.CheckSearch(Search, "Trip.yaml", Now).Single(D => D.Key == "filterMatching").Code, Is.EqualTo(ConfigCode.FilterMatchingInvalid));
            Assert.That(() => ConfigValidation.ThrowIfRefused(ConfigValidation.CheckSearch(Search, "Trip.yaml", Now)), Throws.InstanceOf<ConfigValidationException>());
        }

        // ---- new files and editing ----

        [Test]
        public void ANewSearch_IsWrittenWithExact()
        {
            Assert.That(SearchFiles.Create(Folder, "Weekend", out string? Name).Succeeded, Is.True);
            string Text = File.ReadAllText(Path.Join(Folder, Name!));
            Assert.That(Text, Does.Contain("\nfilterMatching: Exact\n"));
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Text).FilterMatching, Is.EqualTo(FilterMatchMode.Exact));
            Assert.That(Matching(Text), Is.Empty);
            Assert.That(Yaml.SearchSampleConfig().FilterMatching, Is.EqualTo(FilterMatchMode.Exact));
        }

        [Test]
        public void AnUnrelatedEdit_NeverAddsIt()
        {
            string Path_ = Path.Join(Folder, "Yosemite.yaml");
            File.WriteAllText(Path_, SearchFileSessionTests.SearchText);
            var Session = new ConfigEditSession<CampertronConfig>(Folder, "Yosemite.yaml", () => Now, null);
            Assert.That(Session.Value("filterMatching"), Is.Null, "not in the file");

            Session.Set("displayName", "Upper Pines weekend");
            Session.SetItems("includeEquipment", new[] { new ConfigListItem(0, "Tent"), new ConfigListItem(1, "RV"), new ConfigListItem(null, "Trailer") });
            Assert.That(Session.Candidate, Does.Not.Contain("filterMatching"));
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(Path_), Does.Not.Contain("filterMatching"));
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(File.ReadAllText(Path_)).EffectiveFilterMatching, Is.EqualTo(FilterMatchMode.Contains));
        }

        [Test]
        public void SettingIt_AddsOneLine_AndKeepsEverythingElse()
        {
            string Path_ = Path.Join(Folder, "Yosemite.yaml");
            File.WriteAllText(Path_, SearchFileSessionTests.SearchText);
            var Session = new ConfigEditSession<CampertronConfig>(Folder, "Yosemite.yaml", () => Now, null);

            Session.Set("filterMatching", FilterMatchMode.Exact);
            Assert.That(Session.Candidate, Is.EqualTo(SearchFileSessionTests.SearchText.Replace("consecutiveDays: 1\n", "consecutiveDays: 1\nfilterMatching: Exact\n")));
            Session.Set("filterMatching", null);
            Assert.That(Session.IsDirty, Is.False, "back to the file's value: no edit");
            Session.Set("filterMatching", FilterMatchMode.Contains);
            Assert.That(Session.IsDirty, Is.True, "Contains written out is a deliberate choice");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(Path_), Is.EqualTo(SearchFileSessionTests.SearchText.Replace("consecutiveDays: 1\n", "consecutiveDays: 1\nfilterMatching: Contains\n")));
            Assert.That(File.ReadAllText(Path_ + ".bak"), Is.EqualTo(SearchFileSessionTests.SearchText));
        }

        [Test]
        public void ChangingIt_ReplacesOnlyItsValue()
        {
            string Path_ = Path.Join(Folder, "Yosemite.yaml");
            string Text = SearchWith("filterMatching: 'Contains'   # how the lists match");
            File.WriteAllText(Path_, Text);
            var Session = new ConfigEditSession<CampertronConfig>(Folder, "Yosemite.yaml", () => Now, null);
            Assert.That(Session.Value("filterMatching"), Is.EqualTo(FilterMatchMode.Contains));

            Session.Set("filterMatching", FilterMatchMode.Exact);
            Assert.That(Session.Candidate, Is.EqualTo(Text.Replace("filterMatching: 'Contains'", "filterMatching: 'Exact'")), "quoting and the comment kept");
        }
    }
}
