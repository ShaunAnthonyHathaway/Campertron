using System.Reflection;
using CampertronLibrary.function.Base;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using static CampertronUnitTests.function.Base.YamlDocumentTesting;

namespace CampertronUnitTests.function.Base
{
    // The YAML document editor (YamlDocument, decision D39): each edit, and the check of every result with Campertron's
    // own settings deserializer against the model the caller intends. Everything here is in memory (one test writes the
    // runtime writer's output into its own temporary folder to compare value text): no settings or cache folder is used.
    public class YamlDocumentTests
    {
        // lines 0-11; the list has a comment above its first item, an end-of-line comment and a blank line between items
        internal const string Email =
            "# mail server\n" +
            "smtpServer: 'smtp.example.com'\n" +
            "smtpPort: 587 # submission\n" +
            "smtpUsername: \"camper\"\n" +
            "smtpPassword: 'S3cr3t-Pa55'   # app password\n" +
            "sendToAddressList:\n" +
            "  # me first\n" +
            "  - me@example.com   # main\n" +
            "  - you@example.com\n" +
            "\n" +
            "  - them@example.com\n" +
            "sendFromAddress: camper@example.com\n";

        internal const string Search =
            "displayName: Watchman # name\n" +
            "autoRun: true\n" +
            "campgroundID: '232445'\n" +
            "searchBy: DaysOut\n" +
            "searchValue: 30\n" +
            "includeEquipment: [Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"] # tents\n" +
            "excludeEquipment: []\n";

        // ---- scalars ----

        [Test]
        public void SetScalar_ChangesOnlyTheValue_AndKeepsTheSpaceBeforeItsComment()
        {
            string Result = Succeeds(Edit<EmailConfig>(Email, M => M.SmtpPort = 25, YamlDocumentEdit.SetScalar("smtpPort", 25)));

            Assert.That(Result, Is.EqualTo(Email.Replace("smtpPort: 587 # submission", "smtpPort: 25 # submission")));
        }

        [Test]
        public void SetScalar_SeveralKeys_KeepEachValuesQuoting()
        {
            string Result = Succeeds(Edit<EmailConfig>(Email, M =>
                {
                    M.SmtpServer = "mail.example.org";
                    M.SmtpUsername = "o'brien";
                    M.SendFromAddress = "noreply@example.org";
                },
                YamlDocumentEdit.SetScalar("smtpServer", "mail.example.org"),
                YamlDocumentEdit.SetScalar("smtpUsername", "o'brien"),
                YamlDocumentEdit.SetScalar("sendFromAddress", "noreply@example.org")));

            Assert.That(Result, Is.EqualTo(Email
                .Replace("smtpServer: 'smtp.example.com'", "smtpServer: 'mail.example.org'")
                .Replace("smtpUsername: \"camper\"", "smtpUsername: \"o'brien\"")
                .Replace("sendFromAddress: camper@example.com", "sendFromAddress: noreply@example.org")));
        }

        [Test]
        public void SetScalar_MissingKey_IsInsertedAfterTheLastProperty_BeforeATrailingComment()
        {
            const string Text = "smtpServer: x\nsmtpPort: 25\n# end of file\n";

            string Result = Succeeds(Edit<EmailConfig>(Text, M => M.SmtpUsername = "camper", YamlDocumentEdit.SetScalar("smtpUsername", "camper")));

            Assert.That(Result, Is.EqualTo("smtpServer: x\nsmtpPort: 25\nsmtpUsername: camper\n# end of file\n"));
        }

        [Test]
        public void SetScalar_SeveralMissingKeys_AreInsertedInTheOrderOfTheEdits()
        {
            const string Text = "outputTo: Console\n";

            string Result = Succeeds(Edit<GeneralConfig>(Text, M =>
                {
                    M.AutoRefresh = true;
                    M.RefreshRidbDataDayInterval = 12.5;
                },
                YamlDocumentEdit.SetScalar("autoRefresh", true),
                YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", 12.5)));

            Assert.That(Result, Is.EqualTo("outputTo: Console\nautoRefresh: true\nrefreshRidbDataDayInterval: 12.5\n"));
        }

        [Test]
        public void TypedValues_AreWrittenAsCampertronsWriterWritesThem()
        {
            string Folder = Path.Join(Path.GetTempPath(), "CampertronYamlDocumentTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            try
            {
                var Model = new GeneralConfig
                {
                    OutputTo = OutputType.HtmlFile,
                    RefreshRidbDataDayInterval = 0.1 + 0.2,
                    LastRidbDataRefresh = new DateTime(2026, 11, 15, 1, 2, 3, DateTimeKind.Utc).AddTicks(4567),
                    AutoRefresh = true
                };
                Yaml.GeneralConfigConvertToYaml(Model, "General", Folder);
                // the writer's line breaks are YamlDotNet's (LF here); the document below uses LF
                string Written = File.ReadAllText(Path.Join(Folder, "General.yaml")).ReplaceLineEndings("\n");

                YamlDocumentResult R = YamlDocument.Edit("outputTo: Console # x\n", new[]
                {
                    YamlDocumentEdit.SetScalar("outputTo", OutputType.HtmlFile),
                    YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", Model.RefreshRidbDataDayInterval),
                    YamlDocumentEdit.SetScalar("lastRidbDataRefresh", Model.LastRidbDataRefresh),
                    YamlDocumentEdit.SetScalar("autoRefresh", true)
                }, Model);

                Assert.That(Succeeds(R).Replace(" # x", ""), Is.EqualTo(Written));
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public void SetScalar_Null_IsWrittenAsNull_WhateverTheOldQuoting()
        {
            string Result = Succeeds(Edit<EmailConfig>(Email, M => M.SmtpUsername = null, YamlDocumentEdit.SetScalar("smtpUsername", null)));

            Assert.That(Result, Is.EqualTo(Email.Replace("smtpUsername: \"camper\"", "smtpUsername: null")));
        }

        [Test]
        public void EmptyValues_AreFilledAfterTheColon_AndAnEmptyListBecomesAFlowSequence()
        {
            const string Text = "smtpServer:\nsmtpUsername: # who\nsendToAddressList:\nsmtpPort: 25\n";

            string Result = Succeeds(Edit<EmailConfig>(Text, M =>
                {
                    M.SmtpServer = "x";
                    M.SmtpUsername = "camper";
                    M.SendToAddressList = new List<string> { "a@example.com" };
                },
                YamlDocumentEdit.SetScalar("smtpServer", "x"),
                YamlDocumentEdit.SetScalar("smtpUsername", "camper"),
                YamlDocumentEdit.InsertItem("sendToAddressList", 0, "a@example.com")));

            Assert.That(Result, Is.EqualTo("smtpServer: x\nsmtpUsername: camper # who\nsendToAddressList: [a@example.com]\nsmtpPort: 25\n"));
        }

        [Test]
        public void EmptyValue_IsAZeroLengthRangeRightAfterTheColon()
        {
            const string Text = "smtpServer:   \nsmtpUsername: # who\n";

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);

            Assert.That(A.Succeeded, Is.True);
            YamlDocumentProperty Server = A.Properties[0];
            YamlDocumentProperty User = A.Properties[1];
            Assert.That((Server.ValueStart, Server.ValueEnd), Is.EqualTo((11, 11)));
            Assert.That((User.ValueStart, User.ValueEnd), Is.EqualTo((Text.IndexOf("e: #", StringComparison.Ordinal) + 2, Text.IndexOf("e: #", StringComparison.Ordinal) + 2)));
            Assert.That(User.InlineComment, Is.Not.Null);
            Assert.That(Text[User.InlineComment!.Start..User.InlineComment.End], Is.EqualTo("# who"));
            // the parser itself reports the same zero-length positions
            List<Scalar> Values = ParserScalars(Text).Where((S, i) => i % 2 == 1).ToList();
            Assert.That(Values.Select(S => (S.Start.Index, S.End.Index)), Is.EqualTo(new[] { (11L, 11L), ((long)User.ValueStart, (long)User.ValueStart) }));
        }

        // ---- removing a property ----

        [Test]
        public void RemoveProperty_RemovesItsOwnLines_AndReportsExactlyWhatWent()
        {
            YamlDocumentResult R = Edit<EmailConfig>(Email, M => M.SmtpPassword = null, YamlDocumentEdit.RemoveProperty("smtpPassword"));

            string Line = "smtpPassword: 'S3cr3t-Pa55'   # app password\n";
            Assert.That(Succeeds(R), Is.EqualTo(Email.Replace(Line, "")));
            YamlDocumentLoss Loss = R.Losses.Single();
            Assert.That(Loss.Kind, Is.EqualTo(YamlDocumentLossKind.Property));
            Assert.That(Loss.Key, Is.EqualTo("smtpPassword"));
            Assert.That(Email[Loss.Start..Loss.End], Is.EqualTo(Line));
            Assert.That((Loss.FirstLine, Loss.LastLine), Is.EqualTo((5, 5)));
            Assert.That(Loss.Comments.Select(C => Email[C.Start..C.End]), Is.EqualTo(new[] { "# app password" }));
        }

        [Test]
        public void RemoveProperty_OfAList_RemovesItsItemsAndTheCommentsBetweenThem()
        {
            YamlDocumentResult R = Edit<EmailConfig>(Email, M => M.SendToAddressList = null, YamlDocumentEdit.RemoveProperty("sendToAddressList"));

            Assert.That(Succeeds(R), Is.EqualTo(
                "# mail server\n" +
                "smtpServer: 'smtp.example.com'\n" +
                "smtpPort: 587 # submission\n" +
                "smtpUsername: \"camper\"\n" +
                "smtpPassword: 'S3cr3t-Pa55'   # app password\n" +
                "sendFromAddress: camper@example.com\n"));
            YamlDocumentLoss Loss = R.Losses.Single();
            Assert.That((Loss.FirstLine, Loss.LastLine), Is.EqualTo((6, 11)));
            Assert.That(Loss.Comments.Select(C => Email[C.Start..C.End]), Is.EqualTo(new[] { "# me first", "# main" }));
        }

        [Test]
        public void RemoveProperty_KeepsTheCommentAboveTheKey()
        {
            const string Text = "outputTo: Console\n# refresh every month\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n";

            string Result = Succeeds(Edit<GeneralConfig>(Text, M => M.RefreshRidbDataDayInterval = 0, YamlDocumentEdit.RemoveProperty("refreshRidbDataDayInterval")));

            Assert.That(Result, Is.EqualTo("outputTo: Console\n# refresh every month\nautoRefresh: false\n"));
        }

        // ---- block sequences ----

        [Test]
        public void BlockSequence_SetSequence_ReplacesTheItemLines_AndReportsTheirComments()
        {
            YamlDocumentResult R = Edit<EmailConfig>(Email, M => M.SendToAddressList = new List<string> { "a@example.com", "b@example.com" },
                YamlDocumentEdit.SetSequence("sendToAddressList", new[] { "a@example.com", "b@example.com" }));

            Assert.That(Succeeds(R), Is.EqualTo(
                "# mail server\n" +
                "smtpServer: 'smtp.example.com'\n" +
                "smtpPort: 587 # submission\n" +
                "smtpUsername: \"camper\"\n" +
                "smtpPassword: 'S3cr3t-Pa55'   # app password\n" +
                "sendToAddressList:\n" +
                "  - a@example.com\n" +
                "  - b@example.com\n" +
                "sendFromAddress: camper@example.com\n"));
            Assert.That(R.Losses.Single().Kind, Is.EqualTo(YamlDocumentLossKind.SequenceItems));
            Assert.That(R.Losses.Single().Comments, Has.Count.EqualTo(2));
        }

        [Test]
        public void BlockSequence_SetItem_ChangesOnlyThatItem()
        {
            string Result = Succeeds(Edit<EmailConfig>(Email, M => M.SendToAddressList![0] = "us@example.com", YamlDocumentEdit.SetItem("sendToAddressList", 0, "us@example.com")));

            Assert.That(Result, Is.EqualTo(Email.Replace("  - me@example.com   # main", "  - us@example.com   # main")));
        }

        [TestCase(0, "  - new@example.com\n  # me first\n  - me@example.com   # main\n  - you@example.com\n\n  - them@example.com\n")]
        [TestCase(1, "  # me first\n  - me@example.com   # main\n  - new@example.com\n  - you@example.com\n\n  - them@example.com\n")]
        [TestCase(2, "  # me first\n  - me@example.com   # main\n  - you@example.com\n\n  - new@example.com\n  - them@example.com\n")]
        [TestCase(3, "  # me first\n  - me@example.com   # main\n  - you@example.com\n\n  - them@example.com\n  - new@example.com\n")]
        public void BlockSequence_InsertItem_AddsALineWithTheItemsIndentation(int Index, string Items)
        {
            string Result = Succeeds(Edit<EmailConfig>(Email, M => M.SendToAddressList!.Insert(Index, "new@example.com"),
                YamlDocumentEdit.InsertItem("sendToAddressList", Index, "new@example.com")));

            Assert.That(Result, Is.EqualTo(EmailWithItems(Items)));
        }

        [Test]
        public void BlockSequence_RemoveItem_RemovesTheItemWithTheCommentsThatBelongToIt()
        {
            YamlDocumentResult R = Edit<EmailConfig>(Email, M => M.SendToAddressList!.RemoveAt(0), YamlDocumentEdit.RemoveItem("sendToAddressList", 0));

            Assert.That(Succeeds(R), Is.EqualTo(EmailWithItems("  - you@example.com\n\n  - them@example.com\n")));
            YamlDocumentLoss Loss = R.Losses.Single();
            Assert.That((Loss.Kind, Loss.Index), Is.EqualTo((YamlDocumentLossKind.SequenceItem, 0)));
            Assert.That(Email[Loss.Start..Loss.End], Is.EqualTo("  # me first\n  - me@example.com   # main\n"));
            Assert.That(Loss.Comments.Select(C => Email[C.Start..C.End]), Is.EqualTo(new[] { "# me first", "# main" }));
        }

        [Test]
        public void BlockSequence_RemoveItem_AfterABlankLine_KeepsTheBlankLine()
        {
            string Result = Succeeds(Edit<EmailConfig>(Email, M => M.SendToAddressList!.RemoveAt(2), YamlDocumentEdit.RemoveItem("sendToAddressList", 2)));

            Assert.That(Result, Is.EqualTo(EmailWithItems("  # me first\n  - me@example.com   # main\n  - you@example.com\n\n")));
        }

        [Test]
        public void BlockSequence_RemovingTheOnlyItem_LeavesAnEmptyList_NotAMissingOne()
        {
            const string Text = "sendToAddressList: # list\n  - only@example.com # x\nsendFromAddress: a\n";

            YamlDocumentResult R = Edit<EmailConfig>(Text, M => M.SendToAddressList = new List<string>(), YamlDocumentEdit.RemoveItem("sendToAddressList", 0));

            Assert.That(Succeeds(R), Is.EqualTo("sendToAddressList: [] # list\nsendFromAddress: a\n"));
            Assert.That(R.Losses.Single().Comments.Select(C => Text[C.Start..C.End]), Is.EqualTo(new[] { "# x" }));
        }

        [Test]
        public void BlockSequence_Reorder_MovesEachItemWithItsComments()
        {
            YamlDocumentResult R = Edit<EmailConfig>(Email, M => M.SendToAddressList = new List<string> { "them@example.com", "me@example.com", "you@example.com" },
                YamlDocumentEdit.ReorderItems("sendToAddressList", new[] { 2, 0, 1 }));

            Assert.That(Succeeds(R), Is.EqualTo(EmailWithItems("  - them@example.com\n  # me first\n  - me@example.com   # main\n\n  - you@example.com\n")));
            Assert.That(R.Losses, Is.Empty);
        }

        // ---- flow sequences ----

        [Test]
        public void FlowSequence_SetItem_KeepsTheItemsQuoting()
        {
            string Result = Succeeds(Edit<CampertronConfig>(Search, M => M.IncludeEquipment![1] = "MEDIUM TENT", YamlDocumentEdit.SetItem("includeEquipment", 1, "MEDIUM TENT")));

            Assert.That(Result, Is.EqualTo(Search.Replace("'SMALL TENT'", "'MEDIUM TENT'")));
        }

        [Test]
        public void FlowSequence_SetSequence_ReplacesTheItemsInsideTheBrackets()
        {
            string Result = Succeeds(Edit<CampertronConfig>(Search, M => M.IncludeEquipment = new List<string> { "Tent", "RV: 30 ft" },
                YamlDocumentEdit.SetSequence("includeEquipment", new[] { "Tent", "RV: 30 ft" })));

            Assert.That(Result, Is.EqualTo(Search.Replace("[Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"]", "[Tent, 'RV: 30 ft']")));
        }

        [TestCase(0, "[CABIN, Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"]")]
        [TestCase(1, "[Tent, CABIN, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"]")]
        [TestCase(3, "[Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\", CABIN]")]
        public void FlowSequence_InsertItem_UsesTheSequencesSeparator(int Index, string Flow)
        {
            string Result = Succeeds(Edit<CampertronConfig>(Search, M => M.IncludeEquipment!.Insert(Index, "CABIN"), YamlDocumentEdit.InsertItem("includeEquipment", Index, "CABIN")));

            Assert.That(Result, Is.EqualTo(Search.Replace("[Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"]", Flow)));
        }

        [Test]
        public void FlowSequence_InsertIntoAnEmptyList()
        {
            string Result = Succeeds(Edit<CampertronConfig>(Search, M => M.ExcludeEquipment!.Add("RV"), YamlDocumentEdit.InsertItem("excludeEquipment", 0, "RV")));

            Assert.That(Result, Is.EqualTo(Search.Replace("excludeEquipment: []", "excludeEquipment: [RV]")));
        }

        [TestCase(0, "['SMALL TENT', \"LARGE TENT OVER 9X12`\"]")]
        [TestCase(1, "[Tent, \"LARGE TENT OVER 9X12`\"]")]
        [TestCase(2, "[Tent, 'SMALL TENT']")]
        public void FlowSequence_RemoveItem_RemovesTheItemAndOneSeparator(int Index, string Flow)
        {
            YamlDocumentResult R = Edit<CampertronConfig>(Search, M => M.IncludeEquipment!.RemoveAt(Index), YamlDocumentEdit.RemoveItem("includeEquipment", Index));

            Assert.That(Succeeds(R), Is.EqualTo(Search.Replace("[Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"]", Flow)));
            Assert.That(R.Losses.Single().Index, Is.EqualTo(Index));
        }

        [Test]
        public void FlowSequence_RemovingTheOnlyItem_LeavesEmptyBrackets()
        {
            string Text = Search.Replace("excludeEquipment: []", "excludeEquipment: [RV]");

            string Result = Succeeds(Edit<CampertronConfig>(Text, M => M.ExcludeEquipment!.Clear(), YamlDocumentEdit.RemoveItem("excludeEquipment", 0)));

            Assert.That(Result, Is.EqualTo(Search));
        }

        [Test]
        public void FlowSequence_Reorder_MovesTheItemsWithTheirQuoting()
        {
            string Result = Succeeds(Edit<CampertronConfig>(Search, M => M.IncludeEquipment = new List<string> { "LARGE TENT OVER 9X12`", "Tent", "SMALL TENT" },
                YamlDocumentEdit.ReorderItems("includeEquipment", new[] { 2, 0, 1 })));

            Assert.That(Result, Is.EqualTo(Search.Replace("[Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"]", "[\"LARGE TENT OVER 9X12`\", Tent, 'SMALL TENT']")));
        }

        [Test]
        public void NewList_IsWrittenAsAOneLineFlowSequence()
        {
            const string Text = "displayName: x\nautoRun: true\n";

            string Result = Succeeds(Edit<CampertronConfig>(Text, M => M.SearchValueDates = new List<string> { "2026-11-20", "Nov 21" },
                YamlDocumentEdit.SetSequence("searchValueDates", new[] { "2026-11-20", "Nov 21" })));

            Assert.That(Result, Is.EqualTo("displayName: x\nautoRun: true\nsearchValueDates: ['2026-11-20', Nov 21]\n"));
        }

        // ---- the runtime loader's check ----

        [Test]
        public void GeneralConfig_EveryKindOfValue_IsCheckedWithTheRuntimeLoader()
        {
            const string Text =
                "outputTo: Console # where results go\n" +
                "refreshRidbDataDayInterval: '30'\n" +
                "lastRidbDataRefresh: \"2020-01-02T03:04:05.0000000Z\"\n" +
                "autoRefresh: yes\n";
            var Last = new DateTime(2026, 11, 15, 1, 2, 3, DateTimeKind.Utc);

            string Result = Succeeds(Edit<GeneralConfig>(Text, M =>
                {
                    M.OutputTo = OutputType.Email;
                    M.RefreshRidbDataDayInterval = 7.5;
                    M.LastRidbDataRefresh = Last;
                    M.AutoRefresh = false;
                },
                YamlDocumentEdit.SetScalar("outputTo", OutputType.Email),
                YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", 7.5),
                YamlDocumentEdit.SetScalar("lastRidbDataRefresh", Last),
                YamlDocumentEdit.SetScalar("autoRefresh", false)));

            Assert.That(Result, Is.EqualTo(
                "outputTo: Email # where results go\n" +
                "refreshRidbDataDayInterval: '7.5'\n" +
                "lastRidbDataRefresh: \"2026-11-15T01:02:03.0000000Z\"\n" +
                "autoRefresh: false\n"));
            GeneralConfig Loaded = Load<GeneralConfig>(Result);
            Assert.That((Loaded.OutputTo, Loaded.RefreshRidbDataDayInterval, Loaded.LastRidbDataRefresh, Loaded.LastRidbDataRefresh!.Value.Kind, Loaded.AutoRefresh),
                Is.EqualTo((OutputType.Email, 7.5, (DateTime?)Last, DateTimeKind.Utc, false)));
        }

        [Test]
        public void EmailConfig_IsCheckedWithTheRuntimeLoader()
        {
            YamlDocumentResult R = Edit<EmailConfig>(Email, M =>
                {
                    M.SmtpPassword = "n3w 'pass' #1";
                    M.SmtpPort = 465;
                    M.SendToAddressList!.Add("all@example.com");
                },
                YamlDocumentEdit.SetScalar("smtpPassword", "n3w 'pass' #1"),
                YamlDocumentEdit.SetScalar("smtpPort", 465),
                YamlDocumentEdit.InsertItem("sendToAddressList", 3, "all@example.com"));

            string Result = Succeeds(R);
            Assert.That(Result, Does.Contain("smtpPassword: 'n3w ''pass'' #1'   # app password\n"));
            EmailConfig Loaded = Load<EmailConfig>(Result);
            Assert.That(Loaded.SmtpPassword, Is.EqualTo("n3w 'pass' #1"));
            Assert.That(Loaded.SendToAddressList, Is.EqualTo(new[] { "me@example.com", "you@example.com", "them@example.com", "all@example.com" }));
        }

        [Test]
        public void CampertronConfig_IsCheckedWithTheRuntimeLoader()
        {
            string Result = Succeeds(Edit<CampertronConfig>(Search, M =>
                {
                    M.DisplayName = "Watchman, weekends";
                    M.AutoRun = false;
                    M.CampgroundID = "232490";
                    M.SearchBy = SearchTypes.StartEndDate;
                    M.SearchValueDates = new List<string> { "2026-12-01", "2026-12-05" };
                    M.TotalHumans = 4;
                    M.ShowSaturday = true;
                },
                YamlDocumentEdit.SetScalar("displayName", "Watchman, weekends"),
                YamlDocumentEdit.SetScalar("autoRun", false),
                YamlDocumentEdit.SetScalar("campgroundID", "232490"),
                YamlDocumentEdit.SetScalar("searchBy", SearchTypes.StartEndDate),
                YamlDocumentEdit.SetSequence("searchValueDates", new[] { "2026-12-01", "2026-12-05" }),
                YamlDocumentEdit.SetScalar("totalHumans", 4),
                YamlDocumentEdit.SetScalar("showSaturday", true)));

            Assert.That(Result, Is.EqualTo(
                "displayName: Watchman, weekends # name\n" +
                "autoRun: false\n" +
                "campgroundID: '232490'\n" +
                "searchBy: StartEndDate\n" +
                "searchValue: 30\n" +
                "includeEquipment: [Tent, 'SMALL TENT', \"LARGE TENT OVER 9X12`\"] # tents\n" +
                "excludeEquipment: []\n" +
                "searchValueDates: ['2026-12-01', '2026-12-05']\n" +
                "totalHumans: 4\n" +
                "showSaturday: true\n"));
        }

        [Test]
        public void WrongIntendedModel_IsRefused_WithoutText()
        {
            // the edit writes 25, but the caller says the result should hold 26
            YamlDocumentResult R = YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpPort", 25) }, Changed<EmailConfig>(Email, M => M.SmtpPort = 26));

            YamlDocumentDiagnostic D = Refused(R, YamlDocumentCode.ModelMismatch);
            Assert.That(D.Key, Is.EqualTo("smtpPort"));
        }

        [Test]
        public void AnUneditedFieldThatDiffersFromTheIntendedModel_IsRefused()
        {
            YamlDocumentResult R = YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpPort", 25) },
                Changed<EmailConfig>(Email, M => { M.SmtpPort = 25; M.SendToAddressList!.Reverse(); }));

            Assert.That(Refused(R, YamlDocumentCode.ModelMismatch).Key, Is.EqualTo("sendToAddressList"));
        }

        [Test]
        public void NoEdits_ChecksTheDocumentAgainstTheIntendedModel()
        {
            Assert.That(Succeeds(YamlDocument.Edit(Email, Array.Empty<YamlDocumentEdit>(), Load<EmailConfig>(Email))), Is.EqualTo(Email));
            Refused(YamlDocument.Edit(Email, Array.Empty<YamlDocumentEdit>(), new EmailConfig()), YamlDocumentCode.ModelMismatch);
        }

        [Test]
        public void ADocumentTheRuntimeLoaderRejects_IsRefusedAfterTheEdit_WithoutItsMessage()
        {
            const string Text = "smtpServer: x\nsmtpPort: S3cr3t-Pa55\n";

            YamlDocumentResult R = YamlDocument.Edit(Text, new[] { YamlDocumentEdit.SetScalar("smtpServer", "y") }, new EmailConfig { SmtpServer = "y" });

            YamlDocumentDiagnostic D = Refused(R, YamlDocumentCode.RuntimeLoadFailed);
            Assert.That(D.Line, Is.EqualTo(2));
            Assert.That(D.ToString(), Does.Not.Contain("S3cr3t"));
        }

        [Test]
        public void NoDiagnosticEverQuotesAValueOfTheDocument()
        {
            const string Secret = "S3cr3t-Pa55";
            var Results = new List<YamlDocumentResult>
            {
                // a wrong intended password, a password the loader can't read as a port, a malformed file, a key typo
                YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpPassword", "other") }, Changed<EmailConfig>(Email, M => M.SmtpPassword = Secret + "x")),
                YamlDocument.Edit("smtpPort: " + Secret + "\nsmtpServer: x\n", new[] { YamlDocumentEdit.SetScalar("smtpServer", "y") }, new EmailConfig { SmtpServer = "y" }),
                YamlDocument.Edit("smtpPassword: '" + Secret + "\nsmtpServer: x\n", Array.Empty<YamlDocumentEdit>(), new EmailConfig()),
                YamlDocument.Edit("smtpPasword: " + Secret + "\nsmtpServer: x\n", new[] { YamlDocumentEdit.SetScalar("smtpServer", "y") }, new EmailConfig { SmtpServer = "y" }),
                YamlDocument.Edit("smtpPassword: " + Secret + "\nsmtpPassword: " + Secret + "\n", Array.Empty<YamlDocumentEdit>(), new EmailConfig()),
                YamlDocument.Edit("smtpPassword: &p " + Secret + "\nsmtpUsername: *p\n", new[] { YamlDocumentEdit.SetScalar("smtpPassword", "x") }, new EmailConfig()),
                YamlDocument.Edit("smtpPassword: " + Secret + "\n", new[] { YamlDocumentEdit.SetScalar("smtpPassword", Secret + "\ud800") }, new EmailConfig())
            };

            foreach (YamlDocumentResult R in Results)
            {
                Assert.That(R.Diagnostics, Is.Not.Empty);
                foreach (YamlDocumentDiagnostic D in R.Diagnostics)
                {
                    Assert.That(D.ToString(), Does.Not.Contain("Pa55"));
                    Assert.That(D.Key ?? "", Does.Not.Contain("Pa55"));
                }
            }
            Assert.That(Results.Select(R => R.Succeeded), Is.EqualTo(new[] { false, false, false, true, false, false, false }));
        }

        // ---- the models ----

        [Test]
        public void ModelKeys_AreTheKeysCampertronsWriterWrites()
        {
            foreach (object Model in new object[] { new GeneralConfig(), new EmailConfig(), new CampertronConfig() })
            {
                string Written = new YamlDotNet.Serialization.SerializerBuilder()
                    .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance).Build().Serialize(Model);
                List<string> Keys = YamlDocument.Analyze(Written).Properties.Select(P => P.Key).ToList();

                Assert.That(YamlDocument.ModelKeys(Model.GetType()).Keys, Is.EquivalentTo(Keys), Model.GetType().Name);
            }
            Assert.That(() => YamlDocument.ModelKeys(typeof(CampertronInternalConfig)), Throws.ArgumentException);
        }

        [Test]
        public void EditsAreCheckedAgainstTheModel()
        {
            Assert.That(Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpPort", "25") }, new EmailConfig()), YamlDocumentCode.ValueTypeMismatch).Key, Is.EqualTo("smtpPort"));
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpPort", 25L) }, new EmailConfig()), YamlDocumentCode.ValueTypeMismatch);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpPort", null) }, new EmailConfig()), YamlDocumentCode.ValueTypeMismatch);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("theme", "dark") }, new EmailConfig()), YamlDocumentCode.UnknownProperty);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("sendToAddressList", null) }, new EmailConfig()), YamlDocumentCode.NotAScalar);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.InsertItem("smtpServer", 0, "x") }, new EmailConfig()), YamlDocumentCode.NotASequence);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.RemoveProperty("smtpPort"), YamlDocumentEdit.SetScalar("smtpPort", 1) }, new EmailConfig()), YamlDocumentCode.ConflictingEdits);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.RemoveProperty("nothing") }, new EmailConfig()), YamlDocumentCode.MissingProperty);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetItem("sendToAddressList", 3, "x") }, new EmailConfig()), YamlDocumentCode.IndexOutOfRange);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.InsertItem("sendToAddressList", 4, "x") }, new EmailConfig()), YamlDocumentCode.IndexOutOfRange);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.RemoveItem("sendToAddressList", -1) }, new EmailConfig()), YamlDocumentCode.IndexOutOfRange);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.ReorderItems("sendToAddressList", new[] { 0, 0, 1 }) }, new EmailConfig()), YamlDocumentCode.InvalidOrder);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.ReorderItems("sendToAddressList", new[] { 1, 0 }) }, new EmailConfig()), YamlDocumentCode.InvalidOrder);
            Refused(YamlDocument.Edit(Email, new[] { YamlDocumentEdit.SetScalar("smtpServer", "a\ud800b") }, new EmailConfig()), YamlDocumentCode.UnrepresentableValue);
            Refused(YamlDocument.Edit(Email, Enumerable.Range(0, YamlDocument.MaxEdits + 1).Select(i => YamlDocumentEdit.RemoveProperty("k" + i)).ToArray(), new EmailConfig()),
                YamlDocumentCode.TooManyEdits);
            Refused(YamlDocument.Edit("smtpPort: 1\n", new[] { YamlDocumentEdit.RemoveProperty("smtpPort") }, new EmailConfig()), YamlDocumentCode.LastProperty);
            Assert.That(() => YamlDocument.Edit(Email, Array.Empty<YamlDocumentEdit>(), new CampertronInternalConfig()), Throws.ArgumentException);
            Assert.That(() => YamlDocumentEdit.SetSequence("x", new string[] { null! }), Throws.ArgumentException);
        }

        internal static string EmailWithItems(string Items) =>
            "# mail server\n" +
            "smtpServer: 'smtp.example.com'\n" +
            "smtpPort: 587 # submission\n" +
            "smtpUsername: \"camper\"\n" +
            "smtpPassword: 'S3cr3t-Pa55'   # app password\n" +
            "sendToAddressList:\n" +
            Items +
            "sendFromAddress: camper@example.com\n";

        internal static List<Scalar> ParserScalars(string Text)
        {
            var Reader = new Parser(new StringReader(Text));
            var Result = new List<Scalar>();
            while (Reader.MoveNext())
            {
                if (Reader.Current is Scalar S)
                {
                    Result.Add(S);
                }
            }
            return Result;
        }
    }

    internal static class YamlDocumentTesting
    {
        // the runtime settings loader's deserializer on a text (a leading U+FEFF is not part of the text it sees: File.ReadAllText drops it)
        internal static T Load<T>(string Text) where T : class => Yaml.SettingsDeserializer().Deserialize<T>(Text.TrimStart('\uFEFF'))!;

        internal static T Changed<T>(string Text, Action<T> Change) where T : class
        {
            T Model = Load<T>(Text);
            Change(Model);
            return Model;
        }

        internal static YamlDocumentResult Edit<T>(string Text, Action<T> Change, params YamlDocumentEdit[] Edits) where T : class =>
            YamlDocument.Edit(Text, Edits, Changed(Text, Change));

        internal static string Succeeds(YamlDocumentResult R)
        {
            Assert.That(R.Succeeded, Is.True, Describe(R));
            Assert.That(R.Text, Is.Not.Null);
            Assert.That(R.Diagnostics.Where(D => D.Severity == YamlDocumentSeverity.Error), Is.Empty);
            return R.Text!;
        }

        internal static YamlDocumentDiagnostic Refused(YamlDocumentResult R, YamlDocumentCode Code)
        {
            Assert.That(R.Succeeded, Is.False, "the edit succeeded");
            Assert.That(R.Text, Is.Null);
            Assert.That(R.Losses, Is.Empty);
            YamlDocumentDiagnostic Error = R.Diagnostics.Single(D => D.Severity == YamlDocumentSeverity.Error);
            Assert.That(Error.Code, Is.EqualTo(Code), Describe(R));
            return Error;
        }

        internal static YamlDocumentDiagnostic Refused(YamlDocumentAnalysis A, YamlDocumentCode Code)
        {
            Assert.That(A.Succeeded, Is.False, "the document was accepted");
            Assert.That(A.Properties, Is.Empty);
            YamlDocumentDiagnostic Error = A.Diagnostics.Single(D => D.Severity == YamlDocumentSeverity.Error);
            Assert.That(Error.Code, Is.EqualTo(Code), string.Join("; ", A.Diagnostics));
            return Error;
        }

        internal static string Describe(YamlDocumentResult R) => string.Join("; ", R.Diagnostics);
    }
}
