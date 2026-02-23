using System;
using System.Text.RegularExpressions;

namespace Carbonix.Warnings
{
    public class WarningRule
    {
        /// <summary>Gets the unique identifier for this rule.</summary>
        public string Id { get; }

        /// <summary>Gets the text spoken or displayed when the alert fires.</summary>
        public string Text { get; }

        public WarningSeverity Severity { get; }
        public WarningSubsystem Subsystem { get; }

        /// <summary>
        /// Gets the condition that must be true for the rule to be evaluated, or <c>null</c> if always ungated.
        /// </summary>
        public ICondition Gate { get; }

        /// <summary>
        /// Gets the condition that fires the alert when satisfied, or <c>null</c>
        /// for STATUSTEXT-only rules.
        /// </summary>
        public ICondition Trigger { get; }

        /// <summary>
        /// Optional regex that claims matching STATUSTEXT messages. Claimed
        /// messages are routed through this rule's gate and trigger logic
        /// instead of creating a standalone auto-resolve alert.
        /// </summary>
        public Regex StatusTextPattern { get; }

        public WarningRule(
            string id,
            string text,
            WarningSeverity severity,
            WarningSubsystem subsystem,
            ICondition trigger,
            ICondition gate = null,
            Regex statusTextPattern = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            if (trigger == null && statusTextPattern == null)
                throw new ArgumentException(
                    "At least one of trigger or statusTextPattern must be provided.");
            Severity = severity;
            Subsystem = subsystem;
            Trigger = trigger;
            Gate = gate;
            StatusTextPattern = statusTextPattern;
        }
    }
}
