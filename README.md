# AvaloniaEdit.RichTextInput

`AvaloniaEdit.RichTextInput` is an AvaloniaEdit distribution with rich input support built in. It keeps the normal AvaloniaEdit editor API, namespace, resources, syntax highlighting, folding, completion, virtualization, undo/redo, and TextMate integration, while adding IM-style mixed content input for text, emoji, images, files, links, mentions, and business-defined inline components.

这个仓库适合聊天发送框、会话消息展示、客服输入框、评论框、带图片/卡片的长文档编辑器等场景。它不是只包了一层外部控件，因为富内容输入需要和 AvaloniaEdit 的 IME、selection、clipboard、drag/drop、caret、line layout、scroll virtualization 深度协作。

## Branches

- `ava12`: Avalonia 12.x 适配分支，包版本从 `12.0.0-rich.1` 开始。
- `ava11`: Avalonia 11.x 适配分支，包版本从 `11.3.0-rich.1` 开始。

## Packages

```xml
<PackageReference Include="AvaloniaEdit.RichTextInput" Version="12.0.0-rich.1" />
```

TextMate syntax highlighting:

```xml
<PackageReference Include="AvaloniaEdit.RichTextInput.TextMate" Version="12.0.0-rich.1" />
<PackageReference Include="TextMateSharp.Grammars" Version="2.0.3" />
```

The NuGet package ID is different from upstream AvaloniaEdit, but the assembly name and public namespaces stay compatible. Existing XAML references still use `AvaloniaEdit`:

```xaml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />
</Application.Styles>
```

```xaml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:avaloniaEdit="clr-namespace:AvaloniaEdit;assembly=AvaloniaEdit">
  <avaloniaEdit:TextEditor
      Name="Editor"
      ShowLineNumbers="True"
      FontFamily="Cascadia Code,Consolas,Menlo,Monospace" />
</Window>
```

## Rich Input Quick Start

```csharp
using AvaloniaEdit.RichTextInput;

var richInput = RichTextInputManager.Install(editor.TextArea);

richInput.InsertEmoji("😀");
richInput.InsertImage(bitmap, "screenshot.png");
richInput.InsertFileName(@"C:\Users\me\report.pdf");
richInput.InsertCustom("order-card", order, styleKey: "order-card");
```

Custom rendering can be registered by key, by full context, or by the legacy item factory:

```csharp
richInput.RegisterElementFactory("order-card", context =>
    new OrderCardView
    {
        DataContext = context.Content.Value,
        MaxWidth = context.AvailableWidth
    });

richInput.ElementFactoryWithContext = context =>
{
    if (context.Metadata.TryGetValue("compact", out var value) && value is true)
        return new CompactCardView { DataContext = context.Content.Value };

    return null;
};
```

Common capabilities exposed by `RichTextInputManager`:

- Inline images, files, emoji, mentions, links, IP/URL style spans, and custom components.
- Fine-grained clipboard and drag/drop handling, including multi-file paste/drop and platform image formats.
- Same-process rich copy/paste fallback when the platform clipboard drops custom formats.
- Inline IME preedit display for Chinese, Japanese, Korean, and other composition-based input methods.
- Selection, context menu, click, double-click, removal, serialization, and value extraction APIs.
- Configurable line content alignment, inline object alignment, caret height, selection style, active line style, and link style.
- Performance paths for many inline components and long mixed-content documents.

See [docs/RichTextInput.md](docs/RichTextInput.md) for the full API guide, paste/drop customization examples, mention popup triggers, link styling, IME behavior, serialization, and demo usage.

## Demo

The demo lives in `src/AvaloniaEdit.Demo`.

```bash
dotnet run --project src/AvaloniaEdit.Demo/AvaloniaEdit.Demo.csproj
```

## Upstream

This project is based on [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit), which is a port of [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) for [Avalonia](https://github.com/AvaloniaUI/Avalonia). The original editor functionality, license, and attribution are preserved.
