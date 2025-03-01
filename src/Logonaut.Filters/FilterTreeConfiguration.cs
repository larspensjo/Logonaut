using Newtonsoft.Json;

// To serialize a filter:
// string json = FilterSerializer.Serialize(myFilterTreeConfiguration);
// Save the JSON string to a file or configuration store.
// To load a saved filter:
// FilterTreeConfiguration config = FilterSerializer.Deserialize(json);

namespace Logonaut.Filters
{
    public class FilterTreeConfiguration
    {
        /// <summary>
        /// A name for this filter configuration.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The number of context lines to display before and after each matching line.
        /// </summary>
        public int ContextLines { get; set; }

        /// <summary>
        /// The root filter of the filter tree.
        /// </summary>
        public IFilter? RootFilter { get; set; }
    }
}
