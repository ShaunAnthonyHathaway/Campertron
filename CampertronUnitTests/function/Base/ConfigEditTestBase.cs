using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Shared by the settings editing session's tests (CM5, D43): each test has its own temporary settings folder, the
    // writer's step seam is restored afterwards, standard output and error are captured (no secret may reach them), and
    // the validation runs at a fixed time. Nothing uses LocalApplicationData, the network or the embedded database.
    public abstract class ConfigEditTestBase
    {
        protected static readonly DateTime Now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        protected string Folder = string.Empty;
        protected StringWriter Out = null!;
        protected StringWriter Err = null!;
        private TextWriter? OriginalOut;
        private TextWriter? OriginalErr;
        private Action<string, string> OriginalStep = null!;

        protected string General => Path.Join(Folder, "General.yaml");
        protected string Email => Path.Join(Folder, "Email.yaml");

        [SetUp]
        public void CreateFolder()
        {
            // the Windows CI job gives a fresh folder in the runner user's LocalApplicationData (where the settings live, with
            // its access rules); elsewhere the temporary folder
            string? UserFolder = Environment.GetEnvironmentVariable("CAMPERTRON_TEST_USER_FOLDER");
            string Parent = !string.IsNullOrEmpty(UserFolder) && Directory.Exists(UserFolder) ? UserFolder : Path.GetTempPath();
            Folder = Path.Join(Parent, "CampertronConfigEditTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Out = new StringWriter();
            Err = new StringWriter();
            OriginalOut = Console.Out;
            OriginalErr = Console.Error;
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalStep = ConfigFileReplace.Step;
        }

        [TearDown]
        public void DeleteFolder()
        {
            ConfigFileReplace.Step = OriginalStep;
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            if (OriginalErr != null)
            {
                Console.SetError(OriginalErr);
            }
            Out.Dispose();
            Err.Dispose();
            if (Directory.Exists(Folder))
            {
                foreach (string File_ in Directory.GetFiles(Folder))
                {
                    File.SetAttributes(File_, FileAttributes.Normal);
                }
                Directory.Delete(Folder, true);
            }
        }

        protected static byte[] Utf8(string Text, bool Bom = false) =>
            (Bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(Text)).ToArray();

        protected static void Write(string Path_, string Text, bool Bom = false) => File.WriteAllBytes(Path_, Utf8(Text, Bom));

        protected static string Text(string Path_) => new UTF8Encoding(false).GetString(File.ReadAllBytes(Path_));

        private protected ConfigEditSession<GeneralConfig> OpenGeneral() => new ConfigEditSession<GeneralConfig>(Folder, () => Now);

        private protected ConfigEditSession<EmailConfig> OpenEmail(bool EmailOutput = false)
        {
            var Session = new ConfigEditSession<EmailConfig>(Folder, () => Now);
            Session.EmailOutput = EmailOutput;
            return Session;
        }

        private protected static string[] Messages<T>(ConfigEditSession<T> Session) where T : class, new() =>
            Session.Notices.Select(N => N.Message).ToArray();

        // every text a session shows: its state, notices, conflict and preview
        private protected static string Everything<T>(ConfigEditSession<T> Session) where T : class, new() =>
            string.Join("\n", new[] { Session.StateReason ?? "", Session.Conflict?.Message ?? "" }
                .Concat(Session.Notices.Select(N => N.ToString()))
                .Concat(Session.Preview.Select(L => L.Text)));

        // Unix permission checks: ignored on Windows (reported as skipped, with the reason); the platform analyzer knows that
        // the rest of the test runs only where Unix file modes exist
        [System.Runtime.Versioning.UnsupportedOSPlatformGuard("windows")]
        protected static bool OnUnix(string WindowsCounterpart)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions (on Windows: " + WindowsCounterpart + ")");
            }
            return !OperatingSystem.IsWindows();
        }

        // the files in the folder (names only), for checking that no temporary file is left
        protected string[] Files() => Directory.GetFiles(Folder).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;
    }
}
