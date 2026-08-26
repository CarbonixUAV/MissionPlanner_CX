using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.UI
{
    [TestClass]
    public class MapPinToolTests
    {
        // --------------------------------------------------
        //                       TCPA
        // --------------------------------------------------
        //
        // Cruise is about 27 m/s, so 2700 m straight at the point is 100 s.

        const double Cruise = 27;

        [TestMethod]
        public void Tcpa_StraightAtThePointIsRangeOverSpeed()
        {
            Assert.AreEqual(100, MapPinTool.TcpaSeconds(2700, 90, 90, Cruise).Value, 1e-6);
        }

        [TestMethod]
        public void Tcpa_ShortensForTheOffTrackComponent()
        {
            // 30 degrees off: nearest at range*cos(30)/V, not range/V. Kept
            // well inside the miss gate so this tests the arithmetic and not
            // the threshold.
            var seconds = MapPinTool.TcpaSeconds(1500, 120, 90, Cruise).Value;

            Assert.AreEqual(1500 * Math.Cos(30 * Math.PI / 180) / Cruise, seconds, 1e-6);

            // And notably sooner than the naive range/speed would claim.
            Assert.IsTrue(seconds < 1500 / Cruise);
        }

        [TestMethod]
        public void Tcpa_MeasuresOffTrackTheShortWayRound()
        {
            // Bearing 010 against a track of 350 is 20 degrees off, not 340.
            var wrapped = MapPinTool.TcpaSeconds(2000, 10, 350, Cruise);
            var plain = MapPinTool.TcpaSeconds(2000, 30, 10, Cruise);

            Assert.AreEqual(plain.Value, wrapped.Value, 1e-6);
        }

        [TestMethod]
        public void Tcpa_SilentWhenNotClosing()
        {
            // Directly away, and exactly abeam.
            Assert.IsNull(MapPinTool.TcpaSeconds(2000, 270, 90, Cruise));
            Assert.IsNull(MapPinTool.TcpaSeconds(2000, 180, 90, Cruise));
        }

        [TestMethod]
        public void Tcpa_SilentWhenItWouldMissByTooFar()
        {
            // 20 degrees off at 5 km misses by 1710 m - passing near, not
            // arriving - even though the closure is healthy.
            Assert.IsNull(MapPinTool.TcpaSeconds(5000, 110, 90, Cruise));

            // The same angle close in misses by 342 m, which is arriving.
            Assert.IsNotNull(MapPinTool.TcpaSeconds(1000, 110, 90, Cruise));
        }

        [TestMethod]
        public void Tcpa_SilentBeyondTheHorizon()
        {
            // Straight at it, but 30 km out is over 18 minutes at cruise.
            Assert.IsNull(MapPinTool.TcpaSeconds(30000, 90, 90, Cruise));

            // Just inside the horizon still answers.
            Assert.IsNotNull(MapPinTool.TcpaSeconds(
                MapPinTool.Horizon * Cruise - 1, 90, 90, Cruise));
        }

        [TestMethod]
        public void Tcpa_SilentWhenTooSlowForTheTrackToMeanAnything()
        {
            Assert.IsNull(MapPinTool.TcpaSeconds(500, 90, 90, MapPinTool.MinGroundSpeed - 0.1));
            Assert.IsNotNull(MapPinTool.TcpaSeconds(500, 90, 90, MapPinTool.MinGroundSpeed));
        }

        [TestMethod]
        public void Duration_QuantisesSoTheFigureSitsStill()
        {
            Assert.AreEqual("4 min", MapPinTool.Duration(4 * 60 + 12));
            Assert.AreEqual("5 min", MapPinTool.Duration(4 * 60 + 40));
            Assert.AreEqual("1 min", MapPinTool.Duration(60));
            Assert.AreEqual("50 s", MapPinTool.Duration(59));
            Assert.AreEqual("30 s", MapPinTool.Duration(35));

            // Never counts itself down to nothing.
            Assert.AreEqual("10 s", MapPinTool.Duration(3));
            Assert.AreEqual("10 s", MapPinTool.Duration(0));
        }

        [TestMethod]
        public void Duration_FloorAgreesWithTheArrivalCutoff()
        {
            // The readout vanishes below ArrivedSeconds, so the last figure it
            // ever renders is whatever that instant quantises to. If the floor
            // and the cutoff drift apart, the countdown either sticks on a
            // stale number or skips a step on its way out.
            Assert.AreEqual("10 s", MapPinTool.Duration(MapPinTool.ArrivedSeconds));
        }
    }
}
