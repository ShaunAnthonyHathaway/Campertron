using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronUnitTests.function.Base
{
    // TD-05: every runtime cache operation uses the configured CachePath (CampertronInternalConfig.CachePath:
    // LocalApplicationData/CampertronCache on the desktop, /cache in a container). Covers the cache-clear boundary
    // (Clear.CheckCacheDirectory, Clear.ClearRefreshCache), the HTML results and the email attachment. The refresh
    // itself is covered in AtomicRefreshTests. Each test uses its own temporary directory with sentinel files around
    // the cache directory, and LocalApplicationData redirected (XDG_DATA_HOME, Linux) into it where it matters.
    public class CachePathTests
    {
        private string Root = string.Empty;
        private string CacheDirectory => Path.Join(Root, "CampertronCache");
        private string? OriginalXdgDataHome;
        private StringWriter Output = null!;
        private TextWriter? OriginalOut;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronCachePathTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CacheDirectory);
            Output = new StringWriter();
            OriginalOut = Console.Out;
            Console.SetOut(Output);
        }

        [TearDown]
        public void TearDown()
        {
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            Output.Dispose();
            if (OriginalXdgDataHome != null)
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", OriginalXdgDataHome.Length > 0 ? OriginalXdgDataHome : null);
                OriginalXdgDataHome = null;
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // ---- the configured paths ----

        [Test]
        public void GetConfig_Desktop_CachePathIsLocalApplicationDataCampertronCache()
        {
            // the run's (isolated) LocalApplicationData, as LoadTests use it
            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") != null)
            {
                Assert.Ignore("desktop mode only");
            }
            string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            CampertronInternalConfig Config = Load.GetConfig();

            Assert.That(Config.ContainerMode, Is.False);
            Assert.That(Config.CachePath, Is.EqualTo(Path.Join(LocalAppData, "CampertronCache")));
            Assert.That(Config.ConfigPath, Is.EqualTo(Path.Join(LocalAppData, "CampertronConfig")));
            Assert.That(Directory.Exists(Config.CachePath), Is.True);
        }

        // ---- the cache-clear boundary ----

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void CheckCacheDirectory_RejectsAnUnsetPath(string? CachePath)
        {
            Assert.That(() => Clear.CheckCacheDirectory(CachePath), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("CachePath is not set"));
            Assert.That(() => Clear.ClearRefreshCache(CachePath!), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CheckCacheDirectory_RejectsTheFilesystemRoot()
        {
            string FsRoot = Path.GetPathRoot(Path.GetTempPath())!;
            Assert.That(() => Clear.CheckCacheDirectory(FsRoot), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("filesystem root"));
            Assert.That(() => Clear.ClearRefreshCache(FsRoot), Throws.TypeOf<InvalidOperationException>());
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(() => Clear.ClearRefreshCache("/"), Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => Clear.ClearRefreshCache("//"), Throws.TypeOf<InvalidOperationException>());
                Assert.That(() => Clear.ClearRefreshCache("/tmp/.."), Throws.TypeOf<InvalidOperationException>());
            }
            Assert.That(Output.ToString(), Does.Not.Contain("Clearing cache"));
        }

        [Test]
        public void CheckCacheDirectory_RejectsTheConfigDirectory_HoweverItIsWritten()
        {
            string ConfigPath = Path.Join(Root, "CampertronConfig");
            Directory.CreateDirectory(ConfigPath);
            foreach (string Alias in new[] { ConfigPath, ConfigPath + Path.DirectorySeparatorChar, Path.Join(ConfigPath, "..", "CampertronConfig"), Path.Join(ConfigPath, ".") })
            {
                Assert.That(() => Clear.CheckCacheDirectory(Alias, ConfigPath), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("config directory"), Alias);
            }
            // the real layouts are accepted unchanged
            Assert.That(Clear.CheckCacheDirectory(CacheDirectory, ConfigPath), Is.EqualTo(CacheDirectory));
            Assert.That(Clear.CheckCacheDirectory("/cache", "/config"), Is.EqualTo("/cache"));
        }

        [Test]
        public void ClearRefreshCache_DeletesOnlyTheFilesDirectlyInTheConfiguredDirectory()
        {
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Cached.json"), "x");
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Campsites.json"), "x");
            File.WriteAllText(Path.Join(CacheDirectory, "results.html"), "x");
            File.WriteAllText(Path.Join(CacheDirectory, ".hidden"), "x");
            Directory.CreateDirectory(Path.Join(CacheDirectory, "sub"));
            File.WriteAllText(Path.Join(CacheDirectory, "sub", "232445-Cached.json"), "kept");
            List<string> Sentinels = WriteSentinelsAroundTheCache();

            Clear.ClearRefreshCache(CacheDirectory);

            Assert.That(Directory.GetFileSystemEntries(CacheDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "sub" }));
            Assert.That(File.ReadAllText(Path.Join(CacheDirectory, "sub", "232445-Cached.json")), Is.EqualTo("kept"));
            AssertSentinelsUntouched(Sentinels);
            Assert.That(Directory.Exists(CacheDirectory), Is.True, "the cache directory itself is kept");
        }

        [Test]
        public void ClearRefreshCache_RemovesSymbolicLinksAsLinks_AndNeverFollowsThem()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("uses Unix symbolic links");
            }
            string OutsideFile = Path.Join(Root, "outside-target.json");
            File.WriteAllText(OutsideFile, "outside");
            string OutsideDirectory = Path.Join(Root, "outside-directory");
            Directory.CreateDirectory(OutsideDirectory);
            File.WriteAllText(Path.Join(OutsideDirectory, "232445-Cached.json"), "outside");
            File.CreateSymbolicLink(Path.Join(CacheDirectory, "link-to-file.json"), OutsideFile);
            File.CreateSymbolicLink(Path.Join(CacheDirectory, "broken-link.json"), Path.Join(Root, "missing.json"));
            Directory.CreateSymbolicLink(Path.Join(CacheDirectory, "link-to-directory"), OutsideDirectory);

            Clear.ClearRefreshCache(CacheDirectory);

            // file links (also a dangling one) are unlinked; a directory link is not a file and stays, unfollowed
            Assert.That(Directory.GetFileSystemEntries(CacheDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { "link-to-directory" }));
            Assert.That(File.ReadAllText(OutsideFile), Is.EqualTo("outside"));
            Assert.That(File.ReadAllText(Path.Join(OutsideDirectory, "232445-Cached.json")), Is.EqualTo("outside"));
        }

        [TestCase("RecreationDotOrg.db")]
        [TestCase("General.yaml")]
        public void ClearRefreshCache_RefusesADirectoryHoldingConfigFiles_AndDeletesNothing(string ConfigFile)
        {
            File.WriteAllText(Path.Join(CacheDirectory, ConfigFile), "config");
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Cached.json"), "x");

            Assert.That(() => Clear.ClearRefreshCache(CacheDirectory), Throws.TypeOf<InvalidOperationException>().With.Message.Contains(ConfigFile));

            Assert.That(Directory.GetFiles(CacheDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { ConfigFile, "232445-Cached.json" }));
        }

        [Test]
        public void ClearRefreshCache_MissingDirectory_Throws_AndCreatesNothing()
        {
            string Missing = Path.Join(Root, "missing-cache");
            Assert.That(() => Clear.ClearRefreshCache(Missing), Throws.TypeOf<DirectoryNotFoundException>());
            Assert.That(Directory.Exists(Missing), Is.False);
        }

        // ---- HTML results and the email attachment ----

        [Test]
        public void WriteHtmlResults_WritesToTheConfiguredCachePath_AndReplacesOnlyItsPreviousHtml()
        {
            string LocalCache = Path.Join(RedirectLocalApplicationData(), "CampertronCache");
            Directory.CreateDirectory(LocalCache);
            File.WriteAllText(Path.Join(LocalCache, "previous.html"), "local");
            File.WriteAllText(Path.Join(CacheDirectory, "previous.html"), "old");
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Cached.json"), "x");
            Directory.CreateDirectory(Path.Join(CacheDirectory, "sub"));
            File.WriteAllText(Path.Join(CacheDirectory, "sub", "nested.html"), "nested");
            List<string> Sentinels = WriteSentinelsAroundTheCache();

            string HtmlFile = CampsiteConfig.WriteHtmlResults(CacheDirectory, "<b>results</b>");

            Assert.That(Path.GetDirectoryName(HtmlFile), Is.EqualTo(CacheDirectory));
            Assert.That(Path.GetFileName(HtmlFile), Does.Match("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\.html$"));
            Assert.That(File.ReadAllText(HtmlFile), Is.EqualTo("<b>results</b>"));
            Assert.That(Directory.GetFiles(CacheDirectory).Select(Path.GetFileName), Is.EquivalentTo(new[] { Path.GetFileName(HtmlFile), "232445-Cached.json" }));
            Assert.That(File.ReadAllText(Path.Join(CacheDirectory, "sub", "nested.html")), Is.EqualTo("nested"));
            Assert.That(Directory.GetFiles(LocalCache).Select(Path.GetFileName), Is.EquivalentTo(new[] { "previous.html" }), "LocalApplicationData untouched");
            AssertSentinelsUntouched(Sentinels);

            // the next results replace this one
            string Next = CampsiteConfig.WriteHtmlResults(CacheDirectory, "next");
            Assert.That(File.Exists(HtmlFile), Is.False);
            Assert.That(Directory.GetFiles(CacheDirectory, "*.html"), Is.EqualTo(new[] { Next }));
        }

        [Test]
        public void WriteHtmlResults_ContainerLayout_DoesNotCreateALocalApplicationDataCache()
        {
            string LocalAppData = RedirectLocalApplicationData();
            string ContainerCache = Path.Join(Root, "cache");
            Directory.CreateDirectory(ContainerCache);

            string HtmlFile = CampsiteConfig.WriteHtmlResults(ContainerCache, "results");

            Assert.That(Path.GetDirectoryName(HtmlFile), Is.EqualTo(ContainerCache));
            Assert.That(Directory.Exists(Path.Join(LocalAppData, "CampertronCache")), Is.False);
            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty);
        }

        [Test]
        public void WriteHtmlResults_CreatesAMissingCacheDirectory_AsBefore()
        {
            // unchanged behavior of the HTML output: a missing cache directory is created (now the configured one)
            string Missing = Path.Join(Root, "new-cache");
            string HtmlFile = CampsiteConfig.WriteHtmlResults(Missing, "results");
            Assert.That(Path.GetDirectoryName(HtmlFile), Is.EqualTo(Missing));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WriteHtmlResults_UnsetCachePath_Throws_WithoutAFallback(string? CachePath)
        {
            string LocalAppData = RedirectLocalApplicationData();
            Assert.That(() => CampsiteConfig.WriteHtmlResults(CachePath, "results"), Throws.TypeOf<InvalidOperationException>());
            Assert.That(Directory.GetFileSystemEntries(LocalAppData), Is.Empty);
        }

        [Test]
        public void CreateResultsEmail_AttachesTheHtmlFileFromTheConfiguredCachePath()
        {
            string ContainerCache = Path.Join(Root, "cache");
            string HtmlFile = CampsiteConfig.WriteHtmlResults(ContainerCache, "<b>results</b>");
            var Email = new EmailConfig { SendFromAddress = "from@example.invalid", SendToAddressList = new List<string> { "a@example.invalid", "b@example.invalid" } };

            using (MailMessage Message = CampsiteConfig.CreateResultsEmail(Email, "<b>results</b>", HtmlFile))
            {
                Assert.That(Message.Body, Is.EqualTo("<b>results</b>"));
                Assert.That(Message.IsBodyHtml, Is.True);
                Assert.That(Message.Subject, Is.EqualTo("Campertron Auto-Email"));
                Assert.That(Message.To.Select(a => a.Address), Is.EqualTo(new[] { "a@example.invalid", "b@example.invalid" }));
                Assert.That(Message.Attachments, Has.Count.EqualTo(1));
                Attachment Html = Message.Attachments[0];
                Assert.That(Html.Name, Is.EqualTo(Path.GetFileName(HtmlFile)));
                Assert.That(Html.ContentType.MediaType, Is.EqualTo("application/octet-stream"));
                Assert.That(Html.ContentStream, Is.InstanceOf<FileStream>());
                Assert.That(((FileStream)Html.ContentStream).Name, Is.EqualTo(HtmlFile));
                using (var Reader = new StreamReader(Html.ContentStream))
                {
                    Assert.That(Reader.ReadToEnd(), Is.EqualTo("<b>results</b>"));
                }
            }
        }

        [Test]
        public void ProcessConsoleConfig_Email_WritesTheHtmlToTheConfiguredCachePath_BeforeSending()
        {
            // SMTP to a closed loopback port: the connection is refused, so nothing is transmitted
            string LocalAppData = RedirectLocalApplicationData();
            string ContainerCache = Path.Join(Root, "cache");
            Directory.CreateDirectory(ContainerCache);
            var Email = new EmailConfig { SmtpServer = "127.0.0.1", SmtpPort = ClosedLoopbackPort(), SmtpUsername = "u", SmtpPassword = "p", SendFromAddress = "from@example.invalid", SendToAddressList = new List<string> { "to@example.invalid" } };
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            Assert.That(() => CampsiteConfig.ProcessConsoleConfig(Values("hit"), ref Last, new GeneralConfig { OutputTo = OutputType.Email }, Email, ContainerCache),
                        Throws.InstanceOf<SmtpException>());

            string[] Html = Directory.GetFiles(ContainerCache, "*.html");
            Assert.That(Html, Has.Length.EqualTo(1));
            // the email's HTML is the results page for this one search (beta.9); an untyped line is kept, encoded
            string Page = File.ReadAllText(Html[0]);
            Assert.That(Page, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(Page, Does.Contain("<p class=\"note\">hit</p>"));
            Assert.That(Directory.GetFileSystemEntries(LocalAppData), Is.Empty, "nothing written to LocalApplicationData");
            Assert.That(Output.ToString(), Does.Not.Contain("Sent email"));
        }

        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.Email)]
        public void ProcessConsoleConfig_HtmlOutputWithoutCachePath_Throws_BeforeWritingOrOpeningAnything(OutputType Output_)
        {
            string LocalAppData = RedirectLocalApplicationData();
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
            var Opened = new List<string>();
            Action<string> OriginalOpen = CampsiteConfig.OpenHtmlFile;
            CampsiteConfig.OpenHtmlFile = Opened.Add;
            try
            {
                Assert.That(() => CampsiteConfig.ProcessConsoleConfig(Values("hit"), ref Last, new GeneralConfig { OutputTo = Output_ }, new EmailConfig(), null),
                            Throws.TypeOf<InvalidOperationException>());
            }
            finally
            {
                CampsiteConfig.OpenHtmlFile = OriginalOpen;
            }

            Assert.That(Opened, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(LocalAppData), Is.Empty);
        }

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.UnitTest)]
        public void ProcessConsoleConfig_ConsoleOutput_WritesNoFile(OutputType Output_)
        {
            string LocalAppData = RedirectLocalApplicationData();
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            CampsiteConfig.ProcessConsoleConfig(Values("hit"), ref Last, new GeneralConfig { OutputTo = Output_ }, new EmailConfig(), CacheDirectory);

            Assert.That(Output.ToString(), Does.Contain("hit"));
            Assert.That(Directory.GetFileSystemEntries(CacheDirectory), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(LocalAppData), Is.Empty);
        }

        // ---- helpers ----

        private static List<ConsoleConfig.ConsoleConfigValue> Values(string Line) =>
            new List<ConsoleConfig.ConsoleConfigValue> { CampsiteConfig.AddConsoleConfigItem(Line) };

        private static int ClosedLoopbackPort()
        {
            var Listener = new TcpListener(IPAddress.Loopback, 0);
            Listener.Start();
            int Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
            Listener.Stop();
            return Port;
        }

        // files next to the cache directory, in similarly named neighbors and in a config directory
        private List<string> WriteSentinelsAroundTheCache()
        {
            var Sentinels = new List<string>();
            foreach (string Directory_ in new[] { Root, Path.Join(Root, "CampertronCache2"), Path.Join(Root, "CampertronCache.old"), Path.Join(Root, "CampertronCach"), Path.Join(Root, "CampertronConfig") })
            {
                Directory.CreateDirectory(Directory_);
                foreach (string Name in new[] { "232445-Cached.json", "results.html", "General.yaml" })
                {
                    string File_ = Path.Join(Directory_, Name);
                    File.WriteAllText(File_, "sentinel " + File_);
                    Sentinels.Add(File_);
                }
            }
            return Sentinels;
        }

        private static void AssertSentinelsUntouched(List<string> Sentinels)
        {
            foreach (string File_ in Sentinels)
            {
                Assert.That(File.Exists(File_) ? File.ReadAllText(File_) : "missing", Is.EqualTo("sentinel " + File_), File_);
            }
        }

        // points LocalApplicationData (XDG_DATA_HOME, Linux) at an empty directory in the test directory; restored in TearDown
        private string RedirectLocalApplicationData()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("redirects LocalApplicationData through XDG_DATA_HOME (Linux)");
            }
            string LocalAppData = Path.Join(Root, "localappdata");
            Directory.CreateDirectory(LocalAppData);
            OriginalXdgDataHome ??= Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? string.Empty;
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", LocalAppData);
            Assert.That(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Is.EqualTo(LocalAppData));
            return LocalAppData;
        }
    }
}
