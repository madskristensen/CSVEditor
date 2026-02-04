[marketplace]: <https://marketplace.visualstudio.com/items?itemName=MadsKristensen.CSVEditor>
[vsixgallery]: <http://vsixgallery.com/extension/CSVEditor.5164fa67-5caa-4d84-9087-bbaedc2a5539/>
[repo]: <https://github.com/madskristensen/CSVEditor>

# CSV Editor for Visual Studio

[![Build](https://github.com/madskristensen/CSVEditor/actions/workflows/build.yaml/badge.svg)](https://github.com/madskristensen/CSVEditor/actions/workflows/build.yaml)
![GitHub Sponsors](https://img.shields.io/github/sponsors/madskristensen)

Download this extension from the [Visual Studio Marketplace][marketplace]
or get the [CI build][vsixgallery].

--------------------------------------

**Finally, a proper CSV/TSV editing experience in Visual Studio.** CSV Editor transforms flat, hard-to-read data files into colorful, navigable documents.

![Syntax Highlighting](art/syntax-highlighting.png)

## ✨ Key Features at a Glance

- **Syntax Highlighting** — Color-coded columns for instant visual parsing
- **Column Alignment** — Align columns visually with a single command for table-like readability
- **Shrink Columns** — Remove leading/trailing whitespace from all cells
- **Alternate Row Colors** — Toggle alternating row backgrounds for improved readability
- **Copy as Markdown** — Copy selection as a formatted Markdown table
- **Smart Header Detection** — Automatically identifies header rows
- **QuickInfo Tooltips** — Hover over any cell to see column name, index, and detected data type
- **Column Sorting** — Sort ascending or descending directly from the tooltip
- **Error Detection** — Rows with inconsistent column counts are highlighted
- **Go To Column** — Jump to any column by number
- **Large File Support** — Optimized for files with 100K+ lines

**Supports:** CSV (comma-separated) and TSV (tab-separated) files

## Why CSV Editor?

Working with CSV files in a plain text editor is painful:

- **Hard to read** — Without column colors, data blends into an unreadable wall of text
- **Easy to misalign** — One wrong comma and your entire row is shifted
- **No context** — Which column is this value in? What's the header name?
- **Manual sorting** — Need to sort by a column? Time to open Excel or write a script

CSV Editor solves these problems while keeping you in Visual Studio where you belong.

## Features

![Context Menu](art/context-menu.png)

### Syntax Highlighting

Each column gets its own color, making it easy to visually track data across rows. Colors cycle through a palette designed to work with both light and dark themes.

### Column Alignment

Right-click and select **Align CSV Columns** to visually align columns for perfect readability. This uses virtual alignment (adornments) — your file content is not modified.

![Column Alignment](art/column-alignment.png)

### Shrink Columns

Right-click and select **Shrink CSV Columns** to remove leading and trailing whitespace from all cells. This is useful to:

- Clean up messy data with accidental spaces
- Reduce file size by removing unnecessary whitespace
- Prepare data for processing by systems that don't trim automatically

### Alternate Row Colors

Right-click and select **Toggle Alternate Row Colors** to highlight odd and even rows with alternating background colors. This significantly improves readability for:

- Wide tables with many columns
- Tables viewed in word-wrap mode
- Quick visual scanning of data

### Copy as Markdown Table

Right-click and select **Copy as Markdown Table** to copy the selection (or entire file if nothing is selected) as a formatted Markdown table. Perfect for:

- Pasting into GitHub issues, PRs, or README files
- Documentation and wikis
- Slack, Teams, or other Markdown-enabled chat

Example output:

```markdown
| Name  | Age | City     |
|-------|-----|----------|
| Alice | 30  | New York |
| Bob   | 25  | London   |
```

### QuickInfo Tooltips

Hover over any cell to see:

- **Column name** (from header) or column number
- **Column index** (e.g., "Column #3 of 8")
- **Detected data type** (Text, Number, Date, Boolean, etc.)
- **Sort links** — Click to sort the entire file by that column (ascending or descending)

Sort links appear when hovering over cells in the first row, whether or not the file has a header.

![QuickInfo Tooltip](art/quickinfo.png)

### Error Detection

Rows with too many or too few columns are flagged with squiggles, helping you catch data issues before they cause problems downstream.

![Error Detection](art/error-detection.png)

### Go To Column

Use **Edit > Go To > Go To Column** (or the command palette) to jump directly to a specific column by number.

### Large File Support

CSV Editor is optimized for large files with thousands of rows:

- **Smart caching** — Parsed lines are cached to avoid redundant work
- **Background validation** — Error detection runs in the background for files over 50K lines
- **Sampled column widths** — Alignment uses sampling for files over 50K lines, keeping the editor responsive
- **Virtualized rendering** — Only visible rows are processed, regardless of file size

## How can I help?

If you enjoy using the extension, please give it a ★★★★★ rating on the [Visual Studio Marketplace][marketplace].

Should you encounter bugs or have feature requests, head over to the [GitHub repo][repo] to open an issue if one doesn't already exist.

Pull requests are also very welcome, as I can't always get around to fixing all bugs myself. This is a personal passion project, so my time is limited.

Another way to help out is to [sponsor me on GitHub](https://github.com/sponsors/madskristensen).

