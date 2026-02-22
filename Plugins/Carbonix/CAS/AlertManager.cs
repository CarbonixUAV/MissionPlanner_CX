using System;
using System.Collections.Generic;
using System.Linq;

namespace Carbonix.CAS
{
    /// <summary>
    /// Manages the lifecycle of crew alerts.
    /// </summary>
    /// <remarks>
    /// All public members are thread-safe. Fire and resolve from any thread;
    /// read state from the UI thread via <see cref="GetUnclearedAlerts"/>.
    /// </remarks>
    public class AlertManager
    {
        readonly object _lock = new object();
        readonly List<AlertEntry> _alerts = new List<AlertEntry>();

        /// <summary>Occurs when any alert changes state.</summary>
        /// <remarks>Raised on a background thread; handlers must marshal to the UI thread.</remarks>
        public event Action AlertsChanged;

        /// <summary>Occurs when a new alert is created or a resolved alert is re-triggered.</summary>
        public event Action<AlertEntry> NewAlertFired;

        /// <summary>
        /// Fires or heartbeats an alert with the specified tier and message.
        /// </summary>
        /// <remarks>
        /// If an active alert with the same message exists, its timestamp is updated.
        /// If a resolved alert with the same message exists, it is re-triggered as
        /// unacknowledged. Otherwise a new alert is created.
        /// </remarks>
        /// <param name="tier">The alert tier.</param>
        /// <param name="message">The alert message text, matched case-insensitively.</param>
        public void Fire(AlertTier tier, string message)
        {
            Fire(tier, message, null);
        }

        /// <summary>
        /// Fires or heartbeats an alert with an optional auto-resolve timeout.
        /// </summary>
        /// <param name="tier">The alert tier.</param>
        /// <param name="message">The alert message text, matched case-insensitively.</param>
        /// <param name="autoResolveAfter">
        /// If not <c>null</c>, the alert auto-resolves this long after its last heartbeat.
        /// </param>
        public void Fire(AlertTier tier, string message, TimeSpan? autoResolveAfter)
        {
            bool isNew = false;
            AlertEntry entry = null;

            lock (_lock)
            {
                var existing = _alerts.FirstOrDefault(
                    a => string.Equals(a.Message, message, StringComparison.OrdinalIgnoreCase)
                         && a.State != AlertState.History);

                if (existing != null)
                {
                    existing.LastSeenUtc = DateTime.UtcNow;
                    if (autoResolveAfter.HasValue)
                        existing.AutoResolveAfter = autoResolveAfter;

                    if (existing.IsResolved)
                    {
                        // Condition recurred — re-trigger
                        existing.State = AlertState.ActiveUnacked;
                        existing.ResolvedUtc = null;
                        isNew = true;
                        entry = existing;
                    }
                    // else: heartbeat only, no event
                }
                else
                {
                    entry = new AlertEntry(tier, message);
                    entry.AutoResolveAfter = autoResolveAfter;
                    _alerts.Add(entry);
                    isNew = true;
                }
            }

            if (isNew)
            {
                NewAlertFired?.Invoke(entry);
                AlertsChanged?.Invoke();
            }
        }

        /// <summary>
        /// Resolves the active alert matching the specified message.
        /// </summary>
        /// <remarks>
        /// No-op if no active alert matches. Unacknowledged alerts become
        /// <see cref="AlertState.ResolvedUnacked"/>; acknowledged alerts become
        /// <see cref="AlertState.ResolvedAcked"/>.
        /// </remarks>
        /// <param name="message">The alert message to resolve, matched case-insensitively.</param>
        public void Resolve(string message)
        {
            bool changed = false;

            lock (_lock)
            {
                var entry = _alerts.FirstOrDefault(
                    a => string.Equals(a.Message, message, StringComparison.OrdinalIgnoreCase)
                         && a.IsActive);
                if (entry != null)
                {
                    entry.State = entry.State == AlertState.ActiveUnacked
                        ? AlertState.ResolvedUnacked
                        : AlertState.ResolvedAcked;
                    entry.ResolvedUtc = DateTime.UtcNow;
                    changed = true;
                }
            }

            if (changed)
                AlertsChanged?.Invoke();
        }

        /// <summary>
        /// Acknowledges all unacknowledged alerts at the specified tier.
        /// </summary>
        /// <param name="tier">The tier to acknowledge.</param>
        public void AckTier(AlertTier tier)
        {
            bool changed = false;

            lock (_lock)
            {
                foreach (var a in _alerts)
                {
                    if (a.Tier != tier || !a.IsUnacked) continue;

                    a.State = a.State == AlertState.ActiveUnacked
                        ? AlertState.ActiveAcked
                        : AlertState.ResolvedAcked;
                    changed = true;
                }
            }

            if (changed)
                AlertsChanged?.Invoke();
        }

        /// <summary>
        /// Acknowledges a single alert by ID.
        /// </summary>
        /// <param name="id">The alert ID to acknowledge.</param>
        public void AckSingle(int id)
        {
            bool changed = false;

            lock (_lock)
            {
                var entry = _alerts.FirstOrDefault(a => a.Id == id);
                if (entry != null && entry.IsUnacked)
                {
                    entry.State = entry.State == AlertState.ActiveUnacked
                        ? AlertState.ActiveAcked
                        : AlertState.ResolvedAcked;
                    changed = true;
                }
            }

            if (changed)
                AlertsChanged?.Invoke();
        }

        /// <summary>
        /// Dismisses a resolved and acknowledged alert to history.
        /// </summary>
        /// <param name="id">The alert ID to dismiss.</param>
        /// <returns><c>true</c> if the alert was dismissed; otherwise, <c>false</c>.</returns>
        public bool Dismiss(int id)
        {
            bool changed = false;

            lock (_lock)
            {
                var entry = _alerts.FirstOrDefault(a => a.Id == id);
                if (entry != null && entry.State == AlertState.ResolvedAcked)
                {
                    entry.State = AlertState.History;
                    changed = true;
                }
            }

            if (changed)
                AlertsChanged?.Invoke();

            return changed;
        }

        /// <summary>
        /// Returns all uncleared (non-history) alerts, ordered by tier then recency.
        /// </summary>
        public List<AlertEntry> GetUnclearedAlerts()
        {
            lock (_lock)
            {
                return _alerts
                    .Where(a => a.IsUncleared)
                    .OrderBy(a => a.Tier == AlertTier.Warning ? 0 : 1)
                    .ThenByDescending(a => a.FiredUtc)
                    .ToList();
            }
        }

        /// <summary>
        /// Returns all dismissed alerts, ordered by most recently fired.
        /// </summary>
        public List<AlertEntry> GetHistoryAlerts()
        {
            lock (_lock)
            {
                return _alerts
                    .Where(a => a.State == AlertState.History)
                    .OrderByDescending(a => a.FiredUtc)
                    .ToList();
            }
        }

        /// <summary>
        /// Determines whether any uncleared alerts exist.
        /// </summary>
        /// <returns><c>true</c> if at least one uncleared alert exists; otherwise, <c>false</c>.</returns>
        public bool HasUnclearedAlerts()
        {
            lock (_lock)
                return _alerts.Any(a => a.IsUncleared);
        }

        /// <summary>
        /// Resolves any active alerts whose auto-resolve deadline has passed.
        /// </summary>
        /// <remarks>
        /// Call this periodically (e.g. once per second).
        /// </remarks>
        public void SweepAutoResolve()
        {
            bool changed = false;
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                foreach (var a in _alerts)
                {
                    if (!a.IsActive || !a.AutoResolveAfter.HasValue) continue;
                    if (now - a.LastSeenUtc < a.AutoResolveAfter.Value) continue;

                    a.State = a.State == AlertState.ActiveUnacked
                        ? AlertState.ResolvedUnacked
                        : AlertState.ResolvedAcked;
                    a.ResolvedUtc = now;
                    changed = true;
                }
            }

            if (changed)
                AlertsChanged?.Invoke();
        }
    }
}
