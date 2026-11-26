using System.Reflection;
using CampertronLibrary.function.Forms;

namespace CampertronUnitTests.function.Forms
{
    // The shared form layer (D79's PR 3) as a whole: it is free of any UI toolkit, it keeps no static state that changes (a
    // web host runs many sessions in one process), its host seam and its base object do what they say, and a message's
    // severity is read the same way by every UI (D74).
    public class FormLayerTests
    {
        private const string Layer = "CampertronLibrary.function.Forms";

        private static IEnumerable<Type> LayerTypes() => typeof(FormObject).Assembly.GetTypes().Where(T => T.Namespace == Layer || (T.DeclaringType?.Namespace == Layer));

        [Test]
        public void TheLayer_IsInTheLibrary_WhichReferencesNoUiToolkit()
        {
            Assembly Library = typeof(FormObject).Assembly;
            Assert.That(Library.GetName().Name, Is.EqualTo("CampertronLibrary"));
            Assert.That(Library.GetReferencedAssemblies().Select(A => A.Name).Where(N => N!.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                || N.StartsWith("ReactiveUI", StringComparison.OrdinalIgnoreCase) || N.StartsWith("System.Reactive", StringComparison.OrdinalIgnoreCase)
                || N.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase) || N.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)), Is.Empty);
            // the forms the desktop GUI and the web UI share are all here
            string[] Expected =
            {
                "FormObject", "FormHost", "RelayCommand", "MessageSeverity", "SettingsViewModel", "GeneralSettingsViewModel", "EmailSettingsViewModel", "NtfySettingsViewModel",
                "SettingsFileViewModel", "SettingField", "TextSettingField", "BoolSettingField", "ChoiceSettingField", "ListSettingField", "ListRow", "NoticeItem", "PreviewLineItem",
                "SecretFileMessages", "SearchesViewModel", "SearchFileViewModel", "AutoRunWording", "CampgroundCatalog", "CampgroundSettingField", "FilterSelectorField",
                "FilterChoiceSource", "ResultsSearchList", "ResultsSearchRow", "ResultSearchViewModel", "ResultRow", "ResultText", "ResultsWording", "BookingLink", "ProgressGate",
                "BlockHold", "DataOperationGate", "RefreshRules", "RefreshWording", "MonitoringController", "MonitoringControllerOptions", "MonitoringControlView",
                "MonitoringWording", "MonitoringHostWords",
            };
            Assert.That(Expected.Where(Name => !LayerTypes().Any(T => T.Name == Name)), Is.Empty);
        }

        [Test]
        public void TheLayer_KeepsNoStaticStateThatChanges()
        {
            const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var Changing = new List<string>();
            int Seen = 0;
            foreach (Type T in LayerTypes())
            {
                foreach (FieldInfo Field in T.GetFields(Statics))
                {
                    Seen++;
                    // a constant, or set once when its type is first used; a compiler's cached delegate is neither state nor shared data
                    bool Compilers = Field.Name.Contains('<') || T.Name.Contains('<');
                    if (!Field.IsLiteral && !Field.IsInitOnly && !Compilers)
                    {
                        Changing.Add(T.FullName + "." + Field.Name);
                    }
                }
                foreach (PropertyInfo Property in T.GetProperties(Statics))
                {
                    if (Property.SetMethod != null)
                    {
                        Changing.Add(T.FullName + "." + Property.Name + " (a static property that can be set)");
                    }
                }
                foreach (EventInfo Event in T.GetEvents(Statics))
                {
                    Changing.Add(T.FullName + "." + Event.Name + " (a static event)");
                }
            }
            Assert.That(Seen, Is.GreaterThan(20), "the scan saw the layer's constants");
            Assert.That(Changing, Is.Empty);
        }

        private sealed class Sample : FormObject
        {
            private string _Name = string.Empty;
            private int _Count;
            public string Name
            {
                get => _Name;
                set => RaiseAndSetIfChanged(ref _Name, value);
            }
            public int Count
            {
                get => _Count;
                set
                {
                    RaiseAndSetIfChanged(ref _Count, value);
                    RaisePropertyChanged(nameof(Text));
                }
            }
            public string Text => Name + " " + Count;
        }

        [Test]
        public void AFormObject_SaysAPropertyChanged_AtOnce_OnlyWhenItDid()
        {
            var Form = new Sample();
            Form.Name = "no listener: nothing fails";
            var Raised = new List<string?>();
            object? Sender = null;
            int Thread_ = 0;
            Form.PropertyChanged += (s, e) =>
            {
                Sender = s;
                Thread_ = Environment.CurrentManagedThreadId;
                Raised.Add(e.PropertyName + "=" + Form.Text);
            };
            Form.Name = "a";
            Assert.That(Raised, Is.EqualTo(new[] { "Name=a 0" }), "raised after the value is set, by the property's name");
            Assert.That(Sender, Is.SameAs(Form));
            Assert.That(Thread_, Is.EqualTo(Environment.CurrentManagedThreadId), "on the calling thread, before the setter returns");
            Form.Name = "a";
            Assert.That(Raised, Has.Count.EqualTo(1), "the same value is no change");
            Form.Count = 2;
            Assert.That(Raised.Skip(1), Is.EqualTo(new[] { "Count=a 2", "Text=a 2" }));
        }

        [Test]
        public void ACommand_RunsOnlyWhileItIsAvailable_AndSaysWhenThatMayHaveChanged()
        {
            bool Available = false;
            int Runs = 0, Asked = 0;
            var Command = new RelayCommand(() => Runs++, () => Available);
            Command.CanExecuteChanged += (_, _) => Asked++;
            Assert.That(Command.CanExecute(null), Is.False);
            Command.Execute(null);
            Assert.That(Runs, Is.EqualTo(0), "an unavailable command does nothing, whoever calls it");
            Available = true;
            Command.Refresh();
            Assert.That(Asked, Is.EqualTo(1));
            Command.Execute(null);
            Assert.That(Runs, Is.EqualTo(1));
            Assert.That(new RelayCommand(() => { }).CanExecute(null), Is.True);
        }

        [Test]
        public void TheHost_GivesTheFormsThreadAndTimers_AndAFormWithoutOneSaysSo()
        {
            var Queue = new Queue<Action>();
            int Captures = 0;
            var Host = new FormHost(() => { Captures++; return Queue.Enqueue; }, (Due, Job) => new Timer_(Due, Job), (Interval, Job) => new Timer_(Interval, Job));
            Action<Action> Post = Host.CapturePost();
            int Ran = 0;
            Post(() => Ran++);
            Assert.That(Ran, Is.EqualTo(0), "queued for the form's thread, never run by the caller");
            Queue.Dequeue()();
            Assert.That(Ran, Is.EqualTo(1));
            Assert.That(Captures, Is.EqualTo(1));
            Assert.That(((Timer_)Host.Delay(TimeSpan.FromSeconds(2), () => { })).Time, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(FormHost.Need(Host), Is.SameAs(Host));
            Assert.Throws<InvalidOperationException>(() => FormHost.Need(null));
            Assert.Throws<ArgumentNullException>(() => new FormHost(null!, Host.Delay, Host.Ticker));

            // a filter choice source made for a host captures the host's post where it is made, once
            _ = new FilterChoiceSource("/nowhere", Host);
            Assert.That(Captures, Is.EqualTo(2));
        }

        private sealed class Timer_ : IDisposable
        {
            internal Timer_(TimeSpan Time, Action Job) => this.Time = Time;
            internal TimeSpan Time { get; }
            public void Dispose()
            {
            }
        }

        [Test]
        public void AMessagesSeverity_IsTheFormsOwn_ElseItsLabel_ElseWhetherItIsAnError()
        {
            Assert.That(MessageSeverity.LabelOf("Error: fix this"), Is.EqualTo("Error"));
            Assert.That(MessageSeverity.LabelOf("Warning: look at this"), Is.EqualTo("Warning"));
            Assert.That(MessageSeverity.LabelOf("Info: for your information"), Is.EqualTo("Info"));
            Assert.That(MessageSeverity.LabelOf("Ntfy.yaml: Error: no topic"), Is.EqualTo("Error"), "a summary of another settings file names the file first");
            Assert.That(MessageSeverity.LabelOf("Ntfy.yaml: ntfy is on."), Is.Null);
            Assert.That(MessageSeverity.LabelOf("Some file.yaml: Error: no"), Is.Null, "only a plain file name before the label");
            Assert.That(MessageSeverity.LabelOf("Errors: three"), Is.Null);
            Assert.That(MessageSeverity.LabelOf("error: lower case is no label"), Is.Null);
            Assert.That(MessageSeverity.LabelOf(null), Is.Null);

            Assert.That(MessageSeverity.Effective("Warning", "Error: text", IsError: true), Is.EqualTo("Warning"), "the form's own severity first");
            Assert.That(MessageSeverity.Effective(null, "Warning: text", IsError: true), Is.EqualTo("Warning"), "then the label");
            Assert.That(MessageSeverity.Effective(string.Empty, "Saved General.yaml.", IsError: false), Is.EqualTo("Info"));
            Assert.That(MessageSeverity.Effective(null, "Nothing was saved.", IsError: true), Is.EqualTo("Error"));

            // what a screen reader gets: never color alone
            Assert.That(MessageSeverity.AccessibleText("Nothing was saved.", "Error"), Is.EqualTo("Error: Nothing was saved."));
            Assert.That(MessageSeverity.AccessibleText("Warning: look at this", "Warning"), Is.EqualTo("Warning: look at this"), "a text with its label is read as it is");
            Assert.That(MessageSeverity.AccessibleText("Ntfy.yaml: Error: no topic", "Error"), Is.EqualTo("Ntfy.yaml: Error: no topic"));
            Assert.That(MessageSeverity.AccessibleText(null, "Info"), Is.Empty);
            Assert.That(new[] { MessageSeverity.Error, MessageSeverity.Warning, MessageSeverity.Info }, Is.EqualTo(new[] { "Error", "Warning", "Info" }));
        }
    }
}
