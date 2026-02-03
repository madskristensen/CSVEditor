using System;
using System.Collections.Generic;
using System.Linq;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CSVEditor;

[Command(PackageIds.GoToColumnCommand)]
internal sealed class GoToColumnCommand : BaseCommand<GoToColumnCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView == null)
            return;

        IWpfTextView textView = docView.TextView;
        ITextSnapshot snapshot = textView.TextSnapshot;

        // Detect delimiter
        var length = Math.Min(snapshot.Length, 2000);
        var text = snapshot.GetText(0, length);
        var delimiter = DelimiterDetector.Detect(text).ToChar();

        // Parse header row using CsvParser
        if (snapshot.LineCount == 0)
        {
            await VS.MessageBox.ShowWarningAsync("CSV Editor", "No columns found in the current document.");
            return;
        }

        var headerLine = snapshot.GetLineFromLineNumber(0).GetText();
        CsvRow headerRow = CsvParser.ParseLine(headerLine, delimiter, 0);

        if (headerRow.Count == 0)
        {
            await VS.MessageBox.ShowWarningAsync("CSV Editor", "No columns found in the current document.");
            return;
        }

        // Build column names list for display
        var columnNames = headerRow.Select(c => c.Value).ToList();

        // Show input dialog
        var input = ShowInputDialog(columnNames);
        if (string.IsNullOrWhiteSpace(input))
            return;

        // Parse input - try as number first
        int columnIndex;
        if (int.TryParse(input.Trim(), out var number) && number >= 1 && number <= headerRow.Count)
        {
            columnIndex = number - 1;
        }
        else
        {
            // Try to find by name (case-insensitive)
            columnIndex = columnNames.FindIndex(n => 
                string.Equals(n, input.Trim(), StringComparison.OrdinalIgnoreCase));

            if (columnIndex < 0)
            {
                await VS.MessageBox.ShowWarningAsync("CSV Editor", $"Column '{input}' not found.");
                return;
            }
        }

        // Navigate to column using CsvParser
        NavigateToColumn(textView, delimiter, columnIndex);
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

    private string ShowInputDialog(List<string> columnNames)
    {
        var columnList = new System.Text.StringBuilder();
        for (var i = 0; i < columnNames.Count && i < 15; i++)
        {
            var name = string.IsNullOrWhiteSpace(columnNames[i]) ? $"Column {i + 1}" : columnNames[i];
            columnList.AppendLine($"  {i + 1}. {name}");
        }
        if (columnNames.Count > 15)
        {
            columnList.AppendLine($"  ... and {columnNames.Count - 15} more");
        }

        var prompt = $"Enter column number (1-{columnNames.Count}) or name:\n\n{columnList}";

        try
        {
            return Microsoft.VisualBasic.Interaction.InputBox(prompt, "Go to Column", "1");
        }
        catch
        {
            return null;
        }
    }

    private void NavigateToColumn(ITextView textView, char delimiter, int targetColumn)
    {
        ITextSnapshot snapshot = textView.TextSnapshot;
        var caretPosition = textView.Caret.Position.BufferPosition.Position;
        ITextSnapshotLine line = snapshot.GetLineFromPosition(caretPosition);

        // Parse current line using CsvParser
        CsvRow row = CsvParser.ParseLine(line.GetText(), delimiter, line.LineNumber, line.Start.Position);

        // Get the target cell
        CsvCell cell = row.GetCellOrDefault(targetColumn);
        if (cell != null)
        {
            var point = new SnapshotPoint(snapshot, cell.Span.Start);
                        textView.Caret.MoveTo(point);
                        textView.ViewScroller.EnsureSpanVisible(
                            new SnapshotSpan(point, 0),
                            EnsureSpanVisibleOptions.AlwaysCenter);
                    }
                }
            }
