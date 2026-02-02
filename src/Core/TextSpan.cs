using System;



namespace CSVEditor.Core;

/// <summary>
/// Represents a span of text with a start position and length.
/// This is a simple value type that avoids dependency on VS APIs.
/// </summary>
public readonly struct TextSpan : IEquatable<TextSpan>
{
    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;

    public TextSpan(int start, int length)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), "Start must be non-negative.");
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

        Start = start;
        Length = length;
    }

    public bool Contains(int position) => position >= Start && position < End;

    public bool Overlaps(TextSpan other) => Start < other.End && other.Start < End;

    public override string ToString() => $"[{Start}..{End})";

    public bool Equals(TextSpan other) => Start == other.Start && Length == other.Length;

    public override bool Equals(object obj) => obj is TextSpan other && Equals(other);

    public override int GetHashCode() => unchecked(Start * 397 ^ Length);

    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);

    public static bool operator !=(TextSpan left, TextSpan right) => !left.Equals(right);
}
