using System;
using CSVEditor.Core;

namespace CSVEditor.Test;

[TestClass]
public sealed class TextSpanTests
{
    [TestMethod]
    public void Contains_PositionInSpan_ReturnsTrue()
    {
        var span = new TextSpan(5, 10);
        Assert.IsTrue(span.Contains(5));
        Assert.IsTrue(span.Contains(10));
        Assert.IsTrue(span.Contains(14));
    }

    [TestMethod]
    public void Contains_PositionOutsideSpan_ReturnsFalse()
    {
        var span = new TextSpan(5, 10);
        Assert.IsFalse(span.Contains(4));
        Assert.IsFalse(span.Contains(15));
    }

    [TestMethod]
    public void End_ReturnsStartPlusLength()
    {
        var span = new TextSpan(5, 10);
        Assert.AreEqual(15, span.End);
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var span1 = new TextSpan(5, 10);
        var span2 = new TextSpan(5, 10);
        Assert.AreEqual(span1, span2);
        Assert.IsTrue(span1 == span2);
    }

    [TestMethod]
    public void Constructor_NegativeStart_Throws()
    {
        bool thrown = false;
        try
        {
            _ = new TextSpan(-1, 10);
        }
        catch (ArgumentOutOfRangeException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Expected ArgumentOutOfRangeException was not thrown");
    }

    [TestMethod]
    public void Constructor_NegativeLength_Throws()
    {
        bool thrown = false;
        try
        {
            _ = new TextSpan(0, -1);
        }
        catch (ArgumentOutOfRangeException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Expected ArgumentOutOfRangeException was not thrown");
    }
}
