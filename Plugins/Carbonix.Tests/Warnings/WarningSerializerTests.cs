using System.Collections.Generic;
using System.Linq;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class WarningSerializerTests
    {
        [TestMethod]
        public void SerializeRules_ProducesExpectedShape()
        {
            var rules = new List<(Aircraft?, WarningRule)>
            {
                (null, new WarningRule(
                    id: "gps_sats",
                    text: "GPS low sats",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount", CompareOp.LT, 20),
                    gate: Condition.Field("armed", CompareOp.GT, 0))),
            };

            var json = WarningSerializer.SerializeRules(rules);
            var arr = JArray.Parse(json);

            Assert.AreEqual(1, arr.Count);
            var obj = (JObject)arr[0];
            Assert.AreEqual("gps_sats", (string)obj["id"]);
            Assert.AreEqual("GPS low sats", (string)obj["text"]);
            Assert.AreEqual("Caution", (string)obj["severity"]);
            Assert.AreEqual("GPS", (string)obj["subsystem"]);
            Assert.IsNull(obj["aircraft"]); // null aircraft omitted
            Assert.IsNotNull(obj["trigger"]);
            Assert.IsNotNull(obj["gate"]);
        }

        [TestMethod]
        public void SerializeRules_AircraftIncludedWhenSet()
        {
            var rules = new List<(Aircraft?, WarningRule)>
            {
                (Aircraft.Ottano, new WarningRule(
                    id: "engine",
                    text: "Engine stopped",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Engine,
                    trigger: Condition.Field("efi_rpm", CompareOp.LT, 500))),
            };

            var json = WarningSerializer.SerializeRules(rules);
            var obj = JArray.Parse(json)[0] as JObject;

            Assert.AreEqual("Ottano", (string)obj["aircraft"]);
        }

        [TestMethod]
        public void SerializeRules_GateOmittedWhenNull()
        {
            var rules = new List<(Aircraft?, WarningRule)>
            {
                (null, new WarningRule(
                    id: "test",
                    text: "test",
                    severity: WarningSeverity.Advisory,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("x", CompareOp.GT, 0))),
            };

            var json = WarningSerializer.SerializeRules(rules);
            var obj = JArray.Parse(json)[0] as JObject;

            Assert.IsNull(obj["gate"]);
        }

        [TestMethod]
        public void DeserializeRules_ParsesCorrectly()
        {
            var json = @"[{
                ""id"": ""test_rule"",
                ""text"": ""Test alert"",
                ""severity"": ""Warning"",
                ""subsystem"": ""Engine"",
                ""aircraft"": ""Volanti"",
                ""trigger"": { ""field"": ""rpm"", ""op"": ""<"", ""value"": 500 },
                ""gate"": { ""field"": ""armed"", ""op"": "">"", ""value"": 0 }
            }]";

            var result = WarningSerializer.DeserializeRules(json);

            Assert.AreEqual(1, result.Count);
            var (aircraft, rule) = result[0];
            Assert.AreEqual(Aircraft.Volanti, aircraft);
            Assert.AreEqual("test_rule", rule.Id);
            Assert.AreEqual("Test alert", rule.Text);
            Assert.AreEqual(WarningSeverity.Warning, rule.Severity);
            Assert.AreEqual(WarningSubsystem.Engine, rule.Subsystem);
            Assert.IsInstanceOfType(rule.Trigger, typeof(FieldCondition));
            Assert.IsInstanceOfType(rule.Gate, typeof(FieldCondition));
        }

        [TestMethod]
        public void DeserializeRules_NullAircraftWhenOmitted()
        {
            var json = @"[{
                ""id"": ""x"",
                ""text"": ""x"",
                ""severity"": ""Advisory"",
                ""subsystem"": ""GPS"",
                ""trigger"": { ""field"": ""a"", ""op"": "">"", ""value"": 0 }
            }]";

            var result = WarningSerializer.DeserializeRules(json);
            Assert.IsNull(result[0].aircraft);
            Assert.IsNull(result[0].rule.Gate);
        }

        [TestMethod]
        public void RoundTrip_MultipleRulesWithMixedAircraft()
        {
            var original = new List<(Aircraft?, WarningRule)>
            {
                (null, new WarningRule(
                    id: "gps_sats",
                    text: "GPS low sats",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("satcount", CompareOp.LT, 18, clear: 22),
                    gate: Condition.Field("armed", CompareOp.GT, 0))),

                (Aircraft.Ottano, new WarningRule(
                    id: "engine",
                    text: "Engine stopped",
                    severity: WarningSeverity.Warning,
                    subsystem: WarningSubsystem.Engine,
                    trigger: Condition.Field("efi_rpm", CompareOp.LT, 500),
                    gate: Condition.Field("armed", CompareOp.GT, 0)
                        .And(Condition.Field("efi_headtemp", CompareOp.NEQ, 0)))),

                (Aircraft.Volanti, new WarningRule(
                    id: "esc_temp",
                    text: "Pusher ESC temp high",
                    severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.ESC,
                    trigger: Condition.Field("esc5_temp", CompareOp.GT, 100))),
            };

            var json = WarningSerializer.SerializeRules(original);
            var result = WarningSerializer.DeserializeRules(json);

            Assert.AreEqual(original.Count, result.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.AreEqual(original[i].Item1, result[i].aircraft,
                    $"Rule {i}: aircraft mismatch");

                var exp = original[i].Item2;
                var act = result[i].rule;
                Assert.AreEqual(exp.Id, act.Id, $"Rule {i}: Id mismatch");
                Assert.AreEqual(exp.Text, act.Text, $"Rule {i}: Text mismatch");
                Assert.AreEqual(exp.Severity, act.Severity, $"Rule {i}: Severity mismatch");
                Assert.AreEqual(exp.Subsystem, act.Subsystem, $"Rule {i}: Subsystem mismatch");

                ConditionAssert.AreStructurallyEqual(exp.Trigger, act.Trigger);
                ConditionAssert.AreStructurallyEqual(exp.Gate, act.Gate);
            }
        }
    }
}
