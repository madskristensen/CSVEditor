using System;
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
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private int _expectedColumnCount = -1;

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvErrorTagger(ITextBuffer textBuffer)
    {
        _textBuffer = textBuffer;
        _textBuffer.Changed += OnTextBufferChanged;
    }

    private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        if (e.Changes.Count > 0)
        {
            var firstChange = e.Changes[0];
            if (firstChange.OldPosition < 500)
            {
                _delimiterDetected = false;
                _expectedColumnCount = -1;
            }
        }

        var snapshot = e.After;
        var fullSpan = new SnapshotSpan(snapshot, 0, snapshot.Length);
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(fullSpan));
    }

    public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0)
            yield break;

        var snapshot = spans[0].Snapshot;

        if (!_delimiterDetected)
        {
            DetectDelimiterAndColumnCount(snapshot);
        }

        foreach (var span in spans)
        {
            var startLine = snapshot.GetLineFromPosition(span.Start);
            var endLine = snapshot.GetLineFromPosition(span.End);

            for (var lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
            {
                var line = snapshot.GetLineFromLineNumber(lineNumber);
                foreach (var error in ValidateLine(line, lineNumber))
                {
                    yield return error;
                }
            }
        }
    }

    private void DetectDelimiterAndColumnCount(ITextSnapshot snapshot)
    {
        var length = Math.Min(snapshot.Length, 2000);
        var text = snapshot.GetText(0, length);

        var delimiter = DelimiterDetector.Detect(text);
        _detectedDelimiter = delimiter.ToChar();
        _delimiterDetected = true;

        // Get expected column count from header row using CsvParser
        if (snapshot.LineCount > 0)
        {
            var headerLine = snapshot.GetLineFromLineNumber(0).GetText();
            var headerRow = CsvParser.ParseLine(headerLine, _detectedDelimiter, 0);
            _expectedColumnCount = headerRow.Count;
        }
    }

    private IEnumerable<ITagSpan<IErrorTag>> ValidateLine(ITextSnapshotLine line, int lineNumber)
    {
        var lineText = line.GetText();

        if (string.IsNullOrEmpty(lineText))
            yield break;

        // Use CsvParser to parse the line
        var row = CsvParser.ParseLine(lineText, _detectedDelimiter, lineNumber, line.Start.Position);

        // Check for unclosed quotes by looking for cells that start with quote but aren't marked as quoted properly
        var unclosedQuoteError = CheckUnclosedQuotes(line, lineText);
        if (unclosedQuoteError != null)
        {
            yield return unclosedQuoteError;
            yield break;
        }

        // Check column count (skip header row)
        if (lineNumber > 0 && _expectedColumnCount > 0)
        {
            if (row.Count != _expectedColumnCount)
            {
                var errorSpan = new SnapshotSpan(line.Start, line.Length);
                var errorMessage = row.Count < _expectedColumnCount
                    ? $"Too few columns: expected {_expectedColumnCount}, found {row.Count}"
                    : $"Too many columns: expected {_expectedColumnCount}, found {row.Count}";

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
