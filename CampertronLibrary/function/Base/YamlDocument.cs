using System.Reflection;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CampertronLibrary.function.Base
{
    // Comment-preserving editing of Campertron's settings documents (General.yaml, Email.yaml, search files), in memory
    // only (decision D39, CM1 of GUI configuration management). It has no production caller yet: nothing in Campertron
    // writes through it, so today's writers still re-serialize (TD-37).
    //
    // How: YamlDotNet's normal parser (the one Campertron's settings loader uses, comments skipped) locates every key,
    // value and sequence item by its source range; comments are found in the gaps between those ranges. An edit is a
    // list of text-range replacements, so everything outside them stays exactly as it was: comments, blank lines, key
    // order, indentation, quoting, line breaks, a leading U+FEFF. Nothing is re-serialized and nothing is normalized:
    // what can't be edited safely is refused, with the reason. Before an edited text is returned it is parsed again, its
    // untouched text, keys, values, comments and line breaks are compared with the original, and it is loaded with
    // Campertron's own settings deserializer (Yaml.SettingsDeserializer) and compared with the model the caller intends.
    // Any failure returns no text.
    //
    // It never opens a file and never prints; it uses no settings or cache folder.
    internal static class YamlDocument
    {
        // Limits of this editor and, since D41 (TD-52), of Campertron's settings loader and its validation too
        // (Yaml.ScanSettings), so all three accept and refuse the same documents. Settings files are a few hundred
        // characters: the sample search is about 700 and General.yaml about 120.
        // 1 Mi UTF-16 code units bounds the text, every copy and every re-parse.
        internal const int MaxDocumentLength = 1_048_576;
        // bounds the parser's work and the event list; Campertron's files have well under 100 events
        internal const int MaxParserEvents = 100_000;
        // collections nested deeper than this are refused before the recursive tree walk and the recursive deserializer
        // see them; Campertron's files have 2 levels (the mapping and a list)
        internal const int MaxDepth = 32;
        // aliases are locked against editing anyway; the cap bounds the deserializer's work on what is kept
        internal const int MaxAliases = 100;
        internal const int MaxEdits = 256;

        private const string StandardTagPrefix = "tag:yaml.org,2002:";

        // ---- public entry points ----

        // Reads a document without changing it: what can be edited, where everything is, and why something can't be.
        internal static YamlDocumentAnalysis Analyze(string Text)
        {
            ArgumentNullException.ThrowIfNull(Text);
            var Problems = new List<YamlDocumentDiagnostic>();
            Doc? D = Read(Text, Problems);
            return Project(D, Problems);
        }

        // Applies the edits to the text and returns the edited text only if it passes every check, including a load with
        // Campertron's settings deserializer that must give exactly Intended (the model the caller expects the edited
        // document to hold). T is GeneralConfig, EmailConfig or CampertronConfig.
        internal static YamlDocumentResult Edit<T>(string Text, IReadOnlyList<YamlDocumentEdit> Edits, T Intended) where T : class
        {
            return Run(Text, Edits, Intended, null);
        }

        // The same checks for a text produced elsewhere: whether Candidate is exactly what these edits make of Text (for
        // example a file read back after it was written). Returns Candidate as the result's text if it is.
        internal static YamlDocumentResult VerifyCandidate<T>(string Text, IReadOnlyList<YamlDocumentEdit> Edits, T Intended, string Candidate) where T : class
        {
            ArgumentNullException.ThrowIfNull(Candidate);
            return Run(Text, Edits, Intended, Candidate);
        }

        // The top-level keys Campertron's settings deserializer reads for T, with their properties: every public property
        // with a public getter and setter, named by the camelCase convention the loader and the writers use.
        internal static IReadOnlyDictionary<string, PropertyInfo> ModelKeys(Type Model)
        {
            CheckModelType(Model);
            var Keys = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (PropertyInfo Property in Model.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(P => P.MetadataToken))
            {
                if (Property.GetGetMethod() != null && Property.GetSetMethod() != null && Property.GetIndexParameters().Length == 0)
                {
                    Keys.Add(CamelCaseNamingConvention.Instance.Apply(Property.Name), Property);
                }
            }
            return Keys;
        }

        private static void CheckModelType(Type Model)
        {
            if (Model != typeof(GeneralConfig) && Model != typeof(EmailConfig) && Model != typeof(CampertronConfig))
            {
                throw new ArgumentException($"{Model.Name} is not a Campertron settings model.", nameof(Model));
            }
        }

        private static YamlDocumentResult Run<T>(string Text, IReadOnlyList<YamlDocumentEdit> Edits, T Intended, string? Candidate) where T : class
        {
            ArgumentNullException.ThrowIfNull(Text);
            ArgumentNullException.ThrowIfNull(Edits);
            ArgumentNullException.ThrowIfNull(Intended);
            CheckModelType(typeof(T));
            if (Edits.Any(Edit => Edit == null))
            {
                throw new ArgumentException("An edit can't be null.", nameof(Edits));
            }
            IReadOnlyDictionary<string, PropertyInfo> Keys = ModelKeys(typeof(T));
            var Problems = new List<YamlDocumentDiagnostic>();
            Doc? D = Read(Text, Problems);
            if (D == null)
            {
                return Failed(Problems);
            }
            Plan? P = MakePlan(D, Edits, Keys, Problems);
            if (P == null)
            {
                return Failed(D.Warnings.Concat(UnknownKeys(D, Keys)).Concat(Problems));
            }
            string Output = Candidate ?? Apply(D.Text, P.Patches);
            Doc? Result = Check(D, P, Output, Problems);
            if (Result == null)
            {
                return Failed(D.Warnings.Concat(UnknownKeys(D, Keys)).Concat(Problems));
            }
            List<YamlDocumentDiagnostic> Warnings = D.Warnings.Concat(UnknownKeys(Result, Keys)).ToList();
            YamlDocumentDiagnostic? ModelProblem = CheckModel(Result, Intended, Keys);
            if (ModelProblem != null)
            {
                return Failed(Warnings.Append(ModelProblem));
            }
            return new YamlDocumentResult(true, Output, Warnings, P.Losses);
        }

        private static IEnumerable<YamlDocumentDiagnostic> UnknownKeys(Doc D, IReadOnlyDictionary<string, PropertyInfo> Keys)
        {
            List<string> Unknown = D.Props.Select(P => P.Key).Where(Key => !Keys.ContainsKey(Key)).ToList();
            if (Unknown.Count > 0)
            {
                yield return Warn(YamlDocumentCode.UnknownKeys, null,
                    "Campertron's settings loader does not accept these keys: " + string.Join(", ", Unknown) +
                    ". They are kept exactly as they are; the result was checked without them, so the loader still refuses the document until they are removed.");
            }
        }

        private static YamlDocumentResult Failed(IEnumerable<YamlDocumentDiagnostic> Diagnostics) =>
            new YamlDocumentResult(false, null, Diagnostics.ToList(), Array.Empty<YamlDocumentLoss>());

        // ---- the document ----

        private enum NodeKind { Scalar, Alias, Sequence, Mapping }

        private sealed class Node
        {
            internal NodeKind Kind;
            internal int Start;
            internal int End;
            internal ScalarStyle Style;
            internal string Value = string.Empty;
            internal bool Flow;
            internal int Open = -1;
            internal int Close = -1;
            internal int PropertiesEnd;
            internal bool Anchored;
            internal bool Tagged;
            internal readonly List<Node> Children = new();
        }

        private sealed class Item
        {
            internal Node Node = null!;
            internal int Line;
            internal int UnitStart;
            internal int UnitEnd;
            internal int UnitFirstLine;
        }

        private sealed class Sequence
        {
            internal bool Flow;
            internal readonly List<Item> Items = new();
            // block: the text before each '-' and between '-' and the first item's scalar
            internal string Indent = string.Empty;
            internal string DashGap = " ";
            // flow: the text after '[' and before ']', and between the first two items
            internal string Lead = string.Empty;
            internal string Trail = string.Empty;
            internal string Separator = ", ";
        }

        private sealed class Prop
        {
            internal string Key = string.Empty;
            internal Node KeyNode = null!;
            internal Node Value = null!;
            internal int Colon;
            internal int ValueStart;
            internal int ValueEnd;
            internal int LastContent;
            internal int FirstLine;
            internal int LastLine;
            internal YamlDocumentShape Shape;
            internal YamlDocumentLock Lock;
            internal Sequence? Seq;
            internal YamlDocumentComment? Inline;
            internal int FirstEvent;
            internal int LastEvent;
        }

        private sealed class Doc
        {
            internal string Text = string.Empty;
            internal int Bom;
            internal readonly List<ParsingEvent> Events = new();
            internal readonly List<YamlDocumentComment> Comments = new();
            internal readonly Dictionary<int, YamlDocumentComment> CommentAt = new();
            internal int[] LineStart = Array.Empty<int>();
            internal int[] LineContentEnd = Array.Empty<int>();
            internal int[] LineEnd = Array.Empty<int>();
            internal int Lf;
            internal int CrLf;
            internal int Cr;
            internal string Eol = "\n";
            internal readonly List<Prop> Props = new();
            internal readonly Dictionary<string, Prop> ByKey = new(StringComparer.Ordinal);
            internal string Indent = string.Empty;
            internal readonly List<YamlDocumentDiagnostic> Warnings = new();
            internal int Aliases;
            internal readonly List<Node> BlockScalars = new();

            internal int LineOf(int Index)
            {
                int Found = Array.BinarySearch(LineStart, Index);
                return Found >= 0 ? Found : Math.Max(0, ~Found - 1);
            }
            internal bool HasLineBreak(int Line) => LineEnd[Line] > LineContentEnd[Line];
            internal string Body => Text.Substring(Bom);
        }

        // a problem that ends the reading of a document
        private sealed class Refusal : Exception
        {
            internal Refusal(YamlDocumentDiagnostic Diagnostic) : base(Diagnostic.Message)
            {
                this.Diagnostic = Diagnostic;
            }
            internal YamlDocumentDiagnostic Diagnostic { get; }
        }

        private static Doc? Read(string Text, List<YamlDocumentDiagnostic> Problems)
        {
            try
            {
                return ReadOrRefuse(Text);
            }
            catch (Refusal R)
            {
                Problems.Add(R.Diagnostic);
                return null;
            }
        }

        private static Doc ReadOrRefuse(string Text)
        {
            if (Text.Length > MaxDocumentLength)
            {
                throw Refuse(YamlDocumentCode.TooLarge, null, 0, 0, $"The document has more than {MaxDocumentLength} characters.");
            }
            var D = new Doc { Text = Text, Bom = Text.Length > 0 && Text[0] == '\uFEFF' ? 1 : 0 };
            ReadLines(D);
            for (int i = D.Bom; i < Text.Length; i++)
            {
                char C = Text[i];
                if (char.IsHighSurrogate(C) && i + 1 < Text.Length && char.IsLowSurrogate(Text[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(C))
                {
                    throw RefuseAt(D, YamlDocumentCode.InvalidText, null, i, "The text has a lone UTF-16 surrogate, which no YAML file can hold.");
                }
                else if (C == '\u0085' || C == '\u2028' || C == '\u2029')
                {
                    // YamlDotNet reads these as line breaks, which this editor's line model does not
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLineBreak, null, i, "The text has a NEL, line separator or paragraph separator character.");
                }
            }
            try
            {
                var Reader = new Parser(new StringReader(D.Body));
                while (Reader.MoveNext())
                {
                    if (D.Events.Count == MaxParserEvents)
                    {
                        throw Refuse(YamlDocumentCode.TooManyEvents, null, 0, 0, $"The document has more than {MaxParserEvents} YAML events.");
                    }
                    D.Events.Add(Reader.Current!);
                }
            }
            catch (YamlException Ex)
            {
                // the exception's message can quote the document, so only its position is reported
                throw Refuse(YamlDocumentCode.Malformed, null, (int)Ex.Start.Line, (int)Ex.Start.Column, "The document is not valid YAML.");
            }
            catch (InvalidOperationException)
            {
                // YamlDotNet 16.3.0's scanner throws this for some malformed text (an unclosed flow list followed by
                // another line) instead of a YamlException; refused the same way (D41)
                Mark Last = D.Events.Count > 0 ? D.Events[^1].Start : Mark.Empty;
                throw Refuse(YamlDocumentCode.Malformed, null, (int)Last.Line, (int)Last.Column, "The document is not valid YAML.");
            }

            int At = 1;
            if (D.Events.Count < 3 || D.Events[At] is StreamEnd)
            {
                throw Refuse(YamlDocumentCode.NoContent, null, 0, 0, "The document has no settings: it is empty or contains only comments.");
            }
            var Start = (DocumentStart)D.Events[At++];
            var Leaves = new List<(int Start, int End)>();
            if (Idx(D, Start.End) > Idx(D, Start.Start))
            {
                Leaves.Add((Idx(D, Start.Start), Idx(D, Start.End)));
            }
            int RootEvent = At;
            Node Root = Build(D, ref At, 1, Leaves);
            var End = (DocumentEnd)D.Events[At++];
            if (D.Events[At] is not StreamEnd)
            {
                throw RefuseAt(D, YamlDocumentCode.MultipleDocuments, null, Idx(D, D.Events[At].Start), "The file holds more than one YAML document.");
            }
            if (Root.Kind != NodeKind.Mapping || Root.Flow || Root.Anchored || Root.Tagged)
            {
                throw RefuseAt(D, YamlDocumentCode.RootNotBlockMapping, null, Root.Start, "The document is not a plain block mapping of keys and values.");
            }
            ReadComments(D, Leaves);
            ReadProperties(D, Root, RootEvent);
            CheckLines(D, Start, End);
            if (new[] { D.Lf, D.CrLf, D.Cr }.Count(Count => Count > 0) > 1)
            {
                D.Warnings.Add(Warn(YamlDocumentCode.MixedLineEndings, null,
                    $"The document mixes line breaks (LF {D.Lf}, CRLF {D.CrLf}, CR {D.Cr}); new lines get {Describe(D.Eol)}, the most frequent one."));
            }
            return D;
        }

        private static string Describe(string Eol) => Eol == "\r\n" ? "CRLF" : Eol == "\r" ? "CR" : "LF";

        private static int Idx(Doc D, Mark M) => checked((int)M.Index) + D.Bom;

        // lines start after a leading U+FEFF; a line break is CRLF, LF or CR
        private static void ReadLines(Doc D)
        {
            string T = D.Text;
            var Starts = new List<int> { D.Bom };
            var ContentEnds = new List<int>();
            var Ends = new List<int>();
            for (int i = D.Bom; i < T.Length; i++)
            {
                if (T[i] == '\r' || T[i] == '\n')
                {
                    ContentEnds.Add(i);
                    if (T[i] == '\r' && i + 1 < T.Length && T[i + 1] == '\n')
                    {
                        D.CrLf++;
                        i++;
                    }
                    else if (T[i] == '\r')
                    {
                        D.Cr++;
                    }
                    else
                    {
                        D.Lf++;
                    }
                    Ends.Add(i + 1);
                    Starts.Add(i + 1);
                }
            }
            ContentEnds.Add(T.Length);
            Ends.Add(T.Length);
            D.LineStart = Starts.ToArray();
            D.LineContentEnd = ContentEnds.ToArray();
            D.LineEnd = Ends.ToArray();
            D.Eol = D.Lf >= D.CrLf && D.Lf >= D.Cr ? "\n" : D.CrLf >= D.Cr ? "\r\n" : "\r";
        }

        private static Node Build(Doc D, ref int At, int Depth, List<(int Start, int End)> Leaves)
        {
            ParsingEvent E = D.Events[At];
            switch (E)
            {
                case Scalar S:
                    {
                        var N = new Node { Kind = NodeKind.Scalar, Start = Idx(D, S.Start), End = Idx(D, S.End), Style = S.Style, Value = S.Value, Anchored = !S.Anchor.IsEmpty };
                        N.Tagged = Tag(D, S.Tag, N.Start);
                        if (S.Style == ScalarStyle.Literal || S.Style == ScalarStyle.Folded)
                        {
                            D.BlockScalars.Add(N);
                        }
                        Leaves.Add((N.Start, N.End));
                        At++;
                        return N;
                    }
                case AnchorAlias A:
                    {
                        if (++D.Aliases > MaxAliases)
                        {
                            throw RefuseAt(D, YamlDocumentCode.TooManyAliases, null, Idx(D, A.Start), $"The document has more than {MaxAliases} aliases.");
                        }
                        var N = new Node { Kind = NodeKind.Alias, Start = Idx(D, A.Start), End = Idx(D, A.End) };
                        Leaves.Add((N.Start, N.End));
                        At++;
                        return N;
                    }
                case SequenceStart Ss:
                    return BuildCollection(D, ref At, Depth, Leaves, NodeKind.Sequence, Ss.Style == SequenceStyle.Flow, !Ss.Anchor.IsEmpty, Ss.Tag, '[', ']');
                case MappingStart Ms:
                    return BuildCollection(D, ref At, Depth, Leaves, NodeKind.Mapping, Ms.Style == MappingStyle.Flow, !Ms.Anchor.IsEmpty, Ms.Tag, '{', '}');
                default:
                    throw RefuseAt(D, YamlDocumentCode.Malformed, null, Idx(D, E.Start), "The document has an unexpected YAML event.");
            }
        }

        private static Node BuildCollection(Doc D, ref int At, int Depth, List<(int Start, int End)> Leaves, NodeKind Kind, bool Flow, bool Anchored,
            TagName TagName, char OpenChar, char CloseChar)
        {
            ParsingEvent Open = D.Events[At];
            if (Depth > MaxDepth)
            {
                throw RefuseAt(D, YamlDocumentCode.TooDeep, null, Idx(D, Open.Start), $"The document nests collections more than {MaxDepth} levels deep.");
            }
            var N = new Node { Kind = Kind, Flow = Flow, Anchored = Anchored, Start = Idx(D, Open.Start), PropertiesEnd = Idx(D, Open.End) };
            N.Tagged = Tag(D, TagName, N.Start);
            if (N.PropertiesEnd > N.Start)
            {
                Leaves.Add((N.Start, N.PropertiesEnd));
            }
            At++;
            while (D.Events[At] is not SequenceEnd && D.Events[At] is not MappingEnd)
            {
                N.Children.Add(Build(D, ref At, Depth + 1, Leaves));
            }
            ParsingEvent Close = D.Events[At++];
            if (Flow)
            {
                N.Open = N.PropertiesEnd > N.Start ? N.PropertiesEnd : N.Start;
                N.Close = Idx(D, Close.Start);
                if (N.Open >= D.Text.Length || D.Text[N.Open] != OpenChar || N.Close >= D.Text.Length || D.Text[N.Close] != CloseChar)
                {
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, null, N.Start, "A flow collection's brackets are not where the parser reports them.");
                }
                N.End = N.Close + 1;
            }
            else
            {
                if (N.Children.Count == 0)
                {
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, null, N.Start, "A block collection has no entries.");
                }
                N.End = N.Children[^1].End;
            }
            if (Kind == NodeKind.Mapping)
            {
                var Seen = new HashSet<string>(StringComparer.Ordinal);
                for (int k = 0; k < N.Children.Count; k += 2)
                {
                    Node Key = N.Children[k];
                    if (Key.Kind != NodeKind.Scalar)
                    {
                        continue;
                    }
                    if (Key.Style == ScalarStyle.Plain && Key.Value == "<<")
                    {
                        throw RefuseAt(D, YamlDocumentCode.MergeKey, null, Key.Start, "The document uses a merge key (<<), which Campertron's settings loader does not accept.");
                    }
                    if (!Seen.Add(Key.Value))
                    {
                        throw RefuseAt(D, YamlDocumentCode.DuplicateKey, Depth == 1 ? Key.Value : null, Key.Start,
                            "A key appears twice in one mapping; Campertron's settings loader would silently use the last one, so neither can be edited safely.");
                    }
                }
            }
            return N;
        }

        // a standard tag (!!str, !!int ...) is kept and locks its property; any other tag refuses the document
        private static bool Tag(Doc D, TagName T, int At)
        {
            if (T.IsEmpty)
            {
                return false;
            }
            if (T.IsGlobal && T.Value.StartsWith(StandardTagPrefix, StringComparison.Ordinal))
            {
                return true;
            }
            throw RefuseAt(D, YamlDocumentCode.UnsupportedTag, null, At, "The document uses a tag that is not a standard YAML tag.");
        }

        // A comment is a '#' at the start of a line or after a space or tab, outside every scalar, alias, anchor, tag and
        // directive; it runs to the end of its line. The parser reports where those are, so only the gaps between them
        // are scanned (a '#' inside a scalar is never seen here). A block scalar's header can end with a comment.
        private static void ReadComments(Doc D, List<(int Start, int End)> Leaves)
        {
            string T = D.Text;
            Leaves.Sort((A, B) => A.Start != B.Start ? A.Start.CompareTo(B.Start) : A.End.CompareTo(B.End));
            int Position = D.Bom;
            // an empty scalar is a zero-length leaf: it holds no text and does not split a gap
            foreach ((int LeafStart, int LeafEnd) in Leaves.Where(Leaf => Leaf.End > Leaf.Start).Append((T.Length, T.Length)))
            {
                if (LeafStart > Position)
                {
                    ScanGap(D, Position, LeafStart);
                }
                Position = Math.Max(Position, LeafEnd);
            }
            // a block scalar's header line can end with a comment: after the indicator ('|' or '>' at the scalar's start or
            // after its anchor or tag) and an optional indentation digit and chomping sign
            foreach (Node N in D.BlockScalars)
            {
                int LineEnd = D.LineContentEnd[D.LineOf(N.Start)];
                int Indicator = N.Start;
                while (Indicator < LineEnd && !((T[Indicator] == '|' || T[Indicator] == '>') && (Indicator == N.Start || T[Indicator - 1] == ' ' || T[Indicator - 1] == '\t')))
                {
                    Indicator++;
                }
                for (int i = Indicator + 1; i < LineEnd; i++)
                {
                    if (T[i] == '#' && (T[i - 1] == ' ' || T[i - 1] == '\t'))
                    {
                        AddComment(D, i, LineEnd);
                        break;
                    }
                }
            }
            D.Comments.Sort((A, B) => A.Start.CompareTo(B.Start));
        }

        private static void ScanGap(Doc D, int From, int To)
        {
            string T = D.Text;
            for (int i = From; i < To; i++)
            {
                if (T[i] != '#')
                {
                    continue;
                }
                if (i > D.Bom && T[i - 1] != ' ' && T[i - 1] != '\t' && T[i - 1] != '\n' && T[i - 1] != '\r')
                {
                    throw RefuseAt(D, YamlDocumentCode.UncertainComment, null, i, "A '#' outside every value is not preceded by a space, so it can't be told apart from content.");
                }
                int End = D.LineContentEnd[D.LineOf(i)];
                if (End > To)
                {
                    throw RefuseAt(D, YamlDocumentCode.UncertainComment, null, i, "A comment runs into a value the parser reports on the same line.");
                }
                AddComment(D, i, End);
                i = End;
            }
        }

        private static void AddComment(Doc D, int Start, int End)
        {
            var C = new YamlDocumentComment(Start, End, D.LineOf(Start) + 1);
            D.Comments.Add(C);
            D.CommentAt[Start] = C;
        }

        private static YamlDocumentComment? CommentAfter(Doc D, int From, int Line)
        {
            int i = From;
            int End = D.LineContentEnd[Line];
            while (i < End && (D.Text[i] == ' ' || D.Text[i] == '\t'))
            {
                i++;
            }
            if (i == End)
            {
                return null;
            }
            return D.CommentAt.TryGetValue(i, out YamlDocumentComment? C) && C.End == End ? C : Unexpected;
        }
        // marks "something other than whitespace and one comment"
        private static readonly YamlDocumentComment Unexpected = new(-1, -1, 0);

        private static bool IsBlank(Doc D, int Line)
        {
            for (int i = D.LineStart[Line]; i < D.LineContentEnd[Line]; i++)
            {
                if (D.Text[i] != ' ' && D.Text[i] != '\t')
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsCommentLine(Doc D, int Line)
        {
            YamlDocumentComment? C = CommentAfter(D, D.LineStart[Line], Line);
            return C != null && C != Unexpected;
        }

        private static void ReadProperties(Doc D, Node Root, int RootEvent)
        {
            string T = D.Text;
            int Event = RootEvent + 1;
            for (int k = 0; k < Root.Children.Count; k += 2)
            {
                Node Key = Root.Children[k];
                Node Value = Root.Children[k + 1];
                var P = new Prop { KeyNode = Key, Value = Value, Key = Key.Value, FirstEvent = Event };
                Event += CountEvents(Key) + CountEvents(Value);
                P.LastEvent = Event - 1;
                if (Key.Kind != NodeKind.Scalar || Key.Style == ScalarStyle.Literal || Key.Style == ScalarStyle.Folded)
                {
                    throw RefuseAt(D, YamlDocumentCode.ComplexKey, null, Key.Start, "A top-level key is not a plain or quoted scalar.");
                }
                P.FirstLine = D.LineOf(Key.Start);
                string Before = T.Substring(D.LineStart[P.FirstLine], Key.Start - D.LineStart[P.FirstLine]);
                if (Before.Any(C => C != ' '))
                {
                    throw RefuseAt(D, Before.Contains('?') ? YamlDocumentCode.ComplexKey : YamlDocumentCode.UnsupportedLayout, P.Key, Key.Start,
                        Before.Contains('?') ? "A top-level key uses the explicit '?' key indicator." : "A top-level key does not start its line.");
                }
                if (k == 0)
                {
                    D.Indent = Before;
                }
                P.Colon = Key.End;
                while (P.Colon < T.Length && (T[P.Colon] == ' ' || T[P.Colon] == '\t'))
                {
                    P.Colon++;
                }
                if (P.Colon >= T.Length || T[P.Colon] != ':' || Value.Start <= P.Colon)
                {
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, P.Key, Key.Start, "A top-level key is not followed by its colon.");
                }
                P.ValueStart = Value.Start;
                if (Value.Kind == NodeKind.Scalar && Value.End == Value.Start)
                {
                    if (Value.Start != P.Colon + 1)
                    {
                        throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, P.Key, Value.Start, "An empty value is not where the parser reports it.");
                    }
                    P.LastContent = P.Colon;
                    P.ValueEnd = Value.Start;
                }
                else
                {
                    // a block scalar's range runs on over its last line break and any blank lines after it: the property
                    // ends at its last character that is not a space, tab or line break
                    int Last = Value.End - 1;
                    while (Last >= Value.Start && (T[Last] == ' ' || T[Last] == '\t' || T[Last] == '\r' || T[Last] == '\n'))
                    {
                        Last--;
                    }
                    if (Last < Value.Start)
                    {
                        throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, P.Key, Value.Start, "A value has no visible character.");
                    }
                    P.LastContent = Last;
                    P.ValueEnd = Last + 1;
                }
                P.LastLine = D.LineOf(P.LastContent);
                YamlDocumentComment? Tail = CommentAfter(D, P.LastContent + 1, P.LastLine);
                if (Tail == Unexpected)
                {
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, P.Key, P.LastContent + 1, "Something other than a comment follows a value on its line.");
                }
                P.Inline = Tail;
                P.Lock = LockOf(Key) is var KeyLock && KeyLock != YamlDocumentLock.None ? KeyLock : LockOf(Value);
                P.Shape = Value.Kind switch
                {
                    NodeKind.Scalar => Value.Style == ScalarStyle.Literal || Value.Style == ScalarStyle.Folded ? YamlDocumentShape.BlockScalar : YamlDocumentShape.Scalar,
                    NodeKind.Alias => YamlDocumentShape.Alias,
                    NodeKind.Sequence => Value.Flow ? YamlDocumentShape.FlowSequence : YamlDocumentShape.BlockSequence,
                    _ => YamlDocumentShape.Mapping
                };
                if (Value.Kind == NodeKind.Sequence && P.Lock == YamlDocumentLock.None)
                {
                    P.Seq = ReadSequence(D, P);
                }
                D.Props.Add(P);
                D.ByKey.Add(P.Key, P);
            }
        }

        private static int CountEvents(Node N) => N.Kind switch
        {
            NodeKind.Scalar or NodeKind.Alias => 1,
            _ => 2 + N.Children.Sum(CountEvents)
        };

        private static YamlDocumentLock LockOf(Node N)
        {
            if (N.Anchored)
            {
                return YamlDocumentLock.Anchor;
            }
            if (N.Kind == NodeKind.Alias)
            {
                return YamlDocumentLock.Alias;
            }
            if (N.Tagged)
            {
                return YamlDocumentLock.Tag;
            }
            foreach (Node Child in N.Children)
            {
                YamlDocumentLock L = LockOf(Child);
                if (L != YamlDocumentLock.None)
                {
                    return L;
                }
            }
            return YamlDocumentLock.None;
        }

        // A sequence whose items can be edited: single-line plain or quoted scalars without anchors or tags. A block
        // sequence needs every '-' at the start of its own line with the same indentation, and nothing but comment lines
        // and blank lines between the items; a flow sequence must sit on one line. Anything else returns null (the
        // property is kept and can still be removed or replaced whole by its key).
        private static Sequence? ReadSequence(Doc D, Prop P)
        {
            string T = D.Text;
            Node S = P.Value;
            var Seq = new Sequence { Flow = S.Flow };
            foreach (Node C in S.Children)
            {
                if (C.Kind != NodeKind.Scalar || C.Anchored || C.Tagged || C.End == C.Start ||
                    (C.Style != ScalarStyle.Plain && C.Style != ScalarStyle.SingleQuoted && C.Style != ScalarStyle.DoubleQuoted) ||
                    D.LineOf(C.Start) != D.LineOf(C.End - 1))
                {
                    return null;
                }
            }
            if (S.Flow)
            {
                if (D.LineOf(S.Open) != D.LineOf(S.Close))
                {
                    return null;
                }
                if (S.Children.Count == 0)
                {
                    Seq.Lead = Seq.Trail = T.Substring(S.Open + 1, S.Close - S.Open - 1);
                    return IsSpace(Seq.Lead) ? Seq : null;
                }
                Seq.Lead = T.Substring(S.Open + 1, S.Children[0].Start - S.Open - 1);
                Seq.Trail = T.Substring(S.Children[^1].End, S.Close - S.Children[^1].End);
                if (!IsSpace(Seq.Lead) || !IsSpace(Seq.Trail))
                {
                    return null;
                }
                for (int i = 0; i < S.Children.Count; i++)
                {
                    if (i > 0)
                    {
                        string Separator = T.Substring(S.Children[i - 1].End, S.Children[i].Start - S.Children[i - 1].End);
                        if (Separator.Trim(' ', '\t') != ",")
                        {
                            return null;
                        }
                        if (i == 1)
                        {
                            Seq.Separator = Separator;
                        }
                    }
                    Node C = S.Children[i];
                    Seq.Items.Add(new Item { Node = C, Line = D.LineOf(C.Start), UnitStart = C.Start, UnitEnd = C.End, UnitFirstLine = D.LineOf(C.Start) });
                }
                return Seq;
            }
            int Previous = P.FirstLine;
            for (int i = 0; i < S.Children.Count; i++)
            {
                Node C = S.Children[i];
                int Line = D.LineOf(C.Start);
                int Dash = C.Start - 1;
                while (Dash >= D.LineStart[Line] && (T[Dash] == ' ' || T[Dash] == '\t'))
                {
                    Dash--;
                }
                if (Dash < D.LineStart[Line] || T[Dash] != '-')
                {
                    return null;
                }
                string Indent = T.Substring(D.LineStart[Line], Dash - D.LineStart[Line]);
                if (Indent.Any(Ch => Ch != ' ') || (i > 0 && Indent != Seq.Indent))
                {
                    return null;
                }
                if (i == 0)
                {
                    Seq.Indent = Indent;
                    Seq.DashGap = T.Substring(Dash + 1, C.Start - Dash - 1);
                }
                if (CommentAfter(D, C.End, Line) == Unexpected)
                {
                    return null;
                }
                // the comment lines directly above the item (no blank line between) belong to it and move with it
                int First = Line;
                while (First - 1 > Previous && IsCommentLine(D, First - 1))
                {
                    First--;
                }
                for (int Between = Previous + 1; Between < First; Between++)
                {
                    if (!IsBlank(D, Between) && !IsCommentLine(D, Between))
                    {
                        return null;
                    }
                }
                Seq.Items.Add(new Item { Node = C, Line = Line, UnitFirstLine = First, UnitStart = D.LineStart[First], UnitEnd = D.LineContentEnd[Line] });
                Previous = Line;
            }
            return Seq;
        }

        private static bool IsSpace(string S) => S.All(C => C == ' ' || C == '\t');

        // every line outside the properties must be blank, a comment, the directives and '---' that open the document,
        // or its closing '...': anything else is content this editor does not understand
        private static void CheckLines(Doc D, DocumentStart Start, DocumentEnd End)
        {
            var Used = new bool[D.LineStart.Length];
            foreach (Prop P in D.Props)
            {
                for (int L = P.FirstLine; L <= P.LastLine; L++)
                {
                    if (Used[L])
                    {
                        throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, P.Key, D.LineStart[L], "Two top-level properties share a line.");
                    }
                    Used[L] = true;
                }
            }
            if (!Start.IsImplicit)
            {
                for (int L = D.LineOf(Idx(D, Start.Start)); L <= D.LineOf(Math.Max(Idx(D, Start.Start), Idx(D, Start.End) - 1)); L++)
                {
                    Used[L] = true;
                }
            }
            if (!End.IsImplicit)
            {
                int At = Idx(D, End.Start);
                int L = D.LineOf(At);
                if (At + 3 > D.Text.Length || string.CompareOrdinal(D.Text, At, "...", 0, 3) != 0 || D.LineStart[L] != At ||
                    CommentAfter(D, At + 3, L) == Unexpected)
                {
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, null, At, "The document's end marker is not on a line of its own.");
                }
                Used[L] = true;
            }
            for (int L = 0; L < Used.Length; L++)
            {
                if (!Used[L] && !IsBlank(D, L) && !IsCommentLine(D, L))
                {
                    throw RefuseAt(D, YamlDocumentCode.UnsupportedLayout, null, D.LineStart[L], "A line outside the top-level properties holds something other than a comment.");
                }
            }
        }

        // ---- planning ----

        private sealed class Patch
        {
            internal int Start;
            internal int End;
            internal string Text = string.Empty;
            // a line inserted after the last property comes after an item inserted at the same place
            internal int Rank;
            internal int Order;
        }

        private sealed class Expect
        {
            internal bool Scalar;
            internal string Value = string.Empty;
            internal ScalarStyle Style;
            internal bool Flow;
            internal List<string> Items = new();
            // per item of the result: the source text it must have (an unchanged or moved item), or null (a new item)
            internal List<string?> Units = new();
        }

        private sealed class Plan
        {
            internal readonly List<Patch> Patches = new();
            internal readonly List<YamlDocumentLoss> Losses = new();
            internal readonly Dictionary<string, Expect> Expected = new(StringComparer.Ordinal);
            internal readonly List<string> Inserted = new();
            internal readonly HashSet<string> Removed = new(StringComparer.Ordinal);
            internal readonly List<string> LostComments = new();
        }

        private static Plan? MakePlan(Doc D, IReadOnlyList<YamlDocumentEdit> Edits, IReadOnlyDictionary<string, PropertyInfo> Keys, List<YamlDocumentDiagnostic> Problems)
        {
            if (Edits.Count > MaxEdits)
            {
                Problems.Add(Error(YamlDocumentCode.TooManyEdits, null, $"More than {MaxEdits} edits in one call."));
                return null;
            }
            var Targets = new HashSet<string>(StringComparer.Ordinal);
            foreach (YamlDocumentEdit Edit in Edits)
            {
                if (!Targets.Add(Edit.Key))
                {
                    Problems.Add(Error(YamlDocumentCode.ConflictingEdits, Edit.Key, $"More than one edit targets {Edit.Key}."));
                    return null;
                }
            }
            var P = new Plan();
            for (int e = 0; e < Edits.Count; e++)
            {
                YamlDocumentDiagnostic? Problem = PlanEdit(D, Edits[e], e, Keys, P);
                if (Problem != null)
                {
                    Problems.Add(Problem);
                    return null;
                }
            }
            if (D.Props.Count - P.Removed.Count + P.Inserted.Count == 0)
            {
                Problems.Add(Error(YamlDocumentCode.LastProperty, null, "The edits would leave the document without any setting."));
                return null;
            }
            P.Patches.Sort((A, B) =>
                A.Start != B.Start ? A.Start.CompareTo(B.Start) :
                (A.End > A.Start) != (B.End > B.Start) ? (A.End > A.Start ? 1 : -1) :
                A.Rank != B.Rank ? A.Rank.CompareTo(B.Rank) : A.Order.CompareTo(B.Order));
            for (int i = 1; i < P.Patches.Count; i++)
            {
                if (P.Patches[i].Start < P.Patches[i - 1].End)
                {
                    Problems.Add(Error(YamlDocumentCode.ConflictingEdits, null, "Two edits would change overlapping text."));
                    return null;
                }
            }
            return P;
        }

        private static YamlDocumentDiagnostic? PlanEdit(Doc D, YamlDocumentEdit Edit, int Order, IReadOnlyDictionary<string, PropertyInfo> Keys, Plan P)
        {
            string Key = Edit.Key;
            D.ByKey.TryGetValue(Key, out Prop? Existing);
            if (Edit.Kind == YamlDocumentEditKind.RemoveProperty)
            {
                if (Existing == null)
                {
                    return Error(YamlDocumentCode.MissingProperty, Key, $"{Key} is not in the document.");
                }
                if (Existing.Lock != YamlDocumentLock.None)
                {
                    return Locked(Existing);
                }
                (int Start, int End) = LinesRegion(D, Existing.FirstLine, Existing.LastLine);
                Remove(D, P, Start, End, Order);
                P.Losses.Add(Loss(D, YamlDocumentLossKind.Property, Key, -1, Start, End));
                P.Removed.Add(Key);
                return null;
            }
            if (!Keys.TryGetValue(Key, out PropertyInfo? Property))
            {
                return Error(YamlDocumentCode.UnknownProperty, Key, $"{Key} is not a setting of this file.");
            }
            bool IsList = Property.PropertyType == typeof(List<string>);
            if (Edit.Kind == YamlDocumentEditKind.SetScalar)
            {
                if (IsList)
                {
                    return Error(YamlDocumentCode.NotAScalar, Key, $"{Key} is a list: use a sequence edit, or remove the property.");
                }
                if (!Accepts(Property.PropertyType, Edit.Value))
                {
                    return Error(YamlDocumentCode.ValueTypeMismatch, Key, $"The new value of {Key} is not of the setting's type.");
                }
                if (Existing == null)
                {
                    if (!Render(Edit.Value, null, false, out string Text, out string Value, out ScalarStyle Style))
                    {
                        return Unrepresentable(Key);
                    }
                    Insert(D, P, Key, Text, Order);
                    P.Expected[Key] = new Expect { Scalar = true, Value = Value, Style = Style };
                    return null;
                }
                if (Existing.Lock != YamlDocumentLock.None)
                {
                    return Locked(Existing);
                }
                if (Existing.Shape == YamlDocumentShape.BlockScalar)
                {
                    return Error(YamlDocumentCode.BlockScalar, Key, $"{Key} is a block scalar ('|' or '>'); this editor does not rewrite one.");
                }
                if (Existing.Shape != YamlDocumentShape.Scalar)
                {
                    return Error(YamlDocumentCode.NotAScalar, Key, $"{Key} does not hold a single value.");
                }
                Node V = Existing.Value;
                if (!Render(Edit.Value, V.Style, false, out string NewText, out string NewValue, out ScalarStyle NewStyle))
                {
                    return Unrepresentable(Key);
                }
                if (V.End == V.Start)
                {
                    P.Patches.Add(new Patch { Start = V.Start, End = V.Start, Text = " " + NewText, Order = Order });
                }
                else
                {
                    P.Patches.Add(new Patch { Start = V.Start, End = V.End, Text = NewText, Order = Order });
                }
                P.Expected[Key] = new Expect { Scalar = true, Value = NewValue, Style = NewStyle };
                return null;
            }
            if (!IsList)
            {
                return Error(YamlDocumentCode.NotASequence, Key, $"{Key} is not a list.");
            }
            IReadOnlyList<string> Values = Edit.Values;
            bool MissingOrNull = Existing == null || (Existing.Shape == YamlDocumentShape.Scalar && Existing.Lock == YamlDocumentLock.None && IsNull(Existing.Value));
            if (MissingOrNull && (Edit.Kind == YamlDocumentEditKind.SetSequence || (Edit.Kind == YamlDocumentEditKind.InsertItem && Edit.Index == 0)))
            {
                // a new list is written as a one-line flow sequence: [a, b]
                var Rendered = new List<string>();
                foreach (string Value in Values)
                {
                    if (!Render(Value, null, true, out string ItemText, out _, out _))
                    {
                        return Unrepresentable(Key);
                    }
                    Rendered.Add(ItemText);
                }
                string Flow = "[" + string.Join(", ", Rendered) + "]";
                if (Existing == null)
                {
                    Insert(D, P, Key, Flow, Order);
                }
                else if (Existing.Value.End == Existing.Value.Start)
                {
                    P.Patches.Add(new Patch { Start = Existing.Value.Start, End = Existing.Value.Start, Text = " " + Flow, Order = Order });
                }
                else
                {
                    P.Patches.Add(new Patch { Start = Existing.Value.Start, End = Existing.Value.End, Text = Flow, Order = Order });
                }
                P.Expected[Key] = new Expect { Flow = true, Items = Values.ToList(), Units = Values.Select(_ => (string?)null).ToList() };
                return null;
            }
            if (Existing == null)
            {
                return Error(YamlDocumentCode.MissingProperty, Key, $"{Key} is not in the document.");
            }
            if (Existing.Lock != YamlDocumentLock.None)
            {
                return Locked(Existing);
            }
            if (Existing.Shape != YamlDocumentShape.BlockSequence && Existing.Shape != YamlDocumentShape.FlowSequence)
            {
                return Error(YamlDocumentCode.NotASequence, Key, $"{Key} does not hold a list.");
            }
            Sequence? Seq = Existing.Seq;
            if (Seq == null)
            {
                return Error(YamlDocumentCode.UnsupportedSequence, Key,
                    $"The items of {Key} are not all single-line plain or quoted values on lines of their own (or one line for [ ]), so they are not edited.");
            }
            return Seq.Flow ? PlanFlow(D, Edit, Existing, Seq, Order, P) : PlanBlock(D, Edit, Existing, Seq, Order, P);
        }

        private static YamlDocumentDiagnostic? PlanFlow(Doc D, YamlDocumentEdit Edit, Prop Existing, Sequence Seq, int Order, Plan P)
        {
            string T = D.Text;
            string Key = Existing.Key;
            List<Item> Items = Seq.Items;
            Node S = Existing.Value;
            List<string?> Units = Items.Select(I => (string?)T.Substring(I.UnitStart, I.UnitEnd - I.UnitStart)).ToList();
            List<string> Values = Items.Select(I => I.Node.Value).ToList();
            switch (Edit.Kind)
            {
                case YamlDocumentEditKind.SetSequence:
                    {
                        var Rendered = new List<string>();
                        foreach (string Value in Edit.Values)
                        {
                            if (!Render(Value, null, true, out string ItemText, out _, out _))
                            {
                                return Unrepresentable(Key);
                            }
                            Rendered.Add(ItemText);
                        }
                        string Inner = Rendered.Count == 0 ? (Items.Count == 0 ? Seq.Lead : string.Empty) : Seq.Lead + string.Join(Seq.Separator, Rendered) + Seq.Trail;
                        P.Patches.Add(new Patch { Start = S.Open + 1, End = S.Close, Text = Inner, Order = Order });
                        if (Items.Count > 0)
                        {
                            P.Losses.Add(Loss(D, YamlDocumentLossKind.SequenceItems, Key, -1, S.Open + 1, S.Close));
                        }
                        Values = Edit.Values.ToList();
                        Units = Values.Select(_ => (string?)null).ToList();
                        break;
                    }
                case YamlDocumentEditKind.SetItem:
                    {
                        if (Edit.Index < 0 || Edit.Index >= Items.Count)
                        {
                            return OutOfRange(Key);
                        }
                        Item I = Items[Edit.Index];
                        if (!Render(Edit.Values[0], I.Node.Style, true, out string ItemText, out _, out _))
                        {
                            return Unrepresentable(Key);
                        }
                        P.Patches.Add(new Patch { Start = I.Node.Start, End = I.Node.End, Text = ItemText, Order = Order });
                        Values[Edit.Index] = Edit.Values[0];
                        Units[Edit.Index] = ItemText;
                        break;
                    }
                case YamlDocumentEditKind.InsertItem:
                    {
                        if (Edit.Index < 0 || Edit.Index > Items.Count)
                        {
                            return OutOfRange(Key);
                        }
                        if (!Render(Edit.Values[0], null, true, out string ItemText, out _, out _))
                        {
                            return Unrepresentable(Key);
                        }
                        if (Items.Count == 0)
                        {
                            string Inner = Seq.Lead.Length == 0 ? ItemText : Seq.Lead + ItemText + Seq.Trail;
                            P.Patches.Add(new Patch { Start = S.Open + 1, End = S.Close, Text = Inner, Order = Order });
                        }
                        else if (Edit.Index < Items.Count)
                        {
                            P.Patches.Add(new Patch { Start = Items[Edit.Index].Node.Start, End = Items[Edit.Index].Node.Start, Text = ItemText + Seq.Separator, Order = Order });
                        }
                        else
                        {
                            P.Patches.Add(new Patch { Start = Items[^1].Node.End, End = Items[^1].Node.End, Text = Seq.Separator + ItemText, Order = Order });
                        }
                        Values.Insert(Edit.Index, Edit.Values[0]);
                        Units.Insert(Edit.Index, ItemText);
                        break;
                    }
                case YamlDocumentEditKind.RemoveItem:
                    {
                        if (Edit.Index < 0 || Edit.Index >= Items.Count)
                        {
                            return OutOfRange(Key);
                        }
                        int Start;
                        int End;
                        if (Items.Count == 1)
                        {
                            (Start, End) = (Items[0].Node.Start, Items[0].Node.End);
                        }
                        else if (Edit.Index < Items.Count - 1)
                        {
                            (Start, End) = (Items[Edit.Index].Node.Start, Items[Edit.Index + 1].Node.Start);
                        }
                        else
                        {
                            (Start, End) = (Items[Edit.Index - 1].Node.End, Items[Edit.Index].Node.End);
                        }
                        Remove(D, P, Start, End, Order);
                        P.Losses.Add(Loss(D, YamlDocumentLossKind.SequenceItem, Key, Edit.Index, Start, End));
                        Values.RemoveAt(Edit.Index);
                        Units.RemoveAt(Edit.Index);
                        break;
                    }
                case YamlDocumentEditKind.ReorderItems:
                    {
                        if (!IsPermutation(Edit.Order, Items.Count))
                        {
                            return Error(YamlDocumentCode.InvalidOrder, Key, $"The new order of {Key} is not a permutation of its {Items.Count} items.");
                        }
                        for (int i = 0; i < Items.Count; i++)
                        {
                            if (Edit.Order[i] != i)
                            {
                                P.Patches.Add(new Patch { Start = Items[i].UnitStart, End = Items[i].UnitEnd, Text = Units[Edit.Order[i]]!, Order = Order });
                            }
                        }
                        Values = Edit.Order.Select(i => Items[i].Node.Value).ToList();
                        Units = Edit.Order.Select(i => Units[i]).ToList();
                        break;
                    }
            }
            P.Expected[Key] = new Expect { Flow = true, Items = Values, Units = Units };
            return null;
        }

        private static YamlDocumentDiagnostic? PlanBlock(Doc D, YamlDocumentEdit Edit, Prop Existing, Sequence Seq, int Order, Plan P)
        {
            string T = D.Text;
            string Key = Existing.Key;
            List<Item> Items = Seq.Items;
            List<string?> Units = Items.Select(I => (string?)T.Substring(I.UnitStart, I.UnitEnd - I.UnitStart)).ToList();
            List<string> Values = Items.Select(I => I.Node.Value).ToList();
            bool Flow = false;
            switch (Edit.Kind)
            {
                case YamlDocumentEditKind.SetSequence:
                    {
                        if (Edit.Values.Count == 0)
                        {
                            ToEmpty(D, Existing, Order, P, YamlDocumentLossKind.SequenceItems, -1);
                            Flow = true;
                        }
                        else
                        {
                            var Lines = new List<string>();
                            foreach (string Value in Edit.Values)
                            {
                                if (!Render(Value, null, false, out string ItemText, out _, out _))
                                {
                                    return Unrepresentable(Key);
                                }
                                Lines.Add(Seq.Indent + "-" + Seq.DashGap + ItemText);
                            }
                            int Start = D.LineStart[Existing.FirstLine + 1];
                            int End = D.LineContentEnd[Existing.LastLine];
                            P.Patches.Add(new Patch { Start = Start, End = End, Text = string.Join(D.Eol, Lines), Order = Order });
                            P.Losses.Add(Loss(D, YamlDocumentLossKind.SequenceItems, Key, -1, Start, End));
                            P.LostComments.AddRange(CommentsIn(D, Start, End).Select(C => Slice(T, C.Start, C.End)));
                        }
                        Values = Edit.Values.ToList();
                        Units = Values.Select(_ => (string?)null).ToList();
                        break;
                    }
                case YamlDocumentEditKind.SetItem:
                    {
                        if (Edit.Index < 0 || Edit.Index >= Items.Count)
                        {
                            return OutOfRange(Key);
                        }
                        Item I = Items[Edit.Index];
                        if (!Render(Edit.Values[0], I.Node.Style, false, out string ItemText, out _, out _))
                        {
                            return Unrepresentable(Key);
                        }
                        P.Patches.Add(new Patch { Start = I.Node.Start, End = I.Node.End, Text = ItemText, Order = Order });
                        Values[Edit.Index] = Edit.Values[0];
                        Units[Edit.Index] = Slice(T, I.UnitStart, I.Node.Start) + ItemText + Slice(T, I.Node.End, I.UnitEnd);
                        break;
                    }
                case YamlDocumentEditKind.InsertItem:
                    {
                        if (Edit.Index < 0 || Edit.Index > Items.Count)
                        {
                            return OutOfRange(Key);
                        }
                        if (!Render(Edit.Values[0], null, false, out string ItemText, out _, out _))
                        {
                            return Unrepresentable(Key);
                        }
                        string Line = Seq.Indent + "-" + Seq.DashGap + ItemText;
                        if (Edit.Index < Items.Count)
                        {
                            int At = Items[Edit.Index].UnitStart;
                            P.Patches.Add(new Patch { Start = At, End = At, Text = Line + D.Eol, Order = Order });
                        }
                        else
                        {
                            AddAfterLine(D, P, Items[^1].Line, Line, 0, Order);
                        }
                        Values.Insert(Edit.Index, Edit.Values[0]);
                        Units.Insert(Edit.Index, Line);
                        break;
                    }
                case YamlDocumentEditKind.RemoveItem:
                    {
                        if (Edit.Index < 0 || Edit.Index >= Items.Count)
                        {
                            return OutOfRange(Key);
                        }
                        if (Items.Count == 1)
                        {
                            // a block sequence can't be empty: the value becomes [] (an empty list, not a missing one)
                            ToEmpty(D, Existing, Order, P, YamlDocumentLossKind.SequenceItem, 0);
                            Flow = true;
                        }
                        else
                        {
                            Item I = Items[Edit.Index];
                            (int Start, int End) = LinesRegion(D, I.UnitFirstLine, I.Line);
                            Remove(D, P, Start, End, Order);
                            P.Losses.Add(Loss(D, YamlDocumentLossKind.SequenceItem, Key, Edit.Index, Start, End));
                        }
                        Values.RemoveAt(Edit.Index);
                        Units.RemoveAt(Edit.Index);
                        break;
                    }
                case YamlDocumentEditKind.ReorderItems:
                    {
                        if (!IsPermutation(Edit.Order, Items.Count))
                        {
                            return Error(YamlDocumentCode.InvalidOrder, Key, $"The new order of {Key} is not a permutation of its {Items.Count} items.");
                        }
                        for (int i = 0; i < Items.Count; i++)
                        {
                            if (Edit.Order[i] != i)
                            {
                                P.Patches.Add(new Patch { Start = Items[i].UnitStart, End = Items[i].UnitEnd, Text = Units[Edit.Order[i]]!, Order = Order });
                            }
                        }
                        Values = Edit.Order.Select(i => Items[i].Node.Value).ToList();
                        Units = Edit.Order.Select(i => Units[i]).ToList();
                        break;
                    }
            }
            P.Expected[Key] = new Expect { Flow = Flow, Items = Values, Units = Units };
            return null;
        }

        // the only item of a block sequence goes: " []" after the colon (an inline comment there stays) and every line of
        // the sequence after the key's line is removed
        private static void ToEmpty(Doc D, Prop Existing, int Order, Plan P, YamlDocumentLossKind Kind, int Index)
        {
            P.Patches.Add(new Patch { Start = Existing.Colon + 1, End = Existing.Colon + 1, Text = " []", Order = Order });
            (int Start, int End) = LinesRegion(D, Existing.FirstLine + 1, Existing.LastLine);
            Remove(D, P, Start, End, Order);
            P.Losses.Add(Loss(D, Kind, Existing.Key, Index, Start, End));
        }

        // Whole lines First..Last with the line break after Last. When Last is the final line and has no line break, the
        // line break before First goes instead, so a file that ends without one still does.
        private static (int Start, int End) LinesRegion(Doc D, int First, int Last)
        {
            if (D.HasLineBreak(Last) || First == 0)
            {
                return (D.LineStart[First], D.LineEnd[Last]);
            }
            return (D.LineContentEnd[First - 1], D.LineEnd[Last]);
        }

        private static void Remove(Doc D, Plan P, int Start, int End, int Order)
        {
            P.Patches.Add(new Patch { Start = Start, End = End, Text = string.Empty, Order = Order });
            P.LostComments.AddRange(CommentsIn(D, Start, End).Select(C => Slice(D.Text, C.Start, C.End)));
        }

        // a new property goes after the last one's last line, with the top-level indentation and the insertion line break
        private static void Insert(Doc D, Plan P, string Key, string ValueText, int Order)
        {
            AddAfterLine(D, P, D.Props[^1].LastLine, D.Indent + Key + ": " + ValueText, 1, Order);
            P.Inserted.Add(Key);
        }

        private static void AddAfterLine(Doc D, Plan P, int Line, string NewLine, int Rank, int Order)
        {
            if (D.HasLineBreak(Line))
            {
                P.Patches.Add(new Patch { Start = D.LineEnd[Line], End = D.LineEnd[Line], Text = NewLine + D.Eol, Rank = Rank, Order = Order });
            }
            else
            {
                // after a final line without a line break: the break goes before the new line, and the file still ends without one
                P.Patches.Add(new Patch { Start = D.Text.Length, End = D.Text.Length, Text = D.Eol + NewLine, Rank = Rank, Order = Order });
            }
        }

        private static IEnumerable<YamlDocumentComment> CommentsIn(Doc D, int Start, int End) =>
            D.Comments.Where(C => C.Start >= Start && C.End <= End);

        private static YamlDocumentLoss Loss(Doc D, YamlDocumentLossKind Kind, string Key, int Index, int Start, int End) =>
            new YamlDocumentLoss(Kind, Key, Index, Start, End, D.LineOf(Start) + 1, D.LineOf(Math.Max(Start, End - 1)) + 1, CommentsIn(D, Start, End).ToList());

        private static bool IsPermutation(IReadOnlyList<int> Order, int Count) =>
            Order.Count == Count && Order.All(i => i >= 0 && i < Count) && Order.Distinct().Count() == Count;

        private static bool IsNull(Node N) =>
            N.Kind == NodeKind.Scalar && N.Style == ScalarStyle.Plain && (N.Value.Length == 0 || N.Value is "~" or "null" or "Null" or "NULL");

        private static bool Accepts(Type Property, object? Value)
        {
            Type? Underlying = Nullable.GetUnderlyingType(Property);
            if (Value == null)
            {
                return !Property.IsValueType || Underlying != null;
            }
            return Value.GetType() == (Underlying ?? Property);
        }

        // ---- rendering a scalar ----

        // Writes Value as a scalar. Previous is the style of the value it replaces (null for a new value): it is kept when
        // the new value can be written in it and reads back unchanged. A string is never written plain when the loader would
        // read it as null; a new string is written plain only if it can't be read as anything but a string (no null,
        // boolean, number or date form), otherwise single-quoted, or double-quoted when it holds characters single quotes
        // can't. Other values are written as Campertron's writer writes them (the same YamlDotNet serializer), quoted when
        // the old value was. Parsed is the value the parser must report for the written scalar.
        private static bool Render(object? Value, ScalarStyle? Previous, bool InFlow, out string Text, out string Parsed, out ScalarStyle Style)
        {
            Text = Parsed = string.Empty;
            Style = ScalarStyle.Plain;
            if (Value == null)
            {
                Text = Parsed = "null";
                return true;
            }
            if (Value is string S)
            {
                Parsed = S;
                if (!IsWellFormed(S))
                {
                    return false;
                }
                bool KeepPlain = Previous == ScalarStyle.Plain;
                if ((Previous == null || KeepPlain) && IsPlainSafe(S, InFlow, AllowTypedForms: KeepPlain))
                {
                    Text = S;
                    Style = ScalarStyle.Plain;
                }
                else if (Previous != ScalarStyle.DoubleQuoted && IsSingleQuotable(S))
                {
                    Text = "'" + S.Replace("'", "''") + "'";
                    Style = ScalarStyle.SingleQuoted;
                }
                else
                {
                    Text = DoubleQuote(S);
                    Style = ScalarStyle.DoubleQuoted;
                }
                return true;
            }
            if (Value is not (bool or int or double or DateTime or Enum))
            {
                return false;
            }
            string Canonical = Serializer().Serialize(Value).TrimEnd('\r', '\n');
            if (Canonical.Length == 0 || Canonical.Any(C => C < ' ' || C == '\'' || C == '"' || C == '#' || C > '~') || Canonical.StartsWith("---", StringComparison.Ordinal))
            {
                return false;
            }
            Parsed = Canonical;
            if (Previous == ScalarStyle.SingleQuoted)
            {
                Text = "'" + Canonical + "'";
                Style = ScalarStyle.SingleQuoted;
            }
            else if (Previous == ScalarStyle.DoubleQuoted)
            {
                Text = "\"" + Canonical + "\"";
                Style = ScalarStyle.DoubleQuoted;
            }
            else
            {
                Text = Canonical;
            }
            return true;
        }

        // the serializer configuration of Campertron's writers (Yaml.*ConvertToYaml), built per call as they build it; used
        // for one value at a time
        private static ISerializer Serializer() => new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

        // valid UTF-16 and nothing YamlDotNet reads as a line break that this editor would not
        private static bool IsWellFormed(string S)
        {
            for (int i = 0; i < S.Length; i++)
            {
                if (char.IsHighSurrogate(S[i]) && i + 1 < S.Length && char.IsLowSurrogate(S[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(S[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // printable on its own: no control character, no line break of any kind, no byte-order mark, no non-character
        private static bool IsQuietChar(char C) =>
            C >= ' ' && C != '\u007F' && !(C >= '\u0080' && C <= '\u009F') && C != '\u2028' && C != '\u2029' && C != '\uFEFF' && C != '\uFFFE' && C != '\uFFFF';

        private static bool IsPlainSafe(string S, bool InFlow, bool AllowTypedForms)
        {
            if (S.Length == 0 || S.ToLowerInvariant() is "null" or "~")
            {
                return false;
            }
            if (!AllowTypedForms && LooksTyped(S))
            {
                return false;
            }
            if ("-?:,[]{}#&*!|>'\"%@`".Contains(S[0]) || S[0] == ' ' || S[^1] == ' ' || S.StartsWith("...", StringComparison.Ordinal))
            {
                return false;
            }
            for (int i = 0; i < S.Length; i++)
            {
                char C = S[i];
                if (!IsQuietChar(C) || C == '\t')
                {
                    return false;
                }
                if (C == ':' && (InFlow || i == S.Length - 1 || S[i + 1] == ' '))
                {
                    return false;
                }
                if (C == '#' && S[i - 1] == ' ')
                {
                    return false;
                }
                if (InFlow && ",[]{}".Contains(C))
                {
                    return false;
                }
            }
            return true;
        }

        // a form YAML resolves to a boolean, a number, an infinity or NaN, or a date (YAML 1.1 and 1.2, any case)
        private static bool LooksTyped(string S)
        {
            string L = S.ToLowerInvariant();
            if (L is "true" or "false" or "yes" or "no" or "on" or "off" or "y" or "n")
            {
                return true;
            }
            char First = S[0];
            return char.IsAsciiDigit(First) || First == '+' || First == '-' || First == '.';
        }

        private static bool IsSingleQuotable(string S) => S.All(C => IsQuietChar(C) && C != '\t');

        private static string DoubleQuote(string S)
        {
            var B = new StringBuilder("\"");
            for (int i = 0; i < S.Length; i++)
            {
                char C = S[i];
                if (char.IsHighSurrogate(C))
                {
                    B.Append(C).Append(S[++i]);
                }
                else if (C == '\\' || C == '"')
                {
                    B.Append('\\').Append(C);
                }
                else if (C == '\n')
                {
                    B.Append("\\n");
                }
                else if (C == '\r')
                {
                    B.Append("\\r");
                }
                else if (C == '\t')
                {
                    B.Append("\\t");
                }
                else if (!IsQuietChar(C))
                {
                    B.Append("\\u").Append(((int)C).ToString("X4"));
                }
                else
                {
                    B.Append(C);
                }
            }
            return B.Append('"').ToString();
        }

        // ---- applying and checking ----

        private static string Apply(string Text, List<Patch> Patches)
        {
            var B = new StringBuilder(Text.Length + Patches.Sum(Patch => Patch.Text.Length));
            int Position = 0;
            foreach (Patch Patch in Patches)
            {
                B.Append(Text, Position, Patch.Start - Position).Append(Patch.Text);
                Position = Patch.End;
            }
            return B.Append(Text, Position, Text.Length - Position).ToString();
        }

        // Whether Output is exactly what the plan makes of the original: the text outside the patches is unchanged; the
        // output parses as a supported document; its keys are the original ones minus the removed and plus the inserted,
        // in order; every untouched property's lines are byte for byte the same; edited values and items read back as
        // planned (moved and kept items with their own text); the comments are the original ones minus those the losses
        // name; the line breaks, the final line break and a leading U+FEFF are kept. Returns the parsed output.
        private static Doc? Check(Doc Original, Plan P, string Output, List<YamlDocumentDiagnostic> Problems)
        {
            string O = Original.Text;
            int Position = 0;
            int Out = 0;
            foreach (Patch Patch in P.Patches)
            {
                int Kept = Patch.Start - Position;
                if (Out + Kept + Patch.Text.Length > Output.Length ||
                    string.CompareOrdinal(O, Position, Output, Out, Kept) != 0 ||
                    string.CompareOrdinal(Patch.Text, 0, Output, Out + Kept, Patch.Text.Length) != 0)
                {
                    return Preservation(Problems, "Text outside the edited ranges changed.");
                }
                Out += Kept + Patch.Text.Length;
                Position = Patch.End;
            }
            if (Output.Length - Out != O.Length - Position || string.CompareOrdinal(O, Position, Output, Out, O.Length - Position) != 0)
            {
                return Preservation(Problems, "Text outside the edited ranges changed.");
            }
            var Inner = new List<YamlDocumentDiagnostic>();
            Doc? R = Read(Output, Inner);
            if (R == null)
            {
                YamlDocumentDiagnostic Why = Inner[0];
                Problems.Add(new YamlDocumentDiagnostic(YamlDocumentCode.ReparseFailed, YamlDocumentSeverity.Error, Why.Key, Why.Line, Why.Column,
                    $"The edited text would not be a supported document ({Why.Code}); nothing was changed."));
                return null;
            }
            if (R.Bom != Original.Bom || HasFinalBreak(R) != HasFinalBreak(Original))
            {
                return Preservation(Problems, "The byte-order mark or the final line break changed.");
            }
            (int Lf, int CrLf, int Cr) Expected = (Original.Lf, Original.CrLf, Original.Cr);
            foreach (Patch Patch in P.Patches)
            {
                Expected = Add(Expected, Breaks(O.Substring(Patch.Start, Patch.End - Patch.Start)), -1);
                Expected = Add(Expected, Breaks(Patch.Text), 1);
            }
            if (Expected != (R.Lf, R.CrLf, R.Cr))
            {
                return Preservation(Problems, "The line breaks outside the edited ranges changed.");
            }
            List<string> Keys = Original.Props.Select(Prop => Prop.Key).Where(Key => !P.Removed.Contains(Key)).Concat(P.Inserted).ToList();
            if (!Keys.SequenceEqual(R.Props.Select(Prop => Prop.Key), StringComparer.Ordinal))
            {
                return Preservation(Problems, "The keys or their order changed.");
            }
            foreach (Prop After in R.Props)
            {
                if (!P.Expected.TryGetValue(After.Key, out Expect? E))
                {
                    Prop Before = Original.ByKey[After.Key];
                    if (OwnText(Original, Before) != OwnText(R, After))
                    {
                        return Preservation(Problems, $"{After.Key} changed although no edit targets it.", After.Key);
                    }
                    continue;
                }
                if (E.Scalar)
                {
                    if (After.Shape != YamlDocumentShape.Scalar || After.Value.Value != E.Value || After.Value.Style != E.Style)
                    {
                        return Preservation(Problems, $"{After.Key} does not read back as the value that was written.", After.Key);
                    }
                    continue;
                }
                Sequence? Seq = After.Seq;
                if (Seq == null || Seq.Flow != E.Flow || !Seq.Items.Select(I => I.Node.Value).SequenceEqual(E.Items, StringComparer.Ordinal))
                {
                    return Preservation(Problems, $"The items of {After.Key} do not read back as planned.", After.Key);
                }
                for (int i = 0; i < E.Units.Count; i++)
                {
                    string? Unit = E.Units[i];
                    if (Unit != null && Slice(R.Text, Seq.Items[i].UnitStart, Seq.Items[i].UnitEnd) != Unit)
                    {
                        return Preservation(Problems, $"An item of {After.Key} lost its text or its comments.", After.Key);
                    }
                }
            }
            var Remaining = Original.Comments.Select(C => Slice(O, C.Start, C.End)).GroupBy(S => S, StringComparer.Ordinal)
                .ToDictionary(G => G.Key, G => G.Count(), StringComparer.Ordinal);
            foreach (string Lost in P.LostComments)
            {
                Remaining[Lost]--;
            }
            var Found = R.Comments.Select(C => Slice(R.Text, C.Start, C.End)).GroupBy(S => S, StringComparer.Ordinal)
                .ToDictionary(G => G.Key, G => G.Count(), StringComparer.Ordinal);
            if (Remaining.Any(Pair => Pair.Value != Found.GetValueOrDefault(Pair.Key)) || Found.Keys.Any(Key => !Remaining.ContainsKey(Key)))
            {
                return Preservation(Problems, "The comments are not the original ones less those the losses name.");
            }
            return R;
        }

        private static Doc? Preservation(List<YamlDocumentDiagnostic> Problems, string Message, string? Key = null)
        {
            Problems.Add(Error(YamlDocumentCode.PreservationFailed, Key, Message + " Nothing was changed."));
            return null;
        }

        private static bool HasFinalBreak(Doc D) => D.Text.Length > D.Bom && (D.Text[^1] == '\n' || D.Text[^1] == '\r');

        private static (int Lf, int CrLf, int Cr) Breaks(string S)
        {
            (int Lf, int CrLf, int Cr) Count = (0, 0, 0);
            for (int i = 0; i < S.Length; i++)
            {
                if (S[i] == '\r' && i + 1 < S.Length && S[i + 1] == '\n')
                {
                    Count.CrLf++;
                    i++;
                }
                else if (S[i] == '\r')
                {
                    Count.Cr++;
                }
                else if (S[i] == '\n')
                {
                    Count.Lf++;
                }
            }
            return Count;
        }

        private static (int, int, int) Add((int Lf, int CrLf, int Cr) A, (int Lf, int CrLf, int Cr) B, int Sign) =>
            (A.Lf + Sign * B.Lf, A.CrLf + Sign * B.CrLf, A.Cr + Sign * B.Cr);

        private static string OwnText(Doc D, Prop P) => Slice(D.Text, D.LineStart[P.FirstLine], D.LineContentEnd[P.LastLine]);

        private static string Slice(string S, int Start, int End) => S.Substring(Start, End - Start);

        // Loads the checked output with Campertron's settings deserializer and compares it with the model the caller
        // intends, property by property. A document with keys the loader does not accept is loaded from the same parsed
        // events without those properties (the deserializer's own path for a text: Deserialize(string) parses it and
        // deserializes the events); otherwise from the text itself, exactly as the loader reads a file.
        private static YamlDocumentDiagnostic? CheckModel<T>(Doc R, T Intended, IReadOnlyDictionary<string, PropertyInfo> Keys) where T : class
        {
            T? Loaded;
            try
            {
                IDeserializer Loader = Yaml.SettingsDeserializer();
                if (R.Props.All(Prop => Keys.ContainsKey(Prop.Key)))
                {
                    Loaded = Loader.Deserialize<T>(R.Body);
                }
                else
                {
                    var Kept = new List<ParsingEvent>(R.Events.Count);
                    int Next = 0;
                    foreach (Prop Unknown in R.Props.Where(Prop => !Keys.ContainsKey(Prop.Key)))
                    {
                        Kept.AddRange(R.Events.Skip(Next).Take(Unknown.FirstEvent - Next));
                        Next = Unknown.LastEvent + 1;
                    }
                    Kept.AddRange(R.Events.Skip(Next));
                    Loaded = Loader.Deserialize<T>(new EventList(Kept));
                }
            }
            catch (YamlException Ex)
            {
                // the exception's message can quote a value (such as a password), so only its type and position are kept
                return new YamlDocumentDiagnostic(YamlDocumentCode.RuntimeLoadFailed, YamlDocumentSeverity.Error, null, (int)Ex.Start.Line, (int)Ex.Start.Column,
                    $"Campertron's settings loader could not read the edited document ({Ex.GetType().Name}); nothing was changed.");
            }
            if (Loaded == null)
            {
                return Error(YamlDocumentCode.RuntimeLoadFailed, null, "Campertron's settings loader read no settings from the edited document; nothing was changed.");
            }
            foreach ((string Key, PropertyInfo Property) in Keys)
            {
                if (!SameValue(Property.PropertyType, Property.GetValue(Loaded), Property.GetValue(Intended)))
                {
                    return Error(YamlDocumentCode.ModelMismatch, Key, $"After the edit {Key} does not hold the intended value; nothing was changed.");
                }
            }
            return null;
        }

        private static bool SameValue(Type Type, object? A, object? B)
        {
            if (A == null || B == null)
            {
                return A == null && B == null;
            }
            Type Underlying = Nullable.GetUnderlyingType(Type) ?? Type;
            if (Underlying == typeof(string) || Underlying == typeof(bool) || Underlying == typeof(int) || Underlying.IsEnum)
            {
                return A.Equals(B);
            }
            if (Underlying == typeof(double))
            {
                return BitConverter.DoubleToInt64Bits((double)A) == BitConverter.DoubleToInt64Bits((double)B);
            }
            if (Underlying == typeof(DateTime))
            {
                return ((DateTime)A).Ticks == ((DateTime)B).Ticks && ((DateTime)A).Kind == ((DateTime)B).Kind;
            }
            if (Underlying == typeof(List<string>))
            {
                return ((List<string>)A).SequenceEqual((List<string>)B, StringComparer.Ordinal);
            }
            throw new InvalidOperationException($"A settings model has a property of type {Type.Name}, which the editor can't compare.");
        }

        // the parsed events of a document, replayed for the deserializer
        // an IParser over parsed events; the settings loader deserializes the events it counted with it (D41)
        internal sealed class EventList : IParser
        {
            private readonly List<ParsingEvent> Events;
            private int Position = -1;
            internal EventList(List<ParsingEvent> Events)
            {
                this.Events = Events;
            }
            public ParsingEvent? Current => Position >= 0 && Position < Events.Count ? Events[Position] : null;
            public bool MoveNext()
            {
                if (Position < Events.Count)
                {
                    Position++;
                }
                return Position < Events.Count;
            }
        }

        // ---- the analysis a caller sees ----

        private static YamlDocumentAnalysis Project(Doc? D, List<YamlDocumentDiagnostic> Problems)
        {
            if (D == null)
            {
                return new YamlDocumentAnalysis(false, Problems, Array.Empty<YamlDocumentProperty>(), Array.Empty<YamlDocumentComment>(), false, false, 0, 0, 0, "\n");
            }
            var Properties = D.Props.Select(P => new YamlDocumentProperty(P.Key, P.KeyNode.Start, P.KeyNode.End, P.ValueStart, P.ValueEnd,
                P.FirstLine + 1, P.LastLine + 1, D.Indent.Length, P.Shape,
                P.Shape == YamlDocumentShape.Scalar || P.Shape == YamlDocumentShape.BlockScalar ? P.Value.Style : null, P.Lock, P.Seq != null,
                P.Seq == null ? Array.Empty<YamlDocumentItem>() :
                    P.Seq.Items.Select(I => new YamlDocumentItem(I.Node.Start, I.Node.End, I.Node.Style, I.Line + 1, I.UnitStart, I.UnitEnd)).ToList(),
                P.Inline)).ToList();
            return new YamlDocumentAnalysis(true, D.Warnings.ToList(), Properties, D.Comments.ToList(), D.Bom == 1, HasFinalBreak(D), D.Lf, D.CrLf, D.Cr, D.Eol);
        }

        // ---- diagnostics (fixed text, key names and numbers only) ----

        private static YamlDocumentDiagnostic Error(YamlDocumentCode Code, string? Key, string Message) =>
            new YamlDocumentDiagnostic(Code, YamlDocumentSeverity.Error, Key, 0, 0, Message);

        private static YamlDocumentDiagnostic Warn(YamlDocumentCode Code, string? Key, string Message) =>
            new YamlDocumentDiagnostic(Code, YamlDocumentSeverity.Warning, Key, 0, 0, Message);

        private static Refusal Refuse(YamlDocumentCode Code, string? Key, int Line, int Column, string Message) =>
            new Refusal(new YamlDocumentDiagnostic(Code, YamlDocumentSeverity.Error, Key, Line, Column, Message));

        private static Refusal RefuseAt(Doc D, YamlDocumentCode Code, string? Key, int Index, string Message)
        {
            int Line = D.LineOf(Index);
            return Refuse(Code, Key, Line + 1, Index - D.LineStart[Line] + 1, Message);
        }

        private static YamlDocumentDiagnostic Locked(Prop P) =>
            Error(YamlDocumentCode.FieldLocked, P.Key, $"{P.Key} uses {(P.Lock == YamlDocumentLock.Anchor ? "an anchor" : P.Lock == YamlDocumentLock.Alias ? "an alias" : "a tag")}; this editor keeps it as it is and does not change it.");

        private static YamlDocumentDiagnostic OutOfRange(string Key) =>
            Error(YamlDocumentCode.IndexOutOfRange, Key, $"The item index is outside the items of {Key}.");

        private static YamlDocumentDiagnostic Unrepresentable(string Key) =>
            Error(YamlDocumentCode.UnrepresentableValue, Key, $"The new value of {Key} can't be written as YAML (it is not valid UTF-16 text or not a supported type).");
    }
}
