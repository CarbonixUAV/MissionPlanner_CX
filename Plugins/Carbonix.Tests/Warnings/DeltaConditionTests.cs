using System.Collections.Concurrent;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class DeltaConditionTests
    {
        ConditionState _state;

        [TestInitialize]
        public void Setup() => _state = new ConditionState();

        // --- Both sides StateField ---

        [TestMethod]
        public void Evaluate_BothFields_DeltaAboveThreshold_ReturnsTrue()
        {
            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "sensors.gps", ValueSource.StateField,
                CompareOp.GT, 10);

            var src = new ConditionSource { value = 100 };
            src.sensors.gps = false; // 0

            Assert.IsTrue(cond.Evaluate(src, _state));
        }

        [TestMethod]
        public void Evaluate_BothFields_DeltaBelowThreshold_ReturnsFalse()
        {
            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "sensors.gps", ValueSource.StateField,
                CompareOp.GT, 200);

            var src = new ConditionSource { value = 100 };
            src.sensors.gps = false; // 0

            Assert.IsFalse(cond.Evaluate(src, _state));
        }

        [TestMethod]
        public void Evaluate_AbsoluteValue_OrderDoesNotMatter()
        {
            var cond = Condition.Delta(
                "sensors.gps", ValueSource.StateField,
                "value", ValueSource.StateField,
                CompareOp.GT, 10);

            // gps=0, value=100: |0 - 100| = 100 > 10
            var src = new ConditionSource { value = 100 };
            src.sensors.gps = false;

            Assert.IsTrue(cond.Evaluate(src, _state));
        }

        // --- Mixed: StateField + NamedValue ---

        [TestMethod]
        public void Evaluate_FieldAndNamedValue_ComparesCorrectly()
        {
            var store = new ConcurrentDictionary<string, float>();
            store["CHT2"] = 200f;

            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "CHT2", ValueSource.NamedValue,
                CompareOp.GT, 50);
            cond.RightStore = store;

            // |300 - 200| = 100 > 50
            Assert.IsTrue(cond.Evaluate(new ConditionSource { value = 300 }, _state));

            // |220 - 200| = 20 > 50 → false
            Assert.IsFalse(cond.Evaluate(new ConditionSource { value = 220 }, _state));
        }

        // --- Both sides NamedValue ---

        [TestMethod]
        public void Evaluate_BothNamedValues_ComparesCorrectly()
        {
            var store = new ConcurrentDictionary<string, float>();
            store["A"] = 100f;
            store["B"] = 180f;

            var cond = Condition.Delta(
                "A", ValueSource.NamedValue,
                "B", ValueSource.NamedValue,
                CompareOp.GT, 50);
            cond.LeftStore = store;
            cond.RightStore = store;

            // |100 - 180| = 80 > 50
            Assert.IsTrue(cond.Evaluate(new object(), _state));

            store["B"] = 120f;
            // |100 - 120| = 20 > 50 → false
            Assert.IsFalse(cond.Evaluate(new object(), _state));
        }

        // --- Missing values ---

        [TestMethod]
        public void Evaluate_LeftMissing_ReturnsFalse()
        {
            var store = new ConcurrentDictionary<string, float>();

            var cond = Condition.Delta(
                "missing", ValueSource.NamedValue,
                "value", ValueSource.StateField,
                CompareOp.GT, 0);
            cond.LeftStore = store;

            Assert.IsFalse(cond.Evaluate(new ConditionSource { value = 100 }, _state));
        }

        [TestMethod]
        public void Evaluate_RightMissing_ReturnsFalse()
        {
            var store = new ConcurrentDictionary<string, float>();

            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "missing", ValueSource.NamedValue,
                CompareOp.GT, 0);
            cond.RightStore = store;

            Assert.IsFalse(cond.Evaluate(new ConditionSource { value = 100 }, _state));
        }

        [TestMethod]
        public void Evaluate_NullStore_ReturnsFalse()
        {
            var cond = Condition.Delta(
                "A", ValueSource.NamedValue,
                "B", ValueSource.NamedValue,
                CompareOp.GT, 0);

            Assert.IsFalse(cond.Evaluate(new object(), _state));
        }

        // --- Hysteresis ---

        [TestMethod]
        public void Evaluate_Hysteresis_FiresAndClears()
        {
            var store = new ConcurrentDictionary<string, float>();
            store["B"] = 0f;

            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "B", ValueSource.NamedValue,
                CompareOp.GT, 50, clear: 30);
            cond.RightStore = store;

            var src = new ConditionSource { value = 20 };
            Assert.IsFalse(cond.Evaluate(src, _state), "Delta 20, below threshold");

            src.value = 60;
            Assert.IsTrue(cond.Evaluate(src, _state), "Delta 60, above fire threshold");

            src.value = 40;
            Assert.IsTrue(cond.Evaluate(src, _state), "Delta 40, between thresholds — stays fired");

            src.value = 25;
            Assert.IsFalse(cond.Evaluate(src, _state), "Delta 25, below clear threshold");
        }

        // --- All operators ---

        [TestMethod]
        public void Evaluate_LT_TrueWhenDeltaBelow()
        {
            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "sensors.gps", ValueSource.StateField,
                CompareOp.LT, 50);

            // |20 - 0| = 20 < 50
            Assert.IsTrue(cond.Evaluate(new ConditionSource { value = 20 }, _state));
            // |60 - 0| = 60 < 50 → false
            Assert.IsFalse(cond.Evaluate(new ConditionSource { value = 60 }, _state));
        }

        [TestMethod]
        public void Evaluate_GTEQ_TrueWhenDeltaAtOrAbove()
        {
            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "sensors.gps", ValueSource.StateField,
                CompareOp.GTEQ, 50);

            Assert.IsTrue(cond.Evaluate(new ConditionSource { value = 50 }, _state));
            Assert.IsTrue(cond.Evaluate(new ConditionSource { value = 60 }, _state));
            Assert.IsFalse(cond.Evaluate(new ConditionSource { value = 49 }, _state));
        }

        // --- Engine binding ---

        [TestMethod]
        public void Engine_BindsNamedValueStores()
        {
            var cond = Condition.Delta(
                "value", ValueSource.StateField,
                "NV1", ValueSource.NamedValue,
                CompareOp.GT, 10);

            var rules = new System.Collections.Generic.List<WarningRule>
            {
                new WarningRule("test", "Test", WarningSeverity.Caution,
                    WarningSubsystem.Engine, trigger: cond)
            };

            var engine = new CarbonixWarningEngine(
                new System.Collections.Generic.Dictionary<string, ICondition>(),
                rules);

            Assert.AreSame(engine.NamedValues, cond.RightStore,
                "Engine should bind right named value store");
            Assert.IsNull(cond.LeftStore,
                "Left side is StateField — store should remain null");
        }

        // --- Serialization ---

        [TestMethod]
        public void Serialize_Delta_ProducesExpectedJson()
        {
            var cond = Condition.Delta(
                "efi_headtemp", ValueSource.StateField,
                "CHT2", ValueSource.NamedValue,
                CompareOp.GT, 50);

            var json = WarningSerializer.SerializeCondition(cond);
            var obj = JObject.Parse(json);

            var delta = obj["delta"] as JObject;
            Assert.IsNotNull(delta);
            Assert.AreEqual("efi_headtemp", (string)delta["left"]["stateField"]);
            Assert.AreEqual("CHT2", (string)delta["right"]["namedValue"]);
            Assert.AreEqual(">", (string)obj["op"]);
            Assert.AreEqual(50.0, (double)obj["value"]);
            Assert.IsNull(obj["clear"]);
        }

        [TestMethod]
        public void Serialize_Delta_WithClear_IncludesClearValue()
        {
            var cond = Condition.Delta(
                "efi_headtemp", ValueSource.StateField,
                "CHT2", ValueSource.NamedValue,
                CompareOp.GT, 50, clear: 30);

            var json = WarningSerializer.SerializeCondition(cond);
            var obj = JObject.Parse(json);

            Assert.AreEqual(30.0, (double)obj["clear"]);
        }

        [TestMethod]
        public void Deserialize_Delta_ParsesCorrectly()
        {
            var json = @"{
                ""delta"": {
                    ""left"": { ""stateField"": ""efi_headtemp"" },
                    ""right"": { ""namedValue"": ""CHT2"" }
                },
                ""op"": "">"",
                ""value"": 50
            }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(DeltaCondition));
            var dc = (DeltaCondition)result;
            Assert.AreEqual("efi_headtemp", dc.LeftName);
            Assert.AreEqual(ValueSource.StateField, dc.LeftSource);
            Assert.AreEqual("CHT2", dc.RightName);
            Assert.AreEqual(ValueSource.NamedValue, dc.RightSource);
            Assert.AreEqual(CompareOp.GT, dc.Op);
            Assert.AreEqual(50.0, dc.Threshold);
            Assert.IsNull(dc.ClearThreshold);
        }

        [TestMethod]
        public void Deserialize_Delta_WithClear()
        {
            var json = @"{
                ""delta"": {
                    ""left"": { ""stateField"": ""a"" },
                    ""right"": { ""stateField"": ""b"" }
                },
                ""op"": "">"",
                ""value"": 50,
                ""clear"": 30
            }";

            var result = WarningSerializer.DeserializeCondition(json);

            var dc = (DeltaCondition)result;
            Assert.AreEqual(50.0, dc.Threshold);
            Assert.AreEqual(30.0, dc.ClearThreshold);
        }

        [TestMethod]
        public void RoundTrip_Delta_FieldAndNamedValue()
        {
            var original = Condition.Delta(
                "efi_headtemp", ValueSource.StateField,
                "CHT2", ValueSource.NamedValue,
                CompareOp.GT, 50);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_Delta_BothFields()
        {
            var original = Condition.Delta(
                "a", ValueSource.StateField,
                "b", ValueSource.StateField,
                CompareOp.GTEQ, 25);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_Delta_BothNamedValues()
        {
            var original = Condition.Delta(
                "A", ValueSource.NamedValue,
                "B", ValueSource.NamedValue,
                CompareOp.LT, 10);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_Delta_WithClear()
        {
            var original = Condition.Delta(
                "efi_headtemp", ValueSource.StateField,
                "CHT2", ValueSource.NamedValue,
                CompareOp.GT, 50, clear: 30);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_Delta_InComposite()
        {
            var original = Condition.Delta(
                    "a", ValueSource.StateField,
                    "b", ValueSource.NamedValue,
                    CompareOp.GT, 50)
                .And(Condition.Field("value", CompareOp.GT, 0));
            AssertRoundTrip(original);
        }

        static void AssertRoundTrip(ICondition original)
        {
            var json = WarningSerializer.SerializeCondition(original);
            var deserialized = WarningSerializer.DeserializeCondition(json);
            ConditionAssert.AreStructurallyEqual(original, deserialized);
        }
    }
}
