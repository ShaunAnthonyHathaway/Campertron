using System.Globalization;
using static ConsoleConfig;
using static CampertronLibrary.function.Base.ConsoleUi;

namespace CampertronLibrary.function.Base
{
    // The console's view of the search results and of the searches that run. Like the HTML results page, it is
    // rendered only from the tagged output streams (ConsoleConfigValue.Kind/Metadata) through the model ResultsHtml
    // builds, so the console and the page group the same results the same way: single nights by date, consecutive
    // nights by campsite (dates in date order). It never parses the values' text. Untyped nonblank text is kept as a
    // plain line; the stream's own calendar values (OutputKind.Calendar) are skipped and the calendar is drawn here.
    // Everything is plain lines of ASCII punctuation around the data: no tabs, no emoji, no cursor movement. Color is
    // carried as spans and decided by the writer (ConsoleUi); the open days of a calendar are also marked with "*".
    // Width only wraps lists (never a link); int.MaxValue wraps nothing.
    // Like the page, it shows each stream as supplied: it does not correct a stream that TD-04 has already altered.
    public static class ConsoleResults
    {
        public const string EmptyMessage = "No matching availability.";
        public const ConsoleColor TitleColor = ConsoleColor.Green;
        public const ConsoleColor DateColor = ConsoleColor.Cyan;
        private const int MaxNameColumn = 16;

        public static List<StyledLine> Render(IEnumerable<ConsoleConfigItem> Searches, CultureInfo Culture, int Width, bool WithSummary)
        {
            List<StyledLine> Lines = new List<StyledLine>();
            List<ResultsHtml.SearchModel> Models = Build(Searches);
            foreach (ResultsHtml.SearchModel Model in Models)
            {
                Blank(Lines);
                RenderSearch(Lines, Model, Culture, Width);
            }
            if (WithSummary)
            {
                Blank(Lines);
                Lines.Add(new StyledLine(SummaryText(Models, Culture)));
            }
            return Lines;
        }

        // "Done: 2 searches, 10 openings at 4 sites. 1 search without availability."
        public static string SummaryText(IEnumerable<ConsoleConfigItem> Searches, CultureInfo Culture)
        {
            return SummaryText(Build(Searches), Culture);
        }
        // the name the results show for a search and the number of openings (a campsite on a date) in its stream
        public static string SearchName(ConsoleConfigItem Search)
        {
            return ResultsHtml.Build(Search, 1).Name;
        }
        public static int Openings(ConsoleConfigItem Search)
        {
            return ResultsHtml.Build(Search, 1).Entries;
        }

        private static List<ResultsHtml.SearchModel> Build(IEnumerable<ConsoleConfigItem> Searches)
        {
            List<ResultsHtml.SearchModel> Models = new List<ResultsHtml.SearchModel>();
            foreach (ConsoleConfigItem Search in Searches)
            {
                Models.Add(ResultsHtml.Build(Search, Models.Count + 1));
            }
            return Models;
        }
        private static string SummaryText(List<ResultsHtml.SearchModel> Models, CultureInfo Culture)
        {
            int Entries = Models.Sum(p => p.Entries);
            int Sites = Models.Sum(p => p.Sites);
            int Without = Models.Count(p => p.Entries == 0);
            string Text = "Done: " + Count(Models.Count, "search", "searches", Culture) + ", ";
            if (Entries == 0)
            {
                return Text + "no matching availability.";
            }
            Text += Count(Entries, "opening", "openings", Culture) + " at " + Count(Sites, "site", "sites", Culture) + ".";
            if (Without > 0)
            {
                Text += " " + Count(Without, "search", "searches", Culture) + " without availability.";
            }
            return Text;
        }

        private static void RenderSearch(List<StyledLine> Lines, ResultsHtml.SearchModel Model, CultureInfo Culture, int Width)
        {
            Lines.Add(new StyledLine("== " + Model.Name + " ==", TitleColor));
            OutputMetadata? Header = Model.Header;
            if (Header != null)
            {
                string Campground = CampgroundText(Header.CampgroundName, Header.ParkName, Header.CampgroundID);
                if (Campground.Length > 0)
                {
                    AddText(Lines, Campground, Width, "  ");
                }
            }

            List<OutputMetadata> Results = AllResults(Model);
            bool EquipmentShared = false;
            if (Model.Entries > 0)
            {
                List<string> Facts = new List<string>();
                if (Header != null && Header.ConsecutiveDays > 1)
                {
                    Facts.Add("at least " + Header.ConsecutiveDays.ToString(Culture) + " consecutive nights");
                }
                Facts.Add(Count(Model.Entries, "opening", "openings", Culture) + " at " + Count(Model.Sites, "site", "sites", Culture) +
                          " on " + Count(Model.Dates, "date", "dates", Culture));
                if (Model.TimesShared)
                {
                    string Times = TimesText(Model.SharedCheckin, Model.SharedCheckout);
                    if (Times.Length > 0)
                    {
                        Facts.Add(Times);
                    }
                }
                AddWrapped(Lines, string.Empty, Facts, " | ", Width, "  ");
                List<string> FirstEquipment = EquipmentOf(Results[0]);
                EquipmentShared = FirstEquipment.Count > 0 && Results.All(p => EquipmentOf(p).SequenceEqual(FirstEquipment));
                if (EquipmentShared)
                {
                    AddWrapped(Lines, "Equipment (all sites): ", FirstEquipment, ", ", Width, "  ");
                }
            }
            else
            {
                Lines.Add(new StyledLine(EmptyMessage));
            }

            int NameColumn = Math.Min(MaxNameColumn, Results.Select(p => SiteName(p).Length).DefaultIfEmpty(0).Max());
            foreach (ResultsHtml.Block ThisBlock in Model.Blocks)
            {
                switch (ThisBlock)
                {
                    case ResultsHtml.MonthBlock Month:
                        if (Month.Groups.Count > 0)
                        {
                            Blank(Lines);
                            foreach (ResultsHtml.DateGroup Group in Month.Groups)
                            {
                                RenderDateGroup(Lines, Group, Model, EquipmentShared, NameColumn, Culture, Width);
                            }
                            Blank(Lines);
                            Lines.AddRange(Calendar.Lines(Month.Month.Year, Month.Month.Month,
                                Month.Groups.Where(p => p.Date.HasValue).Select(p => p.Date!.Value), Culture, "  ", DateColor));
                        }
                        else if (Month.Empty && Model.Entries > 0)
                        {
                            //a search without any result says so once (above), not once per month
                            Blank(Lines);
                            Lines.Add(new StyledLine(Month.Month.ToString("Y", Culture) + ": no matching availability."));
                        }
                        break;
                    case ResultsHtml.DateGroup Group:
                        Blank(Lines);
                        RenderDateGroup(Lines, Group, Model, EquipmentShared, NameColumn, Culture, Width);
                        break;
                    case ResultsHtml.SiteCard Card:
                        Blank(Lines);
                        RenderSiteCard(Lines, Card, Model, EquipmentShared, NameColumn, Culture, Width);
                        break;
                    case ResultsHtml.NoteBlock Note:
                        foreach (string NoteLine in Note.Text.Replace("\r\n", "\n").Split('\n'))
                        {
                            Lines.Add(new StyledLine(NoteLine));
                        }
                        break;
                }
            }
        }

        // "Fri 11/20/2026 - 2 sites", then every site of that date
        private static void RenderDateGroup(List<StyledLine> Lines, ResultsHtml.DateGroup Group, ResultsHtml.SearchModel Model, bool EquipmentShared, int NameColumn, CultureInfo Culture, int Width)
        {
            int Sites = Group.Hits.Select(p => p.CampsiteID ?? p.CampsiteName ?? string.Empty).Distinct().Count();
            Lines.Add(new StyledLine(DateText(Group.Date, Culture), DateColor).Add(" - " + Count(Sites, "site", "sites", Culture)));
            foreach (OutputMetadata Hit in Group.Hits)
            {
                AddSiteLines(Lines, Hit, Model, EquipmentShared, NameColumn, Width);
            }
        }

        // a campsite of a consecutive-nights search: the site, its dates in date order, and a calendar per month
        private static void RenderSiteCard(List<StyledLine> Lines, ResultsHtml.SiteCard Card, ResultsHtml.SearchModel Model, bool EquipmentShared, int NameColumn, CultureInfo Culture, int Width)
        {
            string Indent = AddSiteLines(Lines, Card.Site, Model, EquipmentShared, NameColumn, Width);
            // chronological: FilterConsecutiveDays emits a campsite's dates in ConcurrentBag order, which is not stable
            List<DateTime?> Dates = Card.Dates.OrderBy(p => p.Date ?? DateTime.MaxValue).Select(p => p.Date?.Date).ToList();
            if (Dates.Count > 0)
            {
                AddWrapped(Lines, Indent, Dates.Select(p => DateText(p, Culture)), ", ", Width, Indent);
                foreach (DateTime MonthStart in Dates.Where(p => p.HasValue).Select(p => new DateTime(p!.Value.Year, p.Value.Month, 1)).Distinct().OrderBy(p => p))
                {
                    Blank(Lines);
                    Lines.AddRange(Calendar.Lines(MonthStart.Year, MonthStart.Month, Dates.Where(p => p.HasValue).Select(p => p!.Value), Culture, "  ", DateColor));
                }
            }
        }

        // the site's name with its loop and type, its booking link on a line of its own (never wrapped), and its
        // equipment and check-in/out unless the whole search shares them. Returns the indent of the continuation lines.
        private static string AddSiteLines(List<StyledLine> Lines, OutputMetadata Site, ResultsHtml.SearchModel Model, bool EquipmentShared, int NameColumn, int Width)
        {
            string Name = SiteName(Site);
            string Indent = new string(' ', 2 + Math.Max(NameColumn, Math.Min(Name.Length, MaxNameColumn)) + 2);
            string LoopType = Join(", ", Site.CampsiteLoop, Site.CampsiteType);
            //names share a column up to MaxNameColumn; a longer name is simply followed by two spaces
            string Head = "  " + Name;
            AddText(Lines, LoopType.Length > 0 ? (Head.Length + 2 <= Indent.Length ? Head.PadRight(Indent.Length) : Head + "  ") + LoopType : Head, Width, Indent);
            Lines.Add(new StyledLine(Indent + (string.IsNullOrWhiteSpace(Site.CampsiteID)
                ? "(no booking link available)"
                : ResultsHtml.BookingUrlPrefix + Uri.EscapeDataString(Site.CampsiteID))));
            if (!EquipmentShared)
            {
                List<string> Equipment = EquipmentOf(Site);
                if (Equipment.Count > 0)
                {
                    AddWrapped(Lines, Indent + "Equipment: ", Equipment, ", ", Width, Indent + "  ");
                }
            }
            if (!Model.TimesShared)
            {
                string Times = TimesText(Site.Checkin, Site.Checkout);
                if (Times.Length > 0)
                {
                    Lines.Add(new StyledLine(Indent + Times));
                }
            }
            return Indent;
        }

        // ---- the searches that are about to run ----

        public sealed class Scope
        {
            public List<StyledLine> Lines { get; } = new List<StyledLine>();
            public List<string> Warnings { get; } = new List<string>();
        }
        // What is searched: each search (by name) with its campground, its dates, weekdays, nights, people and number
        // of filters, and a warning for every search that cannot find anything as it is written. CampInfo returns the
        // campground of an ID from the campground list (null or empty names if it is not there).
        public static Scope DescribeSearches(IEnumerable<CampertronConfig> Searches, Func<string, ReturnParkCampground?> CampInfo, CultureInfo Culture, int Width)
        {
            Scope Result = new Scope();
            List<CampertronConfig> Ordered = Searches.OrderBy(p => p.DisplayName).ToList();
            Result.Lines.Add(new StyledLine("Running " + Count(Ordered.Count, "search", "searches", Culture) + ":"));
            foreach (CampertronConfig Search in Ordered)
            {
                string Name = string.IsNullOrWhiteSpace(Search.DisplayName) ? "(no displayName)" : Search.DisplayName.Trim();
                string Quoted = "search \"" + Name + "\"";
                Result.Lines.Add(new StyledLine());
                Result.Lines.Add(new StyledLine("  " + Name, TitleColor));
                if (string.IsNullOrWhiteSpace(Search.CampgroundID))
                {
                    Result.Lines.Add(new StyledLine("    (no campground)"));
                    Result.Warnings.Add(Quoted + " has no campgroundID, so it finds nothing. Look up the ID in CampertronGUI.");
                }
                else
                {
                    ReturnParkCampground? Info = CampInfo(Search.CampgroundID);
                    if (Info == null || (string.IsNullOrWhiteSpace(Info.CampsiteName) && string.IsNullOrWhiteSpace(Info.ParkName)))
                    {
                        Result.Lines.Add(new StyledLine("    Campground ID " + Search.CampgroundID + " (not in the campground list)"));
                        Result.Warnings.Add(Quoted + ": campground ID " + Search.CampgroundID + " is not in the campground list. Check the ID in CampertronGUI, or refresh the campground data.");
                    }
                    else
                    {
                        AddText(Result.Lines, "    " + CampgroundText(Info.CampsiteName, Info.ParkName, Search.CampgroundID), Width, "      ");
                    }
                }

                List<string> Facts = new List<string>();
                List<DateTime> Dates = Search.GetSearchDates().Select(p => p.Date).Distinct().OrderBy(p => p).ToList();
                if (Dates.Count == 0)
                {
                    Facts.Add("no dates");
                    Result.Warnings.Add(Quoted + " has no search dates, so it finds nothing. Check searchBy, searchValue and searchValueDates.");
                }
                else if (Dates.Count == 1)
                {
                    Facts.Add(Dates[0].ToString("d", Culture));
                }
                else if (Dates.Count == (Dates[Dates.Count - 1] - Dates[0]).Days + 1)
                {
                    Facts.Add(Dates[0].ToString("d", Culture) + " to " + Dates[Dates.Count - 1].ToString("d", Culture));
                }
                else
                {
                    Facts.Add(Count(Dates.Count, "date", "dates", Culture) + " from " + Dates[0].ToString("d", Culture) + " to " + Dates[Dates.Count - 1].ToString("d", Culture));
                }
                Facts.Add(WeekdaysText(Search, Culture));
                if (Search.ConsecutiveDays > 1)
                {
                    Facts.Add("at least " + Search.ConsecutiveDays.ToString(Culture) + " consecutive nights");
                }
                else if (Search.ConsecutiveDays < 1)
                {
                    Result.Warnings.Add(Quoted + ": consecutiveDays is " + Search.ConsecutiveDays.ToString(Culture) + ", so no result is shown. Use 1 for single nights.");
                }
                if (Search.TotalHumans == null)
                {
                    Result.Warnings.Add(Quoted + " has no totalHumans, so no campsite can match.");
                }
                else
                {
                    Facts.Add(Count(Search.TotalHumans.Value, "person", "people", Culture));
                }
                int Filters = new[] { Search.IncludeEquipment, Search.ExcludeEquipment, Search.IncludeSites, Search.ExcludeSites,
                                      Search.IncludeAttributes, Search.ExcludeAttributes, Search.IncludeCampsiteType, Search.ExcludeCampsiteType }
                              .Count(p => p != null && p.Count > 0);
                if (Filters > 0)
                {
                    Facts.Add(Count(Filters, "filter", "filters", Culture));
                }
                AddWrapped(Result.Lines, "    ", Facts, " | ", Width, "      ");
            }
            return Result;
        }
        // "November 2026, December 2026": the months a search asks recreation.gov for (the months from now through
        // GetMonthsToCheck that contain one of its dates, as GetAvailabilitiesByCampground walks them)
        public static string MonthsText(CampertronConfig Search, DateTime Now, CultureInfo Culture)
        {
            List<string> Months = new List<string>();
            List<DateTime> Dates = Search.GetSearchDates();
            for (int i = 0; i <= (Search.GetMonthsToCheck() ?? 0); i++)
            {
                DateTime Month = Now.AddMonths(i);
                if (Dates.Any(p => p.Year == Month.Year && p.Month == Month.Month))
                {
                    Months.Add(new DateTime(Month.Year, Month.Month, 1).ToString("Y", Culture));
                }
            }
            return string.Join(", ", Months);
        }
        private static string WeekdaysText(CampertronConfig Search, CultureInfo Culture)
        {
            List<DayOfWeek> Days = new List<DayOfWeek>();
            foreach (DayOfWeek Day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
            {
                if (Search.ShowThisDay(Day.ToString()))
                {
                    Days.Add(Day);
                }
            }
            if (Days.Count == 7)
            {
                return "every day";
            }
            return Days.Count == 0 ? "no weekday" : string.Join(", ", Days.Select(p => Culture.DateTimeFormat.GetAbbreviatedDayName(p)));
        }

        // ---- text ----

        private static List<OutputMetadata> AllResults(ResultsHtml.SearchModel Model)
        {
            List<OutputMetadata> Results = new List<OutputMetadata>();
            foreach (ResultsHtml.Block ThisBlock in Model.Blocks)
            {
                switch (ThisBlock)
                {
                    case ResultsHtml.MonthBlock Month:
                        Results.AddRange(Month.Groups.SelectMany(p => p.Hits));
                        break;
                    case ResultsHtml.DateGroup Group:
                        Results.AddRange(Group.Hits);
                        break;
                    case ResultsHtml.SiteCard Card:
                        Results.AddRange(Card.Dates.Count > 0 ? Card.Dates : new List<OutputMetadata> { Card.Site });
                        break;
                }
            }
            return Results;
        }
        private static void Blank(List<StyledLine> Lines)
        {
            if (Lines.Count > 0 && Lines[Lines.Count - 1].Text.Length > 0)
            {
                Lines.Add(new StyledLine());
            }
        }
        // a line of names: on a terminal it is wrapped at spaces (continuation lines indented), never inside a word
        private static void AddText(List<StyledLine> Lines, string Text, int Width, string HangIndent)
        {
            // a wrapped line is single-spaced: the padding between a name and what follows it is not kept
            Lines.AddRange(Width == int.MaxValue || Text.Length <= Width ? new[] { new StyledLine(Text) } : Wrap(Text, Width, HangIndent).Select(p => new StyledLine(p)));
        }
        // Prefix followed by the items, joined by Separator; a new line starts (with HangIndent) before an item that
        // would pass Width. An item is never broken.
        private static void AddWrapped(List<StyledLine> Lines, string Prefix, IEnumerable<string> Items, string Separator, int Width, string HangIndent)
        {
            string Current = Prefix;
            bool HasItem = false;
            foreach (string Item in Items)
            {
                string Addition = (HasItem ? Separator : string.Empty) + Item;
                if (!HasItem && Width != int.MaxValue && Prefix.Trim().Length > 0 && Prefix.Length + Item.Length + 1 > Width && HangIndent.Length + Item.Length < Prefix.Length + Item.Length)
                {
                    //the first item does not fit after its label: it starts the next line
                    Lines.Add(new StyledLine(Prefix.TrimEnd()));
                    Current = HangIndent + Item;
                }
                else if (HasItem && Width != int.MaxValue && Current.Length + (Separator.Trim() == "," ? Addition + "," : Addition).Length > Width)
                {
                    //a list keeps its comma at the end of the line; other separators only separate within a line
                    Lines.Add(new StyledLine(Separator.Trim() == "," ? (Current + Separator).TrimEnd() : Current));
                    Current = HangIndent + Item;
                }
                else
                {
                    Current += Addition;
                }
                HasItem = true;
            }
            if (HasItem)
            {
                Lines.Add(new StyledLine(Current));
            }
        }
        // "Fri 11/20/2026": the abbreviated weekday and the culture's short date
        public static string DateText(DateTime? Date, CultureInfo Culture)
        {
            return Date == null ? "Date not available" : Culture.DateTimeFormat.GetAbbreviatedDayName(Date.Value.DayOfWeek) + " " + Date.Value.ToString("d", Culture);
        }
        // "WATCHMAN CAMPGROUND, Zion National Park (ID 232445)"
        public static string CampgroundText(string? Campground, string? Park, string? Id)
        {
            string Names = Join(", ", Campground, Park);
            if (string.IsNullOrWhiteSpace(Id))
            {
                return Names;
            }
            return Names.Length > 0 ? Names + " (ID " + Id.Trim() + ")" : "Campground ID " + Id.Trim();
        }
        private static string SiteName(OutputMetadata Site)
        {
            return FirstNonBlank(Site.CampsiteName, Site.CampsiteID) ?? "Campsite";
        }
        private static List<string> EquipmentOf(OutputMetadata Site)
        {
            return (Site.Equipment ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        }
        private static string TimesText(string? Checkin, string? Checkout)
        {
            return Join(", ", string.IsNullOrWhiteSpace(Checkin) ? null : "check-in " + Checkin.Trim(),
                              string.IsNullOrWhiteSpace(Checkout) ? null : "check-out " + Checkout.Trim());
        }
        private static string Count(int Value, string Singular, string Plural, CultureInfo Culture)
        {
            return Value.ToString("N0", Culture) + " " + (Value == 1 ? Singular : Plural);
        }
        private static string Join(string Separator, params string?[] Parts)
        {
            return string.Join(Separator, Parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        }
        private static string? FirstNonBlank(params string?[] Values)
        {
            return Values.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))?.Trim();
        }
    }
}
