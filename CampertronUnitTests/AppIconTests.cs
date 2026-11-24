using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CampertronUnitTests
{
    // The unified app icon (decision D89, applied by v0.3.0's PR 11): the approved SVG sources in assets/icon, the
    // campertron.ico that assets/icon/generate-icon.py renders from them (16, 20 and 24 and 32 px from their own drawings,
    // 48 to 256 px from the canonical one), and the three applications that use it: CampertronGUI (its executable and its
    // window), CampertronConsole (its executable) and CampertronWeb (the favicon). The site's own icons are not this one
    // until PR 14. Read-only on the repository; no renderer is needed: the frames are PNGs read here.
    public class AppIconTests
    {
        private static readonly string[] Palette =
        {
            "#3A7C7E", "#134347", "#0B2B2E", "#0F373A", "#557F84", "#436D72", "#4C767B",
            "#0A1C1F", "#FF3B2E", "#FFD9C2", "#E4521B", "#FFB02E"
        };
        private static readonly string[] Sources =
        {
            "campertron-icon.svg", "campertron-icon-small-16.svg", "campertron-icon-small-20.svg",
            "campertron-icon-small-24.svg", "campertron-icon-small-32.svg"
        };
        private static readonly int[] FrameSizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
        private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

        private static string RepositoryRoot()
        {
            for (DirectoryInfo? At = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); At != null; At = At.Parent)
            {
                if (File.Exists(Path.Join(At.FullName, "Campertron.sln")) && Directory.Exists(Path.Join(At.FullName, "assets", "icon")))
                {
                    return At.FullName;
                }
            }
            Assert.Fail("the repository (Campertron.sln and assets/icon/) was not found above the test folder");
            return string.Empty;
        }

        private static string Asset(string Name) => Path.Join(RepositoryRoot(), "assets", "icon", Name);

        [Test]
        public void TheIconFolder_HoldsTheApprovedSources_TheGeneratorAndTheIcon_AndNothingElse()
        {
            string[] Files = Directory.GetFiles(Path.Join(RepositoryRoot(), "assets", "icon")).Select(F => Path.GetFileName(F)).Order(StringComparer.Ordinal).ToArray();
            Assert.That(Files, Is.EqualTo(Sources.Append("generate-icon.py").Append("campertron.ico").Order(StringComparer.Ordinal).ToArray()),
                "no campfire variant, review sheet, loose render or other production source");
            Assert.That(Directory.GetDirectories(Path.Join(RepositoryRoot(), "assets", "icon")), Is.Empty);
        }

        [TestCase("campertron-icon.svg", 512)]
        [TestCase("campertron-icon-small-16.svg", 16)]
        [TestCase("campertron-icon-small-20.svg", 20)]
        [TestCase("campertron-icon-small-24.svg", 24)]
        [TestCase("campertron-icon-small-32.svg", 32)]
        public void EachSource_IsPlainShapesAndGradients_InTheApprovedPalette_OnItsCanvas(string Name, int Size)
        {
            string Text = File.ReadAllText(Asset(Name));
            XDocument Document = XDocument.Parse(Text, LoadOptions.None);
            XElement Root = Document.Root!;
            Assert.That(Root.Name, Is.EqualTo(Svg + "svg"));
            Assert.That((string?)Root.Attribute("viewBox"), Is.EqualTo($"0 0 {Size} {Size}"));
            Assert.That((string?)Root.Attribute("width"), Is.EqualTo(Size.ToString()));
            Assert.That((string?)Root.Attribute("height"), Is.EqualTo(Size.ToString()));
            Assert.That(Document.DescendantNodes().Where(Node => Node is XComment or XProcessingInstruction or XCData), Is.Empty, "no comment or processing instruction");

            var Elements = new HashSet<string> { "svg", "defs", "linearGradient", "radialGradient", "stop", "rect", "circle", "path" };
            var Attributes = new HashSet<string> { "width", "height", "viewBox", "id", "x1", "y1", "x2", "y2", "offset", "stop-color", "stop-opacity", "x", "y", "rx", "cx", "cy", "r", "fill", "d" };
            foreach (XElement Element in Root.DescendantsAndSelf())
            {
                Assert.That(Element.Name.Namespace, Is.EqualTo(Svg), Element.Name.ToString());
                Assert.That(Elements, Does.Contain(Element.Name.LocalName), "no text, font, image, script, style, filter, mask, group, link or metadata: " + Element.Name.LocalName);
                foreach (XAttribute Attribute in Element.Attributes().Where(A => !A.IsNamespaceDeclaration))
                {
                    Assert.That(Attributes, Does.Contain(Attribute.Name.ToString()), "no href, style, class, transform, opacity or visibility: " + Attribute.Name);
                }
            }
            Assert.That(Text, Does.Not.Contain("data:").And.Not.Contain("base64").And.Not.Contain("https:"));
            Assert.That(Regex.Matches(Text, "http://").Count, Is.EqualTo(1), "only the SVG namespace");

            HashSet<string> Defined = Root.Descendants().Select(E => (string?)E.Attribute("id")).OfType<string>().ToHashSet();
            foreach (Match Reference in Regex.Matches(Text, @"url\(([^)]*)\)"))
            {
                Assert.That(Reference.Groups[1].Value, Does.StartWith("#"), "only the file's own gradients");
                Assert.That(Defined, Does.Contain(Reference.Groups[1].Value.Substring(1)));
            }
            string[] Colours = Regex.Matches(Text, "#[0-9A-Fa-f]{6}\\b").Select(M => M.Value.ToUpperInvariant()).Distinct().ToArray();
            Assert.That(Palette, Is.SupersetOf(Colours), "the approved palette only");
            Assert.That(Text, Does.Not.Match("#[0-9A-Fa-f]{3}\\b|rgb\\(|hsl\\(|currentColor"));

            //every shape is filled, has a size and lies on the canvas; paths use absolute commands only
            foreach (XElement Shape in Root.Descendants().Where(E => E.Name.LocalName is "rect" or "circle" or "path"))
            {
                string Fill = (string?)Shape.Attribute("fill") ?? string.Empty;
                Assert.That(Fill, Does.Match("^(#[0-9A-F]{6}|url\\(#[a-z]+\\))$"), "every shape is filled: " + Shape);
                double Get(string A) => double.Parse((string?)Shape.Attribute(A) ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                switch (Shape.Name.LocalName)
                {
                    case "rect":
                        Assert.That(Get("width") > 0 && Get("height") > 0 && Get("x") >= 0 && Get("y") >= 0 && Get("x") + Get("width") <= Size && Get("y") + Get("height") <= Size, Is.True, Shape.ToString());
                        break;
                    case "circle":
                        Assert.That(Get("r") > 0 && Get("cx") - Get("r") >= 0 && Get("cy") - Get("r") >= 0 && Get("cx") + Get("r") <= Size && Get("cy") + Get("r") <= Size, Is.True, Shape.ToString());
                        break;
                    default:
                        string D = (string)Shape.Attribute("d")!;
                        Assert.That(D, Does.Match("^[MLHVQAZ0-9 .]+$"), "absolute commands only: " + D);
                        Assert.That(Regex.Matches(D, @"\d+(\.\d+)?").Select(M => double.Parse(M.Value, System.Globalization.CultureInfo.InvariantCulture)), Is.All.InRange(0.0, (double)Size), D);
                        break;
                }
            }
        }

        // The ICO's frames: (size, PNG bytes), in the file's order
        private static List<(int Size, byte[] Png)> Frames(byte[] Ico)
        {
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(Ico.AsSpan(0)), Is.EqualTo(0), "reserved");
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(Ico.AsSpan(2)), Is.EqualTo(1), "an icon, not a cursor");
            int Count = BinaryPrimitives.ReadUInt16LittleEndian(Ico.AsSpan(4));
            var Result = new List<(int, byte[])>();
            int Expected = 6 + 16 * Count;
            for (int I = 0; I < Count; I++)
            {
                ReadOnlySpan<byte> Entry = Ico.AsSpan(6 + 16 * I, 16);
                int Width = Entry[0] == 0 ? 256 : Entry[0], Height = Entry[1] == 0 ? 256 : Entry[1];
                Assert.That((Entry[2], Entry[3]), Is.EqualTo(((byte)0, (byte)0)), "no palette");
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(Entry.Slice(4)), Is.EqualTo(1), "planes");
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(Entry.Slice(6)), Is.EqualTo(32), "32 bits per pixel");
                int Length = (int)BinaryPrimitives.ReadUInt32LittleEndian(Entry.Slice(8));
                int Offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(Entry.Slice(12));
                Assert.That(Offset, Is.EqualTo(Expected), "the frames follow the directory, one after another");
                Expected += Length;
                Assert.That(Width, Is.EqualTo(Height));
                Result.Add((Width, Ico.AsSpan(Offset, Length).ToArray()));
            }
            Assert.That(Ico.Length, Is.EqualTo(Expected), "nothing after the last frame");
            return Result;
        }

        // A PNG's chunks, each CRC checked
        private static List<(string Kind, byte[] Data)> Chunks(byte[] Png)
        {
            Assert.That(Png.AsSpan(0, 8).ToArray(), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "a PNG");
            var Result = new List<(string, byte[])>();
            for (int At = 8; At < Png.Length;)
            {
                int Length = (int)BinaryPrimitives.ReadUInt32BigEndian(Png.AsSpan(At));
                string Kind = System.Text.Encoding.ASCII.GetString(Png, At + 4, 4);
                byte[] Data = Png.AsSpan(At + 8, Length).ToArray();
                uint Crc = BinaryPrimitives.ReadUInt32BigEndian(Png.AsSpan(At + 8 + Length));
                Assert.That(Crc32(Png.AsSpan(At + 4, 4 + Length)), Is.EqualTo(Crc), Kind + "'s CRC");
                Result.Add((Kind, Data));
                At += 12 + Length;
            }
            return Result;
        }

        // PNG's CRC-32 (ISO 3309), so that no package is needed
        private static uint Crc32(ReadOnlySpan<byte> Bytes)
        {
            uint Crc = 0xFFFFFFFF;
            foreach (byte B in Bytes)
            {
                Crc ^= B;
                for (int K = 0; K < 8; K++)
                {
                    Crc = (Crc & 1) != 0 ? 0xEDB88320 ^ (Crc >> 1) : Crc >> 1;
                }
            }
            return ~Crc;
        }

        // An 8-bit RGBA, non-interlaced PNG's pixels (the only kind the generator writes)
        private static byte[] Pixels(byte[] Png, int Size)
        {
            List<(string Kind, byte[] Data)> All = Chunks(Png);
            byte[] Header = All[0].Data;
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(Header.AsSpan(0)), Is.EqualTo(Size), "width");
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(Header.AsSpan(4)), Is.EqualTo(Size), "height");
            Assert.That(Header.AsSpan(8, 5).ToArray(), Is.EqualTo(new byte[] { 8, 6, 0, 0, 0 }), "8-bit RGBA, not interlaced");
            using var Compressed = new MemoryStream(All.Where(C => C.Kind == "IDAT").SelectMany(C => C.Data).ToArray());
            using var Inflater = new ZLibStream(Compressed, CompressionMode.Decompress);
            using var Raw = new MemoryStream();
            Inflater.CopyTo(Raw);
            byte[] Filtered = Raw.ToArray();
            int Stride = Size * 4;
            Assert.That(Filtered.Length, Is.EqualTo((Stride + 1) * Size));
            byte[] Out = new byte[Stride * Size];
            for (int Y = 0; Y < Size; Y++)
            {
                byte Filter = Filtered[Y * (Stride + 1)];
                for (int X = 0; X < Stride; X++)
                {
                    int A = X >= 4 ? Out[Y * Stride + X - 4] : 0, B = Y > 0 ? Out[(Y - 1) * Stride + X] : 0, C = X >= 4 && Y > 0 ? Out[(Y - 1) * Stride + X - 4] : 0;
                    int P = A + B - C, Pa = Math.Abs(P - A), Pb = Math.Abs(P - B), Pc = Math.Abs(P - C);
                    int Predicted = Filter switch
                    {
                        0 => 0, 1 => A, 2 => B, 3 => (A + B) / 2,
                        4 => Pa <= Pb && Pa <= Pc ? A : Pb <= Pc ? B : C,
                        _ => throw new AssertionException("unknown PNG filter " + Filter)
                    };
                    Out[Y * Stride + X] = (byte)(Filtered[Y * (Stride + 1) + 1 + X] + Predicted);
                }
            }
            return Out;
        }

        [Test]
        public void TheIcon_HasOneFramePerApprovedSize_EachAnRgbaPngWithoutMetadata()
        {
            List<(int Size, byte[] Png)> All = Frames(File.ReadAllBytes(Asset("campertron.ico")));
            Assert.That(All.Select(F => F.Size), Is.EqualTo(FrameSizes), "16, 20, 24 and 32 px from their own drawings; 48 to 256 px from the canonical one");
            foreach ((int Size, byte[] Png) in All)
            {
                List<string> Kinds = Chunks(Png).Select(C => C.Kind).ToList();
                Assert.That(Kinds.First(), Is.EqualTo("IHDR"));
                Assert.That(Kinds.Last(), Is.EqualTo("IEND"));
                Assert.That(Kinds.Distinct(), Is.EqualTo(new[] { "IHDR", "IDAT", "IEND" }), Size + " px: no text, time, EXIF, colour profile or background chunk");
                Assert.That(Pixels(Png, Size).Length, Is.EqualTo(Size * Size * 4));
            }
        }

        // the rounded square of each drawing: its corner radius at the frame's size
        private static double Radius(int Size) => Size switch { 16 => 3.5, 20 => 4.5, 24 => 5.5, 32 => 7, _ => 112.0 * Size / 512 };

        // how far a pixel's centre lies inside the rounded square (negative: outside)
        private static double Inside(int Size, int X, int Y)
        {
            double R = Radius(Size), Px = X + 0.5, Py = Y + 0.5;
            double Cx = Math.Clamp(Px, R, Size - R), Cy = Math.Clamp(Py, R, Size - R);
            return R - Math.Sqrt((Px - Cx) * (Px - Cx) + (Py - Cy) * (Py - Cy));
        }

        [Test]
        public void EachFrame_IsOpaqueInsideTheRoundedSquare_TransparentOutside_WithNoFringe()
        {
            int[] Edge = { 0x3A, 0x7C, 0x7E, 0x13, 0x43, 0x47, 0x0B, 0x2B, 0x2E, 0x0F, 0x37, 0x3A };
            foreach ((int Size, byte[] Png) in Frames(File.ReadAllBytes(Asset("campertron.ico"))))
            {
                byte[] P = Pixels(Png, Size);
                for (int Y = 0; Y < Size; Y++)
                {
                    for (int X = 0; X < Size; X++)
                    {
                        int I = (Y * Size + X) * 4, Alpha = P[I + 3];
                        double Depth = Inside(Size, X, Y);
                        if (Depth >= 1)
                        {
                            Assert.That(Alpha, Is.EqualTo(255), $"{Size} px ({X},{Y}): opaque inside the square");
                        }
                        else if (Depth <= -1)
                        {
                            Assert.That(Alpha, Is.EqualTo(0), $"{Size} px ({X},{Y}): transparent outside it, no background");
                        }
                        else if (Alpha < 255)
                        {
                            //an edge pixel: the outline and the background blended with transparency, never a light or dark
                            //halo: each channel within the outline's and the background's colours, or, composited on any
                            //surface, within one 8-bit level of one of them (a nearly transparent pixel's colour is coarse)
                            bool Blend = Enumerable.Range(0, 3).All(K => P[I + K] >= Enumerable.Range(0, 4).Min(C => Edge[C * 3 + K]) - 3 && P[I + K] <= Enumerable.Range(0, 4).Max(C => Edge[C * 3 + K]) + 3);
                            double Worst = Enumerable.Range(0, 4).Min(C => Enumerable.Range(0, 3).Max(K => Math.Abs(P[I + K] - Edge[C * 3 + K]) * Alpha / 255.0));
                            Assert.That(Blend || Worst < 1.0, Is.True, $"{Size} px ({X},{Y}): no fringe (#{P[I]:X2}{P[I + 1]:X2}{P[I + 2]:X2}, alpha {Alpha})");
                        }
                    }
                }
                Assert.That(new[] { P[3], P[(Size - 1) * 4 + 3], P[(Size * (Size - 1)) * 4 + 3], P[(Size * Size - 1) * 4 + 3] }, Is.All.EqualTo(0), Size + " px: transparent corners");
            }
        }

        // Pixels whose colour each approved drawing fixes exactly (flat areas away from every edge): the outline, the
        // background, the helmet, the visor, an eye and its core, the tent's fly and its door. A frame rendered from another
        // drawing (the canonical one at a small size, a campfire variant) or on a sheet's background misses them.
        [TestCase(16, "8,0=#3A7C7E 2,8=#0F373A 7,2=#4C767B 7,4=#0A1C1F 5,5=#FF3B2E 10,6=#FF3B2E 4,13=#E4521B 8,13=#FFB02E")]
        [TestCase(20, "10,0=#3A7C7E 2,10=#0F373A 9,3=#4C767B 10,5=#0A1C1F 7,6=#FF3B2E 12,7=#FF3B2E 5,16=#E4521B 10,16=#FFB02E")]
        [TestCase(24, "12,0=#3A7C7E 2,12=#0F373A 11,4=#4C767B 12,6=#0A1C1F 8,8=#FF3B2E 9,8=#FFD9C2 14,8=#FFD9C2 6,20=#E4521B 12,20=#FFB02E")]
        [TestCase(32, "16,0=#3A7C7E 2,16=#0F373A 15,5=#4C767B 10,11=#0A1C1F 7,27=#E4521B 16,27=#FFB02E")]
        [TestCase(256, "128,1=#3A7C7E 84,70=#0A1C1F 108,88=#FFD9C2 148,88=#FFD9C2 70,215=#E4521B 128,210=#FFB02E 128,218=#FFB02E")]
        [TestCase(128, "64,0=#3A7C7E 42,35=#0A1C1F 54,44=#FFD9C2 74,44=#FFD9C2 35,107=#E4521B 64,105=#FFB02E")]
        public void EachFrame_ShowsTheApprovedDrawingForItsSize(int Size, string Samples)
        {
            byte[] Png = Frames(File.ReadAllBytes(Asset("campertron.ico"))).Single(F => F.Size == Size).Png;
            byte[] P = Pixels(Png, Size);
            foreach (string Sample in Samples.Split(' '))
            {
                string[] Parts = Sample.Split('=');
                int[] At = Parts[0].Split(',').Select(int.Parse).ToArray();
                int I = (At[1] * Size + At[0]) * 4;
                string Colour = $"#{P[I]:X2}{P[I + 1]:X2}{P[I + 2]:X2}";
                Assert.That((Colour, P[I + 3]), Is.EqualTo((Parts[1], (byte)255)), $"{Size} px at ({At[0]},{At[1]})");
            }
        }

        [Test]
        public void TheGradients_RunTopToBottom_AtTheLargeSizes_AndNotAtTheSmallOnes()
        {
            foreach ((int Size, byte[] Png) in Frames(File.ReadAllBytes(Asset("campertron.ico"))))
            {
                byte[] P = Pixels(Png, Size);
                //the background's colour just inside the left edge, near the top and near the bottom
                int X = Math.Max(2, Size / 32 + 1);
                string Top = Colour(P, Size, X, Size / 2 - Size / 4), Bottom = Colour(P, Size, X, Size / 2 + Size / 4);
                if (Size <= 32)
                {
                    Assert.That((Top, Bottom), Is.EqualTo(("#0F373A", "#0F373A")), Size + " px: flat colours");
                }
                else
                {
                    Assert.That(Top, Is.Not.EqualTo(Bottom), Size + " px: the background's gradient");
                }
            }
        }

        private static string Colour(byte[] P, int Size, int X, int Y)
        {
            int I = (Y * Size + X) * 4;
            return $"#{P[I]:X2}{P[I + 1]:X2}{P[I + 2]:X2}";
        }

        [Test]
        public void TheApplications_UseTheIcon_GuiExecutableAndWindow_ConsoleExecutable_WebFavicon()
        {
            string Root = RepositoryRoot();
            const string Icon = @"..\assets\icon\campertron.ico";
            XDocument Gui = XDocument.Load(Path.Join(Root, "CampertronGUI", "CampertronGUI.csproj"));
            XDocument Console_ = XDocument.Load(Path.Join(Root, "CampertronConsole", "CampertronConsole.csproj"));
            XDocument Web = XDocument.Load(Path.Join(Root, "CampertronWeb", "CampertronWeb.csproj"));
            Assert.That(Gui.Descendants("ApplicationIcon").Select(E => E.Value), Is.EqualTo(new[] { Icon }), "the GUI's Windows executable");
            Assert.That(Console_.Descendants("ApplicationIcon").Select(E => E.Value), Is.EqualTo(new[] { Icon }), "the console's Windows executable");
            Assert.That(Gui.Descendants("AvaloniaResource").Where(E => (string?)E.Attribute("Include") == Icon).Select(E => (string?)E.Attribute("Link")),
                Is.EqualTo(new[] { @"Assets\campertron.ico" }), "the window's icon, as an Avalonia resource");
            Assert.That(File.ReadAllText(Path.Join(Root, "CampertronGUI", "Views", "MainWindow.axaml")), Does.Contain("Icon=\"/Assets/campertron.ico\""));
            Assert.That(Web.Descendants("EmbeddedResource").Where(E => (string?)E.Attribute("Include") == Icon).Select(E => (string?)E.Attribute("LogicalName")),
                Is.EqualTo(new[] { "CampertronWeb.Assets.campertron.ico" }), "the web UI's favicon, embedded");
            foreach (XDocument Project in new[] { Gui, Console_, Web })
            {
                Assert.That(Project.ToString(), Does.Not.Contain("campfire"));
            }
        }

        [Test]
        public void TheIconsFiles_NameNoPrivatePathAndNoDesignReviewFile()
        {
            string Root = RepositoryRoot();
            var Pattern = new Regex(@"/home/|/Users/|[A-Za-z]:\\Users\\|icon-review|implementation-check|campfire", RegexOptions.IgnoreCase);
            string[] Files =
            {
                Path.Join(Root, "assets", "icon", "generate-icon.py"), Path.Join(Root, "CampertronGUI", "CampertronGUI.csproj"),
                Path.Join(Root, "CampertronGUI", "Views", "MainWindow.axaml"), Path.Join(Root, "CampertronConsole", "CampertronConsole.csproj"),
                Path.Join(Root, "CampertronWeb", "CampertronWeb.csproj"), Path.Join(Root, "CampertronWeb", "WebAssets.cs")
            };
            foreach (string File_ in Files.Concat(Sources.Select(Asset)))
            {
                Assert.That(Pattern.Match(File.ReadAllText(File_)).Value, Is.Empty, File_);
            }
            string IcoText = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(Asset("campertron.ico")));
            Assert.That(Pattern.Match(IcoText).Value, Is.Empty, "the icon's bytes name no path");
        }

        // D89: the site's favicon, apple-touch icon and header logo become this icon only in PR 14, after v0.3.0 is
        // published (a merged change to site/ publishes it, D87), and the site's hero image stays the full logo. PR 14
        // updates these hashes with its own approval.
        [TestCase("site/favicon.png", "261681f7565cce8053c2147d78fbe7f95f23ab68a4c8d2c1a2a79d199ef62148")]
        [TestCase("site/apple-touch-icon.png", "a2f419f63ed91c5cc8639e67a73f8537453714eaa949bc201db2ccc55b32362a")]
        [TestCase("site/img/logo.jpg", "60f58d6c67fd103c90c972972c3a89d9f24c35ff4f815147ca6eb058aa78f2dd")]
        public void TheSitesIcons_AreUnchanged_UntilPr14(string File_, string Sha256)
        {
            string Actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Join(RepositoryRoot(), File_)))).ToLowerInvariant();
            Assert.That(Actual, Is.EqualTo(Sha256), File_);
        }
    }
}
