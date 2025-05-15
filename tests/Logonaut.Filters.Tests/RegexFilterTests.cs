using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Newtonsoft.Json; // Add for serialization tests

namespace Logonaut.Filters.Tests;
[TestClass] public class RegexFilterTests
{
    [TestMethod] public void RegexFilter_Matches_WhenPatternPresent()
    {
        // Arrange
        var filter = new RegexFilter(@"\d+"); // Match one or more digits

        // Act
        bool result1 = filter.IsMatch("This string contains 123 numbers.");
        bool result2 = filter.IsMatch("Line with 9.");

        // Assert
        Assert.IsTrue(result1, "Should match when the regex pattern is found.");
        Assert.IsTrue(result2, "Should match even a single digit.");
    }

    [TestMethod] public void RegexFilter_DoesNotMatch_WhenPatternAbsent()
    {
        // Arrange
        var filter = new RegexFilter(@"\d+"); // Match one or more digits

        // Act
        bool result = filter.IsMatch("This string contains no digits.");

        // Assert
        Assert.IsFalse(result, "Should not match when the regex pattern is not found.");
    }

    [TestMethod] public void RegexFilter_CaseInsensitive_ByDefault()
    {
        // Arrange
        var filter = new RegexFilter("test"); // isCaseSensitive defaults to false

        // Act
        bool resultLower = filter.IsMatch("This is a test string");
        bool resultUpper = filter.IsMatch("This is a TEST string");
        bool resultMixed = filter.IsMatch("This is a TeSt string");

        // Assert
        Assert.IsTrue(resultLower, "Should match lowercase (case-insensitive).");
        Assert.IsTrue(resultUpper, "Should match uppercase (case-insensitive).");
        Assert.IsTrue(resultMixed, "Should match mixed case (case-insensitive).");
    }

    [TestMethod] public void RegexFilter_CaseSensitive_WhenSpecified()
    {
        // Arrange
        var filter = new RegexFilter("test", isCaseSensitive: true);

        // Act
        bool resultLower = filter.IsMatch("This is a test string");
        bool resultUpper = filter.IsMatch("This is a TEST string");
        bool resultMixed = filter.IsMatch("This is a TeSt string");

        // Assert
        Assert.IsTrue(resultLower, "Should match exact case.");
        Assert.IsFalse(resultUpper, "Should not match different case (uppercase).");
        Assert.IsFalse(resultMixed, "Should not match different case (mixed case).");
    }

    [TestMethod] public void RegexFilter_ChangingCaseSensitivity_UpdatesMatch()
    {
        // Arrange
        var filter = new RegexFilter("CaseTest", isCaseSensitive: true);
        string upperLine = "CASeTeST";
        string lowerLine = "casetest";
        string exactLine = "CaseTest";

        // Assert (Initial state: case-sensitive)
        Assert.IsTrue(filter.IsMatch(exactLine), "Initial sensitive match failed.");
        Assert.IsFalse(filter.IsMatch(upperLine), "Initial sensitive mismatch (upper) failed.");
        Assert.IsFalse(filter.IsMatch(lowerLine), "Initial sensitive mismatch (lower) failed.");

        // Act: Change to case-insensitive
        filter.IsCaseSensitive = false;

        // Assert (New state: case-insensitive)
        Assert.IsTrue(filter.IsMatch(exactLine), "Insensitive match (exact) failed.");
        Assert.IsTrue(filter.IsMatch(upperLine), "Insensitive match (upper) failed.");
        Assert.IsTrue(filter.IsMatch(lowerLine), "Insensitive match (lower) failed.");

        // Act: Change back to case-sensitive
        filter.IsCaseSensitive = true;

        // Assert (Back to state: case-sensitive)
        Assert.IsTrue(filter.IsMatch(exactLine), "Restored sensitive match failed.");
        Assert.IsFalse(filter.IsMatch(upperLine), "Restored sensitive mismatch (upper) failed.");
        Assert.IsFalse(filter.IsMatch(lowerLine), "Restored sensitive mismatch (lower) failed.");
    }


    [TestMethod] public void RegexFilter_Disabled_AlwaysMatches()
    {
        // Arrange
        var filter = new RegexFilter("important pattern") { Enabled = false };

        // Act
        bool resultNoMatch = filter.IsMatch("This string has nothing relevant.");
        bool resultWithMatch = filter.IsMatch("This has the important pattern.");

        // Assert
        Assert.IsTrue(resultNoMatch, "A disabled filter should match even if the pattern isn't present.");
        Assert.IsTrue(resultWithMatch, "A disabled filter should match even if the pattern is present.");
    }

    [TestMethod] public void RegexFilter_WithEmptyPattern_MatchesNothing()
    {
        var filter = new RegexFilter("");
        Assert.IsFalse(filter.IsMatch("Any string"), "Empty pattern should not match.");
        Assert.IsFalse(filter.IsMatch(""), "Empty pattern should not match empty string.");
    }

    [TestMethod] public void RegexFilter_InvalidPatternInConstructor_IsMatchReturnsFalse()
    {
        var filter = new RegexFilter("[invalid regex"); 
        bool result = filter.IsMatch("some text");
        Assert.IsFalse(result, "Filter with invalid regex should not match anything.");
    }

    [TestMethod] public void RegexFilter_SettingInvalidPattern_IsMatchReturnsFalse()
    {
        var filter = new RegexFilter("valid");
        Assert.IsTrue(filter.IsMatch("valid text"), "Initial valid filter should match.");
        filter.Value = "[invalid regex";
        Assert.IsFalse(filter.IsMatch("some text"), "Filter should not match after setting invalid regex.");
        Assert.IsFalse(filter.IsMatch("valid text"), "Filter should not match previous pattern after setting invalid regex.");
    }

    [TestMethod] public void RegexFilter_SettingValidPatternAfterInvalid_IsMatchWorks()
    {
        var filter = new RegexFilter("[invalid regex");
        Assert.IsFalse(filter.IsMatch("some text"), "Initial invalid filter should not match.");
        filter.Value = @"\d+"; 
        Assert.IsTrue(filter.IsMatch("text with 123"), "Filter should match after setting valid regex.");
        Assert.IsFalse(filter.IsMatch("text without digits"), "Filter should not match non-digit text.");
    }

    [TestMethod] public void RegexFilter_Properties_ReturnCorrectValues()
    {
        var filter = new RegexFilter("myPattern");
        Assert.IsTrue(filter.IsEditable, "RegexFilter should be editable.");
        Assert.AreEqual("/myPattern/", filter.DisplayText, "DisplayText should format correctly.");
        Assert.AreEqual("RegexType", filter.TypeText, "TypeText should be 'RegexType'.");
        Assert.AreEqual("myPattern", filter.Value, "Value should return the pattern.");
    }

    [TestMethod] public void RegexFilter_ValueProperty_UpdatesPatternAndDisplayText()
    {
        var filter = new RegexFilter("old");
        filter.Value = "new pattern";
        Assert.AreEqual("new pattern", filter.Value, "Value should be updated.");
        Assert.AreEqual("/new pattern/", filter.DisplayText, "DisplayText should reflect the new value.");
        Assert.IsTrue(filter.IsMatch("A new pattern here"), "Matching should use the new pattern.");
        Assert.IsFalse(filter.IsMatch("This has the old pattern"), "Matching should not use the old pattern.");
    }

    [TestMethod] public void RegexFilter_DefaultHighlightColorKey_IsCorrect()
    {
        var filter = new RegexFilter("test");
        Assert.AreEqual("FilterHighlight.Default", filter.HighlightColorKey, "Default HighlightColorKey is incorrect.");
    }

    [TestMethod] public void RegexFilter_CanSetHighlightColorKey()
    {
        var filter = new RegexFilter("test");
        filter.HighlightColorKey = "FilterHighlight.Blue";
        Assert.AreEqual("FilterHighlight.Blue", filter.HighlightColorKey, "HighlightColorKey was not set correctly.");
    }

    [TestMethod] public void RegexFilter_Serialization_PreservesHighlightColorKey()
    {
        var originalFilter = new RegexFilter("pattern", true, "FilterHighlight.Yellow");
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };

        string json = JsonConvert.SerializeObject(originalFilter, settings);
        var deserializedFilter = JsonConvert.DeserializeObject<RegexFilter>(json, settings);

        Assert.IsNotNull(deserializedFilter);
        Assert.AreEqual(originalFilter.Value, deserializedFilter.Value);
        Assert.AreEqual(originalFilter.IsCaseSensitive, deserializedFilter.IsCaseSensitive);
        Assert.AreEqual(originalFilter.HighlightColorKey, deserializedFilter.HighlightColorKey, "HighlightColorKey was not preserved.");
    }
    
    [TestMethod] public void RegexFilter_JsonConstructor_HandlesNullHighlightColorKey()
    {
        // Simulate deserialization where highlightColorKey might be missing in older JSON
        string jsonMissingColor = @"{""Value"":""testValue"", ""IsCaseSensitive"":false}"; // HighlightColorKey is missing
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        var deserializedFilter = JsonConvert.DeserializeObject<RegexFilter>(jsonMissingColor, settings);

        Assert.IsNotNull(deserializedFilter);
        Assert.AreEqual("testValue", deserializedFilter.Value);
        Assert.AreEqual("FilterHighlight.Default", deserializedFilter.HighlightColorKey, "Deserialized filter with missing color key should default.");

        string jsonWithNullColor = @"{""Value"":""testValue"", ""IsCaseSensitive"":false, ""HighlightColorKey"":null}"; // Explicitly null
        deserializedFilter = JsonConvert.DeserializeObject<RegexFilter>(jsonWithNullColor, settings);
        Assert.IsNotNull(deserializedFilter);
        Assert.AreEqual("FilterHighlight.Default", deserializedFilter.HighlightColorKey, "Deserialized filter with null color key should default.");
    }
}
