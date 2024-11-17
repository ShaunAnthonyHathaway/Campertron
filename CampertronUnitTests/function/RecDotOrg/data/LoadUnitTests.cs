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
            Assert.That(Load.RunConsoleSearch(true), Is.True);
        }
    }
}