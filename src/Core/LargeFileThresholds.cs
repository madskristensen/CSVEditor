namespace CSVEditor;

/// <summary>
/// Shared constants for large file handling across the extension.
/// </summary>
internal static class LargeFileThresholds
{
    /// <summary>
    /// Files larger than this are considered "large" and some features are disabled or limited.
    /// </summary>
    public const int LargeFileLineCount = 50000;

    /// <summary>
    /// Files larger than this disable error validation entirely.
    /// </summary>
    public const int DisableErrorValidationLineCount = 100000;

    /// <summary>
    /// Files larger than this disable column alignment.
    /// </summary>
    public const int DisableAlignmentLineCount = 100000;

    /// <summary>
    /// Maximum lines to cache for parsing results.
    /// </summary>
    public const int MaxCachedLines = 10000;

    /// <summary>
    /// Sample size for column width calculation in large files.
    /// </summary>
    public const int ColumnWidthSampleSize = 1000;
}
