namespace CampertronLibrary.function.Forms
{
    // The five minutes after a block (GS2), for every UI: a run that recreation.gov blocks (its block page or HTTP 429)
    // keeps Start unavailable for five minutes, where it happened only (a window, a session), by the host's monotonic
    // clock. The host asks for the hold's line every second while it lasts; once the five minutes have passed the block is
    // forgotten. Safe from any thread (v0.4.0's phase 4): the desktop GUI shares one hold between its Results tab, on the
    // UI thread, and its monitoring, whose controller asks for the line and begins a hold on its own threads (the web UI
    // shares one between its results page and its monitoring under a lock of its own).
    internal sealed class BlockHold
    {
        internal static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
        private readonly TimeProvider _Time;
        private readonly object _Lock = new object();
        private long? _BlockedAt;

        internal BlockHold(TimeProvider Time)
        {
            _Time = Time;
        }

        // recreation.gov blocked a run that just ended
        internal void Begin()
        {
            lock (_Lock)
            {
                _BlockedAt = _Time.GetTimestamp();
            }
        }

        // a block is remembered (its five minutes may have just passed: Text then forgets it)
        internal bool IsSet
        {
            get
            {
                lock (_Lock)
                {
                    return _BlockedAt.HasValue;
                }
            }
        }

        // Start is unavailable: a run was blocked less than five minutes ago
        internal bool IsActive
        {
            get
            {
                lock (_Lock)
                {
                    return _BlockedAt.HasValue && _Time.GetElapsedTime(_BlockedAt.Value) < Duration;
                }
            }
        }

        // the hold's line with the time left, or null when nothing is held; the block is forgotten once its time has passed
        internal string? Text()
        {
            lock (_Lock)
            {
                if (!_BlockedAt.HasValue)
                {
                    return null;
                }
                TimeSpan Left = Duration - _Time.GetElapsedTime(_BlockedAt.Value);
                if (Left <= TimeSpan.Zero)
                {
                    _BlockedAt = null;
                    return null;
                }
                return ResultsWording.Cooldown(Left);
            }
        }
    }
}
