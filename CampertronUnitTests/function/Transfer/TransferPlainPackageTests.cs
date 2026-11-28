using System.Text;
using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // The plain envelope (D132 point 6; v0.4.0-settings-transfer.md §Plain envelope, §Integrity and authenticated encryption,
    // §Limits): a secret-free package, readable, with a SHA-256 for each text and one for the list that detect corruption only
    public class TransferPlainPackageTests : TransferTestBase
    {
        [Test]
        public void APackageWithoutEntries_IsNeitherWrittenNorRead()
        {
            // the design: "A package has at least one entry" (§Entry kinds)
            Assert.That(TransferPackage.Create(ExportedBy, Array.Empty<(TransferEntryKind, string, string)>(), out TransferFailure? Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.NoEntries));
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(Array.Empty<(string, string, string)>())))), Is.EqualTo(TransferProblem.NoEntries));
        }

        [Test]
        public void OneEntry_RoundTrips_AndIsReadAsPlain()
        {
            byte[] Bytes_ = Plain(Package(General()));
            TransferReadResult Read_ = Read(Bytes_);
            Assert.That(Read_.Failure, Is.Null);
            Assert.That(Read_.Package!.Encrypted, Is.False);
            Assert.That(Read_.Package.FormatVersion, Is.EqualTo(1));
            Assert.That(Read_.Package.ExportedBy, Is.EqualTo(ExportedBy));
            Assert.That(Contents(Read_.Package), Is.EqualTo(new[] { General() }));
            Assert.That(Derivations, Is.Zero);
        }

        [Test]
        public void EveryNonSecretKind_AndSeveralSearches_RoundTrip_InTheWritersOrder()
        {
            TransferPackage Given = Package(Search("b.yaml"), Ntfy(), Search("A.yaml"), Email(), Search("a (2).yaml"), General());
            TransferPackage Read_ = Read(Plain(Given)).Package!;
            // general, email, ntfy, then the searches ignoring case and then ordinally
            Assert.That(Read_.Entries.Select(E => E.Name), Is.EqualTo(new[] { "General.yaml", "Email.yaml", "Ntfy.yaml", "a (2).yaml", "A.yaml", "b.yaml" }));
            Assert.That(Contents(Read_), Is.EqualTo(Contents(Given)));
        }

        [Test]
        public void TheSameChoices_GiveTheSameBytes_AndTheFormatsMembersInTheDesignsOrder()
        {
            TransferPackage First = Package(General(), Email(), Search("Trip.yaml"));
            TransferPackage Second = Package(Search("Trip.yaml"), Email(), General());
            byte[] A = Plain(First);
            Assert.That(Plain(Second), Is.EqualTo(A));
            Assert.That(Plain(Read(A).Package!), Is.EqualTo(A), "a package read and written again is the same bytes");
            string T = Text(A);
            Assert.That(T, Does.StartWith("{\n  \"campertronSettingsPackage\": 1,\n  \"protection\": \"none\",\n  \"exportedBy\": \"0.4.0\",\n  \"payload\": {\n    \"entries\": [\n      {\n        \"kind\": \"general\",\n        \"name\": \"General.yaml\",\n        \"text\": "));
            Assert.That(T, Does.Not.Contain("\r"), "a line feed on every platform");
            Assert.That(A.All(B => B < 0x80), Is.True, "every character outside ASCII is escaped");
            Assert.That(T.IndexOf("\"manifestSha256\"", StringComparison.Ordinal), Is.GreaterThan(T.LastIndexOf("\"sha256\"", StringComparison.Ordinal)));
        }

        [Test]
        public void ATextKeepsItsByteOrderMark_LineEndings_AndCharactersOutsideAscii()
        {
            string Odd = "﻿# Trip été \U0001F3D5\r\ndisplayName: 'Café <&>'\r\n";
            TransferPackage Read_ = Read(Plain(Package(Search("Trip.yaml", Odd)))).Package!;
            Assert.That(Read_.Entries.Single().Text, Is.EqualTo(Odd));
        }

        [Test]
        public void EachEntrysSha256_IsItsTextsUtf8Hash_AndTheManifestTheDesignsLines()
        {
            byte[] Bytes_ = Plain(Package(General(), Search("Trip.yaml")));
            TransferPackage Read_ = Read(Bytes_).Package!;
            foreach (TransferEntry Entry in Read_.Entries)
            {
                Assert.That(Entry.Sha256, Is.EqualTo(Sha(Entry.Text)));
            }
            string Expected = ManifestOf(1, Read_.Entries.Select(E => (TransferFormat.KindText(E.Kind), E.Name, E.Sha256)));
            Assert.That(MemberOf(Bytes_, "payload.manifestSha256"), Is.EqualTo(Expected));
            // the test's own writer gives a package the reader accepts: the format is the design's, not only the writer's
            var Raw = new[] { ("general", "General.yaml", General().Item3), ("search", "Trip.yaml", Search("Trip.yaml").Item3) };
            Assert.That(Read(Bytes(PlainJson(PayloadJson(Raw)))).Failure, Is.Null);
        }

        [Test]
        public void AChangedText_Name_Kind_Order_OrHash_IsDamaged()
        {
            var Raw = new[] { ("general", "General.yaml", "outputTo: Console\n"), ("search", "A.yaml", "displayName: A\n"), ("search", "B.yaml", "displayName: B\n") };
            string Manifest = ManifestOf(1, Raw.Select(E => (E.Item1, E.Item2, Sha(E.Item3))));
            // each entry's hash covers its text
            string ChangedText = PlainJson("{\"entries\":[" + EntryJson("general", "General.yaml", "outputTo: Email\n", Sha("outputTo: Console\n")) + "," + EntryJson("search", "A.yaml", "displayName: A\n") + "," + EntryJson("search", "B.yaml", "displayName: B\n") + "],\"manifestSha256\":\"" + Manifest + "\"}");
            Assert.That(Problem(Bytes(ChangedText)), Is.EqualTo(TransferProblem.Damaged));
            // the manifest covers the names, the order and the count (each entry's own hash still right)
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(new[] { Raw[0], ("search", "C.yaml", "displayName: A\n"), Raw[2] }, Manifest)))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(new[] { Raw[0], Raw[2], Raw[1] }, Manifest)))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(new[] { Raw[0], Raw[1] }, Manifest)))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(new[] { ("email", "Email.yaml", "outputTo: Console\n"), Raw[1], Raw[2] }, Manifest)))), Is.EqualTo(TransferProblem.Damaged));
            // the manifest itself, and a hash in upper case
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(Raw, new string('0', 64))))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(Raw, Manifest.ToUpperInvariant())))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson("{\"entries\":[" + EntryJson("general", "General.yaml", "x: 1\n", Sha("x: 1\n").ToUpperInvariant()) + "],\"manifestSha256\":\"" + ManifestOf(1, new[] { ("general", "General.yaml", Sha("x: 1\n").ToUpperInvariant()) }) + "\"}"))), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(Bytes(PlainJson(PayloadJson(Raw)))), Is.Null, "the same package unchanged is read");
        }

        [Test]
        public void CorruptionOfAnyByte_IsRefused_NeverReadInPart()
        {
            TransferPackage Given = Package(General(), Email(), Search("Trip.yaml"));
            byte[] Good = Plain(Given);
            // exportedBy is information only and outside both checksums (§Integrity and authenticated encryption: the
            // manifest's lines are the version, the count and each entry's kind, name and hash)
            int ExportedByAt = Text(Good).IndexOf("\"exportedBy\": \"", StringComparison.Ordinal) + "\"exportedBy\": \"".Length;
            var Problems = new HashSet<TransferProblem>();
            for (int At = 0; At < Good.Length; At++)
            {
                byte[] Bad = (byte[])Good.Clone();
                Bad[At] = Bad[At] == (byte)'x' ? (byte)'y' : (byte)'x';
                TransferReadResult Result = Read(Bad);
                if (Result.Package != null)
                {
                    Assert.That(At, Is.InRange(ExportedByAt, ExportedByAt + ExportedBy.Length - 1), $"a change at byte {At} was read as a package");
                    Assert.That(Contents(Result.Package), Is.EqualTo(Contents(Given)));
                    continue;
                }
                Problems.Add(Result.Failure!.Problem);
            }
            Assert.That(Problems, Is.SubsetOf(new[] { TransferProblem.Damaged, TransferProblem.NotAPackage, TransferProblem.NameRefused, TransferProblem.UnknownKind, TransferProblem.NewerFormat }));
            Assert.That(Problems, Does.Contain(TransferProblem.Damaged));
        }

        [Test]
        public void TruncatedInput_AtManyLengths_IsRefused_DamagedOnceItNamesTheFormat()
        {
            byte[] Good = Plain(Package(General(), Search("Trip.yaml")));
            int Named = Text(Good).IndexOf("\"campertronSettingsPackage\"", StringComparison.Ordinal) + "\"campertronSettingsPackage\"".Length;
            foreach (int Length in new[] { 1, 2, 10, Named - 1, Named, Named + 3, Good.Length / 3, Good.Length / 2, Good.Length - 30, Good.Length - 2, Good.Length - 1 })
            {
                Assert.That(Problem(Good.AsSpan(0, Length).ToArray()), Is.EqualTo(Length < Named ? TransferProblem.NotAPackage : TransferProblem.Damaged), $"{Length} bytes");
            }
        }

        [Test]
        public void TrailingData_IsRefused_ButTrailingWhiteSpaceIsJsons()
        {
            byte[] Good = Plain(Package(General()));
            Assert.That(Problem(Good.Concat(Bytes("\n  \n")).ToArray()), Is.Null);
            foreach (string Tail in new[] { "x", "{}", "\n{\"campertronSettingsPackage\":1}", "\0", ",", "]" })
            {
                Assert.That(Problem(Good.Concat(Bytes(Tail)).ToArray()), Is.EqualTo(TransferProblem.Damaged), Tail);
            }
        }

        [Test]
        public void InvalidUtf8_AByteOrderMark_AndALoneSurrogate_AreRefused()
        {
            byte[] Good = Plain(Package(General()));
            Assert.That(Problem(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Good).ToArray()), Is.EqualTo(TransferProblem.Damaged));
            byte[] Bad = Changed(Good, "outputTo", "outputTé");
            int At = Array.IndexOf(Bad, (byte)0xC3);
            Bad[At + 1] = 0x28;
            Assert.That(Problem(Bad), Is.EqualTo(TransferProblem.Damaged));
            Assert.That(Problem(new byte[] { 0xFF, 0xFE, 0x7B, 0x00 }), Is.EqualTo(TransferProblem.NotAPackage));
            // an escaped lone surrogate in a text or a name
            var Raw = new[] { ("general", "General.yaml", "x: 1\n") };
            string Json = PlainJson(PayloadJson(Raw)).Replace("x: 1\\n", "x: \\uD800\\n", StringComparison.Ordinal);
            Assert.That(Json, Does.Contain("\\uD800"));
            Assert.That(Problem(Bytes(Json)), Is.EqualTo(TransferProblem.Damaged));
        }

        // a plain package of search entries filled with letters to exactly Size bytes, or null if the entries can't reach it
        private static TransferPackage Sized(int Size, int Entries)
        {
            var Texts = Enumerable.Range(0, Entries).Select(_ => 1).ToArray();
            TransferPackage Make() => Package(Texts.Select((Length, At) => Search($"s{At:D3}.yaml", new string('a', Length))).ToArray());
            int Overhead = Plain(Make()).Length - Texts.Sum();
            int Remaining = Size - Overhead;
            for (int At = 0; At < Entries; At++)
            {
                Texts[At] = Math.Min(TransferFormat.MaxTextBytes, Remaining - (Entries - At - 1));
                Remaining -= Texts[At];
            }
            Assert.That(Remaining, Is.Zero, "the entries can't reach the size");
            return Make();
        }

        [Test]
        public void APackageOfExactlyOneMebibyte_IsWrittenAndRead_AndOneByteMoreIsTooLarge()
        {
            TransferPackage AtLimit = Sized(TransferFormat.MaxPackageBytes, 17);
            byte[] Bytes_ = Plain(AtLimit);
            Assert.That(Bytes_.Length, Is.EqualTo(1_048_576));
            Assert.That(Read(Bytes_).Package!.Entries, Has.Count.EqualTo(17));
            // one byte more: the writer refuses it, and the reader refuses it from its length alone (a space JSON allows)
            TransferPackage Over = Sized(TransferFormat.MaxPackageBytes + 1, 17);
            Assert.That(TransferPackageWriter.WritePlain(Over).Failure!.Problem, Is.EqualTo(TransferProblem.TooLarge));
            Assert.That(Problem(Bytes_.Append((byte)' ').ToArray()), Is.EqualTo(TransferProblem.TooLarge));
        }

        [Test]
        public void TwoHundredFiftySixEntries_AreRead_AndTheNextIsRefused_BeforeAnyHash()
        {
            var Entries = Enumerable.Range(0, 256).Select(At => Search($"s{At:D3}.yaml", "x: 1\n")).ToArray();
            Assert.That(Read(Plain(Package(Entries))).Package!.Entries, Has.Count.EqualTo(256));
            var More = Entries.Append(Search("s256.yaml", "x: 1\n")).ToArray();
            Assert.That(TransferPackage.Create(ExportedBy, More, out TransferFailure? Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.TooManyEntries));
            // read: 257 entries with wrong hashes and a wrong manifest are refused for their count, not their hashes
            string Json = PlainJson("{\"entries\":[" + string.Join(",", Enumerable.Range(0, 257).Select(At => EntryJson("search", $"s{At:D3}.yaml", "x: 1\n", new string('0', 64)))) + "],\"manifestSha256\":\"" + new string('0', 64) + "\"}");
            Assert.That(Problem(Bytes(Json)), Is.EqualTo(TransferProblem.TooManyEntries));
        }

        [Test]
        public void AnEntryOfExactly64KiB_IsRead_AndOneByteMoreIsRefused_BeforeItsHash()
        {
            string AtLimit = new string('a', TransferFormat.MaxTextBytes - 2) + "é";
            Assert.That(Encoding.UTF8.GetByteCount(AtLimit), Is.EqualTo(65_536));
            Assert.That(Read(Plain(Package(Search("Big.yaml", AtLimit)))).Package!.Entries.Single().Text, Is.EqualTo(AtLimit));
            string Over = AtLimit + "a";
            Assert.That(TransferPackage.Create(ExportedBy, new[] { Search("Big.yaml", Over) }, out TransferFailure? Failure), Is.Null);
            Assert.That(Failure!.Problem, Is.EqualTo(TransferProblem.EntryTooLarge));
            Assert.That(Failure.Message, Does.Contain("Big.yaml"));
            string Json = PlainJson("{\"entries\":[" + EntryJson("search", "Big.yaml", Over, new string('0', 64)) + "],\"manifestSha256\":\"" + new string('0', 64) + "\"}");
            Assert.That(Problem(Bytes(Json)), Is.EqualTo(TransferProblem.EntryTooLarge));
        }

        [Test]
        public void TheChecksum_IsCalledCorruptionDetection_NeverAuthentication()
        {
            // someone who changes a plain package can recompute both hashes: the reader then reads it, as the design says
            var Raw = new[] { ("general", "General.yaml", "outputTo: Email\n") };
            Assert.That(Read(Bytes(PlainJson(PayloadJson(Raw)))).Package, Is.Not.Null);
            foreach (TransferProblem Each in Enum.GetValues<TransferProblem>().Where(P => P <= TransferProblem.EncryptionUnavailable))
            {
                string Message = TransferWording.Package(Each).Message;
                Assert.That(Message, Does.Not.Contain("authentic").IgnoreCase.And.Not.Contain("verified").IgnoreCase.And.Not.Contain("signed").IgnoreCase);
            }
        }
    }
}
