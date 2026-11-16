using System.Reflection;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The console menu's key handling (Menu.ProcessInput, Menu.WriteMenu; TD-12). Until it was fixed, ProcessInput called
    // WriteMenu again for every key (the arrows and every key the menu does not use), and WriteMenu's loop called
    // ProcessInput, so each key press went one level deeper: the Release console of 6451739 ended with a stack overflow after
    // 17,231 key presses (8 MB stack; 4,202 with 1 MB). Now a key only moves the selection and returns, and the loop in
    // WriteMenu draws. The tests press keys through ProcessInput directly: no terminal, no file, no network, and never
    // Enter (which would search, refresh or end the process).
    [NonParallelizable]
    public class MenuKeysTests
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private MenuLocation OriginalLocation;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Error = null!;

        [SetUp]
        public void Setup()
        {
            OriginalLocation = Menu._location;
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Error = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Error);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Out.Dispose();
            Error.Dispose();
            Menu._location = OriginalLocation;
        }

        // the keys the menu does not use: each one is handled by ProcessInput's default case
        private static readonly ConsoleKey[] OtherKeys =
        {
            ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.Escape, ConsoleKey.Spacebar, ConsoleKey.Tab, ConsoleKey.Backspace,
            ConsoleKey.Home, ConsoleKey.End, ConsoleKey.PageUp, ConsoleKey.PageDown, ConsoleKey.Delete, ConsoleKey.Insert,
            ConsoleKey.A, ConsoleKey.S, ConsoleKey.X, ConsoleKey.D1, ConsoleKey.NumPad5, ConsoleKey.F1, ConsoleKey.F12
        };

        // presses one key at the menu, as WriteMenu's loop does. The arrows and the other keys use neither the settings nor
        // the terminal, so none is given. Enter is refused here: it runs the selected option.
        private static void Press(ConsoleKey Key)
        {
            if (Key == ConsoleKey.Enter)
            {
                throw new InvalidOperationException("these tests never press Enter");
            }
            Menu.ProcessInput(new ConsoleKeyInfo('\0', Key, false, false, false), null!);
        }

        [TestCase(MenuLocation.Search, ConsoleKey.UpArrow, MenuLocation.Search)]
        [TestCase(MenuLocation.Refresh, ConsoleKey.UpArrow, MenuLocation.Search)]
        [TestCase(MenuLocation.ViewConfig, ConsoleKey.UpArrow, MenuLocation.Refresh)]
        [TestCase(MenuLocation.Exit, ConsoleKey.UpArrow, MenuLocation.ViewConfig)]
        [TestCase(MenuLocation.Search, ConsoleKey.DownArrow, MenuLocation.Refresh)]
        [TestCase(MenuLocation.Refresh, ConsoleKey.DownArrow, MenuLocation.ViewConfig)]
        [TestCase(MenuLocation.ViewConfig, ConsoleKey.DownArrow, MenuLocation.Exit)]
        [TestCase(MenuLocation.Exit, ConsoleKey.DownArrow, MenuLocation.Exit)]
        public void AnArrowKey_MovesTheSelectionOneStep_AndStaysAtTheEnds(object From, ConsoleKey Key, object To)
        {
            Menu._location = (MenuLocation)From;

            Press(Key);

            Assert.That(Menu._location, Is.EqualTo((MenuLocation)To));
            Assert.That(Out.ToString() + Error.ToString(), Is.Empty, "a key only moves the selection; the loop in WriteMenu draws");
        }

        [Test]
        public void TheArrowKeys_WalkThroughTheFourOptionsInOrder_BothWays()
        {
            Menu._location = MenuLocation.Search;
            List<MenuLocation> Seen = new List<MenuLocation>();
            foreach (ConsoleKey Key in new[] { ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.UpArrow, ConsoleKey.UpArrow, ConsoleKey.UpArrow, ConsoleKey.UpArrow })
            {
                Press(Key);
                Seen.Add(Menu._location);
            }

            Assert.That(Seen, Is.EqualTo(new[]
            {
                MenuLocation.Refresh, MenuLocation.ViewConfig, MenuLocation.Exit, MenuLocation.Exit,
                MenuLocation.ViewConfig, MenuLocation.Refresh, MenuLocation.Search, MenuLocation.Search
            }));
            // the option the menu draws for each location
            Assert.That(new[] { MenuLocation.Search, MenuLocation.Refresh, MenuLocation.ViewConfig, MenuLocation.Exit }.Select(Menu.GetLocationInt), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        }

        [TestCase(MenuLocation.Search)]
        [TestCase(MenuLocation.Refresh)]
        [TestCase(MenuLocation.ViewConfig)]
        [TestCase(MenuLocation.Exit)]
        public void AKeyTheMenuDoesNotUse_ChangesNothing_AndReturns(object At)
        {
            foreach (ConsoleKey Key in OtherKeys)
            {
                Menu._location = (MenuLocation)At;

                Press(Key);

                Assert.That(Menu._location, Is.EqualTo((MenuLocation)At), Key.ToString());
            }
            Assert.That(Out.ToString() + Error.ToString(), Is.Empty);
        }

        [Test]
        public void OneMillionKeyPresses_OnASmallStack_NeverGoDeeper()
        {
            // 256 KB: the recursion took about 480 bytes of an 8 MB stack per key press (17,231 key presses), so a
            // thread this small would have ended after a few hundred. One round presses Up, Down and a key the menu does
            // not use at every one of the four options (also Up at Search and Down at Exit) and ends where it began.
            const int Presses = 1_000_000;
            ConsoleKey[] Keys =
            {
                // at Search, then to Refresh
                ConsoleKey.UpArrow, ConsoleKey.X, ConsoleKey.DownArrow,
                // at Refresh: to Search and back, then to View Config
                ConsoleKey.Escape, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow,
                // at View Config: to Refresh and back, then to Exit
                ConsoleKey.LeftArrow, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow,
                // at Exit, then up to Search
                ConsoleKey.Tab, ConsoleKey.DownArrow, ConsoleKey.UpArrow, ConsoleKey.UpArrow, ConsoleKey.UpArrow
            };
            Assert.That(Presses % Keys.Length, Is.Zero);
            Exception? Failure = null;
            int Pressed = 0;
            MenuLocation End = MenuLocation.Exit;
            Thread Typist = new Thread(() =>
            {
                try
                {
                    Menu._location = MenuLocation.Search;
                    for (int i = 0; i < Presses; i++)
                    {
                        Press(Keys[i % Keys.Length]);
                        Pressed++;
                    }
                    End = Menu._location;
                }
                catch (Exception ex)
                {
                    Failure = ex;
                }
            }, 256 * 1024);

            Typist.Start();
            Assert.That(Typist.Join(TimeSpan.FromMinutes(2)), Is.True, "the key presses did not finish");

            Assert.That(Failure, Is.Null, Failure?.ToString());
            Assert.That(Pressed, Is.EqualTo(Presses));
            Assert.That(End, Is.EqualTo(MenuLocation.Search));
            Assert.That(Out.ToString() + Error.ToString(), Is.Empty);
        }

        [Test]
        public void ProcessInput_NeverCallsWriteMenu_AndTheLoopInWriteMenuReadsTheKeys()
        {
            MethodBase ProcessInput = typeof(Menu).GetMethod(nameof(Menu.ProcessInput), All)!;
            MethodBase WriteMenu = typeof(Menu).GetMethod(nameof(Menu.WriteMenu), All)!;

            List<string> FromProcessInput = Called(ProcessInput);
            Assert.That(FromProcessInput, Does.Not.Contain(Name(WriteMenu)), "a key must return to the loop that draws the menu (TD-12)");
            Assert.That(FromProcessInput, Does.Not.Contain(Name(ProcessInput)));

            // the loop is where the keys are read and where the menu is drawn again (and the scan sees calls at all)
            List<string> FromWriteMenu = Called(WriteMenu);
            Assert.That(FromWriteMenu, Does.Contain(Name(ProcessInput)));
            Assert.That(FromWriteMenu, Does.Contain("System.Console::ReadKey"));
            Assert.That(FromWriteMenu, Does.Contain(typeof(Menu).FullName + "::" + nameof(Menu.WriteOptions)));
            Assert.That(FromWriteMenu, Does.Not.Contain(Name(WriteMenu)));
        }

        private static string Name(MemberInfo Member)
        {
            return (Member.DeclaringType?.FullName ?? "?") + "::" + Member.Name;
        }

        private static List<string> Called(MethodBase Method)
        {
            return YamlDocumentPreservationTests.ReferencedMembers(Method).Select(Name).ToList();
        }
    }
}
