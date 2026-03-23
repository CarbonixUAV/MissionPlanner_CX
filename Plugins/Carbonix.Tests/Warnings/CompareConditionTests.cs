using System;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    public class ConditionSource
    {
        public double value { get; set; }
        public string label { get; set; }
        float _rawSpeed;
        public float ScaledSpeed => _rawSpeed * 2;
        public void SetRawSpeed(float v) => _rawSpeed = v;
        public SensorFlags sensors = new SensorFlags();
    }

    public class SensorFlags
    {
        public bool gps { get; set; }
        public bool compass { get; set; }
    }

    [TestClass]
    public class CompareConditionTests
    {
        ConditionState _state;

        [TestInitialize]
        public void Setup() => _state = new ConditionState();

        // ---- All six operators ----

        [TestMethod]
        public void Evaluate_LT_TrueWhenBelow()
        {
            var c = Condition.Field("value", CompareOp.LT, 100);
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 99 }, _state));
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 100 }, _state));
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 101 }, _state));
        }

        [TestMethod]
        public void Evaluate_LTEQ_TrueWhenAtOrBelow()
        {
            var c = Condition.Field("value", CompareOp.LTEQ, 100);
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 99 }, _state));
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 100 }, _state));
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 101 }, _state));
        }

        [TestMethod]
        public void Evaluate_EQ_TrueOnlyWhenEqual()
        {
            var c = Condition.Field("value", CompareOp.EQ, 100);
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 99 }, _state));
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 100 }, _state));
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 101 }, _state));
        }

        [TestMethod]
        public void Evaluate_GT_TrueWhenAbove()
        {
            var c = Condition.Field("value", CompareOp.GT, 100);
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 99 }, _state));
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 100 }, _state));
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 101 }, _state));
        }

        [TestMethod]
        public void Evaluate_GTEQ_TrueWhenAtOrAbove()
        {
            var c = Condition.Field("value", CompareOp.GTEQ, 100);
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 99 }, _state));
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 100 }, _state));
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 101 }, _state));
        }

        [TestMethod]
        public void Evaluate_NEQ_TrueWhenNotEqual()
        {
            var c = Condition.Field("value", CompareOp.NEQ, 100);
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 99 }, _state));
            Assert.IsFalse(c.Evaluate(new ConditionSource { value = 100 }, _state));
            Assert.IsTrue(c.Evaluate(new ConditionSource { value = 101 }, _state));
        }

        // ---- Hysteresis (clear threshold) ----

        [TestMethod]
        public void Evaluate_Hysteresis_FiresAtThreshold_ClearsAtClear()
        {
            // Fires when value < 18, clears when value >= 22
            var c = Condition.Field("value", CompareOp.LT, 18, clear: 22);
            var src = new ConditionSource { value = 25 };

            Assert.IsFalse(c.Evaluate(src, _state), "Above both thresholds");

            src.value = 17;
            Assert.IsTrue(c.Evaluate(src, _state), "Below fire threshold — should fire");

            src.value = 20;
            Assert.IsTrue(c.Evaluate(src, _state), "Between thresholds — stays fired");

            src.value = 22;
            Assert.IsFalse(c.Evaluate(src, _state), "At clear threshold — should clear");

            src.value = 20;
            Assert.IsFalse(c.Evaluate(src, _state), "Back between thresholds — stays cleared");

            src.value = 17;
            Assert.IsTrue(c.Evaluate(src, _state), "Below fire threshold again — re-fires");
        }

        [TestMethod]
        public void Evaluate_Hysteresis_GT_FiresAbove_ClearsBelow()
        {
            // Fires when value > 100, clears when value <= 90
            var c = Condition.Field("value", CompareOp.GT, 100, clear: 90);
            var src = new ConditionSource { value = 80 };

            Assert.IsFalse(c.Evaluate(src, _state));

            src.value = 101;
            Assert.IsTrue(c.Evaluate(src, _state), "Above fire threshold");

            src.value = 95;
            Assert.IsTrue(c.Evaluate(src, _state), "Between thresholds — stays fired");

            src.value = 90;
            Assert.IsFalse(c.Evaluate(src, _state), "At clear threshold — should clear");
        }

        // ---- Error paths ----

        [TestMethod]
        public void Evaluate_MissingProperty_Throws()
        {
            var c = Condition.Field("nonexistent", CompareOp.GT, 0);

            Assert.ThrowsException<MissingMemberException>(
                () => c.Evaluate(new ConditionSource { value = 1 }, _state));
        }

        [TestMethod]
        public void Evaluate_PrivateField_ResolvesRawValue()
        {
            var c = Condition.Field("_rawSpeed", CompareOp.GT, 10);
            var src = new ConditionSource();
            src.SetRawSpeed(20);

            Assert.IsTrue(c.Evaluate(src, _state), "Should read private field directly");
            Assert.AreEqual(40, src.ScaledSpeed, "Sanity: public property applies multiplier");
        }

        [TestMethod]
        public void Evaluate_NonConvertibleProperty_Throws()
        {
            var c = Condition.Field("label", CompareOp.GT, 0);

            Assert.ThrowsException<FormatException>(
                () => c.Evaluate(new ConditionSource { label = "text" }, _state));
        }

        // ---- Dotted path resolution ----

        [TestMethod]
        public void Evaluate_DottedPath_ResolvesNestedProperty()
        {
            var c = Condition.Field("sensors.gps", CompareOp.GT, 0);
            var src = new ConditionSource();

            src.sensors.gps = false;
            Assert.IsFalse(c.Evaluate(src, _state));

            src.sensors.gps = true;
            Assert.IsTrue(c.Evaluate(src, _state));
        }

        [TestMethod]
        public void Evaluate_DottedPath_MissingSegment_Throws()
        {
            var c = Condition.Field("sensors.nonexistent", CompareOp.GT, 0);

            Assert.ThrowsException<MissingMemberException>(
                () => c.Evaluate(new ConditionSource(), _state));
        }

        // ---- NamedValue basics ----

        [TestMethod]
        public void NamedValue_NullStore_ReturnsFalse()
        {
            var c = Condition.NamedValue("key", CompareOp.GT, 0);
            Assert.IsNull(c.Store);
            Assert.IsFalse(c.Evaluate(null, _state));
        }

        [TestMethod]
        public void NamedValue_MissingKey_ReturnsFalse()
        {
            var c = Condition.NamedValue("key", CompareOp.GT, 0);
            c.Store = new System.Collections.Concurrent.ConcurrentDictionary<string, float>();
            Assert.IsFalse(c.Evaluate(null, _state));
        }
    }

    // ---- Composite conditions ----

    [TestClass]
    public class CompositeConditionTests
    {
        ConditionState _state;

        [TestInitialize]
        public void Setup() => _state = new ConditionState();

        [TestMethod]
        public void And_TrueOnlyWhenBothTrue()
        {
            var src = new ConditionSource { value = 50 };

            var both = Condition.Field("value", CompareOp.GT, 0)
                .And(Condition.Field("value", CompareOp.LT, 100));
            Assert.IsTrue(both.Evaluate(src, _state));

            var leftFalse = Condition.Field("value", CompareOp.GT, 100)
                .And(Condition.Field("value", CompareOp.LT, 100));
            Assert.IsFalse(leftFalse.Evaluate(src, _state));

            var rightFalse = Condition.Field("value", CompareOp.GT, 0)
                .And(Condition.Field("value", CompareOp.LT, 10));
            Assert.IsFalse(rightFalse.Evaluate(src, _state));
        }

        [TestMethod]
        public void Or_TrueWhenEitherTrue()
        {
            var src = new ConditionSource { value = 50 };

            var leftTrue = Condition.Field("value", CompareOp.GT, 0)
                .Or(Condition.Field("value", CompareOp.GT, 100));
            Assert.IsTrue(leftTrue.Evaluate(src, _state));

            var rightTrue = Condition.Field("value", CompareOp.GT, 100)
                .Or(Condition.Field("value", CompareOp.LT, 100));
            Assert.IsTrue(rightTrue.Evaluate(src, _state));

            var neitherTrue = Condition.Field("value", CompareOp.GT, 100)
                .Or(Condition.Field("value", CompareOp.LT, 10));
            Assert.IsFalse(neitherTrue.Evaluate(src, _state));
        }

        [TestMethod]
        public void Not_InvertsResult()
        {
            var src = new ConditionSource { value = 50 };

            var notTrue = Condition.Field("value", CompareOp.GT, 0).Not();
            Assert.IsFalse(notTrue.Evaluate(src, _state));

            var notFalse = Condition.Field("value", CompareOp.GT, 100).Not();
            Assert.IsTrue(notFalse.Evaluate(src, _state));
        }
    }
}
