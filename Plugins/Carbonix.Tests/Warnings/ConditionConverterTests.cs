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

        static void AssertRoundTrip(ICondition original)
        {
            var json = WarningSerializer.SerializeCondition(original);
            var deserialized = WarningSerializer.DeserializeCondition(json);
            ConditionAssert.AreStructurallyEqual(original, deserialized);
        }
    }
}
