using System.Collections.Generic;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // The problems a form shows (v0.4.0's phase 3, D117; business-rules-settings §30): every field, row, value and box in
    // error, then the file's own errors, in the order the form shows them, each in the words a list of the form's problems
    // says (the field's name, then its message). The web UI's error summary lists them, each linked to its element
    // (WebSettingsForm.Errors, which composed them until phase 3); the guided setup goes on only from a form that has none
    // (GuidedSetupFlow). It judges nothing itself: each problem is the form's own (a field's, a row's, a value's or the
    // file's). No UI toolkit and no static state that changes (ER-37).

    // where a problem belongs: a field (Field), one of a list's rows (Row, Index), the campground picker's typed ID
    // (CampgroundId), one of a filter selector's values (Option, Index), one of its other values (OtherValue, Index), its
    // typed value (CustomValue), or the file itself (File)
    public enum FormProblemPlace
    {
        Field,
        Row,
        CampgroundId,
        Option,
        OtherValue,
        CustomValue,
        File,
    }

    public sealed record FormProblem(FormProblemPlace Place, SettingField? Field, int Index, string Text);

    public static class FormProblems
    {
        // the form's problems now, in the order the form shows them; empty when it has none
        public static IReadOnlyList<FormProblem> Of(SettingsFileViewModel Form)
        {
            var Found = new List<FormProblem>();
            foreach (SettingField Field in Form.FieldList)
            {
                if (Field.IsError)
                {
                    Found.Add(new FormProblem(FormProblemPlace.Field, Field, 0, Field.AccessibleName + ": " + Field.Message));
                }
                switch (Field)
                {
                    case ListSettingField List:
                        for (int i = 0; i < List.Rows.Count; i++)
                        {
                            if (List.Rows[i].IsError)
                            {
                                Found.Add(new FormProblem(FormProblemPlace.Row, List, i, List.Rows[i].AccessibleName + ": " + List.Rows[i].Message));
                            }
                        }
                        break;
                    case CampgroundSettingField Picker when Picker.TypedIdIsError && Picker.TypedIdMessage != null:
                        Found.Add(new FormProblem(FormProblemPlace.CampgroundId, Picker, 0, SearchesWording.CampgroundIdName + ": " + Picker.TypedIdMessage));
                        break;
                    case FilterSelectorField Selector:
                        for (int i = 0; i < Selector.Options.Count; i++)
                        {
                            if (Selector.Options[i].IsError)
                            {
                                Found.Add(new FormProblem(FormProblemPlace.Option, Selector, i, Selector.Key + ": " + Selector.Options[i].MessageText.Substring("Error: ".Length)));
                            }
                        }
                        for (int i = 0; i < Selector.OtherValues.Count; i++)
                        {
                            if (Selector.OtherValues[i].IsError && Selector.OtherValues[i].Message is string Message)
                            {
                                Found.Add(new FormProblem(FormProblemPlace.OtherValue, Selector, i, Selector.Key + ": " + Message));
                            }
                        }
                        if (Selector.CustomIsError && Selector.CustomMessage != null)
                        {
                            Found.Add(new FormProblem(FormProblemPlace.CustomValue, Selector, 0, Selector.CustomName + ": " + Selector.CustomMessage));
                        }
                        break;
                }
            }
            foreach (NoticeItem Notice in Form.FileNotices.Where(N => N.IsError))
            {
                Found.Add(new FormProblem(FormProblemPlace.File, null, 0, Notice.Message));
            }
            return Found;
        }

        // whether the form has any problem now
        public static bool Any(SettingsFileViewModel Form) => Of(Form).Count > 0;
    }
}
