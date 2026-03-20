using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        // --- Definitions (conditions + rules with ref support) ---

        /// <summary>
        /// Serializes a <see cref="WarningDefinitions"/> to a JSON string.
        /// Named conditions are serialized in order; later conditions and rules
        /// emit <c>{"ref": "Name"}</c> for previously-defined conditions.
        /// </summary>
        public static string SerializeDefinitions(WarningDefinitions defs)
        {
            return SerializeDefinitionsToken(defs).ToString(Formatting.Indented);
        }

        /// <summary>
        /// Serializes a <see cref="WarningDefinitions"/> to a <see cref="JObject"/>.
        /// </summary>
        public static JObject SerializeDefinitionsToken(WarningDefinitions defs)
        {
            var writeRefs = new Dictionary<ICondition, string>(
                ConditionConverter.ReferenceEqualityComparer.Instance);
            var converter = new ConditionConverter(null, writeRefs);
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Converters = { converter },
            });

            // Serialize conditions in order. Each condition can ref earlier
            // ones but not itself — we add to writeRefs after serializing.
            var conditionsObj = new JObject();
            foreach (var kvp in defs.Conditions)
            {
                conditionsObj[kvp.Key] = JToken.FromObject(kvp.Value, serializer);
                writeRefs[kvp.Value] = kvp.Key;
            }

            // Serialize rules with the full writeRefs
            var rulesArray = JToken.FromObject(defs.Rules, serializer);

            return new JObject
            {
                ["conditions"] = conditionsObj,
                ["rules"] = rulesArray,
            };
        }

        /// <summary>
        /// Deserializes a <see cref="WarningDefinitions"/> from a JSON string.
        /// </summary>
        public static WarningDefinitions DeserializeDefinitions(string json)
        {
            return DeserializeDefinitions(JObject.Parse(json));
        }

        /// <summary>
        /// Deserializes a <see cref="WarningDefinitions"/> from a <see cref="JObject"/>.
        /// The conditions section is processed top-down; each condition can
        /// reference earlier ones via <c>{"ref": "Name"}</c>.
        /// </summary>
        public static WarningDefinitions DeserializeDefinitions(JObject obj)
        {
            var readRefs = new Dictionary<string, ICondition>();
            var converter = new ConditionConverter(readRefs, null);
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                Converters = { converter },
            });

            var conditions = new Dictionary<string, ICondition>();
            var conditionsObj = obj["conditions"] as JObject;
            if (conditionsObj != null)
            {
                foreach (var prop in conditionsObj.Properties())
                {
                    var cond = prop.Value.ToObject<ICondition>(serializer);
                    readRefs[prop.Name] = cond;
                    conditions[prop.Name] = cond;
                }
            }

            var rules = new List<WarningRule>();
            var rulesArray = obj["rules"] as JArray;
            if (rulesArray != null)
            {
                rules = rulesArray.ToObject<List<WarningRule>>(serializer);
            }

            return new WarningDefinitions
            {
                Conditions = conditions,
                Rules = rules,
            };
        }
    }
}
