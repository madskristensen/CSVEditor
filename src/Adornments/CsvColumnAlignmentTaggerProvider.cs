using System.ComponentModel.Composition;
using CSVEditor.Classification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Adornments;

/// <summary>
/// Provides intra-text adornment tags for CSV column alignment.
/// Inserts virtual whitespace after each cell to align columns.
/// Only active when explicitly enabled via the "Align CSV Columns" command.
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
