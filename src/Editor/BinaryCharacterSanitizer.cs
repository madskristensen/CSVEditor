using System.ComponentModel.Composition;
using System.Text;
using CSVEditor.Classification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace CSVEditor.Editor;

/// <summary>
/// Sanitizes binary characters in CSV/TSV files by replacing them with visible placeholders. This handles files
/// containing null bytes or other binary data that would normally be rejected by Visual Studio's text editor.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType(CsvContentTypeDefinition.CsvContentTypeName)]
[ContentType(CsvContentTypeDefinition.TsvContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class BinaryCharacterSanitizer : IWpfTextViewCreationListener
{
    private const char _replacementChar = '\uFFFD'; // Unicode replacement character �

    public void TextViewCreated(IWpfTextView textView)
    {
        // Subscribe to the first layout change to sanitize content after the buffer is fully loaded
        textView.LayoutChanged += OnFirstLayoutChanged;
    }

    private void OnFirstLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
    {
        if (sender is not IWpfTextView textView)
        {
            return;
        }

        // Unsubscribe immediately - we only want to run once
        textView.LayoutChanged -= OnFirstLayoutChanged;

        ITextSnapshot snapshot = textView.TextBuffer.CurrentSnapshot;
        var text = snapshot.GetText();

        // Single pass: sanitize while checking for binary characters
        StringBuilder sanitized = null;
        var foundBinaryChars = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var isBinary = (c is < (char)32 and not '\t' and not '\n' and not '\r') || (c is > (char)127 and < (char)160);

            if (isBinary && !foundBinaryChars)
            {
                // First binary char found - create StringBuilder and copy everything up to this point
                foundBinaryChars = true;
                sanitized = new StringBuilder(text.Length);
                _ = sanitized.Append(text, 0, i);
                _ = sanitized.Append(_replacementChar);
            }
            else if (foundBinaryChars)
            {
                // We're building sanitized version
                _ = sanitized.Append(isBinary ? _replacementChar : c);
            }
            // else: no binary chars yet, no StringBuilder allocated, just keep scanning
        }

        // Only apply edit if we found binary characters
        if (foundBinaryChars)
        {
            using (ITextEdit edit = textView.TextBuffer.CreateEdit())
            {
                _ = edit.Replace(new Span(0, snapshot.Length), sanitized.ToString());
                _ = edit.Apply();
            }
        }
    }
}
