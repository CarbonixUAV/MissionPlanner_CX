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
            var condition = new FieldCondition("satcount", CompareOp.LT, 20);

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("satcount", (string)obj["field"]);
            Assert.AreEqual("<", (string)obj["op"]);
            Assert.AreEqual(20.0, (double)obj["value"]);
            Assert.IsNull(obj["clear"]);
        }

        [TestMethod]
        public void Serialize_FieldCondition_WithClear_IncludesClearValue()
        {
            var condition = new FieldCondition("satcount", CompareOp.LT, 18, clearThreshold: 22);

            var json = WarningSerializer.SerializeCondition(condition);
            var obj = JObject.Parse(json);

            Assert.AreEqual("satcount", (string)obj["field"]);
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
            Assert.AreEqual("a", (string)arr[0]["field"]);
            Assert.AreEqual("b", (string)arr[1]["field"]);
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
            Assert.AreEqual("a", (string)arr[0]["field"]);
            Assert.AreEqual("b", (string)arr[1]["field"]);
            Assert.AreEqual("c", (string)arr[2]["field"]);
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
            Assert.AreEqual("x", (string)obj["not"]["field"]);
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
                    new FieldCondition("x", op, 1));
                var obj = JObject.Parse(json);
                Assert.AreEqual(symbol, (string)obj["op"],
                    $"Operator {op} should serialize to \"{symbol}\"");
            }
        }

        // -- Deserialize direction (B → C): assert known JSON → expected structure --

        [TestMethod]
        public void Deserialize_FieldCondition_ParsesCorrectly()
        {
            var json = @"{ ""field"": ""rpm"", ""op"": ""<"", ""value"": 500 }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(FieldCondition));
            var field = (FieldCondition)result;
            Assert.AreEqual("rpm", field.PropertyName);
            Assert.AreEqual(CompareOp.LT, field.Op);
            Assert.AreEqual(500.0, field.Threshold);
            Assert.IsNull(field.ClearThreshold);
        }

        [TestMethod]
        public void Deserialize_FieldCondition_WithClear()
        {
            var json = @"{ ""field"": ""satcount"", ""op"": ""<"", ""value"": 18, ""clear"": 22 }";

            var result = WarningSerializer.DeserializeCondition(json);

            var field = (FieldCondition)result;
            Assert.AreEqual(18.0, field.Threshold);
            Assert.AreEqual(22.0, field.ClearThreshold);
        }

        [TestMethod]
        public void Deserialize_And_BuildsBinaryTree()
        {
            var json = @"{ ""and"": [
                { ""field"": ""a"", ""op"": "">"", ""value"": 1 },
                { ""field"": ""b"", ""op"": ""<"", ""value"": 2 },
                { ""field"": ""c"", ""op"": ""=="", ""value"": 3 }
            ]}";

            var result = WarningSerializer.DeserializeCondition(json);

            // 3-element array folds into And(And(a, b), c)
            Assert.IsInstanceOfType(result, typeof(AndCondition));
            var outer = (AndCondition)result;
            Assert.IsInstanceOfType(outer.Left, typeof(AndCondition));
            Assert.IsInstanceOfType(outer.Right, typeof(FieldCondition));
            Assert.AreEqual("c", ((FieldCondition)outer.Right).PropertyName);

            var inner = (AndCondition)outer.Left;
            Assert.AreEqual("a", ((FieldCondition)inner.Left).PropertyName);
            Assert.AreEqual("b", ((FieldCondition)inner.Right).PropertyName);
        }

        [TestMethod]
        public void Deserialize_Not_WrapsInner()
        {
            var json = @"{ ""not"": { ""field"": ""armed"", ""op"": "">"", ""value"": 0 } }";

            var result = WarningSerializer.DeserializeCondition(json);

            Assert.IsInstanceOfType(result, typeof(NotCondition));
            var not = (NotCondition)result;
            Assert.IsInstanceOfType(not.Inner, typeof(FieldCondition));
            Assert.AreEqual("armed", ((FieldCondition)not.Inner).PropertyName);
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
            var json = @"{ ""field"": ""x"", ""op"": ""~="", ""value"": 1 }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeCondition(json));
            StringAssert.Contains(ex.Message, "~=");
        }

        // -- Round-trip (A → B → C): structural comparison --

        [TestMethod]
        public void RoundTrip_SimpleField()
        {
            var original = new FieldCondition("rpm", CompareOp.LT, 500);
            AssertRoundTrip(original);
        }

        [TestMethod]
        public void RoundTrip_FieldWithClear()
        {
            var original = new FieldCondition("satcount", CompareOp.LT, 18, clearThreshold: 22);
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

        static void AssertRoundTrip(ICondition original)
        {
            var json = WarningSerializer.SerializeCondition(original);
            var deserialized = WarningSerializer.DeserializeCondition(json);
            ConditionAssert.AreStructurallyEqual(original, deserialized);
        }
    }
}
