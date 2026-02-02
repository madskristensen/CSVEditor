using System;
using System.Collections;
using System.Collections.Generic;



namespace CSVEditor.Core;

/// <summary>
/// Represents a single row in a CSV document.
/// </summary>
public sealed class CsvRow : IReadOnlyList<CsvCell>
{
    private readonly IReadOnlyList<CsvCell> _cells;

    /// <summary>
    /// The span in the original text that this row occupies.
    /// </summary>
    public TextSpan Span { get; }

    /// <summary>
    /// The zero-based line number of this row.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// The number of cells in this row.
    /// </summary>
    public int Count => _cells.Count;

    public CsvCell this[int index] => _cells[index];

    public CsvRow(IReadOnlyList<CsvCell> cells, TextSpan span, int lineNumber)
    {
        _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        Span = span;
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Gets the cell at the specified column index, or null if the index is out of range.
    /// </summary>
    public CsvCell GetCellOrDefault(int columnIndex)
    {
        return columnIndex >= 0 && columnIndex < _cells.Count ? _cells[columnIndex] : null;
    }

    /// <summary>
    /// Finds the cell that contains the given position.
    /// </summary>
    public CsvCell GetCellAtPosition(int position)
    {
        foreach (var cell in _cells)
        {
            if (cell.Span.Contains(position))
                return cell;
        }
        return null;
    }

    public IEnumerator<CsvCell> GetEnumerator() => _cells.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"Row {LineNumber}: {Count} cells";
}
