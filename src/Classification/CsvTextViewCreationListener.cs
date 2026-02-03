using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Configures text view options for CSV/TSV files to provide a cleaner editing experience.
/// Disables margins that are not useful for data files.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class CsvTextViewCreationListener : IWpfTextViewCreationListener
{
    public void TextViewCreated(IWpfTextView textView)
    {
        // Disable selection margin (the gray bar left of line numbers)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.SelectionMarginId, false);

        // Disable glyph margin (breakpoints, bookmarks - not useful for CSV)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.GlyphMarginId, false);

        // Disable outlining/folding margin (CSV files don't have collapsible regions)
        textView.Options.SetOptionValue(DefaultTextViewOptions.OutliningUndoOptionId, false);

        // Disable the change tracking margin (the colored bars showing edits)
        textView.Options.SetOptionValue(DefaultTextViewHostOptions.ChangeTrackingId, false);
    }
}
