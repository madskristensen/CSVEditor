using System.ComponentModel.Composition;
using CSVEditor.Classification;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.QuickInfo;

/// <summary>
/// Provides QuickInfo (hover tooltips) for CSV columns.
/// </summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("CSV QuickInfo Provider")]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[Order(Before = "Default Quick Info Presenter")]
internal sealed class CsvQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(() => new CsvQuickInfoSource(textBuffer));
    }
}
