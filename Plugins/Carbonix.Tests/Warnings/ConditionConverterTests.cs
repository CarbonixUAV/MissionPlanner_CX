using System.Collections.Generic;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class ConditionConverterTests
    {
        // -- Serialize direction (A → B): assert known C# → expected JSON --

        [TestMethod]
        public void Serialize_FieldCondition_ProducesExpectedJson()
        {
            var condition = Condition.Field("satcount", CompareOp.LT, 20);

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("satcount", (string)obj["stateField"]);
            Assert.AreEqual("<", (string)obj["op"]);
            Assert.AreEqual(20.0, (double)obj["value"]);
            Assert.IsNull(obj["clear"]);
        }

        [TestMethod]
        public void Serialize_FieldCondition_WithClear_IncludesClearValue()
        {
            var condition = Condition.Field("satcount", CompareOp.LT, 18, clear: 22);

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("satcount", (string)obj["stateField"]);
            Assert.AreEqual("<", (string)obj["op"]);
            Assert.AreEqual(18.0, (double)obj["value"]);
            Assert.AreEqual(22.0, (double)obj["clear"]);
        }

        [TestMethod]
        public void Serialize_And_ProducesArray()
        {
            var condition = Condition.Field("a", CompareOp.GT, 1)
                .And(Condition.Field("b", CompareOp.LT, 2));

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            var arr = obj["and"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual(2, arr.Count);
            Assert.AreEqual("a", (string)arr[0]["stateField"]);
            Assert.AreEqual("b", (string)arr[1]["stateField"]);
        }

        [TestMethod]
        public void Serialize_NestedAnd_FlattensToSingleArray()
        {
            // And(And(a, b), c) should flatten to [a, b, c]
            var condition = Condition.Field("a", CompareOp.GT, 1)
                .And(Condition.Field("b", CompareOp.LT, 2))
                .And(Condition.Field("c", CompareOp.EQ, 3));

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            var arr = obj["and"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual("a", (string)arr[0]["stateField"]);
            Assert.AreEqual("b", (string)arr[1]["stateField"]);
            Assert.AreEqual("c", (string)arr[2]["stateField"]);
        }

        [TestMethod]
        public void Serialize_NestedOr_FlattensToSingleArray()
        {
            var condition = Condition.Field("a", CompareOp.GT, 1)
                .Or(Condition.Field("b", CompareOp.LT, 2))
                .Or(Condition.Field("c", CompareOp.EQ, 3));

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            var arr = obj["or"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
        }

        [TestMethod]
        public void Serialize_Not_WrapsInner()
        {
            var condition = Condition.Field("x", CompareOp.EQ, 0).Not();

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.IsNotNull(obj["not"]);
            Assert.AreEqual("x", (string)obj["not"]["stateField"]);
        }

        [TestMethod]
        public void Serialize_AllOperators()
        {
            var ops = new[]
            {
                (CompareOp.LT, "<"),
                (CompareOp.LTEQ, "<="),
                (CompareOp.EQ, "=="),
                (CompareOp.GT, ">"),
                (CompareOp.GTEQ, ">="),
                (CompareOp.NEQ, "!="),
            };

            foreach (var (op, symbol) in ops)
            {
                var json = WarningSerializer.SerializeCondition(
                    Condition.Field("x", op, 1));
                var obj = JObject.Parse(json);
                Assert.AreEqual(symbol, (string)obj["op"],
                    $"Operator {op} should serialize to \"{symbol}\"");
            }
        }

        // -- Deserialize direction (B → C): assert known JSON → expected structure --

        [TestMethod]
        public void Deserialize_FieldCondition_ParsesCorrectly()
        {
            var json = @"{ ""stateField"": ""rpm"", ""op"": ""<"", ""value"": 500 }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(CompareCondition));
            var cc = (CompareCondition)result;
            Assert.AreEqual(ValueSource.StateField, cc.ValueSource);
            Assert.AreEqual("rpm", cc.Name);
            Assert.AreEqual(CompareOp.LT, cc.Op);
            Assert.AreEqual(500.0, cc.Threshold);
            Assert.IsNull(cc.ClearThreshold);
        }

        [TestMethod]
        public void Deserialize_FieldCondition_WithClear()
        {
            var json = @"{ ""stateField"": ""satcount"", ""op"": ""<"", ""value"": 18, ""clear"": 22 }";

            var result = WarningSerializer.DeserializeCondition(json);

            var cc = (CompareCondition)result;
            Assert.AreEqual(18.0, cc.Threshold);
            Assert.AreEqual(22.0, cc.ClearThreshold);
        }

        [TestMethod]
        public void Deserialize_And_BuildsBinaryTree()
        {
            var json = @"{ ""and"": [
                { ""stateField"": ""a"", ""op"": "">"", ""value"": 1 },
                { ""stateField"": ""b"", ""op"": ""<"", ""value"": 2 },
                { ""stateField"": ""c"", ""op"": ""=="", ""value"": 3 }
            ]}";

            var result = WarningSerializer.DeserializeCondition(json);

            // 3-element array folds into And(And(a, b), c)
            Assert.IsInstanceOfType(result, typeof(AndCondition));
            var outer = (AndCondition)result;
            Assert.IsInstanceOfType(outer.Left, typeof(AndCondition));
            Assert.IsInstanceOfType(outer.Right, typeof(CompareCondition));
            Assert.AreEqual("c", ((CompareCondition)outer.Right).Name);

            var inner = (AndCondition)outer.Left;
            Assert.AreEqual("a", ((CompareCondition)inner.Left).Name);
            Assert.AreEqual("b", ((CompareCondition)inner.Right).Name);
        }

        [TestMethod]
        public void Deserialize_Not_WrapsInner()
        {
            var json = @"{ ""not"": { ""stateField"": ""armed"", ""op"": "">"", ""value"": 0 } }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(NotCondition));
            var not = (NotCondition)result;
            Assert.IsInstanceOfType(not.Inner, typeof(CompareCondition));
            Assert.AreEqual("armed", ((CompareCondition)not.Inner).Name);
        }

        [TestMethod]
        public void Deserialize_UnknownKey_ThrowsWithMessage()
        {
            var json = @"{ ""bogus"": 42 }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(json));
            StringAssert.Contains(ex.Message, "bogus");
        }

        [TestMethod]
        public void Deserialize_And_LessThanTwoElements_Throws()
        {
            var json = @"{ ""and"": [ { ""stateField"": ""x"", ""op"": "">"", ""value"": 0 } ] }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(json));
            StringAssert.Contains(ex.Message, "at least 2");
        }

        [TestMethod]
        public void Deserialize_Or_EmptyArray_Throws()
        {
            var json = @"{ ""or"": [] }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(json));
            StringAssert.Contains(ex.Message, "at least 2");
        }

        [TestMethod]
        public void Deserialize_UnknownOperator_ThrowsWithMessage()
        {
            var json = @"{ ""stateField"": ""x"", ""op"": ""~="", ""value"": 1 }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(json));
            StringAssert.Contains(ex.Message, "~=");
        }

        // -- Round-trip (A → B → C): structural comparison --

        [TestMethod]
        public void RoundTrip_SimpleField()
        {
            var original = Condition.Field("rpm", CompareOp.LT, 500);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_FieldWithClear()
        {
            var original = Condition.Field("satcount", CompareOp.LT, 18, clear: 22);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_ComplexTree()
        {
            // Not(Or(And(field, field_with_clear), field))
            var original =
                Condition.Field("a", CompareOp.GT, 100, clear: 90)
                    .And(Condition.Field("b", CompareOp.LTEQ, 50))
                    .Or(Condition.Field("c", CompareOp.NEQ, 0))
                    .Not();

            AssertRoundTrip(original);
        }

        // -- NamedValueCondition --

        [TestMethod]
        public void Serialize_NamedValueCondition_ProducesExpectedJson()
        {
            var condition = Condition.NamedValue("VTOLState", CompareOp.GT, 0);

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("VTOLState", (string)obj["namedValue"]);
            Assert.AreEqual(">", (string)obj["op"]);
            Assert.AreEqual(0.0, (double)obj["value"]);
        }

        [TestMethod]
        public void Deserialize_NamedValueCondition_ParsesCorrectly()
        {
            var json = @"{ ""namedValue"": ""VTOLState"", ""op"": "">"", ""value"": 0 }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(CompareCondition));
            var cc = (CompareCondition)result;
            Assert.AreEqual(ValueSource.NamedValue, cc.ValueSource);
            Assert.AreEqual("VTOLState", cc.Name);
            Assert.AreEqual(CompareOp.GT, cc.Op);
            Assert.AreEqual(0.0, cc.Threshold);
            Assert.IsNull(cc.Store); // Store not bound during deserialization
        }

        [TestMethod]
        public void RoundTrip_NamedValueCondition()
        {
            var original = Condition.NamedValue("VTOLState", CompareOp.GT, 0);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_NamedValueInComposite()
        {
            var original = Condition.NamedValue("VTOLState", CompareOp.GT, 0)
                .And(Condition.Field("armed", CompareOp.GT, 0));
            AssertRoundTrip(original);
        }

        // -- StatusTextCondition --

        [TestMethod]
        public void Serialize_StatusText_FireOnly_ProducesExpectedJson()
        {
            var condition = Condition.StatusText("engine stop");

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("engine stop", (string)obj["statusText"]);
            Assert.IsNull(obj["clearText"]);
            Assert.AreEqual(StatusTextCondition.DefaultTimeoutMs, (int)obj["timeoutMs"]);
        }

        [TestMethod]
        public void Serialize_StatusText_WithClear_IncludesClearText()
        {
            var condition = Condition.StatusText("telemetry lost", "telemetry recovered");

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("telemetry lost", (string)obj["statusText"]);
            Assert.AreEqual("telemetry recovered", (string)obj["clearText"]);
            Assert.IsNull(obj["timeoutMs"]);
        }

        [TestMethod]
        public void Serialize_StatusText_WithClearAndTimeout()
        {
            var condition = Condition.StatusText("telemetry lost", "telemetry recovered", 30000);

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("telemetry lost", (string)obj["statusText"]);
            Assert.AreEqual("telemetry recovered", (string)obj["clearText"]);
            Assert.AreEqual(30000, (int)obj["timeoutMs"]);
        }

        [TestMethod]
        public void Deserialize_StatusText_FireOnly_ParsesCorrectly()
        {
            var json = @"{ ""statusText"": ""engine stop"" }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(StatusTextCondition));
            var stc = (StatusTextCondition)result;
            Assert.AreEqual("engine stop", stc.FirePattern.ToString());
            Assert.IsNull(stc.ClearPattern);
            Assert.IsNull(stc.TimeoutMs);
        }

        [TestMethod]
        public void Deserialize_StatusText_WithTimeoutMs()
        {
            var json = @"{ ""statusText"": ""engine stop"", ""timeoutMs"": 10000 }";

            var result = WarningSerializer.DeserializeCondition(json);

            var stc = (StatusTextCondition)result;
            Assert.AreEqual(10000, stc.TimeoutMs);
        }

        [TestMethod]
        public void Deserialize_StatusText_WithClear_ParsesCorrectly()
        {
            var json = @"{ ""statusText"": ""telemetry lost"", ""clearText"": ""telemetry recovered"" }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(StatusTextCondition));
            var stc = (StatusTextCondition)result;
            Assert.AreEqual("telemetry lost", stc.FirePattern.ToString());
            Assert.AreEqual("telemetry recovered", stc.ClearPattern.ToString());
            Assert.IsNull(stc.TimeoutMs);
        }

        [TestMethod]
        public void RoundTrip_StatusText_FireOnly()
        {
            var original = Condition.StatusText("engine stop");
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_StatusText_WithClear()
        {
            var original = Condition.StatusText("telemetry lost", "telemetry recovered");
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_StatusText_WithClearAndTimeout()
        {
            var original = Condition.StatusText("lost", "recovered", 30000);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_StatusTextInComposite()
        {
            var original = Condition.NamedValue("VTOLState", CompareOp.GT, 0)
                .Or(Condition.StatusText("QASSIST"));
            AssertRoundTrip(original);
        }

        // -- EdgeCondition --

        [TestMethod]
        public void Serialize_Edge_WrapsInner()
        {
            var condition = Condition.Edge(Condition.Field("armed", CompareOp.GT, 0));

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.IsNotNull(obj["edge"]);
            Assert.AreEqual("armed", (string)obj["edge"]["stateField"]);
        }

        [TestMethod]
        public void Deserialize_Edge_ParsesCorrectly()
        {
            var json = @"{ ""edge"": { ""stateField"": ""armed"", ""op"": "">"", ""value"": 0 } }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(EdgeCondition));
            var edge = (EdgeCondition)result;
            Assert.IsInstanceOfType(edge.Inner, typeof(CompareCondition));
            Assert.AreEqual("armed", ((CompareCondition)edge.Inner).Name);
        }

        [TestMethod]
        public void RoundTrip_Edge()
        {
            var original = Condition.Edge(Condition.Field("armed", CompareOp.GT, 0));
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_EdgeOfStatusText()
        {
            var original = Condition.Edge(Condition.StatusText("TRANSITION STARTED"));
            AssertRoundTrip(original);
        }

        // -- LatchCondition --

        [TestMethod]
        public void Serialize_Latch_ProducesSetAndClear()
        {
            var condition = Condition.Latch(
                set: Condition.Field("armed", CompareOp.GT, 0),
                clear: Condition.Field("airspeed", CompareOp.GT, 20));

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.IsNotNull(obj["latch"]);
            var latchObj = obj["latch"] as JObject;
            Assert.IsNotNull(latchObj["set"]);
            Assert.IsNotNull(latchObj["clear"]);
            Assert.AreEqual("armed", (string)latchObj["set"]["stateField"]);
            Assert.AreEqual("airspeed", (string)latchObj["clear"]["stateField"]);
        }

        [TestMethod]
        public void Deserialize_Latch_ParsesCorrectly()
        {
            var json = @"{
                ""latch"": {
                    ""set"": { ""stateField"": ""armed"", ""op"": "">"", ""value"": 0 },
                    ""clear"": { ""stateField"": ""airspeed"", ""op"": "">"", ""value"": 20 }
                }
            }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(LatchCondition));
            var latch = (LatchCondition)result;
            Assert.IsInstanceOfType(latch.Set, typeof(CompareCondition));
            Assert.IsInstanceOfType(latch.Clear, typeof(CompareCondition));
            Assert.AreEqual("armed", ((CompareCondition)latch.Set).Name);
            Assert.AreEqual("airspeed", ((CompareCondition)latch.Clear).Name);
        }

        [TestMethod]
        public void Deserialize_Latch_MissingSet_Throws()
        {
            var json = @"{ ""latch"": { ""clear"": { ""stateField"": ""x"", ""op"": "">"", ""value"": 0 } } }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(json));
            StringAssert.Contains(ex.Message, "set");
        }

        [TestMethod]
        public void RoundTrip_Latch()
        {
            var original = Condition.Latch(
                set: Condition.Field("armed", CompareOp.GT, 0),
                clear: Condition.Field("airspeed", CompareOp.GT, 20));
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_LatchWithEdgeInputs()
        {
            var original = Condition.Latch(
                set: Condition.Edge(Condition.StatusText("TRANSITION STARTED")),
                clear: Condition.Edge(Condition.StatusText("TRANSITION DONE")));
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_LatchInComposite()
        {
            var original = Condition.Latch(
                    set: Condition.Edge(Condition.Field("armed", CompareOp.GT, 0)),
                    clear: Condition.NamedValue("VTOLState", CompareOp.EQ, 0))
                .Not();
            AssertRoundTrip(original);
        }

        static void AssertRoundTrip(ICondition original)
        {
            var json = WarningSerializer.SerializeCondition(original);
            var deserialized = WarningSerializer.DeserializeCondition(json);
            ConditionAssert.AreStructurallyEqual(original, deserialized);
        }

        // -- Ref support --

        [TestMethod]
        public void ReadJson_Ref_ResolvesToNamedCondition()
        {
            var armed = Condition.Field("armed", CompareOp.GT, 0);
            var readRefs = new Dictionary<string, ICondition> { ["Armed"] = armed };
            var converter = new ConditionConverter(readRefs, null);
            var settings = new JsonSerializerSettings { Converters = { converter } };

            var result = JsonConvert.DeserializeObject<ICondition>(
                @"{ ""ref"": ""Armed"" }", settings);

            Assert.AreSame(armed, result);
        }

        [TestMethod]
        public void ReadJson_Ref_UnknownName_Throws()
        {
            var readRefs = new Dictionary<string, ICondition>();
            var converter = new ConditionConverter(readRefs, null);
            var settings = new JsonSerializerSettings { Converters = { converter } };

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<ICondition>(
                    @"{ ""ref"": ""Bogus"" }", settings));
            StringAssert.Contains(ex.Message, "Bogus");
        }

        [TestMethod]
        public void ReadJson_Ref_NoDict_Throws()
        {
            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(@"{ ""ref"": ""Armed"" }"));
            StringAssert.Contains(ex.Message, "Armed");
        }

        [TestMethod]
        public void WriteJson_KnownInstance_EmitsRef()
        {
            var armed = Condition.Field("armed", CompareOp.GT, 0);
            var writeRefs = new Dictionary<ICondition, string>(
                ConditionConverter.ReferenceEqualityComparer.Instance)
            {
                [armed] = "Armed"
            };
            var converter = new ConditionConverter(null, writeRefs);
            var settings = new JsonSerializerSettings { Converters = { converter } };

            var json = JsonConvert.SerializeObject(armed, settings);
            var obj = JObject.Parse(json);

            Assert.AreEqual("Armed", (string)obj["ref"]);
            Assert.IsNull(obj["stateField"]);
        }

        [TestMethod]
        public void WriteJson_UnknownInstance_InlinesFull()
        {
            var armed = Condition.Field("armed", CompareOp.GT, 0);
            var other = Condition.Field("rpm", CompareOp.LT, 500);
            var writeRefs = new Dictionary<ICondition, string>(
                ConditionConverter.ReferenceEqualityComparer.Instance)
            {
                [armed] = "Armed"
            };
            var converter = new ConditionConverter(null, writeRefs);
            var settings = new JsonSerializerSettings { Converters = { converter } };

            var json = JsonConvert.SerializeObject(other, settings);
            var obj = JObject.Parse(json);

            Assert.IsNull(obj["ref"]);
            Assert.AreEqual("rpm", (string)obj["stateField"]);
        }

        [TestMethod]
        public void WriteJson_RefInComposite_EmitsRefForKnownChild()
        {
            var armed = Condition.Field("armed", CompareOp.GT, 0);
            var composite = armed.And(Condition.Field("rpm", CompareOp.LT, 500));
            var writeRefs = new Dictionary<ICondition, string>(
                ConditionConverter.ReferenceEqualityComparer.Instance)
            {
                [armed] = "Armed"
            };
            var converter = new ConditionConverter(null, writeRefs);
            var settings = new JsonSerializerSettings { Converters = { converter } };

            var json = JsonConvert.SerializeObject(composite, settings);
            var obj = JObject.Parse(json);

            var arr = obj["and"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual("Armed", (string)arr[0]["ref"]);
            Assert.AreEqual("rpm", (string)arr[1]["stateField"]);
        }
    }
}
