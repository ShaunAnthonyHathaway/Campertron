using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using static ConsoleConfig;

namespace CampertronLibrary.function.Base
{
    // one ntfy notification of one search's new openings (decision D67, business-rules §28): a title, a plain-text body and,
    // when the campground's page is known, the click-through URL. Built only from the search's results (ResultsHtml's
    // model, the email page's authority), never from settings: no password, token, topic, path or other value of a file
    internal sealed class NtfyMessage
    {
        internal NtfyMessage(string Title, string Body, string? Click, int Openings)
        {
            this.Title = Title;
            this.Body = Body;
            this.Click = Click;
            this.Openings = Openings;
        }
        internal string Title { get; }
        internal string Body { get; }
        internal string? Click { get; }
        internal int Openings { get; }
    }

    internal static class NtfyMessages
    {
        // ntfy turns a message over 4,096 bytes into an attachment (https://docs.ntfy.sh/publish/, Limitations): the body
        // stays below that, with room to spare; the title, limited to 1 KB there, is cut far below it
        internal const int MaxBodyBytes = 3800;
        internal const int MaxTitleChars = 200;
        // the campground's page on recreation.gov (the email's booking links open single campsites)
        internal const string CampgroundUrlPrefix = "https://www.recreation.gov/camping/campgrounds/";

        // the notification of one search, or null when its results hold no opening (ntfy is told only about openings;
        // the email of D57/TD-06 is sent for every search of a check that has news)
        internal static NtfyMessage? Build(ConsoleConfigItem Search)
        {
            ResultsHtml.SearchModel Model = ResultsHtml.Build(Search, 1);
            if (Model.Entries == 0)
            {
                return null;
            }
            OutputMetadata? Header = Model.Header;
            string Campground = !string.IsNullOrWhiteSpace(Header?.CampgroundName) ? Header!.CampgroundName!.Trim()
                : !string.IsNullOrWhiteSpace(Header?.CampgroundID) ? "campground " + Header!.CampgroundID!.Trim() : "the campground";
            string Title = Count(Model.Entries, "new opening", "new openings") + " at " + Campground;
            if (Title.Length > MaxTitleChars)
            {
                Title = Title.Substring(0, MaxTitleChars - 1) + "…";
            }

            // every opening as a date and a site, from the model's date groups and consecutive-night cards, by date
            var ByDate = new SortedDictionary<DateTime, SortedSet<string>>();
            void Add(OutputMetadata Hit)
            {
                if (Hit.Date is DateTime Date)
                {
                    string Site = !string.IsNullOrWhiteSpace(Hit.CampsiteName) ? Hit.CampsiteName!.Trim() : Hit.CampsiteID ?? "?";
                    if (!ByDate.TryGetValue(Date.Date, out SortedSet<string>? Sites))
                    {
                        Sites = new SortedSet<string>(StringComparer.Ordinal);
                        ByDate.Add(Date.Date, Sites);
                    }
                    Sites.Add(Site);
                }
            }
            foreach (ResultsHtml.Block Block in Model.Blocks)
            {
                switch (Block)
                {
                    case ResultsHtml.MonthBlock Month:
                        foreach (ResultsHtml.DateGroup Group in Month.Groups)
                        {
                            Group.Hits.ForEach(Add);
                        }
                        break;
                    case ResultsHtml.DateGroup Group:
                        Group.Hits.ForEach(Add);
                        break;
                    case ResultsHtml.SiteCard Card:
                        Card.Dates.ForEach(Add);
                        break;
                }
            }

            var Lines = new List<string> { Model.Name };
            string Place = string.Join(", ", new[] { Header?.CampgroundName, Header?.ParkName }.Where(P => !string.IsNullOrWhiteSpace(P)).Select(P => P!.Trim()));
            if (Place.Length > 0)
            {
                Lines.Add(Place);
            }
            string Summary = Count(Model.Entries, "opening", "openings") + " at " + Count(Model.Sites, "site", "sites") + " on " + Count(Model.Dates, "date", "dates");
            if (Header != null && Header.ConsecutiveDays > 1)
            {
                Summary += ", in runs of at least " + Header.ConsecutiveDays.ToString(CultureInfo.InvariantCulture) + " nights";
            }
            Lines.Add(Summary + ".");
            Lines.Add(string.Empty);
            string Text = string.Join("\n", Lines);

            // the dates, while they fit; then one line that says how many are left (deterministic: by date, sites by name)
            var DateLines = ByDate.Select(P => P.Key.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture) + ": " + SitesText(P.Value)).ToList();
            int Shown = 0;
            foreach (string Line in DateLines)
            {
                string More = "\n…and " + Count(DateLines.Count - Shown - 1, "more date", "more dates") + "; recreation.gov shows them all.";
                bool Last = Shown == DateLines.Count - 1;
                if (Bytes(Text + "\n" + Line + (Last ? string.Empty : More)) > MaxBodyBytes)
                {
                    break;
                }
                Text += "\n" + Line;
                Shown++;
            }
            if (Shown < DateLines.Count)
            {
                Text += "\n…and " + Count(DateLines.Count - Shown, "more date", "more dates") + "; recreation.gov shows them all.";
            }

            string? CampgroundId = Header?.CampgroundID?.Trim();
            string? Click = !string.IsNullOrEmpty(CampgroundId) && CampgroundId.All(char.IsAsciiDigit) ? CampgroundUrlPrefix + CampgroundId : null;
            return new NtfyMessage(Title, Text, Click, Model.Entries);
        }

        // at most ten site names on a date's line, then how many more
        private static string SitesText(SortedSet<string> Sites)
        {
            List<string> Names = Sites.Take(10).ToList();
            return string.Join(", ", Names) + (Sites.Count > 10 ? " and " + (Sites.Count - 10).ToString(CultureInfo.InvariantCulture) + " more" : string.Empty);
        }

        private static int Bytes(string Text) => Encoding.UTF8.GetByteCount(Text);

        private static string Count(int Value, string Singular, string Plural) =>
            Value.ToString(CultureInfo.InvariantCulture) + " " + (Value == 1 ? Singular : Plural);
    }

    // why a publish failed (business-rules §28)
    internal enum NtfyFailure
    {
        ServerInvalid,
        Dns,
        ConnectionRefused,
        Network,
        Tls,
        Timeout,
        Canceled,
        Redirect,
        Unauthorized,
        Forbidden,
        NotFound,
        TooLarge,
        RateLimited,
        ClientError,
        ServerError,
        UnexpectedStatus,
        MalformedResponse,
        Unexpected
    }

    // a publish that failed: its kind and, for an HTTP answer, the status code. The message is fixed text and the status
    // number only: never the server, the topic, the token, a response body or an inner exception's message
    internal sealed class NtfyException : Exception
    {
        internal NtfyException(NtfyFailure Failure, int? Status = null, int TimeoutSeconds = 0, string? ExceptionType = null) : base(NtfyPublisher.Describe(Failure, Status, TimeoutSeconds, ExceptionType)[0])
        {
            this.Failure = Failure;
            this.Status = Status;
            this.TimeoutSeconds = TimeoutSeconds;
            this.ExceptionType = ExceptionType;
        }
        internal NtfyFailure Failure { get; }
        internal int? Status { get; }
        internal int TimeoutSeconds { get; }
        internal string? ExceptionType { get; }
    }

    // ntfy's publish (decision D67, business-rules §28): one POST of a JSON object to the server's root URL
    // (https://docs.ntfy.sh/publish/, "Publish as JSON"), the topic in the body, "Authorization: Bearer <token>" when a
    // token is configured. Bounded: timeoutSeconds for the whole request, the answer read up to 64 KiB. Redirects are
    // never followed (the Authorization header never reaches another address); a 2xx answer is a delivery only when it is
    // ntfy's message (a JSON object with an id); every other answer is a failure of its own kind. Nothing is retried here:
    // the unattended monitoring tells its next check (D67's delivery rule). Nothing is logged.
    internal static class NtfyPublisher
    {
        internal const int MaxResponseBytes = 64 * 1024;

        // the HTTP handler of a publish: no redirects, no cookies; replaceable so tests can answer without a network
        internal static Func<HttpMessageHandler> CreateHandler { get; set; } = () => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(NtfyEndpoint.MaxTimeoutSeconds)
        };

        // the JSON object ntfy receives
        internal static byte[] Json(NtfyConfig Config, NtfyMessage Message)
        {
            using var Buffer = new MemoryStream();
            using (var Writer = new Utf8JsonWriter(Buffer))
            {
                Writer.WriteStartObject();
                Writer.WriteString("topic", Config.Topic);
                Writer.WriteString("title", Message.Title);
                Writer.WriteString("message", Message.Body);
                Writer.WriteNumber("priority", (int)Config.Priority);
                if (Config.EffectiveTags.Count > 0)
                {
                    Writer.WriteStartArray("tags");
                    foreach (string Tag in Config.EffectiveTags)
                    {
                        Writer.WriteStringValue(Tag);
                    }
                    Writer.WriteEndArray();
                }
                if (Message.Click != null)
                {
                    Writer.WriteString("click", Message.Click);
                }
                Writer.WriteEndObject();
            }
            return Buffer.ToArray();
        }

        // publishes one message, or throws NtfyException (or NtfyTokenException for a token source that can't be used,
        // before any connection)
        internal static void Publish(NtfyConfig Config, NtfyMessage Message, CancellationToken Cancel = default)
        {
            if (!NtfyEndpoint.TryServer(Config.Server, out Uri? Root) || !NtfyEndpoint.IsValidTopic(Config.Topic))
            {
                throw new NtfyException(NtfyFailure.ServerInvalid);
            }
            string? Token = NtfyToken.ForPublishing(Config);
            int Seconds = Math.Clamp(Config.TimeoutSeconds, NtfyEndpoint.MinTimeoutSeconds, NtfyEndpoint.MaxTimeoutSeconds);
            using var Client = new HttpClient(CreateHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(Seconds) };
            using var Request = new HttpRequestMessage(HttpMethod.Post, Root);
            Request.Content = new ByteArrayContent(Json(Config, Message));
            Request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            Request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            Request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Campertron", null));
            if (Token != null)
            {
                Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            }
            HttpResponseMessage Response;
            try
            {
                Response = Client.Send(Request, HttpCompletionOption.ResponseHeadersRead, Cancel);
            }
            catch (Exception ex)
            {
                throw Classify(ex, Cancel, Seconds);
            }
            using (Response)
            {
                int Status = (int)Response.StatusCode;
                if (Status >= 300 && Status < 400)
                {
                    throw new NtfyException(NtfyFailure.Redirect, Status);
                }
                if (Status < 200 || Status >= 300)
                {
                    throw new NtfyException(Status switch
                    {
                        401 => NtfyFailure.Unauthorized,
                        403 => NtfyFailure.Forbidden,
                        404 => NtfyFailure.NotFound,
                        413 => NtfyFailure.TooLarge,
                        429 => NtfyFailure.RateLimited,
                        >= 400 and < 500 => NtfyFailure.ClientError,
                        >= 500 and < 600 => NtfyFailure.ServerError,
                        _ => NtfyFailure.UnexpectedStatus
                    }, Status);
                }
                byte[] Body;
                try
                {
                    Body = ReadBounded(Response, Cancel);
                }
                catch (Exception ex)
                {
                    throw Classify(ex, Cancel, Seconds);
                }
                if (!IsNtfyMessage(Body))
                {
                    throw new NtfyException(NtfyFailure.MalformedResponse, Status);
                }
            }
        }

        // ntfy answers a publish with the message it stored: a JSON object with its string id
        internal static bool IsNtfyMessage(byte[] Body)
        {
            try
            {
                using JsonDocument Document = JsonDocument.Parse(Body);
                return Document.RootElement.ValueKind == JsonValueKind.Object &&
                       Document.RootElement.TryGetProperty("id", out JsonElement Id) && Id.ValueKind == JsonValueKind.String && Id.GetString()!.Length > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static byte[] ReadBounded(HttpResponseMessage Response, CancellationToken Cancel)
        {
            using Stream Content = Response.Content.ReadAsStream(Cancel);
            byte[] Buffer = new byte[MaxResponseBytes + 1];
            int Count = 0;
            int Read;
            while (Count < Buffer.Length && (Read = Content.Read(Buffer, Count, Buffer.Length - Count)) > 0)
            {
                Count += Read;
            }
            if (Count > MaxResponseBytes)
            {
                throw new NtfyException(NtfyFailure.MalformedResponse, (int)Response.StatusCode);
            }
            return Buffer.AsSpan(0, Count).ToArray();
        }

        // a failure without an answer, by its kind; the exception's own message (which can name the address) is dropped
        internal static Exception Classify(Exception Error, CancellationToken Cancel, int TimeoutSeconds)
        {
            if (Error is NtfyException)
            {
                return Error;
            }
            if (Error is OperationCanceledException)
            {
                return Cancel.IsCancellationRequested ? new NtfyException(NtfyFailure.Canceled) : new NtfyException(NtfyFailure.Timeout, null, TimeoutSeconds);
            }
            for (Exception? Inner = Error; Inner != null; Inner = Inner.InnerException)
            {
                if (Inner is AuthenticationException)
                {
                    return new NtfyException(NtfyFailure.Tls);
                }
                if (Inner is SocketException Socket)
                {
                    return Socket.SocketErrorCode switch
                    {
                        SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => new NtfyException(NtfyFailure.Dns),
                        SocketError.ConnectionRefused => new NtfyException(NtfyFailure.ConnectionRefused),
                        SocketError.TimedOut => new NtfyException(NtfyFailure.Timeout, null, TimeoutSeconds),
                        _ => new NtfyException(NtfyFailure.Network)
                    };
                }
            }
            if (Error is HttpRequestException || Error is IOException)
            {
                return new NtfyException(NtfyFailure.Network);
            }
            return new NtfyException(NtfyFailure.Unexpected, null, 0, Error.GetType().Name);
        }

        // ---- the words ----

        // what to tell the operator: the problem (written after "Error: ") and what to check. Never a value
        internal static List<string> Describe(NtfyFailure Failure, int? Status, int TimeoutSeconds, string? ExceptionType)
        {
            string Http = Status != null ? " (HTTP " + Status.Value.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
            string Check = "Check server and topic in Ntfy.yaml, and the network.";
            string Problem;
            switch (Failure)
            {
                case NtfyFailure.ServerInvalid:
                    Problem = "server or topic in Ntfy.yaml can't be used.";
                    break;
                case NtfyFailure.Dns:
                    Problem = "the ntfy server's name could not be found (DNS).";
                    break;
                case NtfyFailure.ConnectionRefused:
                    Problem = "the ntfy server refused the connection.";
                    break;
                case NtfyFailure.Network:
                    Problem = "the network failed while publishing.";
                    break;
                case NtfyFailure.Tls:
                    Problem = "the encrypted connection to the ntfy server failed (its certificate is not trusted, or it does not offer https on that port).";
                    break;
                case NtfyFailure.Timeout:
                    Problem = "the ntfy server did not answer within " + TimeoutSeconds.ToString(CultureInfo.InvariantCulture) + " seconds (timeoutSeconds).";
                    break;
                case NtfyFailure.Canceled:
                    Problem = "the publish was canceled.";
                    break;
                case NtfyFailure.Redirect:
                    Problem = "the ntfy server answered with a redirect" + Http + ", which Campertron does not follow.";
                    Check = "Write the server's final address in server (for example https instead of http).";
                    break;
                case NtfyFailure.Unauthorized:
                    Problem = "the ntfy server wants an access token, or refused the one sent" + Http + ".";
                    Check = "Check the token in tokenFile or CAMPERTRON_NTFY_TOKEN, and that it may publish to the topic.";
                    break;
                case NtfyFailure.Forbidden:
                    Problem = "the ntfy server does not allow publishing to the topic" + Http + ".";
                    Check = "Check the token in tokenFile or CAMPERTRON_NTFY_TOKEN, and that it may publish to the topic.";
                    break;
                case NtfyFailure.NotFound:
                    Problem = "the ntfy server answered \"not found\"" + Http + ".";
                    Check = "Check that server in Ntfy.yaml is an ntfy server's address.";
                    break;
                case NtfyFailure.TooLarge:
                    Problem = "the ntfy server refused the message as too large" + Http + ".";
                    break;
                case NtfyFailure.RateLimited:
                    Problem = "the ntfy server limits how often Campertron may publish" + Http + ".";
                    Check = "Campertron does not publish again at once: fewer searches, or a server or account with higher limits, help.";
                    break;
                case NtfyFailure.ClientError:
                    Problem = "the ntfy server refused the message" + Http + ".";
                    Check = "Check topic, tags and priority in Ntfy.yaml.";
                    break;
                case NtfyFailure.ServerError:
                    Problem = "the ntfy server failed" + Http + ".";
                    Check = "The server has a problem; Campertron tries again with the next new openings.";
                    break;
                case NtfyFailure.UnexpectedStatus:
                    Problem = "the ntfy server gave an unexpected answer" + Http + ".";
                    break;
                case NtfyFailure.MalformedResponse:
                    Problem = "the server's answer" + Http + " is not an ntfy message, so the delivery can't be confirmed.";
                    Check = "Check that server in Ntfy.yaml is an ntfy server, not a proxy's or a login page.";
                    break;
                default:
                    Problem = "an unexpected problem stopped the publish (" + (ExceptionType ?? "unknown") + ").";
                    break;
            }
            return new List<string> { "the ntfy notification could not be sent: " + Problem, Check };
        }
    }
}
