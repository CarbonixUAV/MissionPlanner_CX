using System.Threading;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class StatusTextConditionTests
    {
        ConditionState _state;

        [TestInitialize]
        public void Setup() => _state = new ConditionState();

        // ---- TryMatch ----

        [TestMethod]
        public void TryMatch_MatchingText_ReturnsTrue()
        {
            var stc = Condition.StatusText("engine stop");
            Assert.IsTrue(stc.TryMatch("engine stop detected", _state));
        }

        [TestMethod]
        public void TryMatch_NonMatchingText_ReturnsFalse()
        {
            var stc = Condition.StatusText("engine stop");
            Assert.IsFalse(stc.TryMatch("GPS lost", _state));
        }

        [TestMethod]
        public void TryMatch_CaseInsensitive()
        {
            var stc = Condition.StatusText("Engine Stop");
            Assert.IsTrue(stc.TryMatch("ENGINE STOP", _state));
        }

        // ---- Evaluate (fire-only) ----

        [TestMethod]
        public void Evaluate_NotFired_ReturnsFalse()
        {
            var stc = Condition.StatusText("engine stop");
            Assert.IsFalse(stc.Evaluate(null, _state));
        }

        [TestMethod]
        public void Evaluate_AfterMatch_ReturnsTrue()
        {
            var stc = Condition.StatusText("engine stop");
            stc.TryMatch("engine stop", _state);
            Assert.IsTrue(stc.Evaluate(null, _state));
        }

        [TestMethod]
        public void Evaluate_FireOnly_ExpiresAfterTimeout()
        {
            var stc = Condition.StatusText("engine stop");
            stc.TryMatch("engine stop", _state);
            Thread.Sleep(StatusTextCondition.DefaultTimeoutMs + 500);
            Assert.IsFalse(stc.Evaluate(null, _state));
        }

        // ---- Evaluate (sticky / fire + clear) ----

        [TestMethod]
        public void Evaluate_Sticky_DoesNotExpire()
        {
            var stc = Condition.StatusText("telemetry lost", "telemetry recovered");
            stc.TryMatch("telemetry lost", _state);
            Thread.Sleep(StatusTextCondition.DefaultTimeoutMs + 500);
            Assert.IsTrue(stc.Evaluate(null, _state), "Sticky condition should not expire");
        }

        [TestMethod]
        public void TryClear_MatchingClearText_ResetsState()
        {
            var stc = Condition.StatusText("telemetry lost", "telemetry recovered");
            stc.TryMatch("telemetry lost", _state);
            Assert.IsTrue(stc.Evaluate(null, _state));

            Assert.IsTrue(stc.TryClear("telemetry recovered", _state));
            Assert.IsFalse(stc.Evaluate(null, _state));
        }

        [TestMethod]
        public void TryClear_NonMatchingText_ReturnsFalse()
        {
            var stc = Condition.StatusText("telemetry lost", "telemetry recovered");
            stc.TryMatch("telemetry lost", _state);
            Assert.IsFalse(stc.TryClear("something else", _state));
            Assert.IsTrue(stc.Evaluate(null, _state));
        }

        [TestMethod]
        public void TryClear_FireOnly_ReturnsFalse()
        {
            var stc = Condition.StatusText("engine stop");
            stc.TryMatch("engine stop", _state);
            Assert.IsFalse(stc.TryClear("engine recovered", _state));
        }

        // ---- Evaluate (clear + timeout) ----

        [TestMethod]
        public void Evaluate_ClearAndTimeout_ExpiresOnTimeout()
        {
            var stc = Condition.StatusText("link lost", "link recovered",
                StatusTextCondition.DefaultTimeoutMs);
            stc.TryMatch("link lost", _state);
            Assert.IsTrue(stc.Evaluate(null, _state));

            Thread.Sleep(StatusTextCondition.DefaultTimeoutMs + 500);
            Assert.IsFalse(stc.Evaluate(null, _state), "Should expire via timeout");
        }

        [TestMethod]
        public void Evaluate_ClearAndTimeout_ClearsBeforeTimeout()
        {
            var stc = Condition.StatusText("link lost", "link recovered",
                StatusTextCondition.DefaultTimeoutMs);
            stc.TryMatch("link lost", _state);
            Assert.IsTrue(stc.Evaluate(null, _state));

            stc.TryClear("link recovered", _state);
            Assert.IsFalse(stc.Evaluate(null, _state), "Should clear via clear text");
        }

        [TestMethod]
        public void Evaluate_NoTimeoutNoClear_PermanentUntilReset()
        {
            var stc = Condition.StatusText("event", null);
            stc.TryMatch("event fired", _state);
            Thread.Sleep(StatusTextCondition.DefaultTimeoutMs + 500);
            Assert.IsTrue(stc.Evaluate(null, _state), "Should remain active without timeout");

            stc.Reset(_state);
            Assert.IsFalse(stc.Evaluate(null, _state));
        }

        // ---- Reset ----

        [TestMethod]
        public void Reset_ClearsFireOnlyState()
        {
            var stc = Condition.StatusText("engine stop");
            stc.TryMatch("engine stop", _state);
            stc.Reset(_state);
            Assert.IsFalse(stc.Evaluate(null, _state));
        }

        [TestMethod]
        public void Reset_ClearsStickyState()
        {
            var stc = Condition.StatusText("telemetry lost", "telemetry recovered");
            stc.TryMatch("telemetry lost", _state);
            stc.Reset(_state);
            Assert.IsFalse(stc.Evaluate(null, _state));
        }
    }
}
