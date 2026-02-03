using System.Collections.Generic;
using CSVEditor.Core;
using Microsoft.VisualStudio.Text;

namespace CSVEditor.Classification;

/// <summary>
/// Provides shared caching of CSV parsing results across taggers for a single buffer.
/// This eliminates redundant parsing when multiple taggers process the same lines.
/// </summary>
internal sealed class CsvBufferCache
{
    private static readonly object _cacheKey = new object();

    private readonly ITextBuffer _buffer;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private int _delimiterDetectionVersion = -1;

    // Line cache: maps line number to (snapshot version, parsed row)
    private readonly Dictionary<int, (int Version, CsvRow Row)> _lineCache = [];
    private int _expectedColumnCount = -1;
    private int _expectedColumnCountVersion = -1;

    private CsvBufferCache(ITextBuffer buffer)
    {
        _buffer = buffer;
        _buffer.Changed += OnBufferChanged;
    }

    /// <summary>
    /// Gets or creates the shared cache for the specified buffer.
    /// </summary>
    public static CsvBufferCache GetOrCreate(ITextBuffer buffer)
    {
        return buffer.Properties.GetOrCreateSingletonProperty(_cacheKey, () => new CsvBufferCache(buffer));
    }

    /// <summary>
    /// Gets the detected delimiter for this buffer, detecting if necessary.
    /// </summary>
    public char GetDelimiter(ITextSnapshot snapshot)
    {
        var version = snapshot.Version.VersionNumber;
        if (_delimiterDetected && _delimiterDetectionVersion == version)
        {
            return _detectedDelimiter;
        }

        // Only re-detect if version changed significantly or not detected yet
        if (!_delimiterDetected || version != _delimiterDetectionVersion)
        {
            var length = Math.Min(snapshot.Length, 2000);
            var text = snapshot.GetText(0, length);
            CsvDelimiter delimiter = DelimiterDetector.Detect(text);
            _detectedDelimiter = delimiter.ToChar();
            _delimiterDetected = true;
            _delimiterDetectionVersion = version;
        }

        return _detectedDelimiter;
    }

    /// <summary>
    /// Gets the expected column count (from header row), calculating if necessary.
    /// </summary>
    public int GetExpectedColumnCount(ITextSnapshot snapshot)
    {
        var version = snapshot.Version.VersionNumber;
        if (_expectedColumnCountVersion == version)
        {
            return _expectedColumnCount;
        }

        if (snapshot.LineCount > 0)
        {
            CsvRow headerRow = GetParsedLine(snapshot, 0);
            _expectedColumnCount = headerRow.Count;
        }
        else
        {
            _expectedColumnCount = -1;
        }
        _expectedColumnCountVersion = version;
        return _expectedColumnCount;
    }

    /// <summary>
    /// Gets a parsed line from cache, or parses and caches it.
    /// </summary>
    public CsvRow GetParsedLine(ITextSnapshot snapshot, int lineNumber)
    {
        var version = snapshot.Version.VersionNumber;

        if (_lineCache.TryGetValue(lineNumber, out (int Version, CsvRow Row) cached) && cached.Version == version)
        {
            return cached.Row;
        }

        ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNumber);
        var delimiter = GetDelimiter(snapshot);
        CsvRow row = CsvParser.ParseLine(line.GetText(), delimiter, lineNumber, line.Start.Position);

        _lineCache[lineNumber] = (version, row);
        return row;
    }

    /// <summary>
    /// Gets a parsed line for a specific ITextSnapshotLine.
    /// </summary>
    public CsvRow GetParsedLine(ITextSnapshotLine line)
    {
        return GetParsedLine(line.Snapshot, line.LineNumber);
    }

    /// <summary>
    /// Invalidates cache entries for specific lines.
    /// </summary>
    public void InvalidateLines(int startLine, int endLine)
    {
        for (var i = startLine; i <= endLine; i++)
        {
            _lineCache.Remove(i);
        }
    }

    /// <summary>
    /// Invalidates the delimiter detection (e.g., when content at start of file changes).
    /// </summary>
    public void InvalidateDelimiter()
    {
        _delimiterDetected = false;
        _expectedColumnCount = -1;
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Invalidate delimiter if change is near the beginning
        if (e.Changes.Count > 0 && e.Changes[0].OldPosition < 500)
        {
            InvalidateDelimiter();
        }

        // Invalidate affected lines and all lines after (line numbers may have shifted)
        if (e.Changes.Count > 0)
        {
            var firstAffectedLine = e.After.GetLineFromPosition(e.Changes[0].NewPosition).LineNumber;

            // Remove all cached lines from the first affected line onwards
            var keysToRemove = new List<int>();
            foreach (var key in _lineCache.Keys)
            {
                if (key >= firstAffectedLine)
                {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove)
            {
                _lineCache.Remove(key);
            }
        }
    }
}
