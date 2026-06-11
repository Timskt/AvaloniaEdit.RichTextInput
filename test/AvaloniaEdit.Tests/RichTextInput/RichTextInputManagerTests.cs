using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.RichTextInput;
using NUnit.Framework;

namespace AvaloniaEdit.Tests.RichTextInput
{
    [TestFixture]
    public class RichTextInputManagerTests
    {
        [AvaloniaTest]
        public void InsertContentAddsObjectReplacementCharacter()
        {
            var textArea = CreateTextArea("hello");
            var manager = RichTextInputManager.Install(textArea);
            textArea.Caret.Offset = 5;

            manager.InsertContent(RichTextContent.FromCustom("demo.bin", new object()));

            Assert.AreEqual("hello" + RichTextInputManager.ObjectReplacementString, textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreEqual(5, manager.Items[0].Offset);
        }

        [AvaloniaTest]
        public void SelectContentThenRemoveSelectedTextDeletesInlineContent()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertContent(1, RichTextContent.FromCustom("chip", 42));

            manager.SelectContent(item);
            textArea.RemoveSelectedText();

            Assert.AreEqual("ab", textArea.Document.Text);
            Assert.AreEqual(0, manager.Items.Count);
        }

        [AvaloniaTest]
        public void RemovingTextBetweenInlineContentKeepsDocumentOrder()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContent(0, RichTextContent.FromCustom("first", 1));
            manager.InsertContent(textArea.Document.TextLength, RichTextContent.FromCustom("second", 2));

            textArea.Document.Remove(1, 1);

            Assert.AreEqual(RichTextInputManager.ObjectReplacementString + "b" + RichTextInputManager.ObjectReplacementString, textArea.Document.Text);
            var items = manager.GetItemsInDocumentOrder();
            Assert.AreEqual("first", items[0].Content.DisplayText);
            Assert.AreEqual(0, items[0].Offset);
            Assert.AreEqual("second", items[1].Content.DisplayText);
            Assert.AreEqual(2, items[1].Offset);
        }

        [AvaloniaTest]
        public void InsertContentsAddsMultipleRichItemsInDocumentOrder()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            textArea.Caret.Offset = 1;

            var items = manager.InsertContents(new[]
            {
                RichTextContent.FromCustom("one", 1),
                RichTextContent.FromCustom("two", 2),
                RichTextContent.FromCustom("three", 3)
            });

            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + RichTextInputManager.ObjectReplacementString + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual(3, manager.Items.Count);
            Assert.AreEqual(new[] { "one", "two", "three" }, manager.GetItemsInDocumentOrder().Select(item => item.Content.DisplayText).ToArray());
            Assert.AreEqual(4, textArea.Caret.Offset);
        }

        [AvaloniaTest]
        public void TryGetItemUsesUpdatedOffsetsAfterBulkInsert()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContent(1, RichTextContent.FromCustom("old", 0));

            manager.InsertContents(0, Enumerable.Range(1, 3).Select(i => RichTextContent.FromCustom($"new-{i}", i)));

            Assert.IsTrue(manager.TryGetItem(4, out var oldItem));
            Assert.AreEqual("old", oldItem.Content.DisplayText);
            Assert.AreEqual(0, manager.GetFirstInterestedOffset(0));
            Assert.AreEqual(4, manager.GetFirstInterestedOffset(3));
        }

        [AvaloniaTest]
        public void PlainTypingInLargeRichDocumentDoesNotValidateEveryRichItem()
        {
            var textArea = CreateTextArea("start end");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContents(6, Enumerable.Range(0, 500)
                .Select(i => RichTextContent.FromCustom($"card-{i}", i)));
            var validationPasses = manager.InvalidItemValidationPassCount;

            for (var i = 0; i < 50; i++)
                textArea.Document.Insert(0, "x");

            Assert.AreEqual(validationPasses, manager.InvalidItemValidationPassCount);
            Assert.AreEqual(500, manager.Items.Count);
            Assert.IsTrue(manager.Items.All(item =>
                textArea.Document.GetCharAt(item.Offset) == RichTextInputManager.ObjectReplacementCharacter));
        }

        [AvaloniaTest]
        public void PlainTypingInLargeRichDocumentReusesOrderedItemCache()
        {
            var textArea = CreateTextArea("start end");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContents(6, Enumerable.Range(0, 500)
                .Select(i => RichTextContent.FromCustom($"card-{i}", i)));

            Assert.AreEqual(6, manager.GetFirstInterestedOffset(0));
            var cacheBuilds = manager.ItemsCacheBuildCount;

            for (var i = 0; i < 50; i++)
            {
                textArea.Document.Insert(0, "x");
                Assert.AreEqual(7 + i, manager.GetFirstInterestedOffset(0));
            }

            Assert.AreEqual(cacheBuilds, manager.ItemsCacheBuildCount);
        }

        [AvaloniaTest]
        public async Task InsertDataAsyncRestoresSerializedRichTextSnapshot()
        {
            var sourceTextArea = CreateTextArea("hi ");
            var source = RichTextInputManager.Install(sourceTextArea);
            source.InsertContent(sourceTextArea.Document.TextLength, new RichTextContent(RichTextContentKind.File, "report.pdf", null, "/tmp/report.pdf"));
            sourceTextArea.Document.Insert(sourceTextArea.Document.TextLength, " ok");
            var dataObject = new DataObject();
            source.TrySetRichClipboardData(dataObject, new SimpleSegment(0, sourceTextArea.Document.TextLength));

            var targetTextArea = CreateTextArea("");
            var target = RichTextInputManager.Install(targetTextArea);
            var inserted = await target.InsertDataAsync(dataObject, 0, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("hi " + RichTextInputManager.ObjectReplacementString + " ok", targetTextArea.Document.Text);
            Assert.AreEqual(1, target.Items.Count);
            Assert.AreEqual("report.pdf", target.Items[0].Content.DisplayText);
        }

        [AvaloniaTest]
        public async Task InsertDataAsyncAddsBitmapContentFromDataObject()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var dataObject = new DataObject();
            dataObject.Set("Bitmap", new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), null, null));

            var inserted = await manager.InsertDataAsync(dataObject, 1, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreEqual(RichTextContentKind.Image, manager.Items[0].Content.Kind);
        }

        [AvaloniaTest]
        public async Task InsertDataAsyncAddsBitmapContentFromWindowsDibClipboardData()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var dataObject = new DataObject();
            dataObject.Set("CF_DIB", CreateSinglePixelDib32());

            var inserted = await manager.InsertDataAsync(dataObject, 1, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreEqual(RichTextContentKind.Image, manager.Items[0].Content.Kind);
        }

        [AvaloniaTest]
        public async Task InsertDataAsyncAddsBitmapContentFromMacClassicBitmapClipboardData()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var dataObject = new DataObject();
            dataObject.Set("BMP ", CreateSinglePixelBmp32());

            var inserted = await manager.InsertDataAsync(dataObject, 1, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreEqual(RichTextContentKind.Image, manager.Items[0].Content.Kind);
        }

        [AvaloniaTest]
        public async Task RichClipboardRestoresLiveBitmapContent()
        {
            var sourceTextArea = CreateTextArea("");
            var source = RichTextInputManager.Install(sourceTextArea);
            using var imageStream = new MemoryStream(CreateSinglePixelBmp32());
            source.InsertImage(new Bitmap(imageStream), "screenshot");
            var dataObject = new DataObject();
            var sourceBitmap = source.Items[0].Content.Value;

            var copied = source.TrySetRichClipboardData(dataObject, new SimpleSegment(0, sourceTextArea.Document.TextLength));
            var targetTextArea = CreateTextArea("");
            var target = RichTextInputManager.Install(targetTextArea);
            var inserted = await target.InsertDataAsync(dataObject, 0, false);

            Assert.IsTrue(copied);
            Assert.IsTrue(inserted);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementString, targetTextArea.Document.Text);
            Assert.AreEqual(1, target.Items.Count);
            Assert.AreEqual(RichTextContentKind.Image, target.Items[0].Content.Kind);
            Assert.IsInstanceOf<Bitmap>(target.Items[0].Content.Value);
            Assert.AreSame(sourceBitmap, target.Items[0].Content.Value);
        }

        [AvaloniaTest]
        public async Task RichClipboardMixedSelectionPrefersRichSnapshotOverPlainText()
        {
            var payload = new object();
            var sourceTextArea = CreateTextArea("hello ");
            var source = RichTextInputManager.Install(sourceTextArea);
            using var imageStream = new MemoryStream(CreateSinglePixelBmp32());
            source.InsertImage(new Bitmap(imageStream), "screenshot");
            sourceTextArea.Document.Insert(sourceTextArea.Document.TextLength, " ");
            source.InsertCustom("card", payload, "business-card");
            sourceTextArea.Document.Insert(sourceTextArea.Document.TextLength, " ");
            source.InsertFileName("/tmp/report.pdf");
            sourceTextArea.Document.Insert(sourceTextArea.Document.TextLength, " done");

            var dataObject = new DataObject();
            dataObject.Set(DataFormats.Text, "hello Image card report.pdf done");
            var copied = source.TrySetRichClipboardData(dataObject, new SimpleSegment(0, sourceTextArea.Document.TextLength));
            var targetTextArea = CreateTextArea("");
            var target = RichTextInputManager.Install(targetTextArea);

            var inserted = await target.InsertPasteDataAsync(dataObject, 0, false);

            Assert.IsTrue(copied);
            Assert.IsTrue(inserted);
            Assert.AreEqual(sourceTextArea.Document.Text, targetTextArea.Document.Text);
            Assert.AreEqual(3, target.Items.Count);
            Assert.AreEqual(new[]
            {
                RichTextContentKind.Image,
                RichTextContentKind.Custom,
                RichTextContentKind.File
            }, target.Items.Select(item => item.Content.Kind).ToArray());
            Assert.IsInstanceOf<Bitmap>(target.Items[0].Content.Value);
            Assert.AreSame(payload, target.Items[1].Content.Value);
            Assert.AreEqual("business-card", target.Items[1].Content.StyleKey);
            Assert.AreEqual("report.pdf", target.Items[2].Content.DisplayText);
        }

        [AvaloniaTest]
        public async Task RichClipboardFallbackRestoresSnapshotWhenClipboardDropsCustomFormat()
        {
            RichTextInputManager.ClearRichClipboardFallback();
            var payload = new object();
            var sourceTextArea = CreateTextArea("hello ");
            var source = RichTextInputManager.Install(sourceTextArea);
            source.InsertCustom("card", payload, "business-card");
            sourceTextArea.Document.Insert(sourceTextArea.Document.TextLength, " done");
            var dataObject = new DataObject();

            var copied = source.TrySetRichClipboardData(dataObject, new SimpleSegment(0, sourceTextArea.Document.TextLength));
            var plainOnlyDataObject = new DataObject();
            plainOnlyDataObject.Set(
                DataFormats.Text,
                sourceTextArea.Document.Text.Replace(RichTextInputManager.ObjectReplacementCharacter, ' '));
            var targetTextArea = CreateTextArea("");
            var target = RichTextInputManager.Install(targetTextArea);
            target.PasteHandler = context =>
            {
                if (target.CanInsertDefaultData(context.DataObject))
                {
                    context.UseDefault();
                    return Task.CompletedTask;
                }

                context.InsertText(context.DataObject.Get(DataFormats.Text) as string);
                return Task.CompletedTask;
            };

            var inserted = await target.InsertPasteDataAsync(plainOnlyDataObject, 0, false);

            Assert.IsTrue(copied);
            Assert.IsTrue(inserted);
            Assert.AreEqual(sourceTextArea.Document.Text, targetTextArea.Document.Text);
            Assert.AreEqual(1, target.Items.Count);
            Assert.AreEqual(RichTextContentKind.Custom, target.Items[0].Content.Kind);
            Assert.AreSame(payload, target.Items[0].Content.Value);
            Assert.AreEqual("business-card", target.Items[0].Content.StyleKey);
        }

        [AvaloniaTest]
        public async Task InsertDataAsyncAddsBitmapContentFromMacTiffClipboardData()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Assert.Ignore("macOS AppKit image conversion is only available on macOS.");

            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var dataObject = new DataObject();
            dataObject.Set("public.tiff", CreateSinglePixelTiffRgb());

            var inserted = await manager.InsertDataAsync(dataObject, 1, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreEqual(RichTextContentKind.Image, manager.Items[0].Content.Kind);
        }

        [AvaloniaTest]
        public void CanInsertDefaultDataRecognizesWindowsDibClipboardData()
        {
            var manager = RichTextInputManager.Install(CreateTextArea(""));
            var dataObject = new DataObject();
            dataObject.Set("CF_DIB", CreateSinglePixelDib32());

            Assert.IsTrue(manager.CanInsertDefaultData(dataObject));
        }

        [AvaloniaTest]
        public void CanInsertDefaultDataRecognizesMacClassicBitmapClipboardData()
        {
            var manager = RichTextInputManager.Install(CreateTextArea(""));
            var dataObject = new DataObject();
            dataObject.Set("BMP ", CreateSinglePixelBmp32());

            Assert.IsTrue(manager.CanInsertDefaultData(dataObject));
        }

        [AvaloniaTest]
        public void CanInsertDefaultDataRecognizesMacLegacyTiffClipboardData()
        {
            var manager = RichTextInputManager.Install(CreateTextArea(""));
            var dataObject = new DataObject();
            dataObject.Set("NeXT TIFF v4.0 pasteboard type", CreateSinglePixelTiffRgb());

            Assert.IsTrue(manager.CanInsertDefaultData(dataObject));
        }

        [AvaloniaTest]
        public async Task InsertDataAsyncAddsMultipleFileNameContentsIncludingFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "AvaloniaEdit.RichInput." + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(root, "docs");
            var executable = Path.Combine(root, "tool.exe");
            var document = Path.Combine(root, "readme.txt");
            Directory.CreateDirectory(folder);
            File.WriteAllText(executable, "binary");
            File.WriteAllText(document, "hello");
            try
            {
                var textArea = CreateTextArea("");
                var manager = RichTextInputManager.Install(textArea);
                manager.FileContentImporter = context =>
                    context.IsExecutable
                        ? Task.FromResult(RichTextContent.FromCustom(context.Name, context.FileName, "executable-file"))
                        : context.CreateDefaultContentAsync();
                var dataObject = new DataObject();
                dataObject.Set(DataFormats.FileNames, new[] { folder, executable, document });

                var inserted = await manager.InsertDataAsync(dataObject, 0, false);

                Assert.IsTrue(inserted);
                Assert.AreEqual(
                    RichTextInputManager.ObjectReplacementString + RichTextInputManager.ObjectReplacementString + RichTextInputManager.ObjectReplacementString,
                    textArea.Document.Text);
                Assert.AreEqual(3, manager.Items.Count);
                Assert.AreEqual(RichTextContentKind.Folder, manager.Items[0].Content.Kind);
                Assert.AreEqual(RichTextContentKind.Custom, manager.Items[1].Content.Kind);
                Assert.AreEqual(RichTextContentKind.File, manager.Items[2].Content.Kind);
                Assert.AreEqual("docs", manager.Items[0].Content.DisplayText);
                Assert.AreEqual("executable-file", manager.Items[1].Content.StyleKey);
                Assert.AreEqual("readme.txt", manager.Items[2].Content.DisplayText);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        [AvaloniaTest]
        public void RichTextInputDoesNotBlockChineseTextInput()
        {
            var textArea = CreateTextArea("");
            RichTextInputManager.Install(textArea);

            textArea.PerformTextInput("中文输入");

            Assert.AreEqual("中文输入", textArea.Document.Text);
        }

        [AvaloniaTest]
        public void CaretHeightUsesTextMetricsWhenInlineContentMakesLineTall()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.ElementFactory = item => new Border
            {
                Width = 24,
                Height = 100
            };
            manager.InsertContent(1, RichTextContent.FromCustom("tall", 1));
            textArea.Caret.Offset = 0;

            var caretRectangle = textArea.Caret.CalculateCaretRectangle();

            Assert.Less(caretRectangle.Height, 40);
        }

        [AvaloniaTest]
        public void RichTextInputDefaultsToBottomAlignmentAndAllowsPerItemOverride()
        {
            var textArea = CreateTextArea("");
            var manager = RichTextInputManager.Install(textArea);
            var imageLike = manager.InsertCustom("image-like", 1);
            manager.LineContentAlignment = LineContentVerticalAlignment.Top;
            manager.InlineObjectAlignmentSelector = item => item == imageLike
                ? InlineObjectVerticalAlignment.Top
                : InlineObjectVerticalAlignment.Bottom;

            Assert.AreEqual(InlineObjectVerticalAlignment.Bottom, manager.InlineObjectAlignment);
            Assert.AreEqual(LineContentVerticalAlignment.Top, textArea.Options.LineContentVerticalAlignment);
            Assert.AreEqual(InlineObjectVerticalAlignment.Top, manager.GetInlineObjectAlignment(imageLike));
        }

        [AvaloniaTest]
        public void InlineContentStyleSelectorCanCustomizeSelectionVisuals()
        {
            var textArea = CreateTextArea("");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertCustom("styled", 1);
            var selectedBrush = Brushes.Red;
            manager.InlineContentStyleSelector = (contentItem, selected) => new RichTextInlineContentStyle
            {
                Background = selected ? selectedBrush : Brushes.Transparent,
                BorderThickness = new Thickness(selected ? 2 : 0),
                CornerRadius = new CornerRadius(8)
            };

            var style = manager.GetInlineContentStyle(item, true);

            Assert.AreSame(selectedBrush, style.Background);
            Assert.AreEqual(new Thickness(2), style.BorderThickness);
            Assert.AreEqual(new CornerRadius(8), style.CornerRadius);
        }

        [AvaloniaTest]
        public void RegisteredElementFactoryUsesStyleKeyAndContext()
        {
            var textArea = CreateTextArea("");
            var manager = RichTextInputManager.Install(textArea);
            manager.RegisterElementFactory("order-card", context =>
                new Button
                {
                    Content = $"{context.StyleKey}:{context.Content.DisplayText}:{context.AvailableWidth > 0}"
                });
            var item = manager.InsertCustom("A001", new object(), "order-card");

            var control = manager.CreateElement(item);
            var button = (Button)((Border)control).Child;

            Assert.AreEqual("order-card:A001:True", button.Content);
        }

        [AvaloniaTest]
        public void SelectionBackgroundSkipsSelectedRichContent()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertContent(1, RichTextContent.FromCustom("card", 1));

            var segments = manager.TransformSelectionBackgroundSegments(new[]
            {
                new SelectionSegment(item.Offset, item.EndOffset)
            }).ToArray();

            Assert.AreEqual(0, segments.Length);
        }

        [AvaloniaTest]
        public void SelectionBackgroundKeepsTextAroundRichContent()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContent(1, RichTextContent.FromCustom("card", 1));

            var segments = manager.TransformSelectionBackgroundSegments(new[]
            {
                new SelectionSegment(0, textArea.Document.TextLength)
            }).ToArray();

            Assert.AreEqual(2, segments.Length);
            Assert.AreEqual(new SimpleSegment(0, 1), new SimpleSegment(segments[0]));
            Assert.AreEqual(new SimpleSegment(2, 1), new SimpleSegment(segments[1]));
        }

        [AvaloniaTest]
        public void SelectionBackgroundCanIncludeRichContentWhenSuppressionIsDisabled()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.SuppressTextSelectionBackgroundForRichContent = false;
            var item = manager.InsertContent(1, RichTextContent.FromCustom("card", 1));

            var segments = manager.TransformSelectionBackgroundSegments(new[]
            {
                new SelectionSegment(item.Offset, item.EndOffset)
            }).ToArray();

            Assert.AreEqual(1, segments.Length);
            Assert.AreEqual(new SimpleSegment(item.Offset, item.Length), new SimpleSegment(segments[0]));
        }

        [AvaloniaTest]
        public void SelectionValueIncludesSelectedTextAndRichContent()
        {
            var textArea = CreateTextArea("a bc d");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertContent(2, RichTextContent.FromCustom("card", 7, "card"));
            textArea.Selection = Selection.Create(textArea, 1, 5);

            var value = manager.GetSelectionValue();
            var plainText = manager.GetSelectedPlainText(selectedItem => $"[{selectedItem.Content.DisplayText}]");
            var selectedItems = manager.GetSelectedItems();

            Assert.AreEqual(" " + RichTextInputManager.ObjectReplacementString + "bc", value.Text);
            Assert.AreEqual(1, value.Items.Count);
            Assert.AreSame(item.Content, value.Items[0].Content);
            Assert.AreEqual(1, value.Items[0].Offset);
            Assert.AreEqual(" [card]bc", plainText);
            Assert.AreEqual(1, selectedItems.Count);
            Assert.AreSame(item, selectedItems[0]);
        }

        [AvaloniaTest]
        public void PointerSelectionBehaviorSelectorCanPreserveTextSelection()
        {
            var textArea = CreateTextArea("abcd");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertContent(2, RichTextContent.FromCustom("card", 7, "card"));
            textArea.Selection = Selection.Create(textArea, 0, 1);
            manager.ContentPointerSelectionBehaviorSelector = args =>
                args.Item.Content.StyleKey == "card"
                    ? RichTextContentPointerSelectionBehavior.None
                    : RichTextContentPointerSelectionBehavior.SelectContent;

            manager.ApplyContentPointerSelection(new RichTextContentPointerEventArgs(
                manager,
                item,
                null,
                RichTextContentPointerEventKind.PointerPressed));

            Assert.AreEqual(0, textArea.Selection.SurroundingSegment.Offset);
            Assert.AreEqual(1, textArea.Selection.SurroundingSegment.EndOffset);
        }

        [AvaloniaTest]
        public void EnterOnSelectedRichContentKeepsContentAndInsertsNewLineAfterIt()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertContent(1, RichTextContent.FromCustom("card", 7, "card"));
            manager.SelectContent(item);

            var handled = manager.HandleSelectedContentEnterKey();

            Assert.IsTrue(handled);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "\nb", textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreSame(item.Content, manager.Items[0].Content);
            Assert.AreEqual(1, manager.Items[0].Offset);
            Assert.AreEqual(3, textArea.Caret.Offset);
            Assert.IsTrue(textArea.Selection.IsEmpty);
        }

        [AvaloniaTest]
        public void EnterOnSelectedRichContentKeepsFollowingRichContentItems()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var card = manager.InsertContent(1, RichTextContent.FromCustom("card", 7, "card"));
            var image = manager.InsertContent(textArea.Document.TextLength, RichTextContent.FromCustom("image", 8, "image"));
            manager.SelectContent(card);

            var handled = manager.HandleSelectedContentEnterKey();

            Assert.IsTrue(handled);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "\nb" + RichTextInputManager.ObjectReplacementString, textArea.Document.Text);
            Assert.AreEqual(2, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreSame(image.Content, manager.Items[1].Content);
            Assert.AreEqual(1, manager.Items[0].Offset);
            Assert.AreEqual(4, manager.Items[1].Offset);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[1].Offset));
            Assert.AreEqual(3, textArea.Caret.Offset);
            Assert.IsTrue(textArea.Selection.IsEmpty);
        }

        [AvaloniaTest]
        public void EnterBeforeSelectedRichContentKeepsSelectedAndFollowingRichContentItems()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.SelectedContentEnterBehavior = RichTextSelectedContentEnterBehavior.InsertNewLineBeforeContent;
            var card = manager.InsertContent(1, RichTextContent.FromCustom("card", 7, "card"));
            var file = manager.InsertContent(textArea.Document.TextLength, RichTextContent.FromFileName("report.pdf"));
            manager.SelectContent(card);

            var handled = manager.HandleSelectedContentEnterKey();

            Assert.IsTrue(handled);
            Assert.AreEqual("a\n" + RichTextInputManager.ObjectReplacementString + "b" + RichTextInputManager.ObjectReplacementString, textArea.Document.Text);
            Assert.AreEqual(2, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreSame(file.Content, manager.Items[1].Content);
            Assert.AreEqual(2, manager.Items[0].Offset);
            Assert.AreEqual(4, manager.Items[1].Offset);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[1].Offset));
            Assert.AreEqual(2, textArea.Caret.Offset);
            Assert.IsTrue(textArea.Selection.IsEmpty);
        }

        [AvaloniaTest]
        public void PlainEnterInLineWithRichContentKeepsAllRichContentItems()
        {
            var textArea = CreateTextArea("abcd");
            var manager = RichTextInputManager.Install(textArea);
            var card = manager.InsertContent(1, RichTextContent.FromCustom("card", 7, "card"));
            var image = manager.InsertContent(4, RichTextContent.FromCustom("image", 8, "image"));
            textArea.Caret.Offset = 3;

            textArea.PerformTextInput("\n");

            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b\nc" + RichTextInputManager.ObjectReplacementString + "d", textArea.Document.Text);
            Assert.AreEqual(2, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreSame(image.Content, manager.Items[1].Content);
            Assert.AreEqual(1, manager.Items[0].Offset);
            Assert.AreEqual(5, manager.Items[1].Offset);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[1].Offset));
        }

        [AvaloniaTest]
        public void PlainEnterImmediatelyBeforeRichContentKeepsAllRichContentItems()
        {
            var textArea = CreateTextArea("abcd");
            var manager = RichTextInputManager.Install(textArea);
            var card = manager.InsertContent(1, RichTextContent.FromCustom("card", 7, "card"));
            var image = manager.InsertContent(4, RichTextContent.FromCustom("image", 8, "image"));
            textArea.Caret.Offset = card.Offset;

            textArea.PerformTextInput("\n");

            Assert.AreEqual("a\n" + RichTextInputManager.ObjectReplacementString + "bc" + RichTextInputManager.ObjectReplacementString + "d", textArea.Document.Text);
            Assert.AreEqual(2, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreSame(image.Content, manager.Items[1].Content);
            Assert.AreEqual(2, manager.Items[0].Offset);
            Assert.AreEqual(5, manager.Items[1].Offset);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[1].Offset));
        }

        [AvaloniaTest]
        public void PlainEnterImmediatelyAfterRichContentKeepsAllRichContentItems()
        {
            var textArea = CreateTextArea("abcd");
            var manager = RichTextInputManager.Install(textArea);
            var card = manager.InsertContent(1, RichTextContent.FromCustom("card", 7, "card"));
            var image = manager.InsertContent(4, RichTextContent.FromCustom("image", 8, "image"));
            textArea.Caret.Offset = card.EndOffset;

            textArea.PerformTextInput("\n");

            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "\nbc" + RichTextInputManager.ObjectReplacementString + "d", textArea.Document.Text);
            Assert.AreEqual(2, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreSame(image.Content, manager.Items[1].Content);
            Assert.AreEqual(1, manager.Items[0].Offset);
            Assert.AreEqual(5, manager.Items[1].Offset);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[1].Offset));
        }

        [AvaloniaTest]
        public void PlainEnterWithCSharpIndentationKeepsRichContentItemsOnReformattedLine()
        {
            var textArea = CreateTextArea("if (true) { abcd }");
            textArea.IndentationStrategy = new CSharpIndentationStrategy(textArea.Options);
            var manager = RichTextInputManager.Install(textArea);
            var card = manager.InsertContent(13, RichTextContent.FromCustom("card", 7, "card"));
            var image = manager.InsertContent(16, RichTextContent.FromCustom("image", 8, "image"));
            textArea.Caret.Offset = 15;

            textArea.PerformTextInput("\n");

            Assert.AreEqual(2, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreSame(image.Content, manager.Items[1].Content);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[1].Offset));
        }

        [AvaloniaTest]
        public void EnterImmediatelyBeforeTrailingRichContentKeepsContent()
        {
            var textArea = CreateTextArea("if (true) { ab");
            textArea.IndentationStrategy = new CSharpIndentationStrategy(textArea.Options);
            var manager = RichTextInputManager.Install(textArea);
            var card = manager.InsertContent(textArea.Document.TextLength, RichTextContent.FromCustom("card", 7, "card"));
            textArea.Caret.Offset = card.Offset;

            var handled = manager.HandleCaretAdjacentContentEnterKey();

            Assert.IsTrue(handled);
            Assert.AreEqual("if (true) { ab\n" + RichTextInputManager.ObjectReplacementString, textArea.Document.Text);
            Assert.AreEqual(1, manager.Items.Count);
            Assert.AreSame(card.Content, manager.Items[0].Content);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(manager.Items[0].Offset));
        }

        [AvaloniaTest]
        public void EnterImmediatelyBeforeRichContentDoesNotIndentNewRichContentLine()
        {
            var textArea = CreateTextArea("    ab");
            textArea.IndentationStrategy = new CSharpIndentationStrategy(textArea.Options);
            var manager = RichTextInputManager.Install(textArea);
            var image = manager.InsertContent(textArea.Document.TextLength, RichTextContent.FromCustom("image", 8, "image"));
            textArea.Caret.Offset = image.Offset;

            var handled = manager.HandleCaretAdjacentContentEnterKey();

            Assert.IsTrue(handled);
            var richLine = textArea.Document.GetLineByNumber(2);
            Assert.AreEqual(richLine.Offset, manager.Items[0].Offset);
            Assert.AreEqual(RichTextInputManager.ObjectReplacementCharacter, textArea.Document.GetCharAt(richLine.Offset));
        }

        [AvaloniaTest]
        public void PlainNewLineEnterBehaviorDoesNotIndentTextLine()
        {
            var textArea = CreateTextArea("    hello");
            textArea.IndentationStrategy = new CSharpIndentationStrategy(textArea.Options);
            var manager = RichTextInputManager.Install(textArea);
            manager.EnterKeyBehavior = RichTextEnterKeyBehavior.PlainNewLine;
            textArea.Caret.Offset = textArea.Document.TextLength;

            var handled = manager.HandleEnterKey();

            Assert.IsTrue(handled);
            Assert.AreEqual("    hello\n", textArea.Document.Text);
            var line = textArea.Document.GetLineByNumber(2);
            Assert.AreEqual(line.Offset, textArea.Caret.Offset);
        }

        [AvaloniaTest]
        public void DefaultEnterBehaviorKeepsTextAreaIndentationForPlainText()
        {
            var textArea = CreateTextArea("    hello");
            textArea.IndentationStrategy = new CSharpIndentationStrategy(textArea.Options);
            var manager = RichTextInputManager.Install(textArea);
            textArea.Caret.Offset = textArea.Document.TextLength;

            var handled = manager.HandleEnterKey();

            Assert.IsFalse(handled);
        }

        [AvaloniaTest]
        public void CaretCanMoveAcrossAdjacentRichContentWithKeyboard()
        {
            var textArea = CreateTextArea("");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContent(0, RichTextContent.FromCustom("image", 1));
            manager.InsertContent(1, RichTextContent.FromFileName("report.pdf"));
            textArea.Caret.Offset = textArea.Document.TextLength;

            var desiredXPos = double.NaN;
            var firstLeft = CaretNavigationCommandHandler.GetNewCaretPosition(
                textArea.TextView,
                textArea.Caret.Position,
                CaretMovementType.CharLeft,
                textArea.Selection.EnableVirtualSpace,
                ref desiredXPos);
            var secondLeft = CaretNavigationCommandHandler.GetNewCaretPosition(
                textArea.TextView,
                firstLeft,
                CaretMovementType.CharLeft,
                textArea.Selection.EnableVirtualSpace,
                ref desiredXPos);
            var right = CaretNavigationCommandHandler.GetNewCaretPosition(
                textArea.TextView,
                secondLeft,
                CaretMovementType.CharRight,
                textArea.Selection.EnableVirtualSpace,
                ref desiredXPos);

            Assert.AreEqual(1, textArea.Document.GetOffset(firstLeft.Location));
            Assert.AreEqual(1, firstLeft.VisualColumn);
            Assert.AreEqual(0, textArea.Document.GetOffset(secondLeft.Location));
            Assert.AreEqual(0, secondLeft.VisualColumn);
            Assert.AreEqual(1, textArea.Document.GetOffset(right.Location));
            Assert.AreEqual(1, right.VisualColumn);
        }

        [AvaloniaTest]
        public void InlineContentIsBelowImePreeditLayer()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.ElementFactory = item => new Border
            {
                Width = 80,
                Height = 80
            };
            manager.InsertContent(1, RichTextContent.FromCustom("card", 1));

            textArea.TextView.Measure(new Size(300, 200));
            textArea.TextView.Arrange(new Rect(0, 0, 300, 200));

            var children = textArea.TextView.GetVisualChildren().ToArray();
            var inlineIndex = Array.FindIndex(children, child => child is RichTextInlineContentControl);
            var preeditIndex = Array.FindIndex(children, child => child is PreeditLayer);

            Assert.GreaterOrEqual(inlineIndex, 0);
            Assert.Greater(preeditIndex, inlineIndex);
        }

        [AvaloniaTest]
        public void InlineContentDoesNotAnimateWhenTextViewScrolls()
        {
            var text = "line0\na" + RichTextInputManager.ObjectReplacementString + "b\n"
                + string.Join("\n", Enumerable.Range(0, 40).Select(i => "line" + i));
            var textArea = CreateTextArea(text);
            textArea.TextView.AnimateInlineObjectPlacement = true;
            textArea.TextView.InlineObjectPlacementAnimationMinimumDistance = 0;
            var manager = RichTextInputManager.Install(textArea);
            manager.ElementFactory = item => new Border
            {
                Width = 40,
                Height = 40
            };
            manager.SetValue(new RichTextInputValue(text, new[]
            {
                new RichTextInputValueItem
                {
                    Offset = "line0\na".Length,
                    Content = RichTextContent.FromCustom("card", 1)
                }
            }));

            textArea.TextView.Measure(new Size(300, 60));
            textArea.TextView.Arrange(new Rect(0, 0, 300, 60));
            var before = textArea.TextView.GetVisualChildren().OfType<RichTextInlineContentControl>().Single().Bounds;

            ((IScrollable)textArea.TextView).Offset = new Vector(0, 16);
            textArea.TextView.Measure(new Size(300, 60));
            textArea.TextView.Arrange(new Rect(0, 0, 300, 60));
            var after = textArea.TextView.GetVisualChildren().OfType<RichTextInlineContentControl>().Single().Bounds;

            Assert.AreEqual(before.Y - 16, after.Y, 0.5);
        }

        [AvaloniaTest]
        public void ImeClientUsesTextViewVisualForCandidateCoordinates()
        {
            var textArea = CreateTextArea("ab");
            var client = GetImeClient(textArea);

            Assert.AreSame(textArea.TextView, client.TextViewVisual);
        }

        [AvaloniaTest]
        public void InlineImePreeditOccupiesLayoutBeforeRichContentWithoutChangingDocument()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            var item = manager.InsertContent(1, RichTextContent.FromCustom("card", 1));
            textArea.Caret.Offset = item.Offset;

            SetPreeditText(textArea, "zhong");
            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(item.Offset));
            var elements = visualLine.Elements.ToArray();
            var preeditIndex = Array.FindIndex(elements, element => element is PreeditTextElement);
            var richContentIndex = Array.FindIndex(elements, element => element is InlineObjectElement);

            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.GreaterOrEqual(preeditIndex, 0);
            Assert.Greater(richContentIndex, preeditIndex);
            Assert.AreEqual(0, elements[preeditIndex].DocumentLength);
            Assert.AreEqual(1, elements[preeditIndex].VisualLength);
        }

        [AvaloniaTest]
        public void InlineImePreeditSupportsUnicodeCompositionText()
        {
            foreach (var preeditText in new[] { "zhong", "にほん", "ㅎㅏㄴ", "привет", "e\u0301" })
            {
                var textArea = CreateTextArea("ab");
                textArea.Caret.Offset = 1;

                SetPreeditText(textArea, preeditText);
                var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
                var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

                Assert.AreEqual("ab", textArea.Document.Text);
                Assert.AreEqual(0, preedit.DocumentLength);
                Assert.AreEqual(1, preedit.VisualLength);
            }
        }

        [AvaloniaTest]
        public void InlineImePreeditRepeatedUpdatesKeepSingleVisualElement()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;

            SetPreeditText(textArea, "ni");
            SetPreeditText(textArea, "nihao");
            SetPreeditText(textArea, "nihao");

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

            Assert.AreEqual("nihao", preedit.Text);
            Assert.AreEqual("ab", textArea.Document.Text);
        }

        [AvaloniaTest]
        public void InlineImePreeditPreservesCompositionCursorOffset()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;

            SetPreeditText(textArea, "abcdef", 3);
            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

            Assert.AreEqual(3, preedit.CursorOffset);
        }

        [AvaloniaTest]
        public async Task SerializedRichTextSnapshotPreservesStyleKey()
        {
            var sourceTextArea = CreateTextArea("");
            var source = RichTextInputManager.Install(sourceTextArea);
            source.InsertCustom("A001", new object(), "order-card");
            var dataObject = new DataObject();
            source.TrySetRichClipboardData(dataObject, new SimpleSegment(0, sourceTextArea.Document.TextLength));

            var targetTextArea = CreateTextArea("");
            var target = RichTextInputManager.Install(targetTextArea);
            var inserted = await target.InsertDataAsync(dataObject, 0, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("order-card", target.Items[0].Content.StyleKey);
        }

        [AvaloniaTest]
        public void LiveValueCanBeAssignedToAnotherRichInput()
        {
            var payload = new object();
            var sourceTextArea = CreateTextArea("send ");
            var source = RichTextInputManager.Install(sourceTextArea);
            source.InsertCustom("A001", payload, "order-card");
            sourceTextArea.Document.Insert(sourceTextArea.Document.TextLength, " done");

            var value = source.GetValue();
            var targetTextArea = CreateTextArea("");
            var target = RichTextInputManager.Install(targetTextArea);
            target.SetValue(value);

            Assert.AreEqual(sourceTextArea.Document.Text, targetTextArea.Document.Text);
            Assert.AreEqual(1, target.Items.Count);
            Assert.AreSame(payload, target.Items[0].Content.Value);
            Assert.AreEqual("order-card", target.Items[0].Content.StyleKey);
        }

        [AvaloniaTest]
        public async Task PasteHandlerCanChooseCustomContent()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.PasteHandler = context =>
            {
                Assert.IsTrue(context.ContainsFormat(DataFormats.Text));
                Assert.IsTrue(context.Formats.Contains(DataFormats.Text));
                context.InsertContents(new[] { RichTextContent.FromCustom("paste-card", 42, "paste-card") });
                return Task.CompletedTask;
            };
            var dataObject = new DataObject();
            dataObject.Set(DataFormats.Text, "raw");

            var inserted = await manager.InsertPasteDataAsync(dataObject, 1, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual("paste-card", manager.Items[0].Content.StyleKey);
        }

        [AvaloniaTest]
        public async Task DropHandlerCanChooseCustomContent()
        {
            var textArea = CreateTextArea("ab");
            var manager = RichTextInputManager.Install(textArea);
            manager.DropHandler = context =>
            {
                Assert.IsTrue(context.ContainsFormat(DataFormats.Text));
                Assert.IsTrue(context.Formats.Contains(DataFormats.Text));
                context.InsertContents(new[] { RichTextContent.FromCustom("drop-card", 99, "drop-card") });
                return Task.CompletedTask;
            };
            var dataObject = new DataObject();
            dataObject.Set(DataFormats.Text, "raw");

            var inserted = await manager.InsertDropDataAsync(dataObject, 1, false);

            Assert.IsTrue(inserted);
            Assert.AreEqual("a" + RichTextInputManager.ObjectReplacementString + "b", textArea.Document.Text);
            Assert.AreEqual("drop-card", manager.Items[0].Content.StyleKey);
        }

        [AvaloniaTest]
        public void ReplaceRangeWithContentCanTurnMentionQueryIntoInlineContent()
        {
            var textArea = CreateTextArea("hello @tim");
            var manager = RichTextInputManager.Install(textArea);

            var item = manager.ReplaceRangeWithContent(6, 4, RichTextContent.FromCustom("@Tim", 7, "mention-user"));

            Assert.AreEqual("hello " + RichTextInputManager.ObjectReplacementString, textArea.Document.Text);
            Assert.AreEqual(6, item.Offset);
            Assert.AreEqual("mention-user", item.Content.StyleKey);
            Assert.AreEqual(textArea.Document.TextLength, textArea.Caret.Offset);
        }

        [AvaloniaTest]
        public void TryGetTextTriggerRangeFindsQueryBeforeCaret()
        {
            var textArea = CreateTextArea("hello @tim");
            var manager = RichTextInputManager.Install(textArea);
            textArea.Caret.Offset = textArea.Document.TextLength;

            var found = manager.TryGetTextTriggerRange('@', out var triggerOffset, out var query);

            Assert.IsTrue(found);
            Assert.AreEqual(6, triggerOffset);
            Assert.AreEqual("tim", query);
        }

        [AvaloniaTest]
        public void TryGetTextTriggerReturnsFullMatch()
        {
            var textArea = CreateTextArea("hello @tim");
            var manager = RichTextInputManager.Install(textArea);
            textArea.Caret.Offset = textArea.Document.TextLength;

            var found = manager.TryGetTextTrigger(
                new RichTextTextTriggerOptions
                {
                    Trigger = '@',
                    IsQueryCharacter = c => char.IsLetterOrDigit(c) || c == '_'
                },
                out var match);

            Assert.IsTrue(found);
            Assert.AreEqual('@', match.Trigger);
            Assert.AreEqual(6, match.TriggerOffset);
            Assert.AreEqual(textArea.Document.TextLength, match.CaretOffset);
            Assert.AreEqual(4, match.Length);
            Assert.AreEqual("tim", match.Query);
        }

        [AvaloniaTest]
        public void TryGetTextTriggerStopsAtRichContent()
        {
            var textArea = CreateTextArea("@tim ");
            var manager = RichTextInputManager.Install(textArea);
            manager.InsertContent(textArea.Document.TextLength, RichTextContent.FromCustom("card", 1));
            textArea.Document.Insert(textArea.Document.TextLength, "x");
            textArea.Caret.Offset = textArea.Document.TextLength;

            var found = manager.TryGetTextTrigger(new RichTextTextTriggerOptions { Trigger = '@' }, out _);

            Assert.IsFalse(found);
        }

        [AvaloniaTest]
        public void CustomElementFactoryIsWrappedForSelection()
        {
            var textArea = CreateTextArea("");
            var manager = RichTextInputManager.Install(textArea);
            manager.ElementFactory = item => new Button { Content = item.Content.DisplayText };
            manager.InsertCustom("styled.file", new object());

            Assert.IsTrue(manager.TryGetItem(0, out var item));
            var control = manager.CreateElement(item);

            Assert.IsInstanceOf<Border>(control);
            Assert.IsInstanceOf<Button>(((Border)control).Child);
        }

        private static TextArea CreateTextArea(string text)
        {
            return new TextArea
            {
                Document = new TextDocument(text)
            };
        }

        private static void SetPreeditText(TextArea textArea, string text, int? cursorOffset = null)
        {
            var client = GetImeClient(textArea);
            if (cursorOffset.HasValue)
                client.GetType().GetMethod("SetPreeditText", new[] { typeof(string), typeof(int?) }).Invoke(client, new object[] { text, cursorOffset });
            else
                client.SetPreeditText(text);
        }

        private static TextInputMethodClient GetImeClient(TextArea textArea)
        {
            var field = typeof(TextArea).GetField("_imClient", BindingFlags.Instance | BindingFlags.NonPublic);
            return (TextInputMethodClient)field.GetValue(textArea);
        }

        private static byte[] CreateSinglePixelDib32()
        {
            var bytes = new byte[44];
            WriteInt32(bytes, 0, 40);
            WriteInt32(bytes, 4, 1);
            WriteInt32(bytes, 8, 1);
            WriteUInt16(bytes, 12, 1);
            WriteUInt16(bytes, 14, 32);
            WriteInt32(bytes, 20, 4);
            bytes[40] = 0xff;
            bytes[41] = 0;
            bytes[42] = 0;
            bytes[43] = 0xff;
            return bytes;
        }

        private static byte[] CreateSinglePixelBmp32()
        {
            var dib = CreateSinglePixelDib32();
            var bytes = new byte[14 + dib.Length];
            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            WriteInt32(bytes, 2, bytes.Length);
            WriteInt32(bytes, 10, 14 + 40);
            Buffer.BlockCopy(dib, 0, bytes, 14, dib.Length);
            return bytes;
        }

        private static byte[] CreateSinglePixelTiffRgb()
        {
            const int ifdOffset = 8;
            const int entryCount = 10;
            const int bitsPerSampleOffset = ifdOffset + 2 + (entryCount * 12) + 4;
            const int pixelOffset = bitsPerSampleOffset + 6;
            var bytes = new byte[pixelOffset + 3];
            bytes[0] = (byte)'I';
            bytes[1] = (byte)'I';
            WriteUInt16(bytes, 2, 42);
            WriteInt32(bytes, 4, ifdOffset);
            WriteUInt16(bytes, ifdOffset, entryCount);

            var entryOffset = ifdOffset + 2;
            WriteTiffEntry(bytes, ref entryOffset, 256, 4, 1, 1); // ImageWidth
            WriteTiffEntry(bytes, ref entryOffset, 257, 4, 1, 1); // ImageLength
            WriteTiffEntry(bytes, ref entryOffset, 258, 3, 3, bitsPerSampleOffset); // BitsPerSample
            WriteTiffEntry(bytes, ref entryOffset, 259, 3, 1, 1); // Compression: none
            WriteTiffEntry(bytes, ref entryOffset, 262, 3, 1, 2); // PhotometricInterpretation: RGB
            WriteTiffEntry(bytes, ref entryOffset, 273, 4, 1, pixelOffset); // StripOffsets
            WriteTiffEntry(bytes, ref entryOffset, 277, 3, 1, 3); // SamplesPerPixel
            WriteTiffEntry(bytes, ref entryOffset, 278, 4, 1, 1); // RowsPerStrip
            WriteTiffEntry(bytes, ref entryOffset, 279, 4, 1, 3); // StripByteCounts
            WriteTiffEntry(bytes, ref entryOffset, 284, 3, 1, 1); // PlanarConfiguration
            WriteInt32(bytes, entryOffset, 0);

            WriteUInt16(bytes, bitsPerSampleOffset, 8);
            WriteUInt16(bytes, bitsPerSampleOffset + 2, 8);
            WriteUInt16(bytes, bitsPerSampleOffset + 4, 8);
            bytes[pixelOffset] = 0xff;
            return bytes;
        }

        private static void WriteTiffEntry(byte[] bytes, ref int offset, ushort tag, ushort type, int count, int value)
        {
            WriteUInt16(bytes, offset, tag);
            WriteUInt16(bytes, offset + 2, type);
            WriteInt32(bytes, offset + 4, count);
            if (type == 3 && count == 1)
            {
                WriteUInt16(bytes, offset + 8, (ushort)value);
                WriteUInt16(bytes, offset + 10, 0);
            }
            else
            {
                WriteInt32(bytes, offset + 8, value);
            }

            offset += 12;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }
    }
}
