using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronGUITests
{
    // Runs once for the whole assembly, before any GUI test.
    //
    // The GUI reads LocalApplicationData/CampertronConfig directly: MainWindow's constructor calls Load.GetConfig() and
    // ReactiveViewModel queries RecreationDotOrg.db there. This fixture points LocalApplicationData at a new temporary
    // directory (XDG_DATA_HOME, in this test process only) and creates a tiny, deterministic database there from the
    // EF model, as AddressStateTests does. Because the database already exists, has the corrected address layout and
    // none of the omitted RIDB tables, and General.yaml sets autoRefresh: false, Load.GetConfig() neither extracts the
    // 226 MB embedded database, nor repairs it, nor refreshes it. The tests therefore never touch the user's real
    // data, the network or the embedded database, and they do not depend on current RIDB contents.
    //
    // Only Linux honours XDG_DATA_HOME for LocalApplicationData, so on any other OS the GUI tests are ignored rather
    // than risk the user's real data folder.
    [SetUpFixture]
    public class GuiTestEnvironment
    {
        public const string StateUtah = "UT";
        public const string StateColorado = "CO";
        public const string CityAlpha = "ALPHAVILLE";
        public const string CityBravo = "BRAVOTOWN";
        public const string ParkOne = "Fixture Recreation Area One";
        public const string ParkTwo = "Fixture Recreation Area Two";
        public const string CampgroundAlphaID = "990001";
        public const string CampgroundAlpha = "FIXTURE CAMPGROUND ALPHA";
        public const string CampgroundBravoID = "990002";
        public const string CampgroundBravo = "FIXTURE CAMPGROUND BRAVO";
        // beta.8 layout fixtures: a long park name, a campground with an empty city and no contact, directions or fee
        // data, and a campground with a long (trailing-space padded) name, a sentence-long phone value and a long
        // description.
        public const string ParkThree = "Fixture Recreation Area Three With An Exceptionally Long Name For Layout And Trimming Tests";
        public const string EmptyCity = "";
        public const string CampgroundCharlieID = "990003";
        public const string CampgroundCharlie = "FIXTURE CAMPGROUND CHARLIE";
        public const string CampgroundDeltaID = "990004";
        public const string CampgroundDeltaTrimmed = "FIXTURE CAMPGROUND DELTA WITH A VERY LONG NAME THAT KEEPS GOING FOR LAYOUT TESTS (Bravotown, CO)";
        public const string CampgroundDelta = CampgroundDeltaTrimmed + "      ";
        public const string DeltaPhone = "FOR RESERVATIONS CALL (877) 444-6777, FOR OTHER, NON REAL TIME INFORMATION, CALL (555) 010-0100 DURING OFFICE HOURS.";
        // CM4 identity fixtures (decision D42, TD-38), all in their own park and cities, keyed by FacilityID: three
        // campgrounds with the same name (two of them, 990005 and 990012, also with the same city and state, so only the
        // ID tells them apart), a blank and a whitespace-only name, a campground with only a Mailing address and one
        // with none (both listed only By Park, without a location), a leading-zero ID, and a reservable, enabled
        // facility of another type with the twins' name (never listed). Each has its own details.
        public const string ParkIdentity = "Fixture Identity Park";
        public const string CityIdentityUtah = "IDENTITYVILLE";
        public const string CityIdentityColorado = "IDENTITYTOWN";
        public const string TwinName = "FIXTURE TWIN CAMPGROUND";
        public const string TwinUtahID = "990005";
        public const string TwinColoradoID = "990006";
        public const string TwinSameLocationID = "990012";
        public const string TwinSameLocationName = "  fixture twin campground ";
        public const string BlankNameID = "990007";
        public const string WhitespaceNameID = "990008";
        public const string MailingOnlyID = "990009";
        public const string MailingOnlyName = "FIXTURE MAILING ADDRESS CAMPGROUND";
        public const string LeadingZeroID = "0990010";
        public const string LeadingZeroName = "FIXTURE LEADING ZERO CAMPGROUND";
        public const string OtherFacilityID = "990011";
        // CM6 (decision D44, TD-53): a reservable, enabled campground without a park and without an address, which no
        // lookup tab lists and only the search editor's picker and typed ID reach
        public const string OrphanID = "990013";
        public const string OrphanName = "FIXTURE ORPHAN CAMPGROUND";

        // A database created here is a few hundred KB; the embedded RIDB database extracts to 226 MB.
        public const long MaxFixtureDbBytes = 5_000_000;

        private static string? _Root;
        private static string? _OriginalXdgDataHome;

        public static string Root => _Root ?? throw new InvalidOperationException("GUI test environment not initialized");

        // A GUID's 32 hexadecimal digits with the digits 0 to 9 written as the letters g to p: as unique as the GUID, and no digit
        // in it, so a folder named with it can't hold a number a test checks the shown texts for (TD-63)
        internal static string LettersOnly(Guid Id) =>
            new string(Id.ToString("N").Select(c => c >= '0' && c <= '9' ? (char)('g' + (c - '0')) : c).ToArray());

        public static string ConfigPath => Path.Join(Root, "CampertronConfig");
        public static string DbFile => Path.Join(ConfigPath, "RecreationDotOrg.db");

        [OneTimeSetUp]
        public void CreateIsolatedEnvironment()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("GUI tests isolate LocalApplicationData through XDG_DATA_HOME, which only Linux honours.");
            }

            // D62: no GUI test may start a process; the automatic restart's production start then throws instead (the
            // tests give the restart seams of their own)
            Campertron.AutomaticRestart.StartForbidden = true;
            _OriginalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            // the root's random part in letters only (LettersOnly): the root is in every path a test's folder has, and a test that
            // checks the texts it shows for digits must see only the digits Campertron chose to show (TD-63)
            _Root = Path.Join(Path.GetTempPath(), "CampertronGUITests-" + LettersOnly(Guid.NewGuid()));
            Directory.CreateDirectory(_Root);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _Root);

            // Fail closed: never run a GUI test unless LocalApplicationData really is the temporary directory.
            string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (LocalAppData != _Root)
            {
                throw new InvalidOperationException($"LocalApplicationData resolves to '{LocalAppData}', not the isolated '{_Root}'");
            }

            Directory.CreateDirectory(ConfigPath);
            File.WriteAllText(Path.Join(ConfigPath, "General.yaml"),
                "outputTo: UnitTest\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            CreateFixtureDatabase();

            // Load.GetConfig() would replace a database that LegacyAddressLayout flags; the fixture must not be flagged.
            Assert.That(LegacyAddressLayout.DetectRepairReason(DbFile), Is.EqualTo(LegacyAddressLayout.RepairReason.None));
        }

        [OneTimeTearDown]
        public void DeleteIsolatedEnvironment()
        {
            SqliteConnection.ClearAllPools();
            if (_Root != null)
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", _OriginalXdgDataHome);
                if (Directory.Exists(_Root))
                {
                    Directory.Delete(_Root, true);
                }
                _Root = null;
            }
        }

        private static void CreateFixtureDatabase()
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990101", RecAreaName = ParkOne });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990102", RecAreaName = ParkTwo });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990103", RecAreaName = ParkThree });
                db.FacilitiesEntries.Add(NewCampground(CampgroundAlphaID, CampgroundAlpha, "990101"));
                db.FacilitiesEntries.Add(NewCampground(CampgroundBravoID, CampgroundBravo, "990102"));
                db.FacilitiesEntries.Add(new FacilitiesData
                {
                    FacilityID = CampgroundCharlieID,
                    FacilityName = CampgroundCharlie,
                    FacilityDescription = "<p>Sparse fixture</p>",
                    FacilityTypeDescription = "Campground",
                    ParentRecAreaID = "990103",
                    Reservable = true,
                    Enabled = true
                });
                FacilitiesData Delta = NewCampground(CampgroundDeltaID, CampgroundDelta, "990103");
                Delta.FacilityPhone = DeltaPhone;
                Delta.FacilityDescription = "<h2>Overview</h2>" + string.Concat(Enumerable.Repeat(
                    "<p>A long fixture paragraph that describes the campground, its trails, its river, its seasons and its rules in detail.</p>", 40));
                db.FacilitiesEntries.Add(Delta);
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundAlphaID, CityAlpha, StateUtah, "84000"));
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundBravoID, CityBravo, StateColorado, "80000"));
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundCharlieID, EmptyCity, StateUtah, "84001"));
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundDeltaID, CityBravo, StateColorado, "80001"));

                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990104", RecAreaName = ParkIdentity });
                db.FacilitiesEntries.Add(NewIdentityCampground(TwinUtahID, TwinName, "Utah twin"));
                db.FacilitiesEntries.Add(NewIdentityCampground(TwinColoradoID, TwinName, "Colorado twin"));
                db.FacilitiesEntries.Add(NewIdentityCampground(TwinSameLocationID, TwinSameLocationName, "Same-place twin"));
                db.FacilitiesEntries.Add(NewIdentityCampground(BlankNameID, "", "Blank name"));
                db.FacilitiesEntries.Add(NewIdentityCampground(WhitespaceNameID, "   ", "Whitespace name"));
                db.FacilitiesEntries.Add(NewIdentityCampground(MailingOnlyID, MailingOnlyName, "Mailing address only"));
                db.FacilitiesEntries.Add(NewIdentityCampground(LeadingZeroID, LeadingZeroName, "Leading zero"));
                FacilitiesData Other = NewIdentityCampground(OtherFacilityID, TwinName, "Not a campground");
                Other.FacilityTypeDescription = "Facility";
                db.FacilitiesEntries.Add(Other);
                db.FacilityAddressesEntries.Add(NewAddress(TwinUtahID, CityIdentityUtah, StateUtah, "84002"));
                db.FacilityAddressesEntries.Add(NewAddress(TwinColoradoID, CityIdentityColorado, StateColorado, "80002"));
                db.FacilityAddressesEntries.Add(NewAddress(TwinSameLocationID, CityIdentityUtah, StateUtah, "84002"));
                db.FacilityAddressesEntries.Add(NewAddress(BlankNameID, CityIdentityUtah, StateUtah, "84002"));
                FacilityAddressesRecdata Mailing = NewAddress(MailingOnlyID, CityIdentityUtah, StateUtah, "84002");
                Mailing.FacilityAddressType = "Mailing";
                db.FacilityAddressesEntries.Add(Mailing);
                db.FacilityAddressesEntries.Add(NewAddress(LeadingZeroID, CityIdentityUtah, StateUtah, "84002"));
                db.FacilityAddressesEntries.Add(NewAddress(OtherFacilityID, CityIdentityUtah, StateUtah, "84002"));
                FacilitiesData Orphan = NewIdentityCampground(OrphanID, OrphanName, "Orphan");
                Orphan.ParentRecAreaID = null;
                db.FacilitiesEntries.Add(Orphan);
                AddFilterFixture(db);
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
            // as a start leaves an installed database (D51: Campertron's attribute index)
            RidbIndexes.Create(DbFile);
        }

        // the campsites of Alpha and Bravo for the filter selectors (D51): Alpha's equipment has two spellings of one value
        // (Tent and tent) and its types one that contains another (STANDARD NONELECTRIC in GROUP STANDARD NONELECTRIC); a
        // campsite name appears twice; Bravo has other values (RV/MOTORHOME contains RV)
        public static readonly (string Facility, string Campsite, string Name, string Type, string[] Equipment, string[] Attributes)[] FilterCampsites =
        {
            (CampgroundAlphaID, "9900011", "A001", "STANDARD NONELECTRIC", new[] { "Tent", "SMALL TENT" }, new[] { "Shade:Yes", "Checkin Time:2:00 PM" }),
            (CampgroundAlphaID, "9900012", "A002", "STANDARD ELECTRIC", new[] { "RV", "Trailer" }, new[] { "Shade:No" }),
            (CampgroundAlphaID, "9900013", "G001", "GROUP STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade:Yes" }),
            (CampgroundAlphaID, "9900014", "A002", "STANDARD NONELECTRIC", new[] { "tent" }, new[] { "Shade:yes" }),
            (CampgroundBravoID, "9900021", "H1", "EQUESTRIAN NONELECTRIC", new[] { "Horse", "RV/MOTORHOME" }, new[] { "Shade:No" }),
            (CampgroundBravoID, "9900022", "H2", "EQUESTRIAN NONELECTRIC", new[] { "RV" }, Array.Empty<string>()),
            // Charlie's campsite names (D51 stage 3): blank, spaces only, space-padded, two spellings of one name, numbers that
            // sort naturally (2 before 10) and a name two campsite records share; and its attributes (stage 4): a blank value,
            // a value with a colon, a space-padded value and two spellings of one value
            (CampgroundCharlieID, "9900031", "", "STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade:", "Checkin Time:2:00 PM" }),
            (CampgroundCharlieID, "9900032", "  ", "STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade:Yes" }),
            (CampgroundCharlieID, "9900033", " B1 ", "STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade: Yes" }),
            (CampgroundCharlieID, "9900034", "B1", "STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade:YES" }),
            (CampgroundCharlieID, "9900035", "b1", "STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade:Yes", "Toilet:Flush" }),
            (CampgroundCharlieID, "9900036", "10", "STANDARD NONELECTRIC", new[] { "Tent" }, Array.Empty<string>()),
            (CampgroundCharlieID, "9900037", "2", "STANDARD NONELECTRIC", new[] { "Tent" }, Array.Empty<string>()),
            (CampgroundCharlieID, "9900038", "10", "STANDARD ELECTRIC", new[] { "Tent" }, Array.Empty<string>()),
        };

        private static void AddFilterFixture(RecreationDotOrgContext db)
        {
            int Attribute = 1;
            foreach (var Site in FilterCampsites)
            {
                db.CampsitesEntries.Add(new CampsitesRecdata { CampsiteID = Site.Campsite, FacilityID = Site.Facility, CampsiteName = Site.Name, CampsiteType = Site.Type, Loop = "Loop" });
                foreach (string Equipment in Site.Equipment)
                {
                    db.PermittedEquipmentEntries.Add(new PermittedEquipmentRecdata { CampsiteID = Site.Campsite, EquipmentName = Equipment });
                }
                foreach (string Pair in Site.Attributes)
                {
                    int Colon = Pair.IndexOf(':');
                    db.CampsiteAttributesEntries.Add(new CampsiteAttributesRecdata
                    {
                        AttributeID = Attribute++, EntityID = Site.Campsite, AttributeName = Pair.Substring(0, Colon), AttributeValue = Pair.Substring(Colon + 1), EntityType = "Campsite"
                    });
                }
            }
        }

        private static FacilitiesData NewCampground(string FacilityID, string FacilityName, string RecAreaID)
        {
            return new FacilitiesData
            {
                FacilityID = FacilityID,
                FacilityName = FacilityName,
                FacilityDescription = "<p>Fixture description for " + FacilityName + "</p>",
                FacilityDirections = "Fixture directions",
                FacilityUseFeeDescription = "Fixture fee",
                FacilityPhone = "555-0100",
                FacilityEmail = "fixture@example.invalid",
                FacilityTypeDescription = "Campground",
                ParentRecAreaID = RecAreaID,
                Reservable = true,
                Enabled = true
            };
        }

        // An identity fixture in ParkIdentity whose visible details all name it (Tag) and carry its ID, so a test can tell
        // whose details are shown.
        public static string IdentityDescription(string Tag) => Tag + " description";
        public static string IdentityPhone(string FacilityID) => "555-" + FacilityID;

        private static FacilitiesData NewIdentityCampground(string FacilityID, string FacilityName, string Tag)
        {
            return new FacilitiesData
            {
                FacilityID = FacilityID,
                FacilityName = FacilityName,
                FacilityDescription = "<p>" + IdentityDescription(Tag) + "</p>",
                FacilityDirections = Tag + " directions",
                FacilityUseFeeDescription = Tag + " fee",
                FacilityPhone = IdentityPhone(FacilityID),
                FacilityEmail = FacilityID + "@example.invalid",
                FacilityTypeDescription = "Campground",
                ParentRecAreaID = "990104",
                Reservable = true,
                Enabled = true
            };
        }

        private static FacilityAddressesRecdata NewAddress(string FacilityID, string City, string State, string PostalCode)
        {
            return new FacilityAddressesRecdata
            {
                FacilityAddressID = "A-" + FacilityID,
                FacilityID = FacilityID,
                FacilityAddressType = "Physical",
                City = City,
                PostalCode = PostalCode,
                AddressStateCode = State
            };
        }
    }
}
