using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronLibrary.function.RecDotOrg.search
{
    // one request of an interactive search: the month's URL as AvailabilityApi.MonthUrl builds it, its campground and
    // month (the first day), and which attempt it is (1 to SearchHostOptions.MaxAttempts)
    internal sealed record SearchRequest(string Url, string CampgroundId, DateTime Month, int Attempt);

    // what came back: the HTTP status and the body as text; TooLarge when the body was over the limit (then not read)
    internal sealed record SearchResponse(int StatusCode, string? Body, bool TooLarge = false);

    // The transport seam of an interactive search: production is HttpSearchTransport (one HttpClient per run), tests
    // answer in memory. It gets the request and a token that ends at the request timeout or when the run is canceled; it
    // throws for a failed request (HttpRequestException, IOException, TimeoutException, OperationCanceledException)
    internal delegate SearchResponse SearchTransport(SearchRequest Request, CancellationToken Token);

    // the bounds of an interactive run. The defaults are the production values; tests shorten the waits
    internal sealed class SearchHostOptions
    {
        // every request of a month counts: failures, timeouts, heavy traffic and other answers worth trying again
        internal const int MaxAttempts = 3;
        // null: HttpSearchTransport, created and disposed by the run
        internal SearchTransport? Transport { get; init; }
        internal TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
        // the console's waits before trying again (a failed request, heavy traffic); here cancellable and bounded
        internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);
        internal TimeSpan HeavyTrafficDelay { get; init; } = TimeSpan.FromSeconds(1);
        internal int MaxResponseBytes { get; init; } = 32 * 1024 * 1024;
    }

    // what one month's request came to. Data: the month (Code null); otherwise Code says why it was not checked.
    // Attempts: requests made; Retryable: worth another attempt
    internal sealed record MonthOutcome(AvailabilityEntries? Data, SearchIssueCode? Code, int Attempts, int? StatusCode, bool Retryable)
    {
        internal static MonthOutcome Received(AvailabilityEntries Data, int Attempts) => new(Data, null, Attempts, null, false);
        internal static MonthOutcome Failure(SearchIssueCode Code, int Attempts, bool Retryable, int? StatusCode = null) => new(null, Code, Attempts, StatusCode, Retryable);
    }

    // how a month of one search ended: checked, or not checked and why
    internal sealed record MonthMark(bool Checked, SearchIssueCode? Reason, int Attempts, int? StatusCode);

    // The state of one interactive run (CampsiteSearch.Run, GS1), carried by RunningData.Host through the search the
    // console runs too (AvailabilityApi.GetAvailabilitiesByCampground): the run's cancellation, the transport and its bounds (at most
    // three requests per month, cancellable waits, no request after a block), the signal every search waiting for a
    // shared month is released by (data, a final failure, a block or cancellation), the months each search checked, and
    // the progress. It never writes to the console, never ends the process and reads no settings file.
    internal sealed class SearchHost
    {
        private readonly SearchHostOptions Options;
        private readonly SearchTransport Transport;
        private readonly IProgress<SearchProgress>? Progress;
        private readonly ConcurrentDictionary<string, UrlState> UrlStates = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<CampertronConfig, ConcurrentDictionary<DateTime, MonthMark>> Months = new(ReferenceEqualityComparer.Instance);
        private readonly ManualResetEventSlim BlockedSignal = new(false);
        private int BlockedState;
        private SearchIssue? BlockIssue;

        // the progress, under Gate
        private readonly object Gate = new();
        private long Sequence;
        private SearchRunStage Stage = SearchRunStage.Validating;
        private int CampgroundsPlanned;
        private int CampgroundsPrepared;
        private int Retrying;
        private readonly HashSet<string> Planned = new(StringComparer.Ordinal);
        private readonly HashSet<string> Completed = new(StringComparer.Ordinal);
        private readonly HashSet<string> Failed = new(StringComparer.Ordinal);
        private readonly List<string> SearchOrder = new();
        private readonly Dictionary<string, SearchState> SearchStates = new(StringComparer.Ordinal);
        private readonly List<Exception> DefectList = new();

        internal SearchHost(SearchHostOptions Options, SearchTransport Transport, CancellationToken Token, IProgress<SearchProgress>? Progress,
            IEnumerable<(string FileName, SearchState State)> Searches)
        {
            this.Options = Options;
            this.Transport = Transport;
            this.Token = Token;
            this.Progress = Progress;
            foreach ((string FileName, SearchState State) in Searches)
            {
                SearchOrder.Add(FileName);
                SearchStates[FileName] = State;
            }
        }

        internal CancellationToken Token { get; }

        internal void ThrowIfCanceled() => Token.ThrowIfCancellationRequested();

        // RequestBlocked or RateLimited once recreation.gov blocked the run (the first block wins), else null
        internal SearchIssue? Blocked => Volatile.Read(ref BlockedState) == 0 ? null : BlockIssue;

        // ---- a month ----

        // The month of Url for Search: fetched if this search is the first to need it (RunningData.Urls, as the console),
        // otherwise waited for. Null when it could not be checked: the reason is the search's mark for the month. Throws
        // OperationCanceledException when the run is canceled
        internal AvailabilityEntries? GetMonth(string Url, CampertronConfig Search, DateTime Month, RunningData RunData)
        {
            Token.ThrowIfCancellationRequested();
            UrlState State = UrlStates.GetOrAdd(Url, _ => new UrlState());
            MonthOutcome Outcome;
            if (RunData.Urls.TryAdd(Url, false))
            {
                //this search fetches it: whatever happens, every search waiting for it is released (finally)
                Outcome = MonthOutcome.Failure(SearchIssueCode.InternalError, 0, false);
                try
                {
                    if (RunData.SiteData.TryGetValue(Url, out AvailabilityEntries? Known))
                    {
                        Outcome = MonthOutcome.Received(Known, 0);
                    }
                    else
                    {
                        Outcome = Fetch(Url, Search.CampgroundID ?? string.Empty, Month);
                        if (Outcome.Data != null)
                        {
                            RunData.SiteData.TryAdd(Url, Outcome.Data);
                        }
                    }
                }
                catch (OperationCanceledException) when (Token.IsCancellationRequested)
                {
                    Outcome = MonthOutcome.Failure(SearchIssueCode.Canceled, Outcome.Attempts, false);
                }
                catch (Exception ex)
                {
                    //a defect of the fetch itself: contained to this month, never hidden
                    RecordDefect(ex);
                    Outcome = MonthOutcome.Failure(SearchIssueCode.InternalError, Outcome.Attempts, false);
                }
                finally
                {
                    State.Complete(Outcome);
                    RequestFinished(Url, Outcome);
                }
            }
            else if (RunData.SiteData.TryGetValue(Url, out AvailabilityEntries? Known))
            {
                Outcome = MonthOutcome.Received(Known, 0);
            }
            else
            {
                //another search fetches it: released by its data, its final failure, a block or cancellation
                State.Done.Wait(Token);
                Outcome = State.Outcome!;
            }

            if (Outcome.Data != null)
            {
                Mark(Search, Month, new MonthMark(true, null, Outcome.Attempts, null));
                return Outcome.Data;
            }
            if (Outcome.Code == SearchIssueCode.Canceled)
            {
                Token.ThrowIfCancellationRequested();
            }
            Mark(Search, Month, new MonthMark(false, Outcome.Code, Outcome.Attempts, Outcome.StatusCode));
            return null;
        }

        // the months a search marked (its first days), checked or not
        internal IReadOnlyDictionary<DateTime, MonthMark> MonthsOf(CampertronConfig Search) =>
            Months.TryGetValue(Search, out var Marks) ? new Dictionary<DateTime, MonthMark>(Marks) : new Dictionary<DateTime, MonthMark>();

        private void Mark(CampertronConfig Search, DateTime Month, MonthMark Mark)
        {
            Months.GetOrAdd(Search, _ => new ConcurrentDictionary<DateTime, MonthMark>())[Month] = Mark;
        }

        // at most MaxAttempts requests, none after a block or cancellation; the waits between them end at once when the
        // run is canceled or blocked
        private MonthOutcome Fetch(string Url, string CampgroundId, DateTime Month)
        {
            MonthOutcome Last = MonthOutcome.Failure(SearchIssueCode.InternalError, 0, false);
            for (int Attempt = 1; Attempt <= SearchHostOptions.MaxAttempts; Attempt++)
            {
                if (Token.IsCancellationRequested)
                {
                    return MonthOutcome.Failure(SearchIssueCode.Canceled, Attempt - 1, false);
                }
                SearchIssue? Block = Blocked;
                if (Block != null)
                {
                    return MonthOutcome.Failure(Block.Code, Attempt - 1, false);
                }
                Last = Try(Url, CampgroundId, Month, Attempt);
                if (Last.Data != null || !Last.Retryable || Attempt == SearchHostOptions.MaxAttempts)
                {
                    return Last;
                }
                TimeSpan Delay = Last.Code == SearchIssueCode.HeavyTraffic ? Options.HeavyTrafficDelay : Options.RetryDelay;
                RetryWait(+1);
                try
                {
                    WaitHandle.WaitAny(new[] { Token.WaitHandle, BlockedSignal.WaitHandle }, Delay);
                }
                finally
                {
                    RetryWait(-1);
                }
            }
            return Last;
        }

        private MonthOutcome Try(string Url, string CampgroundId, DateTime Month, int Attempt)
        {
            SearchResponse? Response;
            using (CancellationTokenSource Limit = CancellationTokenSource.CreateLinkedTokenSource(Token))
            {
                Limit.CancelAfter(Options.RequestTimeout);
                try
                {
                    Response = Transport(new SearchRequest(Url, CampgroundId, Month, Attempt), Limit.Token);
                }
                catch (OperationCanceledException) when (Token.IsCancellationRequested)
                {
                    return MonthOutcome.Failure(SearchIssueCode.Canceled, Attempt, false);
                }
                catch (OperationCanceledException)
                {
                    return MonthOutcome.Failure(SearchIssueCode.Timeout, Attempt, true);
                }
                catch (TimeoutException)
                {
                    return MonthOutcome.Failure(SearchIssueCode.Timeout, Attempt, true);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is SocketException || ex is AuthenticationException)
                {
                    return MonthOutcome.Failure(SearchIssueCode.NetworkFailure, Attempt, true);
                }
                catch (Exception ex)
                {
                    RecordDefect(ex);
                    return MonthOutcome.Failure(SearchIssueCode.InternalError, Attempt, false);
                }
            }
            if (Token.IsCancellationRequested)
            {
                return MonthOutcome.Failure(SearchIssueCode.Canceled, Attempt, false);
            }
            if (Response == null)
            {
                RecordDefect(new InvalidOperationException("The search transport returned no response."));
                return MonthOutcome.Failure(SearchIssueCode.InternalError, Attempt, false);
            }
            return Classify(Response, Attempt);
        }

        // An answer, as the console reads it (the block page, then the month JSON through Data.DynamicDeserialize), but
        // bounded: what the console would retry forever is tried again only within the attempts; the HTTP status is
        // also read (429 is a block; 408 and 5xx are worth another attempt; other errors are not)
        private MonthOutcome Classify(SearchResponse Response, int Attempt)
        {
            int Status = Response.StatusCode;
            if (Response.TooLarge || Response.Body != null && Encoding.UTF8.GetByteCount(Response.Body) > Options.MaxResponseBytes)
            {
                return MonthOutcome.Failure(SearchIssueCode.ResponseTooLarge, Attempt, false, Status);
            }
            string Body = Response.Body ?? string.Empty;
            if (Body.Contains("REQUEST BLOCKED", StringComparison.OrdinalIgnoreCase))
            {
                SetBlocked(SearchIssueCode.RequestBlocked, Status);
                return MonthOutcome.Failure(SearchIssueCode.RequestBlocked, Attempt, false, Status);
            }
            if (Status == 429)
            {
                SetBlocked(SearchIssueCode.RateLimited, Status);
                return MonthOutcome.Failure(SearchIssueCode.RateLimited, Attempt, false, Status);
            }
            if (Status < 200 || Status > 299)
            {
                return MonthOutcome.Failure(SearchIssueCode.HttpStatus, Attempt, Status == 408 || Status >= 500, Status);
            }
            if (string.IsNullOrWhiteSpace(Body))
            {
                return MonthOutcome.Failure(SearchIssueCode.MalformedResponse, Attempt, true, Status);
            }
            try
            {
                bool HeavyTraffic = false;
                bool ReceivedData = false;
                AvailabilityEntries Entries = Data.DynamicDeserialize(Body, ref HeavyTraffic, ref ReceivedData);
                if (HeavyTraffic)
                {
                    return MonthOutcome.Failure(SearchIssueCode.HeavyTraffic, Attempt, true, Status);
                }
                if (!ReceivedData || Entries?.campsites == null)
                {
                    //no campsites in it (for example {"count":0}): not a month, so not a month without availability
                    return MonthOutcome.Failure(SearchIssueCode.UnexpectedResponse, Attempt, true, Status);
                }
                return MonthOutcome.Received(Entries, Attempt);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return MonthOutcome.Failure(SearchIssueCode.MalformedResponse, Attempt, true, Status);
            }
            catch (Exception)
            {
                //the parser throws for JSON of another shape (an error object, a string value): not a month
                return MonthOutcome.Failure(SearchIssueCode.UnexpectedResponse, Attempt, true, Status);
            }
        }

        private void SetBlocked(SearchIssueCode Code, int Status)
        {
            lock (Gate)
            {
                if (BlockedState == 0)
                {
                    BlockIssue = new SearchIssue(Code, SearchIssueSeverity.Error, CampsiteSearch.Message(Code), StatusCode: Status, Attempts: 1);
                    Volatile.Write(ref BlockedState, 1);
                    BlockedSignal.Set();
                    Report();
                }
            }
        }

        // ---- defects ----

        internal void RecordDefect(Exception ex)
        {
            lock (Gate)
            {
                DefectList.Add(ex);
            }
        }

        internal IReadOnlyList<Exception> Defects
        {
            get
            {
                lock (Gate)
                {
                    return SearchLists.Of(DefectList);
                }
            }
        }

        // ---- progress ----

        // the first snapshot: the run's stage and every search's state before anything happens
        internal void ReportNow()
        {
            lock (Gate)
            {
                Report();
            }
        }

        internal void SetStage(SearchRunStage NewStage)
        {
            lock (Gate)
            {
                if (Stage != NewStage)
                {
                    Stage = NewStage;
                    Report();
                }
            }
        }

        internal void SetSearchState(string FileName, SearchState State)
        {
            lock (Gate)
            {
                if (SearchStates.TryGetValue(FileName, out SearchState Current) && Current != State)
                {
                    SearchStates[FileName] = State;
                    Report();
                }
            }
        }

        internal void PlanCampgrounds(int Count)
        {
            lock (Gate)
            {
                CampgroundsPlanned = Count;
            }
        }

        internal void CampgroundPrepared()
        {
            lock (Gate)
            {
                CampgroundsPrepared++;
                Report();
            }
        }

        internal void PlanRequests(IEnumerable<string> Urls)
        {
            lock (Gate)
            {
                Planned.UnionWith(Urls);
            }
        }

        internal (int Planned, int Completed, int Failed) RequestCounts()
        {
            lock (Gate)
            {
                return (Planned.Count, Completed.Count, Failed.Count);
            }
        }

        internal bool RequestDone(string Url)
        {
            lock (Gate)
            {
                return Completed.Contains(Url);
            }
        }

        // a month's fetch ended: finished (data, or a failure after a request) or not (canceled, or never requested)
        private void RequestFinished(string Url, MonthOutcome Outcome)
        {
            lock (Gate)
            {
                Planned.Add(Url);
                if (Outcome.Data != null || Outcome.Attempts > 0 && Outcome.Code != SearchIssueCode.Canceled)
                {
                    Completed.Add(Url);
                    if (Outcome.Data == null)
                    {
                        Failed.Add(Url);
                    }
                    Report();
                }
            }
        }

        private void RetryWait(int Change)
        {
            lock (Gate)
            {
                Retrying += Change;
                Report();
            }
        }

        // under Gate: one snapshot per change, in order. A failing progress handler is a defect, never the run's end
        private void Report()
        {
            if (Progress == null)
            {
                return;
            }
            SearchRunStage Shown = Stage == SearchRunStage.CheckingAvailability && Retrying > 0 ? SearchRunStage.Retrying : Stage;
            SearchProgress Snapshot = new SearchProgress(++Sequence, Shown, CampgroundsPlanned, CampgroundsPrepared, Planned.Count, Completed.Count, Failed.Count, Retrying,
                SearchLists.Of(SearchOrder.Select(Name => new SearchProgressItem(Name, SearchStates[Name]))));
            try
            {
                Progress.Report(Snapshot);
            }
            catch (Exception ex)
            {
                DefectList.Add(ex);
            }
        }

        private sealed class UrlState
        {
            internal readonly ManualResetEventSlim Done = new(false);
            private MonthOutcome? Result;
            internal MonthOutcome? Outcome => Volatile.Read(ref Result);
            internal void Complete(MonthOutcome Outcome)
            {
                Volatile.Write(ref Result, Outcome);
                Done.Set();
            }
        }
    }

    // The production transport: one HttpClient for the whole run (disposed with it), the request's token for its timeout
    // and the run's cancellation, and at most MaxBytes of a body. Like the console it sends no header of its own and
    // honors the system's proxy settings
    internal sealed class HttpSearchTransport : IDisposable
    {
        private readonly HttpClient Client;
        private readonly int MaxBytes;

        internal HttpSearchTransport(int MaxBytes, HttpMessageHandler? Handler = null)
        {
            Client = Handler == null ? new HttpClient() : new HttpClient(Handler, disposeHandler: true);
            //the request's own token carries the timeout, so a timeout and a cancellation can be told apart
            Client.Timeout = Timeout.InfiniteTimeSpan;
            this.MaxBytes = MaxBytes;
        }

        internal SearchResponse Send(SearchRequest Request, CancellationToken Token)
        {
            using HttpRequestMessage Message = new HttpRequestMessage(HttpMethod.Get, Request.Url);
            using HttpResponseMessage Response = Client.SendAsync(Message, HttpCompletionOption.ResponseHeadersRead, Token).GetAwaiter().GetResult();
            int Status = (int)Response.StatusCode;
            if (Response.Content.Headers.ContentLength > MaxBytes)
            {
                return new SearchResponse(Status, null, true);
            }
            using Stream Body = Response.Content.ReadAsStreamAsync(Token).GetAwaiter().GetResult();
            MemoryStream Read = new MemoryStream();
            byte[] Buffer = new byte[81920];
            int Count;
            while ((Count = Body.ReadAsync(Buffer.AsMemory(), Token).AsTask().GetAwaiter().GetResult()) > 0)
            {
                if (Read.Length + Count > MaxBytes)
                {
                    return new SearchResponse(Status, null, true);
                }
                Read.Write(Buffer, 0, Count);
            }
            Read.Position = 0;
            //decoded as the console's StreamReader does (UTF-8, a byte-order mark honored)
            using StreamReader Reader = new StreamReader(Read, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return new SearchResponse(Status, Reader.ReadToEnd());
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
