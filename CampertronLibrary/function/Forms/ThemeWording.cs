namespace CampertronLibrary.function.Forms
{
    // The words of the theme that both UIs show (D50 for the desktop GUI; since D110 the web UI's system page too, written
    // once here, ER-37): the choice's heading, its three names and what Light and Dark do. Each UI keeps its theme where it
    // belongs (the Full UI rule's exception, D84, D110): the desktop GUI in its own preferences.json, the web UI in the
    // browser. So what System follows (the operating system, or the browser) and where the choice is kept are each UI's own
    // words. Nothing of the theme is in a settings file, and the console has no theme.
    public static class ThemeWording
    {
        public const string Heading = "Theme";
        public const string SystemLabel = "System";
        public const string LightLabel = "Light";
        public const string DarkLabel = "Dark";
        public const string LightHelp = "Always use the light theme.";
        public const string DarkHelp = "Always use the dark theme.";
        public const string DefaultNote = "This is the default.";
    }
}
