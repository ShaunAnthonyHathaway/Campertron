using Microsoft.AspNetCore.Http;

namespace CampertronWeb
{
    // The web UI's theme (decision D110; business-rules-settings §40; engineering rule ER-44): System, the default, follows
    // the browser's light or dark setting (prefers-color-scheme); Light and Dark always use that theme. The desktop GUI has
    // the same three choices (D50) and keeps its own in its preferences.json (desktop-only storage, D84); the web UI keeps
    // a browser's choice in that browser, as one cookie, so that:
    //  - every page is written in the chosen theme from its first byte (the layout's data-theme and color-scheme, and the
    //    fixed pages' too): no script decides it, so nothing shows the other theme first, and a page without script works;
    //  - each browser (each profile, each cookie jar) has its own choice: nothing of it is the web UI's, a session's or a
    //    settings file's, and no request can change another browser's;
    //  - it outlives a sign-out, a password change, a restart of web mode and of the browser (a cookie with a lifetime), and
    //    System is no cookie at all.
    // The cookie holds only "light" or "dark", nothing secret and nothing of the session: it signs no one in and keeps no
    // one signed in. HttpOnly (no script reads it), SameSite=Lax (a link from another site still shows the chosen theme;
    // it changes nothing, and only the system page's protected POST sets it), Secure when the request is HTTPS (behind a
    // trusted reverse proxy that says so; a browser keeps no Secure cookie of plain HTTP), Path=/, one year from the last
    // choice. Its name ends with the settings folder's short name, as the session's does, so that two web UIs on one
    // computer keep their own. Any other value is System.
    internal enum WebThemeChoice { System, Light, Dark }

    internal sealed class WebTheme
    {
        // the system page's field and its values (the form's radio buttons)
        internal const string Field = "theme";
        internal const string SystemValue = "system";
        internal const string LightValue = "light";
        internal const string DarkValue = "dark";
        internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

        internal WebTheme(string Instance)
        {
            CookieName = "campertron-theme-" + Instance;
        }

        internal string CookieName { get; }

        // the choice the request's browser made, or System
        internal WebThemeChoice Of(HttpRequest Request) => Request.Cookies[CookieName] switch
        {
            LightValue => WebThemeChoice.Light,
            DarkValue => WebThemeChoice.Dark,
            _ => WebThemeChoice.System
        };

        // the choice of a request, for a page written without Razor (WebFixedPages): System when the web UI's theme is not
        // at hand
        internal static WebThemeChoice For(HttpContext Context) =>
            (Context.RequestServices?.GetService(typeof(WebTheme)) as WebTheme)?.Of(Context.Request) ?? WebThemeChoice.System;

        // the form's value, exactly one of the three, or false
        internal static bool TryParse(string? Value, out WebThemeChoice Choice)
        {
            (bool Known, Choice) = Value switch
            {
                SystemValue => (true, WebThemeChoice.System),
                LightValue => (true, WebThemeChoice.Light),
                DarkValue => (true, WebThemeChoice.Dark),
                _ => (false, WebThemeChoice.System)
            };
            return Known;
        }

        internal static string Value(WebThemeChoice Choice) => Choice switch
        {
            WebThemeChoice.Light => LightValue,
            WebThemeChoice.Dark => DarkValue,
            _ => SystemValue
        };

        // the page's data-theme: "system", "light" or "dark" (for System the stylesheet's prefers-color-scheme decides)
        internal static string Attribute(WebThemeChoice Choice) => Value(Choice);

        // the page's color-scheme (its meta element), so that the browser draws the page's canvas and its own controls in
        // the chosen theme before the stylesheet has arrived
        internal static string ColorScheme(WebThemeChoice Choice) => Choice switch
        {
            WebThemeChoice.Light => "light",
            WebThemeChoice.Dark => "dark",
            _ => "light dark"
        };

        // keeps the browser's choice: Light or Dark as the cookie, System as no cookie
        internal void Save(HttpContext Context, WebThemeChoice Choice)
        {
            var Options = new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Context.Request.IsHttps,
                IsEssential = true
            };
            if (Choice == WebThemeChoice.System)
            {
                Context.Response.Cookies.Delete(CookieName, Options);
                return;
            }
            Options.MaxAge = Lifetime;
            Context.Response.Cookies.Append(CookieName, Value(Choice), Options);
        }
    }
}
