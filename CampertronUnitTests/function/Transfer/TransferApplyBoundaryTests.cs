using System.Reflection;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // Phase 16b's boundary (D134; v0.4.0-settings-transfer.md §Phase 16b boundary, §Monitoring, refresh, search and editor
    // coordination, §Logging and redaction, §Interface parity), read from the apply's IL and the applications' sources: it
    // writes only through the safe writers, its own protected writer, its backup set and its lock; it reaches no monitoring,
    // search, refresh, campground data, cache, Camp.History, console, log, environment value or secret resolver; it keeps no
    // journal and no start reads it; no application uses it yet (phases 17 to 19); phase 16a's package is untouched by it
    public class TransferApplyBoundaryTests : TransferTestBase
    {
        private static readonly Assembly Library = typeof(TransferImport).Assembly;
        private const string ApplyNamespace = "CampertronLibrary.function.Transfer.Apply";

        private static IEnumerable<Type> ApplyTypes() => Library.GetTypes().Where(T => T.Namespace == ApplyNamespace && !T.IsNested);

        private static List<string> Referenced() => TransferIl.Members(ApplyTypes()).Select(TransferIl.Name).Distinct().OrderBy(N => N, StringComparer.Ordinal).ToList();

        [Test]
        public void TheApply_IsTheseTypes_AllInternal()
        {
            Assert.That(ApplyTypes().Where(T => T.IsPublic).Select(T => T.Name), Is.Empty);
            Assert.That(ApplyTypes().Select(T => T.Name).Order(StringComparer.Ordinal), Is.EqualTo(new[]
            {
                "TransferApply", "TransferApplyHost", "TransferApplyLock", "TransferApplyOutcome", "TransferApplyResult", "TransferBackupSet", "TransferImport",
                "TransferImportChoices", "TransferImportFailure", "TransferImportFileChange", "TransferImportProblem", "TransferImportSecretDecision",
                "TransferImportSecretSource", "TransferImportWording", "TransferLockProblem", "TransferOldSecret", "TransferOutputToDecision", "TransferPlan",
                "TransferPreview", "TransferPreviewFile", "TransferPreviewOutputTo", "TransferPreviewSecret", "TransferProtection", "TransferSecretWrite", "TransferWrite"
            }));
        }

        [Test]
        public void TheApply_WritesOnlyThroughTheSafeWriters_ItsProtectedWriter_ItsBackupSet_AndItsLock()
        {
            List<string> Members = Referenced();
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Base.ConfigFileReplace::Replace").And.Contain("CampertronLibrary.function.Base.ConfigFileReplace::Create"),
                "the scan sees the safe writers");
            // the file system's writing members, each named by where the design allows it
            string[] Writers =
            {
                "System.IO.File::Move",              // the new secret file into place, create-only (overwrite: false)
                "System.IO.File::Delete",            // a created file, a new or superseded secret file, a temporary or backup set file, each after its proof
                "System.IO.File::SetUnixFileMode",   // a new file or folder of Campertron's own, never an existing settings file
                "System.IO.Directory::Move",         // the backup set into place
                "System.IO.Directory::Delete",       // the previous backup set's folder, emptied of its own files only (not recursive)
                "System.IO.Directory::CreateDirectory",
                "System.IO.FileSystemAclExtensions::Create",
                "System.IO.FileInfo::Delete", "System.IO.FileSystemInfo::Delete",
                "System.IO.Stream::Write", "System.IO.FileStream::.ctor", "System.IO.FileStream::Flush"
            };
            string[] NeverWriters =
            {
                "System.IO.File::WriteAllText", "System.IO.File::WriteAllBytes", "System.IO.File::AppendAllText", "System.IO.File::Copy", "System.IO.File::Replace",
                "System.IO.File::Create", "System.IO.File::OpenWrite", "System.IO.File::SetAttributes", "System.IO.File::SetLastWriteTime", "System.IO.FileSystemAclExtensions::SetAccessControl",
                "System.IO.StreamWriter::.ctor"
            };
            foreach (string Never in NeverWriters)
            {
                Assert.That(Members.Where(M => M.StartsWith(Never, StringComparison.Ordinal)), Is.Empty, Never);
            }
            List<string> Unknown = Members.Where(M => M.StartsWith("System.IO.", StringComparison.Ordinal)
                && Regex.IsMatch(M, @"::(Write|Delete|Move|Create|Set|Append|Copy|Replace|Flush|\.ctor)") && !M.StartsWith("System.IO.Path::", StringComparison.Ordinal)
                && !M.StartsWith("System.IO.FileInfo::.ctor", StringComparison.Ordinal) && !M.StartsWith("System.IO.DirectoryInfo::.ctor", StringComparison.Ordinal)
                && !M.StartsWith("System.IO.FileStreamOptions::", StringComparison.Ordinal) && !M.StartsWith("System.IO.IOException::", StringComparison.Ordinal)
                && !M.StartsWith("System.IO.InvalidDataException::", StringComparison.Ordinal))
                .Where(M => !Writers.Any(W => M.StartsWith(W, StringComparison.Ordinal))).ToList();
            Assert.That(Unknown, Is.Empty, "a writing member the design does not name");
        }

        [Test]
        public void TheApply_ReachesNoMonitoring_Search_Refresh_CampgroundData_Console_OrEnvironmentValue()
        {
            List<string> Members = Referenced();
            string[] Forbidden =
            {
                "MonitoringService", "MonitoringController", "MonitoringLock::TryAcquire", "CampsiteSearch", "ConsoleSearch", "RidbRefresh", "RidbStaged", "Cache::", "Load::RunConsoleSearch",
                "Load::GetConfig", "Load::PrepareDatabase", "Menu::", "System.Console::", "CampertronLibrary.function.Base.ConsoleUi", "System.Environment::", "System.Diagnostics.",
                "System.Net.", "Microsoft.Data.Sqlite", "Microsoft.EntityFrameworkCore", "SmtpPassword::Resolve", "SmtpPassword::ResolveOnce", "SmtpPassword::ForSending",
                "SmtpPassword::SelectedSource", "SmtpPassword::VariableIsSet", "NtfyToken::Resolve", "NtfyToken::ResolveOnce", "NtfyToken::SelectedSource", "NtfyToken::VariableIsSet",
                "::get_GetEnvironment", "TestNotification", "NtfyPublisher", "Yaml::CampertronConfigGenerateSampleConfig", "Yaml::CreateSample", "Yaml::GeneralConfigGetConfig",
                "SearchFiles::Create", "ConfigEditSession`1::.ctor", "ConfigEditSession`1::Save", "TransferExport::Build", "WebPassword", "WebSetupCode", "FormHost"
            };
            foreach (string Prefix in Forbidden)
            {
                Assert.That(Members.Where(M => M.Contains(Prefix, StringComparison.Ordinal)), Is.Empty, Prefix);
            }
            // the monitoring lock's classification of a refused open is its convention, not its lock
            Assert.That(Members, Does.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringLock::IsHeld"));
            // no editor, form or console host: the candidate reads the saved files only, never an editor's unsaved text; of the
            // editors' session only its static comparison of two values (a constructed generic's name holds its argument)
            Assert.That(Members.Where(M => M.StartsWith("CampertronLibrary.function.Base.ConfigEditSession`1", StringComparison.Ordinal) && !M.EndsWith("::SameValue", StringComparison.Ordinal)), Is.Empty,
                "an editor's session");
            Assert.That(Members.Where(M => M.StartsWith("CampertronLibrary.function.Forms.", StringComparison.Ordinal) || M.StartsWith("CampertronLibrary.function.Interactive.", StringComparison.Ordinal)), Is.Empty,
                "the form layer or the interactive console");
            Assert.That(Members, Does.Contain(Members.First(M => M.StartsWith("CampertronLibrary.function.Base.ConfigEditSession`1", StringComparison.Ordinal))), "the scan sees the comparison");
        }

        [Test]
        public void TheApply_NamesNoNeverCarriedFile_AndKeepsNoJournal()
        {
            List<string> Literals = TransferIl.Literals(ApplyTypes());
            foreach (string Never in new[] { "Camp.History", "RecreationDotOrg", "preferences.json", "Web.keys", "CampertronCache", "CampertronConfig", "CampertronExtract", "LocalApplicationData", "journal", "recovery." })
            {
                Assert.That(Literals.Where(L => L.Contains(Never, StringComparison.OrdinalIgnoreCase)), Is.Empty, Never);
            }
            // no start reads anything of an import: outside the transfer core nothing in the library refers to the apply
            List<string> Outside = TransferIl.Members(Library.GetTypes().Where(T => T.Namespace == null || !T.Namespace.StartsWith("CampertronLibrary.function.Transfer", StringComparison.Ordinal)))
                .Select(TransferIl.Name).Where(M => M.Contains(ApplyNamespace, StringComparison.Ordinal)).Distinct().ToList();
            Assert.That(Outside, Is.Empty, "no start, loader or form refers to the apply");
        }

        [Test]
        public void ThePackage_DoesNotReachTheApply()
        {
            // phase 16a's package stays as it was: none of its types refers to the apply
            List<string> FromPackage = TransferIl.Members(Library.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.Transfer"))
                .Select(TransferIl.Name).Where(M => M.Contains(ApplyNamespace, StringComparison.Ordinal)).ToList();
            Assert.That(FromPackage, Is.Empty);
        }

        private static string RepositoryRoot()
        {
            for (DirectoryInfo? At = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); At != null; At = At.Parent)
            {
                if (File.Exists(Path.Join(At.FullName, "Campertron.sln")))
                {
                    return At.FullName;
                }
            }
            Assert.Fail("the repository was not found above the test folder");
            return string.Empty;
        }

        [Test]
        public void NoApplication_UsesTheApply_InPhase16b()
        {
            // the adapters are phases 17 to 19, each with its own approval: no application names the import, its preview or its apply
            var Sources = new[] { "CampertronConsole", "CampertronGUI", "CampertronWeb" }
                .SelectMany(Project => Directory.EnumerateFiles(Path.Join(RepositoryRoot(), Project), "*", SearchOption.AllDirectories))
                .Where(F => !Regex.IsMatch(F.Replace('\\', '/'), "/(bin|obj)/") && Regex.IsMatch(F, @"\.(cs|cshtml|axaml|js|css|html)$")).ToList();
            Assert.That(Sources, Has.Count.GreaterThan(50));
            foreach (string Source in Sources)
            {
                string Text_ = File.ReadAllText(Source);
                Assert.That(Text_, Does.Not.Contain("Transfer.Apply").And.Not.Match(@"\bTransfer(Import|Apply|Preview|BackupSet|Protection|ApplyLock)\b").And.Not.Contain("Campertron.transfer."), Source);
            }
        }

        [Test]
        public void TheApply_IsOnTheOneFramework_AndItsSentencesHoldNoPath()
        {
            Assert.That(Library.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName, Is.EqualTo(".NETCoreApp,Version=v11.0"));
            var Sentences = Enum.GetValues<TransferImportProblem>().Where(P => P != TransferImportProblem.FailedNotRestored)
                .Select(P => { try { return TransferImportWording.Apply(P).Message; } catch (ArgumentException) { return TransferImportWording.Preview(P, "Trip.yaml", "the detail").Message; } })
                .Append(TransferImportWording.NotRestored(new[] { "General.yaml" }, new[] { "Email.yaml", TransferImportWording.SecretFileWords(TransferSecretKind.SmtpPassword) }).Message)
                .Concat(new[] { TransferImportWording.CleanupWarning, TransferImportWording.BackupWords, TransferImportWording.NtfyIncomplete, TransferImportWording.PathIsThisComputers, TransferImportWording.Applied })
                .Concat(TransferImportWording.RestartWords).ToList();
            Assert.That(Sentences, Has.Count.GreaterThan(30));
            foreach (string Sentence in Sentences)
            {
                Assert.That(Sentence, Does.Not.Contain("/").And.Not.Contain("\\").And.Not.Contain(TransferApplyLock.FileName).And.Not.Contain("{"), Sentence);
                Assert.That(Sentence, Does.EndWith(".").Or.EndWith(".)").Or.EndWith("detail"), Sentence);
            }
        }
    }
}
