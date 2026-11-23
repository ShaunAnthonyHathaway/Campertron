using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // What the web UI's pages of the shared forms (the settings pages, D94; the searches pages, D96) write the same way:
    // the focus when a page opens, a disabled control, a message's style by its severity, the ids a control is described
    // by, and a text without the settings folder's path (business-rules-settings §31: a page never shows a path).
    internal static class WebPageText
    {
        internal static string? Auto(string? Focus, string Id) => Focus == Id ? "autofocus" : null;
        internal static string? Off(bool Available) => Available ? null : "disabled";
        internal static string Css(string? Severity) => Severity == MessageSeverity.Error ? "error" : Severity == MessageSeverity.Warning ? "warning" : "info";

        internal static string? Described(params string?[] Ids)
        {
            string Joined = string.Join(" ", Ids.Where(Id => !string.IsNullOrEmpty(Id)));
            return Joined.Length == 0 ? null : Joined;
        }

        // An action's result or a file's state can carry an exception's message from the operating system ("Access to the
        // path '…' is denied."), which names the settings folder; the page says "the settings folder" instead. The desktop
        // GUI shows the folder anyway and keeps the message as it is
        internal static string WithoutFolder(string SettingsFolder, string? Text)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return string.Empty;
            }
            string Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SettingsFolder));
            return Folder.Length == 0 ? Text : Text.Replace(Folder, "the settings folder", StringComparison.Ordinal);
        }
    }

    // One field of a shared form as a page shows it (Pages/Shared/_SettingField.cshtml), with what has the focus
    internal sealed record SettingFieldView(SettingsFileViewModel Form, SettingField Field, string? Focus);
}
