using CampertronLibrary.function.Base;
using static CampertronUnitTests.function.Base.YamlDocumentTesting;

namespace CampertronUnitTests.function.Base
{
    // What the YAML document editor refuses rather than normalizes, and what it keeps but locks. A refused document or edit
    // never returns text. All in memory.
    public class YamlDocumentRefusalTests
    {
        private static YamlDocumentResult EditEmail(string Text, params YamlDocumentEdit[] Edits) => YamlDocument.Edit(Text, Edits, new EmailConfig());

        // ---- documents ----

        [Test]
        public void AMalformedDocument_IsRefused_WithItsPosition()
        {
            const string Text = "smtpServer: x\nsmtpPort: [25\n";

            YamlDocumentDiagnostic D = Refused(YamlDocument.Analyze(Text), YamlDocumentCode.Malformed);
            Assert.That(D.Line, Is.GreaterThan(1));
            Refused(EditEmail(Text, YamlDocumentEdit.SetScalar("smtpServer", "y")), YamlDocumentCode.Malformed);
        }

        [TestCase("")]
        [TestCase("\n\n")]
        [TestCase("   \n")]
        [TestCase("# only a comment\n")]
        [TestCase("# one\n\n# two")]
        public void AnEmptyOrCommentOnlyDocument_IsRefused(string Text)
        {
            // Campertron's loader reads no settings from it either (it reports such a file as unusable, D37)
            Refused(YamlDocument.Analyze(Text), YamlDocumentCode.NoContent);
            Refused(EditEmail(Text, YamlDocumentEdit.SetScalar("smtpServer", "y")), YamlDocumentCode.NoContent);
        }

        [Test]
        public void ADuplicateKey_IsRefused()
        {
            YamlDocumentDiagnostic D = Refused(YamlDocument.Analyze("smtpPort: 1\nsmtpServer: x\nsmtpPort: 2\n"), YamlDocumentCode.DuplicateKey);
            Assert.That((D.Key, D.Line, D.Column), Is.EqualTo(("smtpPort", 3, 1)));
            // also inside a section Campertron does not know
            Refused(YamlDocument.Analyze("smtpPort: 1\nextra:\n  a: 1\n  a: 2\n"), YamlDocumentCode.DuplicateKey);
        }

        [Test]
        public void AMergeKey_IsRefused()
        {
            Refused(YamlDocument.Analyze("base: &b {smtpPort: 1}\n<<: *b\n"), YamlDocumentCode.MergeKey);
            Refused(YamlDocument.Analyze("smtpPort: 1\nextra:\n  <<: {a: 1}\n"), YamlDocumentCode.MergeKey);
        }

        [Test]
        public void MoreThanOneDocument_IsRefused()
        {
            YamlDocumentDiagnostic D = Refused(YamlDocument.Analyze("smtpPort: 1\n---\nsmtpPort: 2\n"), YamlDocumentCode.MultipleDocuments);
            Assert.That(D.Line, Is.EqualTo(2));
            Refused(YamlDocument.Analyze("smtpPort: 1\n... # x\nsmtpServer: y\n"), YamlDocumentCode.MultipleDocuments);
        }

        [TestCase("? smtpPort\n: 25\n")]
        [TestCase("smtpServer: x\n[a, b]: 1\n")]
        [TestCase("smtpServer: x\n? |\n  block\n: 1\n")]
        public void AComplexKey_IsRefused(string Text)
        {
            Refused(YamlDocument.Analyze(Text), YamlDocumentCode.ComplexKey);
        }

        [TestCase("- smtpServer\n")]
        [TestCase("{smtpPort: 1}\n")]
        [TestCase("just text\n")]
        [TestCase("&r\nsmtpPort: 1\n")]
        [TestCase("--- !!map\nsmtpPort: 1\n")]
        [TestCase("---\n")]
        public void ARootThatIsNotAPlainBlockMapping_IsRefused(string Text)
        {
            Refused(YamlDocument.Analyze(Text), YamlDocumentCode.RootNotBlockMapping);
        }

        [TestCase("smtpServer: !secret x\n")]
        [TestCase("smtpServer: ! x\n")]
        [TestCase("smtpServer: x\nextra: !<tag:example.com,2000:t> [1]\n")]
        public void ATagThatIsNotAStandardYamlTag_IsRefused(string Text)
        {
            Refused(YamlDocument.Analyze(Text), YamlDocumentCode.UnsupportedTag);
        }

        [Test]
        public void TextThatNoYamlFileCanHold_IsRefused()
        {
            Refused(YamlDocument.Analyze("smtpServer: a\ud800\n"), YamlDocumentCode.InvalidText);
            Refused(YamlDocument.Analyze("smtpServer: a\udc00b\n"), YamlDocumentCode.InvalidText);
            // YamlDotNet reads NEL, LS and PS as line breaks, unlike this editor's line model
            foreach (string Break in new[] { "\u0085", "\u2028", "\u2029" })
            {
                Refused(YamlDocument.Analyze("smtpServer: 'a" + Break + "b'\n"), YamlDocumentCode.UnsupportedLineBreak);
            }
        }

        // ---- limits ----

        [Test]
        public void TheEditorsLimits_RefuseOversizedOrPathologicalDocuments()
        {
            Refused(YamlDocument.Analyze("smtpServer: " + new string('x', YamlDocument.MaxDocumentLength) + "\n"), YamlDocumentCode.TooLarge);
            Refused(YamlDocument.Analyze("a: [" + string.Join(",", Enumerable.Repeat("0", YamlDocument.MaxParserEvents)) + "]\n"), YamlDocumentCode.TooManyEvents);
            Refused(YamlDocument.Analyze("a: " + new string('[', YamlDocument.MaxDepth) + new string(']', YamlDocument.MaxDepth) + "\n"), YamlDocumentCode.TooDeep);
            Refused(YamlDocument.Analyze("a: &x 1\nb: [" + string.Join(", ", Enumerable.Repeat("*x", YamlDocument.MaxAliases + 1)) + "]\n"), YamlDocumentCode.TooManyAliases);
            // just inside them
            Assert.That(YamlDocument.Analyze("a: " + new string('[', YamlDocument.MaxDepth - 1) + new string(']', YamlDocument.MaxDepth - 1) + "\n").Succeeded, Is.True);
            Assert.That(YamlDocument.Analyze("a: &x 1\nb: [" + string.Join(", ", Enumerable.Repeat("*x", YamlDocument.MaxAliases)) + "]\n").Succeeded, Is.True);
        }

        // ---- kept but locked ----

        [Test]
        public void AnchorsAndAliases_LockTheirProperties_AndTheRestStaysEditable()
        {
            const string Text = "smtpServer: &h mail.example.org # h\nsendFromAddress: *h\nsmtpPort: 25\nsendToAddressList: &l [a]\nsmtpUsername: x\n";

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);
            Assert.That(A.Properties.Select(P => P.Lock), Is.EqualTo(new[]
            {
                YamlDocumentLock.Anchor, YamlDocumentLock.Alias, YamlDocumentLock.None, YamlDocumentLock.Anchor, YamlDocumentLock.None
            }));
            foreach (YamlDocumentEdit Edit in new[]
            {
                YamlDocumentEdit.SetScalar("smtpServer", "x"), YamlDocumentEdit.RemoveProperty("smtpServer"), YamlDocumentEdit.SetScalar("sendFromAddress", "x"),
                YamlDocumentEdit.InsertItem("sendToAddressList", 0, "b"), YamlDocumentEdit.SetSequence("sendToAddressList", new[] { "b" })
            })
            {
                Assert.That(Refused(EditEmail(Text, Edit), YamlDocumentCode.FieldLocked).Key, Is.EqualTo(Edit.Key));
            }

            string Result = Succeeds(Edit<EmailConfig>(Text, M => M.SmtpPort = 26, YamlDocumentEdit.SetScalar("smtpPort", 26)));
            Assert.That(Result, Is.EqualTo(Text.Replace("smtpPort: 25", "smtpPort: 26")));
            Assert.That(Load<EmailConfig>(Result).SendFromAddress, Is.EqualTo("mail.example.org"));
        }

        [Test]
        public void AnAnchorOnAListItem_LocksTheList()
        {
            YamlDocumentAnalysis A = YamlDocument.Analyze("sendToAddressList:\n- &a a@example.com\n- b@example.com\n");

            Assert.That((A.Properties[0].Lock, A.Properties[0].IsEditableSequence), Is.EqualTo((YamlDocumentLock.Anchor, false)));
        }

        [Test]
        public void AStandardTag_IsKept_AndLocksItsProperty()
        {
            const string Text = "smtpPort: !!int 25\nsmtpServer: !!str 007\n";

            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);
            Assert.That(A.Properties.Select(P => P.Lock), Is.EqualTo(new[] { YamlDocumentLock.Tag, YamlDocumentLock.Tag }));
            Refused(EditEmail(Text, YamlDocumentEdit.SetScalar("smtpPort", 26)), YamlDocumentCode.FieldLocked);

            string Result = Succeeds(Edit<EmailConfig>(Text, M => M.SmtpUsername = "x", YamlDocumentEdit.SetScalar("smtpUsername", "x")));
            Assert.That(Result, Is.EqualTo(Text + "smtpUsername: x\n"));
        }

        [Test]
        public void ABlockScalar_IsKept_ButNotRewritten()
        {
            const string Text = "smtpServer: |\n  mail.example.org\nsmtpPort: 25\n";

            Assert.That(YamlDocument.Analyze(Text).Properties[0].Shape, Is.EqualTo(YamlDocumentShape.BlockScalar));
            Refused(EditEmail(Text, YamlDocumentEdit.SetScalar("smtpServer", "x")), YamlDocumentCode.BlockScalar);
            Assert.That(Succeeds(Edit<EmailConfig>(Text, M => M.SmtpPort = 26, YamlDocumentEdit.SetScalar("smtpPort", 26))), Is.EqualTo(Text.Replace("25", "26")));
            // a block scalar in a list makes the list's items not editable; the list can still be removed
            const string List = "smtpServer: x\nsendToAddressList:\n- |\n  a@example.com\n- b@example.com\n";
            Assert.That(YamlDocument.Analyze(List).Properties[1].IsEditableSequence, Is.False);
            Refused(EditEmail(List, YamlDocumentEdit.InsertItem("sendToAddressList", 0, "c")), YamlDocumentCode.UnsupportedSequence);
            Assert.That(Succeeds(Edit<EmailConfig>(List, M => M.SendToAddressList = null, YamlDocumentEdit.RemoveProperty("sendToAddressList"))), Is.EqualTo("smtpServer: x\n"));
        }

        // ---- sequences the editor does not change item by item ----

        [TestCase("sendToAddressList: [a,\n  b]\n")]
        [TestCase("sendToAddressList: [a, # c\n  b]\n")]
        [TestCase("sendToAddressList: [a, b, ]\n")]
        [TestCase("sendToAddressList:\n- [a]\n")]
        [TestCase("sendToAddressList:\n- a: 1\n")]
        [TestCase("sendToAddressList:\n-\n  a\n")]
        [TestCase("sendToAddressList:\n- 'a\n  b'\n")]
        [TestCase("sendToAddressList:\n- \n- b\n")]
        [TestCase("sendToAddressList:\n  - a\n   - b\n")]
        public void ASequenceWhoseItemsCantBeEditedSafely_IsLocked(string Text)
        {
            YamlDocumentAnalysis A = YamlDocument.Analyze(Text);

            Assert.That(A.Succeeded, Is.True, string.Join("; ", A.Diagnostics));
            Assert.That(A.Properties[0].IsEditableSequence, Is.False);
            Refused(EditEmail(Text, YamlDocumentEdit.InsertItem("sendToAddressList", 0, "c")), YamlDocumentCode.UnsupportedSequence);
            Refused(EditEmail(Text, YamlDocumentEdit.SetSequence("sendToAddressList", new[] { "c" })), YamlDocumentCode.UnsupportedSequence);
        }

        [Test]
        public void SequenceEditsNeedAList()
        {
            Refused(EditEmail("sendToAddressList: a@example.com\n", YamlDocumentEdit.SetSequence("sendToAddressList", new[] { "b" })), YamlDocumentCode.NotASequence);
            Refused(EditEmail("smtpServer: x\n", YamlDocumentEdit.RemoveItem("sendToAddressList", 0)), YamlDocumentCode.MissingProperty);
            Refused(EditEmail("smtpServer: x\nsendToAddressList:\n", YamlDocumentEdit.RemoveItem("sendToAddressList", 0)), YamlDocumentCode.NotASequence);
            Refused(EditEmail("smtpServer: x\n", YamlDocumentEdit.InsertItem("sendToAddressList", 1, "a")), YamlDocumentCode.MissingProperty);
        }

        [Test]
        public void ARefusedEdit_LeavesNothingBehind()
        {
            // a valid first edit and an invalid second one: no text, no losses, only the reason
            YamlDocumentResult R = EditEmail(YamlDocumentTests.Email, YamlDocumentEdit.RemoveProperty("smtpPassword"), YamlDocumentEdit.SetScalar("smtpPort", "x"));

            YamlDocumentDiagnostic D = Refused(R, YamlDocumentCode.ValueTypeMismatch);
            Assert.That(D.Key, Is.EqualTo("smtpPort"));
        }
    }
}
