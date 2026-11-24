using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace CampertronWeb
{
    // The web UI's own files for the browser (decisions D80, D81 and D92): its stylesheet and, since v0.3.0's PR 11, its
    // icon (the unified app icon, D89), and since D108 the results page's script (its automatic progress). They are
    // embedded in this assembly and served from memory by name from a fixed list. Nothing is ever served from a folder:
    // there is no static-file middleware and no web root, so no address reaches a settings file, a secret, a key, a log,
    // the database or the program's own files, whatever path it names. The stylesheet and the icon need no sign-in (the
    // sign-in page shows them); the script needs the owner, as every page does (the fallback policy): only a page of a
    // signed-in session ever names it.
    internal static class WebAssets
    {
        private sealed record Asset(byte[] Bytes, string ContentType, string Version);

        private static readonly IReadOnlyDictionary<string, Asset> Known = new Dictionary<string, Asset>(StringComparer.Ordinal)
        {
            ["site.css"] = Load("CampertronWeb.Assets.site.css", "text/css; charset=utf-8"),
            //ASP.NET Core's own type for .ico (FileExtensionContentTypeProvider)
            ["campertron.ico"] = Load("CampertronWeb.Assets.campertron.ico", "image/x-icon")
        };

        // the stylesheet's address, with a version from its content so that a new build is fetched again
        internal static readonly string StylePath = "/assets/site.css?v=" + Known["site.css"].Version;

        // the icon's address in every page's head, versioned in the same way
        internal static readonly string IconPath = "/assets/campertron.ico?v=" + Known["campertron.ico"].Version;

        // the address a browser asks for by itself when a page names no icon (a fixed page of another program, a bookmark)
        internal const string FaviconPath = "/favicon.ico";

        // the results page's script (D108): its own address, not one of the anonymous names above, so it needs the owner
        internal const string ResultsScriptRoute = "/assets/results.js";
        private static readonly Asset ResultsScript = Load("CampertronWeb.Assets.results.js", "text/javascript; charset=utf-8");
        internal static readonly string ResultsScriptPath = ResultsScriptRoute + "?v=" + ResultsScript.Version;

        private static Asset Load(string Resource, string ContentType)
        {
            using Stream Stream = typeof(WebAssets).Assembly.GetManifestResourceStream(Resource)
                ?? throw new InvalidOperationException("The web UI's embedded file " + Resource + " is missing.");
            using var Memory = new MemoryStream();
            Stream.CopyTo(Memory);
            byte[] Bytes = Memory.ToArray();
            return new Asset(Bytes, ContentType, Convert.ToHexString(SHA256.HashData(Bytes)).Substring(0, 16).ToLowerInvariant());
        }

        // GET /assets/{name}: one of the known names, else 404
        internal static async Task Serve(HttpContext Context)
        {
            string? Name = Context.Request.RouteValues["name"] as string;
            if (Name == null || !Known.TryGetValue(Name, out Asset? Found))
            {
                Context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await Write(Context, Found);
        }

        // GET /favicon.ico: the icon itself, never anything else; without a version, so a browser checks it again
        internal static Task ServeFavicon(HttpContext Context) => Write(Context, Known["campertron.ico"]);

        // GET /assets/results.js: the results page's script, for the owner only (kept by the browser, never by a shared cache)
        internal static Task ServeResultsScript(HttpContext Context) => Write(Context, ResultsScript, "private");

        private static async Task Write(HttpContext Context, Asset Found, string Scope = "public")
        {
            Context.Response.ContentType = Found.ContentType;
            Context.Response.ContentLength = Found.Bytes.Length;
            //the address with the current version never changes its content; any other is checked again
            Context.Response.Headers.CacheControl = string.Equals(Context.Request.Query["v"], Found.Version, StringComparison.Ordinal)
                ? Scope + ", max-age=31536000, immutable" : "no-cache";
            if (!HttpMethods.IsHead(Context.Request.Method))
            {
                await Context.Response.Body.WriteAsync(Found.Bytes, Context.RequestAborted);
            }
        }
    }
}
