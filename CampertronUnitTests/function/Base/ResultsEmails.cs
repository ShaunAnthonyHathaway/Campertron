using System.Globalization;
using System.Net.Mail;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Reads the results email (D72) the way a mail client gets it: the parts of a MailMessage, and the message as
    // System.Net.Mail writes it (SmtpClient with a pickup directory in a temporary folder: nothing reaches a network),
    // parsed into its MIME parts. Also builds searches large enough for the summary path. It uses only what existed
    // before D72 (ProcessConsoleConfig's send seam, MailMessage), so ResultsEmailDeliveryTests can also run against an
    // older library as a negative control.
    internal static class ResultsEmails
    {
        public static string Html(MailMessage Message) => ViewText(Message, "text/html");
        public static string Text(MailMessage Message) => ViewText(Message, "text/plain");

        private static string ViewText(MailMessage Message, string MediaType)
        {
            AlternateView View = Message.AlternateViews.Single(p => p.ContentType.MediaType == MediaType);
            View.ContentStream.Position = 0;
            using var Copy = new MemoryStream();
            View.ContentStream.CopyTo(Copy);
            View.ContentStream.Position = 0;
            return new UTF8Encoding(false, true).GetString(Copy.ToArray());
        }

        public static byte[] AttachmentBytes(Attachment File_)
        {
            File_.ContentStream.Position = 0;
            using var Copy = new MemoryStream();
            File_.ContentStream.CopyTo(Copy);
            File_.ContentStream.Position = 0;
            return Copy.ToArray();
        }

        // the message exactly as System.Net.Mail writes it for sending
        public static string Raw(MailMessage Message)
        {
            string Folder = Path.Join(Path.GetTempPath(), "CampertronResultsEmail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            try
            {
                using (var Client = new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = Folder })
                {
                    Client.Send(Message);
                }
                return File.ReadAllText(Directory.GetFiles(Folder).Single(), Encoding.ASCII);
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        internal sealed class MimePart
        {
            public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
            public string Body = string.Empty;
            public List<MimePart> Parts = new();
            public string ContentType => Headers.TryGetValue("Content-Type", out string? Value) ? Value : string.Empty;
            public string MediaType => ContentType.Split(';')[0].Trim().ToLowerInvariant();
            public string Decoded => new UTF8Encoding(false, true).GetString(System.Convert.FromBase64String(string.Concat(Body.Where(c => !char.IsWhiteSpace(c)))));
            public IEnumerable<MimePart> Leaves() => Parts.Count == 0 ? new[] { this } : Parts.SelectMany(p => p.Leaves());
        }

        // a minimal MIME reader for the messages above: headers (unfolded), multipart bodies split at their boundary.
        // A part's body ends before the CRLF that belongs to the next boundary delimiter (RFC 2046 §5.1.1)
        public static MimePart Parse(string Raw)
        {
            MimePart Part = new MimePart();
            int Split = Raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            string Head = Split < 0 ? Raw : Raw.Substring(0, Split);
            Part.Body = Split < 0 ? string.Empty : Raw.Substring(Split + 4);
            foreach (string Line in Head.Replace("\r\n ", " ").Replace("\r\n\t", " ").Split("\r\n"))
            {
                int Colon = Line.IndexOf(':');
                if (Colon > 0 && !Part.Headers.ContainsKey(Line.Substring(0, Colon).Trim()))
                {
                    Part.Headers[Line.Substring(0, Colon).Trim()] = Line.Substring(Colon + 1).Trim();
                }
            }
            if (Part.MediaType.StartsWith("multipart/", StringComparison.Ordinal))
            {
                string Boundary = Parameter(Part.ContentType, "boundary");
                string Delimiter = "--" + Boundary;
                string Body = "\r\n" + Part.Body;
                string[] Pieces = Body.Split("\r\n" + Delimiter);
                // Pieces[0] is the preamble; the last piece starts with "--" (the close delimiter)
                foreach (string Piece in Pieces.Skip(1))
                {
                    if (Piece.StartsWith("--", StringComparison.Ordinal))
                    {
                        break;
                    }
                    Part.Parts.Add(Parse(Piece.Substring(Piece.IndexOf("\r\n", StringComparison.Ordinal) + 2)));
                }
            }
            return Part;
        }

        public static string Parameter(string Header, string Name)
        {
            foreach (string Item in Header.Split(';').Skip(1))
            {
                int Equals = Item.IndexOf('=');
                if (Equals > 0 && Item.Substring(0, Equals).Trim().Equals(Name, StringComparison.OrdinalIgnoreCase))
                {
                    return Item.Substring(Equals + 1).Trim().Trim('"');
                }
            }
            return string.Empty;
        }

        // one search with Sites campsites open on one date each, spread over November 2026 (single nights)
        public static ConsoleConfig.ConsoleConfigItem Search(string Name, int Sites)
        {
            var Hits = Enumerable.Range(1, Sites)
                .Select(i => OutputStreams.Hit((1000 + i).ToString(CultureInfo.InvariantCulture), "A" + i.ToString("000", CultureInfo.InvariantCulture), new DateTime(2026, 11, 1 + (i - 1) % 30)))
                .OrderBy(p => p.CampsiteAvailableDate).ToArray();
            return OutputStreams.Search(Name, OutputStreams.Header(Name), OutputStreams.Month(new DateTime(2026, 11, 1), Hits));
        }

        public static EmailConfig Settings() => new EmailConfig
        {
            SmtpServer = "smtp.example.invalid",
            SmtpPort = 587,
            SmtpUsername = "user-canary@example.invalid",
            SmtpPassword = "smtp-password-canary",
            SendFromAddress = "campertron@example.invalid",
            SendToAddressList = new List<string> { "someone@example.invalid" }
        };
    }
}
