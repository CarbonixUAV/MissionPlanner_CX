using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner;

namespace Carbonix.Tests.UI
{
    [TestClass]
    public class DistanceTextTests
    {
        string _saved_unit;
        float _saved_multiplier;
        CultureInfo _saved_culture;

        [TestInitialize]
        public void Setup()
        {
            _saved_unit = CurrentState.DistanceUnit;
            _saved_multiplier = CurrentState.multiplierdist;

            // The formats carry a group separator, so pin the culture rather
            // than let the build agent's locale decide what the tests expect.
            _saved_culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        }

        [TestCleanup]
        public void TearDown()
        {
            CurrentState.DistanceUnit = _saved_unit;
            CurrentState.multiplierdist = _saved_multiplier;
            Thread.CurrentThread.CurrentCulture = _saved_culture;
        }

        /// <summary>
        /// Selects a unit family the way MainV2.ChangeUnits does. Setting
        /// DistanceUnit on its own would leave multiplierdist behind, which is
        /// a half-state the running application never has.
        /// </summary>
        static void UseUnits(string unit)
        {
            CurrentState.DistanceUnit = unit;
            CurrentState.multiplierdist = unit == "ft" ? 3.2808399f : 1f;
        }

        // --------------------------------------------------
        //                     Metric
        // --------------------------------------------------

        [TestMethod]
        public void Metres_BelowOneKilometre()
        {
            UseUnits("m");
            Assert.AreEqual("450 m", DistanceText.Format(450));
        }

        [TestMethod]
        public void Metres_StepUpToKilometresAtOneThousand()
        {
            UseUnits("m");
            Assert.AreEqual("999 m", DistanceText.Format(999));
            Assert.AreEqual("1.00 km", DistanceText.Format(1000));
        }

        [TestMethod]
        public void Kilometres_LosePrecisionAsTheyGrow()
        {
            UseUnits("m");
            Assert.AreEqual("45.0 km", DistanceText.Format(45_000));
            Assert.AreEqual("450 km", DistanceText.Format(450_000));
        }

        // --------------------------------------------------
        //                    Imperial
        // --------------------------------------------------

        [TestMethod]
        public void Feet_BelowOneStatuteMile()
        {
            UseUnits("ft");

            // 300 m is a little under 1000 ft.
            Assert.AreEqual("984 ft", DistanceText.Format(300));
        }

        [TestMethod]
        public void Feet_StepUpToMilesAtOneStatuteMile()
        {
            UseUnits("ft");

            // A statute mile is 1609.344 m; a metre short of it is still feet.
            Assert.AreEqual("5,279 ft", DistanceText.Format(1609.0));
            Assert.AreEqual("1.00 mi", DistanceText.Format(1609.344));
        }

        // --------------------------------------------------
        //                 Nautical miles
        // --------------------------------------------------

        [TestMethod]
        public void Format_StaysInTheChosenUnitFamily()
        {
            // A nautical mile's worth of metres still reads in the operator's
            // units; NauticalMiles is the only thing that speaks NM.
            UseUnits("m");
            Assert.AreEqual("1.85 km", DistanceText.Format(1852));

            UseUnits("ft");
            Assert.AreEqual("1.15 mi", DistanceText.Format(1852));
        }

        [TestMethod]
        public void NauticalMiles_OmittedBelowOneByDefault()
        {
            Assert.AreEqual("", DistanceText.NauticalMiles(1851));
            Assert.AreEqual("1.00 NM", DistanceText.NauticalMiles(1852));
            Assert.AreEqual("10.0 NM", DistanceText.NauticalMiles(18520));
            Assert.AreEqual("", DistanceText.NauticalMiles(double.NaN));
        }

        [TestMethod]
        public void NauticalMiles_IgnoreTheDistanceUnitSetting()
        {
            // A nautical mile is a nautical mile either way round.
            UseUnits("m");
            var metric = DistanceText.NauticalMiles(18520);

            UseUnits("ft");
            Assert.AreEqual(metric, DistanceText.NauticalMiles(18520));
        }

        [TestMethod]
        public void NauticalMiles_CanKeepTheFractionForAStandingSlot()
        {
            UseUnits("m");

            Assert.AreEqual("0.46 NM", DistanceText.NauticalMiles(850, false));
            Assert.AreEqual("0.00 NM", DistanceText.NauticalMiles(0, false));

            // Still nothing to say when there is no number at all.
            Assert.AreEqual("", DistanceText.NauticalMiles(double.NaN, false));
        }

        // --------------------------------------------------
        //                     Edges
        // --------------------------------------------------

        [TestMethod]
        public void Zero_ReadsInTheSmallUnit()
        {
            UseUnits("m");
            Assert.AreEqual("0 m", DistanceText.Format(0));
        }

        [TestMethod]
        public void UnsetUnit_FallsBackToMetric()
        {
            UseUnits("");
            Assert.AreEqual("450 m", DistanceText.Format(450));
        }

        [TestMethod]
        public void NotANumber_DoesNotThrow()
        {
            UseUnits("m");
            Assert.AreEqual("--", DistanceText.Format(double.NaN));
            Assert.AreEqual("--", DistanceText.Format(double.PositiveInfinity));
        }
    }
}
