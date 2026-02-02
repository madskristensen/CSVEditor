---
description: 'Guidelines for VSIX development with Community.VisualStudio.Toolkit'
applyTo: '**/*.cs, **/*.vsct, **/*.xaml'
---

# VSIX Toolkit Guidelines

## Scope

Applies to extensions using `Community.VisualStudio.Toolkit` with `ToolkitPackage` base class, but can be used as general VSIX guidance.

## When to Ask for Clarification

**Ask the user before proceeding when:**

- The request is ambiguous or could be interpreted multiple ways
- Multiple valid architectural approaches exist (ask which they prefer)
- A change would affect public API or user-facing behavior
- You're unsure which files/components the request applies to
- The fix requires a trade-off (performance vs. readability, etc.)

**Don't ask when:** The task is clear, the approach is obvious, or it's a straightforward bug fix.

## Updating These Instructions

When corrected or a non-obvious pattern emerges, suggest adding to Common Gotchas or Core Patterns. Keep entries to one line if possible.

## Critical Rules

### Threading (Most Common Bugs)

```csharp
// ALWAYS switch to UI thread before WPF/COM operations
await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

// NEVER use .Result, .Wait(), or Task.Run for UI work - causes deadlocks
// NEVER use async void - use async Task
```

### Core Patterns

| Pattern | Base Class | Example |
|---------|------------|---------|
| Package | `ToolkitPackage` | Call `RegisterCommandsAsync()` in `InitializeAsync` |
| Commands | `BaseCommand<T>` | Use `[Command(PackageIds.X)]` attribute |
| Options (sync) | `BaseOptionModel<T>` | `General.Instance.Property` in MEF/sync code |
| Options (async) | `BaseOptionModel<T>` | `await General.GetLiveInstanceAsync()` in async methods |
| MEF Taggers | `IViewTaggerProvider` | Export with `[ContentType]`, `[TagType]` |

### VS Helpers (use instead of raw SDK)

```csharp
await VS.StatusBar.ShowMessageAsync("msg");
await VS.Documents.OpenAsync(path);
await VS.MessageBox.ShowAsync("title", "msg");
var doc = await VS.Documents.GetActiveDocumentViewAsync();
General.Instance.Save(); // Save options
```

## Common Gotchas

| Issue | Fix |
|-------|-----|
| Command not appearing | Add `DynamicVisibility` flag in VSCT |
| Deadlock on load | Use `await`, never `.Result` |
| Wrong thread exception | `SwitchToMainThreadAsync()` first |
| Settings not persisting | Call `.Save()` after changes |
| Error List line numbers | API is 0-based, most tools output 1-based |
| External process hangs | Use async process with cancellation |

## External Processes

```csharp
// Run CLI tools asynchronously with output capture
var psi = new ProcessStartInfo(exe, args) {
    UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardOutput = true, RedirectStandardError = true
};
// Check exit code, not stderr (many tools write progress to stderr)
```

## .NET Framework 4.8 Constraints

- ✅ Modern C# syntax (records, patterns, file-scoped namespaces)
- ❌ No Span<T>, Index/Range operators, default interface methods

## Code Style

Follow `.editorconfig` rules. Match surrounding code style.
