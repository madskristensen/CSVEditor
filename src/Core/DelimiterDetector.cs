using System;
using System.Collections.Generic;



namespace CSVEditor.Core;

/// <summary>
/// Detects the delimiter used in a CSV file by analyzing the content.
/// </summary>
public static class DelimiterDetector
{
    private static readonly CsvDelimiter[] _candidateDelimiters =
    [
        CsvDelimiter.Comma,
        CsvDelimiter.Tab,
        CsvDelimiter.Semicolon,
        CsvDelimiter.Pipe
    ];

    /// <summary>
    /// Detects the most likely delimiter used in the given CSV content.
    /// </summary>
    /// <param name="content">The CSV content to analyze.</param>
    /// <param name="maxLinesToAnalyze">Maximum number of lines to analyze (default: 10).</param>
    /// <returns>The detected delimiter, or Comma as a fallback.</returns>
    public static CsvDelimiter Detect(string content, int maxLinesToAnalyze = 10)
    {
        if (string.IsNullOrEmpty(content))
            return CsvDelimiter.Comma;

        List<string> lines = GetFirstLines(content, maxLinesToAnalyze);
        if (lines.Count == 0)
            return CsvDelimiter.Comma;

        // Score each delimiter based on consistency across lines
        CsvDelimiter bestDelimiter = CsvDelimiter.Comma;
        var bestScore = -1.0;

        foreach (CsvDelimiter delimiter in _candidateDelimiters)
        {
            var score = ScoreDelimiter(lines, delimiter.ToChar());
            if (score > bestScore)
            {
                bestScore = score;
                bestDelimiter = delimiter;
            }
        }

        return bestDelimiter;
    }

    /// <summary>
    /// Scores a delimiter based on how consistently it splits lines into the same number of fields.
    /// </summary>
    private static double ScoreDelimiter(IReadOnlyList<string> lines, char delimiter)
    {
        if (lines.Count == 0)
            return 0;

        var counts = new List<int>(lines.Count);
        foreach (var line in lines)
        {
            var count = CountFieldsInLine(line, delimiter);
            counts.Add(count);
        }

        // Must have at least 2 fields to be a valid delimiter
        if (counts[0] < 2)
            return 0;

        // Calculate consistency score - use loop instead of LINQ to avoid allocations
        var firstCount = counts[0];
        var consistentLines = 0;
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i] == firstCount)
                consistentLines++;
        }
        var consistencyScore = (double)consistentLines / counts.Count;

        // Bonus for having more fields (comma in text might only split once)
        var fieldCountBonus = Math.Min(firstCount / 10.0, 0.5);

        return consistencyScore + fieldCountBonus;
    }

    /// <summary>
    /// Counts the number of fields in a line, respecting quoted fields.
    /// </summary>
    private static int CountFieldsInLine(string line, char delimiter)
    {
        if (string.IsNullOrEmpty(line))
            return 0;

        var count = 1;
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                // Check for escaped quote
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++; // Skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    private static List<string> GetFirstLines(string content, int maxLines)
    {
        var lines = new List<string>(maxLines);
        var start = 0;

        for (var i = 0; i < content.Length && lines.Count < maxLines; i++)
        {
            if (content[i] == '\n')
            {
                var end = i > 0 && content[i - 1] == '\r' ? i - 1 : i;
                var line = content.Substring(start, end - start);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
                start = i + 1;
            }
        }

        // Add last line if no trailing newline
        if (start < content.Length && lines.Count < maxLines)
        {
            var line = content.Substring(start);
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
