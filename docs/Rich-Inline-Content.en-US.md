# Rich Inline Content and Mixed-Height Layout

This guide documents the rich inline-content path used by `AvaloniaEdit.RichTextInput` on the `ava12-feat` branch. The same design is implemented on `ava11-feat` with the Avalonia 11 compatibility surface.

## 1. What the feature provides

The editor can keep ordinary text, emoji, images, files, and application-defined controls in one document. Rich items participate in editor selection, Backspace/Delete, clipboard and drag/drop handling, undo/redo, and document-order enumeration.

The demo's `Click me` control is not an editor-external overlay. It is a normal rich-content item, so it follows the same lifecycle as an emoji or image.

## 2. The object-replacement marker is part of the document

Every rich item occupies exactly one `U+FFFC` OBJECT REPLACEMENT CHARACTER in the text document:

```text
text + "\\uFFFC" + text
```

The associated `RichTextContentItem` stores the actual value and presentation metadata. An `AnchorSegment` tracks the marker as edits before and after the item change document offsets.

Do not create an inline object with a zero-length element and a naked caret offset. That approach breaks as soon as text, emoji, or another rich item is inserted around it: the object has no stable document range, cannot be removed reliably, and stale offsets can point at the wrong content.

## 3. Insertion, deletion, and history

Use the manager APIs rather than editing the marker directly:

```csharp
manager.InsertEmoji("😀");
manager.InsertImage(bitmap, "photo.png");
manager.InsertContent(RichTextContent.FromCustom(
    "Click me", value: "DemoButton", styleKey: "demo-button"));
```

The manager replaces the current selection when appropriate, creates the marker and anchor, invalidates visual lines, and records an undo operation. Removing an item removes only its marker and associated content. Undo and redo restore the same rich item without changing neighboring emoji or controls.

For bulk removal, enumerate a snapshot and remove in descending document offset order:

```csharp
foreach (var item in manager.GetItemsInDocumentOrder()
    .Where(item => item.Content.StyleKey == "demo-button")
    .OrderByDescending(item => item.Offset)
    .ToArray())
{
    manager.RemoveContent(item);
}
```

Removing from the end prevents earlier deletions from invalidating offsets that have not yet been processed.

## 4. Interactive child controls

The manager renders each rich item inside a wrapper that owns editor selection and pointer routing. A child `Button` can mark pointer events as handled before the wrapper sees them, so the wrapper listens to pressed, released, double-tap, and context-requested events with `handledEventsToo: true`. This keeps selection and subsequent Backspace/Delete reliable even when the child is a real interactive Avalonia control.

When the wrapper is detached, its routed-event handlers are removed as well. This avoids stale event subscriptions and references to old visual trees.

The wrapper is a layered `Grid`, not a sizing `Border`. Its background and selection border are overlay chrome, while only the content host (and intentional style padding) participates in the inline object's desired size. Selecting an item therefore does not add border pixels, change the visual-row height, or shift neighboring text/caret geometry.

## 5. Vertical alignment rules

There are two different coordinate systems:

- **Line-box alignment** (`Top`, `Center`, `Bottom`) positions an inline object relative to the complete line box. This is appropriate for content that intentionally follows the full visual row.
- **Baseline alignment** positions the object relative to the text baseline. This is appropriate for text-like chips, buttons, emoji, and other small inline controls.

The complete line box can be made much taller by a neighboring image or card. Therefore a small button configured as `Center` is expected to move to the middle of that tall row. This is not a faulty calculation; it is the consequence of asking for center alignment in the line box.

The built-in emoji factory uses the editor's current `FontSize`, zero top/bottom margin, and `Baseline` alignment. This prevents a default emoji from adding artificial vertical padding or making an ordinary text row taller. A custom `ElementFactory` can still expand the line deliberately by returning a larger font/control or non-zero vertical margin.

The demo configures `Click me` as `Bottom` and emoji as `Baseline`:

```csharp
manager.InlineObjectAlignmentSelector = item =>
    item.Content.StyleKey == "demo-button"
        ? InlineObjectVerticalAlignment.Bottom
        : item.Content.Kind == RichTextContentKind.Emoji
            ? InlineObjectVerticalAlignment.Baseline
            : manager.InlineObjectAlignment;
```

`InlineObjectAlignmentSelector` has final authority. Once an application supplies a selector, it should explicitly preserve the emoji baseline rule if that is the desired behavior.

This is intentional: the button is placed at the bottom of the complete line box. The renderer keeps the text run, caret, and IME preedit on the same bottom-aligned text content area instead of allowing the taller inline object to move them toward the top of the row. Images can continue to use `Bottom`, `Center`, or another business-specific alignment.

The line box and the text content area are deliberately measured separately. A tall image or button may expand the line box, but it must not change the text baseline or move a caret above the text. `Baseline` remains available for small text-like objects that should follow the baseline directly; explicit `Top`, `Center`, and `Bottom` are relative to the complete line box.

The renderer also clamps arranged objects to the current line box. A large object that is taller than the line is kept at the line start rather than leaking into an adjacent line.

## 6. Demo verification matrix

1. Paste a tall image into a line and use **Add control**. The button should be at the bottom of the line, while text and the caret remain at the bottom of the text content area.
2. Press **Enter**, add the button on a separate line, and confirm normal chip placement.
3. Select the button and press Backspace or Delete. Only the button disappears.
4. Insert an emoji before and after the button, remove the button, and confirm both emoji remain in order.
5. Put an emoji next to ordinary text with no tall sibling. Its font metrics and row height should match the editor text; there should be no extra top/bottom gap.
6. Add multiple buttons and use **Clear controls**. Emoji and images must remain.
7. Exercise Undo and Redo after each insertion and deletion.
8. Test a long Chinese pinyin composition from both the first and second document lines; Inline preedit must keep wrapping on later visual rows.
9. Test an IME composition before and after rich items; preedit must not become a rich marker.
10. Paste/drop images and files, then copy/paste within the same process and verify content metadata is retained where the platform supports the rich format.

## 7. Testing

The rendering regression test is:

```bash
dotnet test test/AvaloniaEdit.Tests/AvaloniaEdit.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~Inline_Object_Baseline_Remains_Text_Aligned_When_Tall_Sibling_Expands_Line
```

It constructs a line containing a tall object and a small baseline-aligned object, then verifies that the small object follows the baseline formula rather than the line-center formula. The mixed-height regression tests also verify that a bottom-aligned button and a tall sibling share the line bottom while ordinary text, caret geometry, and the IME preedit baseline stay in the text content area. Rich input tests additionally cover marker deletion, emoji preservation, Delete/Backspace behavior, anchor movement, editor-font-sized emoji, baseline alignment, and protection against ordinary-row height expansion.

## 8. Compatibility

- `ava12-feat`: Avalonia 12.0.0, `net8.0`/`net10.0` library targets.
- `ava11-feat`: Avalonia 11.0.10 compatibility branch, `netstandard2.0`/`net6.0` library targets.

The public rich-input concepts are the same on both branches; only Avalonia API and target-framework details differ.
