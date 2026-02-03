using System;
using System.Collections.Generic;
using System.Globalization;

namespace CSVEditor.Core;

/// <summary>
/// Detects whether a CSV file has a header row.
/// </summary>
public static class CsvHeaderDetector
{
    /// <summary>
    /// Analyzes the CSV data to determine if the first row is a header.
    /// </summary>
    /// <param name="rows">Parsed CSV rows (including potential header).</param>
    /// <returns>True if the first row appears to be a header, false otherwise.</returns>
    public static bool HasHeader(IReadOnlyList<CsvRow> rows)
    {
        if (rows == null || rows.Count < 2)
            return false; // Need at least 2 rows to determine

        CsvRow firstRow = rows[0];
        if (firstRow.Count == 0)
            return false;

        // Collect data rows (skip first row)
        var dataRows = new List<CsvRow>(Math.Min(rows.Count - 1, 100));
        for (var i = 1; i < rows.Count && dataRows.Count < 100; i++)
        {
            if (rows[i].Count > 0)
                dataRows.Add(rows[i]);
        }

        if (dataRows.Count == 0)
            return false;

        // Check multiple signals
        var score = 0;
        var maxScore = 0;

        // Signal 1: First row values don't match detected column types (weight: 3)
        maxScore += 3;
        if (FirstRowMismatchesDataTypes(firstRow, dataRows))
            score += 3;

        // Signal 2: First row is all text while data has non-text types (weight: 2)
        maxScore += 2;
        if (FirstRowIsAllTextButDataIsNot(firstRow, dataRows))
            score += 2;

        // Signal 3: First row has no duplicates (headers should be unique) (weight: 1)
        maxScore += 1;
        if (FirstRowHasNoDuplicates(firstRow))
            score += 1;

        // Signal 4: First row values look like identifiers/names (weight: 1)
        maxScore += 1;
        if (FirstRowLooksLikeHeaders(firstRow))
            score += 1;

        // Require at least 50% confidence
        return score >= maxScore / 2.0;
    }

    /// <summary>
    /// Overload that works with raw string data.
    /// </summary>
    public static bool HasHeader(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows == null || rows.Count < 2)
            return false;

        // Use a simplified analysis that doesn't require CsvRow
        return HasHeaderFromStrings(rows);
    }

    private static bool HasHeaderFromStrings(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        IReadOnlyList<string> firstRow = rows[0];
        if (firstRow.Count == 0)
            return false;

        // Collect first row values
        var headerValues = new List<string>(firstRow.Count);
        foreach (var val in firstRow)
            headerValues.Add(val ?? "");

        // Collect data values per column
        var columnValues = new List<List<string>>(firstRow.Count);
        for (var col = 0; col < firstRow.Count; col++)
            columnValues.Add(new List<string>());

        var sampleCount = Math.Min(rows.Count, 101);
        for (var i = 1; i < sampleCount; i++)
        {
            for (var col = 0; col < rows[i].Count && col < firstRow.Count; col++)
            {
                var val = rows[i][col];
                if (!string.IsNullOrWhiteSpace(val))
                    columnValues[col].Add(val);
            }
        }

        // Check signals
        var score = 0;
        var maxScore = 7;

        // Signal 1: First row values don't match detected column types
        var mismatchCount = 0;
        var checkCount = 0;
        for (var col = 0; col < firstRow.Count; col++)
        {
            if (columnValues[col].Count < 3)
                continue;

            CsvDataType detectedType = CsvColumnTypeDetector.DetectType(columnValues[col]);
            if (detectedType == CsvDataType.Text || detectedType == CsvDataType.Unknown)
                continue;

            checkCount++;
            if (!ValueMatchesType(headerValues[col]?.Trim() ?? "", detectedType))
                mismatchCount++;
        }
        if (checkCount > 0 && mismatchCount >= checkCount * 0.6)
            score += 3;

        // Signal 2: First row is all text while data has non-text
        var firstRowAllText = true;
        foreach (var val in headerValues)
        {
            if (IsNumeric(val) || IsDate(val) || IsBooleanValue(val))
            {
                firstRowAllText = false;
                break;
            }
        }
        if (firstRowAllText)
        {
            var dataHasNonText = false;
            foreach (List<string> colVals in columnValues)
            {
                foreach (var val in colVals)
                {
                    if (IsNumeric(val) || IsDate(val) || IsBooleanValue(val))
                    {
                        dataHasNonText = true;
                        break;
                    }
                }
                if (dataHasNonText) break;
            }
            if (dataHasNonText)
                score += 2;
        }

        // Signal 3: First row has no duplicates
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasDuplicates = false;
        foreach (var val in headerValues)
        {
            if (!string.IsNullOrWhiteSpace(val) && !seen.Add(val))
            {
                hasDuplicates = true;
                break;
            }
        }
        if (!hasDuplicates && seen.Count > 0)
            score += 1;

        // Signal 4: First row values look like identifiers
        var headerLikeCount = 0;
        var nonEmptyCount = 0;
        foreach (var val in headerValues)
        {
            if (string.IsNullOrWhiteSpace(val))
                continue;
            nonEmptyCount++;
            if (val.Length < 50 && !ContainsMostlyDigits(val))
                headerLikeCount++;
        }
        if (nonEmptyCount > 0 && headerLikeCount >= nonEmptyCount * 0.8)
            score += 1;

        return score >= maxScore / 2.0;
    }

    private static bool FirstRowMismatchesDataTypes(CsvRow firstRow, IReadOnlyList<CsvRow> dataRows)
    {
        var mismatchCount = 0;
        var checkCount = 0;

        for (var col = 0; col < firstRow.Count; col++)
        {
            // Collect values for this column from data rows
            var columnValues = new List<string>(dataRows.Count);
            foreach (CsvRow row in dataRows)
            {
                if (col < row.Count && !string.IsNullOrWhiteSpace(row[col].Value))
                {
                    columnValues.Add(row[col].Value);
                }
            }

            if (columnValues.Count < 3)
                continue; // Need enough data to detect type

            CsvDataType detectedType = CsvColumnTypeDetector.DetectType(columnValues);

            // Skip if data type is Text or Unknown (can't determine mismatch)
            if (detectedType == CsvDataType.Text || detectedType == CsvDataType.Unknown)
                continue;

            checkCount++;
            var headerValue = firstRow[col].Value?.Trim() ?? "";

            // Check if header value matches the detected type
            if (!ValueMatchesType(headerValue, detectedType))
            {
                mismatchCount++;
            }
        }

        // If most columns have type mismatches, it's likely a header
        return checkCount > 0 && mismatchCount >= checkCount * 0.6;
    }

    private static bool FirstRowIsAllTextButDataIsNot(CsvRow firstRow, IReadOnlyList<CsvRow> dataRows)
    {
        // Check if first row is all text (non-numeric, non-date, etc.)
        var firstRowAllText = true;
        foreach (CsvCell cell in firstRow)
        {
            var value = cell.Value?.Trim() ?? "";
            if (string.IsNullOrEmpty(value))
                continue;

            if (IsNumeric(value) || IsDate(value) || IsBooleanValue(value))
            {
                firstRowAllText = false;
                break;
            }
        }

        if (!firstRowAllText)
            return false;

        // Check if data rows have non-text values
        var dataHasNonText = false;
        foreach (CsvRow row in dataRows)
        {
            foreach (CsvCell cell in row)
            {
                var value = cell.Value?.Trim() ?? "";
                if (string.IsNullOrEmpty(value))
                    continue;

                if (IsNumeric(value) || IsDate(value) || IsBooleanValue(value))
                {
                    dataHasNonText = true;
                    break;
                }
            }
            if (dataHasNonText)
                break;
        }

        return dataHasNonText;
    }

    private static bool FirstRowHasNoDuplicates(CsvRow firstRow)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CsvCell cell in firstRow)
        {
            var value = cell.Value?.Trim() ?? "";
            if (string.IsNullOrEmpty(value))
                continue;

            if (!seen.Add(value))
                return false; // Duplicate found
        }
        return seen.Count > 0;
    }

    private static bool FirstRowLooksLikeHeaders(CsvRow firstRow)
    {
        var headerLikeCount = 0;
        var checkCount = 0;

        foreach (CsvCell cell in firstRow)
        {
            var value = cell.Value?.Trim() ?? "";
            if (string.IsNullOrEmpty(value))
                continue;

            checkCount++;

            // Headers typically:
            // - Are relatively short (< 50 chars)
            // - Don't contain digits (or very few)
            // - Use common naming patterns (underscores, camelCase, spaces)
            if (value.Length < 50 && !ContainsMostlyDigits(value))
            {
                headerLikeCount++;
            }
        }

        return checkCount > 0 && headerLikeCount >= checkCount * 0.8;
    }

    private static bool ValueMatchesType(string value, CsvDataType type)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true; // Empty matches anything

        return type switch
        {
            CsvDataType.Integer => long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out _),
            CsvDataType.Decimal => decimal.TryParse(value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out _),
            CsvDataType.Boolean => IsBooleanValue(value),
            CsvDataType.Date or CsvDataType.DateTime => DateTime.TryParse(value,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            CsvDataType.Email => value.Contains("@") && value.Contains("."),
            CsvDataType.Guid => Guid.TryParse(value, out _),
            _ => true
        };
    }

    private static bool IsNumeric(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool IsBooleanValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var lower = value.ToLowerInvariant();
        return lower == "true" || lower == "false" ||
               lower == "yes" || lower == "no" ||
               lower == "y" || lower == "n" ||
               lower == "1" || lower == "0" ||
               lower == "on" || lower == "off";
    }

    private static bool ContainsMostlyDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var digitCount = 0;
        foreach (var c in value)
        {
            if (char.IsDigit(c))
                digitCount++;
        }

        return digitCount > value.Length * 0.5;
    }
}
