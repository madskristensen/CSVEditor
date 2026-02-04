using Community.VisualStudio.Toolkit;
using CSVEditor.Classification;
using CSVEditor.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace CSVEditor.Adornments;

/// <summary>
/// Lightweight spacer element that avoids the overhead of Border/TextBlock.
/// Uses direct size specification without dependency property overhead.
/// </summary>
internal sealed class SpacerElement : FrameworkElement
{
    public SpacerElement(double width, double height)
    {
        Width = width;
        Height = height;
    }

    // No rendering needed - this is just an invisible spacer
    protected override void OnRender(DrawingContext drawingContext) { }
}

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

    // Debounce timer to avoid recalculating column widths on every keystroke
    private System.Windows.Threading.DispatcherTimer _debounceTimer;
    private const int _debounceDelayMs = 300;

    // Track visible line range to detect scrolling
    private int _lastFirstVisibleLine = -1;
    private int _lastLastVisibleLine = -1;

    // Cache for line tags to avoid recreating WPF elements on every GetTags call
    private readonly Dictionary<int, (int Version, int[] Widths, List<ITagSpan<IntraTextAdornmentTag>> Tags)> _lineTagCache = [];
    private const int _maxCachedLines = 200;
    private const int _scrollBuffer = 50;

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
            _textView.LayoutChanged += OnLayoutChanged;

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

    private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
    {
        // Only handle scrolling when alignment is enabled and widths are calculated
        if (_disposed || !CsvAlignmentState.IsEnabled(_buffer) || _columnWidths == null)
            return;

        // Check if the visible range has actually changed (scrolling occurred)
        if (_textView?.TextViewLines == null || _textView.TextViewLines.Count == 0)
            return;

        var firstVisibleLine = _textView.TextViewLines.FirstVisibleLine.Start.GetContainingLine().LineNumber;
        var lastVisibleLine = _textView.TextViewLines.LastVisibleLine.End.GetContainingLine().LineNumber;

        // Only raise TagsChanged if we've scrolled to show new lines
        if (firstVisibleLine != _lastFirstVisibleLine || lastVisibleLine != _lastLastVisibleLine)
        {
            _lastFirstVisibleLine = firstVisibleLine;
            _lastLastVisibleLine = lastVisibleLine;

            // Notify only for the newly visible range (not full document)
            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            ITextSnapshotLine firstLine = snapshot.GetLineFromLineNumber(firstVisibleLine);
            ITextSnapshotLine lastLine = snapshot.GetLineFromLineNumber(lastVisibleLine);
            var visibleSpan = new SnapshotSpan(firstLine.Start, lastLine.End);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(visibleSpan));
        }
    }

    private void OnDebounceTimerTick(object sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        if (_disposed) return;

        StartBackgroundWidthCalculation();
    }

    private void OnAlignmentStateChanged(bool enabled)
    {
        if (_disposed) return;

        if (enabled)
        {
            // Start calculating widths when alignment is enabled
            _columnWidths = null;
            _lineTagCache.Clear();
            StartBackgroundWidthCalculation();
        }
        else
        {
            // Clear widths and refresh when disabled
            _columnWidths = null;
            _lineTagCache.Clear();
            _debounceTimer?.Stop();
            RaiseTagsChanged();
        }
    }

    private void RaiseTagsChanged()
    {
        if (_disposed) return;

        // Only notify for visible lines to avoid expensive full-document processing.
        // The LayoutChanged handler will notify for newly visible lines when scrolling.
        if (_textView?.TextViewLines != null && _textView.TextViewLines.Count > 0)
        {
            var firstVisibleLine = _textView.TextViewLines.FirstVisibleLine.Start.GetContainingLine().LineNumber;
            var lastVisibleLine = _textView.TextViewLines.LastVisibleLine.End.GetContainingLine().LineNumber;

            // Update tracked range
            _lastFirstVisibleLine = firstVisibleLine;
            _lastLastVisibleLine = lastVisibleLine;

            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            ITextSnapshotLine firstLine = snapshot.GetLineFromLineNumber(firstVisibleLine);
            ITextSnapshotLine lastLine = snapshot.GetLineFromLineNumber(lastVisibleLine);
            var visibleSpan = new SnapshotSpan(firstLine.Start, lastLine.End);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(visibleSpan));
        }
    }

    private void StartBackgroundWidthCalculation()
    {
        if (!CsvAlignmentState.IsEnabled(_buffer) || _disposed)
            return;

        _calculationCts?.Cancel();
        _calculationCts?.Dispose();
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
                    // Switch to UI thread to update tags
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        if (!_disposed)
                        {
                            _lineTagCache.Clear();
                            RaiseTagsChanged();
                        }
                    }).FireAndForget();
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

        // For very large files, use sampling instead of full scan
        // This provides a good approximation while keeping typing responsive
        var sampleSize = LargeFileThresholds.ColumnWidthSampleSize;
        const int CheckCancellationInterval = 100;

        if (lineCount > LargeFileThresholds.LargeFileLineCount)
        {
            // Sample evenly distributed lines throughout the file
            var step = Math.Max(1, lineCount / sampleSize);
            for (var i = 0; i < lineCount; i += step)
            {
                if (i % CheckCancellationInterval == 0 && token.IsCancellationRequested)
                    throw new OperationCanceledException();

                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
                CsvRow row = CsvParser.ParseLine(line.GetText(), delimiter, i);

                while (columnMaxWidths.Count < row.Count)
                    columnMaxWidths.Add(0);

                for (var col = 0; col < row.Count; col++)
                {
                    var cellWidth = row[col].Span.Length;
                    if (cellWidth > columnMaxWidths[col])
                        columnMaxWidths[col] = cellWidth;
                }
            }
        }
        else
        {
            // For smaller files, scan all lines
            for (var i = 0; i < lineCount; i++)
            {
                if (i % CheckCancellationInterval == 0 && token.IsCancellationRequested)
                    throw new OperationCanceledException();

                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(i);
                CsvRow row = CsvParser.ParseLine(line.GetText(), delimiter, i);

                while (columnMaxWidths.Count < row.Count)
                    columnMaxWidths.Add(0);

                for (var col = 0; col < row.Count; col++)
                {
                    var cellWidth = row[col].Span.Length;
                    if (cellWidth > columnMaxWidths[col])
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
        if (_disposed)
            return;

        if (CsvAlignmentState.IsEnabled(_buffer))
        {
            // Invalidate cache for changed lines
            foreach (ITextChange change in e.Changes)
            {
                var startLine = e.After.GetLineNumberFromPosition(change.NewPosition);
                var endLine = e.After.GetLineNumberFromPosition(change.NewEnd);
                for (var line = startLine; line <= endLine; line++)
                {
                    _lineTagCache.Remove(line);
                }
            }

            // Debounce: recalculate column widths after typing pauses
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }
    }

    public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0 || _disposed || _textView == null || !CsvAlignmentState.IsEnabled(_buffer))
            yield break;

        ITextSnapshot snapshot = spans[0].Snapshot;
        var snapshotVersion = snapshot.Version.VersionNumber;

        if (_columnWidths == null)
        {
            _columnWidths = CalculateColumnWidthsQuick(snapshot);
            StartBackgroundWidthCalculation();
        }

        if (_columnWidths == null || _columnWidths.Length == 0)
            yield break;

        IWpfTextViewLineCollection textViewLines = _textView.TextViewLines;
        if (textViewLines == null || textViewLines.Count == 0)
            yield break;

        var charWidth = GetCharacterWidth(textViewLines);
        if (charWidth <= 0)
            yield break;

        // Only process visible lines + buffer for smooth scrolling
        var firstVisibleLineNum = textViewLines.FirstVisibleLine.Start.GetContainingLine().LineNumber;
        var lastVisibleLineNum = textViewLines.LastVisibleLine.End.GetContainingLine().LineNumber;
        var minLine = Math.Max(0, firstVisibleLineNum - _scrollBuffer);
        var maxLine = Math.Min(snapshot.LineCount - 1, lastVisibleLineNum + _scrollBuffer);

        var currentWidths = _columnWidths;
        var fontSize = _textView.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize ?? 12;
        var delimiter = _cache.GetDelimiter(snapshot);
        var isTabDelimited = delimiter == '\t';

        foreach (SnapshotSpan span in spans)
        {
            var startLineNum = snapshot.GetLineFromPosition(span.Start).LineNumber;
            var endLineNum = snapshot.GetLineFromPosition(span.End).LineNumber;

            for (var lineNum = startLineNum; lineNum <= endLineNum; lineNum++)
            {
                if (lineNum < minLine || lineNum > maxLine)
                    continue;

                if (_lineTagCache.TryGetValue(lineNum, out (int Version, int[] Widths, List<ITagSpan<IntraTextAdornmentTag>> Tags) cached) &&
                    cached.Version == snapshotVersion &&
                    AreWidthsEqual(cached.Widths, currentWidths))
                {
                    foreach (ITagSpan<IntraTextAdornmentTag> tag in cached.Tags)
                        yield return tag;
                    continue;
                }

                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNum);
                var lineTags = new List<ITagSpan<IntraTextAdornmentTag>>();

                foreach (ITagSpan<IntraTextAdornmentTag> tag in GetTagsForLine(line, charWidth, fontSize, isTabDelimited))
                {
                    lineTags.Add(tag);
                    yield return tag;
                }

                CacheLineTags(lineNum, snapshotVersion, currentWidths, lineTags);
            }
        }
    }

    private void CacheLineTags(int lineNum, int version, int[] widths, List<ITagSpan<IntraTextAdornmentTag>> tags)
    {
        if (_lineTagCache.Count >= _maxCachedLines)
        {
            var keysToRemove = _lineTagCache.Keys.Take(_maxCachedLines / 2).ToList();
            foreach (var key in keysToRemove)
            {
                _lineTagCache.Remove(key);
            }
        }

        // Clone widths array to avoid reference issues if _columnWidths changes
        _lineTagCache[lineNum] = (version, (int[])widths.Clone(), tags);
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

    private IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTagsForLine(
            ITextSnapshotLine line, double charWidth, double fontSize, bool isTabDelimited)
    {
        var lineText = line.GetText();
        if (string.IsNullOrEmpty(lineText))
            yield break;

        CsvRow row = _cache.GetParsedLine(line);
        var columnsToProcess = Math.Min(row.Count - 1, _columnWidths.Length - 1);

        for (var col = 0; col < columnsToProcess; col++)
        {
            CsvCell cell = row[col];
            var paddingChars = _columnWidths[col] - cell.Span.Length;
            var delimiterPos = cell.Span.Start + cell.Span.Length;

            if (delimiterPos >= line.End.Position)
                continue;

            if (isTabDelimited)
            {
                // For TSV: replace the tab with a fixed-width spacer
                var spacerWidth = (paddingChars + 2) * charWidth;
                var spacer = new SpacerElement(spacerWidth, fontSize);
                var tagSpan = new SnapshotSpan(line.Snapshot, delimiterPos, 1);
                yield return new TagSpan<IntraTextAdornmentTag>(tagSpan, new IntraTextAdornmentTag(spacer, null));
            }
            else if (paddingChars > 0)
            {
                // For CSV: add padding after the delimiter
                var afterDelimiter = delimiterPos + 1;
                if (afterDelimiter > line.End.Position)
                    continue;

                var spacerWidth = paddingChars * charWidth;
                var spacer = new SpacerElement(spacerWidth, fontSize);
                var tagSpan = new SnapshotSpan(line.Snapshot, afterDelimiter, 0);
                yield return new TagSpan<IntraTextAdornmentTag>(tagSpan, new IntraTextAdornmentTag(spacer, null));
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
            _textView.LayoutChanged -= OnLayoutChanged;
        }
    }
}
