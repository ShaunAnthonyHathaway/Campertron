using System.Security.Cryptography;
using System.Text;

namespace CampertronLibrary.function.WebAccess
{
    // what became of a setup code that was entered
    internal enum WebSetupCodeResult
    {
        // the code is the current one and is used up now: its holder may set the password
        Accepted,
        // not the current code (also a code that was used, or text that is no code)
        Wrong,
        // the current code is older than its lifetime, or there is none: a new one must be made first
        Expired
    }

    // The one-time setup code of the web UI's first start (decisions D81 and D92, business-rules-settings §31). While a
    // password is required and none is set, whoever can read Campertron's console (in a container: docker logs) proves it
    // with this code and sets the first password in the browser.
    //  - A code is 12 characters of an alphabet without look-alikes (no I, L, O, U, 0 or 1), from the system's random
    //    number generator, written in three groups: XXXX-XXXX-XXXX (30 letters and digits: 58 bits). Entered, it is read
    //    without its hyphens and spaces and in any case.
    //  - It is kept in this object's memory only: never in a file, so a restart makes a new one and the old one is worth
    //    nothing. It is good for Lifetime (30 minutes) and for one use: the first correct entry uses it up, also when two
    //    arrive at once. After that, or after its lifetime, a new code must be made (New), which the setup page offers.
    //  - A wrong entry does not use the code up (anyone on the network could otherwise lock the owner out); guessing is
    //    held off by the sign-in's rate limit and by the code's 58 bits.
    //  - Codes are compared in constant time. An object is one web UI's; it has no static state.
    internal sealed class WebSetupCode
    {
        internal const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";
        internal const int Length = 12;
        internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

        private readonly object _Lock = new object();
        private readonly Func<DateTime> _UtcNow;
        private string? _Code;
        private DateTime _Made;

        internal WebSetupCode(Func<DateTime>? UtcNow = null)
        {
            _UtcNow = UtcNow ?? (() => DateTime.UtcNow);
        }

        // makes a new code, which replaces any earlier one, and returns it as it is shown: XXXX-XXXX-XXXX
        internal string New()
        {
            var Text = new StringBuilder(Length);
            for (int i = 0; i < Length; i++)
            {
                Text.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            }
            lock (_Lock)
            {
                _Code = Text.ToString();
                _Made = _UtcNow();
            }
            return Show(Text.ToString());
        }

        // whether a code exists that could still be entered
        internal bool IsCurrent
        {
            get
            {
                lock (_Lock)
                {
                    return _Code != null && _UtcNow() - _Made < Lifetime;
                }
            }
        }

        // forgets the code (the password was set another way)
        internal void Clear()
        {
            lock (_Lock)
            {
                _Code = null;
            }
        }

        // The entry of a code. Accepted uses the code up at once, so the same code never opens the setup twice
        internal WebSetupCodeResult Use(string? Entered)
        {
            string? Typed = Normalize(Entered);
            lock (_Lock)
            {
                if (_Code == null || _UtcNow() - _Made >= Lifetime)
                {
                    _Code = null;
                    return WebSetupCodeResult.Expired;
                }
                if (Typed == null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Typed), Encoding.ASCII.GetBytes(_Code)))
                {
                    return WebSetupCodeResult.Wrong;
                }
                _Code = null;
                return WebSetupCodeResult.Accepted;
            }
        }

        // whether the code is the current one, without using it up (the setup's second step checks it again with the
        // password, and only then uses it)
        internal WebSetupCodeResult Check(string? Entered)
        {
            string? Typed = Normalize(Entered);
            lock (_Lock)
            {
                if (_Code == null || _UtcNow() - _Made >= Lifetime)
                {
                    return WebSetupCodeResult.Expired;
                }
                return Typed != null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Typed), Encoding.ASCII.GetBytes(_Code))
                    ? WebSetupCodeResult.Accepted : WebSetupCodeResult.Wrong;
            }
        }

        // a code as it is shown: three groups of four
        internal static string Show(string Code) => Code.Substring(0, 4) + "-" + Code.Substring(4, 4) + "-" + Code.Substring(8, 4);

        // the 12 characters of an entry: hyphens and spaces left out, upper case; null for anything that is not 12
        // characters of the alphabet (so a comparison always has two texts of the same length)
        internal static string? Normalize(string? Entered)
        {
            if (Entered == null || Entered.Length > 64)
            {
                return null;
            }
            var Text = new StringBuilder(Length);
            foreach (char C in Entered)
            {
                if (C == '-' || C == ' ')
                {
                    continue;
                }
                char Upper = char.ToUpperInvariant(C);
                if (Alphabet.IndexOf(Upper) < 0 || Text.Length == Length)
                {
                    return null;
                }
                Text.Append(Upper);
            }
            return Text.Length == Length ? Text.ToString() : null;
        }
    }
}
