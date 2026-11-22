using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ntfy's publish and message (decision D67, business-rules §28): one JSON POST to the server's root URL, the topic only
    // in the body, "Authorization: Bearer" only with a token, no redirect followed, every answer but ntfy's 2xx message a
    // failure of its own kind, bounded by timeoutSeconds, and no failure that names the server, the topic, the token or a
    // response body. The message comes from the search's results only and stays below ntfy's 4,096 bytes. A recording
    // handler answers most cases; a loopback HTTP server answers the rest (the NoLiveNetwork guard allows loopback only).
    // Unique canary topics and tokens are searched for in everything a failure says.
    public class NtfyPublishTests
    {
        private string Root = string.Empty;
        private string Topic = string.Empty;
        private string Token = string.Empty;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronNtfyPublishTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Topic = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            Token = "tk_cnry" + Guid.NewGuid().ToString("N");
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalEnvironment = NtfyToken.GetEnvironment;
            Environment_.Clear();
            NtfyToken.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
        }

        [TearDown]
        public void TearDown()
        {
            NtfyPublisher.CreateHandler = OriginalHandler;
            NtfyToken.GetEnvironment = OriginalEnvironment;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // a handler that records each request and answers as told; HttpClient.Send calls the synchronous Send
        private sealed class Recording : HttpMessageHandler
        {
            internal Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Answer = (R, C) => Ok();
            internal readonly List<(HttpMethod Method, Uri? Uri, string? Authorization, string? ContentType, byte[] Body)> Seen = new();
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                byte[] Body = Request.Content?.ReadAsByteArrayAsync(Cancel).GetAwaiter().GetResult() ?? Array.Empty<byte>();
                Seen.Add((Request.Method, Request.RequestUri, Request.Headers.Authorization?.ToString(), Request.Content?.Headers.ContentType?.ToString(), Body));
                return Answer(Request, Cancel);
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        private static HttpResponseMessage Ok(string Json = "{\"id\":\"abc123\",\"time\":1,\"event\":\"message\",\"topic\":\"t\",\"message\":\"m\"}") =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };

        private Recording Use()
        {
            var Handler = new Recording();
            NtfyPublisher.CreateHandler = () => Handler;
            return Handler;
        }

        private NtfyConfig Config(string Server = "https://ntfy.example.invalid", string? Extra = null) =>
            Yaml.LoadSettingsText<NtfyConfig>("enabled: true\nserver: " + Server + "\ntopic: " + Topic + "\n" + (Extra ?? ""));

        private static NtfyMessage Message() => new NtfyMessage("3 new openings at WATCHMAN CAMPGROUND", "Trip\nline", "https://www.recreation.gov/camping/campgrounds/232445", 3);

        private void AssertNoValue(Exception Failure)
        {
            string Shown = Failure.Message + "\n" + string.Join("\n", ConsoleUi.DescribeFailure(Failure));
            Assert.That(Shown, Does.Not.Contain(Topic).And.Not.Contain(Token).And.Not.Contain("ntfy.example.invalid").And.Not.Contain(Root));
            Assert.That(Shown, Does.Not.Contain("abc123").And.Not.Contain("SECRET-BODY"));
        }

        // ---- the request ----

        [Test]
        public void APublish_IsOneJsonPostToTheServersRoot_WithTheTopicOnlyInTheBody()
        {
            Recording Handler = Use();
            NtfyPublisher.Publish(Config(Extra: "priority: high\ntags:\n- tent\n- zion\n"), Message());

            Assert.That(Handler.Seen, Has.Count.EqualTo(1));
            var Request = Handler.Seen[0];
            Assert.That((Request.Method, Request.Uri!.ToString(), Request.ContentType), Is.EqualTo((HttpMethod.Post, "https://ntfy.example.invalid/", "application/json; charset=utf-8")));
            Assert.That(Request.Uri.ToString(), Does.Not.Contain(Topic));
            Assert.That(Request.Authorization, Is.Null, "no token configured: no Authorization header");
            using JsonDocument Json = JsonDocument.Parse(Request.Body);
            JsonElement R = Json.RootElement;
            Assert.That(R.GetProperty("topic").GetString(), Is.EqualTo(Topic));
            Assert.That(R.GetProperty("title").GetString(), Is.EqualTo("3 new openings at WATCHMAN CAMPGROUND"));
            Assert.That(R.GetProperty("message").GetString(), Is.EqualTo("Trip\nline"));
            Assert.That(R.GetProperty("priority").GetInt32(), Is.EqualTo(4));
            Assert.That(R.GetProperty("tags").EnumerateArray().Select(T => T.GetString()), Is.EqualTo(new[] { "tent", "zion" }));
            Assert.That(R.GetProperty("click").GetString(), Is.EqualTo("https://www.recreation.gov/camping/campgrounds/232445"));
            Assert.That(R.EnumerateObject().Select(P => P.Name), Is.EquivalentTo(new[] { "topic", "title", "message", "priority", "tags", "click" }), "nothing else: no attachment, delay, action, e-mail or call");
            Assert.That(() => new UTF8Encoding(false, true).GetString(Request.Body), Throws.Nothing, "strict UTF-8 JSON");
        }

        [Test]
        public void ATokenIsSentAsBearer_FromItsSource_AndNeverInTheUrl()
        {
            Recording Handler = Use();
            string TokenFile = Path.Join(Root, "ntfy_token");
            File.WriteAllText(TokenFile, Token + "\n");
            NtfyPublisher.Publish(Config(Extra: "tokenFile: '" + TokenFile + "'\n"), Message());
            Environment_[NtfyToken.EnvironmentVariable] = Token + "env";
            NtfyPublisher.Publish(Config(), Message());

            Assert.That(Handler.Seen.Select(S => S.Authorization), Is.EqualTo(new[] { "Bearer " + Token, "Bearer " + Token + "env" }));
            Assert.That(Handler.Seen.Select(S => S.Uri!.ToString()), Has.All.Not.Contain(Token));
            Assert.That(Handler.Seen.Select(S => Encoding.UTF8.GetString(S.Body)), Has.All.Not.Contain(Token), "never in the body");
        }

        [Test]
        public void ATokenSourceThatCantBeUsed_StopsThePublishBeforeAnyRequest_NeverWithoutTheToken()
        {
            Recording Handler = Use();
            Assert.Throws<NtfyTokenException>(() => NtfyPublisher.Publish(Config(Extra: "tokenFile: '" + Path.Join(Root, "absent") + "'\n"), Message()));
            Environment_[NtfyToken.EnvironmentVariable] = "";
            Assert.Throws<NtfyTokenException>(() => NtfyPublisher.Publish(Config(), Message()));
            Assert.That(Handler.Seen, Is.Empty, "nothing was published, with or without a token");
        }

        [Test]
        public void EmptyTagsAndNoClick_AreLeftOut()
        {
            Recording Handler = Use();
            NtfyPublisher.Publish(Config(Extra: "tags: []\n"), new NtfyMessage("t", "b", null, 1));
            using JsonDocument Json = JsonDocument.Parse(Handler.Seen[0].Body);
            Assert.That(Json.RootElement.EnumerateObject().Select(P => P.Name), Is.EquivalentTo(new[] { "topic", "title", "message", "priority" }));
            Assert.That(Json.RootElement.GetProperty("priority").GetInt32(), Is.EqualTo(3), "the default priority");
        }

        [Test]
        public void TheProductionHandler_FollowsNoRedirect_AndTheTimeoutIsBounded()
        {
            HttpMessageHandler Handler = OriginalHandler();
            Assert.That(Handler, Is.InstanceOf<SocketsHttpHandler>());
            Assert.That(((SocketsHttpHandler)Handler).AllowAutoRedirect, Is.False);
            Assert.That(((SocketsHttpHandler)Handler).UseCookies, Is.False);
            Handler.Dispose();
        }

        // ---- the answers ----

        [TestCase(301, "Redirect")]
        [TestCase(302, "Redirect")]
        [TestCase(307, "Redirect")]
        [TestCase(308, "Redirect")]
        [TestCase(400, "ClientError")]
        [TestCase(401, "Unauthorized")]
        [TestCase(403, "Forbidden")]
        [TestCase(404, "NotFound")]
        [TestCase(413, "TooLarge")]
        [TestCase(429, "RateLimited")]
        [TestCase(500, "ServerError")]
        [TestCase(502, "ServerError")]
        [TestCase(507, "ServerError")]
        [TestCase(102, "UnexpectedStatus")]
        public void EveryAnswerButNtfysMessage_IsAFailureOfItsKind(int Status, string Failure)
        {
            Recording Handler = Use();
            Handler.Answer = (R, C) =>
            {
                var Response = new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent("{\"error\":\"SECRET-BODY " + Topic + "\"}") };
                if (Status >= 300 && Status < 400)
                {
                    Response.Headers.Location = new Uri("https://elsewhere.example.invalid/" + Topic);
                }
                return Response;
            };
            NtfyException Thrown = Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(), Message()))!;
            Assert.That((Thrown.Failure, Thrown.Status), Is.EqualTo((Enum.Parse<NtfyFailure>(Failure), (int?)Status)));
            Assert.That(Thrown.Message, Does.Contain("(HTTP " + Status + ")"));
            Assert.That(Handler.Seen, Has.Count.EqualTo(1), "one request, no retry and no redirect followed");
            AssertNoValue(Thrown);
        }

        [TestCase("not json")]
        [TestCase("<html><body>Sign in</body></html>")]
        [TestCase("{}")]
        [TestCase("{\"id\":\"\"}")]
        [TestCase("{\"id\":5}")]
        [TestCase("[\"id\"]")]
        [TestCase("")]
        public void A2xxThatIsNotNtfysMessage_IsNotADelivery(string Body)
        {
            Recording Handler = Use();
            Handler.Answer = (R, C) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) };
            NtfyException Thrown = Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(), Message()))!;
            Assert.That(Thrown.Failure, Is.EqualTo(NtfyFailure.MalformedResponse));
            AssertNoValue(Thrown);
        }

        [Test]
        public void AnOversizedAnswer_IsNotReadWhole()
        {
            Recording Handler = Use();
            Handler.Answer = (R, C) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"id\":\"x\",\"pad\":\"" + new string('a', NtfyPublisher.MaxResponseBytes) + "\"}")) };
            Assert.That(Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(), Message()))!.Failure, Is.EqualTo(NtfyFailure.MalformedResponse));
        }

        [Test]
        public void FailuresWithoutAnAnswer_AreClassified_WithoutTheirMessages()
        {
            (Exception Error, NtfyFailure Expected)[] Cases =
            {
                (new HttpRequestException("No such host: ntfy.example.invalid " + Topic, new SocketException((int)SocketError.HostNotFound)), NtfyFailure.Dns),
                (new HttpRequestException("Connection refused (ntfy.example.invalid:443)", new SocketException((int)SocketError.ConnectionRefused)), NtfyFailure.ConnectionRefused),
                (new HttpRequestException("reset", new SocketException((int)SocketError.ConnectionReset)), NtfyFailure.Network),
                (new HttpRequestException("SSL " + Token, new AuthenticationException("The remote certificate is invalid " + Topic)), NtfyFailure.Tls),
                (new HttpRequestException("broken"), NtfyFailure.Network),
                (new InvalidOperationException("odd " + Topic), NtfyFailure.Unexpected),
            };
            foreach ((Exception Error, NtfyFailure Expected) in Cases)
            {
                Recording Handler = Use();
                Handler.Answer = (R, C) => throw Error;
                NtfyException Thrown = Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(), Message()))!;
                Assert.That(Thrown.Failure, Is.EqualTo(Expected), Error.Message);
                AssertNoValue(Thrown);
            }
        }

        [Test]
        public void ATimeout_EndsThePublish_WithinTimeoutSeconds()
        {
            Recording Handler = Use();
            Handler.Answer = (R, C) => { Task.Delay(TimeSpan.FromSeconds(30), C).GetAwaiter().GetResult(); return Ok(); };
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            NtfyException Thrown = Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(Extra: "timeoutSeconds: 1\n"), Message()))!;
            Assert.That((Thrown.Failure, Thrown.TimeoutSeconds), Is.EqualTo((NtfyFailure.Timeout, 1)));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(Thrown.Message, Does.Contain("within 1 seconds"));
        }

        [Test]
        public void ACancellation_EndsThePublish_AsCanceled()
        {
            Recording Handler = Use();
            using var Cancel = new CancellationTokenSource();
            Handler.Answer = (R, C) => { Cancel.Cancel(); C.ThrowIfCancellationRequested(); return Ok(); };
            Assert.That(Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(), Message(), Cancel.Token))!.Failure, Is.EqualTo(NtfyFailure.Canceled));
        }

        [Test]
        public void AServerOrTopicThatCantBeUsed_IsRefusedBeforeAnyRequest()
        {
            Recording Handler = Use();
            Assert.That(Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(Server: "https://ntfy.example.invalid/" + Topic), Message()))!.Failure, Is.EqualTo(NtfyFailure.ServerInvalid));
            Assert.That(Handler.Seen, Is.Empty);
        }

        // ---- a real loopback server ----

        // a one-request HTTP server on 127.0.0.1: records the raw request and answers with the given bytes (or nothing)
        private sealed class Loopback : IDisposable
        {
            private readonly TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            internal readonly List<string> Requests = new();
            internal int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;
            internal Loopback(Func<string, string?> Answer)
            {
                Listener.Start();
                Task.Run(async () =>
                {
                    while (true)
                    {
                        TcpClient Client;
                        try
                        {
                            Client = await Listener.AcceptTcpClientAsync();
                        }
                        catch (Exception)
                        {
                            return;
                        }
                        _ = Task.Run(() =>
                        {
                            using (Client)
                            {
                                NetworkStream Stream = Client.GetStream();
                                var Raw = new List<byte>();
                                var Buffer = new byte[8192];
                                int Length = -1;
                                while (true)
                                {
                                    int Read;
                                    try { Read = Stream.Read(Buffer, 0, Buffer.Length); } catch (Exception) { return; }
                                    if (Read <= 0) { break; }
                                    Raw.AddRange(Buffer.Take(Read));
                                    string Text = Encoding.UTF8.GetString(Raw.ToArray());
                                    int End = Text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                    if (End >= 0)
                                    {
                                        var Match = System.Text.RegularExpressions.Regex.Match(Text, "Content-Length: *([0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        Length = Match.Success ? int.Parse(Match.Groups[1].Value) : 0;
                                        if (Raw.Count >= Encoding.UTF8.GetByteCount(Text.Substring(0, End + 4)) + Length) { break; }
                                    }
                                }
                                string Request = Encoding.UTF8.GetString(Raw.ToArray());
                                lock (Requests) { Requests.Add(Request); }
                                string? Reply = Answer(Request);
                                if (Reply == null)
                                {
                                    Thread.Sleep(TimeSpan.FromSeconds(15));
                                    return;
                                }
                                byte[] Bytes = Encoding.UTF8.GetBytes(Reply);
                                try { Stream.Write(Bytes, 0, Bytes.Length); } catch (Exception) { }
                            }
                        });
                    }
                });
            }
            public void Dispose() => Listener.Stop();
        }

        private static string Http(int Status, string Body, string Extra = "") =>
            "HTTP/1.1 " + Status + " X\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(Body) + "\r\nConnection: close\r\n" + Extra + "\r\n" + Body;

        [Test]
        public void OverRealHttp_AuthenticatedPublishSucceeds_AndARedirectIsNeverFollowedWithTheToken()
        {
            using var Elsewhere = new Loopback(R => Http(200, "{\"id\":\"other\"}"));
            using var Server = new Loopback(R => R.StartsWith("POST / ", StringComparison.Ordinal) && R.Contains("first-call")
                ? Http(200, "{\"id\":\"ok1\",\"event\":\"message\"}")
                : Http(302, "", "Location: http://127.0.0.1:" + Elsewhere.Port + "/\r\n"));
            Environment_[NtfyToken.EnvironmentVariable] = Token;
            NtfyConfig Ntfy = Config(Server: "http://127.0.0.1:" + Server.Port, Extra: "timeoutSeconds: 5\n");

            NtfyPublisher.Publish(Ntfy, new NtfyMessage("first-call", "body", null, 1));
            NtfyException Redirected = Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Ntfy, new NtfyMessage("second", "body", null, 1)))!;

            Assert.That((Redirected.Failure, Redirected.Status), Is.EqualTo((NtfyFailure.Redirect, (int?)302)));
            Assert.That(Server.Requests, Has.Count.EqualTo(2));
            Assert.That(Server.Requests[0], Does.StartWith("POST / HTTP/1.1\r\n").And.Contain("Authorization: Bearer " + Token).And.Contain("\"topic\":\"" + Topic + "\""));
            Thread.Sleep(300);
            Assert.That(Elsewhere.Requests, Is.Empty, "the redirect's target never got a request, so never the token");
            AssertNoValue(Redirected);
        }

        [Test]
        public void OverRealHttp_ARefusedConnectionAndASilentServer_AreBounded()
        {
            var Closed = new TcpListener(IPAddress.Loopback, 0);
            Closed.Start();
            int ClosedPort = ((IPEndPoint)Closed.LocalEndpoint).Port;
            Closed.Stop();
            Assert.That(Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(Server: "http://127.0.0.1:" + ClosedPort), Message()))!.Failure, Is.EqualTo(NtfyFailure.ConnectionRefused));

            using var Silent = new Loopback(R => null);
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            NtfyException Thrown = Assert.Throws<NtfyException>(() => NtfyPublisher.Publish(Config(Server: "http://127.0.0.1:" + Silent.Port, Extra: "timeoutSeconds: 1\n"), Message()))!;
            Assert.That(Thrown.Failure, Is.EqualTo(NtfyFailure.Timeout));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        // ---- the message ----

        private static readonly DateTime Friday = new DateTime(2026, 11, 27);

        [Test]
        public void TheMessage_ComesFromTheSearchsResults()
        {
            ConsoleConfig.ConsoleConfigItem Search = OutputStreams.Search("Zion tents", OutputStreams.Header("Zion tents"),
                OutputStreams.Month(new DateTime(2026, 11, 1),
                    OutputStreams.Hit("343", "A023", Friday), OutputStreams.Hit("440", "B025", Friday), OutputStreams.Hit("343", "A023", Friday.AddDays(1))));

            NtfyMessage Message_ = NtfyMessages.Build(Search)!;

            Assert.That(Message_.Title, Is.EqualTo("3 new openings at WATCHMAN CAMPGROUND"));
            Assert.That(Message_.Body, Is.EqualTo("Zion tents\nWATCHMAN CAMPGROUND, Zion National Park\n3 openings at 2 sites on 2 dates.\n\nFri 2026-11-27: A023, B025\nSat 2026-11-28: A023"));
            Assert.That(Message_.Click, Is.EqualTo("https://www.recreation.gov/camping/campgrounds/232445"));
            Assert.That(Message_.Openings, Is.EqualTo(3));
        }

        [Test]
        public void ASearchWithoutOpenings_HasNoMessage_AndAnIdThatIsNotANumberHasNoClick()
        {
            Assert.That(NtfyMessages.Build(OutputStreams.Search("Empty", OutputStreams.Header("Empty"), OutputStreams.EmptyMonth(new DateTime(2026, 12, 1)))), Is.Null);
            ConsoleConfig.ConsoleConfigItem Odd = OutputStreams.Search("Odd", OutputStreams.Header("Odd", CampgroundId: "23/../x"), OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("1", "A1", Friday)));
            Assert.That(NtfyMessages.Build(Odd)!.Click, Is.Null);
        }

        [Test]
        public void AMessageWithManyOpenings_IsCutDeterministically_BelowNtfysLimit()
        {
            var Hits = new List<AvailabilityData>();
            for (int Day = 0; Day < 300; Day++)
            {
                for (int Site = 0; Site < 14; Site++)
                {
                    Hits.Add(OutputStreams.Hit((1000 + Site).ToString(), "Site with a long name number " + Site, Friday.AddDays(Day)));
                }
            }
            ConsoleConfig.ConsoleConfigItem Search = OutputStreams.Search("Big", OutputStreams.Header("Big"), OutputStreams.Month(new DateTime(2026, 11, 1), Hits.ToArray()));
            NtfyMessage First = NtfyMessages.Build(Search)!;
            NtfyMessage Second = NtfyMessages.Build(Search)!;

            Assert.That(Encoding.UTF8.GetByteCount(First.Body), Is.LessThanOrEqualTo(NtfyMessages.MaxBodyBytes));
            Assert.That(First.Body, Is.EqualTo(Second.Body), "the same results give the same message");
            Assert.That(First.Body, Does.Match(@"\n…and \d+ more dates; recreation.gov shows them all\.$"));
            Assert.That(First.Body, Does.Contain(" and 4 more"), "at most ten sites on a date's line");
            Assert.That(First.Title.Length, Is.LessThanOrEqualTo(NtfyMessages.MaxTitleChars));
            Assert.That(NtfyPublisher.Json(Config(), First).Length, Is.LessThan(5000));
        }

        [Test]
        public void ALongCampgroundName_IsCutInTheTitle()
        {
            ConsoleConfig.ConsoleConfigItem Search = OutputStreams.Search("Long", OutputStreams.Header("Long", Campground: new string('W', 400)), OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("1", "A1", Friday)));
            Assert.That(NtfyMessages.Build(Search)!.Title, Has.Length.EqualTo(NtfyMessages.MaxTitleChars).And.EndsWith("…"));
        }
    }
}
