using System;
using System.Collections.Generic;
using System.Threading;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Holds runtime state for stateful conditions (latches, edges,
    /// status-text fire times, hysteresis). Keyed by
    /// <see cref="ICondition.StateKey"/> so that structurally identical
    /// conditions (e.g. after JSON round-trip) share the same state.
    /// </summary>
    /// <remarks>
    /// Owned by the warning engine and passed into every
    /// <see cref="ICondition.Evaluate"/> call. This separates condition
    /// definitions (immutable) from their runtime state (mutable),
    /// allowing conditions to be safely serialized and shared.
    /// </remarks>
    public class ConditionState
    {
        /// <summary>UTC ticks of last fire for StatusTextConditions.</summary>
        readonly Dictionary<string, long> _fired = new Dictionary<string, long>();

        /// <summary>Latch state (true/false) for LatchConditions.</summary>
        readonly Dictionary<string, bool> _latched = new Dictionary<string, bool>();

        /// <summary>Previous-cycle value for EdgeConditions.</summary>
        readonly Dictionary<string, bool> _edgePrev = new Dictionary<string, bool>();

        /// <summary>Hysteresis state for CompareConditions with clear thresholds.</summary>
        readonly Dictionary<string, bool> _hysteresis = new Dictionary<string, bool>();

        // --- StatusText state ---

        public long GetFiredTicks(string key)
        {
            return _fired.TryGetValue(key, out var v) ? v : 0;
        }

        public void SetFiredTicks(string key, long ticks)
        {
            _fired[key] = ticks;
        }

        // --- Latch state ---

        public bool GetLatched(string key)
        {
            return _latched.TryGetValue(key, out var v) && v;
        }

        public void SetLatched(string key, bool value)
        {
            _latched[key] = value;
        }

        // --- Edge state ---

        public bool? GetEdgePrev(string key)
        {
            return _edgePrev.TryGetValue(key, out var v) ? v : (bool?)null;
        }

        public void SetEdgePrev(string key, bool value)
        {
            _edgePrev[key] = value;
        }

        // --- Hysteresis state ---

        public bool GetHysteresis(string key)
        {
            return _hysteresis.TryGetValue(key, out var v) && v;
        }

        public void SetHysteresis(string key, bool value)
        {
            _hysteresis[key] = value;
        }
    }
}
