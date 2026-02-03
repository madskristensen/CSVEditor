using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using CSVEditor.Classification;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;

namespace CSVEditor.Adornments;

/// <summary>
/// Tagger that creates virtual whitespace adornments to align CSV columns.
/// </summary>
internal sealed class CsvColumnAlignmentTagger : ITagger<IntraTextAdornmentTag>, IDisposable
{
    private readonly IWpfTextView _textView;
    private readonly ITextBuffer _buffer;
    private readonly CsvBufferCache _cache;
    private int[] _columnWidths;
    private bool _disposed;
    private CancellationTokenSource _calculationCts;

    // Debounce timer to avoid recalculating on every keystroke
    private System.Windows.Threading.DispatcherTimer _debounceTimer;
    private const int _debounceDelayMs = 300;

    // Cached WPF resources to reduce allocations
    private static readonly FontFamily _cachedFontFamily = new("Consolas");
    private static readonly Brush _transparentBrush = Brushes.Transparent;

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvColumnAlignmentTagger(IWpfTextView textView, ITextBuffer buffer)
    {
        _textView = textView;
        _buffer = buffer;
        _cache = CsvBufferCache.GetOrCreate(buffer);

        _buffer.Changed += OnBufferChanged;
        if (_textView != null)
        {
            _textView.Closed += OnClosed;

            // Initialize debounce timer on UI thread
            _debounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_debounceDelayMs)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;
        }

        // Register for alignment state changes
        CsvAlignmentState.RegisterStateChangedHandler(buffer, OnAlignmentStateChanged);
    }

    private void OnDebounceTimerTick(object sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        if (_disposed) return;

        // Now actually start the background recalculation
        StartBackgroundWidthCalculation();
    }

    private void OnAlignmentStateChanged(bool enabled)
    {
        if (_disposed) return;

        if (enabled)
        {
            // Start calculating widths when alignment is enabled
            _columnWidths = null;
            StartBackgroundWidthCalculation();
        }
        else
        {
            // Clear widths and refresh when disabled
            _columnWidths = null;
            _debounceTimer?.Stop();
            RaiseTagsChanged();
        }
    }

    private void RaiseTagsChanged()
    {
        if (_disposed) return;

        ITextSnapshot snapshot = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
            new SnapshotSpan(snapshot, 0, snapshot.Length)));
    }

    private void StartBackgroundWidthCalculation()
    {
        if (!CsvAlignmentState.IsEnabled(_buffer))
            return;

        _calculationCts?.Cancel();
        _calculationCts = new CancellationTokenSource();
        CancellationToken token = _calculationCts.Token;
        ITextSnapshot snapshot = _buffer.CurrentSnapshot;

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
                            RaiseTagsChanged();
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
        var delimiter = _cache.GetDelimiter(snapshot);

        // Process all lines, but check cancellation periodically
        for (var i = 0; i < lineCount; i++)
        {
            if (token.IsCancellationRequested)
                throw new OperationCanceledException();

            ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
            CsvRow row = CsvParser.ParseLine(line.GetText(), delimiter, i);

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

        return [.. columnMaxWidths];
    }

    private void OnClosed(object sender, EventArgs e)
    {
        Dispose();
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Only recalculate if alignment is enabled
        if (CsvAlignmentState.IsEnabled(_buffer))
        {
            // Use debounce: don't invalidate column widths immediately.
            // Keep existing widths while typing for smooth experience.
            // Only recalculate after typing pauses.
            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
    }

    public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        // Only produce tags if alignment is enabled for this buffer
        if (spans.Count == 0 || _disposed || _textView == null || !CsvAlignmentState.IsEnabled(_buffer))
            yield break;

        ITextSnapshot snapshot = spans[0].Snapshot;

        // If widths not yet calculated, calculate now
        if (_columnWidths == null)
        {
            _columnWidths = CalculateColumnWidthsQuick(snapshot);
            // Also start full calculation in background
            StartBackgroundWidthCalculation();
        }

        if (_columnWidths == null || _columnWidths.Length == 0)
            yield break;

        // Get character width - must have text view lines
        IWpfTextViewLineCollection textViewLines = _textView.TextViewLines;
        if (textViewLines == null || textViewLines.Count == 0)
            yield break;

        var charWidth = GetCharacterWidth(textViewLines);
        if (charWidth <= 0)
            yield break;

        foreach (SnapshotSpan span in spans)
        {
            ITextSnapshotLine startLine = snapshot.GetLineFromPosition(span.Start);
            ITextSnapshotLine endLine = snapshot.GetLineFromPosition(span.End);

            for (var lineNum = startLine.LineNumber; lineNum <= endLine.LineNumber; lineNum++)
            {
                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNum);
                foreach (ITagSpan<IntraTextAdornmentTag> tag in GetTagsForLine(line, charWidth))
                {
                    yield return tag;
                }
            }
        }
    }

    private double GetCharacterWidth(ITextViewLineCollection textViewLines)
    {
        foreach (ITextViewLine line in textViewLines)
        {
            if (line.Length > 0)
            {
                try
                {
                    TextBounds bounds = line.GetCharacterBounds(line.Start);
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

        // Use shared cache to get parsed line
        CsvRow row = _cache.GetParsedLine(line);

        // Don't add padding after the last column (no delimiter there)
        var columnsToProcess = Math.Min(row.Count - 1, _columnWidths.Length - 1);
        var fontSize = _textView.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize ?? 12;

        for (var col = 0; col < columnsToProcess; col++)
        {
            CsvCell cell = row[col];
            var cellCharWidth = cell.Span.Length;
            var maxCharWidth = _columnWidths[col];
            var paddingChars = maxCharWidth - cellCharWidth;

            if (paddingChars > 0)
            {
                // Create padding element using cached resources to reduce allocations
                var spacer = new TextBlock
                {
                    Text = new string(' ', paddingChars),
                    FontFamily = _cachedFontFamily,
                    FontSize = fontSize,
                    Background = _transparentBrush,
                    Foreground = _transparentBrush
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

    private int[] CalculateColumnWidthsQuick(ITextSnapshot snapshot)
    {
        var columnMaxWidths = new List<int>();
        var delimiter = _cache.GetDelimiter(snapshot);

        // Quick sample: first 50 lines only for initial display
        var linesToSample = Math.Min(snapshot.LineCount, 50);

        for (var i = 0; i < linesToSample; i++)
        {
            ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
            CsvRow row = CsvParser.ParseLine(line.GetText(), delimiter, i);

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

        return [.. columnMaxWidths];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _calculationCts?.Cancel();
        _calculationCts?.Dispose();

        if (_debounceTimer != null)
        {
            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceTimerTick;
            _debounceTimer = null;
        }

        _buffer.Changed -= OnBufferChanged;
        if (_textView != null)
        {
            _textView.Closed -= OnClosed;
        }
    }
}
