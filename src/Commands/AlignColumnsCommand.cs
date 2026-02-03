using Microsoft.VisualStudio.Text;

namespace CSVEditor;

[Command(PackageIds.AlignColumnsCommand)]
internal sealed class AlignColumnsCommand : BaseCommand<AlignColumnsCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
        if (docView?.TextView?.TextBuffer == null)
            return;

        ITextBuffer buffer = docView.TextView.TextBuffer;
        var lineCount = buffer.CurrentSnapshot.LineCount;

        // Check if currently enabled (we're toggling)
        var isCurrentlyEnabled = CsvAlignmentState.IsEnabled(buffer);

        // If trying to enable on a very large file, ask for confirmation
        if (!isCurrentlyEnabled && lineCount > LargeFileThresholds.DisableAlignmentLineCount)
        {
            var confirm = await VS.MessageBox.ShowConfirmAsync(
                "Large File",
                $"This file has {lineCount:N0} lines. Calculating column widths may take a moment.\n\nDo you want to proceed?");

            if (!confirm)
                return;
        }

        // Toggle alignment state
        CsvAlignmentState.SetEnabled(buffer, !isCurrentlyEnabled);

        var status = !isCurrentlyEnabled ? "enabled" : "disabled";

        // Show additional info for large files
        if (!isCurrentlyEnabled && lineCount > LargeFileThresholds.LargeFileLineCount)
        {
            await VS.StatusBar.ShowMessageAsync($"CSV column alignment {status} (using sampled widths for large file)");
        }
        else
        {
            await VS.StatusBar.ShowMessageAsync($"CSV column alignment {status}");
        }
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
    private static readonly object _alignmentEnabledKey = new();

    public static bool IsEnabled(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        return buffer.Properties.TryGetProperty(_alignmentEnabledKey, out bool enabled) && enabled;
    }

    public static void SetEnabled(ITextBuffer buffer, bool enabled)
    {
        if (buffer == null) return;
        buffer.Properties[_alignmentEnabledKey] = enabled;

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
