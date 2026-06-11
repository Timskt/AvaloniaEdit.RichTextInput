using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
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
    }
}
