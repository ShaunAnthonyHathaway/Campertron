using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Interactive
{
    // A screen of the interactive console (since D130, PT1's console findings): the main menu, a menu, a form or a guided
    // setup step, made when the user arrives at it and shown at the top of each of its rounds (InteractivePrompts.Show):
    // arriving starts it on a cleared screen where the terminal can clear, a round after an action goes on below. Main: the
    // main menu, which shows the Campertron logo (InteractiveBranding)
    public sealed class InteractiveScreen
    {
        private readonly InteractivePrompts Prompts;
        internal bool Main { get; }

        internal InteractiveScreen(InteractivePrompts Prompts, bool Main)
        {
            this.Prompts = Prompts;
            this.Main = Main;
        }

        public void Show() => Prompts.Show(this);
    }

    // The interactive console's branding (since D130): the no-argument menu's own Campertron logo (Menu.LogoLines), drawn
    // whole where the terminal's width is known and holds it, so it never wraps or is cut; on a narrower terminal a compact
    // line; where the width is not known, the plain name. Only the name: never a version, a path or a value of a file
    internal static class InteractiveBranding
    {
        internal static readonly IReadOnlyList<string> Logo = Menu.LogoLines;
        internal static readonly int LogoWidth = Logo.Max(Line => Line.Length);
        internal const string Compact = "== CAMPERTRON ==";
        internal const string Plain = "Campertron";

        internal static IReadOnlyList<string> For(InteractiveTerminal Terminal)
        {
            if (!Terminal.WidthKnown)
            {
                return new[] { Plain };
            }
            int Width = Terminal.Width;
            return Width >= LogoWidth ? Logo : Width >= Compact.Length ? new[] { Compact } : new[] { Plain };
        }
    }
}
