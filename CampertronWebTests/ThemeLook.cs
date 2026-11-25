using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampertronWebTests
{
    // What the theme tests (D110) read and set in a browser: the stylesheet's two palettes (from the web UI's own embedded
    // site.css, so the tests follow its colors), the browser's light or dark setting (Chrome's emulated
    // prefers-color-scheme), this browser's theme cookie, and what a page is drawn in: its color-scheme, its background and
    // text, and whether any element shows a color of the other theme (a page in mixed themes)
    internal static class ThemeLook
    {
        internal static string Css()
        {
            using Stream Stream = typeof(WebTheme).Assembly.GetManifestResourceStream("CampertronWeb.Assets.site.css")!;
            return new StreamReader(Stream).ReadToEnd();
        }

        // the declarations of the first rule with exactly this selector (inside a media block or not)
        internal static Dictionary<string, string> Rule(string Css_, string Selector)
        {
            Match Found = Regex.Match(Css_, "(?m)^\\s*" + Regex.Escape(Selector) + "\\s*\\{([^}]*)\\}");
            Assert.That(Found.Success, Is.True, "the stylesheet's rule " + Selector);
            return Found.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(D => D.Split(':', 2, StringSplitOptions.TrimEntries)).Where(P => P.Length == 2).ToDictionary(P => P[0], P => P[1]);
        }

        internal static Dictionary<string, string> Light => Rule(Css(), ":root").Where(P => P.Key.StartsWith("--", StringComparison.Ordinal)).ToDictionary(P => P.Key, P => P.Value);
        internal static Dictionary<string, string> Dark => Rule(Css(), ":root[data-theme=\"dark\"]").Where(P => P.Key.StartsWith("--", StringComparison.Ordinal)).ToDictionary(P => P.Key, P => P.Value);

        // a #rrggbb color as a browser's computed style writes it
        internal static string Rgb(string Hex) => "rgb(" + int.Parse(Hex.Substring(1, 2), NumberStyles.HexNumber) + ", " + int.Parse(Hex.Substring(3, 2), NumberStyles.HexNumber) + ", "
            + int.Parse(Hex.Substring(5, 2), NumberStyles.HexNumber) + ")";

        internal static Dictionary<string, string> Palette(string Theme) => (Theme == "dark" ? Dark : Light).ToDictionary(P => P.Key, P => Rgb(P.Value));

        // the browser's light or dark setting, now (a page that is open sees the change at once)
        internal static Task Prefer(Chrome B, string Scheme) =>
            B.Send("Emulation.setEmulatedMedia", new { media = "", features = new[] { new { name = "prefers-color-scheme", value = Scheme } } });

        // this browser's theme cookie as the web UI sets it (null: none, which is System)
        internal static Task SetCookie(Chrome B, WebTestHost Web, string Value) =>
            B.Send("Network.setCookie", new { name = Web.Service<WebTheme>().CookieName, value = Value, url = Web.Root.ToString(), path = "/", httpOnly = true, sameSite = "Lax" });

        internal static async Task Prepare(Chrome B, WebTestHost Web, string? Theme, string? Prefers)
        {
            if (Prefers != null)
            {
                await Prefer(B, Prefers);
            }
            if (Theme != null)
            {
                await SetCookie(B, Web, Theme);
            }
        }

        // the theme cookie this browser keeps, as Chrome reports it (null: none)
        internal static async Task<JsonElement?> Cookie(Chrome B, WebTestHost Web)
        {
            JsonElement All = await B.Send("Network.getCookies", new { urls = new[] { Web.Root.ToString() } });
            foreach (JsonElement One in All.GetProperty("cookies").EnumerateArray())
            {
                if (One.GetProperty("name").GetString() == Web.Service<WebTheme>().CookieName)
                {
                    return One;
                }
            }
            return null;
        }

        // every color of every element the page shows (the body and what is in it: text, background, borders, outline),
        // distinct, each with the first element that has it. Not the html element, a radio button, a check box or a hidden
        // field: their colors are the browser's own for the page's color-scheme (checked above), which no text of the page
        // shows (the browser draws such a control in its own colors for that scheme)
        private const string ColorsOfThePage = "(function () { var s = {}; var all = [document.body].concat(Array.prototype.slice.call(document.body.querySelectorAll('*')));" +
            " var props = ['color', 'backgroundColor', 'borderTopColor', 'borderLeftColor', 'borderBottomColor', 'outlineColor'];" +
            " for (var i = 0; i < all.length; i++) { if (all[i].matches('input[type=radio], input[type=checkbox], input[type=hidden]')) { continue; } var c = getComputedStyle(all[i]); props.forEach(function (p) { if (!(c[p] in s)) { s[c[p]] = all[i].tagName.toLowerCase() + (all[i].id ? '#' + all[i].id : '')" +
            " + (all[i].className && typeof all[i].className === 'string' ? '.' + all[i].className.split(' ').join('.') : '') + ' ' + p; } }); } return s; })()";

        // The page is drawn in Expected ("light" or "dark") only: its color-scheme (Scheme: what the page asked for, "light
        // dark" for System), its background and text, and no element with a color of the other theme alone
        internal static async Task Check(Chrome B, string Expected, string Scheme, string What)
        {
            Dictionary<string, string> Mine = Palette(Expected);
            Dictionary<string, string> Other = Palette(Expected == "dark" ? "light" : "dark");
            Assert.That(await B.Text("getComputedStyle(document.documentElement).colorScheme"), Is.EqualTo(Scheme), What + ": the page's color-scheme");
            Assert.That(await B.Text("getComputedStyle(document.body).backgroundColor"), Is.EqualTo(Mine["--page"]), What + ": the page's background");
            Assert.That(await B.Text("getComputedStyle(document.body).color"), Is.EqualTo(Mine["--text"]), What + ": the page's text");
            Dictionary<string, string> Colors = (await B.Evaluate(ColorsOfThePage)).EnumerateObject().ToDictionary(P => P.Name, P => P.Value.GetString()!);
            List<string> Foreign = Colors.Where(C => Other.ContainsValue(C.Key) && !Mine.ContainsValue(C.Key)).Select(C => C.Key + " (" + C.Value + ")").ToList();
            Assert.That(Foreign, Is.Empty, What + ": no color of the other theme on the page");
        }

        // what the page asked for: its data-theme and its meta color-scheme
        internal static async Task<(string? Attribute, string? Meta)> Asked(Chrome B) =>
            (await B.Text("document.documentElement.getAttribute('data-theme')"), await B.Text("(document.querySelector('meta[name=\"color-scheme\"]') || {}).content"));
    }
}
