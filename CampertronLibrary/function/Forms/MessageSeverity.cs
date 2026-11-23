namespace CampertronLibrary.function.Forms
{
    // The severity of a validation or status message (decision D74, business-rules-gui.md §21), for every UI: Error (Save
    // is blocked or Campertron refuses to start), Warning (it works but needs attention) or Info (neutral information). A
    // message's severity is the one its form gives when it gives one, else the label the text starts with ("Error: ",
    // "Warning: ", "Info: "), else Error when the form says it is an error, else Info. The colors, the icons and the block
    // that shows a message are each UI's own (the desktop GUI's SeverityBlock); the rule and the words a screen reader gets
    // are here.
    public static class MessageSeverity
    {
        public const string Error = "Error";
        public const string Warning = "Warning";
        public const string Info = "Info";

        // the label a text starts with, or null; a summary of another settings file names that file first ("Ntfy.yaml:
        // Error: …", the General form's ntfy line), and its label follows the name
        public static string? LabelOf(string? Text)
        {
            if (Text == null)
            {
                return null;
            }
            int Named = Text.IndexOf(".yaml: ", StringComparison.Ordinal);
            string Rest = Named > 0 && Text.Substring(0, Named).All(char.IsLetter) ? Text.Substring(Named + 7) : Text;
            foreach (string Label in new[] { Error, Warning, Info })
            {
                if (Text.StartsWith(Label + ": ", StringComparison.Ordinal) || Rest.StartsWith(Label + ": ", StringComparison.Ordinal))
                {
                    return Label;
                }
            }
            return null;
        }

        // the severity a message is shown with: Severity when the form gives one, else the text's label, else IsError
        public static string Effective(string? Severity, string? Text, bool IsError)
        {
            return !string.IsNullOrEmpty(Severity) ? Severity! : LabelOf(Text) ?? (IsError ? Error : Info);
        }

        // what a screen reader reads: a text without its label gets its severity first; with it, the text as it is
        public static string AccessibleText(string? Text, string Effective)
        {
            return string.IsNullOrEmpty(Text) ? string.Empty : LabelOf(Text) == null ? Effective + ": " + Text : Text;
        }
    }
}
