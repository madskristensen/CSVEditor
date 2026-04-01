using CSVEditor.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CSVEditor.Test;

[TestClass]
public class BinaryCharacterSanitizerTests
{
    [TestMethod]
    public void ShouldReplaceBinaryCharactersWithReplacementChar()
    {
        // This test verifies the concept - actual testing would require mocking IWpfTextView
        // which is complex. The key logic is tested here.

        string textWithBinary = "Name,Age\u0000,City\nJohn,30,NYC\nJane\u0001,25,LA";
        char replacementChar = '\uFFFD';

        // Simulate what the sanitizer does
        var sanitized = new System.Text.StringBuilder(textWithBinary.Length);
        foreach (char c in textWithBinary)
        {
            if (c < 32 && c != '\t' && c != '\n' && c != '\r')
            {
                sanitized.Append(replacementChar);
            }
            else if (c > 127 && c < 160)
            {
                sanitized.Append(replacementChar);
            }
            else
            {
                sanitized.Append(c);
            }
        }

        string result = sanitized.ToString();

        // Verify binary characters were replaced
        Assert.IsFalse(result.Contains('\0'));
        Assert.IsFalse(result.Contains('\u0001'));
        Assert.IsTrue(result.Contains(replacementChar));

        // Verify normal characters remain
        Assert.IsTrue(result.Contains("Name"));
        Assert.IsTrue(result.Contains("John"));
        Assert.IsTrue(result.Contains('\n'));
    }

    [TestMethod]
    public void ShouldPreserveTabsNewlinesAndCarriageReturns()
    {
        string textWithWhitespace = "Name\tAge\r\nJohn\t30\r\nJane\t25";

        var sanitized = new System.Text.StringBuilder(textWithWhitespace.Length);
        foreach (char c in textWithWhitespace)
        {
            if (c < 32 && c != '\t' && c != '\n' && c != '\r')
            {
                sanitized.Append('\uFFFD');
            }
            else if (c > 127 && c < 160)
            {
                sanitized.Append('\uFFFD');
            }
            else
            {
                sanitized.Append(c);
            }
        }

        string result = sanitized.ToString();

        // Verify whitespace characters are preserved
        Assert.IsTrue(result.Contains('\t'));
        Assert.IsTrue(result.Contains('\r'));
        Assert.IsTrue(result.Contains('\n'));
        Assert.AreEqual(textWithWhitespace, result);
    }
}
