using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class DefaultWarningsTests
    {
        [TestMethod]
        public void Defaults_RoundTripThroughJson()
        {
            var json = WarningSerializer.SerializeDefinitions(DefaultWarnings.Defaults);
            var defs = WarningSerializer.DeserializeDefinitions(json);

            Assert.AreEqual(DefaultWarnings.Defaults.Rules.Count, defs.Rules.Count);

            for (int i = 0; i < DefaultWarnings.Defaults.Rules.Count; i++)
            {
                var expected = DefaultWarnings.Defaults.Rules[i];
                var actual = defs.Rules[i];

                Assert.AreEqual(expected.Aircraft, actual.Aircraft,
                    $"Aircraft mismatch at index {i} ({expected.Id})");
                Assert.AreEqual(expected.Id, actual.Id);
                Assert.AreEqual(expected.Text, actual.Text);
                Assert.AreEqual(expected.Severity, actual.Severity);
                Assert.AreEqual(expected.Subsystem, actual.Subsystem);

                ConditionAssert.AreStructurallyEqual(expected.Trigger, actual.Trigger);
                ConditionAssert.AreStructurallyEqual(expected.Gate, actual.Gate);
            }
        }

        [TestMethod]
        public void Defaults_ConditionsRoundTrip()
        {
            var json = WarningSerializer.SerializeDefinitions(DefaultWarnings.Defaults);
            var defs = WarningSerializer.DeserializeDefinitions(json);

            Assert.AreEqual(
                DefaultWarnings.Defaults.Conditions.Count,
                defs.Conditions.Count);

            foreach (var kvp in DefaultWarnings.Defaults.Conditions)
            {
                Assert.IsTrue(defs.Conditions.ContainsKey(kvp.Key),
                    $"Missing condition '{kvp.Key}'");
                ConditionAssert.AreStructurallyEqual(
                    kvp.Value, defs.Conditions[kvp.Key]);
            }
        }

        [TestMethod]
        public void Defaults_SerializedJsonContainsRefs()
        {
            var json = WarningSerializer.SerializeDefinitions(DefaultWarnings.Defaults);
            var obj = JObject.Parse(json);

            // The rules section should contain ref nodes (not fully inlined)
            var rulesJson = obj["rules"].ToString();
            StringAssert.Contains(rulesJson, "\"ref\"");

            // The conditions section should also use refs for composed conditions
            var conditionsJson = obj["conditions"].ToString();
            StringAssert.Contains(conditionsJson, "\"ref\"");
        }
    }
}
