using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The automatic restart after a successful refresh of the campground data (decision D62, business-rules-gui.md §26):
    // the one-time handoff (its token, format, folder, single claim and every rejection), the launch of the new process
    // through a seam (no test starts a process: the GUI tests' fixture forbids the production start), the window's
    // placement on the screens there are, and, in the window, the capture, the restored place, Settings > System, the
    // one-time status, ordinary starts and closes that hand nothing on, and the lifecycle around a refresh.
    public class AutomaticRestartTests
    {
        private string Folder = string.Empty;
        private ResultsFolders Folders = null!;
        private string Preferences = string.Empty;
        private static readonly DateTime Now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "restart-tests-" + Guid.NewGuid().ToString("N"));
            Folders = new ResultsFolders();
            Preferences = Path.Join(GuiTestEnvironment.Root, "restart-preferences-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void DeleteFolder()
        {
            Folders.Dispose();
            foreach (string Each in new[] { Folder, Preferences })
            {
                if (Directory.Exists(Each))
                {
                    Directory.Delete(Each, true);
                }
            }
        }

        private string HandoffFolder => Path.Join(Folder, RestartHandoffStore.FolderName);
        private RestartHandoffStore Store() => new RestartHandoffStore(HandoffFolder);

        private static RestartHandoff Sample(DateTime? Created = null, RestartOutcome Outcome = RestartOutcome.Refreshed, bool NoWindow = false) =>
            new RestartHandoff(Created ?? Now, Outcome, NoWindow ? null : new RestartWindow(120, -90, 1100.5, 800, true), RestartTopTab.Campgrounds, RestartSettingsTab.Campertron);

        private string[] LeftInFolder() => Directory.Exists(HandoffFolder) ? Directory.GetFileSystemEntries(HandoffFolder) : Array.Empty<string>();

        // ---- the token ----

        [Test]
        public void Tokens_AreRandom64LowercaseHexDigits_AndUnique()
        {
            var Seen = new HashSet<string>();
            for (int i = 0; i < 1000; i++)
            {
                string Token = RestartToken.Create();
                Assert.That(RestartToken.IsWellFormed(Token), Is.True, Token);
                Assert.That(Seen.Add(Token), Is.True, "unique");
            }
            Assert.That(RestartToken.Binding(Seen.First()), Has.Length.EqualTo(64).And.Not.EqualTo(Seen.First()));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
        [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
        [TestCase("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
        [TestCase("../../../../../../etc/passwd/0123456789abcdef0123456789abcdef01234")]
        [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde/")]
        [TestCase(" 123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
        public void ATokenThatIsNotOne_SelectsNoFile(string? Token)
        {
            Assert.That(RestartToken.IsWellFormed(Token), Is.False);
            Assert.That(Store().PathFor(Token ?? string.Empty), Is.Null, "no path at all: nothing can be selected or traversed");
            RestartClaim Claim = Store().Claim(Token, Now);
            Assert.That(Claim.Status, Is.EqualTo(Token == null ? RestartClaimStatus.NoToken : RestartClaimStatus.BadToken));
            Assert.That(Directory.Exists(HandoffFolder), Is.False, "nothing created");
        }

        [Test]
        public void TheEnvironmentToken_IsTakenOnce_AndRemovedFromThisProcess()
        {
            try
            {
                Environment.SetEnvironmentVariable(RestartToken.EnvironmentVariable, "token");
                Assert.That(RestartToken.TakeFromEnvironment(), Is.EqualTo("token"));
                Assert.That(Environment.GetEnvironmentVariable(RestartToken.EnvironmentVariable), Is.Null, "nothing this process starts inherits it");
                Assert.That(RestartToken.TakeFromEnvironment(), Is.Null);
            }
            finally
            {
                Environment.SetEnvironmentVariable(RestartToken.EnvironmentVariable, null);
            }
        }

        // ---- the handoff ----

        [Test]
        public void AHandoff_RoundTrips_IsClaimedOnce_AndLeavesNothing()
        {
            RestartHandoffStore Handoffs = Store();
            string Token = RestartToken.Create();
            string Written = Handoffs.Create(Token, Sample());
            Assert.That(Path.GetFileName(Written), Is.EqualTo("restart-" + RestartToken.Binding(Token) + ".json"));
            Assert.That(Handoffs.IsPending(Written), Is.True);

            RestartClaim Claim = Handoffs.Claim(Token, Now.AddSeconds(3));
            Assert.That(Claim.Status, Is.EqualTo(RestartClaimStatus.Claimed));
            Assert.That(Claim.Handoff, Is.EqualTo(Sample()), "every value as written (negative coordinates included)");
            Assert.That(Handoffs.IsPending(Written), Is.False);
            Assert.That(LeftInFolder(), Is.Empty, "deleted once used");
            Assert.That(Handoffs.Claim(Token, Now.AddSeconds(4)).Status, Is.EqualTo(RestartClaimStatus.Missing), "consumed no more than once");

            string Other = RestartToken.Create();
            Handoffs.Create(Other, Sample(NoWindow: true));
            Assert.That(Handoffs.Claim(Other, Now).Handoff!.Window, Is.Null, "a handoff without a window is valid");
        }

        [Test]
        public void TheFolderAndTheFile_AreOwnerOnly()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions.");
            }
            string Written = Store().Create(RestartToken.Create(), Sample());
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(HandoffFolder), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
                Assert.That(File.GetUnixFileMode(Written), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }

        [Test]
        public void TheFile_HoldsNoTokenNoPathAndNoSetting_OnlyTheKnownMembers()
        {
            string Token = RestartToken.Create();
            string Written = Store().Create(Token, Sample());
            string Text = File.ReadAllText(Written);
            Assert.That(Text, Does.Not.Contain(Token));
            Assert.That(Text, Does.Not.Contain("/").And.Not.Contain("\\").And.Not.Contain("smtp").And.Not.Contain("yaml"));
            using var Document = System.Text.Json.JsonDocument.Parse(Text);
            Assert.That(Document.RootElement.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "version", "binding", "createdUtc", "outcome", "tab", "settingsTab", "window" }));
            Assert.That(Document.RootElement.GetProperty("window").EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "x", "y", "width", "height", "maximized" }));
            Assert.That(new FileInfo(Written).Length, Is.LessThan(RestartHandoffStore.MaxBytes));
        }

        [Test]
        public void AnotherToken_FindsNothing_AndLeavesTheHandoffAlone()
        {
            RestartHandoffStore Handoffs = Store();
            string Written = Handoffs.Create(RestartToken.Create(), Sample());
            Assert.That(Handoffs.Claim(RestartToken.Create(), Now).Status, Is.EqualTo(RestartClaimStatus.Missing));
            Assert.That(Handoffs.IsPending(Written), Is.True, "a wrong token claims nothing");
        }

        private static IEnumerable<TestCaseData> NotHandoffs()
        {
            string Valid(string Binding, string Created = "2026-11-18T12:00:00.0000000Z", string Version = "1", string Outcome = "\"refreshed\"", string Window = "null",
                string Extra = "", string Tab = "\"settings\"") =>
                "{\"version\":" + Version + ",\"binding\":\"" + Binding + "\",\"createdUtc\":\"" + Created + "\",\"outcome\":" + Outcome + ",\"tab\":" + Tab
                + ",\"settingsTab\":\"system\",\"window\":" + Window + Extra + "}";
            yield return new TestCaseData((Func<string, string>)(_ => ""), "Malformed").SetName("{m}(empty)");
            yield return new TestCaseData((Func<string, string>)(_ => "not json"), "Malformed").SetName("{m}(not JSON)");
            yield return new TestCaseData((Func<string, string>)(_ => "[1,2]"), "Malformed").SetName("{m}(an array)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B) + "x"), "Malformed").SetName("{m}(trailing text)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B).Replace("{", "{/*c*/")), "Malformed").SetName("{m}(a comment)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Extra: ",\"version\":1")), "Malformed").SetName("{m}(a duplicate member)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Extra: ",\"$type\":\"System.IO.FileInfo\"")), "Malformed").SetName("{m}(an unknown member, a type name)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Version: "2")), "UnsupportedVersion").SetName("{m}(version 2)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Version: "2", Extra: ",\"monitor\":\"DP-1\"")), "UnsupportedVersion").SetName("{m}(version 2 with a new member)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Version: "\"1\"")), "Malformed").SetName("{m}(version as text)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(new string('0', 64))), "WrongBinding").SetName("{m}(another binding)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Created: "2026-11-18 12:00")), "Malformed").SetName("{m}(a time not in round-trip form)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Created: "2026-11-18T12:00:00.0000000")), "Malformed").SetName("{m}(a time without UTC)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Created: "2026-11-18T11:57:59.0000000Z")), "Stale").SetName("{m}(older than two minutes)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Created: "2026-11-18T12:00:31.0000000Z")), "Stale").SetName("{m}(from the future)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Outcome: "\"failed\"")), "Malformed").SetName("{m}(an unknown outcome)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Outcome: "0")), "Malformed").SetName("{m}(an outcome as a number)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Tab: "\"Settings\"")), "Malformed").SetName("{m}(a tab in another case)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1,\"y\":2,\"width\":0,\"height\":600,\"maximized\":false}")), "Malformed").SetName("{m}(a zero width)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1,\"y\":2,\"width\":-800,\"height\":600,\"maximized\":false}")), "Malformed").SetName("{m}(a negative width)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1,\"y\":2,\"width\":1e300,\"height\":600,\"maximized\":false}")), "Malformed").SetName("{m}(an unreasonable width)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":5000000,\"y\":2,\"width\":800,\"height\":600,\"maximized\":false}")), "Malformed").SetName("{m}(an unreasonable coordinate)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1.5,\"y\":2,\"width\":800,\"height\":600,\"maximized\":false}")), "Malformed").SetName("{m}(a fractional coordinate)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1,\"y\":2,\"width\":800,\"height\":600}")), "Malformed").SetName("{m}(a window without maximized)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1,\"y\":2,\"width\":800,\"height\":600,\"maximized\":\"yes\"}")), "Malformed").SetName("{m}(maximized as text)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "{\"x\":1,\"y\":2,\"width\":800,\"height\":600,\"maximized\":false,\"inner\":{\"deep\":[1]}}")), "Malformed").SetName("{m}(nested deeper than the window)");
            yield return new TestCaseData((Func<string, string>)(B => Valid(B, Window: "[1,2,800,600]")), "Malformed").SetName("{m}(a window as an array)");
        }

        [TestCaseSource(nameof(NotHandoffs))]
        public void AFileThatIsNotAValidHandoff_IsRejected_AndDeleted(Func<string, string> Content, string ExpectedName)
        {
            RestartClaimStatus Expected = Enum.Parse<RestartClaimStatus>(ExpectedName);
            string Token = RestartToken.Create();
            Directory.CreateDirectory(HandoffFolder);
            File.WriteAllText(Store().PathFor(Token)!, Content(RestartToken.Binding(Token)), new UTF8Encoding(false));
            RestartClaim Claim = Store().Claim(Token, Now);
            Assert.That(Claim.Status, Is.EqualTo(Expected));
            Assert.That(Claim.Handoff, Is.Null);
            Assert.That(LeftInFolder(), Is.Empty, "claimed, so consumed and deleted even when it is not used");
        }

        [Test]
        public void AnOversizedFile_IsRejected_WithoutBeingRead()
        {
            string Token = RestartToken.Create();
            Directory.CreateDirectory(HandoffFolder);
            File.WriteAllBytes(Store().PathFor(Token)!, Encoding.UTF8.GetBytes(new string(' ', RestartHandoffStore.MaxBytes + 1)));
            Assert.That(Store().Claim(Token, Now).Status, Is.EqualTo(RestartClaimStatus.TooLarge));
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void AReplay_IsRejected()
        {
            RestartHandoffStore Handoffs = Store();
            string Token = RestartToken.Create();
            string Written = Handoffs.Create(Token, Sample());
            byte[] Copy = File.ReadAllBytes(Written);
            Assert.That(Handoffs.Claim(Token, Now).Succeeded, Is.True);
            // the same token again: nothing left to claim
            Assert.That(Handoffs.Claim(Token, Now).Status, Is.EqualTo(RestartClaimStatus.Missing));
            // a copy of the file put back later: too old by then
            File.WriteAllBytes(Written, Copy);
            Assert.That(Handoffs.Claim(Token, Now.AddMinutes(3)).Status, Is.EqualTo(RestartClaimStatus.Stale));
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void ALinkInPlaceOfTheHandoff_IsRejected_AndItsTargetUntouched()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Symbolic links need a privilege on Windows.");
            }
            string Token = RestartToken.Create();
            Directory.CreateDirectory(HandoffFolder);
            string Target = Path.Join(Folder, "elsewhere.json");
            string Real = Encoding.UTF8.GetString(RestartHandoffStore.Serialize(Sample(), RestartToken.Binding(Token)));
            File.WriteAllText(Target, Real);
            File.CreateSymbolicLink(Store().PathFor(Token)!, Target);
            Assert.That(Store().Claim(Token, Now).Status, Is.EqualTo(RestartClaimStatus.Unreadable));
            Assert.That(File.ReadAllText(Target), Is.EqualTo(Real), "the link's target is never read or changed");
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void AFolderThatIsALinkOrThatOthersCanWriteTo_IsRefused()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix folders.");
            }
            Directory.CreateDirectory(Folder);
            string Elsewhere = Path.Join(Folder, "elsewhere");
            Directory.CreateDirectory(Elsewhere);
            Directory.CreateSymbolicLink(HandoffFolder, Elsewhere);
            Assert.Throws<IOException>(() => Store().Create(RestartToken.Create(), Sample()));
            Assert.That(Directory.GetFiles(Elsewhere), Is.Empty);
            Directory.Delete(HandoffFolder);
            Directory.CreateDirectory(HandoffFolder);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(HandoffFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
                Assert.Throws<IOException>(() => Store().Create(RestartToken.Create(), Sample()));
                Assert.That(LeftInFolder(), Is.Empty);
            }
        }

        [Test]
        public void TheNewAndTheOldProcess_ClaimAHandoffExactlyOnce()
        {
            RestartHandoffStore Handoffs = Store();
            for (int i = 0; i < 200; i++)
            {
                string Token = RestartToken.Create();
                string Written = Handoffs.Create(Token, Sample());
                RestartClaim? Claim = null;
                bool Invalidated = false;
                using var Go = new ManualResetEventSlim(false);
                var Child = Task.Run(() => { Go.Wait(); Claim = Handoffs.Claim(Token, Now); });
                var Parent = Task.Run(() => { Go.Wait(); Invalidated = Handoffs.TryInvalidate(Written); });
                Go.Set();
                Task.WaitAll(Child, Parent);
                Assert.That(Claim!.Succeeded ^ Invalidated, Is.True, "exactly one of them got it (" + Claim.Status + ", " + Invalidated + ")");
            }
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void LeftoversOfAFailure_AreRemoved_OnlyByTheirExactNames_AndOnlyWhenOld()
        {
            Directory.CreateDirectory(HandoffFolder);
            string Hex = new string('a', 64);
            string[] Ours = { "restart-" + Hex + ".json", "restart-" + Hex + ".json.claimed-" + new string('b', 32), "restart-" + Hex + ".json.invalid-" + new string('c', 32) };
            string[] Others = { "restart-" + Hex + ".json.bak", "restart-" + Hex.ToUpperInvariant() + ".json", "notes.json", "restart-" + Hex + ".json.claimed-x" };
            foreach (string Name in Ours.Concat(Others))
            {
                File.WriteAllText(Path.Join(HandoffFolder, Name), "x");
                File.SetLastWriteTimeUtc(Path.Join(HandoffFolder, Name), Now.AddMinutes(-10));
            }
            string Fresh = Store().Create(RestartToken.Create(), Sample());
            File.SetLastWriteTimeUtc(Fresh, Now);
            Assert.That(Store().RemoveStale(Now), Is.EqualTo(Ours.Length));
            Assert.That(LeftInFolder().Select(Path.GetFileName), Is.EquivalentTo(Others.Append(Path.GetFileName(Fresh))));
        }

        // ---- the launch ----

        [Test]
        public void TheTarget_IsThisProcesssOwnExecutable_NeverAShellOrALookup()
        {
            string App = Path.Join(Folder, "CampertronGUI");
            string Host = Path.Join(Folder, "dotnet");
            string Dll = Path.Join(Folder, "CampertronGUI.dll");
            var Exists = new HashSet<string> { App, Host, Dll };
            bool FileExists(string P) => Exists.Contains(P);

            RestartTarget Self = RestartTargets.Resolve(App, Dll, FileExists)!;
            Assert.That((Self.Executable, Self.Kind, Self.Arguments.Count), Is.EqualTo((App, "apphost", 0)), "self-contained or a local apphost: itself, no argument");
            RestartTarget Hosted = RestartTargets.Resolve(Host, Dll, FileExists)!;
            Assert.That((Hosted.Executable, Hosted.Kind), Is.EqualTo((Host, "dotnet")));
            Assert.That(Hosted.Arguments, Is.EqualTo(new[] { Dll }), "the dotnet host runs the entry assembly, nothing else");
            Assert.That(RestartTargets.Resolve(Path.Join(Folder, "dotnet.exe"), Dll, P => true)!.Kind, Is.EqualTo("dotnet"));
            Assert.That(RestartTargets.Resolve(null, Dll, FileExists), Is.Null);
            Assert.That(RestartTargets.Resolve("CampertronGUI", Dll, P => true), Is.Null, "a relative path is never resolved against the working directory or PATH");
            Assert.That(RestartTargets.Resolve(Path.Join(Folder, "missing"), Dll, FileExists), Is.Null);
            Assert.That(RestartTargets.Resolve(Host, null, FileExists), Is.Null);
            Assert.That(RestartTargets.Resolve(Host, "CampertronGUI.dll", P => true), Is.Null);
            Assert.That(RestartTargets.Resolve(Host, Path.Join(Folder, "CampertronGUI.exe"), P => true), Is.Null, "the host runs an assembly only");
            Assert.That(RestartTargets.Current(), Is.Not.Null, "this test process has a target too (resolved only: nothing starts)");
        }

        private sealed class FakeChild : IRestartChild
        {
            public bool HasExited { get; set; }
            public int Kills;
            public void Kill() { Kills++; HasExited = true; }
            public void Dispose() { }
        }

        private (RestartLaunchOptions Options, List<ProcessStartInfo> Started, ManualTime Time) Launch(Func<ProcessStartInfo, IRestartChild?> Start, RestartTarget? Target = null)
        {
            var Started = new List<ProcessStartInfo>();
            var Time = new ManualTime();
            RestartTarget Chosen = Target ?? new RestartTarget(Path.Join(Folder, "CampertronGUI"), Array.Empty<string>(), "apphost");
            var Options = new RestartLaunchOptions
            {
                Store = Store(), Target = () => Chosen, Time = Time, Sleep = D => Time.Advance(D),
                Start = Info => { Started.Add(Info); return Start(Info); },
            };
            return (Options, Started, Time);
        }

        [Test]
        public void ARestart_StartsTheTargetWithTheTokenInItsEnvironmentOnly_AndEndsOnceTheNewProcessClaimsIt()
        {
            RestartClaim? Claimed = null;
            var Target = new RestartTarget(Path.Join(Folder, "dotnet"), new[] { Path.Join(Folder, "CampertronGUI.dll") }, "dotnet");
            var (Options, Started, _) = Launch(Info =>
            {
                // the new process: Program.Main takes the token and claims the handoff
                Claimed = Store().Claim(Info.Environment[RestartToken.EnvironmentVariable], Now);
                return new FakeChild();
            }, Target);
            RestartAttempt Attempt = AutomaticRestart.Run(Sample(), Options, () => true);

            Assert.That(Attempt.Status, Is.EqualTo(RestartAttemptStatus.Acknowledged));
            Assert.That(Started, Has.Count.EqualTo(1), "one process");
            ProcessStartInfo Info = Started[0];
            Assert.That(Info.FileName, Is.EqualTo(Target.Executable));
            Assert.That(Info.UseShellExecute, Is.False, "no shell");
            Assert.That(Info.ArgumentList, Is.EqualTo(Target.Arguments), "an explicit argument list");
            Assert.That(Info.Arguments, Is.Empty);
            string Token = Info.Environment[RestartToken.EnvironmentVariable]!;
            Assert.That(RestartToken.IsWellFormed(Token), Is.True);
            Assert.That(Info.ArgumentList.Any(a => a.Contains(Token)), Is.False, "the token is not on the command line");
            Assert.That(Claimed!.Succeeded, Is.True);
            Assert.That(Claimed.Handoff, Is.EqualTo(Sample()));
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void AStartThatThrowsOrReturnsNothing_IsALaunchFailure_NotRetried_AndLeavesNoHandoff()
        {
            var (Throws, ThrowsStarted, _) = Launch(_ => throw new System.ComponentModel.Win32Exception(2, "No such file /home/someone/x"));
            RestartAttempt Failed = AutomaticRestart.Run(Sample(), Throws, () => true);
            Assert.That((Failed.Status, Failed.Detail), Is.EqualTo((RestartAttemptStatus.LaunchFailed, "Win32Exception")), "the type only: no message or path");
            Assert.That(ThrowsStarted, Has.Count.EqualTo(1), "no second attempt");
            Assert.That(LeftInFolder(), Is.Empty);

            var (Nothing, NothingStarted, _) = Launch(_ => null);
            Assert.That(AutomaticRestart.Run(Sample(), Nothing, () => true).Status, Is.EqualTo(RestartAttemptStatus.LaunchFailed));
            Assert.That(NothingStarted, Has.Count.EqualTo(1));
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void ANewProcessThatEndsWithoutClaiming_IsALaunchFailure()
        {
            var Child = new FakeChild();
            var (Options, _, Time) = Launch(_ => Child);
            Options = new RestartLaunchOptions
            {
                Store = Options.Store, Target = Options.Target, Start = Options.Start, Time = Time,
                Sleep = D => { Time.Advance(D); Child.HasExited = true; },
            };
            Assert.That(AutomaticRestart.Run(Sample(), Options, () => true).Status, Is.EqualTo(RestartAttemptStatus.ChildExited));
            Assert.That(Child.Kills, Is.Zero);
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void ANewProcessThatNeverClaims_IsEndedAfterTheBoundedWait()
        {
            var Child = new FakeChild();
            var (Options, Started, Time) = Launch(_ => Child);
            long Before = Time.GetTimestamp();
            Assert.That(AutomaticRestart.Run(Sample(), Options, () => true).Status, Is.EqualTo(RestartAttemptStatus.NoAcknowledgment));
            Assert.That(Time.GetElapsedTime(Before), Is.GreaterThanOrEqualTo(AutomaticRestart.AcknowledgeTimeout).And.LessThan(AutomaticRestart.AcknowledgeTimeout + TimeSpan.FromSeconds(1)));
            Assert.That(Child.Kills, Is.EqualTo(1), "a process that never took the handoff is ended: no second window later");
            Assert.That(Started, Has.Count.EqualTo(1));
            Assert.That(LeftInFolder(), Is.Empty);
        }

        [Test]
        public void AClaimAtTheLastMoment_IsAcknowledged_AndTheProcessIsKept()
        {
            var Child = new FakeChild();
            string? Token = null;
            var (Options, _, Time) = Launch(Info => { Token = Info.Environment[RestartToken.EnvironmentVariable]; return Child; });
            Options = new RestartLaunchOptions
            {
                Store = Options.Store, Target = Options.Target, Start = Options.Start, Time = Time,
                // the new process claims it during the old one's last wait
                Sleep = D => { Time.Advance(D); if (Time.GetTimestamp() >= 1_000_000_000 + AutomaticRestart.AcknowledgeTimeout.Ticks) { Store().Claim(Token, Now); } },
            };
            Assert.That(AutomaticRestart.Run(Sample(), Options, () => true).Status, Is.EqualTo(RestartAttemptStatus.Acknowledged));
            Assert.That(Child.Kills, Is.Zero);
        }

        [Test]
        public void NoTargetNoFolderAClosedWindowOrAnUnwritableHandoff_StartNothing()
        {
            var (NoTarget, Started, _) = Launch(_ => new FakeChild());
            NoTarget = new RestartLaunchOptions { Store = NoTarget.Store, Target = () => null, Start = NoTarget.Start };
            Assert.That(AutomaticRestart.Run(Sample(), NoTarget, () => true).Status, Is.EqualTo(RestartAttemptStatus.Unavailable));
            var NoFolder = new RestartLaunchOptions { Store = new RestartHandoffStore(null), Target = () => new RestartTarget("/x", Array.Empty<string>(), "apphost"), Start = Info => { Started.Add(Info); return null; } };
            Assert.That(AutomaticRestart.Run(Sample(), NoFolder, () => true).Status, Is.EqualTo(RestartAttemptStatus.Unavailable));

            var (Closed, _, _) = Launch(_ => new FakeChild());
            Assert.That(AutomaticRestart.Run(Sample(), Closed, () => false).Status, Is.EqualTo(RestartAttemptStatus.Abandoned), "an ordinary close: no restart");
            Assert.That(LeftInFolder(), Is.Empty, "and its handoff is gone");

            Directory.Delete(HandoffFolder, true);
            File.WriteAllText(HandoffFolder, "a file where the folder should be");
            var (Unwritable, _, _) = Launch(_ => new FakeChild());
            RestartAttempt Attempt = AutomaticRestart.Run(Sample(), Unwritable, () => true);
            Assert.That(Attempt.Status, Is.EqualTo(RestartAttemptStatus.HandoffFailed));
            Assert.That(Started, Is.Empty, "no process was started in any of these");
        }

        [Test]
        public void TheProductionStart_IsForbiddenInTheGuiTests()
        {
            Assert.That(AutomaticRestart.StartForbidden, Is.True);
            Assert.Throws<InvalidOperationException>(() => AutomaticRestart.StartProcess(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }));
        }

        // ---- the placement ----

        private static readonly PlacementScreen Primary = new PlacementScreen(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, true);
        private static readonly PlacementScreen Left = new PlacementScreen(new PixelRect(-2560, 0, 2560, 1440), new PixelRect(-2560, 0, 2560, 1400), 1, false);
        private static readonly PlacementScreen HighDpi = new PlacementScreen(new PixelRect(1920, 0, 3840, 2160), new PixelRect(1920, 0, 3840, 2100), 2, false);

        private static PlacementResult Fit(RestartWindow Stored, params PlacementScreen[] Screens) =>
            WindowPlacement.Fit(Stored, Screens, 720, 540) ?? throw new AssertionException("no placement");

        [Test]
        public void AWindowWithinAScreen_KeepsItsPlaceAndSize_AndWhetherItWasMaximized()
        {
            Assert.That(Fit(new RestartWindow(100, 80, 1024, 768, false), Primary), Is.EqualTo(new PlacementResult(new PixelPoint(100, 80), 1024, 768, false)));
            Assert.That(Fit(new RestartWindow(100, 80, 1024, 768, true), Primary).Maximized, Is.True);
            Assert.That(Fit(new RestartWindow(-2000, 300, 1200, 900, false), Primary, Left), Is.EqualTo(new PlacementResult(new PixelPoint(-2000, 300), 1200, 900, false)),
                "negative coordinates of a monitor to the left are a real place");
        }

        [Test]
        public void AWindowOnAMonitorThatIsGone_IsMovedOntoTheNearestScreen_WithItsSize()
        {
            PlacementResult Moved = Fit(new RestartWindow(-2000, 300, 1200, 900, false), Primary);
            Assert.That(Moved.Position, Is.EqualTo(new PixelPoint(0, 140)), "moved the least: to the left edge");
            Assert.That((Moved.Width, Moved.Height), Is.EqualTo((1200.0, 900.0)));
            PlacementResult FarAway = Fit(new RestartWindow(9000, 9000, 800, 600, false), Primary, Left);
            Assert.That(FarAway.Position, Is.EqualTo(new PixelPoint(1120, 440)), "into the corner of the nearest screen's working area");
        }

        [Test]
        public void AWindowPartlyOffItsScreen_IsMovedTheLeast_WithItsTitleBarReachable()
        {
            Assert.That(Fit(new RestartWindow(1500, 100, 800, 600, false), Primary).Position, Is.EqualTo(new PixelPoint(1120, 100)), "off the right edge");
            Assert.That(Fit(new RestartWindow(100, 900, 800, 600, false), Primary).Position, Is.EqualTo(new PixelPoint(100, 440)), "off the bottom");
            PlacementResult Above = Fit(new RestartWindow(100, -500, 800, 600, false), Primary);
            Assert.That(Above.Position, Is.EqualTo(new PixelPoint(100, (int)WindowPlacement.TitleBarRoom)), "above the top: room for the title bar below the edge");
        }

        [Test]
        public void AWindowLargerThanTheWorkingArea_IsShrunk_ButNeverBelowTheMinimum()
        {
            PlacementResult Shrunk = Fit(new RestartWindow(0, 0, 5000, 4000, false), Primary);
            Assert.That(Shrunk.Width, Is.EqualTo(1920));
            Assert.That(Shrunk.Height, Is.EqualTo(1040 - WindowPlacement.TitleBarRoom));
            Assert.That(Shrunk.Position, Is.EqualTo(new PixelPoint(0, 0)));
            Assert.That(Fit(new RestartWindow(10, 10, 300, 200, false), Primary), Is.EqualTo(new PlacementResult(new PixelPoint(10, 10), 720, 540, false)), "the window's minimum size");
            var Tiny = new PlacementScreen(new PixelRect(0, 0, 640, 480), new PixelRect(0, 0, 640, 480), 1, true);
            PlacementResult OnTiny = Fit(new RestartWindow(10, 10, 1024, 768, false), Tiny);
            Assert.That((OnTiny.Width, OnTiny.Height), Is.EqualTo((720.0, 540.0)), "never below the minimum, even on a smaller screen");
            Assert.That(OnTiny.Position, Is.EqualTo(new PixelPoint(0, 0)));
        }

        [Test]
        public void AnotherScaling_KeepsTheSizeInDeviceIndependentPixels_AndFitsTheScreensPixels()
        {
            PlacementResult OnHighDpi = Fit(new RestartWindow(2000, 100, 1024, 768, false), HighDpi);
            Assert.That((OnHighDpi.Width, OnHighDpi.Height), Is.EqualTo((1024.0, 768.0)));
            Assert.That(OnHighDpi.Position, Is.EqualTo(new PixelPoint(2000, 100)));
            PlacementResult Big = Fit(new RestartWindow(2000, 0, 3000, 1500, false), HighDpi);
            Assert.That(Big.Width, Is.EqualTo(1920), "3840 pixels at scale 2");
            Assert.That(Big.Position!.Value.X, Is.EqualTo(1920));
        }

        [Test]
        public void TheScreenThatHoldsItsCorner_IsChosen_WhateverItsScaling_AndEmptyScreensAreIgnored()
        {
            var Broken = new PlacementScreen(new PixelRect(0, 0, 0, 0), new PixelRect(0, 0, 0, 0), 1, true);
            var NoScale = new PlacementScreen(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), double.NaN, false);
            Assert.That(Fit(new RestartWindow(1700, 100, 800, 600, false), Primary, HighDpi).Position, Is.EqualTo(new PixelPoint(1120, 100)), "its corner on the primary screen");
            Assert.That(Fit(new RestartWindow(2400, 100, 800, 600, false), Primary, HighDpi).Position, Is.EqualTo(new PixelPoint(2400, 100)), "on the second screen");
            Assert.That(Fit(new RestartWindow(100, 100, 800, 600, false), Broken, NoScale, Primary).Position, Is.EqualTo(new PixelPoint(100, 100)));
        }

        [Test]
        public void WithoutScreenInformation_OnlyTheSizeIsUsed()
        {
            Assert.That(Fit(new RestartWindow(100, 80, 1100, 800, true)), Is.EqualTo(new PlacementResult(null, 1100, 800, true)));
        }

        [TestCase(double.NaN, 600.0)]
        [TestCase(800.0, double.PositiveInfinity)]
        [TestCase(0.0, 600.0)]
        [TestCase(-800.0, 600.0)]
        [TestCase(800.0, 1e9)]
        public void InvalidSizes_GiveNoPlacement_SoTheOrdinaryStartupPlacementApplies(double Width, double Height)
        {
            Assert.That(WindowPlacement.Fit(new RestartWindow(10, 10, Width, Height, false), new[] { Primary }, 720, 540), Is.Null);
            Assert.That(WindowPlacement.Fit(new RestartWindow(int.MaxValue, 10, 800, 600, false), new[] { Primary }, 720, 540), Is.Null);
        }

        // ---- the window ----

        private (MainWindow Window, MainWindowViewModel ViewModel, TestRefresh Refresh) Show(RestartHandoff? Arrival = null, RestartWindow? Place = null, bool Wait = true)
        {
            File.Copy(GuiTestEnvironment.DbFile, Folders.In("RecreationDotOrg.db"), overwrite: true);
            var Refresh = new TestRefresh();
            var Store = new ThemePreferenceStore(Preferences);
            var System = new SystemSettingsViewModel(Store, Store.Load(), _ => { }, () => Application.Current?.ActualThemeVariant);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folders.Config, CachePath = Folders.Cache }, null, System, Refresh.Seams())
            {
                Arrival = Arrival,
            };
            var Window = new MainWindow { DataContext = ViewModel };
            if (Place is RestartWindow Stored)
            {
                Window.ApplyRestartPlacement(Stored);
            }
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            if (Wait)
            {
                WaitForState(ViewModel, StartupState.Ready);
                Settle(Window);
            }
            return (Window, ViewModel, Refresh);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static CampgroundDataView Card(MainWindow Window) =>
            Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<CampgroundDataView>("CampgroundDataContent")!;

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().Single(c => c.Name == Name);

        private static void RefreshAndEnd(MainWindow Window, TestRefresh Refresh, RidbRefreshResult Result)
        {
            CampgroundDataView View = Card(Window);
            In<Button>(View, "RefreshDatabaseButton").Command!.Execute(null);
            Settle(Window);
            In<Button>(View, "StartRefreshButton").Command!.Execute(null);
            Settle(Window);
            Assert.That(Refresh.WaitForCall(), Is.True);
            Refresh.Finish(Result);
            CampgroundDataViewModel Data = (CampgroundDataViewModel)View.DataContext!;
            WaitUntil(() => !Data.IsRunning, () => "the refresh did not end");
            Settle(Window);
        }

        private static RidbRefreshResult Ended(RidbRefreshOutcome Outcome, bool Installed) =>
            new RidbRefreshResult(Outcome, RidbRefreshStage.Completed, Installed, Installed ? DateTime.UtcNow : null, TimeSpan.FromSeconds(1), RidbRefreshResult.NoProblems,
                null, Outcome == RidbRefreshOutcome.Canceled ? new OperationCanceledException() : null, false);

        [AvaloniaTest]
        public void TheCapture_IsTheWindowsNormalBounds_AlsoWhenItIsMaximized_AndTheTabsShown()
        {
            var (Window, ViewModel, _) = Show();
            Window.Position = new PixelPoint(140, 60);
            Resize(Window, 900, 700);
            Settle(Window);
            Window.RememberNormalBounds();
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            RestartCapture Normal = ViewModel.RestartHooks.Capture!()!.Value;
            Assert.That(Normal.Window, Is.EqualTo(new RestartWindow(140, 60, 900, 700, false)));
            Assert.That((Normal.Tab, Normal.SettingsTab), Is.EqualTo((RestartTopTab.Results, RestartSettingsTab.Campertron)));

            Window.WindowState = WindowState.Maximized;
            Settle(Window);
            Assert.That(ViewModel.RestartHooks.Capture!()!.Value.Window, Is.EqualTo(new RestartWindow(140, 60, 900, 700, true)), "the bounds it had before it was maximized");
            Window.WindowState = WindowState.Minimized;
            Settle(Window);
            Assert.That(ViewModel.RestartHooks.Capture!()!.Value.Window!.Value.Maximized, Is.False, "never minimized: a normal window");
            Window.Close();
        }

        [AvaloniaTest]
        public void ARestartedWindow_TakesTheOldPlace_AndIsMaximizedOnlyAfterItOpened()
        {
            var (Window, _, _) = Show(Place: new RestartWindow(150, 70, 1000, 720, false), Wait: false);
            Assert.That(Window.AppliedPlacement, Is.Not.Null);
            Assert.That(Window.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.Manual));
            Assert.That(Window.Position, Is.EqualTo(Window.AppliedPlacement!.Value.Position ?? Window.Position));
            Assert.That((Window.Width, Window.Height), Is.EqualTo((Window.AppliedPlacement.Value.Width, Window.AppliedPlacement.Value.Height)));
            Assert.That(Window.WindowState, Is.EqualTo(WindowState.Normal));
            Window.Close();

            var (Maximized, _, _) = Show(Place: new RestartWindow(150, 70, 1000, 720, true), Wait: false);
            Assert.That(Maximized.WindowState, Is.EqualTo(WindowState.Maximized), "maximized once shown");
            Assert.That(Maximized.CaptureForRestart()!.Value.Window!.Value.Width, Is.EqualTo(Maximized.AppliedPlacement!.Value.Width), "its normal bounds are the restored ones");
            Maximized.Close();

            var (Invalid, _, _) = Show(Place: new RestartWindow(150, 70, double.NaN, 720, false), Wait: false);
            Assert.That(Invalid.AppliedPlacement, Is.Null);
            Assert.That(Invalid.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.CenterScreen), "the ordinary startup placement");
            Invalid.Close();
        }

        [AvaloniaTest]
        public void ARestartedProcess_OpensAtSettingsSystem_AfterItsStartup_WithItsOneTimeStatus_AndStartsNothing()
        {
            Folders.Write("General.yaml", "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: " + DateTime.UtcNow.ToString("o") + "\nautoRefresh: false\n");
            // before the startup is ready there is nothing to navigate to
            var Early = new MainWindowViewModel(() => new CampertronInternalConfig()) { Arrival = new RestartHandoff(Now, RestartOutcome.Refreshed, null, RestartTopTab.Searches, RestartSettingsTab.Campertron) };
            Assert.That(Early.TakeRestartNavigation(), Is.False);
            var (Window, ViewModel, Refresh) = Show(Arrival: new RestartHandoff(Now, RestartOutcome.Refreshed, null, RestartTopTab.Searches, RestartSettingsTab.Campertron),
                Place: new RestartWindow(0, 0, 900, 600, false));

            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SettingsTabIndex), "Settings > System, whatever tab the old window showed");
            Assert.That(SettingsTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SystemSettingsTabIndex));
            // the campground data card, with the status and the new date, is scrolled into view
            ScrollViewer Scroll = Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<ScrollViewer>("SystemScrollViewer")!;
            TextBlock StatusLine = In<TextBlock>(Card(Window), "RefreshStatusText");
            double Top = StatusLine.TranslatePoint(new Point(0, 0), Scroll)!.Value.Y;
            Assert.That(Top, Is.InRange(0, Scroll.Viewport.Height - StatusLine.Bounds.Height), "the card's status is visible without scrolling");
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Assert.That(Data.StatusText, Is.EqualTo(CampgroundDataViewModel.RestartedText));
            Assert.That(Data.ResultKind, Is.EqualTo(RefreshResultKind.Success));
            Assert.That(In<TextBlock>(Card(Window), "RefreshStatusText").IsEffectivelyVisible, Is.True);
            Assert.That(AutomationProperties.GetLiveSetting(In<TextBlock>(Card(Window), "RefreshStatusText")), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(Data.LastRefreshedText, Does.StartWith("today"), "the new date, read from General.yaml");
            Assert.That(ViewModel.SystemSettings.Configuration!.Items[5].Value, Does.StartWith("today"));
            Assert.That(Data.IsConfirming, Is.False, "the confirmation does not reopen");
            Assert.That(Refresh.Calls, Is.Zero, "no refresh starts");
            Assert.That(Refresh.Handoffs, Is.Empty, "no restart: a consumed handoff never restarts again");
            Assert.That(ViewModel.Searches!.Files, Is.Not.Null, "the other tabs were created once, by the startup");
            Assert.That(ViewModel.TakeRestartNavigation(), Is.False, "once");

            // one read per showing or activation: no handler registered twice
            int Reads = Data.Reads;
            Window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Data.Reads, Is.EqualTo(Reads + 1));
            Window.Close();
        }

        [AvaloniaTest]
        public void ARestartAfterARefreshWithoutItsDate_SaysSo()
        {
            var (Window, ViewModel, _) = Show(Arrival: new RestartHandoff(Now, RestartOutcome.RefreshedWithoutTimestamp, null, RestartTopTab.Settings, RestartSettingsTab.System));
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Assert.That(Data.StatusText, Is.EqualTo(CampgroundDataViewModel.RestartedWithoutTimestampText));
            Assert.That(Data.ResultKind, Is.EqualTo(RefreshResultKind.Warning));
            Assert.That(Data.ResultIsError, Is.False);
            Window.Close();
        }

        [AvaloniaTest]
        public void AnOrdinaryStart_RestoresNothing_AndAnOrdinaryCloseHandsNothingOn()
        {
            var (Window, ViewModel, Refresh) = Show();
            Assert.That(Window.AppliedPlacement, Is.Null);
            Assert.That(Window.WindowStartupLocation, Is.EqualTo(WindowStartupLocation.CenterScreen));
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SearchesTabIndex), "the ordinary first tab");
            Assert.That(ViewModel.SystemSettings.CampgroundData!.HasStatus, Is.False, "no one-time status");
            Assert.That(ViewModel.TakeRestartNavigation(), Is.False);
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Refresh.Handoffs, Is.Empty);
            Assert.That(Refresh.Exits, Is.Zero);
            Assert.That(Directory.Exists(RestartHandoffStore.ForCurrentUser().Folder!), Is.False, "no handoff folder: nothing is kept for a later start");
        }

        [AvaloniaTest]
        public void ALeftoverHandoff_IsIgnoredByAnOrdinaryStart_AndLeftForTheNextRestartsCleanup()
        {
            RestartHandoffStore Real = RestartHandoffStore.ForCurrentUser();
            string Gui = Path.GetDirectoryName(Real.Folder!)!;
            bool GuiExisted = Directory.Exists(Gui);
            string Leftover = Real.Create(RestartToken.Create(), Sample());
            try
            {
                byte[] Before = File.ReadAllBytes(Leftover);
                var (Window, ViewModel, _) = Show();
                Assert.That(Window.AppliedPlacement, Is.Null, "an ordinary start has no token, so it reads no handoff");
                Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SearchesTabIndex));
                Assert.That(ViewModel.SystemSettings.CampgroundData!.HasStatus, Is.False);
                Assert.That(File.ReadAllBytes(Leftover), Is.EqualTo(Before));
                Window.Close();
                Assert.That(Real.RemoveStale(DateTime.UtcNow.AddMinutes(5)), Is.EqualTo(1), "the next restart removes it once it is old");
            }
            finally
            {
                // other tests require the GUI's own folder to be absent (no theme preference written)
                Directory.Delete(GuiExisted ? Real.Folder! : Gui, true);
            }
        }

        [AvaloniaTest]
        public void TheRestartingState_IsSaidInWords_WithoutRefreshOrCancel()
        {
            var (Window, ViewModel, Refresh) = Show();
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
            using var Hold = new ManualResetEventSlim(false);
            Refresh.HoldRestart = Hold;
            RefreshAndEnd(Window, Refresh, Ended(RidbRefreshOutcome.Refreshed, true));
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            CampgroundDataView View = Card(Window);

            Assert.That(Data.IsRestarting, Is.True);
            Assert.That(In<TextBlock>(View, "RefreshStatusText").Text, Is.EqualTo(CampgroundDataViewModel.RestartingText));
            Assert.That(SeverityOf(In<TextBlock>(View, "RefreshStatusText")), Is.Not.EqualTo("Error"));
            Assert.That(In<StackPanel>(View, "RefreshActions").IsVisible, Is.False, "no Refresh");
            Assert.That(In<StackPanel>(View, "RefreshProgressPanel").IsVisible, Is.False, "no Cancel: the data is installed");
            Assert.That(Data.CancelCommand.CanExecute(null), Is.False);
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.Refresh), "no search starts meanwhile");
            Assert.That(Refresh.Exits, Is.Zero, "not before the new process took the handoff");
            Hold.Set();
            CampgroundDataTests.WaitForRestart(Data);
            Assert.That(Refresh.Exits, Is.EqualTo(1), "then this process ends");
            Window.Close();
        }

        [AvaloniaTest]
        public void ALaunchFailure_LeavesTheWindowUsable_SaysToRestartManually_AndIsNotRetried()
        {
            var (Window, ViewModel, Refresh) = Show();
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
            Refresh.NextRestart = new RestartAttempt(RestartAttemptStatus.LaunchFailed, "Win32Exception");
            RefreshAndEnd(Window, Refresh, Ended(RidbRefreshOutcome.Refreshed, true));
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            CampgroundDataTests.WaitForRestart(Data);
            Settle(Window);

            TextBlock Status = In<TextBlock>(Card(Window), "RefreshStatusText");
            Assert.That(Status.Text, Does.StartWith(CampgroundDataViewModel.RestartFailedText + " Elapsed "));
            Assert.That(Status.Text, Does.Not.Contain("Win32Exception").And.Not.Contain("/"), "no exception or path in the window");
            Assert.That(SeverityOf(Status), Is.Not.EqualTo("Error"), "the data was refreshed: a warning, said in words");
            Assert.That(Data.LastRestart!.Detail, Is.EqualTo("Win32Exception"), "kept for diagnostics");
            Assert.That(Data.NeedsManualRestart, Is.True);
            Assert.That(Data.IsRestarting, Is.False);
            Assert.That(Refresh.Exits, Is.Zero, "the window stays");
            Assert.That(Refresh.Handoffs.Count, Is.EqualTo(1), "one attempt, no retry");
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.True, "usable again");
            Assert.That((Focused(Window) as Control)?.Name, Is.EqualTo("RefreshDatabaseButton"), "the focus goes back to the card's action");
            Assert.That(Window.IsVisible, Is.True);
            Window.Close();
        }

        // An automatic restart would lose an unsaved edit of General.yaml, Email.yaml or a search file, so with one there
        // is none (the window stays and says why; CampgroundDataTests checks the words)
        [AvaloniaTest]
        public void UnsavedChangesInAnyEditor_PreventTheAutomaticRestart([Values("General", "Email", "Search")] string Editor)
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var (Window, ViewModel, _) = Show();
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.False);
            switch (Editor)
            {
                case "General":
                    ViewModel.Settings!.General.RefreshInterval.Text = "45";
                    break;
                case "Email":
                    ViewModel.Settings!.Email.SmtpServer.Text = "smtp.changed.example.com";
                    break;
                default:
                    WaitUntil(() => ViewModel.Searches!.Files.Count > 0, () => "the search file was not listed");
                    ViewModel.Searches!.Files[0].DisplayName.Text = "Alpha changed";
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.True, Editor);
            Window.Close();
        }

        [AvaloniaTest]
        public void AFailedOrCanceledRefresh_NeverRestarts([Values("DownloadFailed", "Canceled", "InstalledButCheckFailed", "ValidationFailed", "Unexpected")] string OutcomeName)
        {
            RidbRefreshOutcome Outcome = Enum.Parse<RidbRefreshOutcome>(OutcomeName);
            var (Window, ViewModel, Refresh) = Show();
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
            RefreshAndEnd(Window, Refresh, Ended(Outcome, Outcome == RidbRefreshOutcome.InstalledButCheckFailed));
            Dispatcher.UIThread.RunJobs();
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Assert.That(Refresh.Handoffs, Is.Empty);
            Assert.That(Refresh.Exits, Is.Zero);
            Assert.That(Data.IsRestarting, Is.False);
            Assert.That(Data.LastRestart, Is.Null);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));
            Window.Close();
        }

        [AvaloniaTest]
        public void ClosingTheWindowWhileTheRestartHandsOff_IsAnOrdinaryClose()
        {
            var (Window, ViewModel, Refresh) = Show();
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
            using var Hold = new ManualResetEventSlim(false);
            Refresh.HoldRestart = Hold;
            RefreshAndEnd(Window, Refresh, Ended(RidbRefreshOutcome.Refreshed, true));
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Hold.Set();
            Assert.That(Data.PendingRestart.Wait(10000), Is.True);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Refresh.StillWantedAtEnd, Is.False, "the restart sees the window closed");
            Assert.That(Data.PendingRestart.Result!.Status, Is.EqualTo(RestartAttemptStatus.Abandoned));
            Assert.That(Refresh.Exits, Is.Zero, "nothing more is done for a closed window");
            Assert.That(ViewModel.PendingRefresh.IsCompleted, Is.True, "Program.Main has nothing to wait for");
        }

        // The attempt ended (the new process had claimed the handoff, or the start had failed) but its end reaches the UI
        // thread only after the window closed: like every late callback of the card (D61), it changes nothing, and the
        // process, which is ending with its window, is not asked to end again.
        [AvaloniaTest]
        [TestCase("Acknowledged")]
        [TestCase("LaunchFailed")]
        public void ARestartWhoseEndArrivesAfterTheClose_ChangesNothing(string Status)
        {
            var (Window, ViewModel, Refresh) = Show();
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
            using var Hold = new ManualResetEventSlim(false);
            Refresh.HoldRestart = Hold;
            Refresh.EndedBeforeTheClose = true;
            Refresh.NextRestart = new RestartAttempt(Enum.Parse<RestartAttemptStatus>(Status));
            RefreshAndEnd(Window, Refresh, Ended(RidbRefreshOutcome.Refreshed, true));
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Assert.That(Data.StatusText, Is.EqualTo(CampgroundDataViewModel.RestartingText));
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Hold.Set();
            CampgroundDataTests.WaitForRestart(Data);
            Assert.That(Data.LastRestart?.Status.ToString(), Is.EqualTo(Status), "the end arrived");
            Assert.That(Refresh.Exits, Is.Zero, "a closed window's process is not asked to end again");
            Assert.That(Data.StatusText, Is.EqualTo(CampgroundDataViewModel.RestartingText), "no words for a closed window");
            Assert.That(Data.NeedsManualRestart, Is.False);
            Assert.That(ViewModel.PendingRefresh.IsCompleted, Is.True, "Program.Main has nothing to wait for");
        }

        // From the IL: the handoff, the launch and the placement use no settings, email or database code, write no
        // configuration file and never use a shell; only AutomaticRestart starts a process.
        [Test]
        public void TheRestartsCode_TouchesNoSettingOrDatabase_AndUsesNoShell()
        {
            List<string> Called = ReferencedMembers(typeof(RestartHandoffStore), typeof(RestartToken), typeof(AutomaticRestart), typeof(RestartTargets), typeof(WindowPlacement))
                .Select(c => c.Member).Distinct().ToList();
            string[] Forbidden = { "CampertronLibrary.", "EmailConfig", "GeneralConfig", "Yaml", "ConfigFileReplace", "System.Console", "System.Net.", "Microsoft.Data.Sqlite" };
            Assert.That(Called.Where(m => Forbidden.Any(f => m.Contains(f, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called, Does.Contain("System.Diagnostics.ProcessStartInfo::get_ArgumentList"));
            Assert.That(Called, Does.Contain("System.Diagnostics.Process::Start"));
            Assert.That(Called, Does.Not.Contain("System.Diagnostics.ProcessStartInfo::set_Arguments"), "an argument list, never one command line");
        }
    }
}
