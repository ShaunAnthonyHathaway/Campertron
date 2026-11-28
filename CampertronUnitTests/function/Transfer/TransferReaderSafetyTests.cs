using System.IO.Compression;
using System.Text;
using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // The reader's resource safety and strictness (v0.4.0-settings-transfer.md §Limits, §Header fields, §Format versioning,
    // §Safe errors, §Threat model): every cheap limit before the work it bounds, the strict JSON document, a newer format
    // refused as one, nothing decompressed or unpacked, and fixed sentences that hold no value, secret, passphrase or path
    public class TransferReaderSafetyTests : TransferTestBase
    {
        private static byte[] Good() => Plain(Package(General(), Search("Trip.yaml")));

        [Test]
        public void AnOversizedPackage_IsRefusedFromItsLengthAlone_BeforeParsingOrAnyDerivation()
        {
            // not JSON at all, one byte over: refused as too large, not as no package, so nothing parsed it
            byte[] Garbage = Enumerable.Repeat((byte)'!', TransferFormat.MaxPackageBytes + 1).ToArray();
            Assert.That(Problem(Garbage), Is.EqualTo(TransferProblem.TooLarge));
            // an encrypted package over the limit: refused before it could ask for a passphrase
            byte[] Big = HandEncrypted(new byte[TransferFormat.MaxPackageBytes]);
            Assert.That(Big.Length, Is.GreaterThan(TransferFormat.MaxPackageBytes));
            int Before = Derivations;
            Assert.That(TransferPackageReader.Open(Big).Failure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Derivations, Is.EqualTo(Before));
            // the writer: a payload whose ciphertext alone is over the limit is refused before a key is derived
            var Many = Enumerable.Range(0, 13).Select(At => Search($"s{At:D2}.yaml", new string('a', 65_000))).ToArray();
            Before = Derivations;
            Assert.That(TransferPackageWriter.WriteEncrypted(Package(Many), PassphraseCanary).Failure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Derivations, Is.EqualTo(Before));
        }

        [Test]
        public void AStream_IsReadToTheLimitAndOneByte_NeverMore()
        {
            byte[] Good_ = Good();
            Assert.That(TransferPackageReader.ReadBounded(new MemoryStream(Good_), out TransferFailure? Failure), Is.EqualTo(Good_));
            Assert.That(Failure, Is.Null);
            // a stream that says it is too long is refused before anything is read
            var Long = new CountingStream(TransferFormat.MaxPackageBytes + 1, CanSeek: true);
            Assert.That(TransferPackageReader.ReadBounded(Long, out Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Long.Served, Is.Zero);
            // a stream without a length is read to the limit and one byte, and no further
            var Endless = new CountingStream(long.MaxValue, CanSeek: false);
            Assert.That(TransferPackageReader.ReadBounded(Endless, out Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Endless.Served, Is.EqualTo(TransferFormat.MaxPackageBytes + 1));
            // a stream that grows after it gave its length
            var Grows = new CountingStream(long.MaxValue, CanSeek: true, Claimed: 100);
            Assert.That(TransferPackageReader.ReadBounded(Grows, out Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Grows.Served, Is.EqualTo(TransferFormat.MaxPackageBytes + 1));
            // asynchronously too
            var Async = new CountingStream(long.MaxValue, CanSeek: false);
            (byte[]? Bytes_, TransferFailure? AsyncFailure) = TransferPackageReader.ReadBoundedAsync(Async, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(Bytes_, Is.Null);
            Assert.That(AsyncFailure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Async.Served, Is.EqualTo(TransferFormat.MaxPackageBytes + 1));
            // a stream that fails: a fixed sentence, never the system's message
            Assert.That(TransferPackageReader.ReadBounded(new FailingStream(), out Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.Unreadable));
            Assert.That(Failure.Message, Does.Not.Contain("/secret/path"));
        }

        [Test]
        public void ARepeatedMember_AnywhereInTheDocument_IsDamaged()
        {
            string Payload = PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") });
            Assert.That(Problem(Bytes("{\"campertronSettingsPackage\":1,\"campertronSettingsPackage\":1,\"protection\":\"none\",\"exportedBy\":\"0.4.0\",\"payload\":" + Payload + "}")), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload, Extra: ",\"protection\":\"none\""))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload, Extra: ",\"payload\":" + Payload))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload.Replace("\"manifestSha256\"", "\"manifestSha256\":\"x\",\"manifestSha256\"", StringComparison.Ordinal)))), Is.EqualTo(TransferProblem.Damaged));
            string Entry = EntryJson("general", "General.yaml", "x: 1\n");
            Assert.That(Problem(Bytes(PlainJson(Payload.Replace(Entry, Entry.Replace("\"kind\":\"general\"", "\"kind\":\"general\",\"kind\":\"general\"", StringComparison.Ordinal), StringComparison.Ordinal)))), Is.EqualTo(TransferProblem.Damaged));
            // the same member in another spelling of JSON is the same member
            Assert.That(Problem(Bytes(PlainJson(Payload, Extra: ",\"\\u0070rotection\":\"none\""))), Is.EqualTo(TransferProblem.Damaged));
            byte[] Encrypted_ = Encrypted(Package(General()));
            Assert.That(Problem(Changed(Encrypted_, "\"tag\":", "\"tag\": \"AAAAAAAAAAAAAAAAAAAAAA==\",\n  \"tag\":")), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Changed(Encrypted_, "\"iterations\":", "\"iterations\": 600000,\n    \"iterations\":")), Is.EqualTo(TransferProblem.Damaged));
        }

        [Test]
        public void AMissingMember_IsDamaged()
        {
            byte[] Good_ = Good();
            foreach (string Member in new[] { "\"protection\": \"none\",", "\"exportedBy\": \"0.4.0\"," })
            {
                Assert.That(Problem(Changed(Good_, Member, string.Empty)), Is.EqualTo(TransferProblem.Damaged), Member);
            }
            string Payload = PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") });
            Assert.That(Problem(Bytes("{\"campertronSettingsPackage\":1,\"protection\":\"none\",\"exportedBy\":\"0.4.0\"}")), Is.EqualTo(TransferProblem.Damaged));
            foreach (string Entry in new[] { "{\"kind\":\"general\",\"name\":\"General.yaml\",\"text\":\"x: 1\\n\"}", "{\"name\":\"General.yaml\",\"text\":\"x\",\"sha256\":\"" + Sha("x") + "\"}", "{}" })
            {
                Assert.That(Problem(Bytes(PlainJson("{\"entries\":[" + Entry + "],\"manifestSha256\":\"" + new string('0', 64) + "\"}"))), Is.EqualTo(TransferProblem.Damaged), Entry);
            }
            byte[] Encrypted_ = Encrypted(Package(General()));
            foreach (string Member in new[] { "ciphertext", "tag" })
            {
                string Value = MemberOf(Encrypted_, Member);
                Assert.That(Problem(Changed(Encrypted_, ",\n  \"" + Member + "\": \"" + Spelled(Value) + "\"", string.Empty)), Is.EqualTo(TransferProblem.Damaged), Member);
            }
            Assert.That(Problem(Changed(Encrypted_, "\"iterations\": 600000,", string.Empty)), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Changed(Encrypted_, ",\n    \"nonce\": \"" + Spelled(MemberOf(Encrypted_, "nonce")) + "\"", string.Empty)), Is.EqualTo(TransferProblem.Damaged));
        }

        [Test]
        public void AnUnknownMember_AMemberOfTheOtherForm_OrAnotherProtection_IsDamaged()
        {
            string Payload = PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") });
            foreach (string Extra in new[] { ",\"comment\":\"x\"", ",\"compression\":\"gzip\"", ",\"kdf\":{\"name\":\"pbkdf2-sha256\",\"iterations\":600000,\"salt\":\"AAAAAAAAAAAAAAAAAAAAAA==\"}", ",\"tag\":\"AAAAAAAAAAAAAAAAAAAAAA==\"", ",\"signature\":null" })
            {
                Assert.That(Problem(Bytes(PlainJson(Payload, Extra: Extra))), Is.EqualTo(TransferProblem.Damaged), Extra);
            }
            Assert.That(Problem(Bytes(PlainJson(Payload.Replace("\"manifestSha256\"", "\"note\":1,\"manifestSha256\"", StringComparison.Ordinal)))), Is.EqualTo(TransferProblem.Damaged));
            foreach (string Protection in new[] { "passphrase", "None", "aes", "" })
            {
                Assert.That(Problem(Bytes(PlainJson(Payload, Protection: Protection))), Is.EqualTo(TransferProblem.Damaged), Protection);
            }
            byte[] Encrypted_ = Encrypted(Package(General()));
            Assert.That(Problem(Changed(Encrypted_, "\"protection\": \"passphrase\"", "\"protection\": \"none\"")), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Changed(Encrypted_, "\"tag\":", "\"payload\": {},\n  \"tag\":")), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Changed(Encrypted_, "\"salt\":", "\"memory\": 65536,\n    \"salt\":")), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Changed(Encrypted_, "\"nonce\":", "\"tagLength\": 16,\n    \"nonce\":")), Is.EqualTo(TransferProblem.Damaged));
        }

        [Test]
        public void AMemberOfAnotherType_CommentsTrailingCommasAndDeepNesting_AreDamaged()
        {
            string Payload = PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") });
            Assert.That(Problem(Bytes(PlainJson(Payload, ExportedBy_: "0.4.0").Replace("\"exportedBy\":\"0.4.0\"", "\"exportedBy\":40", StringComparison.Ordinal))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson("[" + Payload + "]"))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload.Replace("\"x: 1\\n\"", "[\"x: 1\\n\"]", StringComparison.Ordinal)))), Is.EqualTo(TransferProblem.Damaged), "a depth of five");
            Assert.That(Problem(Bytes(PlainJson(Payload.Replace("\"entries\":[", "\"entries\":[[],", StringComparison.Ordinal)))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload, Extra: " /* comment */"))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload, Extra: ","))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload.Replace("\"text\"", "'text'", StringComparison.Ordinal)))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(Payload, Extra: ",\"x\":NaN"))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Changed(Encrypted(Package(General())), "\"iterations\": 600000", "\"iterations\": \"600000\"")), Is.EqualTo(TransferProblem.Damaged), "a number as text");
        }

        [Test]
        public void TheFormatVersion_IsAWholeNumber_ANewerOneRefusedAsNewer_NeverReadAsFarAsItCanBe()
        {
            string Payload = PayloadJson(new[] { ("general", "General.yaml", "x: 1\n") });
            foreach (string Version in new[] { "2", "17", "99999999999999999999" })
            {
                TransferReadResult Result = Read(Bytes(PlainJson(Payload, Version: Version)));
                Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferProblem.NewerFormat), Version);
                Assert.That(Result.Failure.Message, Does.Contain("newer version of Campertron"));
            }
            // a newer format may hold members, kinds and nesting format 1 does not know: still "newer", not "damaged"
            Assert.That(Problem(Bytes("{\"campertronSettingsPackage\":2,\"protection\":\"quantum\",\"x\":{\"y\":{\"z\":[[[[1]]]]}}}")), Is.EqualTo(TransferProblem.NewerFormat));
            Assert.That(Problem(Bytes("{\"future\":[1,2],\"campertronSettingsPackage\":3}")), Is.EqualTo(TransferProblem.NewerFormat));
            foreach (string Version in new[] { "0", "-1", "1.0", "1e0", "\"1\"", "true", "null" })
            {
                Assert.That(Problem(Bytes(PlainJson(Payload, Version: Version))), Is.EqualTo(TransferProblem.Damaged), Version);
            }
            Assert.That(Problem(Bytes("{\"protection\":\"none\"}")), Is.EqualTo(TransferProblem.NotAPackage));
            Assert.That(Problem(Bytes("[{\"campertronSettingsPackage\":1}]")), Is.EqualTo(TransferProblem.NotAPackage));
            Assert.That(Problem(Bytes("{\"x\":{\"campertronSettingsPackage\":1}}")), Is.EqualTo(TransferProblem.NotAPackage));
            // the newer format in the encrypted form: its version is in the header, refused before a passphrase is asked
            byte[] Newer = HandEncrypted(Bytes(Payload), Version: "2");
            Assert.That(TransferPackageReader.Open(Newer).Failure!.Problem, Is.EqualTo(TransferProblem.NewerFormat));
        }

        [Test]
        public void ACompressedOrArchivedPackage_IsNeverDecompressedOrUnpacked()
        {
            byte[] Good_ = Good();
            var Gzip = new MemoryStream();
            using (var Writer = new GZipStream(Gzip, CompressionLevel.Optimal, leaveOpen: true))
            {
                Writer.Write(Good_);
            }
            Assert.That(Problem(Gzip.ToArray()), Is.EqualTo(TransferProblem.NotAPackage));
            var Brotli = new MemoryStream();
            using (var Writer = new BrotliStream(Brotli, CompressionLevel.Optimal, leaveOpen: true))
            {
                Writer.Write(Good_);
            }
            Assert.That(Problem(Brotli.ToArray()), Is.EqualTo(TransferProblem.NotAPackage));
            var Zip = new MemoryStream();
            using (var Archive = new ZipArchive(Zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                using Stream Entry = Archive.CreateEntry("package.json").Open();
                Entry.Write(Good_);
            }
            Assert.That(Problem(Zip.ToArray()), Is.EqualTo(TransferProblem.NotAPackage));
            // a plain package's text holds what it was given, never an inflated form of it
            string Deflated = System.Convert.ToBase64String(Gzip.ToArray());
            TransferPackage Read_ = Read(Plain(Package(Search("Trip.yaml", Deflated)))).Package!;
            Assert.That(Read_.Entries.Single().Text, Is.EqualTo(Deflated));
        }

        [Test]
        public void EveryRefusal_IsAFixedSentence_WithoutAValue_Secret_Passphrase_OrPath()
        {
            string Path_ = Path.Join(Path.GetTempPath(), "CampertronTransferPath-" + Guid.NewGuid().ToString("N"));
            var Sentences = new HashSet<string>();
            foreach (TransferProblem Each in Enum.GetValues<TransferProblem>())
            {
                TransferFailure? Failure = null;
                try
                {
                    Failure = TransferWording.Package(Each);
                }
                catch (ArgumentException)
                {
                }
                Failure ??= Try(() => TransferWording.Entry(Each, "Trip.yaml")) ?? Try(() => TransferWording.Secret(Each, TransferSecretKind.NtfyTopic))
                    ?? Try(() => TransferWording.Passphrase(Each)) ?? Try(() => TransferWording.ExportFile(Each, "Trip.yaml"))
                    ?? Try(() => TransferWording.ExportSecret(Each, TransferSecretKind.SmtpPassword, "it is a folder")) ?? Try(() => TransferWording.Export(Each));
                Assert.That(Failure, Is.Not.Null, $"{Each} has no sentence");
                Assert.That(Failure!.Message, Does.Not.Contain(Path_).And.Not.Contain(Path.GetTempPath()).And.Not.Contain("{").And.Not.Contain("Exception"));
                AssertNoCanary(Each.ToString(), Failure.Message, Failure.ToString());
                Sentences.Add(Failure.Message);
            }
            Assert.That(Sentences, Has.Count.EqualTo(Enum.GetValues<TransferProblem>().Length), "one sentence each");
        }

        private static TransferFailure? Try(Func<TransferFailure> Make)
        {
            try
            {
                return Make();
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        [Test]
        public void NoReadOrUnlock_EverPutsTheCanariesIntoARefusal_AResultOrTheConsole()
        {
            byte[] Bytes_ = Encrypted(Package(Email(), Ntfy(), SmtpSecret(), TokenSecret(), TopicSecret()));
            var Results = new List<TransferReadResult>
            {
                Read(Bytes_, "wrong passphrase here"),
                Read(WithMember(Bytes_, "tag", "\"" + FlipBase64(MemberOf(Bytes_, "tag")) + "\""), PassphraseCanary),
                Read(Bytes_, PassphraseCanary),
                Read(Bytes_.AsSpan(0, Bytes_.Length / 2).ToArray()),
                TransferPackageReader.Open(Bytes_)
            };
            foreach (TransferReadResult Result in Results)
            {
                AssertNoCanary("a result", Result.ToString(), Result.Failure?.Message ?? string.Empty, Result.Locked?.ToString() ?? string.Empty, Result.Package?.ToString() ?? string.Empty);
                foreach (TransferEntry Entry in Result.Package?.Entries ?? Array.Empty<TransferEntry>())
                {
                    AssertNoCanary("an entry's ToString", Entry.ToString());
                }
            }
            Assert.That(Results[2].Package!.Entries.Where(E => E.IsSecret).Select(E => E.Text), Is.EqualTo(new[] { SmtpCanary, TokenCanary, TopicCanary }));
        }

        // a stream of one repeated byte that counts what it served
        private sealed class CountingStream : Stream
        {
            private readonly long Available;
            private readonly long? ClaimedLength;
            internal long Served;
            internal CountingStream(long Available, bool CanSeek, long? Claimed = null)
            {
                this.Available = Available;
                this.CanSeek_ = CanSeek;
                ClaimedLength = Claimed;
            }
            private readonly bool CanSeek_;
            public override bool CanRead => true;
            public override bool CanSeek => CanSeek_;
            public override bool CanWrite => false;
            public override long Length => CanSeek_ ? ClaimedLength ?? Available : throw new NotSupportedException();
            public override long Position { get => Served; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] Buffer, int Offset, int Count)
            {
                int Given = (int)Math.Min(Count, Available - Served);
                Buffer.AsSpan(Offset, Given).Fill((byte)' ');
                Served += Given;
                return Given;
            }
            public override long Seek(long Offset, SeekOrigin Origin) => throw new NotSupportedException();
            public override void SetLength(long Value) => throw new NotSupportedException();
            public override void Write(byte[] Buffer, int Offset, int Count) => throw new NotSupportedException();
        }

        private sealed class FailingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] Buffer, int Offset, int Count) => throw new IOException("Could not read /secret/path/package.json");
            public override long Seek(long Offset, SeekOrigin Origin) => throw new NotSupportedException();
            public override void SetLength(long Value) => throw new NotSupportedException();
            public override void Write(byte[] Buffer, int Offset, int Count) => throw new NotSupportedException();
        }
    }
}
