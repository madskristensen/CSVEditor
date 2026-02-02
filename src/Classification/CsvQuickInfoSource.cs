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
    private CsvRow _headerRow;
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

        if (!_delimiterDetected)
        {
            DetectDelimiterAndHeaders(snapshot);
        }

        // Parse current line using CsvParser
        var line = snapshot.GetLineFromPosition(position);
        var row = CsvParser.ParseLine(line.GetText(), _detectedDelimiter, line.LineNumber, line.Start.Position);

        // Find the cell at the hover position
        var cell = row.GetCellAtPosition(position);
        if (cell == null)
            return Task.FromResult<QuickInfoItem>(null);

        var columnName = GetColumnName(cell.ColumnIndex);

        // Create tracking span for the cell
        var cellSpan = new SnapshotSpan(snapshot, cell.Span.Start, cell.Span.Length);
        var trackingSpan = snapshot.CreateTrackingSpan(cellSpan, SpanTrackingMode.EdgeInclusive);

        // Build tooltip content
        var content = BuildTooltipContent(cell.ColumnIndex, columnName, line.LineNumber);

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

        // Parse header row using CsvParser
        if (snapshot.LineCount > 0)
        {
            var firstLine = snapshot.GetLineFromLineNumber(0).GetText();
            _headerRow = CsvParser.ParseLine(firstLine, _detectedDelimiter, 0);
        }
    }

    private string GetColumnName(int columnIndex)
    {
        if (_headerRow != null && columnIndex >= 0 && columnIndex < _headerRow.Count)
        {
            var name = _headerRow[columnIndex].Value;
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return $"Column {columnIndex + 1}";
    }

    private object BuildTooltipContent(int columnIndex, string columnName, int lineNumber)
    {
        var totalColumns = _headerRow?.Count ?? 0;

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
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, $"(Index: {columnIndex + 1} of {totalColumns})")
            )
        );
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
