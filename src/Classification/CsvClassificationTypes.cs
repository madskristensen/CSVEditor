using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;



namespace CSVEditor.Classification;

/// <summary>
/// Defines classification types for CSV columns (rainbow coloring).
/// Each column gets a different color classification.
/// </summary>
internal static class CsvClassificationTypes
{
    public const int ColorCount = 10;

    public const string Column0 = "csv.column.0";
    public const string Column1 = "csv.column.1";
    public const string Column2 = "csv.column.2";
    public const string Column3 = "csv.column.3";
    public const string Column4 = "csv.column.4";
    public const string Column5 = "csv.column.5";
    public const string Column6 = "csv.column.6";
    public const string Column7 = "csv.column.7";
    public const string Column8 = "csv.column.8";
    public const string Column9 = "csv.column.9";

    public static string GetColumnClassificationType(int columnIndex)
        {
            return $"csv.column.{columnIndex % ColorCount}";
        }

        // Classification type definitions
        [Export]
        [Name(Column0)]
        internal static ClassificationTypeDefinition Column0Definition = null;

        [Export]
        [Name(Column1)]
        internal static ClassificationTypeDefinition Column1Definition = null;

        [Export]
        [Name(Column2)]
        internal static ClassificationTypeDefinition Column2Definition = null;

        [Export]
        [Name(Column3)]
        internal static ClassificationTypeDefinition Column3Definition = null;

        [Export]
        [Name(Column4)]
        internal static ClassificationTypeDefinition Column4Definition = null;

        [Export]
        [Name(Column5)]
        internal static ClassificationTypeDefinition Column5Definition = null;

        [Export]
        [Name(Column6)]
        internal static ClassificationTypeDefinition Column6Definition = null;

        [Export]
        [Name(Column7)]
        internal static ClassificationTypeDefinition Column7Definition = null;

        [Export]
        [Name(Column8)]
        internal static ClassificationTypeDefinition Column8Definition = null;

        [Export]
        [Name(Column9)]
        internal static ClassificationTypeDefinition Column9Definition = null;
    }

/// <summary>
/// Format definitions for CSV column colors.
/// These colors are designed to be distinguishable and readable.
/// </summary>
[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column0)]
[Name(CsvClassificationTypes.Column0)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn0Format : ClassificationFormatDefinition
{
    public CsvColumn0Format()
    {
        DisplayName = "CSV Column 1";
        ForegroundColor = Color.FromRgb(220, 50, 47);  // Red
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column1)]
[Name(CsvClassificationTypes.Column1)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn1Format : ClassificationFormatDefinition
{
    public CsvColumn1Format()
    {
        DisplayName = "CSV Column 2";
        ForegroundColor = Color.FromRgb(203, 75, 22);  // Orange
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column2)]
[Name(CsvClassificationTypes.Column2)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn2Format : ClassificationFormatDefinition
{
    public CsvColumn2Format()
    {
        DisplayName = "CSV Column 3";
        ForegroundColor = Color.FromRgb(181, 137, 0);  // Yellow
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column3)]
[Name(CsvClassificationTypes.Column3)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn3Format : ClassificationFormatDefinition
{
    public CsvColumn3Format()
    {
        DisplayName = "CSV Column 4";
        ForegroundColor = Color.FromRgb(133, 153, 0);  // Green
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column4)]
[Name(CsvClassificationTypes.Column4)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn4Format : ClassificationFormatDefinition
{
    public CsvColumn4Format()
    {
        DisplayName = "CSV Column 5";
        ForegroundColor = Color.FromRgb(42, 161, 152);  // Cyan
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column5)]
[Name(CsvClassificationTypes.Column5)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn5Format : ClassificationFormatDefinition
{
    public CsvColumn5Format()
    {
        DisplayName = "CSV Column 6";
        ForegroundColor = Color.FromRgb(38, 139, 210);  // Blue
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column6)]
[Name(CsvClassificationTypes.Column6)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn6Format : ClassificationFormatDefinition
{
    public CsvColumn6Format()
    {
        DisplayName = "CSV Column 7";
        ForegroundColor = Color.FromRgb(108, 113, 196);  // Violet
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column7)]
[Name(CsvClassificationTypes.Column7)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn7Format : ClassificationFormatDefinition
{
    public CsvColumn7Format()
    {
        DisplayName = "CSV Column 8";
        ForegroundColor = Color.FromRgb(211, 54, 130);  // Magenta
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column8)]
[Name(CsvClassificationTypes.Column8)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn8Format : ClassificationFormatDefinition
{
    public CsvColumn8Format()
    {
        DisplayName = "CSV Column 9";
        ForegroundColor = Color.FromRgb(0, 153, 153);  // Teal
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}

[Export(typeof(EditorFormatDefinition))]
[ClassificationType(ClassificationTypeNames = CsvClassificationTypes.Column9)]
[Name(CsvClassificationTypes.Column9)]
[UserVisible(true)]
[Order(Before = Priority.Default)]
internal sealed class CsvColumn9Format : ClassificationFormatDefinition
{
    public CsvColumn9Format()
    {
        DisplayName = "CSV Column 10";
        ForegroundColor = Color.FromRgb(150, 100, 50);  // Brown
        FontTypeface = new System.Windows.Media.Typeface("Consolas");
    }
}
