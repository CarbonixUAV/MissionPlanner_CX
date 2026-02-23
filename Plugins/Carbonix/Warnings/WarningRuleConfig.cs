using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Represents a warning rule with aircraft scope in a JSON-serializable form.
    /// </summary>
    public class WarningRuleConfig
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("severity")]
        [JsonConverter(typeof(StringEnumConverter))]
        public WarningSeverity Severity { get; set; }

        [JsonProperty("subsystem")]
        [JsonConverter(typeof(StringEnumConverter))]
        public WarningSubsystem Subsystem { get; set; }

        [JsonProperty("aircraft", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public Aircraft? Aircraft { get; set; }

        [JsonProperty("trigger", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ConditionConverter))]
        public ICondition Trigger { get; set; }

        [JsonProperty("gate", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(ConditionConverter))]
        public ICondition Gate { get; set; }

        [JsonProperty("statusTextPattern", NullValueHandling = NullValueHandling.Ignore)]
        public string StatusTextPattern { get; set; }

        public (Aircraft? aircraft, WarningRule rule) ToInternal()
        {
            Regex pattern = StatusTextPattern != null
                ? new Regex(StatusTextPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)
                : null;
            return (Aircraft, new WarningRule(Id, Text, Severity, Subsystem, Trigger, Gate, pattern));
        }

        public static WarningRuleConfig FromInternal(Aircraft? aircraft, WarningRule rule)
        {
            return new WarningRuleConfig
            {
                Id = rule.Id,
                Text = rule.Text,
                Severity = rule.Severity,
                Subsystem = rule.Subsystem,
                Aircraft = aircraft,
                Trigger = rule.Trigger,
                Gate = rule.Gate,
                StatusTextPattern = rule.StatusTextPattern?.ToString(),
            };
        }
    }
}
