using System.Collections.Generic;
using System.ComponentModel.Composition;
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
            () => new CsvErrorTagger(buffer)) as ITagger<T>;
    }
}

/// <summary>
/// Tagger that provides error squiggles for CSV validation issues.
/// </summary>
internal sealed class CsvErrorTagger : ITagger<IErrorTag>
{
    private readonly ITextBuffer _textBuffer;
    private readonly CsvBufferCache _cache;

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvErrorTagger(ITextBuffer textBuffer)
    {
        _textBuffer = textBuffer;
        _cache = CsvBufferCache.GetOrCreate(textBuffer);
        _textBuffer.Changed += OnTextBufferChanged;
    }

    private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Only invalidate affected lines, not the entire document
        foreach (ITextChange change in e.Changes)
        {
            ITextSnapshotLine startLine = e.After.GetLineFromPosition(change.NewPosition);
            ITextSnapshotLine endLine = e.After.GetLineFromPosition(change.NewEnd);
            var changedSpan = new SnapshotSpan(startLine.Start, endLine.End);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(changedSpan));
        }
    }

    public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0)
            yield break;

        ITextSnapshot snapshot = spans[0].Snapshot;
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

        // Use shared cache to get parsed line
        CsvRow row = _cache.GetParsedLine(line);

        // Check for unclosed quotes by looking for cells that start with quote but aren't marked as quoted properly
        ITagSpan<IErrorTag> unclosedQuoteError = CheckUnclosedQuotes(line, lineText);
        if (unclosedQuoteError != null)
        {
            yield return unclosedQuoteError;
            yield break;
        }

        // Check column count (skip header row)
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
        // Simple check: count quotes, if odd number then unclosed
        var inQuotes = false;
        var quoteStartIndex = -1;

        for (var i = 0; i < lineText.Length; i++)
        {
            if (lineText[i] == '"')
            {
                if (inQuotes && i + 1 < lineText.Length && lineText[i + 1] == '"')
                {
                    i++; // Skip escaped quote
                    continue;
                }

                if (!inQuotes)
                {
                    quoteStartIndex = i;
                }
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
