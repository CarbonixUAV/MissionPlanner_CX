using Carbonix.CAS;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.CAS
{
    [TestClass]
    public class MasterLightRendererTests
    {
        AlertManager _mgr;

        [TestInitialize]
        public void Setup()
        {
            _mgr = new AlertManager();
        }

        [TestMethod]
        public void DeriveLightState_Flashing_WhenUnacked()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            Assert.AreEqual(LightState.Flashing,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Steady_WhenActiveAcked()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.AckSeverity(WarningSeverity.Warning);
            Assert.AreEqual(LightState.Steady,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Off_WhenAllResolvedAcked()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.AckSeverity(WarningSeverity.Warning);
            _mgr.Resolve("A");
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Flashing_WhenResolvedUnacked()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.Resolve("A");
            Assert.AreEqual(LightState.Flashing,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Off_WhenOnlyAdvisories()
        {
            _mgr.Fire(WarningSeverity.Advisory, "Info");
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Warning));
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Caution));
        }

        [TestMethod]
        public void DeriveLightState_Off_WhenEmpty()
        {
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Warning));
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), WarningSeverity.Caution));
        }
    }
}
