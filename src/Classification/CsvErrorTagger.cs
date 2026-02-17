using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Provides error taggers for CSV validation.
/// </summary>
[Export(typeof(IViewTaggerProvider))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TagType(typeof(IErrorTag))]
internal sealed class CsvErrorTaggerProvider : IViewTaggerProvider
{
    public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
    {
        return buffer.Properties.GetOrCreateSingletonProperty(
            () => new CsvErrorTagger(textView as IWpfTextView, buffer)) as ITagger<T>;
    }
}

/// <summary>
/// Tagger that provides error squiggles for CSV validation issues.
/// Uses background validation for large files to keep typing responsive.
/// </summary>
internal sealed class CsvErrorTagger : ITagger<IErrorTag>, IDisposable
{
    private readonly IWpfTextView _textView;
    private readonly ITextBuffer _textBuffer;
    private readonly CsvBufferCache _cache;
    private bool _disposed;

    // Background validation state
    private CancellationTokenSource _validationCts;
    private readonly Dictionary<int, ITagSpan<IErrorTag>> _backgroundErrors = [];
    private int _backgroundErrorsVersion = -1;
    private readonly object _errorLock = new();

    // Debounce timer for background validation
    private readonly System.Windows.Threading.DispatcherTimer _debounceTimer;
    private const int _debounceDelayMs = 500;

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvErrorTagger(IWpfTextView textView, ITextBuffer textBuffer)
    {
        _textView = textView;
        _textBuffer = textBuffer;
        _cache = CsvBufferCache.GetOrCreate(textBuffer);
        _textBuffer.Changed += OnTextBufferChanged;

        // Initialize debounce timer
        if (_textView != null)
        {
            _debounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_debounceDelayMs)
            };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _textView.Closed += OnViewClosed;
        }
    }

    private void OnViewClosed(object sender, EventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _textBuffer.Changed -= OnTextBufferChanged;

        if (_textView != null)
        {
            _textView.Closed -= OnViewClosed;
        }

        _debounceTimer?.Stop();
        _validationCts?.Cancel();
        _validationCts?.Dispose();
    }

    private void OnDebounceTimerTick(object sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        if (!_disposed)
        {
            StartBackgroundValidation();
        }
    }

    private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        if (_disposed)
            return;

        ITextSnapshot snapshot = e.After;

        // For very large files, use background-only validation
        if (snapshot.LineCount > LargeFileThresholds.LargeFileLineCount)
        {
            // Restart debounce timer for background validation
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
            return;
        }

        // Check if any change involves a quote character. Quote edits can change
        // multi-line quote state on nearby lines, so refresh a bounded region
        // around the change to clear stale errors.
        var hasQuoteChange = false;
        foreach (ITextChange change in e.Changes)
        {
            if (change.OldText.IndexOf('"') >= 0 || change.NewText.IndexOf('"') >= 0)
            {
                hasQuoteChange = true;
                break;
            }
        }

        if (hasQuoteChange)
        {
            const int contextLines = 20;
            foreach (ITextChange change in e.Changes)
            {
                ITextSnapshotLine changeLine = snapshot.GetLineFromPosition(change.NewPosition);
                var startLineNum = Math.Max(0, changeLine.LineNumber - contextLines);
                var endLineNum = Math.Min(snapshot.LineCount - 1, changeLine.LineNumber + contextLines);
                ITextSnapshotLine startLine = snapshot.GetLineFromLineNumber(startLineNum);
                ITextSnapshotLine endLine = snapshot.GetLineFromLineNumber(endLineNum);
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(startLine.Start, endLine.End)));
            }
            return;
        }

        // For smaller files, notify about changed lines immediately
        foreach (ITextChange change in e.Changes)
        {
            ITextSnapshotLine startLine = snapshot.GetLineFromPosition(change.NewPosition);
            ITextSnapshotLine endLine = snapshot.GetLineFromPosition(change.NewEnd);
            var changedSpan = new SnapshotSpan(startLine.Start, endLine.End);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(changedSpan));
        }
    }

    private void StartBackgroundValidation()
    {
        if (_disposed)
            return;

        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = new CancellationTokenSource();

        ITextSnapshot snapshot = _textBuffer.CurrentSnapshot;
        CancellationToken token = _validationCts.Token;

        Task.Run(() => ValidateInBackground(snapshot, token), token);
    }

    private void ValidateInBackground(ITextSnapshot snapshot, CancellationToken token)
    {
        try
        {
            // Skip validation for extremely large files
            if (snapshot.LineCount > LargeFileThresholds.DisableErrorValidationLineCount)
                return;

            var newErrors = new Dictionary<int, ITagSpan<IErrorTag>>();
            var expectedColumnCount = _cache.GetExpectedColumnCount(snapshot);
            var delimiter = _cache.GetDelimiter(snapshot);

            for (var lineNumber = 0; lineNumber < snapshot.LineCount; lineNumber++)
            {
                // Check cancellation periodically
                if (lineNumber % 100 == 0 && token.IsCancellationRequested)
                    return;

                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNumber);
                var lineText = line.GetText();

                if (string.IsNullOrEmpty(lineText))
                    continue;

                // Check for errors
                ITagSpan<IErrorTag> error = ValidateLineBackground(line, lineText, lineNumber, expectedColumnCount, delimiter);
                if (error != null)
                {
                    newErrors[lineNumber] = error;
                }
            }

            if (token.IsCancellationRequested)
                return;

            // Update cached errors
            lock (_errorLock)
            {
                _backgroundErrors.Clear();
                foreach (KeyValuePair<int, ITagSpan<IErrorTag>> kvp in newErrors)
                {
                    _backgroundErrors[kvp.Key] = kvp.Value;
                }
                _backgroundErrorsVersion = snapshot.Version.VersionNumber;
            }

            // Notify UI thread
            _textView?.VisualElement?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(snapshot, 0, snapshot.Length)));
            }));
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            // Ignore background errors
        }
    }

    private ITagSpan<IErrorTag> ValidateLineBackground(ITextSnapshotLine line, string lineText, int lineNumber, int expectedColumnCount, char delimiter)
    {
        ITextSnapshot snapshot = line.Snapshot;

        // Skip continuation lines inside multi-line quoted fields
        if (_cache.IsLineInsideMultiLineQuote(snapshot, lineNumber))
            return null;

        // Check for unclosed quotes
        var inQuotes = false;
        var quoteStartIndex = -1;

        for (var i = 0; i < lineText.Length; i++)
        {
            if (lineText[i] == '"')
            {
                if (inQuotes && i + 1 < lineText.Length && lineText[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                if (!inQuotes)
                    quoteStartIndex = i;
                inQuotes = !inQuotes;
            }
        }

        if (inQuotes && quoteStartIndex >= 0)
        {
            // Only flag as error if the document actually ends with an unclosed quote
            // and this line is the origin of that unclosed region
            if (!_cache.DocumentEndsInQuote(snapshot))
                return null;

            var errorSpan = new SnapshotSpan(line.Start + quoteStartIndex, line.End);
            return new TagSpan<IErrorTag>(errorSpan,
                new ErrorTag(PredefinedErrorTypeNames.SyntaxError, "Unclosed quote"));
        }

        // Skip column count validation for lines that start a multi-line quoted field.
        // ParseLine operates on single lines and cannot correctly count columns for partial rows.
        if (_cache.IsLineInsideMultiLineQuote(snapshot, lineNumber + 1))
            return null;

        // Check column count (skip header row)
        if (lineNumber > 0 && expectedColumnCount > 0)
        {
            // Use cache to avoid duplicate parsing
            CsvRow row = _cache.GetParsedLine(line);
            if (row.Count != expectedColumnCount)
            {
                var errorSpan = new SnapshotSpan(line.Start, line.Length);
                var errorMessage = row.Count < expectedColumnCount
                    ? $"Too few columns: expected {expectedColumnCount}, found {row.Count}"
                    : $"Too many columns: expected {expectedColumnCount}, found {row.Count}";
                return new TagSpan<IErrorTag>(errorSpan,
                    new ErrorTag(PredefinedErrorTypeNames.Warning, errorMessage));
            }
        }

        return null;
    }

    public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (_disposed || spans.Count == 0)
            yield break;

        ITextSnapshot snapshot = spans[0].Snapshot;

        // Disable for extremely large files
        if (snapshot.LineCount > LargeFileThresholds.DisableErrorValidationLineCount)
            yield break;

        // For large files, use background-cached errors only
        if (snapshot.LineCount > LargeFileThresholds.LargeFileLineCount)
        {
            lock (_errorLock)
            {
                // Only use cached errors if they match current version
                if (_backgroundErrorsVersion != snapshot.Version.VersionNumber)
                {
                    // Start background validation if not running
                    _debounceTimer?.Stop();
                    _debounceTimer?.Start();
                    yield break;
                }

                foreach (SnapshotSpan span in spans)
                {
                    ITextSnapshotLine startLine = snapshot.GetLineFromPosition(span.Start);
                    ITextSnapshotLine endLine = snapshot.GetLineFromPosition(span.End);

                    for (var lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
                    {
                        if (_backgroundErrors.TryGetValue(lineNumber, out ITagSpan<IErrorTag> error))
                        {
                            yield return error;
                        }
                    }
                }
            }
            yield break;
        }

        // For smaller files, validate synchronously (fast path)
        var expectedColumnCount = _cache.GetExpectedColumnCount(snapshot);

        foreach (SnapshotSpan span in spans)
        {
            ITextSnapshotLine startLine = snapshot.GetLineFromPosition(span.Start);
            ITextSnapshotLine endLine = snapshot.GetLineFromPosition(span.End);

            for (var lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
            {
                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNumber);
                foreach (ITagSpan<IErrorTag> error in ValidateLine(line, lineNumber, expectedColumnCount))
                {
                    yield return error;
                }
            }
        }
    }

    private IEnumerable<ITagSpan<IErrorTag>> ValidateLine(ITextSnapshotLine line, int lineNumber, int expectedColumnCount)
    {
        var lineText = line.GetText();

        if (string.IsNullOrEmpty(lineText))
            yield break;

        ITextSnapshot snapshot = line.Snapshot;

        // Skip continuation lines inside multi-line quoted fields
        if (_cache.IsLineInsideMultiLineQuote(snapshot, lineNumber))
            yield break;

        ITagSpan<IErrorTag> unclosedQuoteError = CheckUnclosedQuotes(line, lineText, snapshot);
        if (unclosedQuoteError != null)
        {
            yield return unclosedQuoteError;
            yield break;
        }

        // Skip column count validation for lines that start a multi-line quoted field.
        // ParseLine operates on single lines and cannot correctly count columns for partial rows.
        if (_cache.IsLineInsideMultiLineQuote(snapshot, lineNumber + 1))
            yield break;

        CsvRow row = _cache.GetParsedLine(line);

        if (lineNumber > 0 && expectedColumnCount > 0)
        {
            if (row.Count != expectedColumnCount)
            {
                var errorSpan = new SnapshotSpan(line.Start, line.Length);
                var errorMessage = row.Count < expectedColumnCount
                    ? $"Too few columns: expected {expectedColumnCount}, found {row.Count}"
                    : $"Too many columns: expected {expectedColumnCount}, found {row.Count}";

                yield return new TagSpan<IErrorTag>(
                    errorSpan,
                    new ErrorTag(PredefinedErrorTypeNames.Warning, errorMessage));
            }
        }
    }

    private ITagSpan<IErrorTag> CheckUnclosedQuotes(ITextSnapshotLine line, string lineText, ITextSnapshot snapshot)
    {
        var inQuotes = false;
        var quoteStartIndex = -1;

        for (var i = 0; i < lineText.Length; i++)
        {
            if (lineText[i] == '"')
            {
                if (inQuotes && i + 1 < lineText.Length && lineText[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                if (!inQuotes)
                    quoteStartIndex = i;
                inQuotes = !inQuotes;
            }
        }

        if (inQuotes && quoteStartIndex >= 0)
        {
            // Only flag as error if the document actually ends with an unclosed quote.
            // Otherwise, this is a valid multi-line quoted field per RFC 4180.
            if (!_cache.DocumentEndsInQuote(snapshot))
                return null;

            var errorSpan = new SnapshotSpan(line.Start + quoteStartIndex, line.End);
            return new TagSpan<IErrorTag>(
                errorSpan,
                new ErrorTag(PredefinedErrorTypeNames.SyntaxError, "Unclosed quote"));
        }

        return null;
    }
}
