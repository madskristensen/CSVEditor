using Microsoft.VisualStudio.Text;

namespace CSVEditor;

[Command(PackageIds.AlignColumnsCommand)]
internal sealed class AlignColumnsCommand : BaseCommand<AlignColumnsCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        var docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView?.TextBuffer == null)
            return;

        var buffer = docView.TextView.TextBuffer;
        
        // Toggle alignment state for this buffer
        var isEnabled = CsvAlignmentState.IsEnabled(buffer);
        CsvAlignmentState.SetEnabled(buffer, !isEnabled);

        var status = !isEnabled ? "enabled" : "disabled";
        await VS.StatusBar.ShowMessageAsync($"CSV column alignment {status}");
    }

    protected override void BeforeQueryStatus(EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            var docView = await VS.Documents.GetActiveDocumentViewAsync();
            var contentType = docView?.TextView?.TextBuffer?.ContentType?.TypeName;
            var isCsv = contentType == "csv" || contentType == "tsv";
            
            Command.Visible = isCsv;
            
            if (isCsv && docView?.TextView?.TextBuffer != null)
            {
                var isEnabled = CsvAlignmentState.IsEnabled(docView.TextView.TextBuffer);
                Command.Checked = isEnabled;
            }
        });
    }
}

/// <summary>
/// Manages alignment enabled state per text buffer.
/// </summary>
internal static class CsvAlignmentState
{
    private static readonly object AlignmentEnabledKey = new object();

    public static bool IsEnabled(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        return buffer.Properties.TryGetProperty(AlignmentEnabledKey, out bool enabled) && enabled;
    }

    public static void SetEnabled(ITextBuffer buffer, bool enabled)
    {
        if (buffer == null) return;
        buffer.Properties[AlignmentEnabledKey] = enabled;
        
        // Notify the tagger that state changed
        if (buffer.Properties.TryGetProperty(typeof(AlignmentStateChangedHandler), out AlignmentStateChangedHandler handler))
        {
            handler?.Invoke(enabled);
        }
    }

    public static void RegisterStateChangedHandler(ITextBuffer buffer, AlignmentStateChangedHandler handler)
    {
        if (buffer == null) return;
        buffer.Properties[typeof(AlignmentStateChangedHandler)] = handler;
    }

    public delegate void AlignmentStateChangedHandler(bool enabled);
}
