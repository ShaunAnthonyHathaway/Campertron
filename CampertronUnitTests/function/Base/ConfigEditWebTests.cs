using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.WebAccess;

namespace CampertronUnitTests.function.Base
{
    // Web.yaml, the web UI's own settings (decisions D81 and D92; business-rules-settings §31): never a search file; its
    // validation; the settings editing session, in which a missing file is a new file that only a save creates,
    // owner-only; and the one confirmation of a save that leaves the web UI open to the network without a password,
    // asked once and never again.
    public class ConfigEditWebTests : ConfigEditTestBase
    {
        private string Web => Path.Join(Folder, "Web.yaml");

        private ConfigEditSession<WebConfig> OpenWeb() => new ConfigEditSession<WebConfig>(Folder, () => Now);

        // ---- the file ----

        [TestCase("Web.yaml")]
        [TestCase("web.yaml")]
        [TestCase("WEB.YAML")]
        public void WebYaml_IsASettingsFile_NeverASearch_AndASearchCantBeNamedSo(string Name)
        {
            Assert.That(ConfigFiles.Classify(Name), Is.EqualTo(ConfigFileKind.Web));
            Assert.That(ConfigFiles.Classify(Path.Join(Folder, Name)), Is.EqualTo(ConfigFileKind.Web));
            Assert.That(ConfigFiles.Classify("Website.yaml"), Is.EqualTo(ConfigFileKind.Search));
            Assert.That(ConfigFiles.Classify("Web.yml"), Is.EqualTo(ConfigFileKind.Search));
            Write(Path.Join(Folder, Name), "passwordProtection: false\n");
            Write(Path.Join(Folder, "Trip.yaml"), "displayName: Trip\n");
            Assert.That(ConfigFiles.SortedSearchFileNames(Folder), Is.EqualTo(new[] { "Trip.yaml" }), "the console's Search and the monitoring never read Web.yaml as a search");
            Assert.That(SearchFileName.Check(Path.GetFileNameWithoutExtension(Name), out string FileName),
                Is.EqualTo("General.yaml, Email.yaml, Ntfy.yaml and Web.yaml are Campertron's settings files, not search files; choose another name."));
            Assert.That(FileName, Is.Empty);
        }

        [Test]
        public void TheLoader_ReadsWebYaml_WithTheDefaultsForWhatItLeavesOut_AndNeverCreatesIt()
        {
            Assert.That(Yaml.WebConfigGetConfig(Folder), Is.Null, "no file: the caller's defaults");
            Assert.That(Files(), Is.Empty);
            var Defaults = new WebConfig();
            Assert.That((Defaults.PasswordProtection, Defaults.NetworkListening, Defaults.AllowedHosts, Defaults.TrustedProxies), Is.EqualTo((true, true, (List<string>?)null, (List<string>?)null)),
                "a password required and reachable from the network (D81)");

            Write(Web, "# only a comment\n");
            WebConfig Empty = Yaml.WebConfigGetConfig(Folder)!;
            Assert.That((Empty.PasswordProtection, Empty.NetworkListening), Is.EqualTo((true, true)));

            Write(Web, "networkListening: false\nallowedHosts:\n- campertron.example.com\ntrustedProxies: [172.18.0.2, 'fd00::/8']\n");
            WebConfig Read = Yaml.WebConfigGetConfig(Folder)!;
            Assert.That((Read.PasswordProtection, Read.NetworkListening), Is.EqualTo((true, false)));
            Assert.That(Read.AllowedHosts, Is.EqualTo(new[] { "campertron.example.com" }));
            Assert.That(Read.TrustedProxies, Is.EqualTo(new[] { "172.18.0.2", "fd00::/8" }));
        }

        // ---- the validation ----

        [Test]
        public void AValidWebYaml_HasNoFinding_AlsoWithoutAPasswordOnTheNetwork()
        {
            foreach (string Text in new[]
            {
                "", "# nothing\n", "passwordProtection: true\nnetworkListening: true\n",
                "passwordProtection: false\nnetworkListening: true\n",
                "allowedHosts:\n- campertron.example.com\n- nas\ntrustedProxies:\n- 172.18.0.2\n- 10.0.0.0/8\n- ::1\n- fd00::/8\n"
            })
            {
                Assert.That(ConfigValidation.ValidateWeb(Text), Is.Empty, Text + ": the open state is asked once at the save, and is no lasting finding (D81)");
            }
        }

        [Test]
        public void WhatWebYamlCantMean_IsAnError_ThatNamesTheItemAndNeverItsValue()
        {
            const string Text = "passwordProtection: false\nallowedHosts:\n- campertron.example.com\n- https://secret-host.example:8443/\n- ''\n- 10.0.0.5\n- '*.example.com'\ntrustedProxies:\n- 172.18.0.2\n- proxy.secret.example\n- 10.9.8.7/8\n- \"\"\n";
            List<ConfigDiagnostic> Found = ConfigValidation.ValidateWeb(Text);
            Assert.That(Found.Select(D => (D.Code, D.Severity, D.Key, D.Index, D.Line)), Is.EqualTo(new[]
            {
                (ConfigCode.WebHostInvalid, ConfigSeverity.Error, "allowedHosts", (int?)1, 4),
                (ConfigCode.WebHostEmpty, ConfigSeverity.Error, "allowedHosts", (int?)2, 5),
                (ConfigCode.WebHostInvalid, ConfigSeverity.Error, "allowedHosts", (int?)3, 6),
                (ConfigCode.WebHostInvalid, ConfigSeverity.Error, "allowedHosts", (int?)4, 7),
                (ConfigCode.WebProxyInvalid, ConfigSeverity.Error, "trustedProxies", (int?)1, 10),
                (ConfigCode.WebProxyInvalid, ConfigSeverity.Error, "trustedProxies", (int?)2, 11),
                (ConfigCode.WebProxyEmpty, ConfigSeverity.Error, "trustedProxies", (int?)3, 12),
            }));
            Assert.That(Found, Has.All.Property(nameof(ConfigDiagnostic.FileKind)).EqualTo(ConfigFileKind.Web));
            Assert.That(Found, Has.All.Property(nameof(ConfigDiagnostic.FileName)).EqualTo("Web.yaml"));
            Assert.That(Found[0].Message, Is.EqualTo("allowedHosts item 2 must be a host name such as campertron.example.com: no port, no http://, no wildcard. An IP address needs no entry."));
            Assert.That(Found[4].Message, Is.EqualTo("trustedProxies item 2 must be an IP address such as 172.18.0.2, or a network such as 172.18.0.0/16 (the network's own address and its prefix length)."));
            string All = string.Join("\n", Found.Select(D => D.Message));
            Assert.That(All, Does.Not.Contain("secret").And.Not.Contain("10.9.8.7").And.Not.Contain("8443"));
        }

        [Test]
        public void WhatTheLoaderRefusesInWebYaml_IsAnError()
        {
            Assert.That(ConfigValidation.ValidateWeb("passwordProtection: maybe\n").Select(D => (D.Code, D.Key)), Is.EqualTo(new[] { (ConfigCode.InvalidBoolean, (string?)"passwordProtection") }));
            Assert.That(ConfigValidation.ValidateWeb("allowedHosts: campertron.example.com\n").Select(D => (D.Code, D.Key)), Is.EqualTo(new[] { (ConfigCode.NotAList, (string?)"allowedHosts") }));
            Assert.That(ConfigValidation.ValidateWeb("password: hunter2-secret\n").Single().Code, Is.EqualTo(ConfigCode.UnknownKey), "the web UI's password is never in a YAML file");
            Assert.That(ConfigValidation.ValidateWeb("- a\n- b\n").Select(D => D.Severity), Has.Member(ConfigSeverity.Error));
            Write(Web, "trustedProxies:\n- not a proxy\n");
            Assert.That(ConfigValidation.ValidateFile(Web, Now).Single().Code, Is.EqualTo(ConfigCode.WebProxyInvalid), "a file is judged by its name");
        }

        // ---- the editing session ----

        [Test]
        public void AMissingWebYaml_IsANewFile_ThatOnlyASaveCreates_OwnerOnly()
        {
            var Session = OpenWeb();
            Assert.That((Session.Kind, Session.FileName, Session.State, Session.IsNew, Session.IsDirty, Session.CanSave),
                Is.EqualTo((ConfigFileKind.Web, "Web.yaml", ConfigEditState.Editable, true, false, false)));
            Assert.That(Session.Keys, Is.EqualTo(new[] { "passwordProtection", "networkListening", "allowedHosts", "trustedProxies" }));
            Assert.That((Session.Value("passwordProtection"), Session.Value("networkListening"), Session.Value("allowedHosts"), Session.Value("trustedProxies")),
                Is.EqualTo(((object?)true, (object?)true, (object?)null, (object?)null)));
            Assert.That(Session.SecretKeys, Is.Empty, "Web.yaml holds no secret");
            Assert.That(Session.Notices.Single().Message, Is.EqualTo(
                "Web.yaml does not exist yet. The form shows the default settings (a password required, reachable from the network); nothing is written until you change a setting and save, and Save creates the file."));
            Assert.That(Session.Save().Message, Is.EqualTo("Nothing to save: Web.yaml has no changes."));
            Assert.That(Session.Create().Message, Is.EqualTo("Web.yaml is created when you save your settings; nothing was written."));
            Session.Set("passwordProtection", true);
            Session.Set("networkListening", true);
            Assert.That(Session.IsDirty, Is.False, "the defaults are no edit");
            Assert.That(Files(), Is.Empty);

            Session.Set("networkListening", false);
            Session.SetItems("allowedHosts", new[] { new ConfigListItem(null, "campertron.example.com") });
            Assert.That(Session.CanSave, Is.True);
            Assert.That(Files(), Is.Empty, "nothing is written while editing");
            ConfigEditResult Saved = Session.Save();
            Assert.That(Saved.Succeeded, Is.True, Saved.Message);
            Assert.That(Files(), Is.EqualTo(new[] { "Web.yaml" }), "a new file has no backup");
            Assert.That(Text(Web), Is.EqualTo("# Web.yaml: the settings of Campertron's web UI (CampertronConsole --web).\npasswordProtection: true\nnetworkListening: false\nallowedHosts: [campertron.example.com]\n"), "the editor writes a new list as it does for every settings file");
            if (OnUnix("the settings folder's access rules"))
            {
                Assert.That(File.GetUnixFileMode(Web), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            Assert.That((Session.IsNew, Session.IsDirty), Is.EqualTo((false, false)));
            WebConfig Loaded = Yaml.WebConfigGetConfig(Folder)!;
            Assert.That((Loaded.PasswordProtection, Loaded.NetworkListening, Loaded.AllowedHosts!.Single()), Is.EqualTo((true, false, "campertron.example.com")));
        }

        [Test]
        public void AnExistingWebYaml_IsChangedInPlace_KeepingItsCommentsAndOrder_WithABackup()
        {
            const string Original = "# my web settings\r\nnetworkListening: true   # every interface\r\n\r\ntrustedProxies:\r\n  - 172.18.0.2 # the proxy\r\n";
            Write(Web, Original);
            var Session = OpenWeb();
            Assert.That((Session.State, Session.IsNew), Is.EqualTo((ConfigEditState.Editable, false)));
            Assert.That(Session.Value("passwordProtection"), Is.EqualTo(true), "left out: the default");
            Session.Set("networkListening", false);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Web), Is.EqualTo("# my web settings\r\nnetworkListening: false   # every interface\r\n\r\ntrustedProxies:\r\n  - 172.18.0.2 # the proxy\r\n"));
            Assert.That(Text(Web + ".bak"), Is.EqualTo(Original));
            Assert.That(Files(), Is.EqualTo(new[] { "Web.yaml", "Web.yaml.bak" }));
        }

        [Test]
        public void AnInvalidItem_BlocksTheSave_AndNothingIsWritten()
        {
            var Session = OpenWeb();
            Session.SetItems("trustedProxies", new[] { new ConfigListItem(null, "proxy.example.com") });
            Assert.That((Session.CanSave, Session.HasBlockingErrors), Is.EqualTo((false, true)));
            Assert.That(Session.Notices.Where(N => N.Severity == ConfigSeverity.Error).Select(N => (N.Code, N.Key)), Is.EqualTo(new[] { ("WebProxyInvalid", (string?)"trustedProxies") }));
            WebAccessSaveResult Result = WebAccessRule.Save(Session, Confirmed: true);
            Assert.That(Result.Outcome, Is.EqualTo(WebAccessSaveOutcome.NotSaved), "a confirmation never saves what the validation refuses");
            Assert.That(Result.Message, Is.EqualTo("Web.yaml has problems that must be fixed before it can be saved. Nothing was written."));
            Assert.That(Files(), Is.Empty);
        }

        // ---- the one confirmation (D81 point 4) ----

        [Test]
        public void TurningThePasswordOffOnTheNetwork_AsksOnceAtThatSave_AndNeverAgain()
        {
            var Session = OpenWeb();
            Session.Set("passwordProtection", false);
            Assert.That(WebAccessRule.NeedsConfirmation(Session), Is.True);

            WebAccessSaveResult Asked = WebAccessRule.Save(Session, Confirmed: false);
            Assert.That(Asked.Outcome, Is.EqualTo(WebAccessSaveOutcome.NeedsConfirmation));
            Assert.That(Asked.Message, Is.EqualTo(WebAccessRule.Warning));
            Assert.That(Asked.Message, Does.Contain("everyone who can reach this computer on the network can open Campertron's web UI"));
            Assert.That(Files(), Is.Empty, "nothing is written before the confirmation");
            Assert.That((Session.IsDirty, Session.Value("passwordProtection")), Is.EqualTo((true, (object?)false)), "the edit is kept for the confirmation");

            WebAccessSaveResult Saved = WebAccessRule.Save(Session, Confirmed: true);
            Assert.That(Saved.Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved), Saved.Message);
            Assert.That(Yaml.WebConfigGetConfig(Folder)!.PasswordProtection, Is.False, "the confirmed state is what the file holds");

            //the accepted state is saved: a later save of something else, in this session or a new one, asks nothing
            Session.SetItems("allowedHosts", new[] { new ConfigListItem(null, "nas") });
            Assert.That(WebAccessRule.NeedsConfirmation(Session), Is.False);
            Assert.That(WebAccessRule.Save(Session, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved));
            var Later = OpenWeb();
            Later.SetItems("trustedProxies", new[] { new ConfigListItem(null, "172.18.0.2") });
            Assert.That(WebAccessRule.Save(Later, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved), "no repeated warning");
            Assert.That(Later.Notices.Where(N => N.Severity != ConfigSeverity.Info), Is.Empty, "and no lasting notice (D81)");
            Assert.That(ConfigValidation.ValidateFile(Web, Now), Is.Empty);

            //protected again, then opened again: that is a new change, so it asks again, once
            Later.Set("passwordProtection", true);
            Assert.That(WebAccessRule.Save(Later, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved));
            Later.Set("passwordProtection", false);
            Assert.That(WebAccessRule.Save(Later, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.NeedsConfirmation));
        }

        [TestCase(true, true, true, true, false)]
        [TestCase(true, true, true, false, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(true, true, false, true, true)]
        [TestCase(true, false, false, true, true)]
        [TestCase(false, false, false, true, true)]
        [TestCase(false, false, false, false, false)]
        [TestCase(false, true, false, true, false)]
        [TestCase(false, true, true, true, false)]
        [TestCase(false, true, false, false, false)]
        public void OnlyASaveThatMakesTheWebUiOpen_NeedsTheConfirmation(bool WasPassword, bool WasNetwork, bool Password, bool Network, bool Asked)
        {
            var Saved = new WebConfig { PasswordProtection = WasPassword, NetworkListening = WasNetwork };
            var Changed = new WebConfig { PasswordProtection = Password, NetworkListening = Network };
            Assert.That(WebAccessRule.NeedsConfirmation(Saved, Changed), Is.EqualTo(Asked));
            Assert.That(WebAccessRule.IsOpen(Changed), Is.EqualTo(!Password && Network));
        }

        [Test]
        public void WithoutAFile_TheDefaultsAreWhatWasSaved_AndListenOnTheCommandLineDecidesForItsProcess()
        {
            var Open = new WebConfig { PasswordProtection = false };
            Assert.That(WebAccessRule.NeedsConfirmation((WebConfig?)null, Open), Is.True);
            Assert.That(WebAccessRule.NeedsConfirmation((WebConfig?)null, new WebConfig()), Is.False);

            //started with --listen 127.0.0.1: the process is not on the network, whatever the file says
            Assert.That(WebAccessRule.NeedsConfirmation(null, Open, ListensOnNetwork: false), Is.False);
            //started with --listen 0.0.0.0 while the file says this computer only: it is on the network
            var LocalFile = new WebConfig { PasswordProtection = false, NetworkListening = false };
            Assert.That(WebAccessRule.NeedsConfirmation(new WebConfig { NetworkListening = false }, LocalFile, ListensOnNetwork: true), Is.True);
            Assert.That(WebAccessRule.NeedsConfirmation(new WebConfig { NetworkListening = false }, LocalFile), Is.False);

            var Session = OpenWeb();
            Session.Set("passwordProtection", false);
            Session.Set("networkListening", false);
            Assert.That(WebAccessRule.NeedsConfirmation(Session), Is.False, "off the network: no question");
            Assert.That(WebAccessRule.NeedsConfirmation(Session, ListensOnNetwork: true), Is.True);
            Assert.That(WebAccessRule.Save(Session, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved));
        }

        [Test]
        public void TheStateOfThePasswordFileAndTheKeys_NeverKeepsASaveFromGoingThrough()
        {
            //a password file that can't be used, and a keys folder that is a file: both are reported elsewhere, never a
            //condition of a save
            Write(Path.Join(Folder, "Web.password"), "not a record\n");
            Write(Path.Join(Folder, "Web.keys"), "not a folder");
            Assert.That(new WebPasswordFile(Folder).State(), Is.EqualTo(WebPasswordState.Unusable));
            var Session = OpenWeb();
            Session.Set("networkListening", false);
            Assert.That(WebAccessRule.Save(Session, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved));
            Session.Set("passwordProtection", false);
            Session.Set("networkListening", true);
            Assert.That(WebAccessRule.Save(Session, Confirmed: false).Outcome, Is.EqualTo(WebAccessSaveOutcome.NeedsConfirmation));
            Assert.That(WebAccessRule.Save(Session, Confirmed: true).Outcome, Is.EqualTo(WebAccessSaveOutcome.Saved), "after the one confirmation nothing blocks (D81)");
            Assert.That(Text(Path.Join(Folder, "Web.password")), Is.EqualTo("not a record\n"), "a save of Web.yaml touches nothing else");
        }

        // ---- the folders alone ----

        [Test]
        public void GetFolders_FindsTheFoldersOfGetConfig_AndReadsOrCreatesNoFile()
        {
            Func<string> Lookup = Load.LocalApplicationDataLookup;
            string? Container = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), Containers = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS");
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS", null);
            Load.LocalApplicationDataLookup = () => Folder;
            try
            {
                CampertronInternalConfig Found = Load.GetFolders();
                Assert.That((Found.ConfigPath, Found.CachePath, Found.ContainerMode), Is.EqualTo((Path.Join(Folder, "CampertronConfig"), Path.Join(Folder, "CampertronCache"), false)));
                Assert.That((Found.GeneralConfig, Found.EmailConfig, Found.NtfyConfig), Is.EqualTo(((GeneralConfig?)null, (EmailConfig?)null, (NtfyConfig?)null)));
                Assert.That(Directory.GetFileSystemEntries(Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "CampertronConfig", "CampertronCache" }));
                Assert.That(Directory.GetFileSystemEntries(Found.ConfigPath!), Is.Empty, "no General.yaml, no Email.yaml, no database");
                Assert.That(Directory.GetFileSystemEntries(Found.CachePath!), Is.Empty);

                //a settings file that is broken is not read
                Write(Path.Join(Found.ConfigPath, "General.yaml"), "outputTo: [broken\n");
                Assert.That(Load.GetFolders().ConfigPath, Is.EqualTo(Found.ConfigPath));

                Load.LocalApplicationDataLookup = () => string.Empty;
                Assert.That(() => Load.GetFolders(), Throws.InvalidOperationException.With.Message.Contains("does not use the working directory instead"));
            }
            finally
            {
                Load.LocalApplicationDataLookup = Lookup;
                Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", Container);
                Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS", Containers);
            }
        }
    }
}
