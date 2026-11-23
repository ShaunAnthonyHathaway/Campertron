using System.Net;
using System.Net.Sockets;
using CampertronLibrary.function.WebAccess;
using Microsoft.AspNetCore.Http;

namespace CampertronWeb
{
    // The checks every request of the web UI passes before any page sees it (decisions D81 and D92; business-rules-settings
    // §31), in this order:
    //  1. Failures: an exception a page throws becomes the fixed error page and one console line with the exception
    //     type's name; a refusal without a body (404, 400, 405, 413, 429) gets its fixed page.
    //  2. Headers: every response carries the security headers (Headers).
    //  3. The method: GET, HEAD and POST only (a change is a POST; nothing changes on a GET), and the request's size.
    //  4. The Host check (WebHosts.IsAllowedHost), after a trusted proxy's forwarded headers were applied.
    //  5. The Origin check for a POST (WebHosts.IsSameOrigin).
    // Then the rate limits, the sign-in and the page's own antiforgery check follow (CampertronWebHost).
    internal static class WebSecurity
    {
        // no script, style, image, font or connection from anywhere but the web UI itself; no inline script or style; no
        // plug-in; forms post only to it; it is never shown in a frame (D81)
        internal const string ContentSecurityPolicy =
            "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        internal const string PermissionsPolicy =
            "accelerometer=(), autoplay=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

        // The headers of every response. no-store unless the response set its own Cache-Control (only the stylesheet does):
        // no page of the web UI is kept by a browser or a proxy, so the back button after a sign-out asks again and gets the
        // sign-in. Referrer-Policy is same-origin, not no-referrer: no other site ever gets a referrer either way, but with
        // no-referrer a browser sends "Origin: null" with the web UI's own forms (the Fetch standard), and the Origin check
        // would have nothing to check. No Strict-Transport-Security: the web UI itself speaks HTTP only, and the header
        // belongs to the reverse proxy that serves HTTPS. No Server header (Kestrel's is off) and no CORS header
        internal static void Headers(HttpContext Context)
        {
            IHeaderDictionary Headers_ = Context.Response.Headers;
            Headers_.ContentSecurityPolicy = ContentSecurityPolicy;
            Headers_.XContentTypeOptions = "nosniff";
            Headers_.XFrameOptions = "DENY";
            Headers_["Referrer-Policy"] = "same-origin";
            Headers_["Permissions-Policy"] = PermissionsPolicy;
            Headers_["Cross-Origin-Opener-Policy"] = "same-origin";
            Headers_["Cross-Origin-Resource-Policy"] = "same-origin";
            Headers_["X-Robots-Tag"] = "noindex, nofollow";
            if (Headers_.CacheControl.Count == 0)
            {
                Headers_.CacheControl = "no-store";
            }
        }

        internal static Func<HttpContext, RequestDelegate, Task> Failures(WebConsoleLines Lines) => async (Context, Next) =>
        {
            Context.Response.OnStarting(() =>
            {
                Headers(Context);
                return Task.CompletedTask;
            });
            try
            {
                await Next(Context);
            }
            catch (BadHttpRequestException Bad) when (!Context.Response.HasStarted)
            {
                //a request Kestrel or the form reader refuses (too large, malformed): its status, never its message
                int Status = Bad.StatusCode >= 400 && Bad.StatusCode < 500 ? Bad.StatusCode : StatusCodes.Status400BadRequest;
                await WebFixedPages.Write(Context, Status);
                return;
            }
            catch (OperationCanceledException) when (Context.RequestAborted.IsCancellationRequested)
            {
                //the browser went away
                return;
            }
            catch (Exception Failure)
            {
                Lines.RequestFailed(Failure);
                if (Context.Response.HasStarted)
                {
                    Context.Abort();
                    return;
                }
                await WebFixedPages.Write(Context, StatusCodes.Status500InternalServerError);
                return;
            }
            if (!Context.Response.HasStarted && Context.Response.StatusCode >= 400 && Context.Response.ContentType == null && (Context.Response.ContentLength ?? 0) == 0)
            {
                await WebFixedPages.Write(Context, Context.Response.StatusCode);
            }
        };

        internal static Func<HttpContext, RequestDelegate, Task> Methods() => async (Context, Next) =>
        {
            string Method = Context.Request.Method;
            if (!HttpMethods.IsGet(Method) && !HttpMethods.IsHead(Method) && !HttpMethods.IsPost(Method))
            {
                Context.Response.Headers.Allow = "GET, HEAD, POST";
                await WebFixedPages.Write(Context, StatusCodes.Status405MethodNotAllowed);
                return;
            }
            //a body that says it is larger than the web UI reads is refused before anything of it is read (Kestrel's own
            //limit, the same number, stops a body that does not say its length)
            if (Context.Request.ContentLength > CampertronWebHost.MaxRequestBytes)
            {
                await WebFixedPages.Write(Context, StatusCodes.Status413PayloadTooLarge);
                return;
            }
            await Next(Context);
        };

        internal static Func<HttpContext, RequestDelegate, Task> Hosts(WebHostSettings Settings) => async (Context, Next) =>
        {
            if (!WebHosts.IsAllowedHost(Context.Request.Host.Value, Settings.MachineName, Settings.Access.AllowedHosts))
            {
                await WebFixedPages.Write(Context, StatusCodes.Status400BadRequest, WebFixedPages.HostTitle, WebFixedPages.HostText);
                return;
            }
            await Next(Context);
        };

        internal static Func<HttpContext, RequestDelegate, Task> Origins() => async (Context, Next) =>
        {
            HttpRequest Request = Context.Request;
            if (HttpMethods.IsPost(Request.Method)
                && !WebHosts.IsSameOrigin(Request.Scheme, Request.Host.Value, Request.Headers.Origin, Request.Headers.Referer, Request.Headers["Sec-Fetch-Site"]))
            {
                await WebFixedPages.Write(Context, StatusCodes.Status403Forbidden, WebFixedPages.For(StatusCodes.Status403Forbidden).Title, WebFixedPages.OriginText);
                return;
            }
            await Next(Context);
        };

        // The client of a request for the rate limits and the console's line: the connection's address, which is the one a
        // trusted proxy names when the request came through one (ForwardedHeaders, only for Web.yaml's trustedProxies; a
        // forwarded header from anyone else is never read). An IPv6 client counts by its /64 network, because one device
        // has as many addresses in it as it likes
        internal static string ClientKey(HttpContext Context)
        {
            IPAddress? Address = Context.Connection.RemoteIpAddress;
            if (Address == null)
            {
                return "unknown";
            }
            if (Address.IsIPv4MappedToIPv6)
            {
                Address = Address.MapToIPv4();
            }
            if (Address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return Address.ToString();
            }
            byte[] Bytes = Address.GetAddressBytes();
            Array.Clear(Bytes, 8, 8);
            return new IPAddress(Bytes).ToString() + "/64";
        }
    }
}
