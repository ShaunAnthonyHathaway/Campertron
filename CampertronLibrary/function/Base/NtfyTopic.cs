using System.Security.Cryptography;

namespace CampertronLibrary.function.Base
{
    // A random ntfy topic (decision D86, v0.3.0's PR 5; business-rules-settings §32), for the desktop GUI's and the web UI's
    // Ntfy.yaml editors and, later, the guided path: "campertron-" and 26 characters, each drawn from the 36 lower-case
    // letters and digits by the platform's cryptographic random number generator (RandomNumberGenerator.GetInt32, which
    // draws without bias), so a topic has 26 × log2(36) ≈ 134 bits of randomness. Every character is one ntfy accepts in a
    // topic (NtfyEndpoint.IsValidTopic: letters, digits, - and _, at most 64), and lower case only, so it can be typed into
    // a phone. Nothing is asked of a server: the topic is new only by chance, as any random value is, and nothing here knows
    // whether a server has it.
    internal static class NtfyTopic
    {
        internal const string Prefix = "campertron-";
        internal const int RandomLength = 26;
        internal const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

        // the bits of randomness of a topic (for the words and the tests)
        internal static double Bits => RandomLength * Math.Log2(Alphabet.Length);

        // the production source: the platform's cryptographic random number generator, an index from 0 to Max - 1
        internal static readonly Func<int, int> CryptographicIndex = RandomNumberGenerator.GetInt32;

        internal static string New() => New(CryptographicIndex);

        // the same with another source of indexes (the tests' fixed sequences); never used by a UI
        internal static string New(Func<int, int> NextIndex)
        {
            var Topic = new char[Prefix.Length + RandomLength];
            Prefix.CopyTo(0, Topic, 0, Prefix.Length);
            for (int i = 0; i < RandomLength; i++)
            {
                int Index = NextIndex(Alphabet.Length);
                if (Index < 0 || Index >= Alphabet.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(NextIndex), "An index must be from 0 to the alphabet's length - 1.");
                }
                Topic[Prefix.Length + i] = Alphabet[Index];
            }
            return new string(Topic);
        }
    }
}
