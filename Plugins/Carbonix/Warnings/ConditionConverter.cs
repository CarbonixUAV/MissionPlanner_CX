using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.Warnings
{
    /// <summary>
    /// Provides polymorphic JSON conversion for <see cref="ICondition"/> trees.
    /// </summary>
    /// <remarks>
    /// Dispatches on key presence: "ref", "stateField", "namedValue", "and",
    /// "or", "not", "edge", "latch", "statusText".
    /// Operators use symbols in JSON: &lt;, &lt;=, ==, &gt;, &gt;=, !=
    /// When constructed with ref dictionaries, supports <c>{"ref": "Name"}</c>
    /// for reusable named conditions.
    /// </remarks>
    public class ConditionConverter : JsonConverter<ICondition>
    {
        readonly Dictionary<string, ICondition> _readRefs;
        readonly Dictionary<ICondition, string> _writeRefs;

        public ConditionConverter() { }

        /// <summary>
        /// Creates a converter that resolves and emits condition references.
        /// </summary>
        /// <param name="readRefs">Name-to-instance map for deserializing
        /// <c>{"ref": "Name"}</c>. May be mutated externally between calls
        /// (e.g. as the conditions section is built up).</param>
        /// <param name="writeRefs">Instance-to-name map (reference equality)
        /// for emitting <c>{"ref": "Name"}</c> during serialization.</param>
        public ConditionConverter(
            Dictionary<string, ICondition> readRefs,
            Dictionary<ICondition, string> writeRefs)
        {
            _readRefs = readRefs;
            _writeRefs = writeRefs;
        }

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

            if (obj["ref"] != null)
                return ReadRef(obj);
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
            if (obj["edge"] != null)
                return ReadEdge(obj, serializer);
            if (obj["latch"] != null)
                return ReadLatch(obj, serializer);
            if (obj["statusText"] != null)
                return ReadStatusText(obj);
            if (obj["sustain"] != null)
                return ReadSustain(obj, serializer);
            if (obj["delta"] != null)
                return ReadDelta(obj);

            throw new JsonSerializationException(
                $"Unknown condition type. Expected one of: ref, stateField, namedValue, statusText, and, or, not, edge, latch, sustain, delta. " +
                $"Found keys: {string.Join(", ", obj.Properties().Select(p => p.Name))}");
        }

        public override void WriteJson(JsonWriter writer, ICondition value,
            JsonSerializer serializer)
        {
            if (_writeRefs != null && _writeRefs.TryGetValue(value, out var refName))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("ref");
                writer.WriteValue(refName);
                writer.WriteEndObject();
                return;
            }

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
                case StatusTextCondition st:
                    WriteStatusText(writer, st);
                    break;
                case EdgeCondition edge:
                    WriteEdge(writer, edge, serializer);
                    break;
                case LatchCondition latch:
                    WriteLatch(writer, latch, serializer);
                    break;
                case SustainCondition sustain:
                    WriteSustain(writer, sustain, serializer);
                    break;
                case DeltaCondition delta:
                    WriteDelta(writer, delta);
                    break;
                default:
                    throw new JsonSerializationException(
                        $"Unknown condition type: {value.GetType().Name}");
            }
        }

        ICondition ReadRef(JObject obj)
        {
            var name = obj["ref"].Value<string>();
            if (_readRefs == null || !_readRefs.TryGetValue(name, out var resolved))
                throw new JsonSerializationException(
                    $"Unknown condition ref '{name}'. " +
                    "Ensure it is defined before use in the conditions section.");
            return resolved;
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

            double? clear = obj["clear"]?.Value<double>();

            return Condition.NamedValue(name, op, threshold, clear);
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

        static ICondition ReadStatusText(JObject obj)
        {
            var pattern = obj["statusText"].Value<string>();
            var clearPattern = obj["clearText"]?.Value<string>();
            var timeoutMs = obj["timeoutMs"]?.Value<int>();
            return Condition.StatusText(pattern, clearPattern, timeoutMs);
        }

        static void WriteStatusText(JsonWriter writer, StatusTextCondition st)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("statusText");
            writer.WriteValue(st.FirePattern.ToString());
            if (st.ClearPattern != null)
            {
                writer.WritePropertyName("clearText");
                writer.WriteValue(st.ClearPattern.ToString());
            }
            if (st.TimeoutMs.HasValue)
            {
                writer.WritePropertyName("timeoutMs");
                writer.WriteValue(st.TimeoutMs.Value);
            }
            writer.WriteEndObject();
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
            if (cc.ClearThreshold.HasValue)
            {
                writer.WritePropertyName("clear");
                writer.WriteValue(cc.ClearThreshold.Value);
            }
            writer.WriteEndObject();
        }

        void WriteComposite(JsonWriter writer, string key,
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

        static ICondition ReadEdge(JObject obj, JsonSerializer serializer)
        {
            var inner = obj["edge"].ToObject<ICondition>(serializer);
            return new EdgeCondition(inner);
        }

        static ICondition ReadLatch(JObject obj, JsonSerializer serializer)
        {
            var latchObj = obj["latch"] as JObject;
            if (latchObj == null || latchObj["set"] == null || latchObj["clear"] == null)
                throw new JsonSerializationException(
                    "'latch' must be an object with 'set' and 'clear' properties");

            var set = latchObj["set"].ToObject<ICondition>(serializer);
            var clear = latchObj["clear"].ToObject<ICondition>(serializer);
            return new LatchCondition(set, clear);
        }

        static void WriteEdge(JsonWriter writer, EdgeCondition edge,
            JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("edge");
            serializer.Serialize(writer, edge.Inner);
            writer.WriteEndObject();
        }

        static void WriteLatch(JsonWriter writer, LatchCondition latch,
            JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("latch");
            writer.WriteStartObject();
            writer.WritePropertyName("set");
            serializer.Serialize(writer, latch.Set);
            writer.WritePropertyName("clear");
            serializer.Serialize(writer, latch.Clear);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        static ICondition ReadSustain(JObject obj, JsonSerializer serializer)
        {
            var riseMs = obj["riseMs"]?.Value<int>()
                ?? throw new JsonSerializationException("'sustain' requires 'riseMs'");
            var fallMs = obj["fallMs"]?.Value<int>()
                ?? throw new JsonSerializationException("'sustain' requires 'fallMs'");
            var inner = obj["sustain"].ToObject<ICondition>(serializer);
            var reset = obj["reset"]?.ToObject<ICondition>(serializer);
            return new SustainCondition(inner, riseMs, fallMs, reset);
        }

        static void WriteSustain(JsonWriter writer, SustainCondition sustain,
            JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("sustain");
            serializer.Serialize(writer, sustain.Inner);
            writer.WritePropertyName("riseMs");
            writer.WriteValue(sustain.RiseMs);
            writer.WritePropertyName("fallMs");
            writer.WriteValue(sustain.FallMs);
            if (sustain.Reset != null)
            {
                writer.WritePropertyName("reset");
                serializer.Serialize(writer, sustain.Reset);
            }
            writer.WriteEndObject();
        }

        static ICondition ReadDelta(JObject obj)
        {
            var deltaObj = obj["delta"] as JObject;
            if (deltaObj == null || deltaObj["left"] == null || deltaObj["right"] == null)
                throw new JsonSerializationException(
                    "'delta' must be an object with 'left' and 'right' properties");

            var opStr = obj["op"]?.Value<string>()
                ?? throw new JsonSerializationException("'delta' requires 'op'");
            if (!SymbolToOp.TryGetValue(opStr, out var op))
                throw new JsonSerializationException(
                    $"Unknown operator '{opStr}'. Expected one of: {string.Join(", ", SymbolToOp.Keys)}");

            var threshold = obj["value"]?.Value<double>()
                ?? throw new JsonSerializationException("'delta' requires 'value'");
            double? clear = obj["clear"]?.Value<double>();

            ReadValueRef(deltaObj["left"] as JObject, out var leftName, out var leftSource);
            ReadValueRef(deltaObj["right"] as JObject, out var rightName, out var rightSource);

            return Condition.Delta(leftName, leftSource, rightName, rightSource,
                op, threshold, clear);
        }

        static void ReadValueRef(JObject obj, out string name, out ValueSource source)
        {
            if (obj == null)
                throw new JsonSerializationException("Delta value ref must be an object");

            if (obj["stateField"] != null)
            {
                name = obj["stateField"].Value<string>();
                source = ValueSource.StateField;
            }
            else if (obj["namedValue"] != null)
            {
                name = obj["namedValue"].Value<string>();
                source = ValueSource.NamedValue;
            }
            else
            {
                throw new JsonSerializationException(
                    "Delta value ref must have 'stateField' or 'namedValue'");
            }
        }

        static void WriteDelta(JsonWriter writer, DeltaCondition dc)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("delta");
            writer.WriteStartObject();
            writer.WritePropertyName("left");
            WriteValueRef(writer, dc.LeftName, dc.LeftSource);
            writer.WritePropertyName("right");
            WriteValueRef(writer, dc.RightName, dc.RightSource);
            writer.WriteEndObject();
            writer.WritePropertyName("op");
            writer.WriteValue(OpToSymbol[dc.Op]);
            writer.WritePropertyName("value");
            writer.WriteValue(dc.Threshold);
            if (dc.ClearThreshold.HasValue)
            {
                writer.WritePropertyName("clear");
                writer.WriteValue(dc.ClearThreshold.Value);
            }
            writer.WriteEndObject();
        }

        static void WriteValueRef(JsonWriter writer, string name, ValueSource source)
        {
            writer.WriteStartObject();
            if (source == ValueSource.NamedValue)
                writer.WritePropertyName("namedValue");
            else
                writer.WritePropertyName("stateField");
            writer.WriteValue(name);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Flattens nested And/Or chains into arrays for cleaner JSON.
        /// And(And(a, b), c) becomes [a, b, c].
        /// </summary>
        List<ICondition> Flatten(ICondition condition, string key)
        {
            var result = new List<ICondition>();
            FlattenInto(condition, key, result);
            return result;
        }

        void FlattenInto(ICondition condition, string key, List<ICondition> result)
        {
            // Don't flatten through a named ref — emit it as a single ref node
            if (_writeRefs != null && _writeRefs.ContainsKey(condition))
            {
                result.Add(condition);
                return;
            }

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

        /// <summary>
        /// Compares <see cref="ICondition"/> instances by reference identity.
        /// Used for the write-refs dictionary so that only the exact same
        /// object (not a structural copy) emits a <c>{"ref": "..."}</c>.
        /// </summary>
        internal class ReferenceEqualityComparer : IEqualityComparer<ICondition>
        {
            public static readonly ReferenceEqualityComparer Instance =
                new ReferenceEqualityComparer();

            public bool Equals(ICondition x, ICondition y)
                => ReferenceEquals(x, y);

            public int GetHashCode(ICondition obj)
                => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
