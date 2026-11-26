using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Campertron
{
    // The automatic restart after a refresh of the campground data (V011-1, decision D62; business-rules-gui.md §26): the
    // old process hands the new one a small, one-time description of where its window was and what happened. Nothing here
    // is a preference: the handoff exists for seconds, only after a successful refresh, and an ordinary start never reads it.
    // Since v0.4.0's phase 4 (D115, D118) it also says whether the refresh paused a running monitoring, which the new process
    // then starts again (schema version 2); nothing else ever starts the monitoring of a new process.

    // the tab shown at the top level and in Settings' own row (recorded; the restart opens Settings > System). Monitoring
    // since v0.4.0's phase 4 (last, so the earlier names keep their numbers)
    internal enum RestartTopTab { Searches, Results, Settings, Campgrounds, Monitoring }
    internal enum RestartSettingsTab { Campertron, System }

    // what the refresh that caused the restart ended with: the new process words its one-time status by it
    internal enum RestartOutcome { Refreshed, RefreshedWithoutTimestamp }

    // The window's normal (restored) bounds, as Avalonia reports them: the position in device pixels (Window.Position) and
    // the client size in device-independent pixels; Maximized when the window was maximized (the bounds are then the ones
    // it had before). Never minimized or full screen: those restore as a normal window.
    internal readonly record struct RestartWindow(int X, int Y, double Width, double Height, bool Maximized);

    // Monitoring: the old process's refresh paused a running monitoring (v0.4.0's phase 4), which the new process starts again
    internal sealed record RestartHandoff(DateTime CreatedUtc, RestartOutcome Outcome, RestartWindow? Window, RestartTopTab Tab, RestartSettingsTab SettingsTab,
        bool Monitoring = false);

    // The one-time token that names a handoff: 32 random bytes from the system's cryptographic generator, as 64 lowercase
    // hexadecimal digits. It reaches the new process only through one environment variable, which that process removes at
    // once; it is never written to disk (the handoff's file name is its SHA-256, the binding) and never logged.
    internal static class RestartToken
    {
        internal const string EnvironmentVariable = "CAMPERTRON_RESTART_HANDOFF";

        internal static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        internal static bool IsWellFormed(string? Token) =>
            Token != null && Token.Length == 64 && Token.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        // the token's binding: what the handoff is filed under and records, so the file alone never reveals the token
        internal static string Binding(string Token) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(Token))).ToLowerInvariant();

        // The token this process was started with, or null; the variable is removed either way, so nothing this process
        // starts later (a file manager, a browser) inherits it.
        internal static string? TakeFromEnvironment()
        {
            string? Value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (Value != null)
            {
                Environment.SetEnvironmentVariable(EnvironmentVariable, null);
            }
            return Value;
        }
    }

    // why the new process did or didn't use a handoff (tests and diagnostics; never shown with a path or token)
    internal enum RestartClaimStatus { Claimed, NoToken, BadToken, NoStore, Missing, Unreadable, TooLarge, Malformed, UnsupportedVersion, WrongBinding, Stale }

    internal sealed record RestartClaim(RestartClaimStatus Status, RestartHandoff? Handoff)
    {
        internal bool Succeeded => Status == RestartClaimStatus.Claimed && Handoff != null;
    }

    // The handoff files: `LocalApplicationData/CampertronGUI/restart/restart-<binding>.json` (the GUI's own folder, beside
    // its theme preference; never CampertronConfig or a settings file). The folder is created owner-only (0700 on Unix; on
    // Windows it inherits the per-user LocalApplicationData's rules) and refused if it is a link or others can write to it.
    // A handoff is created new (owner-only, 0600 on Unix), claimed by an atomic rename to a name of the claimer's own, so
    // exactly one of the new process and the old one (when it gives up) gets it, read within a small limit, deleted, then
    // validated: the schema version, the binding, the age and every value. Anything else is no handoff.
    internal sealed class RestartHandoffStore
    {
        internal const string FolderName = "restart";
        internal const string FilePrefix = "restart-";
        internal const string FileSuffix = ".json";
        // 2 since v0.4.0's phase 4 (the monitoring); a handoff of another version is no handoff
        internal const int SchemaVersion = 2;
        // a handoff is about 320 bytes; anything larger is not one
        internal const int MaxBytes = 4096;
        // the new process claims it within seconds; an older one is left over from a failure
        internal static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
        // clocks of one machine agree; this only absorbs a correction of the clock between the two processes
        internal static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(30);
        // the bounds a stored value must have before the window placement looks at it at all
        internal const int MaxCoordinate = 1_000_000;
        internal const double MaxSize = 100_000;

        public RestartHandoffStore(string? Folder)
        {
            this.Folder = Folder != null && Path.IsPathFullyQualified(Folder) ? Folder : null;
        }

        // null when there is no usable per-user folder (never the working directory)
        public string? Folder { get; }

        public static RestartHandoffStore ForCurrentUser()
        {
            string? Gui = ThemePreferenceStore.ForCurrentUser().Folder;
            return new RestartHandoffStore(Gui == null ? null : Path.Join(Gui, FolderName));
        }

        internal string? PathFor(string Token) =>
            Folder == null || !RestartToken.IsWellFormed(Token) ? null : Path.Join(Folder, FilePrefix + RestartToken.Binding(Token) + FileSuffix);

        // Writes a new handoff for the token and returns its path; throws (IOException, UnauthorizedAccessException,
        // InvalidOperationException) when it can't, leaving no file of its own behind.
        internal string Create(string Token, RestartHandoff Handoff)
        {
            string Path_ = PathFor(Token) ?? throw new InvalidOperationException("No restart handoff folder or no valid token.");
            EnsureFolder();
            byte[] Bytes = Serialize(Handoff, RestartToken.Binding(Token));
            var Options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                Options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            try
            {
                using var Stream = new FileStream(Path_, Options);
                Stream.Write(Bytes, 0, Bytes.Length);
                Stream.Flush(true);
            }
            catch
            {
                TryDelete(Path_);
                throw;
            }
            return Path_;
        }

        // whether the handoff still waits to be claimed (the old process's acknowledgment: the new one has claimed it)
        internal bool IsPending(string HandoffPath) => File.Exists(HandoffPath);

        // The old process gives up on a handoff: claims it like the new process would and deletes it. True when it got it
        // (so the new process can't any more); false when the new process had claimed it first.
        internal bool TryInvalidate(string HandoffPath)
        {
            string Claimed = HandoffPath + ".invalid-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Move(HandoffPath, Claimed, overwrite: true);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // it can't be claimed by anyone then; deleting it is the best that remains
                TryDelete(HandoffPath);
                return true;
            }
            TryDelete(Claimed);
            return true;
        }

        // The new process claims the handoff its token names: renamed to a name of its own (atomic: only one claimer gets
        // it), read, deleted, then validated. Whatever happens, no handoff of that token remains afterwards.
        internal RestartClaim Claim(string? Token, DateTime NowUtc)
        {
            if (Token == null)
            {
                return new RestartClaim(RestartClaimStatus.NoToken, null);
            }
            if (!RestartToken.IsWellFormed(Token))
            {
                return new RestartClaim(RestartClaimStatus.BadToken, null);
            }
            string? HandoffPath = PathFor(Token);
            if (HandoffPath == null)
            {
                return new RestartClaim(RestartClaimStatus.NoStore, null);
            }
            string Claimed = HandoffPath + ".claimed-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Move(HandoffPath, Claimed, overwrite: true);
            }
            catch (FileNotFoundException)
            {
                return new RestartClaim(RestartClaimStatus.Missing, null);
            }
            catch (DirectoryNotFoundException)
            {
                return new RestartClaim(RestartClaimStatus.Missing, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new RestartClaim(RestartClaimStatus.Unreadable, null);
            }
            try
            {
                byte[]? Bytes = ReadBounded(Claimed, out RestartClaimStatus ReadStatus);
                if (Bytes == null)
                {
                    return new RestartClaim(ReadStatus, null);
                }
                return Parse(Bytes, RestartToken.Binding(Token), NowUtc);
            }
            finally
            {
                TryDelete(Claimed);
            }
        }

        // Handoff files of this folder that a failure left (exact names only, older than MaxAge); returns how many went.
        internal int RemoveStale(DateTime NowUtc)
        {
            if (Folder == null || !Directory.Exists(Folder))
            {
                return 0;
            }
            int Removed = 0;
            try
            {
                foreach (string Candidate in Directory.EnumerateFiles(Folder))
                {
                    if (!IsHandoffFileName(Path.GetFileName(Candidate)))
                    {
                        continue;
                    }
                    DateTime Written = File.GetLastWriteTimeUtc(Candidate);
                    if (NowUtc - Written > MaxAge && TryDelete(Candidate))
                    {
                        Removed++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // best effort: a leftover is never used (its age and its unknown token keep it out)
            }
            return Removed;
        }

        // restart-<64 hex>.json, optionally followed by .claimed-<32 hex> or .invalid-<32 hex>
        internal static bool IsHandoffFileName(string Name)
        {
            if (!Name.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                return false;
            }
            string Rest = Name.Substring(FilePrefix.Length);
            if (Rest.Length < 64 + FileSuffix.Length || !IsLowerHex(Rest.Substring(0, 64)) || !Rest.Substring(64).StartsWith(FileSuffix, StringComparison.Ordinal))
            {
                return false;
            }
            string Tail = Rest.Substring(64 + FileSuffix.Length);
            if (Tail.Length == 0)
            {
                return true;
            }
            foreach (string Marker in new[] { ".claimed-", ".invalid-" })
            {
                if (Tail.StartsWith(Marker, StringComparison.Ordinal) && Tail.Length == Marker.Length + 32 && IsLowerHex(Tail.Substring(Marker.Length)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsLowerHex(string Text) => Text.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        // the folder, created owner-only if missing; refused if it is a link or others can write to it
        private void EnsureFolder()
        {
            string Path_ = Folder ?? throw new InvalidOperationException("No restart handoff folder.");
            if (!Directory.Exists(Path_))
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(Path_);
                }
                else
                {
                    Directory.CreateDirectory(Path_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            var Info = new DirectoryInfo(Path_);
            if (Info.LinkTarget != null)
            {
                throw new IOException("The restart handoff folder is a link.");
            }
            if (!OperatingSystem.IsWindows())
            {
                UnixFileMode Mode = File.GetUnixFileMode(Path_);
                if ((Mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                {
                    throw new IOException("Other users can write to the restart handoff folder.");
                }
            }
        }

        // The claimed file's bytes, within MaxBytes, if it is a regular file; otherwise null with the reason.
        private static byte[]? ReadBounded(string FilePath, out RestartClaimStatus Status)
        {
            try
            {
                var Info = new FileInfo(FilePath);
                if (Info.LinkTarget != null || (Info.Attributes & FileAttributes.Directory) != 0)
                {
                    Status = RestartClaimStatus.Unreadable;
                    return null;
                }
                if (Info.Length > MaxBytes)
                {
                    Status = RestartClaimStatus.TooLarge;
                    return null;
                }
                using var Stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                var Buffer = new byte[MaxBytes + 1];
                int Total = 0;
                int Read;
                while (Total < Buffer.Length && (Read = Stream.Read(Buffer, Total, Buffer.Length - Total)) > 0)
                {
                    Total += Read;
                }
                if (Total > MaxBytes)
                {
                    Status = RestartClaimStatus.TooLarge;
                    return null;
                }
                Status = RestartClaimStatus.Claimed;
                return Buffer.AsSpan(0, Total).ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Status = RestartClaimStatus.Unreadable;
                return null;
            }
        }

        private static bool TryDelete(string FilePath)
        {
            try
            {
                File.Delete(FilePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        // ---- the format: one JSON object of known members, no type information, nothing nested deeper than the window ----

        private static readonly string[] TopTabs = { "searches", "results", "settings", "campgrounds", "monitoring" };
        private static readonly string[] SettingsTabs = { "campertron", "system" };
        private static readonly string[] Outcomes = { "refreshed", "refreshedWithoutTimestamp" };

        internal static byte[] Serialize(RestartHandoff Handoff, string Binding)
        {
            using var Buffer = new MemoryStream();
            using (var Writer = new Utf8JsonWriter(Buffer))
            {
                Writer.WriteStartObject();
                Writer.WriteNumber("version", SchemaVersion);
                Writer.WriteString("binding", Binding);
                Writer.WriteString("createdUtc", Handoff.CreatedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                Writer.WriteString("outcome", Outcomes[(int)Handoff.Outcome]);
                Writer.WriteString("tab", TopTabs[(int)Handoff.Tab]);
                Writer.WriteString("settingsTab", SettingsTabs[(int)Handoff.SettingsTab]);
                Writer.WriteBoolean("monitoring", Handoff.Monitoring);
                if (Handoff.Window is RestartWindow Window)
                {
                    Writer.WriteStartObject("window");
                    Writer.WriteNumber("x", Window.X);
                    Writer.WriteNumber("y", Window.Y);
                    Writer.WriteNumber("width", Window.Width);
                    Writer.WriteNumber("height", Window.Height);
                    Writer.WriteBoolean("maximized", Window.Maximized);
                    Writer.WriteEndObject();
                }
                else
                {
                    Writer.WriteNull("window");
                }
                Writer.WriteEndObject();
            }
            return Buffer.ToArray();
        }

        internal static RestartClaim Parse(byte[] Bytes, string ExpectedBinding, DateTime NowUtc)
        {
            RestartClaim Malformed = new RestartClaim(RestartClaimStatus.Malformed, null);
            JsonDocument Document;
            try
            {
                Document = JsonDocument.Parse(Bytes, new JsonDocumentOptions { MaxDepth = 3, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            }
            catch (JsonException)
            {
                return Malformed;
            }
            using (Document)
            {
                JsonElement Root = Document.RootElement;
                if (Root.ValueKind != JsonValueKind.Object)
                {
                    return Malformed;
                }
                var Members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (JsonProperty Member in Root.EnumerateObject())
                {
                    if (!Members.TryAdd(Member.Name, Member.Value))
                    {
                        return Malformed;
                    }
                }
                string[] Known = { "version", "binding", "createdUtc", "outcome", "tab", "settingsTab", "monitoring", "window" };
                if (Members.Count != Known.Length || Known.Any(K => !Members.ContainsKey(K)))
                {
                    // an unknown member is not this schema: a later version would raise "version"
                    return Members.TryGetValue("version", out JsonElement V) && V.ValueKind == JsonValueKind.Number && V.TryGetInt32(out int Other) && Other != SchemaVersion
                        ? new RestartClaim(RestartClaimStatus.UnsupportedVersion, null)
                        : Malformed;
                }
                if (Members["version"].ValueKind != JsonValueKind.Number || !Members["version"].TryGetInt32(out int Version))
                {
                    return Malformed;
                }
                if (Version != SchemaVersion)
                {
                    return new RestartClaim(RestartClaimStatus.UnsupportedVersion, null);
                }
                if (Members["binding"].ValueKind != JsonValueKind.String || !string.Equals(Members["binding"].GetString(), ExpectedBinding, StringComparison.Ordinal))
                {
                    return new RestartClaim(RestartClaimStatus.WrongBinding, null);
                }
                if (Members["createdUtc"].ValueKind != JsonValueKind.String
                    || !DateTime.TryParseExact(Members["createdUtc"].GetString(), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime Created)
                    || Created.Kind != DateTimeKind.Utc)
                {
                    return Malformed;
                }
                if (NowUtc - Created > MaxAge || Created - NowUtc > FutureTolerance)
                {
                    return new RestartClaim(RestartClaimStatus.Stale, null);
                }
                int OutcomeIndex = IndexOf(Outcomes, Members["outcome"]);
                int TabIndex = IndexOf(TopTabs, Members["tab"]);
                int SettingsIndex = IndexOf(SettingsTabs, Members["settingsTab"]);
                if (OutcomeIndex < 0 || TabIndex < 0 || SettingsIndex < 0)
                {
                    return Malformed;
                }
                JsonValueKind MonitoringKind = Members["monitoring"].ValueKind;
                if (MonitoringKind != JsonValueKind.True && MonitoringKind != JsonValueKind.False)
                {
                    return Malformed;
                }
                RestartWindow? Window = null;
                JsonElement WindowElement = Members["window"];
                if (WindowElement.ValueKind == JsonValueKind.Object)
                {
                    if (!TryReadWindow(WindowElement, out RestartWindow Read))
                    {
                        return Malformed;
                    }
                    Window = Read;
                }
                else if (WindowElement.ValueKind != JsonValueKind.Null)
                {
                    return Malformed;
                }
                return new RestartClaim(RestartClaimStatus.Claimed,
                    new RestartHandoff(Created, (RestartOutcome)OutcomeIndex, Window, (RestartTopTab)TabIndex, (RestartSettingsTab)SettingsIndex,
                        MonitoringKind == JsonValueKind.True));
            }
        }

        private static int IndexOf(string[] Names, JsonElement Value) =>
            Value.ValueKind == JsonValueKind.String ? Array.IndexOf(Names, Value.GetString()) : -1;

        private static bool TryReadWindow(JsonElement Element, out RestartWindow Window)
        {
            Window = default;
            var Members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty Member in Element.EnumerateObject())
            {
                if (!Members.TryAdd(Member.Name, Member.Value))
                {
                    return false;
                }
            }
            string[] Known = { "x", "y", "width", "height", "maximized" };
            if (Members.Count != Known.Length || Known.Any(K => !Members.ContainsKey(K)))
            {
                return false;
            }
            if (Members["x"].ValueKind != JsonValueKind.Number || !Members["x"].TryGetInt32(out int X)
                || Members["y"].ValueKind != JsonValueKind.Number || !Members["y"].TryGetInt32(out int Y)
                || Members["width"].ValueKind != JsonValueKind.Number || !Members["width"].TryGetDouble(out double Width)
                || Members["height"].ValueKind != JsonValueKind.Number || !Members["height"].TryGetDouble(out double Height)
                || (Members["maximized"].ValueKind != JsonValueKind.True && Members["maximized"].ValueKind != JsonValueKind.False))
            {
                return false;
            }
            if (Math.Abs((long)X) > MaxCoordinate || Math.Abs((long)Y) > MaxCoordinate
                || !double.IsFinite(Width) || !double.IsFinite(Height) || Width < 1 || Height < 1 || Width > MaxSize || Height > MaxSize)
            {
                return false;
            }
            Window = new RestartWindow(X, Y, Width, Height, Members["maximized"].ValueKind == JsonValueKind.True);
            return true;
        }
    }
}
