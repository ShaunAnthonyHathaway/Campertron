using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Transfer
{
    // One entry of a package (v0.4.0-settings-transfer.md §Entry kinds): a kind, a name, a text and the SHA-256 of the text's
    // UTF-8 bytes. Only TransferPackage.Create and the reader make one, after the rules passed. ToString never shows the text,
    // which can be a secret.
    internal sealed class TransferEntry
    {
        internal TransferEntry(TransferEntryKind Kind, string Name, string Text, string Sha256)
        {
            this.Kind = Kind;
            this.Name = Name;
            this.Text = Text;
            this.Sha256 = Sha256;
        }
        internal TransferEntryKind Kind { get; }
        internal string Name { get; }
        internal string Text { get; }
        internal string Sha256 { get; }
        internal bool IsSecret => TransferFormat.IsSecret(Kind);
        public override string ToString() => TransferFormat.KindText(Kind) + " " + (IsSecret ? "(secret)" : Name);
    }

    // A settings-transfer package in memory (D132; v0.4.0-settings-transfer.md §Shared package model): the format version,
    // the exporting Campertron's version, whether it was read from (or is to be written as) the encrypted envelope, and its
    // entries. One model for Export, Import and, later, Beam; nothing of it is ever extracted to a disk, and nothing here reads
    // or writes a file. A package with a secret entry is always the encrypted form.
    internal sealed class TransferPackage
    {
        private TransferPackage(int FormatVersion, string ExportedBy, bool Encrypted, IReadOnlyList<TransferEntry> Entries)
        {
            this.FormatVersion = FormatVersion;
            this.ExportedBy = ExportedBy;
            this.Encrypted = Encrypted;
            this.Entries = Entries;
        }
        internal int FormatVersion { get; }
        internal string ExportedBy { get; }
        // read from the encrypted envelope (a package built for writing says whether it holds a secret instead)
        internal bool Encrypted { get; }
        internal IReadOnlyList<TransferEntry> Entries { get; }
        internal bool HasSecret => Entries.Any(E => E.IsSecret);
        public override string ToString() => $"TransferPackage {FormatVersion} by {ExportedBy}: {Entries.Count} entries" + (Encrypted ? ", encrypted" : string.Empty);

        // an entry as given to Create or as the reader found it, before the rules
        internal readonly record struct RawEntry(string Kind, string Name, string Text, string? Sha256)
        {
            public override string ToString() => Kind + " " + Name;
        }

        // A package of the given entries for writing, in the writer's order: general, email, ntfy, the searches by name (ignoring
        // case, then ordinally, as ConfigFiles.SortedSearchFileNames orders them), then the secrets in the table's order. Every
        // rule of a read applies (CheckEntries), so the writer can't make a package the reader refuses; a package with a secret
        // must be written encrypted.
        internal static TransferPackage? Create(string ExportedBy, IEnumerable<(TransferEntryKind Kind, string Name, string Text)> Entries, out TransferFailure? Failure)
        {
            if (!TransferFormat.IsExportedBy(ExportedBy))
            {
                throw new ArgumentException("exportedBy is 1 to 64 letters, digits and . - +.", nameof(ExportedBy));
            }
            List<RawEntry> Raw = Entries
                .OrderBy(E => (int)E.Kind)
                .ThenBy(E => E.Kind == TransferEntryKind.Search ? E.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(E => E.Kind == TransferEntryKind.Search ? E.Name : string.Empty, StringComparer.Ordinal)
                .Select(E => new RawEntry(TransferFormat.KindText(E.Kind), E.Name, E.Text, null)).ToList();
            if (Raw.Count > TransferFormat.MaxEntries)
            {
                Failure = TransferWording.Package(TransferProblem.TooManyEntries);
                return null;
            }
            bool HasSecret = Raw.Any(E => TransferFormat.KindOf(E.Kind) is TransferEntryKind K && TransferFormat.IsSecret(K));
            List<TransferEntry>? Checked = CheckEntries(TransferFormat.Version, Raw, HasSecret, null, out Failure);
            return Checked == null ? null : new TransferPackage(TransferFormat.Version, ExportedBy, HasSecret, Checked);
        }

        // a package the reader read and checked
        internal static TransferPackage FromRead(int FormatVersion, string ExportedBy, bool Encrypted, List<TransferEntry> Entries) =>
            new TransferPackage(FormatVersion, ExportedBy, Encrypted, Entries);

        // The rules of a package's entries, in the read order of v0.4.0-settings-transfer.md §Limits after the count: each
        // entry's kind, name and length; each text's SHA-256 (Sha256 given: the reader); the manifest (given: the reader); the
        // secrets' rules. The first refusal ends it: the package is refused as a whole, never read in part.
        internal static List<TransferEntry>? CheckEntries(int FormatVersion, IReadOnlyList<RawEntry> Raw, bool Encrypted, string? Manifest, out TransferFailure? Failure)
        {
            Failure = null;
            if (Raw.Count == 0)
            {
                Failure = TransferWording.Package(TransferProblem.NoEntries);
                return null;
            }
            if (Raw.Count > TransferFormat.MaxEntries)
            {
                Failure = TransferWording.Package(TransferProblem.TooManyEntries);
                return null;
            }
            var Kinds = new TransferEntryKind[Raw.Count];
            var Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int At = 0; At < Raw.Count; At++)
            {
                // the kind: allow-listed, and a secret only in the encrypted form
                if (TransferFormat.KindOf(Raw[At].Kind) is not TransferEntryKind Kind)
                {
                    Failure = TransferWording.Package(TransferProblem.UnknownKind);
                    return null;
                }
                if (TransferFormat.IsSecret(Kind) && !Encrypted)
                {
                    Failure = TransferWording.Package(TransferProblem.SecretNotEncrypted);
                    return null;
                }
                Kinds[At] = Kind;
                // the name: never a path; a settings or secret name exactly, a search name exactly what SearchFileName.Check
                // gives for it (which refuses the settings files' names, Web.yaml among them, in any case)
                string Name = Raw[At].Name;
                string? Fixed = TransferFormat.FixedName(Kind);
                bool NameAccepted = Fixed != null
                    ? string.Equals(Name, Fixed, StringComparison.Ordinal)
                    : SearchFileName.Check(Name, out string Checked) == null && string.Equals(Checked, Name, StringComparison.Ordinal);
                if (!NameAccepted)
                {
                    Failure = TransferWording.Package(TransferProblem.NameRefused);
                    return null;
                }
                // names unique ignoring case: two that differ only in case are the same file on Windows and macOS
                if (!Names.Add(Name))
                {
                    Failure = TransferWording.Entry(TransferProblem.DuplicateName, Name);
                    return null;
                }
                // the length, before any hash or parse
                int? Length = TransferFormat.Utf8Length(Raw[At].Text);
                if (Length == null)
                {
                    Failure = TransferWording.Package(TransferProblem.Damaged);
                    return null;
                }
                if (TransferFormat.IsSecret(Kind))
                {
                    if (Length < 1 || Length > TransferFormat.MaxSecretBytes)
                    {
                        Failure = TransferWording.Secret(TransferProblem.SecretUnusable, TransferFormat.SecretOf(Kind));
                        return null;
                    }
                }
                else if (Length > TransferFormat.MaxTextBytes)
                {
                    Failure = TransferWording.Entry(TransferProblem.EntryTooLarge, Name);
                    return null;
                }
            }
            // each text's SHA-256: 64 lower-case hexadecimal digits, the hash of its UTF-8 bytes
            var Hashes = new string[Raw.Count];
            for (int At = 0; At < Raw.Count; At++)
            {
                string Computed = TransferFormat.Sha256Hex(Raw[At].Text)!;
                if (Raw[At].Sha256 is string Given && !(TransferFormat.IsSha256Hex(Given) && string.Equals(Given, Computed, StringComparison.Ordinal)))
                {
                    Failure = TransferWording.Package(TransferProblem.Damaged);
                    return null;
                }
                Hashes[At] = Computed;
            }
            // the manifest: the list as a whole (a changed kind, name or order, a missing or added entry)
            if (Manifest != null)
            {
                string Expected = TransferFormat.ManifestSha256(FormatVersion, Raw.Select((E, At) => (E.Kind, E.Name, Hashes[At])).ToList());
                if (!(TransferFormat.IsSha256Hex(Manifest) && string.Equals(Manifest, Expected, StringComparison.Ordinal)))
                {
                    Failure = TransferWording.Package(TransferProblem.Damaged);
                    return null;
                }
            }
            // the secrets' rules: one SecretFile would read (no line break, no NUL, no byte-order mark, 1 to 4,096 bytes),
            // and the settings file it belongs to in the same package
            for (int At = 0; At < Raw.Count; At++)
            {
                if (!TransferFormat.IsSecret(Kinds[At]))
                {
                    continue;
                }
                TransferSecretKind Secret = TransferFormat.SecretOf(Kinds[At]);
                if (!IsUsableSecret(Raw[At].Text))
                {
                    Failure = TransferWording.Secret(TransferProblem.SecretUnusable, Secret);
                    return null;
                }
                if (!Kinds.Contains(TransferFormat.OwnerOf(Secret)))
                {
                    Failure = TransferWording.Secret(TransferProblem.SecretWithoutItsFile, Secret);
                    return null;
                }
            }
            return Raw.Select((E, At) => new TransferEntry(Kinds[At], E.Name, E.Text, Hashes[At])).ToList();
        }

        // a secret SecretFile.FromBytes reads back as exactly this text: 1 to 4,096 bytes of strict UTF-8 without a byte-order
        // mark, a NUL or any line break (FromBytes takes one line break off the end, so a text that ends in one is refused too)
        internal static bool IsUsableSecret(string Text)
        {
            byte[] Bytes;
            try
            {
                Bytes = TransferFormat.StrictUtf8.GetBytes(Text);
            }
            catch (System.Text.EncoderFallbackException)
            {
                return false;
            }
            try
            {
                return Bytes.Length >= 1 && Bytes.Length <= TransferFormat.MaxSecretBytes
                    && SecretFile.FromBytes(Bytes, out string? Read) == null && string.Equals(Read, Text, StringComparison.Ordinal);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(Bytes);
            }
        }
    }
}
