using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using CampertronLibrary.function.WebAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CampertronWeb
{
    // The web UI's host (decisions D80, D81 and D92; business-rules-settings §31; engineering rule ER-38): Kestrel and Razor
    // Pages, built by hand on an empty builder (WebApplication.CreateEmptyBuilder), so that nothing reaches it that this
    // class does not put there:
    //  - no configuration from appsettings files, environment variables (ASPNETCORE_URLS, ASPNETCORE_ENVIRONMENT, the
    //    container image's ASPNETCORE_HTTP_PORTS) or the command line: where it listens is WebHostSettings' alone;
    //  - no logging provider: ASP.NET Core writes nothing to the console (D81); the web UI's lines are WebConsoleLines';
    //  - no developer exception page, no static files, no HTTPS redirection, no HSTS, no CORS.
    // ASP.NET Core's own services need its service container; the web UI's classes are registered there as single
    // instances of one host, with no static state, so several hosts run in one process (the tests do).
    public static class CampertronWebHost
    {
        internal const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        internal const string SessionClaim = "campertron.session";
        internal const string CredentialsPolicy = "credentials";
        // the most POSTs with a password or a setup code a client may send in a rate window (a minute), and all clients
        // together; the most requests of any kind a client may send in it
        internal const int CredentialsPerClient = 5;
        internal const int CredentialsInAll = 30;
        internal const int RequestsPerClient = 240;
        // the largest request body (later pages' forms are far smaller) and the limits of a form
        internal const long MaxRequestBytes = 1024 * 1024;
        internal const string HealthPath = "/healthz";
        internal const string HealthText = "ok";

        // the names of the session cookie and the forms' cookie: with the settings folder's own short name
        // (WebKeyRepository.InstanceId), because a browser keeps cookies by host name alone, not by port, and two web UIs on
        // one computer would otherwise replace each other's
        internal static string SessionCookie(string Instance) => "campertron-session-" + Instance;
        internal static string FormCookie(string Instance) => "campertron-form-" + Instance;

        // Builds the web UI. Settings.SettingsFolder's Web.keys must be prepared (WebKeyRepository.Prepare)
        public static WebApplication Build(WebHostSettings Settings)
        {
            WebApplicationBuilder Builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(CampertronWebHost).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory
            });
            Builder.WebHost.UseKestrelCore().ConfigureKestrel(Options =>
            {
                Options.AddServerHeader = false;
                Options.Limits.MaxRequestBodySize = MaxRequestBytes;
                Options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
                Options.Limits.MaxRequestHeaderCount = 64;
                Options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
                Options.Limits.MaxConcurrentConnections = 256;
                void Http1(ListenOptions Listen) => Listen.Protocols = HttpProtocols.Http1;
                if (Settings.ListenLocalhost)
                {
                    Options.ListenLocalhost(Settings.Port, Http1);
                }
                else if (Settings.Listen != null)
                {
                    Options.Listen(Settings.Listen, Settings.Port, Http1);
                }
                else
                {
                    Options.ListenAnyIP(Settings.Port, Http1);
                }
            });
            IServiceCollection Services = Builder.Services;
            Services.Configure<HostOptions>(Options => Options.ShutdownTimeout = TimeSpan.FromSeconds(5));
            Services.Configure<ConsoleLifetimeOptions>(Options => Options.SuppressStatusMessages = true);

            var Lines = new WebConsoleLines(Settings);
            var Passwords = new WebPasswordFile(Settings.SettingsFolder);
            var Sessions = new WebSessions(Settings.UtcNow);
            var Setup = new WebSetupCode(Settings.UtcNow);
            var Keys = new WebKeyRepository(Settings.SettingsFolder);
            string Instance = Keys.InstanceId();
            Services.AddSingleton(Settings);
            Services.AddSingleton(Lines);
            Services.AddSingleton(Passwords);
            Services.AddSingleton(Sessions);
            Services.AddSingleton(Setup);

            //the keys of the session cookie and the antiforgery tokens: in the settings folder, owner-only (WebKeyRepository)
            Services.AddDataProtection().SetApplicationName("Campertron");
            Services.Configure<KeyManagementOptions>(Options => Options.XmlRepository = Keys);

            Services.AddAuthentication(Scheme).AddCookie(Scheme, Options =>
            {
                Options.Cookie.Name = SessionCookie(Instance);
                Options.Cookie.HttpOnly = true;
                Options.Cookie.SameSite = SameSiteMode.Strict;
                //Secure when the request is HTTPS, which it is only behind a trusted reverse proxy that says so
                Options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                Options.Cookie.IsEssential = true;
                Options.Cookie.Path = "/";
                //the ticket lives as long as a session can; the session's own end is WebSessions' (idle time, sign-out)
                Options.ExpireTimeSpan = WebSessions.AbsoluteLifetime;
                Options.SlidingExpiration = false;
                Options.LoginPath = "/signin";
                Options.AccessDeniedPath = "/signin";
                Options.ReturnUrlParameter = "returnUrl";
                Options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async Context =>
                    {
                        string? Id = Context.Principal?.FindFirstValue(SessionClaim);
                        if (!Settings.Access.PasswordProtection || !Sessions.Use(Id, Passwords.Fingerprint()))
                        {
                            Context.RejectPrincipal();
                            await Context.HttpContext.SignOutAsync(Scheme);
                        }
                    },
                    //a relative address, built here: to the setup while no password is set, else to the sign-in with the
                    //page that was asked for (a GET only; a path of this web UI, checked again by the sign-in)
                    OnRedirectToLogin = Context =>
                    {
                        HttpRequest Request = Context.Request;
                        string Target = Passwords.State() == WebPasswordState.Missing ? "/setup" : "/signin";
                        if (Target == "/signin" && HttpMethods.IsGet(Request.Method) && Request.Path != "/")
                        {
                            Target += QueryString.Create("returnUrl", Request.Path + Request.QueryString);
                        }
                        Context.Response.Redirect(Target);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = Context =>
                    {
                        Context.Response.Redirect("/signin");
                        return Task.CompletedTask;
                    }
                };
            });
            //every page, and every address that is not named otherwise, needs the owner: a signed-in session, or no
            //password protection at all (Web.yaml). The sign-in, the setup, the stylesheet and the health answer say so
            //themselves (AllowAnonymous)
            Services.AddSingleton<IAuthorizationHandler, OwnerHandler>();
            Services.AddAuthorization(Options =>
            {
                AuthorizationPolicy Owner = new AuthorizationPolicyBuilder(Scheme).AddRequirements(new OwnerRequirement()).Build();
                Options.DefaultPolicy = Owner;
                Options.FallbackPolicy = Owner;
            });
            Services.AddAntiforgery(Options =>
            {
                Options.Cookie.Name = FormCookie(Instance);
                Options.Cookie.HttpOnly = true;
                Options.Cookie.SameSite = SameSiteMode.Strict;
                Options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                Options.Cookie.IsEssential = true;
                Options.Cookie.Path = "/";
                Options.SuppressXFrameOptionsHeader = true;
            });
            Services.Configure<FormOptions>(Options =>
            {
                Options.ValueCountLimit = 256;
                Options.KeyLengthLimit = 256;
                Options.ValueLengthLimit = 64 * 1024;
                Options.BufferBodyLengthLimit = MaxRequestBytes;
                Options.MultipartBodyLengthLimit = MaxRequestBytes;
            });
            Services.Configure<RouteOptions>(Options => Options.LowercaseUrls = true);
            //Razor Pages posts are checked for their antiforgery token by default; no page turns that off. A page answers
            //only through one of its own handlers (WebPageRules)
            Services.AddRazorPages(Options => Options.Conventions.ConfigureFilter(new WebPageRules())).AddApplicationPart(typeof(CampertronWebHost).Assembly);

            Services.AddRateLimiter(Options =>
            {
                Options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                Options.OnRejected = (Rejected, Token) =>
                {
                    Rejected.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(Settings.RateWindow.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return ValueTask.CompletedTask;
                };
                //every request of a client; and every POST with a password or a setup code of all clients together, so that
                //many addresses can't keep the computer hashing passwords. The health answer is not counted: a container's
                //health check must not fail because a browser was busy
                Options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    PartitionedRateLimiter.Create<HttpContext, string>(Context => Context.Request.Path == HealthPath
                        ? RateLimitPartition.GetNoLimiter("health")
                        : RateLimitPartition.GetFixedWindowLimiter(WebSecurity.ClientKey(Context), Key => Window(RequestsPerClient, Settings))),
                    PartitionedRateLimiter.Create<HttpContext, string>(Context => IsCredentialsPost(Context)
                        ? RateLimitPartition.GetFixedWindowLimiter("credentials", Key => Window(CredentialsInAll, Settings))
                        : RateLimitPartition.GetNoLimiter("other")));
                //the pages that take a password or a setup code: a client's POSTs to them
                Options.AddPolicy(CredentialsPolicy, Context => HttpMethods.IsPost(Context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter(WebSecurity.ClientKey(Context), Key => Window(CredentialsPerClient, Settings))
                    : RateLimitPartition.GetNoLimiter("read"));
            });

            List<System.Net.IPNetwork> Proxies = TrustedProxies(Settings.Access);
            WebApplication App = Builder.Build();
            App.Use(WebSecurity.Failures(Lines));
            App.Use(WebSecurity.Methods());
            if (Proxies.Count > 0)
            {
                //forwarded headers only from Web.yaml's trustedProxies (D81): the framework's own defaults, which trust this
                //computer's loopback addresses, are removed, so nothing is trusted that the file does not list. One proxy
                //(ForwardLimit 1): the address, the scheme and the host name as it says the browser sent them
                var Forwarded = new ForwardedHeadersOptions
                {
                    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
                    ForwardLimit = 1
                };
                Forwarded.KnownProxies.Clear();
                Forwarded.KnownIPNetworks.Clear();
                foreach (System.Net.IPNetwork Proxy in Proxies)
                {
                    Forwarded.KnownIPNetworks.Add(Proxy);
                }
                App.UseForwardedHeaders(Forwarded);
            }
            App.Use(WebSecurity.Hosts(Settings));
            App.Use(WebSecurity.Origins());
            App.UseRouting();
            App.UseRateLimiter();
            App.UseAuthentication();
            App.UseAuthorization();
            App.MapRazorPages();
            App.MapMethods("/assets/{name}", new[] { HttpMethods.Get, HttpMethods.Head }, WebAssets.Serve).AllowAnonymous();
            //the health answer (D85): whether the web UI answers, and nothing else. No sign-in (a container's health check
            //has no password) and no rate limit; the Host check and the headers apply as to every request
            App.MapMethods(HealthPath, new[] { HttpMethods.Get, HttpMethods.Head }, async Context =>
            {
                Context.Response.ContentType = "text/plain; charset=utf-8";
                if (!HttpMethods.IsHead(Context.Request.Method))
                {
                    await Context.Response.WriteAsync(HealthText, Context.RequestAborted);
                }
            }).AllowAnonymous();
            if (Settings.MapFailingPage)
            {
                App.MapGet("/test/fail", (HttpContext Context) => throw new InvalidOperationException("secret detail of a test failure, /a/path/that/must/not/show"));
            }
            return App;
        }

        private static FixedWindowRateLimiterOptions Window(int Permits, WebHostSettings Settings) => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Permits,
            Window = Settings.RateWindow,
            QueueLimit = 0,
            AutoReplenishment = true
        };

        private static bool IsCredentialsPost(HttpContext Context) =>
            HttpMethods.IsPost(Context.Request.Method)
            && (Context.Request.Path.StartsWithSegments("/signin") || Context.Request.Path.StartsWithSegments("/setup") || Context.Request.Path.StartsWithSegments("/account"));

        // Web.yaml's trustedProxies as networks; an item that is none was refused before the web UI started (WebMode)
        internal static List<System.Net.IPNetwork> TrustedProxies(WebConfig Access)
        {
            var Networks = new List<System.Net.IPNetwork>();
            foreach (string Item in Access.TrustedProxies ?? new List<string>())
            {
                if (WebHosts.TryProxy(Item, out System.Net.IPNetwork Network))
                {
                    Networks.Add(Network);
                }
            }
            return Networks;
        }

        // where the started web UI listens
        internal static List<IPEndPoint> EndPoints(WebApplication App)
        {
            var Found = new List<IPEndPoint>();
            foreach (string Address in App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? Array.Empty<string>())
            {
                if (!Uri.TryCreate(Address, UriKind.Absolute, out Uri? Parsed))
                {
                    continue;
                }
                //Kestrel names a localhost binding (both loopback addresses) by that word
                if (string.Equals(Parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    Found.Add(new IPEndPoint(IPAddress.Loopback, Parsed.Port));
                }
                else if (IPAddress.TryParse(Parsed.Host.Trim('[', ']'), out IPAddress? Ip))
                {
                    Found.Add(new IPEndPoint(Ip, Parsed.Port));
                }
            }
            return Found;
        }

        // signs a browser in: a new session, and the cookie that carries its identifier
        internal static Task SignIn(HttpContext Context, WebSessions Sessions, WebPasswordFile Passwords)
        {
            string Id = Sessions.Create(Passwords.Fingerprint());
            var Identity = new ClaimsIdentity(new[] { new Claim(SessionClaim, Id) }, Scheme);
            return Context.SignInAsync(Scheme, new ClaimsPrincipal(Identity), new AuthenticationProperties { IsPersistent = false });
        }

        // signs a browser out: its session ends here, and its cookie is removed
        internal static Task SignOut(HttpContext Context, WebSessions Sessions)
        {
            Sessions.Remove(Context.User.FindFirstValue(SessionClaim));
            return Context.SignOutAsync(Scheme);
        }
    }

    // the owner of this Campertron: who may use the web UI
    internal sealed class OwnerRequirement : IAuthorizationRequirement
    {
    }

    // without password protection everyone who reaches the web UI is its owner (Web.yaml's passwordProtection: false,
    // D81); with it, only a signed-in session is
    internal sealed class OwnerHandler : AuthorizationHandler<OwnerRequirement>
    {
        private readonly WebHostSettings _Settings;

        public OwnerHandler(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext Context, OwnerRequirement Requirement)
        {
            if (!_Settings.Access.PasswordProtection || Context.User.Identity?.IsAuthenticated == true)
            {
                Context.Succeed(Requirement);
            }
            return Task.CompletedTask;
        }
    }
}
