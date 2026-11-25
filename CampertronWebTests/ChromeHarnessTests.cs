using System.Diagnostics;

namespace CampertronWebTests
{
    // The browser tests' own Chrome is stopped whatever happens, and only it (D109's correction of PR #140's leak: a browser
    // test whose own setup failed left its headless Chrome and its folder behind). A scene is built whole or not at all
    // (LiveScene.Build), a Chrome whose own start fails is disposed by Start, and what is stopped is only what the instance
    // started: its process tree and the processes left that run its Chrome's executable with its own folder on their
    // command line. Another Chrome (another profile, as a user's or another test's), and a process that only names the
    // folder, are never touched.
    public class ChromeHarnessTests
    {
        // a Chrome the test starts without the harness, with a profile of its own: what the harness must never stop
        private sealed class Decoy : IDisposable
        {
            private readonly Process _Process;
            internal string Folder { get; }

            private Decoy(Process Process, string Folder)
            {
                _Process = Process;
                this.Folder = Folder;
            }

            internal static async Task<Decoy> Start()
            {
                string Folder = Path.Join(Path.GetTempPath(), "CampertronDecoy-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Folder);
                var Info = new ProcessStartInfo(Chrome.Require()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string Argument in new[] { "--headless=new", "--no-sandbox", "--disable-gpu", "--no-first-run", "--disable-background-networking", "--disable-component-update",
                    "--host-resolver-rules=MAP * ~NOTFOUND", "--remote-debugging-port=0", "--user-data-dir=" + Path.Join(Folder, "profile"), "about:blank" })
                {
                    Info.ArgumentList.Add(Argument);
                }
                Info.Environment["HOME"] = Folder;
                Process Started = Process.Start(Info)!;
                Started.OutputDataReceived += (_, _) => { };
                Started.ErrorDataReceived += (_, _) => { };
                Started.BeginOutputReadLine();
                Started.BeginErrorReadLine();
                var Decoy_ = new Decoy(Started, Folder);
                var Clock = Stopwatch.StartNew();
                while (!File.Exists(Path.Join(Folder, "profile", "DevToolsActivePort")) && Clock.Elapsed < TimeSpan.FromSeconds(30))
                {
                    await Task.Delay(50);
                }
                Assert.That(Decoy_.IsAlive, Is.True, "the other Chrome runs");
                return Decoy_;
            }

            internal bool IsAlive => !_Process.HasExited && Chrome.ProcessesOf(Folder, Path.GetDirectoryName(Chrome.ExecutableOf(_Process.Id))).Count > 0;

            public void Dispose()
            {
                if (!_Process.HasExited)
                {
                    _Process.Kill(entireProcessTree: true);
                    _Process.WaitForExit(5000);
                }
                _Process.Dispose();
                Directory.Delete(Folder, true);
            }
        }

        // a scene with a browser and no web UI
        private sealed class Bare : LiveScene
        {
            protected override WebTestHost? Web => null;
            protected override ValueTask DisposeWeb() => ValueTask.CompletedTask;
        }

        [Test]
        public async Task ASceneWhoseOwnSetupFails_LeavesNoChromeOrFolderOfItsOwn_AndStopsNoOtherChrome()
        {
            Chrome.Require();
            using Decoy Other = await Decoy.Start();
            Chrome? Made = null;
            int MadeCount = 0;
            Assert.That(async () => await LiveScene.Build(new Bare(), async S =>
            {
                await S.Browse();
                Made = S.B;
                MadeCount = Chrome.ProcessesOf(S.B.Folder, S.B.Binaries).Count;
                throw new InvalidOperationException("a step of the scene's setup failed");
            }), Throws.InvalidOperationException.With.Message.EqualTo("a step of the scene's setup failed"), "the setup's own failure, unchanged");
            Assert.That(Made, Is.Not.Null);
            Assert.That(MadeCount, Is.GreaterThan(0), "the scene's Chrome ran, found by its executable and its own folder");
            Assert.That(Chrome.ProcessesOf(Made!.Folder, Made.Binaries), Is.Empty, "none of its processes is left");
            Assert.That(Directory.Exists(Made.Folder), Is.False, "its folder is gone");
            Assert.That(Other.IsAlive, Is.True, "another Chrome, with a profile of its own, is never stopped");
        }

        [Test]
        public async Task AChromeWhoseOwnStartFails_IsStopped_AndItsFolderRemoved()
        {
            Chrome.Require();
            using Decoy Other = await Decoy.Start();
            (string Folder, string? Binaries, int Count) Seen = (string.Empty, null, 0);
            Assert.That(async () => await Chrome.Start(Prepare: B =>
            {
                Seen = (B.Folder, B.Binaries, Chrome.ProcessesOf(B.Folder, B.Binaries).Count);
                throw new InvalidOperationException("the start failed");
            }), Throws.InvalidOperationException);
            Assert.That(Seen.Count, Is.GreaterThan(0));
            Assert.That(Chrome.ProcessesOf(Seen.Folder, Seen.Binaries), Is.Empty);
            Assert.That(Directory.Exists(Seen.Folder), Is.False);
            Assert.That(Other.IsAlive, Is.True);
        }

        [Test]
        public async Task OnlyItsOwnProcessesAreItsOwn_AnotherChromeOrAProcessThatOnlyNamesItsFolder_SurvivesItsEnd()
        {
            Chrome.Require();
            using Decoy Other = await Decoy.Start();
            Chrome B = await Chrome.Start();
            //a process that is no Chrome but names the instance's folder on its command line
            var Info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            foreach (string Argument in new[] { "-c", "sleep 120; :", B.Folder })
            {
                Info.ArgumentList.Add(Argument);
            }
            using Process Namer = Process.Start(Info)!;
            try
            {
                List<int> Own = Chrome.ProcessesOf(B.Folder, B.Binaries);
                Assert.That(Own, Does.Contain(B.Pid), "the process it started");
                Assert.That(Own, Does.Not.Contain(Namer.Id), "a process of another executable is never its own, whatever it names");
                Assert.That(Own.Intersect(Chrome.ProcessesOf(Other.Folder, B.Binaries)), Is.Empty, "another profile's Chrome is never its own");
                await B.DisposeAsync();
                Assert.That(Chrome.ProcessesOf(B.Folder, B.Binaries), Is.Empty);
                Assert.That(Directory.Exists(B.Folder), Is.False);
                Assert.That(Other.IsAlive, Is.True, "the other Chrome runs on");
                Assert.That(Namer.HasExited, Is.False, "the process that only named the folder runs on");
            }
            finally
            {
                await B.DisposeAsync();
                if (!Namer.HasExited)
                {
                    Namer.Kill(entireProcessTree: true);
                    Namer.WaitForExit(5000);
                }
            }
        }
    }
}
