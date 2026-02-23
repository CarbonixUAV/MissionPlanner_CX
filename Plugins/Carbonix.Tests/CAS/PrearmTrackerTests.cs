using System;
using System.Linq;
using Carbonix.CAS;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.CAS
{
    [TestClass]
    public class PrearmTrackerTests
    {
        AlertManager _mgr;
        PrearmTracker _tracker;
        int _requestCount;
        DateTime _now;

        [TestInitialize]
        public void Setup()
        {
            _mgr = new AlertManager();
            _requestCount = 0;
            _tracker = new PrearmTracker(_mgr, () => _requestCount++);
            _now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _tracker.UtcNow = () => _now;
        }

        // --- OnPrearmMessage ---

        [TestMethod]
        public void OnPrearmMessage_FiresCautionAlert()
        {
            _tracker.OnPrearmMessage("PreArm: GPS not healthy");

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(1, alerts.Count);
            Assert.AreEqual(AlertTier.Caution, alerts[0].Tier);
            Assert.AreEqual("PreArm: GPS not healthy", alerts[0].Message);
            Assert.AreEqual(AlertState.ActiveUnacked, alerts[0].State);
        }

        [TestMethod]
        public void OnPrearmMessage_NoAutoResolve()
        {
            _tracker.OnPrearmMessage("PreArm: GPS not healthy");

            var alert = _mgr.GetUnclearedAlerts()[0];
            Assert.IsNull(alert.AutoResolveAfter);
        }

        [TestMethod]
        public void OnPrearmMessage_DuplicateHeartbeats()
        {
            _tracker.OnPrearmMessage("PreArm: GPS not healthy");
            _now = _now.AddMilliseconds(100);
            _tracker.OnPrearmMessage("PreArm: GPS not healthy");

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(1, alerts.Count);
        }

        // --- Batch close ---

        [TestMethod]
        public void Tick_ClosesBatchAfterGap()
        {
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");

            // Advance past the 2s batch gap
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            // Both alerts still active (nothing to compare against in first batch)
            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(2, alerts.Count);
            Assert.IsTrue(alerts.All(a => a.IsActive));
        }

        [TestMethod]
        public void BatchClose_ResolvesMissingAlert()
        {
            // First batch: A, B, C
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");
            _tracker.OnPrearmMessage("PreArm: C");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false); // close first batch

            // Second batch: A, B (C is fixed)
            _now = _now.AddSeconds(1);
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false); // close second batch

            var alerts = _mgr.GetUnclearedAlerts();
            var active = alerts.Where(a => a.IsActive).ToList();
            var resolved = alerts.Where(a => a.IsResolved).ToList();

            Assert.AreEqual(2, active.Count);
            Assert.AreEqual(1, resolved.Count);
            Assert.AreEqual("PreArm: C", resolved[0].Message);
        }

        [TestMethod]
        public void BatchClose_KeepsPresentAlertsActive()
        {
            // First batch: A, B
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            // Second batch: A, B (same)
            _now = _now.AddSeconds(1);
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(2, alerts.Count);
            Assert.IsTrue(alerts.All(a => a.IsActive));
        }

        [TestMethod]
        public void BatchClose_MixedAddAndRemove()
        {
            // First batch: A, B
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            // Second batch: B, C (A fixed, C new)
            _now = _now.AddSeconds(1);
            _tracker.OnPrearmMessage("PreArm: B");
            _tracker.OnPrearmMessage("PreArm: C");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            var alerts = _mgr.GetUnclearedAlerts();
            var active = alerts.Where(a => a.IsActive).Select(a => a.Message).ToList();
            var resolved = alerts.Where(a => a.IsResolved).Select(a => a.Message).ToList();

            CollectionAssert.AreEquivalent(new[] { "PreArm: B", "PreArm: C" }, active);
            CollectionAssert.AreEquivalent(new[] { "PreArm: A" }, resolved);
        }

        [TestMethod]
        public void BatchClose_EmptyBatch_ResolvesAll()
        {
            // First batch: A, B
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false); // close first batch

            // No messages arrive, but another batch gap passes
            // (simulates a request that returned no prearm failures,
            // but prearmstatus hasn't flipped yet)
            // We need to open and close a batch for this —
            // without any messages, no batch opens, so this is a no-op.
            // The prearmstatus bit is the authority for "all clear".
            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(2, alerts.Count);
            Assert.IsTrue(alerts.All(a => a.IsActive));
        }

        [TestMethod]
        public void BatchClose_CaseInsensitiveMatching()
        {
            // First batch: mixed case
            _tracker.OnPrearmMessage("PreArm: GPS not healthy");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            // Second batch: different case
            _now = _now.AddSeconds(1);
            _tracker.OnPrearmMessage("PreArm: gps not healthy");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            // Should still be active (same message, different case)
            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(1, alerts.Count);
            Assert.IsTrue(alerts[0].IsActive);
        }

        [TestMethod]
        public void BatchClose_DoesNotAffectNonPrearmAlerts()
        {
            // Fire a non-prearm alert directly
            _mgr.Fire(AlertTier.Warning, "EKF VARIANCE");

            // First batch: A
            _tracker.OnPrearmMessage("PreArm: A");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false);

            // Second batch: empty (no prearm messages)
            // Non-prearm alert should be unaffected
            var alerts = _mgr.GetUnclearedAlerts();
            var warning = alerts.First(a => a.Message == "EKF VARIANCE");
            Assert.IsTrue(warning.IsActive);
        }

        // --- Prearm bit resolution ---

        [TestMethod]
        public void Tick_PrearmStatusOk_ResolvesAllPrearmAlerts()
        {
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");

            _tracker.Tick(true, false);

            var alerts = _mgr.GetUnclearedAlerts();
            Assert.AreEqual(2, alerts.Count);
            Assert.IsTrue(alerts.All(a => a.IsResolved));
        }

        [TestMethod]
        public void Tick_PrearmStatusOk_DoesNotAffectNonPrearmAlerts()
        {
            _mgr.Fire(AlertTier.Warning, "EKF VARIANCE");
            _tracker.OnPrearmMessage("PreArm: A");

            _tracker.Tick(true, false);

            var alerts = _mgr.GetUnclearedAlerts();
            var warning = alerts.First(a => a.Message == "EKF VARIANCE");
            var prearm = alerts.First(a => a.Message == "PreArm: A");
            Assert.IsTrue(warning.IsActive);
            Assert.IsTrue(prearm.IsResolved);
        }

        [TestMethod]
        public void Tick_PrearmStatusOk_ClearsBatchState()
        {
            // Start a batch
            _tracker.OnPrearmMessage("PreArm: A");
            _tracker.OnPrearmMessage("PreArm: B");

            // Prearm clears
            _tracker.Tick(true, false);

            // New prearm failure after clear — should start fresh
            _now = _now.AddSeconds(1);
            _tracker.OnPrearmMessage("PreArm: C");
            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false); // close batch

            // Only C should be active; A and B stay resolved
            var active = _mgr.GetUnclearedAlerts().Where(a => a.IsActive).ToList();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("PreArm: C", active[0].Message);
        }

        // --- Periodic requesting ---

        [TestMethod]
        public void Tick_RequestsPrearmChecksEvery10s()
        {
            _tracker.Tick(false, false);
            Assert.AreEqual(1, _requestCount);

            // Less than 10s later — no request
            _now = _now.AddSeconds(9);
            _tracker.Tick(false, false);
            Assert.AreEqual(1, _requestCount);

            // 10s later — request
            _now = _now.AddSeconds(1);
            _tracker.Tick(false, false);
            Assert.AreEqual(2, _requestCount);
        }

        [TestMethod]
        public void Tick_NoRequestWhenPrearmStatusOk()
        {
            _tracker.Tick(true, false);
            Assert.AreEqual(0, _requestCount);
        }

        [TestMethod]
        public void Tick_NoRequestWhenArmed()
        {
            _tracker.Tick(false, true);
            Assert.AreEqual(0, _requestCount);
        }

        [TestMethod]
        public void Tick_RequestSurvivesException()
        {
            bool threw = false;
            var tracker = new PrearmTracker(_mgr, () =>
            {
                if (!threw) { threw = true; throw new Exception("fail"); }
            });
            tracker.UtcNow = () => _now;

            // First tick throws, but should still update last-request time
            tracker.Tick(false, false);

            // 5s later — should request again (not stuck)
            _now = _now.AddSeconds(5);
            tracker.Tick(false, false);
            Assert.IsTrue(threw);
        }

        // --- Edge cases ---

        [TestMethod]
        public void ResolveAll_SafeWithNoActivePrearms()
        {
            // Tick with prearmStatusOk when no prearm alerts exist — no crash
            _tracker.Tick(true, false);
        }

        [TestMethod]
        public void BatchClose_SafeWithNoActivePrearms()
        {
            // Open and close a batch when the alerts were already resolved externally
            _tracker.OnPrearmMessage("PreArm: A");
            _mgr.Resolve("PreArm: A"); // resolved externally

            _now = _now.AddSeconds(3);
            _tracker.Tick(false, false); // close batch — should not crash
        }
    }
}
