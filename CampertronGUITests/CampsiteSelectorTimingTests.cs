using System.Text.RegularExpressions;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronGUITests.CampsiteSelectorTests;

namespace CampertronGUITests
{
    // TD-62 (§DB): the design of CampsiteSelectorTests' timing checks, so that no change makes them blind, loose or noisy.
    // The measurement itself, with a selector of the largest campground's 360 names and a stand-in for the editing session:
    // one slow run is forgiven, while a slow step, a busier step and a step that did not run are caught. And the timed test
    // as written: every step measured and asserted with its bound and budget, the recorded values, the largest campground's
    // names, no skip and no branch on where it runs, and CI's test command running it.
    public class CampsiteSelectorTimingTests
    {
        // fixed, not derived from the bounds under test: a loosened bound is then caught, never slept through
        private static readonly TimeSpan StallTime = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SlowTime = TimeSpan.FromMilliseconds(250);

        private static FilterSelectorField LargestSelector(Action Session)
        {
            var Field = new FilterSelectorField("includeSites", "Include campsites", string.Empty, FilterField.Sites, true, "campsite name", "campsite names");
            Field.SetLoaded(Large("990003").CampsiteNames);
            Field.RowsChangedForTests = Session;
            return Field;
        }

        private static string Repository()
        {
            string? Folder = AppContext.BaseDirectory;
            while (Folder != null && !File.Exists(Path.Join(Folder, "Campertron.sln")))
            {
                Folder = Path.GetDirectoryName(Folder);
            }
            Assert.That(Folder, Is.Not.Null, "the repository");
            return Folder!;
        }

        [Test]
        public void OneSlowRun_NeverFailsAQuickStep()
        {
            // a hosted runner's other work stalls one run (CI #328: 1,275 ms for one change): the fastest run is the step's cost
            FilterSelectorField Field = LargestSelector(() => { });
            int Run = 0;
            StepCost Cost = Measure("one stalled run", Field, QuickRuns, () =>
            {
                if (Run++ == 0)
                {
                    Thread.Sleep(StallTime);
                }
                Field.Toggle(Field.Options[200]);
            });
            Assert.That(Cost.Problems(QuickBound, SelectorBudget, Edits: QuickRuns, KeepsRows: true), Is.Empty);
        }

        [Test]
        public void ASlowStep_ABusierStep_AndAStepThatWasNotMeasured_AreCaught()
        {
            FilterSelectorField Field = LargestSelector(() => { });
            Action Change = () => Field.Toggle(Field.Options[200]);

            StepCost Slow = Measure("slow in every run", Field, QuickRuns, () =>
            {
                Thread.Sleep(SlowTime);
                Change();
            });
            Assert.That(Slow.Problems(QuickBound, SelectorBudget, Edits: QuickRuns, KeepsRows: true), Has.One.Contains("the fastest of 5 runs took"));

            StepCost Busier = Measure("busier in every run", Field, QuickRuns, () =>
            {
                GC.KeepAlive(new byte[SelectorBudget]);
                Change();
            });
            Assert.That(Busier.Problems(QuickBound, SelectorBudget, Edits: QuickRuns, KeepsRows: true), Has.One.Contains("a run allocated"));

            // a measurement of nothing, or of a warm-up instead of the step: no rebuild and no session edit in the runs
            StepCost Nothing = Measure("nothing", Field, QuickRuns, () => { });
            Assert.That(Nothing.Problems(QuickBound, SelectorBudget, Edits: QuickRuns, KeepsRows: true),
                Is.EqualTo(new[] { "nothing: 0 rebuilds of the list in 5 runs, so not every run did the step", "nothing: the editing session got the list 0 times; expected 5" }));

            // the session given the list twice for one change
            StepCost Twice = Measure("twice", Field, QuickRuns, () =>
            {
                Change();
                Field.RowsChangedForTests!();
            });
            Assert.That(Twice.Problems(QuickBound, SelectorBudget, Edits: QuickRuns, KeepsRows: true), Is.EqualTo(new[] { "twice: the editing session got the list 10 times; expected 5" }));

            // the rows made again (here by narrowing, which a change of checks never does)
            int Narrow = 0;
            StepCost Remade = Measure("remade", Field, QuickRuns, () => Field.FindText = Narrow++ % 2 == 0 ? "A" : string.Empty);
            Assert.That(Remade.Problems(QuickBound, SelectorBudget, Edits: 0, KeepsRows: true), Has.One.Contains("the list's rows changed"));
        }

        [Test]
        public void TheTimedTest_MeasuresEveryStep_WithItsBounds_AndRunsOnEveryCiRun()
        {
            // the recorded bounds and budgets (§DB), and the largest campground's names
            Assert.That((QuickRuns, QuickBound, SlowBound), Is.EqualTo((5, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10))));
            Assert.That((SelectorBudget, QuickBudget, SlowBudget), Is.EqualTo((2 * MiB, 16 * MiB, 512 * MiB)));
            Assert.That((LargestCampground, Large("990003").CampsiteNames.Count), Is.EqualTo((360, 360)));

            string Source = File.ReadAllText(Path.Join(Repository(), "CampertronGUITests", "CampsiteSelectorTests.cs")).Replace("\r\n", "\n");
            // nothing that skips a test, or tells a hosted runner from another computer (GuiTestEnvironment is the fixture)
            foreach (string Token in new[] { @"\bEnvironment\.", "GITHUB_", "RUNNER_", "\"CI\"", @"\bAssume\.", @"Assert\.(Ignore|Inconclusive|Pass)", @"\bWarn\.",
                                             @"[\[,]\s*(?:[\w.]+\.)?(Ignore|Explicit|Platform|Retry|Category)(?:Attribute)?\b", "#if" })
            {
                Assert.That(Regex.IsMatch(Source, Token), Is.False, Token);
            }
            Assert.That(Regex.IsMatch(Source, @"\]\s*\n\s*public class CampsiteSelectorTests"), Is.False, "no attribute on the class");

            Match Method = Regex.Match(Source, @"\n((?:[ ]*\[[^\n]*\]\n)+)[ ]*public void TheLargestCampground_IsSearchable_Virtualized_AndQuick\(\)\n[ ]*\{\n(.*?)\n        \}\n", RegexOptions.Singleline);
            Assert.That(Method.Success, Is.True, "the timed test");
            Assert.That(Method.Groups[1].Value.Trim(), Is.EqualTo("[AvaloniaTest]"), "the timed test's only attribute");
            string Body = Method.Groups[2].Value;
            Assert.That(Regex.IsMatch(Regex.Replace(Body, @"//[^\n]*", string.Empty), @"\b(if|return|try|catch|goto)\b"), Is.False, "no branch around a check");
            Assert.That(Body, Does.Not.Contain("MaxValue").And.Not.Contain("PositiveInfinity").And.Not.Contain("TimeSpan.From"), "only the recorded bounds");

            // every step measured, with its runs, and its cost asserted with its bound and budget
            var Measured = Regex.Matches(Body, @"StepCost (\w+) = Measure\(""[^""]+"", Sites, (\w+), ").Select(M => M.Groups[1].Value + " " + M.Groups[2].Value).ToList();
            var Asserted = Regex.Matches(Body, @"Assert\.That\((\w+)\.Problems\((\w+), (\w+), Edits: \w+, KeepsRows: (?:true|false)\), Is\.Empty\);")
                                .Select(M => M.Groups[1].Value + " " + M.Groups[2].Value + " " + M.Groups[3].Value).ToList();
            Assert.That(Measured, Is.EqualTo(new[] { "Sequence 1", "Selector QuickRuns", "Edit 1", "Saved QuickRuns", "Clear 1", "Usual QuickRuns" }));
            Assert.That(Asserted, Is.EqualTo(new[]
            {
                "Sequence SlowBound SlowBudget", "Selector QuickBound SelectorBudget", "Edit SlowBound SlowBudget",
                "Saved QuickBound QuickBudget", "Clear SlowBound SlowBudget", "Usual QuickBound QuickBudget",
            }));
            Assert.That(Regex.Matches(Body, @"Measure\(").Count, Is.EqualTo(6), "no step measured without its check");

            // CI's test command runs every test of the solution, this one among them (on Linux, where the GUI tests run)
            string Ci = File.ReadAllText(Path.Join(Repository(), ".github", "workflows", "ci.yml"));
            List<string> Commands = Ci.Split('\n').Where(L => L.Contains("dotnet test Campertron.sln")).ToList();
            Assert.That(Commands, Has.Count.EqualTo(1));
            Assert.That(Commands[0], Does.Not.Contain("--filter").And.Not.Contain("TestCategory"));
        }
    }
}
