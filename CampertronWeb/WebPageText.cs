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

    // One settings file's form as a page shows it (Pages/Shared/_SettingsFileForm.cshtml; the settings pages, D94, and since
    // v0.3.0's PR 10 the guided setup's notification step, D100): where it posts and where its test posts, the revision the
    // page shows, what has the focus, whether the page answers a POST, the page's notice, the problems, whether the two
    // variables are set (yes or no, D82), the path-free words, and, for the guided setup, the label and hint of its Save and
    // continue (null: Save)
    internal sealed record SettingsFormView(SettingsFileViewModel Form, string Action, string TestAction, int Revision, string? Focus, bool Posted,
        string? PageNotice, List<(string Id, string Text)> Errors, bool EmailVariableSet, bool NtfyVariableSet, Func<string?, string> WithoutFolder,
        string? ContinueLabel = null, string? ContinueHint = null);

    // A shared form's file state, conflict and notices as a page shows them (Pages/Shared/_FileState.cshtml)
    internal sealed record FileStateView(SettingsFileViewModel Form, Func<string?, string> WithoutFolder);
}
