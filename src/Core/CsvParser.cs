using System;
using System.Collections.Generic;
using System.Text;



namespace CSVEditor.Core;

/// <summary>
/// Parses CSV content into structured data with span information for editor integration.
/// </summary>
public sealed class CsvParser
{
    private readonly char _delimiter;
    private readonly string _content;
    private int _position;
    private int _lineNumber;

    private CsvParser(string content, char delimiter)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _delimiter = delimiter;
        _position = 0;
        _lineNumber = 0;
    }

    /// <summary>
    /// Parses CSV content with the specified delimiter.
    /// </summary>
    public static CsvDocument Parse(string content, CsvDelimiter delimiter)
    {
        return Parse(content, delimiter.ToChar());
    }

    /// <summary>
    /// Parses CSV content with the specified delimiter character.
    /// </summary>
    public static CsvDocument Parse(string content, char delimiter)
    {
        if (string.IsNullOrEmpty(content))
            return new CsvDocument(Array.Empty<CsvRow>(), delimiter);

        var parser = new CsvParser(content, delimiter);
        return parser.ParseDocument();
    }

    /// <summary>
    /// Parses CSV content, auto-detecting the delimiter.
    /// </summary>
    public static CsvDocument Parse(string content)
    {
        var delimiter = DelimiterDetector.Detect(content);
        return Parse(content, delimiter);
    }

    /// <summary>
    /// Parses a single line of CSV content.
    /// Useful for incremental parsing in editor scenarios.
    /// </summary>
    public static CsvRow ParseLine(string line, char delimiter, int lineNumber, int lineStartOffset = 0)
    {
        if (string.IsNullOrEmpty(line))
            return new CsvRow(Array.Empty<CsvCell>(), new TextSpan(lineStartOffset, 0), lineNumber);

        var cells = new List<CsvCell>();
        var position = 0;
        var columnIndex = 0;

        while (position <= line.Length)
        {
            var cell = ParseCell(line, delimiter, ref position, columnIndex, lineStartOffset);
            cells.Add(cell);
            columnIndex++;

            if (position >= line.Length)
                break;
        }

        var rowSpan = new TextSpan(lineStartOffset, line.Length);
        return new CsvRow(cells, rowSpan, lineNumber);
    }

    private CsvDocument ParseDocument()
    {
        var rows = new List<CsvRow>();

        while (_position < _content.Length)
        {
            var row = ParseRow();
            rows.Add(row);
        }

        return new CsvDocument(rows, _delimiter);
    }

    private CsvRow ParseRow()
    {
        var rowStart = _position;
        var cells = new List<CsvCell>();
        var columnIndex = 0;

        while (_position < _content.Length)
        {
            var cell = ParseCellInstance(columnIndex);
            cells.Add(cell);
            columnIndex++;

            if (_position >= _content.Length)
                break;

            var c = _content[_position];
            if (c == '\r' || c == '\n')
            {
                // Consume line ending
                if (c == '\r' && _position + 1 < _content.Length && _content[_position + 1] == '\n')
                {
                    _position += 2;
                }
                else
                {
                    _position++;
                }
                break;
            }

            if (c == _delimiter)
            {
                _position++; // Consume delimiter
            }
        }

        var rowLength = _position - rowStart;
        // Trim trailing newline from span
        while (rowLength > 0)
        {
            var lastChar = _content[rowStart + rowLength - 1];
            if (lastChar == '\r' || lastChar == '\n')
                rowLength--;
            else
                break;
        }

        var row = new CsvRow(cells, new TextSpan(rowStart, rowLength), _lineNumber);
        _lineNumber++;
        return row;
    }

    private CsvCell ParseCellInstance(int columnIndex)
    {
        var cellStart = _position;
        var isQuoted = _position < _content.Length && _content[_position] == '"';

        if (isQuoted)
        {
            return ParseQuotedCell(columnIndex);
        }
        else
        {
            return ParseUnquotedCell(columnIndex);
        }
    }

    private CsvCell ParseQuotedCell(int columnIndex)
    {
        var cellStart = _position;
        _position++; // Skip opening quote

        var valueBuilder = new StringBuilder();

        while (_position < _content.Length)
        {
            var c = _content[_position];

            if (c == '"')
            {
                if (_position + 1 < _content.Length && _content[_position + 1] == '"')
                {
                    // Escaped quote
                    valueBuilder.Append('"');
                    _position += 2;
                }
                else
                {
                    // End of quoted field
                    _position++;
                    break;
                }
            }
            else
            {
                valueBuilder.Append(c);
                _position++;
            }
        }

        var span = new TextSpan(cellStart, _position - cellStart);
        return new CsvCell(valueBuilder.ToString(), span, columnIndex, isQuoted: true);
    }

    private CsvCell ParseUnquotedCell(int columnIndex)
    {
        var cellStart = _position;
        var valueBuilder = new StringBuilder();

        while (_position < _content.Length)
        {
            var c = _content[_position];

            if (c == _delimiter || c == '\r' || c == '\n')
            {
                break;
            }

            valueBuilder.Append(c);
            _position++;
        }

        var span = new TextSpan(cellStart, _position - cellStart);
        return new CsvCell(valueBuilder.ToString(), span, columnIndex, isQuoted: false);
    }

    private static CsvCell ParseCell(string line, char delimiter, ref int position, int columnIndex, int lineStartOffset)
    {
        if (position >= line.Length)
        {
            // Empty cell at end
            return new CsvCell("", new TextSpan(lineStartOffset + position, 0), columnIndex);
        }

        var cellStart = position;
        var isQuoted = line[position] == '"';

        if (isQuoted)
        {
            return ParseQuotedCellStatic(line, delimiter, ref position, columnIndex, lineStartOffset, cellStart);
        }
        else
        {
            return ParseUnquotedCellStatic(line, delimiter, ref position, columnIndex, lineStartOffset, cellStart);
        }
    }

    private static CsvCell ParseQuotedCellStatic(string line, char delimiter, ref int position, int columnIndex, int lineStartOffset, int cellStart)
    {
        position++; // Skip opening quote
        var valueBuilder = new StringBuilder();

        while (position < line.Length)
        {
            var c = line[position];

            if (c == '"')
            {
                if (position + 1 < line.Length && line[position + 1] == '"')
                {
                    valueBuilder.Append('"');
                    position += 2;
                }
                else
                {
                    position++;
                    break;
                }
            }
            else
            {
                valueBuilder.Append(c);
                position++;
            }
        }

        // Skip any characters until delimiter (handles trailing content after closing quote)
        while (position < line.Length && line[position] != delimiter)
        {
            position++;
        }

        // Skip delimiter
        if (position < line.Length && line[position] == delimiter)
        {
            position++;
        }

        var span = new TextSpan(lineStartOffset + cellStart, position - cellStart - (position <= line.Length && position > cellStart ? 1 : 0));
        return new CsvCell(valueBuilder.ToString(), span, columnIndex, isQuoted: true);
    }

    private static CsvCell ParseUnquotedCellStatic(string line, char delimiter, ref int position, int columnIndex, int lineStartOffset, int cellStart)
    {
        var valueBuilder = new StringBuilder();

        while (position < line.Length)
        {
            var c = line[position];

            if (c == delimiter)
            {
                position++; // Skip delimiter
                break;
            }

            valueBuilder.Append(c);
            position++;
        }

        var spanLength = position - cellStart;
        if (position <= line.Length && position > cellStart && position > 0 && 
            cellStart + spanLength <= line.Length && 
            position - 1 < line.Length && line[position - 1] == delimiter)
        {
            spanLength--; // Don't include delimiter in span
        }

        var span = new TextSpan(lineStartOffset + cellStart, Math.Max(0, spanLength));
        return new CsvCell(valueBuilder.ToString(), span, columnIndex);
    }
}
