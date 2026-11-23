using System.Reflection;
using System.Security.Cryptography;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The random ntfy topic of D86 (v0.3.0's PR 5; business-rules-settings §32): "campertron-" and 26 lower-case letters and
    // digits drawn by the platform's cryptographic random number generator, about 134 bits, every character one ntfy accepts.
    // Two fixed sequences give exactly the expected topics through the seam the tests use; the production source is
    // RandomNumberGenerator, and nothing in the generator uses System.Random. Nothing contacts a server.
    public class NtfyTopicTests
    {
        [Test]
        public void ATopic_IsThePrefixAnd26LettersOrDigits_ThatNtfyAccepts_WithAbout134Bits()
        {
            for (int i = 0; i < 200; i++)
            {
                string Topic = NtfyTopic.New();
                Assert.That(Topic, Does.Match("^campertron-[a-z0-9]{26}$"));
                Assert.That(Topic.Length, Is.EqualTo(37));
                Assert.That(NtfyEndpoint.IsValidTopic(Topic), Is.True, "ntfy's own rule: ^[-_A-Za-z0-9]{1,64}$");
            }
            Assert.That(NtfyTopic.Bits, Is.GreaterThan(134.0).And.LessThan(134.5));
            Assert.That(NtfyTopic.Bits, Is.GreaterThanOrEqualTo(128.0), "at least as hard to guess as a 128-bit key");
        }

        [Test]
        public void TwoFixedSequences_GiveTheExpectedTopics()
        {
            Assert.That(NtfyTopic.New(Max => 0), Is.EqualTo("campertron-aaaaaaaaaaaaaaaaaaaaaaaaaa"));
            int Next = 0;
            Assert.That(NtfyTopic.New(Max => Next++ % Max), Is.EqualTo("campertron-abcdefghijklmnopqrstuvwxyz"));
            Next = 26;
            Assert.That(NtfyTopic.New(Max => Next++ % Max), Is.EqualTo("campertron-0123456789abcdefghijklmnop"));
            Assert.That(() => NtfyTopic.New(Max => Max), Throws.TypeOf<ArgumentOutOfRangeException>(), "an index outside the alphabet is a defect");
        }

        [Test]
        public void TheProductionSource_IsTheCryptographicRandomNumberGenerator_AndNothingUsesSystemRandom()
        {
            MethodInfo Source = NtfyTopic.CryptographicIndex.Method;
            Assert.That(Source.DeclaringType, Is.EqualTo(typeof(RandomNumberGenerator)));
            Assert.That(Source.Name, Is.EqualTo(nameof(RandomNumberGenerator.GetInt32)));
            Assert.That(Source.GetParameters().Select(P => P.ParameterType), Is.EqualTo(new[] { typeof(int) }), "GetInt32(toExclusive): no bias");

            var Called = new List<string>();
            foreach (MethodBase Method in typeof(NtfyTopic).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Concat<MethodBase>(new[] { typeof(NtfyTopic).TypeInitializer! }))
            {
                Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => (M.DeclaringType?.FullName ?? "") + "::" + M.Name));
            }
            Assert.That(Called.Where(C => C.StartsWith("System.Random", StringComparison.Ordinal) || C.Contains("Guid::NewGuid", StringComparison.Ordinal)), Is.Empty);
            Assert.That(Called, Does.Contain("System.Security.Cryptography.RandomNumberGenerator::GetInt32"));
            string First = NtfyTopic.New(), Second = NtfyTopic.New();
            Assert.That(First, Is.Not.EqualTo(Second), "two topics differ (with 134 bits, always)");
        }

        [Test]
        public void TheCharacters_AreSpreadOverTheWholeAlphabet()
        {
            var Counts = new Dictionary<char, int>();
            for (int i = 0; i < 2000; i++)
            {
                foreach (char C in NtfyTopic.New().Substring(NtfyTopic.Prefix.Length))
                {
                    Counts[C] = Counts.GetValueOrDefault(C) + 1;
                }
            }
            //52,000 draws over 36 characters: about 1,444 each; a fixed or narrow source would miss most of them
            Assert.That(Counts.Keys, Is.EquivalentTo(NtfyTopic.Alphabet.ToCharArray()));
            Assert.That(Counts.Values.Min(), Is.GreaterThan(1200));
            Assert.That(Counts.Values.Max(), Is.LessThan(1700));
        }
    }
}
