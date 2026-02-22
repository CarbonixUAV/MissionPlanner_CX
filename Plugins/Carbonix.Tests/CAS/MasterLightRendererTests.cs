using Carbonix.CAS;
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
            _mgr.Fire(AlertTier.Warning, "A");
            Assert.AreEqual(LightState.Flashing,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), AlertTier.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Steady_WhenActiveAcked()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.AckTier(AlertTier.Warning);
            Assert.AreEqual(LightState.Steady,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), AlertTier.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Off_WhenAllResolvedAcked()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.AckTier(AlertTier.Warning);
            _mgr.Resolve("A");
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), AlertTier.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Flashing_WhenResolvedUnacked()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.Resolve("A");
            Assert.AreEqual(LightState.Flashing,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), AlertTier.Warning));
        }

        [TestMethod]
        public void DeriveLightState_Off_WhenEmpty()
        {
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), AlertTier.Warning));
            Assert.AreEqual(LightState.Off,
                MasterLightRenderer.DeriveLightState(_mgr.GetUnclearedAlerts(), AlertTier.Caution));
        }
    }
}
