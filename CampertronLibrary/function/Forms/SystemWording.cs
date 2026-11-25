namespace CampertronLibrary.function.Forms
{
    // The words of Settings > System that both UIs show (D50, D60; since v0.3.0's PR 9, D99, written once here, ER-37):
    // the current configuration's heading and what it is. Its items and their labels are CurrentConfiguration's (the
    // console's View Config), the campground data's card RefreshWording's, the theme's ThemeWording's (D110: both UIs have
    // the theme; each keeps it its own way, the desktop GUI in its preferences.json, D84, the web UI in the browser). Copy
    // and Open settings folder are the desktop GUI's own (a page's text is selected and copied in the browser; opening a
    // folder is desktop-only).
    public static class SystemWording
    {
        public const string Heading = "System";
        public const string ConfigurationHeading = "Current configuration";
        public const string ConfigurationIntro = "What the console's View Config shows: this version, the settings and cache folders, where results go, how many search files there "
            + "are, when the campground data was last refreshed, and the operating system and .NET it runs on. Read-only; it never shows your email settings.";
    }
}
