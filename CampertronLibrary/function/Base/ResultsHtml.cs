using System.Globalization;
using System.Net;
using System.Text;
using static ConsoleConfig;

namespace CampertronLibrary.function.Base
{
    // Renders the HTML results page (HtmlFile output and the email body/attachment) from the tagged output streams
    // (ConsoleConfigValue.Kind/Metadata), one section per search, in the order supplied. It never parses the console
    // text. Results keep their stream order: single nights are grouped by month and by consecutive runs of the same
    // date, consecutive-day results by campsite (its dates sorted chronologically, as FilterConsecutiveDays gives them in
    // ConcurrentBag order). Every dynamic value is HTML-encoded. The page is one self-contained
    // HTML5 document: inline CSS, no script, no external reference, readable without CSS (email clients).
    // Untyped values (Kind == Text) are a fallback: consecutive Write values are joined up to the next WriteLine and
    // a nonblank line is shown as an encoded paragraph; blank lines are dropped (they only spaced the console).
    public static class ResultsHtml
    {
        public const string Title = "Campertron results";
        public const string EmptyMessage = "No new matching availability to display.";
        public const string BookingUrlPrefix = "https://www.recreation.gov/camping/campsites/";

        public static string Render(IEnumerable<ConsoleConfigItem> Searches, DateTimeOffset Generated, CultureInfo Culture)
        {
            List<SearchModel> Models = new List<SearchModel>();
            foreach (ConsoleConfigItem Search in Searches)
            {
                Models.Add(Build(Search, Models.Count + 1));
            }

            StringBuilder Html = new StringBuilder();
            Html.Append("<!DOCTYPE html>\n");
            Html.Append("<html lang=\"en\">\n<head>\n");
            Html.Append("<meta charset=\"utf-8\" />\n");
            Html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />\n");
            Html.Append("<meta name=\"color-scheme\" content=\"light dark\" />\n");
            Html.Append("<title>").Append(Title).Append("</title>\n");
            Html.Append("<style>").Append(Css).Append("</style>\n");
            Html.Append("</head>\n<body>\n");

            Html.Append("<header class=\"page\">\n");
            Html.Append("<h1>").Append(Title).Append("</h1>\n");
            Html.Append("<p class=\"meta\">Generated <time datetime=\"")
                .Append(Generated.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("\">")
                .Append(E(Generated.ToString("g", Culture))).Append("</time> · ")
                .Append(E(Count(Models.Count, "search", "searches", Culture))).Append("</p>\n");
            if (Models.Count > 1)
            {
                Html.Append("<nav aria-label=\"Searches\">\n<ul>\n");
                foreach (SearchModel Model in Models)
                {
                    Html.Append("<li><a href=\"#").Append(Model.Id).Append("\">").Append(E(Model.Name)).Append("</a> <span class=\"muted\">· ")
                        .Append(E(Model.Entries > 0 ? Count(Model.Entries, "availability entry", "availability entries", Culture) : "no new matching availability"))
                        .Append("</span></li>\n");
                }
                Html.Append("</ul>\n</nav>\n");
            }
            Html.Append("</header>\n");

            Html.Append("<main>\n");
            if (Models.Count == 0)
            {
                Html.Append("<p class=\"empty\">").Append(EmptyMessage).Append("</p>\n");
            }
            foreach (SearchModel Model in Models)
            {
                RenderSearch(Html, Model, Culture);
            }
            Html.Append("</main>\n");

            Html.Append("<footer class=\"page\">\n<p>Results come from recreation.gov and can change at any time. The booking links open the campsite on recreation.gov.</p>\n</footer>\n");
            Html.Append("</body>\n</html>\n");
            return Html.ToString();
        }

        // ---- model built from one tagged stream ----

        private abstract class Block { }
        private sealed class MonthBlock : Block
        {
            public DateTime Month;
            public bool Empty;
            public List<DateGroup> Groups = new List<DateGroup>();
        }
        private sealed class DateGroup : Block
        {
            public DateTime? Date;
            public List<OutputMetadata> Hits = new List<OutputMetadata>();
        }
        private sealed class SiteCard : Block
        {
            public OutputMetadata Site = new OutputMetadata();
            public List<OutputMetadata> Dates = new List<OutputMetadata>();
        }
        private sealed class NoteBlock : Block
        {
            public string Text = string.Empty;
        }
        private sealed class SearchModel
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public OutputMetadata? Header;
            public List<Block> Blocks = new List<Block>();
            public int Entries;
            public int Sites;
            public int Dates;
            public bool HasEmptyMonth;
            public string? SharedCheckin;
            public string? SharedCheckout;
            public bool TimesShared;
        }

        private static SearchModel Build(ConsoleConfigItem Search, int Number)
        {
            SearchModel Model = new SearchModel { Id = "search-" + Number.ToString(CultureInfo.InvariantCulture) };
            MonthBlock? CurrentMonth = null;
            DateGroup? CurrentGroup = null;
            SiteCard? CurrentCard = null;
            StringBuilder Pending = new StringBuilder();
            List<OutputMetadata> AllResults = new List<OutputMetadata>();

            void Flush()
            {
                string Text = Pending.ToString().Trim();
                Pending.Clear();
                if (Text.Length > 0)
                {
                    Model.Blocks.Add(new NoteBlock { Text = Text });
                    CurrentMonth = null;
                    CurrentGroup = null;
                    CurrentCard = null;
                }
            }

            foreach (ConsoleConfigValue Value in Search.Values ?? new List<ConsoleConfigValue>())
            {
                if (Value.Kind == OutputKind.Text)
                {
                    Pending.Append(Value.ConfigValue);
                    if (Value.ConfigType != ConfigType.Write && Value.ConfigType != ConfigType.WriteColor)
                    {
                        Flush();
                    }
                    continue;
                }
                Flush();
                OutputMetadata Meta = Value.Metadata ?? new OutputMetadata();
                switch (Value.Kind)
                {
                    case OutputKind.SearchHeader:
                        if (Model.Header == null)
                        {
                            Model.Header = Meta;
                        }
                        else
                        {
                            Model.Blocks.Add(new NoteBlock { Text = JoinParts(Meta.SearchName, Meta.CampgroundName, Meta.ParkName) });
                        }
                        break;
                    case OutputKind.MonthHeader:
                        CurrentMonth = new MonthBlock { Month = Meta.Date ?? DateTime.MinValue };
                        Model.Blocks.Add(CurrentMonth);
                        CurrentGroup = null;
                        CurrentCard = null;
                        break;
                    case OutputKind.NoEntries:
                        DateTime EmptyMonth = Meta.Date ?? DateTime.MinValue;
                        if (CurrentMonth == null || CurrentMonth.Month != EmptyMonth)
                        {
                            CurrentMonth = new MonthBlock { Month = EmptyMonth };
                            Model.Blocks.Add(CurrentMonth);
                        }
                        CurrentMonth.Empty = true;
                        Model.HasEmptyMonth = true;
                        CurrentGroup = null;
                        CurrentCard = null;
                        break;
                    case OutputKind.Availability:
                        DateTime? HitDate = Meta.Date?.Date;
                        if (CurrentGroup == null || CurrentGroup.Date != HitDate)
                        {
                            CurrentGroup = new DateGroup { Date = HitDate };
                            if (CurrentMonth != null)
                            {
                                CurrentMonth.Groups.Add(CurrentGroup);
                            }
                            else
                            {
                                Model.Blocks.Add(CurrentGroup);
                            }
                        }
                        CurrentGroup.Hits.Add(Meta);
                        AllResults.Add(Meta);
                        CurrentCard = null;
                        break;
                    case OutputKind.ConsecutiveSite:
                        CurrentCard = new SiteCard { Site = Meta };
                        Model.Blocks.Add(CurrentCard);
                        CurrentGroup = null;
                        break;
                    case OutputKind.ConsecutiveDate:
                        if (CurrentCard == null)
                        {
                            // a date without its site part: the date's metadata carries the same site values
                            CurrentCard = new SiteCard { Site = Meta };
                            Model.Blocks.Add(CurrentCard);
                        }
                        CurrentCard.Dates.Add(Meta);
                        AllResults.Add(Meta);
                        CurrentGroup = null;
                        break;
                    default:
                        // Detail: rendered by its tagged record
                        break;
                }
            }
            Flush();

            Model.Name = FirstNonBlank(Model.Header?.SearchName, Search.Name) ?? "Search " + Number.ToString(CultureInfo.InvariantCulture);
            Model.Entries = AllResults.Count;
            Model.Sites = AllResults.Select(p => p.CampsiteID ?? p.CampsiteName ?? string.Empty).Distinct().Count();
            Model.Dates = AllResults.Where(p => p.Date.HasValue).Select(p => p.Date!.Value.Date).Distinct().Count();
            if (AllResults.Count > 0)
            {
                Model.SharedCheckin = AllResults[0].Checkin ?? string.Empty;
                Model.SharedCheckout = AllResults[0].Checkout ?? string.Empty;
                Model.TimesShared = AllResults.All(p => (p.Checkin ?? string.Empty) == Model.SharedCheckin && (p.Checkout ?? string.Empty) == Model.SharedCheckout);
            }
            return Model;
        }

        // ---- rendering ----

        private static void RenderSearch(StringBuilder Html, SearchModel Model, CultureInfo Culture)
        {
            Html.Append("<section class=\"search\" id=\"").Append(Model.Id).Append("\" aria-labelledby=\"").Append(Model.Id).Append("-title\">\n");
            Html.Append("<h2 id=\"").Append(Model.Id).Append("-title\">").Append(E(Model.Name)).Append("</h2>\n");

            OutputMetadata? Header = Model.Header;
            if (Header != null)
            {
                string Campground = JoinParts(Header.CampgroundName, Header.ParkName,
                    string.IsNullOrWhiteSpace(Header.CampgroundID) ? null : "Campground ID " + Header.CampgroundID);
                if (Campground.Length > 0)
                {
                    Html.Append("<p class=\"campground\">").Append(E(Campground)).Append("</p>\n");
                }
                if (Header.ConsecutiveDays > 1)
                {
                    Html.Append("<p class=\"muted\">").Append(E("Dates shown belong to runs of at least " + Header.ConsecutiveDays.ToString(Culture) + " consecutive available dates.")).Append("</p>\n");
                }
            }

            if (Model.Entries > 0)
            {
                Html.Append("<p class=\"summary\">")
                    .Append(E(Count(Model.Entries, "availability entry", "availability entries", Culture) + " for " +
                              Count(Model.Sites, "site", "sites", Culture) + " on " + Count(Model.Dates, "date", "dates", Culture)))
                    .Append("</p>\n");
                if (Model.TimesShared)
                {
                    string Times = TimesText(Model.SharedCheckin, Model.SharedCheckout);
                    if (Times.Length > 0)
                    {
                        Html.Append("<p class=\"muted\">").Append(E(Times + " for every site shown.")).Append("</p>\n");
                    }
                }
            }
            else if (!Model.HasEmptyMonth)
            {
                Html.Append("<p class=\"empty\">").Append(EmptyMessage).Append("</p>\n");
            }

            bool CardsOpen = false;
            foreach (Block ThisBlock in Model.Blocks)
            {
                if (ThisBlock is SiteCard Card)
                {
                    if (!CardsOpen)
                    {
                        Html.Append("<ul class=\"sites consecutive\">\n");
                        CardsOpen = true;
                    }
                    RenderSiteCard(Html, Card, Model, Culture);
                    continue;
                }
                if (CardsOpen)
                {
                    Html.Append("</ul>\n");
                    CardsOpen = false;
                }
                switch (ThisBlock)
                {
                    case MonthBlock Month:
                        Html.Append("<h3>").Append(E(Month.Month.ToString("Y", Culture))).Append("</h3>\n");
                        foreach (DateGroup Group in Month.Groups)
                        {
                            RenderDateGroup(Html, Group, "h4", Model, Culture);
                        }
                        if (Month.Empty && Month.Groups.Count == 0)
                        {
                            Html.Append("<p class=\"empty\">").Append(EmptyMessage).Append("</p>\n");
                        }
                        break;
                    case DateGroup Group:
                        RenderDateGroup(Html, Group, "h3", Model, Culture);
                        break;
                    case NoteBlock Note:
                        Html.Append("<p class=\"note\">").Append(E(Note.Text)).Append("</p>\n");
                        break;
                }
            }
            if (CardsOpen)
            {
                Html.Append("</ul>\n");
            }
            Html.Append("</section>\n");
        }

        private static void RenderDateGroup(StringBuilder Html, DateGroup Group, string Heading, SearchModel Model, CultureInfo Culture)
        {
            int Sites = Group.Hits.Select(p => p.CampsiteID ?? p.CampsiteName ?? string.Empty).Distinct().Count();
            Html.Append('<').Append(Heading).Append(" class=\"date\">").Append(DateElement(Group.Date, Culture))
                .Append(" <span class=\"count\">· ").Append(E(Count(Sites, "site", "sites", Culture))).Append("</span></").Append(Heading).Append(">\n");
            Html.Append("<ul class=\"sites\">\n");
            foreach (OutputMetadata Hit in Group.Hits)
            {
                Html.Append("<li class=\"site\">");
                AppendSiteLines(Html, Hit, Model);
                Html.Append("<br />");
                AppendBookingLink(Html, Hit);
                Html.Append("</li>\n");
            }
            Html.Append("</ul>\n");
        }

        private static void RenderSiteCard(StringBuilder Html, SiteCard Card, SearchModel Model, CultureInfo Culture)
        {
            Html.Append("<li class=\"site\">\n<h3 class=\"site-name\">").Append(E(SiteName(Card.Site))).Append("</h3>\n<p>");
            AppendSiteLines(Html, Card.Site, Model, false);
            Html.Append("</p>\n");
            if (Card.Dates.Count > 0)
            {
                Html.Append("<p class=\"label\">Available dates:</p>\n<ul class=\"dates\">\n");
                // chronological: FilterConsecutiveDays emits a campsite's dates in ConcurrentBag order, which is not stable
                foreach (OutputMetadata Date in Card.Dates.OrderBy(p => p.Date ?? DateTime.MaxValue))
                {
                    Html.Append("<li>").Append(DateElement(Date.Date?.Date, Culture)).Append("</li>\n");
                }
                Html.Append("</ul>\n");
            }
            Html.Append("<p>");
            AppendBookingLink(Html, Card.Site);
            Html.Append("</p>\n</li>\n");
        }

        // name (unless shown as a heading), loop and type, equipment, and check-in/out unless shared by the search
        private static void AppendSiteLines(StringBuilder Html, OutputMetadata Site, SearchModel Model, bool WithName = true)
        {
            bool First = true;
            void Line(string Content)
            {
                if (!First)
                {
                    Html.Append("<br />");
                }
                Html.Append(Content);
                First = false;
            }

            string LoopType = JoinParts(Site.CampsiteLoop, Site.CampsiteType);
            if (WithName)
            {
                Line("<strong class=\"site-name\">" + E(SiteName(Site)) + "</strong>" + (LoopType.Length > 0 ? " <span class=\"muted\">· " + E(LoopType) + "</span>" : string.Empty));
            }
            else if (LoopType.Length > 0)
            {
                Line("<span class=\"muted\">" + E(LoopType) + "</span>");
            }
            List<string> Equipment = (Site.Equipment ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (Equipment.Count > 0)
            {
                Line("<span class=\"equipment\">Equipment: " + E(string.Join(", ", Equipment)) + "</span>");
            }
            if (!Model.TimesShared)
            {
                string Times = TimesText(Site.Checkin, Site.Checkout);
                if (Times.Length > 0)
                {
                    Line("<span class=\"muted\">" + E(Times) + "</span>");
                }
            }
        }

        private static void AppendBookingLink(StringBuilder Html, OutputMetadata Site)
        {
            if (string.IsNullOrWhiteSpace(Site.CampsiteID))
            {
                Html.Append("<span class=\"muted\">No booking link available</span>");
                return;
            }
            string Url = BookingUrlPrefix + Uri.EscapeDataString(Site.CampsiteID);
            Html.Append("<a href=\"").Append(E(Url)).Append("\">Book ").Append(E(SiteName(Site))).Append(" on recreation.gov</a>");
        }

        private static string DateElement(DateTime? Date, CultureInfo Culture)
        {
            if (Date == null)
            {
                return "Date not available";
            }
            return "<time datetime=\"" + Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\">" + E(DateText(Date.Value, Culture)) + "</time>";
        }

        // culture-aware short date with the weekday, for example "Friday, 11/13/2026" (en-US)
        public static string DateText(DateTime Date, CultureInfo Culture)
        {
            return Culture.DateTimeFormat.GetDayName(Date.DayOfWeek) + ", " + Date.ToString("d", Culture);
        }

        // "Site 086" for a bare site name or number; names that already start with "Site" are kept as they are
        private static string SiteName(OutputMetadata Site)
        {
            string Name = FirstNonBlank(Site.CampsiteName, Site.CampsiteID) ?? "Campsite";
            return Name.StartsWith("site", StringComparison.OrdinalIgnoreCase) || Name == "Campsite" ? Name : "Site " + Name;
        }

        private static string TimesText(string? Checkin, string? Checkout)
        {
            List<string> Parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Checkin))
            {
                Parts.Add("Check-in " + Checkin.Trim());
            }
            if (!string.IsNullOrWhiteSpace(Checkout))
            {
                Parts.Add("Check-out " + Checkout.Trim());
            }
            return string.Join(" · ", Parts);
        }

        private static string Count(int Value, string Singular, string Plural, CultureInfo Culture) =>
            Value.ToString("N0", Culture) + " " + (Value == 1 ? Singular : Plural);

        private static string JoinParts(params string?[] Parts) =>
            string.Join(" · ", Parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

        private static string? FirstNonBlank(params string?[] Values) =>
            Values.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim();

        private static string E(string? Value) => WebUtility.HtmlEncode(Value ?? string.Empty);

        // Inline only; no url(), @import or external font. Colors meet WCAG AA contrast in both schemes.
        private const string Css = """

:root{color-scheme:light dark;--bg:#f3f5f3;--surface:#ffffff;--text:#1a1f1c;--muted:#4a5550;--border:#cfd8d2;--accent:#1d5c3a;--link:#0b57d0;--focus:#0b57d0;--empty-bg:#fff7e0;--empty-border:#a86f00}
@media (prefers-color-scheme: dark){:root{--bg:#111513;--surface:#1a201d;--text:#e6ece8;--muted:#b2bdb6;--border:#36423b;--accent:#9fd4b4;--link:#9cc0ff;--focus:#9cc0ff;--empty-bg:#2b2410;--empty-border:#e0ad3a}}
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%;text-size-adjust:100%}
body{margin:0;background:var(--bg);color:var(--text);font-family:system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif;font-size:1rem;line-height:1.5;overflow-wrap:anywhere}
header.page,main,footer.page{max-width:76rem;margin:0 auto;padding-left:1.5rem;padding-right:1.5rem}
header.page{padding-top:1.5rem;padding-bottom:.5rem}
h1{margin:0;font-size:1.75rem;line-height:1.2;color:var(--accent)}
h2{margin:0;font-size:1.375rem;line-height:1.25}
h3{margin:1.25rem 0 .5rem;font-size:1.125rem;line-height:1.3;color:var(--accent)}
h4{margin:1rem 0 .5rem;font-size:1rem}
p{margin:.25rem 0}
.meta,.muted,.count{color:var(--muted)}
.count{font-weight:normal}
nav ul{margin:.75rem 0 0;padding-left:1.25rem}
nav li{margin:.125rem 0}
a{color:var(--link);text-decoration:underline;text-underline-offset:.15em}
a:focus-visible{outline:3px solid var(--focus);outline-offset:2px;border-radius:2px}
section.search{background:var(--surface);border:1px solid var(--border);border-radius:.75rem;padding:1.25rem 1.5rem;margin:1rem 0 1.5rem}
.campground{font-weight:600}
.summary{margin-top:.5rem}
ul.sites{list-style:none;margin:0 0 .5rem;padding:0;display:grid;grid-template-columns:repeat(auto-fill,minmax(min(100%,17rem),1fr));gap:.625rem}
li.site{border:1px solid var(--border);border-radius:.5rem;padding:.625rem .75rem;background:var(--bg);font-size:.9375rem}
li.site h3{margin:0 0 .25rem;color:var(--text)}
.site-name{font-size:1rem}
.equipment{font-size:.875rem}
.label{margin-top:.5rem;font-weight:600}
ul.dates{margin:.125rem 0 .5rem;padding-left:1.25rem}
.empty{border-left:4px solid var(--empty-border);background:var(--empty-bg);padding:.625rem .875rem;border-radius:.25rem;margin:.5rem 0}
.note{white-space:pre-wrap}
footer.page{padding-top:.5rem;padding-bottom:2rem;color:var(--muted);font-size:.875rem}
@media (max-width: 40rem){header.page,main,footer.page{padding-left:1rem;padding-right:1rem}section.search{padding:1rem;border-radius:.5rem}h1{font-size:1.5rem}h2{font-size:1.25rem}}
@media print{:root{color-scheme:light;--bg:#ffffff;--surface:#ffffff;--text:#000000;--muted:#333333;--border:#999999;--accent:#000000;--link:#000000;--empty-bg:#ffffff;--empty-border:#000000}nav{display:none}section.search{border:0;padding:0;margin:0 0 1rem}ul.sites{grid-template-columns:repeat(2,minmax(0,1fr));gap:.375rem}li.site{break-inside:avoid;font-size:.8125rem;padding:.375rem .5rem}h2,h3,h4{break-after:avoid}.equipment{font-size:inherit}a[href^="http"]::after{content:attr(href);display:block;font-size:.75em;overflow-wrap:normal}}

""";
    }
}
