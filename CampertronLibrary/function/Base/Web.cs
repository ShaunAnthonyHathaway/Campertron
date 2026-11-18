namespace CampertronLibrary.function.Base
{
    public static class Web
    {
        // the key Download marks a failure of the transfer itself with (a read from the response), so a caller can tell
        // it from a failure to write the file; the exception is rethrown unchanged
        internal const string TransferFailureKey = "Campertron.DownloadTransfer";

        // Downloads Url into DestinationFile, which must not exist yet (it is created, never replaced or deleted here; the
        // caller owns the folder and removes it). Streams the body in pieces: the token cancels it at once, and when
        // nothing arrives for StallTimeout it ends with DownloadTimedOutException (an HttpRequestException). HeaderTimeout
        // bounds the wait for the server's answer (null: HttpClient's default, 100 seconds). Progress gets the bytes written
        // and the length the server announced (null when it sent none). A status other than success throws
        // HttpRequestException, as GetStreamAsync did; a failed read keeps its exception and gets TransferFailureKey.
        // Handler null: a new HttpClient with the system's proxy settings.
        internal static void Download(Uri Url, string DestinationFile, HttpMessageHandler? Handler, TimeSpan? HeaderTimeout, TimeSpan StallTimeout,
            CancellationToken Token, Action<long, long?>? Progress)
        {
            using HttpClient Client = Handler == null ? new HttpClient() : new HttpClient(Handler, disposeHandler: false);
            if (HeaderTimeout.HasValue)
            {
                Client.Timeout = HeaderTimeout.Value;
            }
            using HttpResponseMessage Response = Client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, Token).GetAwaiter().GetResult();
            Response.EnsureSuccessStatusCode();
            long? Total = Response.Content.Headers.ContentLength;
            using Stream Body = Response.Content.ReadAsStream(Token);
            using FileStream Output = new FileStream(DestinationFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using CancellationTokenSource Stall = CancellationTokenSource.CreateLinkedTokenSource(Token);
            byte[] Buffer = new byte[81920];
            long Written = 0;
            long Reported = 0;
            Progress?.Invoke(0, Total);
            while (true)
            {
                Stall.CancelAfter(StallTimeout);
                int Count;
                try
                {
                    Count = Body.ReadAsync(Buffer.AsMemory(), Stall.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (!Token.IsCancellationRequested && Stall.IsCancellationRequested)
                {
                    throw new DownloadTimedOutException(StallTimeout);
                }
                catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
                {
                    ex.Data[TransferFailureKey] = true;
                    throw;
                }
                if (Count == 0)
                {
                    break;
                }
                Token.ThrowIfCancellationRequested();
                Output.Write(Buffer, 0, Count);
                Written += Count;
                // a report for each quarter mebibyte at most: the console prints none, the GUI throttles what it shows
                if (Written - Reported >= 256 * 1024)
                {
                    Reported = Written;
                    Progress?.Invoke(Written, Total);
                }
            }
            Output.Flush(true);
            Progress?.Invoke(Written, Total);
        }
    }

    // nothing arrived from the server for the stall timeout; a download failure ("a download failed: …" on the console)
    internal sealed class DownloadTimedOutException : HttpRequestException
    {
        internal DownloadTimedOutException(TimeSpan StallTimeout)
            : base("the download stalled: nothing was received for "
                + StallTimeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " seconds")
        {
        }
    }
}
