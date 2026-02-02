using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using CSVEditor.Core;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Provides QuickInfo (hover tooltips) for CSV columns.
/// </summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("CSV QuickInfo Provider")]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[Order(Before = "Default Quick Info Presenter")]
internal sealed class CsvQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new CsvQuickInfoSource(textBuffer));
    }
}

/// <summary>
/// Provides column header information on hover.
/// </summary>
internal sealed class CsvQuickInfoSource : IAsyncQuickInfoSource
{
    private readonly ITextBuffer _textBuffer;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private string[] _columnNames = Array.Empty<string>();
    private bool _disposed;

    public CsvQuickInfoSource(ITextBuffer textBuffer)
    {
        _textBuffer = textBuffer;
    }

    public Task<QuickInfoItem> GetQuickInfoItemAsync(
        IAsyncQuickInfoSession session,
        CancellationToken cancellationToken)
    {
        if (_disposed)
            return Task.FromResult<QuickInfoItem>(null);

        var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
        if (!triggerPoint.HasValue)
            return Task.FromResult<QuickInfoItem>(null);

        var snapshot = _textBuffer.CurrentSnapshot;
        var position = triggerPoint.Value.Position;

        // Detect delimiter if needed
        if (!_delimiterDetected)
        {
            DetectDelimiterAndHeaders(snapshot);
        }

        // Get current line info
        var line = snapshot.GetLineFromPosition(position);
        var lineText = line.GetText();
        var positionInLine = position - line.Start.Position;

        // Find which column and cell the cursor is in
        var (columnIndex, cellStart, cellEnd) = GetColumnAndCellBounds(lineText, positionInLine);
        
        if (cellStart < 0 || cellEnd < 0)
            return Task.FromResult<QuickInfoItem>(null);

        var columnName = GetColumnName(columnIndex);
        var cellValue = GetCellValue(lineText, cellStart, cellEnd);

        // Create tracking span for the cell
        var cellSpan = new SnapshotSpan(snapshot, line.Start + cellStart, cellEnd - cellStart);
        var trackingSpan = snapshot.CreateTrackingSpan(cellSpan, SpanTrackingMode.EdgeInclusive);

        // Build tooltip content
        var content = BuildTooltipContent(columnIndex, columnName, cellValue, line.LineNumber);

        var quickInfoItem = new QuickInfoItem(trackingSpan, content);
        return Task.FromResult(quickInfoItem);
    }

    private void DetectDelimiterAndHeaders(ITextSnapshot snapshot)
    {
        var length = Math.Min(snapshot.Length, 2000);
        var text = snapshot.GetText(0, length);

        var delimiter = DelimiterDetector.Detect(text);
        _detectedDelimiter = delimiter.ToChar();
        _delimiterDetected = true;

        // Parse first line for column names
        if (snapshot.LineCount > 0)
        {
            var firstLine = snapshot.GetLineFromLineNumber(0).GetText();
            _columnNames = ParseColumnNames(firstLine);
        }
    }

    private string[] ParseColumnNames(string headerLine)
    {
        var names = new System.Collections.Generic.List<string>();
        var position = 0;
        var inQuotes = false;
        var cellStart = 0;

        while (position <= headerLine.Length)
        {
            if (position == headerLine.Length || (headerLine[position] == _detectedDelimiter && !inQuotes))
            {
                var cellText = headerLine.Substring(cellStart, position - cellStart);
                // Remove quotes if present
                if (cellText.Length >= 2 && cellText[0] == '"' && cellText[cellText.Length - 1] == '"')
                {
                    cellText = cellText.Substring(1, cellText.Length - 2).Replace("\"\"", "\"");
                }
                names.Add(cellText);
                cellStart = position + 1;
            }
            else if (position < headerLine.Length && headerLine[position] == '"')
            {
                if (inQuotes && position + 1 < headerLine.Length && headerLine[position + 1] == '"')
                {
                    position++; // Skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            position++;
        }

        return names.ToArray();
    }

    private (int columnIndex, int cellStart, int cellEnd) GetColumnAndCellBounds(string lineText, int position)
    {
        if (string.IsNullOrEmpty(lineText))
            return (0, 0, 0);

        var columnIndex = 0;
        var cellStart = 0;
        var inQuotes = false;

        for (var i = 0; i < lineText.Length; i++)
        {
            var c = lineText[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < lineText.Length && lineText[i + 1] == '"')
                {
                    i++; // Skip escaped quote
                    continue;
                }
                inQuotes = !inQuotes;
            }
            else if (c == _detectedDelimiter && !inQuotes)
            {
                if (position >= cellStart && position <= i)
                {
                    return (columnIndex, cellStart, i);
                }
                columnIndex++;
                cellStart = i + 1;
            }
        }

        // Last cell
        if (position >= cellStart)
        {
            return (columnIndex, cellStart, lineText.Length);
        }

        return (0, -1, -1);
    }

    private string GetCellValue(string lineText, int start, int end)
    {
        if (start < 0 || end < 0 || start >= lineText.Length)
            return string.Empty;

        var cellText = lineText.Substring(start, end - start);
        
        // Remove quotes if present
        if (cellText.Length >= 2 && cellText[0] == '"' && cellText[cellText.Length - 1] == '"')
        {
            cellText = cellText.Substring(1, cellText.Length - 2).Replace("\"\"", "\"");
        }

        return cellText;
    }

    private string GetColumnName(int columnIndex)
    {
        if (columnIndex >= 0 && columnIndex < _columnNames.Length)
        {
            var name = _columnNames[columnIndex];
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return $"Column {columnIndex + 1}";
    }

    private object BuildTooltipContent(int columnIndex, string columnName, string cellValue, int lineNumber)
    {
        // For the header row, show different info
        if (lineNumber == 0)
        {
            return new ContainerElement(
                ContainerElementStyle.Stacked,
                new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, "Header Column"),
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.WhiteSpace, " "),
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.Number, $"#{columnIndex + 1}")
                )
            );
        }

        // For data rows, show column name and index
        return new ContainerElement(
            ContainerElementStyle.Stacked,
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, "Column: "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, columnName)
            ),
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, $"(Index: {columnIndex + 1} of {_columnNames.Length})")
            )
        );
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
