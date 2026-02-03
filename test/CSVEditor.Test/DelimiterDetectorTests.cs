using CSVEditor.Core;

namespace CSVEditor.Test;

[TestClass]
public sealed class DelimiterDetectorTests
{
    [TestMethod]
    public void Detect_CommaDelimited_ReturnsComma()
    {
        var content = "Name,Age,City\nJohn,30,NYC\nJane,25,LA";
        CsvDelimiter result = DelimiterDetector.Detect(content);
        Assert.AreEqual(CsvDelimiter.Comma, result);
    }

    [TestMethod]
    public void Detect_TabDelimited_ReturnsTab()
    {
        var content = "Name\tAge\tCity\nJohn\t30\tNYC\nJane\t25\tLA";
        CsvDelimiter result = DelimiterDetector.Detect(content);
        Assert.AreEqual(CsvDelimiter.Tab, result);
    }

    [TestMethod]
    public void Detect_SemicolonDelimited_ReturnsSemicolon()
    {
        var content = "Name;Age;City\nJohn;30;NYC\nJane;25;LA";
        CsvDelimiter result = DelimiterDetector.Detect(content);
        Assert.AreEqual(CsvDelimiter.Semicolon, result);
    }

    [TestMethod]
    public void Detect_PipeDelimited_ReturnsPipe()
    {
        var content = "Name|Age|City\nJohn|30|NYC\nJane|25|LA";
        CsvDelimiter result = DelimiterDetector.Detect(content);
        Assert.AreEqual(CsvDelimiter.Pipe, result);
    }

    [TestMethod]
    public void Detect_EmptyContent_ReturnsCommaDefault()
    {
        CsvDelimiter result = DelimiterDetector.Detect("");
        Assert.AreEqual(CsvDelimiter.Comma, result);
    }

    [TestMethod]
    public void Detect_QuotedFieldsWithCommas_ReturnsCorrectDelimiter()
    {
        var content = "Name;Address;City\n\"Doe, John\";\"123 Main St\";NYC";
        CsvDelimiter result = DelimiterDetector.Detect(content);
        Assert.AreEqual(CsvDelimiter.Semicolon, result);
    }

    [TestMethod]
    public void Detect_InconsistentRows_ChoosesMostConsistent()
    {
        // Tab is more consistent here
        var content = "A\tB\tC\n1\t2\t3\n4\t5\t6";
        CsvDelimiter result = DelimiterDetector.Detect(content);
        Assert.AreEqual(CsvDelimiter.Tab, result);
    }
}
