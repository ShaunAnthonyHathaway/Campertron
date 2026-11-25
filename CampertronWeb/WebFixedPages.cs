using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace CampertronWeb
{
    // The pages the web UI shows when it refuses or fails a request (decision D92; business-rules-settings §31): fixed
    // text for each case, written without Razor so that an error page can't fail the way the page did. Nothing of the
    // request is repeated on them (no address, name, path or value), and nothing of the failure: no exception message, no
    // stack trace, no file path, no setting. Since D110 they are written in the browser's theme (its theme cookie as one of
    // three fixed values, never its text), as every page is.
    internal static class WebFixedPages
    {
        internal static (string Title, string Text) For(int Status) => Status switch
        {
            StatusCodes.Status400BadRequest => ("The request was not accepted",
                "Campertron could not use this request. If you sent a form, open the page again and send it once more: its form may have been open for too long, or in another tab since you signed in or out."),
            StatusCodes.Status403Forbidden => ("The request was not accepted",
                "Campertron accepts a change only from its own pages. Open Campertron's web UI in the browser and try again from there."),
            StatusCodes.Status404NotFound => ("Page not found", "Campertron's web UI has no page at this address."),
            StatusCodes.Status405MethodNotAllowed => ("The request was not accepted", "Campertron's web UI does not accept this kind of request."),
            StatusCodes.Status413PayloadTooLarge => ("The request was too large", "Campertron's web UI does not accept a request of this size."),
            StatusCodes.Status429TooManyRequests => ("Too many requests", "Campertron received too many requests from this address. Wait a minute, then try again."),
            StatusCodes.Status500InternalServerError => ("Something went wrong",
                "Campertron could not show this page because of an unexpected error. Nothing was changed. Campertron's console output says more."),
            _ => ("The request was not accepted", "Campertron's web UI could not answer this request.")
        };

        internal const string HostTitle = "Campertron does not answer to this name";
        internal const string HostText = "Campertron's web UI answers to this computer's IP address, to localhost, to this computer's own name and to the names listed under allowedHosts in Web.yaml in its settings folder. Open it by the IP address, or add the name there and restart the web UI.";
        internal const string OriginText = "Campertron accepts a change only from its own pages, and this request did not say that it came from one. Open Campertron's web UI in the browser and try again from there. Behind a reverse proxy, list the proxy under trustedProxies and its name under allowedHosts in Web.yaml, and let the proxy pass the Host header on.";

        internal static Task Write(HttpContext Context, int Status) => Write(Context, Status, For(Status).Title, For(Status).Text);

        internal static async Task Write(HttpContext Context, int Status, string Title, string Text)
        {
            HttpResponse Response = Context.Response;
            //what the refusal itself said about trying again or about the methods it takes is kept
            var Allow = Response.Headers.Allow;
            var RetryAfter = Response.Headers.RetryAfter;
            Response.Clear();
            Response.Headers.Allow = Allow;
            Response.Headers.RetryAfter = RetryAfter;
            Response.StatusCode = Status;
            Response.ContentType = "text/html; charset=utf-8";
            Response.Headers.CacheControl = "no-store";
            if (HttpMethods.IsHead(Context.Request.Method))
            {
                return;
            }
            await Response.WriteAsync(Html(Title, Text, WebTheme.For(Context)), Encoding.UTF8, Context.RequestAborted);
        }

        // one fixed page; Title and Text are this class's own constants, encoded all the same; the theme the layout's way
        internal static string Html(string Title, string Text, WebThemeChoice Theme = WebThemeChoice.System) =>
            "<!DOCTYPE html>\n<html lang=\"en\" data-theme=\"" + WebTheme.Attribute(Theme) + "\">\n<head>\n<meta charset=\"utf-8\" />\n"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n<meta name=\"color-scheme\" content=\"" + WebTheme.ColorScheme(Theme) + "\" />\n"
            + "<meta name=\"robots\" content=\"noindex, nofollow\" />\n<title>" + WebUtility.HtmlEncode(Title) + " – Campertron</title>\n"
            + "<link rel=\"stylesheet\" href=\"" + WebAssets.StylePath + "\" />\n<link rel=\"icon\" href=\"" + WebAssets.IconPath + "\" />\n</head>\n<body>\n<header class=\"site-header\"><p class=\"site-name\">Campertron</p></header>\n"
            + "<main id=\"main\">\n<h1>" + WebUtility.HtmlEncode(Title) + "</h1>\n<p>" + WebUtility.HtmlEncode(Text) + "</p>\n<p><a href=\"/\">Go to Campertron's start page</a></p>\n</main>\n</body>\n</html>\n";
    }
}
