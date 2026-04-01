# Script to inject binary characters into a CSV file for testing
# Run this to create a CSV file with null bytes and control characters

$inputFile = "test-data\binary-chars-test.csv"
$outputFile = "test-data\binary-chars-test-with-nulls.csv"

# Read the original file
$content = Get-Content $inputFile -Raw

# Replace some text with text + binary characters
$content = $content.Replace("null byte after this", "null byte$([char]0)here")
$content = $content.Replace("Control char here", "Control$([char]1)char$([char]2)here")
$content = $content.Replace("Another test", "Test$([char]7)with$([char]8)bells")

# Write the modified content as bytes to preserve binary characters
[System.IO.File]::WriteAllText($outputFile, $content, [System.Text.Encoding]::UTF8)

Write-Host "Created test file with binary characters: $outputFile" -ForegroundColor Green
Write-Host ""
Write-Host "The file contains:" -ForegroundColor Yellow
Write-Host "  - Null byte (0x00) in row 3"
Write-Host "  - Control characters (0x01, 0x02) in row 5"
Write-Host "  - Bell and backspace (0x07, 0x08) in row 7"
Write-Host ""
Write-Host "Try opening it in Visual Studio to see the binary character handling in action!"
