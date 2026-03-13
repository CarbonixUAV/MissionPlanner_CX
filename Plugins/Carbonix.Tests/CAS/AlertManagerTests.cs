using System;
using System.Collections.Generic;
using System.Linq;
using Carbonix.CAS;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.CAS
{
    [TestClass]
    public class AlertManagerTests
    {
        AlertManager _mgr;
        List<AlertEntry> _newAlerts;

        [TestInitialize]
        public void Setup()
        {
            _mgr = new AlertManager();
            _newAlerts = new List<AlertEntry>();
            _mgr.NewAlertFired += e => _newAlerts.Add(e);
        }

        // --- Fire ---

        [TestMethod]
        public void Fire_CreatesNewAlert()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(1, alerts.Count);
            Assert.AreEqual("GPS LOST", alerts[0].Message);
            Assert.AreEqual(WarningSeverity.Warning, alerts[0].Severity);
            Assert.AreEqual(AlertState.Active, alerts[0].State);
            Assert.IsFalse(alerts[0].IsAcked);
        }

        [TestMethod]
        public void Fire_RaisesNewAlertFired()
        {
            _mgr.Fire(WarningSeverity.Caution, "VIBE HIGH");

            Assert.AreEqual(1, _newAlerts.Count);
            Assert.AreEqual("VIBE HIGH", _newAlerts[0].Message);
        }

        [TestMethod]
        public void Fire_Duplicate_Heartbeats_NoNewEvent()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");
            var firstSeen = _mgr.GetUnclearedAlerts()[0].LastSeenUtc;

            _newAlerts.Clear();
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");

            Assert.AreEqual(0, _newAlerts.Count, "Heartbeat should not fire NewAlertFired");
            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count, "Should not create duplicate");
            Assert.IsTrue(_mgr.GetUnclearedAlerts()[0].LastSeenUtc >= firstSeen);
        }

        [TestMethod]
        public void Fire_DuplicateOfResolved_ReTriggersToActiveUnacked()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");
            _mgr.AckSeverity(WarningSeverity.Warning);
            _mgr.Resolve("GPS LOST");
            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsTrue(alert.IsAcked);

            _newAlerts.Clear();
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");

            alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Active, alert.State);
            Assert.IsFalse(alert.IsAcked);
            Assert.IsNull(alert.ResolvedUtc);
            Assert.AreEqual(1, _newAlerts.Count, "Re-trigger should fire NewAlertFired");
        }

        // --- Resolve ---

        [TestMethod]
        public void Resolve_ActiveUnacked_BecomesResolvedUnacked()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");
            _mgr.Resolve("GPS LOST");

            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsFalse(alert.IsAcked);
        }

        [TestMethod]
        public void Resolve_ActiveAcked_BecomesResolvedAcked()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");
            _mgr.AckSeverity(WarningSeverity.Warning);
            _mgr.Resolve("GPS LOST");

            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsTrue(alert.IsAcked);
        }

        [TestMethod]
        public void Resolve_UnknownMessage_NoOp()
        {
            _mgr.Resolve("NOPE");
            Assert.AreEqual(0, _mgr.GetUnclearedAlerts().Count);
        }

        // --- AckSeverity ---

        [TestMethod]
        public void AckSeverity_AcksAllUnackedAtSeverity()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.Fire(WarningSeverity.Warning, "B");
            _mgr.Fire(WarningSeverity.Caution, "C");

            _mgr.AckSeverity(WarningSeverity.Warning);

            var alerts = _mgr.GetUnclearedAlerts();
            var a = alerts.First(x => x.Message == "A");
            var b = alerts.First(x => x.Message == "B");
            var c = alerts.First(x => x.Message == "C");
            Assert.IsTrue(a.IsAcked);
            Assert.IsTrue(b.IsAcked);
            Assert.IsFalse(c.IsAcked, "Caution should be untouched");
        }

        [TestMethod]
        public void AckSeverity_AcksResolvedUnacked()
        {
            _mgr.Fire(WarningSeverity.Caution, "X");
            _mgr.Resolve("X");
            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsFalse(alert.IsAcked);

            _mgr.AckSeverity(WarningSeverity.Caution);
            alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsTrue(alert.IsAcked);
        }

        // --- AckSingle ---

        [TestMethod]
        public void AckSingle_AcksOnlyTargetAlert()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.Fire(WarningSeverity.Warning, "B");

            var id = _mgr.GetUnclearedAlerts().First(a => a.Message == "A").Id;
            _mgr.AckSingle(id);

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.IsTrue(alerts.First(a => a.Message == "A").IsAcked);
            Assert.IsFalse(alerts.First(a => a.Message == "B").IsAcked);
        }

        // --- Dismiss ---

        [TestMethod]
        public void Dismiss_ResolvedAcked_MovesToHistory()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.AckSeverity(WarningSeverity.Warning);
            _mgr.Resolve("A");
            var id = _mgr.GetUnclearedAlerts()[0].Id;

            Assert.IsTrue(_mgr.Dismiss(id));
            Assert.AreEqual(0, _mgr.GetUnclearedAlerts().Count);
            Assert.AreEqual(1, _mgr.GetHistoryAlerts().Count);
        }

        [TestMethod]
        public void Dismiss_ActiveAcked_Rejected()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.AckSeverity(WarningSeverity.Warning);
            var id = _mgr.GetUnclearedAlerts()[0].Id;

            Assert.IsFalse(_mgr.Dismiss(id));
            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count);
        }

        [TestMethod]
        public void Dismiss_ResolvedUnacked_Rejected()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.Resolve("A");
            var id = _mgr.GetUnclearedAlerts()[0].Id;

            Assert.IsFalse(_mgr.Dismiss(id));
            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count);
        }

        [TestMethod]
        public void Dismiss_InvalidId_ReturnsFalse()
        {
            Assert.IsFalse(_mgr.Dismiss(-1));
        }

        // --- Sorting ---

        [TestMethod]
        public void GetUnclearedAlerts_SortsWarningsBeforeCautions()
        {
            _mgr.Fire(WarningSeverity.Caution, "CAUT_FIRST");
            _mgr.Fire(WarningSeverity.Warning, "WARN_SECOND");

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(WarningSeverity.Warning, alerts[0].Severity);
            Assert.AreEqual(WarningSeverity.Caution, alerts[1].Severity);
        }

        // --- HasUnclearedAlerts ---

        [TestMethod]
        public void HasUnclearedAlerts_FalseWhenAllDismissed()
        {
            _mgr.Fire(WarningSeverity.Warning, "A");
            _mgr.AckSeverity(WarningSeverity.Warning);
            _mgr.Resolve("A");
            _mgr.Dismiss(_mgr.GetUnclearedAlerts()[0].Id);

            Assert.IsFalse(_mgr.HasUnclearedAlerts());
        }

        // --- Case-insensitive dedup ---

        [TestMethod]
        public void Fire_CaseInsensitive_DeduplicatesHeartbeat()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS Lost");
            _newAlerts.Clear();

            _mgr.Fire(WarningSeverity.Warning, "gps lost");

            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count, "Should dedup case-insensitively");
            Assert.AreEqual(0, _newAlerts.Count, "Heartbeat should not fire NewAlertFired");
        }

        [TestMethod]
        public void Fire_CaseInsensitive_PreservesOriginalCase()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS Lost");
            _mgr.Fire(WarningSeverity.Warning, "gps lost");

            Assert.AreEqual("GPS Lost", _mgr.GetUnclearedAlerts()[0].Message);
        }

        [TestMethod]
        public void Resolve_CaseInsensitive_Matches()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS Lost");
            _mgr.Resolve("gps lost");

            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsFalse(alert.IsAcked);
        }

        // --- Auto-resolve ---

        [TestMethod]
        public void SweepAutoResolve_ResolvesExpiredAlerts()
        {
            _mgr.Fire(WarningSeverity.Caution, "Low battery", TimeSpan.FromSeconds(5));

            // Backdate LastSeenUtc so the timeout has elapsed
            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddSeconds(-6);

            _mgr.SweepAutoResolve();

            alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsFalse(alert.IsAcked);
        }

        [TestMethod]
        public void SweepAutoResolve_LeavesNonExpiredAlone()
        {
            _mgr.Fire(WarningSeverity.Caution, "Low battery", TimeSpan.FromSeconds(5));

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.Active, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void SweepAutoResolve_IgnoresAlertsWithoutTimeout()
        {
            _mgr.Fire(WarningSeverity.Warning, "GPS LOST");

            // Backdate well past any reasonable timeout
            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddMinutes(-10);

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.Active, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void SweepAutoResolve_AckedAlert_BecomesResolvedAcked()
        {
            _mgr.Fire(WarningSeverity.Caution, "Low battery", TimeSpan.FromSeconds(5));
            _mgr.AckSeverity(WarningSeverity.Caution);

            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddSeconds(-6);

            _mgr.SweepAutoResolve();

            alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.Resolved, alert.State);
            Assert.IsTrue(alert.IsAcked);
        }

        [TestMethod]
        public void Fire_Heartbeat_ResetsAutoResolveDeadline()
        {
            _mgr.Fire(WarningSeverity.Caution, "Low battery", TimeSpan.FromSeconds(5));

            // Backdate close to expiry
            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddSeconds(-4);

            // Heartbeat resets the clock
            _mgr.Fire(WarningSeverity.Caution, "Low battery", TimeSpan.FromSeconds(5));

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.Active, _mgr.GetUnclearedAlerts()[0].State,
                "Heartbeat should have reset the auto-resolve deadline");
        }
    }
}
