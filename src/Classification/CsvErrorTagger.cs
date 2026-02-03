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
internal sealed class CsvErrorTagger : ITagger<IErrorTag>
{
    private readonly IWpfTextView _textView;
    private readonly ITextBuffer _textBuffer;
    private readonly CsvBufferCache _cache;

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
        }
    }

    private void OnDebounceTimerTick(object sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        StartBackgroundValidation();
    }

    private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        ITextSnapshot snapshot = e.After;

        // For very large files, use background-only validation
        if (snapshot.LineCount > LargeFileThresholds.LargeFileLineCount)
        {
            // Restart debounce timer for background validation
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
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
        _validationCts?.Cancel();
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
            var errorSpan = new SnapshotSpan(line.Start + quoteStartIndex, line.End);
            return new TagSpan<IErrorTag>(errorSpan,
                new ErrorTag(PredefinedErrorTypeNames.SyntaxError, "Unclosed quote"));
        }

        // Check column count (skip header row)
        if (lineNumber > 0 && expectedColumnCount > 0)
        {
            CsvRow row = CsvParser.ParseLine(lineText, delimiter, lineNumber, line.Start.Position);
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
        if (spans.Count == 0)
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

        CsvRow row = _cache.GetParsedLine(line);

        ITagSpan<IErrorTag> unclosedQuoteError = CheckUnclosedQuotes(line, lineText);
        if (unclosedQuoteError != null)
        {
            yield return unclosedQuoteError;
            yield break;
        }

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

    private ITagSpan<IErrorTag> CheckUnclosedQuotes(ITextSnapshotLine line, string lineText)
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
            var errorSpan = new SnapshotSpan(line.Start + quoteStartIndex, line.End);
            return new TagSpan<IErrorTag>(
                errorSpan,
                new ErrorTag(PredefinedErrorTypeNames.SyntaxError, "Unclosed quote"));
        }

        return null;
    }
}
