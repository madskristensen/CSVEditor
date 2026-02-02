using System;
using System.ComponentModel.Composition;
using System.Linq;
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
    private CsvRow _headerRow;
    private bool _disposed;

    public CsvStatusBarController(IWpfTextView textView)
    {
        _textView = textView;
        _textView.Caret.PositionChanged += OnCaretPositionChanged;
        _textView.Closed += OnTextViewClosed;

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

            if (!_delimiterDetected)
            {
                DetectDelimiterAndHeaders(snapshot);
            }

            // Parse current line using CsvParser
            var line = snapshot.GetLineFromPosition(caretPosition);
            var row = CsvParser.ParseLine(line.GetText(), _detectedDelimiter, line.LineNumber, line.Start.Position);

            // Find which cell contains the caret
            var cell = row.GetCellAtPosition(caretPosition);
            var columnIndex = cell?.ColumnIndex ?? 0;
            var columnName = GetColumnName(columnIndex);
            var totalColumns = _headerRow?.Count ?? row.Count;

            var statusText = $"Column: {columnName} ({columnIndex + 1} of {totalColumns})";
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

    public void Dispose()
    {
                if (_disposed)
                    return;

                _disposed = true;
                _textView.Caret.PositionChanged -= OnCaretPositionChanged;
                _textView.Closed -= OnTextViewClosed;
            }
        }
