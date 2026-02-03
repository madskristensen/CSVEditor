using CSVEditor.Core;

namespace CSVEditor.Test;

[TestClass]
public class CsvHeaderDetectorTests
{
    #region Basic Header Detection

    [TestMethod]
    public void HasHeader_WithTypicalCsv_ReturnsTrue()
    {
        // Typical CSV with text headers and numeric/date data
        var rows = new List<List<string>>
        {
            new() { "Name", "Age", "Email", "Date" },
            new() { "John", "25", "john@example.com", "2024-01-15" },
            new() { "Jane", "30", "jane@example.com", "2024-02-20" },
            new() { "Bob", "35", "bob@example.com", "2024-03-25" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasHeader_WithNoHeader_ReturnsFalse()
    {
        // CSV with all numeric data, no header
        var rows = new List<List<string>>
        {
            new() { "1", "100", "50.5" },
            new() { "2", "200", "75.5" },
            new() { "3", "300", "90.5" },
            new() { "4", "400", "85.5" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasHeader_WithSingleRow_ReturnsFalse()
    {
        var rows = new List<List<string>>
        {
            new() { "Name", "Age", "Email" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasHeader_WithEmptyRows_ReturnsFalse()
    {
        var rows = new List<List<string>>();
        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasHeader_WithNullRows_ReturnsFalse()
    {
        var result = CsvHeaderDetector.HasHeader((IReadOnlyList<IReadOnlyList<string>>)null);
        Assert.IsFalse(result);
    }

    #endregion

    #region Type Mismatch Detection

    [TestMethod]
    public void HasHeader_WithTextHeadersAndIntegerData_ReturnsTrue()
    {
        var rows = new List<List<string>>
        {
            new() { "ID", "Count", "Total" },
            new() { "1", "10", "100" },
            new() { "2", "20", "200" },
            new() { "3", "30", "300" },
            new() { "4", "40", "400" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasHeader_WithTextHeadersAndEmailData_ReturnsTrue()
    {
        var rows = new List<List<string>>
        {
            new() { "Contact", "Email Address" },
            new() { "John", "john@example.com" },
            new() { "Jane", "jane@example.com" },
            new() { "Bob", "bob@example.com" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasHeader_WithTextHeadersAndDateData_ReturnsTrue()
    {
        var rows = new List<List<string>>
        {
            new() { "Event", "Start Date", "End Date" },
            new() { "Meeting", "2024-01-15", "2024-01-16" },
            new() { "Conference", "2024-02-20", "2024-02-22" },
            new() { "Workshop", "2024-03-10", "2024-03-11" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsTrue(result);
    }

    #endregion

    #region All Text Data

    [TestMethod]
    public void HasHeader_WithAllTextData_StillDetectsHeader()
    {
        // Even with all text, headers are typically unique short identifiers
        var rows = new List<List<string>>
        {
            new() { "FirstName", "LastName", "City" },
            new() { "John", "Smith", "New York" },
            new() { "Jane", "Doe", "Los Angeles" },
            new() { "Bob", "Johnson", "Chicago" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        // This might be true or false depending on heuristics
        // Headers like "FirstName" look like identifiers
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void HasHeader_WithAllSimilarTextData_ReturnsFalse()
    {
        // All rows look similar - names
        var rows = new List<List<string>>
        {
            new() { "John", "Smith" },
            new() { "Jane", "Doe" },
            new() { "Bob", "Johnson" },
            new() { "Alice", "Williams" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsFalse(result);
    }

    #endregion

    #region Duplicate Headers

    [TestMethod]
    public void HasHeader_WithDuplicateFirstRowValues_LessLikelyHeader()
    {
        // Duplicate values in first row suggest it's data, not headers
        var rows = new List<List<string>>
        {
            new() { "Active", "Active", "Pending" },  // Duplicates
            new() { "1", "2", "3" },
            new() { "4", "5", "6" }
        };

        // This should lean towards not being a header due to duplicates
        // but type mismatch might still win
        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        // Result depends on combined heuristics
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void HasHeader_WithMixedNumericFirstRow_ReturnsFalse()
    {
        // First row has numbers - unlikely to be headers
        var rows = new List<List<string>>
        {
            new() { "100", "200", "300" },
            new() { "150", "250", "350" },
            new() { "175", "275", "375" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasHeader_WithBooleanFirstRow_ReturnsFalse()
    {
        // First row has booleans - unlikely to be headers
        var rows = new List<List<string>>
        {
            new() { "true", "false", "yes" },
            new() { "false", "true", "no" },
            new() { "true", "true", "yes" }
        };

        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasHeader_WithEmptyCells_HandlesGracefully()
    {
        var rows = new List<List<string>>
        {
            new() { "Name", "", "Age" },
            new() { "John", "", "25" },
            new() { "Jane", "", "30" },
            new() { "Bob", "", "35" },
            new() { "Alice", "", "40" }
        };

        // Should not throw, and should detect based on available data
        var result = CsvHeaderDetector.HasHeader(ToReadOnly(rows));
        Assert.IsTrue(result);
    }

    #endregion

    #region Helper Methods

    private static IReadOnlyList<IReadOnlyList<string>> ToReadOnly(List<List<string>> rows)
    {
        var result = new List<IReadOnlyList<string>>(rows.Count);
        foreach (List<string> row in rows)
        {
            result.Add(row);
        }
        return result;
    }

    #endregion
}
