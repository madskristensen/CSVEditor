using System.Collections.Generic;
using System.Text;
using CSVEditor.Classification;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;

namespace CSVEditor;

[Command(PackageIds.ShrinkColumnsCommand)]
internal sealed class ShrinkColumnsCommand : BaseCommand<ShrinkColumnsCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView?.TextBuffer == null)
            return;

        ITextBuffer buffer = docView.TextView.TextBuffer;
        ITextSnapshot snapshot = buffer.CurrentSnapshot;

        // Get the delimiter from the cache
        var cache = CsvBufferCache.GetOrCreate(buffer);
        var delimiter = cache.GetDelimiter(snapshot);

        // Build the shrunk content
        var result = new StringBuilder();
        var lineCount = snapshot.LineCount;

        for (var i = 0; i < lineCount; i++)
        {
            ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
            var lineText = line.GetText();

            if (string.IsNullOrEmpty(lineText))
            {
                if (i < lineCount - 1)
                    result.AppendLine();
                continue;
            }

            CsvRow row = CsvParser.ParseLine(lineText, delimiter, i);
            var shrunkLine = BuildShrunkLine(row, delimiter);
            result.Append(shrunkLine);

            if (i < lineCount - 1)
                result.AppendLine();
        }

        // Replace the entire document content
        using (ITextEdit edit = buffer.CreateEdit())
        {
            edit.Replace(new Span(0, snapshot.Length), result.ToString());
            edit.Apply();
        }

        await VS.StatusBar.ShowMessageAsync("CSV columns shrunk");
    }

    private static string BuildShrunkLine(CsvRow row, char delimiter)
    {
        var parts = new List<string>(row.Count);

        foreach (CsvCell cell in row)
        {
            var trimmedValue = cell.Value.Trim();

            // Preserve quoting if the cell was originally quoted or needs quoting
            if (cell.IsQuoted || NeedsQuoting(trimmedValue, delimiter))
            {
                // Escape any quotes in the value
                var escapedValue = trimmedValue.Replace("\"", "\"\"");
                parts.Add($"\"{escapedValue}\"");
            }
            else
            {
                parts.Add(trimmedValue);
            }
        }

        return string.Join(delimiter.ToString(), parts);
    }

    private static bool NeedsQuoting(string value, char delimiter)
    {
        return value.Contains(delimiter.ToString()) ||
               value.Contains("\"") ||
               value.Contains("\n") ||
               value.Contains("\r");
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
