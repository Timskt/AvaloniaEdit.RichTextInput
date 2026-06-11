# RichTextInput 使用说明

`RichTextInputManager` 给 `TextArea` 增加类似聊天输入框的富内容能力：文本、emoji、图片、文件卡片和业务自定义控件可以混排、复制粘贴、拖放、选中删除，并且保留普通文本降级能力。

## 最小接入

```csharp
using AvaloniaEdit.RichTextInput;

var richInput = RichTextInputManager.Install(editor.TextArea);
```

安装后会自动接管富内容拖放和粘贴。普通文本输入、中文 IME、撤销重做、普通复制粘贴仍走原有编辑器路径。

## 插入内容

```csharp
richInput.InsertEmoji("😀");
richInput.InsertImage(bitmap, "screenshot.png");
richInput.InsertFile(file);
richInput.InsertFileName("/tmp/report.pdf");
richInput.InsertCustom("order-card", order);
```

富内容在文档中用 `\uFFFC` 占位。业务数据保存在 `RichTextContentItem.Content` 中，可以通过 `GetItemsInDocumentOrder()` 按文档顺序读取。

## 自定义渲染

```csharp
richInput.ElementFactory = item =>
{
    if (item.Content.Kind == RichTextContentKind.File)
        return new MyFileChip(item.Content.DisplayText);

    if (item.Content.Kind == RichTextContentKind.Custom)
        return new MyBusinessCard(item.Content.Value);

    return RichTextInputManager.CreateDefaultElement(item);
};
```

`ElementFactory` 只负责返回业务控件。默认外层 wrapper 负责选中、事件、高亮和删除，所以自定义控件不用自己处理编辑器 selection。

如果业务里有很多种组件样式，不建议在一个 `ElementFactory` 里写很长的 `if/else`。推荐给内容指定 `StyleKey`，然后按 key 注册工厂：

```csharp
richInput.RegisterElementFactory("order-card", context =>
    new OrderCardView
    {
        DataContext = context.Content.Value,
        MaxWidth = context.AvailableWidth
    });

richInput.RegisterElementFactory("mention-user", context =>
    new MentionUserChip((User)context.Content.Value));

richInput.InsertCustom(
    displayText: "订单 A001",
    value: order,
    styleKey: "order-card",
    metadata: new Dictionary<string, object>
    {
        ["status"] = "paid",
        ["compact"] = true
    });
```

需要全局接管时，用上下文工厂：

```csharp
richInput.ElementFactoryWithContext = context =>
{
    if (context.Metadata.TryGetValue("compact", out var compact) && compact is true)
        return new CompactCard(context.Content.Value);

    return null; // 返回 null 时继续走 StyleKey 注册工厂、ElementFactory、默认渲染。
};
```

`RichTextElementFactoryContext` 提供：

- `Manager`、`TextArea`
- `Item`、`Content`
- `StyleKey`、`Metadata`
- `AvailableWidth`
- `MaxImageWidth`、`MaxImageHeight`
- `IsSelected`

`RichTextContentItem.Tag` 可以放运行时状态，例如上传进度、临时错误信息或 UI 缓存对象。

## 对齐配置

默认是底部对齐，适合一行里同时存在文字、图片和卡片的聊天输入场景。

```csharp
// 控制文字在被高内容撑高后的行内位置。
richInput.LineContentAlignment = LineContentVerticalAlignment.Bottom;

// 控制所有富内容控件的 inline 对齐。
richInput.InlineObjectAlignment = InlineObjectVerticalAlignment.Bottom;

// 按 item 细分，比如图片底部、文件卡片居中。
richInput.InlineObjectAlignmentSelector = item =>
    item.Content.Kind == RichTextContentKind.Image
        ? InlineObjectVerticalAlignment.Bottom
        : InlineObjectVerticalAlignment.Center;
```

IME 确认、删除文字、插入图片时，同一行的图片/卡片位置可能会变化。需要更接近聊天输入框的顺滑体验时，可以打开 inline object 位置过渡：

```csharp
editor.TextArea.TextView.AnimateInlineObjectPlacement = true;
editor.TextArea.TextView.InlineObjectPlacementAnimationDuration = TimeSpan.FromMilliseconds(260);
editor.TextArea.TextView.InlineObjectPlacementAnimationMinimumDistance = 2;
editor.TextArea.TextView.InlineObjectPlacementAnimationEasing =
    InlineObjectPlacementAnimationEasing.SmootherStep;
editor.TextArea.TextView.InlineObjectPlacementAnimationRetargetDurationMultiplier = 1.2;
```

## 中文 IME

默认使用 QQ 输入框式的 inline composition。输入法组合阶段的拼音、字母或候选前文本会作为临时 visual element 插入到光标位置，占住同一行布局，但不会写入 `TextDocument`、不会进入 undo stack，也不会参与复制/序列化。输入法确认后，提交文本仍走 AvaloniaEdit 原有 `TextInput` 路径。

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline; // 默认
```

如果业务确实需要旧的浮层绘制或完全隐藏 composition 显示，可以切换模式：

```csharp
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
editor.TextArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Hidden;
```

## 选择样式

默认选择态只描边，不铺大块蓝色背景。`SuppressTextSelectionBackgroundForRichContent` 默认是 `true`，选中图片、文件、card 等富内容时会过滤掉对象占位符的普通文本 selection 背景，只保留富内容 wrapper 自己的选中样式。

需要恢复旧式整块文本 selection 背景时关闭它：

```csharp
richInput.SuppressTextSelectionBackgroundForRichContent = false;
```

需要完全自定义富内容选中态时使用 `InlineContentStyleSelector`：

```csharp
richInput.InlineContentStyleSelector = (item, selected) => new RichTextInlineContentStyle
{
    Background = selected ? Brushes.Transparent : Brushes.Transparent,
    BorderBrush = selected ? Brushes.DodgerBlue : Brushes.Transparent,
    BorderThickness = new Thickness(selected ? 1 : 0),
    CornerRadius = new CornerRadius(6),
    Padding = new Thickness(0)
};
```

如果想让业务控件自己显示选中态，可以关闭默认高亮：

```csharp
richInput.HighlightSelectedContent = false;
```

## 交互事件

```csharp
richInput.ContentPointerPressed += (_, e) =>
{
    // 默认行为是单击选中。设置 Handled=true 可接管。
    var point = e.TryGetPosition(editor.TextArea.TextView, out var p) ? p : default;
    var selectedPlainText = e.GetSelectedPlainText(item => item.Content.DisplayText);
};

richInput.ContentDoubleTapped += (_, e) => OpenPreview(e.Item);
richInput.ContentContextRequested += (_, e) =>
{
    var selectedItems = e.GetSelectedItems();
    var selectedValue = e.GetSelectionValue();
    ShowMenu(e.Item, selectedItems, selectedValue);
    e.Handled = true;
};

richInput.ContentRemoving += (_, e) =>
{
    if (IsUploading(e.Item))
        e.Cancel = true;
};
```

普通文本选择默认继续走 AvaloniaEdit 原生鼠标选择；富内容 wrapper 默认负责图片、文件、card 等对象本身的点击、右键和双击。这个默认行为不是固定的，业务可以按对象、鼠标按钮、修饰键和事件类型细分：

```csharp
richInput.ContentPointerSelectionBehaviorSelector = e =>
{
    if (e.EventKind != RichTextContentPointerEventKind.PointerPressed)
        return RichTextContentPointerSelectionBehavior.None;

    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        return RichTextContentPointerSelectionBehavior.ExtendSelection;

    if (e.Item.Content.Kind == RichTextContentKind.Image)
        return RichTextContentPointerSelectionBehavior.SelectContent;

    // 例如 card 点击时保持已有文本选区，让业务自己处理。
    return RichTextContentPointerSelectionBehavior.None;
};

// 返回 false 时允许事件继续冒泡给底层 TextArea，适合做拖拽框选或完全接管 native selection。
richInput.ContentPointerHandledSelector = e =>
    e.Item.Content.Kind == RichTextContentKind.Image;

// 只是不想自动选中，但仍要保留点击/右键事件：
richInput.SelectContentOnPointerPressed = false;

// 完全不包 wrapper，不要富内容点击、选择、右键、双击事件：
richInput.EnableContentPointerInteractions = false;
```

右键点中富内容时可以通过 `ContentContextRequested` 拿到当前对象，也可以通过 `GetSelectedItems()`、`GetSelectionValue()`、`GetSelectedPlainText()` 拿到用户已经选中的局部数据，用于删除、转发、复制、邮件或业务菜单。

当富内容被单独选中时，默认按 Enter 会在该内容后插入换行，并保留图片、文件或 card 本身，避免把对象占位符替换成裸 `\uFFFC` 字符。这个行为也可以配置：

```csharp
richInput.SelectedContentEnterBehavior =
    RichTextSelectedContentEnterBehavior.InsertNewLineAfterContent; // 默认

richInput.SelectedContentEnterBehavior =
    RichTextSelectedContentEnterBehavior.MoveCaretAfterContent;

richInput.SelectedContentEnterBehavior =
    RichTextSelectedContentEnterBehavior.KeepDefault; // 完全交还给 TextArea 默认编辑逻辑
```

普通光标按 Enter 的行为也可以配置。IM/聊天输入框通常希望回车只插入一个纯换行，不继承代码编辑器的自动缩进；代码编辑器里嵌富内容时可以保留默认逻辑：

```csharp
// 聊天输入框推荐：普通文本、图片、card 前后回车都直接换行，下一行顶格。
richInput.EnterKeyBehavior = RichTextEnterKeyBehavior.PlainNewLine;

// 默认：普通文本仍走 TextArea/C# 缩进逻辑；紧贴图片、文件、card 时由富输入接管，避免对象丢失。
richInput.EnterKeyBehavior = RichTextEnterKeyBehavior.PlainNewLineWhenAdjacentToContent;

// 完全交回 AvaloniaEdit 原生 Enter。
richInput.EnterKeyBehavior = RichTextEnterKeyBehavior.KeepDefault;
```

manager 上也提供同名能力，适合工具栏按钮或外部菜单使用：

```csharp
var value = richInput.GetSelectionValue();
var snapshot = richInput.GetSelectionSnapshot();
var text = richInput.GetSelectedPlainText(item => $"[{item.Content.DisplayText}]");
var items = richInput.GetSelectedItems();
```

## 当前行样式

基础样式可以直接设置：

```csharp
editor.TextArea.TextView.CurrentLineBackground = Brushes.Transparent;
editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.DodgerBlue, 1);
```

需要自定义激活行矩形的圆角、边距、宽度或按行号动态变化时，使用 selector：

```csharp
editor.TextArea.TextView.CurrentLineHighlightStyleSelector = context =>
    new CurrentLineHighlightStyle
    {
        BackgroundBrush = new SolidColorBrush(Color.FromArgb(28, 59, 130, 246)),
        BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(72, 37, 99, 235)), 1),
        CornerRadius = new CornerRadius(4),
        Margin = new Thickness(1),
        ExtendToViewportWidth = true
    };
```

## @ 人和指令弹窗

触发逻辑建议放在业务层：按键事件只用来“提前感知用户按了 @”，真正判断统一基于 caret 前面的文档文本。这样键盘输入、中文/日文/韩文 IME 提交、粘贴、拖放、程序插入文本以后，都可以调用同一个 `UpdateMentionPopup()`。

```csharp
var mentionStart = -1;

editor.TextArea.TextEntered += (_, e) =>
    UpdateMentionPopup();

editor.TextArea.KeyDown += (_, e) =>
{
    if (e.KeySymbol == "@")
        OpenEmptyMentionPopupEarly();
};

// 在 PasteHandler、DropHandler 或业务主动插入文本后，也调用 UpdateMentionPopup()。

void UpdateMentionPopup()
{
    if (!richInput.TryGetTextTriggerRange('@', out mentionStart, out var query))
    {
        mentionPopup.IsOpen = false;
        return;
    }

    mentionList.ItemsSource = SearchMembers(query)
        .Prepend(Member.All); // @全体成员

    var anchor = richInput.GetCaretAnchorRect();
    mentionPopup.HorizontalOffset = anchor.X;
    mentionPopup.VerticalOffset = anchor.Bottom;
    mentionPopup.IsOpen = true;
}

void CommitMention(Member member)
{
    var caret = editor.TextArea.Caret.Offset;
    if (mentionStart < 0 || caret < mentionStart)
        return;

    richInput.ReplaceRangeWithContent(
        mentionStart,
        caret - mentionStart,
        RichTextContent.FromCustom("@" + member.DisplayName, member, "mention-user"));

    mentionPopup.IsOpen = false;
    mentionStart = -1;
}
```

需要更多控制时用 `TryGetTextTrigger`：

```csharp
if (richInput.TryGetTextTrigger(
    new RichTextTextTriggerOptions
    {
        Trigger = '@',
        MaxQueryLength = 32,
        AllowEmptyQuery = true,
        IsQueryCharacter = c => char.IsLetterOrDigit(c) || c == '_' || c == '-'
    },
    out var match))
{
    // match.TriggerOffset / match.CaretOffset / match.Query
}
```

如果用户从别处复制 `@张三` 粘进来，通常也应该触发候选逻辑；这和 QQ/IM 输入框的体验更一致。

## URL 和邮箱链接

发送框里建议先按普通文本输入，避免链接点击干扰编辑；消息发出后，在“会话消息展示框”里用同一份 `GetValue()` 或普通文本启用链接识别。普通 URL/邮箱可以直接使用 AvaloniaEdit 内置链接识别：

```csharp
editor.Options.EnableHyperlinks = true;
editor.Options.EnableEmailHyperlinks = true;
editor.Options.RequireControlModifierForHyperlinkClick = false;

editor.TextArea.TextView.LinkTextForegroundBrush = Brushes.DodgerBlue;
editor.TextArea.TextView.LinkTextBackgroundBrush = Brushes.Transparent;
editor.TextArea.TextView.LinkTextUnderline = true;
```

不同链接类型可以设置不同样式，也可以接管点击：

```csharp
messageView.TextArea.TextView.LinkTextStyleSelector = context =>
{
    if (context.LinkKind == "ip")
        return new LinkTextStyle
        {
            ForegroundBrush = Brushes.DarkOrange,
            BackgroundBrush = Brushes.Transparent,
            Underline = false
        };

    return new LinkTextStyle
    {
        ForegroundBrush = Brushes.DodgerBlue,
        BackgroundBrush = Brushes.Transparent,
        Underline = true
    };
};

messageView.TextArea.TextView.LinkTextClicked += (_, e) =>
{
    if (e.LinkKind == "ip")
    {
        OpenIpPanel(e.Text);
        e.Handled = true; // 阻止默认 OpenUri。
    }
};
```

IPv4 可以直接使用内置工厂：

```csharp
messageView.TextArea.TextView.ElementGenerators.Add(
    LinkElementGenerator.CreateIpAddressGenerator());
```

IP、工单号、内部协议等也可以注册自定义 `LinkElementGenerator`：

```csharp
messageView.TextArea.TextView.ElementGenerators.Add(new LinkElementGenerator(
    new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
    match => new Uri("im://ip/" + match.Value),
    linkKind: "ip"));
```

`LinkKind` 可以按业务扩展为 `url`、`email`、`ip`、`user`、`ticket`、`topic` 等。简单跳转用默认 `OpenUriEvent`，复杂行为用 `LinkTextClicked` 拦截。

## 粘贴和拖放导入

富输入框把粘贴/拖放分成三层，业务可以按需要选择接管深度：

| 层级 | API | 适用场景 |
| --- | --- | --- |
| 全量接管 | `PasteHandler` / `DropHandler` | 完全决定本次粘贴或拖放插入什么，适合订单卡片、富文本协议、上传前校验 |
| 数据导入 | `AsyncDataTransferImporter` / `DataTransferImporter` / `DataObjectImporter` | 把某类剪贴板数据统一转换为富内容，粘贴和拖放都复用 |
| 单文件接管 | `FileContentImporter` | 多选文件里逐个判断图片、文件夹、exe、zip、文档等，细粒度最高 |

默认行为：

- 普通文本：继续走 AvaloniaEdit 原生文本粘贴。
- 富输入框复制出来的数据：优先恢复富内容快照。
- 截图/bitmap：插入 `RichTextContentKind.Image`。
- 图片文件：`ConvertImageFilesToImages = true` 时尝试转成图片。
- 文件夹：插入 `RichTextContentKind.Folder`。
- 其他文件：插入 `RichTextContentKind.File`。
- `.exe`、压缩包、Office/PDF/文本等不会被执行或读取正文，只作为文件项进入输入框。
- 多选复制会按剪贴板顺序逐个插入。

### Ava12 接口

Ava12 使用 `IDataTransfer/IAsyncDataTransfer`。只想识别一类数据时，用 importer：

```csharp
// 通用导入：粘贴和拖放都会走这里。
richInput.CanImportAsyncDataTransfer = data => data.Contains(DataFormat.Text);
richInput.AsyncDataTransferImporter = async data =>
{
    var text = await data.TryGetTextAsync();
    return new[] { RichTextContent.FromCustom("custom-payload", text) };
};
```

只接管 Ctrl+V/粘贴时，用 `PasteHandler`：

```csharp
richInput.PasteHandler = async context =>
{
    // context.Formats 可以直接用于日志和业务分流。
    // context.ContainsFormat(...) 支持 DataFormat 或平台格式名。
    if (context.ContainsFormat("com.myapp.order-card"))
    {
        var payload = await context.DataTransfer.TryGetTextAsync();
        context.InsertContents(new[]
        {
            RichTextContent.FromCustom("订单卡片", ParseOrder(payload), "order-card")
        });
        return;
    }

    // 返回且不设置 Handled：继续默认处理。
    // 用 CanInsert 覆盖 Bitmap、Win DIB、文件和富内容快照等默认富内容。
    if (richInput.CanInsert(context.DataTransfer))
        return;

    var text = await context.DataTransfer.TryGetTextAsync();
    if (text?.StartsWith("order:", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.InsertContents(new[]
        {
            RichTextContent.FromCustom(text, new OrderPayload(text), "order-card")
        });
        return;
    }

    context.InsertText(text);
};
```

只接管拖放时，用 `DropHandler`：

```csharp
richInput.DropHandler = async context =>
{
    var files = context.DataTransfer.TryGetFiles();
    if (files == null)
        return;

    var contents = new List<RichTextContent>();
    foreach (var file in files)
    {
        if (file.Name.EndsWith(".fig", StringComparison.OrdinalIgnoreCase))
            contents.Add(RichTextContent.FromCustom(file.Name, file, "design-file"));
        else if (file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            contents.Add(RichTextContent.FromCustom(file.Name, file, "package-file"));
        else
            contents.Add(RichTextContent.FromFile(file));
    }

    context.InsertContents(contents);
};
```

### Ava11 接口

Ava11 使用 `IDataObject`：

```csharp
richInput.CanImportDataObject = data => data.Contains(DataFormats.Text);
richInput.DataObjectImporter = data =>
{
    var text = data.Get(DataFormats.Text) as string;
    return Task.FromResult<IEnumerable<RichTextContent>>(
        new[] { RichTextContent.FromCustom("custom-payload", text) });
};

richInput.PasteHandler = context =>
{
    // Ava11 下 context.Formats 是 IDataObject.GetDataFormats() 的快照。
    if (context.ContainsFormat("com.myapp.order-card"))
    {
        var payload = context.DataObject.Get(DataFormats.Text) as string;
        context.InsertContents(new[]
        {
            RichTextContent.FromCustom("订单卡片", ParseOrder(payload), "order-card")
        });
        return Task.CompletedTask;
    }

    var text = context.DataObject.Get(DataFormats.Text) as string;
    if (text?.StartsWith("order:", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.InsertContents(new[]
        {
            RichTextContent.FromCustom(text, new OrderPayload(text), "order-card")
        });
    }

    return Task.CompletedTask;
};

richInput.DropHandler = context =>
{
    var fileNames = context.DataObject.GetFileNames();
    context.InsertContents(fileNames.Select(fileName =>
        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? RichTextContent.FromCustom(Path.GetFileName(fileName), fileName, "package-file")
            : RichTextContent.FromFileName(fileName)));
    return Task.CompletedTask;
};
```

Ava11/Ava12 都会额外识别 Win10 截图常见的 bitmap 剪贴板格式，例如 `Bitmap`、`image/png`、`image/x-png`、`PNG`、`DeviceIndependentBitmap`、`CF_DIB`、`CF_DIBV5`、`Format17`；也识别 macOS pasteboard 常见图片格式，例如 `public.tiff`、`TIFF picture`、`NeXT TIFF v4.0 pasteboard type`、`PNGf`、`JPEG picture`、`GIF picture`、`BMP `、`TPIC`、`jp2 `、`8BPS`、`AVIF`、`WEBP`。Ava11 从 `IDataObject` 读取，Ava12 从 `IDataTransfer/IAsyncDataTransfer` 的平台字节格式读取。如果读取到 `Bitmap`、图片 `Stream` 或 `byte[]`，会按图片内容插入；读取失败时继续走文件、文件名或业务自定义 importer。

平台图片剪贴板有几个默认兜底：

- Windows 截图工具常给 `CF_DIB`、`CF_DIBV5` 或 `Format17`。这些是 DIB 数据，不是完整 BMP 文件，默认处理会补 BMP 文件头后再解码。
- macOS 截图工具可能只给 `public.tiff` / `TIFF picture` / `NeXT TIFF v4.0 pasteboard type`，Avalonia/Skia 不一定能直接解 TIFF。默认处理会在 macOS 下用 AppKit 把这类图片 bytes 转成 PNG，再插入为图片。
- 如果某个业务截图工具使用私有格式，先用 `PasteHandler` 检查数据格式；能走默认逻辑时调用 `context.UseDefault()`，不能走默认逻辑时再用 `context.InsertContents(...)` 插入自定义图片或卡片。

如果要排查不同系统或截图工具实际给了什么格式，可以临时记录 `context.Formats`。确认格式后，把私有格式放在 `PasteHandler` / `DropHandler` 最前面处理；普通图片、DIB、文件仍交给 `UseDefault()` 或直接返回走默认逻辑：

```csharp
richInput.PasteHandler = context =>
{
    var formats = context.Formats; // Ava12 为 DataFormat 列表，Ava11 为字符串列表。

    if (context.ContainsFormat("com.myapp.rich-message"))
    {
        context.InsertContents(ReadMyRichMessage(context));
        return Task.CompletedTask;
    }

    context.UseDefault();
    return Task.CompletedTask;
};
```

### 批量插入

程序主动插入多个文件、emoji、图片或业务卡片时，优先用 `InsertContents`。它会一次性插入占位符并批量挂载富内容，避免大量自定义组件逐个插入时频繁触发布局和重绘：

```csharp
richInput.InsertContents(new[]
{
    RichTextContent.FromCustom("@全体成员", allMembers, "mention-all"),
    RichTextContent.FromEmoji("👍"),
    RichTextContent.FromCustom("订单 #1001", order, "order-card")
});

richInput.InsertContents(editor.TextArea.Caret.Offset, files.Select(file =>
    RichTextContent.FromFile(file)));
```

### 长文档持续输入

富输入可以用于较长的消息草稿、笔记或带图片/卡片的文档。普通文本输入、IME 确认文本、在文本区回车或删除普通文本时，管理器不会每次都全量扫描所有图片和自定义组件；富内容锚点由 `AnchorSegment` 跟随文档移动，只有删除范围真的覆盖富内容占位符时才处理对应项。

几个使用建议：

- 批量导入图片、附件、业务卡片时使用 `InsertContents`，避免逐项插入触发多次布局。
- 持续输入时直接使用 `TextArea.Document` 或普通输入流程即可，不需要业务层手动刷新富内容列表。
- 普通输入只会让锚点跟随移动，不会重排全部富内容；真正新增、删除或重建富内容时才会刷新内部顺序缓存。
- 需要发送、保存、复制或展示时再调用 `GetValue()`、`GetSnapshot()`、`GetItemsInDocumentOrder()` 等 API，这些读取类 API 会做一次一致性清理，适合放在用户动作边界上。
- 只处理局部内容时优先传 `ISegment`，例如 `GetValue(selectionSegment)`、`CreateSnapshot(selectionSegment)`、`GetPlainText(selectionSegment)`；它们会按范围定位富内容，避免在很长文档里扫描全部图片和卡片。
- 大文档展示框和发送框宽度不一致时，继续用 `GetValue/SetValue` 复用数据；目标控件会按自己的宽度重新布局图片和自定义组件。
- 自定义 `ElementFactory` 里避免同步解大图、读文件、访问网络或做复杂布局计算；缩略图、上传状态、业务数据建议提前放在 `RichTextContent.Value` / `Metadata`，控件只负责轻量渲染。

### 多选文件逐项处理

`FileContentImporter` 会在默认转换前逐项调用，适合多选复制里同时包含图片、文件夹、exe、zip、文档的情况。返回 `null` 会跳过当前项；想走默认逻辑时显式调用 `CreateDefaultContentAsync()`：

```csharp
richInput.FileContentImporter = async context =>
{
    if (context.IsFolder)
        return RichTextContent.FromCustom(context.Name, context.StorageItem ?? context.FileName, "folder-card");

    if (context.IsExecutable)
        return RichTextContent.FromCustom(context.Name, context.StorageItem ?? context.FileName, "danger-file");

    if (context.IsArchive)
        return RichTextContent.FromCustom(context.Name, context.StorageItem ?? context.FileName, "archive-file");

    if (context.IsDocument)
        return RichTextContent.FromCustom(context.Name, context.StorageItem ?? context.FileName, "document-file");

    return await context.CreateDefaultContentAsync();
};
```

`RichTextFileImportContext` 提供：

- `Name`、`Source`
- `StorageItem`：Ava12/Ava11 storage item，可能为空
- `FileName`：Ava11 文件路径或业务传入路径，可能为空
- `IsFolder`
- `IsImage`
- `IsExecutable`
- `IsArchive`
- `IsDocument`
- `CreateDefaultContentAsync()`

### 常见 IM 场景

文本里识别业务协议：

```csharp
richInput.PasteHandler = async context =>
{
    var text = await context.DataTransfer.TryGetTextAsync();
    if (text?.StartsWith("im://user/", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.InsertContents(new[]
        {
            RichTextContent.FromCustom("@用户", ParseUser(text), "mention-user")
        });
    }
};
```

阻止某些文件，同时允许其他文件默认插入：

```csharp
richInput.FileContentImporter = context =>
{
    if (context.IsExecutable)
        return Task.FromResult<RichTextContent>(null);

    return context.CreateDefaultContentAsync();
};
```

如果要阻止整个粘贴动作，可以在 `PasteHandler` 里显式插入空文本或业务提示：

```csharp
richInput.PasteHandler = context =>
{
    if (ShouldBlock(context))
    {
        ShowToast("当前内容不允许粘贴");
        context.InsertText(string.Empty);
    }

    return Task.CompletedTask;
};
```

先插入上传占位卡片，再异步更新状态：

```csharp
richInput.FileContentImporter = context =>
{
    var upload = new UploadTask(context.Name, context.StorageItem ?? context.FileName);
    var content = RichTextContent.FromCustom(context.Name, upload, "uploading-file");
    _ = upload.StartAsync();
    return Task.FromResult(content);
};
```

同一套数据在发送框和消息展示框之间复用：

```csharp
var value = inputRich.GetValue();
messageRich.SetValue(value);
```

## 复制粘贴快照

复制时会同时写入普通文本和富内容快照。粘回支持 `RichTextInputManager` 的编辑器时会恢复富内容元数据；粘到普通输入框时仍是普通文本。

全选复制或局部选择复制时，剪贴板里通常会同时存在 `DataFormats.Text` 和 `RichTextClipboardFormat`。富输入目标会优先读取 `RichTextClipboardFormat`，不会把图片、文件或业务组件按 `Image` / `report.pdf` 这类显示文本插入；普通控件才使用纯文本降级。

同进程复制粘贴会使用一个有界 live cache 保存本次剪贴板中的 `RichTextContent`，可以完整保留 Bitmap、自定义 `Value`、`Metadata` 和 `StyleKey`。跨进程、应用重启或 cache 失效后，图片会优先从 `Source` 对应的本地文件恢复；没有可读 `Source` 时会尝试把图片数据嵌入富快照。

某些系统剪贴板或平台实现可能不会保留自定义格式，只把 `\uFFFC` 对象占位符降级成空白文本。同进程内会额外记录最近一次富内容复制快照；如果粘贴时只读到了纯文本，但文本和刚复制的富内容快照匹配，仍会恢复图片、文件和自定义组件。

```csharp
// 每张内存图片最多嵌入 4 MB，设置为 0 可关闭嵌入。
richInput.MaxEmbeddedClipboardImageBytes = 4 * 1024 * 1024;
```

超过上限或无法编码的内存图片仍会保留 `RichTextContentKind.Image` 和占位符，但默认渲染没有 Bitmap 时只能显示为图片项的降级样式。需要跨进程稳定恢复时，建议给图片内容提供可读的 `Source`，或者在业务协议里自己存储缩略图/资源 ID。

同应用内从一个富输入框赋值给另一个富输入框或会话展示框，优先使用 live value。它会保留 Bitmap、自定义 `Value`、`Metadata` 和 `StyleKey`，目标控件会按自己的最大宽度和渲染工厂重新布局：

```csharp
var value = inputRich.GetValue();
previewRich.SetValue(value);
```

手动序列化：

```csharp
var snapshotJson = richInput.SerializeSnapshot(editor.TextArea.Selection.SurroundingSegment);
otherRich.SetSerializedSnapshot(snapshotJson);
```

`SerializeSnapshot` 适合存储或跨进程传递；如果要完整保留内存对象、Bitmap 或业务对象引用，用 `GetValue/SetValue`。

滚动查看长文本时，图片、文件和业务卡片会立即跟随文本层移动，不参与位置过渡；位置过渡只用于 IME 确认、插入、删除导致的同一行布局变化。

## 纯文本降级

发送消息、搜索索引或提交到不支持富内容的 API 时，可以把富内容替换成显示文本：

```csharp
var plain = richInput.GetPlainText(null, item =>
    item.Content.Kind == RichTextContentKind.Image
        ? "[图片]"
        : item.Content.DisplayText);
```

## 尺寸和 resize

```csharp
richInput.MinInlineElementWidth = 48;
richInput.MaxInlineElementWidth = 240;
richInput.MaxImageWidth = 190;
richInput.MaxImageHeight = 130;
```

`TextView` resize 后会重绘富内容，文件卡片和图片应使用 `MaxWidth`、`TextTrimming`、`Stretch.Uniform` 等响应式布局，避免输入框被用户拖大拖小时溢出。
