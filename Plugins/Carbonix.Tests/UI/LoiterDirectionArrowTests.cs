using System.Linq;
using System.Drawing;
using GMap.NET.WindowsForms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.UI
{
    [TestClass]
    public class LoiterDirectionArrowTests
    {
        // 200 px circle centered at (150, 150)
        static readonly RectangleF Circle = new RectangleF(50, 50, 200, 200);

        [TestMethod]
        public void TopArrow_PointsRightForClockwiseAndLeftForCounterClockwise()
        {
            // The arms trail behind the tip, so an arrow pointing right has both arm ends to the left of it.
            var cw = GMapMarkerRect.DirectionArrows(Circle, 1, 15)[0];
            Assert.IsTrue(cw.Skip(1).All(p => p.X < cw[0].X));

            var ccw = GMapMarkerRect.DirectionArrows(Circle, -1, 15)[0];
            Assert.IsTrue(ccw.Skip(1).All(p => p.X > ccw[0].X));
        }
    }
}
