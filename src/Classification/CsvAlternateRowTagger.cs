using System.ComponentModel.Composition;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Creates the adornment manager for alternate row backgrounds.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class CsvAlternateRowAdornmentFactory : IWpfTextViewCreationListener
{
    [Export(typeof(AdornmentLayerDefinition))]
    [Name(CsvAlternateRowAdornment.LayerName)]
    [Order(Before = PredefinedAdornmentLayers.Selection)]
    private AdornmentLayerDefinition _editorAdornmentLayer;

    public void TextViewCreated(IWpfTextView textView)
    {
        // Create the adornment manager for this view
        textView.Properties.GetOrCreateSingletonProperty(
            () => new CsvAlternateRowAdornment(textView));
    }
}

/// <summary>
/// Draws alternating row background colors across the full editor width.
/// </summary>
internal sealed class CsvAlternateRowAdornment
{
    public const string LayerName = "CsvAlternateRowBackground";

    private readonly IWpfTextView _textView;
    private readonly IAdornmentLayer _layer;
    private bool _isEnabled;

    // Light gray with opacity - works well in both light and dark themes
    private static readonly Brush _rowBackgroundBrush = new SolidColorBrush(Color.FromArgb(30, 128, 128, 128));

    static CsvAlternateRowAdornment()
    {
        _rowBackgroundBrush.Freeze();
    }

    public CsvAlternateRowAdornment(IWpfTextView textView)
    {
        _textView = textView;
        _layer = textView.GetAdornmentLayer(LayerName);

        _textView.LayoutChanged += OnLayoutChanged;
        _textView.ViewportWidthChanged += OnViewportChanged;
        _textView.ViewportLeftChanged += OnViewportChanged;
        _textView.Closed += OnClosed;

        // Register for state changes
        CsvAlternateRowState.RegisterStateChangedHandler(textView.TextBuffer, OnStateChanged);

        // Check if already enabled
        _isEnabled = CsvAlternateRowState.IsEnabled(textView.TextBuffer);
        if (_isEnabled)
        {
            DrawAdornments();
        }
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _textView.LayoutChanged -= OnLayoutChanged;
        _textView.ViewportWidthChanged -= OnViewportChanged;
        _textView.ViewportLeftChanged -= OnViewportChanged;
        _textView.Closed -= OnClosed;
    }

    private void OnStateChanged(bool enabled)
    {
        _isEnabled = enabled;

        if (enabled)
        {
            DrawAdornments();
        }
        else
        {
            _layer.RemoveAllAdornments();
        }
    }

    private void OnViewportChanged(object sender, EventArgs e)
    {
        if (_isEnabled)
        {
            DrawAdornments();
        }
    }

    private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
    {
        if (!_isEnabled)
            return;

        // Redraw if lines changed or viewport scrolled
        if (e.NewOrReformattedLines.Count > 0 || e.VerticalTranslation || e.HorizontalTranslation)
        {
            DrawAdornments();
        }
    }

    private void DrawAdornments()
    {
        _layer.RemoveAllAdornments();

        if (!_isEnabled || _textView.IsClosed)
            return;

        // Get viewport dimensions
        var viewportWidth = _textView.ViewportWidth;
        var viewportLeft = _textView.ViewportLeft;

        foreach (ITextViewLine line in _textView.TextViewLines)
        {
            // Get the actual line number in the document
            var lineNumber = _textView.TextSnapshot.GetLineNumberFromPosition(line.Start);

            // Only highlight odd rows (0, 2, 4, ...)
            if (lineNumber % 2 != 0)
                continue;

            // Create a rectangle that spans the full viewport width
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = viewportWidth,
                Height = line.Height,
                Fill = _rowBackgroundBrush
            };

            // Position the rectangle at the left edge of the viewport
            Canvas.SetLeft(rect, viewportLeft);
            Canvas.SetTop(rect, line.Top);

            // Add to the adornment layer
            _layer.AddAdornment(
                AdornmentPositioningBehavior.TextRelative,
                line.Extent,
                null,
                rect,
                null);
        }
    }
}

/// <summary>
/// Manages alternate row highlighting state per text buffer.
/// </summary>
internal static class CsvAlternateRowState
{
    private static readonly object _stateKey = new();

    public static bool IsEnabled(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        return buffer.Properties.TryGetProperty(_stateKey, out bool enabled) && enabled;
    }

    public static void SetEnabled(ITextBuffer buffer, bool enabled)
    {
        if (buffer == null) return;
        buffer.Properties[_stateKey] = enabled;

        // Notify handlers that state changed
        if (buffer.Properties.TryGetProperty(typeof(AlternateRowStateChangedHandler), out AlternateRowStateChangedHandler handler))
        {
            handler?.Invoke(enabled);
        }
    }

    public static void RegisterStateChangedHandler(ITextBuffer buffer, AlternateRowStateChangedHandler handler)
    {
        if (buffer == null) return;
        buffer.Properties[typeof(AlternateRowStateChangedHandler)] = handler;
    }

    public delegate void AlternateRowStateChangedHandler(bool enabled);
}
