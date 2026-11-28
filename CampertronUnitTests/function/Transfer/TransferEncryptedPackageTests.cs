using System.Security.Cryptography;
using System.Text;
using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // The encrypted envelope (D132 point 6; v0.4.0-settings-transfer.md §Encrypted envelope, §Header fields, §Integrity and
    // authenticated encryption, §Passphrase rules): PBKDF2-HMAC-SHA256 at exactly 600,000 iterations, AES-256-GCM with a
    // fresh 16-byte salt and 12-byte nonce, the complete header as associated data, one sentence for every wrong passphrase and
    // every change. Every package carries the canary secrets and is written with the canary passphrase.
    public class TransferEncryptedPackageTests : TransferTestBase
    {
        private static TransferPackage AllSecrets() => Package(General(), Email(), Ntfy(), Search("Trip.yaml"), SmtpSecret(), TokenSecret(), TopicSecret());

        // one package of every secret for the tests that change it (written once: each write derives a key)
        private static byte[]? Shared;
        private static byte[] SharedPackage() => Shared ??= Encrypted(AllSecrets());

        private static readonly TransferProblem Changed_ = TransferProblem.WrongPassphraseOrChanged;

        [Test]
        public void ASecretBearingPackage_RoundTrips_WithOneDerivationForEachSide()
        {
            TransferPackage Given = AllSecrets();
            byte[] Bytes_ = Encrypted(Given);
            Assert.That(Derivations, Is.EqualTo(1), "the writer derives one key");
            TransferReadResult Opened = TransferPackageReader.Open(Bytes_);
            Assert.That(Opened.Failure, Is.Null);
            Assert.That(Opened.Package, Is.Null, "an encrypted package needs its passphrase");
            Assert.That(Opened.Locked, Is.Not.Null);
            Assert.That(Derivations, Is.EqualTo(1), "opening derives nothing");
            TransferPackage Read_ = TransferPackageReader.Unlock(Opened.Locked!, PassphraseCanary).Package!;
            Assert.That(Derivations, Is.EqualTo(2));
            Assert.That(Read_.Encrypted, Is.True);
            Assert.That(Read_.HasSecret, Is.True);
            Assert.That(Contents(Read_), Is.EqualTo(Contents(Given)));
        }

        [TestCase("secret-smtp-password")]
        [TestCase("secret-ntfy-token")]
        [TestCase("secret-ntfy-topic")]
        public void EachSecretKind_RoundTrips_WithItsFile(string KindText)
        {
            TransferEntryKind Kind = TransferFormat.KindOf(KindText)!.Value;
            var Secret = Kind == TransferEntryKind.SecretSmtpPassword ? SmtpSecret() : Kind == TransferEntryKind.SecretNtfyToken ? TokenSecret() : TopicSecret();
            var File_ = Kind == TransferEntryKind.SecretSmtpPassword ? Email() : Ntfy();
            TransferPackage Read_ = Read(Encrypted(Package(File_, Secret)), PassphraseCanary).Package!;
            Assert.That(Contents(Read_), Is.EqualTo(new[] { File_, Secret }));
        }

        [Test]
        public void AnEncryptedPackageWithoutASecret_IsReadToo()
        {
            TransferPackage Read_ = Read(Encrypted(Package(General(), Search("Trip.yaml"))), PassphraseCanary).Package!;
            Assert.That(Read_.Encrypted, Is.True);
            Assert.That(Read_.HasSecret, Is.False);
        }

        [Test]
        public void APackageWithASecret_IsNeverWrittenPlain_NorReadPlain()
        {
            Assert.That(TransferPackageWriter.WritePlain(Package(Email(), SmtpSecret())).Failure!.Problem, Is.EqualTo(TransferProblem.SecretNotEncrypted));
            foreach (var Secret in new[] { ("secret-smtp-password", "smtpPassword", SmtpCanary), ("secret-ntfy-token", "ntfyToken", TokenCanary), ("secret-ntfy-topic", "ntfyTopic", TopicCanary) })
            {
                var Raw = new[] { ("email", "Email.yaml", "smtpPort: 25\n"), ("ntfy", "Ntfy.yaml", "enabled: false\n"), Secret };
                TransferReadResult Result = Read(Bytes(PlainJson(PayloadJson(Raw))));
                Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.SecretNotEncrypted), Secret.Item1);
                AssertNoCanary("the refusal", Result.Failure.Message);
            }
        }

        [Test]
        public void TheEnvelope_HoldsNoSecret_NoPassphrase_AndNoTextInAnyEncoding()
        {
            byte[] Bytes_ = SharedPackage();
            AssertNoCanary("the encrypted package", Bytes_);
            string T = Text(Bytes_);
            foreach (string Plaintext in new[] { "Email.yaml", "smtpServer", "General.yaml", "Trip.yaml", "entries", "manifestSha256", "secret-" })
            {
                Assert.That(T, Does.Not.Contain(Plaintext), "the payload is encrypted as a whole");
            }
            using var Document = System.Text.Json.JsonDocument.Parse(Bytes_);
            Assert.That(Document.RootElement.EnumerateObject().Select(P => P.Name), Is.EqualTo(new[] { "campertronSettingsPackage", "protection", "exportedBy", "kdf", "cipher", "ciphertext", "tag" }));
            Assert.That(Document.RootElement.GetProperty("kdf").EnumerateObject().Select(P => P.Name), Is.EqualTo(new[] { "name", "iterations", "salt" }));
            Assert.That(Document.RootElement.GetProperty("cipher").EnumerateObject().Select(P => P.Name), Is.EqualTo(new[] { "name", "nonce" }));
        }

        [Test]
        public void TheKeyIsPbkdf2Sha256AtExactly600000Iterations_AndTheCipherAesGcm_ByAnIndependentDecryption()
        {
            byte[] Bytes_ = SharedPackage();
            Assert.That(MemberOf(Bytes_, "iterations"), Is.EqualTo("600000"));
            Assert.That(Text(Bytes_), Does.Contain("\"name\": \"pbkdf2-sha256\"").And.Contain("\"name\": \"aes-256-gcm\""));
            byte[] Salt = System.Convert.FromBase64String(MemberOf(Bytes_, "salt"));
            byte[] Nonce = System.Convert.FromBase64String(MemberOf(Bytes_, "nonce"));
            byte[] Ciphertext = System.Convert.FromBase64String(MemberOf(Bytes_, "ciphertext"));
            byte[] Tag = System.Convert.FromBase64String(MemberOf(Bytes_, "tag"));
            Assert.That((Salt.Length, Nonce.Length, Tag.Length), Is.EqualTo((16, 12, 16)));
            // the test's own key and associated data, from the design's text
            byte[] Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(PassphraseCanary), Salt, 600_000, HashAlgorithmName.SHA256, 32);
            byte[] Associated = AssociatedDataOf("1", ExportedBy, "600000", MemberOf(Bytes_, "salt"), MemberOf(Bytes_, "nonce"));
            byte[] Plaintext = new byte[Ciphertext.Length];
            using (var Gcm = new AesGcm(Key, 16))
            {
                Gcm.Decrypt(Nonce, Ciphertext, Tag, Plaintext, Associated);
            }
            // the plaintext is the plain envelope's payload object, by the same writer
            Assert.That(Text(Plaintext), Does.StartWith("{\n  \"entries\": [\n"));
            Assert.That(Text(Plaintext), Does.Contain(JsonQuoted(SmtpCanary)));
            // another count derives another key, which fails the tag
            byte[] Weaker = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(PassphraseCanary), Salt, 599_999, HashAlgorithmName.SHA256, 32);
            using (var Gcm = new AesGcm(Weaker, 16))
            {
                Assert.Throws<AuthenticationTagMismatchException>(() => Gcm.Decrypt(Nonce, Ciphertext, Tag, new byte[Ciphertext.Length], Associated));
            }
        }

        private static string JsonQuoted(string Value) => System.Text.Json.JsonEncodedText.Encode(Value).ToString();

        [Test]
        public void APackageTheTestEncryptedItself_IsRead_SoTheFormatIsTheDesignsNotOnlyTheWriters()
        {
            var Raw = new[] { ("email", "Email.yaml", "smtpPort: 25\n"), ("secret-smtp-password", "smtpPassword", SmtpCanary) };
            TransferPackage Read_ = Read(HandEncrypted(PayloadJson(Raw)), PassphraseCanary).Package!;
            Assert.That(Read_.Entries.Last().Text, Is.EqualTo(SmtpCanary));
        }

        [Test]
        public void AWrongPassphrase_IsOneSentence_OneDerivation_AndNamesNothing()
        {
            byte[] Bytes_ = SharedPackage();
            int Before = Derivations;
            TransferReadResult Result = Read(Bytes_, "another passphrase entirely");
            Assert.That(Result.Failure!.Problem, Is.EqualTo(Changed_));
            Assert.That(Derivations - Before, Is.EqualTo(1));
            Assert.That(Result.Failure.Message, Is.EqualTo("The passphrase is wrong, or the package was changed or damaged."));
            AssertNoCanary("the refusal", Result.Failure.Message, Result.ToString());
        }

        [Test]
        public void NfkcEquivalentPassphrases_GiveTheSameKey()
        {
            // "ﬀ" (U+FB00) and "ⅰ" (U+2170) are "ff" and "i" in NFKC; a decomposed é is the composed one
            string Typed = "Café oﬀice ⅰ secret";
            string Equivalent = "Café office i secret";
            byte[] Bytes_ = Encrypted(Package(General()), Typed);
            Assert.That(Read(Bytes_, Equivalent).Package, Is.Not.Null);
            Assert.That(Read(Bytes_, Typed.ToUpperInvariant()).Failure!.Problem, Is.EqualTo(Changed_), "case is not folded");
            Assert.That(Read(Bytes_, " " + Typed).Failure!.Problem, Is.EqualTo(Changed_), "nothing is trimmed");
        }

        [Test]
        public void ThePassphrasesNormalizedLength_Decides_AndIsCheckedBeforeAnyDerivation()
        {
            // "㎒" (U+3392) is "MHz" in NFKC: nine typed characters that normalize to fifteen are long enough
            string Nine = "㎒㎒㎒abcdef";
            Assert.That(Nine.Length, Is.EqualTo(9));
            Assert.That(TransferPassphrase.Check(Nine), Is.Null);
            // ten typed characters of a decomposed é that compose to five are too short
            string Composes = string.Concat(Enumerable.Repeat("é", 5));
            Assert.That(Composes.Length, Is.EqualTo(10));
            Assert.That(TransferPassphrase.Check(Composes)!.Problem, Is.EqualTo(TransferProblem.PassphraseTooShort));
            // 128 normalized characters, and one more; a typed text that normalizes to more than 128
            Assert.That(TransferPassphrase.Check(new string('a', 128)), Is.Null);
            Assert.That(TransferPassphrase.Check(new string('a', 129))!.Problem, Is.EqualTo(TransferProblem.PassphraseTooLong));
            Assert.That(TransferPassphrase.Check(new string('㎒', 43))!.Problem, Is.EqualTo(TransferProblem.PassphraseTooLong), "43 typed, 129 normalized");
            Assert.That(TransferPassphrase.Check(new string('a', 5000))!.Problem, Is.EqualTo(TransferProblem.PassphraseTooLong));
            Assert.That(TransferPassphrase.Check(null)!.Problem, Is.EqualTo(TransferProblem.PassphraseEmpty));
            Assert.That(TransferPassphrase.Check(new string(' ', 12))!.Problem, Is.EqualTo(TransferProblem.PassphraseOnlySpaces));
            Assert.That(TransferPassphrase.Check("abcdefghij\uD800")!.Problem, Is.EqualTo(TransferProblem.PassphraseInvalid));
            // at export and at import, without a derivation
            Assert.That(TransferPackageWriter.WriteEncrypted(Package(General()), Composes).Failure!.Problem, Is.EqualTo(TransferProblem.PassphraseTooShort));
            byte[] Bytes_ = SharedPackage();
            int Before = Derivations;
            Assert.That(Read(Bytes_, Composes).Failure!.Problem, Is.EqualTo(TransferProblem.PassphraseTooShort));
            Assert.That(Read(Bytes_, new string('a', 129)).Failure!.Problem, Is.EqualTo(TransferProblem.PassphraseTooLong));
            Assert.That(Derivations, Is.EqualTo(Before));
        }

        // every way to change an encrypted package: each is the one sentence
        private static IEnumerable<TestCaseData> Tampered()
        {
            yield return new TestCaseData("exportedBy", (Func<byte[], byte[]>)(B => WithMember(B, "exportedBy", "\"0.4.1\""))).SetName("{m}(exportedBy)");
            yield return new TestCaseData("salt", (Func<byte[], byte[]>)(B => WithMember(B, "salt", "\"" + FlipBase64(MemberOf(B, "salt")) + "\""))).SetName("{m}(salt)");
            yield return new TestCaseData("nonce", (Func<byte[], byte[]>)(B => WithMember(B, "nonce", "\"" + FlipBase64(MemberOf(B, "nonce")) + "\""))).SetName("{m}(nonce)");
            yield return new TestCaseData("ciphertext", (Func<byte[], byte[]>)(B => WithMember(B, "ciphertext", "\"" + FlipBase64(MemberOf(B, "ciphertext")) + "\""))).SetName("{m}(ciphertext)");
            yield return new TestCaseData("tag", (Func<byte[], byte[]>)(B => WithMember(B, "tag", "\"" + FlipBase64(MemberOf(B, "tag")) + "\""))).SetName("{m}(tag)");
            yield return new TestCaseData("ciphertext shortened", (Func<byte[], byte[]>)(B => WithMember(B, "ciphertext", "\"" + System.Convert.ToBase64String(System.Convert.FromBase64String(MemberOf(B, "ciphertext")).AsSpan(0, 30).ToArray()) + "\""))).SetName("{m}(shortened)");
        }

        [TestCaseSource(nameof(Tampered))]
        public void AChangedHeaderValue_Ciphertext_OrTag_IsTheSameSentenceAsAWrongPassphrase(string What, Func<byte[], byte[]> Change)
        {
            byte[] Bad = Change(SharedPackage());
            TransferReadResult Result = Read(Bad, PassphraseCanary);
            Assert.That(Result.Failure!.Problem, Is.EqualTo(Changed_), What);
            Assert.That(Result.Failure.Message, Is.EqualTo(TransferWording.Package(Changed_).Message));
        }

        // header values that are not format 1's: refused without a derivation, in the same sentence
        private static IEnumerable<TestCaseData> NotFormatOne()
        {
            yield return new TestCaseData("another iteration count", (Func<byte[], byte[]>)(B => WithMember(B, "iterations", "599999"))).SetName("{m}(iterations 599999)");
            yield return new TestCaseData("a huge iteration count", (Func<byte[], byte[]>)(B => WithMember(B, "iterations", "2147483647"))).SetName("{m}(iterations max)");
            yield return new TestCaseData("the count as an exponent", (Func<byte[], byte[]>)(B => WithMember(B, "iterations", "6e5"))).SetName("{m}(iterations 6e5)");
            yield return new TestCaseData("the count with a fraction", (Func<byte[], byte[]>)(B => WithMember(B, "iterations", "600000.0"))).SetName("{m}(iterations fraction)");
            yield return new TestCaseData("another KDF", (Func<byte[], byte[]>)(B => Changed(B, "\"pbkdf2-sha256\"", "\"pbkdf2-sha1\""))).SetName("{m}(kdf name)");
            yield return new TestCaseData("another cipher", (Func<byte[], byte[]>)(B => Changed(B, "\"aes-256-gcm\"", "\"aes-128-gcm\""))).SetName("{m}(cipher name)");
            yield return new TestCaseData("a short salt", (Func<byte[], byte[]>)(B => WithMember(B, "salt", "\"" + System.Convert.ToBase64String(new byte[15]) + "\""))).SetName("{m}(salt 15)");
            yield return new TestCaseData("a long nonce", (Func<byte[], byte[]>)(B => WithMember(B, "nonce", "\"" + System.Convert.ToBase64String(new byte[16]) + "\""))).SetName("{m}(nonce 16)");
            yield return new TestCaseData("a short tag", (Func<byte[], byte[]>)(B => WithMember(B, "tag", "\"" + System.Convert.ToBase64String(new byte[12]) + "\""))).SetName("{m}(tag 12)");
            yield return new TestCaseData("an empty ciphertext", (Func<byte[], byte[]>)(B => WithMember(B, "ciphertext", "\"\""))).SetName("{m}(ciphertext empty)");
            yield return new TestCaseData("base64 with a space", (Func<byte[], byte[]>)(B => WithMember(B, "salt", "\"" + MemberOf(B, "salt").Insert(4, " ") + "\""))).SetName("{m}(salt with space)");
            yield return new TestCaseData("base64 without its padding", (Func<byte[], byte[]>)(B => WithMember(B, "salt", "\"" + MemberOf(B, "salt").TrimEnd('=') + "\""))).SetName("{m}(salt unpadded)");
            yield return new TestCaseData("base64 of another alphabet", (Func<byte[], byte[]>)(B => WithMember(B, "nonce", "\"" + MemberOf(B, "nonce").Replace('+', '-').Replace('/', '_') + "-\""))).SetName("{m}(nonce url alphabet)");
            yield return new TestCaseData("base64 that is no base64", (Func<byte[], byte[]>)(B => WithMember(B, "tag", "\"!!!!!!!!!!!!!!!!!!!!!!!!\""))).SetName("{m}(tag not base64)");
            yield return new TestCaseData("base64 whose last bits are not zero", (Func<byte[], byte[]>)(B => WithMember(B, "salt", "\"" + MemberOf(B, "salt").Substring(0, 21) + "B==\""))).SetName("{m}(salt noncanonical)");
            // the same bytes with white space Convert.FromBase64String skips: only the strict reading refuses them
            yield return new TestCaseData("the same salt with four spaces", (Func<byte[], byte[]>)(B => WithMember(B, "salt", "\"" + SameBytesWithSpaces(MemberOf(B, "salt")) + "\""))).SetName("{m}(salt same bytes spaced)");
            yield return new TestCaseData("the same tag with four spaces", (Func<byte[], byte[]>)(B => WithMember(B, "tag", "\"" + SameBytesWithSpaces(MemberOf(B, "tag")) + "\""))).SetName("{m}(tag same bytes spaced)");
            // the ciphertext's own length bound is the package's, so only the strict reading refuses its spaced spelling
            yield return new TestCaseData("the same ciphertext with four spaces", (Func<byte[], byte[]>)(B => WithMember(B, "ciphertext", "\"" + SameBytesWithSpaces(MemberOf(B, "ciphertext")) + "\""))).SetName("{m}(ciphertext same bytes spaced)");
            yield return new TestCaseData("an exportedBy outside its characters", (Func<byte[], byte[]>)(B => WithMember(B, "exportedBy", "\"0.4.0 beta\""))).SetName("{m}(exportedBy space)");
            yield return new TestCaseData("an exportedBy too long", (Func<byte[], byte[]>)(B => WithMember(B, "exportedBy", "\"" + new string('1', 65) + "\""))).SetName("{m}(exportedBy 65)");
        }

        // the same base64 with four spaces inside, a multiple of four long: Convert.FromBase64String reads the same bytes
        private static string SameBytesWithSpaces(string Canonical)
        {
            string Spaced = Canonical.Insert(8, "    ");
            Assert.That(System.Convert.FromBase64String(Spaced), Is.EqualTo(System.Convert.FromBase64String(Canonical)), "the same bytes");
            return Spaced;
        }

        [TestCaseSource(nameof(NotFormatOne))]
        public void AHeaderValueFormat1NeverHas_IsRefusedWithoutAnyDerivation_InTheSameSentence(string What, Func<byte[], byte[]> Change)
        {
            byte[] Bad = Change(SharedPackage());
            int Before = Derivations;
            TransferReadResult Opened = TransferPackageReader.Open(Bad);
            Assert.That(Opened.Locked, Is.Not.Null, What + ": opened as an encrypted package that asks its passphrase");
            TransferReadResult Result = TransferPackageReader.Unlock(Opened.Locked!, PassphraseCanary);
            Assert.That(Result.Failure!.Problem, Is.EqualTo(Changed_), What);
            Assert.That(Derivations, Is.EqualTo(Before), What + ": no key was derived");
        }

        [Test]
        public void TheRealTagTruncatedTo12Bytes_IsRefused_ThoughAesGcmAcceptsShorterTags()
        {
            // AES-GCM's tag may be 12 to 16 bytes, and a shorter one is the longer one's start: a reader that took the tag's
            // length from the package would accept a truncated tag. Format 1's tag is 16 bytes, never less
            byte[] Bytes_ = SharedPackage();
            byte[] Tag = System.Convert.FromBase64String(MemberOf(Bytes_, "tag"));
            byte[] Truncated = WithMember(Bytes_, "tag", "\"" + System.Convert.ToBase64String(Tag.AsSpan(0, 12).ToArray()) + "\"");
            int Before = Derivations;
            Assert.That(Problem(Truncated, PassphraseCanary), Is.EqualTo(Changed_));
            Assert.That(Derivations, Is.EqualTo(Before), "refused from its length, before a derivation");
        }

        [Test]
        public void TheHeaderIsAuthenticated_EveryValueOfTheAssociatedData()
        {
            // a package the test encrypts with one header and then shows with another valid one: only the tag can see it
            var Raw = new[] { ("general", "General.yaml", "outputTo: Console\n") };
            byte[] Plaintext = Bytes(PayloadJson(Raw));
            Assert.That(Read(HandEncrypted(Plaintext), PassphraseCanary).Package, Is.Not.Null);
            Assert.That(Problem(HandEncrypted(Plaintext, Header: J => J.Replace("\"exportedBy\":\"0.4.0\"", "\"exportedBy\":\"0.4.1\"", StringComparison.Ordinal)), PassphraseCanary), Is.EqualTo(Changed_));
            // the associated data is built from the values read, so two spellings of the same JSON value are the same header
            Assert.That(Read(HandEncrypted(Plaintext, Header: J => J.Replace("\"exportedBy\":\"0.4.0\"", "\"exportedBy\":\"0\\u002E4.0\"", StringComparison.Ordinal)), PassphraseCanary).Package, Is.Not.Null);
            // a package sealed with associated data the design does not give (another label, another order) is refused
            byte[] Salt = RandomNumberGenerator.GetBytes(16);
            byte[] Nonce = RandomNumberGenerator.GetBytes(12);
            byte[] Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(PassphraseCanary), Salt, 600000, HashAlgorithmName.SHA256, 32);
            foreach (byte[] Wrong in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("campertron-settings-package\n1\n"), AssociatedDataOf("1", ExportedBy, "600000", System.Convert.ToBase64String(Nonce), System.Convert.ToBase64String(Salt)) })
            {
                byte[] Ciphertext = new byte[Plaintext.Length];
                byte[] Tag = new byte[16];
                using (var Gcm = new AesGcm(Key, 16))
                {
                    Gcm.Encrypt(Nonce, Plaintext, Ciphertext, Tag, Wrong);
                }
                byte[] Package_ = Bytes("{\"campertronSettingsPackage\":1,\"protection\":\"passphrase\",\"exportedBy\":\"0.4.0\",\"kdf\":{\"name\":\"pbkdf2-sha256\",\"iterations\":600000,\"salt\":\""
                    + System.Convert.ToBase64String(Salt) + "\"},\"cipher\":{\"name\":\"aes-256-gcm\",\"nonce\":\"" + System.Convert.ToBase64String(Nonce) + "\"},\"ciphertext\":\""
                    + System.Convert.ToBase64String(Ciphertext) + "\",\"tag\":\"" + System.Convert.ToBase64String(Tag) + "\"}");
                Assert.That(Problem(Package_, PassphraseCanary), Is.EqualTo(Changed_));
            }
        }

        [Test]
        public void TheAssociatedData_IsTheDesignsLines()
        {
            byte[] Lines = TransferCrypto.AssociatedData(1, "0.4.0", 600000, "c2FsdHNhbHRzYWx0c2FsdA==", "bm9uY2Vub25jZW5v");
            Assert.That(Text(Lines), Is.EqualTo("campertron-settings-package\n1\npassphrase\n0.4.0\npbkdf2-sha256\n600000\nc2FsdHNhbHRzYWx0c2FsdA==\naes-256-gcm\nbm9uY2Vub25jZW5v\n"));
        }

        [Test]
        public void EveryPackage_GetsAFreshSaltAndNonce_SoTheSameChoicesGiveOtherBytes()
        {
            TransferPackage Given = Package(Email(), SmtpSecret());
            var Packages = Enumerable.Range(0, 6).Select(_ => Encrypted(Given)).ToList();
            Assert.That(Packages.Select(B => MemberOf(B, "salt")).Distinct().Count(), Is.EqualTo(6));
            Assert.That(Packages.Select(B => MemberOf(B, "nonce")).Distinct().Count(), Is.EqualTo(6));
            Assert.That(Packages.Select(B => MemberOf(B, "ciphertext")).Distinct().Count(), Is.EqualTo(6));
            Assert.That(Packages.Select(Text).Distinct().Count(), Is.EqualTo(6), "the encrypted form differs, on purpose");
            foreach (byte[] Each in Packages)
            {
                Assert.That(Contents(Read(Each, PassphraseCanary).Package!), Is.EqualTo(Contents(Given)));
            }
        }

        [Test]
        public void ASecretOf1To4096Bytes_IsCarried_AndOutsideThoseOrWithALineBreak_IsNot()
        {
            string Largest = new string('s', 4093) + "é!";
            Assert.That(Encoding.UTF8.GetByteCount(Largest), Is.EqualTo(4096));
            Assert.That(Read(Encrypted(Package(Email(), SmtpSecret(Largest))), PassphraseCanary).Package!.Entries.Last().Text, Is.EqualTo(Largest));
            Assert.That(Read(Encrypted(Package(Ntfy(), TokenSecret("x"))), PassphraseCanary).Package, Is.Not.Null, "one byte");
            // what SecretFile would refuse, the package refuses: at the writer, and at the reader of a package the test made
            foreach (string Bad in new[] { Largest + "s", "", "two\nlines", "two\r\nlines", "ends with a line break\n", "carriage\rreturn", "nul\0inside", "﻿byte-order mark" })
            {
                Assert.That(TransferPackage.Create(ExportedBy, new[] { Email(), SmtpSecret(Bad) }, out TransferFailure? Failure), Is.Null, Bad.Length.ToString());
                Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.SecretUnusable));
                Assert.That(Failure.Secret, Is.EqualTo(TransferSecretKind.SmtpPassword));
                var Raw = new[] { ("email", "Email.yaml", "smtpPort: 25\n"), ("secret-smtp-password", "smtpPassword", Bad) };
                Assert.That(Problem(HandEncrypted(PayloadJson(Raw)), PassphraseCanary), Is.EqualTo(TransferProblem.SecretUnusable));
            }
        }

        [Test]
        public void ASecretNeedsItsSettingsFile_InTheSamePackage()
        {
            Assert.That(TransferPackage.Create(ExportedBy, new[] { General(), SmtpSecret() }, out TransferFailure? Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.SecretWithoutItsFile));
            Assert.That(TransferPackage.Create(ExportedBy, new[] { Email(), TopicSecret() }, out Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.SecretWithoutItsFile));
            var Raw = new[] { ("general", "General.yaml", "x: 1\n"), ("secret-ntfy-token", "ntfyToken", TokenCanary) };
            TransferReadResult Result = Read(HandEncrypted(PayloadJson(Raw)), PassphraseCanary);
            Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.SecretWithoutItsFile));
            AssertNoCanary("the refusal", Result.Failure.Message);
        }

        [Test]
        public void APayloadThatIsNotFormat1s_AfterItsTagPassed_IsDamaged_NeverReadInPart()
        {
            foreach (string Payload in new[] { "{}", "[]", "{\"entries\":[]}", "﻿{\"entries\":[],\"manifestSha256\":\"\"}", PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") }) + " trailing", PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") }, Extra: ",\"more\":1") })
            {
                Assert.That(Problem(HandEncrypted(Payload), PassphraseCanary), Is.EqualTo(TransferProblem.Damaged), Payload);
            }
            Assert.That(Problem(HandEncrypted(new byte[] { 0x7B, 0xC3, 0x28, 0x7D }), PassphraseCanary), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(HandEncrypted(PayloadJson(Array.Empty<(string, string, string)>())), PassphraseCanary), Is.EqualTo(TransferProblem.NoEntries));
        }
    }
}
