using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.RecDotOrg.data
{
    public class LoadTests
    {
        CampertronInternalConfig config = new CampertronInternalConfig();
        List<AvailableData> availableData = new List<AvailableData>();
        RunningData RunData = Load.Preload(Load.GetConfig());

        [SetUp]
        public void Setup()
        {
            config = Load.GetConfig();
        }

        [Test]
        public void DbExistsTest()
        {
            Assert.That(Load.DbExists(config), Is.True);
        }

        [Test]
        public void GetConfigTest()
        {
            Assert.That(Load.GetConfig(), Is.Not.Null);
        }

        [Test]
        public void HasNewEntriesTest()
        {
            Assert.That(Load.HasNewEntries(availableData, config), Is.False);
        }
        // non-empty hits: the result is true while a hit has no marker, for every output type, but only Email
        // writes the markers (Camp.History is the Email loop's notification history). Own temporary ConfigPath.
        [TestCase(OutputType.Console, false)]
        [TestCase(OutputType.HtmlFile, false)]
        [TestCase(OutputType.UnitTest, false)]
        [TestCase(OutputType.Email, true)]
        public void HasNewEntries_NonEmpty_WritesMarkersOnlyForEmail(OutputType Output, bool WritesMarkers)
        {
            string Root = Path.Join(Path.GetTempPath(), "CampertronHasNewEntries-" + Guid.NewGuid().ToString("N"));
            try
            {
                var ThisConfig = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = Output }, ConfigPath = Root };
                var Hits = new List<AvailableData>
                {
                    new AvailableData { CampsiteID = "111", HitDate = new DateTime(2027, 1, 3) },
                    new AvailableData { CampsiteID = "222", HitDate = new DateTime(2027, 12, 25) }
                };
                string Marker1 = Path.Join(Root, "Camp.History", "2027", "1", "3", "111");
                string Marker2 = Path.Join(Root, "Camp.History", "2027", "12", "25", "222");

                Assert.That(Load.HasNewEntries(Hits, ThisConfig), Is.True);
                Assert.That(Directory.Exists(Marker1) && Directory.Exists(Marker2), Is.EqualTo(WritesMarkers));
                Assert.That(Load.HasNewEntries(Hits, ThisConfig), Is.EqualTo(!WritesMarkers), "new again unless Email marked them");

                // an existing marker counts as reported for every output type and is never removed
                Directory.CreateDirectory(Marker1);
                Directory.CreateDirectory(Marker2);
                Assert.That(Load.HasNewEntries(Hits, ThisConfig), Is.False);
                Assert.That(Directory.Exists(Marker1) && Directory.Exists(Marker2), Is.True);
            }
            finally
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
        }
        // a config without GeneralConfig is not Email: no marker is written
        [Test]
        public void HasNewEntries_WithoutGeneralConfig_WritesNoMarker()
        {
            string Root = Path.Join(Path.GetTempPath(), "CampertronHasNewEntries-" + Guid.NewGuid().ToString("N"));
            try
            {
                var ThisConfig = new CampertronInternalConfig { ConfigPath = Root };
                var Hits = new List<AvailableData> { new AvailableData { CampsiteID = "111", HitDate = new DateTime(2027, 1, 3) } };

                Assert.That(Load.HasNewEntries(Hits, ThisConfig), Is.True);
                Assert.That(Directory.Exists(Path.Join(Root, "Camp.History", "2027", "1", "3", "111")), Is.False);
            }
            finally
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
        }
        [Test]
        public void DbExistsCheckTest()
        {
            Assert.That(Load.DbExistsCheck(config), Is.True);
        }
        [Test]
        public void PostProcessTest()
        {
            config.GeneralConfig.OutputTo = OutputType.UnitTest;
            Assert.That(Load.PostProcess(config, ref RunData), Is.True);
        }        
        [Test]
        public void PreloadTest()
        {
            Assert.That(Load.Preload(config), Is.Not.Null);
        }
        [Test]
        public void RunConsoleSearchTest()
        {
            config.GeneralConfig.OutputTo = OutputType.UnitTest;
            Assert.That(Load.RunConsoleSearch(config, true), Is.True);
        }
        [Test]
        public void RunConsoleSearchTest2()
        {
            config.GeneralConfig.OutputTo = OutputType.UnitTest;
            Assert.That(Load.RunConsoleSearch(config), Is.True);
        }
    }
}