# IME Preedit（输入法组合文本）指南

本文档说明 `AvaloniaEdit.RichTextInput` 中 IME preedit/composition 的完整实现，包括中文、日文、韩文以及其他需要“组合—确认”输入流程的输入法。

本文档适用于当前 **Avalonia 11 分支**：

- Avalonia：`11.0.10`
- 目标框架：`.NET Standard 2.0`、`.NET 6`
- 相关分支：`ava11-feat`

Avalonia 11 分支的 API 和行为保持一致，版本差异请参阅本文最后的[版本兼容性](#版本兼容性)章节。

## 1. 为什么需要 preedit

输入法通常不会在用户每次按键时立即把文字写入文档。例如，用户输入日文 `かな`、中文拼音 `nihao` 或韩文组合字符时，输入法会先产生一段临时文本：

1. 输入法发送 preedit 文本和组合光标位置；
2. 编辑器只显示这段临时文本，不修改 `TextDocument`；
3. 用户选择候选词或确认后，输入法发送 commit 文本；
4. 编辑器把 commit 文本作为普通 `TextInput` 插入文档。

如果编辑器忽略第 1 步，用户在确认前就看不到自己正在输入的内容；如果错误地把 preedit 直接写入文档，又会导致文档长度、撤销栈、复制内容和最终 commit 重复。

本实现把 preedit 作为编辑器视觉层和输入法客户端状态的一部分处理，而不是把它当作普通文档字符。

## 2. 能力总览

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| Inline preedit | 已实现 | preedit 参与当前 visual line 布局，但不进入文档 |
| Overlay preedit | 已实现 | preedit 在 caret 附近独立绘制，不推动正文 |
| Hidden preedit | 已实现 | 保留 IME 状态，但不显示组合文本 |
| preedit cursor | 已实现 | 支持输入法提供的 UTF-16 cursor offset，缺省时位于末尾 |
| 普通整段 underline | 已实现 | 原生 Avalonia IME 没有 clause metadata 时使用 Solid underline |
| per-clause underline | 已实现 | 通过 AvaloniaEdit 扩展 API 提供 clause 范围、样式和 brush |
| clause 跨行/跨 chunk | 已实现 | Overlay 换行时会把 clause 切分到对应 chunk |
| 点击时在旧 caret 提交 | 已实现 | PointerPressed tunnel 阶段处理，避免 commit 落到新 caret |
| 同步/异步 reset 兼容 | 已实现 | 支持 reset 回调同步 commit 和平台延迟重复 commit |
| Overlay 右侧换行 | 已实现 | 按文本元素/grapheme 安全切分，不拆 surrogate pair |
| Overlay 背景填充 | 已实现 | 避免 preedit 后面的正文透出 |
| preedit baseline 对齐 | 已实现 | 优先使用 caret visual line baseline |
| 横向滚动预留空间 | 已实现 | 默认预留 10 个 full-width character 宽度 |
| 长 commit 延迟滚动 | 已实现 | layout 更新后再次执行 `BringCaretToView` |
| Escape 清理 | 已实现 | 取消组合后清理视觉层和输入法状态 |
| LostFocus 清理 | 已实现 | 焦点离开编辑器时清理 preedit |
| 文档替换清理 | 已实现 | 更换 `TextDocument` 时清理旧组合 |
| read-only 保护 | 已实现 | 只读 TextArea 不暴露 IME client |
| child TextBox 隔离 | 已实现 | SearchPanel 等子输入框不会触发编辑器旧 preedit 提交 |

## 3. 最小使用方式

正常使用系统输入法时，业务代码通常不需要手工调用 `SetImePreeditText`。`TextArea` 会通过 Avalonia 的 `TextInputMethodClientRequested` 机制自动成为 IME client：

```csharp
using AvaloniaEdit;
using AvaloniaEdit.Editing;

var editor = new TextEditor
{
    Text = string.Empty
};

// 默认值就是 Inline。
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
```

只要 `TextArea` 可编辑并且获得焦点，Windows IME、macOS 输入法、fcitx/mozc 等平台输入法即可通过 Avalonia 的原生 IME 路径更新 preedit。

如果需要在自动化测试、演示程序或业务自定义输入法中模拟组合文本，可以调用：

```csharp
editor.TextArea.SetImePreeditText("かな", cursorOffset: 2);

// preedit 仍然没有写入 editor.Document。
var isComposing = editor.TextArea.HasImePreedit;

editor.TextArea.ClearImePreedit();
```

## 4. Preedit 生命周期

### 4.1 开始或更新组合

输入法调用 Avalonia 的 `TextInputMethodClient.SetPreeditText`。本项目中的 `TextAreaTextInputMethodClient` 保存：

- 当前 preedit 字符串；
- 可选 cursor offset；
- 可选 clause 列表；
- surrounding text；
- 当前 selection；
- candidate window 所需的 caret rectangle 和 TextView visual。

随后根据 `ImePreeditDisplayMode` 更新 Inline generator 或 Overlay layer。

### 4.2 组合期间

组合文本只存在于以下状态中：

- `TextAreaTextInputMethodClient` 的 preedit 状态；
- Inline 模式的 `PreeditTextElementGenerator`；
- Overlay 模式的 `PreeditLayer`。

它不会进入：

- `TextDocument.Text`；
- 文档版本和文档 undo stack；
- 普通复制/序列化内容；
- 富内容锚点或 inline object 集合。

### 4.3 确认 commit

输入法发送 `TextInput` 后，`TextArea` 会：

1. 清除当前 preedit 视觉状态；
2. 走原有 `PerformTextInput` / selection replacement 路径；
3. 触发 `TextEntering`、文本替换和 `TextEntered`；
4. 立即调用 `Caret.BringCaretToView()`；
5. 在 `DispatcherPriority.Loaded` 再调用一次，等待新的 extent/layout 生效。

因此，应用层不需要把 preedit 再手工插入一次。

### 4.4 取消和清理

以下情况会清理 preedit：

- 输入法发送空 preedit；
- 按下 `Escape`；
- TextArea 失去焦点；
- TextArea 被切换到新的 `TextDocument`；
- commit 文本到达；
- 应用主动调用 `ClearImePreedit()`；
- IME client 从一个 TextArea 切换到另一个 TextArea。

## 5. 显示模式

### 5.1 Inline（默认）

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
```

Inline 模式把 preedit 作为临时 visual line element 放在 caret 处：

- 会推动 caret 后面的正文布局；
- 会跟随正常的 visual line、换行和文本测量；
- 组合文本位置与最终 commit 后的正文位置一致；
- 不会修改文档；
- 编辑器 caret 在组合期间隐藏，由 preedit text run 绘制组合 cursor。

这是推荐的默认模式，尤其适合普通文本框、聊天输入框以及需要与正文严格对齐的编辑器。

### 5.2 Overlay

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
```

Overlay 模式使用独立的 `PreeditLayer`：

- 不推动正文；
- 从 caret 的 `X` 坐标开始，而不是 `caret.Right`；
- 每个 chunk 独立测量和绘制；
- 超出右边界后换到下一行；
- 绘制前填充可找到的编辑器背景；
- 使用 caret 所在 visual line 的 baseline 对齐正文；
- cursor、underline 和背景都在 overlay layer 中绘制。

Overlay 适合需要保持正文布局不变、或者业务希望完全控制组合文本绘制的场景。由于 Overlay 不参与正文 visual line 的布局，应用应同时配置横向滚动预留空间。

### 5.3 Hidden

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Hidden;
```

Hidden 模式仍然接受并保存 preedit，但不渲染组合文本。commit、Escape、焦点变化和文档替换仍按正常生命周期处理。

该模式适合：

- 自己在外部绘制组合文本；
- 暂时屏蔽组合文本视觉显示；
- 只需要 IME client 的 surrounding text/selection 行为。

## 6. 公开 API

### 6.1 `TextArea.SetImePreeditText`

```csharp
public void SetImePreeditText(
    string text,
    int? cursorOffset = null,
    IReadOnlyList<ImePreeditClause> clauses = null);
```

参数说明：

- `text`：preedit 文本。传入 `null` 或空字符串等价于清理 preedit；
- `cursorOffset`：UTF-16 offset，范围会被限制在 `[0, text.Length]`；传 `null` 时默认位于末尾；
- `clauses`：可选的 clause underline 描述。原生 Avalonia IME 通知目前不包含这部分 metadata，因此此参数是 AvaloniaEdit 层扩展。

示例：

```csharp
editor.TextArea.SetImePreeditText(
    text: "日本語入力",
    cursorOffset: 4);
```

### 6.2 `TextArea.HasImePreedit`

```csharp
if (editor.TextArea.HasImePreedit)
{
    // 当前仍处于 IME composition 阶段。
}
```

该属性表示是否存在非空 preedit，不表示文档中是否包含已确认文字。

### 6.3 `TextArea.ClearImePreedit`

```csharp
editor.TextArea.ClearImePreedit();
```

只清除 preedit 状态和视觉内容，不修改文档，不触发普通文本插入。

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

支持的 underline style：

- `Solid`：单实线；
- `None`：不绘制 underline；
- `Thick`：较粗实线，通常用于当前活动 clause；
- `Double`：双实线；
- `Dotted`：点线；
- `Dashed`：虚线。

规则：

- `start` 和 `length` 必须是非负数；
- 范围使用 UTF-16 offset，而不是 Unicode scalar 或屏幕 glyph 数量；
- clause 不得越界；
- clause 不得重叠；
- clause 可以不按顺序传入，内部会按 `start` 排序；
- 未被 clause 覆盖的间隔自动补成 `Solid`；
- 空 clause 会被忽略；
- `underlineBrush == null` 时使用 preedit foreground。

由于 UTF-16 的原因，包含 surrogate pair 的文本必须按 .NET `string` 的 offset 计算。例如：

```csharp
var text = "A😀B";
// A = 0..1，😀 = 1..3，B = 3..4。
var emojiClause = new ImePreeditClause(1, 2);
```

## 7. 横向滚动预留

长行末尾的 Overlay preedit 不在文档 extent 中，因此需要在 caret 到达 viewport 边缘前预留可见空间：

```csharp
editor.Options.ImePreeditHorizontalScrollCharCount = 10;
```

默认值是 `10`。实际预留宽度约为：

```text
TextView.WideSpaceWidth * ImePreeditHorizontalScrollCharCount
```

同时会影响：

- `TextView` 测量出的 horizontal extent；
- `Caret.BringCaretToView()` 传给 `TextView.MakeVisible()` 的矩形。

如果应用不需要预留空间：

```csharp
editor.Options.ImePreeditHorizontalScrollCharCount = 0;
```

不允许负数：

```csharp
// 抛出 ArgumentOutOfRangeException。
editor.Options.ImePreeditHorizontalScrollCharCount = -1;
```

建议：

- 普通 CJK 文本输入可保留默认值 `10`；
- 固定宽度的窄输入框可以降低到 `4` 或 `5`；
- Overlay 被禁用、且只使用 Inline 时可以设置为 `0`；
- 如果应用在运行时修改此选项，TextView 会清理 visual lines 并重新测量。

## 8. 点击、reset 和重复 commit

输入法 API 通常只保证在 `ResetRequested` 或 IME client 变化时重置组合状态，并不保证 caret 移动时自动结束 composition。这样会出现危险路径：

1. preedit 显示在旧 caret；
2. 用户点击编辑器其他位置；
3. caret 先移动到新位置；
4. 输入法之后 commit；
5. commit 错误地插入新 caret。

本实现通过 `PointerPressed` tunnel handler 在 `SelectionMouseHandler` 移动 caret 之前处理：

1. 记录旧 caret 的 preedit；
2. 请求 IME reset；
3. 如果 reset 同步产生 commit，使用输入法已经提交的结果；
4. 如果 reset 没有产生 commit，则主动在旧 caret 执行一次 `PerformTextInput(preedit)`；
5. 对平台稍后发送的同一段重复 commit 做一次精确匹配抑制；
6. 清理 preedit 状态；
7. 继续原来的点击处理，允许 caret 移动。

只有编辑器自己的 TextView/visual subtree 会触发这套逻辑。SearchPanel、CompletionWindow 或其他 child TextBox 不会被当成编辑器点击处理。

## 9. IME client 行为

TextArea 的内部 `TextAreaTextInputMethodClient` 提供：

- `SupportsPreedit = true`；
- `SupportsSurroundingText = true`；
- `TextViewVisual`；
- 当前 caret `CursorRectangle`；
- 当前行 surrounding text；
- 当前行内 selection 范围。

selection 给 IME 的 offset 是相对于当前行的 offset，并且会被限制在当前行范围内。这样可以避免跨行 selection 或文档边界导致平台 IME 收到非法值。

只有可编辑 TextArea 才会响应 `TextInputMethodClientRequested`。当 `ReadOnlySectionProvider` 表示整个编辑器只读时，TextArea 不会把自己的 client 暴露给系统输入法。

## 10. Overlay 渲染实现

核心文件：

```text
src/AvaloniaEdit/Rendering/PreeditLayer.cs
src/AvaloniaEdit/Rendering/PreeditDecorationRenderer.cs
```

渲染流程：

1. 读取 caret rectangle 和 TextView scroll offset；
2. 读取 caret 所在 visual line 的 line top、line height、baseline；
3. 从 `caretRect.X - HorizontalOffset` 开始布局；
4. 使用 `TextLayout` 测量当前可用宽度；
5. 根据 `StringInfo.ParseCombiningCharacters` 选择安全的 text element 边界；
6. 以 NoWrap layout 绘制当前 chunk；
7. 填充背景、绘制文字、绘制 clause underline；
8. 在 cursor offset 所在 chunk 绘制 cursor；
9. 移动到下一行继续处理剩余文本；
10. 输出测试诊断信息，例如 chunk 数量、chunk 起点、chunk 长度、cursor 数量和背景填充数量。

换行不会拆开：

- UTF-16 surrogate pair，例如 emoji；
- combining mark，例如 `e01`；
- .NET text element 所表示的组合字符序列。

如果 visual line 尚未完成布局，渲染会回退到 caret rectangle，不会在 Render 阶段强制构造新的 visual line，避免布局重入。

## 11. Inline 渲染实现

核心文件：

```text
src/AvaloniaEdit/Rendering/PreeditTextElementGenerator.cs
```

Inline generator 只在 caret offset 处构造一个临时 `PreeditTextElement`。该元素：

- 使用 `VisualLineElement` 的长度占位机制推动后续正文；
- 生成一个 `PreeditTextRun`；
- 使用 `TextLayout` 绘制 preedit；
- 在 cursor prefix 的测量宽度处绘制 cursor；
- 复用 clause decoration renderer 绘制 underline。

由于元素长度是布局占位而不是文档字符长度，preedit 不会改变 `Document.TextLength`、文档 offset 或 undo history。

## 12. 应用层建议

### 推荐配置

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

### 状态栏显示 composition 状态

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

如果需要精确监听 native preedit 的每次变化，应用可以在自己的 `TextInputMethodClient` 集成层观察 Avalonia client 更新；普通应用只需要使用 `HasImePreedit` 和 TextArea 的文本事件即可。

### 不要把 preedit 当作文档文本

不要在以下事件中手工把 preedit 插入 `Document`：

- IME client 更新 preedit 时；
- `TextArea.HasImePreedit` 变为 `true` 时；
- Overlay layer 重绘时。

只有 commit 的 `TextInput` 才应该走 `PerformTextInput` 或普通 TextArea 输入路径。

## 13. 常见问题

### Q1：为什么我调用 `SetImePreeditText` 后 `Document.Text` 没有变化？

这是预期行为。preedit 是临时组合状态，只有 commit 文本到达后才会进入文档。

### Q2：为什么未提供 `cursorOffset` 时 cursor 在末尾？

`cursorOffset == null` 被定义为 preedit 文本末尾，兼容只提供 `SetPreeditText(string)` 的平台或调用方。

### Q3：clause 的 offset 应该按 emoji 算一个字符还是两个字符？

按 UTF-16 计算。一个基本 emoji 可能占用两个 UTF-16 code unit，详见 [ImePreeditClause](#64-imepreeditclause)。

### Q4：为什么 SearchPanel 输入框不会提交编辑器 preedit？

PointerPressed tunnel handler 会检查 source 是否属于 TextArea 自己的 TextView visual subtree。外部或子输入控件不会被误判为编辑器表面。

### Q5：Overlay 的背景为什么可能没有填充？

实现会沿 TextArea 的 visual parent 查找非透明 `Background`。如果整个 visual tree 都没有可用背景，无法推断应填充的颜色，此时只绘制 preedit 本身。需要可靠遮挡时，请给 TextArea 或其容器设置不透明背景。

### Q6：只读编辑器为什么无法激活输入法？

只读 TextArea 不会提供 IME client，避免输入法向不可编辑控件提交文本。这是设计行为，不是平台故障。

## 14. 测试与手工验证

### 自动化测试

IME 专用测试：

```bash
dotnet test test/AvaloniaEdit.Tests/AvaloniaEdit.Tests.csproj \
  --no-build --no-restore \
  --filter FullyQualifiedName~ImePreeditTests
```

当前覆盖内容包括：

- 文档不被 preedit 修改；
- Inline cursor 和 clause normalization；
- 默认 cursor 位于末尾；
- 越界/重叠 clause 拒绝；
- wrapped chunk 的 clause 切片；
- Overlay 使用 caret X；
- Overlay 换行和背景填充；
- grapheme/surrogate pair 安全切分；
- baseline 读取；
- 横向 extent 预留；
- 点击旧 caret commit 和重复 commit 抑制；
- reset 同步 commit；
- 不匹配 commit 不被吞掉；
- read-only IME client 隔离；
- 文档替换清理。

完整测试：

```bash
dotnet test AvaloniaEdit.slnx --no-build --no-restore
```

### Demo

```bash
dotnet run --project src/AvaloniaEdit.Demo/AvaloniaEdit.Demo.csproj
```

启动后建议按以下顺序验证：

1. 在编辑器中输入中文拼音，观察 Inline preedit 和 cursor；
2. 使用日文 Romaji 输入法进行长句转换；
3. 在组合期间点击同一编辑器的其他位置，确认文本不会落到错误 caret；
4. 将 `ImePreeditDisplayMode` 改为 `Overlay`，验证中间位置覆盖和右边换行；
5. 在长行末尾进行日文转换，观察横向滚动和 commit 后 caret；
6. 按 Escape，确认组合消失且文档未增加取消的文本；
7. 打开 SearchPanel，在搜索框中输入，确认不会提交编辑器的 preedit；
8. 替换 `TextDocument` 或让编辑器失焦，确认 preedit 被清理。

## 15. 源码结构

| 文件 | 责任 |
| --- | --- |
| `Editing/TextArea.cs` | IME client、生命周期、输入处理、点击前 commit、延迟滚动 |
| `Editing/ImePreeditClause.cs` | clause 数据类型、校验和自动补全 |
| `Editing/Caret.cs` | caret 可见矩形和 IME 横向预留 |
| `Rendering/PreeditTextElementGenerator.cs` | Inline preedit visual line element/run |
| `Rendering/PreeditLayer.cs` | Overlay preedit 分 chunk 布局和绘制 |
| `Rendering/PreeditDecorationRenderer.cs` | underline、brush 和 clause 绘制 |
| `Rendering/TextView.cs` | horizontal extent 中的 preedit 预留 |
| `TextEditorOptions.cs` | `ImePreeditHorizontalScrollCharCount` |
| `test/AvaloniaEdit.Tests/Editing/ImePreeditTests.cs` | IME 单元和 headless rendering 测试 |

## 16. 版本兼容性

### Avalonia 12

Avalonia 12 分支使用：

```text
Avalonia 12.0.0
TargetFramework: net8.0; net10.0
Solution: AvaloniaEdit.slnx
```

构建：

```bash
dotnet build AvaloniaEdit.slnx --no-restore
```

### Avalonia 11

当前分支使用：

```text
Avalonia 11.0.10
TargetFramework: netstandard2.0; net6.0
Solution: AvaloniaEdit.sln
```

构建：

```bash
dotnet build AvaloniaEdit.sln --no-restore
```

Avalonia 11.0.10 是有意选择的最低版本，因为完整的旧 caret commit 兼容逻辑依赖：

- `SetPreeditText(string, int?)`；
- `RequestReset()`；
- `ResetRequested`。

Avalonia 11.0.0 的 IME API 不足以实现同步/异步 reset 和点击前提交的完整行为。

## 17. 已知边界

1. Avalonia 原生 `TextInputMethodClient` 只携带 preedit text 和 cursor offset，不携带 per-clause metadata。clause 支持因此是 AvaloniaEdit 的扩展 API，需要应用层或自定义 IME integration 提供 clause 数据。
2. Overlay 的背景填充依赖 visual tree 中可发现的非透明背景；透明编辑器无法自动猜测正文下方应该使用的颜色。
3. 这套实现解决的是 TextArea 文本 IME composition。富内容 inline object 的业务语义仍由 `RichTextInputManager` 负责。
4. preedit 不参与文档复制、序列化和 undo；如果业务需要保存未确认输入，应额外保存 `HasImePreedit` 对应的外部状态。

## 18. 相关文档

- [RichTextInput 总体 API 指南](RichTextInput.md)
- [English IME Preedit Guide](IME-Preedit.en-US.md)
- [AvaloniaEdit PR #592](https://github.com/AvaloniaUI/AvaloniaEdit/pull/592)
