using System.Collections.Generic;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class WarningSerializerTests
    {

        [TestMethod]
        public void SerializeDefinitions_ConditionRefsInRules()
        {
            var armed = Condition.Field("armed", CompareOp.GT, 0);
            var defs = new WarningDefinitions
            {
                Conditions = new Dictionary<string, ICondition>
                {
                    ["Armed"] = armed,
                },
                Rules = new List<WarningRule>
                {
                    new WarningRule(
                        id: "test", text: "Test",
                        severity: WarningSeverity.Caution,
                        subsystem: WarningSubsystem.GPS,
                        trigger: Condition.Field("satcount", CompareOp.LT, 20),
                        gate: armed),
                },
            };

            var json = WarningSerializer.SerializeDefinitions(defs);
            var obj = JObject.Parse(json);

            // Conditions section has the full definition
            Assert.AreEqual("armed",
                (string)obj["conditions"]["Armed"]["stateField"]);

            // Rule gate should be a ref, not inlined
            var gate = obj["rules"][0]["gate"];
            Assert.AreEqual("Armed", (string)gate["ref"]);
        }

        [TestMethod]
        public void DeserializeDefinitions_RefsResolve()
        {
            var json = @"{
                ""conditions"": {
                    ""Armed"": { ""stateField"": ""armed"", ""op"": "">"", ""value"": 0 }
                },
                ""rules"": [{
                    ""id"": ""test"",
                    ""text"": ""Test"",
                    ""severity"": ""Caution"",
                    ""subsystem"": ""GPS"",
                    ""trigger"": { ""stateField"": ""satcount"", ""op"": ""<"", ""value"": 20 },
                    ""gate"": { ""ref"": ""Armed"" }
                }]
            }";

            var defs = WarningSerializer.DeserializeDefinitions(json);

            Assert.AreEqual(1, defs.Conditions.Count);
            Assert.AreEqual(1, defs.Rules.Count);

            // The gate should be the exact same instance as the named condition
            Assert.AreSame(defs.Conditions["Armed"], defs.Rules[0].Gate);
        }

        [TestMethod]
        public void DeserializeDefinitions_RefBetweenConditions()
        {
            var json = @"{
                ""conditions"": {
                    ""Armed"": { ""stateField"": ""armed"", ""op"": "">"", ""value"": 0 },
                    ""ArmedAndHigh"": {
                        ""and"": [
                            { ""ref"": ""Armed"" },
                            { ""stateField"": ""alt"", ""op"": "">"", ""value"": 100 }
                        ]
                    }
                },
                ""rules"": []
            }";

            var defs = WarningSerializer.DeserializeDefinitions(json);

            Assert.AreEqual(2, defs.Conditions.Count);
            var composite = (AndCondition)defs.Conditions["ArmedAndHigh"];
            Assert.AreSame(defs.Conditions["Armed"], composite.Left);
        }

        [TestMethod]
        public void DeserializeDefinitions_ForwardRef_Throws()
        {
            var json = @"{
                ""conditions"": {
                    ""Composite"": {
                        ""and"": [
                            { ""ref"": ""Armed"" },
                            { ""stateField"": ""alt"", ""op"": "">"", ""value"": 100 }
                        ]
                    },
                    ""Armed"": { ""stateField"": ""armed"", ""op"": "">"", ""value"": 0 }
                },
                ""rules"": []
            }";

            var ex = Assert.ThrowsException<JsonSerializationException>(
                () => WarningSerializer.DeserializeDefinitions(json));
            StringAssert.Contains(ex.Message, "Armed");
        }

        [TestMethod]
        public void RoundTrip_Definitions()
        {
            var armed = Condition.Field("armed", CompareOp.GT, 0);
            var onGround = Condition.Field("landed_state", CompareOp.EQ, 1);
            var defs = new WarningDefinitions
            {
                Conditions = new Dictionary<string, ICondition>
                {
                    ["Armed"] = armed,
                    ["OnGround"] = onGround,
                    ["InFlight"] = armed.And(onGround.Not()),
                },
                Rules = new List<WarningRule>
                {
                    new WarningRule(
                        id: "gps", text: "GPS low",
                        severity: WarningSeverity.Caution,
                        subsystem: WarningSubsystem.GPS,
                        trigger: Condition.Field("satcount", CompareOp.LT, 20),
                        gate: armed),
                    new WarningRule(
                        id: "engine", text: "Engine out",
                        severity: WarningSeverity.Warning,
                        subsystem: WarningSubsystem.Engine,
                        trigger: Condition.Field("efi_rpm", CompareOp.LT, 500),
                        aircraft: Aircraft.Ottano),
                },
            };

            var json = WarningSerializer.SerializeDefinitions(defs);
            var result = WarningSerializer.DeserializeDefinitions(json);

            Assert.AreEqual(defs.Conditions.Count, result.Conditions.Count);
            Assert.AreEqual(defs.Rules.Count, result.Rules.Count);

            foreach (var kvp in defs.Conditions)
            {
                Assert.IsTrue(result.Conditions.ContainsKey(kvp.Key));
                ConditionAssert.AreStructurallyEqual(
                    kvp.Value, result.Conditions[kvp.Key]);
            }

            for (int i = 0; i < defs.Rules.Count; i++)
            {
                Assert.AreEqual(defs.Rules[i].Aircraft, result.Rules[i].Aircraft);
                Assert.AreEqual(defs.Rules[i].Id, result.Rules[i].Id);
                ConditionAssert.AreStructurallyEqual(
                    defs.Rules[i].Trigger, result.Rules[i].Trigger);
                ConditionAssert.AreStructurallyEqual(
                    defs.Rules[i].Gate, result.Rules[i].Gate);
            }
        }

        [TestMethod]
        public void DeserializeDefinitions_NoConditionsSection()
        {
            var json = @"{
                ""rules"": [{
                    ""id"": ""test"",
                    ""text"": ""Test"",
                    ""severity"": ""Caution"",
                    ""subsystem"": ""GPS"",
                    ""trigger"": { ""stateField"": ""satcount"", ""op"": ""<"", ""value"": 20 }
                }]
            }";

            var defs = WarningSerializer.DeserializeDefinitions(json);

            Assert.AreEqual(0, defs.Conditions.Count);
            Assert.AreEqual(1, defs.Rules.Count);
            Assert.AreEqual("test", defs.Rules[0].Id);
        }
    }
}
