using CSVEditor.Core;

namespace CSVEditor.Test;

[TestClass]
public sealed class CsvParserTests
{
    [TestMethod]
    public void Parse_SimpleCSV_ReturnsCorrectRowCount()
    {
        var content = "A,B,C\n1,2,3\n4,5,6";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);
        Assert.AreEqual(3, doc.Count);
    }

    [TestMethod]
    public void Parse_SimpleCSV_ReturnsCorrectColumnCount()
    {
        var content = "A,B,C\n1,2,3";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);
        Assert.AreEqual(3, doc[0].Count);
        Assert.AreEqual(3, doc[1].Count);
    }

    [TestMethod]
    public void Parse_SimpleCSV_ReturnsCorrectValues()
    {
        var content = "Name,Age\nJohn,30";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual("Name", doc[0][0].Value);
        Assert.AreEqual("Age", doc[0][1].Value);
        Assert.AreEqual("John", doc[1][0].Value);
        Assert.AreEqual("30", doc[1][1].Value);
    }

    [TestMethod]
    public void Parse_QuotedFields_ReturnsUnquotedValue()
    {
        var content = "\"Hello\",\"World\"";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual("Hello", doc[0][0].Value);
        Assert.AreEqual("World", doc[0][1].Value);
        Assert.IsTrue(doc[0][0].IsQuoted);
        Assert.IsTrue(doc[0][1].IsQuoted);
    }

    [TestMethod]
    public void Parse_QuotedFieldWithComma_ReturnsCorrectValue()
    {
        var content = "\"Doe, John\",30";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(2, doc[0].Count);
        Assert.AreEqual("Doe, John", doc[0][0].Value);
        Assert.AreEqual("30", doc[0][1].Value);
    }

    [TestMethod]
    public void Parse_EscapedQuotes_ReturnsCorrectValue()
    {
        var content = "\"He said \"\"Hello\"\"\"";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual("He said \"Hello\"", doc[0][0].Value);
    }

    [TestMethod]
    public void Parse_EmptyFields_ReturnsEmptyStrings()
    {
        var content = "A,,C";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(3, doc[0].Count);
        Assert.AreEqual("A", doc[0][0].Value);
        Assert.AreEqual("", doc[0][1].Value);
        Assert.AreEqual("C", doc[0][2].Value);
    }

    [TestMethod]
    public void Parse_TrailingNewline_DoesNotCreateExtraRow()
    {
        var content = "A,B\n1,2\n";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);
        Assert.AreEqual(2, doc.Count);
    }

    [TestMethod]
    public void Parse_WindowsLineEndings_ParsesCorrectly()
    {
        var content = "A,B\r\n1,2\r\n3,4";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(3, doc.Count);
        Assert.AreEqual("A", doc[0][0].Value);
        Assert.AreEqual("1", doc[1][0].Value);
        Assert.AreEqual("3", doc[2][0].Value);
    }

    [TestMethod]
    public void Parse_CellSpans_AreCorrect()
    {
        var content = "ABC,DE,F";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        // "ABC" starts at 0, length 3
        Assert.AreEqual(0, doc[0][0].Span.Start);
        Assert.AreEqual(3, doc[0][0].Span.Length);

        // "DE" starts at 4, length 2
        Assert.AreEqual(4, doc[0][1].Span.Start);
        Assert.AreEqual(2, doc[0][1].Span.Length);

        // "F" starts at 7, length 1
        Assert.AreEqual(7, doc[0][2].Span.Start);
        Assert.AreEqual(1, doc[0][2].Span.Length);
    }

    [TestMethod]
    public void Parse_AutoDetectsDelimiter()
    {
        var content = "A\tB\tC\n1\t2\t3";
        CsvDocument doc = CsvParser.Parse(content);

        Assert.AreEqual('\t', doc.Delimiter);
        Assert.AreEqual(3, doc[0].Count);
    }

    [TestMethod]
    public void Parse_ColumnNames_ExtractedFromHeader()
    {
        var content = "Name,Age,City\nJohn,30,NYC";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.HasCount(3, doc.ColumnNames);
        Assert.AreEqual("Name", doc.ColumnNames[0]);
        Assert.AreEqual("Age", doc.ColumnNames[1]);
        Assert.AreEqual("City", doc.ColumnNames[2]);
    }

    [TestMethod]
    public void GetCellAtPosition_ReturnsCorrectCell()
    {
        var content = "ABC,DEF";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        // Position 1 is in "ABC"
        CsvCell cell1 = doc.GetCellAtPosition(1);
        Assert.IsNotNull(cell1);
        Assert.AreEqual("ABC", cell1.Value);
        Assert.AreEqual(0, cell1.ColumnIndex);

        // Position 5 is in "DEF"
        CsvCell cell2 = doc.GetCellAtPosition(5);
        Assert.IsNotNull(cell2);
        Assert.AreEqual("DEF", cell2.Value);
        Assert.AreEqual(1, cell2.ColumnIndex);
    }

    [TestMethod]
    public void GetColumnName_ReturnsHeaderValue()
    {
        var content = "Name,Age\nJohn,30";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual("Name", doc.GetColumnName(0));
        Assert.AreEqual("Age", doc.GetColumnName(1));
    }

    [TestMethod]
    public void GetColumnName_ReturnsGeneratedName_WhenOutOfRange()
    {
        var content = "A,B\n1,2";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual("Column 3", doc.GetColumnName(2));
        Assert.AreEqual("Column 10", doc.GetColumnName(9));
    }

    [TestMethod]
    public void Parse_TrailingEmptyColumns_ReturnsCorrectColumnCount()
    {
        // Reproduce issue #1: rows with trailing empty columns should still have correct column count
        var content = "A,B,C\n1,2,3\n4,5,\n,,6";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        // All rows should have 3 columns
        Assert.AreEqual(3, doc[0].Count, "Header row should have 3 columns");
        Assert.AreEqual(3, doc[1].Count, "Row 1 should have 3 columns");
        Assert.AreEqual(3, doc[2].Count, "Row 2 should have 3 columns (with trailing empty)");
        Assert.AreEqual(3, doc[3].Count, "Row 3 should have 3 columns");

        // Verify the values
        Assert.AreEqual("4", doc[2][0].Value);
        Assert.AreEqual("5", doc[2][1].Value);
        Assert.AreEqual("", doc[2][2].Value, "Last column in row 2 should be empty string");

        Assert.AreEqual("", doc[3][0].Value, "First column in row 3 should be empty string");
        Assert.AreEqual("", doc[3][1].Value, "Second column in row 3 should be empty string");
        Assert.AreEqual("6", doc[3][2].Value);
    }

    [TestMethod]
    public void Parse_MultiLineQuotedField_ReturnsCorrectRowCount()
    {
        var content = "Col 1,Col 2,Col 3\nValue 1,\"Value \n2\",Value 3\nValue 4,Value 5,Value 6";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(3, doc.Count, "Multi-line quoted field should not create extra rows");
    }

    [TestMethod]
    public void Parse_MultiLineQuotedField_ReturnsCorrectValues()
    {
        var content = "Col 1,Col 2,Col 3\nValue 1,\"Value \n2\",Value 3\nValue 4,Value 5,Value 6";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual("Value 1", doc[1][0].Value);
        Assert.AreEqual("Value \n2", doc[1][1].Value, "Multi-line value should preserve line break");
        Assert.AreEqual("Value 3", doc[1][2].Value);
        Assert.IsTrue(doc[1][1].IsQuoted);
    }

    [TestMethod]
    public void Parse_MultiLineQuotedFieldWithCRLF_ReturnsCorrectValues()
    {
        var content = "Col 1,Col 2,Col 3\r\nValue 1,\"Value \r\n2\",Value 3\r\nValue 4,Value 5,Value 6";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(3, doc.Count);
        Assert.AreEqual("Value \r\n2", doc[1][1].Value, "Multi-line value should preserve CRLF");
    }

    [TestMethod]
    public void Parse_MultiLineQuotedFieldSpanningMultipleLines_ReturnsCorrectValues()
    {
        var content = "A,B\n\"line1\nline2\nline3\",value";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(2, doc.Count);
        Assert.AreEqual("line1\nline2\nline3", doc[1][0].Value);
        Assert.AreEqual("value", doc[1][1].Value);
    }

    [TestMethod]
    public void Parse_MultiLineQuotedFieldWithEscapedQuotes_ReturnsCorrectValues()
    {
        var content = "A,B\n\"line1\n\"\"quoted\"\"\nline3\",value";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(2, doc.Count);
        Assert.AreEqual("line1\n\"quoted\"\nline3", doc[1][0].Value);
    }

    [TestMethod]
    public void Parse_MultiLineQuotedFieldColumnCount_IsCorrect()
    {
        var content = "Col 1,Col 2,Col 3\nValue 1,\"Value \n2\",Value 3\nValue 4,Value 5,Value 6";
        CsvDocument doc = CsvParser.Parse(content, CsvDelimiter.Comma);

        Assert.AreEqual(3, doc[0].Count, "Header row should have 3 columns");
        Assert.AreEqual(3, doc[1].Count, "Row with multi-line field should have 3 columns");
        Assert.AreEqual(3, doc[2].Count, "Row after multi-line field should have 3 columns");
    }
}
