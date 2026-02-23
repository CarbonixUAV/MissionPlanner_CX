using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using System.Reflection;

namespace Carbonix.CAS
{
    /// <summary>
    /// Tracks prearm STATUSTEXT messages and manages their alert lifecycle.
    /// </summary>
    /// <remarks>
    /// Prearm messages arrive in batches after each prearm check. This tracker:
    /// <list type="bullet">
    /// <item>Fires each prearm message as a <see cref="AlertTier.Caution"/> alert</item>
    /// <item>Resolves all prearm alerts when the prearm bit clears</item>
    /// <item>Resolves individual alerts that disappear from the latest batch</item>
    /// <item>Periodically requests prearm checks to keep batches fresh</item>
    /// </list>
    /// </remarks>
    public class PrearmTracker
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static readonly TimeSpan BatchGap = TimeSpan.FromSeconds(2);
        static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(10);

        readonly AlertManager _alertManager;
        readonly Action _requestPrearmChecks;
        readonly HashSet<string> _currentBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DateTime _lastPrearmMessageUtc = DateTime.MinValue;
        DateTime _lastRequestUtc = DateTime.MinValue;
        bool _batchOpen;

        internal Func<DateTime> UtcNow = () => DateTime.UtcNow;

        public PrearmTracker(AlertManager alertManager, Action requestPrearmChecks)
        {
            _alertManager = alertManager ?? throw new ArgumentNullException(nameof(alertManager));
            _requestPrearmChecks = requestPrearmChecks ?? throw new ArgumentNullException(nameof(requestPrearmChecks));
        }

        /// <summary>
        /// Called when a STATUSTEXT with a "PreArm:" prefix is received.
        /// </summary>
        public void OnPrearmMessage(string text)
        {
            var now = UtcNow();

            _batchOpen = true;
            _lastPrearmMessageUtc = now;
            _currentBatch.Add(text);

            _alertManager.Fire(AlertTier.Caution, text);
        }

        /// <summary>
        /// Performs periodic prearm housekeeping.
        /// </summary>
        /// <param name="prearmStatusOk">True if the prearm bit indicates ready to arm.</param>
        /// <param name="armed">True if the vehicle is currently armed.</param>
        public void Tick(bool prearmStatusOk, bool armed)
        {
            if (prearmStatusOk)
            {
                ResolveAll();
                return;
            }

            var now = UtcNow();

            if (_batchOpen && (now - _lastPrearmMessageUtc) > BatchGap)
                CloseBatch();

            if (!armed && (now - _lastRequestUtc) >= RequestInterval)
            {
                try
                {
                    _requestPrearmChecks();
                }
                catch (Exception ex)
                {
                    log.Error("Failed to request prearm checks: " + ex.Message);
                }
                _lastRequestUtc = now;
            }
        }

        void CloseBatch()
        {
            var activePrearms = _alertManager.GetUnclearedAlerts()
                .Where(a => a.IsActive && IsPrearmMessage(a.Message))
                .ToList();

            foreach (var alert in activePrearms)
            {
                if (!_currentBatch.Contains(alert.Message))
                    _alertManager.Resolve(alert.Message);
            }

            _currentBatch.Clear();
            _batchOpen = false;
        }

        void ResolveAll()
        {
            var activePrearms = _alertManager.GetUnclearedAlerts()
                .Where(a => a.IsActive && IsPrearmMessage(a.Message))
                .ToList();

            foreach (var alert in activePrearms)
                _alertManager.Resolve(alert.Message);

            _currentBatch.Clear();
            _batchOpen = false;
        }

        static bool IsPrearmMessage(string message)
        {
            return message.StartsWith("PreArm:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
