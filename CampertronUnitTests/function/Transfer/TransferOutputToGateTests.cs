using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The outputTo gate (D71, D73, D132 point 3, D134; v0.4.0-settings-transfer.md §outputTo gate): a change of outputTo to Email
    // or Ntfy is applied only when the monitoring start's own checks pass on the candidate (the candidate-text seam,
    // ConfigCandidate, judged exactly as the same files written to a folder would be); otherwise it is excluded, this computer's
    // outputTo kept, the rest applied; an outputTo that stays Email or Ntfy gets the checks' findings as warnings; no other
    // outputTo is ever chosen, and nothing monitors because of an import
    public class TransferOutputToGateTests : TransferApplyTestBase
    {
        private const string EmailOutput = "outputTo: Email\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n";

        [Test]
        public void AChangeToEmail_ThatTheStartsChecksPass_IsApplied()
        {
            Receiver(Ntfy: false);
            TransferPreview Preview = PreviewOf(ImportPackage(General(EmailOutput), Email(PackageEmail), Search("Trip.yaml", PackageSearch)));
            Assert.That(Preview.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.Accepted));
            Assert.That(Preview.OutputTo.Result, Is.EqualTo(OutputType.Email));
            Assert.That(Preview.OutputTo.Words, Does.Contain(TransferImportWording.OutputToResult(OutputType.Email)), "which future starts monitor");
            Assert.That(TransferImportWording.OutputToResult(OutputType.Email), Does.Contain("monitor by themselves").And.Contain("only after you select Start"));
            Applied(Preview);
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>(Read_("General.yaml"))!.OutputTo, Is.EqualTo(OutputType.Email));
            Assert.That(Steps.Last(S => S.StartsWith("write:", StringComparison.Ordinal)), Is.EqualTo("write:General.yaml"), "General.yaml last");
            Assert.That(File.Exists(In(MonitoringLock.FileName)), Is.False, "nothing was started");
        }

        [Test]
        public void AChangeTheChecksRefuse_IsExcluded_InTheirOwnWords_AndTheRestIsApplied_WithThisComputersOutputTo()
        {
            Receiver(Ntfy: false);
            // no search to check: the start would refuse email mode
            TransferPreview Preview = PreviewOf(ImportPackage(General(EmailOutput), Email(PackageEmail)));
            Assert.That(Preview.CanApply, Is.True, "the rest can be applied");
            Assert.That(Preview.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.Excluded));
            Assert.That(Preview.OutputTo.Result, Is.EqualTo(OutputType.Console), "this computer's outputTo, never another one");
            List<ConfigDiagnostic> Expected = ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Email);
            Assert.That(Expected, Is.Not.Empty);
            Assert.That(Preview.OutputTo.Findings.Select(F => F.Message), Is.EqualTo(Expected.Select(F => F.Message)), "the start's own check and words");
            string Words = Preview.OutputTo.Words.First();
            Assert.That(Words, Does.StartWith("outputTo's change to Email can't be applied").And.Contain(Expected[0].Message).And.EndWith("outputTo stays Console."));
            Assert.That(Preview.Kept, Does.Contain(("General.yaml", "outputTo")));
            Applied(Preview);
            GeneralConfig G = Yaml.LoadSettingsText<GeneralConfig>(Read_("General.yaml"))!;
            Assert.That(G.OutputTo, Is.EqualTo(OutputType.Console));
            Assert.That(G.RefreshRidbDataDayInterval, Is.EqualTo(30), "the package's other settings");
            Assert.That(Read_("Email.yaml"), Does.Contain("mail.example.com"), "the remainder is applied");
        }

        [Test]
        public void AnExcludedChange_KeepsHtmlFile_OrWithoutGeneralYaml_TheDefaultConsole()
        {
            Write("General.yaml", "outputTo: HtmlFile\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n");
            Assert.That(PreviewOf(ImportPackage(General("outputTo: Ntfy\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n"))).OutputTo.Result, Is.EqualTo(OutputType.HtmlFile));
            File.Delete(In("General.yaml"));
            TransferPreview Preview = PreviewOf(ImportPackage(General("outputTo: Ntfy\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n")));
            Assert.That(Preview.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.Excluded));
            Assert.That(Preview.OutputTo.Result, Is.EqualTo(OutputType.Console), "a start without General.yaml creates it with Console");
            Applied(Preview);
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>(Read_("General.yaml"))!.OutputTo, Is.EqualTo(OutputType.Console));
        }

        [Test]
        public void AChangeToConsoleOrHtmlFile_IsApplied_WithoutTheChecks()
        {
            Write("General.yaml", EmailOutput);
            TransferPreview Preview = PreviewOf(ImportPackage(General("outputTo: HtmlFile\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n")));
            Assert.That(Preview.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.Accepted));
            Assert.That(Preview.OutputTo.Words, Is.EqualTo(new[] { "outputTo will be HtmlFile. With it, no start of Campertron monitors." }));
        }

        [Test]
        public void AnOutputToThatStaysEmail_GetsTheChecksFindings_AsWarnings_NotAsAnExcludedChange()
        {
            Write("General.yaml", EmailOutput);
            Write("Email.yaml", PackageEmail);
            TransferPreview Preview = PreviewOf(ImportPackage(General(EmailOutput.Replace("30", "14")), Email(PackageEmail.Replace("587", "465"))));
            Assert.That(Preview.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.Unchanged));
            Assert.That(Preview.OutputTo.Findings, Is.Empty);
            Assert.That(Preview.OutputTo.ResultFindings, Is.Not.Empty, "no search to check: a warning");
            Assert.That(Preview.CanApply, Is.True, "a warning never refuses the import");
            Assert.That(Preview.OutputTo.Words, Has.Some.StartWith("With outputTo: Email, the monitoring would refuse to start after the import: "));

            TransferPreview NotCarried = PreviewOf(ImportPackage(Email(PackageEmail.Replace("587", "2525"))));
            Assert.That(NotCarried.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.NotCarried));
            Assert.That(NotCarried.OutputTo.ResultFindings, Is.Not.Empty);
        }

        [Test]
        public void TheGate_SeesTheCandidatesSearchFiles_ThatDoNotExistYet()
        {
            Receiver(Ntfy: false);
            Assert.That(ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Email), Is.Not.Empty, "the folder as it is has no search");
            TransferPreview Preview = PreviewOf(ImportPackage(General(EmailOutput), Search("Trip.yaml", PackageSearch), Email(PackageEmail)));
            Assert.That(Preview.OutputTo.Decision, Is.EqualTo(TransferOutputToDecision.Accepted));
            Assert.That(File.Exists(In("Trip.yaml")), Is.False, "judged without anything written");
        }

        [Test]
        public void TheGate_JudgesTheCandidate_ExactlyAsTheSameFilesWrittenToAFolder()
        {
            // the candidate-text seam decides nothing of its own: for each candidate, the checks with a ConfigCandidate give what
            // they give for a folder that holds those files
            Write("General.yaml", EmailOutput);
            Write("Email.yaml", "smtpServer: ''\nsmtpPort: 0\n");
            Write("Ntfy.yaml", "enabled: true\nserver: https://ntfy.example.org\n");
            Write("Old.yaml", "displayName: Old\nautoRun: true\ncampgroundID: ''\n");
            var Candidates = new[]
            {
                new Dictionary<string, string?> { ["Email.yaml"] = PackageEmail },
                new Dictionary<string, string?> { ["Email.yaml"] = PackageEmail, ["Trip.yaml"] = PackageSearch },
                new Dictionary<string, string?> { ["Ntfy.yaml"] = PackageNtfy + "topic: abc\n", ["New trip.yaml"] = PackageSearch },
                new Dictionary<string, string?> { ["Ntfy.yaml"] = null, ["Old.yaml"] = "displayName: Old\nautoRun: true\ncampgroundID: '5'\n" },
                new Dictionary<string, string?> { ["Broken.yaml"] = "displayName: x\nautoRun: true\ncampgroundID: '1'\nsearchBy: DaysOut\nsearchValue: -1\n" },
                new Dictionary<string, string?>()
            };
            foreach (Dictionary<string, string?> Files in Candidates)
            {
                string Written = Path.Join(Path.GetTempPath(), "CampertronTransferSeam-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Written);
                try
                {
                    foreach (string File_ in Directory.EnumerateFiles(Folder))
                    {
                        File.Copy(File_, Path.Join(Written, Path.GetFileName(File_)));
                    }
                    foreach ((string Name, string? Text_) in Files)
                    {
                        if (Text_ == null)
                        {
                            File.Delete(Path.Join(Written, Name));
                        }
                        else
                        {
                            File.WriteAllText(Path.Join(Written, Name), Text_);
                        }
                    }
                    var Candidate = new ConfigCandidate(Files);
                    foreach (OutputType Output in new[] { OutputType.Email, OutputType.Ntfy, OutputType.Console })
                    {
                        string Label = Output + " with " + string.Join(",", Files.Keys);
                        Assert.That(Describe(ConfigValidation.MonitoringStartErrors(Folder, Output, DateTime.Now, Candidate)),
                            Is.EqualTo(Describe(ConfigValidation.MonitoringStartErrors(Written, Output, DateTime.Now))), Label);
                        Assert.That(Describe(ConfigValidation.MonitoringSearchErrors(Folder, Output, null, null, Candidate)),
                            Is.EqualTo(Describe(ConfigValidation.MonitoringSearchErrors(Written, Output))), Label);
                    }
                }
                finally
                {
                    Directory.Delete(Written, true);
                }
            }
        }

        private static List<string> Describe(IEnumerable<ConfigDiagnostic> Found) => Found.Select(D => $"{D.Severity} {D.Code} {D.FileName} {D.Key} {D.Message}").ToList();

        [Test]
        public void WithoutACandidate_TheChecksReadTheFolder_AsBefore()
        {
            Receiver(Searches: new[] { "Here.yaml" });
            Assert.That(Describe(ConfigValidation.MonitoringStartErrors(Folder, OutputType.Ntfy, DateTime.Now, null)), Is.EqualTo(Describe(ConfigValidation.MonitoringStartErrors(Folder, OutputType.Ntfy, DateTime.Now))));
            Assert.That(Describe(ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Email, null, null, null)), Is.EqualTo(Describe(ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Email))));
            var Empty = new ConfigCandidate(new Dictionary<string, string?>());
            Assert.That(Describe(ConfigValidation.MonitoringStartErrors(Folder, OutputType.Ntfy, DateTime.Now, Empty)), Is.EqualTo(Describe(ConfigValidation.MonitoringStartErrors(Folder, OutputType.Ntfy, DateTime.Now))),
                "a candidate that holds nothing is the folder");
        }

        [Test]
        public void NoRuleOfTheChecks_IsCopiedIntoTheTransfer()
        {
            // the transfer reaches the readiness checks only through the start's own functions, and names none of their codes
            List<string> Members = TransferIl.Members(typeof(TransferImport).Assembly.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.Transfer.Apply"))
                .Select(TransferIl.Name).Distinct().ToList();
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Base.ConfigValidation::MonitoringStartErrors").And.Contain("CampertronLibrary.function.Base.ConfigValidation::MonitoringSearchErrors"));
            Assert.That(Members.Where(M => M.Contains("Load::LoadMonitoringSearches", StringComparison.Ordinal) || M.Contains("::CheckEmail", StringComparison.Ordinal)
                || M.Contains("::CheckNtfy", StringComparison.Ordinal) || M.Contains("::CheckSearchesToCheck", StringComparison.Ordinal) || M.Contains("::SearchesToCheck", StringComparison.Ordinal)), Is.Empty);
            List<string> Literals = TransferIl.Literals(typeof(TransferImport).Assembly.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.Transfer.Apply"));
            Assert.That(Literals.Where(L => L.Contains("enabled search", StringComparison.OrdinalIgnoreCase) || L.Contains("smtpServer is empty", StringComparison.Ordinal)), Is.Empty);
        }
    }
}
