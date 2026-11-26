using System.Security.Claims;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The start page of this version's web UI (D92; since v0.3.0's PR 5 it leads to the settings pages, since its PR 6 to
    // the searches pages, since its PR 7 to the results page, since its PR 8 to the monitoring page and since its PR 9 to
    // the campgrounds and system pages): it says what the pages do. It shows no setting, no folder and no search; only the
    // program's version, whether a password is required and, since v0.3.0's PR 10 (D80 point 4, D100), the guided setup's
    // state in words (GuidedSetupStatus, read from the saved files).
    // While nothing is set up (GuidedSetupStatus.IsUnconfigured), the first time a browser session (without password
    // protection: this web UI) asks for this page it leads to the guided setup, so that comes first; never again in that
    // session, so the start page and every other page stay one link away and leaving the guided setup needs no action that
    // marks anything. A GET still creates, saves, sends and starts nothing.
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
        public GuidedSetupStatus Guided { get; private set; } = null!;

        public async Task<IActionResult> OnGetAsync()
        {
            Guided = GuidedSetupStatus.Read(_Settings.SettingsFolder, DateTime.Now);
            if (Guided.IsUnconfigured)
            {
                string Key = PasswordProtection
                    ? User.FindFirstValue(CampertronWebHost.SessionClaim) ?? throw new InvalidOperationException("A signed-in request has no session.")
                    : WebSettingsWorkspaces.OpenKey;
                WebSettingsWorkspaces.Workspace Space = HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>().For(Key);
                await Space.Lock.WaitAsync(HttpContext.RequestAborted);
                try
                {
                    if (!Space.GuidedOffered)
                    {
                        Space.GuidedOffered = true;
                        return LocalRedirect(GuidedSetupModel.Root);
                    }
                }
                finally
                {
                    Space.Lock.Release();
                }
            }
            Version = RuntimeInfo.Describe(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(IndexModel).Assembly).Split(Environment.NewLine)[0];
            return Page();
        }

        // what the start page says of the guided setup, and where its link leads (a method resumes where the files say):
        // the shared words and orchestration's (since v0.4.0's phase 3, D117), the address the web UI's
        public string GuidedText => GuidedSetupWording.StartState(Guided);
        public string GuidedLink => GuidedSetupWording.StartAction(Guided);
        public string GuidedAddress => GuidedSetupModel.Resume(GuidedSetupFlow.ResumeMethod(Guided));
    }
}
