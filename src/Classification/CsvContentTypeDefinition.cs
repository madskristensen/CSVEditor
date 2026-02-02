using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Classification;

/// <summary>
/// Defines the CSV content type and associates it with file extensions.
/// </summary>
internal static class CsvContentTypeDefinition
{
    public const string CsvContentTypeName = "csv";
    public const string TsvContentTypeName = "tsv";

    /// <summary>
    /// Defines the "csv" content type that inherits from "text".
    /// </summary>
    [Export]
    [Name(CsvContentTypeName)]
    [BaseDefinition("text")]
    internal static ContentTypeDefinition CsvContentType = null;

    /// <summary>
    /// Defines the "tsv" content type that inherits from "text".
    /// </summary>
    [Export]
    [Name(TsvContentTypeName)]
    [BaseDefinition("text")]
    internal static ContentTypeDefinition TsvContentType = null;

    /// <summary>
    /// Associates .csv files with the CSV content type.
    /// </summary>
    [Export]
    [FileExtension(".csv")]
    [ContentType(CsvContentTypeName)]
    internal static FileExtensionToContentTypeDefinition CsvFileExtension = null;

    /// <summary>
    /// Associates .tsv files with the TSV content type.
    /// </summary>
    [Export]
    [FileExtension(".tsv")]
    [ContentType(TsvContentTypeName)]
    internal static FileExtensionToContentTypeDefinition TsvFileExtension = null;

    /// <summary>
    /// Associates .tab files with the TSV content type.
    /// </summary>
    [Export]
    [FileExtension(".tab")]
    [ContentType(TsvContentTypeName)]
    internal static FileExtensionToContentTypeDefinition TabFileExtension = null;
}
