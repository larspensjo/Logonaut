using Newtonsoft.Json;

namespace Logonaut.Filters
{
    public static class FilterSerializer
    {
        /// <summary>
        /// Serializes a FilterTreeConfiguration to a JSON string.
        /// </summary>
        public static string Serialize(FilterTreeConfiguration config)
        {
            return JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });
        }

        /// <summary>
        /// Deserializes a JSON string into a FilterTreeConfiguration.
        /// </summary>
        public static FilterTreeConfiguration Deserialize(string json)
        {
            var config = JsonConvert.DeserializeObject<FilterTreeConfiguration>(json, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All });

            if (config == null)
            {
                throw new InvalidOperationException("Deserialization resulted in a null FilterTreeConfiguration.");
            }

            return config;
        }
    }
}
