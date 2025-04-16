using Logonaut.Filters;
using Newtonsoft.Json; // Required for JsonProperty if needed explicitly

namespace Logonaut.Common
{
    /// <summary>
    /// Represents a named filter configuration profile.
    /// </summary>
    public class FilterProfile
    {
        /// <summary>
        /// Gets or sets the user-defined name for this filter profile.
        /// </summary>
        public string Name { get; set; } = "Default"; // Default name

        /// <summary>
        /// Gets or sets the root filter node for this profile's hierarchy.
        /// A null value indicates no filter is set.
        /// Uses TypeNameHandling during serialization.
        /// </summary>
        [JsonProperty(TypeNameHandling = TypeNameHandling.All)]
        public IFilter? RootFilter { get; set; }

        // Constructor for easy creation
        public FilterProfile(string name, IFilter? rootFilter = null)
        {
            Name = name;
            RootFilter = rootFilter;
        }

        // Parameterless constructor for deserialization
        public FilterProfile() { }

        // Override ToString for potential simpler binding if VM isn't used everywhere
        public override string ToString() => Name ?? "Unnamed Profile";
    }
}