namespace CSVEditor.Core;

/// <summary>
/// Represents the detected data type of a CSV column.
/// </summary>
public enum CsvDataType
{
    /// <summary>Unknown or mixed types.</summary>
    Unknown,
    
    /// <summary>Text/string values.</summary>
    Text,
    
    /// <summary>Whole numbers (int, long).</summary>
    Integer,
    
    /// <summary>Decimal numbers.</summary>
    Decimal,
    
    /// <summary>Boolean values (true/false, yes/no, 1/0).</summary>
    Boolean,
    
    /// <summary>Date values (without time).</summary>
    Date,
    
    /// <summary>Date and time values.</summary>
    DateTime,
    
    /// <summary>Email addresses.</summary>
    Email,
    
    /// <summary>Phone numbers.</summary>
    Phone,
    
    /// <summary>URLs/web addresses.</summary>
    Url,
    
    /// <summary>Currency/money values.</summary>
    Currency,
    
    /// <summary>Percentage values.</summary>
    Percentage,
    
    /// <summary>GUID/UUID values.</summary>
    Guid
}
