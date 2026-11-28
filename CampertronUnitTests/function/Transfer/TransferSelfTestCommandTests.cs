using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.SelfTest;

namespace CampertronUnitTests.function.Transfer
{
    // CampertronConsole --transfer-selftest (TD-69): the release smoke tests' entry to the transfer package's compatibility
    // self-test. Only the whole command line asks for it; it prints fixed facts only (the runtime, the operating system's
    // architecture, the format, each check with ok or failed, the result), exits 0 only when every check passed, fails closed
    // without AES-GCM, gives an unexpected exception's type and never its message, writes only to the writer it is given and
    // touches no file, environment value or console; it is the transfer's one public type. The release workflow's checks of
    // its lines are release-policy.sh's check_transfer_selftest (test-release-policy.sh).
    public class TransferSelfTestCommandTests : TransferTestBase
    {
        private static readonly string[] Checks =
        {
            "aes-gcm-supported", "pbkdf2-key-length", "aes-gcm-round-trip", "associated-data-enforced", "ciphertext-tamper-refused", "tag-tamper-refused",
            "secret-package-round-trip", "secrets-absent-from-envelope", "wrong-passphrase-refused", "header-tamper-refused", "fresh-salt-and-nonce"
        };

        private static string RuntimeLine => "transfer-selftest runtime=" + RuntimeInformation.RuntimeIdentifier + " " + RuntimeInformation.ProcessArchitecture;
        private static string OsLine => "transfer-selftest os-architecture=" + RuntimeInformation.OSArchitecture;

        private static (int Code, List<string> Lines) Run()
        {
            using var Writer = new StringWriter();
            int Code = TransferSelfTestCommand.Run(Writer);
            return (Code, Writer.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList());
        }

        [Test]
        public void OnlyTheWholeCommandLine_AsksForTheSelfTest()
        {
            Assert.That(TransferSelfTestCommand.Argument, Is.EqualTo("--transfer-selftest"));
            Assert.That(TransferSelfTestCommand.IsRequest(new[] { "--transfer-selftest" }), Is.True);
            foreach (string[]? Args in new[]
            {
                null, Array.Empty<string>(), new[] { "--transfer-selftest", "extra" }, new[] { "extra", "--transfer-selftest" }, new[] { "--transfer-selftest", "--transfer-selftest" },
                new[] { "--transfer-selftest=yes" }, new[] { "--Transfer-Selftest" }, new[] { "--transfer-self-test" }, new[] { "transfer-selftest" }, new[] { "--version" }, new[] { "" }
            })
            {
                Assert.That(TransferSelfTestCommand.IsRequest(Args), Is.False, Args == null ? "null" : string.Join(" ", Args));
            }
        }

        [Test]
        public void ItPrintsOnlyItsFixedFacts_InOrder_AndExitsZero_WhenEveryCheckPassed()
        {
            (int Code, List<string> Lines) = Run();
            var Expected = new List<string> { RuntimeLine, OsLine, "transfer-selftest format=1" };
            Expected.AddRange(Checks.Select(C => "transfer-selftest " + C + "=ok"));
            Expected.Add("transfer-selftest result=ok");
            Assert.That(Lines, Is.EqualTo(Expected));
            Assert.That(Code, Is.EqualTo(0));
            Assert.That(Derivations, Is.InRange(5, 8), "the self-test's few derivations, each one 600,000 iterations");
        }

        [Test]
        public void WithoutAesGcm_ItFailsClosed_WithExitCodeOne()
        {
            Func<bool> Original = TransferCrypto.Availability;
            TransferCrypto.Availability = () => false;
            try
            {
                (int Code, List<string> Lines) = Run();
                Assert.That(Lines, Is.EqualTo(new[]
                {
                    RuntimeLine, OsLine, "transfer-selftest format=1", "transfer-selftest aes-gcm-supported=failed",
                    "transfer-selftest encryption-refused-without-aes-gcm=ok", "transfer-selftest result=failed"
                }));
                Assert.That(Code, Is.EqualTo(1));
            }
            finally
            {
                TransferCrypto.Availability = Original;
            }
        }

        // the self-test's derivations, numbered from 1, with the one given failing as .NET's cryptography would
        private static (int Code, List<string> Lines) RunWithFailingDerivation(int Failing)
        {
            Action Original = TransferCrypto.Derived;
            int Count = 0;
            TransferCrypto.Derived = () =>
            {
                if (Interlocked.Increment(ref Count) == Failing)
                {
                    throw new CryptographicException("a key derivation that went wrong at /home/someone/CampertronConfig");
                }
            };
            try
            {
                return Run();
            }
            finally
            {
                TransferCrypto.Derived = Original;
            }
        }

        [Test]
        public void ACheckThatFails_FailsTheRun_AndNamesOnlyTheChecks()
        {
            // the second derivation is the secret-bearing package's: that check fails, and those that read its package
            (int Code, List<string> Lines) = RunWithFailingDerivation(2);
            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Lines.Take(3), Is.EqualTo(new[] { RuntimeLine, OsLine, "transfer-selftest format=1" }));
            Assert.That(Lines, Does.Contain("transfer-selftest pbkdf2-key-length=ok").And.Contain("transfer-selftest secret-package-round-trip=failed")
                .And.Contain("transfer-selftest wrong-passphrase-refused=failed"));
            Assert.That(Lines.Last(), Is.EqualTo("transfer-selftest result=failed"));
            Assert.That(Lines.Skip(3), Has.All.Match(@"^transfer-selftest [a-z0-9-]+=(ok|failed)$"), "a check's name and ok or failed, never its exception's words");
            Assert.That(string.Join("\n", Lines), Does.Not.Contain("someone").And.Not.Contain("went wrong"));
        }

        [Test]
        public void AFailedFirstDerivation_FailsClosed_HoweverTheSelfTestEnds()
        {
            // without a key the self-test's later checks end it with an exception of their own: the run still fails, in fixed words
            (int Code, List<string> Lines) = RunWithFailingDerivation(1);
            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Lines.Take(2), Is.EqualTo(new[] { RuntimeLine, OsLine }));
            Assert.That(Lines.Last(), Is.EqualTo("transfer-selftest result=failed"));
            Assert.That(Lines, Has.All.Match(@"^transfer-selftest [a-z0-9-]+=[A-Za-z0-9 .-]+$"));
            Assert.That(string.Join("\n", Lines), Does.Not.Contain("someone").And.Not.Contain("went wrong"));
        }

        [Test]
        public void AnUnexpectedException_GivesOnlyItsTypesName_AndFails()
        {
            Func<bool> Original = TransferCrypto.Availability;
            TransferCrypto.Availability = () => throw new DllNotFoundException("libssl.so.3 was not found in /home/someone/secret-folder");
            try
            {
                (int Code, List<string> Lines) = Run();
                Assert.That(Lines, Is.EqualTo(new[] { RuntimeLine, OsLine, "transfer-selftest unexpected-error=System.DllNotFoundException", "transfer-selftest result=failed" }));
                Assert.That(Code, Is.EqualTo(1));
            }
            finally
            {
                TransferCrypto.Availability = Original;
            }
        }

        private static readonly Type Entry = typeof(TransferSelfTestCommand);

        [Test]
        public void TheEntry_IsTheTransfersOnlyPublicType_WithItsThreeMembers()
        {
            Assembly Library = Entry.Assembly;
            Assert.That(Library.GetTypes().Where(T => T.IsPublic && T.Namespace != null && T.Namespace.StartsWith("CampertronLibrary.function.Transfer", StringComparison.Ordinal)).Select(T => T.FullName),
                Is.EqualTo(new[] { "CampertronLibrary.function.Transfer.SelfTest.TransferSelfTestCommand" }));
            Assert.That(Library.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.Transfer.SelfTest").Select(T => T.Name), Is.EqualTo(new[] { "TransferSelfTestCommand" }));
            Assert.That(Entry.IsAbstract && Entry.IsSealed, Is.True, "a static class");
            Assert.That(Entry.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(M => M.Name).Order(StringComparer.Ordinal),
                Is.EqualTo(new[] { "Argument", "IsRequest", "Run" }));
        }

        [Test]
        public void TheEntry_RunsOnlyTheSelfTest_WritesOnlyToItsWriter_AndTouchesNoFileEnvironmentOrConsole()
        {
            List<string> Members = TransferIl.Members(new[] { Entry }).Select(TransferIl.Name).Distinct().OrderBy(N => N, StringComparer.Ordinal).ToList();
            Assert.That(Members.Where(M => M.StartsWith("CampertronLibrary.", StringComparison.Ordinal)), Is.EqualTo(new[]
            {
                "CampertronLibrary.function.Transfer.TransferSelfTest+Report::get_Lines", "CampertronLibrary.function.Transfer.TransferSelfTest+Report::get_Passed",
                "CampertronLibrary.function.Transfer.TransferSelfTest::Run"
            }), "the self-test only: no export, package, reader, writer, import or apply");
            Assert.That(Members.Where(M => M.StartsWith("System.IO.", StringComparison.Ordinal)), Is.EqualTo(new[] { "System.IO.TextWriter::Flush", "System.IO.TextWriter::WriteLine" }),
                "it writes only to the writer it is given");
            foreach (string Forbidden in new[] { "System.Environment::", "System.Console::", "ConsoleUi", "System.Diagnostics.", "System.Net.", "System.Reflection.", "System.Threading." })
            {
                Assert.That(Members.Where(M => M.Contains(Forbidden, StringComparison.Ordinal)), Is.Empty, Forbidden);
            }
            Assert.That(Members.Where(M => M.StartsWith("System.Runtime.InteropServices.RuntimeInformation::", StringComparison.Ordinal)), Is.EqualTo(new[]
            {
                "System.Runtime.InteropServices.RuntimeInformation::get_OSArchitecture", "System.Runtime.InteropServices.RuntimeInformation::get_ProcessArchitecture",
                "System.Runtime.InteropServices.RuntimeInformation::get_RuntimeIdentifier"
            }));
            // its words are its fixed keys; the self-test's own lines are TransferSelfTest's
            Assert.That(TransferIl.Literals(new[] { Entry }).Distinct().Order(StringComparer.Ordinal), Is.EqualTo(new[]
            {
                " ", "--transfer-selftest", "transfer-selftest format=", "transfer-selftest os-architecture=", "transfer-selftest result=failed",
                "transfer-selftest runtime=", "transfer-selftest unexpected-error="
            }));
        }
    }
}
