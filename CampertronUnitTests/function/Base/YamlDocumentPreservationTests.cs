using System.Reflection;
using System.Reflection.Emit;
using CampertronLibrary.function.Base;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using static CampertronUnitTests.function.Base.YamlDocumentTesting;

namespace CampertronUnitTests.function.Base
{
    // What the YAML document editor keeps: everything outside the ranges an edit changes, byte for byte (comments, blank
    // lines, key order, indentation, quoting, line breaks, Unicode, unknown keys, a leading U+FEFF). Every assertion on a
    // result is an exact string. Also the parser facts the editor relies on, its check of a result produced elsewhere, and
    // that it never reaches the file system. All in memory.
    public class YamlDocumentPreservationTests
    {
        private const string General =
            "# Campertron settings\n" +
            "\n" +
            "outputTo: Console      # Console, Email or HtmlFile\n" +
            "\n" +
            "# how often the campground list is refreshed\n" +
            "refreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: 2020-01-02T03:04:05.0000000Z\n" +
            "autoRefresh: false\n" +
            "# end\n";

        // ---- nothing changed ----

        private static readonly string[] Documents =
        {
            General,
            YamlDocumentTests.Email,
            YamlDocumentTests.Search,
            General.Replace("\n", "\r\n"),
            "\uFEFF" + General,
            "outputTo: Console\nautoRefresh: false",
            "%YAML 1.1\n---\noutputTo: Console # c\n...\n",
            "  outputTo: Console\n  autoRefresh: true\n"
        };

        [Test]
        public void NoEdit_ReturnsTheIdenticalText()
        {
            foreach (string Text in Documents)
            {
                object Model = Text.Contains("smtp") ? Load<EmailConfig>(Text) : Text.Contains("displayName") ? Load<CampertronConfig>(Text) : Load<GeneralConfig>(Text);
                YamlDocumentResult R = Model switch
                {
                    EmailConfig E => YamlDocument.Edit(Text, Array.Empty<YamlDocumentEdit>(), E),
                    CampertronConfig C => YamlDocument.Edit(Text, Array.Empty<YamlDocumentEdit>(), C),
                    _ => YamlDocument.Edit(Text, Array.Empty<YamlDocumentEdit>(), (GeneralConfig)Model)
                };

                Assert.That(Succeeds(R), Is.EqualTo(Text));
                Assert.That(string.CompareOrdinal(R.Text, Text), Is.Zero);
            }
        }

        // ---- comments, blank lines, order, indentation ----

        [Test]
        public void Comments_BlankLines_AndOrder_StayAsTheyAre()
        {
            string Result = Succeeds(Edit<GeneralConfig>(General, M =>
                {
                    M.OutputTo = OutputType.HtmlFile;
                    M.RefreshRidbDataDayInterval = 7;
                },
                YamlDocumentEdit.SetScalar("outputTo", OutputType.HtmlFile),
                YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", 7.0)));

            Assert.That(Result, Is.EqualTo(
                "# Campertron settings\n" +
                "\n" +
                "outputTo: HtmlFile      # Console, Email or HtmlFile\n" +
                "\n" +
                "# how often the campground list is refreshed\n" +
                "refreshRidbDataDayInterval: 7\n" +
                "lastRidbDataRefresh: 2020-01-02T03:04:05.0000000Z\n" +
                "autoRefresh: false\n" +
                "# end\n"));
        }

        [Test]
        public void ACustomKeyOrder_IsKept_AndANewKeyGoesAfterTheLastOne()
        {
            const string Text = "autoRefresh: true\n# first the output\noutputTo: Email\n";

            string Result = Succeeds(Edit<GeneralConfig>(Text, M => M.RefreshRidbDataDayInterval = 14, YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", 14.0)));

            Assert.That(Result, Is.EqualTo("autoRefresh: true\n# first the output\noutputTo: Email\nrefreshRidbDataDayInterval: 14\n"));
        }

        [Test]
        public void Indentation_IsKept_AndUsedForNewLines()
        {
            const string Text =
                "  smtpServer: x\n" +
                "  sendToAddressList:\n" +
                "      -    a@example.com\n" +
                "      -    b@example.com # b\n";

            string Result = Succeeds(Edit<EmailConfig>(Text, M =>
                {
                    M.SendToAddressList!.Insert(1, "c@example.com");
                    M.SmtpPort = 25;
                },
                YamlDocumentEdit.InsertItem("sendToAddressList", 1, "c@example.com"),
                YamlDocumentEdit.SetScalar("smtpPort", 25)));

            Assert.That(Result, Is.EqualTo(
                "  smtpServer: x\n" +
                "  sendToAddressList:\n" +
                "      -    a@example.com\n" +
                "      -    c@example.com\n" +
                "      -    b@example.com # b\n" +
                "  smtpPort: 25\n"));
        }

        [Test]
        public void ACommentAboveAKey_AndCommentsInsideAList_StayWhenTheyAreNotEdited()
        {
            string Result = Succeeds(Edit<EmailConfig>(YamlDocumentTests.Email, M =>
                {
                    M.SmtpServer = "mail.example.org";
                    M.SendToAddressList![1] = "us@example.com";
                },
                YamlDocumentEdit.SetScalar("smtpServer", "mail.example.org"),
                YamlDocumentEdit.SetItem("sendToAddressList", 1, "us@example.com")));

            Assert.That(Result, Is.EqualTo(YamlDocumentTests.EmailWithItems("  # me first\n  - me@example.com   # main\n  - us@example.com\n\n  - them@example.com\n")
                .Replace("smtpServer: 'smtp.example.com'", "smtpServer: 'mail.example.org'")));
        }

        [Test]
        public void AnInlineComment_KeepsItsSpacing_WhenTheValueGrowsOrShrinks()
        {
            const string Text = "smtpServer: ab   # three spaces\nsmtpPort: 25\t# a tab\n";

            Assert.That(Succeeds(Edit<EmailConfig>(Text, M => M.SmtpServer = "a-much-longer-host.example.org", YamlDocumentEdit.SetScalar("smtpServer", "a-much-longer-host.example.org"))),
                Is.EqualTo("smtpServer: a-much-longer-host.example.org   # three spaces\nsmtpPort: 25\t# a tab\n"));
            Assert.That(Succeeds(Edit<EmailConfig>(Text, M => M.SmtpPort = 2525, YamlDocumentEdit.SetScalar("smtpPort", 2525))),
                Is.EqualTo("smtpServer: ab   # three spaces\nsmtpPort: 2525\t# a tab\n"));
        }

        // ---- comment scanning ----

        [Test]
        public void HashSignsInsideValues_AreNotComments()
        {
            const string Text =
                "smtpPassword: 'it''s # not a comment'   # real 1\n" +
                "smtpServer: \"a # b \\\" c # d\" # real 2\n" +
                "smtpUsername: a#b#c # real 3\n" +
                "sendToAddressList: ['x # y', \"z#\", w#v] # real 4\n" +
                "# real 5\n" +
                "notes: | # real 6\n" +
                "  # not a comment either\n" +
                "  text\n" +
                "# real 7\n" +
                "sendFromAddress: x\n";

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);

            Assert.That(A.Succeeded, Is.True, string.Join("; ", A.Diagnostics));
            Assert.That(A.Comments.Select(C => Text[C.Start..C.End]), Is.EqualTo(Enumerable.Range(1, 7).Select(i => "# real " + i)));
            Assert.That(A.Properties.Single(P => P.Key == "smtpPassword").InlineComment!.Start, Is.EqualTo(Text.IndexOf("# real 1", StringComparison.Ordinal)));
            // and an edit next to each of them leaves them where they are (notes is an unknown key: kept and left out of the check)
            YamlDocumentResult R = YamlDocument.Edit(Text, new[]
            {
                YamlDocumentEdit.SetScalar("smtpPassword", "new # pass"),
                YamlDocumentEdit.SetItem("sendToAddressList", 2, "u#t")
            }, new EmailConfig { SmtpPassword = "new # pass", SmtpServer = "a # b \" c # d", SmtpUsername = "a#b#c", SendToAddressList = new List<string> { "x # y", "z#", "u#t" }, SendFromAddress = "x" });
            Assert.That(Succeeds(R), Is.EqualTo(Text.Replace("'it''s # not a comment'", "'new # pass'").Replace("w#v]", "u#t]")));
            Assert.That(R.Diagnostics.Single().Code, Is.EqualTo(YamlDocumentCode.UnknownKeys));
        }

        [Test]
        public void ACommentWithoutASpaceBeforeIt_CantBeToldApart_AndIsRefused()
        {
            // YamlDotNet reads '#c' after a quoted value or a flow mapping as a comment, although YAML wants a space first
            Refused(YamlDocument.Analyze("smtpServer: 'x'#c\n"), YamlDocumentCode.UncertainComment);
            Refused(YamlDocument.Analyze("smtpServer: x\nextra: {k: v}#c\n"), YamlDocumentCode.UncertainComment);
            // after a flow sequence it is a syntax error
            Refused(YamlDocument.Analyze("sendToAddressList: [a]#c\n"), YamlDocumentCode.Malformed);
        }

        // ---- quoting and typed-looking strings ----

        [Test]
        public void Quoting_IsKept_WhenTheNewValueCanUseIt()
        {
            const string Text = "smtpServer: plain.example\nsmtpUsername: 'single'\nsmtpPassword: \"double\"\nsendFromAddress: plain@example.org\n";

            string Result = Succeeds(Edit<EmailConfig>(Text, M =>
                {
                    M.SmtpServer = "other.example";
                    M.SmtpUsername = "it's";
                    M.SmtpPassword = "tab\there \"q\" \\";
                    M.SendFromAddress = "a: b";
                },
                YamlDocumentEdit.SetScalar("smtpServer", "other.example"),
                YamlDocumentEdit.SetScalar("smtpUsername", "it's"),
                YamlDocumentEdit.SetScalar("smtpPassword", "tab\there \"q\" \\"),
                YamlDocumentEdit.SetScalar("sendFromAddress", "a: b")));

            // a plain value that can't stay plain (": ") gets single quotes, the safe representation
            Assert.That(Result, Is.EqualTo("smtpServer: other.example\nsmtpUsername: 'it''s'\nsmtpPassword: \"tab\\there \\\"q\\\" \\\\\"\nsendFromAddress: 'a: b'\n"));
        }

        [Test]
        public void AValueThatSpansLines_IsReplacedWhole_AndTheLinesAfterItStay()
        {
            const string Text = "smtpServer: one\n  two # c\nsmtpUsername: \"three\n  four\"\nsmtpPort: 1\n";

            string Result = Succeeds(Edit<EmailConfig>(Text, M =>
                {
                    M.SmtpServer = "x";
                    M.SmtpUsername = "y";
                },
                YamlDocumentEdit.SetScalar("smtpServer", "x"),
                YamlDocumentEdit.SetScalar("smtpUsername", "y")));

            Assert.That(Result, Is.EqualTo("smtpServer: x # c\nsmtpUsername: \"y\"\nsmtpPort: 1\n"));
        }

        [Test]
        public void QuotedKeys_AreFoundByTheirValue_AndKeepTheirQuotes()
        {
            const string Text = "'smtpServer': x\n\"smtpPort\": 1\n";

            string Result = Succeeds(Edit<EmailConfig>(Text, M => M.SmtpPort = 2, YamlDocumentEdit.SetScalar("smtpPort", 2)));

            Assert.That(Result, Is.EqualTo("'smtpServer': x\n\"smtpPort\": 2\n"));
            Assert.That(YamlDocument.Analyze(Text).Properties.Select(P => P.Key), Is.EqualTo(new[] { "smtpServer", "smtpPort" }));
        }

        [Test]
        public void SingleQuotes_ThatCantHoldTheValue_BecomeDoubleQuotes()
        {
            string Result = Succeeds(Edit<EmailConfig>("smtpUsername: 'single'\n", M => M.SmtpUsername = "two\nlines", YamlDocumentEdit.SetScalar("smtpUsername", "two\nlines")));

            Assert.That(Result, Is.EqualTo("smtpUsername: \"two\\nlines\"\n"));
        }

        [Test]
        public void NumberDateAndBooleanLikeStrings_KeepTheirStyle_AndNewOnesAreQuoted()
        {
            const string Text =
                "displayName: yes\n" +
                "campgroundID: 232445\n" +
                "searchValueDates:\n" +
                "- 2026-11-20\n" +
                "- '2026-11-21'\n" +
                "excludeSites: [007]\n";

            string Result = Succeeds(Edit<CampertronConfig>(Text, M =>
                {
                    M.DisplayName = "no";
                    M.CampgroundID = "232490";
                    M.ExcludeSites![0] = "008";
                    M.IncludeSites = new List<string> { "true", "1e3", "null", "~", ".5" };
                },
                YamlDocumentEdit.SetScalar("displayName", "no"),
                YamlDocumentEdit.SetScalar("campgroundID", "232490"),
                YamlDocumentEdit.SetItem("excludeSites", 0, "008"),
                YamlDocumentEdit.SetSequence("includeSites", new[] { "true", "1e3", "null", "~", ".5" })));
            Result = Succeeds(Edit<CampertronConfig>(Result, M => M.SearchValueDates![0] = "2026-12-01", YamlDocumentEdit.SetItem("searchValueDates", 0, "2026-12-01")));
            Result = Succeeds(Edit<CampertronConfig>(Result, M => M.SearchValueDates!.Add("2026-12-02"), YamlDocumentEdit.InsertItem("searchValueDates", 2, "2026-12-02")));

            // an edited plain value stays plain (the loader reads it as text for a text setting); a new value that YAML would
            // read as a number, date, boolean or null is quoted
            Assert.That(Result, Is.EqualTo(
                "displayName: no\n" +
                "campgroundID: 232490\n" +
                "searchValueDates:\n" +
                "- 2026-12-01\n" +
                "- '2026-11-21'\n" +
                "- '2026-12-02'\n" +
                "excludeSites: [008]\n" +
                "includeSites: ['true', '1e3', 'null', '~', '.5']\n"));
        }

        [Test]
        public void ATextSettingIsNeverWrittenPlainInAFormTheLoaderReadsAsNull()
        {
            foreach (string Value in new[] { "null", "Null", "NULL", "~", "" })
            {
                string Result = Succeeds(Edit<EmailConfig>("smtpServer: host\n", M => M.SmtpServer = Value, YamlDocumentEdit.SetScalar("smtpServer", Value)));

                Assert.That(Result, Is.EqualTo("smtpServer: '" + Value + "'\n"));
                Assert.That(Load<EmailConfig>(Result).SmtpServer, Is.EqualTo(Value));
            }
        }

        // ---- line breaks ----

        [Test]
        public void LF_IsKept()
        {
            string Result = Succeeds(Edit<GeneralConfig>(General, M => M.AutoRefresh = true, YamlDocumentEdit.SetScalar("autoRefresh", true)));

            Assert.That(Result, Is.EqualTo(General.Replace("autoRefresh: false", "autoRefresh: true")));
            Assert.That(Result, Does.Not.Contain("\r"));
        }

        [Test]
        public void CRLF_IsKept_AndUsedForNewLines()
        {
            string Text = YamlDocumentTests.Email.Replace("\n", "\r\n");

            YamlDocumentResult R = Edit<EmailConfig>(Text, M => M.SendToAddressList!.Add("new@example.com"), YamlDocumentEdit.InsertItem("sendToAddressList", 3, "new@example.com"));

            Assert.That(Succeeds(R), Is.EqualTo(Text.Replace("  - them@example.com\r\n", "  - them@example.com\r\n  - new@example.com\r\n")));
            Assert.That(YamlDocument.Analyze(R.Text!).LfCount, Is.Zero);
            // moved items leave every line break where it was
            string Moved = Succeeds(Edit<EmailConfig>(Text, M => M.SendToAddressList!.Reverse(), YamlDocumentEdit.ReorderItems("sendToAddressList", new[] { 2, 1, 0 })));
            Assert.That(Moved, Is.EqualTo(YamlDocumentTests.EmailWithItems("  - them@example.com\n  - you@example.com\n\n  # me first\n  - me@example.com   # main\n").Replace("\n", "\r\n")));
        }

        [Test]
        public void CR_IsKept_AndUsedForNewLines()
        {
            const string Text = "outputTo: Console\rautoRefresh: false\r";

            string Result = Succeeds(Edit<GeneralConfig>(Text, M => M.RefreshRidbDataDayInterval = 1, YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", 1.0)));

            Assert.That(Result, Is.EqualTo("outputTo: Console\rautoRefresh: false\rrefreshRidbDataDayInterval: 1\r"));
        }

        [Test]
        public void MixedLineBreaks_AreKept_AndNewLinesGetTheMostFrequentOne_WithAWarning()
        {
            const string Text = "outputTo: Console\r\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\r\n";

            YamlDocumentResult R = Edit<GeneralConfig>(Text, M => M.LastRidbDataRefresh = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                YamlDocumentEdit.SetScalar("lastRidbDataRefresh", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            Assert.That(Succeeds(R), Is.EqualTo(Text + "lastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\r\n"));
            YamlDocumentDiagnostic Warning = R.Diagnostics.Single();
            Assert.That((Warning.Code, Warning.Severity), Is.EqualTo((YamlDocumentCode.MixedLineEndings, YamlDocumentSeverity.Warning)));
            Assert.That(Warning.Message, Does.Contain("LF 1, CRLF 2, CR 0").And.Contain("CRLF, the most frequent"));
            // a tie goes to LF
            Assert.That(YamlDocument.Analyze("a: 1\r\nb: 2\n").InsertionLineEnding, Is.EqualTo("\n"));
            Assert.That(YamlDocument.Analyze("a: 1\rb: 2\r").InsertionLineEnding, Is.EqualTo("\r"));
            Assert.That(YamlDocument.Analyze("a: 1").InsertionLineEnding, Is.EqualTo("\n"));
        }

        [Test]
        public void AFinalLineBreak_IsKept()
        {
            string Result = Succeeds(Edit<GeneralConfig>(General, M =>
                {
                    M.AutoRefresh = true;
                    M.LastRidbDataRefresh = null;
                },
                YamlDocumentEdit.RemoveProperty("lastRidbDataRefresh"),
                YamlDocumentEdit.SetScalar("autoRefresh", true)));

            Assert.That(Result, Does.EndWith("autoRefresh: true\n# end\n"));
            Assert.That(YamlDocument.Analyze(Result).EndsWithLineBreak, Is.True);
        }

        [Test]
        public void NoFinalLineBreak_IsKept_ByEveryKindOfEdit()
        {
            const string Text = "outputTo: Console\nautoRefresh: false";

            Assert.That(Succeeds(Edit<GeneralConfig>(Text, M => M.AutoRefresh = true, YamlDocumentEdit.SetScalar("autoRefresh", true))),
                Is.EqualTo("outputTo: Console\nautoRefresh: true"));
            // an insertion after the last line puts the line break before the new line
            Assert.That(Succeeds(Edit<GeneralConfig>(Text, M => M.RefreshRidbDataDayInterval = 7, YamlDocumentEdit.SetScalar("refreshRidbDataDayInterval", 7.0))),
                Is.EqualTo("outputTo: Console\nautoRefresh: false\nrefreshRidbDataDayInterval: 7"));
            // removing the last line removes the line break before it
            Assert.That(Succeeds(Edit<GeneralConfig>(Text, M => M.AutoRefresh = false, YamlDocumentEdit.RemoveProperty("autoRefresh"))),
                Is.EqualTo("outputTo: Console"));
            const string List = "smtpServer: x\nsendToAddressList:\n- a\n- b";
            Assert.That(Succeeds(Edit<EmailConfig>(List, M => M.SendToAddressList!.Add("c"), YamlDocumentEdit.InsertItem("sendToAddressList", 2, "c"))),
                Is.EqualTo(List + "\n- c"));
            Assert.That(Succeeds(Edit<EmailConfig>(List, M => M.SendToAddressList!.RemoveAt(1), YamlDocumentEdit.RemoveItem("sendToAddressList", 1))),
                Is.EqualTo("smtpServer: x\nsendToAddressList:\n- a"));
            Assert.That(Succeeds(Edit<EmailConfig>(List, M => M.SendToAddressList = new List<string>(), YamlDocumentEdit.SetSequence("sendToAddressList", Array.Empty<string>()))),
                Is.EqualTo("smtpServer: x\nsendToAddressList: []"));
            Assert.That(Succeeds(Edit<EmailConfig>(List, M => M.SendToAddressList!.Reverse(), YamlDocumentEdit.ReorderItems("sendToAddressList", new[] { 1, 0 }))),
                Is.EqualTo("smtpServer: x\nsendToAddressList:\n- b\n- a"));
        }

        // ---- Unicode, UTF-16 offsets and the byte-order mark ----

        [Test]
        public void Unicode_IsKept_AndEdited()
        {
            const string Text = "displayName: Caf\u00E9 \u2013 \u6771\u4EAC \u26FA # \u00FCber\nautoRun: true\n";

            string Result = Succeeds(Edit<CampertronConfig>(Text, M => M.DisplayName = "Zo\u00EB's camp", YamlDocumentEdit.SetScalar("displayName", "Zo\u00EB's camp")));

            Assert.That(Result, Is.EqualTo("displayName: Zo\u00EB's camp # \u00FCber\nautoRun: true\n"));
        }

        [Test]
        public void AstralCharacters_CountAsTwoUtf16Units_AsInTheParsersMarks()
        {
            // U+1F600 and U+1F3D5 are each a surrogate pair
            const string Text = "displayName: '\U0001F600 camp'\nautoRun: true # \U0001F3D5\nsearchValue: 3\n";

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);
            YamlDocumentProperty AutoRun = A.Properties[1];
            Assert.That(AutoRun.KeyStart, Is.EqualTo(Text.IndexOf("autoRun", StringComparison.Ordinal)));
            Assert.That(AutoRun.KeyStart, Is.EqualTo(YamlDocumentTests.ParserScalars(Text)[2].Start.Index));
            Assert.That(Text[AutoRun.InlineComment!.Start..AutoRun.InlineComment.End], Is.EqualTo("# \U0001F3D5"));
            // two quotes, the surrogate pair, " camp"
            Assert.That(A.Properties[0].ValueEnd - A.Properties[0].ValueStart, Is.EqualTo(9));

            string Result = Succeeds(Edit<CampertronConfig>(Text, M =>
                {
                    M.DisplayName = "\U0001F3D5 site";
                    M.SearchValue = 4;
                },
                YamlDocumentEdit.SetScalar("displayName", "\U0001F3D5 site"),
                YamlDocumentEdit.SetScalar("searchValue", 4)));
            Assert.That(Result, Is.EqualTo("displayName: '\U0001F3D5 site'\nautoRun: true # \U0001F3D5\nsearchValue: 4\n"));
        }

        [Test]
        public void AByteOrderMarkInTheText_IsKept_AndOffsetsCountIt()
        {
            string Text = "\uFEFF" + General;

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);
            Assert.That(A.HasByteOrderMark, Is.True);
            Assert.That(A.Properties[0].KeyStart, Is.EqualTo(General.IndexOf("outputTo", StringComparison.Ordinal) + 1));
            Assert.That(A.Comments[0].Start, Is.EqualTo(1));
            Assert.That((A.Comments[0].Line, A.Properties[0].FirstLine), Is.EqualTo((1, 3)));

            string Result = Succeeds(Edit<GeneralConfig>(Text, M => M.OutputTo = OutputType.Email, YamlDocumentEdit.SetScalar("outputTo", OutputType.Email)));
            Assert.That(Result, Is.EqualTo("\uFEFF" + General.Replace("outputTo: Console ", "outputTo: Email ")));
            Assert.That(YamlDocument.Analyze(General).HasByteOrderMark, Is.False);
        }

        // ---- keys Campertron does not know ----

        [Test]
        public void UnknownKeysAndSections_AreKept_AndTheResultIsCheckedWithoutThem()
        {
            const string Text =
                "outputTo: Console\n" +
                "theme: dark # mine\n" +
                "extra:\n" +
                "  nested: [1, 2]   # n\n" +
                "  deeper:\n" +
                "    key: 'value'\n" +
                "    text: |\n" +
                "      kept # as it is\n" +
                "autoRefresh: false\n";
            var Intended = new GeneralConfig { OutputTo = OutputType.Email, AutoRefresh = true };

            YamlDocumentResult R = YamlDocument.Edit(Text, new[]
            {
                YamlDocumentEdit.SetScalar("outputTo", OutputType.Email),
                YamlDocumentEdit.SetScalar("autoRefresh", true)
            }, Intended);

            Assert.That(Succeeds(R), Is.EqualTo(Text.Replace("outputTo: Console", "outputTo: Email").Replace("autoRefresh: false", "autoRefresh: true")));
            YamlDocumentDiagnostic Warning = R.Diagnostics.Single();
            Assert.That((Warning.Code, Warning.Severity), Is.EqualTo((YamlDocumentCode.UnknownKeys, YamlDocumentSeverity.Warning)));
            Assert.That(Warning.Message, Does.Contain("theme, extra"));
            // the check still compares the known settings
            Refused(YamlDocument.Edit(Text, new[] { YamlDocumentEdit.SetScalar("outputTo", OutputType.Email) }, Intended), YamlDocumentCode.ModelMismatch);
        }

        [Test]
        public void AnUnknownKeyCanBeRemoved()
        {
            const string Text = "outputTo: Console\ntheme: dark # mine\nautoRefresh: false\n";

            YamlDocumentResult R = YamlDocument.Edit(Text, new[] { YamlDocumentEdit.RemoveProperty("theme") }, new GeneralConfig());

            Assert.That(Succeeds(R), Is.EqualTo("outputTo: Console\nautoRefresh: false\n"));
            Assert.That(R.Diagnostics, Is.Empty);
        }

        // ---- directives and document markers ----

        [Test]
        public void DirectivesAndDocumentMarkers_AreKept_AndANewKeyGoesBeforeTheEndMarker()
        {
            const string Text = "%YAML 1.1\n--- # start\noutputTo: Console\n... # end\n";

            string Result = Succeeds(Edit<GeneralConfig>(Text, M => M.AutoRefresh = true, YamlDocumentEdit.SetScalar("autoRefresh", true)));

            Assert.That(Result, Is.EqualTo("%YAML 1.1\n--- # start\noutputTo: Console\nautoRefresh: true\n... # end\n"));
        }

        // ---- what the parser reports, and what the editor makes of it ----

        [Test]
        public void TheParser_ReportsAFlowSequencesEndAtItsClosingBracket()
        {
            const string Text = "excludeSites: [a, 'b'] # c\nincludeSites: []\n";
            var Reader = new Parser(new StringReader(Text));
            var Ends = new List<SequenceEnd>();
            while (Reader.MoveNext())
            {
                if (Reader.Current is SequenceEnd E)
                {
                    Ends.Add(E);
                }
            }

            // the mark is AT the ']' (a zero-length range), not after it
            Assert.That(Ends.Select(E => (E.Start.Index, E.End.Index)), Is.EqualTo(new[] { (21L, 21L), (42L, 42L) }));
            Assert.That((Text[21], Text[42]), Is.EqualTo((']', ']')));
            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);
            Assert.That(A.Properties.Select(P => Text[P.ValueStart..P.ValueEnd]), Is.EqualTo(new[] { "[a, 'b']", "[]" }));
            Assert.That(A.Properties[0].Items.Select(I => Text[I.Start..I.End]), Is.EqualTo(new[] { "a", "'b'" }));
            Assert.That(Text[A.Properties[0].InlineComment!.Start..A.Properties[0].InlineComment!.End], Is.EqualTo("# c"));
        }

        [Test]
        public void FlowCollections_KeepTheirBracketsAndWhatFollows_WhenItemsChange()
        {
            const string Text = "excludeSites: [ a ,  'b' ] # c\nincludeSites: [ ]\nextra: {k: [1]} # f\n";
            var Intended = new CampertronConfig { ExcludeSites = new List<string> { "a", "x" }, IncludeSites = new List<string> { "y" } };

            YamlDocumentResult R = YamlDocument.Edit(Text, new[]
            {
                YamlDocumentEdit.SetItem("excludeSites", 1, "x"),
                YamlDocumentEdit.InsertItem("includeSites", 0, "y")
            }, Intended);

            // "y" is a YAML 1.1 boolean, so a new "y" is quoted
            Assert.That(Succeeds(R), Is.EqualTo("excludeSites: [ a ,  'x' ] # c\nincludeSites: [ 'y' ]\nextra: {k: [1]} # f\n"));
        }

        [Test]
        public void ABlockScalarsRangeRunsOverItsLastLineBreak_AndTheEditorStopsAtItsLastCharacter()
        {
            const string Text = "notes: |\n  line one\n  line two\n\nsmtpServer: x\n";

            Scalar Notes = YamlDocumentTests.ParserScalars(Text)[1];
            // the parser's range ends at the next key: it includes the line break and the blank line after the content
            Assert.That((Notes.Style, Notes.End.Index), Is.EqualTo((ScalarStyle.Literal, (long)Text.IndexOf("smtpServer", StringComparison.Ordinal))));
            YamlDocumentProperty P = YamlDocument.Analyze(Text).Properties[0];
            Assert.That(Text[P.ValueStart..P.ValueEnd], Is.EqualTo("|\n  line one\n  line two"));
            Assert.That((P.FirstLine, P.LastLine, P.Shape), Is.EqualTo((1, 3, YamlDocumentShape.BlockScalar)));

            // removing it removes its own lines and keeps the next key (and the blank line, which is not its own)
            YamlDocumentResult R = YamlDocument.Edit(Text, new[] { YamlDocumentEdit.RemoveProperty("notes") }, new EmailConfig { SmtpServer = "x" });
            Assert.That(Succeeds(R), Is.EqualTo("\nsmtpServer: x\n"));
            // a new key after a block scalar goes after its content
            const string Last = "smtpServer: x\nnotes: >-\n  folded # text\n\n# end\n";
            Assert.That(Succeeds(YamlDocument.Edit(Last, new[] { YamlDocumentEdit.SetScalar("smtpPort", 25) }, new EmailConfig { SmtpServer = "x", SmtpPort = 25 })),
                Is.EqualTo("smtpServer: x\nnotes: >-\n  folded # text\nsmtpPort: 25\n\n# end\n"));
        }

        [Test]
        public void ABlockScalarsHeaderComment_IsAComment_AndGoesWithItsProperty()
        {
            const string Text = "smtpServer: x\nnotes: |- # header\n  # content\n";

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);
            Assert.That(A.Comments.Select(C => Text[C.Start..C.End]), Is.EqualTo(new[] { "# header" }));

            YamlDocumentResult R = YamlDocument.Edit(Text, new[] { YamlDocumentEdit.RemoveProperty("notes") }, new EmailConfig { SmtpServer = "x" });
            Assert.That(Succeeds(R), Is.EqualTo("smtpServer: x\n"));
            Assert.That(R.Losses.Single().Comments.Select(C => Text[C.Start..C.End]), Is.EqualTo(new[] { "# header" }));
        }

        // ---- the check of a result produced elsewhere ----

        [Test]
        public void VerifyCandidate_AcceptsExactlyTheEditedText()
        {
            YamlDocumentEdit[] Edits = { YamlDocumentEdit.SetScalar("smtpPort", 25) };
            EmailConfig Intended = Changed<EmailConfig>(YamlDocumentTests.Email, M => M.SmtpPort = 25);
            string Expected = YamlDocument.Edit(YamlDocumentTests.Email, Edits, Intended).Text!;

            Assert.That(Succeeds(YamlDocument.VerifyCandidate(YamlDocumentTests.Email, Edits, Intended, Expected)), Is.EqualTo(Expected));
        }

        [TestCase("smtpPort: 25 # submission", "smtpPort: 25 #  submission")]
        [TestCase("smtpPort: 25 # submission\n", "smtpPort: 25 # submission\r\n")]
        [TestCase("smtpServer: 'smtp.example.com'", "smtpServer: \"smtp.example.com\"")]
        [TestCase("  # me first\n", "")]
        [TestCase("smtpPort: 25 #", "smtpPort: 25#")]
        [TestCase("sendFromAddress: camper@example.com\n", "sendFromAddress: camper@example.com\nsendFromAddress: x\n")]
        public void VerifyCandidate_RefusesAnyOtherText(string Find, string Replacement)
        {
            YamlDocumentEdit[] Edits = { YamlDocumentEdit.SetScalar("smtpPort", 25) };
            EmailConfig Intended = Changed<EmailConfig>(YamlDocumentTests.Email, M => M.SmtpPort = 25);
            string Candidate = YamlDocument.Edit(YamlDocumentTests.Email, Edits, Intended).Text!.Replace(Find, Replacement);

            Refused(YamlDocument.VerifyCandidate(YamlDocumentTests.Email, Edits, Intended, Candidate), YamlDocumentCode.PreservationFailed);
        }

        [Test]
        public void VerifyCandidate_RefusesTheNaiveBlockScalarRemoval_ThatTakesTheNextKeyWithIt()
        {
            // the block scalar's parser range ends at the start of "smtpServer"; removing "through the line of its end"
            // would remove that key too
            const string Text = "notes: |\n  text\nsmtpServer: x\nsmtpPort: 25\n";
            YamlDocumentEdit[] Edits = { YamlDocumentEdit.RemoveProperty("notes") };
            var Intended = new EmailConfig { SmtpServer = "x", SmtpPort = 25 };

            Assert.That(Succeeds(YamlDocument.Edit(Text, Edits, Intended)), Is.EqualTo("smtpServer: x\nsmtpPort: 25\n"));
            Refused(YamlDocument.VerifyCandidate(Text, Edits, Intended, "smtpPort: 25\n"), YamlDocumentCode.PreservationFailed);
        }

        [Test]
        public void VerifyCandidate_RefusesAnUnparsableText()
        {
            YamlDocumentEdit[] Edits = { YamlDocumentEdit.SetScalar("smtpPort", 25) };
            EmailConfig Intended = Changed<EmailConfig>(YamlDocumentTests.Email, M => M.SmtpPort = 25);
            string Candidate = YamlDocumentTests.Email.Replace("smtpPort: 587", "smtpPort: [25");

            // the text outside the edit is checked first, so this one already fails there
            Refused(YamlDocument.VerifyCandidate(YamlDocumentTests.Email, Edits, Intended, Candidate), YamlDocumentCode.PreservationFailed);
        }

        // ---- no file system ----

        [Test]
        public void TheEditor_NeverReachesTheFileSystemTheConsoleOrTheNetwork()
        {
            var Methods = new List<MethodBase>();
            var Types = new Stack<Type>(new[] { typeof(YamlDocument), typeof(YamlDocumentEdit), typeof(YamlDocumentResult), typeof(YamlDocumentAnalysis),
                typeof(YamlDocumentDiagnostic), typeof(YamlDocumentLoss), typeof(YamlDocumentProperty), typeof(YamlDocumentItem), typeof(YamlDocumentComment) });
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            while (Types.Count > 0)
            {
                Type T = Types.Pop();
                Methods.AddRange(T.GetMethods(All));
                Methods.AddRange(T.GetConstructors(All));
                foreach (Type Nested in T.GetNestedTypes(All))
                {
                    Types.Push(Nested);
                }
            }
            Methods.Add(typeof(Yaml).GetMethod(nameof(Yaml.SettingsDeserializer), All)!);

            var Called = new HashSet<string>();
            foreach (MethodBase M in Methods)
            {
                foreach (MemberInfo Member in ReferencedMembers(M))
                {
                    Called.Add((Member.DeclaringType?.FullName ?? "?") + "::" + Member.Name);
                }
            }

            Assert.That(Methods, Has.Count.GreaterThan(100));
            Assert.That(Called, Does.Contain("YamlDotNet.Core.Parser::.ctor"));
            Assert.That(Called, Does.Contain("System.IO.StringReader::.ctor"));
            string[] Forbidden =
            {
                "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream", "System.IO.FileSystem",
                "System.IO.StreamReader", "System.IO.StreamWriter", "System.IO.Path", "System.Console", "System.Environment", "System.Net.", "System.Diagnostics.Process",
                "CampertronLibrary.function.Base.ConsoleUi", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.RecDotOrg."
            };
            // the only Environment member is the thread id the compiler-generated iterator of a "yield" method reads
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal)) && C != "System.Environment::get_CurrentManagedThreadId"), Is.Empty);
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.Base.Yaml::", StringComparison.Ordinal)),
                Is.EqualTo(new[] { "CampertronLibrary.function.Base.Yaml::SettingsDeserializer" }));
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(F => (OpCode)F.GetValue(null)!).ToDictionary(O => O.Value);

        // the methods, constructors, fields and types a method body refers to, read from its IL (also used by ConfigFilesTests)
        internal static IEnumerable<MemberInfo> ReferencedMembers(MethodBase Method)
        {
            byte[]? IL = Method.GetMethodBody()?.GetILAsByteArray();
            if (IL == null)
            {
                yield break;
            }
            Type[]? TypeArguments = Method.DeclaringType != null && Method.DeclaringType.IsGenericType ? Method.DeclaringType.GetGenericArguments() : null;
            Type[]? MethodArguments = Method.IsGenericMethod ? Method.GetGenericArguments() : null;
            int i = 0;
            while (i < IL.Length)
            {
                short Value = IL[i] == 0xFE ? (short)(0xFE00 | IL[i + 1]) : IL[i];
                i += IL[i] == 0xFE ? 2 : 1;
                OpCode Op = OpCodesByValue[Value];
                switch (Op.OperandType)
                {
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        yield return Method.Module.ResolveMember(BitConverter.ToInt32(IL, i), TypeArguments, MethodArguments)!;
                        i += 4;
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                        i += 4 + 4 * BitConverter.ToInt32(IL, i);
                        break;
                    default:
                        i += 4;
                        break;
                }
            }
        }
    }
}
