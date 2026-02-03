using CSVEditor.Core;

namespace CSVEditor.Test;

[TestClass]
public class CsvColumnTypeDetectorTests
{
    #region Integer Detection

    [TestMethod]
    public void DetectType_WithIntegers_ReturnsInteger()
    {
        var values = new List<string> { "1", "42", "-5", "1000", "0" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Integer, result);
    }

    [TestMethod]
    public void DetectType_WithNegativeIntegers_ReturnsInteger()
    {
        var values = new List<string> { "-1", "-42", "-100", "-999", "0" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Integer, result);
    }

    [TestMethod]
    public void DetectType_WithThousandSeparators_ReturnsInteger()
    {
        var values = new List<string> { "1,000", "10,000", "1,000,000" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Integer, result);
    }

    #endregion

    #region Decimal Detection

    [TestMethod]
    public void DetectType_WithDecimals_ReturnsDecimal()
    {
        var values = new List<string> { "1.5", "3.14", "-0.5", "100.00" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Decimal, result);
    }

    [TestMethod]
    public void DetectType_WithMixedIntegersAndDecimals_ReturnsDecimal()
    {
        var values = new List<string> { "1", "2.5", "3", "4.0", "5" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        // All are valid decimals, but only some are integers
        Assert.AreEqual(CsvDataType.Decimal, result);
    }

    #endregion

    #region Email Detection

    [TestMethod]
    public void DetectType_WithEmails_ReturnsEmail()
    {
        var values = new List<string>
        {
            "user@example.com",
            "test.user@domain.org",
            "admin@company.co.uk"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Email, result);
    }

    [TestMethod]
    public void DetectType_WithEmailsContainingPlus_ReturnsEmail()
    {
        var values = new List<string>
        {
            "user+tag@example.com",
            "test+filter@domain.org"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Email, result);
    }

    #endregion

    #region Phone Detection

    [TestMethod]
    public void DetectType_WithInternationalPhones_ReturnsPhone()
    {
        var values = new List<string>
        {
            "+1-210-782-2664",
            "+44 20 7946 0958",
            "+33 1 23 45 67 89"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Phone, result);
    }

    [TestMethod]
    public void DetectType_WithParenthesesPhones_ReturnsPhone()
    {
        var values = new List<string>
        {
            "(638)693-9337",
            "(212) 555-1234",
            "(800) 123-4567"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Phone, result);
    }

    [TestMethod]
    public void DetectType_WithExtensionPhones_ReturnsPhone()
    {
        var values = new List<string>
        {
            "5551234567x100",
            "555-123-4567 ext. 456",
            "5551234567X200"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Phone, result);
    }

    [TestMethod]
    public void DetectType_WithAmbiguousNumbers_DoesNotReturnPhone()
    {
        // Numbers without clear phone indicators should NOT be detected as phones
        var values = new List<string>
        {
            "308.514.0641",  // Could be version, IP-like
            "123-456-789",   // No area code parens or plus
            "1234567890"     // Just digits
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreNotEqual(CsvDataType.Phone, result);
    }

    #endregion

    #region Date vs Phone (Regression Tests)

    [TestMethod]
    public void DetectType_WithISODates_ReturnsDate_NotPhone()
    {
        // This was previously misdetected as Phone
        var values = new List<string>
        {
            "2020-09-01",
            "2021-12-25",
            "2023-01-15"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Date, result);
    }

    [TestMethod]
    public void DetectType_WithSlashDates_ReturnsDate_NotPhone()
    {
        var values = new List<string>
        {
            "09/01/2020",
            "12/25/2021",
            "01/15/2023"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Date, result);
    }

    [TestMethod]
    public void DetectType_WithDotDates_ReturnsDate_NotPhone()
    {
        var values = new List<string>
        {
            "2020.09.01",
            "2021.12.25",
            "2023.01.15"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Date, result);
    }

    #endregion

    #region DateTime Detection

    [TestMethod]
    public void DetectType_WithDateTimes_ReturnsDateTime()
    {
        var values = new List<string>
        {
            "2024-01-15 10:30:00",
            "2023-12-25 08:00:00",
            "2022-06-01 14:45:30"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.DateTime, result);
    }

    [TestMethod]
    public void DetectType_WithDatesOnly_ReturnsDate()
    {
        var values = new List<string> { "2024-01-15", "2023-12-25", "2022-06-01" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Date, result);
    }

    #endregion

    #region Boolean Detection

    [TestMethod]
    public void DetectType_WithBooleans_ReturnsBoolean()
    {
        var values = new List<string> { "true", "false", "True", "FALSE", "yes", "no" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Boolean, result);
    }

    [TestMethod]
    public void DetectType_WithYesNo_ReturnsBoolean()
    {
        var values = new List<string> { "yes", "no", "YES", "NO", "Yes", "No" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Boolean, result);
    }

    [TestMethod]
    public void DetectType_WithOnOff_ReturnsBoolean()
    {
        var values = new List<string> { "on", "off", "ON", "OFF" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Boolean, result);
    }

    #endregion

    #region URL Detection

    [TestMethod]
    public void DetectType_WithHttpsUrls_ReturnsUrl()
    {
        var values = new List<string>
        {
            "https://example.com",
            "https://test.org/page",
            "https://www.domain.com/path?query=1"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Url, result);
    }

    [TestMethod]
    public void DetectType_WithHttpUrls_ReturnsUrl()
    {
        var values = new List<string>
        {
            "http://example.com",
            "http://test.org/page"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Url, result);
    }

    [TestMethod]
    public void DetectType_WithWwwUrls_ReturnsUrl()
    {
        var values = new List<string>
        {
            "www.example.com",
            "www.test.org/page"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Url, result);
    }

    #endregion

    #region GUID Detection

    [TestMethod]
    public void DetectType_WithGuids_ReturnsGuid()
    {
        var values = new List<string>
        {
            "550e8400-e29b-41d4-a716-446655440000",
            "6ba7b810-9dad-11d1-80b4-00c04fd430c8",
            "f47ac10b-58cc-4372-a567-0e02b2c3d479"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Guid, result);
    }

    [TestMethod]
    public void DetectType_WithGuidsNoBraces_ReturnsGuid()
    {
        var values = new List<string>
        {
            "550e8400e29b41d4a716446655440000",
            "{550e8400-e29b-41d4-a716-446655440000}"
        };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Guid, result);
    }

    #endregion

    #region Currency Detection

    [TestMethod]
    public void DetectType_WithDollarCurrency_ReturnsCurrency()
    {
        var values = new List<string> { "$100", "$1,234.56", "$50.00" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Currency, result);
    }

    [TestMethod]
    public void DetectType_WithEuroCurrency_ReturnsCurrency()
    {
        var values = new List<string> { "€100", "€1,234.56", "€50.00" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Currency, result);
    }

    [TestMethod]
    public void DetectType_WithPoundCurrency_ReturnsCurrency()
    {
        var values = new List<string> { "£100", "£1,234.56", "£50.00" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Currency, result);
    }

    #endregion

    #region Percentage Detection

    [TestMethod]
    public void DetectType_WithPercentages_ReturnsPercentage()
    {
        var values = new List<string> { "50%", "25.5%", "100%", "-10%" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Percentage, result);
    }

    [TestMethod]
    public void DetectType_WithPercentagesWithSpace_ReturnsPercentage()
    {
        var values = new List<string> { "50 %", "25.5 %", "100 %" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Percentage, result);
    }

    #endregion

    #region Text and Unknown Detection

    [TestMethod]
    public void DetectType_WithMixedText_ReturnsText()
    {
        var values = new List<string> { "Hello", "World", "Test", "Random Text" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Text, result);
    }

    [TestMethod]
    public void DetectType_WithEmptyList_ReturnsUnknown()
    {
        var values = new List<string>();
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Unknown, result);
    }

    [TestMethod]
    public void DetectType_WithNullList_ReturnsUnknown()
    {
        CsvDataType result = CsvColumnTypeDetector.DetectType(null);
        Assert.AreEqual(CsvDataType.Unknown, result);
    }

    [TestMethod]
    public void DetectType_WithOnlyEmptyStrings_ReturnsUnknown()
    {
        var values = new List<string> { "", "  ", null, "" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Unknown, result);
    }

    #endregion

    #region Confidence Threshold Tests

    [TestMethod]
    public void DetectType_WithMostlyIntegers_ReturnsInteger()
    {
        // 80% threshold - 8 out of 10 should be enough
        var values = new List<string> { "1", "2", "3", "4", "5", "6", "7", "8", "text", "9" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Integer, result);
    }

    [TestMethod]
    public void DetectType_WithTooFewMatches_ReturnsText()
    {
        // Less than 80% integers - should fall back to text
        var values = new List<string> { "1", "2", "text", "more text", "hello" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Text, result);
    }

    [TestMethod]
    public void DetectType_WithEmptyValuesIgnored_StillDetectsType()
    {
        // Empty values should be ignored in confidence calculation
        var values = new List<string> { "1", "", "2", null, "3", "  ", "4", "5" };
        CsvDataType result = CsvColumnTypeDetector.DetectType(values);
        Assert.AreEqual(CsvDataType.Integer, result);
    }

    #endregion

    #region GetTypeName Tests

    [TestMethod]
    public void GetTypeName_ReturnsCorrectNames()
    {
        Assert.AreEqual("Integer", CsvColumnTypeDetector.GetTypeName(CsvDataType.Integer));
        Assert.AreEqual("Decimal", CsvColumnTypeDetector.GetTypeName(CsvDataType.Decimal));
        Assert.AreEqual("Boolean", CsvColumnTypeDetector.GetTypeName(CsvDataType.Boolean));
        Assert.AreEqual("Date", CsvColumnTypeDetector.GetTypeName(CsvDataType.Date));
        Assert.AreEqual("DateTime", CsvColumnTypeDetector.GetTypeName(CsvDataType.DateTime));
        Assert.AreEqual("Email", CsvColumnTypeDetector.GetTypeName(CsvDataType.Email));
        Assert.AreEqual("Phone", CsvColumnTypeDetector.GetTypeName(CsvDataType.Phone));
        Assert.AreEqual("URL", CsvColumnTypeDetector.GetTypeName(CsvDataType.Url));
        Assert.AreEqual("Currency", CsvColumnTypeDetector.GetTypeName(CsvDataType.Currency));
        Assert.AreEqual("Percentage", CsvColumnTypeDetector.GetTypeName(CsvDataType.Percentage));
        Assert.AreEqual("GUID", CsvColumnTypeDetector.GetTypeName(CsvDataType.Guid));
        Assert.AreEqual("Text", CsvColumnTypeDetector.GetTypeName(CsvDataType.Text));
        Assert.AreEqual("Unknown", CsvColumnTypeDetector.GetTypeName(CsvDataType.Unknown));
    }

    #endregion
}
