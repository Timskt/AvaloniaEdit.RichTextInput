using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace AvaloniaEdit.Tests.Rendering
{
    internal class TextViewTests
    {
        // https://github.com/AvaloniaUI/Avalonia/blob/master/src/Headless/Avalonia.Headless/HeadlessPlatformStubs.cs#L126
        private const int HeadlessGlyphAdvance = 8;
        
        [AvaloniaTest]
        public void Visual_Line_Should_Create_Two_Text_Lines_When_Wrapping()
        {
            TextView textView = new TextView();

            TextDocument document = new TextDocument("hello world".ToCharArray());   

            textView.Document = document;

            ((ILogicalScrollable)textView).CanHorizontallyScroll = false;
            textView.Width = HeadlessGlyphAdvance * 8;

            textView.Measure(Size.Infinity);

            VisualLine visualLine = textView.GetOrConstructVisualLine(document.Lines[0]);

            Assert.AreEqual(2, visualLine.TextLines.Count);
            Assert.AreEqual("hello ", new string(visualLine.TextLines[0].TextRuns[0].Text.Span));
            Assert.AreEqual("world", new string(visualLine.TextLines[1].TextRuns[0].Text.Span));
        }

        [AvaloniaTest]
        public void Visual_Line_Should_Create_One_Text_Lines_When_Not_Wrapping()
        {
            TextView textView = new TextView();

            TextDocument document = new TextDocument("hello world".ToCharArray());

            textView.Document = document;
            textView.EnsureVisualLines();
            ((ILogicalScrollable)textView).CanHorizontallyScroll = false;
            textView.Width = HeadlessGlyphAdvance * 500;

            textView.Measure(Size.Infinity);

            VisualLine visualLine = textView.GetOrConstructVisualLine(document.Lines[0]);

            Assert.AreEqual(1, visualLine.TextLines.Count);
            Assert.AreEqual("hello world", new string(visualLine.TextLines[0].TextRuns[0].Text.Span));
        }

        [AvaloniaTest]
        public void Custom_Link_Generator_Can_Create_Ip_Link()
        {
            var textView = new TextView();
            var document = new TextDocument("server 127.0.0.1 ready");
            textView.Document = document;
            textView.ElementGenerators.Add(new LinkElementGenerator(
                new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
                match => new Uri("im://ip/" + match.Value),
                "ip"));

            var visualLine = textView.GetOrConstructVisualLine(document.Lines[0]);
            var link = visualLine.Elements.OfType<VisualLineLinkText>().Single();

            Assert.AreEqual("127.0.0.1", link.MatchedText);
            Assert.AreEqual("ip", link.LinkKind);
            Assert.AreEqual("im://ip/127.0.0.1", link.NavigateUri.ToString());
        }

        [AvaloniaTest]
        public void Built_In_Ip_Link_Generator_Creates_Ip_Link()
        {
            var textView = new TextView();
            var document = new TextDocument("server 127.0.0.1 invalid 999.0.0.1");
            textView.Document = document;
            textView.ElementGenerators.Add(LinkElementGenerator.CreateIpAddressGenerator());

            var visualLine = textView.GetOrConstructVisualLine(document.Lines[0]);
            var link = visualLine.Elements.OfType<VisualLineLinkText>().Single();

            Assert.AreEqual("127.0.0.1", link.MatchedText);
            Assert.AreEqual("ip", link.LinkKind);
            Assert.AreEqual("im://ip/127.0.0.1", link.NavigateUri.ToString());
        }

        [AvaloniaTest]
        public void Link_Style_Selector_Can_Style_By_Link_Kind()
        {
            var textView = new TextView
            {
                LinkTextStyleSelector = context => context.LinkKind == "ip"
                    ? new LinkTextStyle
                    {
                        ForegroundBrush = Brushes.DarkOrange,
                        BackgroundBrush = Brushes.Transparent,
                        Underline = false
                    }
                    : null
            };
            var document = new TextDocument("server 127.0.0.1 ready");
            textView.Document = document;
            textView.ElementGenerators.Add(new LinkElementGenerator(
                new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
                match => new Uri("im://ip/" + match.Value),
                "ip"));

            var visualLine = textView.GetOrConstructVisualLine(document.Lines[0]);
            var link = visualLine.Elements.OfType<VisualLineLinkText>().Single();
            var style = textView.GetLinkTextStyle(new LinkTextStyleContext(
                textView,
                link.NavigateUri,
                link.MatchedText,
                link.LinkKind,
                link));

            Assert.AreSame(Brushes.DarkOrange, style.ForegroundBrush);
            Assert.IsFalse(style.Underline.Value);
        }

        [AvaloniaTest]
        public void Current_Line_Style_Selector_Can_Customize_Rectangle()
        {
            var textView = new TextView();
            var document = new TextDocument("hello");
            var border = new Pen(Brushes.Red, 2);
            textView.Document = document;
            textView.CurrentLineHighlightStyleSelector = context => new CurrentLineHighlightStyle
            {
                BackgroundBrush = Brushes.Yellow,
                BorderPen = border,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(1),
                ExtendToViewportWidth = false,
                MinWidth = 40
            };

            var visualLine = textView.GetOrConstructVisualLine(document.Lines[0]);
            var style = textView.GetCurrentLineHighlightStyle(visualLine, new Rect(0, 0, 100, 20), 32);

            Assert.AreSame(Brushes.Yellow, style.BackgroundBrush);
            Assert.AreSame(border, style.BorderPen);
            Assert.AreEqual(new CornerRadius(3), style.CornerRadius);
            Assert.AreEqual(new Thickness(1), style.Margin);
            Assert.IsFalse(style.ExtendToViewportWidth);
            Assert.AreEqual(40, style.MinWidth);
        }

        [AvaloniaTest]
        public void Inline_Object_Arrange_Uses_Line_Content_Alignment_Offset()
        {
            var inlineControl = new Border
            {
                Width = 8,
                Height = 8
            };
            var textView = new TextView
            {
                Document = new TextDocument("a\ufffc"),
                Width = 200,
                Height = 80
            };
            textView.Options.LineHeightFactor = 2;
            textView.Options.LineContentVerticalAlignment = LineContentVerticalAlignment.Bottom;
            textView.ElementGenerators.Add(new InlineObjectTestGenerator(1, inlineControl, InlineObjectVerticalAlignment.Bottom));

            textView.Measure(new Size(200, 80));
            textView.Arrange(new Rect(0, 0, 200, 80));

            var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
            var textLine = visualLine.TextLines[0];
            var inlineRun = textLine.TextRuns.OfType<InlineObjectRun>().Single();
            var lineHeight = Math.Max(textLine.Height, textView.DefaultLineHeight);
            var textOffset = Math.Max(0, lineHeight - textLine.Height);
            var unalignedY = textLine.Baseline - inlineRun.Baseline;

            Assert.Greater(textOffset, 0);
            Assert.Greater(inlineControl.Bounds.Y, unalignedY + textOffset / 2);
            Assert.Greater(inlineControl.Bounds.Y, 0);
        }

        [AvaloniaTest]
        public void Inline_Object_Layout_Remains_Inside_First_Line_For_Font_And_Alignment_Matrix()
        {
            var fontSizes = new[] { 12d, 16d, 20d };
            var lineHeightFactors = new[] { 1d, 1.16d, 1.5d };
            var lineAlignments = new[]
            {
                LineContentVerticalAlignment.Top,
                LineContentVerticalAlignment.Center,
                LineContentVerticalAlignment.Bottom
            };
            var objectAlignments = new[]
            {
                InlineObjectVerticalAlignment.Top,
                InlineObjectVerticalAlignment.Center,
                InlineObjectVerticalAlignment.Bottom
            };
            var objectHeights = new[] { 10d, 24d, 48d };

            foreach (var fontSize in fontSizes)
            foreach (var lineHeightFactor in lineHeightFactors)
            foreach (var lineAlignment in lineAlignments)
            foreach (var objectAlignment in objectAlignments)
            foreach (var objectHeight in objectHeights)
            {
                var inlineControl = new Border
                {
                    Width = 16,
                    Height = objectHeight
                };
                var textView = new TextView
                {
                    Document = new TextDocument("text \ufffc"),
                    Width = 300,
                    Height = 160,
                    FontSize = fontSize
                };
                textView.Options.LineHeightFactor = lineHeightFactor;
                textView.Options.LineContentVerticalAlignment = lineAlignment;
                textView.ElementGenerators.Add(new InlineObjectTestGenerator(5, inlineControl, objectAlignment));

                textView.Measure(new Size(300, 160));
                textView.Arrange(new Rect(0, 0, 300, 160));

                var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
                Assert.GreaterOrEqual(
                    inlineControl.Bounds.Y,
                    -0.001,
                    $"font={fontSize}, factor={lineHeightFactor}, line={lineAlignment}, object={objectAlignment}, height={objectHeight}");
                Assert.LessOrEqual(
                    inlineControl.Bounds.Bottom,
                    visualLine.Height + 0.501,
                    $"font={fontSize}, factor={lineHeightFactor}, line={lineAlignment}, object={objectAlignment}, height={objectHeight}");
            }
        }

        [AvaloniaTest]
        public void Inline_Object_Baseline_Remains_Text_Aligned_When_Tall_Sibling_Expands_Line()
        {
            var tallControl = new Border
            {
                Width = 40,
                Height = 80
            };
            var smallControl = new Border
            {
                Width = 16,
                Height = 12
            };
            var textView = new TextView
            {
                Document = new TextDocument("a\ufffcb\ufffc"),
                Width = 300,
                Height = 160,
                FontSize = 16
            };
            textView.Options.LineContentVerticalAlignment = LineContentVerticalAlignment.Bottom;
            textView.ElementGenerators.Add(new MultipleInlineObjectTestGenerator(
                new InlineObjectTestEntry(1, tallControl, InlineObjectVerticalAlignment.Bottom),
                new InlineObjectTestEntry(3, smallControl, InlineObjectVerticalAlignment.Baseline)));

            textView.Measure(new Size(300, 160));
            textView.Arrange(new Rect(0, 0, 300, 160));

            var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
            var textLine = visualLine.TextLines[0];
            var runs = textLine.TextRuns.OfType<InlineObjectRun>().ToArray();
            var smallRun = runs.Single(run => ReferenceEquals(run.Element, smallControl));
            var lineHeight = Math.Max(textLine.Height, textView.DefaultLineHeight);
            var textOffset = visualLine.GetTextLineDrawingOffset(textLine, lineHeight);
            var expectedBaselineY = textOffset + textLine.Baseline - smallRun.ArrangementBaseline;
            expectedBaselineY = Math.Max(0, Math.Min(expectedBaselineY, lineHeight - smallControl.DesiredSize.Height));
            var centeredY = Math.Max(0, (lineHeight - smallControl.DesiredSize.Height) / 2);

            Assert.AreEqual(expectedBaselineY, smallControl.Bounds.Y, 0.501);
            Assert.AreEqual(
                visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.Baseline),
                smallControl.Bounds.Bottom,
                0.501);
            Assert.Greater(
                Math.Abs(smallControl.Bounds.Y - centeredY),
                1,
                "A baseline-aligned small control must not be centered by a tall inline sibling.");
        }

        [AvaloniaTest]
        public void Tall_Inline_Object_Does_Not_Offset_Overstrike_Caret_From_Drawn_Text()
        {
            var textArea = new AvaloniaEdit.Editing.TextArea
            {
                Document = new TextDocument("ab"),
                Width = 300,
                Height = 160,
                OverstrikeMode = true
            };
            var manager = AvaloniaEdit.RichTextInput.RichTextInputManager.Install(textArea);
            manager.ElementFactory = _ => new Border
            {
                Width = 80,
                Height = 60
            };
            manager.InsertContent(1, AvaloniaEdit.RichTextInput.RichTextContent.FromCustom("button", 1));
            textArea.Caret.Offset = 0;
            textArea.Measure(new Size(300, 160));
            textArea.Arrange(new Rect(0, 0, 300, 160));

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var textLine = visualLine.TextLines[0];
            var nextPosition = visualLine.GetNextCaretPosition(
                textArea.Caret.VisualColumn,
                AvaloniaEdit.Document.LogicalDirection.Forward,
                CaretPositioningMode.Normal,
                true);
            var textBounds = textLine.GetTextBounds(
                textArea.Caret.VisualColumn,
                nextPosition - textArea.Caret.VisualColumn)[0].Rectangle;
            var drawingOriginY = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.Baseline)
                - textLine.Baseline;

            var caretRectangle = textArea.Caret.CalculateCaretRectangle();

            Assert.AreEqual(drawingOriginY + textBounds.Y, caretRectangle.Y, 0.501);
        }

        [AvaloniaTest]
        public void Tall_Inline_Object_Line_Hit_Test_Uses_The_Same_Text_Line_Across_Its_Full_Height()
        {
            var control = new Border { Width = 40, Height = 80 };
            var textView = CreateTextViewWithInlineObject(
                control,
                "a\ufffcb",
                InlineObjectVerticalAlignment.Bottom);
            var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
            var textLine = visualLine.TextLines[0];
            var x = visualLine.GetTextLineVisualXPosition(textLine, 1);
            var lineTop = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineTop);
            var lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineBottom);

            Assert.AreEqual(
                visualLine.GetVisualColumn(new Point(x, lineTop + 0.1), false),
                visualLine.GetVisualColumn(new Point(x, lineBottom - 0.1), false));
        }

        [AvaloniaTest]
        public void Inline_Object_Baseline_Does_Not_Move_Text_Line_Baseline_When_Button_Is_Inserted()
        {
            var plainTextView = CreateTextViewWithInlineObject(null, "ab");
            var button = new Button
            {
                Content = "Click me",
                Padding = new Thickness(10, 2),
                MinHeight = 0,
                Height = 40
            };
            var buttonTextView = CreateTextViewWithInlineObject(
                button,
                "a\ufffcb",
                InlineObjectVerticalAlignment.Baseline);

            var plainTextLine = plainTextView.GetOrConstructVisualLine(plainTextView.Document.Lines[0]).TextLines[0];
            var buttonTextLine = buttonTextView.GetOrConstructVisualLine(buttonTextView.Document.Lines[0]).TextLines[0];

            Assert.AreEqual(plainTextLine.Baseline, buttonTextLine.Baseline, 0.501);
        }

        [AvaloniaTest]
        public void Bottom_Aligned_Button_Keeps_Text_At_The_Bottom_Of_The_Expanded_Line()
        {
            var button = new Button
            {
                Content = "Click me",
                Padding = new Thickness(10, 2),
                MinHeight = 0,
                Height = 40
            };
            var textView = new TextView
            {
                Document = new TextDocument("a\ufffcb"),
                Width = 300,
                Height = 160,
                FontSize = 16
            };
            textView.Options.LineContentVerticalAlignment = LineContentVerticalAlignment.Bottom;
            textView.ElementGenerators.Add(new InlineObjectTestGenerator(
                1,
                button,
                InlineObjectVerticalAlignment.Bottom));

            textView.Measure(new Size(300, 160));
            textView.Arrange(new Rect(0, 0, 300, 160));

            var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
            var textLine = visualLine.TextLines[0];
            var inlineRun = textLine.TextRuns.OfType<InlineObjectRun>().Single();
            var lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineBottom);
            var textBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom);

            Assert.AreEqual(lineBottom, button.Bounds.Bottom, 0.501);
            Assert.AreEqual(lineBottom, textBottom, 1.001);
        }

        [AvaloniaTest]
        public void Bottom_Aligned_Button_Stays_With_Text_When_Tall_Sibling_Expands_Line()
        {
            var image = new Border
            {
                Width = 80,
                Height = 100
            };
            var button = new Button
            {
                Content = "Click me",
                Padding = new Thickness(10, 2),
                MinHeight = 0
            };
            var textView = new TextView
            {
                Document = new TextDocument("a\ufffcb\ufffcc"),
                Width = 400,
                Height = 180,
                FontSize = 16
            };
            textView.Options.LineContentVerticalAlignment = LineContentVerticalAlignment.Bottom;
            textView.ElementGenerators.Add(new MultipleInlineObjectTestGenerator(
                new InlineObjectTestEntry(1, image, InlineObjectVerticalAlignment.Bottom),
                new InlineObjectTestEntry(3, button, InlineObjectVerticalAlignment.Bottom)));

            textView.Measure(new Size(400, 180));
            textView.Arrange(new Rect(0, 0, 400, 180));

            var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
            var textLine = visualLine.TextLines[0];
            var lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineBottom);
            var textBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.TextBottom);

            Assert.AreEqual(lineBottom, image.Bounds.Bottom, 0.501);
            Assert.AreEqual(lineBottom, button.Bounds.Bottom, 0.501);
            Assert.AreEqual(lineBottom, textBottom, 1.001);
        }

        private static TextView CreateTextViewWithInlineObject(
            Control inlineControl,
            string text,
            InlineObjectVerticalAlignment alignment = InlineObjectVerticalAlignment.Baseline)
        {
            var textView = new TextView
            {
                Document = new TextDocument(text),
                Width = 300,
                Height = 160,
                FontSize = 16
            };

            if (inlineControl != null)
            {
                textView.ElementGenerators.Add(new InlineObjectTestGenerator(
                    1,
                    inlineControl,
                    alignment));
            }

            textView.Measure(new Size(300, 160));
            textView.Arrange(new Rect(0, 0, 300, 160));
            return textView;
        }

        [AvaloniaTest]
        public void Inline_Object_Baseline_And_Arrange_Offsets_Are_Applied()
        {
            var baselineControl = new Border { Width = 8, Height = 8 };
            var arrangedControl = new Border { Width = 8, Height = 8 };
            var baselineTextView = CreateOffsetTextView(baselineControl, 4, default(Vector));
            var arrangedTextView = CreateOffsetTextView(arrangedControl, 0, new Vector(3, -2));

            baselineTextView.Measure(new Size(200, 80));
            baselineTextView.Arrange(new Rect(0, 0, 200, 80));
            arrangedTextView.Measure(new Size(200, 80));
            arrangedTextView.Arrange(new Rect(0, 0, 200, 80));

            var baselineRun = baselineTextView
                .GetOrConstructVisualLine(baselineTextView.Document.Lines[0])
                .TextLines[0]
                .TextRuns
                .OfType<InlineObjectRun>()
                .Single();
            var arrangedRun = arrangedTextView
                .GetOrConstructVisualLine(arrangedTextView.Document.Lines[0])
                .TextLines[0]
                .TextRuns
                .OfType<InlineObjectRun>()
                .Single();

            Assert.AreEqual(4, baselineRun.BaselineOffset);
            Assert.AreEqual(new Vector(3, -2), arrangedRun.ArrangeOffset);
            Assert.AreEqual(new Vector(3, -2).X, arrangedControl.Bounds.X - baselineControl.Bounds.X, 0.501);
            Assert.Less(arrangedControl.Bounds.Y, baselineControl.Bounds.Y);
        }

        [AvaloniaTest]
        public void Inline_Object_Arrange_Offset_Is_Clamped_To_Line_Box()
        {
            foreach (var offsetY in new[] { -1000d, 1000d })
            {
                var control = new Border
                {
                    Width = 8,
                    Height = 8
                };
                var textView = CreateOffsetTextView(control, 0, new Vector(0, offsetY));
                textView.Measure(new Size(200, 80));
                textView.Arrange(new Rect(0, 0, 200, 80));

                var visualLine = textView.GetOrConstructVisualLine(textView.Document.Lines[0]);
                var textLine = visualLine.TextLines[0];
                var lineTop = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineTop);
                var lineBottom = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.LineBottom);
                var expectedY = offsetY < 0 ? lineTop : lineBottom - control.DesiredSize.Height;

                Assert.AreEqual(expectedY, control.Bounds.Y, 0.501);
                Assert.GreaterOrEqual(control.Bounds.Top, lineTop - 0.001);
                Assert.LessOrEqual(control.Bounds.Bottom, lineBottom + 0.001);
            }
        }

        private static TextView CreateOffsetTextView(Control inlineControl, double baselineOffset, Vector arrangeOffset)
        {
            var textView = new TextView
            {
                Document = new TextDocument("a\ufffc"),
                Width = 200,
                Height = 80
            };
            textView.Options.LineHeightFactor = 2;
            textView.Options.LineContentVerticalAlignment = LineContentVerticalAlignment.Bottom;
            textView.ElementGenerators.Add(new InlineObjectTestGenerator(
                1,
                inlineControl,
                InlineObjectVerticalAlignment.Bottom,
                baselineOffset,
                arrangeOffset));
            return textView;
        }

        private sealed class MultipleInlineObjectTestGenerator : VisualLineElementGenerator
        {
            private readonly InlineObjectTestEntry[] _entries;

            public MultipleInlineObjectTestGenerator(params InlineObjectTestEntry[] entries)
            {
                _entries = entries;
            }

            public override int GetFirstInterestedOffset(int startOffset)
            {
                var nextOffset = -1;
                foreach (var entry in _entries)
                {
                    if (entry.Offset >= startOffset && (nextOffset < 0 || entry.Offset < nextOffset))
                        nextOffset = entry.Offset;
                }

                return nextOffset;
            }

            public override VisualLineElement ConstructElement(int offset)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Offset == offset)
                        return new InlineObjectElement(1, entry.Control, entry.Alignment);
                }

                return null;
            }
        }

        private sealed class InlineObjectTestEntry
        {
            public InlineObjectTestEntry(int offset, Control control, InlineObjectVerticalAlignment alignment)
            {
                Offset = offset;
                Control = control;
                Alignment = alignment;
            }

            public int Offset { get; }
            public Control Control { get; }
            public InlineObjectVerticalAlignment Alignment { get; }
        }

        private sealed class InlineObjectTestGenerator : VisualLineElementGenerator
        {
            private readonly int _offset;
            private readonly Control _control;
            private readonly InlineObjectVerticalAlignment _alignment;
            private readonly double _baselineOffset;
            private readonly Vector _arrangeOffset;

            public InlineObjectTestGenerator(
                int offset,
                Control control,
                InlineObjectVerticalAlignment alignment,
                double baselineOffset = 0,
                Vector arrangeOffset = default(Vector))
            {
                _offset = offset;
                _control = control;
                _alignment = alignment;
                _baselineOffset = baselineOffset;
                _arrangeOffset = arrangeOffset;
            }

            public override int GetFirstInterestedOffset(int startOffset)
            {
                return startOffset <= _offset ? _offset : -1;
            }

            public override VisualLineElement ConstructElement(int offset)
            {
                return offset == _offset
                    ? new InlineObjectElement(1, _control, _alignment, _baselineOffset, _arrangeOffset)
                    : null;
            }
        }
    }
}
