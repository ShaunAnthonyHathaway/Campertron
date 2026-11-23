using System.Security.Cryptography;

namespace CampertronWeb
{
    // The web UI's sessions (decisions D81 and D92, business-rules-settings §31). A sign-in makes one: a random identifier
    // that the session cookie carries inside its encrypted ticket, and an entry here, in this object's memory. A request
    // is signed in only while its identifier has an entry, so:
    //  - Sign out removes the entry: the cookie is worth nothing afterwards, also a copy of it.
    //  - A password change removes every entry: every browser is signed out (D81).
    //  - A session ends after IdleTimeout without a request and at the latest AbsoluteLifetime after its sign-in.
    //  - A session belongs to the password file as it was at the sign-in (its length and time of writing): when the file
    //    is replaced or removed outside the web UI, the session ends.
    //  - A restart of web mode ends every session: nothing of a session is written to disk.
    //  - At most MaxSessions exist; one more removes the one unused for the longest time.
    // The identifier is no secret a file or a page holds: it exists only inside the cookie's ticket, which Data Protection
    // encrypts and signs. An object is one web UI's; it has no static state.
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
            lock (_Lock)
            {
                RemoveEnded(Now);
                while (_Sessions.Count >= MaxSessions)
                {
                    _Sessions.Remove(_Sessions.OrderBy(Pair => Pair.Value.Seen).First().Key);
                }
                _Sessions[Id] = new Entry { Created = Now, Seen = Now, Password = PasswordFile };
            }
            return Id;
        }

        // whether the identifier is a session that has not ended, for the password file as it is now; a session that is
        // used stays alive (its idle time starts again)
        internal bool Use(string? Id, (long, DateTime)? PasswordFile)
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
                if (Ended(Found, Now) || Found.Password == null || !Found.Password.Equals(PasswordFile))
                {
                    _Sessions.Remove(Id);
                    return false;
                }
                Found.Seen = Now;
                return true;
            }
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
        }

        // ends every session (a password change)
        internal void Clear()
        {
            lock (_Lock)
            {
                _Sessions.Clear();
            }
        }

        private static bool Ended(Entry One, DateTime Now) => Now - One.Seen >= IdleTimeout || Now - One.Created >= AbsoluteLifetime;

        private void RemoveEnded(DateTime Now)
        {
            foreach (string Id in _Sessions.Where(Pair => Ended(Pair.Value, Now)).Select(Pair => Pair.Key).ToList())
            {
                _Sessions.Remove(Id);
            }
        }
    }
}
