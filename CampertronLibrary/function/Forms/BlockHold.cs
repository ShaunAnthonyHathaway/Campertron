namespace CampertronLibrary.function.Forms
{
    // The five minutes after a block (GS2), for every UI: a run that recreation.gov blocks (its block page or HTTP 429)
    // keeps Start unavailable for five minutes, where it happened only (a window, a session), by the host's monotonic
    // clock. The host asks for the hold's line every second while it lasts; once the five minutes have passed the block is
    // forgotten. Used on the form's thread only.
    internal sealed class BlockHold
    {
        internal static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);
        private readonly TimeProvider _Time;
        private long? _BlockedAt;

        internal BlockHold(TimeProvider Time)
        {
            _Time = Time;
        }

        // recreation.gov blocked a run that just ended
        internal void Begin()
        {
            _BlockedAt = _Time.GetTimestamp();
        }

        // a block is remembered (its five minutes may have just passed: Text then forgets it)
        internal bool IsSet => _BlockedAt.HasValue;

        // Start is unavailable: a run was blocked less than five minutes ago
        internal bool IsActive => _BlockedAt.HasValue && _Time.GetElapsedTime(_BlockedAt.Value) < Duration;

        // the hold's line with the time left, or null when nothing is held; the block is forgotten once its time has passed
        internal string? Text()
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
