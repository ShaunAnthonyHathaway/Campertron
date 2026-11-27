using System.Globalization;
using System.Text;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using W = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;

namespace CampertronLibrary.function.Interactive.Settings
{
    // how a screen below the main menu ended: back to the screen above it, or the end of the input (the console ends)
    public enum ScreenEnd
    {
        Back,
        EndOfInput
    }

    // The interactive console's host of one shared settings form (v0.4.0's phase 7, D122; business-rules §43): a
    // SettingsFileViewModel of the shared form layer (ER-37), shown at a terminal line by line and driven through the form's
    // own fields and commands, as the desktop GUI's bindings and the web UI's WebSettingsForm drive it. The form is the
    // authority: it parses what is typed, judges it, composes every word it shows, says whether Save is offered and saves
    // through its editing session (the safe writer, the backup without a replaced secret, the external-change check,
    // D40, D43, D111, D112). Nothing here parses, validates, reads or writes a settings file, and no word of the form is
    // written again here: this class only shows the form's state, asks the console's questions (InteractivePrompts) and
    // hands each answer to the field as if it were typed there.
    //
    // Phase 7 hosts Email.yaml's and Ntfy.yaml's forms; what only they have (the plain-text password, the password file,
    // the topic, the token file, the test) is in their own branches, as WebSettingsForm has it, so a later phase adds
    // General.yaml's and Web.yaml's (its confirmation) beside them without replacing this. The form lives on the console's
    // one thread: every field, command and the test run there, so the form needs no FormHost (the test is sent the way
    // the web UI sends it, TryBegin, Send and End, and the console waits for it; a Ctrl+C meanwhile is ConsoleUi's, as at
    // every moment no answer is read, and ends the process with nothing written).
    //
    // What the console adds, and only here: a value that a file holds is shown with its control characters replaced, so no
    // file can write a terminal's escape sequence; the topic is shown only on request (it works like a password, and a
    // terminal is easily seen); an action that writes, sends or discards asks once, No by default; leaving with unsaved
    // changes asks, and leaving keeps nothing that was not saved. Esc or Ctrl+C cancels the question being answered and
    // the form's choices come again; at the form's choices they leave the form (with the question when it holds changes);
    // the end of the input ends it at once, with nothing saved.
    internal sealed class InteractiveSettingsForm
    {
        private enum Step
        {
            Stay,
            Back,
            EndOfInput
        }

        private sealed record Choice(string Label, Func<Step> Run);

        private const string PasswordFileKey = "smtpPasswordFile";
        private const string PasswordKey = "smtpPassword";
        private const string TopicKey = "topic";
        private const string TokenFileKey = "tokenFile";

        private readonly InteractivePrompts Prompts;
        private readonly SettingsFileViewModel Form;
        private readonly Func<DateTime> Clock;
        private readonly Func<bool> EmailVariableSet;
        private readonly Func<bool> NtfyVariableSet;

        private InteractiveSettingsForm(InteractivePrompts Prompts, SettingsFileViewModel Form, Func<DateTime> Clock, Func<bool> EmailVariableSet, Func<bool> NtfyVariableSet)
        {
            this.Prompts = Prompts;
            this.Form = Form;
            this.Clock = Clock;
            this.EmailVariableSet = EmailVariableSet;
            this.NtfyVariableSet = NtfyVariableSet;
        }

        private EmailSettingsViewModel? Email => Form as EmailSettingsViewModel;
        private NtfySettingsViewModel? Ntfy => Form as NtfySettingsViewModel;
        private TestNotificationForm? Test => Email?.Test ?? Ntfy?.Test;

        // The form until the user leaves it or the input ends. Clock: the time a test's result says. The variables: whether
        // CAMPERTRON_EMAIL_PASSWORD and CAMPERTRON_NTFY_TOKEN are set in this process, yes or no only (D82)
        internal static ScreenEnd Run(InteractivePrompts Prompts, SettingsFileViewModel Form, Func<DateTime> Clock, Func<bool> EmailVariableSet, Func<bool> NtfyVariableSet)
        {
            return new InteractiveSettingsForm(Prompts, Form, Clock, EmailVariableSet, NtfyVariableSet).Loop();
        }

        private ScreenEnd Loop()
        {
            ShowIntroduction();
            while (true)
            {
                //every time the choices come, the file on disk is looked at: a clean form follows it, an edited one opens a
                //conflict (the session's own rule; never an overwrite)
                Form.CheckForExternalChanges();
                ShowState();
                List<Choice> Choices = BuildChoices();
                PromptResult<int> Picked = Prompts.Choose(Form.FileName, Choices.Select(C => C.Label).ToList());
                Step Next = Picked.End switch
                {
                    PromptEnd.Answered => Choices[Picked.Value].Run(),
                    PromptEnd.Cancelled or PromptEnd.Interrupted => Leave(),
                    _ => Step.EndOfInput
                };
                if (Next == Step.Back)
                {
                    return ScreenEnd.Back;
                }
                if (Next == Step.EndOfInput)
                {
                    return ScreenEnd.EndOfInput;
                }
            }
        }

        // ---- what the form shows ----

        private void ShowIntroduction()
        {
            Prompts.Heading(Form.Title + " (" + Form.FileName + ")");
            if (Ntfy != null)
            {
                SayLines(Ntfy.ModeHelp);
            }
            if (Email != null)
            {
                Prompts.Say(W.EmailVariable + ": " + (EmailVariableSet() ? W.Yes : W.No) + ". " + W.EmailVariableNote);
            }
            if (Ntfy != null)
            {
                Prompts.Say(W.NtfyVariable + ": " + (NtfyVariableSet() ? W.Yes : W.No) + ". " + W.NtfyVariableNote);
            }
        }

        private void ShowState()
        {
            Prompts.Heading(Form.Title);
            if (Email != null)
            {
                Prompts.Say(Email.OutputExplanation);
            }
            if (Ntfy != null)
            {
                Prompts.Say(Ntfy.ModeText);
            }
            if (Form.HasStateText)
            {
                Prompts.Say(MessageSeverity.AccessibleText(Form.StateText, MessageSeverity.Warning));
            }
            if (Form.HasConflict)
            {
                Prompts.Say(W.Conflict(Form.FileName) + ": " + MessageSeverity.AccessibleText(Form.ConflictText, MessageSeverity.Warning));
            }
            foreach (NoticeItem Notice in Form.FileNotices)
            {
                Prompts.Say(Notice.Text);
            }
            if (!Form.IsMissing)
            {
                foreach (SettingField Field in Form.FieldList)
                {
                    ShowField(Field);
                }
            }
            if (Form.IsDirty && Form.PreviewSummary.Length > 0)
            {
                Prompts.Say(Form.PreviewSummary);
            }
            if (Form.HasStatus)
            {
                string Severity = MessageSeverity.Effective(Form.StatusIsError ? MessageSeverity.Error : null, Form.StatusText, Form.StatusIsError);
                Prompts.Say(MessageSeverity.AccessibleText(Form.StatusText, Severity));
            }
            if (Test is TestNotificationForm Sending)
            {
                if (Sending.HasResult)
                {
                    Prompts.Say(Sending.Heading + ": " + Sending.ResultText);
                }
                else if (Sending.HasUnavailableReason)
                {
                    Prompts.Say(Sending.Heading + ": " + Sending.UnavailableReason);
                }
            }
        }

        private void ShowField(SettingField Field)
        {
            Prompts.Say("  " + Field.AccessibleName + ": " + ValueOf(Field) + (Field.IsChanged ? W.Changed : string.Empty), "    ");
            if (Field.HasMessage)
            {
                Prompts.Say("    " + Field.MessageText, "      ");
            }
            if (Field.HasLockReason)
            {
                Prompts.Say("    " + Field.LockReason, "      ");
            }
            if (Field is ListSettingField List)
            {
                ShowRows(List, "    ");
            }
        }

        private void ShowRows(ListSettingField List, string Indent)
        {
            foreach (ListRow Row in List.Rows)
            {
                Prompts.Say(Indent + Row.Number.ToString(CultureInfo.InvariantCulture) + ". " + RowValue(Row), Indent + "   ");
                if (Row.HasMessage)
                {
                    Prompts.Say(Indent + "   " + Row.MessageText, Indent + "     ");
                }
            }
        }

        // a field's value as the console shows it: the form's own words for the plain-text password (never the password,
        // D82), the topic only on request, a file's value with its control characters replaced
        private string ValueOf(SettingField Field)
        {
            switch (Field)
            {
                case TextSettingField Secret when Secret.IsSecret:
                    return Email?.PasswordState ?? W.Empty;
                case TextSettingField Topic when Ntfy != null && Field.Key == TopicKey:
                    return Topic.Text.Length == 0 ? W.Empty : W.TopicHidden;
                case TextSettingField Text:
                    return Shown(Text.Text);
                case BoolSettingField Switch:
                    return Switch.IsChecked ? W.On : W.Off;
                case ChoiceSettingField Choice:
                    return Choice.Selected == null ? W.Empty : Visible(Choice.Selected.Label);
                case ListSettingField List:
                    return List.HasNoRows ? List.EmptyText : W.Items(List.Rows.Count);
                default:
                    return W.Empty;
            }
        }

        private static string RowValue(ListRow Row) => Row.Placeholder ?? Shown(Row.Text);

        private static string Shown(string Text) => Text.Length == 0 ? W.Empty : Visible(Text);

        // a value as a terminal may show it: every control character (an escape, a line break, a bell) replaced, so a value
        // of a file is shown as one line and can never move the cursor, clear the screen or change the terminal
        internal static string Visible(string Text)
        {
            var Out = new StringBuilder(Text.Length);
            foreach (char C in Text)
            {
                Out.Append(char.IsControl(C) ? '\uFFFD' : C);
            }
            return Out.ToString();
        }

        // a text of the form that has its own line breaks (Email.yaml's password sources): each line said on its own
        private void SayLines(string Text)
        {
            foreach (string Line in Text.Split('\n'))
            {
                Prompts.Say(Line);
            }
        }

        // ---- the form's choices ----

        private List<Choice> BuildChoices()
        {
            var Choices = new List<Choice>();
            if (Form.IsMissing)
            {
                Choices.Add(new Choice(Form.CreateLabel, Create));
            }
            else if (Form.HasConflict)
            {
                if (Form.CanApplyMine)
                {
                    Choices.Add(new Choice(Form.ApplyMineLabel, () => Command(Form.ApplyMineCommand)));
                }
                if (Form.CanRecreate)
                {
                    Choices.Add(new Choice(W.Recreate, Recreate));
                }
                Choices.Add(new Choice(W.UseTheirs, UseTheirs));
            }
            else if (!Form.IsEditable)
            {
                //a file that can't be read or written: only its reload (it may have been fixed meanwhile)
                Choices.Add(new Choice(W.Reload, () => { Form.ReloadUnlessChanged(); return Step.Stay; }));
            }
            else
            {
                foreach (SettingField Field in Form.FieldList)
                {
                    if (Field.IsEditable)
                    {
                        Choices.Add(new Choice(W.Change(Field.AccessibleName), () => Edit(Field)));
                    }
                    AddFieldActions(Choices, Field);
                }
                if (Form.HasPreview)
                {
                    Choices.Add(new Choice(W.ShowChanges, ShowChanges));
                }
                if (Form.IsDirty)
                {
                    Choices.Add(new Choice(W.Save, Save));
                }
                if (Form.IsDirty || HasInputErrors)
                {
                    Choices.Add(new Choice(W.Revert, Revert));
                }
                if (!Form.IsDirty)
                {
                    Choices.Add(new Choice(W.Reload, () => { Form.ReloadUnlessChanged(); return Step.Stay; }));
                }
                if (Test is TestNotificationForm Sending && Sending.CanSend)
                {
                    Choices.Add(new Choice(Sending.ButtonLabel, () => SendTest(Sending)));
                }
            }
            Choices.Add(new Choice(W.BackToNotifications, Leave));
            return Choices;
        }

        // the actions the form offers at one of its fields (the web UI's buttons beside a box)
        private void AddFieldActions(List<Choice> Choices, SettingField Field)
        {
            if (Email != null && Field.Key == PasswordFileKey && Email.CanRemovePasswordFile)
            {
                Choices.Add(new Choice(W.RemovePasswordFile, () => Command(Email.RemovePasswordFileCommand)));
            }
            if (Email != null && Field.Key == PasswordKey && Email.CanRemovePassword)
            {
                Choices.Add(new Choice(W.RemovePassword, () => Command(Email.RemovePasswordCommand)));
            }
            if (Ntfy != null && Field.Key == TopicKey)
            {
                if (Ntfy.CanGenerateTopic)
                {
                    Choices.Add(new Choice(Ntfy.GenerateTopicLabel, GenerateTopic));
                }
                if (Ntfy.Topic.Text.Length > 0)
                {
                    Choices.Add(new Choice(W.ShowTopic, ShowTopic));
                }
            }
            if (Ntfy != null && Field.Key == TokenFileKey && Ntfy.CanRemoveTokenFile)
            {
                Choices.Add(new Choice(W.RemoveTokenFile, () => Command(Ntfy.RemoveTokenFileCommand)));
            }
        }

        // a field's own input error (a port that is not a number, a timeout out of range) that never reached the session
        private bool HasInputErrors => Form.FieldList.OfType<TextSettingField>().Any(F => F.InputError != null);

        private static Step Command(RelayCommand Command)
        {
            Command.Execute(null);
            return Step.Stay;
        }

        // a question ended without an answer: the end of the input ends the form, a cancel says so and the choices come again
        private Step Unanswered<T>(PromptResult<T> Result)
        {
            if (Result.End == PromptEnd.EndOfInput)
            {
                return Step.EndOfInput;
            }
            if (Result.IsCancelled)
            {
                Prompts.Say(InteractiveWording.Cancelled);
            }
            return Step.Stay;
        }

        // yes or no, No by default: true only for yes; Ended is set when the input ended
        private bool Confirmed(string Question, out Step Ended)
        {
            PromptResult<bool> Answer = Prompts.Confirm(Question, false);
            Ended = Answer.IsAnswered ? Step.Stay : Unanswered(Answer);
            return Answer.IsAnswered && Answer.Value;
        }

        // ---- editing a field ----

        private Step Edit(SettingField Field)
        {
            Prompts.Heading(Field.AccessibleName);
            Prompts.Say(Field.Hint);
            if (Field.HasMessage)
            {
                Prompts.Say(Field.MessageText);
            }
            return Field switch
            {
                TextSettingField Secret when Secret.IsSecret => EditSecret(Secret),
                TextSettingField Text => EditText(Text),
                BoolSettingField Switch => EditSwitch(Switch),
                ChoiceSettingField Choice => EditChoice(Choice),
                ListSettingField List => EditList(List),
                _ => Step.Stay
            };
        }

        private Step EditText(TextSettingField Field)
        {
            if (Email != null && Field.Key == PasswordFileKey)
            {
                SayLines(Email.PasswordSources);
            }
            Prompts.Say(W.Now(Ntfy != null && Field.Key == TopicKey && Field.Text.Length > 0 ? W.TopicHidden : Shown(Field.Text)));
            Prompts.Say(W.TextHow);
            PromptResult<string> Answer = Prompts.Text(W.NewValue, false);
            if (!Answer.IsAnswered)
            {
                return Unanswered(Answer);
            }
            Field.Text = Answer.Value!;
            return Step.Stay;
        }

        // the plain-text password: typed without being shown and never written back (InteractivePrompts.Secret); the form
        // gets it as if it were typed into its box, and says only what Save will do with it (PasswordState)
        private Step EditSecret(TextSettingField Field)
        {
            if (Email != null)
            {
                SayLines(Email.PasswordSources);
                Prompts.Say(Email.PasswordState);
            }
            Prompts.Say(W.SecretHow);
            PromptResult<SecretText> Answer = Prompts.Secret(W.NewPassword);
            if (!Answer.IsAnswered)
            {
                return Unanswered(Answer);
            }
            Field.Text = Answer.Value!.Reveal();
            return Step.Stay;
        }

        private Step EditSwitch(BoolSettingField Field)
        {
            PromptResult<bool> Answer = Prompts.Confirm(W.TurnOn(Field.AccessibleName), Field.IsChecked);
            if (!Answer.IsAnswered)
            {
                return Unanswered(Answer);
            }
            Field.IsChecked = Answer.Value;
            return Step.Stay;
        }

        private Step EditChoice(ChoiceSettingField Field)
        {
            Prompts.Say(W.Now(Field.Selected == null ? W.Empty : Visible(Field.Selected.Label)));
            Prompts.Say(W.ChoiceHow);
            PromptResult<int> Answer = Prompts.Choose(Field.AccessibleName, Field.Choices.Select(C => Visible(C.Label)).ToList());
            if (!Answer.IsAnswered)
            {
                return Unanswered(Answer);
            }
            Field.Selected = Field.Choices[Answer.Value];
            return Step.Stay;
        }

        // A list (Email.yaml's recipients, Ntfy.yaml's tags): its rows and the form's own row commands (add, change, remove,
        // move), each change handed to the form as the GUI's rows hand it (D71: an empty row is never written)
        private Step EditList(ListSettingField List)
        {
            while (true)
            {
                Prompts.Say(List.RowsHelp);
                if (List.HasNoRows)
                {
                    Prompts.Say("    " + List.EmptyText);
                }
                ShowRows(List, "    ");
                if (List.HasMessage)
                {
                    Prompts.Say(List.MessageText);
                }
                var Choices = new List<Choice> { new Choice(List.AddLabel, () => AddRow(List)) };
                if (List.Rows.Count > 0)
                {
                    Choices.Add(new Choice(W.ChangeItem(List.ItemName), () => OnRow(List, Row => ChangeRow(List, Row))));
                    Choices.Add(new Choice(W.RemoveItem(List.ItemName), () => OnRow(List, Row => Command(Row.RemoveCommand!))));
                }
                if (List.Rows.Count > 1)
                {
                    Choices.Add(new Choice(W.MoveUp(List.ItemName), () => OnRow(List, Row => Command(Row.UpCommand!))));
                    Choices.Add(new Choice(W.MoveDown(List.ItemName), () => OnRow(List, Row => Command(Row.DownCommand!))));
                }
                Choices.Add(new Choice(W.Done, () => Step.Back));
                PromptResult<int> Picked = Prompts.Choose(List.AccessibleName, Choices.Select(C => C.Label).ToList());
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return Step.EndOfInput;
                }
                if (!Picked.IsAnswered)
                {
                    //Esc or Ctrl+C: back to the form's choices, with the list as it is now (nothing is saved)
                    return Step.Stay;
                }
                Step Next = Choices[Picked.Value].Run();
                if (Next == Step.EndOfInput)
                {
                    return Step.EndOfInput;
                }
                if (Next == Step.Back)
                {
                    return Step.Stay;
                }
            }
        }

        private Step AddRow(ListSettingField List)
        {
            Prompts.Say(W.RowHow);
            PromptResult<string> Answer = Prompts.Text(char.ToUpperInvariant(List.ItemName[0]) + List.ItemName.Substring(1), false);
            if (!Answer.IsAnswered)
            {
                return Unanswered(Answer);
            }
            if (Answer.Value!.Length == 0)
            {
                Prompts.Say(W.NothingDone);
                return Step.Stay;
            }
            List.AddRow();
            List.Rows[List.Rows.Count - 1].Text = Answer.Value;
            return Step.Stay;
        }

        private Step ChangeRow(ListSettingField List, ListRow Row)
        {
            Prompts.Say(W.Now(RowValue(Row)));
            Prompts.Say(W.RowHow);
            PromptResult<string> Answer = Prompts.Text(Row.AccessibleName, false);
            if (!Answer.IsAnswered)
            {
                return Unanswered(Answer);
            }
            Row.Text = Answer.Value!;
            return Step.Stay;
        }

        // which row: chosen by its number among the rows as they are shown
        private Step OnRow(ListSettingField List, Func<ListRow, Step> Then)
        {
            List<ListRow> Rows = List.Rows.ToList();
            PromptResult<int> Picked = Prompts.Choose(W.WhichItem(List.ItemName), Rows.Select(R => R.AccessibleName + ": " + RowValue(R)).ToList());
            if (!Picked.IsAnswered)
            {
                return Unanswered(Picked);
            }
            return Then(Rows[Picked.Value]);
        }

        // ---- the form's actions ----

        private Step Create()
        {
            if (!Confirmed(W.CreateQuestion(Form.FileName), out Step Ended))
            {
                return Ended;
            }
            Form.CreateCommand.Execute(null);
            return Step.Stay;
        }

        private Step Recreate()
        {
            if (!Confirmed(W.RecreateQuestion(Form.FileName), out Step Ended))
            {
                return Ended;
            }
            Form.RecreateCommand.Execute(null);
            return Step.Stay;
        }

        private Step UseTheirs()
        {
            if (!Confirmed(W.UseTheirsQuestion(Form.FileName), out Step Ended))
            {
                return Ended;
            }
            Form.UseTheirsCommand.Execute(null);
            return Step.Stay;
        }

        // the lines Save would write, as the form's preview has them (the plain-text password masked by the session); for
        // Ntfy.yaml only after the question, because they can hold the topic
        private Step ShowChanges()
        {
            if (Ntfy != null && !Confirmed(W.ShowChangesQuestion, out Step Ended))
            {
                return Ended;
            }
            Prompts.Heading(Form.PreviewHeading);
            Prompts.Say(Form.PreviewSummary);
            foreach (PreviewLineItem Line in Form.PreviewLines)
            {
                Prompts.Say("  " + Visible(Line.Display), "    ");
            }
            Prompts.Say(Form.PreviewNote);
            return Step.Stay;
        }

        // Save, through the form's own Save (its checks again, the session's safe write and backup): No by default. A form
        // that can't save says why in its own words, and the user stays at the form
        private Step Save()
        {
            if (!Confirmed(W.SaveQuestion(Form.FileName), out Step Ended))
            {
                return Ended == Step.EndOfInput ? Ended : Said(W.Kept);
            }
            Form.Save();
            return Step.Stay;
        }

        private Step Revert()
        {
            if (!Confirmed(W.RevertQuestion(Form.FileName), out Step Ended))
            {
                return Ended == Step.EndOfInput ? Ended : Said(W.Kept);
            }
            Form.Revert();
            return Step.Stay;
        }

        private Step Said(string Text)
        {
            Prompts.Say(Text);
            return Step.Stay;
        }

        // a random topic in Topic, from the form (NtfyTopic): an unsaved change, never shown unless asked for
        private Step GenerateTopic()
        {
            Prompts.Heading(Ntfy!.GenerateTopicLabel);
            Prompts.Say(Ntfy.GenerateTopicHelp);
            Ntfy.GenerateTopicCommand.Execute(null);
            return Step.Stay;
        }

        private Step ShowTopic()
        {
            if (!Confirmed(W.ShowTopicQuestion, out Step Ended))
            {
                return Ended;
            }
            Prompts.Say("  " + Ntfy!.Topic.AccessibleName + ": " + Shown(Ntfy.Topic.Text), "    ");
            return Step.Stay;
        }

        // The test email or test notification (D86, D88; ER-39): the form's words, its own rules for when it can be sent (the
        // saved file, no unsaved change), then the library's TestNotification through the form, which reads the secret only
        // for this one send and gives fixed words. It writes nothing, never touches Camp.History, the lock or a monitoring
        private Step SendTest(TestNotificationForm Sending)
        {
            Prompts.Heading(Sending.Heading);
            Prompts.Say(Sending.Explanation);
            if (!Confirmed(W.SendQuestion, out Step Ended))
            {
                return Ended == Step.EndOfInput ? Ended : Said(W.NotSent);
            }
            if (!Sending.TryBegin())
            {
                Prompts.Say(Sending.UnavailableReason ?? W.NotSent);
                return Step.Stay;
            }
            Prompts.Say(Sending.BusyText);
            Sending.End(Sending.Send(Clock, CancellationToken.None));
            return Step.Stay;
        }

        // leaving the form: nothing that was not saved is kept, so unsaved changes are asked about first
        private Step Leave()
        {
            if (!Form.IsDirty)
            {
                return Step.Back;
            }
            PromptResult<bool> Answer = Prompts.Confirm(W.LeaveQuestion(Form.FileName), false);
            if (Answer.End == PromptEnd.EndOfInput)
            {
                return Step.EndOfInput;
            }
            if (Answer.IsAnswered && Answer.Value)
            {
                return Step.Back;
            }
            Prompts.Say(W.Kept);
            return Step.Stay;
        }
    }
}
