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
    private static readonly object _cacheKey = new();
    private const int _maxLinesToSampleForTypes = 100;

    private readonly ITextBuffer _buffer;
    private char _detectedDelimiter = ',';
    private bool _delimiterDetected;
    private int _delimiterDetectionVersion = -1;

    // Line cache: maps line number to (snapshot version, parsed row)
    private readonly Dictionary<int, (int Version, CsvRow Row)> _lineCache = [];
    private int _expectedColumnCount = -1;
    private int _expectedColumnCountVersion = -1;

    // Column type cache
    private CsvDataType[] _columnTypes;
    private int _columnTypesVersion = -1;

    // Header detection cache
    private bool _hasHeader;
    private bool _hasHeaderDetected;
    private int _hasHeaderVersion = -1;

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
    /// Gets the detected data type for a specific column.
    /// </summary>
    public CsvDataType GetColumnType(ITextSnapshot snapshot, int columnIndex)
    {
        EnsureColumnTypesCalculated(snapshot);

        if (_columnTypes == null || columnIndex < 0 || columnIndex >= _columnTypes.Length)
            return CsvDataType.Unknown;

        return _columnTypes[columnIndex];
    }

    /// <summary>
    /// Gets all detected column types.
    /// </summary>
    public CsvDataType[] GetColumnTypes(ITextSnapshot snapshot)
    {
        EnsureColumnTypesCalculated(snapshot);
        return _columnTypes ?? [];
    }

    private void EnsureColumnTypesCalculated(ITextSnapshot snapshot)
    {
        var version = snapshot.Version.VersionNumber;
        if (_columnTypesVersion == version && _columnTypes != null)
            return;

        var columnCount = GetExpectedColumnCount(snapshot);
        if (columnCount <= 0)
        {
            _columnTypes = [];
            _columnTypesVersion = version;
            return;
        }

        // Collect sample values for each column (skip header row)
        var columnValues = new List<string>[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            columnValues[i] = new List<string>(_maxLinesToSampleForTypes);
        }

        var linesToSample = Math.Min(snapshot.LineCount, _maxLinesToSampleForTypes + 1);
        for (var lineNum = 1; lineNum < linesToSample; lineNum++) // Start at 1 to skip header
        {
            CsvRow row = GetParsedLine(snapshot, lineNum);
            for (var col = 0; col < row.Count && col < columnCount; col++)
            {
                columnValues[col].Add(row[col].Value);
            }
        }

        // Detect type for each column
        _columnTypes = new CsvDataType[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            _columnTypes[i] = CsvColumnTypeDetector.DetectType(columnValues[i]);
        }

        _columnTypesVersion = version;
    }

    /// <summary>
    /// Determines if the CSV file has a header row.
    /// </summary>
    public bool HasHeader(ITextSnapshot snapshot)
    {
        var version = snapshot.Version.VersionNumber;
        if (_hasHeaderDetected && _hasHeaderVersion == version)
        {
            return _hasHeader;
        }

        // Need at least 2 rows to determine
        if (snapshot.LineCount < 2)
        {
            _hasHeader = false;
            _hasHeaderDetected = true;
            _hasHeaderVersion = version;
            return false;
        }

        // Collect sample rows for analysis
        var sampleCount = Math.Min(snapshot.LineCount, _maxLinesToSampleForTypes + 1);
        var rows = new List<CsvRow>(sampleCount);
        for (var i = 0; i < sampleCount; i++)
        {
            CsvRow row = GetParsedLine(snapshot, i);
            if (row.Count > 0)
                rows.Add(row);
        }

        _hasHeader = CsvHeaderDetector.HasHeader(rows);
        _hasHeaderDetected = true;
        _hasHeaderVersion = version;
        return _hasHeader;
    }

    /// <summary>
    /// Gets the first data row index (0 if no header, 1 if has header).
    /// </summary>
    public int GetFirstDataRowIndex(ITextSnapshot snapshot)
    {
        return HasHeader(snapshot) ? 1 : 0;
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
        _columnTypes = null;
        _hasHeaderDetected = false;
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Invalidate delimiter if change is near the beginning
        if (e.Changes.Count > 0 && e.Changes[0].OldPosition < 500)
        {
            InvalidateDelimiter();
        }

        // Don't invalidate column types on every keystroke - they're based on sampling
        // and unlikely to change significantly from a single edit
        // _columnTypes = null;  // REMOVED: Too expensive to recalculate on every keystroke
        // _hasHeaderDetected = false;  // REMOVED: Header row rarely changes

        // For large files, use a smarter invalidation strategy:
        // Only clear the cache for directly affected lines, not all subsequent lines.
        // Line number shifts are handled by version checking in GetParsedLine.
        if (e.Changes.Count > 0)
        {
            // Only invalidate if this is a line-count-changing edit (has newlines)
            var hasLineCountChange = false;
            foreach (ITextChange change in e.Changes)
            {
                if (change.OldText.IndexOf('\n') >= 0 || change.NewText.IndexOf('\n') >= 0 ||
                    change.OldText.IndexOf('\r') >= 0 || change.NewText.IndexOf('\r') >= 0)
                {
                    hasLineCountChange = true;
                    break;
                }
            }

            if (hasLineCountChange)
            {
                // Line numbers shifted - clear entire cache (unavoidable)
                // But limit cache size to prevent memory bloat
                if (_lineCache.Count > LargeFileThresholds.MaxCachedLines)
                {
                    _lineCache.Clear();
                }
                else
                {
                    // For moderate-sized caches, just clear lines after the change
                    var firstAffectedLine = e.After.GetLineFromPosition(e.Changes[0].NewPosition).LineNumber;
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

                // Only recalculate header/types when structure changes
                _hasHeaderDetected = false;
                _columnTypes = null;
            }
            else
            {
                // Single-line edit: only invalidate that specific line
                var affectedLine = e.After.GetLineFromPosition(e.Changes[0].NewPosition).LineNumber;
                _lineCache.Remove(affectedLine);
            }
        }
    }
}
