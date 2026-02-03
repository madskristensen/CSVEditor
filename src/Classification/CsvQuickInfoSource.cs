using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CSVEditor.Core;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
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
    [Import]
    internal ITextDocumentFactoryService TextDocumentFactory = null;

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new CsvQuickInfoSource(textBuffer, TextDocumentFactory));
    }
}

/// <summary>
/// Provides column header information on hover with sort actions.
/// </summary>
internal sealed class CsvQuickInfoSource : IAsyncQuickInfoSource
{
    private readonly ITextBuffer _textBuffer;
    private readonly ITextDocumentFactoryService _textDocumentFactory;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private CsvRow _headerRow;
    private bool _disposed;

    public CsvQuickInfoSource(ITextBuffer textBuffer, ITextDocumentFactoryService textDocumentFactory)
    {
        _textBuffer = textBuffer;
        _textDocumentFactory = textDocumentFactory;
    }

    public async Task<QuickInfoItem> GetQuickInfoItemAsync(
        IAsyncQuickInfoSession session,
        CancellationToken cancellationToken)
    {
        if (_disposed)
            return null;

        SnapshotPoint? triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
        if (!triggerPoint.HasValue)
            return null;

        ITextSnapshot snapshot = _textBuffer.CurrentSnapshot;
        var position = triggerPoint.Value.Position;

        if (!_delimiterDetected)
        {
            DetectDelimiterAndHeaders(snapshot);
        }

        // Parse current line using CsvParser
        ITextSnapshotLine line = snapshot.GetLineFromPosition(position);
        CsvRow row = CsvParser.ParseLine(line.GetText(), _detectedDelimiter, line.LineNumber, line.Start.Position);

        // Find the cell at the hover position
        CsvCell cell = row.GetCellAtPosition(position);
        if (cell == null)
            return null;

        var columnName = GetColumnName(cell.ColumnIndex);

        // Create tracking span for the cell
        var cellSpan = new SnapshotSpan(snapshot, cell.Span.Start, cell.Span.Length);
        ITrackingSpan trackingSpan = snapshot.CreateTrackingSpan(cellSpan, SpanTrackingMode.EdgeInclusive);

        // Build tooltip content (must be on UI thread for WPF elements)
        object content;
        if (line.LineNumber == 0)
        {
            // Header row - need WPF elements for clickable links, must be on UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            content = BuildHeaderTooltip(cell.ColumnIndex, columnName, _headerRow?.Count ?? 0, session);
        }
        else
        {
            // Data rows - use thread-safe ContainerElement
            content = BuildDataRowTooltip(cell.ColumnIndex, columnName);
        }

        return new QuickInfoItem(trackingSpan, content);
    }

    private void DetectDelimiterAndHeaders(ITextSnapshot snapshot)
    {
        var length = Math.Min(snapshot.Length, 2000);
        var text = snapshot.GetText(0, length);

        CsvDelimiter delimiter = DelimiterDetector.Detect(text);
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

    private object BuildDataRowTooltip(int columnIndex, string columnName)
    {
        var totalColumns = _headerRow?.Count ?? 0;

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

    private object BuildHeaderTooltip(int columnIndex, string columnName, int totalColumns, IAsyncQuickInfoSession session)
    {
        // This method must be called on the UI thread
        var panel = new StackPanel { Orientation = Orientation.Vertical };

        // Header info
        var headerInfo = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 6)
        };
        headerInfo.Inlines.Add(new Run("Header Column ") { Foreground = Brushes.Gray });
        headerInfo.Inlines.Add(new Run($"#{columnIndex + 1}") { Foreground = Brushes.DodgerBlue, FontWeight = FontWeights.SemiBold });
        if (totalColumns > 0)
        {
            headerInfo.Inlines.Add(new Run($" of {totalColumns}") { Foreground = Brushes.Gray });
        }
        panel.Children.Add(headerInfo);

        // Sort links
        var sortPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var sortAscLink = new Hyperlink(new Run("Sort A→Z"));
        sortAscLink.Click += (s, e) =>
        {
            session.DismissAsync().ConfigureAwait(false);
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SortByColumn(columnIndex, ascending: true);
            });
        };

        var sortDescLink = new Hyperlink(new Run("Sort Z→A"));
        sortDescLink.Click += (s, e) =>
        {
            session.DismissAsync().ConfigureAwait(false);
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SortByColumn(columnIndex, ascending: false);
            });
        };

        var ascText = new TextBlock(sortAscLink) { Margin = new Thickness(0, 0, 12, 0) };
        var descText = new TextBlock(sortDescLink);

        sortPanel.Children.Add(ascText);
        sortPanel.Children.Add(descText);
        panel.Children.Add(sortPanel);

        return panel;
    }

    private void SortByColumn(int columnIndex, bool ascending)
    {
        ITextSnapshot snapshot = _textBuffer.CurrentSnapshot;

        if (snapshot.LineCount < 2)
            return;

        // Parse all data rows (skip header)
        var dataRows = new System.Collections.Generic.List<(int lineNumber, string lineText, string sortValue)>();

        for (var i = 1; i < snapshot.LineCount; i++)
        {
            ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
            var lineText = line.GetText();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(lineText))
                continue;

            CsvRow row = CsvParser.ParseLine(lineText, _detectedDelimiter, i);
            var sortValue = columnIndex < row.Count ? row[columnIndex].Value : "";

            dataRows.Add((i, lineText, sortValue));
        }

        if (dataRows.Count == 0)
            return;

        // Sort the rows
        System.Collections.Generic.IEnumerable<(int lineNumber, string lineText, string sortValue)> sortedRows;

        // Try numeric sort first, fall back to string sort
        if (dataRows.All(r => IsNumeric(r.sortValue)))
        {
            sortedRows = ascending
                ? dataRows.OrderBy(r => ParseDouble(r.sortValue))
                : dataRows.OrderByDescending(r => ParseDouble(r.sortValue));
        }
        else
        {
            sortedRows = ascending
                ? dataRows.OrderBy(r => r.sortValue, StringComparer.OrdinalIgnoreCase)
                : dataRows.OrderByDescending(r => r.sortValue, StringComparer.OrdinalIgnoreCase);
        }

        // Build the new document content
        var sb = new StringBuilder();
        ITextSnapshotLine headerLine = snapshot.GetLineFromLineNumber(0);
        sb.AppendLine(headerLine.GetText()); // Keep header first

        foreach ((int lineNumber, string lineText, string sortValue) row in sortedRows)
        {
            sb.AppendLine(row.lineText);
        }

        // Remove trailing newline
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
        {
            sb.Length--;
            if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                sb.Length--;
        }

        // Replace document content
        var newText = sb.ToString();

        using (ITextEdit edit = _textBuffer.CreateEdit())
        {
            edit.Replace(new Microsoft.VisualStudio.Text.Span(0, snapshot.Length), newText);
            edit.Apply();
        }
    }

    private static bool IsNumeric(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return double.TryParse(value, out _);
    }

    private static double ParseDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return double.MinValue;

        if (double.TryParse(value, out var result))
            return result;

        return double.MinValue;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
