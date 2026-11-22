using System.Diagnostics;
using System.Globalization;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The validation of General.yaml and the search files (ConfigValidation, D41) and the diagnostic model: what the loader
    // refuses (errors, with the same deserializer), what the settings mean (warnings and information), positions, keys and
    // list items, and that no diagnostic holds a value of a file. The run-time loaders refuse only what would otherwise take
    // a dangerous path; everything else loads as before. Temporary folders only.
    public class ConfigValidationTests
    {
        private string Root = string.Empty;
        private CultureInfo OriginalCulture = null!;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private static readonly DateTime Now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Local);

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronConfigValidationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture;
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Directory.Delete(Root, true);
        }

        private static List<(ConfigCode, ConfigSeverity, string?)> Found(List<ConfigDiagnostic> D) => D.Select(X => (X.Code, X.Severity, X.Key)).ToList();

        private string Write(string Name, string Text)
        {
            string FilePath = Path.Join(Root, Name);
            File.WriteAllText(FilePath, Text);
            return FilePath;
        }

        private static Exception Catch(Action Act)
        {
            try
            {
                Act();
            }
            catch (Exception ex)
            {
                return ex;
            }
            Assert.Fail("expected an exception");
            return null!;
        }

        // ---- General.yaml ----

        private const string General = "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-01T00:00:00.0000000Z\nautoRefresh: true\n";

        [Test]
        public void AValidGeneralYaml_AndTheSample_HaveNoDiagnostic()
        {
            Assert.That(ConfigValidation.ValidateGeneral(General, Now), Is.Empty);
            Yaml.GeneralConfigGetConfig(Root);
            Assert.That(ConfigValidation.ValidateFile(Path.Join(Root, "General.yaml"), Now), Is.Empty);
            Assert.That(ConfigValidation.ValidateGeneral("outputTo: HtmlFile\n", Now), Is.Empty, "missing settings take their defaults");
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>("outputTo: HtmlFile\n").RefreshRidbDataDayInterval, Is.EqualTo(0));
        }

        [Test]
        public void AnEmptyOrCommentOnlyOrMalformedGeneralYaml_IsAnError()
        {
            foreach (string Text in new[] { "", "# only a comment\n", "---\n", "~\n" })
            {
                Assert.That(Found(ConfigValidation.ValidateGeneral(Text, Now)), Is.EqualTo(new[] { (ConfigCode.Empty, ConfigSeverity.Error, (string?)null) }), Text);
            }
            ConfigDiagnostic Malformed = ConfigValidation.ValidateGeneral("outputTo: Console\n  bad: indent\n", Now).Single();
            Assert.That((Malformed.Code, Malformed.Severity, Malformed.Line > 0), Is.EqualTo((ConfigCode.Malformed, ConfigSeverity.Error, true)));
            Assert.That(Found(ConfigValidation.ValidateGeneral("- a\n", Now)).Single().Item1, Is.EqualTo(ConfigCode.NotAMapping));
            Assert.That(Found(ConfigValidation.ValidateGeneral("outputTo: Console\n---\noutputTo: Email\n", Now)).Single().Item1, Is.EqualTo(ConfigCode.MultipleDocuments));
            Assert.That(ConfigValidation.ValidateFolder(Root, Now).Where(D => D.FileKind == ConfigFileKind.General).Select(D => (D.Code, D.Severity)),
                Is.EqualTo(new[] { (ConfigCode.FileMissing, ConfigSeverity.Info) }), "a missing General.yaml is created at the next start");
        }

        [Test]
        public void EveryUnknownKey_IsAnError_WithItsPosition_AndTheLoaderStillRejectsTheFile()
        {
            const string Text = "outputTo: Console\ncolour: true\nautoRefresh: false\nsize: 3\n";
            List<ConfigDiagnostic> D = ConfigValidation.ValidateGeneral(Text, Now);
            Assert.That(D.Select(X => (X.Code, X.Key, X.Line, X.Column)), Is.EqualTo(new[] { (ConfigCode.UnknownKey, (string?)"colour", 2, 1), (ConfigCode.UnknownKey, (string?)"size", 4, 1) }));
            Assert.That(D[0].Message, Does.Contain("can't use the file until the key is removed or corrected"));
            Write("General.yaml", Text);
            Assert.That(Catch(() => Yaml.GeneralConfigGetConfig(Root)), Is.InstanceOf<YamlDotNet.Core.YamlException>(), "D20: unknown keys are still refused");
        }

        [Test]
        public void ADuplicateKey_IsAWarning_AndTheLoaderUsesTheLastOne()
        {
            const string Text = "outputTo: Console\noutputTo: Email\n";
            Assert.That(ConfigValidation.ValidateGeneral(Text, Now).Select(X => (X.Code, X.Severity, X.Key, X.Line)),
                Is.EqualTo(new[] { (ConfigCode.DuplicateKey, ConfigSeverity.Warning, (string?)"outputTo", 2) }));
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>(Text).OutputTo, Is.EqualTo(OutputType.Email));
        }

        [Test]
        public void AnInvalidOutputTo_IsAnError_AndAnUndefinedNumberIsRefusedAtStart()
        {
            ConfigDiagnostic Word = ConfigValidation.ValidateGeneral("outputTo: Emial\n", Now).Single();
            Assert.That((Word.Code, Word.Key, Word.Message), Is.EqualTo((ConfigCode.InvalidChoice, "outputTo", "outputTo must be one of Console, Email, HtmlFile or Ntfy.")));
            Assert.That(Found(ConfigValidation.ValidateGeneral("outputTo: 7\n", Now)), Is.EqualTo(new[] { (ConfigCode.InvalidChoice, ConfigSeverity.Error, (string?)"outputTo") }));
            Assert.That(ConfigValidation.ValidateGeneral("outputTo: 1\n", Now), Is.Empty, "a defined number still loads (Email)");

            Write("General.yaml", "outputTo: 7\n");
            Exception Refused = Catch(() => Yaml.GeneralConfigGetConfig(Root));
            Assert.That(Refused, Is.TypeOf<ConfigValidationException>());
            Assert.That(ConsoleUi.DescribeFailure(Refused)[0], Is.EqualTo("General.yaml could not be used: outputTo must be one of Console, Email, HtmlFile or Ntfy."));
        }

        [TestCase(".nan")]
        [TestCase(".inf")]
        [TestCase("-.inf")]
        [TestCase("1e300")]
        [TestCase("-4000000")]
        public void AnUnusableInterval_IsRefusedWithAutoRefresh_AndOnlyReportedWithout(string Interval)
        {
            string On = "outputTo: Console\nrefreshRidbDataDayInterval: " + Interval + "\nlastRidbDataRefresh: 2026-11-01T00:00:00Z\nautoRefresh: true\n";
            Assert.That(Found(ConfigValidation.ValidateGeneral(On, Now)), Is.EqualTo(new[] { (ConfigCode.RefreshIntervalInvalid, ConfigSeverity.Error, (string?)"refreshRidbDataDayInterval") }));
            Write("General.yaml", On);
            Assert.That(Catch(() => Yaml.GeneralConfigGetConfig(Root)), Is.TypeOf<ConfigValidationException>(), "it would stop the start or download the data on every start");

            string Off = On.Replace("autoRefresh: true", "autoRefresh: false");
            Assert.That(Found(ConfigValidation.ValidateGeneral(Off, Now)), Is.EqualTo(new[] { (ConfigCode.RefreshIntervalInvalid, ConfigSeverity.Warning, (string?)"refreshRidbDataDayInterval") }));
            Write("General.yaml", Off);
            Assert.That(Yaml.GeneralConfigGetConfig(Root).AutoRefresh, Is.False, "unused while autoRefresh is false: it still loads");
        }

        [Test]
        public void TheRefreshSettings_AreChecked_AsTheAutomaticRefreshUsesThem()
        {
            Assert.That(Found(ConfigValidation.ValidateGeneral("refreshRidbDataDayInterval: thirty\n", Now)).Single().Item1, Is.EqualTo(ConfigCode.InvalidNumber));
            Assert.That(Found(ConfigValidation.ValidateGeneral("lastRidbDataRefresh: yesterday\n", Now)).Single().Item1, Is.EqualTo(ConfigCode.InvalidDate));
            Assert.That(Found(ConfigValidation.ValidateGeneral("autoRefresh: maybe\n", Now)).Single().Item1, Is.EqualTo(ConfigCode.InvalidBoolean));
            Assert.That(Found(ConfigValidation.ValidateGeneral(General.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: -5"), Now)),
                Is.EqualTo(new[] { (ConfigCode.RefreshEveryStart, ConfigSeverity.Warning, (string?)"refreshRidbDataDayInterval") }));
            Assert.That(Found(ConfigValidation.ValidateGeneral("refreshRidbDataDayInterval: 30\nautoRefresh: true\n", Now)),
                Is.EqualTo(new[] { (ConfigCode.LastRefreshMissing, ConfigSeverity.Warning, (string?)"lastRidbDataRefresh") }));
            Assert.That(Found(ConfigValidation.ValidateGeneral(General.Replace("2026-11-01", "2027-11-01"), Now)),
                Is.EqualTo(new[] { (ConfigCode.LastRefreshInFuture, ConfigSeverity.Warning, (string?)"lastRidbDataRefresh") }));
        }

        [Test]
        public void OutputToUnitTest_StaysLoadable_AndIsMarkedAsTestOnly()
        {
            Assert.That(Found(ConfigValidation.ValidateGeneral("outputTo: UnitTest\n", Now)), Is.EqualTo(new[] { (ConfigCode.TestOutput, ConfigSeverity.Info, (string?)"outputTo") }));
            Write("General.yaml", "outputTo: UnitTest\n");
            Assert.That(Yaml.GeneralConfigGetConfig(Root).OutputTo, Is.EqualTo(OutputType.UnitTest));
        }

        // ---- search files ----

        private const string Search =
            "displayName: Trip\nautoRun: true\ncampgroundID: 232445\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 30\nsearchValueDates: []\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n" +
            "includeEquipment: []\nexcludeEquipment: []\nincludeSites: []\nexcludeSites: []\nincludeAttributes: []\nexcludeAttributes: []\n" +
            "includeCampsiteType: []\nexcludeCampsiteType: []\nconsecutiveDays: 1\n";

        private static string With(string Key, string Value) => Search.Replace(Search.Split('\n').Single(L => L.StartsWith(Key + ":")), Key + ": " + Value);

        private static List<(ConfigCode, ConfigSeverity, string?)> Check(string Text, OutputType? Output = null) =>
            Found(ConfigValidation.ValidateSearch(Text, "Trip.yaml", Now, Output));

        [Test]
        public void AValidSearch_AndTheSample_HaveNoDiagnostic()
        {
            Assert.That(ConfigValidation.ValidateSearch(Search, "Trip.yaml", Now), Is.Empty);
            Yaml.CampertronConfigGetConfigs(Root);
            Assert.That(ConfigValidation.ValidateFile(Path.Join(Root, "ZionConfig.yaml"), Now), Is.Empty);
            Assert.That(Check("displayName: Trip\nautoRun: true\ncampgroundID: '1'\ntotalHumans: 1\nsearchValue: 3\nshowSunday: true\nconsecutiveDays: 1\n"), Is.Empty,
                "missing optional lists are fine");
        }

        // a search that is not enabled (autoRun: false): its SearchDisabled information says what that means (V011-3), and
        // its problems are warnings, because the console never runs it (the Results tab checks it as an enabled search)
        [Test]
        public void ADisabledSearch_IsInformation_AndItsProblemsAreOnlyWarnings()
        {
            string Disabled = With("autoRun", "false").Replace("searchValue: 30", "searchValue: -5");
            Assert.That(Check(Disabled), Is.EqualTo(new[] { (ConfigCode.SearchDisabled, ConfigSeverity.Info, (string?)"autoRun"), (ConfigCode.SearchValueNegative, ConfigSeverity.Warning, (string?)"searchValue") }));
            Assert.That(ConfigValidation.ValidateSearch(Disabled, "Trip.yaml", Now).First().Message, Is.EqualTo("Not enabled (autoRun: false): the console's Search and email mode skip this "
                + "search. The Results tab runs it only when you select it, and checks it there as an enabled search, so some warnings shown here are errors there."));
            Write("Trip.yaml", Disabled);
            Assert.That(Yaml.CampertronConfigGetConfigs(Root), Is.Empty, "the console never runs a search that is not enabled, so it does not refuse it");
        }

        [Test]
        public void TheSearchsOwnSettings_AreWarnings_WhereTheSearchStillRuns()
        {
            Assert.That(Check(With("campgroundID", "''")), Is.EqualTo(new[] { (ConfigCode.CampgroundIdMissing, ConfigSeverity.Warning, (string?)"campgroundID") }));
            Assert.That(Check(With("campgroundID", "Zion")), Is.EqualTo(new[] { (ConfigCode.CampgroundIdNotNumeric, ConfigSeverity.Warning, (string?)"campgroundID") }));
            Assert.That(Check(With("totalHumans", "")), Is.EqualTo(new[] { (ConfigCode.TotalHumansMissing, ConfigSeverity.Warning, (string?)"totalHumans") }));
            Assert.That(Check(With("totalHumans", "0")), Is.EqualTo(new[] { (ConfigCode.TotalHumansMissing, ConfigSeverity.Warning, (string?)"totalHumans") }));
            Assert.That(Check(With("displayName", "''")), Is.EqualTo(new[] { (ConfigCode.DisplayNameMissing, ConfigSeverity.Warning, (string?)"displayName") }));
            string NoDays = Search;
            foreach (string Day in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })
            {
                NoDays = NoDays.Replace("show" + Day + ": true", "show" + Day + ": false");
            }
            Assert.That(Check(NoDays), Is.EqualTo(new[] { (ConfigCode.NoWeekdays, ConfigSeverity.Warning, (string?)null) }));
            Assert.That(Check(With("consecutiveDays", "0")), Is.EqualTo(new[] { (ConfigCode.ConsecutiveDaysBelowOne, ConfigSeverity.Warning, (string?)"consecutiveDays") }));
            Assert.That(ConfigValidation.ValidateSearch(With("consecutiveDays", "0"), "Trip.yaml", Now, OutputType.Email).Single().Message,
                Does.Contain("recorded as reported although no email shows them"));
            Assert.That(ConfigValidation.ValidateSearch(With("consecutiveDays", "0"), "Trip.yaml", Now, OutputType.Ntfy).Single().Message,
                Is.EqualTo("consecutiveDays is below 1: no result is shown, and with outputTo: Ntfy the openings it finds are recorded as reported although no notification shows them."),
                "a check that no provider failed records the run's other hits too (Load.ReportResults; NotificationDeliveryTests' remainder)");
            Assert.That(Check(With("searchValue", "0")), Is.EqualTo(new[] { (ConfigCode.SearchValueZero, ConfigSeverity.Warning, (string?)"searchValue") }));
            Assert.That(Check(With("searchValue", "400")), Is.EqualTo(new[] { (ConfigCode.SearchBeyondTwelveMonths, ConfigSeverity.Warning, (string?)"searchValue") }));
            Write("Trip.yaml", With("searchValue", "0"));
            Assert.That(Yaml.CampertronConfigGetConfigs(Root), Has.Count.EqualTo(1), "a warning never stops a search");
        }

        [Test]
        public void AnInvalidSearchBy_IsAnError_AndAnUndefinedNumberIsRefused()
        {
            Assert.That(Check(With("searchBy", "Weekly")), Is.EqualTo(new[] { (ConfigCode.InvalidChoice, ConfigSeverity.Error, (string?)"searchBy") }));
            Assert.That(Check(With("searchBy", "9")), Is.EqualTo(new[] { (ConfigCode.InvalidChoice, ConfigSeverity.Error, (string?)"searchBy") }));
            Write("Trip.yaml", With("searchBy", "9"));
            Assert.That(Catch(() => Yaml.CampertronConfigGetConfigs(Root)), Is.TypeOf<ConfigValidationException>());
        }

        [TestCase("DaysOut", "-1", "SearchValueNegative")]
        [TestCase("MonthsOut", "-1", "SearchValueNegative")]
        [TestCase("DaysOut", "3000000", "SearchWindowOutOfRange")]
        [TestCase("MonthsOut", "200000", "SearchWindowOutOfRange")]
        public void ASearchWindowThatRunsTowardTheEndOfTheCalendar_IsRefusedBeforeItsDates(string SearchBy, string Value, string Code)
        {
            string Text = With("searchBy", SearchBy).Replace("searchValue: 30", "searchValue: " + Value);
            Assert.That(Check(Text), Is.EqualTo(new[] { (Enum.Parse<ConfigCode>(Code), ConfigSeverity.Error, (string?)"searchValue") }));
            string FilePath = Write("Trip.yaml", Text);
            var Clock = Stopwatch.StartNew();
            Exception Refused = Catch(() => Yaml.CampertronConfigGetConfigs(Root));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "refused before the dates are generated (a negative value looped about 2.9 million days)");
            Assert.That(Refused, Is.TypeOf<ConfigValidationException>());
            List<string> Lines = ConsoleUi.DescribeFailure(Refused);
            Assert.That(Lines[0], Does.StartWith("Trip.yaml could not be used: searchValue is "));
            Assert.That(Lines[1], Is.EqualTo("File: " + FilePath));
        }

        [Test]
        public void StartEndDate_NeedsTwoUsableDatesInOrder()
        {
            string Dates(string List) => With("searchBy", "StartEndDate").Replace("searchValueDates: []", "searchValueDates: " + List);
            Assert.That(Check(Dates("[]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesTooFew, ConfigSeverity.Error, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-01]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesTooFew, ConfigSeverity.Error, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[~]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-01, 2026-12-05, 2026-12-09]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesExtra, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-05, 2026-12-01]")), Is.EqualTo(new[] { (ConfigCode.DatesReversed, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-05, 2026-12-05]")), Is.EqualTo(new[] { (ConfigCode.DatesEqual, ConfigSeverity.Error, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-01, soon]")).Single(), Is.EqualTo((ConfigCode.DateInvalid, ConfigSeverity.Error, (string?)"searchValueDates")));
            Assert.That(ConfigValidation.ValidateSearch(Dates("[2026-12-01, soon]"), "Trip.yaml", Now).Single().Index, Is.EqualTo(1));
            Assert.That(Check(Dates("[2026-01-01, 2026-01-05]")), Is.EqualTo(new[] { (ConfigCode.DateInPast, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-01, 2028-01-05]")), Is.EqualTo(new[] { (ConfigCode.SearchBeyondTwelveMonths, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Dates("[2026-12-01, 2026-12-05]")), Is.Empty);
        }

        [Test]
        public void Until_NeedsItsEndDate_AndSpecificDatesIgnoresUnreadableOnes()
        {
            string Until(string List) => With("searchBy", "Until").Replace("searchValueDates: []", "searchValueDates: " + List);
            Assert.That(Check(Until("[]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesTooFew, ConfigSeverity.Error, (string?)"searchValueDates") }));
            Assert.That(Check(Until("~")), Is.EqualTo(new[] { (ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Until("[nope]")), Is.EqualTo(new[] { (ConfigCode.DateInvalid, ConfigSeverity.Error, (string?)"searchValueDates") }));
            Assert.That(Check(Until("[2026-01-01]")), Is.EqualTo(new[] { (ConfigCode.DateInPast, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Until("[2026-12-20, 2026-12-30]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesExtra, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Until("[2026-12-20]")), Is.Empty);

            string Specific(string List) => With("searchBy", "SpecificDates").Replace("searchValueDates: []", "searchValueDates: " + List);
            Assert.That(Check(Specific("[2026-12-20, soon]")), Is.EqualTo(new[] { (ConfigCode.DateInvalid, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Specific("[]")), Is.EqualTo(new[] { (ConfigCode.SearchDatesMissing, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            Assert.That(Check(Specific("[2026-12-20, 2026-12-21]")), Is.Empty);
        }

        [Test]
        public void ADateThatDependsOnTheCulture_IsAWarning()
        {
            string Specific(string List) => With("searchBy", "SpecificDates").Replace("searchValueDates: []", "searchValueDates: " + List);
            Assert.That(Check(Specific("['11/20/2026']")), Is.EqualTo(new[] { (ConfigCode.DateNotIso, ConfigSeverity.Warning, (string?)"searchValueDates") }));
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.That(Check(Specific("['11/20/2026']")), Is.EqualTo(new[] { (ConfigCode.DateInvalid, ConfigSeverity.Warning, (string?)"searchValueDates") }), "the same text is no date in en-GB");
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            Assert.That(Check(Specific("[2026-12-20]")), Is.EqualTo(new[] { (ConfigCode.DateCultureDependent, ConfigSeverity.Warning, (string?)"searchValueDates") }),
                "th-TH reads it with the Thai Buddhist calendar");
        }

        [Test]
        public void AnEmptyFilterItem_ThatWouldStopTheSearch_IsRefused_AndOtherEmptyItemsAreWarnings()
        {
            string Text = With("includeEquipment", "[~, Tent]");
            List<ConfigDiagnostic> D = ConfigValidation.ValidateSearch(Text, "Trip.yaml", Now);
            Assert.That(D.Select(X => (X.Code, X.Severity, X.Key, X.Index)), Is.EqualTo(new[] { (ConfigCode.ListItemNull, ConfigSeverity.Error, (string?)"includeEquipment", (int?)0) }));
            Write("Trip.yaml", Text);
            Assert.That(Catch(() => Yaml.CampertronConfigGetConfigs(Root)), Is.TypeOf<ConfigValidationException>(), "FilterData would throw on it during the search");

            Assert.That(Check(With("includeSites", "[~]")), Is.EqualTo(new[] { (ConfigCode.ListItemNull, ConfigSeverity.Warning, (string?)"includeSites") }));
            Assert.That(Check(With("excludeCampsiteType", "['  ']")), Is.EqualTo(new[] { (ConfigCode.ListItemEmpty, ConfigSeverity.Warning, (string?)"excludeCampsiteType") }));
            Assert.That(Check(With("includeEquipment", "~")), Is.Empty, "a missing (null) list is no filter");
        }

        [Test]
        public void UnknownAndMisshapenKeys_AreErrors_AtTheTopLevelAndNested()
        {
            Assert.That(Check(Search + "extra:\n  inner: 1\n"), Is.EqualTo(new[] { (ConfigCode.UnknownKey, ConfigSeverity.Error, (string?)"extra") }));
            Assert.That(Check(With("includeEquipment", "\n  tent: yes")), Is.EqualTo(new[] { (ConfigCode.NotAList, ConfigSeverity.Error, (string?)"includeEquipment") }));
            Assert.That(Check(With("displayName", "[a, b]")), Is.EqualTo(new[] { (ConfigCode.NotAScalar, ConfigSeverity.Error, (string?)"displayName") }));
            Assert.That(Check(With("totalHumans", "two")), Is.EqualTo(new[] { (ConfigCode.InvalidNumber, ConfigSeverity.Error, (string?)"totalHumans") }));
            Assert.That(Check(Search + "displayName: Again\n").Single(), Is.EqualTo((ConfigCode.DuplicateKey, ConfigSeverity.Warning, (string?)"displayName")));
            Assert.That(Check("searchby: DaysOut\nshowsunday: true\n").Select(X => (X.Item1, X.Item3)),
                Is.EqualTo(new[] { (ConfigCode.UnknownKey, (string?)"searchby"), (ConfigCode.UnknownKey, (string?)"showsunday") }), "keys are case-sensitive, as the loader reads them");
        }

        [Test]
        public void TheSettingsFolder_IsValidatedFileByFile_AndANoEnabledSearchIsReported()
        {
            Write("General.yaml", "outputTo: Email\n");
            Write("Email.yaml", "smtpServer: smtp.example.org\nsmtpPort: 587\nsmtpUsername: u\nsmtpPassword: p\nsendToAddressList: [a@example.org]\nsendFromAddress: b@example.org\n");
            Write("One.yaml", With("autoRun", "false").Replace("searchValue: 30", "searchValue: -1"));
            Write("Two.yaml", With("autoRun", "false") + "colour: red\n");
            List<ConfigDiagnostic> D = ConfigValidation.ValidateFolder(Root, Now);
            Assert.That(D.Select(X => (X.FileName, X.Code)), Is.EquivalentTo(new[]
            {
                ("One.yaml", ConfigCode.SearchDisabled), ("One.yaml", ConfigCode.SearchValueNegative), ("Two.yaml", ConfigCode.UnknownKey), ("*.yaml", ConfigCode.NoEnabledSearch)
            }));
            Assert.That(D.Single(X => X.Code == ConfigCode.NoEnabledSearch).Message, Does.Contain("email mode stops with an error instead of checking"), "D64");

            File.Delete(Path.Join(Root, "One.yaml"));
            File.Delete(Path.Join(Root, "Two.yaml"));
            Assert.That(ConfigValidation.ValidateFolder(Root, Now).Select(X => (X.Code, X.Severity)), Is.EqualTo(new[] { (ConfigCode.NoSearchFiles, ConfigSeverity.Info) }));
        }

        // ---- the diagnostics themselves ----

        [Test]
        public void TheCodes_AreTheDocumentedOnes()
        {
            // business-rules.md §15 documents each code; a renamed or removed code changes what a caller sees
            Assert.That(Enum.GetNames<ConfigCode>(), Is.EqualTo(new[]
            {
                "FileMissing", "FileTooLarge", "TooManyEvents", "TooDeep", "TooManyAliases", "NotUtf8", "InvalidUtf8", "Empty", "Malformed", "MultipleDocuments",
                "NotAMapping", "DuplicateKey", "UnknownKey", "InvalidChoice", "InvalidNumber", "InvalidBoolean", "InvalidDate", "NotAList", "NotAScalar", "NotLoadable",
                "TestOutput", "RefreshIntervalInvalid", "RefreshEveryStart", "LastRefreshMissing", "LastRefreshInFuture",
                "SmtpServerMissing", "SmtpPortOutOfRange", "SenderMissing", "SenderInvalid", "RecipientsMissing", "RecipientInvalid", "CredentialsMissing", "RecipientEmpty", "RecipientDuplicate",
                "PasswordFilePathEmpty", "PasswordFilePathNotAbsolute", "PasswordFileSet", "PasswordNotUsed",
                "NtfyDisabled", "NtfyServerMissing", "NtfyServerInvalid", "NtfyServerNotHttps", "NtfyTopicMissing", "NtfyTopicInvalid", "NtfyTagInvalid", "NtfyTagsTooLong",
                "NtfyTimeoutOutOfRange", "NtfyTokenFilePathEmpty", "NtfyTokenFilePathNotAbsolute", "NtfyTokenFileSet", "NtfyTagEmpty",
                "SearchDisabled", "DisplayNameMissing", "CampgroundIdMissing", "CampgroundIdNotNumeric", "TotalHumansMissing", "SearchValueNegative", "SearchValueZero",
                "SearchWindowOutOfRange", "SearchBeyondTwelveMonths", "SearchDatesMissing", "SearchDatesTooFew", "SearchDatesExtra", "DateInvalid", "DateNotIso",
                "DateCultureDependent", "DatesEqual", "DatesReversed", "DateInPast", "NoWeekdays", "ConsecutiveDaysBelowOne", "ListItemNull", "ListItemEmpty",
                "FilterMatchingInvalid", "NoSearchFiles", "NoEnabledSearch", "MonitoringNoSearch", "MonitoringSearchRefused"
            }));
            Assert.That(Enum.GetNames<ConfigSeverity>(), Is.EqualTo(new[] { "Info", "Warning", "Error" }));
        }

        [Test]
        public void ADiagnostic_NamesTheFileByName_TheKey_TheItem_AndThePosition()
        {
            string FilePath = Write("Trip.yaml", With("includeEquipment", "\n- Tent\n- ~"));
            ConfigDiagnostic D = ConfigValidation.ValidateFile(FilePath, Now).Single();
            Assert.That((D.FileName, D.FileKind, D.Key, D.Index, D.Line, D.Column), Is.EqualTo(("Trip.yaml", ConfigFileKind.Search, (string?)"includeEquipment", (int?)1, 17, 3)));
            Assert.That(D.ToString(), Is.EqualTo("Error ListItemNull Trip.yaml includeEquipment[1] (line 17, column 3): includeEquipment item 2 is empty, which stops the search when a campsite is checked."));
            Assert.That(D.ToString(), Does.Not.Contain(Root), "never the folder");
        }

        [Test]
        public void NoDiagnostic_HoldsAValueOfTheFile()
        {
            // every value (and anchor) holds the sentinel; key names are not values and are named
            const string S = "V4lueS3cr3t";
            string[] Searches =
            {
                With("displayName", S + "-name").Replace("campgroundID: 232445", "campgroundID: " + S),
                With("searchBy", "StartEndDate").Replace("searchValueDates: []", "searchValueDates: [" + S + "-a, " + S + "-b]"),
                With("searchBy", "Until").Replace("searchValueDates: []", "searchValueDates: [" + S + "]"),
                With("searchBy", "SpecificDates").Replace("searchValueDates: []", "searchValueDates: [" + S + ", '11/20/2026']"),
                With("searchBy", S),
                With("totalHumans", S),
                With("consecutiveDays", S),
                With("autoRun", S),
                With("includeEquipment", "[~, " + S + "]") + "unknownKey: " + S + "\n",
                "displayName: &" + S + " [" + S + "]\nincludeEquipment: [*" + S + ", [" + S + "]]\n",
                "displayName: '" + S + "\n",
                S + "\n",
                "- " + S + "\n"
            };
            var All = new List<ConfigDiagnostic>();
            foreach (string Text in Searches)
            {
                All.AddRange(ConfigValidation.ValidateSearch(Text, "Trip.yaml", Now, OutputType.Email));
            }
            All.AddRange(ConfigValidation.ValidateGeneral("outputTo: " + S + "\nlastRidbDataRefresh: " + S + "\nrefreshRidbDataDayInterval: " + S + "\n", Now));
            All.AddRange(ConfigValidation.ValidateGeneral("unknownKey: " + S + "\noutputTo: [" + S + "\n", Now));
            All.AddRange(ConfigValidation.ValidateGeneral("outputTo: Console\n---\n" + S + ": " + S + "\n", Now));
            Assert.That(All, Has.Count.GreaterThan(15));
            foreach (ConfigDiagnostic D in All)
            {
                Assert.That(D.Message + " " + D.Key + " " + D + " " + D.FileName, Does.Not.Contain(S), D.Code.ToString());
            }
        }
    }
}
