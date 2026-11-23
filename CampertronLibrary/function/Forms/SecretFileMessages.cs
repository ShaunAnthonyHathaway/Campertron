using CampertronLibrary.function.Base;
using System.Collections.Generic;
using System.IO;

namespace CampertronLibrary.function.Forms
{
    // D75: the secret file a field names (smtpPasswordFile, tokenFile), judged from its metadata only
    // (SecretFileCheck.Inspect: whether it exists, is a file of 1 to 4,096 bytes and can be read by the user, and on Linux
    // and macOS whether others can read it). The file is never opened and no environment variable is read. A problem the
    // monitoring's start would refuse is an error while that provider is in use, else a warning; a file others can read is
    // a warning. Neither blocks Save (the maintainer's decision of 2026-11-22: the file may exist only where Campertron
    // runs, such as a Docker secret in a container). A field with its own error (a path that is not fully qualified) gets
    // no file message. Shown with the field's own message, after every refresh: a reload, a change and the window's
    // activation look at the file again
    internal static class SecretFileMessages
    {
        internal static void Show(TextSettingField Field, string? FilePath, string Kind, bool InUse)
        {
            if (Field.IsError || Field.InputError != null || string.IsNullOrWhiteSpace(FilePath) || !Path.IsPathFullyQualified(FilePath))
            {
                return;
            }
            SecretFileStatus Status = SecretFileCheck.Inspect(FilePath);
            if (SecretFileCheck.Describe(Field.Key, FilePath, Status, Kind, InUse) is not (string Text, bool IsProblem))
            {
                return;
            }
            string Own = Field.Message is string Message ? Message + " " : string.Empty;
            Field.SetMessage(Own + Text, IsProblem && InUse ? "Error" : "Warning");
        }

        // The same judgment for General.yaml's outputTo (the fix after v0.2.0 step 3's local test, §BU): every secret file the
        // providers of this outputTo would read, as Email.yaml and Ntfy.yaml are saved (ConfigValidation.MonitoringSecretFiles),
        // in use, so a problem is an error and a file others can read a warning; each said after the settings file that names
        // it ("Email.yaml: smtpPasswordFile: …"). Never blocks Save; the files are never opened
        internal static List<(string Text, bool IsProblem)> ForMonitoring(string Folder, OutputType? Output)
        {
            var Out = new List<(string Text, bool IsProblem)>();
            foreach (ConfigValidation.MonitoringSecretFile Secret in ConfigValidation.MonitoringSecretFiles(Folder, Output, System.DateTime.Now))
            {
                if (SecretFileCheck.Describe(Secret.Key, Secret.FilePath, SecretFileCheck.Inspect(Secret.FilePath), Secret.Kind, true) is (string Text, bool IsProblem))
                {
                    Out.Add((Secret.SettingsFile + ": " + Text, IsProblem));
                }
            }
            return Out;
        }

        // ntfy's token file for the General form's ntfy line: its message while ntfy is on (as Ntfy.yaml is saved), an error
        // while ntfy is in use, else a warning; null when there is nothing to say
        internal static (string Text, string Severity)? ForNtfyLine(string Folder, bool InUse)
        {
            foreach (ConfigValidation.MonitoringSecretFile Secret in ConfigValidation.MonitoringSecretFiles(Folder, OutputType.Ntfy, System.DateTime.Now))
            {
                if (SecretFileCheck.Describe(Secret.Key, Secret.FilePath, SecretFileCheck.Inspect(Secret.FilePath), Secret.Kind, InUse) is (string Text, bool IsProblem))
                {
                    return (Text, IsProblem && InUse ? "Error" : "Warning");
                }
            }
            return null;
        }
    }
}
