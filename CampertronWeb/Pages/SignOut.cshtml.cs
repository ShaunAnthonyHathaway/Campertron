using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // Signing out (D92): a POST only, with the antiforgery token, from the button every signed-in page has. The session
    // ends in the web UI itself (WebSessions), so the cookie, or a copy of it, is worth nothing afterwards. A GET changes
    // nothing and goes to the start page
    public sealed class SignOutModel : PageModel
    {
        public IActionResult OnGet() => Redirect("/");

        public async Task<IActionResult> OnPostAsync()
        {
            if (!HttpContext.RequestServices.GetRequiredService<WebHostSettings>().Access.PasswordProtection)
            {
                return Redirect("/");
            }
            await CampertronWebHost.SignOut(HttpContext, HttpContext.RequestServices.GetRequiredService<WebSessions>());
            return Redirect("/signin?notice=signed-out");
        }
    }
}
