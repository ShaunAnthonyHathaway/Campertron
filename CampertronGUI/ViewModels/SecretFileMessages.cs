using CampertronLibrary.function.Base;
using System.IO;

namespace Campertron.ViewModels
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
    }
}
