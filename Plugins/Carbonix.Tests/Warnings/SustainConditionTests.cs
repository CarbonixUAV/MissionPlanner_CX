using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class SustainConditionTests
    {
        ConditionState _state;
        ICondition _inner;
        ConditionSource _src;

        // N steps of 250ms to reach riseMs. With riseMs=1000 → N=4.
        const int RiseMs = 1000;
        const int FallMs = 1000;
        const int StepsToTrip = RiseMs / 250;  // = 4
        const int StepsToClear = FallMs / 250; // = 4

        [TestInitialize]
        public void Setup()
        {
            _state = new ConditionState();
            _inner = Condition.Field("value", CompareOp.GT, 0);
            _src = new ConditionSource { value = 0 };
        }

        // ---- Immediate edges (0 ms) ----

        [TestMethod]
        public void Evaluate_ZeroRise_TripsOnFirstTrue()
        {
            var sustain = _inner.Sustain(riseMs: 0, fallMs: FallMs);
            _src.value = 1;
            Assert.IsTrue(sustain.Evaluate(_src, _state));
        }

        [TestMethod]
        public void Evaluate_ZeroFall_ClearsOnFirstFalse()
        {
            var sustain = _inner.Sustain(riseMs: 0, fallMs: 0);
            _src.value = 1;
            sustain.Evaluate(_src, _state); // trip
            _src.value = 0;
            Assert.IsFalse(sustain.Evaluate(_src, _state));
        }

        [TestMethod]
        public void Evaluate_ZeroBoth_PassesThrough()
        {
            var sustain = _inner.Sustain(riseMs: 0, fallMs: 0);

            _src.value = 1;
            Assert.IsTrue(sustain.Evaluate(_src, _state));

            _src.value = 0;
            Assert.IsFalse(sustain.Evaluate(_src, _state));
        }

        // ---- Rise behaviour ----

        [TestMethod]
        public void Evaluate_BriefBlip_DoesNotTrip()
        {
            var sustain = _inner.Sustain(RiseMs, FallMs);

            _src.value = 1;
            sustain.Evaluate(_src, _state); // single blip
            _src.value = 0;

            for (int i = 0; i < StepsToTrip * 4; i++)
                Assert.IsFalse(sustain.Evaluate(_src, _state),
                    $"Should not trip after single blip (step {i})");
        }

        [TestMethod]
        public void Evaluate_SustainedTrue_TripsAfterRiseMs()
        {
            var sustain = _inner.Sustain(RiseMs, FallMs);
            _src.value = 1;

            bool tripped = false;
            for (int i = 0; i < StepsToTrip * 2; i++)
            {
                if (sustain.Evaluate(_src, _state))
                {
                    tripped = true;
                    break;
                }
            }

            Assert.IsTrue(tripped, "Should trip within 2x riseMs of sustained true");
        }

        [TestMethod]
        public void Evaluate_NotTrippedBeforeRiseMs()
        {
            var sustain = _inner.Sustain(RiseMs, FallMs);
            _src.value = 1;

            // N-1 steps should not trip
            for (int i = 0; i < StepsToTrip - 1; i++)
                Assert.IsFalse(sustain.Evaluate(_src, _state),
                    $"Should not trip before riseMs (step {i})");
        }

        // ---- Fall behaviour ----

        [TestMethod]
        public void Evaluate_SustainedFalse_ClearsAfterFallMs()
        {
            var sustain = _inner.Sustain(riseMs: 0, fallMs: FallMs);
            _src.value = 1;
            sustain.Evaluate(_src, _state); // trip immediately

            _src.value = 0;
            bool cleared = false;
            for (int i = 0; i < StepsToClear * 2; i++)
            {
                if (!sustain.Evaluate(_src, _state))
                {
                    cleared = true;
                    break;
                }
            }

            Assert.IsTrue(cleared, "Should clear within 2x fallMs of sustained false");
        }

        // ---- Hysteresis ----

        [TestMethod]
        public void Evaluate_HoldsTrippedUntilClearThreshold()
        {
            // Trip with riseMs=0, then apply slow decay (long fallMs)
            // and verify it stays tripped for several ticks.
            var sustain = _inner.Sustain(riseMs: 0, fallMs: 4000);
            _src.value = 1;
            sustain.Evaluate(_src, _state); // trip

            // First few ticks of false should still be tripped (above clear threshold)
            _src.value = 0;
            Assert.IsTrue(sustain.Evaluate(_src, _state), "Should stay tripped while V > 0.2");
        }

        // ---- Reset ----

        [TestMethod]
        public void Evaluate_ResetWhileTripped_ClearsImmediately()
        {
            // sensors.gps is a bool field; true converts to 1.0 via dotted resolver
            var resetCond = Condition.Field("sensors.gps", CompareOp.GT, 0);
            var sustain = _inner.Sustain(riseMs: 0, fallMs: FallMs, reset: resetCond);
            var src = new ConditionSource { value = 1 };

            sustain.Evaluate(src, _state); // trip (gps=false → reset not firing)
            Assert.IsTrue(sustain.Evaluate(src, _state), "Should be tripped");

            src.sensors.gps = true; // reset fires
            Assert.IsFalse(sustain.Evaluate(src, _state), "Reset should clear immediately");
        }

        [TestMethod]
        public void Evaluate_ResetClearsAccumulatedV()
        {
            var resetCond = Condition.Field("sensors.gps", CompareOp.GT, 0);
            var sustain = _inner.Sustain(RiseMs, FallMs, reset: resetCond);
            var src = new ConditionSource { value = 1 };

            // Partially accumulate
            for (int i = 0; i < StepsToTrip - 1; i++)
                sustain.Evaluate(src, _state);

            // Reset — V should snap to 0
            src.sensors.gps = true;
            Assert.IsFalse(sustain.Evaluate(src, _state), "Reset returns false");

            // Release reset, apply full riseMs — should trip again from scratch
            src.sensors.gps = false;
            bool tripped = false;
            for (int i = 0; i < StepsToTrip * 2; i++)
            {
                if (sustain.Evaluate(src, _state)) { tripped = true; break; }
            }
            Assert.IsTrue(tripped, "Should trip again after reset clears V");
        }

        [TestMethod]
        public void Serialize_RoundTrip_WithReset_PreservesReset()
        {
            var reset = Condition.Field("sensors.gps", CompareOp.GT, 0);
            var original = _inner.Sustain(RiseMs, FallMs, reset: reset);

            var json = WarningSerializer.SerializeCondition(original);
            var restored = WarningSerializer.DeserializeCondition(json);

            ConditionAssert.AreStructurallyEqual(original, restored);
        }

        [TestMethod]
        public void Serialize_WithReset_ProducesResetKey()
        {
            var reset = Condition.Field("sensors.gps", CompareOp.GT, 0);
            var sustain = _inner.Sustain(RiseMs, FallMs, reset: reset);

            var json = WarningSerializer.SerializeCondition(sustain);
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.IsNotNull(obj["reset"], "Should have 'reset' key");
        }

        // ---- Serialization ----

        [TestMethod]
        public void Serialize_RoundTrip_PreservesParameters()
        {
            var original = _inner.Sustain(RiseMs, FallMs);

            var json = WarningSerializer.SerializeCondition(original);
            var restored = WarningSerializer.DeserializeCondition(json);

            ConditionAssert.AreStructurallyEqual(original, restored);
        }

        [TestMethod]
        public void Serialize_ProducesExpectedJson()
        {
            var sustain = _inner.Sustain(2000, 5000);
            var json = WarningSerializer.SerializeCondition(sustain);
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.IsNotNull(obj["sustain"], "Should have 'sustain' key");
            Assert.AreEqual(2000, (int)obj["riseMs"]);
            Assert.AreEqual(5000, (int)obj["fallMs"]);
        }
    }
}
