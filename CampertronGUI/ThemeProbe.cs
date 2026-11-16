using Avalonia;
using Avalonia.Styling;
using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Campertron
{
    // The theme's part of `CampertronGUI --smoke-test` (decision D50): on the platform the release runs on, the theme
    // preference store in a new temporary folder (never the user's own preferences file): the default without a file, a
    // saved theme read back by a new store, another member of the file kept by a save, a malformed file and an unknown
    // theme falling back to System; then the three themes applied to the running application through Avalonia (Light and
    // Dark must become the theme it shows, System must be Avalonia's Default variant, which follows the operating system),
    // and the application's own theme restored. The result line names only what was checked.
    internal static class ThemeProbe
    {
        internal static (bool Passed, string Detail) Run(Application? App)
        {
            if (App == null)
            {
                return Fail("there is no application to apply a theme to");
            }
            string Root = Path.Join(Path.GetTempPath(), "campertron-theme-probe-" + Guid.NewGuid().ToString("N"));
            ThemeVariant? Before = App.RequestedThemeVariant;
            try
            {
                var Store = new ThemePreferenceStore(Path.Join(Root, ThemePreferenceStore.FolderName));
                if (Store.Load() != new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Missing))
                {
                    return Fail("without a preferences file the theme is not System");
                }
                if (Store.Save(ThemeChoice.Dark) is string Failure)
                {
                    return Fail("Dark could not be saved: " + Failure);
                }
                if (new ThemePreferenceStore(Store.Folder).Load() != new ThemeLoad(ThemeChoice.Dark, ThemePreferenceSource.Saved))
                {
                    return Fail("a new store did not read the saved Dark");
                }
                File.WriteAllText(Store.FilePath!, "{\"theme\":\"Dark\",\"later\":{\"kept\":true}}");
                if (Store.Save(ThemeChoice.Light) != null || Store.Load().Choice != ThemeChoice.Light
                    || JsonNode.Parse(File.ReadAllText(Store.FilePath!))?["later"]?["kept"]?.GetValue<bool>() != true)
                {
                    return Fail("saving Light did not keep the file's other member");
                }
                File.WriteAllText(Store.FilePath!, "theme: Dark\n");
                if (Store.Load() != new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Malformed))
                {
                    return Fail("a malformed preferences file did not give System");
                }
                File.WriteAllText(Store.FilePath!, "{\"theme\":\"Sepia\"}");
                if (Store.Load() != new ThemeLoad(ThemeChoice.System, ThemePreferenceSource.Unknown))
                {
                    return Fail("an unknown theme did not give System");
                }

                AppTheme.Apply(App, ThemeChoice.Light);
                if (App.ActualThemeVariant != ThemeVariant.Light)
                {
                    return Fail("Light was applied but the application shows " + App.ActualThemeVariant);
                }
                AppTheme.Apply(App, ThemeChoice.Dark);
                if (App.ActualThemeVariant != ThemeVariant.Dark)
                {
                    return Fail("Dark was applied but the application shows " + App.ActualThemeVariant);
                }
                AppTheme.Apply(App, ThemeChoice.System);
                if (App.RequestedThemeVariant != ThemeVariant.Default
                    || (App.ActualThemeVariant != ThemeVariant.Light && App.ActualThemeVariant != ThemeVariant.Dark))
                {
                    return Fail("System did not follow the operating system (requested " + App.RequestedThemeVariant + ", shown " + App.ActualThemeVariant + ")");
                }
                return (true, "pass default=System saved=Dark reloaded=Dark kept-keys=yes malformed=System unknown=System applied=Light,Dark,System");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException
                || ex is InvalidOperationException || ex is FormatException)
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                App.RequestedThemeVariant = Before;
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        private static (bool, string) Fail(string Reason)
        {
            return (false, "fail " + Reason);
        }
    }
}
