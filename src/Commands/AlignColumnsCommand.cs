using System.Collections.Generic;
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
    private static readonly object _handlersKey = new();

    public static bool IsEnabled(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        return buffer.Properties.TryGetProperty(_alignmentEnabledKey, out bool enabled) && enabled;
    }

    public static void SetEnabled(ITextBuffer buffer, bool enabled)
    {
        if (buffer == null) return;
        buffer.Properties[_alignmentEnabledKey] = enabled;

        // Notify all registered handlers that state changed
        if (buffer.Properties.TryGetProperty(_handlersKey, out List<AlignmentStateChangedHandler> handlers))
        {
            foreach (AlignmentStateChangedHandler handler in handlers)
            {
                handler?.Invoke(enabled);
            }
        }
    }

    public static void RegisterStateChangedHandler(ITextBuffer buffer, AlignmentStateChangedHandler handler)
    {
        if (buffer == null || handler == null) return;

        if (!buffer.Properties.TryGetProperty(_handlersKey, out List<AlignmentStateChangedHandler> handlers))
        {
            handlers = [];
            buffer.Properties[_handlersKey] = handlers;
        }

        if (!handlers.Contains(handler))
        {
            handlers.Add(handler);
        }
    }

    /// <summary>
    /// Requests a refresh of the alignment tags (e.g., after header row edits).
    /// </summary>
    public static void RequestRefresh(ITextBuffer buffer)
    {
        if (buffer == null || !IsEnabled(buffer)) return;

        // Notify handlers with current state to trigger refresh
        if (buffer.Properties.TryGetProperty(_handlersKey, out List<AlignmentStateChangedHandler> handlers))
        {
            foreach (AlignmentStateChangedHandler handler in handlers)
            {
                handler?.Invoke(true);
            }
        }
    }

    public delegate void AlignmentStateChangedHandler(bool enabled);
}
