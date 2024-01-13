using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.RecDotOrg.data
{
    public class LoadTests
    {
        CampertronInternalConfig config = new CampertronInternalConfig();
        [SetUp]
        public void Setup()
        {
            config = Load.GetConfig();
        }
        [Test]
        public void LOAD_DbExists()
        {
            GeneralConfig GeneralConfig = Yaml.GeneralConfigGetConfig(config.ConfigPath);
            Assert.IsTrue(Load.DbExists(config));
        }
        [Test]
        public void LOAD_DbInitializes()
        {//loads without throwing an exception
            GeneralConfig GeneralConfig = Yaml.GeneralConfigGetConfig(config.ConfigPath);
            Load.DbExistsCheck(config);
            Assert.Pass();
        }
        [Test]
        public void LOAD_HasNewEntries_True()
        {
            List<CampsiteHistory> NewList = new List<CampsiteHistory>() { new CampsiteHistory() { CampsiteID = "1", HitDate = DateTime.UtcNow } };
            List<CampsiteHistory> OldList = new List<CampsiteHistory>() { new CampsiteHistory() { CampsiteID = "2", HitDate = DateTime.UtcNow } };
            List<AvailableData> AvailableData = new List<AvailableData>();
            foreach (CampsiteHistory CampsiteHistory in NewList)
            {
                AvailableData.Add(new AvailableData() { CampsiteID = CampsiteHistory.CampsiteID, HitDate = CampsiteHistory.HitDate });
            }
            Assert.IsTrue(Load.HasNewEntries(ref NewList, OldList, AvailableData));
        }
        [Test]
        public void LOAD_HasNewEntries_False()
        {
            CampsiteHistory TestCampsiteHistory = new CampsiteHistory()
            {
                CampsiteID = "1",
                HitDate = DateTime.UtcNow
            };
            List<CampsiteHistory> NewList = new List<CampsiteHistory>() { new CampsiteHistory() { CampsiteID = TestCampsiteHistory.CampsiteID, HitDate = TestCampsiteHistory.HitDate } };
            List<CampsiteHistory> OldList = new List<CampsiteHistory>() { new CampsiteHistory() { CampsiteID = TestCampsiteHistory.CampsiteID, HitDate = TestCampsiteHistory.HitDate } };
            List<AvailableData> AvailableData = new List<AvailableData>();
            foreach (CampsiteHistory CampsiteHistory in NewList)
            {
                AvailableData.Add(new AvailableData() { CampsiteID = CampsiteHistory.CampsiteID, HitDate = CampsiteHistory.HitDate });
            }
            Assert.IsFalse(Load.HasNewEntries(ref NewList, OldList, AvailableData));
        }
    }
}