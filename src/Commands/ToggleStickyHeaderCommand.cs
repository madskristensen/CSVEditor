using CSVEditor.Classification;
using Microsoft.VisualStudio.Text;

namespace CSVEditor;

[Command(PackageIds.ToggleStickyHeaderCommand)]
internal sealed class ToggleStickyHeaderCommand : BaseCommand<ToggleStickyHeaderCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView?.TextBuffer == null)
            return;

        ITextBuffer buffer = docView.TextView.TextBuffer;

        var isEnabled = CsvStickyHeaderState.IsEnabled(buffer);
        CsvStickyHeaderState.SetEnabled(buffer, !isEnabled);

        var status = !isEnabled ? "enabled" : "disabled";
        await VS.StatusBar.ShowMessageAsync($"CSV locked header row {status}");
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
                Command.Checked = CsvStickyHeaderState.IsEnabled(docView.TextView.TextBuffer);
            }
        });
    }
}

/// <summary>
/// Manages the sticky header enabled state per text buffer.
/// </summary>
internal static class CsvStickyHeaderState
{
    private static readonly object _enabledKey = new();

    public static bool IsEnabled(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        return buffer.Properties.TryGetProperty(_enabledKey, out bool enabled) && enabled;
    }

    public static void SetEnabled(ITextBuffer buffer, bool enabled)
    {
        if (buffer == null) return;
        buffer.Properties[_enabledKey] = enabled;

        if (buffer.Properties.TryGetProperty(typeof(StickyHeaderStateChangedHandler), out StickyHeaderStateChangedHandler handler))
        {
            handler?.Invoke(enabled);
        }
    }

    public static void RegisterStateChangedHandler(ITextBuffer buffer, StickyHeaderStateChangedHandler handler)
    {
        if (buffer == null) return;
        buffer.Properties[typeof(StickyHeaderStateChangedHandler)] = handler;
    }

    public delegate void StickyHeaderStateChangedHandler(bool enabled);
}
