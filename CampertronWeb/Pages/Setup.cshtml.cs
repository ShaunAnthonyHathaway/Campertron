using CampertronLibrary.function.WebAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The first password (decisions D81 and D92; business-rules-settings §31). The page exists only while a password is
    // required and none is set; otherwise it goes to the sign-in or the start page. Its form takes the one-time setup
    // code from Campertron's console and the new password, twice; both forms are POSTs with the antiforgery token, at
    // most five a minute from a client.
    //  - A wrong or expired code changes nothing and does not use the code up; it counts as a failed sign-in on the
    //    console. A password that is refused (too short, not the same twice) does not use the code up either.
    //  - The right code with a usable password uses the code up and creates the password file (create-only: of two
    //    setups at once, one sets the password and the other is sent to the sign-in). If the file can't be written, a new
    //    code is written to the console, because the old one is used up.
    //  - Nobody is signed in by the setup: the sign-in follows, with the password just set.
    //  - The second form writes a new code to the console, only when the last one is used or expired, so that nobody can
    //    make the owner's current code worthless.
    // The code and the passwords are never put back into the page, a redirect or a message.
    [AllowAnonymous]
    [EnableRateLimiting(CampertronWebHost.CredentialsPolicy)]
    public sealed class SetupModel : PageModel
    {
        public const int MinLength = WebPasswordHash.MinLength;
        internal const string WrongCode = "That setup code is not right. Enter the code from Campertron's console output.";
        internal const string ExpiredCode = "The setup code is no longer valid: it was used, or it is older than 30 minutes. Write a new one to the console with the button below, then enter it.";
        internal const string NotSaved = "The password could not be saved: Campertron can't write to its settings folder. A new setup code is in the console; correct the folder's permissions, then try again.";
        internal const string NewCodeWritten = "A new setup code is in Campertron's console output.";
        internal const string CodeStillValid = "The last setup code is still valid: it is in Campertron's console output.";

        [BindProperty]
        public string? Code { get; set; }
        [BindProperty]
        public string? Password { get; set; }
        [BindProperty]
        public string? Repeat { get; set; }
        public List<(string Field, string Text)> Errors { get; } = new();
        public string? Notice { get; private set; }

        public string? ErrorFor(string Field) => Errors.Where(Problem => Problem.Field == Field).Select(Problem => Problem.Text).FirstOrDefault();

        public IActionResult OnGet() => Elsewhere() ?? Page();

        public IActionResult OnPost()
        {
            if (Elsewhere() is IActionResult Other)
            {
                return Other;
            }
            string? EnteredCode = Code, Entered = Password, Again = Repeat;
            Code = Password = Repeat = null;
            ModelState.Clear();
            IServiceProvider Services = HttpContext.RequestServices;
            WebSetupCode Setup = Services.GetRequiredService<WebSetupCode>();
            WebConsoleLines Lines = Services.GetRequiredService<WebConsoleLines>();
            WebSetupCodeResult Checked = Setup.Check(EnteredCode);
            if (Checked != WebSetupCodeResult.Accepted)
            {
                if (Checked == WebSetupCodeResult.Wrong)
                {
                    Lines.FailedSignIn(HttpContext.Connection.RemoteIpAddress);
                }
                Errors.Add(("code", Checked == WebSetupCodeResult.Expired ? ExpiredCode : WrongCode));
                return Page();
            }
            if (WebPasswordHash.CheckNew(Entered, Again) is string Problem)
            {
                Errors.Add((string.Equals(Entered, Again, StringComparison.Ordinal) || string.IsNullOrEmpty(Entered) || Entered.Length < MinLength ? "password" : "repeat", Problem));
                return Page();
            }
            if (Setup.Use(EnteredCode) != WebSetupCodeResult.Accepted)
            {
                //another request used the code between the check and here
                Errors.Add(("code", ExpiredCode));
                return Page();
            }
            WebHostSettings Settings = Services.GetRequiredService<WebHostSettings>();
            try
            {
                Services.GetRequiredService<WebPasswordFile>().Create(Entered!, Settings.PasswordIterations);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Lines.SetupCode(Setup.New());
                Errors.Add(("password", NotSaved));
                return Page();
            }
            //created here, or by another setup in the same moment: either way a password is set now
            return Redirect("/signin?notice=password-set");
        }

        public IActionResult OnPostNewCode()
        {
            if (Elsewhere() is IActionResult Other)
            {
                return Other;
            }
            Code = Password = Repeat = null;
            ModelState.Clear();
            WebSetupCode Setup = HttpContext.RequestServices.GetRequiredService<WebSetupCode>();
            if (Setup.IsCurrent)
            {
                Notice = CodeStillValid;
                return Page();
            }
            HttpContext.RequestServices.GetRequiredService<WebConsoleLines>().SetupCode(Setup.New());
            Notice = NewCodeWritten;
            return Page();
        }

        private IActionResult? Elsewhere()
        {
            if (!HttpContext.RequestServices.GetRequiredService<WebHostSettings>().Access.PasswordProtection)
            {
                return Redirect("/");
            }
            return HttpContext.RequestServices.GetRequiredService<WebPasswordFile>().State() == WebPasswordState.Missing ? null : Redirect("/signin");
        }
    }
}
