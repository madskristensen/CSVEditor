using System;
using System.Collections;
using System.Collections.Generic;



namespace CSVEditor.Core;

/// <summary>
/// Represents a parsed CSV document with structured access to rows and cells.
/// </summary>
public sealed class CsvDocument : IReadOnlyList<CsvRow>
{
    private readonly IReadOnlyList<CsvRow> _rows;

    /// <summary>
    /// The delimiter character used in this document.
    /// </summary>
    public char Delimiter { get; }

    /// <summary>
    /// The number of rows in this document.
    /// </summary>
    public int Count => _rows.Count;

    /// <summary>
    /// The header row (first row), or null if the document is empty.
    /// </summary>
    public CsvRow HeaderRow => _rows.Count > 0 ? _rows[0] : null;

    /// <summary>
    /// The column names from the header row.
    /// </summary>
    public IReadOnlyList<string> ColumnNames { get; }

    /// <summary>
    /// The maximum number of columns in any row.
    /// </summary>
    public int MaxColumnCount { get; }

    public CsvRow this[int index] => _rows[index];

    public CsvDocument(IReadOnlyList<CsvRow> rows, char delimiter)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        Delimiter = delimiter;

        // Extract column names from header
        if (rows.Count > 0)
        {
            var names = new List<string>(rows[0].Count);
            foreach (var cell in rows[0])
            {
                names.Add(cell.Value);
            }
            ColumnNames = names;
        }
        else
        {
            ColumnNames = Array.Empty<string>();
        }

        // Calculate max column count
        var maxCount = 0;
        foreach (var row in rows)
        {
            if (row.Count > maxCount)
                maxCount = row.Count;
        }
        MaxColumnCount = maxCount;
    }

    /// <summary>
    /// Gets the column name for the given index, or a generated name if the index is out of range.
    /// </summary>
    public string GetColumnName(int columnIndex)
    {
        if (columnIndex >= 0 && columnIndex < ColumnNames.Count)
        {
            var name = ColumnNames[columnIndex];
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return $"Column {columnIndex + 1}";
    }

    /// <summary>
    /// Finds the cell at the given position in the document.
    /// </summary>
    public CsvCell GetCellAtPosition(int position)
    {
        foreach (var row in _rows)
        {
            if (row.Span.Contains(position))
            {
                return row.GetCellAtPosition(position);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the row at the given position in the document.
    /// </summary>
    public CsvRow GetRowAtPosition(int position)
    {
        foreach (var row in _rows)
        {
            if (row.Span.Contains(position))
                return row;
        }
        return null;
    }

    /// <summary>
    /// Gets all cells in a specific column across all rows.
    /// </summary>
    public IEnumerable<CsvCell> GetColumn(int columnIndex)
    {
        foreach (var row in _rows)
        {
            var cell = row.GetCellOrDefault(columnIndex);
            if (cell != null)
                yield return cell;
        }
    }

    public IEnumerator<CsvRow> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
