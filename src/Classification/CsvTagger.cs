using System.Collections.Generic;
using System.ComponentModel.Composition;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Creates CSV taggers for text views with CSV content type.
/// </summary>
[Export(typeof(IViewTaggerProvider))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TagType(typeof(IClassificationTag))]
internal sealed class CsvTaggerProvider : IViewTaggerProvider
{
    [Import]
    internal IClassificationTypeRegistryService ClassificationRegistry = null!;

    public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
    {
        return buffer.Properties.GetOrCreateSingletonProperty(
            () => new CsvTagger(buffer, ClassificationRegistry)) as ITagger<T>;
    }
}

/// <summary>
/// Tagger that provides rainbow coloring for CSV columns.
/// </summary>
internal sealed class CsvTagger : ITagger<IClassificationTag>
{
    private readonly ITextBuffer _textBuffer;
    private readonly Dictionary<string, IClassificationType> _classificationTypes;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvTagger(ITextBuffer textBuffer, IClassificationTypeRegistryService classificationRegistry)
    {
        _textBuffer = textBuffer;
        _classificationTypes = new Dictionary<string, IClassificationType>();

        // Pre-cache classification types
        for (var i = 0; i < CsvClassificationTypes.ColorCount; i++)
        {
            var name = CsvClassificationTypes.GetColumnClassificationType(i);
            var classificationType = classificationRegistry.GetClassificationType(name);
            if (classificationType != null)
            {
                _classificationTypes[name] = classificationType;
            }
        }

        _textBuffer.Changed += OnTextBufferChanged;
    }

    private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Re-detect delimiter on significant changes
        if (e.Changes.Count > 0)
        {
            var firstChange = e.Changes[0];
            // If change is in the first few lines, re-detect delimiter
            if (firstChange.OldPosition < 500)
            {
                _delimiterDetected = false;
            }
        }

        // Notify that tags may have changed for affected lines
        foreach (var change in e.Changes)
        {
            var startLine = e.After.GetLineFromPosition(change.NewPosition);
            var endLine = e.After.GetLineFromPosition(change.NewEnd);
            var changedSpan = new SnapshotSpan(startLine.Start, endLine.End);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(changedSpan));
        }
    }

    public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0)
            yield break;

        var snapshot = spans[0].Snapshot;

        // Detect delimiter if not yet done
        if (!_delimiterDetected)
        {
            DetectDelimiter(snapshot);
        }

        foreach (var span in spans)
        {
            var startLine = snapshot.GetLineFromPosition(span.Start);
            var endLine = snapshot.GetLineFromPosition(span.End);

            for (var lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
            {
                var line = snapshot.GetLineFromLineNumber(lineNumber);
                foreach (var tag in GetLineTags(line))
                {
                    yield return tag;
                }
            }
        }
    }

    private void DetectDelimiter(ITextSnapshot snapshot)
    {
        // Get first portion of the document for delimiter detection
        var length = Math.Min(snapshot.Length, 2000);
        var text = snapshot.GetText(0, length);

        var delimiter = DelimiterDetector.Detect(text);
        _detectedDelimiter = delimiter.ToChar();
        _delimiterDetected = true;
    }

    private IEnumerable<ITagSpan<IClassificationTag>> GetLineTags(ITextSnapshotLine line)
    {
        var lineText = line.GetText();
        if (string.IsNullOrEmpty(lineText))
            yield break;

        var lineStart = line.Start.Position;
        var position = 0;
        var columnIndex = 0;

        while (position < lineText.Length)
        {
            var cellStart = position;
            var inQuotes = false;

            // Parse cell
            while (position < lineText.Length)
            {
                var c = lineText[position];

                if (c == '"')
                {
                    if (inQuotes && position + 1 < lineText.Length && lineText[position + 1] == '"')
                    {
                        position += 2; // Skip escaped quote
                        continue;
                    }
                    inQuotes = !inQuotes;
                }
                else if (c == _detectedDelimiter && !inQuotes)
                {
                    break;
                }

                position++;
            }

            // Create tag for this cell
            var cellLength = position - cellStart;
            if (cellLength > 0)
            {
                var classificationName = CsvClassificationTypes.GetColumnClassificationType(columnIndex);
                if (_classificationTypes.TryGetValue(classificationName, out var classificationType))
                {
                    var cellSpan = new SnapshotSpan(line.Snapshot, lineStart + cellStart, cellLength);
                    yield return new TagSpan<IClassificationTag>(cellSpan, new ClassificationTag(classificationType));
                }
            }

            // Skip delimiter
            if (position < lineText.Length && lineText[position] == _detectedDelimiter)
            {
                position++;
            }

            columnIndex++;
        }
    }
}
