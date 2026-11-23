using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Http;

namespace CampertronWeb
{
    // How the web UI's settings pages hand a browser's form to the shared form layer (v0.3.0's PR 5;
    // business-rules-settings §32), as the desktop GUI's bindings do: each entry the page sent is put into its field of
    // the SettingsFileViewModel as if it were typed there, in the order the form shows them, and then the button's action
    // runs through the form's own command. Nothing here parses a value, judges it or composes a message: the form does
    // (ER-37). Only fields the form offers for editing take an entry; a field that is locked, a list whose rows changed
    // since the page was shown and anything the page did not show are left as they are.
    internal static class WebSettingsForm
    {
        internal const string FieldPrefix = "f.";
        internal const string SecretPrefix = "secret.";
        internal const string PresentPrefix = "present.";
        internal const string RowsPrefix = "rows.";

        // the HTML id of a field, of a list's row and of a list's Add button
        internal static string Id(SettingField Field) => "f-" + Field.Key;
        internal static string RowId(SettingField Field, int Index) => "f-" + Field.Key + "-" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        internal static string AddId(SettingField Field) => "add-" + Field.Key;

        // the entries of a page, into the form's fields
        internal static void Apply(SettingsFileViewModel Form, IFormCollection Posted)
        {
            foreach (SettingField Field in Form.FieldList)
            {
                if (!Field.IsEditable)
                {
                    continue;
                }
                switch (Field)
                {
                    case TextSettingField Secret when Secret.IsSecret:
                        //write-only (D82): an empty box keeps the password; text replaces it. It is never shown back
                        if (Posted.TryGetValue(SecretPrefix + Field.Key, out var NewSecret) && NewSecret.ToString() is string Entered && Entered.Length > 0)
                        {
                            Secret.Text = Entered;
                        }
                        break;
                    case TextSettingField Text:
                        if (Posted.TryGetValue(FieldPrefix + Field.Key, out var Value) && Value.ToString() is string Typed && Typed != Text.Text)
                        {
                            Text.Text = Typed;
                        }
                        break;
                    case BoolSettingField Switch:
                        if (Posted.ContainsKey(PresentPrefix + Field.Key))
                        {
                            Switch.IsChecked = string.Equals(Posted[FieldPrefix + Field.Key], "true", StringComparison.Ordinal);
                        }
                        break;
                    case ChoiceSettingField Choice:
                        if (Posted.TryGetValue(FieldPrefix + Field.Key, out var Picked) && int.TryParse(Picked, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int At)
                            && At >= 0 && At < Choice.Choices.Count && !ReferenceEquals(Choice.Choices[At], Choice.Selected))
                        {
                            Choice.Selected = Choice.Choices[At];
                        }
                        break;
                    case ListSettingField List:
                        //the rows as the page showed them, or none of them
                        if (Posted.TryGetValue(RowsPrefix + Field.Key, out var Shown) && int.TryParse(Shown, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Count)
                            && Count == List.Rows.Count)
                        {
                            for (int i = 0; i < Count; i++)
                            {
                                if (Posted.TryGetValue(FieldPrefix + Field.Key + "." + i.ToString(System.Globalization.CultureInfo.InvariantCulture), out var Row)
                                    && Row.ToString() is string RowText && RowText != List.Rows[i].Text)
                                {
                                    List.Rows[i].Text = RowText;
                                }
                            }
                        }
                        break;
                }
            }
        }

        // Runs the button's action through the form's own commands (a command that is not available does nothing) and
        // returns the id of what should have the keyboard focus afterwards, or null
        internal static string? Run(SettingsFileViewModel Form, string? Action)
        {
            switch (Action ?? string.Empty)
            {
                case "save":
                    //the page offers Save whenever the form can be edited (its entries arrive with the button), so the form
                    //itself decides: it saves, says what keeps it from saving ("Nothing was saved: fix the errors shown
                    //first.", "Nothing to save: …") or, for Web.yaml, asks its one confirmation (D81)
                    Form.Save();
                    return Form is WebSettingsViewModel { HasConfirmation: true } ? "confirmation" : "status";
                case "confirm-save":
                    (Form as WebSettingsViewModel)?.ConfirmSave();
                    return "status";
                case "cancel-confirmation":
                    (Form as WebSettingsViewModel)?.CancelConfirmation();
                    return "status";
                case "revert":
                    Form.RevertCommand.Execute(null);
                    return "status";
                case "reload":
                    Form.ReloadUnlessChanged();
                    return "status";
                case "create":
                    Form.CreateCommand.Execute(null);
                    return "status";
                case "use-theirs":
                    Form.UseTheirsCommand.Execute(null);
                    return "status";
                case "apply-mine":
                    Form.ApplyMineCommand.Execute(null);
                    return "status";
                case "recreate":
                    Form.RecreateCommand.Execute(null);
                    return "status";
                case "remove-password-file":
                    (Form as EmailSettingsViewModel)?.RemovePasswordFileCommand.Execute(null);
                    return "f-smtpPasswordFile";
                case "remove-password":
                    (Form as EmailSettingsViewModel)?.RemovePasswordCommand.Execute(null);
                    return "f-smtpPassword";
                case "remove-token-file":
                    (Form as NtfySettingsViewModel)?.RemoveTokenFileCommand.Execute(null);
                    return "f-tokenFile";
                case "generate-topic":
                    (Form as NtfySettingsViewModel)?.GenerateTopicCommand.Execute(null);
                    return "f-topic";
            }
            //a list's rows: "row-add.KEY", "row-remove.KEY.N", "row-up.KEY.N", "row-down.KEY.N"
            string[] Parts = (Action ?? string.Empty).Split('.');
            if (Parts.Length >= 2 && Form.FieldList.OfType<ListSettingField>().FirstOrDefault(L => L.Key == Parts[1]) is ListSettingField List)
            {
                if (Parts[0] == "row-add" && Parts.Length == 2)
                {
                    List.AddCommand.Execute(null);
                    return List.Rows.Count > 0 ? RowId(List, List.Rows.Count - 1) : AddId(List);
                }
                if (Parts.Length == 3 && int.TryParse(Parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Index)
                    && Index >= 0 && Index < List.Rows.Count)
                {
                    ListRow Row = List.Rows[Index];
                    switch (Parts[0])
                    {
                        case "row-remove":
                            Row.RemoveCommand?.Execute(null);
                            return List.Rows.Count == 0 ? AddId(List) : RowId(List, Math.Min(Index, List.Rows.Count - 1));
                        case "row-up":
                            Row.UpCommand?.Execute(null);
                            return RowId(List, List.Rows.IndexOf(Row));
                        case "row-down":
                            Row.DownCommand?.Execute(null);
                            return RowId(List, List.Rows.IndexOf(Row));
                    }
                }
            }
            //"check" and anything else: the entries were applied, nothing more
            return null;
        }
    }
}
