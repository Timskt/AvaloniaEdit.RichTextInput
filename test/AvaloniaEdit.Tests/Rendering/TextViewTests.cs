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
using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
            Assert.IsFalse(style.Underline);
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
        public void Inline_Object_Baseline_And_Arrange_Offsets_Are_Applied()
        {
            var baselineControl = new Border { Width = 8, Height = 8 };
            var arrangedControl = new Border { Width = 8, Height = 8 };
            var baselineTextView = CreateOffsetTextView(baselineControl, 4, default);
            var arrangedTextView = CreateOffsetTextView(arrangedControl, 0, new Vector(3, 5));

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
            Assert.AreEqual(new Vector(3, 5), arrangedRun.ArrangeOffset);
            Assert.AreEqual(new Vector(3, 5).X, arrangedControl.Bounds.X - baselineControl.Bounds.X, 0.501);
            Assert.Greater(arrangedControl.Bounds.Y, baselineControl.Bounds.Y);
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
                Vector arrangeOffset = default)
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
