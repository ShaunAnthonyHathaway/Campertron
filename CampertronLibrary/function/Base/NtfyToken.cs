namespace CampertronLibrary.function.Base
{
    // Where ntfy's access token comes from (decision D67, business-rules §28), in this order of precedence:
    //   1. File: Ntfy.yaml's tokenFile is written (any value, even none): the token is that file's content (SecretFile, the
    //      same contract as the SMTP password file of D66);
    //   2. Environment: the environment variable CAMPERTRON_NTFY_TOKEN is set (even to nothing): its value;
    //   3. None: no token; ntfy publishes without authentication (a topic without access control).
    // There is no plaintext token in Ntfy.yaml. A source that is configured but can't be used is a typed problem of that
    // source: never a reason to use the next one, and never a reason to publish without the token. The token is read
    // only by the unattended monitoring's start (or a publish that started without it), once per process; no message
    // holds the token, the file's path or the variable's value. A token is printable ASCII without spaces, as it goes
    // into the HTTP header "Authorization: Bearer <token>".
    internal enum NtfyTokenSource
    {
        None,
        File,
        Environment
    }

    internal enum NtfyTokenProblem
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
        FileInvalidCharacters,
        EnvironmentEmpty,
        EnvironmentInvalidCharacters
    }

    // the token of one resolution: its source and either the token (null for None) or the problem. ToString never shows it
    internal sealed class NtfyTokenResult
    {
        private NtfyTokenResult(NtfyTokenSource Source, string? Token, NtfyTokenProblem? Problem)
        {
            this.Source = Source;
            this.Token = Token;
            this.Problem = Problem;
        }
        internal static NtfyTokenResult Found(NtfyTokenSource Source, string? Token) => new NtfyTokenResult(Source, Token, null);
        internal static NtfyTokenResult Failed(NtfyTokenSource Source, NtfyTokenProblem Problem) => new NtfyTokenResult(Source, null, Problem);
        internal NtfyTokenSource Source { get; }
        internal string? Token { get; }
        internal NtfyTokenProblem? Problem { get; }
        internal bool Succeeded => Problem == null;
        public override string ToString() => Succeeded ? $"NtfyToken {Source}: found" : $"NtfyToken {Source}: {Problem}";
    }

    // a configured token source that can't be used, when a publish needs the token; its message is NtfyToken.Describe's
    // first line: fixed text, no value
    internal sealed class NtfyTokenException : Exception
    {
        internal NtfyTokenException(NtfyTokenResult Result) : base(NtfyToken.Describe(Result)[0])
        {
            this.Result = Result;
        }
        internal NtfyTokenResult Result { get; }
    }

    internal static class NtfyToken
    {
        internal const string EnvironmentVariable = "CAMPERTRON_NTFY_TOKEN";
        internal const string FileKey = "tokenFile";

        // the process's environment; replaceable so tests can set and unset the variable without changing the process
        internal static Func<string, string?> GetEnvironment { get; set; } = Environment.GetEnvironmentVariable;

        // which source applies, by precedence, without reading the file or the variable's value
        internal static NtfyTokenSource SelectedSource(NtfyConfig Ntfy)
        {
            if (Ntfy.TokenFileWritten)
            {
                return NtfyTokenSource.File;
            }
            return GetEnvironment(EnvironmentVariable) != null ? NtfyTokenSource.Environment : NtfyTokenSource.None;
        }

        // the token from the source that applies (null for None), or that source's problem. Reads the file when tokenFile
        // is written; never another source then
        internal static NtfyTokenResult Resolve(NtfyConfig Ntfy)
        {
            switch (SelectedSource(Ntfy))
            {
                case NtfyTokenSource.File:
                    return FromFile(SecretFile.Read(Ntfy.TokenFile, out string? Text), Text);
                case NtfyTokenSource.Environment:
                    return FromEnvironment(GetEnvironment(EnvironmentVariable) ?? string.Empty);
                default:
                    return NtfyTokenResult.Found(NtfyTokenSource.None, null);
            }
        }

        // the unattended monitoring's start: resolves once and keeps the result for every publish of this process (the
        // load-once policy). A problem is returned for the start to report; nothing is kept then
        internal static NtfyTokenResult ResolveOnce(NtfyConfig Ntfy)
        {
            if (Ntfy.ResolvedToken is NtfyTokenResult Known && Known.Succeeded)
            {
                return Known;
            }
            NtfyTokenResult Result = Resolve(Ntfy);
            if (Result.Succeeded)
            {
                Ntfy.ResolvedToken = Result;
            }
            return Result;
        }

        // the token a publish sends (null: none), the start's or resolved now (once). Throws NtfyTokenException, naming no
        // value, if the source configured can't be used: never another source and never no token
        internal static string? ForPublishing(NtfyConfig Ntfy)
        {
            NtfyTokenResult Result = ResolveOnce(Ntfy);
            if (!Result.Succeeded)
            {
                throw new NtfyTokenException(Result);
            }
            return Result.Token;
        }

        // the values a failure report hides for ntfy: the resolved token and the topic (which acts as a password where the
        // server has no access control); never read from a file or the environment here
        internal static IReadOnlyList<string> KnownSecrets(NtfyConfig? Ntfy)
        {
            var Secrets = new List<string>();
            if (Ntfy?.ResolvedToken?.Token is string Token && Token.Length > 0)
            {
                Secrets.Add(Token);
            }
            if (!string.IsNullOrEmpty(Ntfy?.Topic) && !Secrets.Contains(Ntfy.Topic))
            {
                Secrets.Add(Ntfy.Topic);
            }
            return Secrets;
        }

        // a token goes into an HTTP header: printable ASCII without spaces (no control character, no line break)
        internal static bool IsValidToken(string Token) => Token.Length > 0 && Token.All(C => C >= '!' && C <= '~');

        private static NtfyTokenResult FromFile(SecretFileProblem? Problem, string? Text)
        {
            if (Problem == null)
            {
                return Text != null && IsValidToken(Text)
                    ? NtfyTokenResult.Found(NtfyTokenSource.File, Text)
                    : NtfyTokenResult.Failed(NtfyTokenSource.File, NtfyTokenProblem.FileInvalidCharacters);
            }
            NtfyTokenProblem Mapped = Problem.Value switch
            {
                SecretFileProblem.PathEmpty => NtfyTokenProblem.FilePathEmpty,
                SecretFileProblem.PathNotAbsolute => NtfyTokenProblem.FilePathNotAbsolute,
                SecretFileProblem.Missing => NtfyTokenProblem.FileMissing,
                SecretFileProblem.IsFolder => NtfyTokenProblem.FileIsFolder,
                SecretFileProblem.NotReadable => NtfyTokenProblem.FileNotReadable,
                SecretFileProblem.Empty => NtfyTokenProblem.FileEmpty,
                SecretFileProblem.TooLarge => NtfyTokenProblem.FileTooLarge,
                SecretFileProblem.HasByteOrderMark => NtfyTokenProblem.FileHasByteOrderMark,
                SecretFileProblem.NotUtf8 => NtfyTokenProblem.FileNotUtf8,
                SecretFileProblem.HasNul => NtfyTokenProblem.FileHasNul,
                SecretFileProblem.OnlyLineBreak => NtfyTokenProblem.FileOnlyLineBreak,
                _ => NtfyTokenProblem.FileHasLineBreak
            };
            return NtfyTokenResult.Failed(NtfyTokenSource.File, Mapped);
        }

        // CAMPERTRON_NTFY_TOKEN as it is set: nothing is trimmed; set to nothing, or holding a character a header can't
        // carry (a space, a line break, a control or non-ASCII character), it is a problem
        internal static NtfyTokenResult FromEnvironment(string Value)
        {
            if (Value.Length == 0)
            {
                return NtfyTokenResult.Failed(NtfyTokenSource.Environment, NtfyTokenProblem.EnvironmentEmpty);
            }
            return IsValidToken(Value)
                ? NtfyTokenResult.Found(NtfyTokenSource.Environment, Value)
                : NtfyTokenResult.Failed(NtfyTokenSource.Environment, NtfyTokenProblem.EnvironmentInvalidCharacters);
        }

        // ---- the words ----

        private const string Order = "Campertron never publishes without the token, or with another token source, when the one configured can't be used " +
            "(the order is tokenFile, then " + EnvironmentVariable + ", then none).";

        // what to tell the operator: the problem (written after "Error: "), what to do, and the precedence. Key and
        // variable names only: never the token, the file's path, the variable's value or the topic
        internal static List<string> Describe(NtfyTokenResult Result)
        {
            const string TheFile = "the file that tokenFile in Ntfy.yaml names";
            const string SaveText = "Save the token alone in the file, as UTF-8 text without a byte-order mark (one line break at its end is allowed).";
            string Problem;
            string Fix;
            switch (Result.Problem)
            {
                case NtfyTokenProblem.FilePathEmpty:
                    Problem = "tokenFile in Ntfy.yaml is empty.";
                    Fix = "Write the absolute path of the file that holds the ntfy access token (for example /run/secrets/ntfy_token), or remove tokenFile.";
                    break;
                case NtfyTokenProblem.FilePathNotAbsolute:
                    Problem = "tokenFile in Ntfy.yaml is not an absolute path.";
                    Fix = "Write the file's full path (for example /run/secrets/ntfy_token): Campertron never reads it relative to another folder.";
                    break;
                case NtfyTokenProblem.FileMissing:
                    Problem = TheFile + " does not exist, or a folder on its path can't be read.";
                    Fix = "Create the file, or mount it into the container (a read-only bind mount or a Compose secret), or correct tokenFile.";
                    break;
                case NtfyTokenProblem.FileIsFolder:
                    Problem = TheFile + " is a folder, not a file.";
                    Fix = "Name the token file itself in tokenFile.";
                    break;
                case NtfyTokenProblem.FileNotReadable:
                    Problem = TheFile + " can't be read (its permissions do not allow it, or another program holds it).";
                    Fix = "Let the user that runs Campertron read it (in the container image, the user app, uid 1654), and preferably no one else.";
                    break;
                case NtfyTokenProblem.FileEmpty:
                    Problem = TheFile + " is empty, or is not a regular file.";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.FileTooLarge:
                    Problem = TheFile + " is larger than " + SecretFile.MaxFileBytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes, too large for a token file.";
                    Fix = "Check that tokenFile names the token file. " + SaveText;
                    break;
                case NtfyTokenProblem.FileHasByteOrderMark:
                    Problem = TheFile + " starts with a byte-order mark.";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.FileNotUtf8:
                    Problem = TheFile + " is not UTF-8 text.";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.FileHasNul:
                    Problem = TheFile + " holds a NUL character (it may be UTF-16 text).";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.FileOnlyLineBreak:
                    Problem = TheFile + " holds only a line break, no token.";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.FileHasLineBreak:
                    Problem = TheFile + " holds more than one line: the whole file is the token, and only one line break at its end is removed.";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.FileInvalidCharacters:
                    Problem = TheFile + " holds a space or a character that is not printable ASCII, which an access token never has.";
                    Fix = SaveText;
                    break;
                case NtfyTokenProblem.EnvironmentEmpty:
                    Problem = EnvironmentVariable + " is set but empty.";
                    Fix = "Set it to the ntfy access token, or remove it to publish without a token.";
                    break;
                case NtfyTokenProblem.EnvironmentInvalidCharacters:
                    Problem = EnvironmentVariable + " holds a space, a line break or a character that is not printable ASCII, which an access token never has.";
                    Fix = "Set it to the token alone (nothing in it is trimmed).";
                    break;
                default:
                    Problem = "the ntfy access token is available.";
                    Fix = string.Empty;
                    break;
            }
            return new List<string> { "the ntfy access token can't be used: " + Problem, Fix, Order };
        }
    }
}
