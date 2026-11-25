using System.Xml.Linq;

namespace CampertronWebTests
{
    // What every live page's markup holds (D108, D109; WebLive), checked alike for the results, system and monitoring pages
    // by their accessibility tests and by LivePagesTests. While its operation goes on: the web UI's one script in the head,
    // named by its address, with nothing in it; one polite, atomic, empty announcement region outside the part the script
    // replaces (#live-view); #live inside that part, naming the operation with the page's words and the manual way's text;
    // the status line with no live role of its own (an ordinary update is never announced); the manual way, "Show the
    // latest …", stays. Otherwise none of the script's parts.
    internal static class LiveMarkup
    {
        internal static string? Attr(XElement E, string Name) => (string?)E.Attribute(Name);

        internal static bool IsLiveScript(XElement E) => E.Name.LocalName == "script" && Attr(E, "src") == WebAssets.LiveScriptPath;

        // whether the page shows an operation that goes on, as its markup says (its #live)
        internal static bool IsActive(WebPage Page) => Page.Xml().Descendants().Any(E => Attr(E, "id") == "live");

        internal static void Check(string What, WebPage Page, bool Active, string ShowLatest)
        {
            XDocument Html = Page.Xml();
            List<XElement> Elements = Html.Descendants().ToList();
            List<XElement> Scripts = Elements.Where(E => E.Name.LocalName == "script").ToList();
            XElement? Live = Elements.SingleOrDefault(E => Attr(E, "id") == "live");
            XElement? Announce = Elements.SingleOrDefault(E => Attr(E, "id") == "live-announce");
            XElement? View = Elements.SingleOrDefault(E => Attr(E, "id") == "live-view");
            Assert.That(View, Is.Not.Null, What + ": the part the script may replace");
            if (!Active)
            {
                Assert.That(Scripts, Is.Empty, What + ": no script without an operation that goes on");
                Assert.That((Live, Announce), Is.EqualTo(((XElement?)null, (XElement?)null)), What);
                return;
            }
            Assert.That(Scripts.Select(S => (Attr(S, "src"), Attr(S, "defer"), S.Value, S.Parent!.Name.LocalName)),
                Is.EqualTo(new[] { ((string?)WebAssets.LiveScriptPath, (string?)"defer", "", "head") }), What + ": the web UI's one script, by its address");
            Assert.That(Announce, Is.Not.Null, What);
            Assert.That((Attr(Announce!, "role"), Attr(Announce!, "aria-live"), Attr(Announce!, "aria-atomic"), Announce!.Value, Attr(Announce!, "class")),
                Is.EqualTo(((string?)"status", (string?)"polite", (string?)"true", "", (string?)"visually-hidden")), What + ": one polite region, empty until something changes");
            Assert.That(Announce!.Ancestors().Any(A => Attr(A, "id") == "live-view"), Is.False, What + ": outside the part the script replaces");
            Assert.That(Live, Is.Not.Null, What);
            Assert.That(Live!.Ancestors().Any(A => Attr(A, "id") == "live-view"), Is.True, What + ": #live is the page's own moment, inside the part");
            Assert.That(Attr(Live!, "data-key"), Is.Not.Null.And.Not.Empty, What);
            Assert.That(Attr(Live!, "data-state"), Does.Match("^[a-z]{1,32}$"), What);
            Assert.That(Attr(Live!, "data-rank"), Does.Match("^[0-9]+$"), What);
            Assert.That(Attr(Live!, "data-seq"), Does.Match("^[0-9]+$"), What);
            Assert.That(Attr(Live!, "data-view"), Does.Match("^[0-9a-f]{16}$"), What);
            Assert.That(Attr(Live!, "class"), Is.EqualTo("hint"), What);
            Assert.That(Live!.Value, Does.StartWith("Select " + ShowLatest + " to see how the "), What + ": until the script runs, the manual way");
            foreach (string Warning in new[] { "data-failing", "data-ended-failing", "data-signed-out", "data-out-of-date" })
            {
                Assert.That(Attr(Live!, Warning), Does.StartWith("Warning: "), What + ": " + Warning + " says its severity in words");
            }
            Assert.That(Attr(Live!, "data-updating"), Does.StartWith("This page updates by itself while ").And.Contain("{time}"), What);
            XElement? Status = Elements.SingleOrDefault(E => Attr(E, "id") == "status");
            Assert.That(Status, Is.Not.Null, What);
            Assert.That((Attr(Status!, "role"), Attr(Status!, "aria-live"), Attr(Status!, "tabindex")), Is.EqualTo(((string?)null, (string?)null, (string?)"-1")),
                What + ": the status line has no live role while the script updates it");
            Assert.That(Elements.Count(E => E.Name.LocalName == "a" && Attr(E, "id") == "show-latest" && E.Value == ShowLatest), Is.EqualTo(1), What + ": the manual way stays");
            Assert.That(Elements.Where(E => Attr(E, "aria-live") != null || Attr(E, "role") is "status" or "alert" or "log")
                .Where(E => E != Announce && !E.Ancestors().Any(A => Attr(A, "id") == "live-view")), Is.Empty, What + ": no other live region outside the part");
        }
    }
}
