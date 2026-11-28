using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CampertronLibrary.function.Transfer;

namespace CampertronUnitTests.function.Transfer
{
    // Shared by the settings-transfer package's tests (v0.4.0's phase 16a; v0.4.0-settings-transfer.md §Test obligations):
    // canary secrets and a canary passphrase searched for in every output, a counter of key derivations, standard output and
    // error captured (nothing may reach them), and an independent second writer of the format from the design's text (the
    // oracle the reader is tested against). Nothing uses LocalApplicationData, the network or the embedded database.
    public abstract class TransferTestBase
    {
        // unique synthetic values that must never appear where they do not belong
        internal const string SmtpCanary = "CANARY-smtp-3f9a1c7e5b2d4086";
        internal const string TokenCanary = "CANARY-token-8e2b6d0a4c1f9375";
        internal const string TopicCanary = "campertron-canarytopic7q2w9e4r";
        internal const string PassphraseCanary = "CANARY-passphrase-5d8c1a6f";
        internal const string ExportedBy = "0.4.0";

        protected StringWriter Out = null!;
        protected StringWriter Err = null!;
        private TextWriter? OriginalOut;
        private TextWriter? OriginalErr;
        private Action OriginalDerived = null!;
        protected int Derivations;

        [SetUp]
        public void CaptureTransfer()
        {
            Out = new StringWriter();
            Err = new StringWriter();
            OriginalOut = Console.Out;
            OriginalErr = Console.Error;
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalDerived = TransferCrypto.Derived;
            Derivations = 0;
            TransferCrypto.Derived = () => Interlocked.Increment(ref Derivations);
        }

        [TearDown]
        public void RestoreTransfer()
        {
            TransferCrypto.Derived = OriginalDerived;
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            if (OriginalErr != null)
            {
                Console.SetError(OriginalErr);
            }
            // nothing of the transfer writes to the console, and no canary ever reaches it
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "the transfer core wrote to standard output or error");
            Out.Dispose();
            Err.Dispose();
        }

        internal static readonly string[] Canaries = { SmtpCanary, TokenCanary, TopicCanary, PassphraseCanary };

        internal static void AssertNoCanary(string What, params string[] Texts)
        {
            foreach (string Text in Texts)
            {
                foreach (string Canary in Canaries)
                {
                    Assert.That(Text, Does.Not.Contain(Canary), $"{What} holds a canary");
                }
            }
        }

        internal static void AssertNoCanary(string What, byte[] Bytes, params string[] Values)
        {
            foreach (string Canary in Values.Length > 0 ? Values : Canaries)
            {
                Assert.That(TransferSelfTest.Holds(Bytes, Canary), Is.False, $"{What} holds a canary in some encoding");
            }
        }

        // ---- packages through the production writer and reader ----

        internal static TransferPackage Package(params (TransferEntryKind Kind, string Name, string Text)[] Entries)
        {
            TransferPackage? Package_ = TransferPackage.Create(ExportedBy, Entries, out TransferFailure? Failure);
            Assert.That(Failure, Is.Null, "the test's package was refused");
            return Package_!;
        }

        internal static (TransferEntryKind, string, string) General(string Text = "outputTo: Console\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n") => (TransferEntryKind.General, "General.yaml", Text);
        internal static (TransferEntryKind, string, string) Email(string Text = "smtpServer: mail.example.com\nsmtpPort: 587\n") => (TransferEntryKind.Email, "Email.yaml", Text);
        internal static (TransferEntryKind, string, string) Ntfy(string Text = "enabled: true\nserver: https://ntfy.example.com\n") => (TransferEntryKind.Ntfy, "Ntfy.yaml", Text);
        internal static (TransferEntryKind, string, string) Search(string Name, string Text = "displayName: Trip\ncampgroundID: '232447'\n") => (TransferEntryKind.Search, Name, Text);
        internal static (TransferEntryKind, string, string) SmtpSecret(string Value = SmtpCanary) => (TransferEntryKind.SecretSmtpPassword, "smtpPassword", Value);
        internal static (TransferEntryKind, string, string) TokenSecret(string Value = TokenCanary) => (TransferEntryKind.SecretNtfyToken, "ntfyToken", Value);
        internal static (TransferEntryKind, string, string) TopicSecret(string Value = TopicCanary) => (TransferEntryKind.SecretNtfyTopic, "ntfyTopic", Value);

        internal static byte[] Plain(TransferPackage Package_)
        {
            TransferWriteResult Written = TransferPackageWriter.WritePlain(Package_);
            Assert.That(Written.Failure, Is.Null);
            return Written.Bytes!;
        }

        internal static byte[] Encrypted(TransferPackage Package_, string Passphrase = PassphraseCanary)
        {
            TransferWriteResult Written = TransferPackageWriter.WriteEncrypted(Package_, Passphrase);
            Assert.That(Written.Failure, Is.Null);
            return Written.Bytes!;
        }

        // the result of reading a package completely: Open, and with a passphrase Unlock
        internal static TransferReadResult Read(byte[] Bytes, string? Passphrase = null)
        {
            TransferReadResult Opened = TransferPackageReader.Open(Bytes);
            return Opened.Locked != null && Passphrase != null ? TransferPackageReader.Unlock(Opened.Locked, Passphrase) : Opened;
        }

        internal static TransferProblem? Problem(byte[] Bytes, string? Passphrase = null) => Read(Bytes, Passphrase).Failure?.Problem;

        internal static List<(TransferEntryKind, string, string)> Contents(TransferPackage Package_) =>
            Package_.Entries.Select(E => (E.Kind, E.Name, E.Text)).ToList();

        internal static string Text(byte[] Bytes) => Encoding.UTF8.GetString(Bytes);

        internal static byte[] Bytes(string Text) => new UTF8Encoding(false).GetBytes(Text);

        internal static byte[] Changed(byte[] Bytes_, string From, string To)
        {
            string Text_ = Text(Bytes_);
            Assert.That(Text_, Does.Contain(From), "the test's change does not apply");
            return Bytes(Text_.Replace(From, To, StringComparison.Ordinal));
        }

        // ---- an independent second writer of format 1, from the design's text (the oracle) ----

        internal static string Sha(string Text_) => System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text_)));

        internal static string Q(string Value) => "\"" + JsonEncodedText.Encode(Value).ToString() + "\"";

        // one entry as JSON; Sha256 null for the text's own
        internal static string EntryJson(string Kind, string Name, string Text_, string? Sha256 = null) =>
            "{" + Q("kind") + ":" + Q(Kind) + "," + Q("name") + ":" + Q(Name) + "," + Q("text") + ":" + Q(Text_) + "," + Q("sha256") + ":" + Q(Sha256 ?? Sha(Text_)) + "}";

        // the manifest's lines (§Integrity and authenticated encryption), hashed
        internal static string ManifestOf(int Version, IEnumerable<(string Kind, string Name, string Sha256)> Entries)
        {
            var List = Entries.ToList();
            var Lines = new StringBuilder();
            Lines.Append(Version).Append('\n').Append(List.Count).Append('\n');
            foreach (var E in List)
            {
                Lines.Append(E.Kind).Append('\n').Append(E.Name).Append('\n').Append(E.Sha256).Append('\n');
            }
            return Sha(Lines.ToString());
        }

        // a payload object; Manifest null for the entries' own
        internal static string PayloadJson(IEnumerable<(string Kind, string Name, string Text)> Entries, string? Manifest = null, string Extra = "")
        {
            var List = Entries.ToList();
            string ManifestValue = Manifest ?? ManifestOf(1, List.Select(E => (E.Kind, E.Name, Sha(E.Text))));
            return "{" + Q("entries") + ":[" + string.Join(",", List.Select(E => EntryJson(E.Kind, E.Name, E.Text))) + "]," + Q("manifestSha256") + ":" + Q(ManifestValue) + Extra + "}";
        }

        internal static string PlainJson(string Payload, string Version = "1", string Protection = "none", string ExportedBy_ = ExportedBy, string Extra = "") =>
            "{" + Q("campertronSettingsPackage") + ":" + Version + "," + Q("protection") + ":" + Q(Protection) + "," + Q("exportedBy") + ":" + Q(ExportedBy_) + "," + Q("payload") + ":" + Payload + Extra + "}";

        // the associated data as the design writes it
        internal static byte[] AssociatedDataOf(string Version, string ExportedBy_, string Iterations, string Salt, string Nonce) =>
            Encoding.UTF8.GetBytes(string.Join("", new[] { "campertron-settings-package", Version, "passphrase", ExportedBy_, "pbkdf2-sha256", Iterations, Salt, "aes-256-gcm", Nonce }.Select(L => L + "\n")));

        // An encrypted envelope written by the test itself with .NET's primitives: its plaintext any bytes, its header values
        // any text (the associated data built from them), so the reader can be given what the production writer never makes.
        internal static byte[] HandEncrypted(byte[] Plaintext, string Passphrase = PassphraseCanary, string ExportedBy_ = ExportedBy, string Version = "1",
            string KdfName = "pbkdf2-sha256", string Iterations = "600000", string CipherName = "aes-256-gcm", byte[]? Salt = null, byte[]? Nonce = null,
            Func<string, string>? Header = null)
        {
            Salt ??= RandomNumberGenerator.GetBytes(16);
            Nonce ??= RandomNumberGenerator.GetBytes(12);
            string Salt64 = System.Convert.ToBase64String(Salt);
            string Nonce64 = System.Convert.ToBase64String(Nonce);
            byte[] Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Passphrase.Normalize(NormalizationForm.FormKC)), Salt, 600000, HashAlgorithmName.SHA256, 32);
            byte[] Ciphertext = new byte[Plaintext.Length];
            byte[] Tag = new byte[16];
            using (var Gcm = new AesGcm(Key, 16))
            {
                Gcm.Encrypt(Nonce, Plaintext, Ciphertext, Tag, AssociatedDataOf(Version, ExportedBy_, Iterations, Salt64, Nonce64));
            }
            string Json = "{" + Q("campertronSettingsPackage") + ":" + Version + "," + Q("protection") + ":" + Q("passphrase") + "," + Q("exportedBy") + ":" + Q(ExportedBy_) + ","
                + Q("kdf") + ":{" + Q("name") + ":" + Q(KdfName) + "," + Q("iterations") + ":" + Iterations + "," + Q("salt") + ":" + Q(Salt64) + "},"
                + Q("cipher") + ":{" + Q("name") + ":" + Q(CipherName) + "," + Q("nonce") + ":" + Q(Nonce64) + "},"
                + Q("ciphertext") + ":" + Q(System.Convert.ToBase64String(Ciphertext)) + "," + Q("tag") + ":" + Q(System.Convert.ToBase64String(Tag)) + "}";
            return Bytes(Header == null ? Json : Header(Json));
        }

        internal static byte[] HandEncrypted(string PayloadJson_, string Passphrase = PassphraseCanary) => HandEncrypted(Bytes(PayloadJson_), Passphrase);

        // a string member's value in a package's text (the first one of that name)
        internal static string MemberOf(byte[] Bytes_, string Name)
        {
            using JsonDocument Document = JsonDocument.Parse(Bytes_);
            JsonElement Root = Document.RootElement;
            foreach (string Path_ in new[] { Name, "kdf." + Name, "cipher." + Name })
            {
                JsonElement At = Root;
                bool Found = true;
                foreach (string Part in Path_.Split('.'))
                {
                    if (!At.TryGetProperty(Part, out At))
                    {
                        Found = false;
                        break;
                    }
                }
                if (Found)
                {
                    return At.ValueKind == JsonValueKind.Number ? At.GetRawText() : At.GetString()!;
                }
            }
            throw new AssertionException($"{Name} is not in the package");
        }

        // a string as the production writer spells it in JSON (its default encoder escapes + < > & ' and all outside ASCII)
        internal static string Spelled(string Value) => JsonEncodedText.Encode(Value).ToString();

        // a package with one member's value replaced by another JSON text
        internal static byte[] WithMember(byte[] Bytes_, string Name, string NewJsonValue)
        {
            string Old = MemberOf(Bytes_, Name);
            string AsString = "\"" + Spelled(Old) + "\"";
            string Quoted = Text(Bytes_).Contains("\"" + Name + "\": " + AsString, StringComparison.Ordinal) ? AsString : Old;
            return Changed(Bytes_, "\"" + Name + "\": " + Quoted, "\"" + Name + "\": " + NewJsonValue);
        }

        internal static string FlipBase64(string Value)
        {
            byte[] Raw = System.Convert.FromBase64String(Value);
            Raw[Raw.Length / 2] ^= 0x01;
            return System.Convert.ToBase64String(Raw);
        }
    }

    // What a type's code refers to, read from its IL (the GUI tests' scanner, GuiTestHelpers, as the web tests copied it):
    // the members, and the string literals (ldstr), of the methods of the given types and of their nested types
    internal static class TransferIl
    {
        internal static List<MemberInfo> Members(IEnumerable<Type> Types)
        {
            var Found = new List<MemberInfo>();
            foreach (MethodBase Method in Methods(Types))
            {
                Scan(Method, Member => Found.Add(Member), null);
            }
            return Found;
        }

        internal static List<string> Literals(IEnumerable<Type> Types)
        {
            var Found = new List<string>();
            foreach (MethodBase Method in Methods(Types))
            {
                Scan(Method, null, Literal => Found.Add(Literal));
            }
            return Found;
        }

        internal static string Name(MemberInfo Member) => ((Member as Type)?.FullName ?? Member.DeclaringType?.FullName ?? "?") + "::" + Member.Name;

        private static IEnumerable<MethodBase> Methods(IEnumerable<Type> Types)
        {
            var All = new List<Type>();
            void Add(Type T)
            {
                All.Add(T);
                foreach (Type Nested in T.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    Add(Nested);
                }
            }
            foreach (Type T in Types.Where(t => !t.IsNested))
            {
                Add(T);
            }
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            return All.SelectMany(t => t.GetMethods(Declared).Cast<MethodBase>().Concat(t.GetConstructors(Declared)));
        }

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(F => (OpCode)F.GetValue(null)!).ToDictionary(O => O.Value);

        private static void Scan(MethodBase Method, Action<MemberInfo>? OnMember, Action<string>? OnLiteral)
        {
            byte[]? IL = Method.GetMethodBody()?.GetILAsByteArray();
            if (IL == null)
            {
                return;
            }
            Type[]? TypeArguments = Method.DeclaringType != null && Method.DeclaringType.IsGenericType ? Method.DeclaringType.GetGenericArguments() : null;
            Type[]? MethodArguments = Method.IsGenericMethod ? Method.GetGenericArguments() : null;
            int i = 0;
            while (i < IL.Length)
            {
                short Value = IL[i] == 0xFE ? (short)(0xFE00 | IL[i + 1]) : IL[i];
                i += IL[i] == 0xFE ? 2 : 1;
                OpCode Op = OpCodesByValue[Value];
                switch (Op.OperandType)
                {
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        if (OnMember != null)
                        {
                            MemberInfo? Member = null;
                            try
                            {
                                Member = Method.Module.ResolveMember(BitConverter.ToInt32(IL, i), TypeArguments, MethodArguments);
                            }
                            catch (ArgumentException)
                            {
                                // a generic context the scan does not reconstruct; not a library member
                            }
                            if (Member != null)
                            {
                                OnMember(Member);
                            }
                        }
                        i += 4;
                        break;
                    case OperandType.InlineString:
                        OnLiteral?.Invoke(Method.Module.ResolveString(BitConverter.ToInt32(IL, i)));
                        i += 4;
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                        i += 4 + 4 * BitConverter.ToInt32(IL, i);
                        break;
                    default:
                        i += 4;
                        break;
                }
            }
        }
    }
}
