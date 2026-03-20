using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Carbonix.Warnings
{
    public class WarningRule
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
        public ICondition Trigger { get; set; }

        [JsonProperty("gate", NullValueHandling = NullValueHandling.Ignore)]
        public ICondition Gate { get; set; }

        public WarningRule() { }

        public WarningRule(
            string id,
            string text,
            WarningSeverity severity,
            WarningSubsystem subsystem,
            ICondition trigger,
            ICondition gate = null,
            Aircraft? aircraft = null)
        {
            Id = id;
            Text = text;
            Severity = severity;
            Subsystem = subsystem;
            Trigger = trigger;
            Gate = gate;
            Aircraft = aircraft;
        }
    }
}
