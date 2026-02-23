using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Provides polymorphic JSON conversion for <see cref="ICondition"/> trees.
    /// </summary>
    /// <remarks>
    /// Dispatches on key presence: "stateField", "namedValue", "and", "or", "not".
    /// Operators use symbols in JSON: &lt;, &lt;=, ==, &gt;, &gt;=, !=
    /// </remarks>
    public class ConditionConverter : JsonConverter<ICondition>
    {
        static readonly Dictionary<string, CompareOp> SymbolToOp =
            new Dictionary<string, CompareOp>
            {
                { "<",  CompareOp.LT },
                { "<=", CompareOp.LTEQ },
                { "==", CompareOp.EQ },
                { ">",  CompareOp.GT },
                { ">=", CompareOp.GTEQ },
                { "!=", CompareOp.NEQ },
            };

        static readonly Dictionary<CompareOp, string> OpToSymbol =
            SymbolToOp.ToDictionary(kv => kv.Value, kv => kv.Key);

        public override ICondition ReadJson(JsonReader reader, Type objectType,
            ICondition existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);

            if (obj["stateField"] != null)
                return ReadField(obj);
            if (obj["namedValue"] != null)
                return ReadNamedValue(obj);
            if (obj["and"] != null)
                return ReadComposite(obj, "and", serializer);
            if (obj["or"] != null)
                return ReadComposite(obj, "or", serializer);
            if (obj["not"] != null)
                return ReadNot(obj, serializer);

            throw new JsonSerializationException(
                $"Unknown condition type. Expected one of: stateField, namedValue, and, or, not. " +
                $"Found keys: {string.Join(", ", obj.Properties().Select(p => p.Name))}");
        }

        public override void WriteJson(JsonWriter writer, ICondition value,
            JsonSerializer serializer)
        {
            switch (value)
            {
                case CompareCondition cc:
                    if (cc.ValueSource == ValueSource.NamedValue)
                        WriteNamedValue(writer, cc);
                    else
                        WriteField(writer, cc);
                    break;
                case AndCondition and:
                    WriteComposite(writer, "and", and, serializer);
                    break;
                case OrCondition or:
                    WriteComposite(writer, "or", or, serializer);
                    break;
                case NotCondition not:
                    WriteNot(writer, not, serializer);
                    break;
                default:
                    throw new JsonSerializationException(
                        $"Unknown condition type: {value.GetType().Name}");
            }
        }

        static ICondition ReadField(JObject obj)
        {
            var name = obj["stateField"].Value<string>();
            var opStr = obj["op"].Value<string>();
            var threshold = obj["value"].Value<double>();

            if (!SymbolToOp.TryGetValue(opStr, out var op))
                throw new JsonSerializationException(
                    $"Unknown operator '{opStr}'. Expected one of: {string.Join(", ", SymbolToOp.Keys)}");

            double? clear = obj["clear"]?.Value<double>();

            return Condition.Field(name, op, threshold, clear);
        }

        static ICondition ReadNamedValue(JObject obj)
        {
            var name = obj["namedValue"].Value<string>();
            var opStr = obj["op"].Value<string>();
            var threshold = obj["value"].Value<double>();

            if (!SymbolToOp.TryGetValue(opStr, out var op))
                throw new JsonSerializationException(
                    $"Unknown operator '{opStr}'. Expected one of: {string.Join(", ", SymbolToOp.Keys)}");

            return Condition.NamedValue(name, op, threshold);
        }

        static ICondition ReadComposite(JObject obj, string key, JsonSerializer serializer)
        {
            var items = obj[key] as JArray;
            if (items == null || items.Count < 2)
                throw new JsonSerializationException(
                    $"'{key}' must be an array with at least 2 elements");

            var conditions = items
                .Select(i => i.ToObject<ICondition>(serializer))
                .ToList();

            if (key == "and")
                return conditions.Aggregate((a, b) => new AndCondition(a, b));
            else
                return conditions.Aggregate((a, b) => new OrCondition(a, b));
        }

        static ICondition ReadNot(JObject obj, JsonSerializer serializer)
        {
            var inner = obj["not"].ToObject<ICondition>(serializer);
            return new NotCondition(inner);
        }

        static void WriteField(JsonWriter writer, CompareCondition cc)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("stateField");
            writer.WriteValue(cc.Name);
            writer.WritePropertyName("op");
            writer.WriteValue(OpToSymbol[cc.Op]);
            writer.WritePropertyName("value");
            writer.WriteValue(cc.Threshold);
            if (cc.ClearThreshold.HasValue)
            {
                writer.WritePropertyName("clear");
                writer.WriteValue(cc.ClearThreshold.Value);
            }
            writer.WriteEndObject();
        }

        static void WriteNamedValue(JsonWriter writer, CompareCondition cc)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("namedValue");
            writer.WriteValue(cc.Name);
            writer.WritePropertyName("op");
            writer.WriteValue(OpToSymbol[cc.Op]);
            writer.WritePropertyName("value");
            writer.WriteValue(cc.Threshold);
            writer.WriteEndObject();
        }

        static void WriteComposite(JsonWriter writer, string key,
            ICondition condition, JsonSerializer serializer)
        {
            var items = Flatten(condition, key);

            writer.WriteStartObject();
            writer.WritePropertyName(key);
            writer.WriteStartArray();
            foreach (var item in items)
                serializer.Serialize(writer, item);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        static void WriteNot(JsonWriter writer, NotCondition not,
            JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("not");
            serializer.Serialize(writer, not.Inner);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Flattens nested And/Or chains into arrays for cleaner JSON.
        /// And(And(a, b), c) becomes [a, b, c].
        /// </summary>
        static List<ICondition> Flatten(ICondition condition, string key)
        {
            var result = new List<ICondition>();
            FlattenInto(condition, key, result);
            return result;
        }

        static void FlattenInto(ICondition condition, string key, List<ICondition> result)
        {
            if (key == "and" && condition is AndCondition and)
            {
                FlattenInto(and.Left, key, result);
                FlattenInto(and.Right, key, result);
            }
            else if (key == "or" && condition is OrCondition or)
            {
                FlattenInto(or.Left, key, result);
                FlattenInto(or.Right, key, result);
            }
            else
            {
                result.Add(condition);
            }
        }
    }
}
