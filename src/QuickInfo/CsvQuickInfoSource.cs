using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CSVEditor.Classification;
using CSVEditor.Core;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;

namespace CSVEditor.QuickInfo;

/// <summary>
/// Provides column header information on hover with sort actions.
/// </summary>
internal sealed class CsvQuickInfoSource(ITextBuffer textBuffer) : IAsyncQuickInfoSource
{
    private readonly CsvBufferCache _cache = CsvBufferCache.GetOrCreate(textBuffer);
    private bool _disposed;

    public async Task<QuickInfoItem> GetQuickInfoItemAsync(
        IAsyncQuickInfoSession session,
        CancellationToken cancellationToken)
    {
        if (_disposed)
            return null;

        SnapshotPoint? triggerPoint = session.GetTriggerPoint(textBuffer.CurrentSnapshot);
        if (!triggerPoint.HasValue)
            return null;

        ITextSnapshot snapshot = textBuffer.CurrentSnapshot;
        var position = triggerPoint.Value.Position;

        // Use document-level parse to correctly resolve cells in multi-line quoted fields
        CsvCell cell = _cache.GetCellAtPosition(snapshot, position);
        if (cell == null)
            return null;

        ITextSnapshotLine line = snapshot.GetLineFromPosition(position);
        var hasHeader = _cache.HasHeader(snapshot);
        var columnName = GetColumnName(snapshot, cell.ColumnIndex, hasHeader);
        CsvDataType columnType = _cache.GetColumnType(snapshot, cell.ColumnIndex);
        var totalColumns = _cache.GetExpectedColumnCount(snapshot);
        var isFirstRow = line.LineNumber == 0;

        // Create tracking span for the cell (clamp to current line for multi-line cells)
        var lineStart = line.Start.Position;
        var lineEnd = line.End.Position;
        var spanStart = Math.Max(cell.Span.Start, lineStart);
        var spanLength = Math.Min(cell.Span.End, lineEnd) - spanStart;
        if (spanLength <= 0)
            return null;

        var cellSpan = new SnapshotSpan(snapshot, spanStart, spanLength);
        ITrackingSpan trackingSpan = snapshot.CreateTrackingSpan(cellSpan, SpanTrackingMode.EdgeInclusive);

        // Build tooltip content (must be on UI thread for WPF elements)
        object content;
        if (isFirstRow)
        {
            // First row - need WPF elements for clickable sort links, must be on UI thread
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            content = BuildFirstRowTooltip(cell.ColumnIndex, columnName, columnType, totalColumns, hasHeader, session);
        }
        else
        {
            // Data rows - use thread-safe ContainerElement
            content = BuildDataRowTooltip(cell.ColumnIndex, columnName, columnType, totalColumns);
        }

        return new QuickInfoItem(trackingSpan, content);
    }

    private string GetColumnName(ITextSnapshot snapshot, int columnIndex, bool hasHeader)
    {
        if (hasHeader)
        {
            CsvRow headerRow = _cache.GetParsedLine(snapshot, 0);
            if (headerRow != null && columnIndex >= 0 && columnIndex < headerRow.Count)
            {
                var name = headerRow[columnIndex].Value;
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        return $"Column {columnIndex + 1}";
    }

    private object BuildDataRowTooltip(int columnIndex, string columnName, CsvDataType columnType, int totalColumns)
    {
        var typeName = CsvColumnTypeDetector.GetTypeName(columnType);

        // Build column info text to match header tooltip style
        var columnText = $"Column {columnName} (#{columnIndex + 1} of {totalColumns})";

        return new ContainerElement(
            ContainerElementStyle.Stacked,
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, columnText)
            ),
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "Type: "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, typeName)
            )
        );
    }

    private object BuildFirstRowTooltip(int columnIndex, string columnName, CsvDataType columnType, int totalColumns, bool hasHeader, IAsyncQuickInfoSession session)
    {
        // This method must be called on the UI thread
        var typeName = CsvColumnTypeDetector.GetTypeName(columnType);

        // Build column info text
        var columnText = hasHeader
            ? $"Column {columnName} (#{columnIndex + 1} of {totalColumns})"
            : $"Column #{columnIndex + 1} of {totalColumns}";

        // Get theme-aware link color for hyperlinks
        Brush linkColor = GetThemedLinkBrush();

        // Sort links panel - only this needs WPF elements for click handling
        var sortPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var sortAscLink = new Hyperlink(new Run("Sort A→Z")) { Foreground = linkColor };
        sortAscLink.Click += (s, e) =>
        {
            session.DismissAsync().ConfigureAwait(false);
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SortByColumn(columnIndex, ascending: true);
            });
        };

        var sortDescLink = new Hyperlink(new Run("Sort Z→A")) { Foreground = linkColor };
        sortDescLink.Click += (s, e) =>
        {
            session.DismissAsync().ConfigureAwait(false);
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SortByColumn(columnIndex, ascending: false);
            });
        };

        sortPanel.Children.Add(new TextBlock(sortAscLink) { Margin = new Thickness(0, 0, 12, 0) });
        sortPanel.Children.Add(new TextBlock(sortDescLink));

        // Use ClassifiedTextElement for text (theme-aware) and WPF only for clickable links
        return new ContainerElement(
            ContainerElementStyle.Stacked,
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, columnText)
            ),
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "Type: "),
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Keyword, typeName)
            ),
            new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Text, "")
            ),
            sortPanel
        );
    }

    private static Brush GetThemedLinkBrush()
    {
        System.Drawing.Color color = VSColorTheme.GetThemedColor(EnvironmentColors.ControlLinkTextColorKey);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze(); // Freeze for cross-thread access and performance
        return brush;
    }

    private void SortByColumn(int columnIndex, bool ascending)
    {
        ITextSnapshot snapshot = textBuffer.CurrentSnapshot;

        if (snapshot.LineCount < 2)
            return;

        var delimiter = _cache.GetDelimiter(snapshot);
        var hasHeader = _cache.HasHeader(snapshot);
        var firstDataRow = hasHeader ? 1 : 0;

        // Parse all data rows (skip header if present)
        var dataRows = new System.Collections.Generic.List<(int lineNumber, string lineText, string sortValue)>();

        for (var i = firstDataRow; i < snapshot.LineCount; i++)
        {
            ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
            var lineText = line.GetText();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(lineText))
                continue;

            // Use cache for parsing to avoid duplicate work
            CsvRow row = _cache.GetParsedLine(line);
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

        // Keep header first if present
        if (hasHeader)
        {
            ITextSnapshotLine headerLine = snapshot.GetLineFromLineNumber(0);
            sb.AppendLine(headerLine.GetText());
        }

        foreach ((var lineNumber, var lineText, var sortValue) in sortedRows)
        {
            sb.AppendLine(lineText);
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

        using (ITextEdit edit = textBuffer.CreateEdit())
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
