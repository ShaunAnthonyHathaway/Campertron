using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // Phase 16a's boundary (v0.4.0-settings-transfer.md §Phase 16a boundary, §Interface parity, §Threat model): the transfer
    // core writes nothing, reads no environment variable's value, writes nothing to a console, decompresses and unpacks
    // nothing, and finds no settings folder by itself (read from its IL); no application project holds a rule, a word or a
    // primitive of the format (read from their sources); and the compatibility self-test passes here, fails closed without
    // AES-GCM, holds no value in its lines and says only what ran where it ran.
    public class TransferBoundaryTests : TransferTestBase
    {
        private static readonly Assembly Library = typeof(TransferPackage).Assembly;

        private static IEnumerable<Type> TransferTypes() => Library.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.Transfer" && !T.IsNested);

        private static List<string> Referenced(IEnumerable<Type> Types) => TransferIl.Members(Types).Select(TransferIl.Name).Distinct().OrderBy(N => N, StringComparer.Ordinal).ToList();

        [Test]
        public void TheTransferCore_IsTheseTypes_AllInternal()
        {
            Assert.That(TransferTypes().Where(T => T.IsPublic).Select(T => T.Name), Is.Empty);
            Assert.That(TransferTypes().Select(T => T.Name).Order(StringComparer.Ordinal), Is.EqualTo(new[]
            {
                "TransferCrypto", "TransferEntry", "TransferEntryKind", "TransferExport", "TransferExportOffer", "TransferExportResult", "TransferExportSelection",
                "TransferFailure", "TransferFormat", "TransferLockedPackage", "TransferPackage", "TransferPackageReader", "TransferPackageWriter", "TransferPassphrase",
                "TransferProblem", "TransferReadResult", "TransferSecretKind", "TransferSelfTest", "TransferWording", "TransferWriteResult"
            }));
        }

        // what the core may touch of the file system: whether a file or folder exists, a file's length, and reading a stream
        // it is given; the settings and secret files only through the shared readers named below
        private static readonly string[] FileSystemAllowed =
        {
            "System.IO.File::Exists", "System.IO.Directory::Exists", "System.IO.FileInfo::.ctor", "System.IO.FileInfo::get_Length",
            "System.IO.Path::Join", "System.IO.Stream::Read", "System.IO.Stream::ReadAsync", "System.IO.Stream::get_CanSeek",
            "System.IO.Stream::get_Length", "System.IO.Stream::get_Position", "System.IO.IOException::IOException", "System.IO.InvalidDataException::InvalidDataException", "System.IO.Stream::Stream"
        };

        // what the core may use of the rest of the library: the shared readers, loaders, editor and rules, never a writer
        private static readonly string[] LibraryAllowed =
        {
            "CampertronLibrary.function.Base.ConfigFileReplace::Read", "CampertronLibrary.function.Base.ConfigFileReplace+SettingsFile::get_Text",
            "CampertronLibrary.function.Base.ConfigFiles::SortedSearchFileNames", "CampertronLibrary.function.Base.SearchFileName::Check",
            "CampertronLibrary.function.Base.Yaml::LoadSettingsText", "CampertronLibrary.function.Base.Yaml+SettingsLimitException::SettingsLimitException",
            "CampertronLibrary.function.Base.YamlDocument::Analyze", "CampertronLibrary.function.Base.YamlDocument::Edit", "CampertronLibrary.function.Base.YamlDocument::ModelKeys",
            "CampertronLibrary.function.Base.SecretFile::Read", "CampertronLibrary.function.Base.SecretFile::FromBytes", "CampertronLibrary.function.Base.SecretFileCheck::Locate",
            "CampertronLibrary.function.Base.SmtpPassword::SelectedSource", "CampertronLibrary.function.Base.SmtpPassword::ReadFile",
            "CampertronLibrary.function.Base.SmtpPasswordResult::get_Succeeded", "CampertronLibrary.function.Base.SmtpPasswordResult::get_Password", "CampertronLibrary.function.Base.SmtpPasswordResult::get_Problem",
            "CampertronLibrary.function.Base.NtfyToken::SelectedSource", "CampertronLibrary.function.Base.NtfyToken::IsValidToken"
        };

        [Test]
        public void TheScan_SeesTheCoresReaders_AndPrimitives_SoItsRefusalsAreNotEmptyByAccident()
        {
            List<string> Members = Referenced(TransferTypes());
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Base.ConfigFileReplace::Read")
                .And.Contain("CampertronLibrary.function.Base.SecretFile::Read")
                .And.Contain("CampertronLibrary.function.Base.YamlDocument::Edit")
                .And.Contain("System.Security.Cryptography.Rfc2898DeriveBytes::Pbkdf2")
                .And.Contain("System.Security.Cryptography.AesGcm::Encrypt")
                .And.Contain("System.Security.Cryptography.AesGcm::Decrypt")
                .And.Contain("System.IO.Stream::Read"));
            Assert.That(TransferIl.Literals(TransferTypes()), Does.Contain("campertronSettingsPackage").Or.Contain("pbkdf2-sha256"));
        }

        [Test]
        public void TheCore_TargetsCampertronsOneFramework()
        {
            // the format is produced and consumed by the one framework Campertron builds for (net11.0, D31): every combination
            // the tests can run is this one
            var Framework = Library.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();
            Assert.That(Framework!.FrameworkName, Is.EqualTo(".NETCoreApp,Version=v11.0"));
            Assert.That(RuntimeInformation.FrameworkDescription, Does.StartWith(".NET 11."));
        }

        [Test]
        public void TheCore_WritesNothing_AndReadsOnlyThroughTheSharedReaders()
        {
            List<string> Members = Referenced(TransferTypes());
            List<string> FileSystem = Members.Where(M => M.StartsWith("System.IO.", StringComparison.Ordinal)).ToList();
            Assert.That(FileSystem.Except(FileSystemAllowed), Is.Empty, "a file-system member the core may not use");
            List<string> Base = Members.Where(M => M.StartsWith("CampertronLibrary.function.", StringComparison.Ordinal) && !M.StartsWith("CampertronLibrary.function.Transfer.", StringComparison.Ordinal))
                .Where(M => !Regex.IsMatch(M, @"\.(YamlDocumentEdit::RemoveProperty|YamlDocumentAnalysis::get_Properties|YamlDocumentProperty::get_Key|YamlDocumentResult::get_(Succeeded|Text)|(SecretFileProblem|SmtpPasswordSource|NtfyTokenSource|SmtpPasswordProblem|YamlDocumentEdit|YamlDocumentAnalysis|YamlDocumentProperty|YamlDocumentResult|SmtpPasswordResult))$"))
                .ToList();
            Assert.That(Base.Except(LibraryAllowed), Is.Empty, "a library member the core may not use (a writer, a resolver of a secret or of a settings folder)");
            // the settings models' getters only: the core changes a copy of a model by reflection, never writes one
            Assert.That(Members.Where(M => Regex.IsMatch(M, @"^(GeneralConfig|EmailConfig|NtfyConfig|CampertronConfig)::(?!get_)")), Is.Empty);
        }

        [Test]
        public void TheCore_ReadsNoEnvironmentValue_WritesToNoConsole_AndDecompressesNothing()
        {
            List<string> Members = Referenced(TransferTypes());
            string[] Forbidden =
            {
                "System.Environment::", "System.Console::", "CampertronLibrary.function.Base.ConsoleUi", "System.Diagnostics.", "System.IO.Compression.", "System.Formats.Tar.",
                "System.Net.", "System.Text.Json.JsonSerializer", "System.Text.Json.JsonDocument", "System.Text.Json.Nodes.",
                "SmtpPassword::Resolve", "SmtpPassword::ResolveOnce", "SmtpPassword::ForSending", "SmtpPassword::FromEnvironment", "SmtpPassword::KnownSecrets", "SmtpPassword::VariableIsSet",
                "NtfyToken::Resolve", "NtfyToken::ResolveOnce", "NtfyToken::ForPublishing", "NtfyToken::FromEnvironment", "NtfyToken::KnownSecrets", "NtfyToken::VariableIsSet", "::get_GetEnvironment"
            };
            foreach (string Prefix in Forbidden)
            {
                Assert.That(Members.Where(M => M.Contains(Prefix, StringComparison.Ordinal)), Is.Empty, Prefix);
            }
            // cryptography: .NET's own PBKDF2, AES-GCM, random numbers and SHA-256 only
            List<string> Crypto = Members.Where(M => M.StartsWith("System.Security.Cryptography.", StringComparison.Ordinal)).ToList();
            Assert.That(Crypto.Where(M => !Regex.IsMatch(M, @"^System\.Security\.Cryptography\.(Rfc2898DeriveBytes::Pbkdf2|AesGcm(::(\.ctor|Encrypt|Decrypt|Dispose|get_IsSupported))?|RandomNumberGenerator::(GetBytes|GetString)|SHA256::HashData|CryptographicOperations::ZeroMemory|CryptographicException::CryptographicException|HashAlgorithmName::get_SHA256)$")), Is.Empty);
        }

        [Test]
        public void TheCore_NamesNoNeverCarriedFile_AndTheSelfTestTouchesNoFile()
        {
            List<string> Literals = TransferIl.Literals(TransferTypes());
            foreach (string Never in new[] { "Web.yaml", "Web.password", "Web.keys", "Camp.History", "preferences.json", "RecreationDotOrg", ".bak", ".tmp", "gzip", "deflate", "brotli", ".zip", "tar" })
            {
                Assert.That(Literals.Where(L => L.Contains(Never, StringComparison.OrdinalIgnoreCase) && !(Never == "tar" && !Regex.IsMatch(L, @"\btar\b"))), Is.Empty, Never);
            }
            Assert.That(Referenced(new[] { typeof(TransferSelfTest) }).Where(M => M.StartsWith("System.IO.", StringComparison.Ordinal) || M.Contains("TransferExport", StringComparison.Ordinal)), Is.Empty);
        }

        private static string RepositoryRoot()
        {
            for (DirectoryInfo? At = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); At != null; At = At.Parent)
            {
                if (File.Exists(Path.Join(At.FullName, "Campertron.sln")) && Directory.Exists(Path.Join(At.FullName, "CampertronLibrary", "function", "Transfer")))
                {
                    return At.FullName;
                }
            }
            Assert.Fail("the repository was not found above the test folder");
            return string.Empty;
        }

        private static IEnumerable<string> ApplicationSources() =>
            new[] { "CampertronConsole", "CampertronGUI", "CampertronWeb" }
            .SelectMany(Project => Directory.EnumerateFiles(Path.Join(RepositoryRoot(), Project), "*", SearchOption.AllDirectories))
            .Where(F => !Regex.IsMatch(F.Replace('\\', '/'), "/(bin|obj)/") && Regex.IsMatch(F, @"\.(cs|cshtml|axaml|js|css|html)$"));

        [Test]
        public void NoApplication_HoldsTheFormatsWords_Kinds_OrPrimitives()
        {
            // an adapter calls the transfer core and defines no word, limit, kind or name of its own, and reaches no
            // cryptographic API for a package itself (§Interface parity)
            string[] Format = { "campertronSettingsPackage", "manifestSha256", "pbkdf2-sha256", "aes-256-gcm", "campertron-settings-package", "secret-smtp-password", "secret-ntfy-token", "secret-ntfy-topic", "AesGcm", "Rfc2898DeriveBytes", "Pbkdf2", "TransferCrypto", "TransferFormat" };
            var Sources = ApplicationSources().ToList();
            Assert.That(Sources, Has.Count.GreaterThan(50), "the applications' sources were found");
            foreach (string Source in Sources)
            {
                string Text_ = File.ReadAllText(Source);
                foreach (string Word in Format)
                {
                    Assert.That(Text_, Does.Not.Contain(Word), $"{Path.GetRelativePath(RepositoryRoot(), Source)} holds {Word}");
                }
            }
        }

        [Test]
        public void NoApplication_UsesTheTransferCore_InPhase16a()
        {
            // phase 16a has no caller but its tests: the adapters are phases 17 to 19, each with its own approval
            foreach (string Source in ApplicationSources())
            {
                Assert.That(File.ReadAllText(Source), Does.Not.Contain("function.Transfer").And.Not.Match(@"\bTransfer(Export|Package|Passphrase|Wording|SelfTest)\b"), Source);
            }
        }

        [Test]
        public void TheSelfTest_Passes_HoldsNoValue_AndSaysOnlyWhereItRan()
        {
            TransferSelfTest.Report Report = TransferSelfTest.Run();
            Assert.That(Report.Passed, Is.True, string.Join("\n", Report.Lines));
            Assert.That(Report.Checks.Select(C => C.Name), Is.EqualTo(new[]
            {
                "aes-gcm-supported", "pbkdf2-key-length", "aes-gcm-round-trip", "associated-data-enforced", "ciphertext-tamper-refused", "tag-tamper-refused",
                "secret-package-round-trip", "secrets-absent-from-envelope", "wrong-passphrase-refused", "header-tamper-refused", "fresh-salt-and-nonce"
            }));
            List<string> Lines = Report.Lines.ToList();
            string Here = RuntimeInformation.RuntimeIdentifier + " " + RuntimeInformation.ProcessArchitecture;
            Assert.That(Lines[0], Is.EqualTo("transfer-selftest runtime=" + Here));
            Assert.That(Lines.Skip(1), Has.All.Match(@"^transfer-selftest [a-z0-9-]+=(ok|failed)$"), "a line holds a name and ok or failed, never a value");
            // the one runtime it names is this process's: never another release target
            foreach (string Target in new[] { "linux-x64", "linux-arm64", "linux-arm", "win-x64", "win-x86", "win-arm64", "osx-x64", "osx-arm64" }.Where(T => !Here.StartsWith(T + " ", StringComparison.Ordinal)))
            {
                Assert.That(Lines.Where(L => L.Contains(Target + " ", StringComparison.Ordinal)), Is.Empty, Target);
            }
            Assert.That(Derivations, Is.InRange(5, 8), "a few derivations, each one 600,000 iterations");
        }

        [Test]
        public void WithoutAesGcm_EveryEncryptedPath_Refuses_AndTheSelfTestFails()
        {
            byte[] Encrypted_ = Encrypted(Package(Email(), SmtpSecret()));
            TransferReadResult Locked = TransferPackageReader.Open(Encrypted_);
            Func<bool> Original = TransferCrypto.Availability;
            TransferCrypto.Availability = () => false;
            try
            {
                int Before = Derivations;
                Assert.That(TransferPackageWriter.WriteEncrypted(Package(Email(), SmtpSecret()), PassphraseCanary).Failure!.Problem, Is.EqualTo(TransferProblem.EncryptionUnavailable));
                Assert.That(TransferPackageReader.Open(Encrypted_).Failure!.Problem, Is.EqualTo(TransferProblem.EncryptionUnavailable));
                Assert.That(TransferPackageReader.Unlock(Locked.Locked!, PassphraseCanary).Failure!.Problem, Is.EqualTo(TransferProblem.EncryptionUnavailable));
                Assert.That(TransferPackageWriter.WritePlain(Package(Email(), SmtpSecret())).Failure!.Problem, Is.EqualTo(TransferProblem.SecretNotEncrypted), "never a plain package with a secret instead");
                Assert.That(Derivations, Is.EqualTo(Before));
                // a plain package needs no cipher
                Assert.That(Read(Plain(Package(General()))).Package, Is.Not.Null);
                TransferSelfTest.Report Report = TransferSelfTest.Run();
                Assert.That(Report.Passed, Is.False);
                Assert.That(Report.Lines, Does.Contain("transfer-selftest aes-gcm-supported=failed").And.Contain("transfer-selftest encryption-refused-without-aes-gcm=ok").And.Contain("transfer-selftest result=failed"));
            }
            finally
            {
                TransferCrypto.Availability = Original;
            }
        }
    }
}
