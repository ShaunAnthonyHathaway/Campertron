using System.Text;

namespace CampertronLibrary.function.Base
{
    // Where the SMTP password comes from (decision D66, business-rules §27). One rule for every caller (email mode's start,
    // the send, the failure report, the validation and the GUI's notices), in this order of precedence:
    //   1. File: Email.yaml's smtpPasswordFile is written (any value, even none): the password is the content of that file;
    //   2. Environment: the environment variable CAMPERTRON_EMAIL_PASSWORD is set (even to nothing): its value;
    //   3. EmailYaml: Email.yaml's smtpPassword, as before (it may be empty: a relay may accept mail without signing in).
    // A source that is configured but can't be used is a typed problem, never a reason to use the next one: an operator who
    // set smtpPasswordFile or CAMPERTRON_EMAIL_PASSWORD gets a clear refusal instead of a sign-in with another password.
    // Nothing here writes, copies, deletes, lists or changes the permissions of anything; the file is opened only to read it,
    // only when the password is needed (email mode's start, or a send that started without it), and at most once per
    // process for the start's EmailConfig (email mode's load-once policy: a changed file needs a restart). No message holds
    // the password, the file's path, the variable's value or any value of Email.yaml: fixed text and key names only.
    internal enum SmtpPasswordSource
    {
        File,
        Environment,
        EmailYaml
    }

    // why the password source that is configured can't be used (business-rules §27 documents each)
    internal enum SmtpPasswordProblem
    {
        FilePathEmpty,
        FilePathNotAbsolute,
        FileMissing,
        FileIsFolder,
        FileNotReadable,
        FileEmpty,
        FileTooLarge,
        FileHasByteOrderMark,
        FileNotUtf8,
        FileHasNul,
        FileOnlyLineBreak,
        FileHasLineBreak,
        EnvironmentEmpty,
        EnvironmentHasLineBreak
    }

    // The password of one resolution: its source and either the password or the problem. ToString never shows the password
    internal sealed class SmtpPasswordResult
    {
        private SmtpPasswordResult(SmtpPasswordSource Source, string? Password, SmtpPasswordProblem? Problem)
        {
            this.Source = Source;
            this.Password = Password;
            this.Problem = Problem;
        }
        internal static SmtpPasswordResult Found(SmtpPasswordSource Source, string? Password) => new SmtpPasswordResult(Source, Password, null);
        internal static SmtpPasswordResult Failed(SmtpPasswordSource Source, SmtpPasswordProblem Problem) => new SmtpPasswordResult(Source, null, Problem);
        internal SmtpPasswordSource Source { get; }
        // the password to sign in with; null for a problem, and null or empty when Email.yaml's smtpPassword is (as before)
        internal string? Password { get; }
        internal SmtpPasswordProblem? Problem { get; }
        internal bool Succeeded => Problem == null;
        public override string ToString() => Succeeded ? $"SmtpPassword {Source}: found" : $"SmtpPassword {Source}: {Problem}";
    }

    // a configured password source that can't be used, when a send needs the password (SmtpPassword.ForSending). Its message
    // is SmtpPassword.Describe's first line: fixed text, no value
    internal sealed class SmtpPasswordException : Exception
    {
        internal SmtpPasswordException(SmtpPasswordResult Result) : base(SmtpPassword.Describe(Result)[0])
        {
            this.Result = Result;
        }
        internal SmtpPasswordResult Result { get; }
    }

    internal static class SmtpPassword
    {
        internal const string EnvironmentVariable = "CAMPERTRON_EMAIL_PASSWORD";
        internal const string FileKey = "smtpPasswordFile";
        internal const string EmailYamlKey = "smtpPassword";
        // a password file is a few bytes; anything larger is the wrong file (and is never read whole): SecretFile's limit
        internal const int MaxFileBytes = SecretFile.MaxFileBytes;

        // the process's environment; replaceable so tests can set and unset the variable without changing the process
        internal static Func<string, string?> GetEnvironment { get; set; } = Environment.GetEnvironmentVariable;

        // which source applies, by precedence, without reading the file or the variable's value
        internal static SmtpPasswordSource SelectedSource(EmailConfig Email)
        {
            if (Email.SmtpPasswordFileWritten)
            {
                return SmtpPasswordSource.File;
            }
            return GetEnvironment(EnvironmentVariable) != null ? SmtpPasswordSource.Environment : SmtpPasswordSource.EmailYaml;
        }

        // D82 point 4 (the web UI, v0.3.0's PR 5): whether CAMPERTRON_EMAIL_PASSWORD is set in this process, even to nothing
        // (SelectedSource's rule), as yes or no only: the value is compared with nothing and never kept or returned
        internal static bool VariableIsSet() => GetEnvironment(EnvironmentVariable) is not null;

        // the password from the source that applies, or that source's problem. Reads the file when smtpPasswordFile is
        // written; never another source then
        internal static SmtpPasswordResult Resolve(EmailConfig Email)
        {
            switch (SelectedSource(Email))
            {
                case SmtpPasswordSource.File:
                    return ReadFile(Email.SmtpPasswordFile);
                case SmtpPasswordSource.Environment:
                    return FromEnvironment(GetEnvironment(EnvironmentVariable) ?? string.Empty);
                default:
                    return SmtpPasswordResult.Found(SmtpPasswordSource.EmailYaml, Email.SmtpPassword);
            }
        }

        // email mode's start: resolves once and keeps the result on the EmailConfig for every send of this process (the
        // load-once policy). A problem is returned for the start to report; nothing is kept then
        internal static SmtpPasswordResult ResolveOnce(EmailConfig Email)
        {
            if (Email.ResolvedPassword is SmtpPasswordResult Known && Known.Succeeded)
            {
                return Known;
            }
            SmtpPasswordResult Result = Resolve(Email);
            if (Result.Succeeded)
            {
                Email.ResolvedPassword = Result;
            }
            return Result;
        }

        // the password a send signs in with: the start's, or resolved now (once) if the send began without one. Throws
        // SmtpPasswordException, naming no value, if the source configured can't be used: never another source
        internal static string? ForSending(EmailConfig Email)
        {
            SmtpPasswordResult Result = ResolveOnce(Email);
            if (!Result.Succeeded)
            {
                throw new SmtpPasswordException(Result);
            }
            return Result.Password;
        }

        // every password value this process knows for Email, to remove from a failure message: the resolved one and the
        // plaintext smtpPassword (never read from a file or the environment here)
        internal static IReadOnlyList<string> KnownSecrets(EmailConfig? Email)
        {
            var Secrets = new List<string>();
            if (Email?.ResolvedPassword?.Password is string Resolved && Resolved.Length > 0)
            {
                Secrets.Add(Resolved);
            }
            if (!string.IsNullOrEmpty(Email?.SmtpPassword) && !Secrets.Contains(Email.SmtpPassword))
            {
                Secrets.Add(Email.SmtpPassword);
            }
            return Secrets;
        }

        // ---- the file ----

        // The file smtpPasswordFile names, read by the shared secret-file reader (SecretFile, the contract of business-rules
        // §27, also used for ntfy's token file, D67): an absolute path, followed through links to a regular file of 1 to
        // 4,096 bytes, strict UTF-8, one line break at its end removed and nothing trimmed
        internal static SmtpPasswordResult ReadFile(string? FilePath) => FromSecretFile(SecretFile.Read(FilePath, out string? Text), Text);

        // The password from the file's bytes (SecretFile.FromBytes): its whole text, with at most one line break at its end removed
        internal static SmtpPasswordResult FromFileBytes(ReadOnlySpan<byte> Bytes) => FromSecretFile(SecretFile.FromBytes(Bytes, out string? Text), Text);

        private static SmtpPasswordResult FromSecretFile(SecretFileProblem? Problem, string? Text)
        {
            if (Problem == null)
            {
                return SmtpPasswordResult.Found(SmtpPasswordSource.File, Text);
            }
            SmtpPasswordProblem Mapped = Problem.Value switch
            {
                SecretFileProblem.PathEmpty => SmtpPasswordProblem.FilePathEmpty,
                SecretFileProblem.PathNotAbsolute => SmtpPasswordProblem.FilePathNotAbsolute,
                SecretFileProblem.Missing => SmtpPasswordProblem.FileMissing,
                SecretFileProblem.IsFolder => SmtpPasswordProblem.FileIsFolder,
                SecretFileProblem.NotReadable => SmtpPasswordProblem.FileNotReadable,
                SecretFileProblem.Empty => SmtpPasswordProblem.FileEmpty,
                SecretFileProblem.TooLarge => SmtpPasswordProblem.FileTooLarge,
                SecretFileProblem.HasByteOrderMark => SmtpPasswordProblem.FileHasByteOrderMark,
                SecretFileProblem.NotUtf8 => SmtpPasswordProblem.FileNotUtf8,
                SecretFileProblem.HasNul => SmtpPasswordProblem.FileHasNul,
                SecretFileProblem.OnlyLineBreak => SmtpPasswordProblem.FileOnlyLineBreak,
                _ => SmtpPasswordProblem.FileHasLineBreak
            };
            return SmtpPasswordResult.Failed(SmtpPasswordSource.File, Mapped);
        }

        // ---- the environment ----

        // CAMPERTRON_EMAIL_PASSWORD as it is set: nothing is trimmed. Set to nothing, or holding a line break, it is a problem
        internal static SmtpPasswordResult FromEnvironment(string Value)
        {
            if (Value.Length == 0)
            {
                return SmtpPasswordResult.Failed(SmtpPasswordSource.Environment, SmtpPasswordProblem.EnvironmentEmpty);
            }
            if (Value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                return SmtpPasswordResult.Failed(SmtpPasswordSource.Environment, SmtpPasswordProblem.EnvironmentHasLineBreak);
            }
            return SmtpPasswordResult.Found(SmtpPasswordSource.Environment, Value);
        }

        // ---- the words ----

        private const string Order = "Campertron never uses another password source when the one configured can't be used " +
            "(the order is smtpPasswordFile, then " + EnvironmentVariable + ", then smtpPassword).";

        // what to tell the operator about a problem: the problem (written after "Error: "), what to do, and the precedence.
        // Key and variable names only: never the password, the file's path, the variable's value or a value of Email.yaml
        internal static List<string> Describe(SmtpPasswordResult Result)
        {
            string Problem;
            string Fix;
            const string TheFile = "the file that smtpPasswordFile in Email.yaml names";
            const string SaveText = "Save the password alone in the file, as UTF-8 text without a byte-order mark (one line break at its end is allowed).";
            switch (Result.Problem)
            {
                case SmtpPasswordProblem.FilePathEmpty:
                    Problem = "smtpPasswordFile in Email.yaml is empty.";
                    Fix = "Write the absolute path of the file that holds the SMTP password (for example /run/secrets/smtp_password), or remove smtpPasswordFile.";
                    break;
                case SmtpPasswordProblem.FilePathNotAbsolute:
                    Problem = "smtpPasswordFile in Email.yaml is not an absolute path.";
                    Fix = "Write the file's full path (for example /run/secrets/smtp_password, or C:\\Secrets\\smtp_password.txt on Windows): Campertron never reads it relative to another folder.";
                    break;
                case SmtpPasswordProblem.FileMissing:
                    Problem = TheFile + " does not exist, or a folder on its path can't be read.";
                    Fix = "Create the file, or mount it into the container (a read-only bind mount or a Compose secret), or correct smtpPasswordFile.";
                    break;
                case SmtpPasswordProblem.FileIsFolder:
                    Problem = TheFile + " is a folder, not a file.";
                    Fix = "Name the password file itself in smtpPasswordFile.";
                    break;
                case SmtpPasswordProblem.FileNotReadable:
                    Problem = TheFile + " can't be read (its permissions do not allow it, or another program holds it).";
                    Fix = "Let the user that runs Campertron read it (in the container image, the user app, uid 1654), and preferably no one else.";
                    break;
                case SmtpPasswordProblem.FileEmpty:
                    Problem = TheFile + " is empty, or is not a regular file.";
                    Fix = SaveText;
                    break;
                case SmtpPasswordProblem.FileTooLarge:
                    Problem = TheFile + " is larger than " + MaxFileBytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes, too large for a password file.";
                    Fix = "Check that smtpPasswordFile names the password file. " + SaveText;
                    break;
                case SmtpPasswordProblem.FileHasByteOrderMark:
                    Problem = TheFile + " starts with a byte-order mark.";
                    Fix = SaveText;
                    break;
                case SmtpPasswordProblem.FileNotUtf8:
                    Problem = TheFile + " is not UTF-8 text.";
                    Fix = SaveText;
                    break;
                case SmtpPasswordProblem.FileHasNul:
                    Problem = TheFile + " holds a NUL character (it may be UTF-16 text).";
                    Fix = SaveText;
                    break;
                case SmtpPasswordProblem.FileOnlyLineBreak:
                    Problem = TheFile + " holds only a line break, no password.";
                    Fix = SaveText;
                    break;
                case SmtpPasswordProblem.FileHasLineBreak:
                    Problem = TheFile + " holds more than one line: the whole file is the password, and only one line break at its end is removed.";
                    Fix = SaveText;
                    break;
                case SmtpPasswordProblem.EnvironmentEmpty:
                    Problem = EnvironmentVariable + " is set but empty.";
                    Fix = "Set it to the SMTP password, or remove it to use smtpPassword in Email.yaml.";
                    break;
                case SmtpPasswordProblem.EnvironmentHasLineBreak:
                    Problem = EnvironmentVariable + " holds a line break.";
                    Fix = "Set it to the password alone, without a line break (nothing in it is trimmed).";
                    break;
                default:
                    Problem = "the SMTP password is available.";
                    Fix = string.Empty;
                    break;
            }
            return new List<string> { "the SMTP password can't be used: " + Problem, Fix, Order };
        }

        // the last line of a failed send's report: where to check the password, by the source it came from
        internal static string SendFailureHint(EmailConfig? Email)
        {
            switch (Email?.ResolvedPassword?.Source)
            {
                case SmtpPasswordSource.File:
                    return "Check the server, port and user name in Email.yaml, and the password in the file that smtpPasswordFile names.";
                case SmtpPasswordSource.Environment:
                    return "Check the server, port and user name in Email.yaml, and the password in " + EnvironmentVariable + ".";
                default:
                    return "Check the server, port, user name and password in Email.yaml.";
            }
        }
    }
}
