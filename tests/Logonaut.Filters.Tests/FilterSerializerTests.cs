using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;

namespace Logonaut.Filters.Tests
{
    [TestClass]
    public class FilterSerializerTests
    {
        [TestMethod]
        public void SerializeAndDeserialize_FilterConfiguration_ShouldPreserveValues()
        {
            // Create a filter configuration with a name, context lines, and a root filter.
            var config = new FilterTreeConfiguration
            {
                Name = "Test Filter Config",
                ContextLines = 5,
                RootFilter = new AndFilter()
            };

            // Add sub-filters to the root AndFilter.
            // First, a simple substring filter.
            var andFilter = config.RootFilter as AndFilter;
            Assert.IsNotNull(andFilter, "Root filter should be an AndFilter.");
            andFilter.Add(new SubstringFilter("A"));

            // Then, a negation filter wrapping a substring filter.
            var negation = new NegationFilter(new SubstringFilter("B"));
            andFilter.Add(negation);

            // Serialize the filter configuration to JSON.
            string json = FilterSerializer.Serialize(config);
            Assert.IsFalse(string.IsNullOrWhiteSpace(json), "Serialized JSON should not be empty.");

            // Deserialize the JSON back to a FilterTreeConfiguration.
            var deserializedConfig = FilterSerializer.Deserialize(json);
            Assert.IsNotNull(deserializedConfig, "Deserialized configuration should not be null.");
            Assert.AreEqual(config.Name, deserializedConfig.Name, "Names should match.");
            Assert.AreEqual(config.ContextLines, deserializedConfig.ContextLines, "ContextLines should match.");

            // Verify the root filter type.
            Assert.IsInstanceOfType(deserializedConfig.RootFilter, typeof(AndFilter), "Root filter should be an AndFilter.");
            var deserializedAndFilter = deserializedConfig.RootFilter as AndFilter;
            Assert.IsNotNull(deserializedAndFilter, "Filter expected.");
            Assert.AreEqual(2, deserializedAndFilter.SubFilters.Count, "AndFilter should contain 2 sub-filters.");

            // Validate the first sub-filter is a SubstringFilter with the expected value.
            Assert.IsInstanceOfType(deserializedAndFilter.SubFilters[0], typeof(SubstringFilter), "First sub-filter should be a SubstringFilter.");
            var substringFilter = deserializedAndFilter.SubFilters[0] as SubstringFilter;
            Assert.IsNotNull(substringFilter, "Filter expected.");
            Assert.AreEqual("A", substringFilter.Substring, "Substring value should be 'A'.");

            // Validate the second sub-filter is a NegationFilter wrapping a SubstringFilter with value "B".
            Assert.IsInstanceOfType(deserializedAndFilter.SubFilters[1], typeof(NegationFilter), "Second sub-filter should be a NegationFilter.");
            var negationFilter = deserializedAndFilter.SubFilters[1] as NegationFilter;
            Assert.IsNotNull(negationFilter, "Filter expected.");
            Assert.IsNotNull(negationFilter.InnerFilter, "NegationFilter should contain an inner filter.");
            Assert.IsInstanceOfType(negationFilter.InnerFilter, typeof(SubstringFilter), "Inner filter should be a SubstringFilter.");
            var innerSubstring = negationFilter.InnerFilter as SubstringFilter;
            Assert.IsNotNull(innerSubstring, "Filter expected.");
            Assert.AreEqual("B", innerSubstring.Substring, "Inner substring value should be 'B'.");
        }
    }
}
