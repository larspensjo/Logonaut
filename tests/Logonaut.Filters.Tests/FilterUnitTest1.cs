using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;

namespace Logonaut.Filters.Tests
{
    [TestClass]
    public class SubstringFilterTests
    {
        [TestMethod]
        public void SubstringFilter_Matches_WhenSubstringPresent()
        {
            var filter = new SubstringFilter("test");
            bool result = filter.IsMatch("This is a test string");
            Assert.IsTrue(result, "The filter should match when the substring is present.");
        }

        [TestMethod]
        public void SubstringFilter_DoesNotMatch_WhenSubstringAbsent()
        {
            var filter = new SubstringFilter("test");
            bool result = filter.IsMatch("This is a string");
            Assert.IsFalse(result, "The filter should not match when the substring is absent.");
        }

        [TestMethod]
        public void SubstringFilter_Disabled_AlwaysMatches()
        {
            var filter = new SubstringFilter("test") { Enabled = false };
            bool result = filter.IsMatch("Any string, regardless of content");
            Assert.IsTrue(result, "A disabled filter should be considered neutral and match.");
        }
    }

    [TestClass]
    public class AndFilterTests
    {
        [TestMethod]
        public void AndFilter_Matches_WhenAllSubFiltersMatch()
        {
            var andFilter = new AndFilter();
            andFilter.Add(new SubstringFilter("test"));
            andFilter.Add(new SubstringFilter("string"));

            bool result = andFilter.IsMatch("This is a test string");
            Assert.IsTrue(result, "All sub-filters match, so the AndFilter should match.");
        }

        [TestMethod]
        public void AndFilter_DoesNotMatch_WhenAnySubFilterDoesNotMatch()
        {
            var andFilter = new AndFilter();
            andFilter.Add(new SubstringFilter("test"));
            andFilter.Add(new SubstringFilter("missing"));

            bool result = andFilter.IsMatch("This is a test string");
            Assert.IsFalse(result, "One sub-filter does not match, so the AndFilter should not match.");
        }

        [TestMethod]
        public void AndFilter_Disabled_AlwaysMatches()
        {
            var andFilter = new AndFilter() { Enabled = false };
            andFilter.Add(new SubstringFilter("test"));
            andFilter.Add(new SubstringFilter("string"));

            bool result = andFilter.IsMatch("Irrelevant content");
            Assert.IsTrue(result, "A disabled filter should be considered neutral and match.");
        }

        [TestMethod]
        public void AndFilter_WithNoSubFilters_MatchesEverything()
        {
            var andFilter = new AndFilter();
            bool result = andFilter.IsMatch("Any string");
            Assert.IsTrue(result, "An AndFilter with no sub-filters should match everything.");
        }
    }

    [TestClass]
    public class OrFilterTests
    {
        [TestMethod]
        public void OrFilter_Matches_WhenAnySubFilterMatches()
        {
            var orFilter = new OrFilter();
            orFilter.Add(new SubstringFilter("test"));
            orFilter.Add(new SubstringFilter("absent"));

            bool result = orFilter.IsMatch("This is a test string");
            Assert.IsTrue(result, "At least one sub-filter matches, so the OrFilter should match.");
        }

        [TestMethod]
        public void OrFilter_DoesNotMatch_WhenNoSubFilterMatches()
        {
            var orFilter = new OrFilter();
            orFilter.Add(new SubstringFilter("notfound"));
            orFilter.Add(new SubstringFilter("missing"));

            bool result = orFilter.IsMatch("This is a test string");
            Assert.IsFalse(result, "No sub-filter matches, so the OrFilter should not match.");
        }

        [TestMethod]
        public void OrFilter_Disabled_AlwaysMatches()
        {
            var orFilter = new OrFilter() { Enabled = false };
            orFilter.Add(new SubstringFilter("test"));
            orFilter.Add(new SubstringFilter("string"));

            bool result = orFilter.IsMatch("Any string");
            Assert.IsTrue(result, "A disabled filter should be considered neutral and match.");
        }

        [TestMethod]
        public void OrFilter_WithNoSubFilters_DoesNotMatch()
        {
            var orFilter = new OrFilter();
            bool result = orFilter.IsMatch("Any string");
            Assert.IsTrue(result, "An OrFilter with no sub-filters should allways match.");
        }
    }

    [TestClass]
    public class NorFilterTests
    {
        [TestMethod]
        public void NorFilter_Matches_WhenNoSubFilterMatches()
        {
            var norFilter = new NorFilter();
            norFilter.Add(new SubstringFilter("notfound"));
            norFilter.Add(new SubstringFilter("missing"));

            bool result = norFilter.IsMatch("This is a test string");
            Assert.IsTrue(result, "No sub-filter matches, so the NorFilter should match.");
        }

        [TestMethod]
        public void NorFilter_DoesNotMatch_WhenAnySubFilterMatches()
        {
            var norFilter = new NorFilter();
            norFilter.Add(new SubstringFilter("test"));
            norFilter.Add(new SubstringFilter("missing"));

            bool result = norFilter.IsMatch("This is a test string");
            Assert.IsFalse(result, "At least one sub-filter matches, so the NorFilter should not match.");
        }

        [TestMethod]
        public void NorFilter_Disabled_AlwaysMatches()
        {
            var norFilter = new NorFilter() { Enabled = false };
            norFilter.Add(new SubstringFilter("test"));
            norFilter.Add(new SubstringFilter("string"));

            bool result = norFilter.IsMatch("Any string");
            Assert.IsTrue(result, "A disabled filter should be considered neutral and match.");
        }

        [TestMethod]
        public void NorFilter_WithNoSubFilters_MatchesEverything()
        {
            var norFilter = new NorFilter();
            bool result = norFilter.IsMatch("Any string");
            Assert.IsTrue(result, "A NorFilter with no sub-filters should match everything.");
        }
    }
}
