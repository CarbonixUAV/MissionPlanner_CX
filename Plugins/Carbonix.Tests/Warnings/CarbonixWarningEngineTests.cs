using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    public class FakeSource
    {
        public double armed { get; set; }
        public double rpm { get; set; }
    }

    [TestClass]
    public class CarbonixWarningEngineTests
    {
        static Regex Pat(string pattern) =>
            new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---- ClaimStatusText ----

        [TestMethod]
        public void ClaimStatusText_MatchingPattern_ReturnsTrue()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                statusTextPattern: Pat("engine stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource { armed = 1 };

            Assert.IsTrue(engine.ClaimStatusText("engine stop"));
        }

        [TestMethod]
        public void ClaimStatusText_NoMatch_ReturnsFalse()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                statusTextPattern: Pat("engine stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource { armed = 1 };

            Assert.IsFalse(engine.ClaimStatusText("GPS lost"));
        }

        [TestMethod]
        public void ClaimStatusText_MatchButGateClosed_ReturnsTrue()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                gate: Condition.Field("armed", CompareOp.GT, 0),
                statusTextPattern: Pat("engine stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource { armed = 0 };

            Assert.IsTrue(engine.ClaimStatusText("engine stop"));
        }

        [TestMethod]
        public void ClaimStatusText_CaseInsensitive()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                statusTextPattern: Pat("Engine Stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource();

            Assert.IsTrue(engine.ClaimStatusText("ENGINE STOP"));
        }

        [TestMethod]
        public void ClaimStatusText_NoPatternRules_ReturnsFalse()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine,
                trigger: Condition.Field("rpm", CompareOp.LT, 500));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource();

            Assert.IsFalse(engine.ClaimStatusText("anything"));
        }

        [TestMethod]
        public void ClaimStatusText_MatchWithNoSource_ReturnsTrue()
        {
            // Source not yet set — gate can't be evaluated, so claim is allowed
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                gate: Condition.Field("armed", CompareOp.GT, 0),
                statusTextPattern: Pat("engine stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });

            Assert.IsTrue(engine.ClaimStatusText("engine stop"));
        }

        // ---- EvaluateRule with text triggers ----

        [TestMethod]
        public void Evaluate_TextTriggerSet_FiresTransition()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                statusTextPattern: Pat("engine stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource();

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            engine.ClaimStatusText("engine stop");
            // Force one evaluation cycle
            InvokeEvaluate(engine);

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("test", events[0].id);
            Assert.IsTrue(events[0].active);
        }

        [TestMethod]
        public void Evaluate_TextTriggerExpired_ResolvesRule()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                statusTextPattern: Pat("engine stop"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource();

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            engine.ClaimStatusText("engine stop");
            InvokeEvaluate(engine);
            Assert.AreEqual(1, events.Count);

            // Wait for text trigger to expire
            Thread.Sleep(CarbonixWarningEngine.TextTriggerTimeoutMs + 500);
            InvokeEvaluate(engine);

            Assert.AreEqual(2, events.Count);
            Assert.IsFalse(events[1].active);
        }

        [TestMethod]
        public void Evaluate_CombinedRule_FieldKeepsActiveAfterTextExpires()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine,
                trigger: Condition.Field("rpm", CompareOp.LT, 500),
                statusTextPattern: Pat("engine stop"));

            var source = new FakeSource { rpm = 300 };
            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = source;

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            // Field trigger fires the rule
            InvokeEvaluate(engine);
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].active);

            // Also claim a STATUSTEXT (both sources now active)
            engine.ClaimStatusText("engine stop");
            InvokeEvaluate(engine);
            // No new event — still active
            Assert.AreEqual(1, events.Count);

            // Wait for text trigger to expire, but field trigger is still true
            Thread.Sleep(CarbonixWarningEngine.TextTriggerTimeoutMs + 500);
            InvokeEvaluate(engine);
            // Still active — no resolve event
            Assert.AreEqual(1, events.Count);

            // Now clear the field trigger too
            source.rpm = 600;
            InvokeEvaluate(engine);
            Assert.AreEqual(2, events.Count);
            Assert.IsFalse(events[1].active);
        }

        [TestMethod]
        public void Evaluate_TextOnlyRule_NullTrigger_Works()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Caution,
                subsystem: WarningSubsystem.Engine, trigger: null,
                statusTextPattern: Pat("QASSIST"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = new FakeSource();

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            // Without text trigger, rule is inactive
            InvokeEvaluate(engine);
            Assert.AreEqual(0, events.Count);

            // Claim triggers it
            engine.ClaimStatusText("QASSIST");
            InvokeEvaluate(engine);
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].active);
        }

        [TestMethod]
        public void Evaluate_GateClosed_ResolvesRegardlessOfTextTrigger()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine, trigger: null,
                gate: Condition.Field("armed", CompareOp.GT, 0),
                statusTextPattern: Pat("engine stop"));

            var source = new FakeSource { armed = 1 };
            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = source;

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            // Arm + claim → fires
            engine.ClaimStatusText("engine stop");
            InvokeEvaluate(engine);
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].active);

            // Disarm → gate closes → resolves even though text trigger is fresh
            source.armed = 0;
            InvokeEvaluate(engine);
            Assert.AreEqual(2, events.Count);
            Assert.IsFalse(events[1].active);
        }

        [TestMethod]
        public void Evaluate_FieldTriggerOnly_UnchangedBehavior()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Warning,
                subsystem: WarningSubsystem.Engine,
                trigger: Condition.Field("rpm", CompareOp.LT, 500),
                gate: Condition.Field("armed", CompareOp.GT, 0));

            var source = new FakeSource { armed = 1, rpm = 1000 };
            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = source;

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            // Not triggered
            InvokeEvaluate(engine);
            Assert.AreEqual(0, events.Count);

            // Trigger
            source.rpm = 300;
            InvokeEvaluate(engine);
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].active);

            // Resolve
            source.rpm = 600;
            InvokeEvaluate(engine);
            Assert.AreEqual(2, events.Count);
            Assert.IsFalse(events[1].active);
        }

        // ---- Named value binding ----

        [TestMethod]
        public void Constructor_BindsNamedValueConditions()
        {
            var nvc = Condition.NamedValue("VTOLState", CompareOp.GT, 0);
            Assert.IsNull(nvc.Store);

            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Caution,
                subsystem: WarningSubsystem.FlightControl,
                trigger: nvc,
                statusTextPattern: Pat("QASSIST"));

            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });

            Assert.AreSame(engine.NamedValues, nvc.Store);
        }

        [TestMethod]
        public void Evaluate_NamedValueTrigger_FiresAndResolves()
        {
            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Caution,
                subsystem: WarningSubsystem.FlightControl,
                trigger: Condition.NamedValue("VTOLState", CompareOp.GT, 0),
                gate: Condition.Field("armed", CompareOp.GT, 0),
                statusTextPattern: Pat("QASSIST"));

            var source = new FakeSource { armed = 1 };
            var engine = new CarbonixWarningEngine(new List<WarningRule> { rule });
            engine.Source = source;

            var events = new List<(string id, bool active)>();
            engine.WarningStateChanged += (s, e) => events.Add((e.Rule.Id, e.IsActive));

            // Named value not yet received — inactive
            InvokeEvaluate(engine);
            Assert.AreEqual(0, events.Count);

            // Named value indicates QAssist active
            engine.UpdateNamedValue("VTOLState", 1.0f);
            InvokeEvaluate(engine);
            Assert.AreEqual(1, events.Count);
            Assert.IsTrue(events[0].active);

            // Named value indicates QAssist off
            engine.UpdateNamedValue("VTOLState", 0.0f);
            InvokeEvaluate(engine);
            Assert.AreEqual(2, events.Count);
            Assert.IsFalse(events[1].active);
        }

        /// <summary>
        /// Calls the private Evaluate method directly via the same path the
        /// loop uses, without starting the async loop.
        /// </summary>
        static void InvokeEvaluate(CarbonixWarningEngine engine)
        {
            // Use reflection to call the private Evaluate(object source) method
            var method = typeof(CarbonixWarningEngine).GetMethod("Evaluate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(engine, new[] { engine.Source });
        }
    }
}
