using CampertronLibrary.function.Base;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampertronWeb.Pages
{
    // The start page of this version's web UI (D92): it says that the web UI's own pages come later, and nothing else. It
    // shows no setting, no folder and no search; only the program's version and whether a password is required
    public sealed class IndexModel : PageModel
    {
        private readonly WebHostSettings _Settings;

        public IndexModel(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        public bool PasswordProtection => _Settings.Access.PasswordProtection;
        public bool SignedIn => PasswordProtection && User.Identity?.IsAuthenticated == true;
        public string Version { get; private set; } = string.Empty;

        public void OnGet()
        {
            Version = RuntimeInfo.Describe(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(IndexModel).Assembly).Split(Environment.NewLine)[0];
        }
    }
}
