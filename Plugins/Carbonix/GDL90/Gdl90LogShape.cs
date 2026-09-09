using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Carbonix.GDL90
{
    /// <summary>
    /// Provides the projection of encoder inputs into the fields the frame log records.
    /// </summary>
    public static class Gdl90LogShape
    {
        static readonly JsonSerializer Serializer = JsonSerializer.Create(
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                DefaultValueHandling = DefaultValueHandling.Include,
            });

        /// <summary>Projects a message into the fields the frame log records.</summary>
        /// <param name="message">The message to describe, or null for no fields.</param>
        /// <returns>The message's values under the names it declares for them.</returns>
        public static JObject Describe(IGdl90Message message)
        {
            return message == null ? new JObject() : JObject.FromObject(message, Serializer);
        }

        /// <summary>
        /// Describes an ADS-B target that was received and not forwarded: the report it
        /// would have made, and the reason it was not sent.
        /// </summary>
        /// <param name="result">The target and the reason it was dropped, or null for no fields.</param>
        /// <returns>The record's fields.</returns>
        public static JObject Drop(Gdl90TrafficResult result)
        {
            if (result == null) return new JObject();

            var fields = Describe(Gdl90Traffic.BuildReport(result.Vehicle, result.Age, result.ThreatLevel));
            fields["reason"] = result.DropReason;
            return fields;
        }
    }
}
