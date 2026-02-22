using System;
using System.Collections.Generic;
using System.Linq;
using Carbonix.CAS;
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
            _mgr.Fire(AlertTier.Warning, "GPS LOST");

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(1, alerts.Count);
            Assert.AreEqual("GPS LOST", alerts[0].Message);
            Assert.AreEqual(AlertTier.Warning, alerts[0].Tier);
            Assert.AreEqual(AlertState.ActiveUnacked, alerts[0].State);
        }

        [TestMethod]
        public void Fire_RaisesNewAlertFired()
        {
            _mgr.Fire(AlertTier.Caution, "VIBE HIGH");

            Assert.AreEqual(1, _newAlerts.Count);
            Assert.AreEqual("VIBE HIGH", _newAlerts[0].Message);
        }

        [TestMethod]
        public void Fire_Duplicate_Heartbeats_NoNewEvent()
        {
            _mgr.Fire(AlertTier.Warning, "GPS LOST");
            var firstSeen = _mgr.GetUnclearedAlerts()[0].LastSeenUtc;

            _newAlerts.Clear();
            _mgr.Fire(AlertTier.Warning, "GPS LOST");

            Assert.AreEqual(0, _newAlerts.Count, "Heartbeat should not fire NewAlertFired");
            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count, "Should not create duplicate");
            Assert.IsTrue(_mgr.GetUnclearedAlerts()[0].LastSeenUtc >= firstSeen);
        }

        [TestMethod]
        public void Fire_DuplicateOfResolved_ReTriggersToActiveUnacked()
        {
            _mgr.Fire(AlertTier.Warning, "GPS LOST");
            _mgr.AckTier(AlertTier.Warning);
            _mgr.Resolve("GPS LOST");
            Assert.AreEqual(AlertState.ResolvedAcked, _mgr.GetUnclearedAlerts()[0].State);

            _newAlerts.Clear();
            _mgr.Fire(AlertTier.Warning, "GPS LOST");

            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.AreEqual(AlertState.ActiveUnacked, alert.State);
            Assert.IsNull(alert.ResolvedUtc);
            Assert.AreEqual(1, _newAlerts.Count, "Re-trigger should fire NewAlertFired");
        }

        // --- Resolve ---

        [TestMethod]
        public void Resolve_ActiveUnacked_BecomesResolvedUnacked()
        {
            _mgr.Fire(AlertTier.Warning, "GPS LOST");
            _mgr.Resolve("GPS LOST");

            Assert.AreEqual(AlertState.ResolvedUnacked, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void Resolve_ActiveAcked_BecomesResolvedAcked()
        {
            _mgr.Fire(AlertTier.Warning, "GPS LOST");
            _mgr.AckTier(AlertTier.Warning);
            _mgr.Resolve("GPS LOST");

            Assert.AreEqual(AlertState.ResolvedAcked, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void Resolve_UnknownMessage_NoOp()
        {
            _mgr.Resolve("NOPE");
            Assert.AreEqual(0, _mgr.GetUnclearedAlerts().Count);
        }

        // --- AckTier ---

        [TestMethod]
        public void AckTier_AcksAllUnackedAtTier()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.Fire(AlertTier.Warning, "B");
            _mgr.Fire(AlertTier.Caution, "C");

            _mgr.AckTier(AlertTier.Warning);

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(AlertState.ActiveAcked, alerts.First(a => a.Message == "A").State);
            Assert.AreEqual(AlertState.ActiveAcked, alerts.First(a => a.Message == "B").State);
            Assert.AreEqual(AlertState.ActiveUnacked, alerts.First(a => a.Message == "C").State,
                "Caution should be untouched");
        }

        [TestMethod]
        public void AckTier_AcksResolvedUnacked()
        {
            _mgr.Fire(AlertTier.Caution, "X");
            _mgr.Resolve("X");
            Assert.AreEqual(AlertState.ResolvedUnacked, _mgr.GetUnclearedAlerts()[0].State);

            _mgr.AckTier(AlertTier.Caution);
            Assert.AreEqual(AlertState.ResolvedAcked, _mgr.GetUnclearedAlerts()[0].State);
        }

        // --- AckSingle ---

        [TestMethod]
        public void AckSingle_AcksOnlyTargetAlert()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.Fire(AlertTier.Warning, "B");

            var id = _mgr.GetUnclearedAlerts().First(a => a.Message == "A").Id;
            _mgr.AckSingle(id);

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(AlertState.ActiveAcked, alerts.First(a => a.Message == "A").State);
            Assert.AreEqual(AlertState.ActiveUnacked, alerts.First(a => a.Message == "B").State);
        }

        // --- Dismiss ---

        [TestMethod]
        public void Dismiss_ResolvedAcked_MovesToHistory()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.AckTier(AlertTier.Warning);
            _mgr.Resolve("A");
            var id = _mgr.GetUnclearedAlerts()[0].Id;

            Assert.IsTrue(_mgr.Dismiss(id));
            Assert.AreEqual(0, _mgr.GetUnclearedAlerts().Count);
            Assert.AreEqual(1, _mgr.GetHistoryAlerts().Count);
        }

        [TestMethod]
        public void Dismiss_ActiveAcked_Rejected()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.AckTier(AlertTier.Warning);
            var id = _mgr.GetUnclearedAlerts()[0].Id;

            Assert.IsFalse(_mgr.Dismiss(id));
            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count);
        }

        // --- Sorting ---

        [TestMethod]
        public void GetUnclearedAlerts_SortsWarningsBeforeCautions()
        {
            _mgr.Fire(AlertTier.Caution, "CAUT_FIRST");
            _mgr.Fire(AlertTier.Warning, "WARN_SECOND");

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(AlertTier.Warning, alerts[0].Tier);
            Assert.AreEqual(AlertTier.Caution, alerts[1].Tier);
        }

        // --- HasUnclearedAlerts ---

        [TestMethod]
        public void HasUnclearedAlerts_FalseWhenAllDismissed()
        {
            _mgr.Fire(AlertTier.Warning, "A");
            _mgr.AckTier(AlertTier.Warning);
            _mgr.Resolve("A");
            _mgr.Dismiss(_mgr.GetUnclearedAlerts()[0].Id);

            Assert.IsFalse(_mgr.HasUnclearedAlerts());
        }

        // --- Case-insensitive dedup ---

        [TestMethod]
        public void Fire_CaseInsensitive_DeduplicatesHeartbeat()
        {
            _mgr.Fire(AlertTier.Warning, "GPS Lost");
            _newAlerts.Clear();

            _mgr.Fire(AlertTier.Warning, "gps lost");

            Assert.AreEqual(1, _mgr.GetUnclearedAlerts().Count, "Should dedup case-insensitively");
            Assert.AreEqual(0, _newAlerts.Count, "Heartbeat should not fire NewAlertFired");
        }

        [TestMethod]
        public void Fire_CaseInsensitive_PreservesOriginalCase()
        {
            _mgr.Fire(AlertTier.Warning, "GPS Lost");
            _mgr.Fire(AlertTier.Warning, "gps lost");

            Assert.AreEqual("GPS Lost", _mgr.GetUnclearedAlerts()[0].Message);
        }

        [TestMethod]
        public void Resolve_CaseInsensitive_Matches()
        {
            _mgr.Fire(AlertTier.Warning, "GPS Lost");
            _mgr.Resolve("gps lost");

            Assert.AreEqual(AlertState.ResolvedUnacked, _mgr.GetUnclearedAlerts()[0].State);
        }

        // --- Auto-resolve ---

        [TestMethod]
        public void SweepAutoResolve_ResolvesExpiredAlerts()
        {
            _mgr.Fire(AlertTier.Caution, "Low battery", TimeSpan.FromSeconds(5));

            // Backdate LastSeenUtc so the timeout has elapsed
            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddSeconds(-6);

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.ResolvedUnacked, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void SweepAutoResolve_LeavesNonExpiredAlone()
        {
            _mgr.Fire(AlertTier.Caution, "Low battery", TimeSpan.FromSeconds(5));

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.ActiveUnacked, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void SweepAutoResolve_IgnoresAlertsWithoutTimeout()
        {
            _mgr.Fire(AlertTier.Warning, "GPS LOST");

            // Backdate well past any reasonable timeout
            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddMinutes(-10);

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.ActiveUnacked, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void SweepAutoResolve_AckedAlert_BecomesResolvedAcked()
        {
            _mgr.Fire(AlertTier.Caution, "Low battery", TimeSpan.FromSeconds(5));
            _mgr.AckTier(AlertTier.Caution);

            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddSeconds(-6);

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.ResolvedAcked, _mgr.GetUnclearedAlerts()[0].State);
        }

        [TestMethod]
        public void Fire_Heartbeat_ResetsAutoResolveDeadline()
        {
            _mgr.Fire(AlertTier.Caution, "Low battery", TimeSpan.FromSeconds(5));

            // Backdate close to expiry
            var alert = _mgr.GetUnclearedAlerts()[0];
            alert.LastSeenUtc = DateTime.UtcNow.AddSeconds(-4);

            // Heartbeat resets the clock
            _mgr.Fire(AlertTier.Caution, "Low battery", TimeSpan.FromSeconds(5));

            _mgr.SweepAutoResolve();

            Assert.AreEqual(AlertState.ActiveUnacked, _mgr.GetUnclearedAlerts()[0].State,
                "Heartbeat should have reset the auto-resolve deadline");
        }
    }
}
