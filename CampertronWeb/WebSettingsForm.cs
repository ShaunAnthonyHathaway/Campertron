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
    //
    // Since v0.3.0's PR 6 (D96) also a search file's form (SearchFileViewModel): its campground picker and its eight filter
    // selectors take their entries here too. A selector's check boxes are applied only to the values the page showed (the
    // page sends a token of them, OptionsToken) and only where the user changed one (the check boxes as shown are the
    // form's own until an action changes them, which makes the page out of date); the picker's choice is the campground
    // with exactly the ID the page listed.
    internal static class WebSettingsForm
    {
        internal const string FieldPrefix = "f.";
        internal const string SecretPrefix = "secret.";
        internal const string PresentPrefix = "present.";
        internal const string RowsPrefix = "rows.";
        internal const string OptionsPrefix = "opts.";
        // the campground picker's find box, its list's choice and the typed ID
        internal const string CampgroundFind = "campground.find";
        internal const string CampgroundMatch = "campground.match";
        internal const string CampgroundTyped = "campground.id";

        // the HTML id of a field, of a list's row and of a list's Add button
        internal static string Id(SettingField Field) => "f-" + Field.Key;
        internal static string RowId(SettingField Field, int Index) => "f-" + Field.Key + "-" + Number(Index);
        internal static string AddId(SettingField Field) => "add-" + Field.Key;
        // a selector's check box, its other value, its find and typed-value boxes and its state (the focus after its actions)
        internal static string OptionId(SettingField Field, int Index) => "f-" + Field.Key + "-o-" + Number(Index);
        internal static string OtherId(SettingField Field, int Index) => "f-" + Field.Key + "-other-" + Number(Index);
        internal static string FindId(SettingField Field) => "f-" + Field.Key + "-find";
        internal static string CustomId(SettingField Field) => "f-" + Field.Key + "-custom";
        internal static string StatusId(SettingField Field) => "f-" + Field.Key + "-status";
        internal const string CampgroundFindId = "campground-find";
        internal const string CampgroundTypedId = "campground-id";
        internal const string CampgroundCurrentId = "campground-current";
        internal static string MatchId(int Index) => "campground-match-" + Number(Index);
        // the most campgrounds the picker lists (the desktop GUI's list holds them all and scrolls)
        internal const int ListedCampgrounds = 50;

        private static string Number(int Index) => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // the values a selector shows, as a short token: entries for check boxes that are no longer the same are not applied
        internal static string OptionsToken(FilterSelectorField Selector)
        {
            var Text = new System.Text.StringBuilder();
            foreach (FilterOptionRow Option in Selector.Options)
            {
                Text.Append(Option.IsHeading ? 'h' : Option.IsChoice ? 'c' : 'g').Append('\u001f').Append(Option.Label);
                foreach (string Value in Option.Values)
                {
                    Text.Append('\u001f').Append(Value);
                }
                Text.Append('\u001e');
            }
            byte[] Hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Text.ToString()));
            return Convert.ToHexString(Hash, 0, 12).ToLowerInvariant();
        }

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
                    case CampgroundSettingField Picker:
                        //the choice in the list the page showed, then the find box (which may list other campgrounds)
                        Picker.SelectMatch(Posted.TryGetValue(CampgroundMatch, out var Match) ? Match.ToString() : null);
                        if (Posted.TryGetValue(CampgroundFind, out var Find))
                        {
                            Picker.FilterText = Find.ToString();
                        }
                        if (Posted.TryGetValue(CampgroundTyped, out var TypedId))
                        {
                            Picker.TypedId = TypedId.ToString();
                        }
                        break;
                    case FilterSelectorField Selector:
                        ApplySelector(Selector, Posted);
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

        // A selector's entries: the check boxes the user changed on the page (against the state the page showed, which is
        // the form's own while the page is current), then its find and typed-value boxes
        private static void ApplySelector(FilterSelectorField Selector, IFormCollection Posted)
        {
            if (Posted.TryGetValue(OptionsPrefix + Selector.Key, out var Token) && string.Equals(Token.ToString(), OptionsToken(Selector), StringComparison.Ordinal))
            {
                List<FilterOptionRow> Shown = Selector.Options.ToList();
                List<bool?> WasChecked = Shown.Select(Option => Option.IsChecked).ToList();
                var Checked = new HashSet<int>();
                foreach (string? Value in Posted.TryGetValue(FieldPrefix + Selector.Key + ".o", out var Values) ? Values.ToArray() : Array.Empty<string?>())
                {
                    if (int.TryParse(Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int At))
                    {
                        Checked.Add(At);
                    }
                }
                for (int i = 0; i < Shown.Count; i++)
                {
                    if (Shown[i].IsHeading)
                    {
                        continue;
                    }
                    //a partly checked group is shown unchecked: checking it adds every spelling, leaving it changes nothing
                    if (Checked.Contains(i) && WasChecked[i] != true)
                    {
                        Shown[i].IsChecked = true;
                    }
                    else if (!Checked.Contains(i) && WasChecked[i] == true)
                    {
                        Shown[i].IsChecked = false;
                    }
                }
            }
            if (Selector.CanFind && Posted.TryGetValue(FieldPrefix + Selector.Key + ".find", out var Find))
            {
                Selector.FindText = Find.ToString();
            }
            if (Posted.TryGetValue(FieldPrefix + Selector.Key + ".custom", out var Custom))
            {
                Selector.CustomText = Custom.ToString();
            }
        }

        // the problems a page lists at its top (with the element each link leads to): every field, row, value and box in
        // error, then the file's own errors
        internal static List<(string Id, string Text)> Errors(SettingsFileViewModel Form)
        {
            var Found = new List<(string, string)>();
            foreach (SettingField Field in Form.FieldList)
            {
                if (Field.IsError)
                {
                    Found.Add((Id(Field), Field.AccessibleName + ": " + Field.Message));
                }
                switch (Field)
                {
                    case ListSettingField List:
                        for (int i = 0; i < List.Rows.Count; i++)
                        {
                            if (List.Rows[i].IsError)
                            {
                                Found.Add((RowId(List, i), List.Rows[i].AccessibleName + ": " + List.Rows[i].Message));
                            }
                        }
                        break;
                    case CampgroundSettingField Picker when Picker.TypedIdIsError && Picker.TypedIdMessage != null:
                        Found.Add((CampgroundTypedId, SearchesWording.CampgroundIdName + ": " + Picker.TypedIdMessage));
                        break;
                    case FilterSelectorField Selector:
                        for (int i = 0; i < Selector.Options.Count; i++)
                        {
                            if (Selector.Options[i].IsError)
                            {
                                Found.Add((OptionId(Selector, i), Selector.Key + ": " + Selector.Options[i].MessageText.Substring("Error: ".Length)));
                            }
                        }
                        for (int i = 0; i < Selector.OtherValues.Count; i++)
                        {
                            if (Selector.OtherValues[i].IsError && Selector.OtherValues[i].Message is string Message)
                            {
                                Found.Add((OtherId(Selector, i), Selector.Key + ": " + Message));
                            }
                        }
                        if (Selector.CustomIsError && Selector.CustomMessage != null)
                        {
                            Found.Add((CustomId(Selector), Selector.CustomName + ": " + Selector.CustomMessage));
                        }
                        break;
                }
            }
            foreach (NoticeItem Notice in Form.FileNotices.Where(N => N.IsError))
            {
                Found.Add(("notices", Notice.Message));
            }
            return Found;
        }

        // Runs the button's action through the form's own commands (a command that is not available does nothing) and
        // returns the id of what should have the keyboard focus afterwards, or null
        internal static string? Run(SettingsFileViewModel Form, string? Action)
        {
            SearchFileViewModel? Search = Form as SearchFileViewModel;
            switch (Action ?? string.Empty)
            {
                case "use-campground" when Search != null:
                    Search.Campground.UseSelectedCommand.Execute(null);
                    return CampgroundCurrentId;
                case "use-campground-id" when Search != null:
                    Search.Campground.UseTypedIdCommand.Execute(null);
                    return Search.Campground.TypedIdIsError ? CampgroundTypedId : CampgroundCurrentId;
                case "find-campground" when Search != null:
                    return "campground-matches";
                case "retry-choices" when Search != null:
                    Search.RetryChoicesCommand.Execute(null);
                    return "filters";
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
                    //also a field's own input error, which never reached the session (the desktop GUI then offers no Revert)
                    if (Form.IsEditable)
                    {
                        Form.Revert();
                    }
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
            //a selector's buttons: "find.KEY", "sel-all.KEY", "sel-clear.KEY", "custom-add.KEY", "other-remove.KEY.N"
            if (Parts.Length >= 2 && Form.FieldList.OfType<FilterSelectorField>().FirstOrDefault(F => F.Key == Parts[1]) is FilterSelectorField Selector)
            {
                switch (Parts[0])
                {
                    case "find" when Parts.Length == 2:
                        return StatusId(Selector);
                    case "sel-all" when Parts.Length == 2:
                        Selector.SelectShownCommand.Execute(null);
                        return StatusId(Selector);
                    case "sel-clear" when Parts.Length == 2:
                        Selector.ClearShownCommand.Execute(null);
                        return StatusId(Selector);
                    case "custom-add" when Parts.Length == 2:
                        Selector.AddCustomCommand.Execute(null);
                        return CustomId(Selector);
                    case "other-remove" when Parts.Length == 3 && int.TryParse(Parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Other)
                                                && Other >= 0 && Other < Selector.OtherValues.Count:
                        Selector.OtherValues[Other].RemoveCommand.Execute(null);
                        return StatusId(Selector);
                }
                return null;
            }
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
