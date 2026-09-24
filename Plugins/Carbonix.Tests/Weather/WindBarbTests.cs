using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Weather
{
    [TestClass]
    public class WindBarbTests
    {
        static void Check(double knots, int pennants, int full, int half)
        {
            var s = WindBarb.Symbol(knots);
            Assert.IsFalse(s.Calm);
            Assert.AreEqual(pennants, s.Pennants);
            Assert.AreEqual(full, s.FullBarbs);
            Assert.AreEqual(half, s.HalfBarbs);
        }

        [TestMethod]
        public void CalmBelowOneKnot()
        {
            Assert.IsTrue(WindBarb.Symbol(0).Calm);
            Assert.IsTrue(WindBarb.Symbol(0.9).Calm);
            Assert.IsFalse(WindBarb.Symbol(1.0).Calm);
        }

        [TestMethod]
        public void BareStaffUpToTwoKnots()
        {
            Assert.IsTrue(WindBarb.Symbol(1).BareStaff);
            Assert.IsTrue(WindBarb.Symbol(2.4).BareStaff);
            Assert.IsFalse(WindBarb.Symbol(2.5).BareStaff);
        }

        [TestMethod]
        public void RoundsToNearestFive()
        {
            Check(2.5, 0, 0, 1);
            Check(5, 0, 0, 1);
            Check(7.4, 0, 0, 1);
            Check(7.5, 0, 1, 0);
            Check(12.4, 0, 1, 0);
            Check(12.5, 0, 1, 1);
        }

        [TestMethod]
        public void FormatsWholeNumbersToTwoSignificantFigures()
        {
            Assert.AreEqual("0", WindBarb.FormatSpeed(0.4));
            Assert.AreEqual("1", WindBarb.FormatSpeed(0.5));
            Assert.AreEqual("7", WindBarb.FormatSpeed(7.4));
            Assert.AreEqual("8", WindBarb.FormatSpeed(7.5));
            Assert.AreEqual("99", WindBarb.FormatSpeed(99.4));
            Assert.AreEqual("100", WindBarb.FormatSpeed(99.5));
            Assert.AreEqual("100", WindBarb.FormatSpeed(104.9));
            Assert.AreEqual("110", WindBarb.FormatSpeed(105));
            Assert.AreEqual("120", WindBarb.FormatSpeed(123));
        }

        [TestMethod]
        public void StandardExamples()
        {
            Check(10, 0, 1, 0);
            Check(15, 0, 1, 1);
            Check(25, 0, 2, 1);
            Check(45, 0, 4, 1);
            Check(50, 1, 0, 0);
            Check(65, 1, 1, 1);
            Check(105, 2, 0, 1);
        }
    }
}
