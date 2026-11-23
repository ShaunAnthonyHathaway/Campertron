using CampertronLibrary.function.WebAccess;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // Changing the password (decisions D81 and D92; business-rules-settings §31): for the signed-in owner, with the
    // current password again, a POST with the antiforgery token, at most five a minute from a client. A wrong current
    // password changes nothing and counts as a failed sign-in on the console. A change replaces the password file in one
    // step and ends every session, this one too (D81): the sign-in follows. Without password protection there is no
    // password to change, and the page goes to the start page. No password is ever put back into the page
    [EnableRateLimiting(CampertronWebHost.CredentialsPolicy)]
    public sealed class PasswordModel : PageModel
    {
        public const int MinLength = WebPasswordHash.MinLength;
        internal const string WrongCurrent = "That is not the current password.";
        internal const string NotSaved = "The password could not be changed: Campertron can't write to its settings folder. The password is still the one it was.";

        [BindProperty]
        public string? Current { get; set; }
        [BindProperty]
        public string? Password { get; set; }
        [BindProperty]
        public string? Repeat { get; set; }
        public List<(string Field, string Text)> Errors { get; } = new();

        public string? ErrorFor(string Field) => Errors.Where(Problem => Problem.Field == Field).Select(Problem => Problem.Text).FirstOrDefault();

        public IActionResult OnGet() => Protected() ? Page() : Redirect("/");

        public async Task<IActionResult> OnPostAsync()
        {
            if (!Protected())
            {
                return Redirect("/");
            }
            string? Old = Current, Entered = Password, Again = Repeat;
            Current = Password = Repeat = null;
            ModelState.Clear();
            IServiceProvider Services = HttpContext.RequestServices;
            WebPasswordFile Passwords = Services.GetRequiredService<WebPasswordFile>();
            if (!Passwords.Verify(Old))
            {
                Services.GetRequiredService<WebConsoleLines>().FailedSignIn(HttpContext.Connection.RemoteIpAddress);
                Errors.Add(("current", WrongCurrent));
                return Page();
            }
            if (WebPasswordHash.CheckNew(Entered, Again) is string Problem)
            {
                Errors.Add((string.Equals(Entered, Again, StringComparison.Ordinal) || string.IsNullOrEmpty(Entered) || Entered.Length < MinLength ? "password" : "repeat", Problem));
                return Page();
            }
            try
            {
                Passwords.Replace(Entered!, Services.GetRequiredService<WebHostSettings>().PasswordIterations);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Errors.Add(("password", NotSaved));
                return Page();
            }
            WebSessions Sessions = Services.GetRequiredService<WebSessions>();
            Sessions.Clear();
            await CampertronWebHost.SignOut(HttpContext, Sessions);
            return Redirect("/signin?notice=password-changed");
        }

        private bool Protected() => HttpContext.RequestServices.GetRequiredService<WebHostSettings>().Access.PasswordProtection;
    }
}
