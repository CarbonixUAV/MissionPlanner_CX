using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests
{
    [TestClass]
    public class AnglesTests
    {
        [TestMethod]
        public void Compass_NamesEachOctantAtItsCentre()
        {
            Assert.AreEqual("N", Angles.Compass(0));
            Assert.AreEqual("NE", Angles.Compass(45));
            Assert.AreEqual("E", Angles.Compass(90));
            Assert.AreEqual("SE", Angles.Compass(135));
            Assert.AreEqual("S", Angles.Compass(180));
            Assert.AreEqual("SW", Angles.Compass(225));
            Assert.AreEqual("W", Angles.Compass(270));
            Assert.AreEqual("NW", Angles.Compass(315));
        }

        [TestMethod]
        public void Compass_RoundsToTheNearestOctant()
        {
            Assert.AreEqual("N", Angles.Compass(22));
            Assert.AreEqual("NE", Angles.Compass(23));
            Assert.AreEqual("NE", Angles.Compass(67));
            Assert.AreEqual("E", Angles.Compass(68));
        }

        [TestMethod]
        public void Compass_WrapsBackToNorthNearThreeSixty()
        {
            // The bucket above 337.5 rounds to index 8, which has to fold back
            // to N rather than run off the end of the table.
            Assert.AreEqual("NW", Angles.Compass(337));
            Assert.AreEqual("N", Angles.Compass(338));
            Assert.AreEqual("N", Angles.Compass(359.9));
            Assert.AreEqual("N", Angles.Compass(360));
        }

        [TestMethod]
        public void Compass_HandlesBearingsPushedNegativeByDeclination()
        {
            // true - declination goes negative for an easterly variation on a
            // near-north bearing.
            Assert.AreEqual("N", Angles.Compass(-10));
            Assert.AreEqual("NW", Angles.Compass(-45));
        }

        [TestMethod]
        public void Wrap360_BringsAnythingIntoRange()
        {
            Assert.AreEqual(350, Angles.Wrap360(-10), 1e-9);
            Assert.AreEqual(10, Angles.Wrap360(370), 1e-9);
            Assert.AreEqual(180, Angles.Wrap360(180), 1e-9);
        }

        [TestMethod]
        public void Wrap360_ReadsNorthAsThreeSixty()
        {
            // Aviation convention: north is 360, never 000. Callers rounding
            // for display rely on this, so zero must not come back out.
            Assert.AreEqual(360, Angles.Wrap360(0), 1e-9);
            Assert.AreEqual(360, Angles.Wrap360(360), 1e-9);
            Assert.AreEqual(360, Angles.Wrap360(720), 1e-9);
            Assert.AreEqual(360, Angles.Wrap360(-360), 1e-9);
        }

        [TestMethod]
        public void Wrap180_SignsTheDifference()
        {
            Assert.AreEqual(0, Angles.Wrap180(0), 1e-9);
            Assert.AreEqual(-10, Angles.Wrap180(350), 1e-9);
            Assert.AreEqual(10, Angles.Wrap180(10), 1e-9);
            Assert.AreEqual(180, Angles.Wrap180(180), 1e-9);
            Assert.AreEqual(-179, Angles.Wrap180(181), 1e-9);
        }
    }
}
