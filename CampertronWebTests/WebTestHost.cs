using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;

namespace CampertronWebTests
{
    // One web UI for a test: the real host (CampertronWebHost.Build after WebMode.Prepare, as the console starts it) on a
    // free loopback port, with a settings folder of its own in the temporary folder, its console lines collected, a clock
    // the test moves, and new password hashes at the lowest iteration count a record may have. Nothing here reaches
    // another computer, a real settings folder or the user's home folder.
    internal sealed class WebTestHost : IAsyncDisposable
    {
        internal const string Password = "correct horse battery";
        internal const int Iterations = WebPasswordHash.MinIterations;

        internal string Folder { get; private set; } = string.Empty;
        internal WebHostSettings Settings { get; private set; } = new WebHostSettings();
        internal WebApplication App { get; private set; } = null!;
        internal Uri Root { get; private set; } = null!;
        internal int Port => Root.Port;
        internal List<string> Lines { get; } = new();
        internal List<string> Warnings { get; } = new();
        internal List<string> Errors { get; } = new();
        internal DateTime Now { get; set; } = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        private bool OwnsFolder;

        internal static string NewFolder()
        {
            string Folder = Path.Join(Path.GetTempPath(), "CampertronWebTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            return Folder;
        }

        // WebYaml: the text of Web.yaml (null: no file). WithPassword: the password file exists already. Folder: a
        // settings folder of an earlier host (a restart), which is then not removed here
        internal static async Task<WebTestHost> Start(string? WebYaml = null, bool WithPassword = false, string? Folder = null, Action<WebHostSettings>? Configure = null)
        {
            var Host = new WebTestHost { Folder = Folder ?? NewFolder(), OwnsFolder = Folder == null };
            if (WebYaml != null)
            {
                File.WriteAllText(Path.Join(Host.Folder, "Web.yaml"), WebYaml);
            }
            if (WithPassword)
            {
                Assert.That(new WebPasswordFile(Host.Folder).Create(Password, Iterations), Is.True);
            }
            Host.Settings = Host.NewSettings();
            Configure?.Invoke(Host.Settings);
            string[]? Problem = WebMode.Prepare(Host.Settings, WebArguments.Parse(new[] { "--web", "--listen", "127.0.0.1" }));
            Assert.That(Problem, Is.Null, "the web UI's start was refused: " + string.Join(" | ", Problem ?? Array.Empty<string>()));
            Host.App = CampertronWebHost.Build(Host.Settings);
            await Host.App.StartAsync();
            IPEndPoint EndPoint = CampertronWebHost.EndPoints(Host.App).Single();
            Host.Root = new Uri("http://127.0.0.1:" + EndPoint.Port + "/");
            return Host;
        }

        internal WebHostSettings NewSettings() => new WebHostSettings
        {
            SettingsFolder = Folder,
            Port = 0,
            Line = Text => { lock (Lines) { Lines.Add(Text); } },
            Warning = Text => { lock (Warnings) { Warnings.Add(Text); } },
            Error = (Text, Details) => { lock (Errors) { Errors.Add(Text); Errors.AddRange(Details); } },
            UtcNow = () => Now,
            PasswordIterations = Iterations,
            MachineName = "campbox"
        };

        internal T Service<T>() where T : notnull => (T)App.Services.GetService(typeof(T))!;

        // the cookies' names: they end with the settings folder's own short name
        internal string SessionCookie => CampertronWebHost.SessionCookie(new WebKeyRepository(Folder).InstanceId());
        internal string FormCookie => CampertronWebHost.FormCookie(new WebKeyRepository(Folder).InstanceId());

        // the current setup code as the console shows it: a new one, written as the start writes it
        internal string NewSetupCode() => Service<WebSetupCode>().New();

        internal Browser Browser() => new Browser(Root);

        // a browser that is signed in with the test password
        internal async Task<Browser> SignedIn()
        {
            Browser Signed = Browser();
            await Signed.Get("/signin");
            WebPage Result = await Signed.Post("/signin", ("Password", Password));
            Assert.That(Result.Status, Is.EqualTo(HttpStatusCode.Redirect), "the test sign-in failed");
            return Signed;
        }

        public async ValueTask DisposeAsync()
        {
            if (App != null)
            {
                await App.StopAsync();
                await App.DisposeAsync();
            }
            if (OwnsFolder && Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }
    }

    // one answer of the web UI
    internal sealed class WebPage
    {
        internal HttpStatusCode Status { get; init; }
        internal string Body { get; init; } = string.Empty;
        internal HttpResponseMessage Response { get; init; } = null!;
        internal string? Location => Response.Headers.Location?.OriginalString;
        internal IEnumerable<string> SetCookies => Response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? Found) ? Found : Array.Empty<string>();
        internal string Header(string Name) =>
            Response.Headers.TryGetValues(Name, out IEnumerable<string>? Found) ? string.Join(", ", Found)
            : Response.Content.Headers.TryGetValues(Name, out IEnumerable<string>? Content) ? string.Join(", ", Content) : string.Empty;

        // the page as XML: every page of the web UI is well-formed (the layout says so), so no HTML parser is needed
        internal XDocument Xml()
        {
            using var Reader = XmlReader.Create(new StringReader(Body), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            return XDocument.Load(Reader);
        }

        internal string Text() => string.Join(" ", Xml().Descendants("main").Single().DescendantNodes().OfType<XText>().Select(Node => Node.Value.Trim()).Where(Value => Value.Length > 0));
    }

    // A browser's part in the tests: cookies kept between requests, no redirect followed, the antiforgery token of the
    // last page sent with the next form, and the Origin a browser sends with a form's POST
    internal sealed class Browser : IDisposable
    {
        private static readonly Regex TokenPattern = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
        private readonly HttpClientHandler Handler;
        private readonly HttpClient Client;
        internal Uri Root { get; }
        internal CookieContainer Cookies => Handler.CookieContainer;
        internal string? Token { get; set; }
        // the Origin sent with a POST; null sends none
        internal string? Origin { get; set; }
        internal Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal Browser(Uri Root)
        {
            this.Root = Root;
            Origin = Root.GetLeftPart(UriPartial.Authority);
            Handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer(), UseProxy = false };
            Client = new HttpClient(Handler) { BaseAddress = Root, Timeout = TimeSpan.FromSeconds(30) };
        }

        internal Task<WebPage> Get(string Path) => Send(new HttpRequestMessage(HttpMethod.Get, Path));

        // a form's POST, with the last page's antiforgery token unless a field of that name is given
        internal Task<WebPage> Post(string Path, params (string Name, string Value)[] Fields)
        {
            var Form = Fields.Select(Field => new KeyValuePair<string, string>(Field.Name, Field.Value)).ToList();
            if (Token != null && !Form.Any(Field => Field.Key == "__RequestVerificationToken"))
            {
                Form.Add(new KeyValuePair<string, string>("__RequestVerificationToken", Token));
            }
            var Request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new FormUrlEncodedContent(Form) };
            if (Origin != null)
            {
                Request.Headers.TryAddWithoutValidation("Origin", Origin);
            }
            return Send(Request);
        }

        internal async Task<WebPage> Send(HttpRequestMessage Request)
        {
            foreach (KeyValuePair<string, string> Header in Headers)
            {
                if (string.Equals(Header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    Request.Headers.Host = Header.Value;
                }
                else
                {
                    Request.Headers.TryAddWithoutValidation(Header.Key, Header.Value);
                }
            }
            HttpResponseMessage Response = await Client.SendAsync(Request);
            string Body = await Response.Content.ReadAsStringAsync();
            //a Secure cookie is one the browser got over HTTPS from a reverse proxy; the tests speak HTTP to the web UI as
            //that proxy does, so they keep such a cookie as the browser would
            if (Response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? Set))
            {
                foreach (string One in Set.Where(Text => Text.Contains("; secure", StringComparison.OrdinalIgnoreCase) && !Text.Contains("expires=Thu, 01 Jan 1970")))
                {
                    string Pair = One.Substring(0, One.IndexOf(';'));
                    Cookies.Add(Root, new Cookie(Pair.Substring(0, Pair.IndexOf('=')), Pair.Substring(Pair.IndexOf('=') + 1)));
                }
            }
            Match Found = TokenPattern.Match(Body);
            if (Found.Success)
            {
                Token = Found.Groups[1].Value;
            }
            return new WebPage { Status = Response.StatusCode, Body = Body, Response = Response };
        }

        internal string? Cookie(string Name) => Cookies.GetCookies(Root).FirstOrDefault(One => One.Name == Name)?.Value;

        public void Dispose()
        {
            Client.Dispose();
            Handler.Dispose();
        }
    }

    // A request written byte for byte over a socket, for what HttpClient would change or could not do: an address with
    // "..", which it tidies away, and a request that only says how large its body is. Reads the answer's head and as much
    // of its body as the answer says it has, so it does not depend on when the web UI closes the connection
    internal static class RawRequest
    {
        internal static async Task<string> Send(int Port, string Head)
        {
            using var Client = new System.Net.Sockets.TcpClient();
            await Client.ConnectAsync(IPAddress.Loopback, Port);
            using System.Net.Sockets.NetworkStream Stream = Client.GetStream();
            await Stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(Head));
            using var Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var Answer = new MemoryStream();
            byte[] Buffer = new byte[16 * 1024];
            while (true)
            {
                string Text = System.Text.Encoding.UTF8.GetString(Answer.GetBuffer(), 0, (int)Answer.Length);
                int HeadEnd = Text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (HeadEnd >= 0)
                {
                    Match Length = Regex.Match(Text.Substring(0, HeadEnd), "(?im)^Content-Length: (\\d+)\\r?$");
                    if (Length.Success && Answer.Length >= System.Text.Encoding.UTF8.GetByteCount(Text.Substring(0, HeadEnd + 4)) + long.Parse(Length.Groups[1].Value))
                    {
                        return Text;
                    }
                }
                int Read;
                try
                {
                    Read = await Stream.ReadAsync(Buffer, Timeout.Token);
                }
                catch (IOException)
                {
                    //the web UI closed the connection after its answer
                    Read = 0;
                }
                if (Read == 0)
                {
                    return System.Text.Encoding.UTF8.GetString(Answer.GetBuffer(), 0, (int)Answer.Length);
                }
                Answer.Write(Buffer, 0, Read);
            }
        }

        internal static Task<string> Get(int Port, string Target) =>
            Send(Port, "GET " + Target + " HTTP/1.1\r\nHost: 127.0.0.1:" + Port + "\r\nConnection: close\r\n\r\n");
    }
}
