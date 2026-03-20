using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class EdgeConditionTests
    {
        ConditionState _state;

        [TestInitialize]
        public void Setup() => _state = new ConditionState();

        [TestMethod]
        public void Evaluate_InitialTrue_Suppressed()
        {
            var edge = Condition.Edge(Condition.Field("value", CompareOp.GT, 0));
            var src = new ConditionSource { value = 1 };

            Assert.IsFalse(edge.Evaluate(src, _state), "First eval seeds state, no edge");
            Assert.IsFalse(edge.Evaluate(src, _state), "Stays suppressed while true");

            src.value = 0;
            edge.Evaluate(src, _state); // drop to false

            src.value = 1;
            Assert.IsTrue(edge.Evaluate(src, _state), "Real rising edge fires");
        }

        [TestMethod]
        public void Evaluate_StaysTrue_ReturnsFalseAfterFirst()
        {
            var edge = Condition.Edge(Condition.Field("value", CompareOp.GT, 0));
            var src = new ConditionSource { value = 1 };

            edge.Evaluate(src, _state); // seeds state
            Assert.IsFalse(edge.Evaluate(src, _state), "No edge when inner stays true");
        }

        [TestMethod]
        public void Evaluate_FalseToTrue_ReturnsTrue()
        {
            var edge = Condition.Edge(Condition.Field("value", CompareOp.GT, 10));
            var src = new ConditionSource { value = 5 };

            Assert.IsFalse(edge.Evaluate(src, _state), "Inner is false, no edge");

            src.value = 15;
            Assert.IsTrue(edge.Evaluate(src, _state), "Rising edge on transition");
            Assert.IsFalse(edge.Evaluate(src, _state), "No edge on sustained true");
        }

        [TestMethod]
        public void Evaluate_RepeatedCycles()
        {
            var edge = Condition.Edge(Condition.Field("value", CompareOp.GT, 10));
            var src = new ConditionSource { value = 5 };

            edge.Evaluate(src, _state); // false

            src.value = 15;
            Assert.IsTrue(edge.Evaluate(src, _state), "First rising edge");

            src.value = 5;
            Assert.IsFalse(edge.Evaluate(src, _state), "Falling — no edge");

            src.value = 15;
            Assert.IsTrue(edge.Evaluate(src, _state), "Second rising edge");
        }

        [TestMethod]
        public void Evaluate_StatusText_EdgeOnMatch()
        {
            var stc = Condition.StatusText("TRANSITION STARTED");
            var edge = Condition.Edge(stc);

            Assert.IsFalse(edge.Evaluate(null, _state), "No match yet");

            stc.TryMatch("TRANSITION STARTED", _state);
            Assert.IsTrue(edge.Evaluate(null, _state), "Rising edge on match");
            Assert.IsFalse(edge.Evaluate(null, _state), "No edge while still active");
        }
    }

    [TestClass]
    public class LatchConditionTests
    {
        ConditionState _state;

        [TestInitialize]
        public void Setup() => _state = new ConditionState();

        // ---- Level-triggered basics ----

        [TestMethod]
        public void Evaluate_InitialState_ReturnsFalse()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var src = new ConditionSource { value = 75 };

            Assert.IsFalse(latch.Evaluate(src, _state), "Neither input high, starts false");
        }

        [TestMethod]
        public void Evaluate_SetHigh_LatchesTrue()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var src = new ConditionSource { value = 101 };

            Assert.IsTrue(latch.Evaluate(src, _state), "Set high should latch true");
        }

        [TestMethod]
        public void Evaluate_SetDrops_HoldsTrue()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var src = new ConditionSource { value = 101 };

            latch.Evaluate(src, _state); // latched true

            src.value = 75; // both inputs low
            Assert.IsTrue(latch.Evaluate(src, _state), "Should hold when both inputs drop");
        }

        [TestMethod]
        public void Evaluate_ClearHigh_LatchesFalse()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var src = new ConditionSource { value = 101 };

            latch.Evaluate(src, _state); // latched true

            src.value = 49; // clear goes high
            Assert.IsFalse(latch.Evaluate(src, _state), "Clear high should unlatch");
        }

        [TestMethod]
        public void Evaluate_ClearDrops_HoldsFalse()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var src = new ConditionSource { value = 101 };

            latch.Evaluate(src, _state); // true
            src.value = 49;
            latch.Evaluate(src, _state); // false

            src.value = 75; // both inputs low
            Assert.IsFalse(latch.Evaluate(src, _state), "Should hold false when both drop");
        }

        // ---- Priority ----

        [TestMethod]
        public void Evaluate_BothHigh_ClearWins()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 0),
                clear: Condition.Field("value", CompareOp.GT, 0));
            var src = new ConditionSource { value = 1 };

            Assert.IsFalse(latch.Evaluate(src, _state), "When both high, clear wins");
        }

        // ---- Full cycle ----

        [TestMethod]
        public void Evaluate_FullSetClearCycle()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var src = new ConditionSource { value = 75 };

            Assert.IsFalse(latch.Evaluate(src, _state), "Initial: both low");

            src.value = 101;
            Assert.IsTrue(latch.Evaluate(src, _state), "Set goes high");

            src.value = 75;
            Assert.IsTrue(latch.Evaluate(src, _state), "Both low: holds true");

            src.value = 49;
            Assert.IsFalse(latch.Evaluate(src, _state), "Clear goes high");

            src.value = 75;
            Assert.IsFalse(latch.Evaluate(src, _state), "Both low: holds false");

            src.value = 101;
            Assert.IsTrue(latch.Evaluate(src, _state), "Re-set");
        }

        // ---- Edge-triggered via composition ----

        [TestMethod]
        public void Evaluate_EdgeTriggered_LatchOnEdgeClearOnLevel()
        {
            // Latch on rising edge of armed, clear while airspeed > 20
            var latch = Condition.Latch(
                set: Condition.Edge(Condition.Field("value", CompareOp.GT, 0)),
                clear: Condition.Field("value", CompareOp.GT, 20));
            var src = new ConditionSource { value = 0 };

            latch.Evaluate(src, _state); // baseline

            src.value = 1; // "arm" — edge fires, set goes high for one cycle
            Assert.IsTrue(latch.Evaluate(src, _state), "Latches on edge");

            src.value = 5; // still "armed" but edge has passed, set is low
            Assert.IsTrue(latch.Evaluate(src, _state), "Holds true, clear not high");

            src.value = 25; // "airspeed above 20" — clear goes high
            Assert.IsFalse(latch.Evaluate(src, _state), "Clears on level");

            src.value = 15; // airspeed drops, clear goes low
            Assert.IsFalse(latch.Evaluate(src, _state), "Holds false, no new set edge");
        }

        [TestMethod]
        public void Evaluate_EdgeTriggered_BothSides()
        {
            var setStc = Condition.StatusText("TRANSITION STARTED");
            var clearStc = Condition.StatusText("TRANSITION DONE");
            var latch = Condition.Latch(
                set: Condition.Edge(setStc),
                clear: Condition.Edge(clearStc));

            latch.Evaluate(null, _state); // baseline

            setStc.TryMatch("TRANSITION STARTED", _state);
            Assert.IsTrue(latch.Evaluate(null, _state), "Latches on set edge");
            Assert.IsTrue(latch.Evaluate(null, _state), "Holds after edge passes");

            clearStc.TryMatch("TRANSITION DONE", _state);
            Assert.IsFalse(latch.Evaluate(null, _state), "Clears on clear edge");
            Assert.IsFalse(latch.Evaluate(null, _state), "Holds false after edge passes");
        }

        // ---- StatusText set, field clear ----

        [TestMethod]
        public void Evaluate_StatusTextSet_FieldClear()
        {
            var setCondition = Condition.StatusText("TRANSITION STARTED");
            var latch = Condition.Latch(
                set: setCondition,
                clear: Condition.Field("value", CompareOp.EQ, 0));
            var src = new ConditionSource { value = 1 };

            latch.Evaluate(src, _state); // baseline

            setCondition.TryMatch("TRANSITION STARTED", _state);
            Assert.IsTrue(latch.Evaluate(src, _state), "Latches on StatusText");

            // StatusText still active (within timeout), but latch holds
            // regardless of whether set stays high or drops
            Assert.IsTrue(latch.Evaluate(src, _state), "Holds true");

            src.value = 0; // clear goes high
            Assert.IsFalse(latch.Evaluate(src, _state), "Clears on field condition");
        }

        // ---- Nested in composition ----

        [TestMethod]
        public void Evaluate_LatchComposedWithNot()
        {
            var latch = Condition.Latch(
                set: Condition.Field("value", CompareOp.GT, 100),
                clear: Condition.Field("value", CompareOp.LT, 50));
            var notLatch = latch.Not();
            var src = new ConditionSource { value = 75 };

            Assert.IsTrue(notLatch.Evaluate(src, _state), "Not(false) = true");

            src.value = 101;
            Assert.IsFalse(notLatch.Evaluate(src, _state), "Not(true) = false");
        }
    }
}
