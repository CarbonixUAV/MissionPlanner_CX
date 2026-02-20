using System.Collections.Generic;
using System.IO;
using System.Linq;
using Carbonix;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class JsonSettingsFileTests
    {
        string _tempDir;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "CarbonixTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Teardown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        [TestMethod]
        public void LoadOrCreate_RoundTripsDefaults()
        {
            var path = Path.Combine(_tempDir, "warnings.json");
            var defaults = DefaultWarnings.AllRules
                .Select(r => WarningRuleConfig.FromInternal(r.aircraft, r.rule))
                .ToList();

            var result = JsonSettingsFile.LoadOrCreate(path, defaults,
                WarningSerializer.JsonSettings);

            Assert.AreEqual(defaults.Count, result.Count);
            for (int i = 0; i < defaults.Count; i++)
            {
                Assert.AreEqual(defaults[i].Id, result[i].Id);
                Assert.AreEqual(defaults[i].Severity, result[i].Severity);
                Assert.AreEqual(defaults[i].Subsystem, result[i].Subsystem);
                Assert.AreEqual(defaults[i].Aircraft, result[i].Aircraft);
                ConditionAssert.AreStructurallyEqual(defaults[i].Trigger, result[i].Trigger);
                ConditionAssert.AreStructurallyEqual(defaults[i].Gate, result[i].Gate);
            }
        }

        [TestMethod]
        public void LoadOrCreate_ReturnsNewInstances_NotOriginalDefaults()
        {
            var path = Path.Combine(_tempDir, "warnings.json");
            var defaults = DefaultWarnings.AllRules
                .Select(r => WarningRuleConfig.FromInternal(r.aircraft, r.rule))
                .ToList();

            var result = JsonSettingsFile.LoadOrCreate(path, defaults,
                WarningSerializer.JsonSettings);

            // Round-trip should produce new objects, not the same references
            for (int i = 0; i < defaults.Count; i++)
            {
                Assert.AreNotSame(defaults[i], result[i],
                    $"Rule {i}: expected a new instance from round-trip");
                Assert.AreNotSame(defaults[i].Trigger, result[i].Trigger,
                    $"Rule {i}: trigger should be a new instance from round-trip");
            }
        }

        [TestMethod]
        public void LoadOrCreate_WritesFileWhenMissing()
        {
            var path = Path.Combine(_tempDir, "warnings.json");
            var defaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "test", text: "test", severity: WarningSeverity.Advisory,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("x", CompareOp.GT, 0)))
            };

            JsonSettingsFile.LoadOrCreate(path, defaults,
                WarningSerializer.JsonSettings);

            Assert.IsTrue(File.Exists(path));
        }

        [TestMethod]
        public void LoadOrCreate_PreservesCustomizedFile()
        {
            var path = Path.Combine(_tempDir, "warnings.json");
            var defaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "rule1", text: "original text", severity: WarningSeverity.Advisory,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("x", CompareOp.GT, 0)))
            };

            // First call writes the file
            JsonSettingsFile.LoadOrCreate(path, defaults,
                WarningSerializer.JsonSettings);

            // Simulate user customization
            var text = File.ReadAllText(path);
            File.WriteAllText(path, text.Replace("original text", "custom text"));

            // Same defaults — should load customized file
            var result = JsonSettingsFile.LoadOrCreate(path, defaults,
                WarningSerializer.JsonSettings);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("rule1", result[0].Id);
            Assert.AreEqual("custom text", result[0].Text);
        }

        [TestMethod]
        public void LoadOrCreate_BacksUpCorruptFile()
        {
            var path = Path.Combine(_tempDir, "warnings.json");
            File.WriteAllText(path, "{{not valid json");

            var defaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "test", text: "test", severity: WarningSeverity.Advisory,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("x", CompareOp.GT, 0)))
            };

            var result = JsonSettingsFile.LoadOrCreate(path, defaults,
                WarningSerializer.JsonSettings);

            // Should have backed up the corrupt file
            Assert.IsTrue(File.Exists(path + ".1.bak"), "Corrupt file should be backed up");
            // Should have written fresh defaults
            Assert.IsTrue(File.Exists(path), "New file should be written");
            // Should return the defaults
            Assert.AreEqual("test", result[0].Id);
        }

        [TestMethod]
        public void LoadOrCreate_BacksUpCustomizedFileWhenDefaultsChange()
        {
            var path = Path.Combine(_tempDir, "warnings.json");

            var oldDefaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "old", text: "old rule", severity: WarningSeverity.Advisory,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("x", CompareOp.GT, 0)))
            };
            JsonSettingsFile.LoadOrCreate(path, oldDefaults,
                WarningSerializer.JsonSettings);

            // Simulate user editing the file (preserves wrapper + hash)
            var text = File.ReadAllText(path);
            File.WriteAllText(path, text.Replace("old rule", "user-modified"));

            // Load with new defaults — should evict with backup
            var newDefaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "new", text: "new rule", severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Engine,
                    trigger: Condition.Field("y", CompareOp.LT, 100)))
            };
            var result = JsonSettingsFile.LoadOrCreate(path, newDefaults,
                WarningSerializer.JsonSettings);

            Assert.IsTrue(File.Exists(path + ".1.bak"),
                "Customized file should be backed up");
            Assert.AreEqual("new", result[0].Id);
        }

        [TestMethod]
        public void LoadOrCreate_ReplacesUnmodifiedFileWhenDefaultsChange()
        {
            var path = Path.Combine(_tempDir, "warnings.json");

            var oldDefaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "old", text: "old rule", severity: WarningSeverity.Advisory,
                    subsystem: WarningSubsystem.GPS,
                    trigger: Condition.Field("x", CompareOp.GT, 0)))
            };
            JsonSettingsFile.LoadOrCreate(path, oldDefaults,
                WarningSerializer.JsonSettings);

            // File is NOT modified by user

            // Load with new defaults — should silently replace
            var newDefaults = new List<WarningRuleConfig>
            {
                WarningRuleConfig.FromInternal(null, new WarningRule(
                    id: "new", text: "new rule", severity: WarningSeverity.Caution,
                    subsystem: WarningSubsystem.Engine,
                    trigger: Condition.Field("y", CompareOp.LT, 100)))
            };
            var result = JsonSettingsFile.LoadOrCreate(path, newDefaults,
                WarningSerializer.JsonSettings);

            Assert.IsFalse(File.Exists(path + ".1.bak"),
                "Unmodified file should not be backed up");
            Assert.AreEqual("new", result[0].Id);
        }
    }
}
