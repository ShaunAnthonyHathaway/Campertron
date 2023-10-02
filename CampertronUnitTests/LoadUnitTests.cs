using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests
{
    public class LoadTests
    {
        [SetUp]
        public void Setup()
        {

        }
        [Test]
        public void LOAD_DbExists()
        {
            GeneralConfig GeneralConfig = Yaml.GeneralConfigGetConfig();
            Assert.IsTrue(Load.DbExists());
        }
        [Test]
        public void LOAD_DbInitializes()
        {//loads without throwing an exception
            GeneralConfig GeneralConfig = Yaml.GeneralConfigGetConfig();
            Load.DbExistsCheck(GeneralConfig);
            Assert.Pass();
        }
        [Test]
        public void LOAD_HasNewEntries_True()
        {
            List<CampsiteHistory> NewList = new List<CampsiteHistory>() { new CampsiteHistory() { CampsiteID = "1", HitDate = DateTime.UtcNow } };
            List<CampsiteHistory> OldList = new List<CampsiteHistory>() { new CampsiteHistory() { CampsiteID = "2", HitDate = DateTime.UtcNow } };
            Assert.IsTrue(Load.HasNewEntries(NewList, OldList));
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
            Assert.IsFalse(Load.HasNewEntries(NewList, OldList));
        }
    }
}