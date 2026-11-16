using Avalonia;
using Avalonia.Styling;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Campertron
{
    // The GUI's theme (decision D50): follow the operating system, or always Light, or always Dark.
    public enum ThemeChoice { System, Light, Dark }

    // Where a loaded theme came from. Everything except Saved means System.
    internal enum ThemePreferenceSource
    {
        Missing,     // no preference file, or no theme in it: the default
        Saved,       // the file names one of the three themes
        Malformed,   // the file is not a JSON object (or is too large)
        Unknown,     // the theme is not one of the three names (for example one of a later version)
        Unreadable,  // the file exists but can't be read
        Unavailable, // there is no per-user application data folder
    }

    internal readonly record struct ThemeLoad(ThemeChoice Choice, ThemePreferenceSource Source)
    {
        public bool UsedFallback => Source is ThemePreferenceSource.Malformed or ThemePreferenceSource.Unknown
            or ThemePreferenceSource.Unreadable;
    }

    // The GUI's own preferences file (decision D50): `LocalApplicationData/CampertronGUI/preferences.json`, a JSON object
    // whose "theme" is "System", "Light" or "Dark". It belongs to the GUI alone: it is not a settings file of Campertron
    // (General.yaml, Email.yaml and the search files are in CampertronConfig and never hold it; the console never reads
    // it). Reading never creates anything, and anything that is not a readable object with a known theme gives System.
    // Saving replaces the file through a temporary file in the same folder and keeps every other member of the object,
    // so a preference that a later version adds survives a save by this one.
    internal sealed class ThemePreferenceStore
    {
        internal const string FolderName = "CampertronGUI";
        internal const string FileName = "preferences.json";
        internal const string ThemeKey = "theme";
        // a preferences file is a few dozen bytes; anything larger is not one
        internal const int MaxBytes = 64 * 1024;

        public ThemePreferenceStore(string? Folder)
        {
            this.Folder = Folder != null && Path.IsPathFullyQualified(Folder) ? Folder : null;
        }

        // The folder that holds the file; null when there is no usable per-user folder (never the working directory).
        public string? Folder { get; }
        public string? FilePath => Folder == null ? null : Path.Join(Folder, FileName);

        // The current user's store: in LocalApplicationData, the per-user folder that already holds CampertronConfig and
        // CampertronCache (on Linux $XDG_DATA_HOME or ~/.local/share, on Windows %LOCALAPPDATA%, on macOS
        // ~/Library/Application Support). The folder is looked up without being created.
        public static ThemePreferenceStore ForCurrentUser()
        {
            string Data;
            try
            {
                Data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
            }
            catch (PlatformNotSupportedException)
            {
                Data = string.Empty;
            }
            return new ThemePreferenceStore(Path.IsPathFullyQualified(Data) ? Path.Join(Data, FolderName) : null);
        }

        public ThemeLoad Load()
        {
            if (FilePath == null)
            {
                return new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unavailable);
            }
            byte[] Bytes;
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new ThemeLoad(ThemeChoice.System, Directory.Exists(FilePath) ? ThemePreferenceSource.Unreadable : ThemePreferenceSource.Missing);
                }
                using var Stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (Stream.Length > MaxBytes)
                {
                    return new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Malformed);
                }
                Bytes = new byte[MaxBytes + 1];
                int Length = 0;
                int Read;
                while (Length < Bytes.Length && (Read = Stream.Read(Bytes, Length, Bytes.Length - Length)) > 0)
                {
                    Length += Read;
                }
                if (Length > MaxBytes)
                {
                    return new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Malformed);
                }
                Array.Resize(ref Bytes, Length);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unreadable);
            }
            if (Parse(Bytes) is not JsonObject Root)
            {
                return new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Malformed);
            }
            if (!Root.TryGetPropertyValue(ThemeKey, out JsonNode? Value))
            {
                return new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Missing);
            }
            return TryParse(Value, out ThemeChoice Choice)
                ? new ThemeLoad(Choice, ThemePreferenceSource.Saved)
                : new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unknown);
        }

        // Saves the theme. Returns null when it was saved, otherwise why not (the folder or the system's message; the
        // file holds nothing secret). Another member of an existing object is kept; a file that is no JSON object is
        // replaced.
        public string? Save(ThemeChoice Choice)
        {
            if (Folder == null || FilePath == null)
            {
                return "there is no folder for your user's application data";
            }
            string? Temporary = null;
            try
            {
                JsonObject Root = ReadObjectForSave() ?? new JsonObject();
                Root[ThemeKey] = Choice.ToString();
                byte[] Bytes = Encoding.UTF8.GetBytes(Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                Directory.CreateDirectory(Folder);
                Temporary = Path.Join(Folder, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (var Stream = new FileStream(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Stream.Write(Bytes, 0, Bytes.Length);
                    Stream.Flush(true);
                }
                File.Move(Temporary, FilePath, true);
                Temporary = null;
                return null;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return ex.Message;
            }
            finally
            {
                if (Temporary != null)
                {
                    try
                    {
                        File.Delete(Temporary);
                    }
                    catch (Exception ex) when (IsFileError(ex))
                    {
                    }
                }
            }
        }

        internal static bool TryParse(JsonNode? Value, out ThemeChoice Choice)
        {
            Choice = ThemeChoice.System;
            if (Value is not JsonValue Text || !Text.TryGetValue(out string? Name) || Name == null)
            {
                return false;
            }
            foreach (ThemeChoice Candidate in Enum.GetValues<ThemeChoice>())
            {
                if (string.Equals(Name, Candidate.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    Choice = Candidate;
                    return true;
                }
            }
            return false;
        }

        private JsonObject? ReadObjectForSave()
        {
            try
            {
                if (FilePath == null || !File.Exists(FilePath) || new FileInfo(FilePath).Length > MaxBytes)
                {
                    return null;
                }
                return Parse(File.ReadAllBytes(FilePath)) as JsonObject;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return null;
            }
        }

        private static JsonNode? Parse(byte[] Bytes)
        {
            ReadOnlySpan<byte> Text = Bytes;
            // a byte-order mark is not part of the JSON text
            if (Text.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            {
                Text = Text.Slice(3);
            }
            try
            {
                JsonNode? Node = JsonNode.Parse(Text, null, new JsonDocumentOptions { MaxDepth = 16 });
                // an object reads its members when first asked: a duplicate member fails here, not later
                if (Node is JsonObject Members)
                {
                    Members.TryGetPropertyValue(ThemeKey, out _);
                }
                return Node;
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        private static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException
                || ex is NotSupportedException || ex is ArgumentException;
        }
    }

    // Applies a theme to the application through Avalonia (decision D50). System is ThemeVariant.Default, with which
    // Avalonia follows the operating system's Light or Dark setting and its changes; Light and Dark force that variant.
    // There is no platform-specific theme detection of Campertron's own.
    internal static class AppTheme
    {
        public static ThemeVariant VariantOf(ThemeChoice Choice)
        {
            return Choice switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        public static void Apply(Application? App, ThemeChoice Choice)
        {
            if (App != null)
            {
                App.RequestedThemeVariant = VariantOf(Choice);
            }
        }
    }
}
