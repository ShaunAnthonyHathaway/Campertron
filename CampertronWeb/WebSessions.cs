using System.Security.Cryptography;

namespace CampertronWeb
{
    // The web UI's sessions (decisions D81 and D92, business-rules-settings §31). A sign-in makes one: a random identifier
    // that the session cookie carries inside its encrypted ticket, and an entry here, in this object's memory. A request
    // is signed in only while its identifier has an entry, so:
    //  - Sign out removes the entry: the cookie is worth nothing afterwards, also a copy of it.
    //  - A password change removes every entry: every browser is signed out (D81).
    //  - A session ends after IdleTimeout without a request and at the latest AbsoluteLifetime after its sign-in. Since D109
    //    a live page's script's requests (they only look) never renew it (Check): only what the user does.
    //  - A session belongs to the password file as it was at the sign-in (its length and time of writing): when the file
    //    is replaced or removed outside the web UI, the session ends.
    //  - A restart of web mode ends every session: nothing of a session is written to disk.
    //  - At most MaxSessions exist; one more removes the one unused for the longest time.
    // The identifier is no secret a file or a page holds: it exists only inside the cookie's ticket, which Data Protection
    // encrypts and signs. An object is one web UI's; it has no static state. Since v0.3.0's PR 5 a session that ends says so
    // (Ended), so that its settings forms, which can hold an entered SMTP password until it is saved, end with it.
    internal sealed class WebSessions
    {
        internal const int MaxSessions = 32;
        internal static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);
        internal static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

        private sealed class Entry
        {
            internal DateTime Created;
            internal DateTime Seen;
            internal (long, DateTime)? Password;
        }

        private readonly object _Lock = new object();
        private readonly Dictionary<string, Entry> _Sessions = new(StringComparer.Ordinal);
        private readonly Func<DateTime> _UtcNow;

        internal WebSessions(Func<DateTime> UtcNow)
        {
            _UtcNow = UtcNow;
        }

        // a session ended (its identifier), or every session (null); raised after the change, outside the lock
        internal event Action<string?>? Ended;

        // whether the identifier is a session that has not ended, without using it (its idle time goes on)
        internal bool IsAlive(string? Id)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return false;
            }
            lock (_Lock)
            {
                return _Sessions.TryGetValue(Id, out Entry? Found) && !Ended_(Found, _UtcNow());
            }
        }

        internal int Count
        {
            get
            {
                lock (_Lock)
                {
                    return _Sessions.Count;
                }
            }
        }

        // a new session for a sign-in with the password file as it is now; returns its identifier
        internal string Create((long, DateTime)? PasswordFile)
        {
            string Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            DateTime Now = _UtcNow();
            var Gone = new List<string>();
            lock (_Lock)
            {
                Gone.AddRange(RemoveEnded(Now));
                while (_Sessions.Count >= MaxSessions)
                {
                    string Oldest = _Sessions.OrderBy(Pair => Pair.Value.Seen).First().Key;
                    _Sessions.Remove(Oldest);
                    Gone.Add(Oldest);
                }
                _Sessions[Id] = new Entry { Created = Now, Seen = Now, Password = PasswordFile };
            }
            foreach (string One in Gone)
            {
                Ended?.Invoke(One);
            }
            return Id;
        }

        // whether the identifier is a session that has not ended, for the password file as it is now; a session that is
        // used stays alive (its idle time starts again)
        internal bool Use(string? Id, (long, DateTime)? PasswordFile) => Validate(Id, PasswordFile, true);

        // the same check for a request that only looks on its page's behalf (a live page's script, D109: WebLive.IsObservation):
        // the session's idle time goes on, so a page left open never keeps its browser signed in. A session that ended is
        // removed and said to have ended, as Use does
        internal bool Check(string? Id, (long, DateTime)? PasswordFile) => Validate(Id, PasswordFile, false);

        private bool Validate(string? Id, (long, DateTime)? PasswordFile, bool Renew)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return false;
            }
            DateTime Now = _UtcNow();
            lock (_Lock)
            {
                if (!_Sessions.TryGetValue(Id, out Entry? Found))
                {
                    return false;
                }
                if (!Ended_(Found, Now) && Found.Password != null && Found.Password.Equals(PasswordFile))
                {
                    if (Renew)
                    {
                        Found.Seen = Now;
                    }
                    return true;
                }
                _Sessions.Remove(Id);
            }
            Ended?.Invoke(Id);
            return false;
        }

        internal void Remove(string? Id)
        {
            if (string.IsNullOrEmpty(Id))
            {
                return;
            }
            lock (_Lock)
            {
                _Sessions.Remove(Id);
            }
            Ended?.Invoke(Id);
        }

        // ends every session (a password change)
        internal void Clear()
        {
            lock (_Lock)
            {
                _Sessions.Clear();
            }
            Ended?.Invoke(null);
        }

        private static bool Ended_(Entry One, DateTime Now) => Now - One.Seen >= IdleTimeout || Now - One.Created >= AbsoluteLifetime;

        private List<string> RemoveEnded(DateTime Now)
        {
            List<string> Gone = _Sessions.Where(Pair => Ended_(Pair.Value, Now)).Select(Pair => Pair.Key).ToList();
            foreach (string Id in Gone)
            {
                _Sessions.Remove(Id);
            }
            return Gone;
        }
    }
}
