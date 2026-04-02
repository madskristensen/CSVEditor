using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CSVEditor.Classification;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Adornments;

/// <summary>
/// Paints a sticky header row at the top of the viewport when the CSV locked header
/// feature is enabled. The adornment is positioned with
/// <see cref="AdornmentPositioningBehavior.OwnerControlled"/> and manually kept at
/// (0, ViewportTop) so it behaves exactly like VS Sticky Scroll.
/// </summary>
internal sealed class CsvStickyHeader : IDisposable
{
    // Layer name – must match the [Name] on the AdornmentLayerDefinition below.
    internal const string LayerName = "CsvStickyHeaderLayer";

    private readonly IWpfTextView _textView;
    private readonly ITextBuffer _buffer;
    private readonly CsvBufferCache _cache;
    private readonly IAdornmentLayer _layer;

    private Border _headerElement;
    private bool _disposed;

    // Column widths are shared with the alignment tagger via a well-known property key.
    internal static readonly object ColumnWidthsKey = new();

    // Event key for notifying sticky header when column widths change.
    internal static readonly object ColumnWidthsChangedKey = new();

    public CsvStickyHeader(IWpfTextView textView)
    {
        _textView = textView;
        _buffer = textView.TextBuffer;
        _cache = CsvBufferCache.GetOrCreate(_buffer);
        _layer = textView.GetAdornmentLayer(LayerName);

        _textView.LayoutChanged += OnLayoutChanged;
        _textView.ViewportLeftChanged += OnViewportChanged;
        _textView.Closed += OnClosed;
        _buffer.Changed += OnBufferChanged;

        // Listen for sticky header state changes
        CsvStickyHeaderState.RegisterStateChangedHandler(_buffer, OnStickyStateChanged);

        // Listen for alignment state changes (to rebuild header with/without alignment)
        CsvAlignmentState.RegisterStateChangedHandler(_buffer, OnAlignmentStateChanged);

        // Register for column width updates from alignment tagger
        _buffer.Properties[ColumnWidthsChangedKey] = new System.Action(OnColumnWidthsChanged);
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    private void OnStickyStateChanged(bool enabled)
    {
        if (_disposed) return;

        if (enabled)
        {
            CreateOrUpdate();
        }
        else
        {
            Remove();
            // When header is disabled and alignment is on, refresh alignment
            // in case the header row was edited while the sticky header was showing
            CsvAlignmentState.RequestRefresh(_buffer);
        }
    }

    private void OnAlignmentStateChanged(bool enabled)
    {
        // When alignment is toggled, rebuild header to pick up (or drop) column widths.
        if (_disposed || !CsvStickyHeaderState.IsEnabled(_buffer)) return;
        CreateOrUpdate();
    }

    private void OnColumnWidthsChanged()
    {
        // When alignment tagger recalculates widths, rebuild header to stay in sync.
        if (_disposed || !CsvStickyHeaderState.IsEnabled(_buffer)) return;
        if (!CsvAlignmentState.IsEnabled(_buffer)) return;
        CreateOrUpdate();
    }

    private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
    {
        if (_disposed || !CsvStickyHeaderState.IsEnabled(_buffer)) return;

        // Rebuild when font/zoom changes; otherwise just reposition.
        if (e.OldViewState.ViewportWidth != e.NewViewState.ViewportWidth ||
            e.OldViewState.EditSnapshot != e.NewViewState.EditSnapshot)
        {
            CreateOrUpdate();
        }
        else
        {
            RepositionHeader();
        }
    }

    private void OnViewportChanged(object sender, EventArgs e)
    {
        if (_disposed || !CsvStickyHeaderState.IsEnabled(_buffer)) return;
        RepositionHeader();
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        if (_disposed || !CsvStickyHeaderState.IsEnabled(_buffer)) return;

        // Only rebuild if the header row itself was edited.
        foreach (ITextChange change in e.Changes)
        {
            var changedLine = e.After.GetLineNumberFromPosition(change.NewPosition);
            if (changedLine == 0)
            {
                CreateOrUpdate();
                return;
            }
        }
    }

    private void OnClosed(object sender, EventArgs e) => Dispose();

    // -------------------------------------------------------------------------
    // Build / remove the header element
    // -------------------------------------------------------------------------

    private void CreateOrUpdate()
    {
        if (_disposed) return;

        _layer.RemoveAllAdornments();
        _headerElement = null;

        ITextSnapshot snapshot = _buffer.CurrentSnapshot;
        if (snapshot.LineCount == 0) return;

        if (!_cache.HasHeader(snapshot)) return;

        CsvRow headerRow = _cache.GetParsedLine(snapshot, 0);
        if (headerRow.Count == 0) return;

        FrameworkElement headerContent = BuildHeaderPanel(headerRow, snapshot);

        _headerElement = new Border
        {
            Child = headerContent,
            Background = GetHeaderBackground(),
            BorderBrush = GetHeaderBorderBrush(),
            BorderThickness = new Thickness(0, 0, 0, 1),
            IsHitTestVisible = false,  // Don't capture mouse clicks
            Focusable = false,         // Don't accept keyboard focus
        };

        _layer.AddAdornment(
            AdornmentPositioningBehavior.OwnerControlled,
            visualSpan: null,
            tag: null,
            adornment: _headerElement,
            removedCallback: null);

        RepositionHeader();
    }

    private void Remove()
    {
        _layer.RemoveAllAdornments();
        _headerElement = null;
    }

    // -------------------------------------------------------------------------
    // Layout helpers
    // -------------------------------------------------------------------------

    private void RepositionHeader()
    {
        if (_headerElement == null) return;

        // Pin to top-left of the viewport; horizontal offset tracks horizontal scroll.
        Canvas.SetLeft(_headerElement, _textView.ViewportLeft);
        Canvas.SetTop(_headerElement, _textView.ViewportTop);

        // Make the element as wide as the viewport.
        _headerElement.Width = _textView.ViewportWidth;
    }

    /// <summary>
    /// Gets the left margin where text actually starts in the editor (accounts for line numbers, glyph margin, etc.)
    /// </summary>
    private double GetTextLeftMargin()
    {
        if (_textView.TextViewLines == null || _textView.TextViewLines.Count == 0)
            return 0;

        foreach (ITextViewLine line in _textView.TextViewLines)
        {
            if (line.Length > 0)
            {
                try
                {
                    TextBounds bounds = line.GetCharacterBounds(line.Start);
                    // Return the left position relative to the viewport
                    return bounds.Left - _textView.ViewportLeft;
                }
                catch
                {
                    // Continue to next line
                }
            }
        }

        return 0;
    }

    private FrameworkElement BuildHeaderPanel(CsvRow headerRow, ITextSnapshot snapshot)
    {
        // Use exact editor font properties for consistent character widths
        var defaultProps = _textView.FormattedLineSource?.DefaultTextProperties;
        double fontSize = defaultProps?.FontRenderingEmSize ?? 12;
        var typeface = defaultProps?.Typeface ?? new Typeface("Consolas");

        // Grab column widths from the alignment tagger if alignment is active.
        int[] columnWidths = null;
        bool isAligned = CsvAlignmentState.IsEnabled(_buffer);
        if (isAligned)
        {
            _buffer.Properties.TryGetProperty(ColumnWidthsKey, out columnWidths);
        }

        char delimiter = _cache.GetDelimiter(snapshot);
        bool isTabDelimited = delimiter == '\t';

        // Get the raw first line text from the snapshot
        ITextSnapshotLine firstLine = snapshot.GetLineFromLineNumber(0);
        string lineText = firstLine.GetText();
        int lineStartPosition = firstLine.Start.Position;

        // Get the text left margin for positioning
        double textLeftMargin = GetTextLeftMargin();

        // For non-aligned mode, use a simple StackPanel - WPF handles layout automatically
        // For aligned mode, use Canvas for pixel-precise positioning with padding
        if (!isAligned || columnWidths == null)
        {
            return BuildNonAlignedHeader(headerRow, lineText, lineStartPosition, fontSize, typeface, delimiter, isTabDelimited, textLeftMargin);
        }
        else
        {
            return BuildAlignedHeader(headerRow, lineText, lineStartPosition, fontSize, typeface, delimiter, isTabDelimited, columnWidths, textLeftMargin);
        }
    }

    private FrameworkElement BuildNonAlignedHeader(CsvRow headerRow, string lineText, int lineStartPosition, 
        double fontSize, Typeface typeface, char delimiter, bool isTabDelimited, double textLeftMargin)
    {
        // Use a StackPanel with a left margin - simple and reliable
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(textLeftMargin, 0, 0, 0),
        };

        for (int col = 0; col < headerRow.Count; col++)
        {
            CsvCell cell = headerRow[col];
            Color color = GetColumnColor(col);
            bool isLastColumn = col == headerRow.Count - 1;

            // Extract the raw cell text
            int cellStartInLine = cell.Span.Start - lineStartPosition;
            int cellLength = cell.Span.Length;

            if (cellStartInLine < 0 || cellStartInLine + cellLength > lineText.Length)
            {
                cellStartInLine = Math.Max(0, cellStartInLine);
                cellLength = Math.Min(cellLength, lineText.Length - cellStartInLine);
            }

            string rawCellText = lineText.Substring(cellStartInLine, cellLength);

            // Add cell text
            panel.Children.Add(new TextBlock
            {
                Text = rawCellText,
                FontFamily = typeface.FontFamily,
                FontSize = fontSize,
                FontStyle = typeface.Style,
                FontWeight = typeface.Weight,
                FontStretch = typeface.Stretch,
                Foreground = new SolidColorBrush(color),
            });

            // Add delimiter (except after last column)
            if (!isLastColumn)
            {
                string delimText = isTabDelimited ? "\t" : delimiter.ToString();
                panel.Children.Add(new TextBlock
                {
                    Text = delimText,
                    FontFamily = typeface.FontFamily,
                    FontSize = fontSize,
                    FontStyle = typeface.Style,
                    FontWeight = typeface.Weight,
                    FontStretch = typeface.Stretch,
                    Foreground = new SolidColorBrush(color),
                });
            }
        }

        return panel;
    }

    private FrameworkElement BuildAlignedHeader(CsvRow headerRow, string lineText, int lineStartPosition,
        double fontSize, Typeface typeface, char delimiter, bool isTabDelimited, int[] columnWidths, double textLeftMargin)
    {
        double charWidth = GetCharacterWidth();
        var canvas = new Canvas();
        double xOffset = textLeftMargin;

        for (int col = 0; col < headerRow.Count; col++)
        {
            CsvCell cell = headerRow[col];
            Color color = GetColumnColor(col);
            bool isLastColumn = col == headerRow.Count - 1;

            // Extract the raw cell text
            int cellStartInLine = cell.Span.Start - lineStartPosition;
            int cellLength = cell.Span.Length;

            if (cellStartInLine < 0 || cellStartInLine + cellLength > lineText.Length)
            {
                cellStartInLine = Math.Max(0, cellStartInLine);
                cellLength = Math.Min(cellLength, lineText.Length - cellStartInLine);
            }

            string rawCellText = lineText.Substring(cellStartInLine, cellLength);

            // Add cell text at current position
            var tb = new TextBlock
            {
                Text = rawCellText,
                FontFamily = typeface.FontFamily,
                FontSize = fontSize,
                FontStyle = typeface.Style,
                FontWeight = typeface.Weight,
                FontStretch = typeface.Stretch,
                Foreground = new SolidColorBrush(color),
            };
            Canvas.SetLeft(tb, xOffset);
            Canvas.SetTop(tb, 0);
            canvas.Children.Add(tb);

            // Advance past cell text
            xOffset += cellLength * charWidth;

            if (!isLastColumn && col < columnWidths.Length)
            {
                int paddingChars = columnWidths[col] - cellLength;

                if (isTabDelimited)
                {
                    // TSV: spacer replaces tab
                    xOffset += (paddingChars + 2) * charWidth;
                }
                else
                {
                    // CSV: add delimiter then padding
                    var delimTb = new TextBlock
                    {
                        Text = delimiter.ToString(),
                        FontFamily = typeface.FontFamily,
                        FontSize = fontSize,
                        FontStyle = typeface.Style,
                        FontWeight = typeface.Weight,
                        FontStretch = typeface.Stretch,
                        Foreground = new SolidColorBrush(color),
                    };
                    Canvas.SetLeft(delimTb, xOffset);
                    Canvas.SetTop(delimTb, 0);
                    canvas.Children.Add(delimTb);

                    xOffset += charWidth; // delimiter
                    if (paddingChars > 0)
                    {
                        xOffset += paddingChars * charWidth;
                    }
                }
            }
        }

        canvas.Width = xOffset;
        canvas.Height = fontSize * 1.3;
        return canvas;
    }

    private double GetCharacterWidth()
    {
        if (_textView.TextViewLines == null || _textView.TextViewLines.Count == 0)
            return _textView.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize * 0.6 ?? 8.0;

        foreach (ITextViewLine line in _textView.TextViewLines)
        {
            if (line.Length > 0)
            {
                try
                {
                    TextBounds bounds = line.GetCharacterBounds(line.Start);
                    if (bounds.Width > 0)
                        return bounds.Width;
                }
                catch
                {
                    // Continue to next line
                }
            }
        }

        return _textView.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize * 0.6 ?? 8.0;
    }

    // -------------------------------------------------------------------------
    // Color / theme helpers
    // -------------------------------------------------------------------------

    private static readonly Color[] _columnColors =
    [
        Color.FromRgb(220,  50,  47),   // 0 Red
        Color.FromRgb(203,  75,  22),   // 1 Orange
        Color.FromRgb(181, 137,   0),   // 2 Yellow
        Color.FromRgb(133, 153,   0),   // 3 Green
        Color.FromRgb( 42, 161, 152),   // 4 Cyan
        Color.FromRgb( 38, 139, 210),   // 5 Blue
        Color.FromRgb(108, 113, 196),   // 6 Violet
        Color.FromRgb(211,  54, 130),   // 7 Magenta
        Color.FromRgb(  0, 153, 153),   // 8 Teal
        Color.FromRgb(150, 100,  50),   // 9 Brown
    ];

    private static Color GetColumnColor(int columnIndex)
        => _columnColors[columnIndex % _columnColors.Length];

    private static Brush GetHeaderBackground()
    {
        var vsColor = Microsoft.VisualStudio.Shell.VsColors.ToolWindowBackgroundKey;
        var resource = Application.Current?.TryFindResource(vsColor);
        if (resource is Brush brush) return brush;

        return new SolidColorBrush(Color.FromRgb(37, 37, 38)); // VS dark fallback
    }

    private static Brush GetHeaderBorderBrush()
    {
        var vsColor = Microsoft.VisualStudio.Shell.VsColors.ToolWindowBorderKey;
        var resource = Application.Current?.TryFindResource(vsColor);
        if (resource is Brush brush) return brush;

        return new SolidColorBrush(Color.FromRgb(63, 63, 70));
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _textView.LayoutChanged -= OnLayoutChanged;
        _textView.ViewportLeftChanged -= OnViewportChanged;
        _textView.Closed -= OnClosed;
        _buffer.Changed -= OnBufferChanged;
    }
}

/// <summary>
/// Declares the adornment layer for the CSV sticky header.
/// Ordered after the text layer so the header paints on top of any row-0 text.
/// </summary>
internal sealed class CsvStickyHeaderLayerDefinition
{
    [Export]
    [Name(CsvStickyHeader.LayerName)]
    [Order(After = PredefinedAdornmentLayers.Text)]
    internal AdornmentLayerDefinition LayerDefinition = null;
}
