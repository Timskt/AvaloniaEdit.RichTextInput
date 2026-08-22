# IME Preedit / Composition Guide

This document describes the complete IME preedit/composition implementation in `AvaloniaEdit.RichTextInput`, including Chinese, Japanese, Korean, and other input methods that use a composition-then-commit workflow.

This copy targets the **Avalonia 12 branch**:

- Avalonia: `12.0.0`
- Target frameworks: `.NET 8` and `.NET 10`
- Related branch: `ava12-feat`

The Avalonia 11 branch exposes the same IME behavior and public API. See [Version compatibility](#16-version-compatibility) for the branch-specific differences.

## 1. Why preedit matters

An input method does not necessarily insert a character into the document for every key press. When a user types Japanese `かな`, Chinese pinyin such as `nihao`, or a Korean syllable sequence, the input method first produces temporary composition text:

1. The input method sends preedit text and a composition cursor offset.
2. The editor displays that temporary text without changing the `TextDocument`.
3. The user chooses a candidate or confirms the composition.
4. The input method sends committed text, which is inserted as ordinary `TextInput`.

Ignoring step 1 makes the input appear invisible until confirmation. Writing preedit directly into the document corrupts document length, undo history, copy/serialization output, and often causes the final commit to be inserted twice.

This implementation keeps preedit in the IME client state and rendering layers instead of treating it as ordinary document content.

## 2. Capability matrix

| Capability | Status | Details |
| --- | --- | --- |
| Inline preedit | Implemented | Participates in visual-line layout without entering the document |
| Overlay preedit | Implemented | Drawn independently next to the caret without pushing body text |
| Hidden preedit | Implemented | Keeps IME state while suppressing composition rendering |
| Preedit cursor | Implemented | Supports the IME UTF-16 cursor offset; defaults to the end |
| Whole-string underline | Implemented | Solid underline when native IME data has no clause metadata |
| Per-clause underline | Implemented | AvaloniaEdit extension with ranges, styles, and brushes |
| Clauses across wrapped chunks | Implemented | Overlay rendering slices clauses per chunk |
| Commit at the old caret on click | Implemented | Runs in the PointerPressed tunnel phase |
| Synchronous/asynchronous reset compatibility | Implemented | Handles reset callbacks and delayed duplicate commits |
| Overlay wrapping at the right edge | Implemented | Splits at safe text-element/grapheme boundaries |
| Overlay background fill | Implemented | Prevents body text underneath from showing through |
| Preedit baseline alignment | Implemented | Prefers the caret visual line baseline |
| Horizontal scroll reservation | Implemented | Defaults to ten full-width character widths |
| Deferred scrolling after long commits | Implemented | Brings the caret into view again after layout |
| Escape cleanup | Implemented | Clears composition state and rendering |
| Lost-focus cleanup | Implemented | Clears preedit when focus leaves the editor |
| Document replacement cleanup | Implemented | Clears the old composition when `TextDocument` changes |
| Read-only protection | Implemented | Read-only TextArea does not expose an IME client |
| Child TextBox isolation | Implemented | SearchPanel and other child inputs do not trigger editor commit |

## 3. Minimal usage

For a system IME, application code normally does not need to call `SetImePreeditText` manually. `TextArea` becomes an Avalonia IME client through `TextInputMethodClientRequested`:

```csharp
using AvaloniaEdit;
using AvaloniaEdit.Editing;

var editor = new TextEditor
{
    Text = string.Empty
};

// Inline is the default.
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
```

As long as the TextArea is editable and focused, platform input methods such as Windows IME, macOS input methods, or fcitx/mozc can update preedit through Avalonia's native IME path.

For automated tests, demos, or a business-defined input method, preedit can be simulated directly:

```csharp
editor.TextArea.SetImePreeditText("かな", cursorOffset: 2);

// The preedit is still not part of editor.Document.
var isComposing = editor.TextArea.HasImePreedit;

editor.TextArea.ClearImePreedit();
```

## 4. Preedit lifecycle

### 4.1 Starting or updating a composition

Avalonia calls `TextInputMethodClient.SetPreeditText`. The internal `TextAreaTextInputMethodClient` stores:

- the current preedit string;
- the optional cursor offset;
- optional clause metadata;
- surrounding text;
- the current selection;
- the caret rectangle and TextView visual used by the candidate window.

It then updates either the Inline generator or the Overlay layer according to `ImePreeditDisplayMode`.

### 4.2 While composing

Composition text exists only in:

- the preedit state of `TextAreaTextInputMethodClient`;
- `PreeditTextElementGenerator` in Inline mode;
- `PreeditLayer` in Overlay mode.

It does not enter:

- `TextDocument.Text`;
- document versions or the undo stack;
- ordinary copy/serialization output;
- rich-content anchors or the inline-object collection.

### 4.3 Committing text

When the IME sends `TextInput`, `TextArea`:

1. Clears the current preedit state and rendering;
2. Uses the existing `PerformTextInput`/selection replacement path;
3. Raises `TextEntering`, performs replacement, and raises `TextEntered`;
4. Calls `Caret.BringCaretToView()` immediately;
5. Calls it again at `DispatcherPriority.Loaded`, after the new extent/layout is available.

Application code should not insert the preedit a second time.

### 4.4 Cancellation and cleanup

Preedit is cleared when:

- the IME sends an empty preedit;
- `Escape` is pressed;
- the TextArea loses focus;
- the TextArea receives a new `TextDocument`;
- committed text arrives;
- the application calls `ClearImePreedit()`;
- the IME client moves from one TextArea to another.

## 5. Display modes

### 5.1 Inline (default)

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
```

Inline mode creates a temporary visual-line element at the caret:

- it pushes body text after the caret;
- it participates in visual-line measurement and wrapping;
- its position matches the position of the eventual committed text;
- it does not modify the document;
- the normal editor caret is hidden while the preedit text run draws the composition cursor.

This is the recommended default for text boxes, chat inputs, and editors where composition must align exactly with body text.

### 5.2 Overlay

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
```

Overlay mode uses a separate `PreeditLayer`:

- it does not push body text;
- it starts at the caret `X` coordinate, not `caret.Right`;
- each chunk is measured and rendered independently;
- text past the right edge wraps to the next line;
- a discoverable editor background is filled before drawing text;
- the caret visual line baseline is used to align the composition with body text;
- cursor, underlines, and background are drawn by the overlay layer.

Use Overlay when the body layout must remain unchanged or the application needs independent control of composition rendering. Because Overlay is outside body visual-line layout, configure horizontal scroll reservation as well.

### 5.3 Hidden

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Hidden;
```

Hidden mode accepts and stores preedit but does not render it. Commit, Escape, focus changes, and document replacement still follow the normal lifecycle.

This mode is useful when:

- the application draws composition text elsewhere;
- composition rendering should be temporarily suppressed;
- only the IME client's surrounding-text and selection behavior is required.

## 6. Public API

### 6.1 `TextArea.SetImePreeditText`

```csharp
public void SetImePreeditText(
    string text,
    int? cursorOffset = null,
    IReadOnlyList<ImePreeditClause> clauses = null);
```

Parameters:

- `text`: the preedit string. `null` or an empty string clears preedit;
- `cursorOffset`: a UTF-16 offset clamped to `[0, text.Length]`; `null` means the end of the string;
- `clauses`: optional underline descriptions. Native Avalonia IME notifications currently do not carry clause metadata, so this parameter is an AvaloniaEdit extension.

Example:

```csharp
editor.TextArea.SetImePreeditText(
    text: "日本語入力",
    cursorOffset: 4);
```

### 6.2 `TextArea.HasImePreedit`

```csharp
if (editor.TextArea.HasImePreedit)
{
    // The IME is currently composing.
}
```

This reports whether a non-empty preedit is active. It does not report whether committed text exists in the document.

### 6.3 `TextArea.ClearImePreedit`

```csharp
editor.TextArea.ClearImePreedit();
```

This clears preedit state and rendering without modifying the document or inserting ordinary text.

### 6.4 `ImePreeditClause`

```csharp
var clauses = new[]
{
    new ImePreeditClause(
        start: 0,
        length: 2,
        underlineStyle: ImePreeditUnderlineStyle.Solid),
    new ImePreeditClause(
        start: 2,
        length: 2,
        underlineStyle: ImePreeditUnderlineStyle.Thick,
        underlineBrush: Brushes.Red),
    new ImePreeditClause(
        start: 4,
        length: 2,
        underlineStyle: ImePreeditUnderlineStyle.None)
};

editor.TextArea.SetImePreeditText("日本語入力", 3, clauses);
```

Supported underline styles:

- `Solid`: one solid underline;
- `None`: no underline;
- `Thick`: a thicker solid underline, commonly used for the active clause;
- `Double`: two solid underlines;
- `Dotted`: a dotted underline;
- `Dashed`: a dashed underline.

Rules:

- `start` and `length` must be non-negative;
- ranges use UTF-16 offsets, not Unicode scalar values or screen glyph counts;
- clauses must stay within the preedit string;
- clauses must not overlap;
- clauses may be supplied in any order and are sorted by `start` internally;
- gaps between supplied clauses are automatically filled with `Solid`;
- empty clauses are ignored;
- a null `underlineBrush` uses the preedit foreground.

Because offsets are UTF-16 offsets, surrogate pairs must be counted as two .NET `char` values. For example:

```csharp
var text = "A😀B";
// A = 0..1, 😀 = 1..3, B = 3..4.
var emojiClause = new ImePreeditClause(1, 2);
```

## 7. Horizontal scroll reservation

Overlay preedit at the end of a long line is outside the document extent. The editor therefore reserves space **only while an Overlay composition is active**, before the caret reaches the viewport edge:

```csharp
editor.Options.ImePreeditHorizontalScrollCharCount = 10;
```

No space is reserved when there is no active Overlay preedit; Inline and Hidden modes do not use this overlay-only reservation. The default is `10`. The approximate reserved width is:

```text
TextView.WideSpaceWidth * ImePreeditHorizontalScrollCharCount
```

The option affects both:

- the horizontal extent measured by `TextView`;
- the rectangle passed to `TextView.MakeVisible()` by `Caret.BringCaretToView()`.

To disable the reservation:

```csharp
editor.Options.ImePreeditHorizontalScrollCharCount = 0;
```

Negative values are rejected:

```csharp
// Throws ArgumentOutOfRangeException.
editor.Options.ImePreeditHorizontalScrollCharCount = -1;
```

Recommendations:

- keep the default `10` for normal CJK input;
- use `4` or `5` in a very narrow fixed-width input box;
- use `0` when Overlay is disabled and only Inline mode is used;
- when changed at runtime, the TextView clears visual lines and remeasures.

## 8. Click, reset, and duplicate commits

IME APIs commonly guarantee reset on `ResetRequested` or when the IME client changes, but not necessarily when the caret moves. Without extra handling, this sequence is possible:

1. preedit is displayed at the old caret;
2. the user clicks elsewhere in the editor;
3. the caret moves to the new position;
4. the IME commits later;
5. the commit is inserted at the wrong caret.

This implementation registers a `PointerPressed` tunnel handler before `SelectionMouseHandler` moves the caret:

1. capture the old-caret preedit;
2. request an IME reset;
3. if reset synchronously produces a commit, use the platform commit;
4. if reset produces no commit, call `PerformTextInput(preedit)` at the old caret;
5. suppress one later platform commit only when it exactly matches the self-committed text;
6. clear preedit state;
7. allow the original click handling to move the caret.

Only the TextArea itself and its TextView visual subtree participate. SearchPanel, CompletionWindow, and other child TextBoxes are not treated as clicks on the editor surface.

## 9. IME client behavior

The internal `TextAreaTextInputMethodClient` provides:

- `SupportsPreedit = true`;
- `SupportsSurroundingText = true`;
- `TextViewVisual`;
- the current caret `CursorRectangle`;
- surrounding text for the current line;
- the current selection as a range relative to the current line.

Selection offsets sent to the IME are line-relative and clamped to the current line. This prevents a cross-line selection or document-boundary condition from producing an invalid platform value.

Only editable TextAreas respond to `TextInputMethodClientRequested`. When `ReadOnlySectionProvider` marks the whole editor read-only, the TextArea does not expose its IME client.

## 10. Overlay rendering implementation

Key files:

```text
src/AvaloniaEdit/Rendering/PreeditLayer.cs
src/AvaloniaEdit/Rendering/PreeditDecorationRenderer.cs
```

Rendering flow:

1. Read the caret rectangle and TextView scroll offset;
2. read line top, line height, and baseline from the caret visual line;
3. start layout at `caretRect.X - HorizontalOffset`;
4. measure the available width with `TextLayout`;
5. choose a safe text-element boundary using `StringInfo.ParseCombiningCharacters`;
6. draw the current chunk with a no-wrap layout;
7. fill background, draw text, then draw clause underlines;
8. draw the cursor in the chunk containing the cursor offset;
9. move to the next line and process the remaining text;
10. expose diagnostics used by headless rendering tests, such as chunk count, origins, lengths, cursor count, and background-fill count.

Wrapping does not split:

- UTF-16 surrogate pairs such as emoji;
- combining marks such as `e\u0301`;
- .NET text elements representing a combined character sequence.

If the visual line is not ready, rendering falls back to the caret rectangle. Render does not force construction of a new visual line, avoiding layout re-entry.

## 11. Inline rendering implementation

Key file:

```text
src/AvaloniaEdit/Rendering/PreeditTextElementGenerator.cs
```

The Inline generator constructs one temporary `PreeditTextElement` at the caret offset. The element:

- uses the visual-line element length mechanism to push following body text;
- creates a `PreeditTextRun`;
- draws preedit with `TextLayout`;
- draws the cursor at the measured width of the cursor prefix;
- reuses the clause decoration renderer for underlines.

The element length is a layout placeholder, not document character length. Consequently, preedit does not change `Document.TextLength`, document offsets, or undo history.

## 12. Application guidance

### Recommended configuration

```csharp
var editor = new TextEditor
{
    FontSize = 14,
    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    VerticalScrollBarVisibility = ScrollBarVisibility.Visible
};

editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
editor.Options.ImePreeditHorizontalScrollCharCount = 10;
```

### Display composition state

```csharp
void UpdateStatus()
{
    statusText.Text = editor.TextArea.HasImePreedit
        ? "IME composing"
        : "Ready";
}

editor.TextArea.TextEntered += (_, _) => UpdateStatus();
editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
```

If an application needs every native preedit update, observe the Avalonia IME client integration layer. Most applications only need `HasImePreedit` and the normal TextArea text events.

### Do not treat preedit as document text

Do not manually insert preedit into `Document` when:

- the IME client reports a preedit update;
- `HasImePreedit` becomes `true`;
- the Overlay layer redraws.

Only committed `TextInput` should use `PerformTextInput` or the normal TextArea input path.

## 13. Troubleshooting

### Q1: Why did `Document.Text` not change after `SetImePreeditText`?

That is expected. Preedit is temporary composition state. Only committed text enters the document.

### Q2: Why is the cursor at the end when no `cursorOffset` is supplied?

`cursorOffset == null` is defined as the end of the preedit string, preserving compatibility with callers that only provide `SetPreeditText(string)`.

### Q3: Should an emoji count as one character or two for a clause offset?

Use UTF-16 offsets. A basic emoji may occupy two UTF-16 code units; see [ImePreeditClause](#64-imepreeditclause).

### Q4: Why does SearchPanel not commit the editor preedit?

The tunnel handler checks whether the event source belongs to the TextArea's own TextView visual subtree. External and child input controls are not treated as editor-surface clicks.

### Q5: Why might Overlay not fill the background?

The implementation searches the TextArea visual-parent chain for a non-transparent `Background`. If no usable background exists, the correct color cannot be inferred, so the preedit is drawn without a fill. Set an opaque background on the TextArea or its container when reliable occlusion is required.

### Q6: Why does a read-only editor not activate the IME?

A read-only TextArea does not expose an IME client. This prevents an input method from committing text into a non-editable control and is intentional.

## 14. Testing and manual verification

### Automated tests

IME-focused tests:

```bash
dotnet test test/AvaloniaEdit.Tests/AvaloniaEdit.Tests.csproj \
  --no-build --no-restore \
  --filter FullyQualifiedName~ImePreeditTests
```

The tests cover:

- document immutability while composing;
- Inline cursor and clause normalization;
- default cursor-at-end behavior;
- rejection of out-of-range and overlapping clauses;
- clause slicing across wrapped chunks;
- Overlay caret-X placement;
- Overlay wrapping and background fill;
- grapheme/surrogate-pair-safe splitting;
- baseline lookup;
- horizontal extent reservation;
- old-caret click commit and duplicate suppression;
- synchronous reset commit;
- preservation of non-matching commits;
- read-only IME-client isolation;
- cleanup on document replacement.

Full test suite:

```bash
dotnet test AvaloniaEdit.slnx --no-build --no-restore
```

### Demo

```bash
dotnet run --project src/AvaloniaEdit.Demo/AvaloniaEdit.Demo.csproj
```

After starting the demo, use this sequence to verify the feature manually:

1. Type Chinese pinyin and observe Inline preedit and its cursor;
2. Use a Japanese Romaji input method for a long conversion;
3. Click elsewhere in the same editor during composition and verify that the text is not committed at the wrong caret;
4. Switch to `Overlay` and verify middle-of-line occlusion and right-edge wrapping;
5. Convert a long Japanese phrase at the end of a long line and observe horizontal scrolling and post-commit caret visibility;
6. Press Escape and verify that the composition disappears without adding cancelled text;
7. Open SearchPanel, type in its search box, and verify that it does not commit the editor's preedit;
8. Replace `TextDocument` or move focus away and verify that preedit is cleared.

## 15. Source layout

| File | Responsibility |
| --- | --- |
| `Editing/TextArea.cs` | IME client, lifecycle, input handling, pre-click commit, deferred scrolling |
| `Editing/ImePreeditClause.cs` | Clause data type, validation, and gap completion |
| `Editing/Caret.cs` | Caret visibility rectangle and IME horizontal reservation |
| `Rendering/PreeditTextElementGenerator.cs` | Inline preedit visual-line element/run |
| `Rendering/PreeditLayer.cs` | Overlay chunk layout and rendering |
| `Rendering/PreeditDecorationRenderer.cs` | Underlines, brushes, and clause rendering |
| `Rendering/TextView.cs` | Horizontal extent reservation |
| `TextEditorOptions.cs` | `ImePreeditHorizontalScrollCharCount` |
| `test/AvaloniaEdit.Tests/Editing/ImePreeditTests.cs` | IME unit and headless rendering tests |

## 16. Version compatibility

### Avalonia 12

The current branch uses:

```text
Avalonia 12.0.0
TargetFramework: net8.0; net10.0
Solution: AvaloniaEdit.slnx
```

Build with:

```bash
dotnet build AvaloniaEdit.slnx --no-restore
```

### Avalonia 11

The Avalonia 11 branch uses:

```text
Avalonia 11.0.10
TargetFramework: netstandard2.0; net6.0
Solution: AvaloniaEdit.sln
```

Build with:

```bash
dotnet build AvaloniaEdit.sln --no-restore
```

Avalonia 11.0.10 is intentionally used as the minimum version because the complete old-caret commit compatibility path depends on:

- `SetPreeditText(string, int?)`;
- `RequestReset()`;
- `ResetRequested`.

Avalonia 11.0.0 does not expose enough IME API to implement the full synchronous/asynchronous reset and pre-click commit behavior.

## 17. Known boundaries

1. Avalonia's native `TextInputMethodClient` carries preedit text and a cursor offset, but not per-clause metadata. Clause support is therefore an AvaloniaEdit extension and requires clause data from the application or a custom IME integration.
2. Overlay background fill depends on a non-transparent background discoverable in the visual tree. A fully transparent editor cannot infer which color should cover body text underneath.
3. This implementation handles TextArea text composition. Business semantics for rich inline objects remain the responsibility of `RichTextInputManager`.
4. Preedit is intentionally excluded from document copy, serialization, and undo. Applications that need to persist unconfirmed input must store that external state separately.

## 18. Related documentation

- [RichTextInput API Guide](RichTextInput.md)
- [中文 IME Preedit 指南](IME-Preedit.zh-CN.md)
- [AvaloniaEdit PR #592](https://github.com/AvaloniaUI/AvaloniaEdit/pull/592)
