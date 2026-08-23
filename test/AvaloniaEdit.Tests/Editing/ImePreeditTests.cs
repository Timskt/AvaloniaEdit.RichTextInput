using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NUnit.Framework;

namespace AvaloniaEdit.Tests.Editing
{
    [TestFixture]
    public class ImePreeditTests
    {
        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void SetImePreeditDoesNotChangeDocumentAndClearRemovesIt()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;

            textArea.SetImePreeditText("にほん", 2);

            Assert.AreEqual("ab", textArea.Document.Text);
            Assert.IsTrue(textArea.HasImePreedit);

            textArea.ClearImePreedit();

            Assert.AreEqual("ab", textArea.Document.Text);
            Assert.IsFalse(textArea.HasImePreedit);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditHasCursorAndNormalizedClauseDecorations()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            var brush = Brushes.Red;

            textArea.SetImePreeditText("abcdef", 3, new[]
            {
                new ImePreeditClause(1, 2, ImePreeditUnderlineStyle.Thick, brush),
                new ImePreeditClause(4, 2, ImePreeditUnderlineStyle.Double)
            });

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

            Assert.AreEqual(3, preedit.CursorOffset);
            Assert.AreEqual(4, preedit.Clauses.Count);
            Assert.AreEqual(0, preedit.Clauses[0].Start);
            Assert.AreEqual(1, preedit.Clauses[0].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Solid, preedit.Clauses[0].UnderlineStyle);
            Assert.AreEqual(1, preedit.Clauses[1].Start);
            Assert.AreEqual(2, preedit.Clauses[1].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Thick, preedit.Clauses[1].UnderlineStyle);
            Assert.AreSame(brush, preedit.Clauses[1].UnderlineBrush);
            Assert.AreEqual(3, preedit.Clauses[2].Start);
            Assert.AreEqual(1, preedit.Clauses[2].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Solid, preedit.Clauses[2].UnderlineStyle);
            Assert.AreEqual(4, preedit.Clauses[3].Start);
            Assert.AreEqual(2, preedit.Clauses[3].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Double, preedit.Clauses[3].UnderlineStyle);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditDefaultsCursorToEnd()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("abcdef");

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(1));
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();

            Assert.AreEqual(6, preedit.CursorOffset);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InvalidOrOverlappingClausesAreRejected()
        {
            var textArea = CreateTextArea("ab");
            Assert.Throws<ArgumentOutOfRangeException>(() => textArea.SetImePreeditText(
                "abc", clauses: new[] { new ImePreeditClause(2, 2) }));
            Assert.Throws<ArgumentException>(() => textArea.SetImePreeditText(
                "abcd", clauses: new[]
                {
                    new ImePreeditClause(0, 3),
                    new ImePreeditClause(2, 2)
                }));
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InvalidClausesDoNotReplaceTheActivePreedit()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("旧组合", 1);

            Assert.Throws<ArgumentException>(() => textArea.SetImePreeditText(
                "新组合",
                clauses: new[]
                {
                    new ImePreeditClause(0, 3),
                    new ImePreeditClause(2, 2)
                }));

            Assert.IsTrue(textArea.HasImePreedit);
            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();
            Assert.AreEqual("旧组合", preedit.Text);
            Assert.AreEqual(1, preedit.CursorOffset);
        }

        [Test]
        public void SliceClausesKeepsLocalRangesAcrossWrappedChunks()
        {
            var red = Brushes.Red;
            var clauses = new[]
            {
                new ImePreeditClause(0, 4, ImePreeditUnderlineStyle.Solid),
                new ImePreeditClause(4, 4, ImePreeditUnderlineStyle.Dashed, red)
            };

            var sliced = PreeditLayer.SliceClauses(clauses, 3, 4);

            Assert.AreEqual(2, sliced.Count);
            Assert.AreEqual(0, sliced[0].Start);
            Assert.AreEqual(1, sliced[0].Length);
            Assert.AreEqual(1, sliced[1].Start);
            Assert.AreEqual(3, sliced[1].Length);
            Assert.AreEqual(ImePreeditUnderlineStyle.Dashed, sliced[1].UnderlineStyle);
            Assert.AreSame(red, sliced[1].UnderlineBrush);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditWrapsUsesCaretXAndDrawsOneCursor()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 40;
            textArea.Height = 100;
            textArea.Background = Brushes.Black;
            textArea.Caret.Offset = 0;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 40, 100);
            var caretX = textArea.Caret.CalculateCaretRectangle().X;

            textArea.SetImePreeditText("日本語入力テスト", 4);
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 40, 100);

            Assert.Greater(layer.LastRenderedChunkCount, 1);
            Assert.AreEqual(layer.LastRenderedChunkCount, layer.LastRenderedChunkOrigins.Count);
            Assert.AreEqual(layer.LastRenderedChunkCount, layer.LastRenderedChunkLengths.Count);
            Assert.AreEqual(caretX - textArea.TextView.HorizontalOffset, layer.LastRenderedChunkOrigins[0].X, 0.01);
            Assert.AreEqual(1, layer.LastRenderedCursorCount);
            Assert.Greater(layer.LastRenderedBackgroundFillCount, 0);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditUsesBodyBaselineWhenVisualLineIsAvailable()
        {
            var textArea = CreateTextArea("日本語");
            textArea.Width = 300;
            textArea.Height = 100;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 300, 100);

            textArea.SetImePreeditText("abc");
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 300, 100);

            Assert.IsNotNull(layer.LastBodyBaseline);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditUsesTextBaselineOnTallInlineObjectLine()
        {
            var textArea = CreateTextArea("a");
            var manager = AvaloniaEdit.RichTextInput.RichTextInputManager.Install(textArea);
            manager.ElementFactory = _ => new Border
            {
                Width = 80,
                Height = 100
            };
            manager.InsertContent(1, AvaloniaEdit.RichTextInput.RichTextContent.FromCustom("card", "tall"));

            textArea.Width = 300;
            textArea.Height = 160;
            textArea.Caret.Offset = 1;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 300, 160);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var textLine = visualLine.GetTextLine(textArea.Caret.VisualColumn, textArea.Caret.Position.IsAtEndOfLine);
            var expectedBaseline = visualLine.GetTextLineVisualYPosition(textLine, VisualYPosition.Baseline)
                - textArea.TextView.VerticalOffset;

            textArea.SetImePreeditText("かな");
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 300, 160);

            Assert.AreEqual(expectedBaseline, layer.LastBodyBaseline.Value, 0.01);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditHandlesNarrowViewportAndGraphemeClusters()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 1;
            textArea.Height = 100;
            textArea.Caret.Offset = 0;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 1, 100);

            textArea.SetImePreeditText("😀e\u0301");
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 1, 100);

            Assert.AreEqual(2, layer.LastRenderedChunkCount);
            Assert.AreEqual(new[] { 2, 2 }, layer.LastRenderedChunkLengths.ToArray());
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditHandlesHardLineBreaksWithoutLoopingOrMergingRows()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 300;
            textArea.Height = 160;
            textArea.Caret.Offset = 0;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 300, 160);

            const string preedit = "ab\r\ncd\nef";
            textArea.SetImePreeditText(preedit, preedit.Length);
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 300, 160);

            Assert.AreEqual(3, layer.LastRenderedChunkCount);
            Assert.AreEqual(new[] { 2, 2, 2 }, layer.LastRenderedChunkLengths.ToArray());
            Assert.Less(layer.LastRenderedChunkOrigins[0].Y, layer.LastRenderedChunkOrigins[1].Y);
            Assert.Less(layer.LastRenderedChunkOrigins[1].Y, layer.LastRenderedChunkOrigins[2].Y);
            Assert.AreEqual(1, layer.LastRenderedCursorCount);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditAtViewportRightEdgeStartsOnNextRowAndMakesProgress()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 16;
            textArea.Height = 100;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 16, 100);

            textArea.SetImePreeditText("日本", 2);
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 16, 100);

            Assert.GreaterOrEqual(layer.LastRenderedChunkCount, 1);
            Assert.AreEqual(2, layer.LastRenderedChunkLengths.Sum());
            Assert.AreEqual(0, layer.LastRenderedChunkOrigins[0].X, 0.01);
            Assert.AreEqual(1, layer.LastRenderedCursorCount);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditLongTextWrapsBeyondTheFirstVisualRow()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = false;
            ArrangeTextView(textArea, 48, 240);

            const string preedit = "zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            ArrangeTextView(textArea, 48, 240);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            Assert.GreaterOrEqual(visualLine.TextLines.Count, 3);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditWrapsWhenHorizontalScrollingIsEnabled()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 48, 240);

            const string preedit = "zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            ArrangeTextView(textArea, 48, 240);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            Assert.GreaterOrEqual(visualLine.TextLines.Count, 3);
            Assert.AreEqual(preedit.Length, visualLine.Elements.OfType<PreeditTextElement>().Single().VisualLength);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditUsesRemainingWidthAndConsumesAllText()
        {
            var textArea = CreateTextArea("prefix中文suffix");
            textArea.Width = 160;
            textArea.Height = 320;
            textArea.Caret.Offset = "prefix".Length;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 160, 320);

            const string preedit = "zhonghuarenmingongheguozhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            ArrangeTextView(textArea, 160, 320);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var consumedLength = visualLine.TextLines.Sum(line => line.Length);
            var firstLine = visualLine.TextLines[0];
            var renderedText = string.Concat(
                visualLine.TextLines.SelectMany(line => line.TextRuns).Select(run => run.Text.ToString()));
            Assert.GreaterOrEqual(consumedLength, visualLine.VisualLengthWithEndOfLineMarker);
            Assert.That(visualLine.TextLines,
                Has.All.Matches<Avalonia.Media.TextFormatting.TextLine>(line => line.Length > 0));
            Assert.AreEqual("prefix" + preedit + "中文suffix", renderedText,
                "Wrapped preedit must not hide either the composition or following document text.");
            Assert.GreaterOrEqual(
                firstLine.WidthIncludingTrailingWhitespace,
                160 - textArea.TextView.WideSpaceWidth,
                "The composition should fill the first row up to approximately one glyph before wrapping.");
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditIncrementalGrowthAlwaysMakesFormattingProgress()
        {
            var textArea = CreateTextArea("prefix中文suffix");
            textArea.Width = 48;
            textArea.Height = 600;
            textArea.Caret.Offset = "prefix".Length;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 48, 600);

            for (var length = 1; length <= 256; length++)
            {
                var preedit = new string('a', length);
                textArea.SetImePreeditText(preedit, preedit.Length);
                ArrangeTextView(textArea, 48, 600);

                var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
                Assert.That(visualLine.TextLines, Has.All.Matches<Avalonia.Media.TextFormatting.TextLine>(line => line.Length > 0),
                    $"Formatter stopped making progress at preedit length {length}.");
                Assert.GreaterOrEqual(visualLine.TextLines.Sum(line => line.Length), visualLine.VisualLengthWithEndOfLineMarker,
                    $"Formatting lost text at preedit length {length}.");
                var renderedText = string.Concat(
                    visualLine.TextLines.SelectMany(line => line.TextRuns).Select(run => run.Text.ToString()));
                Assert.AreEqual("prefix" + preedit + "中文suffix", renderedText,
                    $"Composition or following Chinese text disappeared at preedit length {length}.");
            }
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditCursorRectangleTracksWrappedCompositionCursor()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 48;
            textArea.Height = 320;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 48, 320);
            AttachImeClient(textArea);
            var documentCaretRectangle = textArea.Caret.CalculateCaretRectangle();

            const string preedit = "zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            ArrangeTextView(textArea, 48, 320);

            var client = GetImeClient(textArea);
            Assert.Greater(client.CursorRectangle.Y, documentCaretRectangle.Y,
                "The native IME anchor should follow the wrapped composition cursor, not stay at the document caret.");

            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 48, 320);
            Assert.AreEqual(1, layer.LastRenderedCursorCount,
                "Inline composition should render exactly one cursor after wrapping.");
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditWrapsOnlyItsSecondDocumentLineWhenHorizontalScrollingIsEnabled()
        {
            var longBodyLine = new string('x', 80);
            var textArea = CreateTextArea($"{longBodyLine}\nb\n{longBodyLine}");
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.Lines[1].EndOffset;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 48, 240);

            const string preedit = "zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            ArrangeTextView(textArea, 48, 240);

            var firstLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var compositionLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[1]);
            var thirdLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[2]);

            Assert.AreEqual(1, firstLine.TextLines.Count);
            Assert.GreaterOrEqual(compositionLine.TextLines.Count, 3);
            Assert.AreEqual(1, thirdLine.TextLines.Count);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditWrappingDoesNotChangeNoWrapAfterClear()
        {
            var textArea = CreateTextArea(new string('x', 80));
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 48, 240);

            var bodyLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            Assert.AreEqual(1, bodyLine.TextLines.Count);

            textArea.SetImePreeditText("zhonghuarenmingongheguo", 23);
            ArrangeTextView(textArea, 48, 240);
            var preeditLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            Assert.Greater(preeditLine.TextLines.Count, 1);

            textArea.ClearImePreedit();
            ArrangeTextView(textArea, 48, 240);
            var clearedLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            Assert.AreEqual(1, clearedLine.TextLines.Count);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditLongTextWrapsFromSecondDocumentLine()
        {
            var textArea = CreateTextArea("first\nb");
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = false;
            ArrangeTextView(textArea, 48, 240);

            const string preedit = "zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            ArrangeTextView(textArea, 48, 240);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[1]);
            Assert.GreaterOrEqual(visualLine.TextLines.Count, 3);
            Assert.AreEqual(preedit.Length, visualLine.Elements.OfType<PreeditTextElement>().Single().VisualLength);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditCursorHostPreservesGraphemeAndUtf16Length()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = false;
            ArrangeTextView(textArea, 48, 240);

            const string preeditText = "😀e\u0301zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preeditText, 1);
            ArrangeTextView(textArea, 48, 240);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var preedit = visualLine.Elements.OfType<PreeditTextElement>().Single();
            var runs = visualLine.TextLines.SelectMany(line => line.TextRuns).ToArray();
            var renderedText = string.Concat(runs.Select(run => run.Text.ToString()));

            Assert.AreEqual(preeditText.Length, preedit.VisualLength);
            Assert.AreEqual(2, preedit.RenderedCursorOffset);
            Assert.AreEqual("a" + preeditText, renderedText);
            Assert.That(runs, Has.All.Matches<TextRun>(run => run.Length > 0));
            Assert.GreaterOrEqual(visualLine.TextLines.Count, 3);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditHardBreaksProduceSeparateVisualRows()
        {
            var textArea = CreateTextArea("a");
            textArea.Width = 300;
            textArea.Height = 160;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = false;
            ArrangeTextView(textArea, 300, 160);

            const string preeditText = "ab\r\ncd\nef";
            textArea.SetImePreeditText(preeditText, preeditText.Length);
            ArrangeTextView(textArea, 300, 160);

            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            Assert.GreaterOrEqual(visualLine.TextLines.Count, 3);
            Assert.AreEqual(preeditText.Length, visualLine.Elements.OfType<PreeditTextElement>().Single().VisualLength);
            Assert.That(visualLine.TextLines, Has.All.Matches<TextLine>(line => line.Length > 0));
            Assert.GreaterOrEqual(
                visualLine.TextLines.Sum(line => line.Length),
                visualLine.VisualLengthWithEndOfLineMarker);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void OverlayPreeditLongUnbrokenTextWrapsOnEveryViewportRow()
        {
            var textArea = CreateTextArea("a\nb");
            textArea.Width = 48;
            textArea.Height = 240;
            textArea.Caret.Offset = textArea.Document.TextLength;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            ArrangeTextView(textArea, 48, 240);

            const string preedit = "zhonghuarenmingongheguo";
            textArea.SetImePreeditText(preedit, preedit.Length);
            var layer = GetPreeditLayer(textArea);
            RenderLayer(layer, 48, 240);

            Assert.GreaterOrEqual(layer.LastRenderedChunkCount, 3);
            Assert.AreEqual(preedit.Length, layer.LastRenderedChunkLengths.Sum());
            Assert.AreEqual(1, layer.LastRenderedCursorCount);
            for (var i = 1; i < layer.LastRenderedChunkOrigins.Count; i++)
            {
                Assert.AreEqual(0, layer.LastRenderedChunkOrigins[i].X, 0.01);
                Assert.Greater(layer.LastRenderedChunkOrigins[i].Y, layer.LastRenderedChunkOrigins[i - 1].Y);
            }
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditFollowsProgrammaticCaretMove()
        {
            var textArea = CreateTextArea("abcd");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな", 1);
            ArrangeTextView(textArea, 300, 100);

            var firstLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var firstPreedit = firstLine.Elements.OfType<PreeditTextElement>().Single();
            Assert.AreEqual(1, firstPreedit.RelativeTextOffset);

            textArea.Caret.Offset = 3;
            ArrangeTextView(textArea, 300, 100);

            var movedLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var movedPreedit = movedLine.Elements.OfType<PreeditTextElement>().Single();
            Assert.AreEqual(3, movedPreedit.RelativeTextOffset);
        }

        [Test]
        public void ImePreeditScrollReservationDefaultsToTenAndRejectsNegativeValues()
        {
            var options = new TextEditorOptions();
            Assert.AreEqual(10, options.ImePreeditHorizontalScrollCharCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.ImePreeditHorizontalScrollCharCount = -1);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ImePreeditScrollReservationOnlyAppliesToActiveOverlayComposition()
        {
            var textArea = CreateTextArea(new string('x', 80));
            textArea.Options.ImePreeditHorizontalScrollCharCount = 10;

            Assert.AreEqual(0, textArea.TextView.ImePreeditScrollReservationWidth);

            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            textArea.SetImePreeditText("かな");
            Assert.AreEqual(0, textArea.TextView.ImePreeditScrollReservationWidth);

            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            Assert.Greater(textArea.TextView.ImePreeditScrollReservationWidth, 0);

            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Hidden;
            Assert.AreEqual(0, textArea.TextView.ImePreeditScrollReservationWidth);

            textArea.ClearImePreedit();
            Assert.AreEqual(0, textArea.TextView.ImePreeditScrollReservationWidth);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ImePreeditScrollReservationChangesExtentOnlyForOverlayComposition()
        {
            var textArea = CreateTextArea(new string('x', 80));
            textArea.Width = 100;
            textArea.Height = 80;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Overlay;
            textArea.Options.ImePreeditHorizontalScrollCharCount = 0;
            textArea.SetImePreeditText("かな");
            ArrangeTextView(textArea, 100, 80);
            var withoutReservation = ((IScrollable)textArea.TextView).Extent.Width;

            textArea.Options.ImePreeditHorizontalScrollCharCount = 10;
            ArrangeTextView(textArea, 100, 80);
            var withReservation = ((IScrollable)textArea.TextView).Extent.Width;

            Assert.Greater(withReservation, withoutReservation);
            Assert.Greater(withReservation - withoutReservation, 0);

            textArea.ClearImePreedit();
            ArrangeTextView(textArea, 100, 80);
            Assert.AreEqual(withoutReservation, ((IScrollable)textArea.TextView).Extent.Width, 0.01);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ImeClientReportsCurrentLineAndLineRelativeSelection()
        {
            var textArea = CreateTextArea("first\nprefix中文suffix\nthird");
            var line = textArea.Document.GetLineByNumber(2);
            textArea.Caret.Offset = line.Offset + 7;
            textArea.Selection = Selection.Create(textArea, line.Offset + 1, line.Offset + 8);

            var client = GetImeClient(textArea);

            Assert.AreEqual("prefix中文suffix", client.SurroundingText);
            Assert.AreEqual(1, client.Selection.Start);
            Assert.AreEqual(8, client.Selection.End);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ImeClientRaisesSelectionChangedWhenSelectionChangesWithoutMovingCaret()
        {
            var textArea = CreateTextArea("prefix中文suffix");
            textArea.Caret.Offset = 6;
            AttachImeClient(textArea);
            var client = GetImeClient(textArea);
            var eventCount = 0;
            client.SelectionChanged += (_, _) => eventCount++;

            textArea.Selection = Selection.Create(textArea, 1, 8);

            Assert.AreEqual(6, textArea.Caret.Offset);
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual(1, client.Selection.Start);
            Assert.AreEqual(8, client.Selection.End);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ImeClientSelectionSetterClampsSelectionToCurrentDocumentLine()
        {
            var textArea = CreateTextArea("first\nprefix中文suffix\nthird");
            var line = textArea.Document.GetLineByNumber(2);
            textArea.Caret.Offset = line.Offset + 2;
            AttachImeClient(textArea);
            var client = GetImeClient(textArea);

            client.Selection = new TextSelection(-20, 200);

            Assert.AreEqual(line.Offset, textArea.Selection.SurroundingSegment.Offset);
            Assert.AreEqual(line.Length, textArea.Selection.SurroundingSegment.Length);
            Assert.AreEqual(0, client.Selection.Start);
            Assert.AreEqual(line.Length, client.Selection.End);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void InlinePreeditDoesNotChangeNativeSurroundingSelection()
        {
            var textArea = CreateTextArea("first\nprefix中文suffix\nthird");
            var line = textArea.Document.GetLineByNumber(2);
            textArea.Caret.Offset = line.Offset + 6;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            AttachImeClient(textArea);
            var client = GetImeClient(textArea);

            client.SetPreeditText("zhonghuarenmingongheguo", 24);
            ArrangeTextView(textArea, 120, 320);

            Assert.AreEqual("prefix中文suffix", client.SurroundingText);
            Assert.AreEqual(6, client.Selection.Start);
            Assert.AreEqual(6, client.Selection.End);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void NativeImeClearThenCommitPreservesCommittedChineseAndFollowingText()
        {
            var textArea = CreateTextArea("prefix中文suffix");
            textArea.Caret.Offset = "prefix".Length;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 160, 320);
            AttachImeClient(textArea);

            var client = GetImeClient(textArea);
            client.SetPreeditText("zhonghuarenmingongheguo", 24);
            ArrangeTextView(textArea, 160, 320);
            Assert.IsTrue(textArea.HasImePreedit);

            // macOS native IME clears marked text before delivering insertText.
            client.SetPreeditText(null);
            Assert.IsFalse(textArea.HasImePreedit);
            RaiseTextInput(textArea, "中文");

            Assert.AreEqual("prefix中文中文suffix", textArea.Document.Text);
            var visualLine = textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
            var renderedText = string.Concat(visualLine.TextLines
                .SelectMany(line => line.TextRuns)
                .Select(run => run.Text.ToString()));
            Assert.That(renderedText, Does.Contain("prefix中文中文suffix"));
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void NativeImeCommitThenClearPreservesCommittedChineseAndFollowingText()
        {
            var textArea = CreateTextArea("prefix中文suffix");
            textArea.Caret.Offset = "prefix".Length;
            textArea.ImePreeditDisplayMode = ImePreeditDisplayMode.Inline;
            ((ILogicalScrollable)textArea.TextView).CanHorizontallyScroll = true;
            ArrangeTextView(textArea, 160, 320);
            AttachImeClient(textArea);

            var client = GetImeClient(textArea);
            client.SetPreeditText("zhonghuarenmingongheguo", 24);
            ArrangeTextView(textArea, 160, 320);
            RaiseTextInput(textArea, "中文");
            client.SetPreeditText(null);

            Assert.AreEqual("prefix中文中文suffix", textArea.Document.Text);
            Assert.IsFalse(textArea.HasImePreedit);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void CommitOnClickFallbackInsertsAtOldCaretAndSuppressesOneDuplicate()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな");

            Assert.IsTrue(textArea.CommitImePreeditBeforeCaretMove());
            Assert.AreEqual("aかなb", textArea.Document.Text);
            Assert.AreEqual(3, textArea.Caret.Offset);

            RaiseTextInput(textArea, "かな");
            Assert.AreEqual("aかなb", textArea.Document.Text);

            RaiseTextInput(textArea, "かな");
            Assert.AreEqual("aかなかなb", textArea.Document.Text);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void CommitOnClickSyncResetDoesNotInsertTwice()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな");
            var client = GetImeClient(textArea);
            client.ResetRequested += (_, _) => RaiseTextInput(textArea, "かな");

            Assert.IsTrue(textArea.CommitImePreeditBeforeCaretMove());
            Assert.AreEqual("aかなb", textArea.Document.Text);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void CommitOnClickMismatchIsNotSwallowed()
        {
            var textArea = CreateTextArea("ab");
            textArea.Caret.Offset = 1;
            textArea.SetImePreeditText("かな");

            textArea.CommitImePreeditBeforeCaretMove();
            RaiseTextInput(textArea, "別");

            Assert.AreEqual("aかな別b", textArea.Document.Text);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void ReadOnlyTextAreaDoesNotExposeItsImeClient()
        {
            var textArea = CreateTextArea("ab");
            textArea.ReadOnlySectionProvider = ReadOnlySectionDocument.Instance;
            var args = new TextInputMethodClientRequestedEventArgs
            {
                RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
                Source = textArea
            };

            textArea.RaiseEvent(args);

            Assert.IsNull(args.Client);
        }

        [Avalonia.Headless.NUnit.AvaloniaTest]
        public void DocumentReplacementClearsPreedit()
        {
            var textArea = CreateTextArea("ab");
            textArea.SetImePreeditText("かな");
            textArea.Document = new TextDocument("new");

            Assert.IsFalse(textArea.HasImePreedit);
        }

        private static TextArea CreateTextArea(string text)
            => new TextArea { Document = new TextDocument(text) };

        private static PreeditLayer GetPreeditLayer(TextArea textArea)
        {
            var field = typeof(TextArea).GetField("_preeditLayer", BindingFlags.Instance | BindingFlags.NonPublic);
            return (PreeditLayer)field.GetValue(textArea);
        }

        private static TextInputMethodClient GetImeClient(TextArea textArea)
        {
            var field = typeof(TextArea).GetField("_imClient", BindingFlags.Instance | BindingFlags.NonPublic);
            return (TextInputMethodClient)field.GetValue(textArea);
        }

        private static void AttachImeClient(TextArea textArea)
        {
            var client = GetImeClient(textArea);
            var method = client.GetType().GetMethod("SetTextArea", BindingFlags.Instance | BindingFlags.Public);
            method.Invoke(client, new object[] { textArea });
        }

        private static void ArrangeTextView(TextArea textArea, double width, double height)
        {
            textArea.TextView.Measure(new Size(width, height));
            textArea.TextView.Arrange(new Rect(0, 0, width, height));
            textArea.TextView.GetOrConstructVisualLine(textArea.Document.Lines[0]);
        }

        private static void RenderLayer(PreeditLayer layer, int width, int height)
        {
            using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height)))
            using (var drawingContext = bitmap.CreateDrawingContext())
            {
                layer.Render(drawingContext);
            }
        }

        private static void RaiseTextInput(TextArea textArea, string text)
        {
            textArea.RaiseEvent(new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = text,
                Source = textArea
            });
        }
    }
}
