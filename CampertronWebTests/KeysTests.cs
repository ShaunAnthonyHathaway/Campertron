using System.Net;
using System.Xml.Linq;

namespace CampertronWebTests
{
    // Data Protection's keys: in Web.keys of the settings folder, for its owner only, kept over a restart, and nowhere
    // else (D81, D92)
    public class KeysTests
    {
        private static readonly UnixFileMode FolderOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        private static readonly UnixFileMode FileOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        [Test]
        public async Task TheKeys_AreInTheSettingsFolder_ForTheirOwnerOnly()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            await Visitor.Get("/signin");
            string Keys = Path.Join(Host.Folder, "Web.keys");
            string[] Files = Directory.GetFiles(Keys);
            Assert.That(Files, Has.Length.EqualTo(1), "one key, written by the first form");
            Assert.That(Path.GetFileName(Files[0]), Does.Match(@"^key-[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}\.xml$"));
            Assert.That(XElement.Load(Files[0]).Name.LocalName, Is.EqualTo("key"));
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            Assert.That(File.GetUnixFileMode(Keys), Is.EqualTo(FolderOnly));
            Assert.That(File.GetUnixFileMode(Files[0]), Is.EqualTo(FileOnly));
            Assert.That(Directory.GetFileSystemEntries(Host.Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Web.keys", "Web.password" }));
        }

        [Test]
        public async Task ARestart_KeepsTheKeys_SoAFormFromBeforeItIsStillAccepted_AndAnotherFoldersIsNot()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                string Token, Cookie, KeyFile;
                await using (WebTestHost Before = await WebTestHost.Start(WithPassword: true, Folder: Folder))
                {
                    using Browser Visitor = Before.Browser();
                    await Visitor.Get("/signin");
                    Token = Visitor.Token!;
                    Cookie = Visitor.Cookie("campertron-form-0")!;
                    KeyFile = Directory.GetFiles(Path.Join(Folder, "Web.keys")).Single();
                }
                await using (WebTestHost After = await WebTestHost.Start(Folder: Folder))
                {
                    using Browser Same = After.Browser();
                    Same.Cookies.Add(After.Root, new Cookie("campertron-form-0", Cookie));
                    Same.Token = Token;
                    WebPage Posted = await Same.Post("/signin", ("Password", "wrong password"));
                    Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.OK), "the form's token from before the restart is read with the kept key");
                    Assert.That(Directory.GetFiles(Path.Join(Folder, "Web.keys")), Is.EqualTo(new[] { KeyFile }), "no new key");
                }
                await using WebTestHost Elsewhere = await WebTestHost.Start(WithPassword: true);
                using Browser Other = Elsewhere.Browser();
                Other.Cookies.Add(Elsewhere.Root, new Cookie("campertron-form-0", Cookie));
                Other.Token = Token;
                Assert.That((await Other.Post("/signin", ("Password", WebTestHost.Password))).Status, Is.EqualTo(HttpStatusCode.BadRequest), "another settings folder has other keys");
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public void AKeysFolderOthersCanEnter_IsMadeOwnerOnly_AndOneThatCantKeepKeysIsRefused()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            string Folder = WebTestHost.NewFolder();
            try
            {
                string Keys = Path.Join(Folder, "Web.keys");
                Directory.CreateDirectory(Keys, FolderOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                Assert.That(new WebKeyRepository(Folder).Prepare(), Is.Null);
                Assert.That(File.GetUnixFileMode(Keys), Is.EqualTo(FolderOnly));
                Assert.That(Directory.GetFileSystemEntries(Keys), Is.Empty, "the write that tried the folder left nothing");

                Directory.Delete(Keys);
                File.WriteAllText(Keys, "not a folder");
                Assert.That(new WebKeyRepository(Folder).Prepare(), Is.EqualTo("Web.keys in the settings folder is a link or a file, not a folder."));
                File.Delete(Keys);

                string Target = Path.Join(Folder, "elsewhere");
                Directory.CreateDirectory(Target);
                Directory.CreateSymbolicLink(Keys, Target);
                Assert.That(new WebKeyRepository(Folder).Prepare(), Is.EqualTo("Web.keys in the settings folder is a link or a file, not a folder."), "keys never follow a link out of the settings folder");
                Assert.That(Directory.GetFileSystemEntries(Target), Is.Empty);
                Directory.Delete(Keys);

                if (Environment.UserName != "root")
                {
                    File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    try
                    {
                        string? Problem = new WebKeyRepository(Folder).Prepare();
                        Assert.That(Problem, Is.EqualTo("Web.keys can't be created or written in the settings folder."));
                        Assert.That(Problem, Does.Not.Contain(Folder), "no path in the words");
                    }
                    finally
                    {
                        File.SetUnixFileMode(Folder, FolderOnly);
                    }
                }
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public void AKeyFile_IsWrittenWhole_NeverOverAnother_AndOnlyRegularXmlFilesAreRead()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                var Keys = new WebKeyRepository(Folder);
                Assert.That(Keys.Prepare(), Is.Null);
                Keys.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-one");
                Assert.That(() => Keys.StoreElement(new XElement("key", new XAttribute("id", "2")), "key-one"), Throws.InstanceOf<IOException>(), "a key file is never replaced");
                Keys.StoreElement(new XElement("key", new XAttribute("id", "3")), "../outside");
                Assert.That(Directory.GetFiles(Folder), Is.Empty, "a name with a path becomes a name of the repository's own");
                File.WriteAllText(Path.Join(Keys.Folder, "notes.txt"), "<key id=\"no\" />");
                Directory.CreateDirectory(Path.Join(Keys.Folder, "folder.xml"));
                Assert.That(Keys.GetAllElements().Select(Key => (string?)Key.Attribute("id")), Is.EquivalentTo(new[] { "1", "3" }));
                Assert.That(Directory.GetFiles(Keys.Folder, "*.tmp"), Is.Empty);
                if (!OperatingSystem.IsWindows())
                {
                    Assert.That(Directory.GetFiles(Keys.Folder, "key-*.xml").Select(File.GetUnixFileMode), Has.All.EqualTo(FileOnly));
                }
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }
    }
}
