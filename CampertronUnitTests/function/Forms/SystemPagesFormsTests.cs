using System.Globalization;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // What v0.3.0's PR 9 (D99) added to the shared form layer and the library for the web UI's system and campgrounds pages,
    // at the layer's own level: the lookup's words and the campground data card's words written once (CampgroundsWording,
    // RefreshWording, SystemWording, which both UIs show), a campground's details as the shared snapshot (CampgroundDetails,
    // moved whole from the desktop GUI), the current configuration without its folders' paths for a page, and the last
    // refresh's words read now from General.yaml, read-only. Temporary folders only; no database, no network.
    public class SystemPagesFormsTests : ConfigEditTestBase
    {
        [Test]
        public void TheLookupsAndTheCampgroundDataCardsWords_AreWrittenOnce_AsTheDesktopGUIHadThem()
        {
            //the words the desktop GUI's views held before PR 9 moved them here (its views now bind these; a GUI test checks)
            Assert.That((CampgroundsWording.Heading, CampgroundsWording.ByPark, CampgroundsWording.ByCity, CampgroundsWording.ByState),
                Is.EqualTo(("Campgrounds", "By Park", "By City", "By State")));
            Assert.That(new[] { CampgroundsWording.ParkLabel, CampgroundsWording.StateLabel, CampgroundsWording.CityLabel, CampgroundsWording.CampgroundLabel }.Select(SearchesWording.Plain),
                Is.EqualTo(new[] { "Park", "State", "City", "Campground" }));
            Assert.That(CampgroundsWording.CityLabel, Is.EqualTo("C_ity"));
            Assert.That(CampgroundsWording.ByCityHint, Is.EqualTo("Choose a state, a city and then a campground to see the campground ID and details."));
            Assert.That((CampgroundsWording.NoCampgroundSelected, CampgroundsWording.NotListed, CampgroundsWording.NoCityListed), Is.EqualTo(("No campground selected", "Not listed", "(No city listed)")));
            Assert.That(CampgroundsWording.City(""), Is.EqualTo("(No city listed)"));
            Assert.That(CampgroundsWording.City("  "), Is.EqualTo("(No city listed)"));
            Assert.That(CampgroundsWording.City(" SPRINGDALE"), Is.EqualTo(" SPRINGDALE"), "display only: a stored city is never trimmed or changed");
            Assert.That(CampgroundsWording.CampgroundsShown(1201, 1234, 1234), Is.EqualTo("Showing campgrounds 1,201 to 1,234 of 1,234."));
            Assert.That((RefreshWording.Heading, RefreshWording.LastRefreshedLabel, RefreshWording.RefreshLabel, RefreshWording.StartLabel, RefreshWording.BackName),
                Is.EqualTo(("Campground data", "Last refreshed", "Refresh campground data…", "Start refresh", "Back, don't refresh")));
            Assert.That(RefreshWording.ConfirmationHeading, Is.EqualTo("Refresh the campground data?"));
            Assert.That(SystemWording.ConfigurationHeading, Is.EqualTo("Current configuration"));
        }

        [Test]
        public void CampgroundDetails_IsTheSharedSnapshot_WithTheLookupsLabelsAndMissingValues()
        {
            Assert.That(typeof(CampgroundDetails).Assembly, Is.EqualTo(typeof(CampgroundsWording).Assembly), "in the library, for both UIs");
            Assert.That(typeof(CampgroundDetails).Namespace, Is.EqualTo("CampertronLibrary.function.Forms"));
            var Details = new CampgroundDetails(new FacilitiesData
            {
                FacilityID = "0232445", FacilityName = "  ", FacilityPhone = null, FacilityEmail = " ", FacilityDescription = "One\r\n\r\n\r\n\r\nTwo", FacilityDirections = "Left",
                FacilityUseFeeDescription = "$36"
            }, " Yosemite National Park ");
            Assert.That((Details.HasCampground, Details.CampgroundId, Details.Name, Details.Context), Is.EqualTo((true, "0232445", CampgroundChoice.UnnamedName, "Yosemite National Park")));
            Assert.That((Details.Phone, Details.Email, Details.Description), Is.EqualTo((CampgroundsWording.NotListed, CampgroundsWording.NotListed, "One\n\nTwo")));
            Assert.That(CampgroundDetails.None.HasCampground, Is.False);
            Assert.That(CampgroundDetails.CityContext("", "CA"), Is.EqualTo("CA"));
            Assert.That(CampgroundDetails.CityContext("SPRINGDALE", "UT"), Is.EqualTo("SPRINGDALE, UT"));
        }

        [Test]
        public void ThePagesConfiguration_IsTheDesktopGUIs_WithTheFoldersInWords()
        {
            string Cache = Path.Join(Folder, "cache");
            Directory.CreateDirectory(Cache);
            Write(General, "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-10T00:00:00.0000000Z\nautoRefresh: false\n");
            Write(Path.Join(Folder, "Trip.yaml"), "displayName: Trip\n");
            Assembly App = typeof(CurrentConfiguration).Assembly;
            DateTime At = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            ConfigurationSummary Full = CurrentConfiguration.Read(Folder, Cache, At, CultureInfo.InvariantCulture, App);
            ConfigurationSummary Page = CurrentConfiguration.ReadWithoutPaths(Folder, Cache, At, CultureInfo.InvariantCulture, App);
            Assert.That(Page.Items.Select(I => I.Label), Is.EqualTo(CurrentConfiguration.Labels));
            for (int i = 0; i < 8; i++)
            {
                Assert.That(Page.Items[i].Value, Is.EqualTo(i is 1 or 2 ? CurrentConfiguration.FolderNotShown : Full.Items[i].Value), CurrentConfiguration.Labels[i]);
            }
            Assert.That(Page.Text, Does.Not.Contain(Folder).And.Not.Contain(Cache));
            Assert.That(Page.Items[3].Value, Is.EqualTo("Email"));
            Assert.That(Page.Items[4].Value, Is.EqualTo("1 search file"));
            Assert.That(CurrentConfiguration.ReadWithoutPaths(Folder, null, At, CultureInfo.InvariantCulture, App).Items[2].Value, Is.EqualTo(CurrentConfiguration.Unknown));
        }

        [Test]
        public void TheLastRefresh_IsReadFromGeneralYaml_ReadOnly_AndUnknownWithoutIt()
        {
            DateTime At = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(CurrentConfiguration.LastRefreshed(Folder, At, CultureInfo.InvariantCulture), Is.EqualTo(CurrentConfiguration.Unknown));
            Assert.That(File.Exists(General), Is.False, "nothing is created");
            string Text = "# mine\noutputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00.0000000Z\nautoRefresh: false\n";
            Write(General, Text);
            DateTime Written = File.GetLastWriteTimeUtc(General);
            Assert.That(CurrentConfiguration.LastRefreshed(Folder, At, CultureInfo.InvariantCulture),
                Is.EqualTo(CurrentConfiguration.RefreshText(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), At, CultureInfo.InvariantCulture)));
            Assert.That(File.ReadAllText(General), Is.EqualTo(Text));
            Assert.That(File.GetLastWriteTimeUtc(General), Is.EqualTo(Written));
        }

        [Test]
        public void TheRefreshRequest_IsOutsideTheContainerUnlessSaid()
        {
            Assert.That(new RidbRefreshRequest("/config", "/cache").InContainer, Is.False);
            Assert.That(new RidbRefreshRequest("/config", "/cache", true), Is.EqualTo(new RidbRefreshRequest("/config", "/cache", InContainer: true)));
        }
    }
}
