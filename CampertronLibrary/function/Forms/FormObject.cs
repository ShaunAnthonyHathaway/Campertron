using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CampertronLibrary.function.Forms
{
    // The shared form layer (v0.3.0, decision D79's PR 3; business-rules-gui.md §30): the forms of Settings, Searches, Results
    // and the campground data's refresh, with their fields, parsing, input errors, composed words, save rule and run rules,
    // free of any UI toolkit. The desktop GUI binds its views to these objects; the web UI will fill and read the same
    // objects, so both show the same words and refuse the same input.
    //
    // A form object says when a property changed (INotifyPropertyChanged, which the desktop GUI's bindings listen to; a
    // host that renders a form once ignores it). The notification is raised at once, on the calling thread. A form holds
    // its own state and no static state, so any number of them can live in one process; one form is used by one thread at
    // a time (FormHost).
    public abstract class FormObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // says that the property changed (its listeners read it again)
        protected internal void RaisePropertyChanged([CallerMemberName] string? PropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }

        // sets the property's field and says so, unless the value is the one it has
        protected internal TValue RaiseAndSetIfChanged<TValue>(ref TValue Field, TValue Value, [CallerMemberName] string? PropertyName = null)
        {
            if (EqualityComparer<TValue>.Default.Equals(Field, Value))
            {
                return Value;
            }
            Field = Value;
            RaisePropertyChanged(PropertyName);
            return Value;
        }
    }
}
