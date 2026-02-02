using System;



namespace CSVEditor.Core;

/// <summary>
/// Supported CSV delimiter types.
/// </summary>
public enum CsvDelimiter
{
    Comma,
    Tab,
    Semicolon,
    Pipe,
    Colon
}

/// <summary>
/// Extension methods for CsvDelimiter.
/// </summary>
public static class CsvDelimiterExtensions
{
    public static char ToChar(this CsvDelimiter delimiter) => delimiter switch
    {
        CsvDelimiter.Comma => ',',
        CsvDelimiter.Tab => '\t',
        CsvDelimiter.Semicolon => ';',
        CsvDelimiter.Pipe => '|',
        CsvDelimiter.Colon => ':',
        _ => throw new ArgumentOutOfRangeException(nameof(delimiter))
    };

    public static string ToDisplayName(this CsvDelimiter delimiter) => delimiter switch
    {
        CsvDelimiter.Comma => "Comma (,)",
        CsvDelimiter.Tab => "Tab",
        CsvDelimiter.Semicolon => "Semicolon (;)",
        CsvDelimiter.Pipe => "Pipe (|)",
        CsvDelimiter.Colon => "Colon (:)",
        _ => throw new ArgumentOutOfRangeException(nameof(delimiter))
    };

    public static CsvDelimiter? FromChar(char c) => c switch
    {
        ',' => CsvDelimiter.Comma,
        '\t' => CsvDelimiter.Tab,
        ';' => CsvDelimiter.Semicolon,
        '|' => CsvDelimiter.Pipe,
        ':' => CsvDelimiter.Colon,
        _ => null
    };
}
