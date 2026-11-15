using CampertronLibrary.function.Base;
using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;

namespace Campertron
{
    // The settings editor's part of `CampertronGUI --smoke-test` (CM5, decision D43): on the platform the release runs on
    // (natively on Windows and macOS in the release dry runs), it edits and saves General.yaml and Email.yaml through the
    // editing session in a new temporary folder (never the settings folder): the writer's temporary file, rename, backup,
    // read-back and permissions, a byte-order mark, CRLF line breaks and comments kept, a created Email.yaml owner-only,
    // and a random password that is never printed. The result line names only what was checked.
    internal static class SettingsProbe
    {
        private const string GeneralText = "# Campertron settings probe\r\noutputTo: Console   # where results go\r\n\r\nrefreshRidbDataDayInterval: 30\r\n" +
            "lastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z # UTC\r\nautoRefresh: false # off\r\n";
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };
        private static readonly UnixFileMode GroupReadable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

        internal static (bool Passed, string Detail) Run()
        {
            string Folder = Path.Join(Path.GetTempPath(), "campertron-settings-probe-" + Guid.NewGuid().ToString("N"));
            string Secret = "Probe-" + Guid.NewGuid().ToString("N");
            string Step = "creating the probe folder";
            try
            {
                Directory.CreateDirectory(Folder);
                string General = Path.Join(Folder, ConfigFiles.GeneralFileName);
                string Email = Path.Join(Folder, ConfigFiles.EmailFileName);

                Step = "General.yaml";
                byte[] GeneralBytes = Bom.Concat(Encoding.UTF8.GetBytes(GeneralText)).ToArray();
                File.WriteAllBytes(General, GeneralBytes);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(General, GroupReadable);
                }
                var G = new ConfigEditSession<GeneralConfig>(Folder);
                G.Set("autoRefresh", true);
                G.Set("refreshRidbDataDayInterval", 14d);
                if (!G.Save().Succeeded)
                {
                    return Fail("General.yaml could not be saved");
                }
                byte[] Expected = Bom.Concat(Encoding.UTF8.GetBytes(GeneralText.Replace("Interval: 30", "Interval: 14").Replace("autoRefresh: false", "autoRefresh: true"))).ToArray();
                if (!File.ReadAllBytes(General).AsSpan().SequenceEqual(Expected))
                {
                    return Fail("General.yaml was not saved exactly (byte-order mark, CRLF, comments and the two values)");
                }
                if (!File.ReadAllBytes(General + ".bak").AsSpan().SequenceEqual(GeneralBytes))
                {
                    return Fail("General.yaml.bak is not the exact previous file");
                }
                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(General) != GroupReadable || File.GetUnixFileMode(General + ".bak") != GroupReadable))
                {
                    return Fail("General.yaml or its backup did not keep the file's permissions");
                }
                if (OperatingSystem.IsWindows() && AccessRules(General) != AccessRules(General + ".bak"))
                {
                    return Fail("General.yaml's backup has other access rules than the file");
                }

                Step = "Email.yaml";
                var E = new ConfigEditSession<EmailConfig>(Folder);
                if (E.State != ConfigEditState.Missing || !E.Create().Succeeded)
                {
                    return Fail("Email.yaml could not be created");
                }
                byte[] Sample = File.ReadAllBytes(Email);
                if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(Email) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
                {
                    return Fail("the created Email.yaml is not owner-only (0600)");
                }
                E.Set("smtpServer", "smtp.example.org");
                E.Set("smtpPort", 587);
                E.Set("smtpUsername", "campertron-probe");
                E.Set("smtpPassword", Secret);
                E.Set("sendFromAddress", "probe@example.org");
                E.SetItems("sendToAddressList", new[] { new ConfigListItem(null, "first@example.org"), new ConfigListItem(null, "second@example.org") });
                if (E.Notices.Any(N => N.Message.Contains(Secret, StringComparison.Ordinal)) || E.Preview.Any(L => L.Text.Contains(Secret, StringComparison.Ordinal)))
                {
                    return Fail("the password reached a message or the preview");
                }
                ConfigEditResult Saved = E.Save();
                if (!Saved.Succeeded || Saved.Message.Contains(Secret, StringComparison.Ordinal))
                {
                    return Fail("Email.yaml could not be saved");
                }
                string Written = Encoding.UTF8.GetString(File.ReadAllBytes(Email));
                if (Yaml.LoadSettingsText<EmailConfig>(Written).SmtpPassword != Secret || Written.Split(Secret).Length != 2)
                {
                    return Fail("Email.yaml does not hold the new password exactly once");
                }
                if (!File.ReadAllBytes(Email + ".bak").AsSpan().SequenceEqual(Sample))
                {
                    return Fail("Email.yaml.bak is not the exact previous file");
                }
                if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(Email) != (UnixFileMode.UserRead | UnixFileMode.UserWrite)
                    || File.GetUnixFileMode(Email + ".bak") != (UnixFileMode.UserRead | UnixFileMode.UserWrite)))
                {
                    return Fail("Email.yaml or its backup is not owner-only");
                }
                if (OperatingSystem.IsWindows() && AccessRules(Email) != AccessRules(Email + ".bak"))
                {
                    return Fail("Email.yaml's backup has other access rules than the file");
                }
                if (Directory.GetFiles(Folder).Any(F => F.EndsWith(ConfigFileReplace.TemporarySuffix, StringComparison.Ordinal)))
                {
                    return Fail("a temporary file was left");
                }
                return (true, "pass general=saved email=created,saved backups=exact line-endings=crlf bom=kept comments=kept permissions="
                    + (OperatingSystem.IsWindows() ? "access-rules-kept" : "640,600"));
            }
            catch (Exception ex)
            {
                // the type only: a message could name a path; no message here can hold the password
                return Fail(Step + " failed with " + ex.GetType().Name);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(Folder))
                    {
                        Directory.Delete(Folder, true);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // a leftover temporary folder is harmless; the result above stands
                }
            }
        }

        private static (bool, string) Fail(string Why) => (false, "fail " + Why);

        private static string? AccessRules(string Path_) =>
            OperatingSystem.IsWindows()
                ? new FileInfo(Path_).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                : null;
    }
}
