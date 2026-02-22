using System;

namespace Carbonix.CAS
{
    /// <summary>
    /// Represents a single alert instance tracked through its lifecycle.
    /// </summary>
    public class AlertEntry
    {
        static int _nextId;

        public int Id { get; }
        public AlertTier Tier { get; }
        public string Message { get; }
        public AlertState State { get; internal set; }
        public DateTime FiredUtc { get; }
        public DateTime LastSeenUtc { get; internal set; }
        public DateTime? ResolvedUtc { get; internal set; }

        /// <summary>
        /// Gets the auto-resolve interval measured from the last heartbeat,
        /// or <c>null</c> if auto-resolve is disabled.
        /// </summary>
        public TimeSpan? AutoResolveAfter { get; internal set; }

        public AlertEntry(AlertTier tier, string message)
        {
            Id = _nextId++;
            Tier = tier;
            Message = message;
            State = AlertState.ActiveUnacked;
            FiredUtc = DateTime.UtcNow;
            LastSeenUtc = DateTime.UtcNow;
        }

        public bool IsUnacked =>
            State == AlertState.ActiveUnacked || State == AlertState.ResolvedUnacked;

        public bool IsActive =>
            State == AlertState.ActiveUnacked || State == AlertState.ActiveAcked;

        public bool IsResolved =>
            State == AlertState.ResolvedUnacked || State == AlertState.ResolvedAcked;

        public bool IsUncleared => State != AlertState.History;
    }
}
