using System;
using Carbonix.Warnings;

namespace Carbonix.CAS
{
    /// <summary>
    /// Represents a single alert instance tracked through its lifecycle.
    /// </summary>
    public class AlertEntry
    {
        static int _nextId;

        public int Id { get; }
        public WarningSeverity Severity { get; }
        public string Message { get; }
        public AlertState State { get; internal set; }
        public bool IsAcked { get; internal set; }
        public DateTime FiredUtc { get; }
        public DateTime LastSeenUtc { get; internal set; }
        public DateTime? ResolvedUtc { get; internal set; }

        /// <summary>
        /// Gets the auto-resolve interval measured from the last heartbeat,
        /// or <c>null</c> if auto-resolve is disabled.
        /// </summary>
        public TimeSpan? AutoResolveAfter { get; internal set; }

        public AlertEntry(WarningSeverity severity, string message)
        {
            Id = _nextId++;
            Severity = severity;
            Message = message;
            State = AlertState.Active;
            IsAcked = false;
            FiredUtc = DateTime.UtcNow;
            LastSeenUtc = DateTime.UtcNow;
        }

        public bool IsActive => State == AlertState.Active;

        public bool IsResolved => State == AlertState.Resolved;

        public bool IsUncleared => State != AlertState.History;
    }
}
