# 富内容 Inline Object 与混合高度布局

本文档说明 `AvaloniaEdit.RichTextInput` 在 `ava12-feat` 分支中的富内容实现。`ava11-feat` 分支使用 Avalonia 11 兼容 API，实现的设计和行为保持一致。

## 1. 能力范围

编辑器可以在同一个文档中混排普通文本、emoji、图片、文件和业务自定义控件。富内容会参与编辑器的选区、Backspace/Delete、剪贴板、拖放、撤销/重做以及按文档顺序枚举。

Demo 中的 `Click me` 不是脱离编辑器的浮层，而是一个普通的富内容 item，所以它和 emoji、图片拥有相同的生命周期。

## 2. 富内容必须占用文档中的替换标记

每个富内容在文档文本中占用一个 `U+FFFC OBJECT REPLACEMENT CHARACTER`：

```text
text + "\\uFFFC" + text
```

实际业务值和显示元数据保存在 `RichTextContentItem` 中；`AnchorSegment` 负责跟踪这个 marker，使其前后的文本编辑不会让 item 的 offset 失效。

不要使用“零长度 inline element + 裸 caret offset”的旧方式。文本、emoji 或其他富内容在附近插入后，这种对象没有稳定的文档范围，无法可靠删除，过期 offset 还可能指向错误的内容。

## 3. 插入、删除和撤销重做

请通过 manager API 操作富内容，不要直接修改 marker：

```csharp
manager.InsertEmoji("😀");
manager.InsertImage(bitmap, "photo.png");
manager.InsertContent(RichTextContent.FromCustom(
    "Click me", value: "DemoButton", styleKey: "demo-button"));
```

manager 会按当前选区替换规则插入内容，创建 marker 和 anchor，刷新 visual line，并记录撤销操作。删除 item 时只删除它自己的 marker 和业务内容；Undo/Redo 会恢复相同 item，不会误改相邻 emoji 或控件。

批量删除时应先取得快照，再按 offset 从后向前删除：

```csharp
foreach (var item in manager.GetItemsInDocumentOrder()
    .Where(item => item.Content.StyleKey == "demo-button")
    .OrderByDescending(item => item.Offset)
    .ToArray())
{
    manager.RemoveContent(item);
}
```

从文档尾部开始删除，可以避免前面的删除使尚未处理的 offset 失效。

## 4. 交互式子控件的事件处理

每个富内容会放入由 manager 管理的 wrapper 中，由 wrapper 负责编辑器选中和 pointer 路由。真实的 Avalonia `Button` 可能在事件到达 wrapper 前就把 pointer 事件标记为 handled，因此 wrapper 对 pressed、released、double-tap 和 context-requested 事件使用 `handledEventsToo: true` 监听。

这样点击 Button 后再按 Backspace/Delete，仍能正确选中并删除对应 marker。wrapper 脱离视觉树时也会移除这些 routed-event handler，避免旧视觉树和对象被错误保留。

## 5. 垂直对齐规则

这里有两个不同的坐标系：

- **相对整行对齐**（`Top`、`Center`、`Bottom`）：对象相对于完整 line box 定位，适合明确希望跟随整行高度的内容。
- **Baseline 对齐**：对象相对于文本 baseline 定位，适合文字型 chip、按钮、emoji 和其他小型 inline 控件。

同一行的高图片或卡片可能把 line box 撑得很高。因此，如果把小按钮配置为 `Center`，它出现在高图片的垂直中间是符合该配置的结果，并不是 renderer 的计算错误。

Demo 现在对 `Click me` 使用 `Bottom`：

```csharp
manager.InlineObjectAlignmentSelector = item =>
    item.Content.StyleKey == "demo-button"
        ? InlineObjectVerticalAlignment.Bottom
        : manager.InlineObjectAlignment;
```

这是刻意的设计：按钮位于完整 line box 的底部。renderer 会把文字、光标和 IME preedit 放在同一行的文字内容区域底部，避免高 inline 对象把它们错误地推到行顶。图片仍然可以按业务需要使用 `Bottom`、`Center` 或其他对齐方式。

line box 和文字内容区域是分开计算的。高图片或高按钮可以撑高 line box，但不能改变普通文字的 baseline，也不能让光标向上移动。`Baseline` 仍然可用于需要直接跟随 baseline 的小型文字控件；显式的 `Top`、`Center`、`Bottom` 都是相对于完整 line box 的对齐。

renderer 还会把对象限制在当前 line box 内。高度超过 line 的大对象会从当前行起始位置布局，不会侵入相邻行。

## 6. Demo 手工验证矩阵

1. 在一行粘贴高图片，再点击 **Add control**；按钮应位于整行底部，同时文字和光标仍位于文字内容区域底部。
2. 按 **Enter** 后单独添加按钮，确认单独一行布局正常。
3. 选中按钮按 Backspace 或 Delete；只能删除按钮。
4. 在按钮前后插入 emoji，删除按钮后确认两个 emoji 顺序和显示都不变。
5. 添加多个按钮，点击 **Clear controls**；emoji 和图片必须保留。
6. 对插入和删除分别测试 Undo/Redo。
7. 在富内容前后进行中文/日文 IME 输入；preedit 不应变成富内容 marker。
8. 粘贴/拖放图片和文件，再在同一进程内复制粘贴，确认平台支持时富内容元数据仍能保留。

## 7. 自动化测试

布局回归测试：

```bash
dotnet test test/AvaloniaEdit.Tests/AvaloniaEdit.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~Inline_Object_Baseline_Remains_Text_Aligned_When_Tall_Sibling_Expands_Line
```

测试构造了“高对象 + 小型 baseline 对齐对象”的同一行，并验证小对象使用 baseline 公式，而不是使用整行 center 公式。混合高度回归测试还验证：底部对齐的按钮和高对象共享行底部，而普通文字、光标几何位置以及 IME preedit baseline 仍位于文字内容区域内。富输入测试还覆盖 marker 删除、emoji 保留、Backspace/Delete 和 anchor 移动。

## 8. 版本兼容

- `ava12-feat`：Avalonia 12.0.0，库目标为 `net8.0`/`net10.0`。
- `ava11-feat`：Avalonia 11.0.10 兼容分支，库目标为 `netstandard2.0`/`net6.0`。

两个分支的富输入公共概念相同，差异主要在 Avalonia API 和目标框架。
