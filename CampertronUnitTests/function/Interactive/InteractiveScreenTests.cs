using CampertronLibrary.function.Base;
using CampertronLibrary.function.Interactive;
using CampertronUnitTests.function.Base;
using SW = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's screens and branding (D130, PT1's console findings): a new screen (a choice of the main
    // menu, a return to it, a form, Status, Help) starts on a cleared screen; a round of the same screen after one of its own
    // actions goes on below; "Press Enter to continue." comes only before a new screen would take away something written
    // since the last answer; the main menu shows the Campertron branding once at its top, the other screens never. Judged by
    // the events the terminal records (MenuTerminal: clear, logo, continue), never by a picture of a screen.
    public class InteractiveScreenTests : ConfigEditTestBase
    {
        private string Cache => Folder + "-cache";

        [TearDown]
        public void DeleteCache()
        {
            if (Directory.Exists(Cache))
            {
                Directory.Delete(Cache, true);
            }
        }

        private static Pick P(string Label) => new Pick(Label);
        private static readonly object[] ToExit = { P("Exit") };

        private InteractiveSession Session(InteractiveTerminal Terminal) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => Now,
                () => new CampertronInternalConfig { ConfigPath = Folder, CachePath = Cache, ContainerMode = false });

        private int Run(MenuTerminal Terminal) => InteractiveConsole.Run(Session(Terminal));

        // what was written after each clear, in order: each screen drawn
        private static List<List<(string Kind, string Text)>> Screens(MenuTerminal Terminal)
        {
            var Drawn = new List<List<(string Kind, string Text)>>();
            foreach ((string Kind, string Text) Item in Terminal.Written)
            {
                if (Item.Kind == "clear")
                {
                    Drawn.Add(new List<(string, string)>());
                }
                else if (Drawn.Count > 0)
                {
                    Drawn[^1].Add(Item);
                }
            }
            return Drawn;
        }

        private static int Headings(List<(string Kind, string Text)> Screen, string Text) => Screen.Count(W => W.Kind == "heading" && W.Text == Text);

        // ---- clearing ----

        [Test]
        public void TheConsole_StartsOnAClearedScreen_WithTheBrandingAbovePlainMenuLines()
        {
            var Terminal = new MenuTerminal(ToExit);

            Assert.That(Run(Terminal), Is.Zero);
            Assert.That(Terminal.Written[0], Is.EqualTo(("clear", string.Empty)), "cleared before anything is drawn");
            Assert.That(Terminal.Written[1], Is.EqualTo(("logo", InteractiveBranding.Plain)), "the width is not known: the plain name");
            Assert.That(Headings(Screens(Terminal)[0], InteractiveWording.MainMenu), Is.EqualTo(1));
        }

        [TestCase("Status", null)]
        [TestCase("Help", null)]
        [TestCase("Notifications", "Back to the main menu")]
        [TestCase("Settings", "Back to the main menu")]
        [TestCase("Searches", "Back to the main menu")]
        [TestCase("Campgrounds", "Back to the main menu")]
        [TestCase("Results", "Back to the main menu")]
        [TestCase("Monitoring", "Back to the main menu")]
        [TestCase("Guided setup", "Leave guided setup")]
        public void EveryChoiceOfTheMainMenu_StartsOnAClearedScreen_AndTheMainMenuComesBackAlone(string Choice, string? Back)
        {
            var Script = new List<object> { P(Choice) };
            if (Back != null)
            {
                Script.Add(P(Back));
            }
            Script.Add(P("Exit"));
            var Terminal = new MenuTerminal(Script.ToArray());

            Assert.That(Run(Terminal), Is.Zero);
            List<List<(string Kind, string Text)>> Drawn = Screens(Terminal);
            Assert.That(Drawn, Has.Count.EqualTo(3), "the main menu, the choice's screen, the main menu again");
            Assert.That(Headings(Drawn[1], InteractiveWording.MainMenu), Is.Zero, "the choice's screen holds no main menu");
            Assert.That(Drawn[1].Count(W => W.Kind == "logo"), Is.Zero, "and no branding");
            Assert.That(Headings(Drawn[2], InteractiveWording.MainMenu), Is.EqualTo(1), "one main menu, drawn alone");
            Assert.That(Drawn[2].Take(1), Is.EqualTo(new[] { ("logo", InteractiveBranding.Plain) }), "the branding at its top");
        }

        [Test]
        public void GoingBackAndForth_NeverStacksMenus_EachScreenHoldsOneMenu()
        {
            var Terminal = new MenuTerminal(P("Settings"), P(SW.BackToMainMenu), P("Settings"), P(SW.BackToMainMenu), P("Searches"), P(SW.BackToMainMenu), P("Exit"));

            Assert.That(Run(Terminal), Is.Zero);
            List<List<(string Kind, string Text)>> Drawn = Screens(Terminal);
            Assert.That(Drawn, Has.Count.EqualTo(7));
            Assert.That(Drawn.Select(S => Headings(S, InteractiveWording.MainMenu)), Is.EqualTo(new[] { 1, 0, 1, 0, 1, 0, 1 }));
            Assert.That(Terminal.Continues, Is.Zero, "a choice that goes on at once leaves nothing to read: no continuation");
        }

        [Test]
        public void WhatAScreenSaid_IsReadBeforeTheNextScreenTakesItAway()
        {
            var Terminal = new MenuTerminal(P("Status"), P("Exit"));

            Assert.That(Run(Terminal), Is.Zero);
            int StatusAt = Terminal.Written.FindIndex(W => W.Kind == "heading" && W.Text == InteractiveWording.StatusTitle);
            int ContinueAt = Terminal.Written.FindIndex(W => W.Kind == "continue");
            int NextClear = Terminal.Written.FindIndex(StatusAt, W => W.Kind == "clear");
            Assert.That(StatusAt, Is.GreaterThan(0));
            Assert.That(ContinueAt, Is.GreaterThan(StatusAt).And.LessThan(NextClear), "the status, then Press Enter to continue., then the clear");
            Assert.That(Terminal.Written[ContinueAt - 1], Is.EqualTo(("prompt", InteractiveWording.PressEnter)));
            Assert.That(Terminal.Continues, Is.EqualTo(1));
        }

        [Test]
        public void AFormsOwnAction_StaysOnItsScreen_SoItsProblemStaysReadable()
        {
            File.WriteAllText(General, "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            const string Problem = "    Error: Enter a number of days, such as 30 or 7.5.";
            var Terminal = new MenuTerminal(P("Settings"), P("General settings"), P("Change Refresh interval (days)"), TerminalInput.Line("a month"),
                P(SW.BackToSettings), TerminalInput.Line("y"), P(SW.BackToMainMenu), P("Exit"));

            Assert.That(Run(Terminal), Is.Zero);
            List<List<(string Kind, string Text)>> Drawn = Screens(Terminal);
            List<(string Kind, string Text)> Form = Drawn.Single(S => Headings(S, "General.yaml") >= 2);
            Assert.That(Headings(Form, "General.yaml"), Is.EqualTo(2), "the form's choices again below its action, on the same screen");
            int ProblemAt = Form.FindIndex(W => W.Text == Problem);
            Assert.That(ProblemAt, Is.GreaterThan(0).And.LessThan(Form.FindLastIndex(W => W.Kind == "heading" && W.Text == "General.yaml")),
                "the field's problem shown above the form's choices, never cleared before them");
            Assert.That(File.ReadAllText(General), Does.Contain("refreshRidbDataDayInterval: 30"), "nothing saved");
        }

        [Test]
        public void EscOrCtrlCInAMenu_GoesBackToTheMainMenuOnAClearedScreen()
        {
            var Terminal = new MenuTerminal(P("Settings"), TerminalInput.Escape, P("Searches"), TerminalInput.Interrupt, P("Exit"));

            Assert.That(Run(Terminal), Is.Zero);
            Assert.That(Screens(Terminal).Select(S => Headings(S, InteractiveWording.MainMenu)), Is.EqualTo(new[] { 1, 0, 1, 0, 1 }));
            Assert.That(Terminal.Continues, Is.Zero);
        }

        // ---- the continuation ----

        [Test]
        public void EnterEscOrCtrlC_AtTheContinuation_GoOnToTheMainMenu()
        {
            var Terminal = new MenuTerminal(P("Status"), new Cont(TerminalInput.Escape), P("Help"), new Cont(TerminalInput.Interrupt), P("Status"), P("Exit"));

            Assert.That(Run(Terminal), Is.Zero, "Ctrl+C at the continuation goes on: it never ends the console there");
            Assert.That(Terminal.Continues, Is.EqualTo(3));
            Assert.That(Terminal.Written.Count(W => W.Kind == "stopped"), Is.Zero);
        }

        [Test]
        public void TheEndOfTheInput_AtTheContinuation_EndsTheConsole_AsAnywhereElse()
        {
            var Terminal = new MenuTerminal(P("Status"), new Cont(TerminalInput.EndOfInput), P("Status"));

            Assert.That(Run(Terminal), Is.Zero);
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(Terminal.Written.Count(W => W.Kind == "heading" && W.Text == InteractiveWording.StatusTitle), Is.EqualTo(1), "nothing more was read or done");
        }

        // ---- a terminal that can't clear ----

        [Test]
        public void ATerminalThatCantClear_ShowsEachNewScreenAfterAnEmptyLine_AndFailsNothing()
        {
            var Terminal = new MenuTerminal(P("Settings"), P(SW.BackToMainMenu), P("Exit")) { CanClear = false };

            Assert.That(Run(Terminal), Is.Zero);
            Assert.That(Terminal.Clears, Is.Zero);
            Assert.That(Terminal.Written[0], Is.EqualTo(("logo", InteractiveBranding.Plain)), "nothing before the first screen");
            int SettingsAt = Terminal.Written.FindIndex(W => W.Kind == "heading" && W.Text == SW.Settings + " (nothing is saved until you choose to)");
            Assert.That(SettingsAt, Is.GreaterThan(1));
            Assert.That(Terminal.Written.Take(SettingsAt).Reverse().Take(3), Does.Contain(("out", string.Empty)), "an empty line before the new screen");
        }

        // ---- the branding ----

        [TestCase(80, "full")]
        [TestCase(200, "full")]
        [TestCase(40, "compact")]
        [TestCase(20, "compact")]
        [TestCase(0, "plain")]
        public void TheMainMenu_ShowsTheBrandingOnce_TheLogoWhereItFits_AndNoOtherScreenShowsIt(int Columns, string Kind)
        {
            var Terminal = new MenuTerminal(P("Settings"), P(SW.BackToMainMenu), P("Exit")) { Columns = Columns };

            Assert.That(Run(Terminal), Is.Zero);
            List<List<(string Kind, string Text)>> Drawn = Screens(Terminal);
            IReadOnlyList<string> Expected = Kind == "full" ? Menu.LogoLines : Kind == "compact" ? new[] { InteractiveBranding.Compact } : new[] { InteractiveBranding.Plain };
            Assert.That(Drawn[0].TakeWhile(W => W.Kind == "logo").Select(W => W.Text), Is.EqualTo(Expected));
            Assert.That(Drawn[2].TakeWhile(W => W.Kind == "logo").Select(W => W.Text), Is.EqualTo(Expected));
            Assert.That(Drawn[1].Any(W => W.Kind == "logo"), Is.False, "the Settings menu shows no branding");
            Assert.That(Terminal.Written.Count(W => W.Kind == "logo"), Is.EqualTo(2 * Expected.Count), "once per main menu drawn");
            if (Columns > 0)
            {
                Assert.That(Terminal.Written.Where(W => W.Kind == "logo").Select(W => W.Text.Length), Has.All.LessThanOrEqualTo(Columns), "never wrapped or cut");
            }
        }

        [Test]
        public void TheBranding_IsTheNoArgumentMenusOwnLogo_AndOnlyTheName()
        {
            Assert.That(Menu.LogoText.Replace("\r", string.Empty), Does.StartWith("\n  ____").And.EndWith("|_|                                  \n"), "the no-argument menu's logo, unchanged");
            Assert.That(Menu.LogoLines, Has.Count.EqualTo(6));
            Assert.That(InteractiveBranding.LogoWidth, Is.EqualTo(58));
            Assert.That(Menu.LogoLines.Select(L => L.Length), Has.All.LessThanOrEqualTo(58));
            string All = string.Join("\n", Menu.LogoLines.Append(InteractiveBranding.Compact).Append(InteractiveBranding.Plain));
            Assert.That(All.Any(char.IsDigit), Is.False, "no version");
            Assert.That(All, Does.Not.Contain(Path.DirectorySeparatorChar.ToString() + "home").And.Not.Contain("Campertron" + Path.DirectorySeparatorChar), "no path");
            Assert.That(InteractiveBranding.Compact.ToLowerInvariant(), Does.Contain("campertron"));
        }

        // the no-argument menu draws its logo exactly as before D130 moved the logo into a constant both consoles share: the
        // same text (an empty line before and after it), through the same call
        [Test]
        public void TheNoArgumentMenusLogo_IsWrittenAsItAlwaysWas()
        {
            const string AsBefore = "\n  ____                                _                   \n / ___|__ _ _ __ ___  _ __   ___ _ __| |_ _ __ ___  _ __  \n| |   / _` | '_ ` _ \\| '_ \\ / _ \\ '__| __| '__/ _ \\| '_ \\ \n| |__| (_| | | | | | | |_) |  __/ |  | |_| | | (_) | | | |\n \\____\\__,_|_| |_| |_| .__/ \\___|_|   \\__|_|  \\___/|_| |_|\n                     |_|                                  \n";
            Action<ConsoleColor?> Original = ConsoleUi.ApplyColor;
            ConsoleUi.ApplyColor = Color => { };
            try
            {
                Menu.WriteLogo();
            }
            finally
            {
                ConsoleUi.ApplyColor = Original;
            }
            Assert.That(Menu.LogoText.Replace("\r", string.Empty), Is.EqualTo(AsBefore));
            Assert.That(Out.ToString().Replace("\r", string.Empty), Is.EqualTo(AsBefore + "\n"), "the logo's text and the line break, nothing else");
        }

        // ---- a secret ----

        [Test]
        public void ASecretTyped_IsNeverShown_OnAnyScreenDrawnAfterIt()
        {
            const string Secret = "S3cret-Screen-Canary-71c";
            File.WriteAllText(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsendFromAddress: from@example.invalid\nsendToAddressList:\n- to@example.invalid\n");
            var Terminal = new MenuTerminal(P("Notifications"), P("Email"), P("Change Password (plain text)"), TerminalInput.Line(Secret),
                P(SW.BackToNotifications), TerminalInput.Line("y"), P(SW.BackToMainMenu), P("Exit"));

            Assert.That(Run(Terminal), Is.Zero);
            Assert.That(Terminal.SecretReads, Is.EqualTo(1));
            Assert.That(Terminal.AllText.Contains(Secret, StringComparison.Ordinal), Is.False, "the secret is never written back, on any screen");
            Assert.That(Screens(Terminal).Count, Is.GreaterThanOrEqualTo(4));
        }
    }
}
