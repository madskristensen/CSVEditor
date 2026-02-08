using System.ComponentModel.Composition;
using System.Windows.Input;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Provides a mouse processor that selects the entire CSV cell content on triple-click.
/// </summary>
[Export(typeof(IMouseProcessorProvider))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.PrimaryDocument)]
[Name(nameof(CsvMouseProcessorProvider))]
internal sealed class CsvMouseProcessorProvider : IMouseProcessorProvider
{
    public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
    {
        return wpfTextView.Properties.GetOrCreateSingletonProperty(
            () => new CsvMouseProcessor(wpfTextView));
    }

    private sealed class CsvMouseProcessor(IWpfTextView textView) : MouseProcessorBase
    {
        public override void PreprocessMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount != 3)
                return;

            if (TrySelectCell())
            {
                e.Handled = true;
            }
        }

        private bool TrySelectCell()
        {
            try
            {
                ITextSnapshot snapshot = textView.TextSnapshot;
                var position = textView.Caret.Position.BufferPosition.Position;
                ITextSnapshotLine line = snapshot.GetLineFromPosition(position);

                var cache = CsvBufferCache.GetOrCreate(textView.TextBuffer);
                var delimiter = cache.GetDelimiter(snapshot);

                CsvRow row = CsvParser.ParseLine(
                    line.GetText(), delimiter, line.LineNumber, line.Start.Position);

                // GetCellAtPosition uses exclusive end, so it can miss when
                // the caret lands on a delimiter or at the end of a cell.
                // Fall back to the nearest cell on the same line.
                CsvCell cell = row.GetCellAtPosition(position)
                    ?? GetNearestCell(row, position);

                if (cell == null)
                    return false;

                var start = cell.Span.Start;
                var length = cell.Span.Length;

                // For quoted cells, select only the inner content (without quotes)
                if (cell.IsQuoted && length >= 2)
                {
                    start += 1;
                    length -= 2;
                }

                var selectionSpan = new SnapshotSpan(snapshot, start, length);
                textView.Selection.Select(selectionSpan, isReversed: false);
                textView.Caret.MoveTo(selectionSpan.End);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds the nearest cell when the position falls between cells (e.g., on a delimiter).
        /// </summary>
        private static CsvCell GetNearestCell(CsvRow row, int position)
        {
            if (row.Count == 0)
                return null;

            CsvCell best = null;
            var bestDistance = int.MaxValue;

            foreach (CsvCell cell in row)
            {
                // Distance is 0 if inside the span, otherwise the gap to the nearest edge
                var distance = position < cell.Span.Start
                    ? cell.Span.Start - position
                    : position >= cell.Span.End
                        ? position - cell.Span.End + 1
                        : 0;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell;
                }
            }

            return best;
        }
    }
}
