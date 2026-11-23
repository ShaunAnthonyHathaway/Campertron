using CampertronLibrary.function.WebAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The sign-in (decisions D81 and D92; business-rules-settings §31): the web UI's one password. A POST only, with the
    // page's antiforgery token, at most five a minute from a client. A wrong password says one fixed sentence (the same
    // whatever was wrong with it) and counts as a failed sign-in on the console. The password is never put back into the
    // page, a redirect or a message. After a sign-in the browser goes to the page it asked for only if that is an address
    // of this web UI (never another site), else to the start page.
    [AllowAnonymous]
    [EnableRateLimiting(CampertronWebHost.CredentialsPolicy)]
    public sealed class SignInModel : PageModel
    {
        internal const string WrongPassword = "That password is not right. Enter the password of Campertron's web UI.";

        [BindProperty]
        public string? Password { get; set; }
        public string? Error { get; private set; }
        public string? Notice { get; private set; }

        public IActionResult OnGet(string? notice)
        {
            if (Elsewhere() is IActionResult Other)
            {
                return Other;
            }
            Notice = notice switch
            {
                "signed-out" => "You are signed out.",
                "password-changed" => "The password was changed, and every browser was signed out. Sign in with the new password.",
                "password-set" => "The password is set. Sign in with it.",
                _ => null
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl)
        {
            if (Elsewhere() is IActionResult Other)
            {
                return Other;
            }
            string? Entered = Password;
            Password = null;
            ModelState.Clear();
            WebPasswordFile Passwords = HttpContext.RequestServices.GetRequiredService<WebPasswordFile>();
            if (!Passwords.Verify(Entered))
            {
                HttpContext.RequestServices.GetRequiredService<WebConsoleLines>().FailedSignIn(HttpContext.Connection.RemoteIpAddress);
                Error = WrongPassword;
                return Page();
            }
            await CampertronWebHost.SignIn(HttpContext, HttpContext.RequestServices.GetRequiredService<WebSessions>(), Passwords);
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        // where this request belongs when it is not the sign-in's: the start page without password protection or when
        // already signed in, the setup while no password is set
        private IActionResult? Elsewhere()
        {
            WebHostSettings Settings = HttpContext.RequestServices.GetRequiredService<WebHostSettings>();
            if (!Settings.Access.PasswordProtection || User.Identity?.IsAuthenticated == true)
            {
                return Redirect("/");
            }
            return HttpContext.RequestServices.GetRequiredService<WebPasswordFile>().State() == WebPasswordState.Missing ? Redirect("/setup") : null;
        }
    }
}
