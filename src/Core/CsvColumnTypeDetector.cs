using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CSVEditor.Core;

/// <summary>
/// Detects the data type of a CSV column by analyzing sample values.
/// </summary>
public static class CsvColumnTypeDetector
{
    // Regex patterns for specific types (compiled for performance)
    private static readonly Regex _emailPattern = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled);

    // Phone patterns - must have clear phone indicators to avoid false positives
    // We're conservative here due to international format variety
    private static readonly Regex _phoneWithPlusPattern = new(
        @"^\+\d[\d\s\-\.]{6,}$",
        RegexOptions.Compiled);

    // Requires actual parentheses around area code - not optional
    private static readonly Regex _phoneWithParensPattern = new(
        @"^\(\d{2,4}\)[\s\-\.]?\d{2,4}[\s\-\.]?\d{2,}",
        RegexOptions.Compiled);

    private static readonly Regex _phoneWithExtPattern = new(
        @"\d{7,}.*[xX]\.?\d+$|ext\.?\s*\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern to exclude date-like strings from phone detection
    private static readonly Regex _dateLikePattern = new(
        @"^\d{4}[-/\.]\d{1,2}[-/\.]\d{1,2}$|^\d{1,2}[-/\.]\d{1,2}[-/\.]\d{4}$|^\d{8}$",
        RegexOptions.Compiled);

    private static readonly Regex _urlPattern = new(
        @"^(https?://|www\.)[^\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _currencyPattern = new(
        @"^[$€£¥₹]?\s*-?[\d,]+\.?\d*$|^-?[\d,]+\.?\d*\s*[$€£¥₹]$",
        RegexOptions.Compiled);

    private static readonly Regex _percentagePattern = new(
        @"^-?\d+\.?\d*\s*%$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> _booleanValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "yes", "no", "y", "n", "1", "0", "on", "off"
    };

    /// <summary>
    /// Minimum confidence threshold (0.0 to 1.0) for type detection.
    /// </summary>
    private const double _confidenceThreshold = 0.80;

    /// <summary>
    /// Detects the data type of a column based on sample values.
    /// </summary>
    /// <param name="values">Sample values from the column (excluding header).</param>
    /// <returns>The detected data type.</returns>
    public static CsvDataType DetectType(IReadOnlyList<string> values)
    {
        if (values == null || values.Count == 0)
            return CsvDataType.Unknown;

        // Count non-empty values and matches for each type
        var nonEmptyCount = 0;
        var counts = new Dictionary<CsvDataType, int>
        {
            { CsvDataType.Integer, 0 },
            { CsvDataType.Decimal, 0 },
            { CsvDataType.Boolean, 0 },
            { CsvDataType.Date, 0 },
            { CsvDataType.DateTime, 0 },
            { CsvDataType.Email, 0 },
            { CsvDataType.Phone, 0 },
            { CsvDataType.Url, 0 },
            { CsvDataType.Currency, 0 },
            { CsvDataType.Percentage, 0 },
            { CsvDataType.Guid, 0 }
        };

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            nonEmptyCount++;
            var trimmed = value.Trim();

            // Check each type (order matters - more specific first)
            if (IsGuid(trimmed)) counts[CsvDataType.Guid]++;
            if (IsEmail(trimmed)) counts[CsvDataType.Email]++;
            if (IsUrl(trimmed)) counts[CsvDataType.Url]++;
            if (IsPhone(trimmed)) counts[CsvDataType.Phone]++;
            if (IsPercentage(trimmed)) counts[CsvDataType.Percentage]++;
            if (IsCurrency(trimmed)) counts[CsvDataType.Currency]++;
            if (IsBoolean(trimmed)) counts[CsvDataType.Boolean]++;
            if (IsDateTime(trimmed, out var hasTime))
            {
                if (hasTime)
                    counts[CsvDataType.DateTime]++;
                else
                    counts[CsvDataType.Date]++;
            }
            if (IsInteger(trimmed)) counts[CsvDataType.Integer]++;
            if (IsDecimal(trimmed)) counts[CsvDataType.Decimal]++;
        }

        if (nonEmptyCount == 0)
            return CsvDataType.Unknown;

        // Find the best matching type with priority order
        // More specific types take precedence
        CsvDataType[] typeOrder = new[]
        {
            CsvDataType.Email,      // Most specific patterns first
            CsvDataType.Url,
            CsvDataType.Phone,
            CsvDataType.Guid,
            CsvDataType.Percentage,
            CsvDataType.Currency,
            CsvDataType.Boolean,
            CsvDataType.DateTime,
            CsvDataType.Date,
            CsvDataType.Integer,    // Integers before decimals
            CsvDataType.Decimal
        };

        foreach (CsvDataType type in typeOrder)
        {
            var confidence = (double)counts[type] / nonEmptyCount;
            if (confidence >= _confidenceThreshold)
            {
                return type;
            }
        }

        return CsvDataType.Text;
    }

    /// <summary>
    /// Gets a human-readable name for a data type.
    /// </summary>
    public static string GetTypeName(CsvDataType type)
    {
        return type switch
        {
            CsvDataType.Integer => "Integer",
            CsvDataType.Decimal => "Decimal",
            CsvDataType.Boolean => "Boolean",
            CsvDataType.Date => "Date",
            CsvDataType.DateTime => "DateTime",
            CsvDataType.Email => "Email",
            CsvDataType.Phone => "Phone",
            CsvDataType.Url => "URL",
            CsvDataType.Currency => "Currency",
            CsvDataType.Percentage => "Percentage",
            CsvDataType.Guid => "GUID",
            CsvDataType.Text => "Text",
            _ => "Unknown"
        };
    }

    private static bool IsInteger(string value)
    {
        return long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture, out _);
    }

    private static bool IsDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out _);
    }

    private static bool IsBoolean(string value)
    {
        return _booleanValues.Contains(value);
    }

    private static bool IsDateTime(string value, out bool hasTime)
    {
        hasTime = false;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime dt))
        {
            // Check if the value contains time components
            hasTime = value.Contains(":") || dt.TimeOfDay != TimeSpan.Zero;
            return true;
        }

        // Try additional date formats
        var dateFormats = new[]
        {
            "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "dd-MM-yyyy",
            "yyyy/MM/dd", "yyyyMMdd"
        };

        if (DateTime.TryParseExact(value, dateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _))
        {
            hasTime = false;
            return true;
        }

        return false;
    }

    private static bool IsEmail(string value)
    {
        return value.Length <= 254 && _emailPattern.IsMatch(value);
    }

    private static bool IsPhone(string value)
    {
        // Exclude date-like patterns first
        if (_dateLikePattern.IsMatch(value))
            return false;

        // Check digit count (international phone numbers: 7-15 digits)
        var digitsOnly = Regex.Replace(value, @"[^\d]", "");
        if (digitsOnly.Length < 7 || digitsOnly.Length > 15)
            return false;

        // Require at least one strong phone indicator to avoid false positives:
        // 1. Starts with + (international format)
        if (_phoneWithPlusPattern.IsMatch(value))
            return true;

        // 2. Has parentheses (area code format)
        if (_phoneWithParensPattern.IsMatch(value))
            return true;

        // 3. Has extension (x123, ext. 456)
        if (_phoneWithExtPattern.IsMatch(value))
            return true;

        // Without strong indicators, we can't be confident it's a phone number
        // It could be a date, ID, version number, etc.
        return false;
    }

    private static bool IsUrl(string value)
    {
        return _urlPattern.IsMatch(value) ||
               Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsCurrency(string value)
    {
        if (!_currencyPattern.IsMatch(value))
            return false;

        // Must have a currency symbol to be currency (not just a number)
        return value.IndexOfAny(new[] { '$', '€', '£', '¥', '₹' }) >= 0;
    }

    private static bool IsPercentage(string value)
    {
        return _percentagePattern.IsMatch(value);
    }

    private static bool IsGuid(string value)
    {
        return Guid.TryParse(value, out _);
    }
}
