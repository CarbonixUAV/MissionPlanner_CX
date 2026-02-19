using System;

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
        /// Gets the condition that must be true for the rule to be evaluated, or <c>null</c> if always active.
        /// </summary>
        public ICondition Gate { get; }

        /// <summary>
        /// Gets the condition that fires the alert when satisfied.
        /// </summary>
        public ICondition Trigger { get; }

        public WarningRule(
            string id,
            string text,
            WarningSeverity severity,
            WarningSubsystem subsystem,
            ICondition trigger,
            ICondition gate = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Severity = severity;
            Subsystem = subsystem;
            Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
            Gate = gate;
        }
    }
}
