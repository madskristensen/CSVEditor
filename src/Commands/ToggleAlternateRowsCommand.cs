using CSVEditor.Classification;
using Microsoft.VisualStudio.Text;

namespace CSVEditor;

[Command(PackageIds.ToggleAlternateRowsCommand)]
internal sealed class ToggleAlternateRowsCommand : BaseCommand<ToggleAlternateRowsCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView?.TextBuffer == null)
            return;

        ITextBuffer buffer = docView.TextView.TextBuffer;

        // Toggle alternate row highlighting state for this buffer
        var isEnabled = CsvAlternateRowState.IsEnabled(buffer);
        CsvAlternateRowState.SetEnabled(buffer, !isEnabled);

        var status = !isEnabled ? "enabled" : "disabled";
        await VS.StatusBar.ShowMessageAsync($"CSV alternate row highlighting {status}");
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            var contentType = docView?.TextView?.TextBuffer?.ContentType?.TypeName;
            var isCsv = contentType == "csv" || contentType == "tsv";

            Command.Visible = isCsv;

            if (isCsv && docView?.TextView?.TextBuffer != null)
            {
                var isEnabled = CsvAlternateRowState.IsEnabled(docView.TextView.TextBuffer);
                Command.Checked = isEnabled;
            }
        });
    }
}
