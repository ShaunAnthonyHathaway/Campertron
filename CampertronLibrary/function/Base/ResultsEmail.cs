using System.Globalization;
using System.Text;
using static ConsoleConfig;

namespace CampertronLibrary.function.Base
{
    // The results email of one search (decision D72): an HTML body in the layout of the results page (ResultsHtml) and
    // its plain-text alternative, both rendered from the same model (ResultsHtml.Build: the tagged output streams, never
    // the console text), and, only when the full results don't fit in the body, the results page as an attachment.
    // - The full results go in the body when the HTML body's size as sent (UTF-8, base64 encoded, with its line breaks)
    //   is at most BodyLimitBytes: Gmail clips an HTML body above about 102 KB (Litmus, Email on Acid, Spam Resource;
    //   Google documents no figure) and Email on Acid advises staying below 80 KB. The encoded size is larger than the
    //   HTML itself, so the margin covers either reading of what Gmail counts. A fixed technical limit, not a setting.
    // - Otherwise the body is a short summary (the title, the search, its counts and a sentence saying to open the
    //   attachment) and the full results page (the page HtmlFile output shows, as written to the cache) is attached as
    //   Campertron-results-yyyy-MM-dd.html (the local date of the check).
    // - Email-safe: inline styles for the light colors (every element that sets a background also sets its text color,
    //   so a client that inverts colors keeps the contrast), a prefers-color-scheme block for clients that support dark
    //   mode, no script, no image, no external reference, no CSS variable or grid. Every dynamic value is HTML-encoded
    //   (and the campsite ID URL-escaped in links); control characters are replaced by spaces. The only links are the
    //   booking links. The email never shows a setting, a secret or a file path. Line breaks are CRLF (canonical MIME).
    public static class ResultsEmail
    {
        public const string Subject = "Campertron Auto-Email";
        public const int BodyLimitBytes = 80_000;
        // the base64 line length of System.Net.Mail's encoder (68 characters and CRLF), checked against the message it
        // writes (ResultsEmailTests); a shorter line only makes the measured size larger
        internal const int Base64LineLength = 68;
        public const string AttachmentNotice = "The full results are too large to show in this email.";

        public sealed class Content
        {
            public string Html { get; init; } = string.Empty;
            public string Text { get; init; } = string.Empty;
            // the encoded size of the HTML body with the full results, compared with BodyLimitBytes
            public int FullBodyEncodedBytes { get; init; }
            public bool FullResultsInBody { get; init; }
            // set only when the full results are attached
            public string? AttachmentName { get; init; }
            public string? AttachmentHtml { get; init; }
        }

        // Page is the full results page of this search (ResultsHtml.Render of the same stream); Date is the check's local
        // date, used only in the attachment's name
        public static Content Compose(ConsoleConfigItem Search, string Page, CultureInfo Culture, DateTime Date)
        {
            ResultsHtml.SearchModel Model = ResultsHtml.Build(Search, 1);
            string Html = Crlf(RenderHtml(Model, Culture, null));
            int Encoded = EncodedSize(Html);
            if (FitsInBody(Encoded))
            {
                return new Content { Html = Html, Text = Crlf(RenderText(Model, Culture, null)), FullBodyEncodedBytes = Encoded, FullResultsInBody = true };
            }
            string Name = AttachmentName(Date);
            return new Content
            {
                Html = Crlf(RenderHtml(Model, Culture, Name)),
                Text = Crlf(RenderText(Model, Culture, Name)),
                FullBodyEncodedBytes = Encoded,
                FullResultsInBody = false,
                AttachmentName = Name,
                AttachmentHtml = Page
            };
        }

        public static bool FitsInBody(int EncodedBytes) => EncodedBytes <= BodyLimitBytes;

        public static string AttachmentName(DateTime Date) => "Campertron-results-" + Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".html";

        // the size of Text as System.Net.Mail writes it as a base64-encoded UTF-8 part: 4 characters for every 3 bytes in
        // lines of Base64LineLength characters, each line (the last one too) followed by CRLF
        public static int EncodedSize(string Text)
        {
            long Bytes = new UTF8Encoding(false).GetByteCount(Text);
            long Chars = (Bytes + 2) / 3 * 4;
            long Lines = (Chars + Base64LineLength - 1) / Base64LineLength;
            return (int)Math.Min(int.MaxValue, Chars + 2 * Lines);
        }

        // ---- HTML ----

        private const string Font = "font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif";
        private const string PageColors = "background-color:#f3f5f3;color:#1a1f1c";
        // the attributes of muted text, with any further style before its color
        private static string Muted(string Style = "") => " class=\"cx-muted\" style=\"" + Style + "color:#4a5550\"";
        private const string Accent = "cx-accent";
        private const string NoticeStyle = "margin:8px 0;padding:10px 14px;border-left:4px solid #a86f00;border-radius:4px;background-color:#fff7e0;color:#1a1f1c";
        private const string SiteBox = "padding:10px 12px;border:1px solid #cfd8d2;border-radius:8px;background-color:#f3f5f3;color:#1a1f1c;font-size:15px";
        private const string SiteStyle = "margin:0 0 8px;" + SiteBox;
        // a consecutive-days card, set apart from the lines above it
        private const string CardStyle = "margin:12px 0 8px;" + SiteBox;

        // the dark colors for clients that support prefers-color-scheme; the light ones are inline
        private const string Css = """
@media (prefers-color-scheme: dark){
.cx-page{background-color:#111513 !important;color:#e6ece8 !important}
.cx-card{background-color:#1a201d !important;color:#e6ece8 !important;border-color:#36423b !important}
.cx-site{background-color:#111513 !important;color:#e6ece8 !important;border-color:#36423b !important}
.cx-notice{background-color:#2b2410 !important;color:#e6ece8 !important;border-color:#e0ad3a !important}
.cx-muted{color:#b2bdb6 !important}
.cx-accent{color:#9fd4b4 !important}
.cx-link{color:#9cc0ff !important}
}
""";

        // the full results (Attachment null) or the summary that names the attached file
        internal static string RenderHtml(ResultsHtml.SearchModel Model, CultureInfo Culture, string? Attachment)
        {
            StringBuilder Html = new StringBuilder();
            Html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
            Html.Append("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" />\n");
            Html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
            Html.Append("<meta name=\"color-scheme\" content=\"light dark\" />\n");
            Html.Append("<meta name=\"supported-color-schemes\" content=\"light dark\" />\n");
            Html.Append("<title>").Append(ResultsHtml.Title).Append("</title>\n");
            Html.Append("<style>\n").Append(Css).Append("</style>\n");
            Html.Append("</head>\n");
            Html.Append("<body class=\"cx-page\" style=\"margin:0;padding:0;").Append(PageColors).Append("\">\n");
            Html.Append("<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" bgcolor=\"#f3f5f3\" class=\"cx-page\" style=\"").Append(PageColors).Append("\">\n");
            Html.Append("<tr><td align=\"center\" style=\"padding:16px 12px\">\n");
            Html.Append("<div style=\"max-width:640px;margin:0 auto;text-align:left;").Append(Font).Append(";font-size:16px;line-height:1.5\">\n");
            Html.Append("<h1 class=\"").Append(Accent).Append("\" style=\"margin:0 0 12px;font-size:26px;line-height:1.2;color:#1d5c3a\">").Append(ResultsHtml.Title).Append("</h1>\n");
            Html.Append("<div class=\"cx-card\" style=\"padding:16px 20px;border:1px solid #cfd8d2;border-radius:12px;background-color:#ffffff;color:#1a1f1c\">\n");
            Html.Append("<h2 style=\"margin:0;font-size:22px;line-height:1.25\">").Append(E(Model.Name)).Append("</h2>\n");
            AppendCampground(Html, Model, Culture);
            if (Attachment == null)
            {
                AppendResults(Html, Model, Culture);
            }
            else
            {
                if (Model.Entries > 0)
                {
                    Html.Append("<p style=\"margin:8px 0 4px\">").Append(E(CountsText(Model, Culture))).Append("</p>\n");
                }
                else
                {
                    Html.Append("<p style=\"margin:8px 0 4px\">").Append(E(ResultsHtml.EmptyMessage)).Append("</p>\n");
                }
                Html.Append("<p class=\"cx-notice\" style=\"").Append(NoticeStyle).Append("\">").Append(E(AttachmentNotice))
                    .Append(" Open the attached file, <strong>").Append(E(Attachment)).Append("</strong>, in a web browser to see every site, date and booking link.</p>\n");
            }
            Html.Append("</div>\n");
            Html.Append("<p").Append(Muted("margin:16px 0 0;font-size:14px;")).Append(">Results come from recreation.gov and can change at any time. The booking links open the campsite on recreation.gov.</p>\n");
            Html.Append("</div>\n</td></tr>\n</table>\n</body>\n</html>\n");
            return Html.ToString();
        }

        private static void AppendCampground(StringBuilder Html, ResultsHtml.SearchModel Model, CultureInfo Culture)
        {
            OutputMetadata? Header = Model.Header;
            if (Header == null)
            {
                return;
            }
            string Campground = CampgroundText(Header);
            if (Campground.Length > 0)
            {
                Html.Append("<p style=\"margin:4px 0;font-weight:600\">").Append(E(Campground)).Append("</p>\n");
            }
            if (Header.ConsecutiveDays > 1)
            {
                Html.Append("<p").Append(Muted("margin:4px 0;")).Append('>').Append(E(ConsecutiveText(Header, Culture))).Append("</p>\n");
            }
        }

        private static void AppendResults(StringBuilder Html, ResultsHtml.SearchModel Model, CultureInfo Culture)
        {
            if (Model.Entries > 0)
            {
                Html.Append("<p style=\"margin:8px 0 4px\">").Append(E(CountsText(Model, Culture))).Append("</p>\n");
                string Times = SharedTimesText(Model);
                if (Times.Length > 0)
                {
                    Html.Append("<p").Append(Muted("margin:4px 0;")).Append('>').Append(E(Times)).Append("</p>\n");
                }
            }
            else if (!Model.HasEmptyMonth)
            {
                AppendEmpty(Html);
            }
            foreach (ResultsHtml.Block ThisBlock in Model.Blocks)
            {
                switch (ThisBlock)
                {
                    case ResultsHtml.SiteCard Card:
                        AppendSiteCard(Html, Card, Model, Culture);
                        break;
                    case ResultsHtml.MonthBlock Month:
                        Html.Append("<h3 class=\"").Append(Accent).Append("\" style=\"margin:20px 0 8px;font-size:18px;line-height:1.3;color:#1d5c3a\">")
                            .Append(E(Month.Month.ToString("Y", Culture))).Append("</h3>\n");
                        foreach (ResultsHtml.DateGroup Group in Month.Groups)
                        {
                            AppendDateGroup(Html, Group, "h4", Model, Culture);
                        }
                        if (Month.Empty && Month.Groups.Count == 0)
                        {
                            AppendEmpty(Html);
                        }
                        break;
                    case ResultsHtml.DateGroup Group:
                        AppendDateGroup(Html, Group, "h3", Model, Culture);
                        break;
                    case ResultsHtml.NoteBlock Note:
                        Html.Append("<p").Append(Muted("margin:4px 0;white-space:pre-wrap;font-size:14px;")).Append('>').Append(E(Note.Text)).Append("</p>\n");
                        break;
                }
            }
        }

        private static void AppendEmpty(StringBuilder Html)
        {
            Html.Append("<p class=\"cx-notice\" style=\"").Append(NoticeStyle).Append("\">").Append(E(ResultsHtml.EmptyMessage)).Append("</p>\n");
        }

        private static void AppendDateGroup(StringBuilder Html, ResultsHtml.DateGroup Group, string Heading, ResultsHtml.SearchModel Model, CultureInfo Culture)
        {
            Html.Append('<').Append(Heading).Append(" style=\"margin:16px 0 8px;font-size:16px\">").Append(ResultsHtml.DateElement(Group.Date, Culture))
                .Append(" <span").Append(Muted("font-weight:normal;")).Append(">· ").Append(E(ResultsHtml.Count(SitesOf(Group), "site", "sites", Culture)))
                .Append("</span></").Append(Heading).Append(">\n");
            foreach (OutputMetadata Hit in Group.Hits)
            {
                Html.Append("<div class=\"cx-site\" style=\"").Append(SiteStyle).Append("\">");
                Html.Append(string.Join("<br />", SiteLines(Hit, Model, true))).Append("<br />");
                AppendBookingLink(Html, Hit);
                Html.Append("</div>\n");
            }
        }

        private static void AppendSiteCard(StringBuilder Html, ResultsHtml.SiteCard Card, ResultsHtml.SearchModel Model, CultureInfo Culture)
        {
            Html.Append("<div class=\"cx-site\" style=\"").Append(CardStyle).Append("\">\n");
            Html.Append("<h3 style=\"margin:0 0 4px;font-size:16px\">").Append(E(ResultsHtml.SiteName(Card.Site))).Append("</h3>\n");
            List<string> Lines = SiteLines(Card.Site, Model, false);
            if (Lines.Count > 0)
            {
                Html.Append("<p style=\"margin:0\">").Append(string.Join("<br />", Lines)).Append("</p>\n");
            }
            if (Card.Dates.Count > 0)
            {
                Html.Append("<p style=\"margin:8px 0 0;font-weight:600\">Available dates:</p>\n<ul style=\"margin:2px 0 8px;padding-left:20px\">\n");
                foreach (OutputMetadata Date in SortedDates(Card))
                {
                    Html.Append("<li>").Append(ResultsHtml.DateElement(Date.Date?.Date, Culture)).Append("</li>\n");
                }
                Html.Append("</ul>\n");
            }
            Html.Append("<p style=\"margin:4px 0 0\">");
            AppendBookingLink(Html, Card.Site);
            Html.Append("</p>\n</div>\n");
        }

        // a site's lines, as on the page: its name with loop and type (or, under its own heading, loop and type alone),
        // its equipment, and check-in/out unless shared by the search
        private static List<string> SiteLines(OutputMetadata Site, ResultsHtml.SearchModel Model, bool WithName)
        {
            List<string> Lines = new List<string>();
            string LoopType = ResultsHtml.JoinParts(Site.CampsiteLoop, Site.CampsiteType);
            if (WithName)
            {
                Lines.Add("<strong>" + E(ResultsHtml.SiteName(Site)) + "</strong>" + (LoopType.Length > 0 ? " <span" + Muted() + ">· " + E(LoopType) + "</span>" : string.Empty));
            }
            else if (LoopType.Length > 0)
            {
                Lines.Add("<span" + Muted() + ">" + E(LoopType) + "</span>");
            }
            string Equipment = EquipmentText(Site);
            if (Equipment.Length > 0)
            {
                Lines.Add("<span style=\"font-size:14px\">" + E(Equipment) + "</span>");
            }
            if (!Model.TimesShared)
            {
                string Times = ResultsHtml.TimesText(Site.Checkin, Site.Checkout);
                if (Times.Length > 0)
                {
                    Lines.Add("<span" + Muted() + ">" + E(Times) + "</span>");
                }
            }
            return Lines;
        }

        private static void AppendBookingLink(StringBuilder Html, OutputMetadata Site)
        {
            if (string.IsNullOrWhiteSpace(Site.CampsiteID))
            {
                Html.Append("<span").Append(Muted()).Append(">No booking link available</span>");
                return;
            }
            Html.Append("<a class=\"cx-link\" href=\"").Append(E(ResultsHtml.BookingUrl(Site.CampsiteID))).Append("\" style=\"color:#0b57d0;text-decoration:underline\">Book ")
                .Append(E(ResultsHtml.SiteName(Site))).Append(" on recreation.gov</a>");
        }

        // ---- plain text ----

        // the plain-text alternative: the same content, one fact per line, the booking links as plain URLs
        internal static string RenderText(ResultsHtml.SearchModel Model, CultureInfo Culture, string? Attachment)
        {
            StringBuilder Text = new StringBuilder();
            Text.Append(ResultsHtml.Title).Append("\n\n");
            Text.Append(T(Model.Name)).Append('\n');
            OutputMetadata? Header = Model.Header;
            if (Header != null)
            {
                string Campground = CampgroundText(Header);
                if (Campground.Length > 0)
                {
                    Text.Append(T(Campground)).Append('\n');
                }
                if (Header.ConsecutiveDays > 1)
                {
                    Text.Append(ConsecutiveText(Header, Culture)).Append('\n');
                }
            }
            if (Attachment != null)
            {
                Text.Append(Model.Entries > 0 ? CountsText(Model, Culture) : ResultsHtml.EmptyMessage).Append("\n\n");
                Text.Append(AttachmentNotice).Append(" Open the attached file, ").Append(T(Attachment))
                    .Append(", in a web browser to see every site, date and booking link.\n");
            }
            else
            {
                AppendTextResults(Text, Model, Culture);
            }
            Text.Append("\nResults come from recreation.gov and can change at any time. The booking links open the campsite on recreation.gov.\n");
            return Text.ToString();
        }

        private static void AppendTextResults(StringBuilder Text, ResultsHtml.SearchModel Model, CultureInfo Culture)
        {
            if (Model.Entries > 0)
            {
                Text.Append(CountsText(Model, Culture)).Append('\n');
                string Times = SharedTimesText(Model);
                if (Times.Length > 0)
                {
                    Text.Append(T(Times)).Append('\n');
                }
            }
            else if (!Model.HasEmptyMonth)
            {
                Text.Append('\n').Append(ResultsHtml.EmptyMessage).Append('\n');
            }
            foreach (ResultsHtml.Block ThisBlock in Model.Blocks)
            {
                switch (ThisBlock)
                {
                    case ResultsHtml.SiteCard Card:
                        Text.Append('\n').Append(T(ResultsHtml.SiteName(Card.Site))).Append('\n');
                        AppendTextDetails(Text, Card.Site, Model, true);
                        if (Card.Dates.Count > 0)
                        {
                            Text.Append("  Available dates:\n");
                            foreach (OutputMetadata Date in SortedDates(Card))
                            {
                                Text.Append("  - ").Append(DateLine(Date.Date?.Date, Culture)).Append('\n');
                            }
                        }
                        AppendTextLink(Text, Card.Site);
                        break;
                    case ResultsHtml.MonthBlock Month:
                        string Title = T(Month.Month.ToString("Y", Culture));
                        Text.Append('\n').Append(Title).Append('\n').Append(new string('=', Math.Max(3, Title.Length))).Append('\n');
                        foreach (ResultsHtml.DateGroup Group in Month.Groups)
                        {
                            AppendTextDateGroup(Text, Group, Model, Culture);
                        }
                        if (Month.Empty && Month.Groups.Count == 0)
                        {
                            Text.Append(ResultsHtml.EmptyMessage).Append('\n');
                        }
                        break;
                    case ResultsHtml.DateGroup Group:
                        AppendTextDateGroup(Text, Group, Model, Culture);
                        break;
                    case ResultsHtml.NoteBlock Note:
                        Text.Append('\n').Append(T(Note.Text)).Append('\n');
                        break;
                }
            }
        }

        private static void AppendTextDateGroup(StringBuilder Text, ResultsHtml.DateGroup Group, ResultsHtml.SearchModel Model, CultureInfo Culture)
        {
            Text.Append('\n').Append(DateLine(Group.Date, Culture)).Append(" · ").Append(ResultsHtml.Count(SitesOf(Group), "site", "sites", Culture)).Append('\n');
            foreach (OutputMetadata Hit in Group.Hits)
            {
                string LoopType = ResultsHtml.JoinParts(Hit.CampsiteLoop, Hit.CampsiteType);
                Text.Append("\n  ").Append(T(ResultsHtml.SiteName(Hit))).Append(LoopType.Length > 0 ? " · " + T(LoopType) : string.Empty).Append('\n');
                AppendTextDetails(Text, Hit, Model, false);
                AppendTextLink(Text, Hit);
            }
        }

        private static void AppendTextDetails(StringBuilder Text, OutputMetadata Site, ResultsHtml.SearchModel Model, bool WithLoopType)
        {
            string LoopType = ResultsHtml.JoinParts(Site.CampsiteLoop, Site.CampsiteType);
            if (WithLoopType && LoopType.Length > 0)
            {
                Text.Append("  ").Append(T(LoopType)).Append('\n');
            }
            string Equipment = EquipmentText(Site);
            if (Equipment.Length > 0)
            {
                Text.Append("  ").Append(T(Equipment)).Append('\n');
            }
            if (!Model.TimesShared)
            {
                string Times = ResultsHtml.TimesText(Site.Checkin, Site.Checkout);
                if (Times.Length > 0)
                {
                    Text.Append("  ").Append(T(Times)).Append('\n');
                }
            }
        }

        private static void AppendTextLink(StringBuilder Text, OutputMetadata Site)
        {
            if (string.IsNullOrWhiteSpace(Site.CampsiteID))
            {
                Text.Append("  No booking link available\n");
                return;
            }
            Text.Append("  Book: ").Append(ResultsHtml.BookingUrl(Site.CampsiteID)).Append('\n');
        }

        private static string DateLine(DateTime? Date, CultureInfo Culture) =>
            Date == null ? "Date not available" : T(ResultsHtml.DateText(Date.Value, Culture));

        // ---- shared ----

        private static string CampgroundText(OutputMetadata Header) => ResultsHtml.JoinParts(Header.CampgroundName, Header.ParkName,
            string.IsNullOrWhiteSpace(Header.CampgroundID) ? null : "Campground ID " + Header.CampgroundID);

        private static string ConsecutiveText(OutputMetadata Header, CultureInfo Culture) =>
            "Dates shown belong to runs of at least " + Header.ConsecutiveDays.ToString(Culture) + " consecutive available dates.";

        private static string CountsText(ResultsHtml.SearchModel Model, CultureInfo Culture) =>
            ResultsHtml.Count(Model.Entries, "availability entry", "availability entries", Culture) + " for " +
            ResultsHtml.Count(Model.Sites, "site", "sites", Culture) + " on " + ResultsHtml.Count(Model.Dates, "date", "dates", Culture);

        private static string SharedTimesText(ResultsHtml.SearchModel Model)
        {
            if (!Model.TimesShared)
            {
                return string.Empty;
            }
            string Times = ResultsHtml.TimesText(Model.SharedCheckin, Model.SharedCheckout);
            return Times.Length > 0 ? Times + " for every site shown." : string.Empty;
        }

        private static string EquipmentText(OutputMetadata Site)
        {
            List<string> Equipment = (Site.Equipment ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return Equipment.Count > 0 ? "Equipment: " + string.Join(", ", Equipment) : string.Empty;
        }

        private static int SitesOf(ResultsHtml.DateGroup Group) => Group.Hits.Select(p => p.CampsiteID ?? p.CampsiteName ?? string.Empty).Distinct().Count();

        // chronological, as on the page: FilterConsecutiveDays emits a campsite's dates in ConcurrentBag order
        private static IEnumerable<OutputMetadata> SortedDates(ResultsHtml.SiteCard Card) => Card.Dates.OrderBy(p => p.Date ?? DateTime.MaxValue);

        // HTML-encoded, control characters (a line break inside a value included) replaced by spaces
        private static string E(string? Value) => ResultsHtml.E(T(Value));

        // plain text: control characters (a line break inside a value included) replaced by spaces, so a value can't
        // make lines of its own
        private static string T(string? Value)
        {
            string Text = Value ?? string.Empty;
            if (!Text.Any(char.IsControl))
            {
                return Text;
            }
            StringBuilder Clean = new StringBuilder(Text.Length);
            foreach (char C in Text)
            {
                Clean.Append(char.IsControl(C) ? ' ' : C);
            }
            return Clean.ToString();
        }

        private static string Crlf(string Text) => Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
