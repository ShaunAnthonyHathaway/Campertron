using System.Security.Claims;
using CampertronLibrary.function.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace CampertronWeb.Pages
{
    // The searches pages of the web UI (v0.3.0's PR 6, D96; business-rules-settings §33): the search files of the settings
    // folder, New search, Look for changes on disk, each file's editor and Save As, through the shared form layer's
    // SearchesViewModel (D91), the form CampertronGUI's Searches tab shows, so both UIs list, create, edit, check and save
    // the same files with the same rules and words (the Full UI rule, D84). This page model holds no rule of a search: it
    // hands the browser's entries and button to the form (WebSettingsForm) and shows what the form says.
    //  - /searches lists the files (as the form lists them: by name, the console's own files only) with New search;
    //    /searches/{file} edits one listed file. A file is found by its exact name among the form's listed files: no name
    //    or path from a request ever reaches the file system. New files are named by the library's SearchFileName rules.
    //  - Every change is a POST with the antiforgery token (Razor Pages' own check, ER-38); a GET only looks at the files on
    //    disk, as the desktop GUI does when its tab is shown. The form of a browser session lives between requests
    //    (WebSettingsWorkspaces), used by one request at a time; a page sends the revision it showed and an out-of-date
    //    page applies nothing.
    //  - New search and Save As create a file (create-only, never over one): "?handler=create" and "?handler=saveas", with
    //    their own rate limit (CampertronWebHost.FilesPerClient); after one, the page of the new file is shown (a redirect,
    //    so the browser never sends the creation again).
    //  - Nothing here runs a search, reads availability, starts or stops a monitoring, takes the monitoring lock or writes
    //    Camp.History; nothing deletes a file (the desktop GUI's Searches tab deletes none either).
    [RequestFormLimits(ValueCountLimit = MaxFormValues, KeyLengthLimit = CampertronWebHost.MaxFormKeyLength, ValueLengthLimit = CampertronWebHost.MaxFormValueLength,
        BufferBodyLengthLimit = CampertronWebHost.MaxRequestBytes, MultipartBodyLengthLimit = CampertronWebHost.MaxRequestBytes)]
    public sealed class SearchesModel : PageModel
    {
        // the most entries a search's page may post: a selector's checked values each post one (a campground has hundreds
        // of campsite names). The attribute replaces the web UI's form limits for this page, so it names every other one as
        // the host sets it (CampertronWebHost), and the request's size limit still holds
        internal const int MaxFormValues = 4096;

        private readonly WebHostSettings _Settings;

        public SearchesModel(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        private WebSettingsWorkspaces _Workspaces => HttpContext.RequestServices.GetRequiredService<WebSettingsWorkspaces>();

        public SearchesViewModel Searches { get; private set; } = null!;
        // the file shown; null on the list's page
        public SearchFileViewModel? Form { get; private set; }
        public int Revision { get; private set; }
        // the page's own notice (an out-of-date page), or null
        public string? PageNotice { get; private set; }
        // the result of New search or Save As that led to this file's page, or null
        public string? Created { get; private set; }
        // what has the keyboard focus when the page opens after an action, or null (a page that was asked for)
        public string? FocusId { get; private set; }
        public bool Posted { get; private set; }

        // a file's page (its name as the form lists it, escaped)
        public static string Address(string FileName) => "/searches/" + Uri.EscapeDataString(FileName);

        public string WithoutFolder(string? Text) => WebPageText.WithoutFolder(_Settings.SettingsFolder, Text);

        // the problems the file's page lists at its top
        public List<(string Id, string Text)> Errors() => Form == null ? new List<(string, string)>() : WebSettingsForm.Errors(Form);

        public async Task<IActionResult> OnGetAsync(string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            //as the desktop GUI does when its tab is shown: the files are read the first time, later compared with the disk
            Searches.Show();
            Space.FollowSearches();
            if (file == null)
            {
                Revision = Space.ListRevision;
                return Page();
            }
            if (Find(file) is not SearchFileViewModel Found)
            {
                return NotFound();
            }
            Show(Found);
            Space.FollowSearches();
            Revision = Space.RevisionOf(Found);
            Created = Space.TakeCreated(Found.FileName);
            if (Created != null)
            {
                FocusId = "created";
            }
            return Page();
        }

        // the list's page: Look for changes on disk, and Enter in New search's box (the name is kept, nothing created)
        public async Task<IActionResult> OnPostAsync(string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            if (file != null)
            {
                return await Edit(Space, file, SaveAs: false);
            }
            if (AcceptList(Space))
            {
                if (string.Equals(Request.Form["action"], "look", StringComparison.Ordinal))
                {
                    Searches.CheckCommand.Execute(null);
                    FocusId = "files-heading";
                }
                else
                {
                    FocusId = "new-search-name";
                }
                Space.ListChanged();
            }
            Revision = Space.ListRevision;
            return Page();
        }

        // New search: a file from the example search, not enabled, named by the user; then its page
        public async Task<IActionResult> OnPostCreateAsync()
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            if (AcceptList(Space))
            {
                Searches.NewSearchCommand.Execute(null);
                Space.ListChanged();
                if (!Searches.NewSearchIsError && Searches.SelectedFile is SearchFileViewModel New && Searches.NewSearchMessage is string Message)
                {
                    Space.Created(New.FileName, Message);
                    return LocalRedirect(Address(New.FileName));
                }
                FocusId = "new-search-message";
            }
            Revision = Space.ListRevision;
            return Page();
        }

        // Save As: the shown search, with the page's entries, to a new file; then its page
        public async Task<IActionResult> OnPostSaveAsAsync(string? file)
        {
            WebSettingsWorkspaces.Workspace Space = await Open();
            Posted = true;
            return file == null ? NotFound() : await Edit(Space, file, SaveAs: true);
        }

        // a file's page posted: its entries into the form when the page is current, the disk looked at, then the button
        private Task<IActionResult> Edit(WebSettingsWorkspaces.Workspace Space, string Name, bool SaveAs)
        {
            if (Find(Name) is not SearchFileViewModel Found)
            {
                Searches.Show();
                Space.FollowSearches();
                if (Find(Name) is not SearchFileViewModel Listed)
                {
                    return Task.FromResult<IActionResult>(NotFound());
                }
                Found = Listed;
            }
            Show(Found);
            bool Current = int.TryParse(Request.Form["revision"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent)
                && Sent == Space.RevisionOf(Found);
            if (Current)
            {
                WebSettingsForm.Apply(Found, Request.Form);
                Searches.SaveAsName = Request.Form["saveas.name"].ToString();
            }
            Searches.CheckForExternalChanges();
            Space.FollowSearches();
            if (!Current)
            {
                PageNotice = SettingsModel.StaleText;
                FocusId = "page-notice";
            }
            else if (SaveAs)
            {
                Searches.SaveAsCommand.Execute(null);
                Space.SearchChanged(Found);
                if (!Searches.SaveAsIsError && Searches.SelectedFile is SearchFileViewModel New && !ReferenceEquals(New, Found) && Searches.SaveAsMessage is string Message)
                {
                    Space.Created(New.FileName, Message);
                    return Task.FromResult<IActionResult>(LocalRedirect(Address(New.FileName)));
                }
                //the form keeps the shown file when nothing was written
                Show(Found);
                FocusId = "saveas-message";
            }
            else
            {
                FocusId = Focus(WebSettingsForm.Run(Found, Request.Form["action"]));
                Space.SearchChanged(Found);
            }
            Revision = Space.RevisionOf(Found);
            return Task.FromResult<IActionResult>(Page());
        }

        // the list's page is current: its entry (New search's name) into the form
        private bool AcceptList(WebSettingsWorkspaces.Workspace Space)
        {
            bool Current = int.TryParse(Request.Form["revision"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent)
                && Sent == Space.ListRevision;
            if (Current)
            {
                Searches.NewSearchName = Request.Form["new.name"].ToString();
            }
            Searches.Show();
            Space.FollowSearches();
            if (!Current)
            {
                PageNotice = SettingsModel.StaleText;
                FocusId = "page-notice";
            }
            return Current;
        }

        // the workspace of the session (or of the web UI without password protection) with its lock held until the response
        // is written, and its Searches form following General.yaml's outputTo as the session's General form shows it
        private async Task<WebSettingsWorkspaces.Workspace> Open()
        {
            string Key = _Settings.Access.PasswordProtection
                ? User.FindFirstValue(CampertronWebHost.SessionClaim) ?? throw new InvalidOperationException("A signed-in request has no session.")
                : WebSettingsWorkspaces.OpenKey;
            WebSettingsWorkspaces.Workspace Space = _Workspaces.For(Key);
            await Space.Lock.WaitAsync(HttpContext.RequestAborted);
            HttpContext.Response.RegisterForDispose(new Release(Space.Lock));
            Searches = Space.Searches;
            Space.FollowOutput();
            return Space;
        }

        // a listed file by its exact name
        private SearchFileViewModel? Find(string Name) => Searches.Files.FirstOrDefault(F => string.Equals(F.FileName, Name, StringComparison.Ordinal));

        // the file shown in the editor (its selectors read their campground's values)
        private void Show(SearchFileViewModel Found)
        {
            Form = Found;
            Searches.SelectedFile = Found;
        }

        // what has the focus after a file's action: the page's notice, the problems, the result, the preview, or what the
        // action named
        private string? Focus(string? Named)
        {
            bool HasErrors = Errors().Count > 0;
            switch (Named)
            {
                case "status":
                    return Form!.HasStatus ? (Form.StatusIsError && HasErrors ? "error-summary" : "status") : HasErrors ? "error-summary" : null;
                case null:
                    return HasErrors ? "error-summary" : Form!.HasPreview ? "preview" : null;
                default:
                    return Named;
            }
        }

        private sealed class Release : IDisposable
        {
            private SemaphoreSlim? _Lock;
            internal Release(SemaphoreSlim Lock) => _Lock = Lock;
            public void Dispose() => Interlocked.Exchange(ref _Lock, null)?.Release();
        }
    }
}
