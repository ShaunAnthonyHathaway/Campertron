using System;

namespace Campertron.ViewModels
{
    // Carries a run's progress from the search's worker threads to the UI thread (GS2). GS1 reports a snapshot whenever
    // something changes, under its own lock; Report never blocks it: it keeps only the newest snapshot and queues at most
    // one job for the UI thread. The UI thread publishes at most once per Interval (a later snapshot waits for the rest of
    // the interval on a one-shot timer), so a flood of progress can't fill the dispatcher's queue. Close stops it: nothing
    // is published afterwards (the run's end is shown from its result, at once). Since V011-1 (D61) the RIDB refresh's
    // progress goes through the same gate (ProgressGate<RidbRefreshProgress>); a snapshot's sequence number orders them.
    internal sealed class ProgressGate : ProgressGate<SearchProgress>
    {
        internal ProgressGate(Action<SearchProgress> Publish, Action<Action> Post, Func<TimeSpan, Action, IDisposable> Delay, TimeProvider Time, TimeSpan Interval)
            : base(Value => Value.Sequence, Publish, Post, Delay, Time, Interval)
        {
        }
    }

    internal class ProgressGate<T> : IProgress<T> where T : class
    {
        private readonly object _Lock = new object();
        private readonly Func<T, long> _Sequence;
        private readonly Action<T> _Publish;
        private readonly Action<Action> _Post;
        private readonly Func<TimeSpan, Action, IDisposable> _Delay;
        private readonly TimeProvider _Time;
        private readonly TimeSpan _Interval;
        private T? _Latest;
        private bool _Scheduled;
        private bool _Closed;
        private bool _HasPublished;
        private long _LastPublished;
        private IDisposable? _Timer;

        internal ProgressGate(Func<T, long> Sequence, Action<T> Publish, Action<Action> Post, Func<TimeSpan, Action, IDisposable> Delay, TimeProvider Time, TimeSpan Interval)
        {
            _Sequence = Sequence;
            _Publish = Publish;
            _Post = Post;
            _Delay = Delay;
            _Time = Time;
            _Interval = Interval;
        }

        // how many snapshots reached the UI (tests)
        internal int Published { get; private set; }
        // how many jobs were queued for the UI thread (tests: never more than one at a time)
        internal int Posted { get; private set; }

        // any thread: keeps the newest snapshot; queues one job unless one is queued or waiting
        public void Report(T Value)
        {
            lock (_Lock)
            {
                if (_Closed)
                {
                    return;
                }
                if (_Latest == null || _Sequence(Value) > _Sequence(_Latest))
                {
                    _Latest = Value;
                }
                if (_Scheduled)
                {
                    return;
                }
                _Scheduled = true;
                Posted++;
            }
            _Post(Flush);
        }

        // the UI thread: publishes the newest snapshot, or waits for the rest of the interval
        private void Flush()
        {
            T? Value;
            lock (_Lock)
            {
                _Timer = null;
                if (_Closed)
                {
                    _Scheduled = false;
                    return;
                }
                if (_HasPublished)
                {
                    TimeSpan Since = _Time.GetElapsedTime(_LastPublished);
                    if (Since < _Interval)
                    {
                        _Timer = _Delay(_Interval - Since, Flush);
                        return;
                    }
                }
                Value = _Latest;
                _Latest = null;
                _Scheduled = false;
                _HasPublished = true;
                _LastPublished = _Time.GetTimestamp();
            }
            if (Value != null)
            {
                Published++;
                _Publish(Value);
            }
        }

        // the UI thread: nothing more is published; a waiting timer is stopped
        internal void Close()
        {
            IDisposable? Timer;
            lock (_Lock)
            {
                _Closed = true;
                _Latest = null;
                Timer = _Timer;
                _Timer = null;
            }
            Timer?.Dispose();
        }
    }
}
