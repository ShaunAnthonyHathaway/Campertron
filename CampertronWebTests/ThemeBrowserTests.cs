using System.Text.Json;
using static CampertronWebTests.SystemPageTests;

namespace CampertronWebTests
{
    // The web UI's theme (D110) in a real browser: headless Chrome (Chrome.cs: a profile of its own, nothing beyond the
    // loopback address) with its light or dark setting as the test sets it (prefers-color-scheme). System follows that
    // setting, also when it changes while a page is open; Light and Dark keep their theme whatever it is; every page is
    // drawn in one theme only (no color of the other), from its first paint. The choice is made with the keyboard on the
    // system page, and kept across the pages, a reload, a sign-out and a sign-in and a restart of the browser; each
    // browser keeps its own. The focus is seen in both themes, and a narrow window never scrolls sideways.
    public class ThemeBrowserTests
    {
        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        // the pages a signed-in browser opens (with the start page and a page that is not there)
        private static readonly string[] Pages =
        {
            "", "searches", "results", "monitoring", "settings/general", "settings/email", "settings/ntfy", "settings/web", "settings/system", "settings/system?confirm=refresh",
            "campgrounds/park", "campgrounds/state", "guided-setup", "account/password", "nothing-here"
        };

        // a web UI with a search file and a signed-in Chrome; a failing step leaves nothing behind
        private sealed class Scene : LiveScene
        {
            internal SystemHost S = null!;
            protected override WebTestHost? Web => S?.Host;
            protected override ValueTask DisposeWeb() => S.DisposeAsync();
            internal string Root => S.Host.Root.ToString();

            internal static Task<Scene> Start(int Width = 1024, int Height = 768, string? Theme = null, string? Prefers = null, bool SignIn = true)
            {
                Chrome.Require();
                return Build(new Scene(), async Sc =>
                {
                    Sc.S = await SystemHost.Start();
                    File.WriteAllText(Sc.S.In("Alpha.yaml"), ResultsPageTests.Search("First trip"));
                    await Sc.Browse(Width: Width, Height: Height, Theme: Theme, Prefers: Prefers);
                    if (SignIn)
                    {
                        await Sc.SignIn();
                    }
                });
            }

            // the browser, closed and started again on its own profile
            internal async Task RestartBrowser(int Width = 1024, int Height = 768) => B = await B.Restart(Width, Height);
        }

        // the theme the page is drawn in, for a choice (null: System) and the browser's setting
        private static string Drawn(string? Theme, string Prefers) => Theme ?? Prefers;
        private static string Scheme(string? Theme) => Theme ?? "light dark";

        // ---- System, Light and Dark on every page, whatever the browser's setting ----

        [TestCase(null, "light")]
        [TestCase(null, "dark")]
        [TestCase("light", "dark")]
        [TestCase("dark", "light")]
        public async Task EveryPage_IsDrawnInTheChosenTheme_OrTheBrowsersForSystem_AndNeverInTheOther(string? Theme, string Prefers)
        {
            await using Scene Sc = await Scene.Start(Theme: Theme, Prefers: Prefers, SignIn: false);
            Chrome B = Sc.B;
            await B.Open(Sc.Root + "signin");
            await ThemeLook.Check(B, Drawn(Theme, Prefers), Scheme(Theme), "the sign-in");
            await Sc.SignIn();
            foreach (string Page in Pages)
            {
                await B.Open(Sc.Root + Page);
                Assert.That(await ThemeLook.Asked(B), Is.EqualTo((Theme ?? "system", Scheme(Theme))), Page);
                await ThemeLook.Check(B, Drawn(Theme, Prefers), Scheme(Theme), "/" + Page);
                Assert.That(await B.Number("document.documentElement.scrollWidth"), Is.LessThanOrEqualTo(1024), Page + ": nothing sideways");
            }
            //the system page says which theme it is shown in, the word the browser's setting gives for System
            await B.Open(Sc.Root + "settings/system");
            string Name = Drawn(Theme, Prefers) == "dark" ? "Dark" : "Light";
            Assert.That(await B.Text("document.getElementById('theme-shown').innerText"), Does.StartWith("Campertron is shown in its " + Name + " theme"));
            Assert.That(await B.Is("document.getElementById('theme-" + (Theme ?? "system") + "').checked"), Is.True, "the choice is the one checked");
            await Sc.NoErrorsOrViolations();
        }

        [Test]
        public async Task System_FollowsAChangeOfTheBrowsersSetting_WhileThePageIsOpen_AndLightAndDarkDoNot()
        {
            await using Scene Sc = await Scene.Start(Prefers: "light");
            Chrome B = Sc.B;
            await B.Open(Sc.Root + "settings/system");
            await B.Evaluate("window.__kept = 1");
            await ThemeLook.Check(B, "light", "light dark", "System, the browser light");
            await ThemeLook.Prefer(B, "dark");
            await B.Until("getComputedStyle(document.body).backgroundColor === " + JsonSerializer.Serialize(ThemeLook.Palette("dark")["--page"]), "the dark theme, at once");
            await ThemeLook.Check(B, "dark", "light dark", "System, the browser dark");
            Assert.That(await B.Text("document.getElementById('theme-shown').innerText"), Is.EqualTo("Campertron is shown in its Dark theme, as this browser asks."));
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "without a reload");

            //Light: the browser's dark setting, and a change of it, change nothing
            await B.Evaluate("document.getElementById('theme-light').click()");
            await B.Press("#theme-apply");
            await ThemeLook.Check(B, "light", "light", "Light, the browser dark");
            await ThemeLook.Prefer(B, "light");
            await ThemeLook.Prefer(B, "dark");
            await Task.Delay(100);
            await ThemeLook.Check(B, "light", "light", "Light after the browser's changes");
            //Dark: the same the other way
            await B.Evaluate("document.getElementById('theme-dark').click()");
            await B.Press("#theme-apply");
            await ThemeLook.Prefer(B, "light");
            await Task.Delay(100);
            await ThemeLook.Check(B, "dark", "dark", "Dark, the browser light");
            Assert.That(await B.Text("document.getElementById('theme-shown').innerText"), Is.EqualTo("Campertron is shown in its Dark theme."));
            await Sc.NoErrorsOrViolations();
        }

        // ---- the choice with the keyboard; kept everywhere ----

        [Test]
        public async Task TheChoice_ByKeyboard_IsKeptAcrossThePagesAReloadASignOutAndInAndARestartOfTheBrowser()
        {
            await using Scene Sc = await Scene.Start(Prefers: "light");
            Chrome B = Sc.B;
            await B.Open(Sc.Root + "settings/system");
            Assert.That(await B.Is("document.getElementById('theme-system').checked"), Is.True, "System without a choice");
            //the keyboard alone: to System, the arrow keys to Dark (a radio group's own keys), Tab to Apply theme, Enter
            await B.Evaluate("document.getElementById('theme-system').focus()");
            await Key(B, "ArrowDown", 40);
            await Key(B, "ArrowDown", 40);
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("theme-dark"));
            Assert.That(await B.Is("document.getElementById('theme-dark').checked"), Is.True, "the arrow keys choose");
            await ThemeLook.Check(B, "light", "light dark", "nothing applied before Apply theme");
            await Key(B, "Tab", 9);
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("theme-apply"));
            await B.Evaluate("window.__left = true");
            await Key(B, "Enter", 13);
            await B.Until("!window.__left && document.readyState === 'complete'", "the page after Apply theme");
            await ThemeLook.Check(B, "dark", "dark", "Dark, applied");
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("theme-shown"), "the focus on what the page is shown in");
            Assert.That(await B.Text("document.activeElement.innerText"), Is.EqualTo("Campertron is shown in its Dark theme."));
            Assert.That(await B.Is("document.getElementById('theme-dark').checked"), Is.True);
            JsonElement Kept = (await ThemeLook.Cookie(B, Sc.S.Host))!.Value;
            Assert.That((Kept.GetProperty("value").GetString(), Kept.GetProperty("httpOnly").GetBoolean(), Kept.GetProperty("session").GetBoolean(), Kept.GetProperty("sameSite").GetString()),
                Is.EqualTo(("dark", true, false, "Lax")), "this browser's, kept beyond the session of the browser");
            Assert.That(await B.Is("document.cookie.indexOf('campertron-theme') < 0"), Is.True, "no script reads it");

            //the pages, by their links
            foreach (string Link in new[] { "Searches", "Results", "Monitoring", "Campgrounds", "Start" })
            {
                await B.Evaluate("window.__left = true");
                await B.Evaluate("Array.prototype.find.call(document.querySelectorAll('nav[aria-label=Campertron] a'), function (A) { return A.textContent === " + JsonSerializer.Serialize(Link) + "; }).click()");
                await B.Until("!window.__left && document.readyState === 'complete'", Link);
                await ThemeLook.Check(B, "dark", "dark", Link);
            }
            //a reload
            await B.Evaluate("window.__left = true");
            await B.Send("Page.reload");
            await B.Until("!window.__left && document.readyState === 'complete'", "the reload");
            await ThemeLook.Check(B, "dark", "dark", "a reload");
            //sign out, and in
            await B.Press("nav form[action='/signout'] button");
            Assert.That(await B.Text("location.pathname"), Is.EqualTo("/signin"));
            await ThemeLook.Check(B, "dark", "dark", "the sign-in after a sign-out");
            await Sc.SignIn();
            await ThemeLook.Check(B, "dark", "dark", "signed in again");
            //the browser closed and started again: its profile keeps the choice; the sign-in (the session ended with the
            //browser) is in it
            await Sc.RestartBrowser();
            B = Sc.B;
            await ThemeLook.Prefer(B, "light");
            await B.Open(Sc.Root + "signin");
            await ThemeLook.Check(B, "dark", "dark", "after a restart of the browser");
            await Sc.SignIn();
            await B.Open(Sc.Root + "settings/system");
            Assert.That(await B.Is("document.getElementById('theme-dark').checked"), Is.True);
            await Sc.NoErrorsOrViolations();
        }

        [Test]
        public async Task EachBrowser_KeepsItsOwnChoice()
        {
            await using Scene First = await Scene.Start(Prefers: "light");
            await using Scene Second = await Scene.Start(Prefers: "light");
            await First.B.Open(First.Root + "settings/system");
            await First.B.Evaluate("document.getElementById('theme-dark').click()");
            await First.B.Press("#theme-apply");
            //a second browser on the first's web UI
            await using Chrome Other = await Chrome.Start();
            await ThemeLook.Prefer(Other, "light");
            await Other.Open(First.Root + "signin");
            await ThemeLook.Check(Other, "light", "light dark", "another browser: its own System");
            await ThemeLook.Check(First.B, "dark", "dark", "the first browser's Dark");
            await Second.B.Open(Second.Root + "settings/system");
            await ThemeLook.Check(Second.B, "light", "light dark", "another web UI");
        }

        // ---- what the choice leaves alone; the focus; narrow windows ----

        [Test]
        public async Task ApplyTheme_LeavesTheRefreshsConfirmationOpen_AndStartsNothing()
        {
            await using Scene Sc = await Scene.Start(Prefers: "dark");
            Chrome B = Sc.B;
            await B.Open(Sc.Root + "settings/system?confirm=refresh");
            Assert.That(await B.Is("document.getElementById('confirmation')"), Is.True);
            await B.Evaluate("document.getElementById('theme-light').click()");
            await B.Press("#theme-apply");
            await ThemeLook.Check(B, "light", "light", "Light, the browser dark");
            Assert.That(await B.Is("document.getElementById('confirmation') && document.querySelector(\"form[action='/settings/system?handler=start'] button\")"), Is.True, "the confirmation stays open");
            Assert.That(await B.Text("location.pathname + location.search"), Is.EqualTo("/settings/system?confirm=refresh"));
            Assert.That(Sc.S.Fake.Calls, Is.Zero, "no refresh started");
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("theme-shown"));
        }

        [TestCase("light")]
        [TestCase("dark")]
        public async Task TheFocus_IsSeenInEitherTheme(string Theme)
        {
            await using Scene Sc = await Scene.Start(Theme: Theme, Prefers: Theme == "dark" ? "light" : "dark");
            Chrome B = Sc.B;
            await B.Open(Sc.Root + "settings/system");
            string Focus = ThemeLook.Palette(Theme)["--focus"];
            //the checked choice (a radio group is one stop of Tab), then Tab to Apply theme
            await B.Evaluate("document.querySelector('input[name=theme]:checked').focus()");
            await Key(B, "Tab", 9);
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("theme-apply"));
            Assert.That(await B.Is("document.activeElement.matches(':focus-visible')"), Is.True, "focused by the keyboard");
            Assert.That((await B.Text("getComputedStyle(document.activeElement).outlineStyle"), await B.Text("getComputedStyle(document.activeElement).outlineColor"),
                await B.Text("getComputedStyle(document.activeElement).outlineWidth")), Is.EqualTo(("solid", Focus, "3px")), "the theme's focus ring");
            await Key(B, "Tab", 9);
            Assert.That(await B.Text("getComputedStyle(document.activeElement).outlineColor"), Is.EqualTo(Focus), "the next control's too");
        }

        [TestCase(null, "light")]
        [TestCase(null, "dark")]
        [TestCase("light", "dark")]
        [TestCase("dark", "light")]
        public async Task ANarrowWindow_NeverScrollsSideways(string? Theme, string Prefers)
        {
            await using Scene Sc = await Scene.Start(Width: 320, Height: 640, Theme: Theme, Prefers: Prefers);
            Chrome B = Sc.B;
            foreach (string Page in Pages)
            {
                await B.Open(Sc.Root + Page);
                Assert.That(await B.Number("document.documentElement.scrollWidth"), Is.LessThanOrEqualTo(320), Page + ": nothing sideways at 320 pixels");
                await ThemeLook.Check(B, Drawn(Theme, Prefers), Scheme(Theme), Page + " at 320 pixels");
            }
        }

        // a key the keyboard sends (down and up), to the focused element
        private static async Task Key(Chrome B, string Name, int Code)
        {
            string Text = Name == "Enter" ? "\r" : "";
            await B.Send("Input.dispatchKeyEvent", new { type = Text.Length > 0 ? "keyDown" : "rawKeyDown", key = Name, code = Name, windowsVirtualKeyCode = Code, nativeVirtualKeyCode = Code, text = Text });
            await B.Send("Input.dispatchKeyEvent", new { type = "keyUp", key = Name, code = Name, windowsVirtualKeyCode = Code, nativeVirtualKeyCode = Code });
        }
    }
}
