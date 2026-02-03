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
    private readonly CsvBufferCache _cache;

    public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

    public CsvTagger(ITextBuffer textBuffer, IClassificationTypeRegistryService classificationRegistry)
    {
        _textBuffer = textBuffer;
        _classificationTypes = [];
        _cache = CsvBufferCache.GetOrCreate(textBuffer);

        // Pre-cache classification types
        for (var i = 0; i < CsvClassificationTypes.ColorCount; i++)
        {
            var name = CsvClassificationTypes.GetColumnClassificationType(i);
            IClassificationType classificationType = classificationRegistry.GetClassificationType(name);
            if (classificationType != null)
            {
                _classificationTypes[name] = classificationType;
            }
        }

        _textBuffer.Changed += OnTextBufferChanged;
    }

    private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Notify that tags may have changed for affected lines only
        foreach (ITextChange change in e.Changes)
        {
            ITextSnapshotLine startLine = e.After.GetLineFromPosition(change.NewPosition);
            ITextSnapshotLine endLine = e.After.GetLineFromPosition(change.NewEnd);
            var changedSpan = new SnapshotSpan(startLine.Start, endLine.End);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(changedSpan));
        }
    }

    public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0)
            yield break;

        ITextSnapshot snapshot = spans[0].Snapshot;

        foreach (SnapshotSpan span in spans)
        {
            ITextSnapshotLine startLine = snapshot.GetLineFromPosition(span.Start);
            ITextSnapshotLine endLine = snapshot.GetLineFromPosition(span.End);

            for (var lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
            {
                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNumber);
                foreach (ITagSpan<IClassificationTag> tag in GetLineTags(line))
                {
                    yield return tag;
                }
            }
        }
    }

    private IEnumerable<ITagSpan<IClassificationTag>> GetLineTags(ITextSnapshotLine line)
    {
        var lineText = line.GetText();
        if (string.IsNullOrEmpty(lineText))
            yield break;

        // Use shared cache to get parsed line
        CsvRow row = _cache.GetParsedLine(line);

        foreach (CsvCell cell in row)
        {
            if (cell.Span.Length > 0)
            {
                var classificationName = CsvClassificationTypes.GetColumnClassificationType(cell.ColumnIndex);
                if (_classificationTypes.TryGetValue(classificationName, out IClassificationType classificationType))
                {
                    var cellSpan = new SnapshotSpan(line.Snapshot, cell.Span.Start, cell.Span.Length);
                    yield return new TagSpan<IClassificationTag>(cellSpan, new ClassificationTag(classificationType));
                }
            }
        }
    }
}
