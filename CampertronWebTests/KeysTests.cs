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
            string[] Files = Directory.GetFiles(Keys, "*.xml");
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
        public async Task TheCookies_AreNamedWithTheSettingsFoldersOwnShortName_SoTwoWebUisOnOneComputerKeepTheirOwn()
        {
            await using WebTestHost One = await WebTestHost.Start(WithPassword: true);
            await using WebTestHost Two = await WebTestHost.Start(WithPassword: true);
            string Name = File.ReadAllText(Path.Join(One.Folder, "Web.keys", "instance")).Trim();
            Assert.That(Name, Does.Match("^[0-9a-f]{12}$"), "no secret: a random name, made once");
            Assert.That((One.SessionCookie, One.FormCookie), Is.EqualTo(("campertron-session-" + Name, "campertron-form-" + Name)));
            Assert.That(Two.SessionCookie, Is.Not.EqualTo(One.SessionCookie), "a browser keeps cookies by host name, not by port");
            Assert.That(new WebKeyRepository(One.Folder).InstanceId(), Is.EqualTo(Name), "kept");

            //both signed in from one browser (one cookie jar, as for two ports of one host): neither replaces the other's
            using Browser Owner = await One.SignedIn();
            using Browser Same = Two.Browser();
            foreach (Cookie Kept in Owner.Cookies.GetCookies(One.Root).ToList())
            {
                Same.Cookies.Add(Two.Root, new Cookie(Kept.Name, Kept.Value));
            }
            await Same.Get("/signin");
            Assert.That((await Same.Post("/signin", ("Password", WebTestHost.Password))).Location, Is.EqualTo("/"));
            foreach (Cookie Kept in Same.Cookies.GetCookies(Two.Root).ToList())
            {
                Owner.Cookies.Add(One.Root, new Cookie(Kept.Name, Kept.Value));
            }
            Assert.That((await Owner.StartPage()).Status, Is.EqualTo(HttpStatusCode.OK), "still signed in to the first");
            Assert.That((await Same.StartPage()).Status, Is.EqualTo(HttpStatusCode.OK), "and to the second");

            //a file that is no such name is written again
            string Folder = WebTestHost.NewFolder();
            try
            {
                var Keys = new WebKeyRepository(Folder);
                Assert.That(Keys.Prepare(), Is.Null);
                File.WriteAllText(Path.Join(Keys.Folder, "instance"), "../../not a name");
                Assert.That(Keys.InstanceId(), Does.Match("^[0-9a-f]{12}$"));
                Assert.That(Keys.InstanceId(), Is.EqualTo(File.ReadAllText(Path.Join(Keys.Folder, "instance")).Trim()));
                Assert.That(Directory.GetFiles(Keys.Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "instance" }), "no temporary file left");
                if (!OperatingSystem.IsWindows())
                {
                    Assert.That(File.GetUnixFileMode(Path.Join(Keys.Folder, "instance")), Is.EqualTo(FileOnly));
                }
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public async Task ARestart_KeepsTheKeys_SoAFormFromBeforeItIsStillAccepted_AndAnotherFoldersIsNot()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                string Token, Cookie, KeyFile, CookieName;
                await using (WebTestHost Before = await WebTestHost.Start(WithPassword: true, Folder: Folder))
                {
                    using Browser Visitor = Before.Browser();
                    await Visitor.Get("/signin");
                    Token = Visitor.Token!;
                    CookieName = Before.FormCookie;
                    Cookie = Visitor.Cookie(CookieName)!;
                    KeyFile = Directory.GetFiles(Path.Join(Folder, "Web.keys"), "*.xml").Single();
                }
                await using (WebTestHost After = await WebTestHost.Start(Folder: Folder))
                {
                    using Browser Same = After.Browser();
                    Same.Cookies.Add(After.Root, new Cookie(After.FormCookie, Cookie));
                    Same.Token = Token;
                    WebPage Posted = await Same.Post("/signin", ("Password", "wrong password"));
                    Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.OK), "the form's token from before the restart is read with the kept key");
                    Assert.That(Directory.GetFiles(Path.Join(Folder, "Web.keys"), "*.xml"), Is.EqualTo(new[] { KeyFile }), "no new key");
                    Assert.That(After.FormCookie, Is.EqualTo(CookieName), "and the same cookie names: the folder's own short name is kept");
                }
                await using WebTestHost Elsewhere = await WebTestHost.Start(WithPassword: true);
                using Browser Other = Elsewhere.Browser();
                Other.Cookies.Add(Elsewhere.Root, new Cookie(Elsewhere.FormCookie, Cookie));
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
