using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Provides methods for serializing and deserializing conditions and warning rules.
    /// </summary>
    public static class WarningSerializer
    {
        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new ConditionConverter() },
        };

        // --- Conditions (standalone) ---

        public static string SerializeCondition(ICondition condition)
        {
            return JsonConvert.SerializeObject(condition, JsonSettings);
        }

        public static ICondition DeserializeCondition(string json)
        {
            return JsonConvert.DeserializeObject<ICondition>(json, JsonSettings);
        }

        // --- Rules ---

        public static string SerializeRules(List<(Aircraft? aircraft, WarningRule rule)> rules)
        {
            var configs = rules
                .Select(r => WarningRuleConfig.FromInternal(r.aircraft, r.rule))
                .ToList();
            return JsonConvert.SerializeObject(configs, JsonSettings);
        }

        public static List<(Aircraft? aircraft, WarningRule rule)> DeserializeRules(string json)
        {
            var configs = JsonConvert.DeserializeObject<List<WarningRuleConfig>>(json, JsonSettings);
            return configs.Select(c => c.ToInternal()).ToList();
        }
    }
}
