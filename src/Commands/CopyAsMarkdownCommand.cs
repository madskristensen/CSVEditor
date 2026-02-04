using System.Collections.Generic;
using System.Text;
using CSVEditor.Classification;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CSVEditor;

[Command(PackageIds.CopyAsMarkdownCommand)]
internal sealed class CopyAsMarkdownCommand : BaseCommand<CopyAsMarkdownCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView == null)
            return;

        ITextView textView = docView.TextView;
        ITextSnapshot snapshot = textView.TextBuffer.CurrentSnapshot;
        CsvBufferCache cache = CsvBufferCache.GetOrCreate(textView.TextBuffer);
        char delimiter = cache.GetDelimiter(snapshot);

        // Get selection or entire document
        string textToCopy;
        int startLine, endLine;

        if (textView.Selection.IsEmpty)
        {
            // No selection - use entire document
            textToCopy = snapshot.GetText();
            startLine = 0;
            endLine = snapshot.LineCount - 1;
        }
        else
        {
            // Use selection
            SnapshotSpan selection = textView.Selection.StreamSelectionSpan.SnapshotSpan;
            textToCopy = selection.GetText();
            startLine = snapshot.GetLineNumberFromPosition(selection.Start);
            endLine = snapshot.GetLineNumberFromPosition(selection.End);
        }

        // Parse lines
        var rows = new List<CsvRow>();
        var lines = textToCopy.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line))
                continue;
                
            CsvRow row = CsvParser.ParseLine(line, delimiter, i);
            rows.Add(row);
        }

        if (rows.Count == 0)
        {
            await VS.StatusBar.ShowMessageAsync("No data to copy");
            return;
        }

        // Build markdown table
        string markdown = BuildMarkdownTable(rows, cache.HasHeader(snapshot));

        // Copy to clipboard
        System.Windows.Clipboard.SetText(markdown);

        var rowCount = rows.Count;
        await VS.StatusBar.ShowMessageAsync($"Copied {rowCount} row{(rowCount == 1 ? "" : "s")} as Markdown table");
    }

    private static string BuildMarkdownTable(List<CsvRow> rows, bool hasHeader)
    {
        if (rows.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        
        // Determine max column count
        var maxColumns = 0;
        foreach (CsvRow row in rows)
        {
            if (row.Count > maxColumns)
                maxColumns = row.Count;
        }

        // Calculate column widths for nice formatting
        var columnWidths = new int[maxColumns];
        foreach (CsvRow row in rows)
        {
            for (var col = 0; col < row.Count; col++)
            {
                var cellValue = EscapeMarkdown(row[col].Value);
                if (cellValue.Length > columnWidths[col])
                    columnWidths[col] = cellValue.Length;
            }
        }

        // Ensure minimum width of 3 for separator row
        for (var i = 0; i < columnWidths.Length; i++)
        {
            if (columnWidths[i] < 3)
                columnWidths[i] = 3;
        }

        // Build header row
        CsvRow firstRow = rows[0];
        sb.Append('|');
        for (var col = 0; col < maxColumns; col++)
        {
            var cellValue = col < firstRow.Count ? EscapeMarkdown(firstRow[col].Value) : "";
            sb.Append(' ');
            sb.Append(cellValue.PadRight(columnWidths[col]));
            sb.Append(" |");
        }
        sb.AppendLine();

        // Build separator row
        sb.Append('|');
        for (var col = 0; col < maxColumns; col++)
        {
            sb.Append(' ');
            sb.Append(new string('-', columnWidths[col]));
            sb.Append(" |");
        }
        sb.AppendLine();

        // Build data rows (skip first if it's a header)
        var startIndex = hasHeader ? 1 : 0;
        
        // If no header but we already used row 0 as header, we need to repeat it as data
        if (!hasHeader && rows.Count > 0)
        {
            startIndex = 0;
            // But we already output row 0 as header, so start from 1
            startIndex = 1;
        }

        for (var rowIndex = startIndex; rowIndex < rows.Count; rowIndex++)
        {
            CsvRow row = rows[rowIndex];
            sb.Append('|');
            for (var col = 0; col < maxColumns; col++)
            {
                var cellValue = col < row.Count ? EscapeMarkdown(row[col].Value) : "";
                sb.Append(' ');
                sb.Append(cellValue.PadRight(columnWidths[col]));
                sb.Append(" |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeMarkdown(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Escape pipe characters which break markdown tables
        return value.Replace("|", "\\|");
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            var contentType = docView?.TextView?.TextBuffer?.ContentType?.TypeName;
            Command.Visible = contentType == "csv" || contentType == "tsv";
        });
    }
}
