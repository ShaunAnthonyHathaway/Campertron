using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The campgrounds pages of the web UI (v0.3.0's PR 9, D99; business-rules-settings §36): CampertronGUI's Campgrounds
    // tab as pages, with its lists, rules and words (the Full UI rule, D84): By Park, By City and By State, each with its
    // filters, the campgrounds they give (CampgroundChoice: a campground is its FacilityID, D42) and the chosen
    // campground's details (CampgroundDetails, CampgroundsWording). Read-only: nothing here searches, writes, prepares or
    // refreshes anything; every page is a GET, its choice in its address (?park, ?state, ?city, ?id, ?part), checked
    // against the lists as they are now (WebLookupPage), and a list of campgrounds is shown fifty at a time. The ID is
    // selected and copied in the browser (the desktop GUI's Copy ID is its clipboard's: the web UI serves no script, D80).
    public sealed class CampgroundsModel : PageModel
    {
        internal WebLookupPage Lookup { get; private set; } = null!;

        // view: park, city or state (the desktop GUI's three tabs; none is By Park, the first); part: the part of the list
        // ("page" is Razor Pages' own route value)
        public IActionResult OnGet(string? view, string? park, string? state, string? city, string? id, int? part)
        {
            WebLookupView? Shown = view switch
            {
                null => WebLookupView.Park,
                "park" => WebLookupView.Park,
                "city" => WebLookupView.City,
                "state" => WebLookupView.State,
                _ => null
            };
            if (Shown == null)
            {
                return NotFound();
            }
            WebCampgrounds Data = HttpContext.RequestServices.GetRequiredService<WebCampgrounds>();
            try
            {
                Lookup = WebLookupPage.Read(Data, Shown.Value, park, state, city, id, part);
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                //the campground data can't be read (a damaged file, one being replaced by another program): fixed words
                Lookup = WebLookupPage.Unreadable(Shown.Value);
            }
            return Page();
        }
    }
}
