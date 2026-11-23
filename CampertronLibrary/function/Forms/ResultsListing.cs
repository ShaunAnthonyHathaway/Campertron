namespace CampertronLibrary.function.Forms
{
    // One page of a search's result rows, for a UI that lists them itself instead of in a virtualized list (the web UI's
    // Results page, v0.3.0's PR 7, D97). The rows are ResultSearchViewModel's, in its order (single nights by night, each
    // night's campsites by campsite ID; consecutive nights by campsite, each with its stays), so both UIs list the same rows:
    //  - at most PageSize openings or stays on a page; a page that starts inside a night or a campsite shows its heading
    //    again, and RangeText says which rows the page shows whenever they don't all fit: never a silent cut;
    //  - each campsite's booking link only as BookingLink accepts it (https, www.recreation.gov, the campsite's page, nothing
    //    else); a link it refuses is not shown, and the row says so in fixed words;
    //  - nothing here reads a file, a database or the network: it reads the immutable result it is given.
    // PageSize: an opening's row is about half a kilobyte of a page, so 200 keep a page near 100 KB however many openings a
    // run found (the desktop GUI's list showed 12,030 rows in its tests), and a whole campground's night (Upper Pines has
    // 240 campsites) seldom needs a second page.
    public sealed class ResultsListing
    {
        internal const int PageSize = 200;

        private ResultsListing(IReadOnlyList<ResultsListingGroup> Groups, int Page, int Pages, int First, int Last, int Total, bool Stays)
        {
            this.Groups = Groups;
            this.Page = Page;
            this.Pages = Pages;
            this.First = First;
            this.Last = Last;
            this.Total = Total;
            RangeText = Pages > 1 ? ResultsWording.RowsShown(First, Last, Total, Stays) : null;
        }

        public IReadOnlyList<ResultsListingGroup> Groups { get; }
        // the page shown (from 1) and how many there are (at least 1)
        public int Page { get; }
        public int Pages { get; }
        // the openings or stays on this page (from 1) and in all
        public int First { get; }
        public int Last { get; }
        public int Total { get; }
        // which rows this page shows, when they don't all fit on one page; else null
        public string? RangeText { get; }
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < Pages;

        // the page asked for (from 1; outside 1 to Pages the nearest), of Size rows
        internal static ResultsListing Of(ResultSearchViewModel Search, int RequestedPage, int Size = PageSize)
        {
            if (Size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Size));
            }
            // the rows as units: an opening or a stay with the heading it is under (a heading without rows is a unit of its own)
            var Units = new List<(ResultRow Heading, ResultRow? Row)>();
            bool Stays = false;
            ResultRow? Heading = null;
            bool HeadingHasRows = false;
            foreach (ResultRow Row in Search.Rows)
            {
                if (Row is ResultDateRow || Row is ResultSiteRow)
                {
                    if (Heading != null && !HeadingHasRows)
                    {
                        Units.Add((Heading, null));
                    }
                    Heading = Row;
                    HeadingHasRows = false;
                    Stays |= Row is ResultSiteRow;
                }
                else if (Heading != null)
                {
                    Units.Add((Heading, Row));
                    HeadingHasRows = true;
                }
            }
            if (Heading != null && !HeadingHasRows)
            {
                Units.Add((Heading, null));
            }
            int Total = Units.Count;
            int Pages = Math.Max(1, (Total + Size - 1) / Size);
            int Page = Math.Clamp(RequestedPage, 1, Pages);
            int Skip = (Page - 1) * Size;
            var Groups = new List<ResultsListingGroup>();
            ResultsListingGroup? Open = null;
            foreach ((ResultRow UnitHeading, ResultRow? Row) in Units.Skip(Skip).Take(Size))
            {
                if (Open == null || !ReferenceEquals(Open.Heading, UnitHeading))
                {
                    Open = new ResultsListingGroup(UnitHeading);
                    Groups.Add(Open);
                }
                if (Row != null)
                {
                    Open.Add(Row);
                }
            }
            return new ResultsListing(Groups.AsReadOnly(), Page, Pages, Total == 0 ? 0 : Skip + 1, Math.Min(Total, Skip + Size), Total, Stays);
        }
    }

    // A night (single nights) or a campsite (consecutive nights) and its rows on one page. A campsite's booking link is the
    // group's; a night's campsites have one each.
    public sealed class ResultsListingGroup
    {
        private readonly List<ResultsListingItem> _Items = new List<ResultsListingItem>();

        internal ResultsListingGroup(ResultRow Heading)
        {
            this.Heading = Heading;
            Site = Heading as ResultSiteRow;
            HeadingText = Heading switch
            {
                ResultDateRow Date => Date.Text,
                ResultSiteRow Site_ => Site_.Title,
                _ => Heading.AccessibleName,
            };
            Link = Site != null ? ResultsLink.Of(Site) : null;
        }

        internal ResultRow Heading { get; }
        public string HeadingText { get; }
        // the campsite of consecutive nights (its ID, its stays and nights, its details), or null for a night
        public ResultSiteRow? Site { get; }
        public ResultsLink? Link { get; }
        public IReadOnlyList<ResultsListingItem> Items => _Items;

        internal void Add(ResultRow Row) => _Items.Add(new ResultsListingItem(Row));
    }

    // An opening (a campsite on a night, with its booking link) or a stay of the campsite above it.
    public sealed class ResultsListingItem
    {
        internal ResultsListingItem(ResultRow Row)
        {
            Opening = Row as ResultOpeningRow;
            Stay = Row as ResultStayRow;
            Link = Opening != null ? ResultsLink.Of(Opening) : null;
        }

        public ResultOpeningRow? Opening { get; }
        public ResultStayRow? Stay { get; }
        public ResultsLink? Link { get; }
    }

    // A campsite's booking link as a page shows it: its address only when BookingLink accepts it (the exact page GS1 builds
    // for that campsite), with words that say where it goes; otherwise no address and the fixed words that say why.
    public sealed class ResultsLink
    {
        private ResultsLink(string? Href, string Text)
        {
            this.Href = Href;
            this.Text = Text;
        }

        public string? Href { get; }
        public string Text { get; }
        public bool IsShown => Href != null;

        internal static ResultsLink Of(ResultRow Row) =>
            BookingLink.TryCreate(Row.BookingUrl, Row.CampsiteId, out Uri? Link) && Row.CampsiteTitle is string Title
                ? new ResultsLink(Link.AbsoluteUri, ResultsWording.BookingLinkText(Title))
                : new ResultsLink(null, ResultsWording.NoBookingLink);

        public override string ToString() => Href ?? Text;
    }
}
