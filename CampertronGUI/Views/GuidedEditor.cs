using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace Campertron.Views
{
    // A settings or search file's editor shown inside the guided setup (v0.4.0's phase 5; business-rules-gui §42): the very
    // editor of the Settings or Searches tab (EmailSettingsView, NtfySettingsView, SearchFileView), bound to the window's own
    // form, with its Save replaced by the guided setup's Save and continue, as the web UI's guided setup shows the settings
    // pages' own form with its Save so replaced. The guided setup sets ContinueCommand on the panel that holds the editor;
    // the value is inherited by everything inside it, so the editor's actions (SettingsFileActionsView) show Save and continue
    // bound to it instead of Save, and the editors leave out what the guided step does not show (the search's days of the week
    // and filters, which keep their values, and Ntfy.yaml's way to General.yaml, whose outputTo the last step saves). Outside
    // the guided setup the value is null and every editor is as before. Nothing here judges or saves: Save and continue is the
    // guided setup's (GuidedSetupFlow), which saves through the form's own Save.
    public static class GuidedEditor
    {
        public static readonly AttachedProperty<ICommand?> ContinueCommandProperty =
            AvaloniaProperty.RegisterAttached<Control, ICommand?>("ContinueCommand", typeof(GuidedEditor), inherits: true);

        public static ICommand? GetContinueCommand(Control Element) => Element.GetValue(ContinueCommandProperty);

        public static void SetContinueCommand(Control Element, ICommand? Value) => Element.SetValue(ContinueCommandProperty, Value);

        // whether this editor is shown inside the guided setup
        public static bool IsGuided(Control Element) => GetContinueCommand(Element) != null;
    }
}
