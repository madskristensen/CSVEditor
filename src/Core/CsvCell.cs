using System;



namespace CSVEditor.Core;

/// <summary>
/// Represents a single cell in a CSV row.
/// </summary>
public sealed class CsvCell
{
    /// <summary>
    /// The text content of the cell (without quotes if it was quoted).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The span in the original text that this cell occupies (including quotes if present).
    /// </summary>
    public TextSpan Span { get; }

    /// <summary>
    /// The zero-based column index of this cell.
    /// </summary>
    public int ColumnIndex { get; }

    /// <summary>
    /// Whether this cell was quoted in the original text.
    /// </summary>
    public bool IsQuoted { get; }

    public CsvCell(string value, TextSpan span, int columnIndex, bool isQuoted = false)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Span = span;
        ColumnIndex = columnIndex;
        IsQuoted = isQuoted;
    }

    public override string ToString() => $"[{ColumnIndex}] \"{Value}\" {Span}";
}
