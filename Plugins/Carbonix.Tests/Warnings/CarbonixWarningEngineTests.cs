using System;
using System.Collections.Generic;
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
        // ---- Named value binding ----

        [TestMethod]
        public void Constructor_BindsNamedValueConditions()
        {
            var nvc = Condition.NamedValue("VTOLState", CompareOp.GT, 0);
            Assert.IsNull(nvc.Store);

            var rule = new WarningRule(
                id: "test", text: "Test", severity: WarningSeverity.Caution,
                subsystem: WarningSubsystem.FlightControl,
                trigger: nvc);

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
                gate: Condition.Field("armed", CompareOp.GT, 0));

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
