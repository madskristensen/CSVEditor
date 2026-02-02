using System;
using System.ComponentModel.Composition;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Listens to caret position changes and updates the status bar with column info.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class CsvStatusBarProvider : IWpfTextViewCreationListener
{
    public void TextViewCreated(IWpfTextView textView)
    {
        textView.Properties.GetOrCreateSingletonProperty(
            () => new CsvStatusBarController(textView));
    }
}

/// <summary>
/// Controller that tracks caret position and updates status bar.
/// </summary>
internal sealed class CsvStatusBarController : IDisposable
{
    private readonly IWpfTextView _textView;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private string[] _columnNames = Array.Empty<string>();
    private bool _disposed;

    public CsvStatusBarController(IWpfTextView textView)
    {
        _textView = textView;
        _textView.Caret.PositionChanged += OnCaretPositionChanged;
        _textView.Closed += OnTextViewClosed;

        // Initial update
        UpdateStatusBar();
    }

    private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
    {
        UpdateStatusBar();
    }

    private void OnTextViewClosed(object sender, EventArgs e)
    {
        Dispose();
    }

    private void UpdateStatusBar()
    {
        if (_disposed)
            return;

        try
        {
            var snapshot = _textView.TextSnapshot;
            var caretPosition = _textView.Caret.Position.BufferPosition.Position;

            // Detect delimiter if needed
            if (!_delimiterDetected)
            {
                DetectDelimiterAndHeaders(snapshot);
            }

            // Get current line
            var line = snapshot.GetLineFromPosition(caretPosition);
            var lineText = line.GetText();
            var positionInLine = caretPosition - line.Start.Position;

            // Find which column the caret is in
            var columnIndex = GetColumnAtPosition(lineText, positionInLine);
            var columnName = GetColumnName(columnIndex);
            var totalColumns = _columnNames.Length > 0 ? _columnNames.Length : CountColumns(lineText);

            // Format status bar text
            var statusText = $"Column: {columnName} ({columnIndex + 1} of {totalColumns})";

            // Update status bar
            Community.VisualStudio.Toolkit.VS.StatusBar.ShowMessageAsync(statusText).FireAndForget();
        }
        catch
        {
            // Ignore errors in status bar updates
        }
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

    private int GetColumnAtPosition(string lineText, int position)
    {
        if (string.IsNullOrEmpty(lineText) || position < 0)
            return 0;

        var columnIndex = 0;
        var inQuotes = false;

        for (var i = 0; i < lineText.Length && i < position; i++)
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
                columnIndex++;
            }
        }

        return columnIndex;
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

    private int CountColumns(string lineText)
    {
        if (string.IsNullOrEmpty(lineText))
            return 0;

        var count = 1;
        var inQuotes = false;

        foreach (var c in lineText)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == _detectedDelimiter && !inQuotes)
            {
                count++;
            }
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _textView.Caret.PositionChanged -= OnCaretPositionChanged;
        _textView.Closed -= OnTextViewClosed;
    }
}
