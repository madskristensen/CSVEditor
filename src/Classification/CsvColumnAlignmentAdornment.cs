using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Provides intra-text adornment tags for CSV column alignment.
/// Inserts virtual whitespace after each cell to align columns.
/// </summary>
[Export(typeof(IViewTaggerProvider))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TagType(typeof(IntraTextAdornmentTag))]
internal sealed class CsvColumnAlignmentTaggerProvider : IViewTaggerProvider
{
    public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
    {
        if (textView == null || buffer == null)
            return null;

        // Only provide tagger for the top-level buffer
        if (buffer != textView.TextBuffer)
            return null;

        return buffer.Properties.GetOrCreateSingletonProperty(
            () => new CsvColumnAlignmentTagger(textView as IWpfTextView, buffer)) as ITagger<T>;
    }
}

/// <summary>
/// Tagger that creates virtual whitespace adornments to align CSV columns.
/// </summary>
internal sealed class CsvColumnAlignmentTagger : ITagger<IntraTextAdornmentTag>, IDisposable
{
    private readonly IWpfTextView _textView;
    private readonly ITextBuffer _buffer;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private int[] _columnWidths;
    private bool _disposed;
    private bool _initialLayoutComplete;
    private CancellationTokenSource _calculationCts;
    private System.Windows.Threading.DispatcherTimer _debounceTimer;
    private const int DebounceDelayMs = 500; // Wait 500ms after last keystroke

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvColumnAlignmentTagger(IWpfTextView textView, ITextBuffer buffer)
    {
        _textView = textView;
        _buffer = buffer;

        _buffer.Changed += OnBufferChanged;
        if (_textView != null)
        {
            _textView.Closed += OnClosed;
            _textView.LayoutChanged += OnLayoutChanged;
        }
    }

    private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
    {
        if (_disposed) return;

        // On first layout, trigger tag recalculation
        if (!_initialLayoutComplete)
        {
            _initialLayoutComplete = true;

            // Start background calculation of all column widths
            StartBackgroundWidthCalculation();
        }
    }

    private void ScheduleBackgroundCalculation()
    {
        if (_disposed) return;

        // Debounce: reset timer on each call
        if (_debounceTimer == null)
        {
            _debounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebounceDelayMs)
            };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                if (!_disposed)
                {
                    StartBackgroundWidthCalculation();
                }
            };
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void StartBackgroundWidthCalculation()
    {
        _calculationCts?.Cancel();
        _calculationCts = new CancellationTokenSource();
        var token = _calculationCts.Token;
        var snapshot = _buffer.CurrentSnapshot;

        Task.Run(() =>
        {
            try
            {
                var widths = CalculateAllColumnWidths(snapshot, token);
                if (token.IsCancellationRequested || _disposed)
                    return;

                // Only update if widths actually changed
                var widthsChanged = !AreWidthsEqual(_columnWidths, widths);
                _columnWidths = widths;

                if (widthsChanged)
                {
                    // Notify on UI thread that tags changed
                    _textView?.VisualElement?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        if (!_disposed)
                        {
                            var currentSnapshot = _buffer.CurrentSnapshot;
                            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                                new SnapshotSpan(currentSnapshot, 0, currentSnapshot.Length)));
                        }
                    }));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch
            {
                // Ignore background calculation errors
            }
        }, token);
    }

    private static bool AreWidthsEqual(int[] a, int[] b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    private int[] CalculateAllColumnWidths(ITextSnapshot snapshot, CancellationToken token)
    {
        var columnMaxWidths = new List<int>();
        var lineCount = snapshot.LineCount;

        // Process all lines, but check cancellation periodically
        for (var i = 0; i < lineCount; i++)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException();

            var line = snapshot.GetLineFromLineNumber(i);
            var row = CsvParser.ParseLine(line.GetText(), _detectedDelimiter, i);

            // Ensure we have enough slots
            while (columnMaxWidths.Count < row.Count)
            {
                columnMaxWidths.Add(0);
            }

            // Update max widths
            for (var col = 0; col < row.Count; col++)
            {
                var cellWidth = row[col].Span.Length;
                if (cellWidth > columnMaxWidths[col])
                {
                    columnMaxWidths[col] = cellWidth;
                }
            }
        }

        return columnMaxWidths.ToArray();
    }

    private void OnClosed(object sender, EventArgs e)
    {
        Dispose();
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Re-detect delimiter if change is near the beginning
        if (e.Changes.Count > 0 && e.Changes[0].OldPosition < 500)
        {
            _delimiterDetected = false;
        }

        // DON'T invalidate _columnWidths here - keep using cached widths during typing
        // Schedule debounced recalculation instead
        ScheduleBackgroundCalculation();
    }

    public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0 || _disposed || _textView == null)
            yield break;

        var snapshot = spans[0].Snapshot;

        // Detect delimiter if needed
        if (!_delimiterDetected)
        {
            DetectDelimiter(snapshot);
        }

        // If widths not yet calculated, use quick initial calculation
        if (_columnWidths == null)
        {
            _columnWidths = CalculateColumnWidthsQuick(snapshot);
        }

        if (_columnWidths == null || _columnWidths.Length == 0)
            yield break;

        // Get character width - must have text view lines
        var textViewLines = _textView.TextViewLines;
        if (textViewLines == null || textViewLines.Count == 0)
            yield break;

        var charWidth = GetCharacterWidth(textViewLines);
        if (charWidth <= 0)
            yield break;

        foreach (var span in spans)
        {
            var startLine = snapshot.GetLineFromPosition(span.Start);
            var endLine = snapshot.GetLineFromPosition(span.End);

            for (var lineNum = startLine.LineNumber; lineNum <= endLine.LineNumber; lineNum++)
            {
                var line = snapshot.GetLineFromLineNumber(lineNum);
                foreach (var tag in GetTagsForLine(line, charWidth))
                {
                    yield return tag;
                }
            }
        }
    }

    private double GetCharacterWidth(ITextViewLineCollection textViewLines)
    {
        foreach (var line in textViewLines)
        {
            if (line.Length > 0)
            {
                try
                {
                    var bounds = line.GetCharacterBounds(line.Start);
                    if (bounds.Width > 0)
                        return bounds.Width;
                }
                catch
                {
                    // Continue to next line
                }
            }
        }

        // Fallback
        return _textView.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize * 0.6 ?? 8.0;
    }

    private IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTagsForLine(ITextSnapshotLine line, double charWidth)
    {
        var lineText = line.GetText();
        if (string.IsNullOrEmpty(lineText))
            yield break;

        var row = CsvParser.ParseLine(lineText, _detectedDelimiter, line.LineNumber, line.Start.Position);

        // Don't add padding after the last column (no delimiter there)
        var columnsToProcess = Math.Min(row.Count - 1, _columnWidths.Length - 1);

        for (var col = 0; col < columnsToProcess; col++)
        {
            var cell = row[col];
            var cellCharWidth = cell.Span.Length;
            var maxCharWidth = _columnWidths[col];
            var paddingChars = maxCharWidth - cellCharWidth;

            if (paddingChars > 0)
            {
                // Create padding element using spaces in a monospace font
                var spacer = new TextBlock
                {
                    Text = new string(' ', paddingChars),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = _textView.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize ?? 12,
                    Background = Brushes.Transparent,
                    Foreground = Brushes.Transparent
                };

                // Position: after the delimiter (cell end + 1 for the delimiter)
                var afterDelimiter = cell.Span.Start + cell.Span.Length + 1;

                // Make sure we're within the line bounds
                if (afterDelimiter > line.End.Position)
                    continue;

                var tagSpan = new SnapshotSpan(line.Snapshot, afterDelimiter, 0);
                var tag = new IntraTextAdornmentTag(spacer, null);

                yield return new TagSpan<IntraTextAdornmentTag>(tagSpan, tag);
            }
        }
    }

    private void DetectDelimiter(ITextSnapshot snapshot)
    {
        var length = Math.Min(snapshot.Length, 2000);
        var text = snapshot.GetText(0, length);
        var delimiter = DelimiterDetector.Detect(text);
        _detectedDelimiter = delimiter.ToChar();
        _delimiterDetected = true;
    }

    private int[] CalculateColumnWidthsQuick(ITextSnapshot snapshot)
    {
        var columnMaxWidths = new List<int>();

        // Quick sample: first 50 lines only for initial display
        var linesToSample = Math.Min(snapshot.LineCount, 50);

        for (var i = 0; i < linesToSample; i++)
        {
            var line = snapshot.GetLineFromLineNumber(i);
            var row = CsvParser.ParseLine(line.GetText(), _detectedDelimiter, i);

            while (columnMaxWidths.Count < row.Count)
            {
                columnMaxWidths.Add(0);
            }

            for (var col = 0; col < row.Count; col++)
            {
                var cellWidth = row[col].Span.Length;
                if (cellWidth > columnMaxWidths[col])
                {
                    columnMaxWidths[col] = cellWidth;
                }
            }
        }

        return columnMaxWidths.ToArray();
    }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _debounceTimer?.Stop();
            _calculationCts?.Cancel();
            _calculationCts?.Dispose();

            _buffer.Changed -= OnBufferChanged;
            if (_textView != null)
            {
                _textView.Closed -= OnClosed;
                _textView.LayoutChanged -= OnLayoutChanged;
            }
        }
    }
