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
            var cursorRuns = runs.OfType<PreeditCursorTextRun>().ToArray();

            Assert.AreEqual(preeditText.Length, preedit.VisualLength);
            Assert.AreEqual(2, preedit.RenderedCursorOffset);
            Assert.AreEqual(1, cursorRuns.Length);
            Assert.AreEqual("e\u0301", cursorRuns[0].Text.ToString());
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
